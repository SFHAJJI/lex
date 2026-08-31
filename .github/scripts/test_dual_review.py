"""Induced mutations for the immutable receipt gate.

Two kinds of test here, deliberately.

The pure cases exercise `evaluate` directly and are fast. The integration cases build
**real temporary Git repositories** and run the parser against actual commits, because
the previous design's fatal defect was exactly this: thirteen tests exercised a pure
function while the real path crashed on its first call. A receipt gate that has only
been tested against hand-built tuples has not been tested against Git.

Both of Codex's blocking findings against the comment-based design have named
regressions here: an arbitrary role pair must fail, and a product commit landing after
a receipt must turn the gate red again.
"""

import subprocess
import tempfile
import unittest
from pathlib import Path

from dual_review import evaluate, evaluate_head, parse_receipt, read_commit_message

CANDIDATE = "a" * 40
TREE = "b" * 40
OTHER = "c" * 40


def receipt(
    issue="333", writer="claude", reviewer="codex",
    commit=CANDIDATE, tree=TREE, verdict="READY",
):
    """A canonical receipt exactly as `read_commit_message` hands it over.

    No trailing newline. The loader's contract is the commit message's exact bytes
    minus exactly one terminal newline, so a pure test that appends one is testing a
    string the parser will never receive.
    """
    return (
        "lex-review/1\n"
        f"issue: {issue}\n"
        f"writer: {writer}\n"
        f"reviewer: {reviewer}\n"
        f"candidate-commit: {commit}\n"
        f"candidate-tree: {tree}\n"
        f"verdict: {verdict}"
    )


def labels(mapping):
    return lambda n: mapping.get(n)


GOOD_LABELS = labels({333: ["v3", "owner:claude", "reviewer:codex"]})
BODY = "<!-- lex-item issue=333 -->"


def check(message=None, head_tree=TREE, parents=None, body=BODY, lab=GOOD_LABELS):
    return evaluate(
        "d" * 40,
        head_tree,
        parents if parents is not None else [(CANDIDATE, TREE)],
        message if message is not None else receipt(),
        body,
        lab,
    )


class Accepts(unittest.TestCase):
    def test_an_exact_receipt_is_accepted(self):
        ok, msg = check()
        self.assertTrue(ok, msg)

    def test_roles_are_symmetric(self):
        ok, msg = check(
            message=receipt(writer="codex", reviewer="claude"),
            lab=labels({333: ["owner:codex", "reviewer:claude"]}),
        )
        self.assertTrue(ok, msg)


class CodexBlockingFindings(unittest.TestCase):
    """The two defects that killed the comment-based design."""

    def test_an_arbitrary_role_pair_is_refused(self):
        # O1: owner:not-a-co-owner + reviewer:codex false-passed before.
        ok, msg = check(message=receipt(writer="not-a-co-owner"))
        self.assertFalse(ok)
        self.assertIn("exactly the pair", msg)

    def test_a_product_commit_after_a_receipt_turns_the_gate_red(self):
        # O2: stale-green. A later push means HEAD is no longer a receipt.
        ok, msg = check(message="feat: an ordinary product change\n")
        self.assertFalse(ok)
        self.assertIn("exactly 7 lines", msg)


class RefusesStructurally(unittest.TestCase):
    def test_a_receipt_that_changes_content_is_refused(self):
        ok, msg = check(head_tree=OTHER)
        self.assertFalse(ok)
        self.assertIn("must add nothing", msg)

    def test_a_merge_head_is_refused(self):
        ok, msg = check(parents=[(CANDIDATE, TREE), (OTHER, TREE)])
        self.assertFalse(ok)
        self.assertIn("exactly one parent", msg)

    def test_a_root_commit_is_refused(self):
        ok, msg = check(parents=[])
        self.assertFalse(ok)
        self.assertIn("exactly one parent", msg)

    def test_a_receipt_naming_a_different_candidate_is_refused(self):
        ok, msg = check(message=receipt(commit=OTHER))
        self.assertFalse(ok)
        self.assertIn("is not the parent", msg)

    def test_a_receipt_naming_a_stale_tree_is_refused(self):
        ok, msg = check(message=receipt(tree=OTHER))
        self.assertFalse(ok)
        self.assertIn("does not match the reviewed tree", msg)


