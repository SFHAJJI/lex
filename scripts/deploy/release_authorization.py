#!/usr/bin/env python3
"""Normalize one successful release-state receipt into exact transition authorities."""

import argparse
import json
import re
import sys


REVISION = re.compile(r"^ca-lex-web--[a-z0-9-]+$")
RELEASE = re.compile(r"^assistant-eval-[0-9a-f]{12}-[0-9a-f]{12}$")
IMAGE_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
RESOURCE_ID = re.compile(
    r"^/subscriptions/[0-9a-f-]{36}/resourceGroups/[^/]{1,90}/providers/"
    r"Microsoft\.App/containerApps/ca-lex-web$",
    re.IGNORECASE,
)
UUID = re.compile(
    r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
    re.IGNORECASE,
)


def fail(message):
    raise ValueError(message)


def text(value, pattern, field):
    if not isinstance(value, str) or not pattern.fullmatch(value):
        fail(f"{field} is invalid")
    return value


def deployment_id(value, field):
    if isinstance(value, int) and value > 0:
        return str(value)
    if isinstance(value, str) and re.fullmatch(r"[1-9][0-9]*", value):
        return value
    fail(f"{field} is invalid")


def authorization(value, field):
    if not isinstance(value, dict):
        fail(f"{field} is absent")
    expected = {
        "kind",
        "evidence_release",
        "source_deployment_id",
        "signed_package_sha256",
    }
    if set(value) != expected:
        fail(f"{field} fields are not exact")
    kind = value.get("kind")
    if kind not in {"exact_revision_evaluation", "equivalent_first_release_fallback"}:
        fail(f"{field} kind is unsupported")
    evidence_release = text(value.get("evidence_release"), RELEASE, f"{field} evidence release")
    source = value.get("source_deployment_id")
    package = value.get("signed_package_sha256")
    if kind == "equivalent_first_release_fallback":
        source = deployment_id(source, f"{field} source deployment")
        package = text(package, SHA256, f"{field} signed package")
    else:
        if (source is None) != (package is None):
            fail(f"{field} source and signed package must be paired")
        if source is not None:
            source = deployment_id(source, f"{field} source deployment")
        if package is not None:
            package = text(package, SHA256, f"{field} signed package")
    return {
        "kind": kind,
        "evidence_release": evidence_release,
        "source_deployment_id": source,
        "signed_package_sha256": package,
    }


def exact_authorization(evaluation_release, source=None, package=None):
    return authorization(
        {
            "kind": "exact_revision_evaluation",
            "evidence_release": evaluation_release,
            "source_deployment_id": source,
            "signed_package_sha256": package,
        },
        "exact authorization",
    )


