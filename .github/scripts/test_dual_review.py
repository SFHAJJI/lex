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

import re
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


WORKFLOW = Path(__file__).resolve().parents[2] / ".github" / "workflows" / "dual-review.yml"
REQUIRED_CONTEXT = "dual-review"

# The exact actions permitted to run inside the required job: owner/repository plus
# the immutable commit that was actually reviewed. Changing a pin is a review event,
# never an edit, so the reviewed value lives here as data the gate refuses to differ
# from rather than as a convention the file merely happens to follow.
REVIEWED_ACTIONS = frozenset(
    {"actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803"}
)
ALLOWED_REPOSITORIES = frozenset(spec.split("@", 1)[0] for spec in REVIEWED_ACTIONS)
IMMUTABLE_PIN_RE = re.compile(r"\A[0-9a-f]{40}\Z")

# The reviewed step list, in order. Data, not shape: the gate refuses any job whose
# steps differ from this, so an added step cannot satisfy the checks by being
# well-formed. `uses` steps carry no script; `run` steps carry exactly the named
# command and no action.
REVIEWED_STEPS = (
    ("uses", "actions/checkout"),
    ("run", "unittest"),
    ("run", "dual_review.py"),
)

# Every key inside each reviewed step, in file order. A kind does not bound a step's
# keys, and the keys are where the material lives: `working-directory: decoy` moves a
# reviewed command into a directory the writer committed in the same pull request, so
# a decoy `.github/scripts/` holding a green suite and a `dual_review.py` that exits 0
# false-greens the required context in two lines, with no step, no env: and no change
# to the pinned action. `with:` is the same argument for the checkout: pinning the
# action while leaving its inputs open reviews the machinery and not the material.
REVIEWED_STEP_KEYS = (
    ("uses", "with", "ref", "fetch-depth", "persist-credentials"),
    ("name", "run"),
    ("name", "env", "GH_TOKEN", "REPO", "PR_NUMBER", "HEAD_SHA", "run"),
)

# What those keys are bound to. Pinning a key and not its value reviews the shape of
# the input and not the input. The gate step's env block is the evaluator's entire
# input: `PR_NUMBER: 261` reads a different pull request's body, `REPO:` reads a
# repository the writer controls, and `HEAD_SHA:` evaluates a commit that is not the
# head, which is the single property the receipt exists to bind. Each is one line
# inside a key list that stays exactly as reviewed, so only the value can catch it.
REAL_HEAD_SHA_BINDING = "          HEAD_SHA: ${{ github.event.pull_request.head.sha }}"
SPOOFED_HEAD_SHA_BINDING = "          HEAD_SHA: ${{ github.event.pull_request.base.sha }}"

# Each binding with the index of the reviewed step that must carry it. The index is the
# repair for Codex O2: a binding found anywhere else in the file is a decoy.
REVIEWED_BINDINGS = (
    (0, "          ref: ${{ github.event.pull_request.head.sha }}"),
    (0, "          fetch-depth: 2"),
    (0, "          persist-credentials: false"),
    (2, "          GH_TOKEN: ${{ github.token }}"),
    (2, "          REPO: ${{ github.repository }}"),
    (2, "          PR_NUMBER: ${{ github.event.pull_request.number }}"),
    (2, "          HEAD_SHA: ${{ github.event.pull_request.head.sha }}"),
    # The two commands themselves. Codex O1 against 7f035e9, and I had returned READY on
    # that commit without testing them. Every other value in this workflow was pinned by
    # value while the two lines that actually run the tests and the gate were matched by
    # substring, so `run: echo unittest` and `run: echo dual_review.py` both satisfied
    # the step checks and greened the required context while executing nothing. The most
    # important value in a file is the one nobody thinks of as a value.
    (1, "        run: python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v"),
    (2, "        run: python3 .github/scripts/dual_review.py"),
)

# The reviewed environment: the exact keys the workflow and the job may declare, in
# order, and the exact runner. Claude O1 against 61eceae. My own env: rule there read
# one spelling, `env:` alone on its own line, one round after I had written that a
# scanner reading only its own preferred spelling never sees `- {uses: x}` or
# `- "uses": x`. It lost to those same spellings. Worse, three keys shim the gate
# without any `env:` and without any step: `container:` runs every step inside an
# image of the writer's choosing, `runs-on:` moves the whole job onto a machine of
# the writer's choosing, and `defaults.run.shell` is a template every command is
# substituted into. Refusing keys one at a time loses to the next key. An exact
# allowlist refuses all of them, including the ones nobody has thought of yet.
REVIEWED_WORKFLOW_KEYS = ("name", "on", "permissions", "jobs")
REVIEWED_JOB_KEYS = ("name", "runs-on", "steps")
REVIEWED_RUNNER = "ubuntu-latest"

# One reviewed key per line, in the one spelling this scanner can read. A quoted key
# and a flow mapping are valid YAML that GitHub obeys and that a line-oriented rule
# reading `key:` never sees, so both are refused outright. `${{ }}` expressions are
# removed first: they are the only legitimate braces in the file.
READABLE_KEY_RE = re.compile(r"^(\s*)(- )?([A-Za-z_][A-Za-z0-9_.-]*):(\s.*)?$")
EXPRESSION_RE = re.compile(r"\$\{\{.*?\}\}")


