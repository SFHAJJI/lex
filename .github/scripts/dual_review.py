"""Verify that a pull request carries a SHA-bound verdict from the non-author agent.

Both machine co-owners authenticate as one GitHub account, so actor identity cannot
distinguish them. Declaration is explicit instead:

  In the pull request body:      <!-- lex-author agent=codex -->
  In a review comment:           <!-- lex-verdict agent=claude sha=<40 hex> verdict=READY -->

The gate passes when a READY verdict exists from an agent other than the declared
author, bound to the exact current head commit. A new push invalidates it, because
the recorded SHA no longer matches.

This defends against forgetting, not against forgery. A shared account means either
agent could write the other's marker; doing so would be a deliberate, timestamped,
public falsification rather than the silent omission this exists to prevent.

`evaluate` is pure so every failure mode can be exercised by a test without GitHub.
Exit 0 to pass, 1 to fail. Every failure names what is missing, not merely that
something is.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys

AGENTS = frozenset({"claude", "codex"})

AUTHOR_RE = re.compile(
    r"<!--\s*lex-author\s+agent=(?P<agent>[A-Za-z]+)\s*-->", re.IGNORECASE
)
VERDICT_RE = re.compile(
    r"<!--\s*lex-verdict\s+agent=(?P<agent>[A-Za-z]+)\s+"
    r"sha=(?P<sha>[0-9a-fA-F]{40})\s+"
    r"verdict=(?P<verdict>[A-Za-z_]+)\s*-->",
    re.IGNORECASE,
)


def parse_verdicts(bodies):
    """Every verdict marker found across the given comment bodies, in order."""
    found = []
    for body in bodies:
        for m in VERDICT_RE.finditer(body or ""):
            found.append(
                {
                    "agent": m.group("agent").lower(),
                    "sha": m.group("sha").lower(),
                    "verdict": m.group("verdict").upper(),
                }
            )
    return found


def evaluate(head: str, pr_body: str, comment_bodies) -> tuple[bool, str]:
    """Decide whether dual review is satisfied. Pure: no IO, no environment."""
    head = (head or "").lower()

    author_match = AUTHOR_RE.search(pr_body or "")
    if not author_match:
        return False, (
            "the pull request body declares no author agent. Add "
            "<!-- lex-author agent=claude --> or agent=codex so the reviewing "
            "agent can be identified as the other one."
        )
    author = author_match.group("agent").lower()
    if author not in AGENTS:
        return False, f"unknown author agent {author!r}; expected one of {sorted(AGENTS)}."

    verdicts = parse_verdicts(comment_bodies)
    for v in verdicts:
        if v["agent"] not in AGENTS:
            return False, (
                f"verdict from unknown agent {v['agent']!r}; expected one of "
                f"{sorted(AGENTS)}."
            )

    others = sorted({v["agent"] for v in verdicts} - {author})
    if not others:
        return False, (
            f"no verdict from an agent other than the declared author {author!r}. "
            "The other co-owner must post "
            f"<!-- lex-verdict agent=<other> sha={head} verdict=READY -->."
        )

    for agent in others:
        for_agent = [v for v in verdicts if v["agent"] == agent]
        at_head = [v for v in for_agent if v["sha"] == head]
        if not at_head:
            stale = ", ".join(sorted({v["sha"][:8] for v in for_agent}))
            return False, (
                f"{agent} reviewed {stale} but the head is now {head[:8]}. A verdict "
                "is bound to the exact commit it reviewed; a push invalidates it. "
                "Re-review the current head."
            )
        latest = at_head[-1]
        if latest["verdict"] != "READY":
            return False, f"{agent} recorded {latest['verdict']} on the current head."

    return True, f"dual review satisfied: author={author}, reviewers={others}"


def _gh(path: str):
    out = subprocess.run(
        ["gh", "api", path, "--paginate"], capture_output=True, text=True, check=True
    ).stdout
    chunks = [json.loads(c) for c in re.findall(r"\[.*?\]|\{.*\}", out, re.DOTALL)]
    if not chunks:
        return json.loads(out)
    if all(isinstance(c, list) for c in chunks):
        return [item for c in chunks for item in c]
    return chunks[0]


def main() -> None:
    repo = os.environ["REPO"]
    number = os.environ["PR_NUMBER"]
    pr = _gh(f"repos/{repo}/pulls/{number}")
    comments = _gh(f"repos/{repo}/issues/{number}/comments")
    ok, message = evaluate(
        pr["head"]["sha"], pr.get("body") or "", [c.get("body") or "" for c in comments]
    )
    if not ok:
        print(f"::error::{message}")
        sys.exit(1)
    print(message)


if __name__ == "__main__":
    main()
