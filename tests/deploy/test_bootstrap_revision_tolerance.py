"""Bootstrap topology gates tolerate a retained reviewed revision, nothing else.

Azure removes a superseded inactive revision on its own schedule. Run 31926644551 held the
reviewed A + S + R shape for 875 seconds and Azure had still not removed S at cancellation, so
the deploy gates assert what the invariants need rather than what the platform happens to have
pruned. Every tolerated shape below is paired with the closest shape that must still fail.
"""

import json
from pathlib import Path
import re
import shutil
import subprocess
import unittest


ROOT = Path(__file__).resolve().parents[2]
DEPLOY_WORKFLOW = ROOT / ".github" / "workflows" / "deploy.yml"

AUTHORITY = "ca-lex-web--r28d1e50-b2d7429c-31547318355-1"
PREDECESSOR = "ca-lex-web--b54cceec-4dff34d9-31926644551-1"
SURVIVOR = "ca-lex-web--r54cceec-4dff34d9-31926644551-1"
FALLBACK = "ca-lex-web--b54cceec-4dff34d9-31940000000-1"
CANDIDATE = "ca-lex-web--r54cceec-4dff34d9-31940000000-1"
STRANGER = "ca-lex-web--r00000000-00000000-31900000000-1"


def workflow_text():
    return DEPLOY_WORKFLOW.read_text(encoding="utf-8")


def jq_program(anchor):
    """Return the jq filter the workflow itself runs, so the test cannot drift from it."""
    text = workflow_text()
    start = text.index(anchor)
    opened = text.index("'", text.index("jq -e", start))
    closed = text.index("'", opened + 1)
    program = text[opened + 1:closed]
    if not program.strip():
        raise AssertionError(f"extracted an empty jq program for {anchor!r}")
    return program


def revision(name, active=True, traffic=0):
    return {"name": name, "properties": {"active": active, "trafficWeight": traffic}}


def prepared(retained=(), extra=()):
    state = [
        revision(AUTHORITY, active=True, traffic=100),
        revision(FALLBACK, active=False, traffic=0),
        revision(CANDIDATE, active=True, traffic=0),
    ]
    state.extend(revision(name, active=False, traffic=0) for name in retained)
    state.extend(extra)
    return state


class JqCase(unittest.TestCase):
    def setUp(self):
        self.jq = shutil.which("jq")
        if self.jq is None:
            self.skipTest("jq is unavailable")

    def run_jq(self, program, state, options, positional=()):
        # Same argument order the workflow uses: options, then the filter, then --args.
        command = [self.jq, "-e", *options, program]
        if positional:
            command += ["--args", *positional]
        completed = subprocess.run(
            command, input=json.dumps(state), text=True, capture_output=True)
        self.assertIn(
            completed.returncode, (0, 1),
            f"the workflow filter failed to run: {completed.stderr}")
        return completed.returncode

    def assert_accepted(self, program, state, options, why, positional=()):
        self.assertEqual(0, self.run_jq(program, state, options, positional), why)

    def assert_refused(self, program, state, options, why, positional=()):
        self.assertEqual(1, self.run_jq(program, state, options, positional), why)


