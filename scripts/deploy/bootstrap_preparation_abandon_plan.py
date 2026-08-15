#!/usr/bin/env python3
"""Plan and classify exact abandonment of one zero-traffic bootstrap revision."""

import copy
import hashlib
import json
from pathlib import Path
import sys

from bootstrap_legacy_plan import LEX_IMAGE, REVISION, build_plan as build_legacy_plan
from bootstrap_plan import timestamp


INVENTORY_SCHEMA = "lex-bootstrap-preparation-inventory/1"
PLAN_SCHEMA = "lex-bootstrap-preparation-abandon-plan/1"
READY_STATES = {"Running", "RunningAtMaxScale"}
TARGET_STATES = READY_STATES | {"Failed"}
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


def canonical_revisions(inventory):
    revisions = inventory.get("revisions")
    if not isinstance(revisions, list) or len(revisions) != 3:
        raise ValueError("preparation abandonment requires exactly three revisions")
    canonical = []
    names = set()
    for item in revisions:
        if not isinstance(item, dict) or set(item) != REVISION_KEYS:
            raise ValueError("preparation revision shape is invalid")
        name = item.get("name")
        if not isinstance(name, str) or not REVISION.fullmatch(name) or name in names:
            raise ValueError("preparation revision names must be valid and unique")
        names.add(name)
        if type(item.get("active")) is not bool:
            raise ValueError("preparation revision active must be boolean")
        traffic = item.get("trafficWeight")
        if type(traffic) is not int or not 0 <= traffic <= 100:
            raise ValueError("preparation trafficWeight must be an integer")
        running = item.get("runningState")
        if running is not None and not isinstance(running, str):
            raise ValueError("preparation runningState must be a string or null")
        timestamp(item.get("createdTime"), "preparation revision createdTime")
        template = item.get("template")
        if not isinstance(template, dict):
            raise ValueError("preparation revision template is required")
        suffix = name.split("--", 1)[1]
        observed_suffix = template.get("revisionSuffix")
        if observed_suffix is not None and observed_suffix != suffix:
            raise ValueError("revisionSuffix must exactly match the revision resource name")
        normalized = copy.deepcopy(item)
        normalized["template"]["revisionSuffix"] = suffix
        canonical.append(normalized)
    return sorted(canonical, key=lambda item: (
        timestamp(item["createdTime"], "preparation createdTime"), item["name"]))


def canonical_routes(routes, authority, target, target_active):
    if not isinstance(routes, list) or any(
            not isinstance(route, dict) or set(route) != ROUTE_KEYS for route in routes):
        raise ValueError("preparation ingress route shape is invalid")
    if any(not isinstance(route["revisionName"], str)
           or type(route["weight"]) is not int
           or type(route["latestRevision"]) is not bool
           or route["label"] is not None for route in routes):
        raise ValueError("preparation ingress route types are invalid")
    canonical = sorted(copy.deepcopy(routes), key=lambda route: route["revisionName"])
    allowed = [[{
        "revisionName": authority, "weight": 100,
        "latestRevision": False, "label": None,
    }]]
    if target_active:
        allowed.append(sorted([allowed[0][0], {
            "revisionName": target, "weight": 0,
            "latestRevision": False, "label": None,
        }], key=lambda route: route["revisionName"]))
    if canonical not in allowed:
        raise ValueError("preparation requires exact named A100[/target0] ingress")
    return canonical


