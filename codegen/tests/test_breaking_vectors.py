"""The shared breaking-change vectors, openapi/breaking-vectors.json (DESIGN.md section 3.1).

sdk-python generates the file and the six other server SDKs copy it byte for byte, so the seven spec syncs report the
same breaking changes for one upstream change. Every case runs through codegen/spec_sync.py here; none is skipped. A case
that looks wrong is reported to sdk-python, where a rule change lands first; the file is never edited here.
"""

from __future__ import annotations

import hashlib
import json
import sys
import unittest
from pathlib import Path
from typing import Any

CODEGEN = Path(__file__).resolve().parent.parent
REPO = CODEGEN.parent
VECTORS_PATH = REPO / "openapi" / "breaking-vectors.json"
FIXTURES = CODEGEN / "tests" / "fixtures"
sys.path.insert(0, str(CODEGEN))

import spec_sync  # noqa: E402

VECTORS: Any = json.loads(VECTORS_PATH.read_bytes())

# The API change of 2026-11-04 (fixtures of test_spec_sync.py) as the vectors spell it: the same lines in all seven SDKs.
API_CHANGE_2026_11_04 = [
    "GET /v3/session/{sessionId}/decision/ response registry_checks[].company.registry_data: can now be null",
    "GET /v3/session/{sessionId}/decision/ response registry_checks[].ownership_structure: can now be null",
    "GET /v3/session/{sessionId}/decision/ response signatures[].confirmation.channel: can now be null",
    "POST /v3/email/risk/ response email.email_intelligence.reason_codes[]: type string -> object",
    "POST /v3/email/risk/ response email.enrichment.reason_codes[]: type string -> object",
    "POST /v3/kyb/select/ response kyb_registry.registry_data: can now be null",
    "POST /v3/kyb/shareholders/ response kyb_registry.registry_data: can now be null",
    "POST /v3/kyb/ubo/ response kyb_registry.registry_data: can now be null",
    "POST /v3/phone/risk/ response phone.enrichment.reason_codes[]: type string -> object",
    "POST /v3/phone/risk/ response phone.phone_intelligence.reason_codes[]: type string -> object",
    "POST /v3/session/import-shared/ response registry_checks[].company.registry_data: can now be null",
    "POST /v3/session/import-shared/ response registry_checks[].ownership_structure: can now be null",
    "POST /v3/session/import-shared/ response signatures[].confirmation.channel: can now be null",
    "POST /v3/session/{sessionId}/signatures/{nodeId}/validate/ response signatures[].confirmation.channel: can now be null",
]


def fixture(commit: str) -> Any:
    return json.loads((FIXTURES / f"verification-{commit}.json").read_bytes())


class BreakingVectorsTest(unittest.TestCase):
    def test_the_file_is_the_copy_recorded_in_source_json(self) -> None:
        source = json.loads((REPO / "openapi" / "SOURCE.json").read_text("utf-8"))["breaking-vectors.json"]
        self.assertEqual(hashlib.sha256(VECTORS_PATH.read_bytes()).hexdigest(), source["sha256"], "copied byte for byte")
        self.assertEqual(list(VECTORS), ["version", "rules", "generator", "cases"])
        self.assertEqual(VECTORS["version"], 1)
        self.assertEqual(VECTORS["rules"], "docs/DESIGN.md section 3.1")

    def test_every_case_passes(self) -> None:
        cases = VECTORS["cases"]
        self.assertGreaterEqual(len(cases), 75)
        self.assertEqual(len({case["name"] for case in cases}), len(cases), "case names are unique")
        for case in cases:
            with self.subTest(case=case["name"], rule=case["rule"]):
                expect = case["expect"]
                self.assertEqual(spec_sync.classify(case["before"], case["after"]), (expect["type"], expect["breaking"]))

    def test_the_real_api_change_of_2026_11_04_in_vector_form(self) -> None:
        before, after = fixture("9fe57fc69299601e59cc7a64c1db370fea5c7461"), fixture("e002a815773bbd1599256e2774046ee76520c6c8")
        self.assertEqual(spec_sync.classify(before, after), ("feat(spec)", API_CHANGE_2026_11_04))

    def test_the_real_documentation_change_of_2026_11_06_in_vector_form(self) -> None:
        before, after = fixture("fe9f57a1dfb6374bfd391750d2044fc7a206e4ea"), fixture("ccc799410f82eee1c25c7ce5abc6caa53846be6b")
        self.assertEqual(spec_sync.classify(before, after), ("chore(spec)", []))


if __name__ == "__main__":
    unittest.main()
