# Design verdict: the Lex assistant

Prepared 2026-08-13 against origin/main. Companion to docs/design-verdict.md, which covers the
corpus and coverage layer; this document judges the AI assistant (src/Lex.Ask), the retrieval
and identity layer it stands on, the temporal model, and the pipeline stages that feed them.
Method: three very thorough read-only code explorations (assistant anatomy; identity and tool
surface; scenario capability inventory with live SQLite measurements against the artifacts in
deploy/indexes), followed by personal spot-checks of every load-bearing claim. Verified and
inferred statements are separated; every claim about code carries a file and line.

## 1. VERDICT

The architecture is the right shape in the wrong body. Plan into a frozen operation list,
execute against typed tools, render deterministically, allow model prose only on request and
only behind a validator and a judge: that is the correct design for a legal product, and the
measured failures do not indict it. What they indict, in order of credibility damage:

**1. The deployed artifacts do not carry the identity layer the assistant was built against.**
Both mounted index files are lex-index/2 and contain no work catalog at all: no work_records,
work_names, work_fts, work_discovery or work_vectors (verified by sqlite_master on both).
IndexReader.cs:210 gates on those tables, so hasWorkSearch is false, every search response
reports work_catalog_available=false, and resolution falls back to LIKE matching over titles
and identifiers (IndexReader.cs:894, 1554). The 31 reviewed EU aliases in
config/eu-work-enrichment.json are applied only when the build passes --work-enrichment
(IndexFromCorpus.cs:259) and are absent from the shipped stamps. The deployed EU index holds
10 works and EMIR is not among them. Consequence: a large share of the audited 54 percent
failure rate was plausibly measured against the fallback path, not against the identity code
reviewed here. Caveat, stated honestly: this is verified on the local deploy/indexes copies
built 2026-08-04; deploy/fetch-indexes.sh can pull newer artifacts, so the first plan item is
to verify what production actually mounts.

**2. AskService.cs fails the owner's own one-sentence test.** 3,062 lines holding nine jobs:
wiring, a 400-line nested WorkResolutionGuard class (74-484), diagnostics, prompt and schema
generation, planning with the repair loop, rendering helpers, the live pipeline, a hand-rolled
locale detector, and a dead second agent. LegacyAskAsync (2654-3057, 404 lines) has zero
callers, confirmed by grep; its exclusive dependents SystemPrompt (632-726) and OpenAiTools
(747-769) are equally dead; the cluster is roughly 557 lines. It was left behind by the commit
that introduced the current controller (38ddb9c). A second, dead agent inside the first is the
single worst interviewer-facing artifact in the codebase.

**3. The advertised determinism is overclaimed in one place and unprotected in another.**
The frozen plan is real at the operation level: validated once (OperationContracts.cs:392-440,
1..8 ops, contiguous order, tool policy, clarification-is-alone), arguments value-copied and
capped (:281, 32,768 bytes), rebuilt only through the same validator (AuthorizeInstants,
AskService.cs:1929-1991). But execution is not argument-deterministic: each work-resolving
operation issues up to five unplanned MCP searches, one conditional on a prior result
(AskService.cs:2443-2460), and executed arguments are rewritten from search results
(2532-2552). That is sound engineering wrongly described; say "frozen operation list, runtime
argument binding" and the claim becomes true. Meanwhile the one genuinely non-deterministic
orchestration, the composer-judge loop in AgentAnswerFinalizer.FinalizeAsync, has zero tests
(grep: no reference in tests/), no token budget and no timeout of its own, and the 25 second
first-result deadline is disarmed after the first operation reports (AskService.cs:1752,
2065), leaving synthesis bounded only by client cancellation.

**4. The identity model is too weak for how lawyers actually cite law.** Four verified
mechanisms compose into the audit's worst failure class:
- Matching is one-directional. WorkSearch.cs:305 tests stored-name-inside-user-query only;
  the reverse comparison exists nowhere in src/. A French statute cited by its opening clause
  ("loi du 12 novembre 2004", stored title longer) falls through to weak token hits that
  IsWorkIdentity rejects (IndexReader.cs:1149), so the guard then refuses work-scoped tools.
- EUR-Lex short titles are minted by cutting at " of the European Parliament"
  (EurLexAdapter.cs:884), so EMIR's stored name is literally "Regulation (EU) No 648/2012",
  a verbatim substring of the CRR's official title tail. Absence from the alias file (no EMIR,
  no MAR, 31 entries, 16 names, EU only, no Luxembourg file at all; verified counts) is what
  leaves the bare designation as the stored name.
- Both the named work and the amending-clause work then match as identity (contained_title
  with digits, IndexReader.cs:1153), each with one candidate, so both are "resolved" and
  nothing looks ambiguous (IndexReader.cs:1194).
