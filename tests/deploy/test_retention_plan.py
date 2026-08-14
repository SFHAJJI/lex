import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "retention_plan.py"


class RetentionPlanTests(unittest.TestCase):
    def test_plan_protects_live_roles_and_receipts_and_never_uses_age(self):
        inventory = self.inventory()
        completed, plan = self.run_plan(inventory)

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(2, plan["container_apps"]["max_inactive_revisions"])
        self.assertEqual(
            ["sha256:" + "d" * 64, "sha256:" + "e" * 64],
            plan["acr"]["delete_digests"],
        )
        self.assertEqual(3, len(plan["acr"]["protected_digests"]))
        self.assertNotIn("age_days", json.dumps(plan).lower())
        self.assertNotIn("older_than", json.dumps(plan).lower())
        self.assertEqual(
            ["staging/eu/run/index-eu-eurlex.db"],
            [item["name"] for item in plan["blob_staging"]["delete"]],
        )
        self.assertNotIn("releases/", json.dumps(plan["blob_staging"]["delete"]))

    def test_plan_fails_closed_on_unknown_role_or_mutable_image(self):
        inventory = self.inventory()
        inventory["candidate_revision"] = "missing"
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["acr_manifests"] = inventory["acr_manifests"][1:]
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["revisions"][0]["image"] = "registry/lex-web:latest"
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["rollback_revision"] = ""
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["promotion_receipts"][0]["target_revision"] = "other"
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["promotion_receipts"] = []
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

    def test_steady_state_protects_exactly_production_and_rollback(self):
        inventory = self.inventory()
        inventory["candidate_revision"] = ""
        completed, plan = self.run_plan(inventory)

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(1, plan["container_apps"]["max_inactive_revisions"])
        self.assertEqual(2, len(plan["acr"]["protected_digests"]))

    def test_role_identity_count_does_not_require_distinct_image_digests(self):
        inventory = self.inventory()
        production_image = inventory["revisions"][0]["image"]
        inventory["revisions"][2]["image"] = production_image
        inventory["promotion_receipts"][0]["rollback_image"] = production_image.rpartition("@")[2]

        completed, plan = self.run_plan(inventory)

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(3, plan["container_apps"]["protected_revision_count"])
        self.assertEqual(2, len(plan["acr"]["protected_digests"]))
        protected = {
            item["digest"]: item["reasons"] for item in plan["acr"]["protected_digests"]
        }
        self.assertEqual(
            ["live_production", "live_rollback"],
            protected[production_image.rpartition("@")[2]],
        )

    @staticmethod
    def inventory():
        digest = lambda value: "sha256:" + value * 64
        return {
            "schema": "lex-retention-inventory/1",
            "production_revision": "prod",
            "candidate_revision": "candidate",
            "rollback_revision": "rollback",
            "revisions": [
                {"name": "prod", "active": True, "trafficWeight": 100,
                 "createdTime": "2026-08-14T00:00:00Z", "image": "registry/lex-web@" + digest("a")},
                {"name": "candidate", "active": False, "trafficWeight": 0,
                 "createdTime": "2026-08-14T01:00:00Z", "image": "registry/lex-web@" + digest("b")},
                {"name": "rollback", "active": False, "trafficWeight": 0,
                 "createdTime": "2026-08-13T00:00:00Z", "image": "registry/lex-web@" + digest("c")},
                {"name": "old", "active": False, "trafficWeight": 0,
                 "createdTime": "2026-08-12T00:00:00Z", "image": "registry/lex-web@" + digest("d")},
            ],
            "promotion_receipts": [
                {"id": 2, "state": "success", "created_at": "2026-08-14T00:00:00Z",
                 "target_revision": "prod", "target_image": digest("a"),
                 "rollback_revision": "rollback", "rollback_image": digest("c")},
                {"id": 1, "state": "success", "created_at": "2026-08-13T00:00:00Z",
                 "target_revision": "old-prod", "target_image": digest("e"),
                 "rollback_revision": "old-rollback", "rollback_image": digest("d")},
            ],
            "acr_manifests": [
                {"digest": digest("a")}, {"digest": digest("b")},
                {"digest": digest("c")}, {"digest": digest("d")},
                {"digest": digest("e")},
            ],
            "staging_blobs": [
                {"name": "staging/eu/run/index-eu-eurlex.db", "etag": "etag-1",
                 "immutable_release_verified": True},
                {"name": "staging/eu/run/index-eu-eurlex.vectors", "etag": "etag-2",
                 "immutable_release_verified": False},
                {"name": "releases/eu/manifest/index.db", "etag": "etag-3",
                 "immutable_release_verified": True},
            ],
        }

    @staticmethod
    def run_plan(inventory):
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            source = Path(directory) / "inventory.json"
            source.write_text(json.dumps(inventory), encoding="utf-8")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT), str(source)],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
            plan = json.loads(completed.stdout) if completed.returncode == 0 else None
            return completed, plan


if __name__ == "__main__":
    unittest.main()
