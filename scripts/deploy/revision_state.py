#!/usr/bin/env python3
"""Verify one exact Container Apps revision state from a JSON snapshot."""

import argparse
from datetime import datetime
import json
import sys


def names(value):
    return {item for item in value.split(",") if item}


def weights(value):
    parsed = {}
    for item in filter(None, value.split(",")):
        name, separator, weight = item.partition("=")
        if not separator or not name or name in parsed:
            raise ValueError("traffic must contain unique REVISION=WEIGHT entries")
        parsed[name] = int(weight)
    return parsed


def ordered_names(value):
    parsed = [item for item in value.split(",") if item]
    if len(parsed) != len(set(parsed)):
        raise ValueError("created order must contain unique revision names")
    return parsed


def created_at(value):
    if not isinstance(value, str) or not value.endswith("Z"):
        raise ValueError("createdTime must be an explicit UTC instant")
    parsed = datetime.fromisoformat(value[:-1] + "+00:00")
    return parsed


def verify(
    snapshot,
    expected_limit,
    expected_active,
    expected_inactive,
    expected_traffic,
    expected_created_order=(),
):
    if expected_limit not in (1, 2, 3):
        raise ValueError("inactive revision limit must be one of the bounded states 1, 2, or 3")
    if snapshot.get("max_inactive_revisions") != expected_limit:
        raise ValueError("inactive revision limit differs from the expected bounded state")
    revisions = snapshot.get("revisions")
    if not isinstance(revisions, list):
        raise ValueError("revision inventory is required")

    by_name = {}
    for revision in revisions:
        name = revision.get("name")
        active = revision.get("active")
        traffic = revision.get("trafficWeight", 0)
        if not isinstance(name, str) or not name or name in by_name:
            raise ValueError("revision names must be present and unique")
        if (not isinstance(active, bool) or type(traffic) is not int
                or not 0 <= traffic <= 100):
            raise ValueError("revision active and traffic fields are invalid")
        by_name[name] = revision

    active = {name for name, revision in by_name.items() if revision["active"]}
    inactive = set(by_name) - active
    traffic = {
        name: revision.get("trafficWeight", 0)
        for name, revision in by_name.items()
        if revision.get("trafficWeight", 0) > 0
    }
    if active != expected_active:
        raise ValueError("active revision identities differ from the expected state")
    if inactive != expected_inactive:
        raise ValueError("inactive revision identities differ from the expected state")
    if traffic != expected_traffic:
        raise ValueError("traffic identities or weights differ from the expected state")
    if len(inactive) > expected_limit:
        raise ValueError("inactive revision inventory exceeds its configured limit")

    if expected_created_order:
        missing = set(expected_created_order) - set(by_name)
        if missing:
            raise ValueError("created order references an unknown revision")
        timestamps = [created_at(by_name[name].get("createdTime")) for name in expected_created_order]
        if any(older >= newer for older, newer in zip(timestamps, timestamps[1:])):
            raise ValueError("revision createdTime order is not strictly increasing")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--max-inactive", type=int, required=True)
    parser.add_argument("--active", required=True)
    parser.add_argument("--inactive", required=True)
    parser.add_argument("--traffic", required=True)
    parser.add_argument("--created-order", default="")
    args = parser.parse_args()
    snapshot = json.load(sys.stdin)
    verify(
        snapshot,
        args.max_inactive,
        names(args.active),
        names(args.inactive),
        weights(args.traffic),
        ordered_names(args.created_order),
    )


if __name__ == "__main__":
    try:
        main()
    except (json.JSONDecodeError, OSError, TypeError, ValueError) as error:
        print(f"revision state refused: {error}", file=sys.stderr)
        raise SystemExit(2)
