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
change that can break typed code, by the rules of DESIGN.md 3.1 that openapi/breaking-vectors.json checks in all seven
server SDKs (classify() runs one of its cases), and the commit message repeats each one, with no cap, as an
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

from didit_codegen.names import pascal  # noqa: E402
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
UNIONS = ("oneOf", "anyOf")

# The kinds of change that can break typed code (DESIGN.md 3.1); every server SDK reports exactly these.
REMOVED = "removed"
TYPE_CHANGED = "type"
NOW_NULL = "now-null"
ENUM_VALUE_REMOVED = "enum-value-removed"
NOW_REQUIRED = "now-required"
NO_LONGER_REQUIRED = "no-longer-required"
# String formats the SDKs map to a type of their own (a date, a date and time, a file), so gaining, losing or changing one
# changes a type.
TYPED_FORMATS = frozenset({"date", "date-time", "binary"})
# A request body that appears or disappears: compared field by field, never as a type change.
NO_BODY: Any = object()


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
    # "registry_checks[].company.registry_data" ("(oneOf 2).iban" inside a union's second variant), a parameter name, or ""
    # for the operation or a whole body
    field: str
    kind: str  # REMOVED, TYPE_CHANGED, NOW_NULL, ENUM_VALUE_REMOVED, NOW_REQUIRED or NO_LONGER_REQUIRED
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
        if self.kind == NO_LONGER_REQUIRED:
            return f"{where}: no longer required"
        return f"{where}: removed"

    def normalized(self) -> str:
        """The line openapi/breaking-vectors.json compares in all seven server SDKs: "<METHOD> <path> <where> <field>: <kind>",
        without the field for a whole body and without where and field for the operation itself."""
        if self.location == "operation":
            return f"{self.operation}: operation removed"
        if self.location == "request" and not self.field and self.kind == NOW_REQUIRED:
            return f"{self.operation}: body now required"
        before, _, after = self.detail.partition("->")
        kind = {
            REMOVED: "removed",
            TYPE_CHANGED: f"type {before} -> {after}",
            NOW_NULL: "can now be null",
            ENUM_VALUE_REMOVED: f"enum value {self.detail} removed",
            NOW_REQUIRED: "now required",
            NO_LONGER_REQUIRED: "no longer required",
        }[self.kind]
        where = "request body" if self.location == "request" else self.location.removesuffix(" parameter")
        return f"{self.operation} {' '.join(part for part in (where, self.field) if part)}: {kind}"


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
    """`node` without its documentation, so that only what a caller can observe compares. `required` and `type` lists
    compare in any order, and an inline union variant keeps the type name its title gives it."""
    if isinstance(node, dict):
        if names:
            return {key: observable(value) for key, value in node.items()}
        kept: dict[str, Any] = {}
        for key, value in node.items():
            if documentation_key(key):
                continue
            if key in VALUE_KEYS:
                kept[key] = value
            elif key in UNIONS and isinstance(value, list):
                kept[key] = [observable_variant(variant) for variant in value]
            elif key in ("required", "type") and isinstance(value, list):
                kept[key] = sorted(value, key=str)
            else:
                kept[key] = observable(value, key in NAME_MAPS)
        return kept
    if isinstance(node, list):
        return [observable(item) for item in node]
    return node


def variant_name(schema: Any) -> str | None:
    """The type name an inline oneOf/anyOf variant's title gives it; None without a title, or for a $ref, whose type is
    named after its component."""
    if not isinstance(schema, dict) or "$ref" in schema:
        return None
    title = schema.get("title")
    return pascal(title) if isinstance(title, str) and title.strip() else None


