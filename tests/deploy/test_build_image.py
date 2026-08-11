import json
from pathlib import Path
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "build_image.py"
RECORDED = Path(__file__).resolve().parent / "fixtures" / "acr_build_run.json"
REPOSITORY = "lex-web"
TAG = "sha-994b91eebaeb-art-b2d7429cf5f3"
DIGEST = "sha256:a23ad7a989c693567acdc73c37fad96dc86eaab6a74c76cd5e9bc54cf5093d8d"


class BuildImageTests(unittest.TestCase):
    def test_reads_the_digest_from_a_recorded_acr_build_result(self):
        # The deploy step pipes the build result straight in, so the recorded
        # run is the contract this script has to satisfy.
        completed = self.run_script(RECORDED.read_text(encoding="utf-8"))
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(DIGEST, completed.stdout.strip())

    def test_rejects_an_image_that_is_not_the_requested_tag(self):
        run = self.recorded()
        run["outputImages"][0]["tag"] = "sha-deadbeefdead-art-b2d7429cf5f3"
        completed = self.run_script(json.dumps(run))
        self.assertEqual(1, completed.returncode)
        self.assertIn("different image than requested", completed.stderr)

    def test_rejects_a_run_that_did_not_succeed(self):
        for field in ("status", "provisioningState"):
            with self.subTest(field=field):
                run = self.recorded()
                run[field] = "Failed"
                completed = self.run_script(json.dumps(run))
                self.assertEqual(1, completed.returncode)
                self.assertIn("did not succeed", completed.stderr)

    def test_rejects_more_or_fewer_than_one_output_image(self):
        for images in ([], [{"repository": REPOSITORY, "tag": TAG, "digest": DIGEST}] * 2):
            with self.subTest(count=len(images)):
                run = self.recorded()
                run["outputImages"] = images
                completed = self.run_script(json.dumps(run))
                self.assertEqual(1, completed.returncode)
                self.assertIn("exactly one output image", completed.stderr)

    def test_rejects_a_malformed_digest(self):
        run = self.recorded()
        run["outputImages"][0]["digest"] = "latest"
        completed = self.run_script(json.dumps(run))
        self.assertEqual(1, completed.returncode)
        self.assertIn("immutable image digest", completed.stderr)

    def test_rejects_an_empty_result(self):
        completed = self.run_script("")
        self.assertEqual(1, completed.returncode)
        self.assertIn("is empty", completed.stderr)

    @staticmethod
    def recorded():
        return json.loads(RECORDED.read_text(encoding="utf-8"))

    def run_script(self, payload):
        return subprocess.run(
            [sys.executable, str(SCRIPT), REPOSITORY, TAG],
            cwd=ROOT,
            input=payload,
            text=True,
            capture_output=True,
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