def _scoped_keys(lines):
    """The workflow's top-level keys and its job's keys, plus unreadable lines.

    Scope matters: `types:` and `branches:` sit at four spaces under `on:` exactly
    like a job key does, so indentation alone would confuse a trigger option with the
    environment the gate runs in.
    """
    workflow, job, unreadable = [], [], []
    inside_jobs = inside_job = False
    for line in lines:
        if not line.strip():
            continue
        scrubbed = EXPRESSION_RE.sub("", line)
        match = READABLE_KEY_RE.match(scrubbed)
        if not match or "{" in scrubbed or "}" in scrubbed:
            unreadable.append(line.strip())
            continue
        indent, dash, key = len(match.group(1)), match.group(2), match.group(3)
        if dash:
            continue
        if indent == 0:
            workflow.append(key)
            inside_jobs, inside_job = key == "jobs", False
        elif inside_jobs and indent == 2:
            inside_job = True
        elif inside_job and indent == 4:
            job.append(key)
    return workflow, job, unreadable


def _uncommented(text):
    """The workflow's lines with YAML comments removed.

    Needed because this workflow's own prose contains the word `needs:` while
    explaining why it must never appear. A scanner that read comments would either
    trip on the explanation or, worse, be quietly relaxed until it stopped tripping.
    """
    kept = []
    for line in text.split("\n"):
        quote = None
        cut = None
        for index, char in enumerate(line):
            if quote is not None:
                if char == quote:
                    quote = None
            elif char in "'\"":
                quote = char
            elif char == "#" and (index == 0 or line[index - 1] in " \t"):
                cut = index
                break
        kept.append(line if cut is None else line[:cut])
    return kept


def _job_steps(lines):
    """The job's steps, each as its own list of lines.

    A step starts at `      - ` and continues until the next one or until the block
    dedents. Comment-stripped blank lines belong to whichever step they fall in and
    are harmless; what matters is that an added step becomes its own entry here and
    so changes the count that `REVIEWED_STEPS` pins.
    """
    steps = []
    current = None
    inside = False
    for line in lines:
        if re.match(r"^    steps:\s*$", line):
            inside = True
            continue
        if not inside:
            continue
        if line.strip() and not line.startswith("      "):
            break
        if re.match(r"^      - ", line):
            if current is not None:
                steps.append(current)
            current = [line]
        elif current is not None:
            current.append(line)
    if current is not None:
        steps.append(current)
    return steps


def _step_keys(step):
    """Every key the step declares, in file order, including its nested inputs.

    Unreadable lines are left to the readability rule, which refuses the whole file;
    skipping them here keeps this function reporting keys rather than two things.
    """
    keys = []
    for line in step:
        if not line.strip():
            continue
        match = READABLE_KEY_RE.match(EXPRESSION_RE.sub("", line))
        if match:
            keys.append(match.group(3))
    return keys


