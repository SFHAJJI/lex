from contextlib import redirect_stderr
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

import golden_diff


GOLDEN = Path("tests/Lex.Tests/golden")
TOOL = GOLDEN / "tool-search.txt"
TOOLS_LIST = GOLDEN / "tools-list.txt"
PAGE = GOLDEN / "page-home.txt"
PAGE_ABOUT = GOLDEN / "page-about.txt"
DOCUMENT_POINTER = "/result/content/0/text"
NO_INTENT = object()


def compact(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n"


def mcp(document, *, request_id=1):
    return mcp_text(json.dumps(document, ensure_ascii=False, indent=2), request_id=request_id)


def mcp_text(document, *, request_id=1):
    return compact({
        "jsonrpc": "2.0",
        "id": request_id,
        "result": {
            "content": [{
                "type": "text",
                "text": document,
            }],
        },
    })


class TemporaryRepository:
    def __init__(self, root):
        self.root = root
        self.write(TOOL, mcp({"items": [{"id": "existing"}]}))
        self.write(TOOLS_LIST, compact({
            "jsonrpc": "2.0",
            "id": 1,
            "result": {"tools": [{"name": "search"}]},
        }))
        self.write(PAGE, "HTTP 200\n<html>old</html>\n")
        self.write(PAGE_ABOUT, "HTTP 200\n<html>about</html>\n")
        self.git("init", "-b", "main")
        self.git("config", "user.email", "golden-diff@example.test")
        self.git("config", "user.name", "Golden Diff Tests")
        self.git("add", "--", GOLDEN.as_posix())
        self.git("commit", "-m", "baseline")
        self.base = self.git("rev-parse", "HEAD").stdout.strip()

    def git(self, *arguments):
        return subprocess.run(
            ["git", *map(str, arguments)],
            cwd=self.root,
            text=True,
            capture_output=True,
            check=True,
        )

    def write(self, relative_path, content):
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="")

    def write_bytes(self, relative_path, content):
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def commit(self, *paths):
        self.git("add", "--", *(Path(path).as_posix() for path in paths))
        self.git("commit", "-m", "mutation")

    def amend_baseline(self, relative_path, content):
        self.write(relative_path, content)
        self.git("add", "--", Path(relative_path).as_posix())
        self.git("commit", "--amend", "--no-edit")
        self.base = self.git("rev-parse", "HEAD").stdout.strip()

    def amend_baseline_bytes(self, relative_path, content):
        self.write_bytes(relative_path, content)
        self.git("add", "--", Path(relative_path).as_posix())
        self.git("commit", "--amend", "--no-edit")
        self.base = self.git("rev-parse", "HEAD").stdout.strip()


class GoldenDiffTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.repo = TemporaryRepository(self.root)

    def tearDown(self):
        self.temporary.cleanup()

    def document_addition(self, pointer, file=TOOL):
        return {
            "file": Path(file).as_posix(),
            "pointer": DOCUMENT_POINTER,
            "document_pointer": pointer,
        }

    def direct_addition(self, pointer):
        return {
            "file": TOOLS_LIST.as_posix(),
            "pointer": pointer,
        }

    def intent(self, additions, *, base=None):
        return {
            "schema": "lex-golden-diff-intent/1",
            "base_commit": base or self.repo.base,
            "additions": additions,
        }

    def html_intent(self, file=PAGE, selector="#main", *, base=None):
        return {
            "schema": "lex-golden-diff-intent/1",
            "base_commit": base or self.repo.base,
            "html_selectors": [{
                "file": Path(file).as_posix(),
                "selector": selector,
            }],
        }

    def fenced(self, intent):
        return "Review scope:\n```json\n" + compact(intent) + "```\n"

    def fresh_repository(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        return TemporaryRepository(Path(temporary.name))

    def run_classifier(self, intent=NO_INTENT, *, base=None, event_body=NO_INTENT,
                       event_base=None, repository=None):
        repository = repository or self.repo
        arguments = [
            sys.executable,
            str(ROOT / "scripts" / "golden_diff.py"),
            "--repo",
            str(repository.root),
            "--base",
            base or repository.base,
        ]
        self.assertTrue(intent is NO_INTENT or event_body is NO_INTENT)
        if intent is not NO_INTENT:
            intent_path = repository.root.parent / f"{repository.root.name}-intent.json"
            if isinstance(intent, str):
                intent_path.write_text(intent, encoding="utf-8", newline="")
            else:
                intent_path.write_text(compact(intent), encoding="utf-8", newline="")
            arguments.extend(("--intent", str(intent_path)))
        if event_body is not NO_INTENT:
            event_path = repository.root.parent / f"{repository.root.name}-event.json"
            event_path.write_text(compact({
                "pull_request": {
                    "base": {"sha": event_base or base or repository.base},
                    "body": event_body,
                },
            }), encoding="utf-8", newline="")
            arguments.extend(("--event", str(event_path)))
        return subprocess.run(
            arguments,
            cwd=repository.root,
            text=True,
            capture_output=True,
            check=False,
        )

    def assert_passes(self, intent=NO_INTENT, **kwargs):
        completed = self.run_classifier(intent, **kwargs)
        self.assertEqual(0, completed.returncode, completed.stderr)
        return completed

    def assert_fails(self, intent=NO_INTENT, **kwargs):
        completed = self.run_classifier(intent, **kwargs)
        self.assertNotEqual(0, completed.returncode, completed.stdout)
        self.assertIn("golden diff rejected", completed.stderr)
        return completed

    def assert_json_probe_fails(self, family, old_document, new_text):
        repository = self.fresh_repository()
        if family == "mcp":
            path = TOOL
            baseline = mcp(old_document)
            head = mcp_text(new_text)
            addition = self.document_addition("/added")
        else:
            path = TOOLS_LIST
            baseline = compact(old_document)
            head = new_text + "\n"
            addition = self.direct_addition("/added")
        repository.amend_baseline(path, baseline)
        repository.write(path, head)
        repository.commit(path)
        return self.assert_fails(
            self.intent([addition], base=repository.base), repository=repository)

    def assert_mcp_outer_encoding_probe_fails(
            self, baseline_inner, head_inner, old_token, new_token, *, mutate_base=False):
        repository = self.fresh_repository()
        baseline = mcp_text(baseline_inner)
        head = mcp_text(head_inner)
        if mutate_base:
            mutated = baseline.replace(old_token, new_token, 1)
            self.assertNotEqual(baseline, mutated)
            baseline = mutated
        else:
            mutated = head.replace(old_token, new_token, 1)
            self.assertNotEqual(head, mutated)
            head = mutated
        repository.amend_baseline(TOOL, baseline)
        repository.write(TOOL, head)
        repository.commit(TOOL)
        return self.assert_fails(
            self.intent(
                [self.document_addition("/added")], base=repository.base),
            repository=repository)

    def test_no_golden_change_passes_without_intent(self):
        completed = self.assert_passes()
        self.assertIn("no golden changes", completed.stdout)

    def test_page_only_change_stays_on_human_review_path(self):
        self.repo.amend_baseline(PAGE, "HTTP 200\n<html></html>\n")
        self.repo.write(
            PAGE,
            'HTTP 200\n<html><main id="main" data-testid="main-panel">new</main></html>\n')
        self.repo.commit(PAGE)

        missing = self.assert_fails()
        self.assertIn("intent", missing.stderr.lower())
        completed = self.assert_passes(event_body=self.fenced(self.html_intent()))

        self.assertIn("machine scope", completed.stdout)
        self.assertIn("human diff review", completed.stdout)
        self.assert_passes(event_body=self.fenced(self.html_intent(
            selector='[data-testid="main-panel"]')))

    def test_html_head_requires_exactly_one_selector_occurrence(self):
        for name, head in (
            ("missing", "HTTP 200\n<html><main>new</main></html>\n"),
            ("duplicate", 'HTTP 200\n<html><i id="main"></i><b id="main"></b></html>\n'),
        ):
            with self.subTest(name=name):
                repository = self.fresh_repository()
                repository.write(PAGE, head)
                repository.commit(PAGE)
                completed = self.assert_fails(
                    self.html_intent(base=repository.base), repository=repository)
                self.assertIn("exactly once", completed.stderr)

    def test_quirks_mode_id_cardinality_uses_ascii_case_folding(self):
        cases = (
            (
                "duplicate in base",
                'HTTP 200\n<html><main id="main">old<aside id="MAIN"></aside>'
                '</main></html>\n',
                'HTTP 200\n<html><main id="main">new</main></html>\n',
                "at most once",
            ),
            (
                "duplicate in head",
                'HTTP 200\n<html><main id="main">old</main></html>\n',
                'HTTP 200\n<html><main id="main">new<aside id="MAIN"></aside>'
                '</main></html>\n',
                "exactly once",
            ),
        )
        for name, baseline, head, message in cases:
            with self.subTest(name=name):
                repository = self.fresh_repository()
                repository.amend_baseline(PAGE, baseline)
                repository.write(PAGE, head)
                repository.commit(PAGE)

                completed = self.assert_fails(
                    self.html_intent(base=repository.base), repository=repository)

                self.assertIn(message, completed.stderr)

    def test_quirks_mode_id_fold_does_not_change_standards_or_testid_semantics(self):
        quirks = self.fresh_repository()
        quirks.amend_baseline(
            PAGE, 'HTTP 200\n<html><main id="MAIN">old</main></html>\n')
        quirks.write(
            PAGE, 'HTTP 200\n<html><main id="MAIN">new</main></html>\n')
        quirks.commit(PAGE)
        self.assert_passes(
            self.html_intent(base=quirks.base), repository=quirks)

        standards = self.fresh_repository()
        standards.amend_baseline(
            PAGE,
            '<!doctype html><html><aside id="MAIN">outside</aside>'
            '<main id="main">old</main></html>\n')
        standards.write(
            PAGE,
            '<!doctype html><html><aside id="MAIN">outside</aside>'
            '<main id="main">new</main></html>\n')
        standards.commit(PAGE)
        self.assert_passes(
            self.html_intent(base=standards.base), repository=standards)

        testid = self.fresh_repository()
        testid.amend_baseline(
            PAGE,
            'HTTP 200\n<html><aside data-testid="MAIN">outside</aside>'
            '<main data-testid="main">old</main></html>\n')
        testid.write(
            PAGE,
            'HTTP 200\n<html><aside data-testid="MAIN">outside</aside>'
            '<main data-testid="main">new</main></html>\n')
        testid.commit(PAGE)
        self.assert_passes(
            self.html_intent(selector='[data-testid="main"]', base=testid.base),
            repository=testid)

    def test_declarative_shadow_dom_is_rejected_before_selector_matching(self):
        cases = (
            (
                "empty selected template",
                '<!doctype html><div id="host"></div>\n',
                '<!doctype html><div id="host"><template id="main" '
                'shadowrootmode="open"></template></div>\n',
            ),
            (
                "outside selected scope",
                '<!doctype html><div id="host"><template shadowrootmode="open">'
                '<span>shadow</span></template></div><main id="main">old</main>\n',
                '<!doctype html><div id="host"><template shadowrootmode="open">'
                '<span>shadow</span></template></div><main id="main">new</main>\n',
            ),
            (
                "inside selected scope",
                '<!doctype html><main id="main"><div><template '
                'shadowrootmode="closed"><span>old shadow</span></template>'
                '</div>old</main>\n',
                '<!doctype html><main id="main"><div><template '
                'shadowrootmode="closed"><span>new shadow</span></template>'
                '</div>new</main>\n',
            ),
        )
        for name, baseline, head in cases:
            with self.subTest(name=name):
                repository = self.fresh_repository()
                repository.amend_baseline(PAGE, baseline)
                repository.write(PAGE, head)
                repository.commit(PAGE)

                completed = self.assert_fails(
                    self.html_intent(base=repository.base), repository=repository)

                self.assertIn("HTML", completed.stderr)

    def test_plain_template_retains_conservative_dom_scope_behavior(self):
        empty = self.fresh_repository()
        empty.amend_baseline(PAGE, '<!doctype html><div id="host"></div>\n')
        empty.write(
            PAGE,
            '<!doctype html><div id="host"><template id="main"></template>'
            '</div>\n')
        empty.commit(PAGE)
        self.assert_passes(
            self.html_intent(base=empty.base), repository=empty)

        content = self.fresh_repository()
        content.amend_baseline(
            PAGE,
            '<!doctype html><main id="main"><template><span>old</span>'
            '</template></main>\n')
        content.write(
            PAGE,
            '<!doctype html><main id="main"><template><span>new</span>'
            '</template></main>\n')
        content.commit(PAGE)
        completed = self.assert_fails(
            self.html_intent(base=content.base), repository=content)
        self.assertIn("HTML", completed.stderr)

    def test_customizable_select_markup_fails_closed(self):
        cases = (
            (
                "hidden duplicate inside selected scope",
                '<!doctype html><main id="main"><select><button>'
                '<span id="main">x</span></button><option>one</option>'
                '</select>old</main>\n',
                '<!doctype html><main id="main"><select><button>'
                '<span id="main">x</span></button><option>one</option>'
                '</select>new</main>\n',
            ),
            (
                "hidden duplicate inside merged text",
                '<!doctype html><main id="main"><select>before'
                '<span id="main">x</span>after<option>one</option>'
                '</select>old</main>\n',
                '<!doctype html><main id="main"><select>before'
                '<span id="main">x</span>after<option>one</option>'
                '</select>new</main>\n',
            ),
            (
                "hidden duplicate outside selected scope",
                '<!doctype html><select><button><span id="main">x</span>'
                '</button><option>one</option></select>'
                '<main id="main">old</main>\n',
                '<!doctype html><select><button><span id="main">x</span>'
                '</button><option>one</option></select>'
                '<main id="main">new</main>\n',
            ),
            (
                "customizable syntax without duplicate",
                '<!doctype html><select><button>pick</button><option>one</option>'
                '</select><main id="main">old</main>\n',
                '<!doctype html><select><button>pick</button><option>one</option>'
                '</select><main id="main">new</main>\n',
            ),
            (
                "select without an explicit parser boundary",
                '<!doctype html><select><option>one</option><input></select>'
                '<main id="main">old</main>\n',
                '<!doctype html><select><option>one</option><input></select>'
                '<main id="main">new</main>\n',
            ),
        )
        for name, baseline, head in cases:
            with self.subTest(name=name):
                repository = self.fresh_repository()
                repository.amend_baseline(PAGE, baseline)
                repository.write(PAGE, head)
                repository.commit(PAGE)

                completed = self.assert_fails(
                    self.html_intent(base=repository.base), repository=repository)

                self.assertIn("HTML", completed.stderr)

    def test_plain_select_and_standalone_button_keep_browser_scope(self):
        self.repo.amend_baseline(
            PAGE,
            '<!doctype html><button>outside</button><select>'
            '<!-- <span id="main">comment</span> -->'
            '<optgroup label="a > b"><option>one</option></optgroup>'
            '<hr><script>const fake = "<span id=main>";</script>'
            '<template><option>template</option></template></select>'
            '<main id="main">old</main>\n')
        self.repo.write(
            PAGE,
            '<!doctype html><button>outside</button><select>'
            '<!-- <span id="main">comment</span> -->'
            '<optgroup label="a > b"><option>one</option></optgroup>'
            '<hr><script>const fake = "<span id=main>";</script>'
            '<template><option>template</option></template></select>'
            '<main id="main">new</main>\n')
        self.repo.commit(PAGE)

        self.assert_passes(self.html_intent())

    def test_checked_in_page_select_markup_remains_supported(self):
        listed = subprocess.run(
            ["git", "ls-files", "--", f"{GOLDEN.as_posix()}/page-*.txt"],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=True,
        )
        paths = [path for path in listed.stdout.splitlines() if path]
        self.assertTrue(paths)
        for path in paths:
            with self.subTest(path=path):
                raw = subprocess.run(
                    ["git", "show", f"HEAD:{path}"],
                    cwd=ROOT,
                    capture_output=True,
                    check=True,
                ).stdout
                self.assertEqual(
                    (0, None),
                    golden_diff.html_scope(
                        raw, "#lex-golden-scope-contract-probe", path))

    def test_html_selector_is_parsed_and_may_be_new_in_head(self):
        self.repo.amend_baseline(
            PAGE,
            'HTTP 200\n<html><script>"<div id=\\"main\\">"</script></html>\n')
        self.repo.write(
            PAGE,
            'HTTP 200\n<html><script>"<div id=\\"main\\">"</script>'
            '<main id="main">new</main></html>\n')
        self.repo.commit(PAGE)

        completed = self.assert_passes(self.html_intent())

        self.assertIn("human diff review", completed.stdout)

    def test_html_selector_is_a_machine_scope_not_a_cardinality_label(self):
        baseline = (
            'HTTP 200\n<html><header>same</header><main id="main">old</main>'
            '<footer>same</footer></html>\n')
        mutations = (
            (
                "footer",
                'HTTP 200\n<html><header>same</header><main id="main">old</main>'
                '<footer>changed</footer></html>\n',
            ),
            (
                "header",
                'HTTP 200\n<html><header>changed</header><main id="main">old</main>'
                '<footer>same</footer></html>\n',
            ),
            (
                "sibling insertion",
                'HTTP 200\n<html><header>same</header><aside>new</aside>'
                '<main id="main">old</main><footer>same</footer></html>\n',
            ),
            (
                "sibling deletion",
                'HTTP 200\n<html><main id="main">old</main>'
                '<footer>same</footer></html>\n',
            ),
        )
        for name, head in mutations:
            with self.subTest(name=name):
                repository = self.fresh_repository()
                repository.amend_baseline(PAGE, baseline)
                repository.write(PAGE, head)
                repository.commit(PAGE)

                completed = self.assert_fails(
                    self.html_intent(base=repository.base), repository=repository)

                self.assertIn("outside", completed.stderr)

    def test_html_selector_allows_only_its_exact_subtree_to_change(self):
        self.repo.amend_baseline(
            PAGE,
            'HTTP 200\n<html><header>same</header><main id="main">old'
            '<span>value</span></main><footer>same</footer></html>\n')
        self.repo.write(
            PAGE,
            'HTTP 200\n<html><header>same</header><main id="main">new'
            '<span>changed</span></main><footer>same</footer></html>\n')
        self.repo.commit(PAGE)

        completed = self.assert_passes(self.html_intent())

        self.assertIn("machine scope", completed.stdout)
        self.assertIn("human diff review", completed.stdout)

    def test_html5_scope_handles_comments_raw_text_quoted_gt_and_void_targets(self):
        self.repo.amend_baseline(
            PAGE,
            'HTTP 200\n<html><!-- outside --><style>.x > b { color: red }</style>'
            '<main id="main" title="a > b"><script>const marker = "<main id=main>";'
            '</script>old</main><footer>same</footer></html>\n')
        self.repo.write(
            PAGE,
            'HTTP 200\n<html><!-- outside --><style>.x > b { color: red }</style>'
            '<main id="main" title="a > b"><script>const marker = "<main id=main>";'
            '</script>new<!-- inside --></main><footer>same</footer></html>\n')
        self.repo.commit(PAGE)

        self.assert_passes(self.html_intent())

        void = self.fresh_repository()
        void.amend_baseline(
            PAGE, 'HTTP 200\n<html><img id="main" alt="old"><footer>same</footer>'
            '</html>\n')
        void.write(
            PAGE, 'HTTP 200\n<html><img id="main" alt="new"><footer>same</footer>'
            '</html>\n')
        void.commit(PAGE)

        self.assert_passes(
            self.html_intent(base=void.base), repository=void)

    def test_html_scope_offsets_are_raw_utf8_bytes_with_bom_and_crlf(self):
        baseline = (
            '\ufeffHTTP 200\r\n<html><header>outside 😀</header>'
            '<main id="main">old é</main><footer>same</footer></html>\r\n'
        ).encode("utf-8")
        head = (
            '\ufeffHTTP 200\r\n<html><header>outside 😀</header>'
            '<main id="main">new 日本語</main><footer>same</footer></html>\r\n'
        ).encode("utf-8")
        self.repo.amend_baseline_bytes(PAGE, baseline)
        self.repo.write_bytes(PAGE, head)
        self.repo.commit(PAGE)

        self.assert_passes(self.html_intent())

        outside = self.fresh_repository()
        outside.amend_baseline_bytes(PAGE, baseline)
        outside.write_bytes(
            PAGE,
            head.replace("outside 😀".encode("utf-8"),
                         "outside changed".encode("utf-8")))
        outside.commit(PAGE)

        completed = self.assert_fails(
            self.html_intent(base=outside.base), repository=outside)
        self.assertIn("outside", completed.stderr)

    def test_html_data_testid_selector_has_the_same_exact_scope(self):
        selector = '[data-testid="main-panel"]'
        self.repo.amend_baseline(
            PAGE,
            'HTTP 200\n<html><section data-testid="main-panel">old</section>'
            '<footer>same</footer></html>\n')
        self.repo.write(
            PAGE,
            'HTTP 200\n<html><section data-testid="main-panel">new</section>'
            '<footer>same</footer></html>\n')
        self.repo.commit(PAGE)

        self.assert_passes(self.html_intent(selector=selector))

        outside = self.fresh_repository()
        outside.amend_baseline(
            PAGE,
            'HTTP 200\n<html><section data-testid="main-panel">old</section>'
            '<footer>same</footer></html>\n')
        outside.write(
            PAGE,
            'HTTP 200\n<html><section data-testid="main-panel">old</section>'
            '<footer>changed</footer></html>\n')
        outside.commit(PAGE)

        completed = self.assert_fails(
            self.html_intent(selector=selector, base=outside.base),
            repository=outside)

        self.assertIn("outside", completed.stderr)

    def test_html_new_selector_subtree_must_be_the_only_inserted_bytes(self):
        self.repo.amend_baseline(PAGE, 'HTTP 200\n<html><footer>same</footer></html>\n')
        self.repo.write(
            PAGE,
            'HTTP 200\n<html><main id="main">new</main>'
            '<footer>same</footer></html>\n')
        self.repo.commit(PAGE)

        self.assert_passes(self.html_intent())

        outside = self.fresh_repository()
        outside.amend_baseline(PAGE, 'HTTP 200\n<html><footer>same</footer></html>\n')
        outside.write(
            PAGE,
            'HTTP 200\n<html><header>also new</header><main id="main">new</main>'
            '<footer>same</footer></html>\n')
        outside.commit(PAGE)

        completed = self.assert_fails(
            self.html_intent(base=outside.base), repository=outside)

        self.assertIn("only inserted", completed.stderr)

    def test_html_scope_rejects_unclosed_misnested_and_nested_matches(self):
        mutations = (
            'HTTP 200\n<html><main id="main"><span>unclosed</main></html>\n',
            'HTTP 200\n<html><main id="main"><b>misnested</main></b></html>\n',
            'HTTP 200\n<html><main id="main"><section id="main">nested'
            '</section></main></html>\n',
        )
        for head in mutations:
            with self.subTest(head=head):
                repository = self.fresh_repository()
                repository.write(PAGE, head)
                repository.commit(PAGE)

                completed = self.assert_fails(
                    self.html_intent(base=repository.base), repository=repository)

                self.assertTrue(
                    "structure" in completed.stderr or "exactly once" in completed.stderr,
                    completed.stderr)

    def test_html_scope_uses_browser_tree_construction_not_lexical_nesting(self):
        cases = (
            (
                "implicit p closure",
                'HTTP 200\n<html><p id="main">old</p></html>\n',
                'HTTP 200\n<html><p id="main">new<div>reparented</div></p></html>\n',
            ),
            (
                "foster parenting",
                'HTTP 200\n<html><table id="main"><tbody><tr><td>old</td>'
                '</tr></tbody></table></html>\n',
                'HTTP 200\n<html><table id="main"><div>reparented</div><tbody>'
                '<tr><td>new</td></tr></tbody></table></html>\n',
            ),
            (
                "formatting element reconstruction",
                'HTTP 200\n<html><main id="main"><p><b>old</b></p></main></html>\n',
                'HTTP 200\n<html><main id="main"><p><b>new</p>reconstructed</b>'
                '</main></html>\n',
            ),
        )
        for name, baseline, head in cases:
            with self.subTest(name=name):
                repository = self.fresh_repository()
                repository.amend_baseline(PAGE, baseline)
                repository.write(PAGE, head)
                repository.commit(PAGE)

                completed = self.assert_fails(
                    self.html_intent(base=repository.base), repository=repository)

                self.assertIn("HTML", completed.stderr)

    def test_html_scope_rejects_duplicate_selected_attributes_in_both_orders(self):
        baseline = 'HTTP 200\n<html><main id="main">old</main></html>\n'
        for head in (
            'HTTP 200\n<html><main id="main" id="other">new</main></html>\n',
            'HTTP 200\n<html><main id="other" id="main">new</main></html>\n',
        ):
            with self.subTest(head=head):
                repository = self.fresh_repository()
                repository.amend_baseline(PAGE, baseline)
                repository.write(PAGE, head)
                repository.commit(PAGE)

                completed = self.assert_fails(
                    self.html_intent(base=repository.base), repository=repository)

                self.assertIn("HTML", completed.stderr)

    def test_html_scope_rejects_non_void_self_closing_target(self):
        self.repo.amend_baseline(PAGE, 'HTTP 200\n<html>same</html>\n')
        self.repo.write(PAGE, 'HTTP 200\n<html><main id="main"/>same</html>\n')
        self.repo.commit(PAGE)

        completed = self.assert_fails(self.html_intent())

        self.assertIn("HTML", completed.stderr)

    def test_html_scope_helper_failure_and_output_are_bounded(self):
        raw = b'<main id="main"></main>'
        success = subprocess.CompletedProcess(
            ["node"], 0,
            stdout=(b'{"schema":"lex-html-scope/1","count":1,'
                    b'"start":0,"end":23}\n'),
            stderr=b"")
        with mock.patch.object(
                golden_diff.subprocess, "run", return_value=success) as invoked:
            self.assertEqual((1, (0, 23)),
                             golden_diff.html_scope(raw, "#main", "head"))
        arguments, keywords = invoked.call_args
        self.assertEqual(
            ["node", str(golden_diff.HTML_SCOPE_HELPER), "#main"], arguments[0])
        self.assertEqual(raw, keywords["input"])
        self.assertNotIn("shell", keywords)

        failure = subprocess.CompletedProcess(
            ["node"], 2, stdout=b"", stderr=b"private candidate detail")
        with mock.patch.object(golden_diff.subprocess, "run", return_value=failure):
            with self.assertRaises(golden_diff.Rejection) as raised:
                golden_diff.html_scope(b"<main id=\"main\"></main>", "#main", "head")
        self.assertEqual("head HTML parser rejected malformed structure",
                         str(raised.exception))

        oversized = subprocess.CompletedProcess(
            ["node"], 0,
            stdout=b"x" * (golden_diff.MAX_HTML_HELPER_OUTPUT_BYTES + 1),
            stderr=b"")
        with mock.patch.object(golden_diff.subprocess, "run", return_value=oversized):
            with self.assertRaises(golden_diff.Rejection) as raised:
                golden_diff.html_scope(b"<main id=\"main\"></main>", "#main", "head")
        self.assertEqual("head HTML parser output exceeded its byte limit",
                         str(raised.exception))

    def test_html_base_selector_cannot_be_ambiguous(self):
        self.repo.amend_baseline(
            PAGE,
            'HTTP 200\n<html><i id="main"></i><b id="main"></b></html>\n')
        self.repo.write(PAGE, 'HTTP 200\n<html><main id="main">new</main></html>\n')
        self.repo.commit(PAGE)

        completed = self.assert_fails(self.html_intent())

        self.assertIn("base", completed.stderr)
        self.assertIn("at most once", completed.stderr)

    def test_html_base_and_head_must_be_bounded_strict_utf8(self):
        head_invalid = self.fresh_repository()
        head_invalid.write_bytes(
            PAGE, b'HTTP 200\n<html><main id="main">new</main></html>\xff\n')
        head_invalid.commit(PAGE)
        bad_head = self.assert_fails(
            self.html_intent(base=head_invalid.base), repository=head_invalid)

        base_invalid = self.fresh_repository()
        base_invalid.amend_baseline_bytes(PAGE, b"HTTP 200\n<html>old</html>\xff\n")
        base_invalid.write(
            PAGE, 'HTTP 200\n<html><main id="main">new</main></html>\n')
        base_invalid.commit(PAGE)
        bad_base = self.assert_fails(
            self.html_intent(base=base_invalid.base), repository=base_invalid)

        oversized = self.fresh_repository()
        oversized.write_bytes(
            PAGE,
            b'HTTP 200\n<html><main id="main">' +
            b"x" * (golden_diff.MAX_JSON_BYTES + 1) + b"</main></html>\n")
        oversized.commit(PAGE)
        too_large = self.assert_fails(
            self.html_intent(base=oversized.base), repository=oversized)

        self.assertIn("UTF-8 HTML", bad_head.stderr)
        self.assertIn("UTF-8 HTML", bad_base.stderr)
        self.assertIn("byte limit", too_large.stderr)

    def test_html_mode_only_change_fails(self):
        self.repo.git("update-index", "--chmod=+x", "--", PAGE.as_posix())
        self.repo.git("commit", "-m", "mode only")
        # The committed tree is the subject of this test. On POSIX, the test
        # file itself remains non-executable and otherwise looks dirty before
        # the classifier can compare the two committed modes.
        self.repo.git("config", "core.filemode", "false")
        self.assertEqual(
            "",
            self.repo.git(
                "status", "--porcelain=v1", "--", PAGE.as_posix()).stdout,
        )

        completed = self.assert_fails(self.html_intent())

        self.assertIn("mode", completed.stderr.lower())

    def test_pr_body_supplies_the_json_intent_without_shell_evaluation(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "literal": "$(exit 97) `exit 98`",
        }))
        self.repo.commit(TOOL)

        completed = self.assert_passes(event_body=self.fenced(self.intent([
            self.document_addition("/literal"),
        ])))

        self.assertIn("approved 1", completed.stdout)

    def test_pr_body_intent_must_be_one_fenced_json_object(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "added": True,
        }))
        self.repo.commit(TOOL)
        valid = self.fenced(self.intent([self.document_addition("/added")]))

        missing = self.assert_fails(event_body="ordinary pull request body")
        self.assertIn("fenced", missing.stderr)
        duplicate = self.assert_fails(event_body=valid + valid)
        self.assertIn("exactly one", duplicate.stderr)

    def test_pr_body_rejects_malformed_trailing_and_oversized_payloads(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "added": True,
        }))
        self.repo.commit(TOOL)

        malformed = self.assert_fails(event_body="```json\n{not json}\n```\n")
        self.assertIn("strict", malformed.stderr)
        trailing = self.assert_fails(event_body="```json\n{} trailing\n```\n")
        self.assertIn("strict", trailing.stderr)
        oversized = self.assert_fails(event_body="x" * 140_000)
        self.assertIn("byte limit", oversized.stderr)

    def test_external_json_files_are_read_with_a_limit_plus_one_probe(self):
        stream = mock.MagicMock()
        stream.__enter__.return_value = stream
        stream.read.return_value = b"x" * 12
        path = mock.MagicMock()
        path.open.return_value = stream
        with mock.patch.object(golden_diff, "Path", return_value=path):
            with self.assertRaises(golden_diff.Rejection) as raised:
                golden_diff.read_bounded_file(
                    "ignored", 11, "unreadable", "too large")

        stream.read.assert_called_once_with(12)
        self.assertEqual("too large", str(raised.exception))

    def test_intent_and_event_reject_exactly_one_byte_over_their_limits(self):
        cases = (
            ("intent", golden_diff.MAX_INTENT_BYTES, golden_diff.load_intent),
            ("event", golden_diff.MAX_EVENT_BYTES, golden_diff.load_event_intent),
        )
        for name, limit, loader in cases:
            with self.subTest(name=name):
                path = self.root.parent / f"{self.root.name}-{name}-oversized.json"
                path.write_bytes(b" " * limit + b"x")
                with self.assertRaises(golden_diff.Rejection) as raised:
                    loader(path, self.repo.base)
                self.assertIn("byte limit", str(raised.exception))

    def test_no_golden_change_does_not_require_a_pr_body_intent(self):
        self.assert_passes(event_body="ordinary pull request body")

        stale = self.assert_fails(event_body=self.fenced(self.intent([
            self.document_addition("/stale"),
        ])))
        self.assertIn("stale", stale.stderr)

    def test_pr_event_base_must_match_the_separately_supplied_base(self):
        completed = self.assert_fails(
            event_body="ordinary pull request body", event_base="f" * 40)

        self.assertIn("pull request base", completed.stderr)

    def test_html_selector_scope_is_exact_narrow_and_nonempty(self):
        self.repo.write(PAGE, "HTTP 200\n<html>new</html>\n")
        self.repo.commit(PAGE)

        for selector in ("", "*", "html", "body", ".broad", "#has space"):
            with self.subTest(selector=selector):
                completed = self.assert_fails(
                    event_body=self.fenced(self.html_intent(selector=selector)))
                self.assertIn("selector", completed.stderr.lower())

    def test_html_selector_file_must_match_the_changed_page_one_to_one(self):
        self.repo.write(PAGE, "HTTP 200\n<html>new</html>\n")
        self.repo.commit(PAGE)

        stale = self.assert_fails(event_body=self.fenced(self.html_intent(
            file=GOLDEN / "page-about.txt")))

        self.assertIn("stale", stale.stderr)

    def test_html_selector_scope_covers_each_changed_page_exactly_once(self):
        self.repo.write(PAGE, "HTTP 200\n<html>new</html>\n")
        self.repo.write(PAGE_ABOUT, "HTTP 200\n<html>new about</html>\n")
        self.repo.commit(PAGE, PAGE_ABOUT)

        incomplete = self.assert_fails(
            event_body=self.fenced(self.html_intent()))

        self.assertIn("undeclared", incomplete.stderr)

    def test_intent_mode_arrays_must_be_nonempty(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "added": True,
        }))
        self.repo.commit(TOOL)

        empty = self.intent([])
        completed = self.assert_fails(event_body=self.fenced(empty))

        self.assertIn("nonempty", completed.stderr)

    def test_mixing_page_and_json_golden_families_fails(self):
        self.repo.write(PAGE, "HTTP 200\n<html>new</html>\n")
        self.repo.write(TOOL, mcp({"items": [{"id": "existing"}], "added": True}))
        self.repo.commit(PAGE, TOOL)

        completed = self.assert_fails(self.intent([
            self.document_addition("/added"),
        ]))

        self.assertIn("mix", completed.stderr.lower())

    def test_declared_mcp_document_addition_passes(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing", "score": 1}],
            "summary": {"count": 1},
        }))
        self.repo.commit(TOOL)

        completed = self.assert_passes(self.intent([
            self.document_addition("/items/0/score"),
            self.document_addition("/summary"),
        ]))

        self.assertIn("2 declared JSON additions", completed.stdout)

    def test_declared_direct_tools_list_append_passes(self):
        self.repo.write(TOOLS_LIST, compact({
            "jsonrpc": "2.0",
            "id": 1,
            "result": {"tools": [
                {"name": "search"},
                {"name": "timeline"},
            ]},
        }))
        self.repo.commit(TOOLS_LIST)

        self.assert_passes(self.intent([
            self.direct_addition("/result/tools/1"),
        ]))

    def test_declared_addition_cannot_hide_existing_json_type_change(self):
        for family in ("direct", "mcp"):
            for replacement in (True, 1.0):
                with self.subTest(family=family, replacement=repr(replacement)):
                    document = {"value": replacement, "added": True}
                    indent = 2 if family == "mcp" else None
                    separators = None if indent else (",", ":")
                    head = json.dumps(
                        document, ensure_ascii=False, indent=indent, separators=separators)
                    completed = self.assert_json_probe_fails(
                        family, {"value": 1}, head)
                    self.assertIn("replacement", completed.stderr)

    def test_declared_addition_cannot_hide_noncanonical_whitespace(self):
        direct = self.assert_json_probe_fails(
            "direct", {"value": 1}, '{ "value": 1, "added": true }')
        inner = self.assert_json_probe_fails(
            "mcp", {"value": 1}, json.dumps(
                {"value": 1, "added": True}, ensure_ascii=False, indent=4))

        self.assertIn("canonical", direct.stderr)
        self.assertIn("canonical", inner.stderr)

    def test_json_preserves_existing_csharp_escape_spelling(self):
        documents = {
            "direct": (
                TOOLS_LIST,
                '{"value":"\\u00E9"}\n',
                '{"value":"\\u00E9","added":true}\n',
                '{"value":"é","added":true}\n',
                self.direct_addition("/added"),
            ),
            "mcp": (
                TOOL,
                mcp_text('{\n  "value": "\\u00E9"\n}'),
                mcp_text('{\n  "value": "\\u00E9",\n  "added": true\n}'),
                mcp_text('{\n  "value": "é",\n  "added": true\n}'),
                self.document_addition("/added"),
            ),
        }
        for family, (path, baseline, retained, changed, addition) in documents.items():
            with self.subTest(family=family, spelling="retained"):
                repository = self.fresh_repository()
                repository.amend_baseline(path, baseline)
                repository.write(path, retained)
                repository.commit(path)
                self.assert_passes(
                    self.intent([addition], base=repository.base),
                    repository=repository)

            with self.subTest(family=family, spelling="rewritten"):
                repository = self.fresh_repository()
                repository.amend_baseline(path, baseline)
                repository.write(path, changed)
                repository.commit(path)
                completed = self.assert_fails(
                    self.intent([addition], base=repository.base),
                    repository=repository)
                self.assertIn("lexical", completed.stderr)

    def test_mcp_outer_text_uses_exact_canonical_json_string_encoding(self):
        literal_base = '{\n  "value": "é"\n}'
        literal_head = '{\n  "value": "é",\n  "added": true\n}'
        escaped_base = '{\n  "value": "\\u00E9"\n}'
        escaped_head = '{\n  "value": "\\u00E9",\n  "added": true\n}'
        cases = (
            ("literal-lower", literal_base, literal_head, "é", "\\u00e9"),
            ("literal-upper", literal_base, literal_head, "é", "\\u00E9"),
            ("backslash", escaped_base, escaped_head,
             "\\\\u00E9", "\\u005cu00E9"),
            ("newline", literal_base, literal_head, "\\n", "\\u000A"),
            ("quote", literal_base, literal_head, '\\"', "\\u0022"),
        )
        for name, baseline, head, old_token, new_token in cases:
            for side in ("base", "head"):
                with self.subTest(name=name, side=side):
                    completed = self.assert_mcp_outer_encoding_probe_fails(
                        baseline, head, old_token, new_token,
                        mutate_base=side == "base")
                    self.assertIn("outer JSON string encoding", completed.stderr)

    def test_checked_in_json_golden_git_blobs_meet_the_classifier_base_contract(self):
        tool_paths = sorted(
            path.relative_to(ROOT).as_posix()
            for path in (ROOT / GOLDEN).glob("tool-*.txt"))
        self.assertEqual(15, len(tool_paths))
        for path in tool_paths:
            with self.subTest(path=path):
                raw = subprocess.run(
                    ["git", "show", f"HEAD:{path}"],
                    cwd=ROOT,
                    capture_output=True,
                    check=True,
                ).stdout
                envelope, document, text = golden_diff.mcp_document(raw, path)
                golden_diff.scan_json_layout(
                    raw.decode("utf-8"), envelope, path, "compact", newline=True)
                golden_diff.require_canonical_outer_text(raw, text, path)
                golden_diff.scan_json_layout(
                    text, document, f"{path} embedded document", "indented")

        tools_list = TOOLS_LIST.as_posix()
        raw = subprocess.run(
            ["git", "show", f"HEAD:{tools_list}"],
            cwd=ROOT,
            capture_output=True,
            check=True,
        ).stdout
        document = golden_diff.parse_json(raw, tools_list)
        golden_diff.scan_json_layout(
            raw.decode("utf-8"), document, tools_list, "compact", newline=True)

    def test_base_json_layout_must_already_be_canonical(self):
        direct = self.fresh_repository()
        direct.amend_baseline(TOOLS_LIST, '{ "value" : 1 }\n')
        direct.write(TOOLS_LIST, '{"value":1,"added":true}\n')
        direct.commit(TOOLS_LIST)
        direct_result = self.assert_fails(
            self.intent(
                [self.direct_addition("/added")], base=direct.base),
            repository=direct)

        mcp_repository = self.fresh_repository()
        mcp_repository.amend_baseline(
            TOOL, mcp_text(json.dumps({"value": 1}, indent=4)))
        mcp_repository.write(
            TOOL, mcp({"value": 1, "added": True}))
        mcp_repository.commit(TOOL)
        mcp_result = self.assert_fails(
            self.intent(
                [self.document_addition("/added")], base=mcp_repository.base),
            repository=mcp_repository)

        self.assertIn("base", direct_result.stderr)
        self.assertIn("canonical", direct_result.stderr)
        self.assertIn("base", mcp_result.stderr)
        self.assertIn("canonical", mcp_result.stderr)

    def test_declared_addition_cannot_hide_existing_key_reorder(self):
        reordered = {"second": 2, "first": 1, "added": True}
        for family in ("direct", "mcp"):
            with self.subTest(family=family):
                indent = 2 if family == "mcp" else None
                separators = None if indent else (",", ":")
                head = json.dumps(
                    reordered, ensure_ascii=False, indent=indent, separators=separators)
                completed = self.assert_json_probe_fails(
                    family, {"first": 1, "second": 2}, head)
                self.assertIn("key order", completed.stderr)

    def test_json_depth_and_node_counts_are_bounded(self):
        nested = 0
        for _ in range(140):
            nested = [nested]
        deep = self.assert_json_probe_fails(
            "direct", {}, json.dumps(
                {"added": nested}, ensure_ascii=False, separators=(",", ":")))

        wide = self.assert_json_probe_fails(
            "mcp", {}, json.dumps(
                {"added": [0] * 100_001}, ensure_ascii=False, indent=2))

        self.assertIn(
            f"maximum JSON depth of {golden_diff.MAX_JSON_DEPTH}", deep.stderr)
        self.assertIn(
            f"maximum JSON node count of {golden_diff.MAX_JSON_NODES:,}", wide.stderr)

    def test_trusted_golden_workflow_is_base_controlled_and_least_privilege(self):
        workflow = (ROOT / ".github" / "workflows" /
                    "trusted-golden-diff.yml").read_text(encoding="utf-8")

        self.assertIn("name: trusted-golden-diff\n", workflow)
        self.assertIn(
            "  pull_request_target:\n"
            "    branches: [main]\n"
            "    types: [opened, synchronize, reopened, edited]\n",
            workflow)
        self.assertNotIn("ready_for_review", workflow)
        self.assertIn("permissions:\n  contents: read\n", workflow)
        self.assertNotIn("write", workflow)
        self.assertIn(
            "  trusted-golden-diff:\n"
            "    name: trusted-golden-diff\n",
            workflow)
        self.assertIn(
            "uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803",
            workflow)
        self.assertEqual(1, workflow.count("uses:"))
        self.assertIn(
            "ref: ${{ github.event.pull_request.base.sha }}", workflow)
        self.assertIn("persist-credentials: false", workflow)

        boundary = workflow.index("- name: Validate trusted event boundary")
        checkout = workflow.index("- name: Check out the exact trusted base")
        self.assertLess(boundary, checkout)
        self.assertIn(
            "TRUSTED_BASE_REF: ${{ github.event.pull_request.base.ref }}", workflow)
        self.assertIn(
            "TRUSTED_BASE_REPOSITORY: "
            "${{ github.event.pull_request.base.repo.full_name }}", workflow)
        self.assertIn("TRUSTED_REPOSITORY: ${{ github.repository }}", workflow)
        self.assertIn('[[ "$TRUSTED_BASE_REF" != "main" ]]', workflow)
        self.assertIn(
            '[[ "$TRUSTED_BASE_REPOSITORY" != "$TRUSTED_REPOSITORY" ]]', workflow)

    def test_trusted_golden_workflow_treats_the_merge_ref_only_as_data(self):
        workflow = (ROOT / ".github" / "workflows" /
                    "trusted-golden-diff.yml").read_text(encoding="utf-8")
        ordinary_ci = (ROOT / ".github" / "workflows" / "ci.yml").read_text(
            encoding="utf-8")

        self.assertIn('[[ ! "$base" =~ ^[0-9a-f]{40}$ ]]', workflow)
        self.assertIn('[[ ! "$head" =~ ^[0-9a-f]{40}$ ]]', workflow)
        self.assertIn('[[ ! "$pr" =~ ^[1-9][0-9]*$ ]]', workflow)
        self.assertIn('[[ ! "$candidate" =~ ^[0-9a-f]{40}$ ]]', workflow)
        self.assertIn('"+refs/pull/${pr}/merge:${merge_ref}"', workflow)
        self.assertIn('git worktree add --detach "$candidate_dir" "$candidate"', workflow)
        self.assertIn('candidate_parent="$(mktemp -d)"', workflow)
        self.assertIn('candidate_dir="${candidate_parent}/candidate"', workflow)
        self.assertIn('[[ "$candidate_base" != "$base" ]]', workflow)
        self.assertIn('[[ "$candidate_head" != "$head" ]]', workflow)
        self.assertIn('candidate_record="$(git rev-list --parents -n 1 "$candidate")"',
                      workflow)
        self.assertIn('[[ "${#candidate_parts[@]}" -ne 3 ]]', workflow)
        self.assertIn('[[ "${candidate_parts[0]}" != "$candidate" ]]', workflow)
        self.assertIn(
            'python3 "$GITHUB_WORKSPACE/scripts/golden_diff.py"', workflow)
        self.assertEqual(1, workflow.count("python3 "))
        self.assertEqual(
            1, workflow.count('$GITHUB_WORKSPACE/scripts/golden_diff.py'))
        self.assertIn('--repo "$candidate_dir"', workflow)
        self.assertIn('--event "$GITHUB_EVENT_PATH"', workflow)
        self.assertNotIn('$candidate_dir/scripts/golden_diff.py', workflow)
        self.assertNotIn('$candidate_dir/', workflow)
        self.assertNotIn('cd "$candidate_dir"', workflow)
        self.assertNotIn("working-directory:", workflow)
        self.assertNotIn("scripts/golden_diff.py", ordinary_ci)
        self.assertLess(
            ordinary_ci.index("- run: npm ci --no-audit --no-fund"),
            ordinary_ci.index("- name: tooling contract tests"))

    def test_candidate_cannot_add_a_duplicate_trusted_check_name(self):
        for filename, check_name in (
            ("plain.yml", "trusted-golden-diff"),
            ("escaped.yaml", r"trusted\u002dgolden-diff"),
        ):
            with self.subTest(filename=filename):
                repository = self.fresh_repository()
                spoof = Path(".github/workflows") / filename
                repository.write(
                    spoof,
                    "name: spoof\non: pull_request\njobs:\n"
                    f'  spoof:\n    name: "{check_name}"\n'
                    "    runs-on: ubuntu-latest\n    steps: []\n")
                repository.commit(spoof)

                completed = self.assert_fails(repository=repository)

                self.assertIn("trusted-golden-diff", completed.stderr)
                self.assertIn("workflow", completed.stderr)

        repository = self.fresh_repository()
        spoof = Path(".github/workflows/cr-only.yml")
        repository.write_bytes(
            spoof,
            b'name: spoof\ron: pull_request\rjobs:\r  spoof:\r'
            b'    name: "trusted-golden-\\\rdiff"\r'
            b'    runs-on: ubuntu-latest\r    steps: []\r')
        repository.commit(spoof)

        completed = self.assert_fails(repository=repository)
        self.assertIn("trusted-golden-diff", completed.stderr)
        self.assertIn("workflow", completed.stderr)

    def test_canonical_trusted_workflow_is_immutable_once_present_in_base(self):
        workflow = Path(".github/workflows/trusted-golden-diff.yml")
        self.repo.amend_baseline(
            workflow,
            (ROOT / workflow).read_text(encoding="utf-8"))

        self.assert_passes()

        for name, mutation in (
            ("deletion", None),
            (
                "always-green duplicate with the same three markers",
                "name: trusted-golden-diff\non: pull_request\njobs:\n"
                "  trusted-golden-diff:\n"
                "    runs-on: ubuntu-latest\n    steps: []\n"
                "  spoof:\n    name: trusted-golden-diff\n"
                "    runs-on: ubuntu-latest\n    steps: []\n",
            ),
        ):
            with self.subTest(name=name):
                repository = self.fresh_repository()
                repository.amend_baseline(
                    workflow,
                    (ROOT / workflow).read_text(encoding="utf-8"))
                if mutation is None:
                    repository.git("rm", "--", workflow.as_posix())
                    repository.git("commit", "-m", "remove trusted workflow")
                else:
                    self.assertEqual(3, mutation.count("trusted-golden-diff"))
                    repository.write(workflow, mutation)
                    repository.commit(workflow)

                completed = self.assert_fails(repository=repository)
                self.assertIn("trusted workflow", completed.stderr)

    def test_canonical_trusted_workflow_cannot_appear_only_in_head(self):
        workflow = Path(".github/workflows/trusted-golden-diff.yml")
        self.repo.write(
            workflow,
            (ROOT / workflow).read_text(encoding="utf-8"))
        self.repo.commit(workflow)

        completed = self.assert_fails()
        self.assertIn("trusted workflow", completed.stderr)

    def test_trusted_workflow_parent_record_rejects_one_or_three_parents(self):
        def admitted(record, candidate, base, head):
            parts = record.split()
            return (len(parts) == 3 and parts[0] == candidate
                    and parts[1] == base and parts[2] == head)

        candidate = "c" * 40
        base = "b" * 40
        head = "a" * 40
        self.assertTrue(admitted(" ".join((candidate, base, head)),
                                 candidate, base, head))
        self.assertFalse(admitted(" ".join((candidate, base)),
                                  candidate, base, head))
        self.assertFalse(admitted(" ".join((candidate, base, head, "d" * 40)),
                                  candidate, base, head))

    def test_git_worktree_uses_a_nonexistent_child_of_a_temp_parent(self):
        with tempfile.TemporaryDirectory() as parent:
            candidate = Path(parent) / "candidate"
            self.assertFalse(candidate.exists())
            self.repo.git("worktree", "add", "--detach", candidate, "HEAD")
            try:
                self.assertTrue(candidate.is_dir())
            finally:
                self.repo.git("worktree", "remove", "--force", candidate)

    def test_cli_hides_unexpected_exceptions_without_a_traceback(self):
        for error in (
            RecursionError(r"C:\\private\\nested.json"),
            RuntimeError(r"C:\\private\\golden.txt"),
        ):
            with self.subTest(error=type(error).__name__):
                stderr = io.StringIO()
                with mock.patch.object(golden_diff, "classify", side_effect=error):
                    with redirect_stderr(stderr):
                        result = golden_diff.main(["--base", "a" * 40])

                self.assertEqual(1, result)
                self.assertEqual(
                    "golden diff rejected: internal classifier failure\n",
                    stderr.getvalue())
                self.assertNotIn("Traceback", stderr.getvalue())
                self.assertNotIn("private", stderr.getvalue())

    def test_rfc_6901_escaping_is_exact(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "a/b~c": 1,
        }))
        self.repo.commit(TOOL)

        self.assert_passes(self.intent([
            self.document_addition("/a~1b~0c"),
        ]))
        invalid = self.assert_fails(self.intent([
            self.document_addition("/a~1b~c"),
        ]))
        self.assertIn("RFC 6901", invalid.stderr)

    def test_undeclared_and_stale_additions_fail(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "added": True,
        }))
        self.repo.commit(TOOL)

        undeclared = self.assert_fails()
        self.assertIn("intent", undeclared.stderr.lower())
        stale = self.assert_fails(self.intent([
            self.document_addition("/other"),
        ]))
        self.assertIn("undeclared", stale.stderr)
        self.assertIn("stale", stale.stderr)

    def test_replacement_reports_only_bounded_metadata_for_large_values(self):
        large = "sensitive-" + "x" * 200_000
        self.repo.write(TOOL, mcp({"items": large, "added": True}))
        self.repo.commit(TOOL)

        completed = self.assert_fails(self.intent([
            self.document_addition("/added"),
        ]))

        self.assertIn("replacement", completed.stderr)
        self.assertIn("type=string", completed.stderr)
        self.assertIn("size=", completed.stderr)
        self.assertIn(hashlib.sha256(json.dumps(
            large, ensure_ascii=False, separators=(",", ":")
        ).encode("utf-8")).hexdigest(), completed.stderr)
        self.assertNotIn(large[:100], completed.stderr)
        self.assertLess(len(completed.stderr), 2_000)

    def test_remove_and_rename_fail(self):
        self.repo.write(TOOL, mcp({"items": []}))
        self.repo.commit(TOOL)
        removed = self.assert_fails(self.intent([
            self.document_addition("/declared"),
        ]))
        self.assertIn("removal", removed.stderr)

    def test_object_key_rename_fails(self):
        self.repo.write(TOOL, mcp({"things": [{"id": "existing"}]}))
        self.repo.commit(TOOL)

        renamed = self.assert_fails(self.intent([
            self.document_addition("/things"),
        ]))

        self.assertIn("removal", renamed.stderr)

    def test_array_insertion_is_not_misclassified_as_an_append(self):
        self.repo.write(TOOLS_LIST, compact({
            "jsonrpc": "2.0",
            "id": 1,
            "result": {"tools": [
                {"name": "timeline"},
                {"name": "search"},
            ]},
        }))
        self.repo.commit(TOOLS_LIST)

        completed = self.assert_fails(self.intent([
            self.direct_addition("/result/tools/1"),
        ]))

        self.assertIn("replacement", completed.stderr)

    def test_format_only_change_fails(self):
        document = '{\n  "items": [\n    {"id": "existing"}\n  ]\n}'
        self.repo.write(TOOL, compact({
            "jsonrpc": "2.0",
            "id": 1,
            "result": {"content": [{"type": "text", "text": document}]},
        }))
        self.repo.commit(TOOL)

        completed = self.assert_fails(self.intent([
            self.document_addition("/declared"),
        ]))

        self.assertIn("format-only", completed.stderr)

    def test_direct_tools_list_format_only_change_fails(self):
        self.repo.write(TOOLS_LIST, json.dumps({
            "jsonrpc": "2.0",
            "id": 1,
            "result": {"tools": [{"name": "search"}]},
        }, indent=2) + "\n")
        self.repo.commit(TOOLS_LIST)

        completed = self.assert_fails(self.intent([
            self.direct_addition("/declared"),
        ]))

        self.assertIn("format-only", completed.stderr)

    def test_outer_mcp_envelope_must_not_change(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "added": True,
        }, request_id=2))
        self.repo.commit(TOOL)

        completed = self.assert_fails(self.intent([
            self.document_addition("/added"),
        ]))

        self.assertIn("outer MCP envelope", completed.stderr)

    def test_file_addition_fails(self):
        added = GOLDEN / "tool-added.txt"
        self.repo.write(added, mcp({"new": True}))
        self.repo.commit(added)
        addition = self.assert_fails()
        self.assertIn("file additions", addition.stderr)

    def test_file_delete_fails(self):
        self.repo.git("rm", "--", TOOL.as_posix())
        self.repo.git("commit", "-m", "delete")

        deletion = self.assert_fails()

        self.assertIn("file deletions", deletion.stderr)

    def test_file_rename_fails(self):
        target = GOLDEN / "tool-renamed.txt"
        self.repo.git("mv", "--", TOOL.as_posix(), target.as_posix())
        self.repo.git("commit", "-m", "rename")

        renamed = self.assert_fails()

        self.assertIn("renames", renamed.stderr)

    def test_duplicate_keys_fail_in_intent_outer_and_document_json(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "added": True,
        }))
        self.repo.commit(TOOL)
        duplicate_intent = (
            '{"schema":"lex-golden-diff-intent/1",'
            '"schema":"lex-golden-diff-intent/1",'
            f'"base_commit":"{self.repo.base}","additions":[]}}'
        )
        self.assertIn("duplicate", self.assert_fails(duplicate_intent).stderr)

    def test_duplicate_key_in_outer_json_fails(self):
        self.repo.write(TOOL,
                        '{"jsonrpc":"2.0","id":1,"id":2,"result":{"content":['
                        '{"type":"text","text":"{\\"items\\":[]}"}]}}\n')
        self.repo.commit(TOOL)

        self.assertIn("duplicate", self.assert_fails(self.intent([
            self.document_addition("/declared"),
        ])).stderr)

    def test_duplicate_key_in_embedded_document_fails(self):
        self.repo.write(TOOL, compact({
            "jsonrpc": "2.0",
            "id": 1,
            "result": {"content": [{
                "type": "text",
                "text": '{"items":[],"items":[]}',
            }]},
        }))
        self.repo.commit(TOOL)

        self.assertIn("duplicate", self.assert_fails(self.intent([
            self.document_addition("/declared"),
        ])).stderr)

    def test_path_traversal_and_wrong_pointer_stage_fail(self):
        self.repo.write(TOOL, mcp({
            "items": [{"id": "existing"}],
            "added": True,
        }))
        self.repo.commit(TOOL)

        traversal = self.assert_fails(self.intent([{
            "file": "tests/Lex.Tests/golden/../tool-search.txt",
            "pointer": DOCUMENT_POINTER,
            "document_pointer": "/added",
        }]))
        self.assertIn("path", traversal.stderr.lower())
        wrong_stage = self.assert_fails(self.intent([{
            "file": TOOL.as_posix(),
            "pointer": "/added",
        }]))
        self.assertIn("document_pointer", wrong_stage.stderr)

    def test_intent_binds_the_separately_supplied_full_base_sha(self):
        abbreviated = self.assert_fails(base=self.repo.base[:12])
        self.assertIn("full 40-character", abbreviated.stderr)

        wrong = self.intent([], base="f" * 40)
        mismatch = self.assert_fails(wrong)
        self.assertIn("base_commit", mismatch.stderr)

    def test_dirty_tracked_golden_state_fails(self):
        self.repo.write(PAGE, "dirty")

        completed = self.assert_fails()

        self.assertIn("dirty or untracked", completed.stderr)

    def test_untracked_golden_state_fails(self):
        self.repo.write(GOLDEN / "tool-untracked.txt", mcp({"new": True}))

        completed = self.assert_fails()

        self.assertIn("dirty or untracked", completed.stderr)

    def test_ignored_untracked_golden_state_fails(self):
        self.repo.write(Path(".gitignore"), "tests/Lex.Tests/golden/tool-ignored.txt\n")
        self.repo.commit(Path(".gitignore"))
        self.repo.write(GOLDEN / "tool-ignored.txt", mcp({"new": True}))

        completed = self.assert_fails()

        self.assertIn("dirty or untracked", completed.stderr)


if __name__ == "__main__":
    unittest.main()
