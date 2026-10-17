"""Writes one generated test per server operation (DESIGN.md section 11) into tests/Didit.Tests/Generated.

Each test calls the SDK method against a mocked HTTP layer and asserts the method, the path (ids filled, application
scope expanded), the query string, the body encoding, the authentication header and the parsing of the spec's example
response.
"""

from __future__ import annotations

import json
import re
from typing import Any
from urllib.parse import quote

from . import names
from .csharp import HEADER, TypeInfo
from .model import REQUEST, RESPONSE, Model, ObjectDef, Operation, Prop, TypeRef

ORG = "org-test"
APP = "app-test"
API_KEY = "test-api-key"
ACCOUNT_TOKEN = "test_account_token"
MAX_DEPTH = 4
# Spec examples carry JWTs; the generated tests use this placeholder so the secret scan (.gitleaks.toml) passes on them.
TOKEN_PLACEHOLDER = "sample-token"


def lit(value: str) -> str:
    """A C# string literal (json.dumps output is valid C#)."""
    return json.dumps(value)


def escape(value: str) -> str:
    return quote(value, safe="-_.~")


def is_token(value: Any) -> bool:
    """A JWT, or the truncated start of one, in a spec example."""
    return isinstance(value, str) and value.startswith("eyJ")


def scrub_tokens(value: Any) -> Any:
    if is_token(value):
        return TOKEN_PLACEHOLDER
    if isinstance(value, dict):
        return {k: scrub_tokens(v) for k, v in value.items()}
    if isinstance(value, list):
        return [scrub_tokens(v) for v in value]
    return value


class ValueFactory:
    def __init__(self, model: Model) -> None:
        self.model = model
        self.types = TypeInfo(model)

    def string_sample(self, prop_name: str, schema: dict[str, Any] | None) -> str:
        schema = schema or {}
        fmt = schema.get("format")
        if fmt == "uuid":
            return "11111111-2222-3333-4444-555555555555"
        if fmt == "email":
            return "user@example.com"
        if fmt == "uri":
            return "https://example.com/callback"
        if fmt == "date":
            return "2026-01-02"
        example = schema.get("example")
        if isinstance(example, str) and 0 < len(example) <= 80 and "\n" not in example:
            return TOKEN_PLACEHOLDER if is_token(example) else example
        return f"sample-{prop_name}"

    def value(self, ref: TypeRef, prop_name: str, schema: dict[str, Any] | None, depth: int) -> tuple[str, Any] | None:
        """(C# expression, JSON value) for a request value, or None when it cannot be built."""
        kind = ref.kind
        schema = schema or {}
        if kind == "string":
            text = self.string_sample(prop_name, schema)
            return lit(text), text
        if kind == "long":
            example = schema.get("example")
            number = example if isinstance(example, int) and not isinstance(example, bool) and abs(example) < 2**31 else 1
            return f"{number}L", number
        if kind == "double":
            return "1.5", 1.5
        if kind == "bool":
            return "true", True
        if kind == "datetime":
            return "new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)", "2026-01-02T03:04:05+00:00"
        if kind == "file":
            return 'FileUpload.FromBytes(new byte[] { 1, 2, 3 }, "sample.jpg", "image/jpeg")', None
        if kind == "any":
            return lit("sample"), "sample"
        if kind == "named" and ref.name in self.model.enums:
            value = self.model.enums[ref.name].values[0]
            return f"new {ref.name}({lit(value)})", value
        if kind == "named" and ref.name in self.model.objects:
            if depth >= MAX_DEPTH:
                return None
            return self.object_value(self.model.objects[ref.name], depth + 1, include_optional=depth == 0)
        if kind == "list" and ref.item is not None:
            item = self.value(ref.item, prop_name, (schema.get("items") if isinstance(schema.get("items"), dict) else None), depth)
            if item is None:
                return None
            element = self.types.element(ref.item, REQUEST)
            return f"new {element}[] {{ {item[0]} }}", [item[1]]
        if kind == "map" and ref.item is not None:
            item = self.value(ref.item, prop_name, None, depth)
            if item is None:
                return None
            element = self.types.element(ref.item, REQUEST)
            return f"new Dictionary<string, {element}> {{ [\"key\"] = {item[0]} }}", {"key": item[1]}
        return None

    def object_value(self, obj: ObjectDef, depth: int, include_optional: bool, body_only: bool = False) -> tuple[str, Any] | None:
        assignments: list[str] = []
        body: dict[str, Any] = {}
        for prop in obj.props:
            if body_only and prop.location != "body":
                continue
            if not prop.required and not self.include_optional(prop, include_optional):
                continue
            built = self.value(prop.type, prop.json_name, prop.schema, depth)
            if built is None:
                if prop.required:
                    return None
                continue
            assignments.append(f"{prop.cs_name} = {built[0]}")
            if prop.location == "body":
                body[prop.json_name] = built[1]
        initializer = ", ".join(assignments)
        return (f"new {obj.name} {{ {initializer} }}" if initializer else f"new {obj.name}()"), body

    def include_optional(self, prop: Prop, include_optional: bool) -> bool:
        if prop.location == "query":
            return self.scalar(prop.type)
        if not include_optional or prop.deprecated:
            return False
        return self.scalar(prop.type)

    def scalar(self, ref: TypeRef) -> bool:
        if ref.kind in ("string", "long", "double", "bool", "datetime", "file"):
            return True
        if ref.kind == "named" and ref.name in self.model.enums:
            return True
        if ref.kind == "list" and ref.item is not None:
            return ref.item.kind in ("string", "long") or (ref.item.kind == "named" and ref.item.name in self.model.enums)
        return False