def false_green_defects(text):
    """Every way this workflow could report success without the receipt being checked.

    GitHub reports a SKIPPED job as success, and a skipped required check does not
    block merging. Codex's O1 was exactly that: the gate's own tests ran in a separate
    job that the required one declared with `needs:`, so a failing test suite skipped
    the required context into a reported success.

    The repair for that was a workflow comment saying not to split the jobs again. A
    comment is not a control, and it cannot fail. This function is the control: the
    same split shape, a `continue-on-error` step, a conditional step, a dropped test
    step, a path filter and a renamed required context are all mechanically refused.
    """
    lines = _uncommented(text)
    defects = []

    jobs = []
    inside = False
    for line in lines:
        if line.rstrip() == "jobs:":
            inside = True
            continue
        if inside:
            if line.strip() and not line.startswith(" "):
                inside = False
                continue
            match = re.match(r"^  ([A-Za-z0-9_.-]+):\s*$", line)
            if match:
                jobs.append(match.group(1))
    if jobs != [REQUIRED_CONTEXT]:
        defects.append(
            f"expected exactly one job, {REQUIRED_CONTEXT!r}, found {jobs}; a second "
            "job is only reachable through needs:, and a skipped needs: dependant "
            "reports success"
        )

    for key in ("needs", "continue-on-error", "if"):
        found = [line.strip() for line in lines if re.match(rf"^\s*{key}:", line)]
        if found:
            defects.append(
                f"{key}: is forbidden in this workflow, found {found}; it can skip or "
                "excuse work while the required context still reports success"
            )

    for key in ("paths", "paths-ignore"):
        if any(re.match(rf"^\s*{key}:", line) for line in lines):
            defects.append(f"{key}: would let the gate not run on some pull requests")

    # Anchored to the job's own indentation. An earlier version of this check
    # matched the workflow-level `name:` instead, so a renamed job passed it: the
    # assertion was true of the file and false of the required context.
    if not any(re.match(rf"^    name: {REQUIRED_CONTEXT}\s*$", line) for line in lines):
        defects.append(
            f"the job itself must be named {REQUIRED_CONTEXT!r}; branch protection "
            "matches the required context by name, so a rename silently orphans it"
        )

    # Anchored as the checkout's own inputs, not merely present somewhere in the
    # file: the same expression also appears as the HEAD_SHA env value, so a
    # substring search stayed green after the `ref:` line was deleted.
    required_inputs = (
        (
            r"^          ref: \$\{\{ github\.event\.pull_request\.head\.sha \}\}\s*$",
            "the checkout must name head.sha explicitly, never the synthetic merge "
            "commit",
        ),
        (
            r"^          fetch-depth: 2\s*$",
            "the parent must be fetched so the receipt's tree can be compared to it",
        ),
    )
    for pattern, why in required_inputs:
        if not any(re.match(pattern, line) for line in lines):
            defects.append(f"missing checkout input: {why}")

    # Codex O3. The checkout is the first thing that runs in the required job, and it
    # populates the very workspace the two later steps execute out of. A moving ref
    # (`@main`, `@v6`) or a forked owner/repository is therefore a supply chain this
    # gate cannot see: that action could rewrite `test_dual_review.py` and
    # `dual_review.py` on disk before either runs, after which every check in this
    # file and the receipt evaluation itself would report success on code no reviewer
    # approved. Codex proved the hole by replacing the pin with `actions/checkout@main`
    # and watching all 43 tests stay green. I reproduced it and found it wider: a
    # wholly injected third-party step passed too.
    #
    # The rule is therefore positional and exact, and it is two rules rather than one.
    # The positive rule reads an action in a single spelling: a step at six spaces,
    # `- uses: `, one bare reference, allowlisted owner/repository, immutable 40-hex
    # pin, and that pin the reviewed one. The negative rule exists because I attacked
    # the positive one and beat it four times: `- {uses: x}`, `- "uses": x` and
    # `- 'uses': x` are all valid YAML that GitHub runs and that a scanner reading
    # only its own preferred spelling never sees, so a second action rode in beside an
    # untouched, correctly pinned checkout. So the token `uses` may not appear in this
    # file's YAML in any other form at all. Refusing an unreadable line is right even
    # when the line is innocent: this scanner cannot tell the difference, and the
    # whole point of it is to not report success on something it did not check.
    action = re.compile(r"^      - uses: (\S+)\s*$")
    uses_lines = [line for line in lines if action.match(line)]
    unreadable = [
        line.strip() for line in lines if "uses" in line and not action.match(line)
    ]
    if unreadable:
        defects.append(
            "an action must be written as a step at exactly six spaces of indentation, "
            f"`- uses: <owner>/<repository>@<40-hex>`; this is not readable: {unreadable}"
        )
    if len(uses_lines) != 1:
        defects.append(
            f"expected exactly one uses: in this workflow, the reviewed checkout, found "
            f"{len(uses_lines)}; any other action runs inside the required job and can "
            "replace the tests and the evaluator before either of them runs"
        )
    for line in uses_lines:
        spec = action.match(line).group(1)
        if spec in REVIEWED_ACTIONS:
            continue
        repository, _, ref = spec.partition("@")
        if repository not in ALLOWED_REPOSITORIES:
            defects.append(
                f"uses: {spec!r} does not name an allowlisted "
                f"{sorted(ALLOWED_REPOSITORIES)}; a forked, renamed or transferred "
                "owner/repository is a different action under different control"
            )
        elif not IMMUTABLE_PIN_RE.match(ref):
            defects.append(
                f"uses: {spec!r} is not pinned to an immutable 40-hex commit; a branch, "
                "tag or abbreviated ref can change what runs in the required job "
                "without any change to this repository"
            )
        else:
            defects.append(
                f"uses: {spec!r} is pinned immutably but to an unreviewed commit; only "
                "the reviewed pin may run in the required job"
            )

    types = next((line for line in lines if re.match(r"^\s*types:\s*\[", line)), None)
    if types is None:
        defects.append("the trigger declares no event types")
    else:
        declared = {t.strip() for t in types.split("[", 1)[1].rstrip("] ").split(",")}
        missing = sorted({"opened", "synchronize", "reopened", "edited"} - declared)
        if missing:
            defects.append(f"the trigger must re-evaluate on {missing}")

    # A step may not swallow its own failure. `run: python3 -m unittest ... || true`
    # exits zero and greens the required context exactly like the skipped job did,
    # and a block scalar would hide a whole script from these single-line checks.
    for line in lines:
        if re.match(r"^\s*run:\s*[|>]", line):
            defects.append(
                "run: block scalars are forbidden here; a multi-line script hides "
                "its own failure handling from this check"
            )
        if ("unittest" in line or "dual_review.py" in line) and set("|;&") & set(line):
            defects.append(
                f"shell chaining in a gate command can discard its exit status: "
                f"{line.strip()!r}"
            )

    tests_at = next((i for i, line in enumerate(lines) if "unittest" in line), None)
    gate_at = next((i for i, line in enumerate(lines) if "dual_review.py" in line), None)
    if tests_at is None:
        defects.append("the gate's own induced mutations are not run at all")
    if gate_at is None:
        defects.append("the receipt evaluation is not run at all")
    if tests_at is not None and gate_at is not None and tests_at > gate_at:
        defects.append(
            "the induced mutations must run before the evaluation, in the same job, so "
            "that a test failure fails the required context directly"
        )

    # Claude O1 against ffe873d: pinning the action closed injected `uses:` steps and
    # left injected `run:` steps wide open. Both keys give a step write access to the
    # workspace before the gate executes, so `- run: cp /tmp/replacement.py
    # .github/scripts/` replaces the gate's own code while every other check here still
    # passes. The step list is therefore reviewed data, exactly like REVIEWED_ACTIONS,
    # rather than a shape that additions can satisfy.
    steps = _job_steps(lines)
    if len(steps) != len(REVIEWED_STEPS):
        defects.append(
            f"the job must contain exactly {len(REVIEWED_STEPS)} reviewed steps, found "
            f"{len(steps)}; an extra step of any kind runs with write access to the "
            "workspace before the gate and can replace the gate's own code"
        )
    for index, (step, expected) in enumerate(zip(steps, REVIEWED_STEPS)):
        kind, token = expected
        runs = [line for line in step if re.match(r"^\s+run:", line)]
        uses = [line for line in step if re.match(r"^\s+-?\s*uses:", line)]
        if kind == "uses":
            if not uses:
                defects.append(f"reviewed step {index} must be the checkout action")
            if runs:
                defects.append(
                    f"reviewed step {index} is the checkout action and must not also run "
                    f"a script: {[line.strip() for line in runs]}"
                )
        else:
            if uses:
                defects.append(
                    f"reviewed step {index} must not invoke an action: "
                    f"{[line.strip() for line in uses]}"
                )
            if len(runs) != 1 or token not in runs[0]:
                defects.append(
                    f"reviewed step {index} must be exactly the {token!r} command, found "
                    f"{[line.strip() for line in runs]}"
                )
        found = _step_keys(step)
        if tuple(found) != REVIEWED_STEP_KEYS[index]:
            defects.append(
                f"reviewed step {index}'s keys must be exactly "
                f"{list(REVIEWED_STEP_KEYS[index])}, found {found}; working-directory:, "
                "shell: and the checkout's own inputs each redirect a reviewed command "
                "at material the writer chose, without adding a step"
            )

    # An `env:` above step scope needs no new step at all. Two lines of ordinary-looking
    # configuration, `PYTHONPATH: /tmp/shim`, change how every step's interpreter
    # resolves imports, so the gate and its own tests can be shimmed while the diff
    # reads as housekeeping. Only the receipt-evaluation step's own block may exist.
    env_lines = [
        (index, len(match.group(1)))
        for index, line in enumerate(lines)
        for match in [re.match(r"^(\s*)env:\s*$", line)]
        if match
    ]
    gate_step_span = None
    if len(steps) == len(REVIEWED_STEPS):
        first_line = lines.index(steps[-1][0])
        gate_step_span = range(first_line, first_line + len(steps[-1]))
    for position, indent in env_lines:
        if indent != 8:
            defects.append(
                f"env: at indentation {indent} is forbidden; a workflow-level or job-level "
                "env applies to every step and can shim the interpreter without adding one"
            )
        elif gate_step_span is not None and position not in gate_step_span:
            defects.append(
                "the only permitted env: block belongs to the receipt-evaluation step"
            )
    if len(env_lines) != 1:
        defects.append(
            f"expected exactly one env: block, the receipt evaluation's own, found "
            f"{len(env_lines)}"
        )

    # The environment as an exact allowlist rather than a list of refusals. Every rule
    # above this one reads a line, so a line this scanner cannot read is refused before
    # any of them get to report success on it.
    workflow_keys, job_keys, unreadable_keys = _scoped_keys(lines)
    if unreadable_keys:
        defects.append(
            "every line must be one reviewed key per line, unquoted, with no flow "
            f"mapping; this is not readable as one reviewed key per line: "
            f"{unreadable_keys}"
        )
    if tuple(workflow_keys) != REVIEWED_WORKFLOW_KEYS:
        defects.append(
            f"the workflow's top-level keys must be exactly "
            f"{list(REVIEWED_WORKFLOW_KEYS)}, found {workflow_keys}; a top-level env: "
            "or defaults: applies to every step and shims the gate with no step added"
        )
    if tuple(job_keys) != REVIEWED_JOB_KEYS:
        defects.append(
            f"the job's keys must be exactly {list(REVIEWED_JOB_KEYS)}, found "
            f"{job_keys}; container:, defaults:, services: and env: each change what "
            "the gate's own steps execute inside without adding a step"
        )
    # Codex O2 against 665cdda. Checking a binding anywhere in the file lets a YAML
    # scalar carry the reviewed text as data while the real key binds something else.
    # The surviving shape was the workflow-level `name:` as a folded scalar:
    #
    #     name: >
    #               HEAD_SHA: ${{ github.event.pull_request.head.sha }}
    #
    # with the gate's own env binding HEAD_SHA to base.sha. The continuation line is a
    # value, not a key, so no key check sees it, and a whole-file membership test is
    # satisfied by the decoy. Each binding is therefore checked inside the step that must
    # carry it, so a copy anywhere else proves nothing.
    stripped = [line.rstrip() for line in lines]
    steps_for_bindings = _job_steps(lines)
    missing_bindings = []
    for index, binding in REVIEWED_BINDINGS:
        if index >= len(steps_for_bindings):
            missing_bindings.append(binding)
            continue
        if binding not in [line.rstrip() for line in steps_for_bindings[index]]:
            missing_bindings.append(binding)
    if missing_bindings:
        defects.append(
            f"missing reviewed binding: {missing_bindings}; the reviewed keys must be "
            "bound to the reviewed values inside their own reviewed step, or the "
            "evaluator reads a pull request, a repository or a commit the writer chose "
            "instead of the one under review"
        )

    # Defence in depth for the same class. A block scalar has no legitimate use in this
    # workflow and is the only construct that can put an arbitrary line into the file as
    # data. The earlier rule covered `run:` alone, which is how the `name:` variant
    # survived.
    for line in lines:
        if re.match(r"^\s*[A-Za-z0-9_.-]+:\s*[|>][-+0-9]*\s*$", line):
            defects.append(
                f"block scalar is forbidden anywhere in this workflow: {line.strip()!r}; "
                "it can carry an arbitrary line, including a copy of a reviewed binding, "
                "as data that no key check inspects"
            )

    if not any(
        re.match(rf"^    runs-on: {re.escape(REVIEWED_RUNNER)}\s*$", line)
        for line in lines
    ):
        defects.append(
            f"the job must run on the reviewed GitHub-hosted runner "
            f"{REVIEWED_RUNNER!r}; a self-hosted or otherwise substituted runner "
            "executes the gate on a machine the writer controls"
        )

    return defects


