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
- `Lex.Web` is routing only. `Program.cs` stays near 71 lines; pages live in `*Endpoints.cs`
  (F14/F15 enforce the split).
- The 947 MB index is gitignored and baked into the image. A repo-built container therefore
  mounts zero indexes and must answer `no_corpus_mounted`, never `[]`.

## Before a local pipeline run

`git pull` `lex-corpus-lu-legilux`, `lex-corpus-eu-eurlex` and `lex-articles` first.