class RevisionSetTests(JqCase):
    """verify_revision_set: required names present, extras only from the receipt."""

    def setUp(self):
        super().setUp()
        self.program = jq_program("verify_revision_set() {")

    def options(self, predecessor=PREDECESSOR, survivor=SURVIVOR):
        return ["--arg", "predecessor", predecessor,
                "--arg", "survivor", survivor]

    def accept(self, required, observed, why, predecessor=PREDECESSOR):
        self.assert_accepted(
            self.program, [revision(name) for name in observed],
            self.options(predecessor=predecessor), why, positional=required)

    def refuse(self, required, observed, why, predecessor=PREDECESSOR):
        self.assert_refused(
            self.program, [revision(name) for name in observed],
            self.options(predecessor=predecessor), why, positional=required)

    def test_reviewed_handoff_may_still_be_there_or_already_gone(self):
        self.accept([AUTHORITY, FALLBACK], [AUTHORITY, FALLBACK],
                    "the state the old gate demanded must still be accepted")
        self.accept([AUTHORITY, FALLBACK], [AUTHORITY, SURVIVOR, FALLBACK],
                    "S retained at zero traffic is the measured failure of run 31926644551")
        self.accept([AUTHORITY, FALLBACK], [AUTHORITY, PREDECESSOR, SURVIVOR, FALLBACK],
                    "Azure may still be holding both reviewed handoff revisions")
        self.accept([AUTHORITY, FALLBACK], [AUTHORITY, PREDECESSOR, FALLBACK],
                    "removal order is Azure's, so O may outlive S")
        self.accept([AUTHORITY, FALLBACK, CANDIDATE],
                    [AUTHORITY, SURVIVOR, FALLBACK, CANDIDATE],
                    "the candidate states inherit the same tolerance")

    def test_an_unreviewed_revision_is_never_tolerated(self):
        self.refuse([AUTHORITY, FALLBACK], [AUTHORITY, FALLBACK, STRANGER],
                    "a name outside the receipt and this run must fail closed")
        self.refuse([AUTHORITY, FALLBACK], [AUTHORITY, SURVIVOR, FALLBACK, STRANGER],
                    "tolerating S must not open the set to anything else")
        self.refuse([AUTHORITY, FALLBACK], [AUTHORITY, PREDECESSOR, FALLBACK],
                    "with no O in the receipt nothing but S is tolerable",
                    predecessor="")
        self.refuse([AUTHORITY, SURVIVOR], [AUTHORITY, SURVIVOR, STRANGER],
                    "the pre-fallback inventory stays closed as well")

    def test_a_required_revision_is_never_optional(self):
        self.refuse([AUTHORITY, FALLBACK], [AUTHORITY],
                    "R is the rollback once it exists and cannot be absent")
        self.refuse([AUTHORITY, FALLBACK], [FALLBACK, SURVIVOR],
                    "the traffic authority cannot be absent")
        self.refuse([AUTHORITY, FALLBACK, CANDIDATE], [AUTHORITY, FALLBACK],
                    "the candidate cannot be absent from a candidate state")
        self.refuse([AUTHORITY, SURVIVOR], [AUTHORITY, PREDECESSOR],
                    "S is required before R exists, and O cannot stand in for it")

    def test_a_retained_name_cannot_impersonate_a_required_one(self):
        self.refuse([AUTHORITY, FALLBACK], [AUTHORITY, SURVIVOR],
                    "a retained revision is not a substitute for R")
        self.refuse([AUTHORITY, FALLBACK, FALLBACK], [AUTHORITY, FALLBACK],
                    "two roles collapsed onto one revision leave no rollback")
        self.refuse([AUTHORITY, AUTHORITY], [AUTHORITY],
                    "production cannot also be its own rollback")
        self.refuse([AUTHORITY, FALLBACK],
                    [AUTHORITY, FALLBACK, FALLBACK],
                    "a duplicate revision name is a corrupt inventory")

    def test_the_empty_and_unnamed_cases_fail_closed(self):
        self.refuse([AUTHORITY, ""], [AUTHORITY],
                    "an unset required name must never be silently dropped")
        self.refuse([AUTHORITY, FALLBACK], [],
                    "an empty inventory is not a tolerated state")


