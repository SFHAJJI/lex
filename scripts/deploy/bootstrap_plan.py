#!/usr/bin/env python3
"""Build a canonical, dry-run allowlist for the one-time first release bootstrap."""

from datetime import datetime, timedelta
import hashlib
import json
from pathlib import Path
import re
import sys

from revision_template_digest import digest as canonical_template_digest


DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
REVISION = re.compile(r"^ca-lex-web--[a-z0-9-]+$")
LEX_IMAGE = re.compile(
    r"^crsoufien3orem\.azurecr\.io/lex-web@(sha256:[0-9a-f]{64})$"
)


def checked_digest(value, field):
    if not DIGEST.fullmatch(value or ""):
        raise ValueError(f"{field} is not a SHA-256 digest")
    return value


def image_digest(image):
    if not isinstance(image, str):
        raise ValueError("revision image must be a string")
    digest = (image or "").rpartition("@")[2]
    return checked_digest(digest, "revision image digest")


def timestamp(value, field):
    if not isinstance(value, str) or not (
            value.endswith("Z") or value.endswith("+00:00")):
        raise ValueError(f"{field} must be an explicit UTC instant")
    parsed = datetime.fromisoformat(
        value[:-1] + "+00:00" if value.endswith("Z") else value)
    if parsed.utcoffset() != timedelta(0):
        raise ValueError(f"{field} must be an explicit UTC instant")
    return parsed


