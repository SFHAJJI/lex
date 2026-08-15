#!/usr/bin/env python3
"""Plan and classify one reviewed A+2 inactive-retention reconciliation."""

import hashlib
import json
from pathlib import Path
import sys

from bootstrap_legacy_plan import build_plan as build_legacy_plan
from bootstrap_plan import timestamp


INVENTORY_SCHEMA = "lex-bootstrap-legacy-recovery-inventory/1"
PLAN_SCHEMA = "lex-bootstrap-legacy-recovery-plan/1"


def canonical_inventory(inventory, allowed_counts):
    if not isinstance(inventory, dict) or inventory.get("schema") != INVENTORY_SCHEMA:
        raise ValueError("unsupported bootstrap legacy recovery inventory schema")
    if inventory.get("active_revisions_mode") != "Multiple" \
            or type(inventory.get("max_inactive_revisions")) is not int \
            or inventory["max_inactive_revisions"] != 1:
        raise ValueError("recovery requires Multiple mode and maxInactiveRevisions=1")
    revisions = inventory.get("revisions")
    if not isinstance(revisions, list) or len(revisions) not in allowed_counts:
        raise ValueError("recovery inventory has an unauthorized revision count")

    legacy = build_legacy_plan({
        "schema": "lex-bootstrap-legacy-inventory/1",
        "max_inactive_revisions": 1,
        "revisions": revisions,
    })
    by_name = {item.get("name"): item for item in revisions}
    for name, item in by_name.items():
        if item["template"].get("revisionSuffix") != name.split("--", 1)[1]:
            raise ValueError("revisionSuffix must exactly match the revision resource name")

    routes = inventory.get("ingress_traffic")
    if not isinstance(routes, list) or len(routes) != 1 or not isinstance(routes[0], dict):
        raise ValueError("recovery requires exactly one named ingress route")
    authority = legacy["legacy_authority"]["revision"]
    if routes[0] != {"revisionName": authority, "weight": 100,
                     "latestRevision": False, "label": None}:
        raise ValueError("recovery requires sole exact A100 ingress without label/latest")

    return {
        "schema": INVENTORY_SCHEMA,
        "active_revisions_mode": "Multiple",
        "max_inactive_revisions": 1,
        "ingress_traffic": routes,
        "revisions": sorted(
            revisions,
            key=lambda item: (timestamp(item["createdTime"], "revision createdTime"),
                              item["name"])),
    }


def build_plan(inventory):
    reviewed = canonical_inventory(inventory, {3})
    inactive = [item for item in reviewed["revisions"] if not item["active"]]
    if len(inactive) != 2:
        raise ValueError("recovery requires exact A plus two inactive revisions")
    encoded = json.dumps(
        reviewed, separators=(",", ":"), sort_keys=True).encode("utf-8")
    return {
        "schema": PLAN_SCHEMA,
        "dry_run": True,
        "inventory_sha256": hashlib.sha256(encoded).hexdigest(),
        "active_revisions_mode": "Multiple",
        "max_inactive_revisions": 1,
        "ingress_traffic": [{
            "revision_name": reviewed["ingress_traffic"][0]["revisionName"],
            "weight": 100, "latest_revision": False, "label": None,
        }],
        "operation": {
            "method": "POST", "api_version": "2025-01-01",
            "revision": inactive[0]["name"], "retry": False,
            "traffic_change": False, "activation_change": False,
            "template_change": False, "configuration_change": False,
        },
        "allowed_remaining_inactive_revisions": [item["name"] for item in inactive],
        "reviewed_inventory": reviewed,
    }


def classify(plan, inventory):
    if not isinstance(plan, dict) or plan.get("schema") != PLAN_SCHEMA \
            or not isinstance(plan.get("reviewed_inventory"), dict):
        raise ValueError("unsupported bootstrap legacy recovery plan schema")
    if build_plan(plan["reviewed_inventory"]) != plan:
        raise ValueError("reviewed bootstrap legacy recovery plan is not canonical")
    reviewed = plan["reviewed_inventory"]
    live = canonical_inventory(inventory, {2, 3})
    for field in ("active_revisions_mode", "max_inactive_revisions", "ingress_traffic"):
        if live[field] != reviewed[field]:
            raise ValueError(f"live recovery {field} differs from reviewed plan")
    if live == reviewed:
        return {"state": "unchanged", "remaining_inactive_revision": None}
    if len(live["revisions"]) == 2 \
            and all(item in reviewed["revisions"] for item in live["revisions"]):
        inactive = [item for item in live["revisions"] if not item["active"]]
        if len(inactive) == 1 \
                and inactive[0]["name"] in plan["allowed_remaining_inactive_revisions"]:
            return {"state": "converged",
                    "remaining_inactive_revision": inactive[0]["name"]}
    raise ValueError("live state is neither exact reviewed A+2 nor A+one reviewed survivor")


def main():
    if len(sys.argv) == 2:
        result = build_plan(json.loads(Path(sys.argv[1]).read_text(encoding="utf-8")))
    elif len(sys.argv) == 4 and sys.argv[1] == "--classify":
        plan = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
        inventory = json.loads(Path(sys.argv[3]).read_text(encoding="utf-8"))
        result = classify(plan, inventory)
    else:
        raise ValueError("usage: bootstrap_legacy_recovery_plan.py INVENTORY | "
                         "--classify PLAN INVENTORY")
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, TypeError, ValueError, json.JSONDecodeError) as error:
        print(f"bootstrap legacy recovery plan refused: {error}", file=sys.stderr)
        raise SystemExit(2)
