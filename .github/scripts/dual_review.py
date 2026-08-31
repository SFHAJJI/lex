"""Verify an immutable lex-review/1 receipt commit at a pull request head.

Both machine co-owners authenticate as one GitHub account, so actor identity cannot
distinguish them and GitHub's own review mechanism would deadlock: an account cannot
approve its own pull request. An earlier design read a verdict from pull request
comments. Two holes closed that approach:

  * arbitrary distinct `owner:*` / `reviewer:*` labels false-passed, so a hostile
    `owner:not-a-co-owner` plus `reviewer:codex` pair satisfied the gate;
  * comment truth is mutable and does not re-trigger a head workflow, so an edited,
    deleted or superseded verdict could remain green indefinitely.

The receipt is a commit instead. That makes the attestation immutable and self
invalidating: it names the exact candidate commit and tree, it must add no content,
and any later product commit makes HEAD cease to be a receipt, failing the gate until
the non-writer reviews again and creates a fresh one. No comment is parsed for gate
state; comments remain public evidence only.

This is honest about its limit. A shared account means either agent could author a
receipt naming the other as reviewer. That is a deliberate, immutable, published
falsification rather than the silent omission this exists to prevent, and it becomes
cryptographic attribution once the Decision 41 identity migration lands.

`evaluate` is pure so every failure mode is exercised without touching GitHub.
Exit 0 to pass, 1 to fail.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys

AGENTS = ("claude", "codex")
HEADER = "lex-review/1"
FIELDS = ("issue", "writer", "reviewer", "candidate-commit", "candidate-tree", "verdict")
SHA_RE = re.compile(r"\A[0-9a-f]{40}\Z")
ISSUE_RE = re.compile(r"\A[1-9][0-9]*\Z")
ITEM_RE = re.compile(r"<!--\s*lex-item\s+issue=(\d+)\s*-->", re.IGNORECASE)


def parse_receipt(message: str):
    """Parse a canonical receipt message, or return (None, reason).

    Fail-closed on duplicate fields, reordered fields, extra text, wrong case,
    whitespace ambiguity and non-ASCII. The message is an attestation, so a message
    that is merely *close* to canonical is refused rather than repaired.
    """
    if not isinstance(message, str) or not message:
        return None, "the head commit has no message"
    if not message.isascii():
        return None, "the receipt message contains non-ASCII characters"
    if "\t" in message or "\r" in message:
        return None, "the receipt message contains tab or carriage-return whitespace"

    # No stripping here. `read_commit_message` hands over exact bytes minus one
    # terminal newline, so any other leading or trailing whitespace is a defect in
    # the attestation and must be refused rather than repaired.
    lines = message.split("\n")
    if len(lines) != len(FIELDS) + 1:
        return None, (
            f"the receipt must be exactly {len(FIELDS) + 1} lines, found {len(lines)}; "
            "extra prose is not permitted in an attestation"
        )
    if lines[0] != HEADER:
        return None, f"the first line must be exactly {HEADER!r}, found {lines[0]!r}"

    values = {}
    for index, name in enumerate(FIELDS, start=1):
        line = lines[index]
        prefix = f"{name}: "
        if not line.startswith(prefix):
            return None, (
                f"line {index + 1} must begin {prefix!r}; fields are ordered and named "
                f"exactly, found {line!r}"
            )
        value = line[len(prefix):]
        if value != value.strip() or "  " in value:
            return None, f"{name}: value has ambiguous whitespace"
        if not value:
            return None, f"{name}: value is empty"
        values[name] = value

    if not ISSUE_RE.match(values["issue"]):
        return None, f"issue: {values['issue']!r} is not a decimal issue number"
    for name in ("candidate-commit", "candidate-tree"):
        if not SHA_RE.match(values[name]):
            return None, f"{name}: {values[name]!r} is not 40 lowercase hex characters"
    if values["verdict"] != "READY":
        return None, f"verdict: only READY is accepted, found {values['verdict']!r}"

    writer, reviewer = values["writer"], values["reviewer"]
    if {writer, reviewer} != set(AGENTS):
        return None, (
            f"writer/reviewer must be exactly the pair {AGENTS}, found "
            f"({writer!r}, {reviewer!r}); an arbitrary role name is not a co-owner"
        )
    if writer == reviewer:
        return None, "writer and reviewer must be opposite agents"

    return values, None


def evaluate(head_sha, head_tree, parents, message, pr_body, issue_labels_for):
    """Decide whether the head is a valid receipt. Pure: no IO, no environment."""
    receipt, reason = parse_receipt(message)
    if receipt is None:
        return False, reason

    if len(parents) != 1:
        return False, (
            f"the receipt must have exactly one parent, found {len(parents)}; "
            "a merge or root commit cannot attest a candidate"
        )
    parent_sha, parent_tree = parents[0]

    if head_tree != parent_tree:
        return False, (
            "the receipt changes content: its tree differs from its parent's. A review "
            "attestation must add nothing."
        )
    if receipt["candidate-commit"] != parent_sha:
        return False, (
            f"candidate-commit {receipt['candidate-commit'][:8]} is not the parent "
            f"{parent_sha[:8]}; the receipt does not attest the commit beneath it"
        )
    if receipt["candidate-tree"] != parent_tree or receipt["candidate-tree"] != head_tree:
        return False, (
            f"candidate-tree {receipt['candidate-tree'][:8]} does not match the reviewed "
            f"tree {parent_tree[:8]}"
        )

    items = ITEM_RE.findall(pr_body or "")
    if len(items) != 1:
        return False, (
            f"the pull request body declares {len(items)} tracking issues; exactly one "
            "<!-- lex-item issue=N --> is required"
        )
    if items[0] != receipt["issue"]:
        return False, (
            f"the receipt attests issue {receipt['issue']} but the body declares "
            f"{items[0]}"
        )

    labels = issue_labels_for(int(receipt["issue"]))
    if labels is None:
        return False, f"tracking issue #{receipt['issue']} could not be read"
    owners = sorted(n.split(":", 1)[1] for n in labels if n.startswith("owner:"))
    reviewers = sorted(n.split(":", 1)[1] for n in labels if n.startswith("reviewer:"))
    if owners != [receipt["writer"]]:
        return False, (
            f"issue #{receipt['issue']} carries owner labels {owners}, but the receipt "
            f"names writer {receipt['writer']!r}"
        )
    if reviewers != [receipt["reviewer"]]:
        return False, (
            f"issue #{receipt['issue']} carries reviewer labels {reviewers}, but the "
            f"receipt names reviewer {receipt['reviewer']!r}"
        )

    return True, (
        f"receipt valid: issue #{receipt['issue']}, {receipt['writer']} wrote, "
        f"{receipt['reviewer']} reviewed {parent_sha[:8]}"
    )


def _git(*args):
    """Text git output with surrounding whitespace stripped.

    Safe for identities like a SHA. NEVER use it for the receipt message: stripping is
    exactly the normalisation that would repair a malformed attestation before the
    parser could refuse it. Use `read_commit_message` for that.
    """
    return subprocess.run(
        ["git", *args], capture_output=True, text=True, check=True
    ).stdout.strip()


def read_commit_message(sha, cwd=None):
    """The commit object's message bytes, exactly, or (None, reason).

    Reads the raw object rather than a formatted view, decodes ASCII strictly, and
    removes exactly one canonical terminal newline. Every other leading or trailing
    byte is preserved so the parser can refuse it.

    This exists because the previous loader used a whitespace-stripping helper, which
    silently canonicalised forbidden leading blank lines, trailing tabs, carriage
    returns and extra blank lines into a well-formed seven-line message. The parser's
    fail-closed rule was therefore true of the parser and false of the program.
    """
    raw = subprocess.run(
        ["git"] + (["-C", str(cwd)] if cwd else []) + ["cat-file", "commit", sha],
        capture_output=True,
        check=True,
    ).stdout
    # A commit object is headers, one blank line, then the message verbatim.
    separator = raw.find(b"\n\n")
    if separator < 0:
        return None, "the commit object has no message section"
    body = raw[separator + 2:]
    try:
        text = body.decode("ascii", errors="strict")
    except UnicodeDecodeError:
        return None, "the receipt message is not strictly ASCII"
    if not text.endswith("\n"):
        return None, "the receipt message lacks its single terminal newline"
    return text[:-1], None


def _gh_json(path: str):
    out = subprocess.run(
        ["gh", "api", path, "--paginate", "--slurp"],
        capture_output=True,
        text=True,
        check=True,
    ).stdout
    pages = json.loads(out)
    if isinstance(pages, list) and len(pages) == 1:
        return pages[0]
    return pages


def evaluate_head(head, pr_body, issue_labels_for, cwd=None):
    """Read the head from Git and evaluate it. THE only production path.

    `main` is a thin shell over this, and the tests call this, so there is exactly one
    route from a commit to a verdict. An earlier shape let `main` read the message its
    own way while tests exercised the reader directly; reverting `main` to a
    whitespace-stripping helper then changed the program's behaviour without failing a
    single test. One path means a mutation anywhere in it is caught.
    """
    def git(*args):
        return subprocess.run(
            ["git"] + (["-C", str(cwd)] if cwd else []) + list(args),
            capture_output=True, text=True, check=True,
        ).stdout.strip()

    head_tree = git("rev-parse", f"{head}^{{tree}}")
    parent_shas = git("rev-list", "--parents", "-n", "1", head).split()[1:]
    parents = [(p, git("rev-parse", f"{p}^{{tree}}")) for p in parent_shas]
    message, read_error = read_commit_message(head, cwd=cwd)
    if read_error is not None:
        return False, read_error
    return evaluate(head, head_tree, parents, message, pr_body, issue_labels_for)


def main() -> None:
    repo = os.environ["REPO"]
    number = os.environ["PR_NUMBER"]
    head = os.environ["HEAD_SHA"]

    pr = _gh_json(f"repos/{repo}/pulls/{number}")

    def issue_labels_for(n: int):
        try:
            issue = _gh_json(f"repos/{repo}/issues/{n}")
        except subprocess.CalledProcessError:
            return None
        return [lbl["name"] for lbl in issue.get("labels", [])]

    ok, message_out = evaluate_head(head, pr.get("body") or "", issue_labels_for)
    if not ok:
        print(f"::error::{message_out}")
        sys.exit(1)
    print(message_out)


if __name__ == "__main__":
    main()
