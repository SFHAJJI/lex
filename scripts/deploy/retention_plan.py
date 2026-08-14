#!/usr/bin/env python3
"""Build a fail-closed, identity-based Lex retention plan. Never mutates state."""

import json
from pathlib import Path
import re
import sys


DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")


def image_digest(image):
    digest = image.rpartition("@")[2]
    if not DIGEST.fullmatch(digest):
        raise ValueError(f"revision image is not immutable: {image}")
    return digest


def checked_digest(value, field):
    if not DIGEST.fullmatch(value or ""):
        raise ValueError(f"{field} is not a SHA-256 digest")
    return value


def build_plan(inventory):
    if inventory.get("schema") != "lex-retention-inventory/1":
        raise ValueError("unsupported retention inventory schema")

    revisions = inventory.get("revisions")
    manifests = inventory.get("acr_manifests")
    if not isinstance(revisions, list) or not isinstance(manifests, list):
        raise ValueError("revision and ACR inventories are required")
    by_name = {item.get("name"): item for item in revisions}
    if len(by_name) != len(revisions) or None in by_name:
        raise ValueError("revision names must be present and unique")

    production = inventory.get("production_revision")
    candidate = inventory.get("candidate_revision") or None
    rollback = inventory.get("rollback_revision") or None
    roles = {"production": production, "candidate": candidate, "rollback": rollback}
    for role, name in roles.items():
        if name and name not in by_name:
            raise ValueError(f"{role} revision is absent from the inventory")
    if not production:
        raise ValueError("production revision is required")
    if not rollback:
        raise ValueError("rollback revision is required")
    role_names = [name for name in roles.values() if name]
    if len(set(role_names)) != len(role_names):
        raise ValueError("production, candidate, and rollback revisions must be distinct")

    traffic = [item for item in revisions if int(item.get("trafficWeight") or 0) > 0]
    if len(traffic) != 1 or traffic[0]["name"] != production:
        raise ValueError("production must be the only traffic-bearing revision")

    protected = set()
    protected_by = {}

    def protect(digest, reason):
        digest = checked_digest(digest, reason)
        protected.add(digest)
        protected_by.setdefault(digest, []).append(reason)

    for role, name in roles.items():
        if name:
            protect(image_digest(by_name[name].get("image", "")), f"live_{role}")

    receipts = [
        item for item in inventory.get("promotion_receipts", [])
        if item.get("state") == "success"
    ]
    if not receipts:
        raise ValueError("a successful release-state receipt is required")
    try:
        latest_receipt = max(
            receipts,
            key=lambda item: (item["created_at"], int(item["id"])),
        )
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError("release-state receipts require a timestamp and numeric id") from error

    expected_receipt = {
        "target_revision": production,
        "target_image": image_digest(by_name[production].get("image", "")),
        "rollback_revision": rollback,
        "rollback_image": image_digest(by_name[rollback].get("image", "")),
    }
    for field, expected in expected_receipt.items():
        if latest_receipt.get(field) != expected:
            raise ValueError(f"latest release-state receipt does not bind live {field}")

    all_digests = []
    for manifest in manifests:
        digest = checked_digest(manifest.get("digest"), "ACR manifest digest")
        if digest in all_digests:
            raise ValueError("ACR inventory contains duplicate digests")
        all_digests.append(digest)

    missing = sorted(protected - set(all_digests))
    if missing:
        raise ValueError(f"protected ACR digests are absent: {', '.join(missing)}")

    delete_digests = sorted(set(all_digests) - protected)
    inactive = sorted(
        (item for item in revisions if not item.get("active")),
        key=lambda item: (item.get("createdTime", ""), item["name"]),
    )
    retained_inactive = {name for name in (candidate, rollback) if name}
    superseded = [item["name"] for item in inactive if item["name"] not in retained_inactive]

    staging_delete = []
    for blob in inventory.get("staging_blobs", []):
        name = blob.get("name", "")
        if not blob.get("immutable_release_verified"):
            continue
        if not name.startswith("staging/") or not blob.get("etag"):
            continue
        staging_delete.append({"name": name, "etag": blob["etag"]})

    return {
        "schema": "lex-retention-plan/1",
        "dry_run": True,
        "container_apps": {
            # Container Apps excludes the active production revision from this count.
            "max_inactive_revisions": 2 if candidate else 1,
            "protected_revision_count": len(role_names),
            "protected_revisions": {key: value for key, value in roles.items() if value},
            "superseded_inactive_revisions": superseded,
            "reconciliation": "native_max_inactive_revisions",
        },
        "acr": {
            "protected_digests": [
                {"digest": digest, "reasons": sorted(protected_by[digest])}
                for digest in sorted(protected)
            ],
            "delete_digests": delete_digests,
            "delete_commands": [
                f"az acr repository delete --name crsoufien3orem "
                f"--image lex-web@{digest} --yes"
                for digest in delete_digests
            ],
        },
        "blob_staging": {
            "delete": sorted(staging_delete, key=lambda item: item["name"]),
            "immutable_release_groups": "retain",
        },
    }


def main():
    if len(sys.argv) != 2:
        raise ValueError("usage: retention_plan.py INVENTORY.json")
    inventory = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    print(json.dumps(build_plan(inventory), indent=2, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"retention plan refused: {error}", file=sys.stderr)
        raise SystemExit(2)
