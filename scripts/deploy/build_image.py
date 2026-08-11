#!/usr/bin/env python3
import json
import re
import sys


MAXIMUM_PAYLOAD_BYTES = 1024 * 1024
DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")


def build_digest(payload_bytes, repository, tag):
    if not payload_bytes:
        raise ValueError("ACR build result is empty")
    if len(payload_bytes) > MAXIMUM_PAYLOAD_BYTES:
        raise ValueError("ACR build result exceeds its byte limit")
    try:
        run = json.loads(payload_bytes)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("ACR build result is malformed") from error
    if not isinstance(run, dict):
        raise ValueError("ACR build result must be an object")
    for field in ("status", "provisioningState"):
        state = run.get(field)
        if not isinstance(state, str) or state.lower() != "succeeded":
            raise ValueError(f"ACR build did not succeed: {field}={state}")
    images = run.get("outputImages")
    if not isinstance(images, list) or len(images) != 1:
        raise ValueError("ACR build did not report exactly one output image")
    image = images[0]
    if not isinstance(image, dict):
        raise ValueError("ACR build output image must be an object")
    if image.get("repository") != repository or image.get("tag") != tag:
        raise ValueError(
            "ACR build produced a different image than requested: "
            f"{image.get('repository')}:{image.get('tag')}")
    digest = image.get("digest")
    if not isinstance(digest, str) or not DIGEST.fullmatch(digest):
        raise ValueError("ACR build did not report an immutable image digest")
    return digest


def main():
    if len(sys.argv) != 3:
        print("usage: build_image.py REPOSITORY TAG", file=sys.stderr)
        return 2
    try:
        digest = build_digest(sys.stdin.buffer.read(), sys.argv[1], sys.argv[2])
    except ValueError as error:
        print(str(error), file=sys.stderr)
        return 1
    print(digest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