def build_plan(inventory):
    if not isinstance(inventory, dict) or set(inventory) != INVENTORY_KEYS \
            or inventory.get("schema") != INVENTORY_SCHEMA:
        raise ValueError("unsupported bootstrap preparation inventory shape")
    if inventory.get("active_revisions_mode") != "Multiple" \
            or type(inventory.get("max_inactive_revisions")) is not int \
            or inventory["max_inactive_revisions"] != 1:
        raise ValueError("preparation requires raw Multiple mode and maxInactiveRevisions=1")

    revisions = canonical_revisions(inventory)
    traffic = [item for item in revisions if item["trafficWeight"] > 0]
    zero_active = [item for item in revisions
                   if item["active"] and item["trafficWeight"] == 0]
    inactive = [item for item in revisions if not item["active"]]
    if len(traffic) != 1 or not traffic[0]["active"] or traffic[0]["trafficWeight"] != 100 \
            or len(zero_active) != 1 or len(inactive) != 1 \
            or inactive[0]["trafficWeight"] != 0:
        raise ValueError("preparation must be exact A100 plus one active0 target and one inactive0")
    authority_raw, target_raw, retained_raw = traffic[0], zero_active[0], inactive[0]
    if authority_raw["runningState"] not in READY_STATES:
        raise ValueError("preparation authority A must be running and ready")
    if target_raw["runningState"] not in TARGET_STATES:
        raise ValueError("active0 target must have a stable Running or Failed state")
    if not LEX_IMAGE.fullmatch(target_raw.get("image") or ""):
        raise ValueError("active0 target must use an immutable Lex ACR image")

    legacy_revisions = copy.deepcopy(revisions)
    for item in legacy_revisions:
        if item["name"] == target_raw["name"]:
            item["active"] = False
    legacy = build_legacy_plan({
        "schema": "lex-bootstrap-legacy-inventory/1",
        "max_inactive_revisions": 1,
        "revisions": legacy_revisions,
    })
    identities = {item["revision"]: item for item in legacy["revisions"]}
    authority = identities[authority_raw["name"]]
    retained = identities[retained_raw["name"]]
    target = copy.deepcopy(identities[target_raw["name"]])
    target["active"] = True
    if not timestamp(authority["created_time"], "authority createdTime") \
            < timestamp(retained["created_time"], "retained createdTime") \
            < timestamp(target["created_time"], "target createdTime"):
        raise ValueError("preparation chronology must be exact A < retained < target")

    routes = canonical_routes(inventory.get("ingress_traffic"), authority["revision"],
                              target["revision"], True)
    if inventory.get("latest_revision_name") != target["revision"] \
            or inventory.get("latest_ready_revision_name") \
            not in {retained["revision"], target["revision"]}:
        raise ValueError("preparation latest pointers do not bind retained/target")
    reviewed = {
        "schema": INVENTORY_SCHEMA,
        "active_revisions_mode": "Multiple",
        "max_inactive_revisions": 1,
        "latest_revision_name": target["revision"],
        "latest_ready_revision_name": inventory["latest_ready_revision_name"],
        "ingress_traffic": routes,
        "revisions": revisions,
    }
    return {
        "schema": PLAN_SCHEMA,
        "dry_run": True,
        "inventory_sha256": compact_sha256(reviewed),
        "legacy_authority": authority,
        "retained_inactive": retained,
        "target": target,
        "ingress_traffic": routes,
        "operation": {
            "method": "DEACTIVATE",
            "revision": target["revision"],
            "traffic_change": False,
            "configuration_change": False,
            "template_change": False,
            "retry_only_after_exact_pre_read": True,
        },
        "reviewed_inventory": reviewed,
    }


def classify(plan, inventory):
    if not isinstance(plan, dict) or plan.get("schema") != PLAN_SCHEMA \
            or not isinstance(plan.get("reviewed_inventory"), dict) \
            or build_plan(plan["reviewed_inventory"]) != plan:
        raise ValueError("reviewed bootstrap preparation plan is not canonical")
    try:
        if build_plan(inventory) == plan:
            return {"state": "target-active"}
    except (TypeError, ValueError):
        pass

    if not isinstance(inventory, dict) or set(inventory) != INVENTORY_KEYS \
            or inventory.get("schema") != INVENTORY_SCHEMA \
            or inventory.get("active_revisions_mode") != "Multiple" \
            or inventory.get("max_inactive_revisions") != 1:
        raise ValueError("post-abandon inventory shape or configuration differs")
    target_name = plan["target"]["revision"]
    authority_name = plan["legacy_authority"]["revision"]
    retained_name = plan["retained_inactive"]["revision"]
    revisions = canonical_revisions(inventory)
    by_name = {item["name"]: item for item in revisions}
    if set(by_name) != {authority_name, retained_name, target_name}:
        raise ValueError("post-abandon revision identities differ")
    if not by_name[authority_name]["active"] or by_name[authority_name]["trafficWeight"] != 100 \
            or by_name[authority_name]["runningState"] not in READY_STATES \
            or by_name[retained_name]["active"] or by_name[retained_name]["trafficWeight"] != 0 \
            or by_name[target_name]["active"] or by_name[target_name]["trafficWeight"] != 0:
        raise ValueError("post-abandon revision states differ")
    canonical_routes(inventory.get("ingress_traffic"), authority_name, target_name, False)
    if inventory.get("latest_revision_name") != target_name \
            or inventory.get("latest_ready_revision_name") not in {retained_name, target_name}:
        raise ValueError("post-abandon latest pointers differ")

    normalized = copy.deepcopy(inventory)
    for item in normalized["revisions"]:
        if item["name"] == target_name:
            item["active"] = True
            item["runningState"] = next(
                reviewed["runningState"] for reviewed in plan["reviewed_inventory"]["revisions"]
                if reviewed["name"] == target_name)
    normalized["ingress_traffic"] = plan["reviewed_inventory"]["ingress_traffic"]
    normalized["latest_ready_revision_name"] = \
        plan["reviewed_inventory"]["latest_ready_revision_name"]
    if build_plan(normalized) != plan:
        raise ValueError("post-abandon image, template or chronology differs")
    return {"state": "target-inactive"}


def main():
    if len(sys.argv) == 2:
        result = build_plan(json.loads(Path(sys.argv[1]).read_text(encoding="utf-8")))
    elif len(sys.argv) == 4 and sys.argv[1] == "--classify":
        plan = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
        inventory = json.loads(Path(sys.argv[3]).read_text(encoding="utf-8"))
        result = classify(plan, inventory)
    else:
        raise ValueError("usage: bootstrap_preparation_abandon_plan.py INVENTORY | "
                         "--classify PLAN INVENTORY")
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, TypeError, ValueError, json.JSONDecodeError) as error:
        print(f"bootstrap preparation abandon plan refused: {error}", file=sys.stderr)
        raise SystemExit(2)
