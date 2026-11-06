#!/usr/bin/env python3
"""Downloads the upstream OpenAPI documents and reports what changed (DESIGN.md section 3.1).

    python3 codegen/spec_sync.py                         report only, write nothing
    python3 codegen/spec_sync.py --write                 update openapi/ in place
    python3 codegen/spec_sync.py --write --summary FILE  also write the Markdown report to FILE
    python3 codegen/spec_sync.py --commit-message FILE   write the commit message release-please reads to FILE
    python3 codegen/spec_sync.py --offline DIR           read verification.json, account.json and authentication.json from DIR

With --write it copies the upstream files byte for byte, records their URL, SHA-256 and fetch time in
openapi/SOURCE.json, deletes overlay entries whose operation now carries x-didit-sdk upstream, and copies removed
server operations into openapi/deprecated-operations.json so their SDK methods stay, marked [Obsolete], until the next
minor release. A renamed server operation is recorded there too, so its old method stays as an [Obsolete] alias; a
removed operation whose naming an added one carries (the endpoint moved) keeps its method on the new path instead.
authentication.json is synced and reported like the others, but the Authentication module is hand-written (DESIGN.md
section 10): nothing is deprecated for it, and its contract test fails until the module follows.

The sync is feat(spec) when any document changed something a caller can observe and chore(spec) when only documentation
changed (descriptions, summaries, titles, examples, tags, docs-only vendor extensions). The report opens with every
change that can break typed code (a type change, a field or parameter that can now be null, a removed operation, field,
parameter or enum value, a newly required parameter or request field), and the commit message repeats each one as an
`api(spec):` line, which release-please-config.json lists under "API changes that can break typed code".
Writes changed=true|false, needs_naming=true|false, type=feat|chore and date=YYYY-MM-DD to $GITHUB_OUTPUT when that
variable is set. Exit code 0 on success (changed or not), 2 when a download or validation fails.

Standard library only, Python 3.10+.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))

from didit_codegen.spec import HTTP_METHODS, operation_key  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
UPSTREAM_FILES = (("verification", "verification.json"), ("account", "account.json"), ("authentication", "authentication.json"))
APIS = ("verification", "account")  # the documents the generator reads
HAND_WRITTEN = "authentication"  # the document the hand-written Authentication module follows (DESIGN.md section 10)
DOCUMENTS = (*APIS, HAND_WRITTEN)
DEPRECATED_FILE = "deprecated-operations.json"
DEPRECATED_DESCRIPTION = (
    "Server operations removed or renamed upstream. Their SDK methods stay, marked [Obsolete], until the next minor release "
    "(docs/DESIGN.md section 3.1); delete an entry in that release. Written by codegen/spec_sync.py. Keys follow "
    "overlay.json (\"<METHOD> <path>\", \"account <METHOD> <path>\"). Each operation is the last upstream definition "
    "with path-level parameters merged and non-schema $refs inlined; its schema $refs point into schemas.<api>. A renamed "
    "operation (key suffix \" (renamed from <resource>.<method>)\") has \"renamed_to\" and no definition: its old method "
    "is an alias of the live operation."
)
SCHEMA_REF = "#/components/schemas/"
DOWNLOAD_ATTEMPTS = 3
DOWNLOAD_TIMEOUT = 60
MAX_SCHEMA_DEPTH = 32
MAX_REF_DEPTH = 64
MAX_LIST = 200
MAX_RELEASE_NOTES = 100
SUMMARY_LIMIT = 50_000
CHECKS_MARKER = "<!-- spec-sync:checks -->"  # .github/workflows/spec-sync.yml puts the results of its checks here

# What only the documentation shows. A sync that changes nothing else is chore(spec) and releases nothing (DESIGN.md 3.1).
DOCUMENTATION_KEYS = frozenset({"description", "summary", "title", "example", "examples", "externalDocs", "tags"})
# Vendor extensions are documentation too, except the webhook payloads. The SDK naming (x-didit-sdk) is compared per
# operation together with overlay.json, so upstream adopting an overlay entry as it is changes nothing.
OBSERVABLE_EXTENSIONS = frozenset({"x-webhooks"})
# Objects whose keys are names (properties, paths, status codes, media types), so a property called "description" stays.
NAME_MAPS = frozenset(
    {
        "properties", "patternProperties", "definitions", "$defs", "dependentSchemas", "paths", "webhooks", "x-webhooks",
        "schemas", "responses", "parameters", "requestBodies", "headers", "securitySchemes", "links", "callbacks",
        "content", "encoding", "variables", "mapping",
    }
)  # fmt: skip
# Keywords whose value is data, compared as it is.
VALUE_KEYS = frozenset({"default", "enum", "const"})

# The kinds of change that can break typed code (DESIGN.md 3.1); every server SDK reports exactly these.
REMOVED = "removed"
TYPE_CHANGED = "type"
NOW_NULL = "now-null"
ENUM_VALUE_REMOVED = "enum-value-removed"
NOW_REQUIRED = "now-required"
# String formats the SDKs map to a type of their own (a date and time, a file), so adding or removing one changes a type.
TYPED_FORMATS = frozenset({"date-time", "binary"})


class SyncError(Exception):
    """A download or validation failed. Nothing has been written."""


@dataclass
class Upstream:
    api: str
    name: str
    url: str
    raw: bytes
    data: dict[str, Any]
    old_data: dict[str, Any]
    sha256: str
    old_sha256: str | None
    changed: bool


@dataclass
class Operation:
    api: str
    key: str
    method: str
    path: str
    op: dict[str, Any]
    effective: dict[str, Any]  # the operation with path-level parameters merged


@dataclass(frozen=True)
class BreakingChange:
    """A change that can break typed code: the operation, where in it, and what changed."""

    operation: str  # the operation key: "GET /v3/session/{sessionId}/decision/", "account GET /...", "authentication POST /token/"
    location: str  # "operation", "request", "response" or "<in> parameter"
    field: str  # "registry_checks[].company.registry_data", a parameter name, or "" for the operation or a whole body
    kind: str  # REMOVED, TYPE_CHANGED, NOW_NULL, ENUM_VALUE_REMOVED or NOW_REQUIRED
    detail: str = ""  # "string->object" for TYPE_CHANGED, the JSON value for ENUM_VALUE_REMOVED

    def describe(self) -> str:
        """One Markdown line. No angle brackets: release-please escapes them in the release notes."""
        if self.location == "operation":
            return f"`{self.operation}`: operation removed"
        where = f"`{self.operation}` {self.location} " + (f"`{self.field}`" if self.field else "body")
        if self.kind == TYPE_CHANGED:
            before, _, after = self.detail.partition("->")
            return f"{where}: type changed from {before} to {after}"
        if self.kind == NOW_NULL:
            return f"{where}: can now be null"
        if self.kind == ENUM_VALUE_REMOVED:
            return f"{where}: enum value `{self.detail}` removed"
        if self.kind == NOW_REQUIRED:
            return f"{where}: now required"
        return f"{where}: removed"


@dataclass
class Report:
    date: str
    write: bool
    files: list[Upstream]
    changed: bool = False
    commit_type: str = "chore"  # "feat" when a caller can observe the change, "chore" when only documentation changed
    added: list[tuple[str, str]] = field(default_factory=list)
    removed: list[tuple[str, str]] = field(default_factory=list)
    changed_ops: list[tuple[str, str]] = field(default_factory=list)
    schemas_added: list[str] = field(default_factory=list)
    schemas_removed: list[str] = field(default_factory=list)
    schemas_changed: list[str] = field(default_factory=list)
    breaking: list[BreakingChange] = field(default_factory=list)
    naming_changes: list[str] = field(default_factory=list)
    authentication_notes: list[str] = field(default_factory=list)
    overlay_notes: list[str] = field(default_factory=list)
    deprecated: list[tuple[str, str]] = field(default_factory=list)
    moved: list[tuple[str, str, str]] = field(default_factory=list)  # old key, new key, SDK name
    needs_naming: list[str] = field(default_factory=list)


# JSON helpers


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def canonical(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def dump_json(value: Any, trailing_newline: bool) -> str:
    """The formatting of openapi/overlay.json and openapi/SOURCE.json: 1-space indent, keys in insertion order."""
    return json.dumps(value, indent=1, ensure_ascii=False) + ("\n" if trailing_newline else "")


def pointer(data: Any, ref: str) -> Any:
    if not ref.startswith("#/"):
        raise SyncError(f"only local $refs are supported, got {ref}")
    target = data
    for part in ref[2:].split("/"):
        part = part.replace("~1", "/").replace("~0", "~")
        if not isinstance(target, dict) or part not in target:
            raise SyncError(f"unresolvable $ref {ref}")
        target = target[part]
    return target


def resolve(data: Any, node: Any) -> Any:
    """Follows a chain of local $refs; returns None when one does not resolve."""
    for _ in range(50):
        if not (isinstance(node, dict) and isinstance(node.get("$ref"), str)):
            return node
        try:
            node = pointer(data, node["$ref"])
        except SyncError:
            return None
    return None


def ref_name(ref: str) -> str:
    return ref[len(SCHEMA_REF):].replace("~1", "/").replace("~0", "~")


def schema_refs(node: Any) -> set[str]:
    found: set[str] = set()
    stack = [node]
    while stack:
        item = stack.pop()
        if isinstance(item, dict):
            ref = item.get("$ref")
            if isinstance(ref, str) and ref.startswith(SCHEMA_REF):
                found.add(ref_name(ref))
            stack.extend(item.values())
        elif isinstance(item, list):
            stack.extend(item)
    return found


def schema_closure(schemas: dict[str, Any], node: Any) -> dict[str, Any]:
    """Every component schema `node` references, transitively."""
    names: set[str] = set()
    queue = sorted(schema_refs(node))
    while queue:
        name = queue.pop()
        if name in names or name not in schemas:
            continue
        names.add(name)
        queue.extend(sorted(schema_refs(schemas[name]) - names))
    return {name: schemas[name] for name in sorted(names)}


def inline_refs(data: Any, node: Any, stack: tuple[str, ...] = ()) -> Any:
    """A deep copy of `node` with every $ref that is not a component schema replaced by its target."""
    if isinstance(node, dict):
        ref = node.get("$ref")
        if isinstance(ref, str) and not ref.startswith(SCHEMA_REF) and ref not in stack:
            try:
                target = pointer(data, ref)
            except SyncError:
                return copy.deepcopy(node)
            return inline_refs(data, target, stack + (ref,))
        return {key: inline_refs(data, value, stack) for key, value in node.items()}
    if isinstance(node, list):
        return [inline_refs(data, item, stack) for item in node]
    return copy.deepcopy(node)


# Upstream download and validation


def fetch(name: str, url: str, offline: Path | None) -> bytes:
    if offline is not None:
        path = offline / name
        if not path.is_file():
            raise SyncError(f"{path} does not exist")
        return path.read_bytes()
    if not url.startswith("https://"):
        raise SyncError(f"{name}: refusing to download from a non-https URL: {url}")
    request = urllib.request.Request(url, headers={"User-Agent": "didit-sdk-dotnet-spec-sync", "Accept": "application/json"})
    last: Exception | None = None
    for attempt in range(DOWNLOAD_ATTEMPTS):
        try:
            with urllib.request.urlopen(request, timeout=DOWNLOAD_TIMEOUT) as response:  # noqa: S310 (https only)
                if response.status != 200:
                    raise SyncError(f"{url} returned HTTP {response.status}")
                return response.read()
        except (urllib.error.URLError, TimeoutError, OSError) as error:
            last = error
            if attempt + 1 < DOWNLOAD_ATTEMPTS:
                time.sleep(2 * 2**attempt)
    raise SyncError(f"could not download {url}: {last}")


def validate(name: str, raw: bytes) -> dict[str, Any]:
    try:
        data = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise SyncError(f"{name}: the upstream file is not valid JSON ({error})") from error
    if not isinstance(data, dict) or not isinstance(data.get("openapi"), str) or not isinstance(data.get("paths"), dict):
        raise SyncError(f"{name}: the upstream file is not an OpenAPI document (it needs 'openapi' and 'paths')")
    return data


# Operations and naming


def collect_operations(api: str, data: dict[str, Any]) -> dict[str, Operation]:
    found: dict[str, Operation] = {}
    for path, item in (data.get("paths") or {}).items():
        if not isinstance(item, dict):
            continue
        shared = item.get("parameters") or []
        for method in HTTP_METHODS:
            op = item.get(method)
            if not isinstance(op, dict):
                continue
            key = f"{HAND_WRITTEN} {method.upper()} {path}" if api == HAND_WRITTEN else operation_key(api, method, path)
            found[key] = Operation(api, key, method, path, op, merge_parameters(data, shared, op))
    return found


def parameter_key(data: Any, parameter: Any) -> tuple[str, str]:
    resolved = resolve(data, parameter)
    if not isinstance(resolved, dict):
        return ("", canonical(parameter))
    return (str(resolved.get("in", "")), str(resolved.get("name", "")))


def merge_parameters(data: Any, shared: list[Any], op: dict[str, Any]) -> dict[str, Any]:
    if not shared:
        return op
    own = op.get("parameters") or []
    overridden = {parameter_key(data, parameter) for parameter in own}
    merged = dict(op)
    merged["parameters"] = [parameter for parameter in shared if parameter_key(data, parameter) not in overridden] + list(own)
    return merged


def naming_of(op: Operation, overlay_ops: dict[str, Any]) -> dict[str, Any] | None:
    upstream = op.op.get("x-didit-sdk")
    if isinstance(upstream, dict):
        return upstream
    entry = overlay_ops.get(op.key)
    return entry if isinstance(entry, dict) else None


def label(naming: dict[str, Any] | None) -> str:
    if not naming:
        return "no SDK naming"
    text = f"{naming.get('resource')}.{naming.get('method')}"
    if naming.get("server") is False:
        text += ", not in the server SDK"
    return text


def renamed(old: dict[str, Any], new: dict[str, Any]) -> bool:
    return any(old.get(key) != new.get(key) for key in ("resource", "method", "server"))


def sdk_name(naming: dict[str, Any] | None) -> tuple[Any, Any] | None:
    """The server SDK method a naming produces, or None when it is not in the server SDK."""
    if not naming or naming.get("server") is not True:
        return None
    return naming.get("resource"), naming.get("method")


def alias_key(key: str, naming: dict[str, Any]) -> str:
    return f"{key} (renamed from {naming.get('resource')}.{naming.get('method')})"


def api_of(key: str) -> str:
    return "account" if key.startswith("account ") else "verification"


def schema_label(api: str, name: str) -> str:
    return f"`{name}` ({api})"


# Classification: documentation-only or observable


def documentation_key(key: str) -> bool:
    return key in DOCUMENTATION_KEYS or (key.startswith("x-") and key not in OBSERVABLE_EXTENSIONS)


def observable(node: Any, names: bool = False) -> Any:
    """`node` without its documentation, so that only what a caller can observe compares."""
    if isinstance(node, dict):
        if names:
            return {key: observable(value) for key, value in node.items()}
        return {
            key: value if key in VALUE_KEYS else observable(value, key in NAME_MAPS)
            for key, value in node.items()
            if not documentation_key(key)
        }
    if isinstance(node, list):
        return [observable(item) for item in node]
    return node


def observable_document(data: dict[str, Any]) -> Any:
    return observable({key: value for key, value in data.items() if key != "info"})


def commit_type(files: list[Upstream], naming_changed: bool, breaking: list[BreakingChange]) -> str:
    """feat when a synced document changed anything a caller can observe; chore when only documentation changed."""
    if breaking or naming_changed:
        return "feat"
    for item in files:
        if item.changed and observable_document(item.old_data) != observable_document(item.data):
            return "feat"
    return "chore"


# Breaking-change detection (DESIGN.md 3.1): the same list in every server SDK


def dereference(data: Any, node: Any, stack: tuple[str, ...] = ()) -> Any:
    """A copy of `node` with every local $ref replaced by its target; a $ref's siblings override the target's keys."""
    if isinstance(node, dict):
        ref = node.get("$ref")
        if isinstance(ref, str):
            if ref in stack or len(stack) >= MAX_REF_DEPTH:
                return {"$cycle": ref}
            try:
                target = dereference(data, pointer(data, ref), stack + (ref,))
            except SyncError:
                return {"$unresolved": ref}
            siblings = {key: dereference(data, value, stack) for key, value in node.items() if key != "$ref"}
            return {**target, **siblings} if isinstance(target, dict) else target
        return {key: dereference(data, value, stack) for key, value in node.items()}
    if isinstance(node, list):
        return [dereference(data, item, stack) for item in node]
    return node


def null_variant(schema: Any) -> bool:
    return isinstance(schema, dict) and (schema.get("type") == "null" or schema.get("enum") == [None])


def union_variants(schema: dict[str, Any]) -> list[Any]:
    """The oneOf and anyOf variants other than null."""
    return [variant for keyword in ("oneOf", "anyOf") for variant in schema.get(keyword) or [] if not null_variant(variant)]


def string_enum(schema: Any) -> bool:
    values = schema.get("enum") if isinstance(schema, dict) else None
    return (
        isinstance(values, list)
        and schema.get("type") in (None, "string")
        and any(isinstance(value, str) for value in values)
        and all(value is None or isinstance(value, str) for value in values)
    )


def single(schema: Any, depth: int = 0) -> Any:
    """The one type a oneOf/anyOf stands for, read like the generator: its only non-null variant, one object merging object
    variants, or one enum merging string enum variants. Other schemas, a union of different types included, stay as they
    are. Nullability is read from the schema itself (can_be_null), because merging drops the null variants."""
    if not isinstance(schema, dict) or "allOf" in schema or depth > MAX_SCHEMA_DEPTH:
        return schema
    variants = [single(variant, depth + 1) for variant in union_variants(schema)]
    if not variants or not all(isinstance(variant, dict) for variant in variants):
        return schema
    rest = {key: value for key, value in schema.items() if key not in ("oneOf", "anyOf")}
    if len(variants) == 1:
        return {**variants[0], **rest}
    parts = [object_properties(variant) for variant in variants]
    if all(properties for properties, _ in parts):
        merged: dict[str, Any] = {}
        for properties, _ in parts:
            for name, value in properties.items():
                merged.setdefault(name, value)
        required = set.intersection(*(names for _, names in parts))
        return {**rest, "type": "object", "properties": merged, "required": sorted(required)}
    if all(string_enum(variant) for variant in variants):
        values = list(dict.fromkeys(value for variant in variants for value in variant["enum"] if value is not None))
        return {**rest, "type": "string", "enum": values}
    return schema


def can_be_null(schema: Any, depth: int = 0) -> bool:
    """Whether the schema accepts null: a null type, nullable, null in its enum, or a null or nullable union variant."""
    if not isinstance(schema, dict) or depth > MAX_SCHEMA_DEPTH:
        return False
    return "null" in schema_types(schema) or any(can_be_null(variant, depth + 1) for variant in union_variants(schema))


def schema_types(schema: dict[str, Any]) -> set[str]:
    """The JSON types a schema allows, "null" included; an untyped schema is inferred from its keywords or allOf."""
    raw = schema.get("type")
    types = set(raw) if isinstance(raw, list) else ({raw} if isinstance(raw, str) else set())
    if not types - {"null"}:
        if any(key in schema for key in ("properties", "additionalProperties", "patternProperties")):
            types.add("object")
        elif "items" in schema:
            types.add("array")
        else:
            for member in schema.get("allOf") or []:
                if isinstance(member, dict):
                    types |= schema_types(member) - {"null"}
    if schema.get("nullable") is True:
        types.add("null")
    if any(null_variant(variant) for variant in (schema.get("anyOf") or []) + (schema.get("oneOf") or [])):
        types.add("null")
    if isinstance(schema.get("enum"), list) and None in schema["enum"]:
        types.add("null")
    return types


def type_kind(schema: Any) -> str:
    """The type typed code sees, "" when the schema does not say: its JSON types without null, a string format the SDKs
    map to a type of its own in parentheses, or for a union of different types the kind of every variant, so that a
    variant more or less changes it."""
    schema = single(schema)
    if not isinstance(schema, dict):
        return ""
    variants = union_variants(schema) if "allOf" not in schema else []
    if variants:
        return "|".join(sorted(type_kind(variant) or "any" for variant in variants))
    kind = "/".join(sorted(schema_types(schema) - {"null"}))
    if kind == "string" and schema.get("format") in TYPED_FORMATS:
        kind += f"({schema['format']})"
    return kind


def object_properties(schema: dict[str, Any]) -> tuple[dict[str, Any], set[str]]:
    """The properties and required names of an object schema, merged through allOf."""
    properties: dict[str, Any] = {}
    required: set[str] = set()
    stack = [schema]
    while stack:
        current = stack.pop()
        if not isinstance(current, dict):
            continue
        if isinstance(current.get("properties"), dict):
            properties.update(current["properties"])
        required |= {name for name in current.get("required") or [] if isinstance(name, str)}
        stack.extend(current.get("allOf") or [])
    return properties, required


def join_field(prefix: str, name: str) -> str:
    return f"{prefix}.{name}" if prefix else name


def compare_schemas(old: Any, new: Any, operation: str, location: str, path: str, found: list[BreakingChange], depth: int = 0) -> None:
    """Appends every change from `old` to `new` (both dereferenced) that can break typed code."""
    if depth > MAX_SCHEMA_DEPTH or observable(old) == observable(new):
        return
    if not isinstance(old, dict) or not isinstance(new, dict):
        found.append(BreakingChange(operation, location, path, TYPE_CHANGED, f"{type_kind(old) or 'any'}->{type_kind(new) or 'any'}"))
        return
    old_kind, new_kind = type_kind(old), type_kind(new)
    if old_kind and new_kind and old_kind != new_kind:
        found.append(BreakingChange(operation, location, path, TYPE_CHANGED, f"{old_kind}->{new_kind}"))
        return
    if can_be_null(new) and not can_be_null(old):
        found.append(BreakingChange(operation, location, path, NOW_NULL))
    old, new = single(old), single(new)
    old_enum, new_enum = old.get("enum"), new.get("enum")
    if isinstance(old_enum, list) and isinstance(new_enum, list):
        for value in old_enum:
            if value is not None and value not in new_enum:
                found.append(BreakingChange(operation, location, path, ENUM_VALUE_REMOVED, json.dumps(value, ensure_ascii=False)))

    old_properties, old_required = object_properties(old)
    new_properties, new_required = object_properties(new)
    for name in sorted(old_properties.keys() - new_properties.keys()):
        found.append(BreakingChange(operation, location, join_field(path, name), REMOVED))
    if location != "response":
        for name in sorted(new_required - old_required):
            found.append(BreakingChange(operation, location, join_field(path, name), NOW_REQUIRED))
    for name in sorted(old_properties.keys() & new_properties.keys()):
        compare_schemas(old_properties[name], new_properties[name], operation, location, join_field(path, name), found, depth + 1)
    for key, suffix in (("items", "[]"), ("additionalProperties", "{}")):
        old_sub, new_sub = old.get(key), new.get(key)
        if isinstance(old_sub, dict) or isinstance(new_sub, dict):
            old_sub = old_sub if isinstance(old_sub, dict) else {}
            new_sub = new_sub if isinstance(new_sub, dict) else {}
            compare_schemas(old_sub, new_sub, operation, location, path + suffix, found, depth + 1)
    # A union of different types left: its kinds match (type_kind), so compare the variants of the same kind in order.
    old_variants = sorted(union_variants(old), key=type_kind) if "allOf" not in old else []
    new_variants = sorted(union_variants(new), key=type_kind) if "allOf" not in new else []
    if old_variants and len(old_variants) == len(new_variants):
        for index, (old_variant, new_variant) in enumerate(zip(old_variants, new_variants)):
            compare_schemas(old_variant, new_variant, operation, location, f"{path}(variant {index + 1})", found, depth + 1)


def parameters_of(data: Any, op: dict[str, Any]) -> dict[tuple[str, str], dict[str, Any]]:
    found: dict[tuple[str, str], dict[str, Any]] = {}
    for parameter in dereference(data, op.get("parameters") or []):
        if isinstance(parameter, dict):
            found[(str(parameter.get("in", "")), str(parameter.get("name", "")))] = parameter
    return found


def request_body(data: Any, op: dict[str, Any]) -> tuple[bool, dict[str, Any]]:
    """Whether the request body is required, and its schema per media type."""
    body = dereference(data, op.get("requestBody"))
    if not isinstance(body, dict):
        return False, {}
    content = body.get("content") if isinstance(body.get("content"), dict) else {}
    schemas = {media: entry.get("schema") for media, entry in content.items() if isinstance(entry, dict) and isinstance(entry.get("schema"), dict)}
    return body.get("required") is True, schemas


def success_schemas(data: Any, op: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """The schema of every 2xx response body, by status and media type."""
    responses = op.get("responses") if isinstance(op.get("responses"), dict) else {}
    found: dict[str, dict[str, Any]] = {}
    for status in sorted(responses, key=str):
        if not str(status).startswith("2"):
            continue
        response = dereference(data, responses[status])
        content = response.get("content") if isinstance(response, dict) and isinstance(response.get("content"), dict) else {}
        schemas = {media: entry["schema"] for media, entry in content.items() if isinstance(entry, dict) and isinstance(entry.get("schema"), dict)}
        if schemas:
            found[str(status)] = schemas
    return found


def operation_breaking(old_data: Any, new_data: Any, old: Operation, new: Operation) -> list[BreakingChange]:
    """The changes from `old` to `new` (the same operation key) that can break typed code, in request and response."""
    key = new.key
    found: list[BreakingChange] = []
    old_parameters = parameters_of(old_data, old.effective)
    new_parameters = parameters_of(new_data, new.effective)
    for place, name in sorted(old_parameters.keys() - new_parameters.keys()):
        found.append(BreakingChange(key, f"{place} parameter", name, REMOVED))
    for (place, name), parameter in sorted(new_parameters.items()):
        before = old_parameters.get((place, name))
        if parameter.get("required") is True and (before is None or before.get("required") is not True):
            found.append(BreakingChange(key, f"{place} parameter", name, NOW_REQUIRED))
        if before is not None:
            compare_schemas(before.get("schema") or {}, parameter.get("schema") or {}, key, f"{place} parameter", name, found)

    old_body_required, old_bodies = request_body(old_data, old.op)
    new_body_required, new_bodies = request_body(new_data, new.op)
    if new_body_required and not old_body_required:
        found.append(BreakingChange(key, "request", "", NOW_REQUIRED))
    for media in sorted(old_bodies.keys() & new_bodies.keys()):
        compare_schemas(old_bodies[media], new_bodies[media], key, "request", "", found)

    # Every 2xx response the operation keeps; when its only one changed status, the old body against the new one.
    old_responses, new_responses = success_schemas(old_data, old.op), success_schemas(new_data, new.op)
    statuses = [(status, status) for status in old_responses if status in new_responses]
    if not statuses and len(old_responses) == 1 and len(new_responses) == 1:
        statuses = [(next(iter(old_responses)), next(iter(new_responses)))]
    for before, after in statuses:
        for media in sorted(old_responses[before].keys() & new_responses[after].keys()):
            compare_schemas(old_responses[before][media], new_responses[after][media], key, "response", "", found)
    return list(dict.fromkeys(found))  # one entry per change, even when several responses or media types share it


def breaking_changes(old_docs: dict[str, Any], new_docs: dict[str, Any], old_ops: dict[str, Operation], new_ops: dict[str, Operation]) -> list[BreakingChange]:
    """Every change that can break typed code, by operation: removed operations, then changes to the remaining ones."""
    found = [BreakingChange(key, "operation", "", REMOVED) for key in sorted(old_ops.keys() - new_ops.keys())]
    for key in sorted(old_ops.keys() & new_ops.keys()):
        old, new = old_ops[key], new_ops[key]
        found.extend(operation_breaking(old_docs[old.api], new_docs[new.api], old, new))
    return found


# The sync


def empty_deprecated() -> dict[str, Any]:
    return {"description": DEPRECATED_DESCRIPTION, "operations": {}, "schemas": {api: {} for api in APIS}}


def analyse(report: Report, files: list[Upstream], overlay_ops: dict[str, Any], deprecated: dict[str, Any]) -> None:
    """Fills `report` and updates `overlay_ops` and `deprecated` in place."""
    old_docs = {item.api: item.old_data for item in files}
    new_docs = {item.api: item.data for item in files}
    old_ops: dict[str, Operation] = {}
    new_ops: dict[str, Operation] = {}
    for api in APIS:
        old_ops.update(collect_operations(api, old_docs.get(api, {})))
        new_ops.update(collect_operations(api, new_docs.get(api, {})))
    original_overlay = dict(overlay_ops)

    # Overlay entries that upstream naming now covers: upstream wins, the entry is deleted.
    for key in sorted(original_overlay):
        if key not in new_ops:
            continue
        upstream = new_ops[key].op.get("x-didit-sdk")
        if not isinstance(upstream, dict):
            continue
        entry = original_overlay[key]
        del overlay_ops[key]
        if canonical(upstream) == canonical(entry):
            report.overlay_notes.append(f"Deleted `{key}`: upstream now carries the same `x-didit-sdk`.")
        else:
            report.overlay_notes.append(
                f"Naming change `{key}`: overlay `{label(entry)}`, upstream `{label(upstream)}`. Upstream wins; overlay entry deleted."
            )

    # Added, removed and changed operations.
    for key in sorted(new_ops.keys() - old_ops.keys()):
        op = new_ops[key]
        naming = naming_of(op, overlay_ops)
        if key in deprecated["operations"]:
            record = deprecated["operations"].pop(key)
            note = f"`{key}` is back upstream: deleted from `{DEPRECATED_FILE}`"
            if naming is None and isinstance(record.get("naming"), dict):
                naming = copy.deepcopy(record["naming"])
                overlay_ops[key] = naming
                note += " and its naming restored to `overlay.json`"
            report.overlay_notes.append(note + ".")
        report.added.append((key, label(naming)))
        if naming is None:
            summary = op.op.get("summary") or ""
            detail = f"operationId `{op.op.get('operationId', '-')}`" + (f", {summary}" if summary else "")
            report.needs_naming.append(f"`{key}` ({detail})")

    # An added operation that carries a removed operation's naming replaces it: the endpoint moved, the method stays.
    added_by_name: dict[tuple[Any, Any], str] = {}
    for key in sorted(new_ops.keys() - old_ops.keys()):
        name_of_added = sdk_name(naming_of(new_ops[key], overlay_ops))
        if name_of_added is not None:
            added_by_name.setdefault(name_of_added, key)

    for key in sorted(old_ops.keys() - new_ops.keys()):
        op = old_ops[key]
        naming = naming_of(op, original_overlay)
        name = label(naming)
        report.removed.append((key, name))
        if key in overlay_ops:
            del overlay_ops[key]
        replacement = added_by_name.get(sdk_name(naming)) if sdk_name(naming) is not None else None
        if replacement is not None:
            report.moved.append((key, replacement, name))
            if key in original_overlay:
                report.overlay_notes.append(f"Deleted `{key}`: `{replacement}` carries its naming now.")
            continue
        if naming is not None and naming.get("server") is True:
            schemas = old_docs[op.api].get("components", {}).get("schemas", {})
            operation = inline_refs(old_docs[op.api], op.effective)
            deprecated["operations"][key] = {
                "removed_on": report.date,
                "naming": copy.deepcopy(naming),
                "path": op.path,
                "method": op.method,
                "operation": operation,
            }
            deprecated["schemas"].setdefault(op.api, {}).update(copy.deepcopy(schema_closure(schemas, operation)))
            report.deprecated.append((key, name))
            if key in original_overlay:
                report.overlay_notes.append(f"Moved `{key}` to `{DEPRECATED_FILE}`.")
        elif key in original_overlay:
            report.overlay_notes.append(f"Deleted `{key}`: the operation was removed upstream.")

    # A renamed server operation keeps its old method as an obsolete alias for one minor version (DESIGN.md 3.1).
    live_names = {name for op in new_ops.values() if (name := sdk_name(naming_of(op, overlay_ops))) is not None}
    for key in sorted(deprecated["operations"]):
        record = deprecated["operations"][key]
        if "renamed_to" in record and sdk_name(record.get("naming")) in live_names:
            del deprecated["operations"][key]
            report.overlay_notes.append(f"`{key}`: an operation carries that naming again; deleted from `{DEPRECATED_FILE}`.")
    for key in sorted(old_ops.keys() & new_ops.keys()):
        before = naming_of(old_ops[key], original_overlay)
        after = naming_of(new_ops[key], overlay_ops)
        if not before or not after or not renamed(before, after):
            continue
        if sdk_name(before) is None or sdk_name(before) in live_names:
            report.naming_changes.append(f"SDK naming of `{key}` changes from `{label(before)}` to `{label(after)}`")
            continue
        record_naming = {**after, "resource": before.get("resource"), "method": before.get("method"), "server": True}
        deprecated["operations"][alias_key(key, before)] = {
            "removed_on": report.date,
            "naming": record_naming,
            "path": new_ops[key].path,
            "method": new_ops[key].method,
            "renamed_to": copy.deepcopy(after),
        }
        report.deprecated.append((alias_key(key, before), label(before)))
        report.naming_changes.append(
            f"SDK naming of `{key}` changes from `{label(before)}` to `{label(after)}`: the old method stays as an "
            "`[Obsolete]` alias until the next minor release"
        )

    for key in sorted(old_ops.keys() & new_ops.keys()):
        old, new = old_ops[key], new_ops[key]
        if canonical(old.effective) == canonical(new.effective):
            continue
        report.changed_ops.append((key, label(naming_of(new, overlay_ops))))

    for key in sorted(original_overlay):
        if key not in old_ops and key not in new_ops:
            report.overlay_notes.append(f"`{key}` matches no operation upstream (left in place; delete it if it is obsolete).")

    # The Authentication module is hand-written (DESIGN.md section 10): its operations are reported, never deprecated,
    # and its contract test fails until the module follows an added, removed or renamed one.
    old_auth = collect_operations(HAND_WRITTEN, old_docs.get(HAND_WRITTEN, {}))
    new_auth = collect_operations(HAND_WRITTEN, new_docs.get(HAND_WRITTEN, {}))
    for key in sorted(new_auth.keys() - old_auth.keys()):
        name = label(naming_of(new_auth[key], {}))
        report.added.append((key, name))
        report.authentication_notes.append(f"Added `{key}`: {name}")
    for key in sorted(old_auth.keys() - new_auth.keys()):
        name = label(naming_of(old_auth[key], {}))
        report.removed.append((key, name))
        report.authentication_notes.append(f"Removed `{key}`: {name}")
    for key in sorted(old_auth.keys() & new_auth.keys()):
        if canonical(old_auth[key].effective) == canonical(new_auth[key].effective):
            continue
        before, after = naming_of(old_auth[key], {}), naming_of(new_auth[key], {})
        report.changed_ops.append((key, label(after)))
        if canonical(before) != canonical(after):
            report.authentication_notes.append(f"SDK naming of `{key}` changes from `{label(before)}` to `{label(after)}`")

    # Component schemas.
    for api in DOCUMENTS:
        old_schemas = old_docs.get(api, {}).get("components", {}).get("schemas", {}) or {}
        new_schemas = new_docs.get(api, {}).get("components", {}).get("schemas", {}) or {}
        for name in sorted(new_schemas.keys() - old_schemas.keys()):
            report.schemas_added.append(schema_label(api, name))
        for name in sorted(old_schemas.keys() - new_schemas.keys()):
            report.schemas_removed.append(schema_label(api, name))
        for name in sorted(old_schemas.keys() & new_schemas.keys()):
            if canonical(old_schemas[name]) != canonical(new_schemas[name]):
                report.schemas_changed.append(schema_label(api, name))

    # Keep only the deprecated schemas that a remaining deprecated operation still references.
    for api in APIS:
        stored = deprecated["schemas"].get(api) or {}
        keep: dict[str, Any] = {}
        for key, record in deprecated["operations"].items():
            if api_of(key) == api:
                keep.update(schema_closure(stored, record.get("operation")))
        deprecated["schemas"][api] = keep

    report.breaking = breaking_changes(old_docs, new_docs, {**old_ops, **old_auth}, {**new_ops, **new_auth})
    naming_changed = any(
        canonical(naming_of(old_ops[key], original_overlay)) != canonical(naming_of(new_ops[key], overlay_ops))
        for key in old_ops.keys() & new_ops.keys()
    ) or any(canonical(naming_of(old_auth[key], {})) != canonical(naming_of(new_auth[key], {})) for key in old_auth.keys() & new_auth.keys())
    report.commit_type = commit_type(files, naming_changed, report.breaking)


def bullet_list(items: list[str]) -> list[str]:
    lines = [f"- {item}" for item in items[:MAX_LIST]]
    if len(items) > MAX_LIST:
        lines.append(f"- ... and {len(items) - MAX_LIST} more")
    return lines


def render_summary(report: Report) -> str:
    """The pull request body: "Breaking changes" first, the release it makes, then the details (DESIGN.md 3.1)."""
    lines: list[str] = []
    if report.changed:
        lines += [
            "### Breaking changes",
            "",
            "Changes that can break typed code. They mirror the API, so they ship in a minor release, and the release notes "
            'list them under "API changes that can break typed code" (docs/DESIGN.md sections 3.1 and 13).',
            "",
        ]
        lines += bullet_list([change.describe() for change in report.breaking]) if report.breaking else ["None detected."]
        lines += ["", "### Release", ""]
        if report.commit_type == "feat":
            lines.append("`feat(spec)`: this sync changes what a caller can observe, so merging it releases a minor version.")
        else:
            lines.append("`chore(spec)`: only descriptions, examples or other documentation changed, so this sync releases nothing on its own.")
        lines += ["", CHECKS_MARKER, ""]
    lines += ["Upstream API spec sync (docs/DESIGN.md section 3.1), generated by `codegen/spec_sync.py`.", ""]
    if not report.write:
        lines += ["Dry run: nothing was written.", ""]
    lines += ["| File | Upstream | SHA-256 |", "|---|---|---|"]
    for item in report.files:
        if item.changed:
            digest = f"`{(item.old_sha256 or 'none')[:12]}` to `{item.sha256[:12]}`"
        else:
            digest = f"unchanged (`{item.sha256[:12]}`)"
        lines.append(f"| `openapi/{item.name}` | {item.url} | {digest} |")
    lines.append("")
    if not report.changed:
        lines.append("No upstream changes.")
        return "\n".join(lines) + "\n"

    if report.needs_naming:
        lines += [
            "### Needs SDK naming",
            "",
            "These operations have no `x-didit-sdk` upstream and no entry in `openapi/overlay.json`, so the generator fails "
            "until one is added (docs/DESIGN.md section 4, CONTRIBUTING.md):",
            "",
        ]
        lines += bullet_list(report.needs_naming) + [""]

    lines += ["### Operations", ""]
    for title, items in (("Added", report.added), ("Removed", report.removed), ("Changed", report.changed_ops)):
        lines += [f"#### {title} ({len(items)})", ""]
        lines += bullet_list([f"`{key}`: {name}" for key, name in items]) if items else ["None."]
        lines.append("")
    if report.moved:
        lines += [f"#### Moved ({len(report.moved)})", "", "The SDK method keeps its name and calls the new path.", ""]
        lines += bullet_list([f"`{old}` to `{new}`: {name}" for old, new, name in report.moved]) + [""]

    lines += ["### Schemas", ""]
    for title, names in (("Added", report.schemas_added), ("Removed", report.schemas_removed), ("Changed", report.schemas_changed)):
        lines += [f"#### {title} ({len(names)})", ""]
        lines += bullet_list(names) if names else ["None."]
        lines.append("")

    if report.naming_changes:
        lines += ["### SDK naming changes", ""]
        lines += bullet_list(report.naming_changes) + [""]

    if report.authentication_notes:
        lines += [
            "### Authentication module",
            "",
            "The `authentication` module is hand-written (docs/DESIGN.md section 10). Its contract test fails until the module "
            "follows these operations of `openapi/authentication.json`:",
            "",
        ]
        lines += bullet_list(report.authentication_notes) + [""]

    if report.deprecated:
        lines += [
            "### Deprecated SDK methods",
            "",
            "These server operations were removed or renamed upstream. Their SDK methods are not deleted: they stay, marked "
            f"`[Obsolete]`, until the next minor release. They are recorded in `openapi/{DEPRECATED_FILE}`.",
            "",
        ]
        lines += bullet_list([f"`{key}`: {name}" for key, name in report.deprecated]) + [""]

    if report.overlay_notes:
        lines += ["### Overlay", ""]
        lines += bullet_list(report.overlay_notes) + [""]

    text = "\n".join(lines).rstrip("\n") + "\n"
    if len(text) > SUMMARY_LIMIT:
        cut = text.rfind("\n", 0, SUMMARY_LIMIT)
        text = text[:cut] + "\n\n_Truncated. The full report is in the workflow run summary._\n"
    return text


def commit_message(report: Report) -> str:
    """What release-please reads: the sync, then one `api(spec):` line per breaking change (release-please-config.json
    puts them under "API changes that can break typed code"). Never `!` or a BREAKING CHANGE footer (DESIGN.md 13)."""
    lines = [f"{report.commit_type}(spec): sync API spec {report.date}"]
    notes = [f"api(spec): {change.describe()}" for change in report.breaking[:MAX_RELEASE_NOTES]]
    if len(report.breaking) > MAX_RELEASE_NOTES:
        notes.append(f"api(spec): {len(report.breaking) - MAX_RELEASE_NOTES} more, listed in the spec sync pull request")
    if notes:
        lines += ["", *notes]
    return "\n".join(lines) + "\n"


def write_github_output(report: Report) -> None:
    path = os.environ.get("GITHUB_OUTPUT")
    if not path:
        return
    with open(path, "a", encoding="utf-8") as handle:
        handle.write(f"changed={'true' if report.changed else 'false'}\n")
        handle.write(f"needs_naming={'true' if report.needs_naming else 'false'}\n")
        handle.write(f"type={report.commit_type}\n")
        handle.write(f"date={report.date}\n")


def sorted_operations(operations: dict[str, Any]) -> dict[str, Any]:
    """Operation keys sorted; each entry keeps its own key order, so untouched entries do not churn."""
    return dict(sorted(operations.items()))


def run(
    root: Path,
    offline: Path | None = None,
    write: bool = False,
    summary_path: Path | None = None,
    now: datetime | None = None,
    commit_message_path: Path | None = None,
) -> Report:
    now = (now or datetime.now(timezone.utc)).astimezone(timezone.utc)
    openapi = root / "openapi"
    source_path = openapi / "SOURCE.json"
    overlay_path = openapi / "overlay.json"
    deprecated_path = openapi / DEPRECATED_FILE

    source_text = source_path.read_text("utf-8")
    source = json.loads(source_text)
    overlay_text = overlay_path.read_text("utf-8")
    overlay = json.loads(overlay_text)
    overlay_ops: dict[str, Any] = dict(overlay.get("operations") or {})
    deprecated_original = json.loads(deprecated_path.read_text("utf-8")) if deprecated_path.exists() else empty_deprecated()
    deprecated = copy.deepcopy(deprecated_original)
    deprecated.setdefault("operations", {})
    deprecated.setdefault("schemas", {})
    for api in APIS:
        deprecated["schemas"].setdefault(api, {})

    files: list[Upstream] = []
    for api, name in UPSTREAM_FILES:
        entry = source.get(name)
        if not isinstance(entry, dict) or not isinstance(entry.get("url"), str):
            raise SyncError(f"openapi/SOURCE.json has no url for {name}")
        raw = fetch(name, entry["url"], offline)
        data = validate(name, raw)
        local = openapi / name
        old_raw = local.read_bytes() if local.exists() else b""
        old_data = json.loads(old_raw.decode("utf-8")) if old_raw else {"openapi": "3.0.0", "paths": {}}
        digest = sha256(raw)
        recorded = entry.get("sha256") if isinstance(entry.get("sha256"), str) else None
        files.append(Upstream(api, name, entry["url"], raw, data, old_data, digest, recorded, digest != recorded or raw != old_raw))

    report = Report(date=now.strftime("%Y-%m-%d"), write=write, files=files)
    report.changed = any(item.changed for item in files)
    if report.changed:
        analyse(report, files, overlay_ops, deprecated)

    if report.changed and write:
        stamp = now.strftime("%Y-%m-%dT%H:%M:%SZ")
        for item in files:
            if not item.changed:
                continue
            (openapi / item.name).write_bytes(item.raw)
            entry = source[item.name]
            entry["url"] = item.url
            entry["sha256"] = item.sha256
            entry["fetched_at"] = stamp
        source_path.write_text(dump_json(source, source_text.endswith("\n")), "utf-8", newline="\n")
        if overlay_ops != (overlay.get("operations") or {}):
            overlay["operations"] = sorted_operations(overlay_ops)
            overlay_path.write_text(dump_json(overlay, overlay_text.endswith("\n")), "utf-8", newline="\n")
        if deprecated != deprecated_original:
            if deprecated["operations"]:
                deprecated["operations"] = dict(sorted(deprecated["operations"].items()))
                deprecated["schemas"] = {api: dict(sorted((deprecated["schemas"].get(api) or {}).items())) for api in APIS}
                deprecated_path.write_text(dump_json(deprecated, overlay_text.endswith("\n")), "utf-8", newline="\n")
            elif deprecated_path.exists():
                deprecated_path.unlink()

    summary = render_summary(report)
    if summary_path is not None:
        summary_path.write_text(summary, "utf-8", newline="\n")
    if commit_message_path is not None:
        commit_message_path.write_text(commit_message(report), "utf-8", newline="\n")
    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        with open(step_summary, "a", encoding="utf-8") as handle:
            handle.write(summary)
    write_github_output(report)
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--write", action="store_true", help="update openapi/ in place (default: report only)")
    parser.add_argument("--summary", type=Path, help="write the Markdown report to this file")
    parser.add_argument("--commit-message", type=Path, metavar="FILE", help="write the commit message release-please reads to FILE")
    parser.add_argument("--offline", type=Path, metavar="DIR", help="read the upstream files from DIR instead of the network")
    parser.add_argument("--root", type=Path, default=ROOT, help="repository root (default: the repository that contains this script)")
    args = parser.parse_args(argv)

    try:
        report = run(args.root, offline=args.offline, write=args.write, summary_path=args.summary, commit_message_path=args.commit_message)
    except (SyncError, OSError, json.JSONDecodeError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    if report.changed:
        changed = ", ".join(item.name for item in report.files if item.changed)
        print(
            f"Upstream changed: {changed}. {len(report.added)} added, {len(report.removed)} removed, {len(report.changed_ops)} changed "
            f"operations; {len(report.breaking)} breaking. Commit type: {report.commit_type}(spec)."
        )
        if report.needs_naming:
            print(f"{len(report.needs_naming)} operation(s) need SDK naming in openapi/overlay.json.")
    else:
        print("No upstream changes.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
