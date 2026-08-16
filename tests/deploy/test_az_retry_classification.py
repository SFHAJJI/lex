"""az_retry retries the conflicts Azure tells us to retry, and nothing else.

Three deploys failed on ContainerAppOperationInProgress: Azure refuses to modify a container app
while one of its own operations is still settling, and says so in the error. The helper already
retried the sibling write conflict, so the candidate creation was one pattern short of surviving a
condition that clears on its own within seconds.

The negative cases matter as much: a helper that retried everything would turn a permissions or
validation failure into a slow permissions or validation failure.
"""

import os
import shutil
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HELPER = ROOT / "scripts" / "deploy" / "az-retry.sh"

RETRYABLE = [
    'ERROR: Conflict({"error":{"code":"ContainerAppOperationInProgress","message":'
    '"Cannot modify a container app \'ca-lex-web\' because there is an active provisioning '
    "operation in progress. OperationId: '2cb8567f-21a1-487f-977d-3debde280c38'.\"}})",
    "ERROR: (ConflictingConcurrentWriteNotAllowed) The operation was interrupted by a "
    "conflicting concurrent write on the same entity. Please retry later.",
    "ERROR: (TooManyRequests) rate limited",
    "ERROR: (ServiceUnavailable) try again",
]

FATAL = [
    "ERROR: (AuthorizationFailed) The client does not have authorization to perform action",
    "ERROR: (InvalidParameterValue) The following field is invalid: template.containers.image",
    "ERROR: (ResourceNotFound) The Resource 'Microsoft.App/containerApps/ca-lex-web' was not found",
]


def run(message: str, fail_times: int) -> subprocess.CompletedProcess:
    """Run a command through az_retry that fails `fail_times` times, then succeeds."""
    script = f"""
    set -u
    . "{HELPER.as_posix()}"
    attempts_file=$(mktemp)
    printf '0' > "$attempts_file"
    fake() {{
      count=$(cat "$attempts_file")
      count=$((count + 1))
      printf '%s' "$count" > "$attempts_file"
      if [ "$count" -le {fail_times} ]; then
        printf '%s\n' "$AZ_FAKE_MESSAGE" >&2
        return 1
      fi
      printf 'ok\n'
    }}
    az_retry fake
    status=$?
    printf 'attempts=%s exit=%s\n' "$(cat "$attempts_file")" "$status"
    """
    bash = shutil.which("bash")
    return subprocess.run(
        [bash, "-c", script], capture_output=True, text=True, timeout=180,
        # A real backoff starts at ten seconds and doubles. The classification is what is under
        # test, not the wait, so the delay is configured down rather than slept through.
        env={**os.environ, "AZ_FAKE_MESSAGE": message, "AZ_RETRY_BASE_DELAY": "0"})


class AzRetryClassificationTests(unittest.TestCase):
    def test_a_condition_azure_calls_transient_is_retried_until_it_clears(self):
        for message in RETRYABLE:
            with self.subTest(message=message[:60]):
                result = run(message, fail_times=1)
                self.assertIn("exit=0", result.stdout, result.stderr)
                self.assertIn("attempts=2", result.stdout, result.stderr)

    def test_a_condition_that_will_not_clear_stops_on_its_first_attempt(self):
        for message in FATAL:
            with self.subTest(message=message[:60]):
                result = run(message, fail_times=1)
                self.assertNotIn("exit=0", result.stdout, result.stderr)
                self.assertIn("attempts=1", result.stdout, result.stderr)

    def test_the_helper_names_the_container_app_conflict_explicitly(self):
        # The message Azure returns is the only documentation of this condition, so the pattern is
        # pinned here rather than left to a future reader to rediscover from a failed deploy.
        text = HELPER.read_text(encoding="utf-8")
        self.assertIn("ContainerAppOperationInProgress", text)
        self.assertIn("active provisioning operation in progress", text)


if __name__ == "__main__":
    unittest.main()
