"""Tests for codegen/spec_sync.py. No network: every test reads the upstream files with --offline from a temporary directory."""

from __future__ import annotations

import copy
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from unittest import mock

CODEGEN = Path(__file__).resolve().parent.parent
REPO = CODEGEN.parent
sys.path.insert(0, str(CODEGEN))

import spec_sync  # noqa: E402

NOW = datetime(2026, 10, 17, 6, 17, 30, tzinfo=timezone.utc)
VERIFICATION_URL = "https://docs.didit.me/openapi-25.json"
ACCOUNT_URL = "https://docs.didit.me/openapi-auth.json"


def ref(name: str) -> dict[str, str]:
    return {"$ref": f"#/components/schemas/{name}"}


def json_body(schema: dict[str, Any], required: bool = True) -> dict[str, Any]:
    return {"required": required, "content": {"application/json": {"schema": schema}}}


def ok(schema: dict[str, Any]) -> dict[str, Any]:
    return {"200": {"description": "OK", "content": {"application/json": {"schema": schema}}}}


def base_verification() -> dict[str, Any]:
    """A small synthetic verification document."""
    return {
        "openapi": "3.0.0",
        "info": {"title": "Synthetic verification API", "version": "1.0.0"},
        "paths": {
            "/v3/widgets/": {
                "get": {
                    "operationId": "listWidgets",
                    "parameters": [{"name": "limit", "in": "query", "required": False, "schema": {"type": "integer"}}],
                    "responses": ok(ref("WidgetList")),
                },
                "post": {
                    "operationId": "createWidget",
                    "requestBody": json_body(
                        {
                            "type": "object",
                            "required": ["name"],
                            "properties": {"name": {"type": "string"}, "color": {"type": "string"}},
                        }
                    ),
                    "responses": ok(ref("Widget")),
                },
            },
            "/v3/widgets/{widget_id}/": {
                "parameters": [{"name": "widget_id", "in": "path", "required": True, "schema": {"type": "string"}}],
                "get": {"operationId": "getWidget", "responses": ok(ref("Widget"))},
            },
            "/v3/gadgets/{gadget_id}/": {
                "delete": {
                    "operationId": "deleteGadget",
                    "parameters": [{"name": "gadget_id", "in": "path", "required": True, "schema": {"type": "string"}}],
                    "responses": {
                        **ok(ref("Gadget")),
                        "404": {"$ref": "#/components/responses/NotFound"},
                    },
                }
            },
        },
        "components": {
            "schemas": {
                "Widget": {
                    "type": "object",
                    "properties": {"id": {"type": "string"}, "name": {"type": "string"}, "color": {"type": "string"}},
                },
                "WidgetList": {
                    "type": "object",
                    "properties": {"count": {"type": "integer"}, "results": {"type": "array", "items": ref("Widget")}},
                },
                "Gadget": {"type": "object", "properties": {"id": {"type": "string"}, "part": ref("GadgetPart")}},
                "GadgetPart": {"type": "object", "properties": {"sku": {"type": "string"}}},
                "Error": {"type": "object", "properties": {"detail": {"type": "string"}}},
            },
            "responses": {
                "NotFound": {"description": "Not found", "content": {"application/json": {"schema": ref("Error")}}},
            },
        },
    }


def base_account() -> dict[str, Any]:
    return {
        "openapi": "3.0.0",
        "info": {"title": "Synthetic account API", "version": "1.0.0"},
        "paths": {
            "/login/": {
                "post": {
                    "operationId": "login",
                    "requestBody": json_body({"type": "object", "properties": {"email": {"type": "string"}}}),
                    "responses": ok({"type": "object", "properties": {"access_token": {"type": "string"}}}),
                }
            }
        },
        "components": {"schemas": {}},
    }


def base_overlay() -> dict[str, Any]:
    return {
        "description": "Synthetic overlay.",
        "version": 1,
        "operations": {
            "DELETE /v3/gadgets/{gadget_id}/": {"auth": "api_key", "method": "delete", "resource": "gadgets", "server": True},
            "GET /v3/widgets/": {"auth": "api_key", "method": "list", "resource": "widgets", "server": True},
            "GET /v3/widgets/{widget_id}/": {"auth": "api_key", "method": "retrieve", "resource": "widgets", "server": True},
            "POST /v3/widgets/": {"auth": "api_key", "method": "create", "resource": "widgets", "server": True},
            "account POST /login/": {"resource": "account", "method": "login", "auth": "none", "server": True},
        },
    }


def encode(document: dict[str, Any]) -> bytes:
    return (json.dumps(document, indent=2) + "\n").encode("utf-8")


class SpecSyncTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="spec-sync-test-"))
        self.addCleanup(shutil.rmtree, self.tmp, ignore_errors=True)
        self.root = self.tmp / "repo"
        self.openapi = self.root / "openapi"
        self.offline = self.tmp / "upstream"
        self.openapi.mkdir(parents=True)
        self.offline.mkdir()
        self.github_output = self.tmp / "github_output"
        self.github_output.write_text("", "utf-8")
        environment = {key: value for key, value in os.environ.items() if key not in ("GITHUB_OUTPUT", "GITHUB_STEP_SUMMARY")}
        environment["GITHUB_OUTPUT"] = str(self.github_output)
        patcher = mock.patch.dict(os.environ, environment, clear=True)
        patcher.start()
        self.addCleanup(patcher.stop)

        verification, account = encode(base_verification()), encode(base_account())
        (self.openapi / "verification.json").write_bytes(verification)
        (self.openapi / "account.json").write_bytes(account)
        self.source = {
            "verification.json": {"url": VERIFICATION_URL, "sha256": hashlib.sha256(verification).hexdigest(), "fetched_at": "2026-10-16T06:17:00Z"},
            "account.json": {"url": ACCOUNT_URL, "sha256": hashlib.sha256(account).hexdigest(), "fetched_at": "2026-10-16T06:17:00Z"},
            "webhook-vectors.json": {"generator": "synthetic", "source": "synthetic"},
        }
        (self.openapi / "SOURCE.json").write_text(json.dumps(self.source, indent=1), "utf-8")
        (self.openapi / "overlay.json").write_text(json.dumps(base_overlay(), indent=1), "utf-8")
        self.publish(base_verification(), base_account())

    # helpers

    def publish(self, verification: dict[str, Any], account: dict[str, Any] | None = None) -> bytes:
        raw = encode(verification)
        (self.offline / "verification.json").write_bytes(raw)
        (self.offline / "account.json").write_bytes(encode(account or base_account()))
        return raw

    def sync(self, write: bool = True) -> tuple[spec_sync.Report, str]:
        summary = self.tmp / "summary.md"
        with redirect_stdout(io.StringIO()):
            report = spec_sync.run(self.root, offline=self.offline, write=write, summary_path=summary, now=NOW)
        return report, summary.read_text("utf-8")

    def snapshot(self) -> dict[str, bytes]:
        return {path.name: path.read_bytes() for path in sorted(self.openapi.iterdir())}

    def overlay_ops(self) -> dict[str, Any]:
        return json.loads((self.openapi / "overlay.json").read_text("utf-8"))["operations"]

    def outputs(self) -> dict[str, str]:
        lines = self.github_output.read_text("utf-8").splitlines()
        return dict(line.split("=", 1) for line in lines if "=" in line)

    @staticmethod
    def section(summary: str, title: str) -> str:
        start = summary.index(f"### {title}\n")
        end = summary.find("\n### ", start + 1)
        return summary[start : end if end != -1 else len(summary)]

    # tests

    def test_no_change_writes_nothing(self) -> None:
        before = self.snapshot()
        report, summary = self.sync()
        self.assertFalse(report.changed)
        self.assertEqual(before, self.snapshot())
        self.assertIn("No upstream changes.", summary)
        self.assertEqual(self.outputs(), {"changed": "false", "needs_naming": "false", "date": "2026-10-17"})

    def test_added_operations(self) -> None:
        verification = base_verification()
        verification["paths"]["/v3/gizmos/"] = {"post": {"operationId": "createGizmo", "summary": "Create a gizmo", "responses": ok({"type": "object"})}}
        verification["paths"]["/v3/sprockets/"] = {
            "get": {
                "operationId": "listSprockets",
                "x-didit-sdk": {"resource": "sprockets", "method": "list", "auth": "api_key", "server": True},
                "responses": ok(ref("Sprocket")),
            }
        }
        verification["components"]["schemas"]["Sprocket"] = {"type": "object", "properties": {"id": {"type": "string"}}}
        overlay_before = (self.openapi / "overlay.json").read_bytes()
        account_before = (self.openapi / "account.json").read_bytes()
        published = self.publish(verification)

        report, summary = self.sync()

        self.assertTrue(report.changed)
        self.assertEqual(published, (self.openapi / "verification.json").read_bytes(), "upstream bytes are copied as downloaded")
        self.assertEqual(account_before, (self.openapi / "account.json").read_bytes())
        self.assertEqual(overlay_before, (self.openapi / "overlay.json").read_bytes(), "an unchanged overlay is not rewritten")
        source = json.loads((self.openapi / "SOURCE.json").read_text("utf-8"))
        self.assertEqual(source["verification.json"]["sha256"], hashlib.sha256(published).hexdigest())
        self.assertEqual(source["verification.json"]["fetched_at"], "2026-10-17T06:17:30Z")
        self.assertEqual(source["verification.json"]["url"], VERIFICATION_URL)
        self.assertEqual(source["account.json"], self.source["account.json"])
        self.assertEqual(source["webhook-vectors.json"], self.source["webhook-vectors.json"])

        added = self.section(summary, "Operations")
        self.assertIn("`POST /v3/gizmos/`: no SDK naming", added)
        self.assertIn("`GET /v3/sprockets/`: sprockets.list", added)
        self.assertIn("`createGizmo`", self.section(summary, "Needs SDK naming"))
        self.assertIn("`Sprocket` (verification)", self.section(summary, "Schemas"))
        self.assertIn("None.", self.section(summary, "Breaking"))
        self.assertEqual(self.outputs(), {"changed": "true", "needs_naming": "true", "date": "2026-10-17"})

    def test_removed_operation_is_deprecated(self) -> None:
        verification = base_verification()
        del verification["paths"]["/v3/gadgets/{gadget_id}/"]
        self.publish(verification)

        report, summary = self.sync()

        self.assertEqual(report.removed, [("DELETE /v3/gadgets/{gadget_id}/", "gadgets.delete")])
        deprecated = json.loads((self.openapi / "deprecated-operations.json").read_text("utf-8"))
        self.assertEqual(set(deprecated), {"description", "operations", "schemas"})
        record = deprecated["operations"]["DELETE /v3/gadgets/{gadget_id}/"]
        self.assertEqual(record["removed_on"], "2026-10-17")
        self.assertEqual(record["naming"], base_overlay()["operations"]["DELETE /v3/gadgets/{gadget_id}/"])
        self.assertEqual(record["path"], "/v3/gadgets/{gadget_id}/")
        self.assertEqual(record["method"], "delete")
        self.assertEqual(record["operation"]["operationId"], "deleteGadget")
        self.assertNotIn("$ref", record["operation"]["responses"]["404"], "non-schema $refs are inlined")
        self.assertEqual(record["operation"]["responses"]["404"]["description"], "Not found")
        self.assertEqual(sorted(deprecated["schemas"]["verification"]), ["Error", "Gadget", "GadgetPart"], "schemas are copied transitively")
        self.assertEqual(deprecated["schemas"]["account"], {})
        self.assertNotIn("DELETE /v3/gadgets/{gadget_id}/", self.overlay_ops(), "the naming moves to deprecated-operations.json")

        breaking = self.section(summary, "Breaking")
        self.assertIn("Removed operation `DELETE /v3/gadgets/{gadget_id}/` (gadgets.delete)", breaking)
        self.assertIn("`[Obsolete]`", breaking)
        self.assertIn("until the next minor release", self.section(summary, "Deprecated SDK methods"))

    def test_deprecated_operation_restored_when_it_returns(self) -> None:
        verification = base_verification()
        del verification["paths"]["/v3/gadgets/{gadget_id}/"]
        self.publish(verification)
        self.sync()
        self.assertTrue((self.openapi / "deprecated-operations.json").exists())

        self.publish(base_verification())
        _, summary = self.sync()

        self.assertFalse((self.openapi / "deprecated-operations.json").exists())
        self.assertEqual(self.overlay_ops()["DELETE /v3/gadgets/{gadget_id}/"], base_overlay()["operations"]["DELETE /v3/gadgets/{gadget_id}/"])
        self.assertIn("back upstream", self.section(summary, "Overlay"))

    def test_overlay_entry_dropped_when_upstream_carries_identical_naming(self) -> None:
        verification = base_verification()
        verification["paths"]["/v3/widgets/"]["get"]["x-didit-sdk"] = dict(base_overlay()["operations"]["GET /v3/widgets/"])
        self.publish(verification)

        _, summary = self.sync()

        expected = base_overlay()
        del expected["operations"]["GET /v3/widgets/"]
        self.assertEqual((self.openapi / "overlay.json").read_text("utf-8"), json.dumps(expected, indent=1), "1-space indent, sorted keys, entries keep their key order")
        self.assertIn("Deleted `GET /v3/widgets/`: upstream now carries the same `x-didit-sdk`.", self.section(summary, "Overlay"))
        self.assertIn("None.", self.section(summary, "Breaking"))

    def test_upstream_naming_wins_over_a_different_overlay_entry(self) -> None:
        verification = base_verification()
        verification["paths"]["/v3/widgets/"]["get"]["x-didit-sdk"] = {"resource": "widgets", "method": "list_all", "auth": "api_key", "server": True}
        self.publish(verification)

        report, summary = self.sync()

        self.assertNotIn("GET /v3/widgets/", self.overlay_ops())
        self.assertIn(("GET /v3/widgets/", "widgets.list_all"), report.changed_ops)
        self.assertIn("Naming change `GET /v3/widgets/`: overlay `widgets.list`, upstream `widgets.list_all`", self.section(summary, "Overlay"))
        self.assertIn("SDK naming of `GET /v3/widgets/` changes from `widgets.list` to `widgets.list_all`", self.section(summary, "Breaking"))

    def test_breaking_section(self) -> None:
        verification = base_verification()
        schemas = verification["components"]["schemas"]
        del schemas["Widget"]["properties"]["color"]
        del schemas["GadgetPart"]
        schemas["Gadget"]["properties"]["part"] = {"type": "string"}
        verification["paths"]["/v3/widgets/"]["get"]["parameters"][0]["required"] = True
        create = verification["paths"]["/v3/widgets/"]["post"]
        create["requestBody"]["content"]["application/json"]["schema"]["required"] = ["name", "color"]
        verification["paths"]["/v3/widgets/{widget_id}/"]["get"]["parameters"] = [{"name": "expand", "in": "query", "required": True, "schema": {"type": "string"}}]
        self.publish(verification)

        report, summary = self.sync()

        breaking = self.section(summary, "Breaking")
        self.assertIn("Removed property `color` from schema `Widget` (verification)", breaking)
        self.assertIn("Removed schema `GadgetPart` (verification)", breaking)
        self.assertIn("Newly required query parameter `limit` on `GET /v3/widgets/` (widgets.list)", breaking)
        self.assertIn("Newly required query parameter `expand` on `GET /v3/widgets/{widget_id}/` (widgets.retrieve)", breaking)
        self.assertIn("Newly required request property `color` on `POST /v3/widgets/` (widgets.create)", breaking)
        self.assertNotIn("widget_id", breaking, "an unchanged path-level parameter is not newly required")
        self.assertIn("`Widget` (verification)", self.section(summary, "Schemas"))
        self.assertEqual(sorted(key for key, _ in report.changed_ops), ["GET /v3/widgets/", "GET /v3/widgets/{widget_id}/", "POST /v3/widgets/"])

    def test_without_write_nothing_changes(self) -> None:
        verification = base_verification()
        del verification["paths"]["/v3/gadgets/{gadget_id}/"]
        self.publish(verification)
        before = self.snapshot()

        report, summary = self.sync(write=False)

        self.assertTrue(report.changed)
        self.assertEqual(before, self.snapshot())
        self.assertIn("Dry run", summary)
        self.assertEqual(self.outputs()["changed"], "true")

    def test_invalid_upstream_fails_without_writing(self) -> None:
        (self.offline / "verification.json").write_text(json.dumps({"openapi": "3.0.0"}), "utf-8")
        before = self.snapshot()
        with redirect_stdout(io.StringIO()), mock.patch("sys.stderr", new=io.StringIO()) as stderr:
            code = spec_sync.main(["--root", str(self.root), "--offline", str(self.offline), "--write"])
        self.assertEqual(code, 2)
        self.assertIn("not an OpenAPI document", stderr.getvalue())
        self.assertEqual(before, self.snapshot())

    def test_command_line(self) -> None:
        verification = base_verification()
        verification["paths"]["/v3/gizmos/"] = {"get": {"operationId": "listGizmos", "responses": ok({"type": "object"})}}
        self.publish(verification)
        summary = self.tmp / "cli-summary.md"
        result = subprocess.run(
            [sys.executable, str(CODEGEN / "spec_sync.py"), "--root", str(self.root), "--offline", str(self.offline), "--write", "--summary", str(summary)],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("1 added", result.stdout)
        self.assertIn("`GET /v3/gizmos/`: no SDK naming", summary.read_text("utf-8"))
        self.assertEqual(self.outputs()["changed"], "true")
        self.assertEqual(self.outputs()["needs_naming"], "true")


class RepositorySpecTest(unittest.TestCase):
    """The committed openapi/ files match the hashes in SOURCE.json, so a sync of identical upstream files changes nothing."""

    def test_committed_spec_is_consistent(self) -> None:
        tmp = Path(tempfile.mkdtemp(prefix="spec-sync-repo-"))
        self.addCleanup(shutil.rmtree, tmp, ignore_errors=True)
        shutil.copytree(REPO / "openapi", tmp / "openapi")
        upstream = tmp / "upstream"
        upstream.mkdir()
        for name in ("verification.json", "account.json"):
            shutil.copyfile(REPO / "openapi" / name, upstream / name)
        with mock.patch.dict(os.environ, {}, clear=False):
            os.environ.pop("GITHUB_OUTPUT", None)
            os.environ.pop("GITHUB_STEP_SUMMARY", None)
            report = spec_sync.run(tmp, offline=upstream, write=True, now=NOW)
        self.assertFalse(report.changed, "openapi/SOURCE.json does not match the committed upstream files")


if __name__ == "__main__":
    unittest.main()
