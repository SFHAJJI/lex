import copy
import json
from pathlib import Path
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "revision_state.py"


class RevisionStateTests(unittest.TestCase):
    def test_receipt_failure_recovery_accepts_only_exact_previous_and_candidate(self):
        recovered = self.state(
            limit=1,
            revisions=[
                self.revision("previous", active=True, traffic=100, created="2026-08-13T10:00:00+00:00"),
                self.revision("candidate", active=False, traffic=0, created="2026-08-14T10:00:00+00:00"),
            ],
        )

        completed = self.assert_state(
            recovered,
            active="previous",
            inactive="candidate",
            traffic="previous=100",
            limit=1,
            created_order="previous,candidate",
        )
        self.assertEqual(0, completed.returncode, completed.stderr)

        invalid_states = []
        extra_active = copy.deepcopy(recovered)
        extra_active["revisions"].append(self.revision("former-production", True, 0))
        invalid_states.append(extra_active)
        extra_inactive = copy.deepcopy(recovered)
        extra_inactive["revisions"].append(self.revision("old-rollback", False, 0))
        invalid_states.append(extra_inactive)
        wrong_limit = copy.deepcopy(recovered)
        wrong_limit["max_inactive_revisions"] = 3
        invalid_states.append(wrong_limit)
        wrong_traffic = copy.deepcopy(recovered)
        wrong_traffic["revisions"][0]["trafficWeight"] = 50
        wrong_traffic["revisions"][1]["trafficWeight"] = 50
        invalid_states.append(wrong_traffic)
        out_of_range_traffic = copy.deepcopy(recovered)
        out_of_range_traffic["revisions"][0]["trafficWeight"] = 101
        invalid_states.append(out_of_range_traffic)
        non_utc_created = copy.deepcopy(recovered)
        non_utc_created["revisions"][0]["createdTime"] = "2026-08-13T12:00:00+02:00"
        invalid_states.append(non_utc_created)
        naive_created = copy.deepcopy(recovered)
        naive_created["revisions"][0]["createdTime"] = "2026-08-13T10:00:00"
        invalid_states.append(naive_created)

        for state in invalid_states:
            with self.subTest(state=state):
                completed = self.assert_state(
                    state,
                    active="previous",
                    inactive="candidate",
                    traffic="previous=100",
                    limit=1,
                    created_order="previous,candidate",
                )
                self.assertNotEqual(0, completed.returncode)

    def test_promotion_states_preserve_exact_evaluated_revision_identities(self):
        before = self.state(
            limit=2,
            revisions=[
                self.revision("prior", active=False, traffic=0, created="2026-08-12T10:00:00Z"),
                self.revision("current", active=True, traffic=100, created="2026-08-13T10:00:00Z"),
                self.revision("candidate", active=True, traffic=0, created="2026-08-14T10:00:00Z"),
            ],
        )
        after = self.state(
            limit=1,
            revisions=[
                self.revision("current", active=False, traffic=0, created="2026-08-13T10:00:00Z"),
                self.revision("candidate", active=True, traffic=100, created="2026-08-14T10:00:00Z"),
            ],
        )

        before_result = self.assert_state(
            before,
            active="current,candidate",
            inactive="prior",
            traffic="current=100",
            limit=2,
            created_order="prior,current,candidate",
        )
        after_result = self.assert_state(
            after,
            active="candidate",
            inactive="current",
            traffic="candidate=100",
            limit=1,
            created_order="current,candidate",
        )

        self.assertEqual(0, before_result.returncode, before_result.stderr)
        self.assertEqual(0, after_result.returncode, after_result.stderr)

        wrong_order = copy.deepcopy(before)
        wrong_order["revisions"][0]["createdTime"] = "2026-08-15T10:00:00Z"
        refused = self.assert_state(
            wrong_order,
            active="current,candidate",
            inactive="prior",
            traffic="current=100",
            limit=2,
            created_order="prior,current,candidate",
        )
        self.assertNotEqual(0, refused.returncode)

    def test_rollback_moves_between_exact_symmetric_steady_states(self):
        before = self.state(
            limit=1,
            revisions=[
                self.revision("current", active=True, traffic=100, created="2026-08-14T10:00:00Z"),
                self.revision("pinned-rollback", active=False, traffic=0, created="2026-08-13T10:00:00Z"),
            ],
        )
        after = self.state(
            limit=1,
            revisions=[
                self.revision("current", active=False, traffic=0, created="2026-08-14T10:00:00Z"),
                self.revision("pinned-rollback", active=True, traffic=100, created="2026-08-13T10:00:00Z"),
            ],
        )

        before_result = self.assert_state(
            before,
            active="current",
            inactive="pinned-rollback",
            traffic="current=100",
            limit=1,
            created_order="pinned-rollback,current",
        )
        after_result = self.assert_state(
            after,
            active="pinned-rollback",
            inactive="current",
            traffic="pinned-rollback=100",
            limit=1,
            created_order="pinned-rollback,current",
        )

        self.assertEqual(0, before_result.returncode, before_result.stderr)
        self.assertEqual(0, after_result.returncode, after_result.stderr)

    @staticmethod
    def revision(name, active, traffic, created="2026-08-01T00:00:00Z"):
        return {
            "name": name,
            "active": active,
            "trafficWeight": traffic,
            "createdTime": created,
        }

    @staticmethod
    def state(limit, revisions):
        return {"max_inactive_revisions": limit, "revisions": revisions}

    @staticmethod
    def assert_state(state, *, active, inactive, traffic, limit, created_order=""):
        return subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "--max-inactive",
                str(limit),
                "--active",
                active,
                "--inactive",
                inactive,
                "--traffic",
                traffic,
                "--created-order",
                created_order,
            ],
            cwd=ROOT,
            input=json.dumps(state),
            text=True,
            capture_output=True,
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