- With a strong work match the article query's score is the constant "0.0"
  (IndexReader.cs:1290-1326), so ORDER BY valid_from DESC alone decides which Article 26 the
  reader gets. Recency was never a relevance signal; here it is the only one left.
The codebase itself names the incident twice (WorkSubjectRule.cs:38, AskService.cs:306). The
post-incident fixes are genuinely good: WorkSubjectRule is a closed hierarchy from which no
work can be read out of an Undecided (the exact bug class), disclosures name the runner-up,
and every reply line carries title plus lex_id. Two seams remain: the runner-up disclosure is
advisory on the synthesis path (the composer may drop it; CoverageDisclosure is enforced at
AgentContracts.cs:164-170 and force-appended, the selection disclosure is not), and the date
guard's stand-down is whole-turn: DateIntentGuard.cs:62 disables the bare-year protection
whenever any day-and-month appears anywhere in the turn, which is precisely what full EU
titles ("of 26 June 2013") and French opening-clause citations ("du 12 novembre 2004")
contain. Verified by executing the regex: the guard fires for "What did Article 92 of the CRR
require in 2024" and stands down for the same question asked with the full official title.
The two confidently-wrong classes compose: the citation forms that most confuse the resolver
also disarm the date guard.

**5. Quality gates exist at every layer except the one where the worst data lives.** Fetch
has typed issues (including the new body_empty), index build verifies every text hash, mount
verifies signatures and refuses partial catalogs. Nothing anywhere inspects extracted
provision text: DeriveWriter.cs:182-187 skips only zero-provision versions, and grep for the
empty-string sha across src, tests, docs returns nothing. Measured on the live LU index:
2,415 empty provisions across 283 versions and 126 works produce 1,603 false amendment
states and 4 false "identical text" renumberings; the LSF's 2003-10-01 version has 105 of
145 provisions empty, so article_history reports a false "text changed" event for each, and
diff between its versions compares nothing. The product's honesty is structural at the
refusal layer and incidental at the data layer.

**Secondary verdicts.**
- Temporal model: right, and mostly honored. Storage is bi-temporal (valid_from/valid_to
  plus events and obs_history), queries are mono-temporal; there is no "as we knew it on T"
  path, valid_time_source is single-valued in practice, and Lex.Temporal is a 42-line package
  with zero production call sites. Where it counts the code does the right thing: sitemap
  lastmod uses observed_from with the 23 future-dated works documented in place
  (ApiEndpoints.cs:57-71, independently recounted: 23 works, 42 versions, max 2030-09-15),
  future states carry provisional=true, and D72's timeline_semantics rides every envelope so
  EUR-Lex dates are never called entry into force. Call it bi-temporal storage with an audit
  axis, not a bi-temporal system, and stop advertising the unused package.
- The repair machinery (argument gate normalize, one bounded retry with a skip list, the
  date guard) is the right pattern, not a symptom: deterministic guards beside a rewritten
  prompt, with the skip list encoding "never let a retry invent an answer-choosing value".
  Its defects are scope (the whole-turn stand-down above) and altitude (all of it lives
  inside the god class).
- Coverage versus claim: structurally honest. A closed set of 13 machine-readable statuses
  with a throwing default on unknown status; unknown_publisher deliberately distinct from
  no_result so an unmatched filter can never read as an empty corpus; no_corpus_mounted
  self-describing; truncation declared everywhere it happens. The dishonesty that existed
  was at the label layer (expressions sold as versions) and is fixed on the pushed
  agent/corpus-writer-hardening branch.

**What is already good, so this reads as judgment rather than complaint:** the default reply
path is 0 percent model-authored and every synthesis failure falls back to a deterministic
named line plus a canonical refusal; admission, deadlines and lease release are tested; the
evidence ledger is budgeted; discovery names carry model, prompt hash, repeat-run agreement
and evidence anchors hashed against the very build (WorkSearch.Validate:486-538), which is a
better provenance discipline than most production RAG systems ship; the MCP surface is small,
bounded, and its wire shape is pinned byte-for-byte by goldens.

## 2. LAWYER SCENARIO COVERAGE (the partition)

Legend: DOES = tools and index can, assistant does. FAILS = tools and index can, assistant
does not (assistant-layer bug). GAP(layer) = tools or index cannot; fix belongs to that layer.

1. Point-in-time text, fully specified citation. DOES. as_of with anchor selection, closed
   intervals, per-provision hash and permalink (IndexReader.cs:553-579, McpCore.cs:773).
   Conditional FAILS in production: on the deployed no-catalog artifacts, identity falls to
   LIKE fallback, so resolution quality is not what the code review would predict. Fix is
   deployment (plan item 1), not assistant logic.
