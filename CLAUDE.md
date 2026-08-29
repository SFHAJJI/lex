# Working in this repo

Point-in-time retrieval of Luxembourg law and a reviewed, configuration-led EU corpus. The product claim is that an answer
can be *checked* rather than trusted, so the rules below are not style preferences: most of them
exist because breaking them produced a confidently wrong answer at some point.

## Hard rules

**Scoped `git add` only. Never `git add -A`.** A cloud session edits docs in this repo in
parallel; a blanket add sweeps its work into your commit.

**Never read the law data files.** `works/`, `*.xml`, `*.html`, and law `*.json` in
`lex-articles` or the corpus repos. They are large and reading them tells you nothing the index
cannot. Query `deploy/indexes/*.db` with SQLite instead.

**No em dashes or en dashes in published text.** Site copy, README, release notes, PR bodies,
commit messages. Hyphens in compounds are fine. This does not apply to ingested law or to code
comments.

**Never write the Azure OpenAI key to disk.** Fetch inline, env var only:
`az cognitiveservices account keys list -n oai-soufien-dev -g rg-soufien-portfolio --query key1 -o tsv`

**Never commit `C:\lex-ops\signing-key.pem`.**

**Only official publisher endpoints.** Respect `robots.txt`. User-Agent
`Lex/0.1 (+https://github.com/SFHAJJI/lex)`.

## The golden tests are the safety net

Snapshots cover every rendered page and every MCP tool response, with targeted contract and
architecture assertions beside them. Do not hard-code the counts here; the test runner is the
source of truth as the suite grows.

```bash
dotnet test tests/Lex.Tests/Lex.Tests.csproj          # verify
LEX_GOLDEN_UPDATE=1 dotnet test ...                   # accept an INTENDED change
git diff --numstat tests/Lex.Tests/golden/            # then READ the diff before committing
```

The review of that diff *is* the safety mechanism. A change that touches snapshots you did not
expect is a regression you have not noticed yet.

Golden changes pass through the base-controlled `trusted-golden-diff` workflow on
`pull_request_target`, limited to `main`. Before checkout it requires the event base ref to be
`main` and the event base repository to equal `github.repository`. It checks out the exact event
base, installs the locked trusted parser dependency with lifecycle scripts disabled, validates the
event SHAs and numeric pull request number, fetches the pull request merge ref into a detached
temporary worktree, and executes only the base copies of the classifier and parser against that
candidate. Candidate code is never executed. Candidate HTML reaches the parser only as bounded
stdin bytes. Ordinary pull request CI still tests tooling, but it is not the snapshot-acceptance
gate.

The trusted job reads the event and intent files with fixed allocation ceilings, then reads exactly
one bounded `json` fence from the pull request body as data. It never evaluates that text. The
external `lex-golden-diff-intent/1` document must bind `base_commit` to the separately supplied full
`--base` SHA and contain exactly one of `additions` or `html_selectors`. A change with no golden
files needs no intent fence.

After this bootstrap change merges, repository administration must require the base workflow in a
repository ruleset when GitHub accepts the `workflows` rule. A required branch-protection status
context named `trusted-golden-diff` is only the fallback. A status context plus the GitHub Actions
app identity does not identify one workflow, and duplicate job names across workflows are
ambiguous. The classifier rejects a statically recoverable trusted check name in any other
candidate workflow, including YAML Unicode escapes and line continuations, but a dynamic expression
can construct the same name without that marker. The fallback is not a security boundary against a
compromised repository writer. Once the canonical workflow exists in the trusted base, the
classifier also requires its mode and bytes to remain exactly identical in the candidate. Changing
or removing that workflow needs the separately governed repository-administration path.

For JSON tool snapshots, the classifier accepts only exact RFC 6901 additions. Tool responses use
the fixed outer `pointer` `/result/content/0/text` plus a `document_pointer` into its JSON string.
`tools-list.txt` uses a direct `pointer`. The declarations must match the changed files and pointers
exactly. Base and head JSON must use the family-specific canonical snapshot layout and preserve
every existing value's type, lexical token and key order. MCP snapshots also require the exact
canonical raw outer string encoding at `/result/content/0/text`. Documents must stay within 128
levels and 100,000 parsed nodes, and files must remain under 8 MiB:

```json
{
  "schema": "lex-golden-diff-intent/1",
  "base_commit": "0123456789abcdef0123456789abcdef01234567",
  "additions": [
    {
      "file": "tests/Lex.Tests/golden/tool-search.txt",
      "pointer": "/result/content/0/text",
      "document_pointer": "/new_field"
    },
    {
      "file": "tests/Lex.Tests/golden/tools-list.txt",
      "pointer": "/result/tools/17"
    }
  ]
}
```

