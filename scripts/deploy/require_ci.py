#!/usr/bin/env python3
import json
from pathlib import Path
import re
import sys


MAXIMUM_PAYLOAD_BYTES = 2 * 1024 * 1024
FULL_SHA = re.compile(r"^[0-9a-f]{40}$")


class CiPending(ValueError):
    pass


def normalised(value):
    # The REST check-runs API reports lowercase status and conclusion values;
    # the GraphQL API reports the same states uppercase.
    return value.lower() if isinstance(value, str) else value


def require_checks(payload_path, expected_sha, required_names):
    if not FULL_SHA.fullmatch(expected_sha):
        raise ValueError("deployment commit is not a full lowercase SHA")
    payload_bytes = payload_path.read_bytes()
    if not payload_bytes:
        raise ValueError("GitHub check-runs payload is empty")
    if len(payload_bytes) > MAXIMUM_PAYLOAD_BYTES:
        raise ValueError("GitHub check-runs payload exceeds its byte limit")
    try:
        payload = json.loads(payload_bytes)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("GitHub check-runs payload is malformed") from error
    checks = payload.get("check_runs") if isinstance(payload, dict) else None
    if not isinstance(checks, list):
        raise ValueError("GitHub check-runs payload has no check_runs array")

    for name in required_names:
        matches = [
            item for item in checks
            if isinstance(item, dict)
            and item.get("name") == name
            and item.get("head_sha") == expected_sha
            and isinstance(item.get("started_at"), str)
        ]
        if not matches:
            raise CiPending(f"required CI check is not available yet for this commit: {name}")
        latest = max(matches, key=lambda item: item["started_at"])
        app = latest.get("app")
        if not isinstance(app, dict) or app.get("slug") != "github-actions":
            raise ValueError(f"required CI check has an unexpected producer: {name}")
        if normalised(latest.get("status")) != "completed":
            raise CiPending(
                f"required CI check has not completed for this commit: {name} "
                f"({latest.get('status')})")
        if normalised(latest.get("conclusion")) != "success":
            raise ValueError(
                f"required CI check did not succeed for this commit: {name} "
                f"({latest.get('status')}/{latest.get('conclusion')})")


def main():
    if len(sys.argv) < 4:
        print("usage: require_ci.py CHECK_RUNS_JSON COMMIT CHECK [CHECK ...]", file=sys.stderr)
        return 2
    try:
        require_checks(Path(sys.argv[1]), sys.argv[2], sys.argv[3:])
    except CiPending as error:
        print(str(error), file=sys.stderr)
        return 75
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    print(f"required CI checks passed for {sys.argv[2]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