def observable_variant(variant: Any) -> Any:
    shape = observable(variant)
    name = variant_name(variant)
    if name is not None and isinstance(shape, dict):
        shape["title"] = name
    return shape


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
    """A copy of `node` with every local $ref replaced by its target; a $ref's siblings override the target's keys. A
    component schema loses its title: the SDKs name its type after the component, so only an inline union variant's title
    names a type."""
    if isinstance(node, dict):
        ref = node.get("$ref")
        if isinstance(ref, str):
            if ref in stack or len(stack) >= MAX_REF_DEPTH:
                return {"$cycle": ref}
            try:
                target = dereference(data, pointer(data, ref), stack + (ref,))
            except SyncError:
                return {"$unresolved": ref}
            if not isinstance(target, dict):
                return target
            dropped = ("$ref", "title") if ref.startswith(SCHEMA_REF) else ("$ref",)
            siblings = {key: dereference(data, value, stack) for key, value in node.items() if key not in dropped}
            return {**{key: value for key, value in target.items() if key not in dropped}, **siblings}
        return {key: dereference(data, value, stack) for key, value in node.items()}
    if isinstance(node, list):
        return [dereference(data, item, stack) for item in node]
    return node


def list_of(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def null_variant(schema: Any) -> bool:
    return isinstance(schema, dict) and (schema.get("type") == "null" or schema.get("enum") == [None])


def union_variants(schema: dict[str, Any]) -> list[Any]:
    """The oneOf and anyOf variants other than null."""
    return [variant for keyword in UNIONS for variant in list_of(schema.get(keyword)) if not null_variant(variant)]


def variant_positions(schema: dict[str, Any]) -> list[str]:
    """Each of union_variants(schema) as the breaking changes number it: its keyword and its position from 1 in that
    keyword's list, null variants counted ("anyOf 2" for the second entry of [null, X, Y])."""
    return [f"{keyword} {number}" for keyword in UNIONS for number, variant in enumerate(list_of(schema.get(keyword)), 1) if not null_variant(variant)]  # fmt: skip


def required_names(schema: dict[str, Any]) -> set[str]:
    return {name for name in list_of(schema.get("required")) if isinstance(name, str)}


def with_union_required(variant: Any, union: dict[str, Any]) -> Any:
    """A union variant with the fields the union itself requires, which JSON Schema requires in every variant."""
    inherited = required_names(union)
    if not inherited or not isinstance(variant, dict):
        return variant
    return {**variant, "required": sorted(required_names(variant) | inherited)}


def can_be_null(schema: Any, depth: int = 0) -> bool:
    """Whether the schema accepts null: a null type, nullable, null in its enum, allOf members that all accept it, or a
    null or nullable union variant."""
    if not isinstance(schema, dict) or depth > MAX_SCHEMA_DEPTH:
        return False
    raw = schema.get("type")
    if raw == "null" or (isinstance(raw, list) and "null" in raw) or schema.get("nullable") is True:
        return True
    if None in list_of(schema.get("enum")):
        return True
    members = list_of(schema.get("allOf"))
    if "type" not in schema and members and all(can_be_null(member, depth + 1) for member in members):
        return True
    return any(can_be_null(variant, depth + 1) for keyword in UNIONS for variant in list_of(schema.get(keyword)))


def single(schema: Any, depth: int = 0) -> Any:
    """The schema read as one type: its allOf members merged into it, and a oneOf/anyOf of one non-null variant replaced by
    that variant, so that anyOf [X, null] reads as a nullable X. A union of several variants stays one (compare_variants).
    Nullability is read before (can_be_null), because this drops the null variants."""
    if not isinstance(schema, dict) or depth > MAX_SCHEMA_DEPTH:
        return schema
    members = schema.get("allOf")
    if isinstance(members, list) and all(isinstance(member, dict) for member in members):
        merged = {key: value for key, value in schema.items() if key != "allOf"}
        for member in members:
            for key, value in single(member, depth + 1).items():
                if key == "properties" and isinstance(value, dict):
                    merged["properties"] = {**value, **(merged.get("properties") or {})}
                elif key == "required" and isinstance(value, list):
                    merged["required"] = sorted(required_names(merged) | required_names(member))
                else:
                    merged.setdefault(key, value)
        schema = merged
    variants = union_variants(schema)
    if len(variants) != 1 or not isinstance(variants[0], dict):
        return schema
    rest = {key: value for key, value in schema.items() if key not in (*UNIONS, "required")}
    return single({**with_union_required(variants[0], schema), **rest}, depth + 1)


def json_type(value: Any) -> str:
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, int):
        return "integer"
    if isinstance(value, float):
        return "number"
    if isinstance(value, str):
        return "string"
    return "array" if isinstance(value, list) else "object"


