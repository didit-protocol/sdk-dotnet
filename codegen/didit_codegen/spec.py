"""Load the OpenAPI documents in openapi/ and attach the x-didit-sdk naming (DESIGN.md sections 3 and 4)."""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

HTTP_METHODS = ("get", "put", "post", "delete", "options", "head", "patch", "trace")
APPLICATION_PREFIX = "/v3/organization/{organization_id}/application/{application_id}/"
VALID_AUTH = {"api_key", "account_token", "none", "device_token", "public"}
NAME_RE = re.compile(r"^[a-z][a-z0-9_]*$")


class SpecError(Exception):
    """The spec or overlay cannot be turned into an SDK. The message says what to fix."""


@dataclass
class Document:
    api: str  # "verification" or "account"
    file: str
    data: dict[str, Any]

    @property
    def schemas(self) -> dict[str, Any]:
        return self.data.get("components", {}).get("schemas", {})

    def resolve(self, node: Any) -> Any:
        """Follows local $refs (schemas, parameters, responses, request bodies)."""
        seen = 0
        while isinstance(node, dict) and "$ref" in node:
            ref = node["$ref"]
            if not ref.startswith("#/"):
                raise SpecError(f"{self.file}: only local $refs are supported, got {ref}")
            target: Any = self.data
            for part in ref[2:].split("/"):
                part = part.replace("~1", "/").replace("~0", "~")
                if not isinstance(target, dict) or part not in target:
                    raise SpecError(f"{self.file}: unresolvable $ref {ref}")
                target = target[part]
            node = target
            seen += 1
            if seen > 50:
                raise SpecError(f"{self.file}: $ref cycle at {ref}")
        return node


@dataclass
class Naming:
    resource: str
    method: str
    auth: str
    server: bool
    scope: str | None
    source: str  # "upstream" or "overlay"


@dataclass
class RawOperation:
    doc: Document
    key: str
    http_method: str
    path: str
    op: dict[str, Any]
    path_item: dict[str, Any]
    naming: Naming

    @property
    def resource_segments(self) -> list[str]:
        return self.naming.resource.split(".")


@dataclass
class Spec:
    root: Path
    verification: Document
    account: Document
    overlay: dict[str, Any]
    operations: list[RawOperation] = field(default_factory=list)
    stale_overlay: list[str] = field(default_factory=list)

    @property
    def server_operations(self) -> list[RawOperation]:
        return [op for op in self.operations if op.naming.server]


def operation_key(api: str, method: str, path: str) -> str:
    prefix = "account " if api == "account" else ""
    return f"{prefix}{method.upper()} {path}"


def _validate_naming(key: str, value: Any, source: str) -> Naming:
    if not isinstance(value, dict):
        raise SpecError(f"{key}: x-didit-sdk ({source}) must be an object")
    for required in ("resource", "method", "auth", "server"):
        if required not in value:
            raise SpecError(f"{key}: x-didit-sdk ({source}) is missing '{required}'")
    resource = value["resource"]
    method = value["method"]
    if not isinstance(resource, str) or not all(NAME_RE.match(seg) for seg in resource.split(".")):
        raise SpecError(f"{key}: x-didit-sdk.resource must be a dot path of snake_case names, got {resource!r}")
    if not isinstance(method, str) or not NAME_RE.match(method):
        raise SpecError(f"{key}: x-didit-sdk.method must be snake_case, got {method!r}")
    if value["auth"] not in VALID_AUTH:
        raise SpecError(f"{key}: x-didit-sdk.auth must be one of {sorted(VALID_AUTH)}, got {value['auth']!r}")
    if not isinstance(value["server"], bool):
        raise SpecError(f"{key}: x-didit-sdk.server must be a boolean")
    scope = value.get("scope")
    if scope not in (None, "application"):
        raise SpecError(f"{key}: x-didit-sdk.scope must be 'application' when present, got {scope!r}")
    return Naming(resource, method, value["auth"], value["server"], scope, source)