2. Point-in-time with a bare year. DOES now: AuthorizeInstants widens as_of to
   article_history or timeline with the year window and a disclosure sentence; in_force_on
   becomes a two-option clarification. FAILS (small, verified): when the turn contains any
   day-and-month, including inside a full official title or a French opening-clause citation,
   the guard stands down and a planner-invented date binds silently as Stated with no
   disclosure (DateIntentGuard.cs:62). Assistant-layer fix, small.
3. Comparison across two dates. DOES at version level: diff returns both sides, changed,
   provision_level_comparable, and refuses mixed extraction profiles with a reasoned
   profiles_differ (McpCore.cs:1002-1021). GAP(MCP): the provision-level text diff exists
   only as a web rendering (Fragments.RenderDiff); the tool returns no text delta, so the
   assistant cannot actually show what changed. GAP(pipeline): 1,535 LU docs have NULL
   profile and degrade to text_not_available instead of an honest comparison; and the
   empty-extraction class (below) makes some diffs vacuously empty.
4. Amendment history of an article. DOES mechanically (provision_states 114,633 rows,
   anchor_events 60,435, window filtering). GAP(pipeline, derive): empty extractions poison
   it; 1,603 false states, the LSF case in full. The assistant is faithfully reporting
   corrupted history; no assistant fix applies.
5. In-force on a date. DOES, with a mandatory population disclosure and the hardcoded
   known-exclusions fallback naming the ~24,579 never-consolidated LU acts. Bare years are
   refused into a clarification rather than widened. This scenario is handled the way the
   whole product aspires to behave.
6. Citation and cross-reference following. GAP(pipeline and index). What exists: LU reverse
   citations only, 128,465 edges harvested exclusively from publisher href markup and
   resolved by URL string transform (IndexBuilder.SlugOfEli); EU citations are zero rows
   (the fmx4 profile threads a citation list that produces none); prose references with no
   href are dropped; corpus relations (amended_by, consolidates) are never indexed (no
   relations table in either db); cross-publisher resolution promised by spec D35 does not
   exist. The assistant reaches everything that exists (cited_by is planner-invokable); the
   missing capability is data.
7. Coverage and absence questions. DOES. The coverage tool exists to state absence, the
   three absence shapes are distinct and quoted, and the answer prompt forces the
   distinction between "does not have the law" and "has the law but not its text"
   (AskService.cs:698-704). Minor: shipped stamps lack timeline_semantics and
   known_exclusions keys, so fallbacks fire.
8. Multilingual questions. DOES for what exists, and what exists is nearly nothing: LU is
   French plus exactly one work each in lb, en, de (2 multilingual works); the deployed EU
   index is English only. GAP(artifact): on v2 files provision_states and anchor_events have
   no language column, so article_history ignores the language argument entirely. The
   honest coverage answer ("picking a language removes a publisher") is already served.
9. Questions mixing jurisdictions. DOES for enumerable fan-out (one changes_in_period across
   publishers; up to 8 operations per plan). GAP(index and MCP): no join exists, LU and EU
   slug spaces are disjoint so cross-publisher cited_by is structurally empty, and nothing
   correlates a national implementation with an EU act. Honest today; a real capability gap
   if the product ever claims transposition tracking.
10. Requests that are actually legal advice. DOES refuse, four layers deep: legal_boundary
    as a planned disposition, the execution-time bilingual refusal, the answer policy line,
    and prompt rule 3, reinforced in the composer instructions. Note for the record: the raw
    MCP endpoint carries no advice guard, which is correct, since its tools can only return
    text and typed refusals.

**Where the partition says to invest, in order:** the artifact and deploy layer first (the
identity code that exists is dark in production); the derive gate second (it poisons three
scenarios at once); the identity matching model third (bidirectional containment, LU aliases,
amending-clause demotion); the assistant fourth (two small verified holes, one deletion, one
decomposition); the finalizer's bounds and tests fifth. Of the audit's 13 failures, the
mechanisms found place most of the weight below the assistant, in identity data and
extraction quality, with the assistant's own two holes being small and precisely located.

## 3. PLAN (ordered by value; sizes are rough)

0. Verify what production mounts (ops, XS). Read-only: fetch the live /attestation.json and
   coverage tool output; compare stamp schema and work_search keys against the local
   2026-08-04 artifacts. Everything below assumes the answer is "v2, no catalog".
1. Ship the work catalog (artifact and deploy, M). Build v3 indexes with --work-enrichment,
   verify search reports work_catalog_available=true and reviewed aliases resolve, deploy via
   the normal workflow. Highest single lever on the audit score; without it items 3 and 4
   are partially moot in production.
2. Extraction quality gate (Lex.Derive, S-M). A provision whose trimmed text_md is empty
   becomes a typed issue (empty_provision_text), mirroring the just-added body_empty at
   fetch; a version above a threshold of empty provisions is refused or flagged; surfaced
   through the existing build_issues channel into coverage. Then re-derive LU and rebuild.
   Kills the false-amendment-history class (1,603 states) and un-poisons diff for the LSF.
