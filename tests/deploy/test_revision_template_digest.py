import copy
import json
from pathlib import Path
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "revision_template_digest.py"


class RevisionTemplateDigestTests(unittest.TestCase):
    def test_revision_suffix_is_excluded_but_other_template_drift_is_material(self):
        first = self.digest(self.revision("fallback", "abc"))
        second = self.digest(self.revision("candidate", "abc"))
        drifted = self.digest(self.revision("candidate", "different"))

        self.assertEqual(first, second)
        self.assertNotEqual(first, drifted)
        self.assertRegex(first, r"^sha256:[0-9a-f]{64}$")

    def test_only_null_azure_scale_defaults_are_absent_equivalent(self):
        requested = self.revision("candidate", "abc")
        requested_digest = self.digest(requested)
        readback = copy.deepcopy(requested)
        readback["properties"]["template"]["scale"].update({
            "rules": None,
            "cooldownPeriod": None,
            "pollingInterval": None,
        })

        self.assertEqual(requested_digest, self.digest(readback))
        for name, value in (
            ("rules", []),
            ("cooldownPeriod", 0),
            ("pollingInterval", 30),
        ):
            drifted = copy.deepcopy(requested)
            drifted["properties"]["template"]["scale"][name] = value
            self.assertNotEqual(requested_digest, self.digest(drifted))

        unrelated_null = copy.deepcopy(requested)
        unrelated_null["properties"]["template"]["containers"][0]["env"][0][
            "secretRef"
        ] = None
        self.assertNotEqual(requested_digest, self.digest(unrelated_null))

    def test_accepts_a_template_or_an_arm_revision_but_refuses_invalid_json_shape(self):
        revision = self.revision("candidate", "abc")
        template = revision["properties"]["template"]
        self.assertEqual(self.digest(revision), self.digest(template))

        completed = self.run_digest({"properties": {}})
        self.assertNotEqual(0, completed.returncode)

    @staticmethod
    def revision(suffix, code):
        return {
            "properties": {
                "template": {
                    "revisionSuffix": suffix,
                    "containers": [{
                        "name": "lex-web",
                        "image": "crsoufien3orem.azurecr.io/lex-web@sha256:" + "a" * 64,
                        "resources": {"cpu": 1.0, "memory": "2Gi"},
                        "env": [{"name": "LEX_CODE_COMMIT", "value": code}],
                    }],
                    "scale": {"minReplicas": 1, "maxReplicas": 1},
                }
            }
        }

    def digest(self, value):
        completed = self.run_digest(value)
        self.assertEqual(0, completed.returncode, completed.stderr)
        return completed.stdout.strip()

    @staticmethod
    def run_digest(value):
        return subprocess.run(
            [sys.executable, str(SCRIPT)],
            cwd=ROOT,
            input=json.dumps(value),
            text=True,
            capture_output=True,
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