def schema_kinds(schema: Any, depth: int = 0) -> frozenset[str]:
    """The JSON types a schema allows besides null, inferred when it has no type (a union allows its variants' types);
    empty when it does not say, which typed code reads as any JSON value."""
    if not isinstance(schema, dict) or depth > MAX_SCHEMA_DEPTH:
        return frozenset()
    raw = schema.get("type")
    kinds = {kind for kind in (raw if isinstance(raw, list) else [raw]) if isinstance(kind, str)} - {"null"}
    if kinds:
        return frozenset(kinds)
    if any(key in schema for key in ("properties", "additionalProperties", "patternProperties")):
        return frozenset({"object"})
    if "items" in schema:
        return frozenset({"array"})
    variants = union_variants(schema)
    if variants:
        found = [schema_kinds(variant, depth + 1) for variant in variants]
        return frozenset().union(*found) if all(found) else frozenset()
    for member in list_of(schema.get("allOf")):
        kinds |= schema_kinds(member, depth + 1)
    if not kinds:
        values = [schema["const"]] if "const" in schema else list_of(schema.get("enum"))
        kinds = {json_type(value) for value in values if value is not None}
    return frozenset(kinds)


def type_name(schema: dict[str, Any], kinds: frozenset[str]) -> str:
    """The type typed code sees: its JSON types sorted and joined with "|", a string with a typed format as
    "string (date-time)", and "any" when the schema does not say."""
    if not kinds:
        return "any"
    form = schema.get("format")
    typed = isinstance(form, str) and form in TYPED_FORMATS
    return "|".join(f"string ({form})" if kind == "string" and typed else kind for kind in sorted(kinds))


def variant_type_names(schemas: list[Any]) -> str:
    """The types of a union's variants, or of a method's 2xx bodies, one per variant or body: losing one of two object
    variants reads "object|object" to "object"."""
    found: list[str] = []
    for schema in schemas:
        one = single(schema) if isinstance(schema, dict) else {}
        found += type_name(one, schema_kinds(one)).split("|")
    return "|".join(sorted(found))


def enum_values(schema: dict[str, Any]) -> list[Any] | None:
    values = schema.get("enum")
    return [value for value in values if value is not None] if isinstance(values, list) else None


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
        required |= required_names(current)
        stack.extend(list_of(current.get("allOf")))
    return properties, required


def join_field(prefix: str, name: str) -> str:
    return f"{prefix}.{name}" if prefix else name


def compare_schemas(old: Any, new: Any, operation: str, location: str, path: str, found: list[BreakingChange], depth: int = 0) -> None:
    """Appends every change from `old` to `new` (both dereferenced) that DESIGN.md 3.1 counts. In requests and responses: a
    type change either way (a typed format and any included), a removed field or enum value, and a removed union variant.
    Only in a response: a value that can now be null and a field that is no longer required. Only in a request: a field
    that is newly required. Everything else is listed, not counted."""
    if depth > MAX_SCHEMA_DEPTH or observable(old) == observable(new):
        return
    request = location != "response"
    present = old is not NO_BODY and new is not NO_BODY
    old, new = (schema if isinstance(schema, dict) else {} for schema in (old, new))
    # A request that accepts null only widens what a caller may send.
    if not request and can_be_null(new) and not can_be_null(old):
        found.append(BreakingChange(operation, location, path, NOW_NULL))
    old, new = single(old), single(new)
    old_kinds, new_kinds = schema_kinds(old), schema_kinds(new)
    before, after = type_name(old, old_kinds), type_name(new, new_kinds)
    if present and before != after:
        found.append(BreakingChange(operation, location, path, TYPE_CHANGED, f"{before}->{after}"))
        # The paired variants of a union are still compared, also when an untyped variant made it any; the type change
        # already says which variants went. A schema untyped as a whole has none.
        if (old_kinds or union_variants(old)) and (new_kinds or union_variants(new)):
            compare_variants(old, new, operation, location, path, found, depth, retyped=True)
        return

    old_enum, new_enum = enum_values(old), enum_values(new)
    if old_enum is not None and new_enum is not None:
        for value in old_enum:
            if value not in new_enum:
                found.append(BreakingChange(operation, location, path, ENUM_VALUE_REMOVED, json.dumps(value, ensure_ascii=False)))

    old_properties, old_required = object_properties(old)
    new_properties, new_required = object_properties(new)
    # A union's fields are its variants', compared below.
    if (old_properties and new_properties) or not (union_variants(old) or union_variants(new)):
        for name in sorted(old_properties.keys() - new_properties.keys()):
            found.append(BreakingChange(operation, location, join_field(path, name), REMOVED))
        for name in sorted(new_properties.keys() - old_properties.keys()):
            if request and name in new_required:
                found.append(BreakingChange(operation, location, join_field(path, name), NOW_REQUIRED))
        for name in sorted(old_properties.keys() & new_properties.keys()):
            field_path = join_field(path, name)
            if request and name in new_required - old_required:
                found.append(BreakingChange(operation, location, field_path, NOW_REQUIRED))
            elif not request and name in old_required - new_required:
                found.append(BreakingChange(operation, location, field_path, NO_LONGER_REQUIRED))
            compare_schemas(old_properties[name], new_properties[name], operation, location, field_path, found, depth + 1)
    for key, suffix in (("items", "[]"), ("additionalProperties", "{}")):
        old_sub, new_sub = old.get(key), new.get(key)
        if isinstance(old_sub, dict) or isinstance(new_sub, dict):
            old_sub = old_sub if isinstance(old_sub, dict) else {}
            new_sub = new_sub if isinstance(new_sub, dict) else {}
            compare_schemas(old_sub, new_sub, operation, location, path + suffix, found, depth + 1)
    compare_variants(old, new, operation, location, path, found, depth, retyped=False)


