import json
from pathlib import Path
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "metric_evidence.py"
CANDIDATE = "ca-lex-web--candidate"
START = "2026-08-11T16:18:00Z"
REQUIRED = "2026-08-11T16:21:00Z"


class MetricEvidenceTests(unittest.TestCase):
    def test_selects_an_exact_candidate_series_and_required_bucket(self):
        completed = self.run_script(self.response(CANDIDATE))
        self.assertEqual(0, completed.returncode, completed.stderr)
        evidence = json.loads(completed.stdout)
        self.assertEqual(507_674_624, evidence["memory_max"])
        self.assertEqual(507_674_624, evidence["memory_required"])
        self.assertEqual(1, evidence["replicas_max"])
        self.assertEqual(1, evidence["replicas_required"])
        self.assertEqual(REQUIRED, evidence["required_timestamp"])

    def test_selects_the_candidate_from_a_split_dimension_response(self):
        response = self.response("other-revision")
        candidate = self.response(CANDIDATE)
        for metric, candidate_metric in zip(response["value"], candidate["value"]):
            metric["timeseries"].extend(candidate_metric["timeseries"])
        completed = self.run_script(response)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(CANDIDATE, json.loads(completed.stdout)["revision"])

    def test_rejects_missing_wrong_or_incomplete_series(self):
        missing = self.run_script({"value": []})
        self.assertNotEqual(0, missing.returncode)
        self.assertIn("candidate metric series", missing.stderr)

        wrong = self.run_script(self.response("other-revision"))
        self.assertNotEqual(0, wrong.returncode)

        incomplete = self.response(CANDIDATE)
        incomplete["value"][0]["timeseries"][0]["data"].pop()
        completed = self.run_script(incomplete)
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("required load bucket", completed.stderr)

    def test_measures_only_the_window_inside_a_wide_response(self):
        completed = self.run_script(self.wide_response(CANDIDATE))
        self.assertEqual(0, completed.returncode, completed.stderr)
        evidence = json.loads(completed.stdout)
        self.assertEqual(507_674_624, evidence["memory_max"])
        self.assertEqual(507_674_624, evidence["memory_required"])
        self.assertEqual(1, evidence["replicas_max"])
        self.assertEqual(1, evidence["replicas_required"])

    def test_rejects_a_wide_response_without_the_required_bucket(self):
        response = self.wide_response(CANDIDATE)
        for metric in response["value"]:
            metric["timeseries"][0]["data"] = [
                point for point in metric["timeseries"][0]["data"]
                if point["timeStamp"] != REQUIRED
            ]
        completed = self.run_script(response)
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("required load bucket", completed.stderr)

    def test_ignores_buckets_whose_timestamp_is_not_a_utc_bucket(self):
        response = self.response(CANDIDATE)
        for metric in response["value"]:
            metric["timeseries"][0]["data"].append(
                {"timeStamp": "2026-08-11T16:19:00.0000000Z", "maximum": 1_900_000_000})
        completed = self.run_script(response)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(507_674_624, json.loads(completed.stdout)["memory_max"])

    def test_rejects_malformed_or_oversized_payloads(self):
        malformed = subprocess.run(
            [sys.executable, str(SCRIPT), CANDIDATE, START, REQUIRED],
            cwd=ROOT,
            input="not-json",
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertEqual(2, malformed.returncode)
        oversized = subprocess.run(
            [sys.executable, str(SCRIPT), CANDIDATE, START, REQUIRED],
            cwd=ROOT,
            input=b" " * (4 * 1024 * 1024 + 1),
            capture_output=True,
            check=False,
        )
        self.assertEqual(2, oversized.returncode)

    @staticmethod
    def response(revision):
        points = [
            {"timeStamp": START, "maximum": 296_390_656},
            {"timeStamp": "2026-08-11T16:19:00Z", "maximum": 482_856_960},
            {"timeStamp": "2026-08-11T16:20:00Z", "maximum": 506_757_120},
            {"timeStamp": REQUIRED, "maximum": 507_674_624},
        ]
        replicas = [{"timeStamp": point["timeStamp"], "maximum": 1} for point in points]
        return {
            "value": [
                MetricEvidenceTests.metric("WorkingSetBytes", revision, points),
                MetricEvidenceTests.metric("Replicas", revision, replicas),
            ]
        }

    @staticmethod
    def wide_response(revision):
        # The workflow queries a timespan far wider than the measured window, so
        # the payload carries heavier buckets on both sides of it. None of them
        # may reach memory_max or replicas_max.
        outside = [
            {"timeStamp": "2026-08-11T16:05:00Z", "maximum": 1_900_000_000},
            {"timeStamp": "2026-08-11T16:17:00Z", "maximum": 1_800_000_000},
            {"timeStamp": "2026-08-11T16:22:00Z", "maximum": 1_700_000_000},
            {"timeStamp": "2026-08-11T16:40:00Z", "maximum": 2_000_000_000},
        ]
        response = MetricEvidenceTests.response(revision)
        for metric in response["value"]:
            replicas = metric["name"]["value"] == "Replicas"
            data = metric["timeseries"][0]["data"]
            data.extend(
                {"timeStamp": point["timeStamp"], "maximum": 4 if replicas else point["maximum"]}
                for point in outside)
            data.sort(key=lambda point: point["timeStamp"])
        return response

    @staticmethod
    def metric(name, revision, points):
        return {
            "name": {"value": name},
            "timeseries": [{
                "metadatavalues": [{
                    "name": {"value": "revisionname"},
                    "value": revision,
                }],
                "data": points,
            }],
        }

    @staticmethod
    def run_script(payload):
        return subprocess.run(
            [sys.executable, str(SCRIPT), CANDIDATE, START, REQUIRED],
            cwd=ROOT,
            input=json.dumps(payload, separators=(",", ":")),
            text=True,
            capture_output=True,
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
