import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "require_ci.py"
SHA = "a" * 40


class RequireCiTests(unittest.TestCase):
    def test_accepts_the_latest_successful_exact_sha_checks(self):
        completed = self.run_script([
            self.check("dotnet", "SUCCESS", "2026-08-11T18:00:00Z"),
            self.check("web", "SUCCESS", "2026-08-11T18:00:01Z"),
        ])
        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_rejects_missing_pending_failed_or_wrong_sha_checks(self):
        cases = [
            [self.check("dotnet"), self.check("web", status="IN_PROGRESS")],
            [self.check("dotnet"), self.check("web", conclusion="FAILURE")],
            [self.check("dotnet")],
            [self.check("dotnet"), self.check("web", head_sha="b" * 40)],
        ]
        for checks in cases:
            with self.subTest(checks=checks):
                completed = self.run_script(checks)
                self.assertNotEqual(0, completed.returncode)

    def test_uses_the_most_recent_check_with_each_name(self):
        completed = self.run_script([
            self.check("dotnet", "FAILURE", "2026-08-11T17:00:00Z"),
            self.check("dotnet", "SUCCESS", "2026-08-11T18:00:00Z"),
            self.check("web", "SUCCESS", "2026-08-11T18:00:00Z"),
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
    def check(name, conclusion="SUCCESS", started_at="2026-08-11T18:00:00Z",
              status="COMPLETED", head_sha=SHA):
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
