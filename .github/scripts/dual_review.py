"""Verify that a pull request carries a SHA-bound verdict from the opposite co-owner.

Both machine co-owners authenticate as one GitHub account, so actor identity cannot
distinguish them and GitHub's own review mechanism would deadlock: an account cannot
approve its own pull request. The gate therefore lives in a status check, which is
evaluated on a result rather than on an actor.

Truth lives in the tracking issue, not in the pull request body. The body names its
issue; the issue's `owner:*` and `reviewer:*` labels decide who must review. This
keeps GitHub issues as the single backlog rather than creating a second role registry
in free-form prose.

The repository is public, so anyone may comment. Only comments authored by the
repository owner are considered, and a verdict comment's entire trimmed body must be
one canonical marker. A marker quoted inside prose is ignored: on a public repository
counting it would let a stranger forge a READY, and rejecting it would let a stranger
hold the gate red forever.

A verdict binds to the exact head commit. A push invalidates it, because the recorded
SHA no longer matches.

This defends against forgetting, not against forgery. Under a shared account either
agent could write the other's marker, but that is a deliberate, timestamped, public
falsification rather than the silent omission this exists to prevent. After the
Decision 41 identity migration the owner-login rule is replaced by the distinct
reviewer identity.

`evaluate` is pure, so every failure mode is exercised without touching GitHub.
Exit 0 to pass, 1 to fail.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys

ITEM_RE = re.compile(r"<!--\s*lex-item\s+issue=(?P<issue>\d+)\s*-->", re.IGNORECASE)

# A verdict comment's whole trimmed body must be exactly this. Nothing else.
VERDICT_FULL_RE = re.compile(
    r"\A<!--\s*lex-verdict\s+agent=(?P<agent>[A-Za-z][A-Za-z0-9_-]*)\s+"
    r"sha=(?P<sha>[0-9a-fA-F]{40})\s+"
    r"verdict=(?P<verdict>READY|OBJECTION)\s*-->\Z",
    re.IGNORECASE,
)

OWNER_LABEL_RE = re.compile(r"\Aowner:(?P<agent>[a-z][a-z0-9_-]*)\Z")
REVIEWER_LABEL_RE = re.compile(r"\Areviewer:(?P<agent>[a-z][a-z0-9_-]*)\Z")


def parse_verdicts(comments, owner_login: str):
    """Canonical verdicts from the repository owner only, in chronological order.

    `comments` is a sequence of mappings with `body` and `user.login`. Anything from
    another author, or whose body is not exactly one marker, is ignored rather than
    rejected: on a public repository, rejecting would be a denial of service.
    """
    found = []
    for c in comments:
        login = ((c.get("user") or {}).get("login") or "").lower()
        if login != owner_login.lower():
            continue
        m = VERDICT_FULL_RE.match((c.get("body") or "").strip())
        if not m:
            continue
        found.append(
            {
                "agent": m.group("agent").lower(),
                "sha": m.group("sha").lower(),
                "verdict": m.group("verdict").upper(),
            }
        )
    return found


def roles_from_labels(label_names):
    """The (owner, reviewer) pair a tracking issue declares, or an error string."""
    owners = sorted({m.group("agent") for n in label_names if (m := OWNER_LABEL_RE.match(n))})
    reviewers = sorted(
        {m.group("agent") for n in label_names if (m := REVIEWER_LABEL_RE.match(n))}
    )
    if len(owners) != 1:
        return None, None, (
            f"the tracking issue carries {len(owners)} owner:* labels; exactly one is "
            "required so the writer is unambiguous."
        )
    if len(reviewers) != 1:
        return None, None, (
            f"the tracking issue carries {len(reviewers)} reviewer:* labels; exactly "
            "one is required so the reviewer is unambiguous."
        )
    if owners[0] == reviewers[0]:
        return None, None, (
            f"the tracking issue names {owners[0]!r} as both owner and reviewer; "
            "dual review requires two distinct agents."
        )
    return owners[0], reviewers[0], None


def evaluate(head: str, pr_body: str, comments, owner_login: str, issue_labels_for):
    """Decide whether dual review is satisfied. Pure: no IO, no environment.

    `issue_labels_for(number)` returns that issue's label names, or None if unknown.
    """
    head = (head or "").lower()

    items = ITEM_RE.findall(pr_body or "")
    if len(items) != 1:
        return False, (
            f"the pull request body declares {len(items)} tracking issues; exactly one "
            "is required. Add <!-- lex-item issue=<number> --> naming the backlog item, "
            "whose owner:* and reviewer:* labels decide who must review."
        )
    issue_number = int(items[0])

    labels = issue_labels_for(issue_number)
    if labels is None:
        return False, f"tracking issue #{issue_number} could not be read."

    owner, reviewer, err = roles_from_labels(labels)
    if err:
        return False, f"issue #{issue_number}: {err}"

    verdicts = parse_verdicts(comments, owner_login)
    mine = [v for v in verdicts if v["agent"] == reviewer]
    if not mine:
        return False, (
            f"no verdict from the declared reviewer {reviewer!r} on issue "
            f"#{issue_number}. Post a comment whose entire body is "
            f"<!-- lex-verdict agent={reviewer} sha={head} verdict=READY -->."
        )

    at_head = [v for v in mine if v["sha"] == head]
    if not at_head:
        stale = ", ".join(sorted({v["sha"][:8] for v in mine}))
        return False, (
            f"{reviewer} reviewed {stale} but the head is now {head[:8]}. A verdict is "
            "bound to the exact commit it reviewed; a push invalidates it."
        )

    outcomes = {v["verdict"] for v in at_head}
    if len(outcomes) > 1:
        return False, (
            f"{reviewer} recorded conflicting verdicts on {head[:8]}: "
            f"{sorted(outcomes)}. Resolve by pushing a new commit, or by deleting the "
            "superseded comment so exactly one verdict stands."
        )
    if outcomes != {"READY"}:
        return False, f"{reviewer} recorded {sorted(outcomes)[0]} on the current head."

    return True, (
        f"dual review satisfied: issue #{issue_number}, owner={owner}, "
        f"reviewer={reviewer}, head={head[:8]}"
    )


def _gh_json(path: str):
    """One parsed JSON value from the GitHub API.

    `--slurp` makes --paginate emit a single well-formed array of pages instead of
    concatenated documents, so this is a real parse rather than a regex over text.
    """
    out = subprocess.run(
        ["gh", "api", path, "--paginate", "--slurp"],
        capture_output=True,
        text=True,
        check=True,
    ).stdout
    pages = json.loads(out)
    if isinstance(pages, list) and pages and all(isinstance(p, list) for p in pages):
        return [item for page in pages for item in page]
    if isinstance(pages, list) and len(pages) == 1:
        return pages[0]
    return pages


def main() -> None:
    repo = os.environ["REPO"]
    number = os.environ["PR_NUMBER"]
    owner_login = repo.split("/")[0]

    pr = _gh_json(f"repos/{repo}/pulls/{number}")
    comments = _gh_json(f"repos/{repo}/issues/{number}/comments")

    def issue_labels_for(n: int):
        try:
            issue = _gh_json(f"repos/{repo}/issues/{n}")
        except subprocess.CalledProcessError:
            return None
        return [lbl["name"] for lbl in issue.get("labels", [])]

    ok, message = evaluate(
        pr["head"]["sha"], pr.get("body") or "", comments, owner_login, issue_labels_for
    )
    if not ok:
        print(f"::error::{message}")
        sys.exit(1)
    print(message)


if __name__ == "__main__":
    main()