def query_pairs(obj: ObjectDef | None, args: dict[str, Any], op: Operation) -> list[tuple[str, str]]:
    pairs: list[tuple[str, str]] = []

    def wire(value: Any) -> list[str]:
        if isinstance(value, list):
            return [w for v in value for w in wire(v)]
        if isinstance(value, bool):
            return ["true" if value else "false"]
        if isinstance(value, float):
            return [repr(value)]
        return [str(value)]

    if obj is not None:
        for prop in obj.props:
            if prop.location == "query" and prop.cs_name in args:
                for item in wire(args[prop.cs_name]):
                    pairs.append((prop.json_name, item))
    return pairs


def count_assert(count: int, expr: str) -> str:
    if count == 0:
        return f"Assert.Empty({expr});"
    if count == 1:
        return f"Assert.Single({expr});"
    return f"Assert.Equal({count}, {expr}.Count);"


def sample_response(model: Model, ref: TypeRef | None, depth: int = 0) -> Any:
    """A response body synthesised from the schema, when the spec has no example."""
    if ref is None:
        return {}
    kind = ref.kind
    if kind == "string":
        return "sample"
    if kind == "long":
        return 1
    if kind == "double":
        return 1.5
    if kind == "bool":
        return True
    if kind == "datetime":
        return "2026-01-02T03:04:05Z"
    if kind == "any":
        return {"key": "value"}
    if kind == "named" and ref.name in model.enums:
        return model.enums[ref.name].values[0]
    if kind == "named" and ref.name in model.objects:
        if depth > 3:
            return {}
        return {p.json_name: sample_response(model, p.type, depth + 1) for p in model.objects[ref.name].props if p.location == "body"}
    if kind == "list" and ref.item is not None:
        return [sample_response(model, ref.item, depth + 1)] if depth <= 3 else []
    if kind == "map" and ref.item is not None:
        return {"key": sample_response(model, ref.item, depth + 1)}
    return None