def load(root: Path) -> Spec:
    openapi = root / "openapi"
    verification = Document("verification", "openapi/verification.json", json.loads((openapi / "verification.json").read_text("utf-8")))
    account = Document("account", "openapi/account.json", json.loads((openapi / "account.json").read_text("utf-8")))
    overlay = json.loads((openapi / "overlay.json").read_text("utf-8"))
    overlay_ops: dict[str, Any] = overlay.get("operations", {})

    _restore_deprecated(openapi, verification, account)

    spec = Spec(root, verification, account, overlay)
    used: set[str] = set()
    missing: list[str] = []
    for doc in (verification, account):
        for path, path_item in doc.data.get("paths", {}).items():
            for http_method in HTTP_METHODS:
                op = path_item.get(http_method)
                if op is None:
                    continue
                key = operation_key(doc.api, http_method, path)
                if "x-didit-sdk" in op:
                    naming = _validate_naming(key, op["x-didit-sdk"], "upstream")
                elif key in overlay_ops:
                    naming = _validate_naming(key, overlay_ops[key], "overlay")
                else:
                    missing.append(key)
                    continue
                if key in overlay_ops:
                    used.add(key)
                spec.operations.append(RawOperation(doc, key, http_method, path, op, path_item, naming))

    if missing:
        lines = "\n".join(f"  - {key} (operationId {_operation_id(spec, key)})" for key in missing)
        raise SpecError(
            "These operations have no SDK naming (x-didit-sdk upstream or an entry in openapi/overlay.json):\n"
            + lines
            + "\nAdd an overlay entry such as {\"resource\": \"sessions\", \"method\": \"create\", \"auth\": \"api_key\", \"server\": true} "
            "(DESIGN.md section 4), then regenerate."
        )

    spec.stale_overlay = sorted(set(overlay_ops) - used)
    _check_unique_methods(spec)
    _check_application_scope(spec)
    return spec


def _restore_deprecated(openapi: Path, verification: Document, account: Document) -> None:
    """Operations removed upstream stay in the SDK, marked obsolete, for one minor version (DESIGN.md 3.1).

    codegen/spec_sync.py records them in openapi/deprecated-operations.json with the schemas they reference.
    """
    path = openapi / "deprecated-operations.json"
    if not path.exists():
        return
    data = json.loads(path.read_text("utf-8"))
    docs = {"verification": verification, "account": account}
    for api, schemas in (data.get("schemas") or {}).items():
        target = docs[api].data.setdefault("components", {}).setdefault("schemas", {})
        for name, schema in schemas.items():
            target.setdefault(name, schema)
    for key, entry in (data.get("operations") or {}).items():
        api = "account" if key.startswith("account ") else "verification"
        doc = docs[api]
        method = str(entry["method"]).lower()
        path_item = doc.data.setdefault("paths", {}).setdefault(entry["path"], {})
        if method in path_item:
            continue  # back upstream; spec_sync drops the entry
        operation = dict(entry["operation"])
        operation["deprecated"] = True
        operation["x-didit-sdk"] = entry["naming"]
        operation["x-didit-removed-on"] = entry.get("removed_on")
        path_item[method] = operation


def _operation_id(spec: Spec, key: str) -> str:
    for doc in (spec.verification, spec.account):
        for path, item in doc.data.get("paths", {}).items():
            for method in HTTP_METHODS:
                if method in item and operation_key(doc.api, method, path) == key:
                    return str(item[method].get("operationId", "-"))
    return "-"


def _check_unique_methods(spec: Spec) -> None:
    seen: dict[tuple[str, str], str] = {}
    for op in spec.server_operations:
        ident = (op.naming.resource, op.naming.method)
        if ident in seen:
            raise SpecError(f"{op.key} and {seen[ident]} both map to {op.naming.resource}.{op.naming.method}")
        seen[ident] = op.key
    resources = {op.naming.resource for op in spec.server_operations}
    for op in spec.server_operations:
        for resource in resources:
            if resource.startswith(op.naming.resource + ".") and resource.split(".")[len(op.resource_segments)] == op.naming.method:
                raise SpecError(f"{op.key}: method {op.naming.method} collides with the child resource {resource}")


def _check_application_scope(spec: Spec) -> None:
    for op in spec.server_operations:
        scoped = op.path.startswith(APPLICATION_PREFIX)
        if op.naming.scope == "application" and not scoped:
            raise SpecError(f"{op.key}: scope 'application' but the path does not start with {APPLICATION_PREFIX}")
        if scoped and op.naming.scope != "application":
            raise SpecError(f"{op.key}: the path is application-scoped; add \"scope\": \"application\" to its x-didit-sdk naming")
