# Design review verdict

Date: 2026-08-12. Review target: branch `agent/report-corpus-text-gaps` (commit 4c14d43 plus
uncommitted About-page edits). Method: 20 finder agents in two waves, deduplication, 12
adversarial verifiers, plus direct git and read-only SQLite checks against `deploy/indexes/*.db`
and the public corpus manifests. Every claim below survived an adversarial verification pass or
was measured directly; refuted claims are listed at the end so they are not re-litigated.

## Verdict 1: the reviewed branch was a stale replay

- The branch base (7dacc3a) was 125 commits behind local main and 220 behind origin/main.
- `git diff 3202ff1 4c14d43` is empty: the committed half was byte-identical to "Report corpus
  text coverage (#43)", already merged and running the nightly ingest since 2026-08-07.
- The uncommitted About edits were an older draft of "Align public profile (#152)", already
  merged and deployed. Shipping them would have dropped `canonicalPath: "/about"` (reverting
  #51), reintroduced the /built bio contradiction #152 fixed, replaced newer meta copy, and,
  through a hand-edited stale golden, re-asserted pre-#55 font behaviour.

Disposition, executed 2026-08-12: working-tree edits discarded, branch abandoned unmerged
(`git cherry origin/main` showed its one commit as an equivalent upstream patch), the stray
`metrics-debug.json` moved out of the repo (no secrets, but it embedded subscription and
resource ids and was not gitignored), local main fast-forwarded.

The useful part: because the committed content is on main, everything the review proved about
it is a finding about production.

## Verdict 2: verified defects on main, ranked

1. **The product mislabels expressions as versions, site wide.** The `docs` table holds one row
   per language expression (spec, and `IndexFromCorpus.cs` fans out per expression), but
   `CoverageInfo.Rows` (`COUNT(*) FROM docs WHERE withdrawn=0`, IndexReader.cs) is rendered as
   "dated versions" on the home tiles (HomeEndpoints.cs:128), /about (ExplainerEndpoints.cs:672
   and 697), /built (:626), /how-it-works (:1097), the /architecture table (:65), /browse cards
   and per-work rows (CatalogueEndpoints.cs:238, 169-198), the /coverage summary and per-type
   table (:336-343, 406-408), work pages (DocumentEndpoints.cs:87, 163), and the MCP coverage
   tool (`versions`, `versions_with_text_served`, `versions_without_text`, McpCore.cs:1536,
   1556-1558). Live scale: EU holds 2,364 versions but 4,728 expressions (two languages each),
   LU 4,642 versions and 4,646 keys over 4,649 rows. The site therefore claims roughly 9,374
   "dated versions" over roughly 7,006 actual versions, a 34 percent overstatement of the
   headline number on a product whose pitch is that every number can be checked. The signed
   stamp already carries the true count (`COUNT(DISTINCT key)`, IndexFromCorpus.cs:187-188) and
   publishes it at /attestation.json, so the site currently disagrees with its own attestation.
2. **Positional version identity can relabel law text** (CorpusWriter.cs:93-96). Version
   directories are keyed by valid_from plus an arrival-order suffix; the publisher's stable
   version id is never persisted or reconciled. On a same-date tie re-key, the existing-record
   branch rewrites ValidTo, DocumentType and InForceStatus while the language-keyed merge and
   the observation skip keep the previous occupant's title, source URI and body bytes: one
   consolidation's text under another's validity interval. Every hash still self-matches, so
   `verify corpus` passes and nothing can detect it afterwards. Thirteen same-date tie pairs
   already exist in the LU corpus, including the Code civil. EUR-Lex is protected by its
   celex-based ordering; the exposure is Legilux only. Highest severity item; pre-existing.
3. **The with_text metric measures none of its target gap classes** (CorpusWriter.cs:365-367).
   Measured against the live index: the corpus manifest says 3,154 expressions with text while
   the served coverage number is 3,114; 23 LU versions whose extraction produced zero
   provisions count as "with text" in the manifest but are correctly excluded by text_public;
   2,415 provisions across 283 versions hash to the empty string (the LSF holds 105 of 145)
   and are caught by no metric at all. The manifest number is strictly weaker than the number
   already published and contradicts the deliberate design note in CatalogueEndpoints.cs that
   coverage "is computed from the index rather than written down, so it cannot drift".
4. **Empty fetched bodies are stored and counted as covered, permanently.** ValidateBodyFetch
   (CorpusWriter.cs:483-490) accepts a Retrieved outcome whose text is empty; the writer then
   stores a zero-byte body, records the sha256 of empty input, flips Text.Available, and never
   refetches because any existing observation short-circuits the fetch (:270).
5. **One alt manifestation permanently blocks primary-body backfill.** The primary skip counts
   all observations (:270) while the alt skip is format-aware (:303), so a transient primary
   404 on a night when the alt fetch succeeds freezes the expression on the weaker extraction
   profile forever, contradicting the adjacent backfill comment (:262). EUR-Lex 404s of this
   kind are documented as real (#41).
6. **The three new manifest count fields are checkable by nothing.** CorpusIntegrity.Verify
   compares only Works and Versions (CorpusIntegrity.cs:140-143); its own census counts
   withdrawn versions while the writer counts only the current plan, so the live LU corpus
   verifies clean while `verify corpus` prints 4,660 expressions next to a manifest saying
   4,646. Both corpus READMEs advertise manifest.json as the source of truth for counts.
7. **The manifest mixes two populations.** The counters read the add-only merged on-disk
   expression list while works, versions, languages and the progress denominator read the
   current plan; a dropped language is never pruned, can never be fetched again, and keeps a
   permanent without_text floor while the manifest counts a language its own languages field
   no longer lists.
8. **Contract versioning.** Three non-required, non-nullable ints under an unchanged
   `lex-corpus/3` schema id: absence deserializes to 0 and 0 genuinely occurs (EU
   without_text is 0), so absent and measured are indistinguishable. Spec section C2 still
   documents the pre-#43 manifest shape. Main's newer ScopeExpectedWorks is already `int?`,
   which is the pattern these fields should follow.
9. **The spec's corrigendum shape is unrepresentable.** Spec 3.3 rule 3 requires a second
   same-language expression inside one version directory; the writer's language-only merge
   silently drops it, `Single()` would throw on it, and CorpusIntegrity flags the conforming
   shape as an error. No adapter can emit the shape today, so this is latent, but spec and
   code contradict each other on the system's core coordinate.
10. **The counter test cannot fail in the direction that matters.** The only assertions on the
    new fields run against a metadata-only fixture, so ExpressionsWithText is structurally
    zero and deleting the counter keeps the suite green, exactly the empty-baseline failure
    the repo's CLAUDE.md warns about. A body-producing fixture already exists.
11. **Minor.** The `[corpus]` summary and rejection lines bypass the injected `_progress`
    writer (CorpusWriter.cs:441-453 vs :19), which is why the writer test reads manifest.json
    instead of asserting the log; the without_text subtraction is computed twice; roughly ten
    sites format counts with culture-sensitive `n0` and nothing pins culture (the deployed
    image is only accidentally invariant, and the sub-1,000 fixture counts make goldens blind
    to it).

## What is sound

- The writer's failure ordering: the manifest write is last, a crashed run leaves the previous
  corpus, the Unchanged fast path is byte-stable, and the newer candidate and typed
  acquisition-issue mechanism (requireComplete) is exactly the right gate.
- EUR-Lex version identity (celex grouping, ordinal tie-break, strict original-date guard).
- The coverage philosophy in CatalogueEndpoints ("computed, not written down") and the signed
  stamp recomputing counts from rows; the defects above are departures from these principles,
  not flaws in them.
- The golden discipline itself: the reviewed diff's golden regeneration was tool-generated,
  complete and minimal, and nothing in the repo parses the changed stderr format.

## Remediation plan

Done (2026-08-12): branch disposition as above.

In progress on `agent/corpus-writer-hardening` (this order):

1. **True version counts.** Extend the Coverage() aggregate with `COUNT(DISTINCT key)` and a
   distinct-key with-text variant (measured cost ~5 ms on the 902 MB LU db; `key` is the
   version-level lex_id in both `lex-index/2` and `lex-index/3`, so one query serves both).
   Append `Versions` and `VersionsWithText` to CoverageInfo; switch every "version"-labelled
   surface to them; make per-kind and per-work counts distinct-key based; fix the /changed
   totals undercount (its `group_key || valid_from` key collapses the 13 same-date pairs);
   dedupe the work-page badge counts. MCP keeps its field names and gets correct values, with
   expression counts added under explicitly named keys; the assistant's UiMapper reads the
   same names and needs no change. Fixture goldens are single-language, so only labels move.
2. **Empty-body guard**: a Retrieved outcome with empty or whitespace text becomes a typed
   acquisition issue (feeding the existing buildIssues gate) instead of a stored zero-byte
   body.
3. **Format-aware primary skip**: skip the primary fetch only when a primary (Format == null)
   observation exists, restoring the promised backfill without disturbing alt handling.
4. **A real with_text test**: assert a non-zero ExpressionsWithText through the existing
   body-producing fixture, plus the empty-body and backfill cases above.
5. **Log seam**: route `[corpus]` lines through `_progress` and reuse the stored
   ExpressionsWithoutText.

Next, larger, in order of value:

6. **Version identity** (finding 2): persist the publisher's stable version id in VersionMeta,
   reconcile directory identity on it for same-date ties, and add an integrity check that
   identity fields never flip over a stable body. Wrinkle to accept: the 13 existing tie pairs
   cannot be retro-identified from disk alone; they need one publisher re-query to stamp ids.
7. **Integrity closure** (finding 6): define the counted population (current, non-withdrawn),
   recompute all three manifest fields in Verify, and state tombstone semantics.
8. **Metric semantics** (finding 3): decide whether manifest coverage derives from the
   text_public basis, gains an empty-provision statistic (the one number that would surface
   the LSF class), or is dropped in favour of the index-computed numbers.

Owner decisions still open: finding 3's direction (above), the `lex-corpus` schema posture for
manifest field additions (finding 8), and whether the corrigendum model is implemented or the
spec amended (finding 9).

## Refuted during verification (do not act)

- The LinkedIn handle flip: it matched the already merged #152; only a stale sibling worktree
  still disagrees.
- int accumulator overflow in the writer: five orders of magnitude from reachable; the long
  neighbours exist for the percent product, not magnitude.
- Culture formatting as an active production bug: the deployed image sets no locale and runs
  invariant; latent hazard only.
- The About meta description "regression": the bio was never in the description; pre-existing
  pattern, optional improvement.
- The /built versus /about bio contradiction: an artifact of the stale working tree; upstream
  fixed both pages together.

## Verification protocol

- `dotnet test tests/Lex.Tests/Lex.Tests.csproj`; regenerate goldens only for intended label
  changes and read the diff before committing (the fixture is single-language, so any golden
  number change is unexpected by construction).
- After deploy (owner-driven), fetch /about, /coverage and the MCP coverage tool and reconcile
  their version and expression counts against /attestation.json and the two corpus manifests.
  The numbers must agree with each other and with the stamp, which is the check the product's
  own premise demands.