def field_names(schema: Any) -> set[str]:
    return set(object_properties(single(schema))[0]) if isinstance(schema, dict) else set()


def pair_variants(old: list[Any], new: list[Any]) -> tuple[list[tuple[int, int]], list[int]]:
    """Pairs each old union variant with the new one it became: an unchanged one first, then one with the same title, then
    one of the same type with the most fields in common. Returns the pairs (old and new position, in new order) and the
    old positions left over (variants removed)."""
    old_left, new_left = list(range(len(old))), list(range(len(new)))
    pairs: list[tuple[int, int]] = []
    old_shapes, new_shapes = [observable_variant(variant) for variant in old], [observable_variant(variant) for variant in new]
    old_names, new_names = [variant_name(variant) for variant in old], [variant_name(variant) for variant in new]
    for same in (lambda i, j: old_shapes[i] == new_shapes[j], lambda i, j: old_names[i] is not None and old_names[i] == new_names[j]):
        for i in list(old_left):
            j = next((j for j in new_left if same(i, j)), None)
            if j is not None:
                pairs.append((i, j))
                old_left.remove(i)
                new_left.remove(j)
    candidates = sorted(
        (-len(field_names(old[i]) & field_names(new[j])), i, j)
        for i in old_left
        for j in new_left
        if not schema_kinds(old[i]) or not schema_kinds(new[j]) or schema_kinds(old[i]) == schema_kinds(new[j])
    )
    for _, i, j in candidates:
        if i in old_left and j in new_left:
            pairs.append((i, j))
            old_left.remove(i)
            new_left.remove(j)
    return sorted(pairs, key=lambda pair: pair[1]), old_left


def compare_variants(old: dict[str, Any], new: dict[str, Any], operation: str, location: str, path: str, found: list[BreakingChange], depth: int, retyped: bool) -> None:  # fmt: skip
    """Compares two unions variant by variant (DESIGN.md 3.1). A schema that is not a union is a union of itself, so that X
    against [X', Y] compares X with X'. A changed field inside the N-th variant of the new union is "<path>(oneOf N).<field>".
    A removed variant is a type change, unless the union's type already changed (`retyped`)."""
    old_variants, new_variants = union_variants(old) or [old], union_variants(new) or [new]
    if len(old_variants) == 1 and len(new_variants) == 1:
        return  # compared as the schema itself
    pairs, removed = pair_variants(old_variants, new_variants)
    positions = variant_positions(new)
    for before, after in pairs:
        variant_path = f"{path}({positions[after]})" if len(new_variants) > 1 else path
        old_variant, new_variant = with_union_required(old_variants[before], old), with_union_required(new_variants[after], new)
        compare_schemas(old_variant, new_variant, operation, location, variant_path, found, depth + 1)
    if removed and not retyped:
        found.append(BreakingChange(operation, location, path, TYPE_CHANGED, f"{variant_type_names(old_variants)}->{variant_type_names(new_variants)}"))  # fmt: skip