class PreparedStateTests(JqCase):
    """verify_bootstrap_prepared_state: A100 + C0 active, R inactive, extras reviewed only."""

    def setUp(self):
        super().setUp()
        self.program = jq_program("verify_bootstrap_prepared_state() {")

    def options(self, predecessor=PREDECESSOR, survivor=SURVIVOR):
        return ["--arg", "current", AUTHORITY,
                "--arg", "rollback", FALLBACK,
                "--arg", "candidate", CANDIDATE,
                "--arg", "predecessor", predecessor,
                "--arg", "survivor", survivor]

    def test_a_retained_reviewed_revision_does_not_disturb_the_prepared_state(self):
        self.assert_accepted(self.program, prepared(), self.options(),
                             "the fully pruned state must still be accepted")
        self.assert_accepted(self.program, prepared(retained=[SURVIVOR]), self.options(),
                             "S retained inactive at zero traffic threatens no invariant")
        self.assert_accepted(self.program, prepared(retained=[PREDECESSOR, SURVIVOR]),
                             self.options(),
                             "both reviewed handoff revisions may still be present")

    def test_anything_beyond_the_reviewed_names_still_fails(self):
        self.assert_refused(
            self.program, prepared(retained=[STRANGER]), self.options(),
            "an unreviewed inactive revision is not a tolerated leftover")
        self.assert_refused(
            self.program, prepared(retained=[SURVIVOR]), self.options(survivor=""),
            "tolerance must come from the receipt, not from being inactive")
        self.assert_refused(
            self.program, prepared(extra=[revision(STRANGER, active=True, traffic=0)]),
            self.options(),
            "a second unreviewed active revision is a quota authority we did not review")
        self.assert_refused(
            self.program, prepared(extra=[revision(SURVIVOR, active=True, traffic=0)]),
            self.options(),
            "a reviewed name that is active again is not a retained leftover")

    def test_the_three_invariants_still_fail_closed(self):
        self.assert_refused(
            self.program,
            [revision(AUTHORITY, True, 90), revision(FALLBACK, False, 0),
             revision(CANDIDATE, True, 10)],
            self.options(),
            "traffic must be one authority at one hundred percent")
        self.assert_refused(
            self.program,
            prepared(retained=[SURVIVOR])[:3]
            + [revision(SURVIVOR, active=False, traffic=5)],
            self.options(),
            "a retained revision that carries traffic breaks the single authority")
        self.assert_refused(
            self.program,
            [revision(AUTHORITY, True, 100), revision(CANDIDATE, True, 0)],
            self.options(),
            "the rollback revision must exist")
        self.assert_refused(
            self.program,
            [revision(AUTHORITY, True, 100), revision(FALLBACK, True, 0),
             revision(CANDIDATE, True, 0)],
            self.options(),
            "the rollback must be the inactive one")
        self.assert_refused(
            self.program,
            [revision(AUTHORITY, True, 100), revision(FALLBACK, False, 0),
             revision(CANDIDATE, False, 0)],
            self.options(),
            "the prepared candidate must be active at zero traffic")


class QuotaAuthorityGateTests(JqCase):
    """The final always() gate keeps the same closed inventory from step outputs."""

    def setUp(self):
        super().setUp()
        self.program = jq_program(
            'prepared=$(az_retry az containerapp revision list')

    def options(self, retained="", limit="1"):
        return ["--arg", "current", AUTHORITY,
                "--arg", "candidate", CANDIDATE,
                "--arg", "fallback", FALLBACK,
                "--arg", "retained", retained,
                "--argjson", "limit", limit]

    def test_the_gate_accepts_the_same_reviewed_leftovers(self):
        self.assert_accepted(self.program, prepared(), self.options(),
                             "the fully pruned state must still be accepted")
        self.assert_accepted(
            self.program, prepared(retained=[SURVIVOR]),
            self.options(retained=f" {SURVIVOR}"),
            "an absent O leaves a leading space in the output, which must not matter")
        self.assert_accepted(
            self.program, prepared(retained=[PREDECESSOR, SURVIVOR]),
            self.options(retained=f"{PREDECESSOR} {SURVIVOR}"),
            "both reviewed handoff revisions may still be present at the end of the run")

    def test_the_gate_refuses_what_the_run_was_not_authorized_against(self):
        self.assert_refused(
            self.program, prepared(retained=[SURVIVOR]), self.options(),
            "a leftover the run cannot name is not a leftover it may tolerate")
        self.assert_refused(
            self.program, prepared(retained=[STRANGER]),
            self.options(retained=f"{PREDECESSOR} {SURVIVOR}"),
            "an unreviewed inactive revision must still fail the final gate")
        self.assert_refused(
            self.program, prepared(retained=[SURVIVOR]),
            self.options(retained=f" {SURVIVOR}", limit="2"),
            "the retention policy must be back at one inactive revision")
        self.assert_refused(
            self.program,
            prepared()[:2] + [revision(CANDIDATE, active=True, traffic=0),
                              revision(SURVIVOR, active=True, traffic=0)],
            self.options(retained=f" {SURVIVOR}"),
            "a reactivated reviewed revision is not a retained leftover")
        self.assert_refused(
            self.program,
            [revision(AUTHORITY, True, 100), revision(CANDIDATE, True, 0),
             revision(SURVIVOR, False, 0)],
            self.options(retained=f" {SURVIVOR}"),
            "the rollback revision must still exist beside the leftovers")
        self.assert_refused(
            self.program,
            [revision(AUTHORITY, True, 95), revision(FALLBACK, False, 0),
             revision(CANDIDATE, True, 5), revision(SURVIVOR, False, 0)],
            self.options(retained=f" {SURVIVOR}"),
            "the candidate must carry no traffic")
        self.assert_refused(
            self.program,
            prepared() + [revision(SURVIVOR, active=False, traffic=5)],
            self.options(retained=f" {SURVIVOR}"),
            "a tolerated leftover that still routes is a second public quota authority")