def build_plan(inventory):
    if inventory.get("schema") != "lex-bootstrap-inventory/1":
        raise ValueError("unsupported bootstrap inventory schema")
    if type(inventory.get("max_inactive_revisions")) is not int \
            or inventory["max_inactive_revisions"] != 1:
        raise ValueError("bootstrap preparation requires maxInactiveRevisions=1")

    revisions = inventory.get("revisions")
    manifests = inventory.get("acr_manifests")
    blobs = inventory.get("staging_blobs")
    if not all(isinstance(value, list) for value in (revisions, manifests, blobs)):
        raise ValueError("revision, ACR, and staging inventories are required")

    if any(not isinstance(revision, dict) for revision in revisions):
        raise ValueError("revision inventory entries must be objects")
    by_name = {revision.get("name"): revision for revision in revisions}
    if len(by_name) != len(revisions) or None in by_name or "" in by_name:
        raise ValueError("revision names must be present and unique")
    for revision in revisions:
        name = revision.get("name")
        active = revision.get("active")
        traffic_weight = revision.get("trafficWeight")
        image = revision.get("image")
        if not isinstance(name, str) or not REVISION.fullmatch(name):
            raise ValueError("revision name is invalid")
        if type(active) is not bool:
            raise ValueError("revision active must be a boolean")
        if type(traffic_weight) is not int or not 0 <= traffic_weight <= 100:
            raise ValueError("revision trafficWeight must be an integer from 0 through 100")
        if traffic_weight > 0 and not active:
            raise ValueError("a traffic-bearing revision must be active")
        timestamp(revision.get("createdTime"), "revision createdTime")
        image_digest(image)
        template = revision.get("template")
        if not isinstance(template, dict):
            raise ValueError("revision template is required")
        containers = template.get("containers")
        if (not isinstance(containers, list) or len(containers) != 1
                or not isinstance(containers[0], dict)
                or containers[0].get("image") != image):
            raise ValueError("revision image must match the canonical template")
    production_name = inventory.get("production_revision")
    rollback_name = inventory.get("rollback_revision")
    if not production_name or not rollback_name or production_name == rollback_name:
        raise ValueError("distinct production and rollback revisions are required")
    if production_name not in by_name or rollback_name not in by_name:
        raise ValueError("protected bootstrap revision is absent from the inventory")

    production = by_name[production_name]
    rollback = by_name[rollback_name]
    if production.get("active") is not True or production.get("trafficWeight") != 0:
        raise ValueError("bootstrap production C must be active and zero traffic")
    if rollback.get("active") is not False or rollback.get("trafficWeight") != 0:
        raise ValueError("bootstrap rollback R must be inactive and zero traffic")

    production_created = timestamp(production.get("createdTime"), "production createdTime")
    rollback_created = timestamp(rollback.get("createdTime"), "rollback createdTime")
    if rollback_created >= production_created:
        raise ValueError("bootstrap rollback must be created strictly before production")

    legacy = [
        revision for revision in revisions
        if revision["name"] not in {production_name, rollback_name}
    ]
    if len(legacy) != 1:
        raise ValueError("bootstrap inventory must contain only the legacy traffic authority")
    if any(
        timestamp(revision.get("createdTime"), "legacy createdTime") >= rollback_created
        for revision in legacy
    ):
        raise ValueError("all legacy revisions must predate the bootstrap rollback")
    traffic = [revision for revision in legacy if int(revision.get("trafficWeight") or 0) > 0]
    if len(traffic) != 1 or int(traffic[0].get("trafficWeight") or 0) != 100:
        raise ValueError("exactly one legacy revision must carry 100 percent traffic")
    active_legacy = [revision for revision in legacy if revision["active"]]
    if active_legacy != traffic:
        raise ValueError("the legacy traffic authority must be the only active legacy revision")

    production_reference = production.get("image")
    rollback_reference = rollback.get("image")
    production_match = LEX_IMAGE.fullmatch(production_reference)
    rollback_match = LEX_IMAGE.fullmatch(rollback_reference)
    if not production_match or not rollback_match:
        raise ValueError("bootstrap revisions must use the immutable Lex ACR repository")
    production_image = production_match.group(1)
    rollback_image = rollback_match.group(1)
    if production_image != rollback_image:
        raise ValueError("bootstrap production and rollback must use one immutable image digest")
    production_template = canonical_template_digest(production["template"])
    rollback_template = canonical_template_digest(rollback["template"])
    if production_template != rollback_template:
        raise ValueError("bootstrap revisions differ outside revisionSuffix")

    manifest_digests = []
    for manifest in manifests:
        if not isinstance(manifest, dict):
            raise ValueError("ACR manifest inventory entries must be objects")
        digest = checked_digest(manifest.get("digest"), "ACR manifest digest")
        if digest in manifest_digests:
            raise ValueError("ACR inventory contains duplicate digests")
        manifest_digests.append(digest)
    if production_image not in manifest_digests:
        raise ValueError("protected bootstrap image is absent from ACR")

    blob_by_name = {}
    for blob in blobs:
        if not isinstance(blob, dict):
            raise ValueError("staging blob inventory entries must be objects")
        name = blob.get("name")
        etag = blob.get("etag")
        if (not isinstance(name, str) or not name.startswith("staging/")
                or name == "staging/" or not isinstance(etag, str) or not etag):
            raise ValueError("bootstrap blob allowlist accepts only staging names with ETags")
        if name in blob_by_name:
            raise ValueError("staging blob names must be unique")
        blob_by_name[name] = {"name": name, "etag": etag}

    protected = {
        "production": {
            "revision": production_name,
            "created_time": production["createdTime"],
            "image": production_reference,
            "image_digest": production_image,
            "canonical_template_digest": production_template,
            "active": True,
            "traffic_weight": 0,
        },
        "rollback": {
            "revision": rollback_name,
            "created_time": rollback["createdTime"],
            "image": rollback_reference,
            "image_digest": rollback_image,
            "canonical_template_digest": rollback_template,
            "active": False,
            "traffic_weight": 0,
        },
    }
    plan_identity = {
        "protected": protected,
        "legacy": [
            {
                "revision": revision["name"],
                "created_time": revision["createdTime"],
                "image": revision["image"],
                "image_digest": image_digest(revision.get("image")),
                "canonical_template_digest": canonical_template_digest(
                    revision["template"]
                ),
            }
            for revision in sorted(legacy, key=lambda item: (item["createdTime"], item["name"]))
        ],
        "acr_digests": sorted(manifest_digests),
        "staging_blobs": sorted(blob_by_name.values(), key=lambda item: item["name"]),
    }
    fingerprint = hashlib.sha256(
        json.dumps(plan_identity, separators=(",", ":"), sort_keys=True).encode("utf-8")
    ).hexdigest()

    return {
        "schema": "lex-bootstrap-cleanup-plan/1",
        "dry_run": True,
        "inventory_sha256": fingerprint,
        "protected": protected,
        "container_apps": {
            "max_inactive_revisions": 1,
            "legacy_traffic_revision": traffic[0]["name"],
            "deactivate_revisions": plan_identity["legacy"],
        },
        "acr": {
            "protected_digests": [production_image],
            "delete_digests": sorted(set(manifest_digests) - {production_image}),
        },
        "blob_staging": {
            "delete": plan_identity["staging_blobs"],
        },
    }


def main():
    if len(sys.argv) != 2:
        raise ValueError("usage: bootstrap_plan.py INVENTORY.json")
    inventory = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    print(json.dumps(build_plan(inventory), indent=2, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, TypeError, ValueError, json.JSONDecodeError) as error:
        print(f"bootstrap plan refused: {error}", file=sys.stderr)
        raise SystemExit(2)
