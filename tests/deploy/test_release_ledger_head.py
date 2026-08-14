import json
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
PARSER = ROOT / "scripts" / "deploy" / "release_ledger_head.py"
SPEC = importlib.util.spec_from_file_location("release_ledger_head", PARSER)
LEDGER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(LEDGER)


def record(deployment_id, created_at, state):
    return {
        "id": deployment_id,
        "created_at": created_at,
        "task": "lex-revision-promotion",
        "environment": "production",
        "production_environment": True,
        "latest_status": state,
    }


class ReleaseLedgerHeadTests(unittest.TestCase):
    def test_parser_selects_only_the_first_success_in_strict_newest_first_order(self):
        result = self.run_parser(
            [
                record(103, "2026-08-14T12:00:00Z", "failure"),
                record(102, "2026-08-14T11:00:00Z", "pending"),
                record(101, "2026-08-14T10:00:00Z", "success"),
            ]
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("101", result.stdout.strip())

    def test_parser_rejects_reordered_duplicate_or_non_terminal_success_records(self):
        cases = [
            [
                record(101, "2026-08-14T10:00:00Z", "failure"),
                record(102, "2026-08-14T11:00:00Z", "success"),
            ],
            [
                record(102, "2026-08-14T11:00:00Z", "success"),
                record(101, "2026-08-14T10:00:00Z", "failure"),
            ],
            [
                record(102, "2026-08-14T11:00:00Z", "failure"),
                record(102, "2026-08-14T10:00:00Z", "success"),
            ],
        ]
        for records in cases:
            with self.subTest(records=records):
                self.assertNotEqual(0, self.run_parser(records).returncode)

    def test_parser_uses_id_as_the_documented_same_instant_tie_breaker(self):
        result = self.run_parser(
            [
                record(103, "2026-08-14T12:00:00Z", "failure"),
                record(102, "2026-08-14T12:00:00Z", "success"),
            ]
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("102", result.stdout.strip())

    def test_resolver_paginates_and_queries_each_latest_status_before_selecting_head(self):
        deployments = [
            {
                "id": 300 - index,
                "created_at": f"2026-08-14T11:{59 - (index % 60):02d}:00Z",
                "task": "lex-revision-promotion",
                "environment": "production",
                "production_environment": True,
            }
            for index in range(100)
        ]
        # Keep the fixture globally ordered across the page boundary.
        for index, deployment in enumerate(deployments):
            deployment["created_at"] = f"2026-08-14T{11 - index // 60:02d}:{59 - index % 60:02d}:00Z"
        second_page = [
            {
                "id": 200,
                "created_at": "2026-08-14T09:59:00Z",
                "task": "lex-revision-promotion",
                "environment": "production",
                "production_environment": True,
            }
        ]
        statuses = {str(item["id"]): "failure" for item in deployments}
        statuses["200"] = "success"

        calls = []

        def request(endpoint):
            calls.append(endpoint)
            if "/statuses?" in endpoint:
                deployment_id = endpoint.split("/deployments/", 1)[1].split("/", 1)[0]
                return [{"state": statuses[deployment_id]}]
            page = int(endpoint.rsplit("page=", 1)[1])
            return [deployments, second_page, []][page - 1]

        result = LEDGER.resolve("SFHAJJI/lex", request)

        self.assertEqual(200, result)
        self.assertTrue(any("page=2" in call for call in calls))
        self.assertTrue(any("deployments/200/statuses?per_page=1" in call for call in calls))

    def test_resolver_fails_closed_when_five_full_pages_have_no_success(self):
        pages = []
        statuses = {}
        next_id = 1000
        instant = 0
        for page in range(5):
            values = []
            for _ in range(100):
                hour = 23 - instant // 3600
                minute = 59 - (instant % 3600) // 60
                second = 59 - instant % 60
                values.append(
                    {
                        "id": next_id,
                        "created_at": f"2026-08-14T{hour:02d}:{minute:02d}:{second:02d}Z",
                        "task": "lex-revision-promotion",
                        "environment": "production",
                        "production_environment": True,
                    }
                )
                statuses[str(next_id)] = "failure"
                next_id -= 1
                instant += 1
            pages.append(values)

        def request(endpoint):
            if "/statuses?" in endpoint:
                deployment_id = endpoint.split("/deployments/", 1)[1].split("/", 1)[0]
                return [{"state": statuses[deployment_id]}]
            page = int(endpoint.rsplit("page=", 1)[1])
            return pages[page - 1]

        with self.assertRaisesRegex(ValueError, "bounded deployment history"):
            LEDGER.resolve("SFHAJJI/lex", request)

    def test_resolver_validates_the_complete_returned_page_before_accepting_success(self):
        page = [
            {
                "id": 10,
                "created_at": "2026-08-14T10:00:00Z",
                "task": "lex-revision-promotion",
                "environment": "production",
                "production_environment": True,
            },
            {
                "id": 11,
                "created_at": "2026-08-14T11:00:00Z",
                "task": "lex-revision-promotion",
                "environment": "production",
                "production_environment": True,
            },
        ]

        def request(endpoint):
            if "/statuses?" in endpoint:
                return [{"state": "success"}]
            return page

        with self.assertRaisesRegex(ValueError, "strictly newest first"):
            LEDGER.resolve("SFHAJJI/lex", request)

    def run_parser(self, records):
        with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
            path = Path(temporary) / "records.jsonl"
            path.write_text(
                "".join(json.dumps(item) + "\n" for item in records), encoding="utf-8"
            )
            return subprocess.run(
                [sys.executable, str(PARSER), str(path)],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )

if __name__ == "__main__":
    unittest.main()
