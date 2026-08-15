import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
HELPER = ROOT / "scripts" / "probe-live-evaluate.py"
SCRIPT = ROOT / "scripts" / "probe-live.sh"

spec = importlib.util.spec_from_file_location("probe_live_evaluate", HELPER)
if spec is None or spec.loader is None:
    raise RuntimeError(f"cannot load live-probe helper from {HELPER}")
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


def response(value):
    return json.dumps(
        {
            "jsonrpc": "2.0",
            "id": 1,
            "result": {"content": [{"type": "text", "text": json.dumps(value)}]},
        }
    )


class ProbeLiveTests(unittest.TestCase):
    def test_plain_json_response_is_decoded(self):
        self.assertEqual({"works": 3}, probe.decode_mcp_body(response({"works": 3})))

    def test_last_sse_data_event_is_authoritative(self):
        raw = f"event: message\ndata: {response({'works': 0})}\n\ndata: {response({'works': 7})}\n\n"
        self.assertEqual({"works": 7}, probe.decode_mcp_body(raw))

    def test_exact_publishers_rejects_duplicates_and_missing_entries(self):
        eu = {"envelope": {"publisher": "eu-eurlex"}}
        lu = {"envelope": {"publisher": "lu-legilux"}}
        self.assertTrue(probe.exact_publishers([eu, lu]))
        self.assertFalse(probe.exact_publishers([eu, eu]))
        self.assertFalse(probe.exact_publishers([lu]))

    def test_nonzero_transport_fails_even_when_stdout_is_valid_json(self):
        bash = shutil.which("bash")
        git_bash = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"
        if os.name == "nt" and git_bash.exists():
            bash = str(git_bash)
        if not bash:
            self.skipTest("bash is unavailable")

        with tempfile.TemporaryDirectory() as directory:
            fake_curl = Path(directory) / "curl"
            fake_curl.write_text(
                "#!/usr/bin/env bash\n"
                f"printf '%s\\n' '{response({'works': 99})}'\n"
                "exit 28\n",
                encoding="utf-8",
                newline="\n",
            )
            fake_curl.chmod(0o755)
            curl_bin = str(fake_curl)
            if os.name == "nt":
                curl_bin = subprocess.run(
                    [bash, "-lc", f"cygpath -u '{fake_curl}'"],
                    check=True,
                    capture_output=True,
                    text=True,
                ).stdout.strip()
            environment = os.environ.copy()
            environment["CURL_BIN"] = curl_bin
            result = subprocess.run(
                [bash, str(SCRIPT), "https://probe.invalid"],
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
            )
        self.assertEqual(7, result.returncode)
        self.assertEqual(7, result.stdout.count("FAIL (transport)"))

    def test_checked_in_probe_prefers_python3_and_allows_verified_python3_fallback(self):
        script = SCRIPT.read_text(encoding="utf-8")
        self.assertIn("if python3 -c", script)
        self.assertIn("elif python -c", script)
        self.assertIn("--fail-with-body", script)
        self.assertIn('"$PYTHON" "$SCRIPT_DIR/probe-live-evaluate.py" "$expr"', script)
        self.assertNotIn("| python -c", script)


if __name__ == "__main__":
    unittest.main()
