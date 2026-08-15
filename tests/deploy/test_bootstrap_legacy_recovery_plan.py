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
    def test_exact_a_plus_two_plan_is_a_no_mutation_deploy_handoff(self):
        completed, plan = self.run_plan(self.inventory())

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("lex-bootstrap-legacy-recovery-plan/2", plan["schema"])
        self.assertTrue(plan["dry_run"])
        self.assertEqual("Multiple", plan["active_revisions_mode"])
        self.assertEqual(1, plan["max_inactive_revisions"])
        self.assertEqual(self.routes(), plan["ingress_traffic"])
        self.assertEqual("ca-lex-web--newer", plan["latest_revision_name"])
        self.assertEqual("ca-lex-web--newer", plan["latest_ready_revision_name"])
        self.assertEqual({
            "method": "NONE",
            "traffic_change": False,
            "activation_change": False,
            "template_change": False,
            "configuration_change": False,
        }, plan["operation"])
        self.assertEqual("ca-lex-web--older",
                         plan["handoff"]["first_pruned_inactive"]["revision"])
        self.assertEqual("ca-lex-web--newer",
                         plan["handoff"]["retained_until_candidate"]["revision"])
        self.assertLess(
            plan["handoff"]["first_pruned_inactive"]["created_time"],
            plan["handoff"]["retained_until_candidate"]["created_time"],
        )
        for item in plan["handoff"].values():
            self.assertRegex(item["canonical_template_digest"], r"^sha256:[0-9a-f]{64}$")

    def test_null_or_missing_legacy_suffix_canonicalizes_from_resource_name(self):
        inventory = self.inventory()
        for revision in inventory["revisions"]:
            revision["template"]["revisionSuffix"] = None

        completed, plan = self.run_plan(inventory)
        self.assertEqual(0, completed.returncode, completed.stderr)

        missing = copy.deepcopy(inventory)
        del missing["revisions"][1]["template"]["revisionSuffix"]
        missing_completed, missing_plan = self.run_plan(missing)
        self.assertEqual(0, missing_completed.returncode, missing_completed.stderr)
        self.assertEqual(plan, missing_plan)

        exact_completed, exact_plan = self.run_plan(self.inventory())
        self.assertEqual(0, exact_completed.returncode, exact_completed.stderr)
        self.assertEqual(exact_plan, plan)

    def test_refuses_raw_mode_limit_route_authority_and_shape_drift(self):
        mutations = (
            lambda value: value.update(active_revisions_mode="Single"),
            lambda value: value.update(max_inactive_revisions=99),
            lambda value: value.update(latest_revision_name="ca-lex-web--older"),
            lambda value: value["ingress_traffic"].append(
                {"revisionName": "ca-lex-web--older", "weight": 0,
                 "latestRevision": False, "label": None}),
            lambda value: value["ingress_traffic"][0].update(weight=99),
            lambda value: value["ingress_traffic"][0].update(weight=100.0),
            lambda value: value["ingress_traffic"][0].update(latestRevision=True),
            lambda value: value["ingress_traffic"][0].update(latestRevision=0),
            lambda value: value["ingress_traffic"][0].update(label="legacy"),
            lambda value: value["revisions"].pop(),
            lambda value: value["revisions"][0].update(runningState="Failed"),
            lambda value: value["revisions"][1].update(active=True),
            lambda value: value["revisions"][1].update(trafficWeight=1),
            lambda value: value["revisions"][1]["template"].update(
                revisionSuffix="wrong"),
            lambda value: value.update(max_inactive_revisions=True),
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                inventory = self.inventory()
                mutation(inventory)
                completed, _ = self.run_plan(inventory)
                self.assertNotEqual(0, completed.returncode)

    def test_full_reviewed_inventory_domain_separates_handoff(self):
        completed, first = self.run_plan(self.inventory())
        self.assertEqual(0, completed.returncode, completed.stderr)
        changed = self.inventory()
        changed["revisions"][1]["template"]["scale"] = {"minReplicas": 0}

        completed, second = self.run_plan(changed)

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertNotEqual(first["inventory_sha256"], second["inventory_sha256"])
        self.assertNotEqual(first["handoff"]["first_pruned_inactive"]
                            ["canonical_template_digest"],
                            second["handoff"]["first_pruned_inactive"]
                            ["canonical_template_digest"])

    def test_binds_older_inactive_as_latest_ready_after_failed_active_zero_abandon(self):
        inventory = self.inventory()
        inventory["latest_ready_revision_name"] = "ca-lex-web--older"

        completed, plan = self.run_plan(inventory)

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("ca-lex-web--newer", plan["latest_revision_name"])
        self.assertEqual("ca-lex-web--older", plan["latest_ready_revision_name"])

        invalid = self.inventory()
        invalid["latest_ready_revision_name"] = "ca-lex-web--a"
        completed, _ = self.run_plan(invalid)
        self.assertNotEqual(0, completed.returncode)

    @staticmethod
    def routes():
        return [{"revisionName": "ca-lex-web--a", "weight": 100,
                 "latestRevision": False, "label": None}]

    @classmethod
    def inventory(cls):
        def revision(name, active, traffic, created, marker, running=None):
            image = "crsoufien3orem.azurecr.io/lex-web@sha256:" + marker * 64
            return {
                "name": name,
                "active": active,
                "trafficWeight": traffic,
                "runningState": running,
                "createdTime": created,
                "image": image,
                "template": {
                    "revisionSuffix": name.split("--", 1)[1],
                    "containers": [{"name": "lex-web", "image": image}],
                },
            }

        return {
            "schema": "lex-bootstrap-legacy-recovery-inventory/2",
            "active_revisions_mode": "Multiple",
            "max_inactive_revisions": 1,
            "latest_revision_name": "ca-lex-web--newer",
            "latest_ready_revision_name": "ca-lex-web--newer",
            "ingress_traffic": cls.routes(),
            "revisions": [
                revision("ca-lex-web--a", True, 100,
                         "2026-08-12T00:00:00+00:00", "a", "Running"),
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


if __name__ == "__main__":
    unittest.main()
