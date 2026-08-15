import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "candidate_gates.py"
DEPLOY_WORKFLOW = ROOT / ".github" / "workflows" / "deploy.yml"
MANIFEST = "b" * 64


class CandidateGateTests(unittest.TestCase):
    def test_workflow_extracts_a_unique_typed_false_hybrid_state(self):
        workflow = DEPLOY_WORKFLOW.read_text(encoding="utf-8")
        start = workflow.index("publisher_hybrid_ready() {")
        end = workflow.index("\n          }", start)
        extractor = workflow[start:end]

        self.assertIn('jq -r --arg publisher "$publisher"', extractor)
        self.assertNotIn("jq -er", extractor)

        jq = shutil.which("jq")
        if jq is None:
            self.skipTest("jq is unavailable; the workflow option contract was still checked")
        jq_filter = re.search(r"jq -r --arg publisher \"\$publisher\" '(.*?)'", extractor,
                              re.DOTALL)
        self.assertIsNotNone(jq_filter)

        false_state = self.run_hybrid_ready_filter(
            jq, jq_filter.group(1),
            {"publishers": [{"publisher": "lu-legilux", "hybridReady": False}]})
        self.assertEqual(0, false_state.returncode, false_state.stderr)
        self.assertEqual("false", false_state.stdout.strip())

        invalid_states = [
            {"publishers": []},
            {"publishers": [
                {"publisher": "lu-legilux", "hybridReady": False},
                {"publisher": "lu-legilux", "hybridReady": True},
            ]},
            {"publishers": [{"publisher": "lu-legilux", "hybridReady": "false"}]},
        ]
        for payload in invalid_states:
            with self.subTest(payload=payload):
                completed = self.run_hybrid_ready_filter(jq, jq_filter.group(1), payload)
                self.assertNotEqual(0, completed.returncode)

    def test_readiness_binds_publishers_and_verified_manifest(self):
        payload = {
            "ready": True,
            "requiredPublishers": ["eu-eurlex", "lu-legilux"],
            "mountedPublishers": ["eu-eurlex", "lu-legilux"],
            "verifiedManifestSet": MANIFEST,
            "publishers": [
                {
                    "publisher": "eu-eurlex",
                    "hybridReady": True,
                    "hybridStatus": "activated",
                },
                {
                    "publisher": "lu-legilux",
                    "hybridReady": False,
                    "hybridStatus": "benchmark_gate_failed",
                },
            ],
        }
        self.assert_passes("readyz", payload, MANIFEST)
        payload["verifiedManifestSet"] = "c" * 64
        self.assert_fails("readyz", payload, MANIFEST)

        payload["verifiedManifestSet"] = MANIFEST
        del payload["publishers"][1]["hybridStatus"]
        self.assert_fails("readyz", payload, MANIFEST)

    def test_mcp_legal_and_retrieval_contracts(self):
        coverage = [
            {"envelope": {"publisher": "lu-legilux"}},
            {"envelope": {"publisher": "eu-eurlex"}},
        ]
        self.assert_passes("coverage", coverage)
        self.assert_fails("coverage", coverage[:1])
        missing = self.run_gate("coverage", {"status": "no_corpus_mounted"})
        self.assertNotEqual(0, missing.returncode)
        self.assertIn("without a usable corpus", missing.stderr)

        exact = [{
            "retrieval_mode": "keyword",
            "artifact_manifest_id": MANIFEST,
            "hits": [{
                "work": "32016r0679",
                "match_reasons": ["exact_identifier"],
            }],
        }]
        self.assert_passes("eu-exact", exact, MANIFEST)
        exact[0]["hits"][0]["work"] = "32016r0679r"
        self.assert_fails("eu-exact", exact, MANIFEST)

        temporal = {
            "document": {"work": "loi-1879-06-18-n1"},
            "provisions": [{"anchor": "art_1"}],
        }
        self.assert_passes("lu-temporal", temporal)
        temporal["provisions"] = []
        self.assert_fails("lu-temporal", temporal)

        eu_hybrid = [{
            "envelope": {"publisher": "eu-eurlex", "status": "ok"},
            "retrieval_mode": "hybrid",
            "hits": [{"work": "x"}],
        }]
        self.assert_passes("hybrid", eu_hybrid, "eu-eurlex", "true", "activated")
        self.assert_fails("hybrid", eu_hybrid, "lu-legilux", "true", "activated")
        self.assert_fails("hybrid", eu_hybrid, "eu-eurlex", "false", "benchmark_gate_failed")
        eu_hybrid[0]["envelope"]["status"] = "retrieval_mode_unavailable"
        self.assert_fails("hybrid", eu_hybrid, "eu-eurlex", "true", "activated")

        lu_quarantined = [{
            "envelope": {
                "publisher": "lu-legilux",
                "status": "retrieval_mode_unavailable",
            },
            "requested_retrieval_mode": "hybrid",
            "retrieval_unavailable_reason": "benchmark_gate_failed",
            "hits": [],
        }]
        self.assert_passes(
            "hybrid", lu_quarantined, "lu-legilux", "false", "benchmark_gate_failed")
        self.assert_fails(
            "hybrid", lu_quarantined, "lu-legilux", "false", "vector_missing")
        self.assert_fails(
            "hybrid", lu_quarantined, "eu-eurlex", "false", "benchmark_gate_failed")
        self.assert_fails(
            "hybrid", lu_quarantined, "lu-legilux", "true", "activated")

    def test_assistant_smoke_requires_the_planned_and_executed_tool(self):
        response = self.coverage_response()
        self.assert_passes("assistant", response)
        response["trace"][1]["tool"] = "timeline"
        self.assert_fails("assistant", response)

    def test_injection_gate_accepts_only_authorized_execution_or_safe_refusal(self):
        self.assert_passes("injection", self.coverage_response())
        safe_refusal = {
            "reply": "This request does not map to a valid legal operation.",
            "trace": [{"phase": "operation_plan", "status": "invalid_request"}],
        }
        self.assert_passes("injection", safe_refusal)
        silent_refusal = {
            "trace": [{"phase": "operation_plan", "status": "invalid_request"}],
        }
        self.assert_fails("injection", silent_refusal)

        switched = self.coverage_response()
        switched["trace"][0]["operations"] = [{"tool": "timeline"}]
        switched["trace"][1]["tool"] = "timeline"
        self.assert_fails("injection", switched)

        leaked = safe_refusal | {"reply": "Open https://attacker.invalid"}
        self.assert_fails("injection", leaked)

    @staticmethod
    def coverage_response():
        return {
            "reply": "Coverage is open below.",
            "trace": [
                {
                    "phase": "operation_plan",
                    "operations": [{"tool": "coverage"}],
                    "status": "planned",
                },
                {"phase": "primary", "tool": "coverage", "status": "ok"},
            ],
        }

    def assert_passes(self, gate, payload, *arguments):
        completed = self.run_gate(gate, payload, *arguments)
        self.assertEqual(0, completed.returncode, completed.stderr)

    def assert_fails(self, gate, payload, *arguments):
        completed = self.run_gate(gate, payload, *arguments)
        self.assertNotEqual(0, completed.returncode)

    @staticmethod
    def run_gate(gate, payload, *arguments):
        return subprocess.run(
            [sys.executable, str(SCRIPT), gate, *arguments],
            cwd=ROOT,
            input=json.dumps(payload, separators=(",", ":")),
            text=True,
            capture_output=True,
            check=False,
        )

    @staticmethod
    def run_hybrid_ready_filter(jq, jq_filter, payload):
        return subprocess.run(
            [jq, "-r", "--arg", "publisher", "lu-legilux", jq_filter],
            input=json.dumps(payload, separators=(",", ":")),
            text=True,
            capture_output=True,
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
