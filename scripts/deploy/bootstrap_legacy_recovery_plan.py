#!/usr/bin/env python3
"""Build the exact no-write A+2 handoff for first-official deployment."""

import copy
import hashlib
import json
from pathlib import Path
import sys

from bootstrap_legacy_plan import REVISION, build_plan as build_legacy_plan
from bootstrap_plan import timestamp


INVENTORY_SCHEMA = "lex-bootstrap-legacy-recovery-inventory/2"
PLAN_SCHEMA = "lex-bootstrap-legacy-recovery-plan/2"
READY_STATES = {"Running", "RunningAtMaxScale"}
INVENTORY_KEYS = {
    "schema", "active_revisions_mode", "max_inactive_revisions",
    "latest_revision_name", "latest_ready_revision_name",
    "ingress_traffic", "revisions",
}
REVISION_KEYS = {
    "name", "active", "trafficWeight", "runningState", "createdTime",
    "image", "template",
}
ROUTE_KEYS = {"revisionName", "weight", "latestRevision", "label"}


def compact_sha256(value):
    encoded = json.dumps(value, separators=(",", ":"), sort_keys=True).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def canonical_inventory(inventory):
    if not isinstance(inventory, dict) or set(inventory) != INVENTORY_KEYS \
            or inventory.get("schema") != INVENTORY_SCHEMA:
        raise ValueError("unsupported bootstrap legacy handoff inventory shape")
    if inventory.get("active_revisions_mode") != "Multiple" \
            or type(inventory.get("max_inactive_revisions")) is not int \
            or inventory["max_inactive_revisions"] != 1:
        raise ValueError("handoff requires raw Multiple mode and maxInactiveRevisions=1")
    revisions = inventory.get("revisions")
    if not isinstance(revisions, list) or len(revisions) != 3:
        raise ValueError("handoff requires exact A plus two inactive revisions")

    canonical_revisions = []
    names = set()
    running_states = {}
    for item in revisions:
        if not isinstance(item, dict) or set(item) != REVISION_KEYS:
            raise ValueError("handoff revision shape is invalid")
        name = item.get("name")
        if not isinstance(name, str) or not REVISION.fullmatch(name) or name in names:
            raise ValueError("handoff revision names must be valid and unique")
        names.add(name)
        timestamp(item.get("createdTime"), "handoff revision createdTime")
        running = item.get("runningState")
        if running is not None and not isinstance(running, str):
            raise ValueError("handoff runningState must be a string or null")
        running_states[name] = running
        template = item.get("template")
        if not isinstance(template, dict):
            raise ValueError("handoff revision template is required")
        suffix = name.split("--", 1)[1]
        observed_suffix = template.get("revisionSuffix")
        if observed_suffix is not None and observed_suffix != suffix:
            raise ValueError("revisionSuffix must exactly match the revision resource name")
        canonical_item = {key: copy.deepcopy(value) for key, value in item.items()
                          if key != "runningState"}
        canonical_item["template"]["revisionSuffix"] = suffix
        canonical_revisions.append(canonical_item)

    canonical_revisions.sort(
        key=lambda item: (timestamp(item["createdTime"], "handoff createdTime"),
                          item["name"]),
    )
    legacy = build_legacy_plan({
        "schema": "lex-bootstrap-legacy-inventory/1",
        "max_inactive_revisions": 1,
        "revisions": canonical_revisions,
    })
    authority = legacy["legacy_authority"]
    if running_states[authority["revision"]] not in READY_STATES:
        raise ValueError("legacy A must be running and ready")

    routes = inventory.get("ingress_traffic")
    if not isinstance(routes, list) or any(
            not isinstance(route, dict) or set(route) != ROUTE_KEYS
            or not isinstance(route["revisionName"], str)
            or type(route["weight"]) is not int
            or type(route["latestRevision"]) is not bool
            or route["label"] is not None for route in routes):
        raise ValueError("handoff ingress route shape or types are invalid")
    expected_route = [{
        "revisionName": authority["revision"], "weight": 100,
        "latestRevision": False, "label": None,
    }]
    if routes != expected_route:
        raise ValueError("handoff requires sole exact A100 ingress without label/latest")

    inactive = [item for item in legacy["revisions"] if not item["active"]]
    if len(inactive) != 2:
        raise ValueError("handoff requires exactly two inactive zero-traffic records")
    survivor = inactive[-1]["revision"]
    inactive_names = {item["revision"] for item in inactive}
    if inventory.get("latest_revision_name") != survivor \
            or inventory.get("latest_ready_revision_name") not in inactive_names:
        raise ValueError("handoff requires newer inactive latest and exact inactive latest-ready")

    return {
        "schema": INVENTORY_SCHEMA,
        "active_revisions_mode": "Multiple",
        "max_inactive_revisions": 1,
        "latest_revision_name": survivor,
        "latest_ready_revision_name": inventory["latest_ready_revision_name"],
        "ingress_traffic": copy.deepcopy(routes),
        "authority_ready": True,
        "revisions": canonical_revisions,
    }, legacy, inactive


def build_plan(inventory):
    reviewed, legacy, inactive = canonical_inventory(inventory)
    return {
        "schema": PLAN_SCHEMA,
        "dry_run": True,
        "inventory_sha256": compact_sha256(reviewed),
        "active_revisions_mode": "Multiple",
        "max_inactive_revisions": 1,
        "latest_revision_name": reviewed["latest_revision_name"],
        "latest_ready_revision_name": reviewed["latest_ready_revision_name"],
        "ingress_traffic": reviewed["ingress_traffic"],
        "legacy_authority": legacy["legacy_authority"],
        "handoff": {
            "first_pruned_inactive": inactive[0],
            "retained_until_candidate": inactive[1],
        },
        "operation": {
            "method": "NONE",
            "traffic_change": False,
            "activation_change": False,
            "template_change": False,
            "configuration_change": False,
        },
        "reviewed_inventory": reviewed,
    }


def main():
    if len(sys.argv) != 2:
        raise ValueError("usage: bootstrap_legacy_recovery_plan.py INVENTORY.json")
    inventory = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    print(json.dumps(build_plan(inventory), indent=2, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, TypeError, ValueError, json.JSONDecodeError) as error:
        print(f"bootstrap legacy handoff plan refused: {error}", file=sys.stderr)
        raise SystemExit(2)
