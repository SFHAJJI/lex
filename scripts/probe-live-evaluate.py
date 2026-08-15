#!/usr/bin/env python3
"""Decode one MCP response and evaluate a repository-owned live probe."""

from __future__ import annotations

import json
import sys
from typing import Any


def decode_mcp_body(raw: str) -> Any:
    data_events = [
        line.removeprefix("data:").lstrip()
        for line in raw.splitlines()
        if line.startswith("data:")
    ]
    wire_body = data_events[-1] if data_events else raw
    envelope = json.loads(wire_body)
    return json.loads(envelope["result"]["content"][0]["text"])


def exact_publishers(pubs: list[Any]) -> bool:
    return len(pubs) == 2 and {
        publisher.get("envelope", {}).get("publisher") for publisher in pubs
    } == {"eu-eurlex", "lu-legilux"}


def evaluate(raw: str, expression: str) -> str:
    try:
        data = decode_mcp_body(raw)
    except Exception as error:  # The probe must fail closed on malformed transport data.
        return f"FAIL (unparseable: {error})"

    pubs = data if isinstance(data, list) else [data]
    globals_ = {
        "__builtins__": {},
        "all": all,
        "any": any,
        "exact_publishers": exact_publishers,
        "isinstance": isinstance,
        "len": len,
        "list": list,
        "sum": sum,
        "pubs": pubs,
    }
    try:
        return "PASS" if eval(expression, globals_, {}) else "FAIL"
    except Exception as error:  # The checked-in expression itself is part of the probe contract.
        return f"FAIL (probe error: {error})"


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: probe-live-evaluate.py <expression>")
    print(evaluate(sys.stdin.read(), sys.argv[1]))
