import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "require_ci.py"
FIXTURES = Path(__file__).resolve().parent / "fixtures"
SHA = "a" * 40
RECORDED = FIXTURES / "check_runs_main.json"
RECORDED_SHA = "c0018a86100d8d93cd89515aceebf890adc8bb1a"


class RequireCiTests(unittest.TestCase):
    def test_accepts_a_recorded_github_rest_payload(self):
        # The deploy step feeds this script the REST check-runs payload verbatim,
        # so the recorded response is the contract the gate has to satisfy.
        completed = subprocess.run(
            [sys.executable, str(SCRIPT), str(RECORDED), RECORDED_SHA, "dotnet", "web"],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_recorded_payload_states_are_lowercase(self):
        payload = json.loads(RECORDED.read_text(encoding="utf-8"))
        states = {
            (item["status"], item["conclusion"])
            for item in payload["check_runs"]
        }
        self.assertIn(("completed", "success"), states)
        for status, conclusion in states:
            self.assertEqual(status, status.lower())
            self.assertEqual(conclusion, conclusion.lower())

    def test_accepts_the_latest_successful_exact_sha_checks(self):
        completed = self.run_script([
            self.check("dotnet", "success", "2026-08-11T18:00:00Z"),
            self.check("web", "success", "2026-08-11T18:00:01Z"),
        ])
        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_accepts_graphql_uppercase_states(self):
        completed = self.run_script([
            self.check("dotnet", "SUCCESS", status="COMPLETED"),
            self.check("web", "SUCCESS", status="COMPLETED"),
        ])
        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_reports_missing_or_in_progress_checks_as_retryable(self):
        cases = [
            [self.check("dotnet"), self.check("web", status="in_progress")],
            [self.check("dotnet"), self.check("web", status="queued")],
            [self.check("dotnet")],
            [self.check("dotnet"), self.check("web", head_sha="b" * 40)],
        ]
        for checks in cases:
            with self.subTest(checks=checks):
                completed = self.run_script(checks)
                self.assertEqual(75, completed.returncode, completed.stderr)

    def test_rejects_a_completed_failure_without_retrying(self):
        for conclusion in ("failure", "cancelled", "timed_out"):
            with self.subTest(conclusion=conclusion):
                completed = self.run_script([
                    self.check("dotnet"), self.check("web", conclusion=conclusion),
                ])
                self.assertEqual(1, completed.returncode)

    def test_uses_the_most_recent_check_with_each_name(self):
        completed = self.run_script([
            self.check("dotnet", "failure", "2026-08-11T17:00:00Z"),
            self.check("dotnet", "success", "2026-08-11T18:00:00Z"),
            self.check("web", "success", "2026-08-11T18:00:00Z"),
        ])
        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_reports_an_empty_payload_as_empty(self):
        with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
            payload = Path(temporary) / "checks.json"
            payload.write_bytes(b"")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT), str(payload), SHA, "dotnet", "web"],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("payload is empty", completed.stderr)

    @staticmethod
    def check(name, conclusion="success", started_at="2026-08-11T18:00:00Z",
              status="completed", head_sha=SHA):
        return {
            "name": name,
            "head_sha": head_sha,
            "status": status,
            "conclusion": conclusion,
            "started_at": started_at,
            "app": {"slug": "github-actions"},
        }

    def run_script(self, checks):
        with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
            payload = Path(temporary) / "checks.json"
            payload.write_text(json.dumps({"check_runs": checks}), encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(SCRIPT), str(payload), SHA, "dotnet", "web"],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )


if __name__ == "__main__":
    unittest.main()