def parameters_of(data: Any, op: dict[str, Any]) -> dict[tuple[str, str], dict[str, Any]]:
    found: dict[tuple[str, str], dict[str, Any]] = {}
    for parameter in dereference(data, op.get("parameters") or []):
        if isinstance(parameter, dict):
            found[(str(parameter.get("in", "")), str(parameter.get("name", "")))] = parameter
    return found


def body_schemas(body: Any) -> dict[str, Any]:
    """The schema of a request or response body per media type ({} when a media type has none); empty without a body."""
    content = body.get("content") if isinstance(body, dict) and isinstance(body.get("content"), dict) else {}
    return {media: entry.get("schema") if isinstance(entry.get("schema"), dict) else {} for media, entry in content.items() if isinstance(entry, dict)}  # fmt: skip


def request_body(data: Any, op: dict[str, Any]) -> tuple[bool, dict[str, Any]]:
    """Whether the request body is required, and its schema per media type."""
    body = dereference(data, op.get("requestBody"))
    if not isinstance(body, dict):
        return False, {}
    return body.get("required") is True, body_schemas(body)


def success_schemas(data: Any, op: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """Every 2xx response: its body schema per media type, empty when it has no body."""
    responses = op.get("responses") if isinstance(op.get("responses"), dict) else {}
    return {str(status): body_schemas(dereference(data, responses[status])) for status in sorted(responses, key=str) if str(status).startswith("2")}  # fmt: skip


def compare_bodies(old: dict[str, Any], new: dict[str, Any], operation: str, location: str, found: list[BreakingChange]) -> None:
    """Compares a body per media type; when the two share none, the first against the first, so that a body that changes
    media type (JSON to a PDF), appears or disappears is compared too."""
    pairs = [(media, media) for media in sorted(old.keys() & new.keys())]
    if not pairs and (old or new):
        old, new = old or {"": NO_BODY}, new or {"": NO_BODY}
        pairs = [(min(old), min(new))]
    for before, after in pairs:
        compare_schemas(old[before], new[after], operation, location, "", found)


def compare_statuses(old: dict[str, dict[str, Any]], new: dict[str, dict[str, Any]], removed: list[str], added: list[str], operation: str, found: list[BreakingChange]) -> None:  # fmt: skip
    """The 2xx statuses not compared body by body: `removed` (only `old` has them, or their body disappeared) and `added`
    (only `new` has them, or their body appeared). A method returns every 2xx body, so a status without a body makes it
    nullable, and a removed status whose body no other one returns removes one of its types, "null" when no body is left
    (DESIGN.md 3.1). Any other status change is listed, not counted."""
    new_bodies = [observable(content, True) for content in new.values() if content]
    old_returns_body = any(old.values())
    old_returns_null = not all(old.values())

    def types(responses: dict[str, dict[str, Any]]) -> str:
        distinct: dict[str, Any] = {}
        for content in responses.values():
            if content:
                distinct.setdefault(canonical(observable(content, True)), content.get("application/json", content[min(content)]))
        return variant_type_names(list(distinct.values())) or "null"

    for status in added:
        if not new[status] and old_returns_body and not old_returns_null:
            found.append(BreakingChange(operation, "response", "", NOW_NULL))
    for status in removed:
        if old[status] and observable(old[status], True) not in new_bodies:
            found.append(BreakingChange(operation, "response", "", TYPE_CHANGED, f"{types(old)}->{types(new)}"))


def operation_breaking(old_data: Any, new_data: Any, old: Operation, new: Operation) -> list[BreakingChange]:
    """The changes from `old` to `new` (the same operation, or a moved one) that DESIGN.md 3.1 counts in request and
    response, under the key of `new`."""
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
    compare_bodies(old_bodies, new_bodies, key, "request", found)

    # Every 2xx status both versions have, with a body on both sides or on neither; when the only success status changed
    # (200 to 201), the old body against the new. The other statuses change what the method returns.
    old_responses, new_responses = success_schemas(old_data, old.op), success_schemas(new_data, new.op)
    statuses = [(status, status) for status in old_responses if status in new_responses and bool(old_responses[status]) == bool(new_responses[status])]  # fmt: skip
    only_old, only_new = list(old_responses.items()), list(new_responses.items())
    if not statuses and len(only_old) == 1 == len(only_new) and bool(only_old[0][1]) == bool(only_new[0][1]):
        statuses = [(only_old[0][0], only_new[0][0])]
    else:
        compared = {status for status, _ in statuses}
        removed = [status for status in old_responses if status not in compared]
        added = [status for status in new_responses if status not in compared]
        compare_statuses(old_responses, new_responses, removed, added, key, found)
    for before, after in statuses:
        compare_bodies(old_responses[before], new_responses[after], key, "response", found)
    return list(dict.fromkeys(found))  # one entry per change, even when several responses or media types share it


def on_server(naming: dict[str, Any] | None) -> bool:
    """Whether a server SDK method calls the operation. One without naming counts: the generator fails until it is named."""
    return naming is None or naming.get("server") is True


def breaking_changes(
    old_docs: dict[str, Any],
    new_docs: dict[str, Any],
    old_ops: dict[str, tuple[Operation, dict[str, Any] | None]],
    new_ops: dict[str, tuple[Operation, dict[str, Any] | None]],
    moved: list[tuple[str, str, str]],
) -> list[BreakingChange]:
    """Every change DESIGN.md 3.1 counts, by operation (each with its SDK naming): an operation whose server SDK method goes
    (removed, or no longer on the server SDKs), the changes of a moved one under its new key, and the changes of every other
    operation a server SDK method calls before and after. Operations with server: false are listed, never counted."""
    # The generated server methods after the sync; the hand-written Authentication module's are its own.
    live = {sdk_name(naming) for op, naming in new_ops.values() if op.api != HAND_WRITTEN and sdk_name(naming) is not None}
    moved_to = {old_key: new_key for old_key, new_key, _ in moved}
    found: list[BreakingChange] = []
    for key in sorted(old_ops.keys() - new_ops.keys()):
        op, naming = old_ops[key]
        if sdk_name(naming) is None:
            continue
        if key in moved_to:
            new_op = new_ops[moved_to[key]][0]
            found.extend(operation_breaking(old_docs[op.api], new_docs[new_op.api], op, new_op))
        elif op.api == HAND_WRITTEN or sdk_name(naming) not in live:
            found.append(BreakingChange(key, "operation", "", REMOVED))
    for key in sorted(old_ops.keys() & new_ops.keys()):
        (old, before), (new, after) = old_ops[key], new_ops[key]
        if old.api != HAND_WRITTEN and sdk_name(before) is not None and isinstance(after, dict) and not on_server(after):
            if sdk_name(before) not in live:
                found.append(BreakingChange(key, "operation", "", REMOVED))  # no longer on the server SDKs: its method goes
            continue
        if on_server(before) and on_server(after):
            found.extend(operation_breaking(old_docs[old.api], new_docs[new.api], old, new))
    return found


def classify(before: dict[str, Any], after: dict[str, Any]) -> tuple[str, list[str]]:
    """What the sync makes of verification.json changing from `before` to `after` (complete documents whose operations carry
    their own x-didit-sdk): the commit type, "feat(spec)" or "chore(spec)", and the breaking changes as
    openapi/breaking-vectors.json spells them, distinct and sorted. Nothing is read or written."""
    old_raw, new_raw = canonical(before).encode(), canonical(after).encode()
    files = [Upstream("verification", "verification.json", "", new_raw, after, before, sha256(new_raw), sha256(old_raw), True)]
    report = Report(date="2026-01-01", write=False, files=files, changed=True)
    analyse(report, files, {}, empty_deprecated())
    return f"{report.commit_type}(spec)", sorted({change.normalized() for change in report.breaking})


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
