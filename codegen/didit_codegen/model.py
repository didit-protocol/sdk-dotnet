"""Builds the SDK model (operations and types) from the loaded spec, following DESIGN.md sections 4, 5.1 and 8."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any

from . import names
from .spec import APPLICATION_PREFIX, Document, RawOperation, Spec, SpecError

REQUEST = "request"
RESPONSE = "response"


@dataclass(frozen=True)
class TypeRef:
    kind: str  # string, long, double, bool, datetime, any, file, list, map, named
    item: "TypeRef | None" = None
    name: str | None = None
    nullable: bool = False

    def with_nullable(self, nullable: bool) -> "TypeRef":
        return TypeRef(self.kind, self.item, self.name, nullable)


@dataclass
class Prop:
    json_name: str
    cs_name: str
    type: TypeRef
    required: bool
    nullable: bool
    description: str | None
    location: str = "body"  # body or query
    deprecated: bool = False
    example: Any = None
    schema: dict[str, Any] | None = None


@dataclass
class ObjectDef:
    name: str
    description: str | None
    role: str
    origin: str
    props: list[Prop] = field(default_factory=list)


@dataclass
class EnumDef:
    name: str
    description: str | None
    values: list[str]
    origin: str


@dataclass
class Param:
    name: str  # wire name
    cs_name: str  # C# argument or property name
    type: TypeRef
    required: bool
    description: str | None
    example: Any = None


@dataclass
class Body:
    content_type: str  # application/json or multipart/form-data
    type: TypeRef
    required: bool
    params_type: str | None  # set when the body type is the generated <Resource><Method>Params
    example: Any = None
    schema: dict[str, Any] | None = None


@dataclass
class Response:
    kind: str  # json, page, binary, none
    status: str
    type: TypeRef | None = None
    content_type: str | None = None
    example: Any = None


@dataclass
class Operation:
    raw: RawOperation
    api: str
    key: str
    http_method: str
    path: str
    resource: list[str]
    method: str
    auth: str
    scope: str | None
    summary: str | None
    description: str | None
    deprecated: bool
    path_params: list[Param]
    query_params: list[Param]
    body: Body | None
    response: Response
    params_type: str | None = None  # the generated Params type holding query params and/or the inline body
    query_args: list[Param] = field(default_factory=list)  # query params passed as method arguments
    idempotency_key: bool = False

    @property
    def dotted(self) -> str:
        return ".".join(self.resource + [self.method])


@dataclass
class Model:
    operations: list[Operation]
    objects: dict[str, ObjectDef]
    enums: dict[str, EnumDef]
    page_items: list[TypeRef]
    optional_inner_types: list[TypeRef]


def _has_binary(doc: Document, schema: Any, depth: int = 0) -> bool:
    if depth > 8 or not isinstance(schema, dict):
        return False
    schema = doc.resolve(schema)
    if schema.get("format") == "binary":
        return True
    for prop in (schema.get("properties") or {}).values():
        if _has_binary(doc, prop, depth + 1):
            return True
    if isinstance(schema.get("items"), dict) and _has_binary(doc, schema["items"], depth + 1):
        return True
    for key in ("allOf", "oneOf", "anyOf"):
        for part in schema.get(key) or []:
            if _has_binary(doc, part, depth + 1):
                return True
    return False


def _first_example(media: dict[str, Any] | None) -> Any:
    if not media:
        return None
    if "example" in media:
        return media["example"]
    examples = media.get("examples")
    if isinstance(examples, dict):
        for value in examples.values():
            if isinstance(value, dict) and "value" in value:
                return value["value"]
    schema = media.get("schema")
    if isinstance(schema, dict) and "example" in schema:
        return schema["example"]
    return None


class ModelBuilder:
    def __init__(self, spec: Spec) -> None:
        self.spec = spec
        self.objects: dict[str, ObjectDef] = {}
        self.enums: dict[str, EnumDef] = {}
        self.used_names: set[str] = set(names.RESERVED_TYPE_NAMES)
        self.component_refs: dict[tuple[str, str], TypeRef] = {}
        self.component_names: dict[tuple[str, str], str] = {}
        self.request_components: dict[str, set[str]] = {}
        self.page_items: list[TypeRef] = []
        self.optional_inner: dict[str, TypeRef] = {}

    # ---- names -------------------------------------------------------------------------------------------------

    def _unique(self, name: str) -> str:
        if name not in self.used_names:
            self.used_names.add(name)
            return name
        index = 2
        while f"{name}{index}" in self.used_names:
            index += 1
        unique = f"{name}{index}"
        self.used_names.add(unique)
        return unique

    def _reserve_component_names(self) -> None:
        for doc in (self.spec.verification, self.spec.account):
            for comp in sorted(doc.schemas):
                base = names.component_name(comp)
                if doc.api == "account" and base in self.used_names:
                    base = "Account" + base
                self.component_names[(doc.api, comp)] = self._unique(base)

    # ---- reachability ------------------------------------------------------------------------------------------

    def _collect_refs(self, doc: Document, node: Any, acc: set[str]) -> None:
        if isinstance(node, dict):
            ref = node.get("$ref")
            if isinstance(ref, str):
                if ref.startswith("#/components/schemas/"):
                    comp = ref.split("/")[-1]
                    if comp not in acc:
                        acc.add(comp)
                        self._collect_refs(doc, doc.schemas.get(comp), acc)
                else:
                    self._collect_refs(doc, doc.resolve(node), acc)
            for key, value in node.items():
                if key != "$ref":
                    self._collect_refs(doc, value, acc)
        elif isinstance(node, list):
            for value in node:
                self._collect_refs(doc, value, acc)

    def _compute_request_components(self) -> None:
        for doc in (self.spec.verification, self.spec.account):
            self.request_components[doc.api] = set()
        for raw in self.spec.server_operations:
            body = raw.op.get("requestBody")
            if body:
                self._collect_refs(raw.doc, body, self.request_components[raw.doc.api])

    # ---- type resolution ---------------------------------------------------------------------------------------

    def component(self, doc: Document, comp: str) -> TypeRef:
        key = (doc.api, comp)
        if key in self.component_refs:
            return self.component_refs[key]
        if comp not in doc.schemas:
            raise SpecError(f"{doc.file}: unknown component {comp}")
        schema = doc.schemas[comp]
        name = self.component_names[key]
        role = REQUEST if comp in self.request_components[doc.api] else RESPONSE
        kind = self._shape(doc, schema)
        if kind == "object":
            ref = TypeRef("named", name=name)
            self.component_refs[key] = ref
            self._build_object(doc, schema, name, role, f"{doc.file}#/components/schemas/{comp}")
            return ref
        if kind == "enum":
            ref = TypeRef("named", name=name)
            self.component_refs[key] = ref
            resolved = self.resolve(doc, schema, name, role, reserve=False)
            return resolved
        # Aliases of primitives, arrays and maps resolve to their target type.
        placeholder = TypeRef("any")
        self.component_refs[key] = placeholder
        resolved = self.resolve(doc, schema, name, role, reserve=False)
        self.component_refs[key] = resolved
        return resolved

    def _shape(self, doc: Document, schema: dict[str, Any]) -> str:
        schema = doc.resolve(schema)
        if self._enum_values(schema) is not None:
            return "enum"
        if schema.get("properties"):
            return "object"
        if "allOf" in schema:
            meaningful = [p for p in schema["allOf"] if self._meaningful(p)]
            if len(meaningful) == 1 and not schema.get("properties"):
                return self._shape(doc, meaningful[0])
            return "object"
        if "oneOf" in schema or "anyOf" in schema:
            variants = [doc.resolve(v) for v in schema.get("oneOf") or schema.get("anyOf") or []]
            if variants and all(self._objectish(doc, v) for v in variants):
                return "object"
            enum_like = self._merged_enum(doc, variants)
            if enum_like is not None:
                return "enum"
            return "other"
        return "other"

    @staticmethod
    def _meaningful(part: Any) -> bool:
        if not isinstance(part, dict):
            return False
        return any(k in part for k in ("$ref", "type", "properties", "allOf", "oneOf", "anyOf", "enum", "items", "additionalProperties"))

    def _objectish(self, doc: Document, schema: dict[str, Any]) -> bool:
        schema = doc.resolve(schema)
        if schema.get("properties"):
            return True
        if "allOf" in schema:
            return any(self._objectish(doc, p) for p in schema["allOf"] if self._meaningful(p))
        return False

    @staticmethod
    def _enum_values(schema: dict[str, Any]) -> list[Any] | None:
        values = schema.get("enum")
        if not isinstance(values, list):
            return None
        if schema.get("type") not in (None, "string"):
            return None
        non_null = [v for v in values if v is not None]
        if not non_null or not all(isinstance(v, str) for v in non_null):
            return None
        return values

    def _merged_enum(self, doc: Document, variants: list[dict[str, Any]]) -> list[str] | None:
        """drf-spectacular style oneOf [SomeEnum, BlankEnum, NullEnum] becomes one nullable enum."""
        values: list[str] = []
        found = False
        for variant in variants:
            variant = doc.resolve(variant)
            enum = self._enum_values(variant)
            if enum is None:
                return None
            for value in enum:
                if value is not None and value not in values:
                    values.append(value)
            found = True
        return values if found and values else None

    def resolve(self, doc: Document, schema: Any, hint: str, role: str, reserve: bool = True) -> TypeRef:
        if not isinstance(schema, dict) or not schema:
            return TypeRef("any", nullable=True)
        if "$ref" in schema:
            ref = schema["$ref"]
            if ref.startswith("#/components/schemas/"):
                target = self.component(doc, ref.split("/")[-1])
                return target.with_nullable(bool(schema.get("nullable")) or target.nullable)
            return self.resolve(doc, doc.resolve(schema), hint, role, reserve)

        nullable = bool(schema.get("nullable"))

        if "allOf" in schema:
            meaningful = [p for p in schema["allOf"] if self._meaningful(p)]
            if len(meaningful) == 1 and not schema.get("properties"):
                inner = self.resolve(doc, meaningful[0], hint, role, reserve)
                return inner.with_nullable(nullable or inner.nullable)
            name = self._unique(hint) if reserve else hint
            self._build_merged(doc, meaningful + ([{"type": "object", "properties": schema["properties"], "required": schema.get("required", [])}] if schema.get("properties") else []), name, role, all_required=True, origin=hint, description=schema.get("description"))
            return TypeRef("named", name=name, nullable=nullable)

        variants_key = "oneOf" if "oneOf" in schema else ("anyOf" if "anyOf" in schema else None)
        if variants_key:
            variants = schema[variants_key]
            resolved_variants = [doc.resolve(v) for v in variants]
            if any(v.get("nullable") for v in resolved_variants if isinstance(v, dict)):
                nullable = True
            if resolved_variants and all(self._objectish(doc, v) for v in resolved_variants):
                name = self._unique(hint) if reserve else hint
                self._build_merged(doc, variants, name, role, all_required=False, origin=hint, description=schema.get("description"))
                return TypeRef("named", name=name, nullable=nullable)
            enum_values = self._merged_enum(doc, resolved_variants)
            if enum_values is not None:
                if any(None in (doc.resolve(v).get("enum") or []) for v in variants):
                    nullable = True
                single_refs = [v["$ref"] for v in variants if isinstance(v, dict) and "$ref" in v and None not in (doc.resolve(v).get("enum") or []) and "" not in (doc.resolve(v).get("enum") or [])]
                if len(single_refs) == 1 and set(enum_values) - {""} == set(v for v in (doc.resolve({"$ref": single_refs[0]}).get("enum") or []) if v):
                    target = self.component(doc, single_refs[0].split("/")[-1])
                    return target.with_nullable(True if nullable else target.nullable)
                name = self._unique(hint) if reserve else hint
                self.enums[name] = EnumDef(name, schema.get("description"), enum_values, hint)
                return TypeRef("named", name=name, nullable=nullable)
            return TypeRef("any", nullable=True)

        enum = self._enum_values(schema)
        if enum is not None:
            values = [v for v in enum if v is not None]
            name = self._unique(hint) if reserve else hint
            self.enums[name] = EnumDef(name, schema.get("description"), values, hint)
            return TypeRef("named", name=name, nullable=nullable or None in enum)
        if isinstance(schema.get("enum"), list):
            non_null = [v for v in schema["enum"] if v is not None]
            if not non_null:
                return TypeRef("any", nullable=True)

        kind = schema.get("type")
        if kind == "object" or schema.get("properties"):
            if schema.get("properties"):
                name = self._unique(hint) if reserve else hint
                self._build_object(doc, schema, name, role, hint)
                return TypeRef("named", name=name, nullable=nullable)
            additional = schema.get("additionalProperties")
            if isinstance(additional, dict) and additional:
                value = self.resolve(doc, additional, hint + "Value", role)
                return TypeRef("map", item=value, nullable=nullable)
            return TypeRef("any", nullable=True)
        if kind == "array":
            item = self.resolve(doc, schema.get("items"), hint, role, reserve)
            return TypeRef("list", item=item, nullable=nullable)
        if kind == "string":
            fmt = schema.get("format")
            if fmt == "date-time":
                return TypeRef("datetime", nullable=nullable)
            if fmt == "binary":
                return TypeRef("file", nullable=nullable)
            return TypeRef("string", nullable=nullable)
        if kind == "integer":
            return TypeRef("long", nullable=nullable)
        if kind == "number":
            return TypeRef("double", nullable=nullable)
        if kind == "boolean":
            return TypeRef("bool", nullable=nullable)
        return TypeRef("any", nullable=True)

    def _build_object(self, doc: Document, schema: dict[str, Any], name: str, role: str, origin: str) -> None:
        obj = ObjectDef(name, schema.get("description"), role, origin)
        self.objects[name] = obj
        required = set(schema.get("required") or [])
        for json_name, prop_schema in (schema.get("properties") or {}).items():
            self._add_prop(doc, obj, json_name, prop_schema, json_name in required)

    def _build_merged(self, doc: Document, parts: list[Any], name: str, role: str, all_required: bool, origin: str, description: str | None) -> None:
        obj = ObjectDef(name, description, role, origin)
        self.objects[name] = obj
        collected: dict[str, list[dict[str, Any]]] = {}
        required_sets: list[set[str]] = []
        for part in parts:
            for schema in self._flatten(doc, part):
                props = schema.get("properties") or {}
                required_sets.append(set(schema.get("required") or []))
                for json_name, prop_schema in props.items():
                    collected.setdefault(json_name, []).append(prop_schema)
                if obj.description is None and schema.get("description"):
                    obj.description = schema.get("description")
        if all_required:
            required = set().union(*required_sets) if required_sets else set()
        else:
            required = set()
        for json_name, schemas in collected.items():
            self._add_prop(doc, obj, json_name, self._merge_prop_schemas(doc, schemas), json_name in required)

    def _flatten(self, doc: Document, part: Any) -> list[dict[str, Any]]:
        schema = doc.resolve(part)
        if not isinstance(schema, dict):
            return []
        result: list[dict[str, Any]] = []
        for sub in schema.get("allOf") or []:
            result.extend(self._flatten(doc, sub))
        if schema.get("properties"):
            result.append(schema)
        return result

    def _merge_prop_schemas(self, doc: Document, schemas: list[dict[str, Any]]) -> dict[str, Any]:
        unique: list[dict[str, Any]] = []
        for schema in schemas:
            if not any(json.dumps(schema, sort_keys=True) == json.dumps(u, sort_keys=True) for u in unique):
                unique.append(schema)
        if len(unique) == 1:
            return unique[0]
        resolved = [doc.resolve(s) for s in unique]
        enums = [self._enum_values(s) for s in resolved]
        if all(e is not None for e in enums):
            values: list[Any] = []
            for e in enums:
                for v in e or []:
                    if v not in values:
                        values.append(v)
            merged: dict[str, Any] = {"type": "string", "enum": values}
            for s in resolved:
                if s.get("description"):
                    merged["description"] = s["description"]
                    break
            return merged
        types = {s.get("type") for s in resolved}
        refs = {s.get("$ref") for s in unique}
        if len(refs) == 1 and None not in refs:
            return unique[0]
        if len(types) == 1 and None not in types and types != {"object"} and types != {"array"}:
            merged = dict(resolved[0])
            merged.pop("enum", None)
            return merged
        return {"description": next((s.get("description") for s in resolved if s.get("description")), None)}

    def _add_prop(self, doc: Document, obj: ObjectDef, json_name: str, prop_schema: Any, required: bool, location: str = "body") -> None:
        cs_name = names.pascal(json_name)
        if cs_name == obj.name:
            cs_name += "Value"
        if cs_name in ("AdditionalProperties", "EqualityContract"):
            cs_name += "Value"
        existing = {p.cs_name for p in obj.props}
        if cs_name in existing:
            index = 2
            while f"{cs_name}{index}" in existing:
                index += 1
            cs_name = f"{cs_name}{index}"
        resolved_schema = doc.resolve(prop_schema) if isinstance(prop_schema, dict) else {}
        prop_type = self.resolve(doc, prop_schema, obj.name + names.pascal(json_name), obj.role)
        nullable = prop_type.nullable or bool(isinstance(prop_schema, dict) and prop_schema.get("nullable"))
        example = resolved_schema.get("example") if isinstance(resolved_schema, dict) else None
        if isinstance(prop_schema, dict) and "example" in prop_schema:
            example = prop_schema["example"]
        description = None
        if isinstance(prop_schema, dict):
            description = prop_schema.get("description") or (resolved_schema.get("description") if isinstance(resolved_schema, dict) else None)
        prop = Prop(
            json_name=json_name,
            cs_name=cs_name,
            type=prop_type,
            required=required,
            nullable=nullable,
            description=description,
            location=location,
            deprecated=bool(isinstance(prop_schema, dict) and prop_schema.get("deprecated")),
            example=example,
            schema=resolved_schema if isinstance(resolved_schema, dict) else None,
        )
        obj.props.append(prop)
        if obj.role == REQUEST and not required and location == "body":
            self._note_optional(prop_type.with_nullable(nullable))

    def _note_optional(self, ref: TypeRef) -> None:
        self.optional_inner[json.dumps(_ref_key(ref))] = ref

    # ---- operations --------------------------------------------------------------------------------------------

    def build(self) -> Model:
        self._reserve_component_names()
        self._compute_request_components()
        operations = [self._operation(raw) for raw in sorted(self.spec.server_operations, key=lambda o: (o.naming.resource, o.naming.method))]
        page_items = []
        seen = set()
        for op in operations:
            if op.response.kind == "page" and op.response.type is not None:
                key = json.dumps(_ref_key(op.response.type))
                if key not in seen:
                    seen.add(key)
                    page_items.append(op.response.type)
        return Model(operations, self.objects, self.enums, page_items, list(self.optional_inner.values()))

    def _parameters(self, raw: RawOperation) -> list[dict[str, Any]]:
        merged: dict[tuple[str, str], dict[str, Any]] = {}
        for source in (raw.path_item.get("parameters") or [], raw.op.get("parameters") or []):
            for param in source:
                param = raw.doc.resolve(param)
                merged[(param["name"], param["in"])] = param
        return list(merged.values())

    def _operation(self, raw: RawOperation) -> Operation:
        doc = raw.doc
        naming = raw.naming
        base_name = names.resource_type_name(raw.resource_segments) + names.pascal(naming.method)
        params_name = base_name + "Params"
        params = self._parameters(raw)

        path_params: list[Param] = []
        query_params: list[Param] = []
        idempotency = False
        for param in params:
            location = param["in"]
            pname = param["name"]
            if location == "path":
                if naming.scope == "application" and pname in ("organization_id", "application_id"):
                    continue
                schema = doc.resolve(param.get("schema") or {})
                ptype = TypeRef("long") if schema.get("type") == "integer" else TypeRef("string")
                path_params.append(Param(pname, names.camel(pname), ptype, True, param.get("description"), param.get("example", schema.get("example"))))
            elif location == "query":
                query_params.append(Param(pname, names.pascal(pname), TypeRef("any"), bool(param.get("required")), param.get("description"), param.get("example", (param.get("schema") or {}).get("example"))))
            elif location == "header" and pname.lower() == "idempotency-key":
                idempotency = True

        order = {name: raw.path.find("{" + name + "}") for name in (p.name for p in path_params)}
        missing = [p.name for p in path_params if order[p.name] < 0]
        if missing:
            raise SpecError(f"{raw.key}: path parameters {missing} do not appear in the path")
        path_params.sort(key=lambda p: order[p.name])
        for name in _path_placeholders(raw.path):
            if name not in {p.name for p in path_params} and not (naming.scope == "application" and name in ("organization_id", "application_id")):
                raise SpecError(f"{raw.key}: path placeholder {{{name}}} has no parameter definition")

        body = self._body(raw, params_name)
        response = self._response(raw, base_name, query_params)

        op = Operation(
            raw=raw,
            api=doc.api,
            key=raw.key,
            http_method=raw.http_method.upper(),
            path=raw.path,
            resource=raw.resource_segments,
            method=naming.method,
            auth=naming.auth,
            scope=naming.scope,
            summary=raw.op.get("summary"),
            description=raw.op.get("description"),
            deprecated=bool(raw.op.get("deprecated")),
            path_params=path_params,
            query_params=query_params,
            body=body,
            response=response,
            idempotency_key=idempotency,
        )

        # Query parameters live on the Params type: the inline body's type, or a query-only Params type.
        raw_query = [p for p in params if p["in"] == "query"]
        if raw_query:
            if body is not None and body.params_type is not None:
                target = self.objects[body.params_type]
                op.params_type = body.params_type
            elif body is None:
                name = self._unique(params_name)
                target = ObjectDef(name, raw.op.get("summary"), REQUEST, raw.key)
                self.objects[name] = target
                op.params_type = name
            else:
                target = None
            resolved_query: list[Param] = []
            for param in raw_query:
                schema = param.get("schema") or {}
                hint = (op.params_type or params_name) + names.pascal(param["name"])
                ptype = self.resolve(doc, schema, hint, REQUEST)
                if ptype.kind in ("named",) and ptype.name in self.objects:
                    ptype = TypeRef("string", nullable=ptype.nullable)
                description = param.get("description") or doc.resolve(schema).get("description") if isinstance(schema, dict) else param.get("description")
                if target is not None:
                    existing = {p.cs_name for p in target.props}
                    cs_name = names.pascal(param["name"])
                    if cs_name in existing or cs_name == target.name:
                        cs_name += "Query"
                    target.props.append(Prop(param["name"], cs_name, ptype, bool(param.get("required")), not param.get("required"), description, location="query", example=param.get("example", schema.get("example") if isinstance(schema, dict) else None), schema=doc.resolve(schema) if isinstance(schema, dict) else None))
                    resolved_query.append(Param(param["name"], cs_name, ptype, bool(param.get("required")), description))
                else:
                    arg = Param(param["name"], names.camel(param["name"]), ptype, bool(param.get("required")), description, param.get("example"))
                    op.query_args.append(arg)
                    resolved_query.append(arg)
            op.query_params = resolved_query
        elif body is not None and body.params_type is not None:
            op.params_type = body.params_type
        return op

    def _body(self, raw: RawOperation, params_name: str) -> Body | None:
        doc = raw.doc
        request_body = raw.op.get("requestBody")
        if not request_body:
            return None
        request_body = doc.resolve(request_body)
        content = request_body.get("content") or {}
        json_media = content.get("application/json")
        multipart_media = content.get("multipart/form-data")
        if json_media is not None and not _has_binary(doc, json_media.get("schema")):
            content_type, media = "application/json", json_media
        elif multipart_media is not None:
            content_type, media = "multipart/form-data", multipart_media
        elif json_media is not None:
            content_type, media = "application/json", json_media
        else:
            raise SpecError(f"{raw.key}: unsupported request content types {sorted(content)}")
        schema = media.get("schema") or {}
        required = bool(request_body.get("required"))
        if "$ref" in schema:
            body_type = self.resolve(doc, schema, params_name, REQUEST)
            params_type = None
        else:
            body_type = self.resolve(doc, schema, params_name, REQUEST)
            params_type = body_type.name if body_type.kind == "named" and body_type.name in self.objects else None
        return Body(content_type, body_type, required, params_type, _first_example(media), doc.resolve(schema))

    def _response(self, raw: RawOperation, base_name: str, query_params: list[Param]) -> Response:
        doc = raw.doc
        responses = raw.op.get("responses") or {}
        success = sorted(code for code in responses if str(code).startswith("2"))
        if not success:
            raise SpecError(f"{raw.key}: no 2xx response")
        json_schemas: list[dict[str, Any]] = []
        example = None
        first_status = success[0]
        for code in success:
            response = doc.resolve(responses[code])
            content = response.get("content") or {}
            media = content.get("application/json")
            if media is None:
                others = [ct for ct in content if ct != "application/json"]
                if others and not json_schemas:
                    return Response("binary", code, content_type=others[0])
                continue
            schema = media.get("schema") or {}
            if not any(json.dumps(schema, sort_keys=True) == json.dumps(s, sort_keys=True) for s in json_schemas):
                json_schemas.append(schema)
            if example is None:
                example = _first_example(media)
        if not json_schemas:
            if first_status == "204":
                return Response("none", first_status)
            return Response("json", first_status, TypeRef("any", nullable=True), "application/json")

        hint = base_name + "Response"
        schema = json_schemas[0] if len(json_schemas) == 1 else {"oneOf": json_schemas}
        resolved = doc.resolve(schema)
        props = resolved.get("properties") or {}
        query_names = {p.name for p in query_params}
        if {"count", "next", "previous", "results"} <= set(props) and {"limit", "offset"} <= query_names:
            results = doc.resolve(props["results"])
            item_hint = (names.component_name(schema["$ref"].split("/")[-1]) if "$ref" in schema else hint) + "Results"
            item = self.resolve(doc, results.get("items"), item_hint, RESPONSE)
            return Response("page", first_status, item, "application/json", example)
        return Response("json", first_status, self.resolve(doc, schema, hint, RESPONSE), "application/json", example)


def _path_placeholders(path: str) -> list[str]:
    import re

    return re.findall(r"\{([^}]+)\}", path)


def _ref_key(ref: TypeRef) -> Any:
    return [ref.kind, ref.name, ref.nullable, _ref_key(ref.item) if ref.item else None]


def build(spec: Spec) -> Model:
    model = ModelBuilder(spec).build()
    if APPLICATION_PREFIX is None:  # pragma: no cover
        raise SpecError("unreachable")
    return model
