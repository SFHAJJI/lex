import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "bootstrap_legacy_recovery_plan.py"


class BootstrapLegacyRecoveryPlanTests(unittest.TestCase):
    def test_exact_a_plus_two_plan_authorizes_one_older_inactive_post(self):
        completed, plan = self.run_plan(self.inventory())

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("lex-bootstrap-legacy-recovery-plan/1", plan["schema"])
        self.assertTrue(plan["dry_run"])
        self.assertEqual("Multiple", plan["active_revisions_mode"])
        self.assertEqual(1, plan["max_inactive_revisions"])
        self.assertEqual(
            [{"revision_name": "ca-lex-web--a", "weight": 100,
              "latest_revision": False, "label": None}],
            plan["ingress_traffic"],
        )
        self.assertEqual("POST", plan["operation"]["method"])
        self.assertEqual("2025-01-01", plan["operation"]["api_version"])
        self.assertEqual("ca-lex-web--older", plan["operation"]["revision"])
        self.assertFalse(plan["operation"]["retry"])
        self.assertFalse(plan["operation"]["traffic_change"])
        self.assertFalse(plan["operation"]["activation_change"])
        self.assertFalse(plan["operation"]["template_change"])
        self.assertFalse(plan["operation"]["configuration_change"])
        self.assertEqual(
            ["ca-lex-web--older", "ca-lex-web--newer"],
            plan["allowed_remaining_inactive_revisions"],
        )

    def test_null_or_missing_legacy_suffix_canonicalizes_from_resource_name(self):
        inventory = self.inventory()
        for revision in inventory["revisions"]:
            revision["template"]["revisionSuffix"] = None

        completed, plan = self.run_plan(inventory)

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(
            ["a", "older", "newer"],
            [item["template"]["revisionSuffix"]
             for item in plan["reviewed_inventory"]["revisions"]],
        )

        missing_inventory = copy.deepcopy(inventory)
        del missing_inventory["revisions"][1]["template"]["revisionSuffix"]
        missing_completed, missing_plan = self.run_plan(missing_inventory)
        self.assertEqual(0, missing_completed.returncode, missing_completed.stderr)
        self.assertEqual(plan, missing_plan)

        exact_completed, exact_plan = self.run_plan(self.inventory())
        self.assertEqual(0, exact_completed.returncode, exact_completed.stderr)
        self.assertEqual(exact_plan, plan)

    def test_refuses_any_authority_route_or_shape_drift(self):
        mutations = (
            lambda value: value.update(active_revisions_mode="Single"),
            lambda value: value.update(max_inactive_revisions=2),
            lambda value: value["ingress_traffic"].append(
                {"revisionName": "ca-lex-web--older", "weight": 0,
                 "latestRevision": False, "label": None}),
            lambda value: value["ingress_traffic"][0].update(weight=99),
            lambda value: value["ingress_traffic"][0].update(latestRevision=True),
            lambda value: value["ingress_traffic"][0].update(label="legacy"),
            lambda value: value["ingress_traffic"][0].update(
                revisionName="ca-lex-web--older"),
            lambda value: value["revisions"].append(
                copy.deepcopy(value["revisions"][2])),
            lambda value: value["revisions"].pop(),
            lambda value: value["revisions"][1].update(active=True),
            lambda value: value["revisions"][1].update(trafficWeight=1),
            lambda value: value["revisions"][1]["template"].update(
                revisionSuffix="wrong"),
            lambda value: value["revisions"][1]["template"].update(
                revisionSuffix=7),
            lambda value: value.update(max_inactive_revisions=True),
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                inventory = self.inventory()
                mutation(inventory)
                completed, _ = self.run_plan(inventory)
                self.assertNotEqual(0, completed.returncode)

    def test_classifies_only_unchanged_or_one_exact_reviewed_survivor(self):
        completed, plan = self.run_plan(self.inventory())
        self.assertEqual(0, completed.returncode, completed.stderr)

        completed, outcome = self.classify(plan, self.inventory())
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual({"state": "unchanged", "remaining_inactive_revision": None}, outcome)

        for removed, survivor in (("ca-lex-web--older", "ca-lex-web--newer"),
                                  ("ca-lex-web--newer", "ca-lex-web--older")):
            live = self.inventory()
            live["revisions"] = [
                item for item in live["revisions"] if item["name"] != removed
            ]
            completed, outcome = self.classify(plan, live)
            self.assertEqual(0, completed.returncode, completed.stderr)
            self.assertEqual("converged", outcome["state"])
            self.assertEqual(survivor, outcome["remaining_inactive_revision"])

    def test_classifier_refuses_template_identity_state_and_route_drift(self):
        completed, plan = self.run_plan(self.inventory())
        self.assertEqual(0, completed.returncode, completed.stderr)
        mutations = (
            lambda value: value["revisions"][0]["template"].update(
                scale={"minReplicas": 1}),
            lambda value: value["revisions"][1].update(active=True),
            lambda value: value["revisions"][2].update(trafficWeight=1),
            lambda value: value["ingress_traffic"][0].update(label="drift"),
            lambda value: value.update(max_inactive_revisions=2),
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                live = self.inventory()
                mutation(live)
                completed, _ = self.classify(plan, live)
                self.assertNotEqual(0, completed.returncode)

        plan["operation"]["revision"] = "ca-lex-web--newer"
        completed, _ = self.classify(plan, self.inventory())
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
            "schema": "lex-bootstrap-legacy-recovery-inventory/1",
            "active_revisions_mode": "Multiple",
            "max_inactive_revisions": 1,
            "ingress_traffic": [
                {"revisionName": "ca-lex-web--a", "weight": 100,
                 "latestRevision": False, "label": None},
            ],
            "revisions": [
                revision("ca-lex-web--a", True, 100,
                         "2026-08-12T00:00:00+00:00", "a"),
                revision("ca-lex-web--older", False, 0,
                         "2026-08-13T00:00:00+00:00", "b"),
                revision("ca-lex-web--newer", False, 0,
                         "2026-08-14T00:00:00+00:00", "c"),
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

    @staticmethod
    def classify(plan, inventory):
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            plan_path = Path(directory) / "plan.json"
            inventory_path = Path(directory) / "inventory.json"
            plan_path.write_text(json.dumps(plan), encoding="utf-8")
            inventory_path.write_text(json.dumps(inventory), encoding="utf-8")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT), "--classify",
                 str(plan_path), str(inventory_path)],
                cwd=ROOT, text=True, capture_output=True, check=False,
            )
            outcome = json.loads(completed.stdout) if completed.returncode == 0 else None
            return completed, outcome


if __name__ == "__main__":
    unittest.main()
