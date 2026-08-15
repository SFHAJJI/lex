#!/usr/bin/env python3
import json
import re
import sys


MAXIMUM_PAYLOAD_BYTES = 1024 * 1024
COMMIT = re.compile(r"^[0-9a-f]{40}$")
TICKET = re.compile(r"^[0-9a-f]{64}$")
TAG = re.compile(r"^index-(lu-legilux|eu-eurlex)-[0-9a-f]{64}$")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def object_value(value, message):
    require(isinstance(value, dict), message)
    return value


def manifest(payload, arguments):
    require(len(arguments) in (2, 3, 4),
            "manifest gate requires collection, ticket and optional corpus/index commits")
    collection, ticket = arguments[:2]
    require(collection in ("lu-legilux", "eu-eurlex") and TICKET.fullmatch(ticket),
            "manifest gate received an invalid collection or queue ticket")
    sources = object_value(payload.get("sources"),
                           "release manifest has no signed sources")
    require(sources.get("collection") == collection,
            "release manifest signed collection does not match")
    require(sources.get("queue_ticket_id") == ticket,
            "release manifest signed queue ticket does not match")
    corpus = sources.get("corpus_commit")
    require(isinstance(corpus, str) and COMMIT.fullmatch(corpus),
            "release manifest has no exact signed corpus commit")
    if len(arguments) == 3:
        require(COMMIT.fullmatch(arguments[2]) and corpus == arguments[2],
                "release manifest signed corpus commit does not match")
    if len(arguments) == 4:
        require(COMMIT.fullmatch(arguments[2]) and corpus == arguments[2],
                "release manifest signed corpus commit does not match")
        require(TICKET.fullmatch(arguments[3])
                and sources.get("index_manifest_sha256") == arguments[3],
                "benchmark manifest does not bind the exact index manifest")
    return corpus


def immutability(payload, arguments):
    require(not arguments, "immutability gate accepts no arguments")
    require(payload.get("enabled") is True,
            "publisher repository does not have immutable releases enabled")


def release(payload, arguments):
    require(len(arguments) == 2 and TAG.fullmatch(arguments[0])
            and COMMIT.fullmatch(arguments[1]),
            "release gate requires an exact release tag and corpus commit")
    tag, corpus = arguments
    require(payload.get("tag_name") == tag, "release API returned another tag")
    require(payload.get("target_commitish") == corpus,
            "release target does not match the signed corpus commit")
    require(payload.get("draft") is False and payload.get("prerelease") is False,
            "release is not final")
    require(payload.get("immutable") is True, "release is not immutable")


def tag_ref(payload, arguments):
    require(len(arguments) == 1 and COMMIT.fullmatch(arguments[0]),
            "tag-ref gate requires an exact corpus commit")
    target = object_value(payload.get("object"), "tag ref has no target object")
    require(target.get("type") == "commit" and target.get("sha") == arguments[0],
            "release tag does not directly target the signed corpus commit")


GATES = {
    "manifest": manifest,
    "immutability": immutability,
    "release": release,
    "tag-ref": tag_ref,
}


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in GATES:
        print(f"usage: release_artifact_gate.py {'|'.join(GATES)} [ARG ...]",
              file=sys.stderr)
        return 2
    raw = sys.stdin.buffer.read(MAXIMUM_PAYLOAD_BYTES + 1)
    if not raw or len(raw) > MAXIMUM_PAYLOAD_BYTES:
        print("release metadata is empty or exceeds its byte limit", file=sys.stderr)
        return 2
    try:
        payload = object_value(json.loads(raw), "release metadata must be an object")
        output = GATES[sys.argv[1]](payload, sys.argv[2:])
    except (UnicodeDecodeError, json.JSONDecodeError):
        print("release metadata is malformed", file=sys.stderr)
        return 2
    except ValueError as error:
        print(str(error), file=sys.stderr)
        return 1
    if output is not None:
        print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
