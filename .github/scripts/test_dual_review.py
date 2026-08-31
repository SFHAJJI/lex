"""Induced mutations for the dual-review gate.

A gate that has never been shown red is a name, not a gate. Each test breaks one
property on purpose and asserts the check refuses; the passing cases exist so the
suite cannot be satisfied by a check that simply always fails.

Every case Codex demonstrated against the first implementation is here as a named
regression: a marker quoted inside prose, a marker from a stranger, two author
declarations, a malformed marker beside a valid one, and OBJECTION followed by READY
on the same commit.
"""

import json
import subprocess
import unittest
from unittest import mock

from dual_review import evaluate, parse_verdicts, roles_from_labels, _gh_json

HEAD = "a" * 40
OLD = "b" * 40
OWNER = "SFHAJJI"

CLAUDE_WRITES = ["v3", "owner:claude", "reviewer:codex"]
CODEX_WRITES = ["v3", "owner:codex", "reviewer:claude"]

BODY = "<!-- lex-item issue=333 -->"


def marker(agent: str, sha: str, v: str = "READY") -> str:
    return f"<!-- lex-verdict agent={agent} sha={sha} verdict={v} -->"


def comment(body: str, login: str = OWNER) -> dict:
    return {"body": body, "user": {"login": login}}


def labels(mapping):
    return lambda n: mapping.get(n)


class Passing(unittest.TestCase):
    def test_reviewer_ready_on_this_exact_head(self):
        ok, msg = evaluate(
            HEAD, BODY, [comment(marker("codex", HEAD))], OWNER, labels({333: CLAUDE_WRITES})
        )
        self.assertTrue(ok, msg)

    def test_roles_are_symmetric(self):
        ok, msg = evaluate(
            HEAD, BODY, [comment(marker("claude", HEAD))], OWNER, labels({333: CODEX_WRITES})
        )
        self.assertTrue(ok, msg)

    def test_unrelated_owner_comments_do_not_interfere(self):
        cs = [comment("Looks good to me."), comment(marker("codex", HEAD)), comment("thanks")]
        ok, msg = evaluate(HEAD, BODY, cs, OWNER, labels({333: CLAUDE_WRITES}))
        self.assertTrue(ok, msg)

    def test_marker_case_and_whitespace_do_not_defeat_the_gate(self):
        m = f"<!--   lex-verdict   agent=CODEX   sha={HEAD.upper()}   verdict=ready   -->"
        ok, msg = evaluate(HEAD, BODY, [comment(m)], OWNER, labels({333: CLAUDE_WRITES}))
        self.assertTrue(ok, msg)


class PublicRepositoryAttacks(unittest.TestCase):
    """The repository is public, so anyone may comment."""

    def test_a_stranger_cannot_forge_a_ready(self):
        cs = [comment(marker("codex", HEAD), login="a-passer-by")]
        ok, msg = evaluate(HEAD, BODY, cs, OWNER, labels({333: CLAUDE_WRITES}))
        self.assertFalse(ok)
        self.assertIn("no verdict", msg)

    def test_a_stranger_cannot_hold_the_gate_red(self):
        # An unknown-agent marker from a stranger must be IGNORED, not rejected:
        # rejecting it would be a denial of service on a public repository.
        cs = [
            comment(marker("nobody", HEAD), login="a-passer-by"),
            comment(marker("codex", HEAD)),
        ]
        ok, msg = evaluate(HEAD, BODY, cs, OWNER, labels({333: CLAUDE_WRITES}))
        self.assertTrue(ok, msg)

    def test_a_marker_quoted_inside_prose_does_not_count(self):
        body = f"Codex said:\n\n> {marker('codex', HEAD)}\n\nso we are fine."
        ok, msg = evaluate(HEAD, BODY, [comment(body)], OWNER, labels({333: CLAUDE_WRITES}))
        self.assertFalse(ok)
        self.assertIn("no verdict", msg)

    def test_a_malformed_marker_beside_a_valid_one_does_not_pass(self):
        cs = [comment(f"{marker('codex', HEAD)} plus trailing prose")]
        ok, msg = evaluate(HEAD, BODY, cs, OWNER, labels({333: CLAUDE_WRITES}))
        self.assertFalse(ok)


