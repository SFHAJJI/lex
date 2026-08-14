import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "bootstrap_plan.py"


class BootstrapPlanTests(unittest.TestCase):
    def test_plan_is_an_exact_identity_digest_and_etag_allowlist(self):
        completed, plan = self.run_plan(self.inventory())

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertTrue(plan["dry_run"])
        self.assertEqual(
            ["ca-lex-web--legacy-current"],
            [
                item["revision"]
                for item in plan["container_apps"]["deactivate_revisions"]
            ],
        )
        self.assertTrue(all(
            item["image"].startswith("crsoufien3orem.azurecr.io/lex-web@sha256:")
            and item["canonical_template_digest"].startswith("sha256:")
            for item in plan["container_apps"]["deactivate_revisions"]
        ))
        self.assertEqual(1, plan["container_apps"]["max_inactive_revisions"])
        self.assertEqual(["sha256:" + "d" * 64], plan["acr"]["delete_digests"])
        self.assertEqual(
            [{"name": "staging/legacy/index.db", "etag": '"etag-1"'}],
            plan["blob_staging"]["delete"],
        )
        self.assertEqual(
            "sha256:" + "a" * 64,
            plan["protected"]["production"]["image_digest"],
        )
        self.assertEqual(
            "sha256:" + "a" * 64,
            plan["protected"]["rollback"]["image_digest"],
        )
        self.assertEqual(
            plan["protected"]["production"]["canonical_template_digest"],
            plan["protected"]["rollback"]["canonical_template_digest"],
        )

    def test_plan_refuses_a_failed_candidate_newer_than_the_bootstrap_fallback(self):
        inventory = self.inventory()
        inventory["revisions"][0]["createdTime"] = "2026-08-14T04:00:00Z"

        completed, _ = self.run_plan(inventory)

        self.assertNotEqual(0, completed.returncode)
        self.assertIn("all legacy revisions must predate", completed.stderr)

    def test_plan_refuses_mutable_or_missing_identities_and_duplicate_blobs(self):
        inventory = self.inventory()
        inventory["revisions"][-1]["image"] = "registry/lex-web:latest"
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

    def test_plan_refuses_loose_types_and_another_registry_or_repository(self):
        for field, value in (("active", 1), ("trafficWeight", "0")):
            inventory = self.inventory()
            inventory["revisions"][-1][field] = value
            completed, _ = self.run_plan(inventory)
            self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        other = "other.azurecr.io/lex-web@" + "sha256:" + "a" * 64
        for revision in inventory["revisions"][-2:]:
            revision["image"] = other
            revision["template"]["containers"][0]["image"] = other
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        other = "crsoufien3orem.azurecr.io/other@" + "sha256:" + "a" * 64
        for revision in inventory["revisions"][-2:]:
            revision["image"] = other
            revision["template"]["containers"][0]["image"] = other
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["production_revision"] = "missing"
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["staging_blobs"].append(dict(inventory["staging_blobs"][0]))
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

    def test_plan_requires_explicit_utc_single_container_and_string_etags(self):
        inventory = self.inventory()
        inventory["revisions"][-1]["createdTime"] = "2026-08-14T05:00:00+02:00"
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

    def test_plan_requires_the_bounded_a_active_r_inactive_c_active_state(self):
        inventory = self.inventory()
        inventory["max_inactive_revisions"] = 2
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["revisions"][-2]["active"] = True
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["revisions"][-1]["active"] = False
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["revisions"].insert(0, {
            **inventory["revisions"][0],
            "name": "ca-lex-web--extra-inactive",
            "active": False,
            "trafficWeight": 0,
            "createdTime": "2026-08-13T23:00:00Z",
            "template": {
                **inventory["revisions"][0]["template"],
                "revisionSuffix": "extra-inactive",
            },
        })
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["revisions"][-1]["template"]["containers"].append(
            dict(inventory["revisions"][-1]["template"]["containers"][0])
        )
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["staging_blobs"][0]["etag"] = 123
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["acr_manifests"][0] = "sha256:" + "a" * 64
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

    @staticmethod
    def inventory():
        digest = lambda value: "sha256:" + value * 64
        image = lambda value: "crsoufien3orem.azurecr.io/lex-web@" + digest(value)
        template = lambda suffix, value="a": {
            "revisionSuffix": suffix,
            "containers": [{
                "name": "lex-web",
                "image": image(value),
                "resources": {"cpu": 1.0, "memory": "2Gi"},
                "env": [{"name": "LEX_CODE_COMMIT", "value": "abc123"}],
            }],
            "scale": {"minReplicas": 1, "maxReplicas": 1},
        }
        return {
            "schema": "lex-bootstrap-inventory/1",
            "max_inactive_revisions": 1,
            "production_revision": "ca-lex-web--bootstrap-production",
            "rollback_revision": "ca-lex-web--bootstrap-rollback",
            "revisions": [
                {
                    "name": "ca-lex-web--legacy-current",
                    "active": True,
                    "trafficWeight": 100,
                    "createdTime": "2026-08-14T00:00:00+00:00",
                    "image": image("d"),
                    "template": template("legacy-old", "d"),
                },
                {
                    "name": "ca-lex-web--bootstrap-rollback",
                    "active": False,
                    "trafficWeight": 0,
                    "createdTime": "2026-08-14T02:00:00+00:00",
                    "image": image("a"),
                    "template": template("bootstrap-rollback"),
                },
                {
                    "name": "ca-lex-web--bootstrap-production",
                    "active": True,
                    "trafficWeight": 0,
                    "createdTime": "2026-08-14T03:00:00+00:00",
                    "image": image("a"),
                    "template": template("bootstrap-production"),
                },
            ],
            "acr_manifests": [
                {"digest": digest("a")},
                {"digest": digest("d")},
            ],
            "staging_blobs": [
                {"name": "staging/legacy/index.db", "etag": '"etag-1"'},
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