class WorkflowWiringTests(unittest.TestCase):
    """The tolerance must be receipt-bound and applied to every bootstrap state."""

    def setUp(self):
        self.text = workflow_text()
        self.cases = self.text[
            self.text.index('case "$expected" in'):self.text.index("*) return 1 ;;")]

    def test_no_gate_waits_for_azure_to_remove_a_revision(self):
        self.assertNotIn("fallback-pruning", self.text)
        self.assertNotIn("([.[].name] | sort) ==", self.text)
        self.assertNotIn("length == 3", self.text)

    def test_every_bootstrap_state_closes_the_revision_set(self):
        arms = len(re.findall(r"^ +;;$", self.cases, re.MULTILINE))
        self.assertEqual(5, arms)
        self.assertEqual(arms, self.cases.count("verify_revision_set "))
        self.assertEqual(
            arms, self.cases.count("verify_reviewed_handoff_retention || return 1"))

    def test_tolerance_is_bound_to_the_reviewed_cleanup_receipt(self):
        body = self.text[self.text.index("verify_revision_set() {"):]
        body = body[:body.index("\n          }")]
        self.assertIn('--arg predecessor "$cleanup_predecessor"', body)
        self.assertIn('--arg survivor "$cleanup_survivor"', body)
        self.assertIn(
            'echo "bootstrap_retained=$cleanup_predecessor $cleanup_survivor"', self.text)
        self.assertIn("BOOTSTRAP_RETAINED: ${{ steps.candidate.outputs.bootstrap_retained }}",
                      self.text)

    def test_a_retained_revision_is_reproved_against_the_receipt(self):
        body = self.text[self.text.index("verify_reviewed_handoff_retention() {"):]
        body = body[:body.index("\n          }")]
        for expected in (
            'verify_bootstrap_handoff_identity "$cleanup_predecessor" false 0',
            '"$cleanup_predecessor_created" "$cleanup_predecessor_image"',
            '"$cleanup_predecessor_template" false',
            'verify_bootstrap_handoff_identity "$cleanup_survivor" false 0',
            '"$cleanup_survivor_created" "$cleanup_survivor_image"',
            '"$cleanup_survivor_template" false',
        ):
            self.assertIn(expected, body)

    def test_the_protected_invariants_are_still_asserted_directly(self):
        self.assertIn('[ "$fallback_image" = "$IMAGE" ]', self.text)
        self.assertIn(
            '[ "$fallback_template_digest" = "$canonical_template_digest" ]', self.text)
        self.assertIn('[ "$candidate_image" = "$IMAGE" ]', self.text)
        self.assertIn(
            '[ "$candidate_template_digest" = "$canonical_template_digest" ]', self.text)
        self.assertIn(
            "([.[] | select((.properties.trafficWeight // 0) > 0)] | length) == 1", self.text)
        self.assertIn("expected exactly one traffic-bearing revision", self.text)
        self.assertIn("python3 scripts/deploy/revision_template_digest.py", self.text)

    def test_the_candidate_route_shape_is_unchanged(self):
        self.assertIn("""else ((.routes | length) == 1
                  and .routes[0].revisionName == $authority
                  and .routes[0].weight == 100)""", self.text)
        self.assertIn("""and all(.routes[];
                  (.latestRevision // false) == false and (.label // null) == null)""",
                      self.text)


if __name__ == "__main__":
    unittest.main()
