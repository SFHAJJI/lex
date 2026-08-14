#!/usr/bin/env python3
"""Select the newest successful, exact production release-state deployment."""

import argparse
from datetime import datetime, timezone
import json
import re
import subprocess
import sys


UTC_INSTANT = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")
STATES = {
    "none",
    "error",
    "failure",
    "inactive",
    "in_progress",
    "queued",
    "pending",
    "success",
}
FIELDS = {
    "id",
    "created_at",
    "task",
    "environment",
    "production_environment",
    "latest_status",
}
REPOSITORY = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")


def fail(message):
    raise ValueError(message)


def parse_instant(value):
    if not isinstance(value, str) or not UTC_INSTANT.fullmatch(value):
        fail("deployment created_at is not an exact UTC instant")
    try:
        return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(
            tzinfo=timezone.utc
        )
    except ValueError as error:
        raise ValueError("deployment created_at is invalid") from error


def select(records):
    if not 1 <= len(records) <= 500:
        fail("bounded deployment history must contain between one and 500 records")
    seen = set()
    previous_key = None
    successes = []
    for index, record in enumerate(records):
        if not isinstance(record, dict) or set(record) != FIELDS:
            fail("deployment ledger record fields are not exact")
        deployment_id = record["id"]
        if isinstance(deployment_id, bool) or not isinstance(deployment_id, int) \
                or deployment_id <= 0 or deployment_id in seen:
            fail("deployment ledger id is invalid or duplicated")
        seen.add(deployment_id)
        created_at = parse_instant(record["created_at"])
        key = (created_at, deployment_id)
        if previous_key is not None and key >= previous_key:
            fail("deployment ledger is not strictly newest first")
        previous_key = key
        if record["task"] != "lex-revision-promotion" \
                or record["environment"] != "production" \
                or record["production_environment"] is not True:
            fail("deployment ledger contains a non-production release-state record")
        state = record["latest_status"]
        if not isinstance(state, str) or state not in STATES:
            fail("deployment ledger status is invalid")
        if state == "success":
            successes.append((index, deployment_id))
    if len(successes) != 1 or successes[0][0] != len(records) - 1:
        fail("ledger scan must stop at exactly the newest successful deployment")
    return successes[0][1]


def gh_api(endpoint):
    try:
        result = subprocess.run(
            [
                "gh",
                "api",
                "-H",
                "Accept: application/vnd.github+json",
                "-H",
                "X-GitHub-Api-Version: 2022-11-28",
                endpoint,
            ],
            text=True,
            capture_output=True,
            check=False,
            timeout=30,
        )
    except subprocess.TimeoutExpired as error:
        raise ValueError("GitHub deployment ledger read timed out") from error
    if result.returncode != 0:
        fail("GitHub deployment ledger read failed")
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise ValueError("GitHub deployment ledger response is malformed") from error


def resolve(repository, request=gh_api):
    if not isinstance(repository, str) or not REPOSITORY.fullmatch(repository):
        fail("GITHUB_REPOSITORY is invalid")
    records = []
    seen = set()
    previous_key = None
    for page in range(1, 6):
        deployments = request(
            f"repos/{repository}/deployments?environment=production"
            f"&task=lex-revision-promotion&per_page=100&page={page}"
        )
        if not isinstance(deployments, list) or len(deployments) > 100:
            fail("deployment page is malformed")
        for deployment in deployments:
            if not isinstance(deployment, dict):
                fail("deployment page contains a malformed record")
            deployment_id = deployment.get("id")
            if isinstance(deployment_id, bool) or not isinstance(deployment_id, int) \
                    or deployment_id <= 0 or deployment_id in seen:
                fail("deployment page id is invalid or duplicated")
            seen.add(deployment_id)
            key = (parse_instant(deployment.get("created_at")), deployment_id)
            if previous_key is not None and key >= previous_key:
                fail("deployment pages are not strictly newest first")
            previous_key = key
            if deployment.get("task") != "lex-revision-promotion" \
                    or deployment.get("environment") != "production" \
                    or deployment.get("production_environment") is not True:
                fail("deployment page contains a non-production release-state record")
        for deployment in deployments:
            deployment_id = deployment["id"]
            statuses = request(
                f"repos/{repository}/deployments/{deployment_id}/statuses?per_page=1"
            )
            if not isinstance(statuses, list) or len(statuses) > 1 \
                    or any(not isinstance(status, dict) for status in statuses):
                fail("deployment status response is malformed")
            state = statuses[0].get("state") if statuses else "none"
            records.append(
                {
                    "id": deployment_id,
                    "created_at": deployment.get("created_at"),
                    "task": deployment.get("task"),
                    "environment": deployment.get("environment"),
                    "production_environment": deployment.get("production_environment"),
                    "latest_status": state,
                }
            )
            if state == "success":
                return select(records)
        if len(deployments) < 100:
            break
    fail("no success in bounded deployment history")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("records", nargs="?")
    parser.add_argument("--repository")
    args = parser.parse_args()
    if bool(args.records) == bool(args.repository):
        fail("exactly one ledger input mode is required")
    if args.repository:
        print(resolve(args.repository))
        return
    records = []
    with open(args.records, encoding="utf-8") as stream:
        for line in stream:
            if line.strip():
                records.append(json.loads(line))
    print(select(records))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"release ledger head refused: {error}", file=sys.stderr)
        raise SystemExit(2)
