#!/usr/bin/env python3
"""Build the exact, reviewable plan for one-time pre-release revision cleanup."""

import hashlib
import json
from pathlib import Path
import re
import sys

from bootstrap_plan import LEX_IMAGE, timestamp
from revision_template_digest import digest as canonical_template_digest


REVISION = re.compile(r"^ca-lex-web--[a-z0-9-]+$")
LEX_TAGGED_IMAGE = re.compile(
    r"^crsoufien3orem\.azurecr\.io/lex-web:"
    r"[A-Za-z0-9_][A-Za-z0-9._-]{0,127}$"
)


def build_plan(inventory):
    if inventory.get("schema") != "lex-bootstrap-legacy-inventory/1":
        raise ValueError("unsupported bootstrap legacy inventory schema")
    limit = inventory.get("max_inactive_revisions")
    revisions = inventory.get("revisions")
    if type(limit) is not int or not 1 <= limit <= 100:
        raise ValueError("maxInactiveRevisions must be an integer from 1 through 100")
    if not isinstance(revisions, list) or len(revisions) < 2:
        raise ValueError("legacy inventory must contain A and at least one inactive revision")
    if any(not isinstance(item, dict) for item in revisions):
        raise ValueError("legacy revision inventory entries must be objects")
    names = [item.get("name") for item in revisions]
    if (any(not isinstance(name, str) or not REVISION.fullmatch(name) for name in names)
            or len(set(names)) != len(names)):
        raise ValueError("legacy revision names must be valid and unique")

    canonical = []
    for revision in revisions:
        active = revision.get("active")
        traffic = revision.get("trafficWeight")
        image = revision.get("image")
        template = revision.get("template")
        if type(active) is not bool:
            raise ValueError("legacy revision active must be a boolean")
        if type(traffic) is not int or not 0 <= traffic <= 100:
            raise ValueError("legacy revision trafficWeight must be an integer from 0 through 100")
        if not isinstance(template, dict):
            raise ValueError("legacy revision template is required")
        containers = template.get("containers")
        if (not isinstance(containers, list) or len(containers) != 1
                or not isinstance(containers[0], dict)
                or containers[0].get("image") != image):
            raise ValueError("legacy revision image must match its one-container template")
        timestamp(revision.get("createdTime"), "legacy revision createdTime")
        immutable_match = LEX_IMAGE.fullmatch(image or "") if isinstance(image, str) else None
        tagged_match = (LEX_TAGGED_IMAGE.fullmatch(image or "")
                        if isinstance(image, str) else None)
        if active and not immutable_match:
            raise ValueError("active legacy A must use the immutable Lex ACR repository")
        if not active and not (immutable_match or tagged_match):
            raise ValueError("inactive legacy revision must use the exact Lex ACR repository")
        canonical.append({
            "revision": revision["name"],
            "active": active,
            "traffic_weight": traffic,
            "created_time": revision["createdTime"],
            "image": image,
            # Historical inactive revisions may retain their exact tag reference. They are never
            # activated or promoted; the reviewed plan binds their full image string and template
            # before Azure's oldest-inactive purge. Only active A is an immutable authority.
            "image_digest": immutable_match.group(1) if immutable_match else None,
            "canonical_template_digest": canonical_template_digest(template),
        })

    authorities = [item for item in canonical if item["active"]]
    traffic = [item for item in canonical if item["traffic_weight"] > 0]
    if (len(authorities) != 1 or len(traffic) != 1
            or authorities[0] != traffic[0] or traffic[0]["traffic_weight"] != 100):
        raise ValueError("legacy A must be the only active revision and exact 100% traffic authority")
    if any(item["active"] or item["traffic_weight"] != 0
           for item in canonical if item is not authorities[0]):
        raise ValueError("every non-A legacy revision must be inactive with zero traffic")

    ordered = sorted(canonical, key=lambda item: (item["created_time"], item["revision"]))
    identity = {
        "max_inactive_revisions": limit,
        "legacy_authority": authorities[0],
        "revisions": ordered,
    }
    fingerprint = hashlib.sha256(
        json.dumps(identity, separators=(",", ":"), sort_keys=True).encode("utf-8")
    ).hexdigest()
    return {
        "schema": "lex-bootstrap-legacy-cleanup-plan/1",
        "dry_run": True,
        "inventory_sha256": fingerprint,
        "legacy_authority": authorities[0],
        "revisions": ordered,
        "mutation": {
            "max_inactive_revisions": 1,
            "traffic_change": False,
            "activation_change": False,
        },
    }


def main():
    if len(sys.argv) != 2:
        raise ValueError("usage: bootstrap_legacy_plan.py INVENTORY.json")
    inventory = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    print(json.dumps(build_plan(inventory), indent=2, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, TypeError, ValueError, json.JSONDecodeError) as error:
        print(f"bootstrap legacy plan refused: {error}", file=sys.stderr)
        raise SystemExit(2)