class TestEmitter:
    def __init__(self, model: Model) -> None:
        self.model = model
        self.values = ValueFactory(model)
        self.types = TypeInfo(model)

    def emit(self) -> dict[str, str]:
        by_root: dict[str, list[Operation]] = {}
        for op in self.model.operations:
            by_root.setdefault(op.resource[0], []).append(op)
        files: dict[str, str] = {}
        for root, ops in sorted(by_root.items()):
            class_name = names.pascal(root) + "OperationTests"
            body = [HEADER + "#pragma warning disable CS0618 // Deprecated operations and fields are still tested.\n", "using System;", "using System.Collections.Generic;", "using System.Linq;", "using System.Text.Json;", "using System.Threading.Tasks;", "using Didit.Tests.Support;", "using Xunit;", "", "namespace Didit.Tests.Generated", "{"]
            body.append(f"    public sealed class {class_name}")
            body.append("    {")
            first = True
            for op in sorted(ops, key=lambda o: o.dotted):
                if not first:
                    body.append("")
                first = False
                body += self.test(op)
            body.append("    }")
            body.append("}")
            files[f"tests/Didit.Tests/Generated/{class_name}.g.cs"] = "\n".join(body) + "\n"
        return files

    def test(self, op: Operation) -> list[str]:
        i = "        "
        name = "_".join(names.pascal(s) for s in op.resource) + "_" + names.pascal(op.method)
        lines = [f"{i}[Fact]", f"{i}public async Task {name}()", f"{i}{{"]
        lines.append(f"{i}    var handler = new MockHttpHandler();")
        response_lines, assertions = self.response(op)
        lines += [f"{i}    {line}" for line in response_lines]
        lines.append(f"{i}    using var client = TestClients.Create(handler);")

        call_args: list[str] = []
        path = op.path
        if op.scope == "application":
            path = path.replace("{organization_id}", ORG).replace("{application_id}", APP)
        for param in op.path_params:
            if param.type.kind == "long":
                call_args.append("7L")
                path = path.replace("{" + param.name + "}", "7")
            else:
                value = f"{param.name}-1"
                call_args.append(lit(value))
                path = path.replace("{" + param.name + "}", escape(value))

        expected_body: Any = None
        multipart_parts: list[str] = []
        params_obj = self.model.objects.get(op.params_type) if op.params_type else None
        query_args: dict[str, Any] = {}
        if params_obj is not None:
            assignments = []
            body_json: dict[str, Any] = {}
            for prop in params_obj.props:
                if not prop.required and not self.values.include_optional(prop, True):
                    continue
                built = self.values.value(prop.type, prop.json_name, prop.schema, 1)
                if built is None:
                    if prop.required:
                        return self.skip(op, name, "a required value could not be synthesised")
                    continue
                assignments.append(f"{prop.cs_name} = {built[0]}")
                if prop.location == "query":
                    query_args[prop.cs_name] = built[1]
                else:
                    if prop.type.kind == "file" or (prop.type.kind == "list" and prop.type.item and prop.type.item.kind == "file"):
                        multipart_parts.append(prop.json_name)
                    else:
                        body_json[prop.json_name] = built[1]
                        multipart_parts.append(prop.json_name)
            call_args.append(f"new {params_obj.name} {{ {', '.join(assignments)} }}" if assignments else f"new {params_obj.name}()")
            if op.body is not None:
                expected_body = body_json
        elif op.body is not None:
            built = self.values.value(op.body.type, "body", op.body.schema, 0)
            if built is None:
                return self.skip(op, name, "the body could not be synthesised")
            call_args.append(built[0])
            expected_body = built[1]
        for arg in op.query_args:
            if arg.required:
                return self.skip(op, name, "required query argument")

        method = names.method_name(op.method)
        accessor = ".".join(names.pascal(s) for s in op.resource)
        call = f"await client.{accessor}.{method}({', '.join(call_args)})"
        if op.response.kind == "none":
            lines.append(f"{i}    {call};")
        else:
            lines.append(f"{i}    var result = {call};")
        lines.append("")
        lines.append(f"{i}    var request = Assert.Single(handler.Requests);")
        lines.append(f"{i}    Assert.Equal({lit(op.http_method)}, request.Method);")
        host = "apx.didit.me" if op.api == "account" else "verification.didit.me"
        prefix = "/auth/v2" if op.api == "account" else ""
        lines.append(f"{i}    Assert.Equal({lit(host)}, request.Uri.Host);")
        lines.append(f"{i}    Assert.Equal({lit(prefix + path)}, request.Path);")
        pairs = query_pairs(params_obj, query_args, op)
        query = ("?" + "&".join(f"{escape(k)}={escape(v)}" for k, v in pairs)) if pairs else ""
        lines.append(f"{i}    Assert.Equal({lit(query)}, request.Query);")
        if op.auth == "api_key":
            lines.append(f"{i}    Assert.Equal(TestClients.ApiKey, request.Header(\"x-api-key\"));")
            lines.append(f"{i}    Assert.Null(request.Header(\"Authorization\"));")
        elif op.auth == "account_token":
            lines.append(f"{i}    Assert.Equal(\"Bearer \" + TestClients.AccountToken, request.Header(\"Authorization\"));")
            lines.append(f"{i}    Assert.Null(request.Header(\"x-api-key\"));")
        else:
            lines.append(f"{i}    Assert.Null(request.Header(\"x-api-key\"));")
            lines.append(f"{i}    Assert.Null(request.Header(\"Authorization\"));")
        if op.body is None:
            lines.append(f"{i}    Assert.Empty(request.Body);")
        elif op.body.content_type == "application/json":
            lines.append(f"{i}    Assert.StartsWith(\"application/json\", request.ContentType);")
            lines.append(f"{i}    JsonAssert.Equivalent({lit(json.dumps(expected_body, ensure_ascii=False))}, request.BodyText);")
        else:
            lines.append(f"{i}    Assert.StartsWith(\"multipart/form-data\", request.ContentType);")
            for part in multipart_parts:
                lines.append(f"{i}    Assert.Contains({lit('name=' + chr(34) + part + chr(34))}, request.BodyText);")
        lines += [f"{i}    {line}" for line in assertions]
        lines.append(f"{i}}}")
        return lines

    def skip(self, op: Operation, name: str, reason: str) -> list[str]:
        i = "        "
        return [f"{i}[Fact(Skip = {lit('No generated sample: ' + reason)})]", f"{i}public void {name}()", f"{i}{{", f"{i}}}"]

    def response(self, op: Operation) -> tuple[list[str], list[str]]:
        response = op.response
        status = int(response.status)
        if response.kind == "none":
            return [f"handler.Enqueue({status});"], []
        if response.kind == "binary":
            content_type = response.content_type or "application/octet-stream"
            return (
                [f"handler.Enqueue({status}, new byte[] {{ 37, 80, 68, 70 }}, {lit(content_type)}, new Dictionary<string, string> {{ [\"Content-Disposition\"] = \"attachment; filename=\\\"report.bin\\\"\" }});"],
                ["Assert.Equal(new byte[] { 37, 80, 68, 70 }, result.Content);", f"Assert.Equal({lit(content_type)}, result.ContentType);", "Assert.Equal(\"report.bin\", result.FileName);"],
            )
        example = scrub_tokens(response.example)
        source = "example"
        if example is None:
            source = "schema"
            if response.kind == "page":
                example = {"count": 1, "next": None, "previous": None, "results": [sample_response(self.model, response.type)]}
            else:
                example = sample_response(self.model, response.type)
        text = json.dumps(example, ensure_ascii=False)
        setup = [f"// Response body from the spec's {source}.", f"handler.Enqueue({status}, {lit(text)});"]
        asserts: list[str] = []
        if response.kind == "page":
            results = example.get("results") if isinstance(example, dict) else None
            count = len(results) if isinstance(results, list) else 0
            asserts.append(count_assert(count, "result.Results"))
            if isinstance(example, dict) and isinstance(example.get("count"), int):
                asserts.append(f"Assert.Equal({example['count']}L, result.Count);")
            return setup, asserts
        ref = response.type
        if ref is None:
            return setup, asserts
        if ref.kind == "any":
            kind = {dict: "Object", list: "Array", str: "String"}.get(type(example), None)
            if kind:
                asserts.append(f"Assert.Equal(JsonValueKind.{kind}, result.ValueKind);")
            return setup, asserts
        if ref.kind == "list" and isinstance(example, list):
            asserts.append(count_assert(len(example), "result"))
            return setup, asserts
        if ref.kind == "named" and ref.name in self.model.objects and isinstance(example, dict):
            asserts.append("Assert.NotNull(result);")
            obj = self.model.objects[ref.name]
            for prop in obj.props:
                value = example.get(prop.json_name)
                if prop.type.kind == "string" and isinstance(value, str) and len(value) < 200:
                    asserts.append(f"Assert.Equal({lit(value)}, result.{prop.cs_name});")
                    break
            for prop in obj.props:
                value = example.get(prop.json_name)
                if prop.type.kind == "long" and isinstance(value, int) and not isinstance(value, bool):
                    asserts.append(f"Assert.Equal({value}L, result.{prop.cs_name});")
                    break
            known = {p.json_name for p in obj.props}
            extra = [k for k in example if k not in known]
            if extra:
                asserts.append(f"Assert.True(result.AdditionalProperties?.ContainsKey({lit(extra[0])}), \"unknown fields are preserved\");")
        return setup, asserts


def emit(model: Model) -> dict[str, str]:
    return TestEmitter(model).emit()