def normalize(document, args):
    if not isinstance(document, dict):
        fail("deployment receipt must be an object")
    prior_id = deployment_id(document.get("id"), "deployment id")
    if (
        document.get("task") != "lex-revision-promotion"
        or document.get("environment") != "production"
        or document.get("production_environment") is not True
    ):
        fail("deployment is not an exact production release-state receipt")
    if args.operation not in {"promote", "rollback"}:
        fail("operation is unsupported")
    current = text(args.current, REVISION, "current revision")
    target = text(args.target, REVISION, "target revision")
    if current == target:
        fail("current and target revisions must differ")
    evaluation_release = text(args.evaluation_release, RELEASE, "target evidence release")
    tenant_id = text(args.tenant_id, UUID, "Azure tenant id")
    subscription_id = text(args.subscription_id, UUID, "Azure subscription id")
    if (
        not RESOURCE_ID.fullmatch(args.container_app_resource_id)
        or not args.container_app_resource_id.casefold().startswith(
            f"/subscriptions/{subscription_id}/".casefold()
        )
    ):
        fail("Container App resource id is invalid")
    payload = document.get("payload")
    if not isinstance(payload, dict):
        fail("deployment payload is absent")

    schema = payload.get("schema")
    if schema == "lex-release-state-receipt/3":
        receipt = payload
        if receipt.get("purpose") != "record-audited-revision-transition":
            fail("release-state receipt purpose is invalid")
        current_authorization = authorization(
            receipt.get("target_authorization"), "current authorization"
        )
        retained_authorization = authorization(
            receipt.get("rollback_authorization"), "retained rollback authorization"
        )
    elif isinstance(payload.get("signed_receipt"), dict):
        receipt = payload["signed_receipt"]
        if (
            receipt.get("schema") != "lex-first-release-receipt/1"
            or receipt.get("purpose") != "authorize-equivalent-first-release-fallback"
            or receipt.get("rollback_kind") != "equivalent_first_release_fallback"
        ):
            fail("first-release receipt authority is invalid")
        release = text(
            receipt.get("assistant_evaluation_release"),
            RELEASE,
            "first-release evidence release",
        )
        package = text(
            receipt.get("bootstrap_package_sha256"),
            SHA256,
            "first-release signed package",
        )
        current_authorization = exact_authorization(release, prior_id, package)
        retained_authorization = authorization(
            {
                "kind": "equivalent_first_release_fallback",
                "evidence_release": release,
                "source_deployment_id": prior_id,
                "signed_package_sha256": package,
            },
            "first-release fallback authorization",
        )
        schema = "lex-first-release-receipt/1"
    else:
        fail("deployment receipt schema is unsupported")

    if (
        str(receipt.get("azure_tenant_id", "")).casefold() != tenant_id.casefold()
        or str(receipt.get("azure_subscription_id", "")).casefold()
        != subscription_id.casefold()
        or str(receipt.get("container_app_resource_id", "")).casefold()
        != args.container_app_resource_id.casefold()
        or receipt.get("target_revision") != current
        or str(receipt.get("target_revision_resource_id", "")).casefold()
        != f"{args.container_app_resource_id}/revisions/{current}".casefold()
    ):
        fail("current release-state receipt is cross-resource or stale")
    prior_rollback = text(receipt.get("rollback_revision"), REVISION, "receipt rollback")
    if str(receipt.get("rollback_revision_resource_id", "")).casefold() != (
        f"{args.container_app_resource_id}/revisions/{prior_rollback}".casefold()
    ):
        fail("receipt rollback resource id is invalid")
    current_image = text(
        receipt.get("target_image"), IMAGE_DIGEST, "receipt current image"
    )
    retained_image = text(
        receipt.get("rollback_image"), IMAGE_DIGEST, "receipt rollback image"
    )
    if schema == "lex-release-state-receipt/3" and (
        receipt.get("assistant_evaluation_release")
        != current_authorization["evidence_release"]
    ):
        fail("current evidence release differs from its target authorization")

    if args.operation == "rollback":
        if prior_rollback != target:
            fail("receipt does not bind the requested rollback target")
        target_authorization = retained_authorization
        target_image = retained_image
        if target_authorization["evidence_release"] != evaluation_release:
            fail("requested evidence release does not authorize the rollback target")
    else:
        if current_authorization["kind"] != "exact_revision_evaluation":
            fail("historical fallback production must return to its exact evaluated release")
        target_authorization = exact_authorization(evaluation_release)
        target_image = None

    return {
        "schema": "lex-transition-authorization/1",
        "prior_receipt_schema": schema,
        "prior_deployment_id": prior_id,
        "current_revision": current,
        "current_image": current_image,
        "target_revision": target,
        "target_image": target_image,
        "prior_rollback_revision": prior_rollback,
        "current_authorization": current_authorization,
        "target_authorization": target_authorization,
        "new_rollback_authorization": current_authorization,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("receipt")
    parser.add_argument("--operation", required=True)
    parser.add_argument("--current", required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--evaluation-release", required=True)
    parser.add_argument("--container-app-resource-id", required=True)
    parser.add_argument("--tenant-id", required=True)
    parser.add_argument("--subscription-id", required=True)
    args = parser.parse_args()
    with open(args.receipt, encoding="utf-8") as stream:
        document = json.load(stream)
    json.dump(normalize(document, args), sys.stdout, sort_keys=True, separators=(",", ":"))
    print()


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"release authorization refused: {error}", file=sys.stderr)
        raise SystemExit(2)