class RequiredContextCannotFalseGreen(unittest.TestCase):
    """Codex O1: a failed test stage must fail the required context, not skip it.

    These read the shipped workflow file. Without them the entire O1 repair is a
    comment, and I verified that: re-splitting the jobs left all other tests green.
    """

    def _workflow(self):
        return WORKFLOW.read_text(encoding="utf-8")

    def test_the_shipped_workflow_has_no_false_green_shape(self):
        self.assertEqual(false_green_defects(self._workflow()), [])

    def test_a_skipped_prerequisite_is_caught_in_the_shipped_file(self):
        """The induced proof: Codex's exact objection, applied to the real file."""
        real = self._workflow()
        split = real.replace(
            "  dual-review:\n    name: dual-review\n    runs-on: ubuntu-latest\n",
            "  dual-review-tests:\n    name: dual-review-tests\n"
            "    runs-on: ubuntu-latest\n    steps:\n      - run: python3 -m unittest\n"
            "\n  dual-review:\n    name: dual-review\n"
            "    needs: dual-review-tests\n    runs-on: ubuntu-latest\n",
        )
        self.assertNotEqual(split, real, "the mutation never applied; the proof is void")
        defects = "\n".join(false_green_defects(split))
        self.assertIn("needs:", defects)
        self.assertIn("exactly one job", defects)

    def test_a_scalar_decoy_cannot_satisfy_a_reviewed_binding(self):
        """Codex O2 against 665cdda.

        A whole-file membership test is satisfied by a copy of the binding anywhere. The
        surviving shape put it in the workflow-level `name:` as a folded scalar, where
        the continuation line is a value rather than a key, so no key check inspects it,
        while the gate's own env bound HEAD_SHA to the base commit instead of the head.
        The binding is now checked inside the step that must carry it.
        """
        original = self._workflow()
        for scalar in (">", "|"):
            mutated = original.replace(REAL_HEAD_SHA_BINDING, SPOOFED_HEAD_SHA_BINDING, 1)
            mutated = mutated.replace(
                "name: dual-review", "name: " + scalar + "NEWLINE" + REAL_HEAD_SHA_BINDING, 1
            ).replace("NEWLINE", chr(10))
            self.assertNotEqual(mutated, original, "the mutation never applied")
            defects = "NEWLINE".join(false_green_defects(mutated)).replace("NEWLINE", chr(10))
            self.assertIn(
                "missing reviewed binding",
                defects,
                f"a {scalar} scalar decoy must not satisfy the binding",
            )

    def test_a_block_scalar_is_refused_on_any_key(self):
        """Defence in depth for the same class.

        The earlier rule covered `run:` alone, which is exactly how the `name:` variant
        survived. A block scalar is the only construct that can put an arbitrary line
        into this file as data, and it has no legitimate use here.
        """
        original = self._workflow()
        mutated = original.replace(
            "name: dual-review", "name: >NEWLINE  dual-review", 1
        ).replace("NEWLINE", chr(10))
        self.assertNotEqual(mutated, original, "the mutation never applied")
        self.assertIn(
            "block scalar is forbidden",
            "NEWLINE".join(false_green_defects(mutated)).replace("NEWLINE", chr(10)),
        )

    def test_replacing_a_reviewed_command_with_a_no_op_is_caught(self):
        """Codex O1 against ffe873d's successor, and I had returned READY on it.

        Every other value in this workflow was pinned by value while the two lines that
        actually run the tests and the gate were matched by substring, so `echo unittest`
        satisfied "the unittest command" and greened the required context while executing
        nothing. The most important value in a file is the one nobody thinks of as a
        value.
        """
        real = self._workflow()
        for original, replacement in (
            (
                "        run: python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v",
                "        run: echo unittest",
            ),
            (
                "        run: python3 .github/scripts/dual_review.py",
                "        run: echo dual_review.py",
            ),
        ):
            self.assertIn(original, real, "the reviewed command must be present to replace")
            mutated = real.replace(original, replacement, 1)
            self.assertNotEqual(mutated, real, "the mutation never applied; the proof is void")
            self.assertIn(
                "missing reviewed binding",
                "\n".join(false_green_defects(mutated)),
                f"replacing {original.strip()!r} with a no-op must be caught",
            )

    def test_an_injected_run_step_is_caught(self):
        """Claude O1 against ffe873d: pinning `uses:` left `run:` wide open.

        A step needs no network and no action to defeat the gate. It runs before the
        gate with write access to the workspace, so a single `cp` replaces
        dual_review.py, the replaced tests pass, the replaced gate exits 0, and the
        required context reports success with no receipt checked.
        """
        real = self._workflow()
        anchor = "      - name: induced mutations for the gate itself"
        mutated = real.replace(
            anchor, "      - run: cp /tmp/replacement.py .github/scripts/\n" + anchor
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; the proof is void")
        self.assertIn("exactly 3 reviewed steps", "\n".join(false_green_defects(mutated)))

    def test_a_job_level_env_is_caught(self):
        """The subtler half: this adds no step at all.

        `PYTHONPATH: /tmp/shim` changes how every step's interpreter resolves imports,
        so the gate and its own tests can be shimmed. The diff is two lines that read
        as ordinary configuration.
        """
        real = self._workflow()
        mutated = real.replace(
            "    runs-on: ubuntu-latest\n",
            "    runs-on: ubuntu-latest\n    env:\n      PYTHONPATH: /tmp/shim\n",
            1,
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; the proof is void")
        self.assertIn("indentation 4 is forbidden", "\n".join(false_green_defects(mutated)))

    def test_a_workflow_level_env_is_caught(self):
        real = self._workflow()
        mutated = real.replace("jobs:\n", "env:\n  PYTHONPATH: /tmp/shim\n\njobs:\n", 1)
        self.assertNotEqual(mutated, real, "the mutation never applied; the proof is void")
        self.assertIn("indentation 0 is forbidden", "\n".join(false_green_defects(mutated)))

    def test_a_second_env_block_on_another_step_is_caught(self):
        """Step-scoped, so the indentation rule alone would pass it."""
        real = self._workflow()
        mutated = real.replace(
            "        with:\n",
            "        env:\n          PYTHONPATH: /tmp/shim\n        with:\n",
            1,
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; the proof is void")
        defects = "\n".join(false_green_defects(mutated))
        self.assertIn("belongs to the receipt-evaluation step", defects)

    def test_a_continue_on_error_step_is_caught(self):
        mutated = self._workflow().replace(
            "      - name: induced mutations for the gate itself\n",
            "      - name: induced mutations for the gate itself\n"
            "        continue-on-error: true\n",
        )
        self.assertNotEqual(mutated, self._workflow(), "the mutation never applied")
        self.assertIn("continue-on-error:", "\n".join(false_green_defects(mutated)))

    def test_a_conditional_step_is_caught(self):
        mutated = self._workflow().replace(
            "      - name: verify the immutable lex-review/1 receipt at the head\n",
            "      - name: verify the immutable lex-review/1 receipt at the head\n"
            "        if: false\n",
        )
        self.assertNotEqual(mutated, self._workflow(), "the mutation never applied")
        self.assertIn("if:", "\n".join(false_green_defects(mutated)))

    def test_dropping_the_gate_tests_is_caught(self):
        without = "\n".join(
            line for line in self._workflow().split("\n") if "unittest" not in line
        )
        self.assertNotEqual(without, self._workflow(), "the mutation never applied")
        self.assertIn("induced mutations", "\n".join(false_green_defects(without)))

    def test_running_the_gate_before_its_own_tests_is_caught(self):
        lines = self._workflow().split("\n")
        tests_at = next(i for i, line in enumerate(lines) if "unittest" in line)
        gate_at = next(i for i, line in enumerate(lines) if "dual_review.py" in line)
        lines[tests_at], lines[gate_at] = lines[gate_at], lines[tests_at]
        self.assertIn("must run before", "\n".join(false_green_defects("\n".join(lines))))

    def test_renaming_the_required_context_is_caught(self):
        mutated = self._workflow().replace(
            "    name: dual-review\n", "    name: dual-review-v2\n"
        )
        self.assertNotEqual(mutated, self._workflow(), "the mutation never applied")
        self.assertTrue(false_green_defects(mutated))

    def test_a_path_filter_is_caught(self):
        mutated = self._workflow().replace(
            "    branches: [v3/integration]\n",
            "    branches: [v3/integration]\n    paths: ['src/**']\n",
        )
        self.assertNotEqual(mutated, self._workflow(), "the mutation never applied")
        self.assertIn("paths:", "\n".join(false_green_defects(mutated)))

    def test_dropping_the_explicit_head_checkout_is_caught(self):
        mutated = self._workflow().replace(
            "          ref: ${{ github.event.pull_request.head.sha }}\n", ""
        )
        self.assertNotEqual(mutated, self._workflow(), "the mutation never applied")
        self.assertIn("checkout input", "\n".join(false_green_defects(mutated)))

    def test_dropping_a_trigger_type_is_caught(self):
        mutated = self._workflow().replace(", edited]", "]")
        self.assertNotEqual(mutated, self._workflow(), "the mutation never applied")
        self.assertIn("edited", "\n".join(false_green_defects(mutated)))

    # Codex O3, the two mutations he named plus the neighbours they imply. Every one
    # of these leaves a workflow that is valid YAML, runs the same two commands in the
    # same order, and satisfies every other check in false_green_defects. Before this
    # repair each returned no defects at all and the whole suite stayed green.

    def test_a_moving_checkout_ref_is_caught(self):
        """`@main` is whatever that branch holds at the moment the job starts."""
        real = self._workflow()
        mutated = real.replace(
            "actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803",
            "actions/checkout@main",
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; proof is void")
        self.assertIn("immutable 40-hex", "\n".join(false_green_defects(mutated)))

    def test_a_forked_checkout_repository_is_caught(self):
        """The reviewed pin under an owner nobody reviewed is a different action."""
        real = self._workflow()
        mutated = real.replace(
            "uses: actions/checkout@", "uses: hostile-fork/checkout@"
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; proof is void")
        defects = "\n".join(false_green_defects(mutated))
        self.assertIn("allowlisted", defects)
        self.assertIn("hostile-fork/checkout", defects)

    def test_a_release_tag_pin_is_caught(self):
        """A tag is a pointer its owner can move onto any commit at any time."""
        real = self._workflow()
        mutated = real.replace(
            "actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803",
            "actions/checkout@v6",
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; proof is void")
        self.assertIn("immutable 40-hex", "\n".join(false_green_defects(mutated)))

    def test_an_unreviewed_immutable_pin_is_caught(self):
        """Immutability is not review. This pin is well formed and never seen."""
        real = self._workflow()
        mutated = real.replace(
            "d23441a48e516b6c34aea4fa41551a30e30af803",
            "0000000000000000000000000000000000000bad",
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; proof is void")
        self.assertIn("unreviewed commit", "\n".join(false_green_defects(mutated)))

    def test_an_additional_action_before_the_gate_is_caught(self):
        """Codex named the checkout; any step ahead of the gate has the same reach."""
        real = self._workflow()
        mutated = real.replace(
            "      - name: induced mutations for the gate itself\n",
            "      - uses: hostile/inject@1111111111111111111111111111111111111111\n"
            "      - name: induced mutations for the gate itself\n",
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; proof is void")
        defects = "\n".join(false_green_defects(mutated))
        self.assertIn("exactly one uses:", defects)
        self.assertIn("hostile/inject", defects)

    def test_dropping_the_checkout_entirely_is_caught(self):
        """`unittest discover` over an unpopulated workspace finds nothing, exits 0."""
        real = self._workflow()
        mutated = "\n".join(
            line for line in real.split("\n") if not line.lstrip().startswith("- uses:")
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; proof is void")
        self.assertIn("exactly one uses:", "\n".join(false_green_defects(mutated)))

    def test_hiding_the_checkout_at_the_wrong_indentation_is_caught(self):
        """The requirement is positional: a uses: this cannot place is refused."""
        real = self._workflow()
        mutated = real.replace(
            "      - uses: actions/checkout@", "        uses: actions/checkout@"
        )
        self.assertNotEqual(mutated, real, "the mutation never applied; proof is void")
        self.assertIn("six spaces", "\n".join(false_green_defects(mutated)))

    def test_an_action_in_another_valid_yaml_spelling_is_caught(self):
        """My own positive rule lost to these four before the negative rule existed.

        Each smuggles a second action in beside an untouched, correctly pinned
        checkout, so every other check in this function is satisfied.
        """
        real = self._workflow()
        step = "      - name: induced mutations for the gate itself\n"
        for spelling in (
            "      - {uses: hostile/inject@main}",
            '      - "uses": hostile/inject@main',
            "      - 'uses': hostile/inject@main",
            "      -  uses: hostile/inject@main",
        ):
            with self.subTest(spelling=spelling):
                mutated = real.replace(step, spelling + "\n" + step)
                self.assertNotEqual(mutated, real, "the mutation never applied")
                self.assertIn(
                    "not readable", "\n".join(false_green_defects(mutated))
                )


    def test_an_env_in_another_valid_yaml_spelling_is_caught(self):
        """My own env: rule at 61eceae lost to exactly the trap I had just documented.

        One round earlier I wrote that a scanner reading only its own preferred
        spelling never sees `- {uses: x}` or `- "uses": x`, added a negative rule for
        `uses`, and then wrote the env rule as `env:` alone on its own line. These four
        are valid YAML that GitHub reads as an env block and that rule never saw.
        """
        real = self._workflow()
        job = "    runs-on: ubuntu-latest\n"
        for name, mutated in (
            ("job, flow mapping", real.replace(
                job, job + "    env: {PYTHONPATH: /tmp/shim}\n", 1)),
            ("job, double-quoted key", real.replace(
                job, job + '    "env":\n      PYTHONPATH: /tmp/shim\n', 1)),
            ("job, single-quoted key", real.replace(
                job, job + "    'env':\n      PYTHONPATH: /tmp/shim\n", 1)),
            ("workflow, flow mapping", real.replace(
                "jobs:\n", "env: {PYTHONPATH: /tmp/shim}\n\njobs:\n", 1)),
            ("step, flow mapping", real.replace(
                "        with:\n",
                "        env: {PYTHONPATH: /tmp/shim}\n        with:\n", 1)),
        ):
            with self.subTest(spelling=name):
                self.assertNotEqual(mutated, real, "the mutation never applied")
                self.assertIn("PYTHONPATH", mutated, "the mutation never applied")
                self.assertNotEqual(
                    [], false_green_defects(mutated), "the shim survived"
                )

    def test_a_job_level_container_is_caught(self):
        """This adds no step and no env: every step runs inside the named image.

        The image supplies its own python3. It can exit 0 for both gate steps while
        the required context reports success and no receipt is ever read.
        """
        real = self._workflow()
        mutated = real.replace(
            "    runs-on: ubuntu-latest\n",
            "    runs-on: ubuntu-latest\n    container: attacker/image:latest\n",
            1,
        )
        self.assertNotEqual(mutated, real, "the mutation never applied")
        self.assertIn("job's keys must be exactly", "\n".join(false_green_defects(mutated)))

    def test_a_self_hosted_runner_is_caught(self):
        """One word, no new key: the whole job moves to a machine of the writer's choosing."""
        real = self._workflow()
        mutated = real.replace("    runs-on: ubuntu-latest", "    runs-on: self-hosted", 1)
        self.assertNotEqual(mutated, real, "the mutation never applied")
        self.assertIn(
            "reviewed GitHub-hosted runner", "\n".join(false_green_defects(mutated))
        )

    def test_job_level_defaults_wrapping_every_command_is_caught(self):
        """`defaults.run.shell` is a template GitHub substitutes the script into."""
        real = self._workflow()
        mutated = real.replace(
            "    steps:\n",
            "    defaults:\n      run:\n        shell: bash -e {0}\n    steps:\n",
            1,
        )
        self.assertNotEqual(mutated, real, "the mutation never applied")
        self.assertIn("job's keys must be exactly", "\n".join(false_green_defects(mutated)))

    def test_an_unreviewed_job_key_is_caught(self):
        """The allowlist is exact, so no key needs to be predicted to be refused."""
        real = self._workflow()
        job = "    runs-on: ubuntu-latest\n"
        for injected in (
            "    timeout-minutes: 1\n",
            "    environment: production\n",
            "    services:\n      shim:\n        image: attacker/image\n",
            "    strategy:\n      matrix:\n        n: [1]\n",
        ):
            with self.subTest(key=injected.strip().split(":")[0]):
                mutated = real.replace(job, job + injected, 1)
                self.assertNotEqual(mutated, real, "the mutation never applied")
                self.assertIn(
                    "job's keys must be exactly", "\n".join(false_green_defects(mutated))
                )

    def test_an_unreviewed_workflow_key_is_caught(self):
        real = self._workflow()
        for injected in ("defaults:\n  run:\n    shell: sh\n", "env:\n  PYTHONPATH: /tmp/shim\n"):
            with self.subTest(key=injected.split(":")[0]):
                mutated = real.replace("jobs:\n", injected + "\njobs:\n", 1)
                self.assertNotEqual(mutated, real, "the mutation never applied")
                self.assertIn(
                    "top-level keys must be exactly",
                    "\n".join(false_green_defects(mutated)),
                )

    def test_dropping_a_reviewed_workflow_key_is_caught(self):
        """The allowlist is a sequence, so it fires on removal as well as addition."""
        real = self._workflow()
        mutated = real.replace("permissions:\n  contents: read\n", "", 1)
        self.assertNotEqual(mutated, real, "the mutation never applied")
        self.assertIn(
            "top-level keys must be exactly", "\n".join(false_green_defects(mutated))
        )


    def test_a_step_level_working_directory_is_caught(self):
        """Two lines, no new step, no env:, no action change, and a complete false green.

        `working-directory:` moves a reviewed command into a directory the writer
        committed in the same pull request. A decoy `decoy/.github/scripts/` holding a
        trivially green test suite and a `dual_review.py` that exits 0 makes both gate
        steps pass on the writer's own code while the required context reports success.
        """
        real = self._workflow()
        unittest_step = (
            "        run: python3 -m unittest discover -s .github/scripts "
            "-p 'test_*.py' -v\n"
        )
        self.assertIn(unittest_step, real, "the anchor moved; the proof would be void")
        mutated = real.replace(
            unittest_step, "        working-directory: decoy\n" + unittest_step, 1
        )
        self.assertNotEqual(mutated, real, "the mutation never applied")
        self.assertIn("keys must be exactly", "\n".join(false_green_defects(mutated)))

    def test_an_unreviewed_step_key_is_caught(self):
        """The step list was a sequence of kinds; a kind does not bound a step's keys."""
        real = self._workflow()
        anchor = "        run: python3 .github/scripts/dual_review.py"
        self.assertIn(anchor, real, "the anchor moved; the proof would be void")
        for injected in (
            "        shell: python\n",
            "        timeout-minutes: 1\n",
            "        id: gate\n",
        ):
            with self.subTest(key=injected.strip().split(":")[0]):
                mutated = real.replace(anchor, injected + anchor, 1)
                self.assertNotEqual(mutated, real, "the mutation never applied")
                self.assertIn(
                    "keys must be exactly", "\n".join(false_green_defects(mutated))
                )

    def test_an_unreviewed_checkout_input_is_caught(self):
        """`with:` selects what code lands in the workspace, exactly as `ref:` does.

        Pinning the action while leaving its inputs open reviews the machinery and not
        the material it operates on.
        """
        real = self._workflow()
        anchor = "        with:\n"
        for injected in (
            "          path: decoy\n",
            "          repository: attacker/lex\n",
            "          token: ${{ secrets.ELEVATED }}\n",
        ):
            with self.subTest(input=injected.strip().split(":")[0]):
                mutated = real.replace(anchor, anchor + injected, 1)
                self.assertNotEqual(mutated, real, "the mutation never applied")
                self.assertIn(
                    "keys must be exactly", "\n".join(false_green_defects(mutated))
                )

    def test_dropping_persist_credentials_is_caught(self):
        """The step key list is a sequence, so it fires on removal as well as addition."""
        real = self._workflow()
        mutated = real.replace("          persist-credentials: false\n", "", 1)
        self.assertNotEqual(mutated, real, "the mutation never applied")
        self.assertIn("keys must be exactly", "\n".join(false_green_defects(mutated)))


    def test_repointing_the_gate_environment_is_caught(self):
        """Pinning the keys and not their values reviews the shape of the input only.

        The gate step's env block *is* the evaluator's entire input. `PR_NUMBER: 261`
        reads another pull request's body, `REPO:` reads a repository the writer
        controls, and `HEAD_SHA:` evaluates a commit that is not the head, which is the
        one property the receipt exists to bind. Each is one line, inside a key list
        that stays exactly as reviewed.
        """
        real = self._workflow()
        for original, replacement in (
            (
                "          PR_NUMBER: ${{ github.event.pull_request.number }}\n",
                "          PR_NUMBER: 261\n",
            ),
            (
                "          REPO: ${{ github.repository }}\n",
                "          REPO: attacker/lex\n",
            ),
            (
                "          HEAD_SHA: ${{ github.event.pull_request.head.sha }}\n",
                "          HEAD_SHA: HEAD~1\n",
            ),
            (
                "          GH_TOKEN: ${{ github.token }}\n",
                "          GH_TOKEN: ${{ secrets.WRITER_PAT }}\n",
            ),
            (
                "          persist-credentials: false\n",
                "          persist-credentials: true\n",
            ),
        ):
            with self.subTest(binding=original.strip().split(":")[0]):
                self.assertIn(original, real, "the anchor moved; the proof would be void")
                mutated = real.replace(original, replacement, 1)
                self.assertNotEqual(mutated, real, "the mutation never applied")
                self.assertIn(
                    "reviewed binding", "\n".join(false_green_defects(mutated))
                )


def competing_context_defects(directory):
    """Another workflow declaring a job named `dual-review` would report the same
    required context. GitHub keeps the latest check run of a given name, so a second,
    always-green producer of this context would overwrite the real verdict.
    """
    defects = []
    for path in sorted(directory.iterdir()):
        if path.suffix not in (".yml", ".yaml") or path.name == WORKFLOW.name:
            continue
        lines = _uncommented(path.read_text(encoding="utf-8"))
        for line in lines:
            if re.match(rf"^  {REQUIRED_CONTEXT}:\s*$", line) or re.match(
                rf"^    name: {REQUIRED_CONTEXT}\s*$", line
            ):
                defects.append(
                    f"{path.name} also produces the {REQUIRED_CONTEXT!r} context; the "
                    "latest run of a name wins, so it could overwrite the real verdict"
                )
    return defects


class NoCompetingProducerOfTheRequiredContext(unittest.TestCase):
    def test_no_other_workflow_produces_this_context(self):
        self.assertEqual(competing_context_defects(WORKFLOW.parent), [])

    def test_a_second_producer_would_be_caught(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            (directory / WORKFLOW.name).write_text("name: real\n", encoding="utf-8")
            (directory / "impostor.yml").write_text(
                "jobs:\n  dual-review:\n    runs-on: ubuntu-latest\n"
                "    steps:\n      - run: exit 0\n",
                encoding="utf-8",
            )
            self.assertTrue(competing_context_defects(directory))


if __name__ == "__main__":
    unittest.main(verbosity=2)
