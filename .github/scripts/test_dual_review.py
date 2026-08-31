"""Induced mutations for the dual-review gate.

A gate that has never been shown red is a name, not a gate. Each test below breaks
one property on purpose and asserts the check refuses. The final test asserts the
one arrangement that must pass, so the suite cannot be satisfied by a check that
simply always fails.
"""

import unittest

from dual_review import evaluate

HEAD = "a" * 40
OLD = "b" * 40
BY_CODEX = "<!-- lex-author agent=codex -->"
BY_CLAUDE = "<!-- lex-author agent=claude -->"


def verdict(agent: str, sha: str, v: str = "READY") -> str:
    return f"<!-- lex-verdict agent={agent} sha={sha} verdict={v} -->"


class DualReviewGate(unittest.TestCase):
    def test_passes_when_the_other_agent_approved_this_exact_head(self):
        ok, msg = evaluate(HEAD, BY_CODEX, [verdict("claude", HEAD)])
        self.assertTrue(ok, msg)

    def test_rejects_when_the_body_declares_no_author(self):
        ok, msg = evaluate(HEAD, "no marker here", [verdict("claude", HEAD)])
        self.assertFalse(ok)
        self.assertIn("no author agent", msg)

    def test_rejects_when_no_verdict_exists_at_all(self):
        ok, msg = evaluate(HEAD, BY_CODEX, [])
        self.assertFalse(ok)
        self.assertIn("no verdict", msg)

    def test_rejects_a_self_review_by_the_declared_author(self):
        # The whole point: codex authored it, so only codex approving is not review.
        ok, msg = evaluate(HEAD, BY_CODEX, [verdict("codex", HEAD)])
        self.assertFalse(ok)
        self.assertIn("other than the declared author", msg)

    def test_rejects_a_verdict_bound_to_a_superseded_commit(self):
        # This is the stale-approval hole: approved, then pushed.
        ok, msg = evaluate(HEAD, BY_CODEX, [verdict("claude", OLD)])
        self.assertFalse(ok)
        self.assertIn("head is now", msg)

    def test_rejects_an_objection_on_the_current_head(self):
        ok, msg = evaluate(HEAD, BY_CODEX, [verdict("claude", HEAD, "OBJECTION")])
        self.assertFalse(ok)
        self.assertIn("OBJECTION", msg)

    def test_rejects_an_unknown_reviewing_agent(self):
        ok, msg = evaluate(HEAD, BY_CODEX, [verdict("somebody", HEAD)])
        self.assertFalse(ok)
        self.assertIn("unknown agent", msg)

    def test_rejects_an_unknown_declared_author(self):
        ok, msg = evaluate(HEAD, "<!-- lex-author agent=nobody -->", [verdict("claude", HEAD)])
        self.assertFalse(ok)
        self.assertIn("unknown author agent", msg)

    def test_a_later_objection_on_the_same_head_supersedes_an_earlier_ready(self):
        ok, msg = evaluate(
            HEAD, BY_CODEX, [verdict("claude", HEAD), verdict("claude", HEAD, "OBJECTION")]
        )
        self.assertFalse(ok)
        self.assertIn("OBJECTION", msg)

    def test_a_later_ready_on_the_same_head_supersedes_an_earlier_objection(self):
        ok, msg = evaluate(
            HEAD, BY_CODEX, [verdict("claude", HEAD, "OBJECTION"), verdict("claude", HEAD)]
        )
        self.assertTrue(ok, msg)

    def test_the_roles_are_symmetric(self):
        # Claude authoring and Codex reviewing must work identically.
        ok, msg = evaluate(HEAD, BY_CLAUDE, [verdict("codex", HEAD)])
        self.assertTrue(ok, msg)

    def test_case_and_whitespace_in_markers_do_not_defeat_the_gate(self):
        marker = f"<!--   lex-verdict   agent=CLAUDE   sha={HEAD.upper()}   verdict=ready   -->"
        ok, msg = evaluate(HEAD, BY_CODEX, [marker])
        self.assertTrue(ok, msg)

    def test_a_verdict_quoted_inside_prose_still_counts(self):
        body = f"Reviewed the acquisition boundary.\n\n{verdict('claude', HEAD)}\n\nNo objections."
        ok, msg = evaluate(HEAD, BY_CODEX, [body])
        self.assertTrue(ok, msg)


if __name__ == "__main__":
    unittest.main(verbosity=2)
