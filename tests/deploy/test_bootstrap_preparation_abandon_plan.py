import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "bootstrap_preparation_abandon_plan.py"
HANDOFF_SCRIPT = ROOT / "scripts" / "deploy" / "bootstrap_legacy_recovery_plan.py"


class BootstrapPreparationAbandonPlanTests(unittest.TestCase):
    def test_plans_exact_active_zero_target_and_classifies_only_exact_post_state(self):
        completed, plan = self.run_plan(self.inventory())

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("lex-bootstrap-preparation-abandon-plan/1", plan["schema"])
        self.assertTrue(plan["dry_run"])
        self.assertEqual("ca-lex-web--r", plan["target"]["revision"])
        self.assertEqual("ca-lex-web--s", plan["retained_inactive"]["revision"])
        self.assertEqual({
            "method": "DEACTIVATE",
            "revision": "ca-lex-web--r",
            "traffic_change": False,
            "configuration_change": False,
            "template_change": False,
            "retry_only_after_exact_pre_read": True,
        }, plan["operation"])

        completed, state = self.classify(plan, self.inventory())
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual({"state": "target-active"}, state)

        post = self.inventory()
        post["revisions"][2]["active"] = False
        post["latest_ready_revision_name"] = "ca-lex-web--r"
        completed, state = self.classify(plan, post)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual({"state": "target-inactive"}, state)

    def test_accepts_candidate_route_and_null_or_missing_suffix(self):
        inventory = self.inventory()
        inventory["ingress_traffic"].append({
            "revisionName": "ca-lex-web--r", "weight": 0,
            "latestRevision": False, "label": None,
        })
        inventory["revisions"][0]["template"]["revisionSuffix"] = None
        del inventory["revisions"][1]["template"]["revisionSuffix"]

        completed, plan = self.run_plan(inventory)

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(2, len(plan["ingress_traffic"]))
        self.assertEqual("a", plan["reviewed_inventory"]["revisions"][0]
                         ["template"]["revisionSuffix"])
        self.assertEqual("s", plan["reviewed_inventory"]["revisions"][1]
                         ["template"]["revisionSuffix"])

    def test_refuses_authority_route_mode_chronology_and_template_drift(self):
        mutations = (
            lambda value: value.update(active_revisions_mode="Single"),
            lambda value: value.update(max_inactive_revisions=2),
            lambda value: value.update(latest_revision_name="ca-lex-web--s"),
            lambda value: value.update(latest_ready_revision_name="ca-lex-web--a"),
            lambda value: value["ingress_traffic"][0].update(weight=99),
            lambda value: value["ingress_traffic"][0].update(weight=100.0),
            lambda value: value["ingress_traffic"][0].update(latestRevision=0),
            lambda value: value["ingress_traffic"][0].update(label="drift"),
            lambda value: value["revisions"][0].update(runningState="Failed"),
            lambda value: value["revisions"][1].update(active=True),
            lambda value: value["revisions"][2].update(trafficWeight=1),
            lambda value: value["revisions"][2].update(
                createdTime="2026-08-12T12:00:00Z"),
            lambda value: value["revisions"][2]["template"].update(
                revisionSuffix="wrong"),
            lambda value: value["revisions"].pop(),
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                inventory = self.inventory()
                mutation(inventory)
                completed, _ = self.run_plan(inventory)
                self.assertNotEqual(0, completed.returncode)

    def test_classifier_refuses_any_identity_route_or_unreviewed_state_drift(self):
        completed, plan = self.run_plan(self.inventory())
        self.assertEqual(0, completed.returncode, completed.stderr)
        mutations = (
            lambda value: value["revisions"][1]["template"].update(
                scale={"minReplicas": 1}),
            lambda value: value["revisions"][1].update(image=self.image("d")),
            lambda value: value["revisions"][0].update(active=False),
            lambda value: value["revisions"][2].update(trafficWeight=2),
            lambda value: value["ingress_traffic"].append({
                "revisionName": "ca-lex-web--s", "weight": 0,
                "latestRevision": False, "label": None,
            }),
            lambda value: value.update(max_inactive_revisions=3),
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                live = self.inventory()
                mutation(live)
                completed, _ = self.classify(plan, live)
                self.assertNotEqual(0, completed.returncode)

        plan["target"]["revision"] = "ca-lex-web--s"
        completed, _ = self.classify(plan, self.inventory())
        self.assertNotEqual(0, completed.returncode)

    def test_failed_target_can_be_abandoned_then_enter_an_exact_no_write_handoff(self):
        inventory = self.inventory()
        inventory["revisions"][2]["runningState"] = "Failed"
        inventory["latest_ready_revision_name"] = "ca-lex-web--s"
        completed, plan = self.run_plan(inventory)
        self.assertEqual(0, completed.returncode, completed.stderr)

        post = copy.deepcopy(inventory)
        post["revisions"][2]["active"] = False
        completed, state = self.classify(plan, post)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual({"state": "target-inactive"}, state)

        post["schema"] = "lex-bootstrap-legacy-recovery-inventory/2"
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            source = Path(directory) / "handoff.json"
            source.write_text(json.dumps(post), encoding="utf-8")
            completed = subprocess.run(
                [sys.executable, str(HANDOFF_SCRIPT), str(source)], cwd=ROOT,
                text=True, capture_output=True, check=False,
            )
        self.assertEqual(0, completed.returncode, completed.stderr)
        handoff = json.loads(completed.stdout)
        self.assertEqual("ca-lex-web--r", handoff["latest_revision_name"])
        self.assertEqual("ca-lex-web--s", handoff["latest_ready_revision_name"])

    @classmethod
    def inventory(cls):
        return {
            "schema": "lex-bootstrap-preparation-inventory/1",
            "active_revisions_mode": "Multiple",
            "max_inactive_revisions": 1,
            "latest_revision_name": "ca-lex-web--r",
            "latest_ready_revision_name": "ca-lex-web--s",
            "ingress_traffic": [{
                "revisionName": "ca-lex-web--a", "weight": 100,
                "latestRevision": False, "label": None,
            }],
            "revisions": [
                cls.revision("ca-lex-web--a", True, 100,
                             "2026-08-12T00:00:00Z", "a", "Running"),
                cls.revision("ca-lex-web--s", False, 0,
                             "2026-08-13T00:00:00Z", "b", None),
                cls.revision("ca-lex-web--r", True, 0,
                             "2026-08-14T00:00:00Z", "c", "Running"),
            ],
        }

    @classmethod
    def revision(cls, name, active, traffic, created, marker, running):
        return {
            "name": name,
            "active": active,
            "trafficWeight": traffic,
            "runningState": running,
            "createdTime": created,
            "image": cls.image(marker),
            "template": {
                "revisionSuffix": name.split("--", 1)[1],
                "containers": [{"name": "lex", "image": cls.image(marker)}],
            },
        }

    @staticmethod
    def image(marker):
        return "crsoufien3orem.azurecr.io/lex-web@sha256:" + marker * 64

    @staticmethod
    def run_plan(inventory):
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            inventory_path = Path(directory) / "inventory.json"
            inventory_path.write_text(json.dumps(inventory), encoding="utf-8")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT), str(inventory_path)], cwd=ROOT,
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
                 str(plan_path), str(inventory_path)], cwd=ROOT,
                text=True, capture_output=True, check=False,
            )
            state = json.loads(completed.stdout) if completed.returncode == 0 else None
            return completed, state


if __name__ == "__main__":
    unittest.main()