3. Identity matching upgrades (Lex.Index WorkSearch + config, M).
   a. Bidirectional containment: also test the user's citation inside stored titles, guarded
      (minimum length, act-form-plus-date shape, NormalizeCitation residual) to avoid false
      positives. This is the fix for the French opening-clause citation form.
   b. Luxembourg alias file (config/lu-work-enrichment.json) seeded with the statutes
      practitioners cite by name (LSF, LIR, the codes); owner-reviewed per the D75 pattern.
   c. Add EMIR and MAR to the EU set; warn at build when an aliased work is not held (5 of
      15 currently are not).
   d. Demote amending-clause matches at search time: a contained_title hit whose matched
      span sits inside an amending or repealing clause of the query is not identity
      (WorkSubjectRule's trailing-clause logic, applied one layer down where the conflation
      is created).
4. Assistant surgical fixes (Lex.Ask, S).
   a. Scope the DayAndMonth stand-down to the residual turn after work-mention spans are
      removed (BuildQueryPlan already computes the residual; WorkSubjectRule already holds
      mention spans).
   b. Enforce the runner-up disclosure on the synthesis path exactly as CoverageDisclosure
      is enforced: validator check plus force-append in the finalizer.
   c. Delete LegacyAskAsync, SystemPrompt, OpenAiTools, MaxToolRounds and their orphaned
      tests (about 557 lines). Marked for outright deletion.
   d. Close the navigate phantom: it is accepted by the argument gate and policy but absent
      from PlannerToolNames; either advertise it or, preferred, remove it from the gate so
      unplanned tool names fail closed.
5. Decompose AskService along the seams the anatomy exposed (Lex.Ask, M, behavior-neutral,
   golden-covered): WorkResolutionGuard to its own file; planner client (prompt, schema,
   HTTP, repair loop) to a PlannerClient; locale detection out of the service; the
   controller keeps admission, AuthorizeInstants, ExecutePlan and reply assembly. Target is
   a service whose responsibility is again one sentence.
6. Bound and test the finalizer (Lex.Ask, S-M). Give composer and judge calls a token budget
   and a deadline (or stop fully disarming the first-result deadline and give synthesis its
   own); add the missing FinalizeAsync tests (compose-retry, judge Pass/Repair/Refuse
   mapping) through the existing agent seam.
7. EU citations and relations (pipeline, index, MCP, M-L). Make the fmx4 profile emit the
   citation edges it already models; index corpus relations (consolidates, amended_by) into
   an edges table with validity time; extend cited_by or add a relations tool. This is the
   foundation for real amendment tracking, which today exists only as corpus JSON nothing
   reads.
8. Provision-level diff through MCP (MCP and web, M). Move the web's RenderDiff computation
   into a shared component and give the diff tool an optional text mode, so the assistant
   can answer "what changed" with wording rather than metadata.
9. Observability (Lex.Ask and web, S). Route the 17 live Diagnostic codes through a leveled
   logger correlated with X-Lex-Request-Id; today they are sanitized stderr lines with no
   request correlation, and telemetry spans carry a closed tag set that cannot join them.
10. Housekeeping (XS each): fold or delete Lex.Temporal (42 lines, zero production callers,
    still advertised by the spec as a publishable package); merge the pushed
    agent/corpus-writer-hardening branch so the version-count honesty fixes reach main.

**What I would not do, deliberately.**
- No structural normalisation across extraction profiles. profiles_differ is the product
  telling the truth about its own inputs; an anchor-mapping layer would trade an honest
  refusal for a confidence claim the data cannot support.
- No bi-temporal query surface yet. The audit axis is stored and provable via provenance;
  "as we knew it on T" has no user until a litigation use case names one.
- No entity extraction. It would be the first non-publisher-asserted content in the corpus
  and collides with the never-generate rule; if ever added, it must go through the D75
  quarantine that discovery names already use.
- No replanning loop in the assistant. The failures measured were identity and data
  failures, not plan-shape failures; the frozen list plus clarification is the right
  liability posture for a legal product.
- No framework rewrite. The pipeline's bones (typed operations, closed statuses, evidence
  ledger, deterministic fallback) are better than what a generic agent framework would
  replace them with.

## Verification

- Items 2-8: dotnet test plus golden diff review per repo rules; for item 2, the LSF false
  positive count in article_history drops from 105 to 0 and coverage gains typed issues; for
  item 3, WorkSearchTests gains the opening-clause and amending-tail cases (both currently
  absent); for item 4a, the DateIntentGuardTests truth table gains the full-title case that
  currently stands the guard down.
- Item 1: after deploy, the live search envelope reports work_catalog_available=true and the
  audit's CRR-by-full-title question resolves to the CRR with the runner-up disclosure.
- No production code accompanies this document; each item lands separately through the
  normal test and golden discipline.
