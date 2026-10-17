#!/usr/bin/env python3
"""Downloads the upstream OpenAPI documents and reports what changed (DESIGN.md section 3.1).

    python3 codegen/spec_sync.py                         report only, write nothing
    python3 codegen/spec_sync.py --write                 update openapi/ in place
    python3 codegen/spec_sync.py --write --summary FILE  also write the Markdown report to FILE
    python3 codegen/spec_sync.py --offline DIR           read verification.json and account.json from DIR

With --write it copies the upstream files byte for byte, records their URL, SHA-256 and fetch time in
openapi/SOURCE.json, deletes overlay entries whose operation now carries x-didit-sdk upstream, and copies removed
server operations into openapi/deprecated-operations.json so their SDK methods stay, marked [Obsolete], until the next
minor release. Writes changed=true|false, needs_naming=true|false and date=YYYY-MM-DD to $GITHUB_OUTPUT when that
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
UPSTREAM_FILES = (("verification", "verification.json"), ("account", "account.json"))
APIS = ("verification", "account")
DEPRECATED_FILE = "deprecated-operations.json"
DEPRECATED_DESCRIPTION = (
    "Server operations removed upstream. Their SDK methods stay, marked [Obsolete], until the next minor release "
    "(docs/DESIGN.md section 3.1); delete an entry in that release. Written by codegen/spec_sync.py. Keys follow "
    "overlay.json (\"<METHOD> <path>\", \"account <METHOD> <path>\"). Each operation is the last upstream definition "
    "with path-level parameters merged and non-schema $refs inlined; its schema $refs point into schemas.<api>."
)
SCHEMA_REF = "#/components/schemas/"
DOWNLOAD_ATTEMPTS = 3
DOWNLOAD_TIMEOUT = 60
MAX_DEPTH = 12
MAX_LIST = 200
SUMMARY_LIMIT = 50_000


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


@dataclass
class Report:
    date: str
    write: bool
    files: list[Upstream]
    changed: bool = False
    added: list[tuple[str, str]] = field(default_factory=list)
    removed: list[tuple[str, str]] = field(default_factory=list)
    changed_ops: list[tuple[str, str]] = field(default_factory=list)
    schemas_added: list[str] = field(default_factory=list)
    schemas_removed: list[str] = field(default_factory=list)
    schemas_changed: list[str] = field(default_factory=list)
    breaking: list[str] = field(default_factory=list)
    overlay_notes: list[str] = field(default_factory=list)
    deprecated: list[tuple[str, str]] = field(default_factory=list)
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
            key = operation_key(api, method, path)
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


def api_of(key: str) -> str:
    return "account" if key.startswith("account ") else "verification"


def schema_label(api: str, name: str) -> str:
    return f"`{name}` ({api})"


# Breaking-change detection


def item_prefix(prefix: str) -> str:
    return prefix[:-1] + "[]." if prefix else "[]."


def property_paths(data: Any, schema: Any, follow: bool, prefix: str = "", seen: frozenset[str] = frozenset(), depth: int = 0) -> set[str]:
    """Dotted paths of every property in `schema`, through inline objects, arrays and allOf/oneOf/anyOf."""
    if depth > MAX_DEPTH or not isinstance(schema, dict):
        return set()
    ref = schema.get("$ref")
    if isinstance(ref, str):
        if not follow or ref in seen:
            return set()
        target = resolve(data, schema)
        return property_paths(data, target, follow, prefix, seen | {ref}, depth + 1)
    found: set[str] = set()
    properties = schema.get("properties")
    if isinstance(properties, dict):
        for name, sub in properties.items():
            found.add(prefix + name)
            found |= property_paths(data, sub, follow, f"{prefix}{name}.", seen, depth + 1)
    for combiner in ("allOf", "oneOf", "anyOf"):
        members = schema.get(combiner)
        if isinstance(members, list):
            for sub in members:
                found |= property_paths(data, sub, follow, prefix, seen, depth + 1)
    if isinstance(schema.get("items"), dict):
        found |= property_paths(data, schema["items"], follow, item_prefix(prefix), seen, depth + 1)
    return found


def required_paths(data: Any, schema: Any, prefix: str = "", seen: frozenset[str] = frozenset(), depth: int = 0) -> set[str]:
    """Dotted paths of every required property in `schema`, following $refs and allOf."""
    if depth > MAX_DEPTH or not isinstance(schema, dict):
        return set()
    ref = schema.get("$ref")
    if isinstance(ref, str):
        if ref in seen:
            return set()
        return required_paths(data, resolve(data, schema), prefix, seen | {ref}, depth + 1)
    found: set[str] = set()
    required = schema.get("required")
    if isinstance(required, list):
        found |= {prefix + name for name in required if isinstance(name, str)}
    properties = schema.get("properties")
    if isinstance(properties, dict):
        for name, sub in properties.items():
            found |= required_paths(data, sub, f"{prefix}{name}.", seen, depth + 1)
    members = schema.get("allOf")
    if isinstance(members, list):
        for sub in members:
            found |= required_paths(data, sub, prefix, seen, depth + 1)
    if isinstance(schema.get("items"), dict):
        found |= required_paths(data, schema["items"], item_prefix(prefix), seen, depth + 1)
    return found


def parameters_of(data: Any, op: dict[str, Any]) -> dict[tuple[str, str], dict[str, Any]]:
    found: dict[tuple[str, str], dict[str, Any]] = {}
    for parameter in op.get("parameters") or []:
        resolved = resolve(data, parameter)
        if isinstance(resolved, dict):
            found[parameter_key(data, parameter)] = resolved
    return found


def request_schemas(data: Any, op: dict[str, Any]) -> dict[str, Any]:
    body = resolve(data, op.get("requestBody"))
    if not isinstance(body, dict):
        return {}
    content = body.get("content") or {}
    return {media: entry["schema"] for media, entry in content.items() if isinstance(entry, dict) and isinstance(entry.get("schema"), dict)}


def response_schemas(data: Any, op: dict[str, Any]) -> dict[str, Any]:
    found: dict[str, Any] = {}
    for status, response in (op.get("responses") or {}).items():
        if not str(status).startswith("2"):
            continue
        resolved = resolve(data, response)
        if not isinstance(resolved, dict):
            continue
        for media, entry in (resolved.get("content") or {}).items():
            if isinstance(entry, dict) and isinstance(entry.get("schema"), dict):
                found[f"{status} {media}"] = entry["schema"]
    return found


def body_required(data: Any, op: dict[str, Any]) -> bool:
    body = resolve(data, op.get("requestBody"))
    return isinstance(body, dict) and body.get("required") is True


def operation_breaking(old_data: Any, new_data: Any, old: Operation, new: Operation, name: str) -> list[str]:
    found: list[str] = []
    where = f"`{new.key}` ({name})"
    old_parameters = parameters_of(old_data, old.effective)
    for key, parameter in sorted(parameters_of(new_data, new.effective).items()):
        if parameter.get("required") is True and not (key in old_parameters and old_parameters[key].get("required") is True):
            found.append(f"Newly required {key[0]} parameter `{key[1]}` on {where}")
    if body_required(new_data, new.op) and not body_required(old_data, old.op):
        found.append(f"Request body is now required on {where}")
    old_bodies = request_schemas(old_data, old.op)
    new_bodies = request_schemas(new_data, new.op)
    old_required: set[str] = set()
    for schema in old_bodies.values():
        old_required |= required_paths(old_data, schema)
    new_required: set[str] = set()
    for schema in new_bodies.values():
        new_required |= required_paths(new_data, schema)
    for path in sorted(new_required - old_required):
        found.append(f"Newly required request property `{path}` on {where}")
    for kind, old_map, new_map in (
        ("request", old_bodies, new_bodies),
        ("response", response_schemas(old_data, old.op), response_schemas(new_data, new.op)),
    ):
        gone: set[str] = set()
        for media in old_map.keys() & new_map.keys():
            old_schema, new_schema = old_map[media], new_map[media]
            if "$ref" in old_schema and "$ref" in new_schema:
                continue  # component schemas are compared on their own
            gone |= property_paths(old_data, old_schema, True) - property_paths(new_data, new_schema, True)
        for path in sorted(gone):
            found.append(f"Removed {kind} property `{path}` from {where}")
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
            if isinstance(entry, dict) and renamed(entry, upstream):
                report.breaking.append(f"SDK naming of `{key}` changes from `{label(entry)}` to `{label(upstream)}`")

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

    for key in sorted(old_ops.keys() - new_ops.keys()):
        op = old_ops[key]
        naming = naming_of(op, original_overlay)
        name = label(naming)
        report.removed.append((key, name))
        if key in overlay_ops:
            del overlay_ops[key]
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
            report.breaking.append(
                f"Removed operation `{key}` ({name}): the SDK method stays, marked `[Obsolete]`, until the next minor release"
            )
        else:
            if key in original_overlay:
                report.overlay_notes.append(f"Deleted `{key}`: the operation was removed upstream.")
            report.breaking.append(f"Removed operation `{key}` ({name})")

    for key in sorted(old_ops.keys() & new_ops.keys()):
        old, new = old_ops[key], new_ops[key]
        if canonical(old.effective) == canonical(new.effective):
            continue
        name = label(naming_of(new, overlay_ops))
        report.changed_ops.append((key, name))
        report.breaking.extend(operation_breaking(old_docs[old.api], new_docs[new.api], old, new, name))

    for key in sorted(original_overlay):
        if key not in old_ops and key not in new_ops:
            report.overlay_notes.append(f"`{key}` matches no operation upstream (left in place; delete it if it is obsolete).")

    # Component schemas.
    for api in APIS:
        old_schemas = old_docs.get(api, {}).get("components", {}).get("schemas", {}) or {}
        new_schemas = new_docs.get(api, {}).get("components", {}).get("schemas", {}) or {}
        for name in sorted(new_schemas.keys() - old_schemas.keys()):
            report.schemas_added.append(schema_label(api, name))
        for name in sorted(old_schemas.keys() - new_schemas.keys()):
            report.schemas_removed.append(schema_label(api, name))
            report.breaking.append(f"Removed schema {schema_label(api, name)}")
        for name in sorted(old_schemas.keys() & new_schemas.keys()):
            if canonical(old_schemas[name]) == canonical(new_schemas[name]):
                continue
            report.schemas_changed.append(schema_label(api, name))
            gone = property_paths(old_docs[api], old_schemas[name], False) - property_paths(new_docs[api], new_schemas[name], False)
            for path in sorted(gone):
                report.breaking.append(f"Removed property `{path}` from schema {schema_label(api, name)}")

    # Keep only the deprecated schemas that a remaining deprecated operation still references.
    for api in APIS:
        stored = deprecated["schemas"].get(api) or {}
        keep: dict[str, Any] = {}
        for key, record in deprecated["operations"].items():
            if api_of(key) == api:
                keep.update(schema_closure(stored, record.get("operation")))
        deprecated["schemas"][api] = keep


def bullet_list(items: list[str]) -> list[str]:
    lines = [f"- {item}" for item in items[:MAX_LIST]]
    if len(items) > MAX_LIST:
        lines.append(f"- ... and {len(items) - MAX_LIST} more")
    return lines


def render_summary(report: Report) -> str:
    lines = ["Upstream API spec sync (docs/DESIGN.md section 3.1), generated by `codegen/spec_sync.py`.", ""]
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

    lines += ["### Breaking", ""]
    lines += bullet_list(report.breaking) if report.breaking else ["None."]
    lines.append("")

    lines += ["### Operations", ""]
    for title, items in (("Added", report.added), ("Removed", report.removed), ("Changed", report.changed_ops)):
        lines += [f"#### {title} ({len(items)})", ""]
        lines += bullet_list([f"`{key}`: {name}" for key, name in items]) if items else ["None."]
        lines.append("")

    lines += ["### Schemas", ""]
    for title, names in (("Added", report.schemas_added), ("Removed", report.schemas_removed), ("Changed", report.schemas_changed)):
        lines += [f"#### {title} ({len(names)})", ""]
        lines += bullet_list(names) if names else ["None."]
        lines.append("")

    if report.deprecated:
        lines += [
            "### Deprecated SDK methods",
            "",
            "These server operations were removed upstream. Their SDK methods are not deleted: they stay, marked "
            f"`[Obsolete]`, until the next minor release. Their last definitions are kept in `openapi/{DEPRECATED_FILE}`.",
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


def write_github_output(report: Report) -> None:
    path = os.environ.get("GITHUB_OUTPUT")
    if not path:
        return
    with open(path, "a", encoding="utf-8") as handle:
        handle.write(f"changed={'true' if report.changed else 'false'}\n")
        handle.write(f"needs_naming={'true' if report.needs_naming else 'false'}\n")
        handle.write(f"date={report.date}\n")


def sorted_operations(operations: dict[str, Any]) -> dict[str, Any]:
    return {key: dict(sorted(value.items())) if isinstance(value, dict) else value for key, value in sorted(operations.items())}


def run(root: Path, offline: Path | None = None, write: bool = False, summary_path: Path | None = None, now: datetime | None = None) -> Report:
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
    parser.add_argument("--offline", type=Path, metavar="DIR", help="read verification.json and account.json from DIR instead of the network")
    parser.add_argument("--root", type=Path, default=ROOT, help="repository root (default: the repository that contains this script)")
    args = parser.parse_args(argv)

    try:
        report = run(args.root, offline=args.offline, write=args.write, summary_path=args.summary)
    except (SyncError, OSError, json.JSONDecodeError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    if report.changed:
        changed = ", ".join(item.name for item in report.files if item.changed)
        print(f"Upstream changed: {changed}. {len(report.added)} added, {len(report.removed)} removed, {len(report.changed_ops)} changed operations; {len(report.breaking)} breaking.")
        if report.needs_naming:
            print(f"{len(report.needs_naming)} operation(s) need SDK naming in openapi/overlay.json.")
    else:
        print("No upstream changes.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