class RefusesMalformed(unittest.TestCase):
    def test_reordered_fields_are_refused(self):
        lines = receipt().split("\n")
        lines[2], lines[3] = lines[3], lines[2]
        ok, msg = check(message="\n".join(lines))
        self.assertFalse(ok)
        self.assertIn("ordered", msg)

    def test_a_duplicate_field_is_refused(self):
        ok, msg = check(message=receipt() + "verdict: READY\n")
        self.assertFalse(ok)
        self.assertIn("exactly", msg)

    def test_trailing_prose_is_refused(self):
        ok, msg = check(message=receipt() + "\nlooks good to me\n")
        self.assertFalse(ok)

    def test_wrong_case_is_refused(self):
        ok, msg = check(message=receipt().replace("verdict: READY", "verdict: Ready"))
        self.assertFalse(ok)
        self.assertIn("only READY", msg)

    def test_non_ascii_is_refused(self):
        ok, msg = check(message=receipt().replace("claude", "claudé"))
        self.assertFalse(ok)
        self.assertIn("non-ASCII", msg)

    def test_ambiguous_whitespace_is_refused(self):
        ok, msg = check(message=receipt().replace("issue: 333", "issue:  333"))
        self.assertFalse(ok)

    def test_a_short_sha_is_refused(self):
        ok, msg = check(message=receipt(commit="abc123"))
        self.assertFalse(ok)
        self.assertIn("40 lowercase hex", msg)

    def test_an_uppercase_sha_is_refused(self):
        ok, msg = check(message=receipt(commit=CANDIDATE.upper()))
        self.assertFalse(ok)

    def test_a_non_ready_verdict_is_refused(self):
        ok, msg = check(message=receipt(verdict="OBJECTION"))
        self.assertFalse(ok)
        self.assertIn("only READY", msg)


class RefusesAssignmentMismatch(unittest.TestCase):
    def test_a_missing_tracking_issue_is_refused(self):
        ok, msg = check(body="no marker")
        self.assertFalse(ok)
        self.assertIn("0 tracking issues", msg)

    def test_a_body_naming_a_different_issue_is_refused(self):
        ok, msg = check(body="<!-- lex-item issue=999 -->")
        self.assertFalse(ok)
        self.assertIn("but the body declares", msg)

    def test_labels_contradicting_the_receipt_are_refused(self):
        ok, msg = check(lab=labels({333: ["owner:codex", "reviewer:claude"]}))
        self.assertFalse(ok)
        self.assertIn("owner labels", msg)

    def test_an_unreadable_tracking_issue_is_refused(self):
        ok, msg = check(lab=labels({}))
        self.assertFalse(ok)
        self.assertIn("could not be read", msg)


