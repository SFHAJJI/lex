#!/usr/bin/env python3
"""Hash one Container Apps revision template, excluding only revisionSuffix."""

import copy
import hashlib
import json
import sys


def template_from(value):
    if not isinstance(value, dict):
        raise ValueError("revision template input must be an object")
    if isinstance(value.get("properties"), dict):
        value = value["properties"].get("template")
    elif isinstance(value.get("template"), dict):
        value = value["template"]
    if not isinstance(value, dict) or not isinstance(value.get("containers"), list):
        raise ValueError("Container Apps revision template is required")
    return value


def digest(value):
    canonical = copy.deepcopy(template_from(value))
    canonical.pop("revisionSuffix", None)
    encoded = json.dumps(
        canonical,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return "sha256:" + hashlib.sha256(encoded).hexdigest()


def main():
    print(digest(json.load(sys.stdin)))


if __name__ == "__main__":
    try:
        main()
    except (json.JSONDecodeError, OSError, TypeError, ValueError) as error:
        print(f"revision template digest refused: {error}", file=sys.stderr)
        raise SystemExit(2)