For page-only changes, use a nonempty `html_selectors` array with exactly one narrow `#id` or
`[data-testid="value"]` selector for each changed page snapshot. The classifier locates that exact
element subtree and requires every byte outside it to remain unchanged. Human diff review remains
defense in depth after machine scope verification. The classifier reads
both revisions as strict UTF-8 HTML with a conservative 8 MiB per-file ceiling. A base-controlled
Node helper uses exact `parse5` 8.0.1 HTML5 tree construction with source locations. It rejects
every parse error except the pages' existing missing-doctype condition, requires an explicit target
boundary, and verifies that source-backed DOM descendants and non-descendants agree with that
boundary before returning byte offsets. The Python classifier then compares raw Git blob bytes
outside those offsets. The selector must occur exactly once in the head and at most once in the
base, so a newly added element is valid. ID matching follows HTML quirks-mode ASCII case folding;
standards-mode IDs and `data-testid` values remain case-sensitive. The file bytes must change and
its mode must not. Because parse5 does not attach declarative shadow roots, any HTML `template`
carrying `shadowrootmode` fails closed. Its legacy `select` tree construction also differs from
current customizable-select parsing, so every HTML `select` needs an explicit boundary and a second
tokenizer pass rejects child tags outside the narrow syntax both parsers represent consistently.
Never mix page and JSON tool golden families in one change.

Pass an external file directly with `--intent`, or let CI use `--event` to read the pull request
body. Replacements, removals, formatting-only changes, renames, file additions or deletions, stale
declarations, undeclared additions, broad selectors and duplicate JSON keys fail.

**A passing test is not automatically a real test.** The first version of the tool snapshots
wrote 1 byte each, because the hand-built JSON-RPC had an extra `}`. An empty baseline passes
forever. When you add a snapshot, assert it is non-trivial. When you add an assertion, break the
code on purpose and watch it fail before you trust it.

**The fixture is thin.** Its works are single-language, have no trailing full stop in their
titles and no multi-language expressions. Several real defects are therefore unreachable from
the fixture and must be verified against the live site instead.

## Deploying, and why exit codes are not proof

```bash
gh workflow run deploy.yml -f promote=false
gh run watch --repo SFHAJJI/lex
```

The workflow logs into Azure through GitHub OIDC, builds an immutable image, creates a candidate
revision at zero traffic, and verifies artifact manifests, health and MCP behavior against that
revision. It never promotes. Signed assistant evaluation and the separate `revision-traffic`
workflow own promotion, and the former production revision remains available for rollback.

**Then fetch the served output and check it.** A workflow success proves its smoke tests, not
every user path. Check `az containerapp revision list` for `runningState`, then request the live
route and MCP behavior you changed.

## Things that have already gone wrong here

- **Escaping twice.** `PageShell.Page` escapes `title` itself. Callers must pass **plain text**.
  Passing `H(...)` produced `Gro&#223;herzogtums` in the tab and in Google results.
- **`lang` is about the page, not the subject.** A work page is English chrome about a French
  law, so it is `lang="en"`. A version page is 38,000 words of French law, so it is `lang="fr"`.
- **`lastmod` is not `valid_from`.** `valid_from` is when a law takes effect and is legitimately
  years in the future for 23 works. Use `observed_from`.
- **Never diff across extraction profiles.** `pdf-lu/1` and `akn-lu/1` mint different anchor
  schemes, so pairing them reports parser disagreement as legislation. `diff` returns
  `profiles_differ` and refuses. Only when *both* profiles are known and differ.
- **C# raw strings and JSON.** JSON ending in `}}` breaks `$"""`. Build JSON as `JsonObject`
  and call `ToJsonString()`. Never hand-quote it.
- **Git Bash paths.** `mktemp -d` returns `/tmp/...`, which native Windows Python cannot open.
  Pipe through stdin, or use `C:/...` paths.

## Architecture, briefly

`work → dated consolidation → language expression → format manifestation → file` (JOLux/FRBR).

- `Lex.Index` reads both `lex-index/2` and `lex-index/3`. New builds use version 3: SQLite,
  content-addressed exact text, contentless FTS5 and local semantic vectors. It knows nothing
  about law (F1).
- `Lex.Mcp` is the tool logic, shared by the stdio server and the HTTP endpoint (D27, one MCP
  binary). It never summarises or advises (F10); it returns publisher text or a machine-readable
  refusal.
- `Lex.Web` keeps routing and composition in `Program.cs`. At protected main `820bfb3`, that file
  is 149 lines, not 71; pages live in `*Endpoints.cs` (F14/F15 enforce the split). Re-measure
  before repeating the line count after that commit.
- Packaged index size is release-specific, not a repository constant. The signed
  `lex-artifacts/1` manifest is authoritative for every artifact's exact byte length and SHA-256.
  Deployment fetches these gitignored artifacts from exact signed release tags and fails when a
  required tag or artifact is invalid. A source build deliberately started without fetched
  indexes mounts zero indexes and must answer `no_corpus_mounted`, never `[]`.

## Before a local pipeline run

`git pull` `lex-corpus-lu-legilux`, `lex-corpus-eu-eurlex` and `lex-articles` first.
