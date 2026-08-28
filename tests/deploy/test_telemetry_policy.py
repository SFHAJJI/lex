import copy
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "telemetry_policy.py"
POLICY = ROOT / "deploy" / "telemetry-policy.json"


class TelemetryPolicyTests(unittest.TestCase):
    def test_source_policy_contains_only_safe_names_booleans_counts_and_signals(self):
        policy = json.loads(POLICY.read_text(encoding="utf-8"))
        serialized = POLICY.read_text(encoding="utf-8").casefold()
        for forbidden in ("resource_id", "customer_id", "connection_string", "endpoint",
                          "instrumentation_key", "shared_key", "/subscriptions/"):
            self.assertNotIn(forbidden, serialized)
        self.assertIsNone(re.search(
            r'\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b',
            serialized))

        self.assertEqual(False,
                         policy["environment"]["dapr_application_insights_enabled"])
        self.assertEqual({
            "app_insights_destination_enabled": False,
            "data_dog_destination_enabled": False,
            "otlp_destination_count": 0,
            "trace_destinations": [],
            "log_destinations": [],
            "metric_destinations": [],
        }, policy["environment"]["managed_open_telemetry"])
        self.assertEqual(False, policy["container_app"]["dapr_enabled"])
        self.assertEqual(False, policy["container_app"]["dapr_api_logging_enabled"])
        self.assertEqual("managed-ai-rg", policy["workspaces"][1]["resource_group_name"])

    def test_accepts_the_exact_projected_readback(self):
        completed = self.run_script(self.readback())
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("telemetry policy matched\n", completed.stdout)
        self.assertEqual("", completed.stderr)

    def test_rejects_every_sensitive_platform_drift(self):
        mutations = {
            "environment destination": lambda item: item["environment"].update(
                log_destination="azure-monitor"),
            "environment workspace": lambda item: item["environment"].update(
                workspace_linked=False),
            "environment diagnostic setting": lambda item: item["environment"].update(
                diagnostic_setting_count=1),
            "environment diagnostic shape": lambda item: item["environment"].update(
                diagnostic_settings_type="string"),
            "environment missing diagnostic array": lambda item: item["environment"].update(
                diagnostic_settings_type="null"),
            "Dapr Application Insights": lambda item: item["environment"].update(
                dapr_application_insights_enabled=True),
            "managed trace export": lambda item: item["environment"][
                "managed_open_telemetry"].update(trace_destinations=["appInsights"]),
            "managed Application Insights destination": lambda item: item["environment"][
                "managed_open_telemetry"].update(app_insights_destination_enabled=True),
            "managed OTLP destination shape": lambda item: item["environment"][
                "managed_open_telemetry"].update(otlp_destinations_type="object"),
            "container app diagnostic setting": lambda item: item["container_app"].update(
                diagnostic_setting_count=1),
            "container app Dapr": lambda item: item["container_app"].update(
                dapr_enabled=True),
            "container app Dapr API logging": lambda item: item["container_app"].update(
                dapr_api_logging_enabled=True),
            "application insights diagnostic setting": lambda item: item[
                "application_insights"].update(diagnostic_setting_count=1),
            "IP masking disabled": lambda item: item["application_insights"].update(
                ip_masking_enabled=False),
            "application insights workspace": lambda item: item[
                "application_insights"].update(workspace_linked=False),
            "HTTP category missing": lambda item: item["environment"][
                "diagnostic_categories"].remove("ContainerAppHTTPLogs"),
            "legacy workspace resource-only disabled": lambda item: item["workspaces"][
                0].update(resource_only_permissions=False),
            "AI workspace resource-only disabled": lambda item: item["workspaces"][
                1].update(resource_only_permissions=False),
            "AI workspace resource group": lambda item: item["workspaces"][1].update(
                resource_group_name="unexpected-managed-group"),
            "workspace name": lambda item: item["workspaces"][0].update(
                name="unexpected-workspace"),
            "missing field": lambda item: item["container_app"].pop("name"),
            "unknown field": lambda item: item["application_insights"].update(
                unexpected="value"),
        }
        for name, mutate in mutations.items():
            with self.subTest(name=name):
                payload = copy.deepcopy(self.readback())
                mutate(payload)
                completed = self.run_script(payload)
                self.assertNotEqual(0, completed.returncode)
                self.assertNotIn("00000000-0000-0000-0000-000000000000",
                                 completed.stdout + completed.stderr)

    def test_rejects_malformed_or_unparseable_readback_without_echoing_it(self):
        sentinel = "SECRET_SENTINEL_D892"
        with tempfile.TemporaryDirectory() as directory:
            readback = Path(directory) / "readback.json"
            readback.write_text(f'{{"{sentinel}":', encoding="utf-8")
            completed = subprocess.run(
                [sys.executable, str(SCRIPT), str(POLICY), str(readback)],
                cwd=ROOT, text=True, capture_output=True, check=False)
        self.assertEqual(2, completed.returncode)
        self.assertNotIn(sentinel, completed.stdout + completed.stderr)
        self.assertIn("malformed", completed.stderr)

    def test_rejects_a_secret_bearing_unknown_field_without_echoing_it(self):
        sentinel = "SECRET_SENTINEL_A73E"
        payload = self.readback()
        payload["environment"]["shared_key"] = sentinel
        completed = self.run_script(payload)
        self.assertNotEqual(0, completed.returncode)
        self.assertNotIn(sentinel, completed.stdout + completed.stderr)

    def test_rejects_duplicate_keys_and_nonfinite_constants_as_malformed(self):
        documents = {
            "policy": json.dumps(json.loads(POLICY.read_text(encoding="utf-8")),
                                 separators=(",", ":")),
            "readback": json.dumps(self.readback(), separators=(",", ":")),
        }
        for document, good in documents.items():
            cases = {
                "duplicate key": good.replace(
                    '"schema":', '"schema":"SECRET_SENTINEL_9B31","schema":', 1),
                "NaN": good.replace('"diagnostic_setting_count":0',
                                    '"diagnostic_setting_count":NaN', 1),
                "Infinity": good.replace('"diagnostic_setting_count":0',
                                         '"diagnostic_setting_count":Infinity', 1),
                "negative Infinity": good.replace('"diagnostic_setting_count":0',
                                                  '"diagnostic_setting_count":-Infinity', 1),
            }
            for name, raw in cases.items():
                with self.subTest(document=document, name=name):
                    if document == "policy":
                        completed = self.run_raw(documents["readback"], raw)
                    else:
                        completed = self.run_raw(raw)
                    self.assertEqual(2, completed.returncode)
                    self.assertIn("malformed", completed.stderr)
                    self.assertNotIn("SECRET_SENTINEL_9B31",
                                     completed.stdout + completed.stderr)

    def test_rejects_overflowed_floats_at_any_depth_as_malformed(self):
        documents = {
            "policy": json.dumps(json.loads(POLICY.read_text(encoding="utf-8")),
                                 separators=(",", ":")),
            "readback": json.dumps(self.readback(), separators=(",", ":")),
        }
        for document, good in documents.items():
            for container in ('{"value":1e400}', '[1e400]'):
                with self.subTest(document=document, container=container[0]):
                    raw = good.replace('"schema":', f'"overflow":{container},"schema":', 1)
                    if document == "policy":
                        completed = self.run_raw(documents["readback"], raw)
                    else:
                        completed = self.run_raw(raw)
                    self.assertEqual(2, completed.returncode)
                    self.assertIn("malformed", completed.stderr)

    def test_rejects_missing_or_wrong_typed_workspace_group_without_traceback(self):
        for value in (None, 42):
            with self.subTest(value=value):
                policy = json.loads(POLICY.read_text(encoding="utf-8"))
                policy["workspaces"][1]["resource_group_name"] = value
                completed = self.run_raw(
                    json.dumps(self.readback(), separators=(",", ":")),
                    json.dumps(policy, separators=(",", ":")))
                self.assertEqual(1, completed.returncode)
                self.assertIn("telemetry policy mismatch", completed.stderr)
                self.assertNotIn("Traceback", completed.stderr)

    @staticmethod
    def readback():
        return {
            "schema": "lex-telemetry-readback/1",
            "environment": {
                "resource_group_name": "rg-platform",
                "name": "cae-platform-law",
                "log_destination": "log-analytics",
                "workspace_linked": True,
                "diagnostic_setting_count": 0,
                "diagnostic_settings_type": "array",
                "diagnostic_categories_type": "array",
                "diagnostic_categories": [
                    "ContainerAppConsoleLogs",
                    "ContainerAppHTTPLogs",
                    "ContainerAppSystemLogs",
                ],
                "dapr_application_insights_enabled": False,
                "managed_open_telemetry": {
                    "app_insights_destination_enabled": False,
                    "data_dog_destination_enabled": False,
                    "otlp_destinations_type": "null",
                    "otlp_destination_count": 0,
                    "trace_destinations": [],
                    "log_destinations": [],
                    "metric_destinations": [],
                },
            },
            "container_app": {
                "resource_group_name": "rg-platform",
                "name": "ca-lex-web",
                "diagnostic_setting_count": 0,
                "diagnostic_settings_type": "array",
                "dapr_enabled": False,
                "dapr_api_logging_enabled": False,
            },
            "application_insights": {
                "resource_group_name": "rg-platform",
                "name": "ai-lex-web",
                "workspace_name": "managed-ai-lex-web-ws",
                "workspace_linked": True,
                "ip_masking_enabled": True,
                "diagnostic_setting_count": 0,
                "diagnostic_settings_type": "array",
            },
            "workspaces": [
                {
                    "purpose": "container_apps",
                    "resource_group_name": "rg-enercop-dev",
                    "name": "law-enercop-dev",
                    "resource_only_permissions": True,
                },
                {
                    "purpose": "application_insights",
                    "resource_group_name": "managed-ai-rg",
                    "name": "managed-ai-lex-web-ws",
                    "resource_only_permissions": True,
                },
            ],
        }

    @staticmethod
    def run_script(payload):
        return TelemetryPolicyTests.run_raw(
            json.dumps(payload, separators=(",", ":")))

    @staticmethod
    def run_raw(raw, policy_raw=None):
        with tempfile.TemporaryDirectory() as directory:
            policy = POLICY
            if policy_raw is not None:
                policy = Path(directory) / "policy.json"
                policy.write_text(policy_raw, encoding="utf-8")
            readback = Path(directory) / "readback.json"
            readback.write_text(raw, encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(SCRIPT), str(policy), str(readback)],
                cwd=ROOT, text=True, capture_output=True, check=False)


if __name__ == "__main__":
    unittest.main()
