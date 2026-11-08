"""Unit tests of the generator (no network, standard library only).

    python3 -m unittest discover -s codegen/tests
"""

from __future__ import annotations

import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "codegen"))

from didit_codegen import csharp, model, names, spec  # noqa: E402


OVERLAID_OPERATIONS = ("GET /v3/billing/balance/", "POST /v3/session/")


def copy_openapi(target: Path) -> Path:
    (target / "openapi").mkdir(parents=True)
    for name in ("verification.json", "account.json", "overlay.json"):
        shutil.copy(ROOT / "openapi" / name, target / "openapi" / name)
    # The tests below start from operations named through the overlay. Once upstream carries x-didit-sdk the
    # sync deletes an overlay entry (docs/DESIGN.md section 3), so move the naming back here.
    spec_path = target / "openapi" / "verification.json"
    overlay_path = target / "openapi" / "overlay.json"
    document = json.loads(spec_path.read_text("utf-8"))
    overlay = json.loads(overlay_path.read_text("utf-8"))
    for key in OVERLAID_OPERATIONS:
        if key in overlay["operations"]:
            continue
        verb, path = key.split(" ", 1)
        overlay["operations"][key] = document["paths"][path][verb.lower()].pop("x-didit-sdk")
    spec_path.write_text(json.dumps(document), "utf-8")
    overlay_path.write_text(json.dumps(overlay), "utf-8")
    return target


class NamingTests(unittest.TestCase):
    def test_pascal_and_camel(self) -> None:
        self.assertEqual(names.pascal("id_verification"), "IdVerification")
        self.assertEqual(names.pascal("start_bankid_no"), "StartBankidNo")
        self.assertEqual(names.pascal("Not Started"), "NotStarted")
        self.assertEqual(names.pascal("PHONE_VERIFICATION_APPROVED"), "PhoneVerificationApproved")
        self.assertEqual(names.pascal("status.updated"), "StatusUpdated")
        self.assertEqual(names.pascal("accountId"), "AccountId")
        self.assertEqual(names.pascal("1d"), "Value1d")
        self.assertEqual(names.camel("session_id"), "sessionId")
        self.assertEqual(names.camel("params"), "@params")
        self.assertEqual(names.method_name("retrieve_decision"), "RetrieveDecisionAsync")
        self.assertEqual(names.enum_member_name(""), "Empty")
        self.assertEqual(names.component_name("IPRiskRequest"), "IPRiskRequest")


