import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "release_authorization.py"
SUBSCRIPTION = "11111111-1111-1111-1111-111111111111"
TENANT = "22222222-2222-2222-2222-222222222222"
APP = (
    f"/subscriptions/{SUBSCRIPTION}/resourceGroups/rg-platform/"
    "providers/Microsoft.App/containerApps/ca-lex-web"
)
CURRENT = "ca-lex-web--current"
TARGET = "ca-lex-web--target"
RELEASE_CURRENT = "assistant-eval-aaaaaaaaaaaa-bbbbbbbbbbbb"
RELEASE_TARGET = "assistant-eval-cccccccccccc-dddddddddddd"
DIGEST_A = "sha256:" + "a" * 64
DIGEST_B = "sha256:" + "b" * 64


class ReleaseAuthorizationTests(unittest.TestCase):
    def test_first_release_receipt_authorizes_exact_current_and_equivalent_fallback(self):
        receipt = self.first_release()

        result = self.run_helper(receipt, "rollback", RELEASE_CURRENT)

        self.assertEqual(0, result.returncode, result.stderr)
        normalized = json.loads(result.stdout)
        self.assertEqual("exact_revision_evaluation", normalized["current_authorization"]["kind"])
        self.assertEqual(
            "equivalent_first_release_fallback", normalized["target_authorization"]["kind"]
        )
        self.assertEqual("42", normalized["target_authorization"]["source_deployment_id"])

    def test_schema_three_rollback_swaps_exact_revision_authorities(self):
        receipt = self.release_state()

        result = self.run_helper(receipt, "rollback", RELEASE_TARGET)

        self.assertEqual(0, result.returncode, result.stderr)
        normalized = json.loads(result.stdout)
        self.assertEqual(RELEASE_TARGET, normalized["target_authorization"]["evidence_release"])
        self.assertEqual(
            RELEASE_CURRENT,
            normalized["new_rollback_authorization"]["evidence_release"],
        )

    def test_promotion_carries_current_authority_and_creates_exact_target_authority(self):
        receipt = self.release_state()

        result = self.run_helper(receipt, "promote", RELEASE_TARGET, target="ca-lex-web--new")

        self.assertEqual(0, result.returncode, result.stderr)
        normalized = json.loads(result.stdout)
        self.assertEqual(RELEASE_TARGET, normalized["target_authorization"]["evidence_release"])
        self.assertEqual(RELEASE_CURRENT, normalized["new_rollback_authorization"]["evidence_release"])

    def test_cross_resource_stale_or_wrong_evaluation_receipts_are_rejected(self):
        cases = []
        cross_app = self.release_state()
        cross_app["payload"]["container_app_resource_id"] = APP + "-other"
        cases.append((cross_app, "rollback", RELEASE_TARGET))
        stale = self.release_state()
        stale["payload"]["target_revision"] = "ca-lex-web--stale"
        cases.append((stale, "rollback", RELEASE_TARGET))
        cases.append((self.release_state(), "rollback", RELEASE_CURRENT))
        tampered = self.release_state()
        tampered["payload"]["rollback_authorization"]["signed_package_sha256"] = "bad"
        cases.append((tampered, "rollback", RELEASE_TARGET))
        mismatched_current_evidence = self.release_state()
        mismatched_current_evidence["payload"]["assistant_evaluation_release"] = RELEASE_TARGET
        cases.append((mismatched_current_evidence, "rollback", RELEASE_TARGET))

        for receipt, operation, release in cases:
            with self.subTest(receipt=receipt, release=release):
                self.assertNotEqual(0, self.run_helper(receipt, operation, release).returncode)

    def test_historical_fallback_must_return_to_exact_release_before_promotion(self):
        receipt = self.release_state()
        receipt["payload"]["target_authorization"] = self.auth(
            "equivalent_first_release_fallback",
            RELEASE_CURRENT,
            "41",
            "c" * 64,
        )

        result = self.run_helper(
            receipt, "promote", RELEASE_TARGET, target="ca-lex-web--new"
        )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("must return to its exact evaluated release", result.stderr)

    def run_helper(self, receipt, operation, evaluation_release, target=TARGET):
        with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
            path = Path(temporary) / "receipt.json"
            path.write_text(json.dumps(receipt), encoding="utf-8")
            return subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    str(path),
                    "--operation",
                    operation,
                    "--current",
                    CURRENT,
                    "--target",
                    target,
                    "--evaluation-release",
                    evaluation_release,
                    "--container-app-resource-id",
                    APP,
                    "--tenant-id",
                    TENANT,
                    "--subscription-id",
                    SUBSCRIPTION,
                ],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )

    @staticmethod
    def auth(kind, release, source=None, package=None):
        return {
            "kind": kind,
            "evidence_release": release,
            "source_deployment_id": source,
            "signed_package_sha256": package,
        }

    def release_state(self):
        return {
            "id": 42,
            "task": "lex-revision-promotion",
            "environment": "production",
            "production_environment": True,
            "payload": {
                "schema": "lex-release-state-receipt/3",
                "purpose": "record-audited-revision-transition",
                "azure_tenant_id": TENANT,
                "azure_subscription_id": SUBSCRIPTION,
                "container_app_resource_id": APP,
                "target_revision": CURRENT,
                "target_revision_resource_id": f"{APP}/revisions/{CURRENT}",
                "target_image": DIGEST_A,
                "rollback_revision": TARGET,
                "rollback_revision_resource_id": f"{APP}/revisions/{TARGET}",
                "rollback_image": DIGEST_B,
                "assistant_evaluation_release": RELEASE_CURRENT,
                "target_authorization": self.auth(
                    "exact_revision_evaluation", RELEASE_CURRENT
                ),
                "rollback_authorization": self.auth(
                    "exact_revision_evaluation", RELEASE_TARGET
                ),
            },
        }

    def first_release(self):
        signed = {
            "schema": "lex-first-release-receipt/1",
            "purpose": "authorize-equivalent-first-release-fallback",
            "rollback_kind": "equivalent_first_release_fallback",
            "azure_tenant_id": TENANT,
            "azure_subscription_id": SUBSCRIPTION,
            "container_app_resource_id": APP,
            "target_revision": CURRENT,
            "target_revision_resource_id": f"{APP}/revisions/{CURRENT}",
            "target_image": DIGEST_A,
            "rollback_revision": TARGET,
            "rollback_revision_resource_id": f"{APP}/revisions/{TARGET}",
            "rollback_image": DIGEST_A,
            "assistant_evaluation_release": RELEASE_CURRENT,
            "bootstrap_package_sha256": "c" * 64,
        }
        return {
            "id": 42,
            "task": "lex-revision-promotion",
            "environment": "production",
            "production_environment": True,
            "payload": {"signed_receipt": signed},
        }


if __name__ == "__main__":
    unittest.main()