class RealGitRepositories(unittest.TestCase):
    """Against actual commits, because a pure test proves nothing about Git."""

    @staticmethod
    def _git(repo, *args):
        return subprocess.run(
            ["git", "-C", str(repo), *args],
            capture_output=True, text=True, check=True,
        ).stdout.strip()

    def _repo(self, base):
        path = Path(base)
        self._git(path, "init", "-q", "-b", "main")
        self._git(path, "config", "user.email", "t@example.invalid")
        self._git(path, "config", "user.name", "t")
        (path / "file.txt").write_text("candidate content\n", encoding="utf-8")
        self._git(path, "add", "file.txt")
        self._git(path, "commit", "-q", "-m", "feat: the reviewed candidate")
        return path

    def _state(self, path, head="HEAD"):
        sha = self._git(path, "rev-parse", head)
        tree = self._git(path, "rev-parse", f"{head}^{{tree}}")
        parent_shas = self._git(path, "rev-list", "--parents", "-n", "1", head).split()[1:]
        parents = [(p, self._git(path, "rev-parse", f"{p}^{{tree}}")) for p in parent_shas]
        message = self._git(path, "log", "-1", "--format=%B", head)
        return sha, tree, parents, message

    def test_a_real_empty_receipt_commit_is_accepted(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo(tmp)
            candidate = self._git(path, "rev-parse", "HEAD")
            tree = self._git(path, "rev-parse", "HEAD^{tree}")
            self._git(
                path, "commit", "-q", "--allow-empty", "-m",
                receipt(commit=candidate, tree=tree),
            )
            sha, head_tree, parents, message = self._state(path)
            ok, msg = evaluate(sha, head_tree, parents, message, BODY, GOOD_LABELS)
            self.assertTrue(ok, msg)

    def test_a_real_nonempty_receipt_commit_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo(tmp)
            candidate = self._git(path, "rev-parse", "HEAD")
            tree = self._git(path, "rev-parse", "HEAD^{tree}")
            (path / "smuggled.txt").write_text("extra\n", encoding="utf-8")
            self._git(path, "add", "smuggled.txt")
            self._git(path, "commit", "-q", "-m", receipt(commit=candidate, tree=tree))
            sha, head_tree, parents, message = self._state(path)
            ok, msg = evaluate(sha, head_tree, parents, message, BODY, GOOD_LABELS)
            self.assertFalse(ok, "a receipt carrying content must be refused")

    def test_a_real_product_commit_after_a_receipt_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo(tmp)
            candidate = self._git(path, "rev-parse", "HEAD")
            tree = self._git(path, "rev-parse", "HEAD^{tree}")
            self._git(
                path, "commit", "-q", "--allow-empty", "-m",
                receipt(commit=candidate, tree=tree),
            )
            (path / "file.txt").write_text("changed after review\n", encoding="utf-8")
            self._git(path, "add", "file.txt")
            self._git(path, "commit", "-q", "-m", "feat: a later product change")
            sha, head_tree, parents, message = self._state(path)
            ok, msg = evaluate(sha, head_tree, parents, message, BODY, GOOD_LABELS)
            self.assertFalse(ok, "a push after a receipt must invalidate the gate")

    def test_a_real_merge_head_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo(tmp)
            self._git(path, "checkout", "-q", "-b", "side")
            (path / "side.txt").write_text("side\n", encoding="utf-8")
            self._git(path, "add", "side.txt")
            self._git(path, "commit", "-q", "-m", "feat: side")
            self._git(path, "checkout", "-q", "main")
            (path / "main.txt").write_text("main\n", encoding="utf-8")
            self._git(path, "add", "main.txt")
            self._git(path, "commit", "-q", "-m", "feat: main")
            self._git(path, "merge", "-q", "--no-ff", "side", "-m", "merge")
            sha, head_tree, parents, message = self._state(path)
            ok, msg = evaluate(sha, head_tree, parents, message, BODY, GOOD_LABELS)
            self.assertFalse(ok, "a merge head is not a receipt")


class ProductionLoaderBoundary(unittest.TestCase):
    """Codex O2: the loader must not canonicalise a malformed attestation.

    An earlier loader used a whitespace-stripping helper, so forbidden leading blank
    lines, trailing tabs, carriage returns and extra blank lines were normalised into a
    well-formed seven-line message before the parser saw them. The fail-closed rule was
    true of the parser and false of the program.

    These run through the PRODUCTION reader against real commit objects, written with
    `--cleanup=verbatim` so the bytes survive exactly. A pure `parse_receipt` test
    cannot reach this boundary, which is precisely why the defect lived.
    """

    @staticmethod
    def _git(repo, *args):
        return subprocess.run(
            ["git", "-C", str(repo), *args], capture_output=True, text=True, check=True
        ).stdout.strip()

    def _repo_with_message(self, base, raw_message):
        path = Path(base)
        self._git(path, "init", "-q", "-b", "main")
        self._git(path, "config", "user.email", "t@example.invalid")
        self._git(path, "config", "user.name", "t")
        (path / "file.txt").write_text("candidate\n", encoding="utf-8")
        self._git(path, "add", "file.txt")
        self._git(path, "commit", "-q", "-m", "feat: the reviewed candidate")
        candidate = self._git(path, "rev-parse", "HEAD")
        tree = self._git(path, "rev-parse", "HEAD^{tree}")
        message = raw_message.replace("__COMMIT__", candidate).replace("__TREE__", tree)
        msg_file = path / "msg.txt"
        msg_file.write_bytes(message.encode("utf-8"))
        self._git(
            path, "commit", "-q", "--allow-empty", "--cleanup=verbatim",
            "-F", str(msg_file),
        )
        return path

    def _via_production_path(self, path):
        """Through evaluate_head, the one route from a commit to a verdict."""
        head = self._git(path, "rev-parse", "HEAD")
        return evaluate_head(head, BODY, GOOD_LABELS, cwd=path)

    GOOD = (
        "lex-review/1\n"
        "issue: 333\n"
        "writer: claude\n"
        "reviewer: codex\n"
        "candidate-commit: __COMMIT__\n"
        "candidate-tree: __TREE__\n"
        "verdict: READY\n"
    )

    def test_an_exact_receipt_survives_the_production_loader(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo_with_message(tmp, self.GOOD)
            ok, reason = self._via_production_path(path)
            self.assertTrue(ok, reason)

    def test_a_leading_blank_line_is_refused_through_the_real_loader(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo_with_message(tmp, "\n" + self.GOOD)
            ok, reason = self._via_production_path(path)
            self.assertFalse(ok, "a leading blank line must not be normalised away")

    def test_extra_trailing_blank_lines_are_refused_through_the_real_loader(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo_with_message(tmp, self.GOOD + "\n\n")
            ok, reason = self._via_production_path(path)
            self.assertFalse(ok, "extra trailing blank lines must not be stripped")

    def test_a_trailing_tab_is_refused_through_the_real_loader(self):
        bad = self.GOOD.replace("verdict: READY\n", "verdict: READY\t\n")
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo_with_message(tmp, bad)
            ok, reason = self._via_production_path(path)
            self.assertFalse(ok, "a trailing tab must reach the parser")

    def test_a_carriage_return_is_refused_through_the_real_loader(self):
        bad = self.GOOD.replace("issue: 333\n", "issue: 333\r\n")
        with tempfile.TemporaryDirectory() as tmp:
            path = self._repo_with_message(tmp, bad)
            ok, reason = self._via_production_path(path)
            self.assertFalse(ok, "a carriage return must reach the parser")


if __name__ == "__main__":
    unittest.main(verbosity=2)