class SpecTests(unittest.TestCase):
    def test_current_spec_maps_every_operation(self) -> None:
        loaded = spec.load(ROOT)
        self.assertGreater(len(loaded.server_operations), 0)
        self.assertEqual(loaded.stale_overlay, [])

    def test_new_operation_without_naming_fails_with_its_name(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = copy_openapi(Path(tmp))
            path = root / "openapi" / "verification.json"
            data = json.loads(path.read_text("utf-8"))
            data["paths"]["/v3/brand-new/"] = {"get": {"operationId": "get_v3_brand_new", "responses": {"200": {"description": "ok"}}}}
            path.write_text(json.dumps(data), "utf-8")
            with self.assertRaises(spec.SpecError) as raised:
                spec.load(root)
            self.assertIn("GET /v3/brand-new/", str(raised.exception))
            self.assertIn("get_v3_brand_new", str(raised.exception))
            self.assertIn("overlay.json", str(raised.exception))

    def test_upstream_naming_wins_over_the_overlay(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = copy_openapi(Path(tmp))
            path = root / "openapi" / "verification.json"
            data = json.loads(path.read_text("utf-8"))
            data["paths"]["/v3/session/"]["post"]["x-didit-sdk"] = {"resource": "sessions", "method": "start", "auth": "api_key", "server": True}
            path.write_text(json.dumps(data), "utf-8")
            loaded = spec.load(root)
            op = next(o for o in loaded.operations if o.key == "POST /v3/session/")
            self.assertEqual(op.naming.method, "start")
            self.assertEqual(op.naming.source, "upstream")

    def test_invalid_naming_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = copy_openapi(Path(tmp))
            path = root / "openapi" / "overlay.json"
            data = json.loads(path.read_text("utf-8"))
            data["operations"]["POST /v3/session/"]["method"] = "Create-Session"
            path.write_text(json.dumps(data), "utf-8")
            with self.assertRaises(spec.SpecError):
                spec.load(root)

    def test_removed_operation_is_restored_as_obsolete(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = copy_openapi(Path(tmp))
            path = root / "openapi" / "verification.json"
            data = json.loads(path.read_text("utf-8"))
            removed = data["paths"]["/v3/billing/balance/"].pop("get")
            if not data["paths"]["/v3/billing/balance/"]:
                del data["paths"]["/v3/billing/balance/"]
            path.write_text(json.dumps(data), "utf-8")
            overlay_path = root / "openapi" / "overlay.json"
            overlay = json.loads(overlay_path.read_text("utf-8"))
            naming = overlay["operations"].pop("GET /v3/billing/balance/")
            overlay_path.write_text(json.dumps(overlay), "utf-8")
            (root / "openapi" / "deprecated-operations.json").write_text(json.dumps({
                "operations": {"GET /v3/billing/balance/": {"removed_on": "2026-10-17", "naming": naming, "path": "/v3/billing/balance/", "method": "get", "operation": removed}},
                "schemas": {"verification": {}, "account": {}},
            }), "utf-8")

            built = model.build(spec.load(root))
            op = next(o for o in built.operations if o.dotted == "billing.retrieve_balance")
            self.assertTrue(op.deprecated)
            files = csharp.emit(built)
            self.assertIn("Removed from the Didit API on 2026-10-17", files["src/Didit/Generated/Resources/BillingResource.g.cs"])

    def test_moved_operation_keeps_its_method_on_the_new_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = copy_openapi(Path(tmp))
            path = root / "openapi" / "verification.json"
            data = json.loads(path.read_text("utf-8"))
            moved = data["paths"].pop("/v3/billing/balance/")
            data["paths"]["/v3/billing/balance-v2/"] = moved
            path.write_text(json.dumps(data), "utf-8")
            overlay_path = root / "openapi" / "overlay.json"
            overlay = json.loads(overlay_path.read_text("utf-8"))
            naming = overlay["operations"].pop("GET /v3/billing/balance/")
            overlay["operations"]["GET /v3/billing/balance-v2/"] = naming
            overlay_path.write_text(json.dumps(overlay), "utf-8")
            (root / "openapi" / "deprecated-operations.json").write_text(json.dumps({
                "operations": {"GET /v3/billing/balance/": {"removed_on": "2026-10-17", "naming": naming, "path": "/v3/billing/balance/", "method": "get", "operation": moved["get"]}},
                "schemas": {"verification": {}, "account": {}},
            }), "utf-8")

            built = model.build(spec.load(root))
            ops = [o for o in built.operations if o.dotted == "billing.retrieve_balance"]
            self.assertEqual(len(ops), 1)
            self.assertEqual(ops[0].path, "/v3/billing/balance-v2/")
            self.assertFalse(ops[0].deprecated)

    def test_renamed_operation_keeps_an_obsolete_alias(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = copy_openapi(Path(tmp))
            overlay = json.loads((root / "openapi" / "overlay.json").read_text("utf-8"))
            current = overlay["operations"]["GET /v3/billing/balance/"]
            old = {**current, "method": "get_balance"}
            (root / "openapi" / "deprecated-operations.json").write_text(json.dumps({
                "operations": {"GET /v3/billing/balance/ (renamed from billing.get_balance)": {
                    "removed_on": "2026-10-17", "naming": old, "path": "/v3/billing/balance/", "method": "get", "renamed_to": current,
                }},
                "schemas": {"verification": {}, "account": {}},
            }), "utf-8")

            built = model.build(spec.load(root))
            live = next(o for o in built.operations if o.dotted == "billing.retrieve_balance")
            alias = next(o for o in built.operations if o.dotted == "billing.get_balance")
            self.assertFalse(live.deprecated)
            self.assertTrue(alias.deprecated)
            self.assertEqual((alias.http_method, alias.path), (live.http_method, live.path))
            resource = csharp.emit(built)["src/Didit/Generated/Resources/BillingResource.g.cs"]
            self.assertIn("Renamed to Billing.RetrieveBalanceAsync on 2026-10-17", resource)
            self.assertIn("GetBalanceAsync(", resource)


class ModelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.model = model.build(spec.load(ROOT))
        cls.by_name = {op.dotted: op for op in cls.model.operations}

    def test_type_naming_follows_design_5_1(self) -> None:
        create = self.by_name["sessions.create"]
        self.assertEqual(create.body.params_type, "SessionsCreateParams")
        self.assertEqual(create.response.type.name, "SessionsCreateResponse")
        self.assertIn("SessionsCreateParamsContactDetails", self.model.objects)

    def test_pagination_and_binary_and_application_scope(self) -> None:
        self.assertEqual(self.by_name["sessions.list"].response.kind, "page")
        self.assertEqual(self.by_name["sessions.generate_pdf"].response.kind, "binary")
        cases = self.by_name["cases.list"]
        self.assertEqual(cases.scope, "application")
        self.assertEqual([p.name for p in cases.path_params], [])

    def test_multipart_is_chosen_for_file_uploads(self) -> None:
        verify = self.by_name["id_verification.verify"]
        self.assertEqual(verify.body.content_type, "multipart/form-data")
        front = next(p for p in self.model.objects["IdVerificationVerifyParams"].props if p.json_name == "front_image")
        self.assertEqual(front.type.kind, "file")

    def test_json_bodies_whose_multipart_form_carries_files_accept_both(self) -> None:
        # The JSON body has no file field, so the files live only in the multipart form.
        for dotted, files, required in (
            ("sessions.imports.create", {"file"}, set()),
            ("database_validation.validate", {"selfie"}, {"issuing_state"}),
            (
                "customization.update",
                {"image_square", "image_rectangular", "image_rectangular_dark", "image_favicon"},
                set(),
            ),
        ):
            op = self.by_name[dotted]
            self.assertEqual(op.body.content_type, "multipart/form-data", dotted)
            self.assertTrue(op.body.json_without_files, dotted)
            props = self.model.objects[op.params_type].props
            self.assertEqual({p.json_name for p in props if p.type.kind == "file"}, files, dotted)
            self.assertEqual({p.json_name for p in props if p.required}, required, dotted)
        self.assertIn("source_file_url", {p.json_name for p in self.model.objects["SessionsImportsCreateParams"].props})
        self.assertIn("color_primary", {p.json_name for p in self.model.objects["CustomizationUpdateParams"].props})
        self.assertFalse(self.by_name["id_verification.verify"].body.json_without_files)

    def test_generation_is_deterministic(self) -> None:
        first = csharp.emit(model.build(spec.load(ROOT)))
        second = csharp.emit(model.build(spec.load(ROOT)))
        self.assertEqual(first, second)


if __name__ == "__main__":
    unittest.main()
