import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "bootstrap_legacy_plan.py"


class BootstrapLegacyPlanTests(unittest.TestCase):
    def test_exact_a_and_inactive_legacy_inventory_is_reviewable(self):
        completed, plan = self.run_plan(self.inventory())

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertTrue(plan["dry_run"])
        self.assertEqual("ca-lex-web--a", plan["legacy_authority"]["revision"])
        self.assertEqual(1, plan["mutation"]["max_inactive_revisions"])
        self.assertFalse(plan["mutation"]["traffic_change"])
        self.assertEqual(3, len(plan["revisions"]))
        self.assertTrue(all(item["image_digest"].startswith("sha256:")
                            for item in plan["revisions"]))

    def test_refuses_extra_active_traffic_or_loose_types(self):
        for mutation in (
            lambda value: value["revisions"][1].update(active=True),
            lambda value: value["revisions"][1].update(trafficWeight=1),
            lambda value: value["revisions"][0].update(trafficWeight=99),
            lambda value: value["revisions"][0].update(active=1),
            lambda value: value.update(max_inactive_revisions=0),
            lambda value: value.update(max_inactive_revisions=101),
        ):
            inventory = self.inventory()
            mutation(inventory)
            completed, _ = self.run_plan(inventory)
            self.assertNotEqual(0, completed.returncode)

    def test_refuses_mutable_images_bad_times_duplicates_and_multiple_containers(self):
        inventory = self.inventory()
        inventory["revisions"][0]["image"] = "registry/lex-web:latest"
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        other = "other.azurecr.io/lex-web@sha256:" + "a" * 64
        inventory["revisions"][0]["image"] = other
        inventory["revisions"][0]["template"]["containers"][0]["image"] = other
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["revisions"][0]["createdTime"] = "2026-08-14T02:00:00+02:00"
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["revisions"].append(dict(inventory["revisions"][0]))
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

        inventory = self.inventory()
        inventory["revisions"][0]["template"]["containers"].append(
            dict(inventory["revisions"][0]["template"]["containers"][0])
        )
        completed, _ = self.run_plan(inventory)
        self.assertNotEqual(0, completed.returncode)

    @staticmethod
    def inventory():
        def revision(name, active, traffic, created, marker):
            image = "crsoufien3orem.azurecr.io/lex-web@sha256:" + marker * 64
            return {
                "name": name,
                "active": active,
                "trafficWeight": traffic,
                "createdTime": created,
                "image": image,
                "template": {
                    "revisionSuffix": name.split("--", 1)[1],
                    "containers": [{"name": "lex-web", "image": image}],
                },
            }

        return {
            "schema": "lex-bootstrap-legacy-inventory/1",
            "max_inactive_revisions": 100,
            "revisions": [
                revision("ca-lex-web--a", True, 100, "2026-08-14T00:00:00Z", "a"),
                revision("ca-lex-web--failed-1", False, 0, "2026-08-14T01:00:00Z", "b"),
                revision("ca-lex-web--failed-2", False, 0, "2026-08-14T02:00:00Z", "c"),
            ],
        }

    @staticmethod
    def run_plan(inventory):
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            source = Path(directory) / "inventory.json"
            source.write_text(json.dumps(inventory), encoding="utf-8")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT), str(source)], cwd=ROOT,
                text=True, capture_output=True, check=False,
            )
            plan = json.loads(completed.stdout) if completed.returncode == 0 else None
            return completed, plan


if __name__ == "__main__":
    unittest.main()
