import json
from pathlib import Path
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "release_artifact_gate.py"
COLLECTION = "lu-legilux"
TICKET = "a" * 64
CORPUS = "b" * 40
INDEX_MANIFEST = "e" * 64
TAG = f"index-{COLLECTION}-{TICKET}"


class ReleaseArtifactGateTests(unittest.TestCase):
    def test_manifest_binds_the_signed_ticket_collection_and_corpus(self):
        manifest = self.manifest()
        passed = self.run_gate("manifest", manifest, COLLECTION, TICKET)
        self.assertEqual(0, passed.returncode, passed.stderr)
        self.assertEqual(CORPUS, passed.stdout.strip())

        replayed = self.run_gate("manifest", manifest, COLLECTION, "c" * 64)
        self.assertNotEqual(0, replayed.returncode)
        self.assertIn("queue ticket", replayed.stderr)

        wrong_corpus = self.run_gate(
            "manifest", manifest, COLLECTION, TICKET, "d" * 40)
        self.assertNotEqual(0, wrong_corpus.returncode)
        self.assertIn("corpus commit", wrong_corpus.stderr)

        benchmark_manifest = self.manifest()
        benchmark_manifest["sources"]["index_manifest_sha256"] = INDEX_MANIFEST
        self.assert_passes(
            "manifest", benchmark_manifest, COLLECTION, TICKET, CORPUS, INDEX_MANIFEST)
        wrong_index = self.run_gate(
            "manifest", benchmark_manifest, COLLECTION, TICKET, CORPUS, "f" * 64)
        self.assertNotEqual(0, wrong_index.returncode)
        self.assertIn("index manifest", wrong_index.stderr)

    def test_repository_must_have_immutable_releases_enabled(self):
        self.assert_passes("immutability", {"enabled": True})
        self.assert_fails("immutability", {"enabled": False})
        self.assert_fails("immutability", {"enabled": "true"})

    def test_release_is_final_immutable_and_bound_to_the_exact_tag_target(self):
        release = {
            "tag_name": TAG,
            "target_commitish": CORPUS,
            "draft": False,
            "prerelease": False,
            "immutable": True,
        }
        self.assert_passes("release", release, TAG, CORPUS)
        for field, value in (
            ("tag_name", f"index-{COLLECTION}-{'c' * 64}"),
            ("target_commitish", "d" * 40),
            ("draft", True),
            ("prerelease", True),
            ("immutable", False),
        ):
            with self.subTest(field=field):
                changed = release | {field: value}
                self.assert_fails("release", changed, TAG, CORPUS)

    def test_lightweight_tag_ref_targets_the_signed_corpus_commit(self):
        self.assert_passes(
            "tag-ref", {"object": {"type": "commit", "sha": CORPUS}}, CORPUS)
        self.assert_fails(
            "tag-ref", {"object": {"type": "tag", "sha": CORPUS}}, CORPUS)
        self.assert_fails(
            "tag-ref", {"object": {"type": "commit", "sha": "d" * 40}}, CORPUS)

    @staticmethod
    def manifest():
        return {
            "sources": {
                "collection": COLLECTION,
                "queue_ticket_id": TICKET,
                "corpus_commit": CORPUS,
            },
        }

    def assert_passes(self, gate, payload, *arguments):
        completed = self.run_gate(gate, payload, *arguments)
        self.assertEqual(0, completed.returncode, completed.stderr)

    def assert_fails(self, gate, payload, *arguments):
        completed = self.run_gate(gate, payload, *arguments)
        self.assertNotEqual(0, completed.returncode)

    @staticmethod
    def run_gate(gate, payload, *arguments):
        return subprocess.run(
            [sys.executable, str(SCRIPT), gate, *arguments],
            cwd=ROOT,
            input=json.dumps(payload, separators=(",", ":")),
            text=True,
            capture_output=True,
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