class TrackingIssueIsTheTruth(unittest.TestCase):
    def test_rejects_when_no_tracking_issue_is_declared(self):
        ok, msg = evaluate(HEAD, "no marker", [comment(marker("codex", HEAD))], OWNER, labels({}))
        self.assertFalse(ok)
        self.assertIn("0 tracking issues", msg)

    def test_rejects_two_tracking_issue_declarations(self):
        body = "<!-- lex-item issue=333 --> and <!-- lex-item issue=330 -->"
        ok, msg = evaluate(HEAD, body, [comment(marker("codex", HEAD))], OWNER, labels({}))
        self.assertFalse(ok)
        self.assertIn("2 tracking issues", msg)

    def test_rejects_an_unreadable_tracking_issue(self):
        ok, msg = evaluate(HEAD, BODY, [comment(marker("codex", HEAD))], OWNER, labels({}))
        self.assertFalse(ok)
        self.assertIn("could not be read", msg)

    def test_rejects_two_owner_labels(self):
        bad = ["owner:claude", "owner:codex", "reviewer:codex"]
        ok, msg = evaluate(HEAD, BODY, [comment(marker("codex", HEAD))], OWNER, labels({333: bad}))
        self.assertFalse(ok)
        self.assertIn("owner:* labels", msg)

    def test_rejects_a_missing_reviewer_label(self):
        bad = ["v3", "owner:claude"]
        ok, msg = evaluate(HEAD, BODY, [comment(marker("codex", HEAD))], OWNER, labels({333: bad}))
        self.assertFalse(ok)
        self.assertIn("reviewer:* labels", msg)

    def test_rejects_owner_and_reviewer_being_the_same_agent(self):
        bad = ["owner:claude", "reviewer:claude"]
        ok, msg = evaluate(HEAD, BODY, [comment(marker("claude", HEAD))], OWNER, labels({333: bad}))
        self.assertFalse(ok)
        self.assertIn("both owner and reviewer", msg)

    def test_a_verdict_from_the_writer_is_not_review(self):
        cs = [comment(marker("claude", HEAD))]
        ok, msg = evaluate(HEAD, BODY, cs, OWNER, labels({333: CLAUDE_WRITES}))
        self.assertFalse(ok)
        self.assertIn("no verdict from the declared reviewer", msg)


class ShaBinding(unittest.TestCase):
    def test_rejects_a_verdict_bound_to_a_superseded_commit(self):
        ok, msg = evaluate(
            HEAD, BODY, [comment(marker("codex", OLD))], OWNER, labels({333: CLAUDE_WRITES})
        )
        self.assertFalse(ok)
        self.assertIn("head is now", msg)

    def test_rejects_an_objection_on_the_current_head(self):
        cs = [comment(marker("codex", HEAD, "OBJECTION"))]
        ok, msg = evaluate(HEAD, BODY, cs, OWNER, labels({333: CLAUDE_WRITES}))
        self.assertFalse(ok)
        self.assertIn("OBJECTION", msg)

    def test_rejects_conflicting_verdicts_on_the_same_head(self):
        # Order must not decide. Codex demonstrated OBJECTION-then-READY passing.
        cs = [comment(marker("codex", HEAD, "OBJECTION")), comment(marker("codex", HEAD))]
        ok, msg = evaluate(HEAD, BODY, cs, OWNER, labels({333: CLAUDE_WRITES}))
        self.assertFalse(ok)
        self.assertIn("conflicting", msg)

    def test_rejects_conflicting_verdicts_in_the_other_order_too(self):
        cs = [comment(marker("codex", HEAD)), comment(marker("codex", HEAD, "OBJECTION"))]
        ok, msg = evaluate(HEAD, BODY, cs, OWNER, labels({333: CLAUDE_WRITES}))
        self.assertFalse(ok)
        self.assertIn("conflicting", msg)


class ApiParsing(unittest.TestCase):
    """The first implementation regex-scanned the API text and crashed on real data."""

    def _run(self, stdout):
        with mock.patch("subprocess.run") as r:
            r.return_value = mock.Mock(stdout=stdout)
            return _gh_json("repos/x/y/issues/1/comments")

    def test_parses_a_multi_page_slurped_array(self):
        pages = [[{"body": "one"}, {"body": "two"}], [{"body": "three"}]]
        self.assertEqual(len(self._run(json.dumps(pages))), 3)

    def test_parses_a_single_object_response(self):
        self.assertEqual(self._run(json.dumps([{"number": 7}]))["number"], 7)

    def test_survives_bodies_containing_braces_and_brackets(self):
        # The exact shape that broke the regex parser: JSON punctuation inside prose.
        tricky = [[{"body": "see {a: [1,2]} and \"quoted\"\nmultiline", "user": {"login": "x"}}]]
        self.assertEqual(len(self._run(json.dumps(tricky))), 1)


class Helpers(unittest.TestCase):
    def test_parse_verdicts_ignores_non_owner_authors(self):
        cs = [comment(marker("codex", HEAD), login="stranger")]
        self.assertEqual(parse_verdicts(cs, OWNER), [])

    def test_roles_from_labels_reads_a_clean_pair(self):
        owner, reviewer, err = roles_from_labels(CLAUDE_WRITES)
        self.assertIsNone(err)
        self.assertEqual((owner, reviewer), ("claude", "codex"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
