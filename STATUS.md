# Lex V3 status

Updated 2026-09-30 by the driver. This file replaces the issue-comment ledgers. It is rewritten in
every pull request that changes what is served, what is next or what is blocked.

## French EU expression bodies (Codex, 2026-09-30)

The acquisition adapter now projects French body candidates from this run's proven expression
population. Each candidate keeps its exact expression identity and language evidence; the FRA
route is selected explicitly. Original Work metadata remains in the corpus, including French-only
Works with an observed absence of an English expression. A missing French response cannot reuse
an English receipt. Annex binding checks expression lineage even when two held bodies have the
same bytes. Formex main-body admission also requires the package language to match the expression.
Its interpretation profile advances to version 3 so that this acceptance rule has its own digest;
new corpus/index derivations carry that profile.

Offline cases cover bilingual and French-only acquisition, missing French bodies, shared-receipt
annex binding, and repeatable French corpus/index construction. The French positive Formex fixture
is synthetic; the retained English GDPR package supplies the language-mismatch negative case.
These changes prepare live French acquisition. The completed bounded mount still contains the
previously acquired bodies; no new live French population or serving claim is made here.

Validation: Release solution build had zero warnings/errors; full ingest passed 1,984 with
19 opt-in skips and zero failures (12m39.548s). After the profile-version update, fast tests passed
3,066 with one Windows skip (48.870s), and affected ingest passed 194 with three opt-in skips
(31.873s). Exact-head CI also passed on 12d58871.

Reversible sequencing decision: merge this acquisition prerequisite before the expensive fresh
all-seed EU build, so one union run can retain English and French bodies. This avoids repeating a
whole acquisition merely to add the second served language. Population completion remains the
next live objective. The full EU run is prepared with a 20,000-wire planning cap, separately from
the full Luxembourg run; storage and frozen-source checks still precede launch.
Before a French-bearing mount is served, EU resolve must apply the Decision 89 language rule:
the current CELEX/Work resolver treats two held language expressions as `ambiguous_identifier`.
The EU parity slice must close that prerequisite; expression-specific resolution remains available.

The cross-family review at 12d58871 reported MERGE with two should-fix findings. The repair counts
French expression objects in the population total and tests closure for bilingual and French-only
runs, records the resolve prerequisite above, and adds an absent-outcome negative case. That case reaches the earlier complete-population
guard; the per-member binding guard is unchanged.
Repair validation passed 3,066 fast tests with one Windows skip (58.382s) and 204 affected ingest
tests with three opt-in skips (80.074s). The PR records final exact-head CI.

## EU Formex language scope (Codex, 2026-09-30)

Reversible driver decision under standing order section 5: enumerate Formex manifestations for
English and French expressions only. Every other observed expression remains in the reconciled
population as `not_enumerated_language_out_of_scope`, with eligibility explicitly unknown and no
manifestation or package request. A digest-checked audit of historical run9 responses found
15,706 unique expression language assertions: 1,314 EN/FRA and 14,392 other languages, with no
conflicting language assignments. At the prior six-request enumeration estimate this is 7,884
rather than 94,236 requests, before packages and the rest of acquisition. These historical counts
are sizing evidence only; run9 used per-seed runs and a test-double scope resolver. A fresh union
run must still establish the current population. Audit: `C:\lex-v3\lanes\eu-historical-language-sizing.json`.

The guarded eligibility door requires proven EN/FRA enumerations and records the other expressions
separately. Closure still requires one outcome per expression, rejects missing/duplicate outcomes,
and cannot label an unenumerated expression ineligible. The older all-language door remains strict.
Main-body processing preserves the typed source outcome and maps it to package-not-acquired.
French body acquisition is implemented above; a fresh live run must still retain the bodies.

## Luxembourg COUNT follow-up (Codex, 2026-09-30)

The first whole-population preflight ended at 12:15 UTC with exit 3, using 53/100 wire
requests. Vocabulary was delivered; the S/A/G root COUNTs returned HTTP 500 timeout or
Virtuoso SR319 row-width errors. Custody remains in
`C:\lex-v3\lu-population-preflight-20260930-1`; no population completeness is claimed.
Compact COUNT projections retain the distinct RDF tuples and identical traversal/range filters,
while omitting redundant projected strings. A six-request diagnostic observed S=1,986,924 and
G=221,852; A still returned a retained timeout. These are observations for sizing.

Reversible driver decision: split only explicit retained initial COUNT capacity errors (HTTP 500
with the observed timeout JSON or Virtuoso SR319 prefix), alongside saturated counts. Every child
still needs both enumeration passes and a tiled cover proof. Other statuses, malformed errors and
challenges stop the cover. The shared wire ceiling includes failed parent attempts. An optional
smaller leaf target supports bounded live verification; the publisher threshold remains 1,000,000.
Single-partition callers also report these initial COUNT capacity errors as PartitionRequired;
they still refuse unless a supplied or automatic cover proves every leaf. Compact counts do not
remove the page projection: a page containing an over-wide row can still fail with SR319. The
fresh preflight will retain and inspect page failures before sizing a complete run.
The bounded a439 S cover completed at 13:00:09 UTC: 12 proven leaves (including empty ranges),
10 delivered rows and 10 rows reopened from custody, using 68/150 wire requests in 30 minutes.
Its five-row target exercised automatic splitting and the existing leaf-tiling proof; it proves
only that bounded S range. Evidence: `C:\lex-v3\lu-cover-probe-20260930-1\summary.json`,
`routes-final.json`, `counts-final.json`, and `runtime-audit.json`. The lexical strategy's empty
leaves cost requests too; full Luxembourg sizing must include this measured overhead. The fresh
whole-population preflight follows merge; no production credentials are used.

Validation: solution build passed with zero warnings/errors; fast lane 3,066 passed and one
Windows symlink skip. Affected ingest/census and request-policy tests passed: 840 passed, 12
opt-in live skips. Initial CI found only the two expected identity/surface pin changes; both were
regenerated or transcribed from the observed candidate and corrected. Review and final exact-head
CI evidence are recorded in the pull request before merge.

## Heads

- `v3/integration`: `42890511` (2026-09-30, PR #808 merged). Build 45 s. Fast lane
  (`eng/test-fast.ps1`): 3,067 tests, 3,066 pass, 1 skipped (PR #798's validation). Ingest suite: green on CI for PR #760
  (the CI `dotnet` job runs the whole solution on every pull request, about 7 min on the runner;
  green for PR #808);
  locally about 15 min. 950 web tests pass. CI's web job can flake in `keyboard-walk.test.mjs`
  ("browser debugger never answered"); rerunning the failed job is the fix.
- Driver: Claude Opus 5.5 since 2026-09-29 (the Fable 5.1 driver ran out of tokens on 2026-09-28
  after PR #757; the user default model is now `claude-opus-5-5`).
- Plan: `C:\lex-v3\V3-FINISH-PLAN-2026-09-27.md` (owner's copy). Decision 94 (one driver, one queue,
  one review per pull request) merged in lex-governance on 2026-09-27. Launch target 2026-11-07.

## Served today

REST at `/api/v3/`, from a mounted `v3-corpus` directory, **Luxembourg only**: `resolve`, `as_of`,
`timeline`, `article_history`, `diff`, `changes_in_period`, `in_force_on`, `search` (strict and relaxed
lanes), `coverage`, `provenance`, `dossier`, `citation`, `cited_by`, `verify` (PR #753: a
hash-pinned permalink verified against the state digest the index holds; a work identifier or a
stable coordinate answers the current digests), `relations` (PR #753: the one edge table in both
directions, `cites` edges only, what `citation` and `cited_by` serve as one ordered, paged list),
`evidence_bundle` (PR #755), `classification` and `manifestation` (PR #756), `status_on` and
`browse` (PR #757), `ask` as the contained assistant (PR #759), and `events` and `answer_drift`
over a genesis event log (PR #760). Without a mounted corpus every route answers
`no_corpus_mounted`. EU serves `resolve`, `search` in one EU work named by identifier (PR #761), and
`dossier` for an EU work (PR #762).

EU `dossier` (PR #762) answers from the EU index for an EU work named by any of its identifiers
(CELEX, work or expression IRI, provision, article identity): the work and its CELEX, every
expression the index holds with its language, the Formex act date of its one held wording
(`wording_dates`, with the same date semantics sentence as search), its article count (99 for the
GDPR) and the corpus members its articles were read from (object ref, outcome, content class), each
expression's identifier for `resolve`, `consolidations_held: false`, and fixed words for what is not
held (titles, later wordings, force dates, document type, corrigenda, other languages). Corrigendum
lines are in the EU index but are not joined to the work: the join between a corrected work root and
the work's publisher identifier is not established by any fixture, and the answer says exactly that
rather than calling them absent. Refusals with EU context: a language the work has no expression in,
an identifier naming more than one EU work, an unknown EU identifier, no EU index mounted (was a
mode refusal naming `r6_dossier`), and an identifier both indexes hold (`ambiguous_identifier`).
The routing is one helper, `LocateEuropeWork`, which `search` and `dossier` share. The Luxembourg
dossier's `first_observed` sentence now says what the event log's `first_sighting` does and does not
mean, as `coverage` and `provenance` do since PR #760.

EU `search` (PR #761) answers from the EU index when the named work is an EU work: a CELEX, a work
or expression IRI, or one of its provisions. It runs the Luxembourg search's two lanes (strict, then
relaxed; byte-exact; no ranker; paged with `continue_after`) over the one wording the EU index holds
of the work's expression in the language asked, with EU context (`eu-eurlex`, jurisdiction `eu`,
`official_consolidation_state`). Each hit names the article (CELEX, expression, the publisher's
article id and heading, article identity), its `wording_date` and the provision coordinate EU
`resolve` answers; no text or snippet is served. `wording_date` is the date the Formex package gives
the act (for the GDPR 2016-04-27), and the answer says it is not a publication, entry-into-force,
application or consolidation date and is never merged with a Luxembourg applicability date; no EU
answer carries `applicability_date` and no Luxembourg answer `wording_date` (tested). Refusals, all
with EU context: a language the work holds no expression in is `language_not_available` (French
answers `["eng"]` until Decision 89's expressions are acquired); two expressions in the language are
`ambiguous_identifier`; any `date` is `retrieval_mode_unavailable` (`requested_mode: r6_as_of`),
because one work on a date is `as_of`'s question and the index holds one original wording and no
consolidation, so no later date could be answered honestly; an unknown EU identifier is
`identifier_unknown`; an EU identifier on a mount with no EU index is `no_corpus_mounted`
(`required_corpus: eu`, where it was a mode refusal); an identifier both indexes hold is
`ambiguous_identifier`, as `resolve` answers it. A search that names no work is the Luxembourg
search, unchanged. `EuropeIndexReader` gained a work-scoped search on its own read-only connection,
and its older date-range `Search` and its counts now take the reader's lock (they used the shared
connection without it). Survey of 2026-09-29 (read-only): the EU index holds one original wording
per expression, no dated state, no consolidation, no article order and no permalink, so EU `as_of`,
`timeline`, `article_history`, `diff` and `changes_in_period` need consolidation acquisition and an
EU states table, `in_force_on` and `status_on` need Cellar force facts, and `evidence_bundle` and
`verify` need an EU permalink grammar (the parity details are driver decisions, below). EU `dossier` is the next slice that
needs none of those.

`events` and `answer_drift` (PR #760) read a new index table, `events`: the index's append-only
event log (`seq`, `scope`, `key`, `event`, `observed_from`, `detail_json`; schema
`lex-v3-luxembourg-index/6`, `user_version` 6, the fixed-input byte pin moved). A build is one
observation with no predecessor, so its log is a **genesis log**: one `first_sighting` per held
state, in the states table's key order, numbered from 1, keyed by the state's primary key and
carrying its digest, with `observed_from` null because no observation time reaches the index (the
body's HTTP evidence stays in the adapter, and the custody receipt's clock is not an observation
time, so none is invented). The log is a pure function of the states, so the reader recomputes it
and refuses any other rows. `events` takes `after` (a cursor `{log_id}:{seq}`, where the log id is
the index digest), `limit` (up to 200) and `event` (one of the mintable names); it answers the
events with their state's permalink and stable coordinate, `has_more` and `next_after` (always the
cursor to poll next), the log block (`basis: genesis`, `predecessor_index_sha256: null`,
`observations_compared: 0`, count, last seq), the delivery rule (cursor polling, at least once,
deduplicate by seq within log id, no push: Decision 93), what `first_sighting` does not say, that
silence is not upstream health, the twelve names this pipeline may mint (this build mints
`first_sighting` only; the others need a predecessor build) and those this log holds, and what is
not held. Append-only and "the same cursor answers the same events" hold while one index is
mounted, and the answer says so: a new build starts a new log whose cursors are new. A cursor from another log refuses `snapshot_unknown` (its first producer) rather than
being read against this one; a sequence number beyond the log's last is a request-schema failure.
`answer_drift` (optional `identifier`, refusing as `dossier` refuses; `after`; `limit`) enumerates
the past dated answers the log's `validity_revised` and `interval_closed` events invalidated; a
genesis log holds none, so it answers an empty list with its basis, `asserts_no_drift_in_law:
false`, `asserts_publisher_unrevised: false` and what would answer (a later build compared against
this one), never "nothing drifted". The code holds twelve event names (`V3EventRegistry`); the Stage
4 registry's thirteenth, the coverage event, is never minted (B42 finding 5.2; the scope-line gate
forbids production source naming it); the mintable registry stays at twelve (driver decision). The two "not held" sentences
that said no first-sighting event is held (`provenance`, `coverage`) now say no observation time is
held and what the log's `first_sighting` does and does not mean. Decisions taken by the driver,
reversible: events are scoped to states (no work-level events); the cursor names its log;
`answer_drift`'s future rows are date intervals per revising event (stated in the answer).
Deferred to the next index schema: predecessor chaining (the builder takes the previous verified
index, copies its log unchanged and appends comparison events), `observed_from` from the route
evidence, and every revision event.

`ask` (PR #759) answers the containment and nothing else (Decisions 51 and 91, S4-A05): every
request, whatever the question, is a success envelope under the `point` verdict whose result is a
`handoff_card` carrying the typed presentation result `assistant_v3_unavailable` (not a refusal
code; the registry stays at twenty), the containment and its end condition (the `answer_dossier/1`
and advice-boundary slices reviewed and integrated), `model_gloss: disabled`, and the deterministic
operations that answer from held law, in resolver-first order: `resolve`, `search` (with the
languages this mount holds searchable text in), `as_of`, `evidence_bundle`, each with its route, the
parameters its reviewed request schema requires (read from the schema) and what it answers. The
request schema is `question` (a non-blank string) and nothing else; the question is not read, stored
or echoed, so the card is byte-identical for every question. No model is called and no publisher is
contacted. The same card over MCP (`tools/call ask`). The envelope verdict is `point`, a driver
decision under ruling 5 (review of PR #759: the catalogue's POINT delivers an instrument, an
official link, a reason and a human counter, and the card has only the reason; Decision 91 cites
Decision 63(a)'s presentation verdict as the precedent). Decisions taken by the driver, reversible:
the host takes the verdict from the result (every other operation still answers `answer`); `ask`
refuses `no_corpus_mounted` without the Luxembourg index, as every served route does;
`localization_unavailable` (the other Decision 91 result) is a surface concern of chrome locales and
is not produced by the API. The guards: `V3CorpusAskMountTests` puts the eighteen scope-line
questions (now one shared list, `tests/Lex.V3.TestSupport/ScopeLineQuestions.cs`, which
`ScopeLineQuestionTests` pins its cases to) and five containment questions to the mounted route over
REST and MCP and requires the builder's card, verdict and object type for each, so a route that
answered any of them otherwise fails there (the review's mutation, a question-dependent answer, is
caught); `AssistantContainmentTests.TheLegacyAssistantRouteIsStillDisabled`, which asserted that
`ask` had no route, and `ScopeLineQuestionTests` now pin the binding and the builder and say that the
route is the ingest suite's. The owner may read Decision 91 as "no route until S4-A04 and S4-A05"; if
so, say the word and the route is removed.

`status_on` and `browse` (PR #757) read the `work_facts` table too. `status_on` takes
`as_of`'s request (`identifier`, `date`, optional `language`), selects the state as `as_of` does and
refuses as `as_of` refuses, and beside that state serves the publisher's force assertions about the
work verbatim (`in_force_status` = jolux:inForceStatus tokens, `entry_into_force` =
jolux:dateEntryInForce, `no_longer_in_force` = jolux:dateNoLongerInForce, each with subject, value,
datatype and evidence digest) plus one fixed reading of the dates, `asserted_in_force_on_date`
(true when an entry date is on or before the requested date and no end date is; false when an end
date is on or before it or every entry date is after it; null when no dated fact is asserted, when
only an end date after the requested date is asserted, or when a lexical value is not exactly a
civil date `yyyy-MM-dd`), with the rule and the basis in the answer; when the publisher
asserted no force fact the absence is typed (`force_facts_held: false`, `what_would_answer`,
`asserts_absence_of_law: false`) and is never read as "not in force". The real 1991 act's envelope
asserts no force fact, so the test fixture asserts them through a new helper and holds the reading
on every side of the dates. `browse` lists the held works in work-key order, paged (`limit` up to
200, `after` = work key), each with its publisher identifiers, languages, first and latest state
dates, state count and the publisher's typeDocument and rdf:type facts; `type` filters by the
typeDocument IRI or its last segment, compared exactly and case-sensitively (a substring test, no
LIKE; a value that is neither an absolute IRI nor a bare token is a request-schema failure),
`language` by the states' language (a work selected by one language still lists them all); the listing scans the states in key order and cuts by limit, so a deep page costs
the rows before it. Decisions taken by the driver, reversible: the force reading is a civil-date
comparison and nothing more (an inForceStatus token is served, not read); `browse` answers
`work_record` rows rather than a `classification` tree; no capability cells for the fact table.

`classification` and `manifestation` (PR #756) read a new index table, `work_facts`: the
publisher's typed assertions from the Stage 3 envelope (`TypedAssertions`, all 26 admitted
predicates), verbatim, one row per assertion with the subject, the predicate and fact kind as the
closed vocabulary names them, the object as an IRI or a literal with its datatype and language tag,
and the observation digest. The index schema was `lex-v3-luxembourg-index/5` (`user_version` 5; the
stamp, the logical-rows hash and the reader's exact-schema check all cover the new table; the
fixed-input byte pin moved). `classification` takes `dossier`'s request (`identifier`, optional
`language`) and answers the facts on the work's own IRIs and the selected expressions grouped by
predicate: `document_types` (jolux:typeDocument), `resource_types` (rdf:type), `legal_values`,
`responsible_bodies`, `historical_identifiers`, `publication_dates`, `document_dates`, each with
subject, object kind, value and evidence digest, plus the count and the names of other predicates
held but not grouped; nothing is inferred and no subject term exists. `manifestation` answers, per
selected expression, the manifestations the publisher asserted (jolux:isEmbodiedBy) with their
formats (jolux:userFormat, the last IRI segment as the token) and items (jolux:isExemplifiedBy), marks
the manifestation whose body this corpus retained and carries the corpus manifest's body digest, and
lists the retained members; an optional `format` filters and, when no manifestation of the selected
expressions carries it, refuses `format_not_available` naming the formats held. Both refuse as
`dossier` refuses. Decisions taken by the driver, reversible: every admitted predicate is stored (the
table is the envelope's assertion list, not a selection); `status_on` and `browse` follow in the next
slice over the same table (the real act's envelope carries no in-force facts, so `status_on` needs a
fixture that does; done in this pull request).

`evidence_bundle` (PR #755) is the first operation that serves article text. It takes
`as_of`'s request (`identifier`, `date`, optional `language`), selects the state as `as_of` does and
refuses as `as_of` refuses; then, per selected state and before any text is read, it enforces the
launch contract's rights rule: every corpus member the state's articles come from must be
`acquired` under the rights disposition `agreed_same_run_cc_by`, or the whole request refuses
`text_withheld` naming the official identity, the official link and the retained body digest; a
state whose articles hold no text refuses `text_not_available` (official identity, official source,
the retained receipt digest, and the absence evidence `what_would_answer` /
`asserts_absence_of_law: false`). The answer carries, per state, the permalink, stable coordinate,
state digest, rule profiles, the sources as `provenance` names them and the distinct retained body
digests; per article, the identity, publisher id and wId, the article-level date, the text (the
publisher's wording: the text and reference tokens concatenated in publisher order, the same bytes
`search` matches), `text_sha256` over exactly those UTF-8 bytes, the `wording_sha256` the index
keeps, the notes (marker and body text, beside the text and outside the wording digest), the
language, the `body_sha256` of the corpus member the article was read from, the official source
and an article permalink (`permalink#publisher_id`; `verify` accepts that form and answers
`digest_matches` naming the article, or `anchor_not_in_version` when the pinned state does not hold
it; the language is pinned through the state digest, which is over the expression IRI and
language, and named beside the permalink). Articles are in the state's own order. An article whose
tokens carry no text (the index admits a marker-only article as evidence) is named under
`articles_without_text` with its reason and is not served as a quote; a state none of whose articles
holds text refuses `text_not_available`. Decisions taken by the driver, reversible: the text is the
searchable wording rather than a re-rendering of the token stream; rights are enforced per request
and not per article (one withholding source withholds the bundle); signature, observation time and
export formats are named as not held. Known limit, for the owner to rule on if it matters for
quotes: the text is the publisher's text and reference tokens joined with no separator, because
paragraph structure and whitespace-only nodes are not retained at ingest, so "Art. 1er.La
profession" runs together; the token stream the composer could render from is in the index
(`tokens_json`), the paragraph boundaries are not. The web refusal card now accepts the registry's `text_not_available` payload beside
the reading view's own provision-level one (two producers, two declared key sets) and the catalog's
two text examples show the producer's fields.

Registered and not served: `as_observed`, `concepts`, `knowable_on`, `transposition`. Since PR
#758 a request for one of them is the typed
transport failure `operation_not_served` (HTTP 404, `application/problem+json`, below the envelope
like every transport failure), which tells a registered operation with no route apart from a path
nothing names (`unknown_route`, still the answer for a typo, a query string, a trailing segment or
a case variant); an MCP `tools/call` naming one says "registered operation this mount does not
serve" in its invalid-params message; `coverage` names them and now says what a request returns.
Decision taken by the driver, reversible: a transport failure rather than an envelope refusal,
because the registry has no refusal code for "operation not served" and inventing one would change
the reviewed registry; the owner can ask for an envelope answer instead. Survey of 2026-09-28 (read-only): the index-table group of four
needed a new index table over Stage 3 inputs the ingest already holds (`TypedAssertions`: in-force status and dates, `typeDocument`, `isEmbodiedBy`,
`userFormat`), which bumps the index schema; `concepts`, `transposition`, `as_observed`,
`knowable_on`, `events` and `answer_drift` need data the ingest does not produce.

MCP: served over streamable HTTP at `POST /mcp` (PR #754), the request half of the
transport: one JSON-RPC message in, one JSON document out (`initialize`, `tools/list`, `tools/call`;
a notification answers 202 with no body; a JSON-RPC error is an HTTP 200; a GET is 405; a body over
the platform's 1 MiB ceiling is 413 before parsing; no server-initiated stream is offered). One tool
per served REST operation, named by its operation id, whose `inputSchema` is the reviewed request
schema's `parameters` shape, and whose call runs the same dispatch the REST route runs
(`V3ApiHandler.ExecuteFor`), so the two transports answer one envelope for one request; the
endpoint test proves it for all twenty-three. The launch-contract line "REST and MCP derive identical
envelopes from the registry" is the owner's to tick.

Web: 41 React components, 950 tests. Preview screens render fixtures. The V3 Luxembourg search
answer has a reader (PR #771). The answer census now samples `search` five ways from the real handler:
- a phrase with 4 strict hits and 1 relaxed hit;
- the same phrase one hit per page, with its cursor;
- the page that cursor leads to, 4 hits against a population of 5, because the population counts
  the whole result and not the page;
- the relaxed lane alone, where the strict count is null (not scanned), not zero;
- a word the held text does not carry: no hit is an answer.

`web/scripts/search-answer.mjs` (`readSearch`) reads that shape and throws on any answer that
breaks a rule the answer states about itself:
- strict hits before relaxed ones, and a hit's one reason is its lane's; with a mode, every hit is
  in that lane and the other lane's count is null;
- each article of a state appears once, and there are no more hits than the limit;
- a cursor exactly when the page is truncated, and the cursor names the last hit;
- the population holds all of an untruncated first page, and at least one hit more than a page
  after a cursor or before one;
- each hit's permalink is the work, date and state digest the hit names;
- `ambiguous_works` (a dated search's works with several applicable states, which contribute no
  hits) and `work_resolution` are read into the view.

An EU search answer (`publisher: "eu-eurlex"`) has another shape and is refused by this reader.
It reads no text snippet, because the answer carries none. The pre-V3 renderer
`search-results.mjs` (`lex_id`, `provision_num`, a row set) stays for the preview until the live
search screen replaces it.

The live search screen (PR #772) is built into `dist-live/search.html` beside Trust and Coverage,
with its own bundle `client-live-search.js`, which embeds the census contract and nothing else of
the census.
- The form sends only the phrase as typed (never trimmed or folded, at most 512 characters and 32
  distinct words, counted as the platform splits them) and a language from French, German and
  English. That list is a driver default: the platform answers a language it holds no text in
  with `language_not_available`. The form's controls carry no `name`, so a submit the browser
  performs itself (before hydration, or without the bundle) sends nothing, and the phrase never
  reaches the address bar, the history or a referrer.
- It sends one `POST /api/v3/search` when the reader submits, never while rendering. A new search
  cancels the one in flight, and "Next page" repeats the search with the cursor the page handed
  over (`createSearchSession`).
- An answer is read by `readSearch` and laid out: the population in both lanes, the work
  resolution, any ambiguous works, and "the first hits in the stated order, not the best hits".
  Each hit shows its article, work, version date, lane and printed permalink; the permalink is not
  a link yet, because this origin serves no reading page for it.
- The two refusals a request from this page can meet (`no_corpus_mounted`, `language_not_available`)
  are refusal cards. A transport failure, an unreadable envelope, or an answer the reader refuses
  is each a state with a sentence. A server that was reached and refused the request
  (`request_schema_invalid`, in practice a cursor from a result the server no longer holds) is said
  as refused, not as unreachable.
- The tests drive it with the census's real whole envelopes. The envelope census now holds a
  search answer with hits in both lanes and a search `no_corpus_mounted` refusal.

The V3 Luxembourg dossier answer has a reader (PR #774). The answer census now samples
`dossier` (the fixture work in `fra`: one state, no titles, 9 items not held).
`web/scripts/dossier-answer.mjs` (`readDossier`) reads that shape and throws on any answer that
breaks a rule the answer states about itself:
- the states are the work's own (its work key and publisher work IRI), in the language asked
  when one was asked, in date order, each listed once and holding at least one article;
- each state's stable coordinate names its work and date, and its permalink pins that coordinate
  and its digest;
- a state's next date is the next later date in its own language, or null;
- `state_count`, `history_begins` and `latest_applicability_date` are the states';
- each title group belongs to one of the states' expressions, in the language asked;
- `not_held` names each item once with its reason. It is carried whole: it is the V3 form of the
  pre-V3 screen's unfilled slots.

An EU dossier (`publisher: "eu-eurlex"`) has another shape and is refused. The fixture holds one
state and no titles, so the tests also read a work with three states in two languages and a
titled work, built from the captured answer the way `V3CorpusMount.Dossier` builds them. The
pre-V3 `dossier.mjs` renderer stays for the preview until the live dossier screen replaces it.

The live dossier screen and its journey step (PR #775). `dist-live/dossier.html` has its own
bundle `client-live-dossier.js`, which embeds the contract and nothing else of the census.
- The form sends the work identifier as typed, plus a language only when one is chosen (French,
  German, English, or "any held language", which sends none). The controls carry no `name`, as the
  search form's do.
- One `POST /api/v3/dossier` per submit, and a new request cancels the one in flight.
- The answer is read by `readDossier` and laid out: the work and its languages, its titles ("This
  index holds no title for this work" when none), the state count and range, a table of states
  (language, applicability date, next state, articles held and not admitted, printed permalink),
  and every item the dossier does not hold with its reason.
- An EU work's dossier is answered in another shape and is said as not shown on this screen.
- The envelope census now holds the dossier answer and its four refusals from the real handler. A
  language not held, an EU identifier on a mount without the EU index (`no_corpus_mounted`,
  `required_corpus: "eu"`), and no mount are refusal cards. A `no_corpus_mounted` sentence names
  the missing index from its payload, on this screen and on search: "This build has no EU index
  mounted." or "This build has no Luxembourg index mounted." It never says "no index mounted",
  which is false on a server that holds the other publisher's index (review of #775).
- An unknown work (`identifier_unknown`) is said by its code without the card. `refusal-card.mjs`
  keeps its standing requirement that this card carry a `population_disclosure` (the size of what
  was searched, so "not found" is never read as "no such law"), and the platform's payload carries
  none yet. That disagreement is now visible on a live page: either the producer carries the
  disclosure, or the reader supplies it from its own census.
- `journey.mjs` now runs a third step. On `/dossier.html` it types the fixture work's identifier and
  submits with any language. With the mount the page ends in the dossier ("1 state, from 2024-02-01
  to 2024-02-01", the not-held list); without one, in the `no_corpus_mounted` card. All six runs
  pass: 6 requests each, exactly one to the API with exactly the typed body, the page at its own
  address, no history entry, history state or cookie, and hydration clean. A build whose request
  always added a language failed both dossier runs on the body check.

The reading screen's V3 source has a reader (PR #776). The text of a state, with what a quotation
needs, is the `evidence_bundle` answer, which the answer census already samples (the fixture state
in `fra`, 49 quoted articles). `web/scripts/reading-answer.mjs` (`readEvidenceBundle`) reads it
and throws on any answer that breaks a rule it states about itself:
- text only under the admitting rights disposition, stated by the bundle and by every source;
- one state per served language, in the languages' ordinal order: the one applying on the date
  asked, from on or before it to a next date after it or none, and in the language asked when one
  was asked;
- state coordinates and permalinks as the dossier's, and each article's permalink is the state's
  with the publisher's article id after `#`;
- each quoted article has text in its state's language, with the UTF-8 byte length stated, read
  from one of the state's sources (`body_sha256s` are exactly the sources' bodies) and pointing at
  the state's official source;
- each article's validity flag is exactly "its own date is stated and differs from the state's",
  and the state's count is theirs;
- articles without text are named apart (`no_text_tokens`), never quoted and never also listed as
  quoted; a state with no quoted article is refused, not answered.

The reader does not recompute text digests: it is synchronous, and the browser's digest is not.
Its tests check every captured text digest with Node's SHA-256, and `V3ReplayGuaranteesTests`
recomputes them from the publisher's file. The tests also read a bundle in two languages built as
the producer would send one: German before French, the German state from its own corpus member
with its own source, body and article identities (review of #776). The pre-V3 `reading.mjs` stays for the
preview until the live reading screen replaces it.

The live reading screen and its journey step (PR #777). `dist-live/reading.html` has its own bundle
`client-live-reading.js`.
- The form sends the work identifier as typed, a date written `yyyy-mm-dd` (a text field, checked as
  a calendar date), and a language only when one is chosen. The controls carry no `name`.
- One `POST /api/v3/evidence_bundle` per submit. The answer is read by `readEvidenceBundle` and laid
  out state by state:
  - the permalink, and the counts quoted, held without text and not admitted;
  - each article's text as a quotation in its state's language (`lang="fr"` from `fra`, never the
    interface's);
  - both dates where an article's own date differs from its state's;
  - its notes, its text digest and its permalink;
  - the articles held without text, named and never quoted empty;
  - the not-held list.

  The words "in force" never appear.
- The envelope census now holds the bundle answer and four reading refusals: a date before the
  history, an unknown work, an EU identifier on a mount without the EU index
  (`retrieval_mode_unavailable`), and no mount. It replaces each per-run `object_ref_sha256` with a
  fixed digest, as it does the corpus and index digests, because the corpus mints those per run and
  the bundle's sources carry them.
- `retrieval_mode_unavailable` and `no_corpus_mounted` are cards. So are `ambiguous_version`,
  `text_withheld`, `text_not_available` and `language_not_available`, checked against the payloads
  the refusal census records.
- Two absences are said by their code, without the card:
  - `identifier_unknown`, as on the dossier;
  - `no_version_for_date`, because the card's contract requires every absence to carry
    `what_would_answer` from its closed vocabulary (and `asserts_absence_of_law`), and the
    platform's payload names the nearest earlier and later dates instead. Its status line still
    carries the date to ask again: "The history this index holds for this work begins on …".
    `text_not_available`'s payload does carry both, so its card is shown.
- Found and fixed on the way: the React refusal card threw on the platform's own `ambiguous_version`
  payload. The platform sends its candidates as bare hash-pinned links, and the string card
  normalised them while the React card read the raw strings. Both now use one `candidateView`, which
  says "publication date not stated by the platform" and "withdrawal not stated by the platform". A
  test renders the real payload and holds the two cards to the same text; it failed on the old card.
- `journey.mjs` runs a fourth step: `/reading.html`, typing the fixture work's identifier and
  `2024-02-01` into the two fields. With the mount the page ends in the text ("49 articles quoted",
  "Art. 15."); without one, in the card. All eight runs pass. A build whose reading request always
  added a language failed both reading runs on the body check.

The provision history screen's V3 source has a reader (PR #778). The answer census now samples
`article_history` (the fixture work's `art_15` in `fra`: one row, no absence).
`web/scripts/history-answer.mjs` (`readArticleHistory`) reads it and recomputes everything the
answer derives from its rows, throwing where they disagree:
- rows and absent states together are the states in scope, each once, each in date order, each
  pinned by its permalink, in the language asked when one was asked;
- every article in a row carries the anchor asked for, and its validity flag follows its dates;
- `wording_changed` is true exactly when a row's wording digests differ from the previous row of
  its language; `wording_runs` are each language's first row and every change;
  `distinct_wordings` are each language's distinct digests; `history_begins` is the first row;
- a row's next date is the next later state of its language in scope, whether or not that state
  carries the anchor.

The tests also read a lineage with an absent state between two rows, a changed wording and a
second language, built the way `V3CorpusMount.ArticleHistory` builds one.

The live provision history screen and its journey step (PR #781). `dist-live/history.html` has its
own bundle `client-live-history.js`.
- The form sends the work identifier and the publisher's article id as typed, and a language only
  when one is chosen. The controls carry no `name`.
- One `POST /api/v3/article_history` per submit. The answer is read by `readArticleHistory` and laid
  out:
  - how many held states carry the id, from when, and how many do not;
  - each language's wording runs and distinct wordings;
  - a table of rows: language, applies from, next state, "first held wording", "wording changed" or
    "wording unchanged", the article's own date (marked where it differs from the state's), and the
    permalink;
  - the held states that do not carry the id;
  - the wording rule.

  Nothing is derived: no end date, no "in force", no diff text.
- The envelope census now holds the lineage answer and three refusals: an article id no held state
  carries (`anchor_not_in_version`, `art_44`), an unknown work, and no mount.
  `anchor_not_in_version` is the third absence the card rejects (driver decision: the producer will
  carry the fields); its status line carries the nearest ids to try, "art_4, art_40, art_41,
  art_42".
- `journey.mjs` runs a fifth step: `/history.html`, typing the fixture work's identifier and
  `art_15`. With the mount the page ends in the lineage ("Carried by 1 held state, from
  2024-02-01", "first held wording"); without one, in the card. All ten runs pass, 6 requests each,
  exactly one to the API.

The compare screen's V3 source has a reader (PR #782). The answer census now samples `diff` twice
from the real handler: the fixture's one state against itself, and a second fixture's two states a
year apart with `art_15` reworded and `art_16` renamed to `art_16-new`. The second fixture is its
own, so every other sample stays the one-state answer. `web/scripts/compare-answer.mjs`
(`readDiff`) reads both and recomputes what the comparison derives:
- each side is the state that applies on its date, pinned by its permalink, with its dated articles
  exactly its article identities and its validity flags and count following its dates;
- one comparison per compared language and one entry per language not compared (with its bound),
  each language once and in order, and together exactly the scope: the language asked, or every
  language the work is held in;
- the same state on both dates carries no articles and no counts;
- otherwise every article of each side appears in exactly one row, the rows are the ids in ordinal
  order, each row's status is its sides' (added, removed, unchanged when the ordered wording digests
  are equal, changed when they are not), and the counts are the rows'.

The live compare screen and its journey step (PR #783). `dist-live/compare.html` has its own bundle
`client-live-compare.js`.
- The form sends the work identifier, two dates written `yyyy-mm-dd`, and a language only when one is
  chosen. One `POST /api/v3/diff` per submit.
- The comparison is read by `readDiff` and laid out linearly: each side's state and permalink, the
  date the next held state applies from (said as that state's start, never as "until"), its
  article count and how many of its articles carry their own date differing from the state's, the
  platform's note ("nothing about legal effect is asserted"), the counts, then the changed, added
  and removed articles (each status a word, with the whole wording digests on each side). The
  unchanged articles are one disclosure away. The same state on both dates says so, with no counts.
  The wording rule and the validity conflict rule close the page.
- The envelope census now holds the same-state comparison and three refusals (a from date before the
  history, an unknown work, no mount). The two-state comparison from the answer census is rendered
  in the tests: `art_15` changed, `art_16` removed, `art_16-new` added, 47 unchanged.
  `ambiguous_version`, `profiles_differ` and `language_not_available` are cards, checked against the
  refusal census's payloads. `no_version_for_date` and `identifier_unknown` are said by their code,
  the former with the date the held history begins.
- `journey.mjs` runs a sixth step: `/compare.html`, typing the fixture work's identifier and
  `2024-02-01` twice. With the mount the page ends in "The same version applied on both dates";
  without one, in the card. All twelve runs pass.

The radar screen's V3 source has a reader (PR #784). The launch contract's Radar is the change radar,
`changes_in_period`. The answer census samples it twice from the real handler: a window holding the
one-state fixture's first held state, and a window holding both states of the two-state fixture.
`web/scripts/radar-answer.mjs` (`readChanges`) reads both and holds every rule the answer states
about its rows:
- each row's state lies in the window (the closed interval between the two dates), pinned by the
  permalink that `resolve` serves; rows are in date order, then work;
- a compared row has one baseline of its work and language, dated before it and followed by it,
  with the same rule profiles; its wording change is exactly "an article changed, was added or was
  removed"; it has its counts, and `diff` parameters that ask for that pair;
- an uncompared row carries one of four reasons: `first_held_state` (no baseline),
  `ambiguous_version` and `ambiguous_baseline` (each with its candidates, its own state among them
  for the first), or `profiles_differ` (with the baseline whose profiles differ);
- the page names the first date not served exactly when it is truncated, and an untruncated page
  holds every version the population counts.

The live radar screen and its journey step (PR #787). `dist-live/radar.html` has its own bundle
`client-live-radar.js`.
- The form sends two dates written `yyyy-mm-dd`, plus a work identifier and a language only when
  given. One `POST /api/v3/changes_in_period` per submit.
- The radar is read by `readChanges` and laid out: the window and its population; the platform's
  caveat (a version row asserts no wording change, legal effect or entry into force); and each row's
  work, language, date and permalink. A compared row says whether its wording changed from its
  baseline, with the counts and the baseline's permalink; an uncompared row says why in words (the
  first held state, an ambiguity, different rule profiles), an ambiguity lists its candidate states
  and a row whose profiles differ names its baseline, each by permalink. An empty window says
  whether it meets what is held.
- The envelope census now holds a one-row window, an empty window before anything held, and no
  mount. The two-state radar from the answer census is rendered in the tests (the later state
  "wording changed" from its baseline).
- `journey.mjs` runs a seventh step: `/radar.html`, typing `2024-02-01` twice. With the mount the page
  ends in "2024-02-01 to 2024-02-01: 1 state of 1 work, of 1 held" and the first-held-state reason;
  without one, in the card. All fourteen runs pass.

The API serves the live pages on its own origin (PR #788, ruling 3). `Lex.V3.Api` opens `v3-web`
beside it (the built `web/dist-live`) through `V3WebRoot` and serves exactly the files it held at
start, by GET or HEAD, with `/` as `index.html`. A path it does not hold reaches the API's routing as
before, and another method is 405. Every page response carries:
- the page's own reviewed Content-Security-Policy, read from `index.html` (entities decoded) so
  there is one source, plus `frame-ancestors 'none'`, which only a header can carry;
- `Strict-Transport-Security: max-age=31536000; includeSubDomains`;
- `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff` and `Cache-Control: no-store`.

A directory with a page lacking its policy, a file of a type it does not serve, or a name outside
the served pattern is refused, and the API runs without pages rather than serving them wrong. No
live page's text says "synthetic" (the live banner never does). `journey.mjs --served-by-api`
places the build as `v3-web` and loads every page from the API's origin, with no Node server, and
checks the headers the page arrived with. All fourteen runs pass that way too. `serve-live.mjs`
stays for local development.

Every citation the served answers emit verifies (PR #796, the launch contract's first promise:
"verify resolves every citation the product emitted"). `V3CitationVerificationTests` drives the
operations the live screens read on the answer census's two fixture mounts (search, dossier, as_of,
evidence_bundle, provenance, status_on, article_history, diff and changes_in_period, the last two on
both), walks each answer whole and takes every citation by its role (a `permalink` or `*_permalink`
property, a `resolve.identifier`), which must be a hash-pinned permalink (a malformed one fails,
review of #796), and any other permalink wherever it sits, and asks `verify` for each on the mount
that emitted it: each must answer `digest_matches` for the state its digest names, and an article
permalink must name its article. The walk is held to find a permalink in every screen's answer, at
least one article permalink, and nothing that is only prose. The journey's own pages add the browser half when the journey runs next.

The interface languages on the live pages (PR #797, the launch contract's "DE and LB answer
`localization_unavailable`"). Every live page carries a language list (`LocaleNav`): English,
Français, Deutsch, Lëtzebuergesch, each named in itself and tagged with its own `lang`, the page's
own language marked current, and each link's `hrefLang` the language of the page it leads to
(English for an unreviewed language; review of #797). Only English chrome is reviewed (`REVIEWED_CHROME_LOCALES`), so the
other three lead to `locale-fr.html`, `locale-de.html` and `locale-lb.html`, built into `dist-live`:
the `localization_unavailable` page, in English and labelled English, under the live banner, with
no script. It says exactly what the preview's page says (`localeUnavailableCopy`, now shared; the
preview pages are byte-identical after the change). French chrome replaces its page once its copy
is reviewed (Decision 41); German and Luxembourgish stay unavailable at launch.
- Statute language apart from the interface's: the interface is `<html lang="en">`, and every piece
  of publisher text a live page shows carries its own language. That was already true of the
  quotations and notes on the reading and export pages; the dossier's titles (each in its
  expression's language) and search's matched titles now carry it too. A matched title is marked in
  its own language, which the resolver's card now names (`matched_title_language`, from the title
  row): the resolver searches every language's titles, so the language searched in is not the
  title's (review of #797). History, compare and radar show digests and ids, no publisher text.

The live pages' interface copy starts to come from one table (PR #800, the first step toward French
chrome). `web/scripts/live-chrome.mjs` holds each live page's title, eyebrow, heading and
introduction and every form's labels and buttons, in English, and the eight pages and their forms
render from it; every page rendered before and after the move is byte-identical. `liveChrome(locale)` answers only for a reviewed language and throws
for any other, since serving it would be a substitution; a test holds each page to its entries and
the table to the reviewed languages. French is drafted beside it for review
(`live-chrome-fr-draft.mjs`, printed by `node web/scripts/live-chrome-fr-draft.mjs`): never
imported by the product, and held by a test to the English table's exact shape, so an English
entry without a draft is caught.

The API process records nothing while a browser asks it (PR #801, the launch contract's "no query
text, IP or user agent recorded", on the journey host). In source, `PublicRequestRecordingTests`
already bounds the process to three startup messages, cleared logging providers, a pinned package
set and no write-capable file open. At run time, every journey run now captures the process's
standard output and error from its first byte, and watches its directory from when it first
answers until the page has asked, listing every file at both ends: after the first answer it must
write nothing and touch no file under its directory, including a file written and deleted within
the run (the review of #801 found two listings alone could not see one), and its startup output
must carry none of the run's text (the query, the browser's user agent). The first run of the check caught the synthetic bootstrap's startup
diagnostic (`lex_v3_preview_bootstrap_failed reason=immutable_custody`), which is why startup is
judged apart rather than admitted by its text. With the check, all sixteen journey runs pass, and
all sixteen again with `--served-by-api` (run 2026-09-30 on the journey mount written again that
day: the data lane's corpus format change had made the earlier mount unreadable, "the corpus
manifest-set bytes are not one valid typed document").

Keyboard and screen-reader paths through the eight screens (PR #802, the launch contract's
accessibility line).
- Screen reader: every live screen writes its answer (the result, the refusal card, or the sentence
  a state carries) into a polite live region (`LiveAnswer`) that the server renders around the idle
  or loading state, so the region exists before the answer arrives and a screen reader hears it.
  The export composer's panel is a live region of its own, since pinning changes it and the reading
  above does not. Every journey run now holds the answer to that region, at load and at the end.
- Keyboard: `journey.mjs --keyboard` drives every form step by the keyboard alone: from the top of
  the page, Tab until each text field has focus, type key by key, Enter to submit; the export
  composer's pin is reached by Tab and checked with Space. Each character is its own key press, and
  the page counts the character keys it receives: a run whose text arrived without them fails (the
  review of #802 found CDP's `Input.insertText` sets a field with no key event at all). Every focus stop on the way must show a focus indicator
  (an outline or a shadow), and Tab must reach every field the step types into (a text or a search
  field). All sixteen runs pass with `--keyboard`, and all sixteen with `--keyboard --served-by-api`
  (run 2026-09-30); the first run found the search box is `type="search"`, which the keyboard path
  now types into as well.

The screens' sentences join the chrome table (PR #803, the second step toward French chrome).
- `live-chrome.mjs` now also holds each screen's idle sentence, the loading sentences, and every
  sentence the search, dossier, reading and history screens say about an answer: the counts, the
  headings, the table columns, the work resolution and the lines a row is made of.
- A sentence that carries values is a template with `{name}` placeholders, filled in place
  (`fillParts`, laid out by `Say` in `LiveAnswer.jsx`), so a translation can put a quotation, a
  date or a permalink where its grammar needs it. A placeholder without a value, or a value without
  a placeholder, throws.
- A sentence that counts is `{ one, other }`, chosen by the language's own plural rule
  (`Intl.PluralRules`): French counts zero as one ("0 version"), which a comparison with 1 would
  get wrong.
- The phrases the platform itself sends (a matching rule, a scope, why something is not held) stay
  the platform's English, shown as sent. Compare, radar, export and Trust and Coverage keep their
  answer sentences in their components for the next slice.
- Every census envelope of the four screens, rendered through its answer view before and after the
  move, and each idle page: 21 renders, byte-identical except one. History's per-row wording marker
  is now a language-free code (`data-wording="first"`, `"changed"`, `"unchanged"`), not the English
  label.
- The French draft covers every new entry. A test holds each draft template to its English
  template's placeholders and each counted entry to both forms. French still ships only once
  reviewed (Decision 41).

The compare, radar and export screens' sentences join the table (PR #804).
- Compare: the summary, each side, the counts, each row (its status now labelled from the table,
  `changed`, `added`, `removed`, `unchanged`, rather than printed as the platform's code), the
  unchanged articles' disclosure, and a language not compared, with its bound labelled the same
  way.
- Radar: the summary (two counted phrases, states and works), each row and its verdict, the four
  reasons a state is not compared, the baseline and candidates, the empty window, and the truncated
  list.
- Export: the reading line, the pins, the held-without-text note, the panel's counts, rights,
  snapshot, each item and each exclusion, the three save buttons, the nothing-pinned sentence and
  the two sentences a format refused or a failed composition says.
- Every census envelope of the three screens through its views, with the export panel empty and
  with every article pinned, and each idle page: 17 renders, all byte-identical before and after.
  The paths the census does not reach (two different states compared, a radar row with a baseline,
  a list of candidates) are held by the screens' existing tests to their exact markup.
- The French draft covers every new entry. Trust and Coverage's answer is laid out by the shared
  `Coverage` component, whose copy stays its own for now, and the refusal card's labels come next.

Every citation the journey's pages print verifies (PR #805). This is the launch contract's first
promise, with its stated evidence: "`verify` resolves every citation the product emitted in the
journey suite". PR #796 walked the served answers; this walks what the browser actually shows.
- Each run collects every permalink the answer prints (the code elements beginning with a slash,
  inside the live regions).
- Each permalink must be hash-pinned, in the grammar `V3CitationVerificationTests` uses.
- A step that cites (every screen but Trust and Coverage) must print at least one on an answer.
- Once the run's recording is closed, each permalink is asked of the API's `verify`. It must answer
  `digest_matches` for the very state the permalink pins, and for the article it names.
- Every quotation must carry, beside it, its text digest, its body digest, its official source and a
  permalink pinning its own article. The review of #805 found that the export composer quoted 49
  articles and cited only their state. The reading page and the export composer now share one
  evidence line under each quote (`QuoteEvidence`).
- The journey's summary line counts the citations verified in each run. All sixteen runs pass, and
  all sixteen with `--served-by-api` (2026-09-30). The fixture mount's runs verified 57 citations,
  each `digest_matches`: reading 50 (the state and its 49 articles), export 2, and one each for
  search, dossier, history, compare and radar. Search's five hits sit in one state, whose permalink
  is the answer's `resolve.identifier`.

No interface text on a live page bypasses the chrome table (PR #806, the completeness test the
French chrome plan asked for).
- `live-chrome-scan.test.mjs` compiles the pages a second time with the table swapped for a
  pseudo-locale, in which every letter of every entry becomes its fullwidth form. It then renders
  every page and every census answer of the seven answering screens, with the export panel fully
  pinned.
- Any ASCII letter left in a text node or a human-read attribute, once the answer's own data is
  taken out, is text a French table could never translate, and fails the test. Putting one heading
  back as a literal fails it.
- The first scan found:
  - the page title's suffix;
  - the live banner, a driver's draft that makes a claim ("not a release and not legal advice");
  - the locale navigation's label;
  - the language names in the selects ("French", "German", and "English", which the review of #806
    found hidden behind a page-wide exemption for the locale navigation's own language names);
  - the date and article-id placeholders.

  All of them are now in the table, and the French is drafted.
- Three surfaces keep their own copy and are listed in the test as the next slices: the refusal
  card (its sentences are the checkpoint list of #793), the `Coverage` component, and the evaluation
  card. An export's watermark and JSON are the file's content, not interface text.
- Headless Chrome no longer downloads components (`--disable-component-update`,
  `--disable-background-networking`) in the journey, `keyboard-walk.test.mjs` and the browser
  evidence harness. With a fresh profile per run, it had left 1,581 component packages (4.4 GB) in
  the temporary directory. That, with a reviewer's solution build, filled C: on 2026-09-30. With the
  flags, a 16-run journey leaves no package; the two journeys before it, without the flags, left 39.

The evaluation card's words join the chrome table (PR #807).
- `EvaluationCardView` now takes every word from the table's `card` section: its heading, the "run
  over" line, the clean and not-clean summaries (counted by the language's plural rule), the verdict
  and control words, each table's caption and columns, and the controls, statistical rows and
  negative results.
- The card's own values stay the card's: its target sentence, set and gate names, figures, reasons
  and notes.
- The card rendered clean, and failing with one gate failed and one control missed, is
  byte-identical before and after. The Trust and Coverage page differs only in React's hydration
  boundaries.
- The chrome scan now covers the card as well. It exempts the card's values but not its verdicts
  (review of #807: the card's own "pass" had let a hard-coded "pass" through). What it still leaves
  out is the refusal card and the `Coverage` component. The French is drafted. The card beside the release assets belongs to the
  release pipeline.

The refusal card's words join the chrome table (PR #809).
- `refusal-card.mjs` exports `REFUSAL_CARD_COPY`, the card's words built from its own constants,
  so there is still one English source for the string renderer and the live pages:
  - the token label;
  - the retry sentence;
  - the absence note and its heading;
  - the three route labels;
  - the per-code notes;
  - the declared-null sentences;
  - the description of an offered state.
- The string renderer now builds its candidate line from the same words.
- `live-chrome.mjs` takes `refusalCard` from that export, and the French is drafted beside it.
- `RefusalCard` takes an optional `copy` prop (English by default, so the previews are unchanged),
  and every live screen passes the table's. The card now receives the codes behind each label
  (route codes, declared-null rows, the raw publication date and withdrawal). No rule moved:
  `validateRefusal` decides as before.
- The scan now renders every census refusal of the eight screens through its view. Putting one
  heading back as a literal fails it. The sentence a refusal says is the checkpoint list's (#793)
  and counts as data. So do the payload's member names, which the card shows as sent (driver
  decision below).
- All 18 census refusals render byte-identically before and after. The chrome scan now leaves out
  only Trust and Coverage's `Coverage` component.

Trust and Coverage's answer joins the chrome table, and the scan exempts nothing (PR #810).
- `coverage.mjs` exports `COVERAGE_COPY`: the page's headings, fact labels, captions, column names
  and yes/no, its five notes, and templates for its five sentences (the gaps, the operations
  served, a narrowed answer, an absent capability, an unserved capability). `COVERAGE_COLUMNS`
  gives each table's column order.
- The string renderer and `Coverage.jsx` both take their words from it. The two copies of every
  heading and caption that stood in both renderers are gone.
- The sentence functions take an optional copy, English by default. `Coverage` takes a `copy`
  prop, and the live page passes the chrome table's `coverageAnswer`. The French is drafted.
- The census coverage answer through both renderers, and the live page, are byte-identical before
  and after.
- The chrome scan now renders Trust and Coverage's answer too and has no exemption left: every
  word on the eight live pages, answers and refusals included, comes from the table. The only
  exceptions are data and each language's own name in the locale navigation.

The launch contract's accessibility and scope line held on the live screens (PR #811): "no meaning
by colour alone, linear diff, explicit dates, bracket tables whole".
- `live-a11y.test.mjs` works over every census envelope of the eight screens and each page as
  served:
  - Explicit dates: no relative date ("today", "3 days ago"), no month written out beside a number,
    no local numeric format, and every ISO date a real calendar date. Quotations of the law are
    left out, since their dates are the publisher's.
  - A linear diff: each compared language is one list, one row per article, its status in the
    table's words, with no side-by-side table and no inline colour.
  - Bracket tables whole (the product spec's rule 11): every quotation on the reading and export
    screens is the publisher's whole article text, in order, none dropped or cut.
  - No control takes a personal fact: every form has only the declared, labelled text fields, with
    no number, range or choice that could filter a bracket.
  - Truncating a quote to 200 characters, or adding a "Seniority" number field, fails the test.
- `journey.mjs`, in the real browser: every element painted apart from what is behind it must say
  what it is, in words or an accessible name. Painted means a background image, a background colour
  other than the one it sits on, a visible border, an outline or a shadow. Borders, outlines and
  shadows count since the review of #811 found an empty red-bordered span passed. Each run prints how
  many painted elements it examined. A wordless red square injected beside each search hit failed
  the search run (8 painted, 5 wordless).
- `paint-check.test.mjs` proves the check in a real browser against two fixtures:
  - four marks that say what they are pass;
  - four wordless marks (a background, a border, an outline and a shadow) are all caught.

  Removing the border clause fails it.

The live export composer and its journey step (PR #789), the eighth screen. `dist-live/export.html`
has its own bundle `client-live-export.js`.
- The page asks what the reading page asks: the same form (`ReadingForm`, now shared), the same one
  `POST /api/v3/evidence_bundle`, the same outcome mapping and refusal sentences. It lists each
  state's articles with a pin each (a checkbox with no `name`), the text quoted in its state's
  language, and the articles held without text, pinnable so their exclusion travels.
- Pinning composes the export in the page (`export-build.mjs`) and asks nothing. The panel shows,
  before anything is saved: the counts, the watermark (the composer's own sentence), the rights
  disposition and rule, the date read, the snapshot's observation time, the corpus, index and
  registry digests, each item's citation (the hash-pinned article permalink), whole text digest and
  official source, each exclusion with its reason, and the JSON itself. "Save as JSON" and "Save as
  CSV" hand over exactly `exportJson` or `exportCsv` of that model, from a Blob in the page, under
  `lex-v3-export-<identifier>-<date>`. A new reading clears the pins.
- JSON (`lex-v3-export/1`) and CSV (RFC 4180, CRLF, one row per pinned article, the excluded ones
  included with their status and no text, each row carrying its citation, rights, the watermark,
  the snapshot's observation time, the rights rule and the corpus, index and registry digests, so
  a row copied out alone loses none of it). An excluded article keeps its citation, the state's
  permalink and its article id, in both formats (review of #789). PDF is the next
  paragraph's.
- The reading and export pages say the next held state's date as its start ("the next state held
  applies from"), never "until", as the compare page does after its review.
- `journey.mjs` runs an eighth step: `/export.html`, typing the fixture work's identifier and
  `2024-02-01`, then pinning the first article. With the mount the page ends in the composed export
  ("1 article pinned: 1 exported with text, 0 excluded.", the watermark, the rights); without one, in
  the card. Composing sends no second request (the verdict still counts exactly one).

The PDF export (PR #790). `web/scripts/export-pdf.mjs` sets the same export model as pages, so the
three formats cannot disagree, and the export composer offers "Save as PDF" beside JSON and CSV.
- Every page carries the watermark at its head and "Page n of N" at its foot. The first page says
  what was asked, the snapshot's observation time, the rights served under with the platform's rule,
  the counts, and the corpus, index and registry digests. Each item carries its citation, state
  permalink, text and body digests and official source (monospaced, wrapped without losing a
  character), its rights, its own date where it differs, its notes and its whole text, every byte
  kept: its lines joined with nothing between them are the text (review of #790). Each exclusion
  carries its citation and reason. No heading ends a page alone.
- Written byte by byte with no library: PDF 1.4, A4, uncompressed streams, the standard Helvetica
  and Courier fonts in WinAnsiEncoding, no embedded font and no time of making, so the same model
  gives the same bytes. Typographic spaces and hyphens outside WinAnsi are set plainly, and the PDF
  says so when it did; any other character it cannot set refuses the PDF with the characters named
  (`pdfRefusal`), and the panel then offers JSON and CSV only and says why.
- The tests read the file back on its own terms (header, every cross-reference offset, trailer,
  page tree, stream lengths, each text run decoded from WinAnsi by the test's own table) and check
  the launch contract's citations, rights, watermark and exclusions on it, every line inside the
  text width, and page breaks losing nothing. Read independently with pypdf (strict) and PyMuPDF:
  the fixture's 49 articles make 24 pages, with the watermark, the page numbers and the accented
  text intact.
- The launch-contract line "Exports PDF, JSON, CSV preserve citations, rights, watermarks and
  exclusions" is the owner's to tick.

The evaluation card on the Trust and Coverage page (PR #792, ruling 2). The page carries the card
below the coverage answer, rendered by the server from the card the build is given (by default the
one the platform renders, `schemas/v3-platform/evaluation-card.json`; a release build will be handed
the release card), outside the hydrated tree, so it needs no script and the page still asks one
request.
- `web/scripts/evaluation-card.mjs` (`readEvaluationCard`) holds the card's rules and recomputes
  what it can: each gate's verdict in the closed vocabulary, exactly a not-measured gate carrying a
  reason and no value, a pass at or above its threshold and a fail below it, each rate's Wilson 95
  percent interval (recomputed from the value and the stratum; anchor nDCG, a graded mean, has
  none), the rule-of-three bound beside every 1 (recomputed), each set's own gates and nothing else
  (temporal exactness; the refusal verdict match; retrieval's three, in order), one shuffled control
  per case set in order and of that set's kind (a control over fewer cases than its set says why),
  and every statistical row `not_yet_labelled` (Decision 92). A card that breaks a rule is not
  printed. The live build takes the card it is handed (`buildLive(destination, { card })`).
- `EvaluationCardView` prints what the card was run over first, as the card says it ("THE MOUNT IS A
  FIXTURE ... this card is not a release card"), then whether every gate passes and every control
  caught its shuffle (or which do not), one captioned table per case set (verdicts as words, values,
  thresholds, cases, the interval, the bound), the controls, the statistical rows "not yet
  labelled", and the negative-results register.
- Not yet: the card as signed JSON at a stable route and beside the release assets, and the gates
  run over the real mounted corpus; both follow the release pipeline (item 7) and the first mount.

**The search journey step passes (PR #773, run locally 2026-09-30).** `node scripts/journey.mjs`
now runs two steps, each with and without the fixture mount, and all four runs pass.
- The search step loads `/search.html` and waits for hydration. It types "assemblée générale"
  over the DevTools protocol (`Input.insertText`, so React's own change handler runs) and presses
  the submit button.
- With the mount, the page ends in the answer, showing "4 with the exact phrase, 1 with every
  word, in 1 work", `art_15` and the no-ranker sentence. Without one, it ends in the
  `no_corpus_mounted` card.
- Each run makes 6 requests, exactly one of them `POST /api/v3/search`. Its body, read from the
  request the browser sent, is exactly `{operation_id: "search", parameters: {query, language:
  "fra"}}`. The page ends at `/search.html` with the history length it had when it loaded and
  no history state, so the phrase is not in the address or the history. `document.cookie` is
  empty at the end and the API request carried no cookie. There is no referrer, no storage and
  nothing on the console, and hydration is clean.
- Shown to fail:
  - a live build whose request carried one extra field failed the body check, naming the field;
  - a build that pushed the phrase into the history and set it in a cookie after submitting (the
    review of #773 showed it passed before these checks) failed three checks: history grown from
    2 to 3 entries, history state written, cookie set.
- Not driven yet: the next page. On the fixture the phrase has 5 hits and the form asks the
  platform's default page size, so no page is truncated and no "Next page" button appears. The
  cursor-carrying request is covered only by the injected-fetch test of PR #772. Driving it needs
  a mount with more hits than one page, which the first real mount would give.

**The first browser journey step passes (PR #766, run locally 2026-09-30):** `node scripts/journey.mjs --api <Lex.V3.Api build
output> --mount <journey mount>` runs the real `Lex.V3.Api` from a copy of its build output with the
mount beside it, serves `dist-live/` through `serve-live.mjs`, and drives headless Chrome over the
DevTools protocol until `data-answer-state` settles. With the fixture mount (written by
`V3JourneyMountTests` when `V3_WRITE_JOURNEY_MOUNT` names a directory; skipped otherwise) the page
ends in the coverage answer showing the mounted corpus and index digests; with no mount it ends in
the `no_corpus_mounted` refusal card. Both runs: 6 requests, exactly one to the API
(`POST /api/v3/coverage`, no query string), every other a same-origin asset, the headers the browser
actually sent (`requestWillBeSentExtraInfo`) with no referrer and no cookie, nothing in storage,
nothing on the console and no uncaught exception or unhandled rejection (a page given either fails
both runs, shown with `--live-root` on a deliberately broken build), the reviewed CSP, hydration
clean. The first run caught the journey's own
over-strict check: under `no-referrer` Chrome reports a provisional `Referer: ""`, and the verdict now
reads the headers actually sent. The journey is a local run, not a CI step (it needs a browser and a
built API). PR #765 builds the first live
page, Trust and Coverage, into its own directory (`node scripts/build-live.mjs` writes
`dist-live/`, gitignored, apart from the preview `dist/` and its gates): the loading state under a
live banner (the synthetic banner's "describes no real legal record" would be false on a real mount;
the live banner's wording is the driver's; ruling 3: a live page never says "synthetic" on a real mount), hydrated by its own bundle
(`client-live.js`, which embeds the census contract and nothing else of the census; the preview
pages' `client.js` still carries no request). `scripts/serve-live.mjs` serves that directory and the
API on one origin for a local journey: static files with `nosniff` and `no-store`, and exactly
`POST /api/v3/{operation}` forwarded (no query string, case variant, deeper path, `/mcp` or other
method), carrying the body and its media type and nothing about the caller (no cookie, user agent,
referrer or forwarded address), a body over 1 MiB refused before it is forwarded, an unreachable API
a `502` problem, an API that never answers a `504` after 30 s; the traversal guard and the chunked
body ceiling are tested with raw requests, since `fetch` normalises dot segments and always declares
a length. PR #764
adds the first live screen as a component, `LiveCoverage` (Trust and Coverage): the server renders
its loading state, the browser asks `coverage` with no parameters in an effect through the client
module, and the answer is the `Coverage` page (read by `readCoverage`, so a served answer the reader
cannot account for is shown as unreadable, never rendered), the refusal card for a refusal (the one a
coverage request can meet, `no_corpus_mounted`, in the refusal catalog's words, held equal by a
test; a refusal whose card the card's own rules will not show is a status line naming its code,
never a render that throws), or a status line for a transport failure or an unreadable answer; the
mount's one request is `startLiveCoverage`, which asks once and whose cancel aborts it and silences
it (tested; the effect itself is proven by the next slice's browser journey); the root carries
`data-answer-state` for a browser run to wait on. No page mounts it yet (the page, the build that
embeds the contract, and the same-origin serving are the next slice). PR #763 added the first code
that can call the API: `web/scripts/v3-envelope.mjs`, a strict reader that mirrors
`V3EnvelopeJson.ParseAndVerify` (exact closed member sets, the reviewed registry digest, one branch
with a consistent verdict and context status, the result bound to the operation's schema and object
types, a registry refusal code with every mandatory payload field, the `anchor_not_in_version`
rule, the request reference's and snapshot identity's forms, a real observation instant that is not
the default, and the nesting limit of 32; it cannot see duplicate members or non-canonical bytes and
says so), and
`web/scripts/v3-client.mjs`, which asks one served operation by a same-origin POST whose parameters
travel in the body alone (no credentials, no cache, no referrer, redirects refused, no retry,
nothing logged) and returns the renderable states (`success`, `refusal`, `transport_failure` with
the problem code, or `network_error` when the connection fails before or while the body arrives,
`invalid_envelope` with the reason; a cancellation rejects as one). The reader holds no copy of the registry: it
reads the `contract` block of a new census, `schemas/v3-platform/envelope-samples.json`, which the
platform renders from the reviewed registry beside four whole envelopes the real handler sent
(coverage answer, the `ask` card, `no_corpus_mounted`, a refusal with a payload), each verified by
`ParseAndVerify` before it is recorded (render with `V3_RENDER_ENVELOPE_SAMPLES=1`). The client's
served list is pinned to the platform's own list in the coverage answer sample.

Machine gates (PRs #767, #768 and #769): the evaluation harness in `Lex.V3.Contracts.Evaluation`
(`TemporalEvaluation`, `VerdictEvaluation`, `ShuffledControls`), which nothing outside its own tests
called, now runs against the real handler on a mounted corpus (`V3MachineGatesTests`). The temporal
case set (8 cases on one work with a single first state, two states on one later date and a single
latest state: before history, the first day, inside, the last day before the twins, the twin day,
inside the twins, the latest day, after it) is asked four ways: `as_of` and `in_force_on` (the work
named), each with `language: "fra"` and with no language. A selected state is its digest and a
refusal is `refusal:{code}`. All four arms reach exactness 1.0 and pass gate `temporal_exactness`,
and each arm's date-shift control is caught: every case moves forward by the held interval and every
expectation breaks. The cases at the latest state cannot break under a forward shift, so by the
control's design they sit outside it. The refusal case set (18 requests over three fixture mounts)
covers each of the 14 codes the refusal census records as produced (`ambiguous_identifier`,
`ambiguous_version`, `anchor_not_in_version`, `format_not_available`, `identifier_unknown`,
`language_not_available`, `no_corpus_mounted`, `no_version_for_date`, `pinned_digest_mismatch`,
`profiles_differ`, `retrieval_mode_unavailable`, `snapshot_unknown`, `text_not_available`,
`text_withheld`), and the test pins that list to the census. Three requests must be answered. The
served operations reach exact match 1.0, the gate passes, and the verdict-shuffle control is caught.
The six codes nothing produces yet (`advice_boundary`, `derivation_refused`, `not_transposable`,
`out_of_corpus_scope`, `rate_limited`, `upstream_unreachable`) are outside the set. Two production
mutations were each caught: `as_of` choosing one of two states on an ambiguous date fails both
gates, and `in_force_on` answering the twin date for a named work instead of refusing fails the
temporal gate. The retrieval case set (PR #768) has 13 cases in one collection over two works of one date, and
the test writes every article's text. There are 6 judged searches: a strict hit graded 3 above a
relaxed hit graded 1, one query both works answer, and one search scoped by `identifier` to the
second work that must find that work's article and not the first's. There are 4 cases that must
find nothing: a word held nowhere; a word asked with the `identifier` scope of the other work, in
each direction; and a near-miss article permalink naming an anchor the state does not hold, which
`verify` must refuse `anchor_not_in_version`. The last 3 cases are exact article permalinks
(`permalink#anchor`), the form only `verify` accepts; it answers each held anchor with its work.
`verify` names the requested anchor back rather than serving a provision, so what the resolver
stratum measures is that each held permalink is accepted under its own work and a missing anchor
is refused. A refused search fails the test: search answers zero hits, never a refusal. Anchor
nDCG@10 is 1.0, and no-hit accuracy and resolver exactness are 1.0. The test declares the nDCG
threshold at 1.0 because the judgments are written from the corpus the test builds: it measures
the path's exactness, not retrieval quality, and the labelled statistical row stays "not yet
labelled". The judgments-shuffle control is caught: nDCG@10 falls below 0.15 and both invariant
gates stop passing. The launch contract's three shuffled controls are now each shown to fail on
the real handler. Three production mutations each fail the retrieval gate: `search` serving
relaxed hits before strict ones, a scoped search matching no work, and `verify` answering an
anchor the state does not hold. The cases come from the fixture, so the gates prove the path
until a real mount exists.

Evaluation card (PR #769): `EvaluationCard` in `Lex.V3.Contracts.Evaluation` prints the machine
gates as the card of `36-ideal-evaluation.md` section 6 describes, as far as the launch contract
asks. Each case set and arm is a row with its case count and `cases_sha256`. Each gate carries
its verdict (`pass`, `fail` or `not_measured` with its reason), value, threshold and stratum `n`.
Every rate carries its Wilson 95 percent interval, and every value of exactly 1.0 carries the
rule-of-three bound on the failure rate, so a pass on a small set is never oversold. Anchor nDCG
is a graded mean, so it has no Wilson interval. Its 1.0 still carries the bound: each case scores
at most 1, so 1.0 means every case ranked perfectly. Each shuffled control result is printed like
any other number, with the count and digest of the cases it ran over. The date control runs over
6 of the 8 temporal cases, and its note says why: the cases at or after the latest held state
cannot break under a forward shift. The 8 statistical rows (D1 to D8 of section 2) are all
`not_yet_labelled` under Decision 92. The negative-results register holds its standing entry:
hybrid retrieval is not activated, and search refuses a ranked mode `retrieval_mode_unavailable`.
What would reverse that is the activation gate as section 2 (D6) and section 1.4 (repair 6) state
it. The machine gates test renders the
card to `schemas/v3-platform/evaluation-card.json` (census, `V3_RENDER_EVALUATION_CARD=1`) and
compares it byte for byte on every run. The fixture's cases give the same card run after run. As
held today: temporal 8 cases per arm, Wilson [0.6756, 1], rule of three 0.375; refusal 18 cases,
[0.8241, 1], 0.1667; retrieval nDCG n 9 (rule of three 0.3333), no-hit n 4 ([0.5101, 1], 0.75),
resolver n 3 ([0.4385, 1], 1, which bounds nothing). The card is not published and is not a release card: it
has no release, image or snapshot identity (item 7) and no signature. Ruling 2: it is served on the
Trust and Coverage page and beside the release assets, with the gates run over the real mounted
corpus.

Replay guarantees (PR #770): `V3ReplayGuaranteesTests` runs the two guarantees of
`33-product-spec.md` G1 to G5 that one build can prove against the real handler.
- G2, snapshot determinism: each of the 23 served operations (pinned to `V3RestRouteBinding.Served`,
  so a new operation fails until it has a case) answers the same canonical bytes to the same
  request asked again and to a mount opened from a byte copy of the directory. Asked 30 days later
  by another request, the answer is the same bytes apart from the two fields that belong to the
  request: `context.freshness.observed_at` and `request_ref`, the digest of the request's own
  trace identity. Every envelope is also verified canonical by `V3EnvelopeJson.ParseAndVerify`.
  `resolve` is asked with the fixture's permalink, so the check covers its answer path and not
  only a refusal.
- G5, independent verifiability: a reader holding one `evidence_bundle` answer and the
  publisher's file checks each served article's text against that file. The text of the article
  with that id is its non-blank text nodes in order, outside the publisher's `scl:` annotations
  and the authorial notes, and it equals the served text for every article. The reader then
  recomputes the digests whose derivations are published. The file hashes to the source's
  `body_sha256` and length, and each article's `text_sha256` and byte length are its text's.
  `article_identities_sha256` and `state_sha256` recompute from the derivation `provenance`
  publishes, and the permalink pins that digest. Not recomputed: `wording_sha256`, whose input
  is the stored token stream the bundle does not serve, and the article identities, rule-profile
  digests and body receipt, whose derivations are not published.

Running G5 found a defect: the published derivation put the domain tag outside "each as UTF-8
preceded by its length", while the builder length-prefixes it. A reader who followed the sentence
got another digest. The sentence now reads "over these values, each as UTF-8 preceded by its
length as four bytes big-endian: the domain tag lex-v3-luxembourg-expression-state/1, the
publisher, ...". The same wording is in the provenance answer, its test pin, the answer-samples
census and the web preview's copy. G1 (a replaced publisher file mints a new version and a
`file_replaced` event), G3 (nothing hard-deleted across builds) and G4 (as-observed answering;
`as_observed` and `knowable_on` are registered and not served) need predecessor chaining with
observation times, which follows the first mount; see the event-log question (b) below. G2's
detached signature comes from the release pipeline (item 7). The mount is the fixture, so this
proves the path, not a corpus.

## Data

- **Decision 95 rights receipt (Codex data lane, 2026-09-30).** Governance PR #9 is merged.
  The live receipt now requests Commission Decision 2011/833/EU at
  `https://publications.europa.eu/resource/celex/32011D0833`, negotiating English XHTML through
  the existing document-fetch profile. No request goes to EUR-Lex. Evidence schema
  `lex-eu-legal-notice-evidence/3` records the closed `source` value; the legacy notice URI keeps
  its profile identity. Retained legacy routes can be reconstructed into /3; serialized /2 receipts
  are not accepted by the /3 reader.
  The receipt is acquired before the adapter or Formex population. A refused receipt spends one
  logical product request (plus robots and any admitted redirect hops), then stops. Its retained
  hops are rebound under the adapter's eventual corpus identity without another request.
  Live check through the production session: 303 then 200 XHTML, 48,730 bytes, SHA-256
  `2d5bc877b9a5aad948af21c680aca1d3409f41df5dd0dcc57ef9b1b225f60982`, matching the verified plan.
  Custody and canonical evidence: `C:\lex-v3\lanes\rights-probe`; log: `rights-probe.log` beside it.
  This receipt is the Commission policy the notice cites; Decision 95 records the accepted limit
  for Parliament and Council documents. When EU evidence bundles serve text, they must carry
  `© European Union, https://eur-lex.europa.eu` and the statement that only the electronic Official
  Journal is authentic. EU text bundles are still the parity slice; this change acquires the receipt.
  Next: rerun the bounded first mount with Lex.V3.Tool, using PR #750's command and the Codex
  checkout. The historical failed attempts below remain evidence of the old route.

- EU: complete 82-seed English population on disk at `C:\lex-v3\eu-population-run-9` (filesystem
  custody, 29,207 files, report `population-report.json`, `isCompletePopulation: true`, 1.9 h run).
  French expressions not acquired (Decision 89).
- Luxembourg: live per-work rights proof and adapter canaries only. **No bulk Luxembourg population run
  found on disk or in the repository.** The Luxembourg population scope and its run are an open item.
- No corpus, index or capability manifest has ever been built outside tests. The three builders take a
  Stage 3 envelope that only tests compose.
- **Run 9 cannot feed the builders** (investigated 2026-09-27). The envelope is an in-memory object
  graph with reference-identity checks, built in one process from one run identity; nothing in it was
  serialised and no reader exists. Run 9 is 82 runs with 82 identities. Two mandatory EU inputs were
  never acquired: Formex packages (the only source of EU articles for the index) and the EUR-Lex
  legal-notice evidence. A Luxembourg delivered run is mandatory in every envelope. Building the
  corpus therefore means a fresh, live, one-process acquisition run.
- **Historical Decision 88 producer (PR #746), superseded by Decision 95 and PR #780.**
  It used the `european_union_legal_notice` profile on EUR-Lex. The old route's robots response
  and its eventual empty HTTP 202 are recorded below as evidence of the failed attempts.
  The current producer uses the existing Publications Office document-fetch profile, captures the
  Decision receipt first, and rebinds it after the adapter completes. Redirects remain subject to
  the profile, literal robots evaluation and the one wire ceiling.
- The session now exposes the custody write receipt of every hop an executed attempt sealed
  (`HopWriteReceiptsByObservationId`), which the Formex ZIP binding needs as well.
- **Formex package population.** `EuFormexPackagePopulationProducer` runs the real manifestation
  enumeration for English and French expressions only (PR #799), with repeated-enumeration proofs
  and the shared wire budget. Every other language is `not_enumerated_language_out_of_scope`, with
  eligibility unknown. For the enumerated expressions it closes
  eligibility, then acquires every eligible package the corpus can serve
  (`EuFormexPackageAcquisitionProducer`): one GET of the exact `fmx4` manifestation the enumeration
  delivered, on the route the office serves (manifestation URI, 303 to `{manifestation}/zip`,
  200 ZIP; observed 2026-09-04 on GDPR, pinned by the reachability tests), through the acquisition
  session with its own robots bootstrap and the run's one ceiling; the retained route is bound as a
  package transport to WEMI references rebuilt from the run's own expression identities
  (`EuFormexAnnexTransportBinding`'s new manifestation-level constructor: no Cellar Item is named,
  because the office serves the package by manifestation and no item enumeration is needed), read
  into the annex inventory under one fixed interpretation profile, and `acquired`. Proven offline
  on the real GDPR package through the real session: the corpus states
  `formex_main_body_admitted` for the held EU member and the main-body producer parses its 99
  articles. What is not acquired is stated as its own outcome, never as a transport refusal:
  `not_acquired` with a reason (`body_not_held`: the run holds no body for that expression; French candidates now have their
  own acquisition path, and the corpus binds every acquired Formex outcome to one held body; `language_not_addressable`, `manifestation_not_singular`, `identity_not_admitted` for works
  outside the reviewed seed-root map, including consolidated expressions; and the
  four annex reasons below),
  `route_refused` with the status for any answer but 200 or 404 and for a 200 reached on a route
  that does not bind (a hop off the manifestation's path; review repair on this pull request),
  `package_rejected` with the inventory refusal for a 200 that is not a Formex package,
  `unavailable` for 404. Three corpus
  stage 3 dispositions were added for them (`formex_main_body_package_not_acquired`,
  `_route_refused`, `_package_rejected`), so the corpus file now tells a deferred or rejected
  package from a transport refusal (the builder's domain compatibility check admits them; a
  read-only survey of the annex chain on this pull request found it ranged over the old members
  only, which would have thrown inside `TryBuild` for a held member with a package not acquired;
  fixed and pinned by a corpus built from the annex-bearing package).
- **The annex chain in production (this pull request), fixtures only.** A package whose inventory
  names annexes goes on through the chain the reference tests composed and production never ran:
  the held work body is read as the publisher's XHTML annex inventory, the work's PDF is fetched on
  the document-fetch route (`GET cellar/{work}` with the `pdfa2a` or `pdf` accept the expression's
  enumeration lists; the office's 303 to the PDF item, then the 200), the three are bound as one
  annex population with the PDF page labels (`EuAnnexEvidenceBinder.BindTransportAsync`, the
  manifestation-level form: no Cellar Item, the work CELEX and language from the run), the bound
  members are classified against the PDF route, and the classification travels with the run into
  `EuFormexAnnexClassificationReconciliation`. Proven offline on the three real 2026 specimens
  (XHTML held by the run, Formex package, PDF/A) through the real session: acquired, the corpus
  states `formex_main_body_admitted` and the annex outcome for the held member. One extra GET per
  annex-bearing expression, in its own robots session. Two source checks written against
  synthetic test requests were widened to the session's real shape and say so in the code: the
  classifier admits the profile's `user-agent` beside the address's two headers, and the office's
  `;charset=UTF-8` on the PDF content type. What does not close is `not_acquired` with a reason and
  the ZIP retained: `annex_xhtml_not_inventoried` (the held body carries no publisher annex
  convention; no PDF request is sent), `annex_pdf_not_served`, `annex_evidence_not_bound`,
  `annex_body_not_classified`.
- Decision taken by the driver, reversible: the annex chain reads the held work body, so an
  annex-bearing package whose held body is not XHTML with the publisher's `*.fmx` unit wrappers is
  `not_acquired` rather than acquired without its annexes (the corpus would otherwise carry a main
  body whose annexes it cannot state). The PDF accept is the one the enumeration proves listed,
  `pdfa2a` before `pdf`, and the PDF is requested at work level with the expression's language, as
  the reference chain pinned it; the manifestation is read from the office's 303 and must descend
  from the expression.
- Decision taken by the driver, reversible: the package request goes to the manifestation, not the
  work, so content negotiation cannot pick another expression; the terminal must stay on that
  manifestation's own path. The item-level package types (`EuFormexPackage`, `EuFormexItemSet`,
  `EuFormexStreamName`) remain for the test-composed annex binder and are not on the production
  path; both real packages on disk use original-act stream naming that those types refuse.

- **EU side of the first mount: composed (this pull request), fixtures only.**
  `EuFirstMountAcquisition` (Ingest) acquires one Appendix A work end to end under one wire ceiling:
  the adapter run with production plans and publicly bound witnesses (the SPARQL witness is the
  census count query of the work itself, which selects the source profile and the path robots is
  checked against; the document-fetch witness the adapter's signature requires is a GET of the
  work's Cellar root, because a CELEX such as `12012E/TXT` is not an admitted resource path, and
  document-fetch sessions start from the request they send anyway), the Formex population, and the
  legal-notice route under the run's corpus identity. Renderer sources come from the checkout's six
  Europe renderer files, held in the run's custody (`EuRendererSources.FromCheckoutAsync`); until
  now every renderer source in the repository was a test placeholder and every SPARQL witness an
  internal fixture. Proven on the
  scripted transport through the envelope helper and `LexCorpus6Builder`: the built corpus's rights
  matrix names the real notice route. What the survey of 2026-09-27 found and this slice worked
  around: production code had no renderer sources, no SPARQL witnesses and no Luxembourg
  vocabulary snapshot (all live only in tests and canaries); `Lex.V3.Tool` would have no access to
  the internal transport seams, so the composition roots live in Ingest and the tool will be a thin
  program over them.
- Carried: a deployed build without a checkout needs the renderer bytes from another carrier (an
  embedded resource); the release pipeline slice decides.
- **Luxembourg side of the first mount: composed (this pull request), fixtures only.**
  `LuxembourgFirstMountAcquisition` (Ingest) is the Luxembourg composition root the code base never
  had in production (the live adapter canary built it inline; `TestSupport/LuxembourgProfiles`
  records "the LU composition root is Stage 6"). For one act selected by an ELI key range it holds
  the declared scope as the plan's scope document, enumerates the P, T, C and O vocabulary
  partitions with proofs, reopens and classifies them into the publisher's own values, opens the
  `VerifiedLuxembourgSourceProfile` from that observation (required values are expectations, never
  a source), runs the S, A and G families through the adapter and its Gazette loop, then the Akoma
  Ntoso inventory and legal-content producers. Renderer sources come from the checkout's two
  Luxembourg renderer files. `LuxembourgQueryPlan.CreateDefaultGraph(scope)` is the one new
  Contracts door: until now only tests could name the Luxembourg SPARQL profile, so no production
  code could create the plan. Proven offline on a transport that answers by what each query asks
  (each set's SPARQL shape is distinct), then built into a corpus through the envelope helper and
  `LexCorpus6Builder`.

- **The build and the tool: composed (this pull request), fixtures only.** `V3FirstMountBuild`
  (Ingest) takes the two acquisitions and runs the chain the corpus builder's reference test
  composes: the Stage 3 evidence envelope with the real notice route, the body composition, the
  Luxembourg publisher-PDF derivation chain, the derivation profile envelope, then the corpus, the
  Luxembourg index and the Europe index, each built twice and refused as `not_byte_stable` if the
  pair differs. `V3CorpusMountWriter` writes the five files `Lex.V3.Api` mounts (temporary name,
  then move) plus `build-report.json`, and reads a directory back through the public verifiers.
  `src/Lex.V3.Tool` is the `build` program over it: one custody root, one wire ceiling, renderer
  sources from the checkout, the system clock; exit codes 0 built and verified, 2 usage, 3 typed
  refusal, 4 written directory did not verify. Proven offline end to end: the two scripted
  acquisitions into one store, the build twice with equal bytes, the five files verified, and the
  directory opened by the API's own `V3CorpusMount`. Review repair on this pull request: the tool
  crashed on every exit after start-up (a process-exit hook cancelled an already disposed token
  source, so no run could have returned 0, 3 or 4) and checked the Luxembourg act range only
  after the EU side had spent the budget. Now every argument is checked before the first request
  (`LuxembourgActRange` binds its three family ranges at construction), an unexpected failure
  exits 1 with its message, and process tests in the fast lane pin the exit codes without traffic
  (the refused case names a CELEX that is not a seed, refused before any request). The first real
  mount now needs only the bounded live run.
- **The bounded live run, attempt 1 (2026-09-27 21:26 UTC):** killed after 94 s by the Claude Code
  harness for system memory pressure (1.1 GB free of 15.7 GB, held by other applications); 473
  custody files, no milestone, nothing to record.
- **The bounded live run, attempt 2 (2026-09-29 17:27:20 to 18:19:39 UTC): refused at the EUR-Lex
  legal notice.** Same command as PR #750's comment, the Release tool built in the driver's worktree
  at 17:21 UTC from PR #758's source, custody `C:\lex-v3\first-mount-run-1\custody` (attempt 1's
  files reused, content-addressed). Result, from `build.log`: `refused: europe: LegalNoticeRefused:
  NoticeRouteInvalid ... (spent 619 of 800)`, exit 3, no directory written. The EU adapter run and
  the Formex population returned without a refusal; the one GET of
  `https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en`, the last EU step,
  answered **HTTP 202, `Content-Type: text/html; charset=UTF-8`, `Content-Length: 0`, an empty body,
  `Cache-Control: no-store`**, in 20 ms (retained: route evidence
  `09a1443d1be3deae02630d292d34d39e0387f0c9450b7b3e87f660fbe85a651d` in that custody). An empty 202
  on that page is consistent with the EUR-Lex bot-protection challenge Decision 23 anticipated, so the
  route is refused as designed and no retry or second request was sent. Custody from this attempt
  holds 409 logical requests and 402 HTTP evidence documents with 408 hops: 401 answered 200 and 6
  answered 303 by `publications.europa.eu`, 1 answered 202 by `eur-lex.europa.eu`; plus 106
  acquisition sessions, each of which opens with one robots fetch whose evidence is not among those
  documents. The tool's 619 is the budget's reservation count, which the budget holds equal to sends;
  408 hops and 106 robots fetches make 514, and the remaining 105 are not reconciled here (custody is
  content-addressed, so its file counts are not request counts). Memory was not a problem this time (the
  process stayed near 90 MB). Because the build is one process, the acquisition is spent and nothing
  was written; the Luxembourg act was never reached.
- **Attempt 2's blocker is resolved by Decision 95 and PR #780.** Both required changes are
  implemented: the Publications Office Decision receipt replaces the challenged notice, and it is
  fetched before population traffic. Rebinding its retained hops after acquisition avoids a second
  rights request. The successful live receipt check is recorded above; the completed mount is below.
- **Attempt 3 was interrupted during the IDE restart.** Started 2026-09-30 at 08:32:21 UTC,
  using PR #750's GDPR/Luxembourg command and the unchanged 800-request ceiling. Its isolated tool,
  custody and log are under `C:\lex-v3\first-mount-decision95`. The Decision receipt completed
  303 -> 200 at 08:32:25 UTC, 48,730 bytes with the SHA-256 recorded above, before census traffic.
  No exit receipt was written. Its custody remains intact.

- **First real mount completed, 2026-09-30 11:19 UTC (Codex).** The restart used the same isolated
  runtime from `1774a774`, GDPR/Luxembourg selection and 800-request ceiling in
  `C:\lex-v3\first-mount-decision95-restart-20260930`. It exited 0 after 1 h 21 m 54 s,
  spending 700 requests. EU: 91 expressions enumerated, 91 Formex-eligible; Luxembourg: 10 corpus
  records. The combined corpus has 15 members. Corpus, both indexes and both capability manifests
  built twice with equal bytes; all five written files verified on read-back. Corpus identity:
  `02d19a2a68cd6a5b37fe9ccf79a21bd0359b1f8d232c578fb129d86ec7e8d313`.
  The Decision receipt completed 303 -> 200 before census traffic. No EUR-Lex request was sent.
  At 11:19:23 UTC the local verifier returned HTTP 200/answer for coverage, EU `32016R0679` and
  Luxembourg `http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo/fr`, checking their
  contracts, identifiers and corpus/index digests. Evidence: `v3-corpus/build-report.json`,
  `smoke-1/smoke-report.json`, raw envelopes and API logs under the restart root. This is a bounded
  local mount; full populations and production deployment remain outstanding.
  PR #786's cross-family findings are repaired: the verifier has an exact tree-allowlist entry,
  its Luxembourg default names the `/jo/fr` expression, and snapshot/coverage digests are checked.

- **Luxembourg population selector (Codex data lane).** `Lex.V3.Tool build --lu-population all`
  selects every absolute publisher IRI for the existing S, A and G families, from `A` to U+FFFF.
  An IRI scheme starts with an ASCII letter; blank-node subjects have an empty assertion key
  and remain outside the range, matching S's IRI-only subject universe. It is an alternative to
  the three `--lu-name`, `--lu-start`, `--lu-end` arguments; mixing them or naming an unknown population is rejected
  before acquisition. The selected range is retained in the scope receipt before traffic.
  Vocabulary proofs, source-profile classification, rights decisions, typed body outcomes and
  the one wire ceiling remain on the acquisition path. This is a selector for discovered
  publisher data, not a claim that every discovered object is authoritative or redistributable.
  A two-work offline acquisition exercises both bodies and builds one corpus. The review repair
  proves that blank-node assertions refuse under the old empty bound and stay outside the new one.
  No complete live Luxembourg population has been acquired yet. The bulk run follows the bounded first mount
  and EU population preparation (#786 then #785), under an explicit shared wire ceiling.

## Next, in order

Population preparation (data lane): `Lex.V3.Tool build --celex all` selects all 82 reviewed
Appendix A seeds; a comma-separated selection also binds one combined run. The entire selection
is validated before traffic, one Decision 95 receipt precedes the census, and all families share
one wire ceiling and corpus identity. Formex acquisition binds each original expression to its
own reviewed CELEX, including when a batch spans several works. An expression whose work is not
an Appendix A root receives `not_acquired / identity_not_admitted`; this does not add consolidated
wordings. The earlier 82 separate runs remain separate evidence. A complete combined live build
has not yet run; the bounded first mount above is complete.

1. Data lane (Codex, Decision 95): bounded first mount and real-data resolve completed above.
   Finish PR #786 and population PR #785, then continue the population work below.
2. Data lane (Codex). Complete the EU and Luxembourg populations under the owner's 2026-09-30
   authorisation, with one typed outcome per discovered body, then acquire French EU bodies.
3. Data lane (Codex). Formex, the rest: French bodies (Decision 89) so French packages are held and acquired; then
   every acquired main body feeds the EU index for the temporal and search operations (item 5).
   Designed on 2026-09-28 (read-only survey, recorded in the driver's notes): the adapter mints one
   English fetch ladder per work and the corpus record set holds exactly one body per observed
   object (the work), so a held French body means a second fetch pass with `Accept-Language: fra`
   and a second held record per work keyed by the French expression, which touches the record set
   and its completion checks, the content-class binding, the annex binder's lineage, the builder,
   the index and `resolve` (a work identifier or CELEX then matches two equally authentic
   expressions and today's `ambiguous_identifier` needs the language rule Decision 89 lists for
   Stage 4). Its two design questions are the driver's under ruling 7, decided when the slice
   starts and recorded as driver decisions: (a) whether the frozen corpus record schema
   `lex-v3-source-corpus-record/6` (one body per object) changes, to a body per language or an
   expression-keyed record, or the French body travels beside it; (b) the `resolve` language rule
   for a work identifier (default: the English expression, the French one named as an alternate,
   and a `language` parameter selecting either).
4. Serve the unserved operations for Luxembourg (`verify` and `relations` served by PR #753, MCP
   over streamable HTTP by PR #754, `evidence_bundle` by PR #755, `classification` and
   `manifestation` by PR #756, `status_on` and `browse` by PR #757; a request to an unserved
   operation answers the transport failure `operation_not_served`, PR #758; `ask` answers the
   contained `assistant_v3_unavailable` card, PR #759; `events` and `answer_drift` over a genesis
   log, PR #760). Four remain (`as_observed`, `knowable_on`, `concepts`, `transposition`), all
   needing data the ingest does not produce; they keep `operation_not_served`, and the capability
   manifest is to state per operation which data would serve it (driver decision). The event log's next step,
   predecessor chaining with observation times, needs a second build, so it follows the first mount.
5. Data lane (Codex). EU parity: every temporal and search operation from the EU index; French
   expressions. EU
   `search` in one work served by PR #761, EU `dossier` by PR #762; the temporal operations,
   `provenance`, `evidence_bundle` and `verify` wait on consolidation acquisition and an EU permalink
   grammar; the parity details are driver decisions (below).
6. Wire the eight launch screens to `/api/v3`; journeys J1 to J8 in a real browser. PR #763: the
   envelope reader and the client module; PR #764: the live Trust and Coverage component; PR #765:
   its page, the live build and the one-origin server; PR #766: the first browser journey step,
   passing against the real API with and without a mount; PR #771: the V3 search reader
   `readSearch`, held to five search answers the census now samples; PR #772: the live search
   screen and its page; PR #773: the search journey step (type, submit, the answer; the next page
   waits for a mount with more hits than one page); PR #774: the V3 dossier reader `readDossier`,
   held to the dossier answer the census now samples; PR #775: the live dossier screen, its page
   and journey step; PR #776: the reading screen's reader `readEvidenceBundle` over
   `evidence_bundle`; PR #777: the live reading screen, its page and journey step. The four launch
   screens with V3 readers (coverage, search, dossier, reading) are now live and journeyed. PR
   #778: the provision history reader `readArticleHistory`; PR #781: the live provision history
   screen and journey step; PR #782: the compare reader `readDiff`; PR #783: the live compare screen
   and journey step; PR #784: the radar reader `readChanges` over `changes_in_period`; PR #787: the
   live radar screen and journey step; PR #788: the API serves the live pages with the security
   headers (ruling 3); PR #789: the live export composer and its journey step. All eight of the
   launch contract's screens are live and journeyed; PR #790: the PDF export; PR #792: the
   evaluation card on the Trust and Coverage page (ruling 2); PR #793: the refusal sentence list
   for the checkpoint (ruling 4); PR #794: the absence refusals carry the card's evidence, so the
   live pages show them as cards; PR #796: every citation the served answers emit verifies; PR #797:
   the interface languages, German and Luxembourgish (and French until reviewed) answering
   `localization_unavailable`; PR #800: the live pages' chrome in one table; PR #801: the API process
   records nothing while a browser asks it; PR #802: the keyboard and screen-reader paths (live
   regions, and the journey's keyboard mode); PR #803: the search, dossier, reading and history
   screens' sentences in the chrome table, with French drafted beside them; PR #804: the compare,
   radar and export screens' sentences; PR #805: every citation the journey's pages print verifies;
   PR #806: no interface text on a live page bypasses the chrome table (a pseudo-locale scan); PR
   #807: the evaluation card's words in the table; PR #809: the refusal card's words in the table;
   PR #810: Trust and Coverage's words in the table, so the chrome scan exempts nothing; PR #811:
   the accessibility and scope line held on the live screens. Next: the
   owner's review of the French drafts (`node web/scripts/live-chrome-fr-draft.mjs`), then the
   reviewed French build (`/fr/*.html`, `REVIEWED_CHROME_LOCALES` gains `fr`). French ships only once
   reviewed (Decision 41). Hosting (ruling 3): `Lex.V3.Api`
   serves the live pages on the API's origin with `frame-ancestors`, HSTS and `Referrer-Policy`,
   and a live page never shows the synthetic banner on a real mount. J1 to J8 are restated as V3
   steps by the driver (they exist only in the pre-V3 pack, `05-user-journeys.md`).
7. Release pipeline: build, sign, image, zero-traffic deploy, probes. Then acceptance and promotion.
8. Machine gates (launch contract, Evaluation): the temporal, refusal and retrieval case sets run
   against the real handler, and all three shuffled controls are caught (PRs #767 and #768). The
   evaluation card is rendered from them with the statistical rows `not_yet_labelled` (PR #769).
   Ruling 2: it is served on the Trust and Coverage page (PR #792) and beside the release assets, and the
   launch card carries the machine gates run over the real mounted corpus, so the gates gain a
   mounted-corpus run once the first mount exists. Replay G1 to G5 (`33-product-spec.md`): G2 snapshot
   determinism and G5 independent verifiability run on the real handler (PR #770). G1 version
   immutability, G3 bitemporal completeness and G4 as-observed answering need predecessor
   chaining with observation times, so they follow the first mount and the event-log ruling. What
   is left of the launch contract's machine-gates line after that is "V2 absent from the image",
   which belongs to the release pipeline (item 7).

## Owner rulings, 2026-09-30

Posted on PR #777 (comment 5905743344, the owner's account, relayed by the VS Code panel session).
The owner's words: "choose whatever you recommended piloted by the high quality asap driving to v3";
on EUR-Lex: "yes choose b but if you can find a recipt else where i dont really care as long as we
show text (users primarly want a temporal view of eu texts and dont really care about reciepts)".

1. EUR-Lex legal notice: option (b). EU text in EN and FR is shown. The rights receipt may come
   from any legitimate official source that states reuse is authorised; withholding EU text
   (option (a)) is not wanted. The panel session verifies which source answers an automated client
   and posts the URL, digest and a code-scope note as a second comment on PR #777. No request goes
   to eur-lex.europa.eu meanwhile. The panel opens a lex-governance PR recording these rulings as
   Decision 95, which the driver merges.
2. Evaluation card: served on the Trust and Coverage page and beside the release assets. The launch
   card carries machine gates run over the real mounted corpus, not fixture-only scores.
3. Web hosting: one server delivers the web bundle and the API (`/api/v3`, `/mcp`) on one origin,
   so the current CSP and no-CORS stand. That server sends `frame-ancestors`, HSTS and
   `Referrer-Policy`. A live page's banner never says "synthetic" on a real mount.
4. French and English refusal sentences: the owner reviews them in person. The driver prepares one
   short list for the weekly checkpoint and does not block on it.
5. EU parity details, the event log, the `ask` card verdict and the four operations with no data:
   the driver's stated defaults (below).
6. Codex: a weekly cold read of the week's merges, and a second sign-off at the two release gates
   when available.
7. Standing order: the owner is asked only about money, legal or public claims, credentials and
   going live. Everything else the driver decides, records here as a reversible driver decision,
   and keeps driving toward V3 at high quality.

Decision 95 (lex-governance PR #9, merged 2026-09-30) records these rulings and adds two things:
- The EU rights receipt is Commission Decision 2011/833/EU, fetched on the admitted Publications
  Office route (`https://publications.europa.eu/resource/celex/32011D0833`, `Accept:
  application/xhtml+xml`, `Accept-Language: eng`; observed 2026-09-30: 303 then 200, 48,730 bytes,
  sha256 `2d5bc877...`). Decision 88's one-GET exception for `eur-lex.europa.eu` is withdrawn: no
  request goes to that host. Served EU text carries "© European Union, https://eur-lex.europa.eu"
  and the statement that only the electronic Official Journal is authentic.
- Two lanes (amends Decision 94). Codex drives the data lane (the EU rights receipt, the first real
  mount, the populations, French EU bodies, EU parity, release-pipeline pieces without production
  credentials) in `C:\lex-v3\worktrees\codex-data`. This driver keeps the web lane. Every pull
  request is reviewed by the other model family before merge (`C:\lex-v3\lanes\review-by-codex.ps1`
  for this lane's pull requests), with one repair round and a merge on green CI. Production
  signing, deployment and promotion still wait for the owner.

## Driver decisions (reversible)

Each is the driver's call under ruling 7 and can be reversed by a later pull request that says why.

- The refusal card's payload rows keep the payload's own member names (`requested_identifier`,
  `asserts_absence_of_law`) as their labels, in every interface language (PR #809). They are the
  contract's names, shown with the refusal code beside them, and a reader who quotes them can find
  them in the envelope. A labelled form would be a translation of the contract, which the owner can
  ask for.

- Absence refusals (PRs #775, #777, #778): option (a), the producer carries them. The web refusal card
  (`refusal-card.mjs` `ABSENCE_CODES`, from `35-ideal-ux`) requires every absence refusal to carry
  `what_would_answer` from its closed vocabulary (`corrected_identifier`, `new_official_observation`,
  `expanded_official_scope`) and `asserts_absence_of_law`, and `identifier_unknown` to carry a
  `population_disclosure`. The reviewed registry requires `what_would_answer` (as free text) for
  `identifier_unknown` only, and neither `asserts_absence_of_law` nor `population_disclosure` for any
  code. Three produced refusals therefore fail the card (`refusal-payload-samples.test.mjs`
  `KNOWN_BREAKS`): `identifier_unknown` (free-text `what_would_answer`, no disclosure),
  `no_version_for_date` (the nearest dates, neither absence field) and `anchor_not_in_version`
  (`nearest_anchors`, neither absence field). `text_not_available` conforms. Done in PR #794: the
  mount adds the fields to all three, because the rule protects the product's oldest invariant (an
  absence of a record is not an absence of law) and must travel over MCP too.
  - `what_would_answer` is the vocabulary list: `identifier_unknown` corrected identifier and
    expanded scope, `no_version_for_date` a new official observation, `anchor_not_in_version` a
    corrected identifier and a new observation. `asserts_absence_of_law: false` on each.
  - `identifier_unknown`'s prose moves to `what_would_answer_detail` ("a hash-pinned permalink of a
    state this index holds at that stable coordinate" and the like), and it carries a
    `population_disclosure` counted from the mounted index ("This build's Luxembourg index holds 1
    Luxembourg work, with states dated from 2024-02-01 to 2026-04-11"; the EU index counts its
    members).
  - The registry lists mandatory field names only and the refusal schema leaves payloads open, so
    the registry digest and the contract are unchanged; the refusal-payload and envelope censuses
    are rendered again. `KNOWN_BREAKS` is empty, and the live pages show those refusals as cards.
- EU parity (PR #761): (a) the Formex act date (`wording_date`) is the EU wording-state date, and
  `official_consolidation_state` fits an answer over an original wording; (b) the original wording
  does not answer EU `as_of` for dates after it when no consolidation is held; consolidation
  acquisition follows the first mount; (c) EU search stays scoped to one work at launch (no
  `jurisdiction` parameter); (d) moot under ruling 1 (EU text is shown).
- The event log (PR #760): (a) the mintable registry is twelve names (the coverage event is not
  minted); (b) launch may ship genesis-only logs (append-only within a log; a rebuild or rollback
  starts a new log with new cursors), with predecessor chaining after the first mount; (c) a cursor
  from a retired log refuses `snapshot_unknown`.
- `ask`'s containment card keeps the `point` verdict.
- The four operations with no data (`as_observed`, `knowable_on`, `concepts`, `transposition`) keep
  the typed transport failure `operation_not_served`, and the capability manifest will state, per
  operation, that it is not served and which data would serve it.
- The web hosting shape of ruling 3 is `Lex.V3.Api` serving the built live pages beside `/api/v3` and
  `/mcp`, with the security headers, rather than an ingress split.
- Exports (PRs #789 and #790): JSON, CSV and PDF are written from one model, so they cannot
  disagree. The PDF uses the standard fonts only (no embedded font), so a text holding a character
  outside WinAnsi, other than a typographic space or hyphen, is refused as PDF with the characters
  named rather than set with a substitute; the PDF is not tagged, so it does not mark each text's
  statute language (the JSON carries the language per item). The export's time is
  the envelope's `context.freshness.observed_at`, the snapshot's observation, labelled as such,
  because the envelope carries no time of answering. The CSV is UTF-8 without a byte-order mark,
  exactly what the tests parse.

- Data acquisitions: the owner's 2026-09-30 data-lane instruction authorises the bounded first
  mount, complete EU and Luxembourg populations, and French EU bodies. Production signing,
  deployment and promotion remain outside that authorisation.

## Luxembourg population partitioning (2026-09-30)

The whole-population acquisition now drives adaptive covers for S, A and G when the existing
executor reports `PartitionRequired` at its 1,000,000-row delivery ceiling. It splits six-part
cursor ranges, retains empty leaves, and runs all leaves of a family in one session and under the
same wire budget. The adapter reconciles each cover and independently reopens every leaf before
scope reduction, body acquisition or corpus construction. Explicit act ranges keep their current
path. This adds no publisher traffic by itself. The bounded mount completed; full-population
acquisition follows the reviewed selector and adaptive-cover changes.
Adaptive covers use midpoint boundaries and the existing executor's delivery ceiling. The query
plan still records a legacy 900-row accumulated-slice rule that this executor does not apply;
its renderer bytes were unchanged in that slice. PR #798 subsequently added compact COUNTs and
splitting for narrowly recognized, retained initial COUNT capacity errors. The live bounded S
cover proved 12 leaves and reopened all 10 rows using 68 wire requests. This proves the bounded
range only. Fresh whole-population preflight2 used its 100-request ceiling on S splitting and
refused before A/G; it did not prove the population. Review repair stops at the first unprovable
leaf and records later leaves as not attempted, with no further requests for that cover.
Separate COUNT diagnostics have measured 5,366,756 assertion rows in 22 ranges. The remaining
range is `[http://data.legilux.public.lu/eli/etat/a, http://data.legilux.public.lu/eli/etat/b)`. These independent observations size a future run; they are not a same-instant
population proof. The full run must also allow for retained pages, decoded rows and derived files.
Validation: solution build with zero warnings/errors; fast lane 3,065 pass / 1 Windows skip;
153 initial affected ingest tests pass, including a two-work corpus through split S/A/G families.
The one review repair updates all four construction census pins and adds the stop-after-failure
regression. Full unfiltered ingest: 1,964 pass, 19 opt-in skips, zero failures (1,983 total).
Repair fast lane: 3,065 pass / 1 Windows skip; repair build: zero warnings/errors.

## Waiting on others

- The bounded real mount is available at
  `C:\lex-v3\first-mount-decision95-restart-20260930\v3-corpus` for the web lane's mounted-corpus
  evaluation and journeys. The data lane continues the full EU and Luxembourg populations.

## For the weekly checkpoint

- The French and English refusal sentences, one short list (ruling 4): PR #793. The 20 sentences the
  live pages say by refusal code, each page's two sentences for a refusal named only by its code,
  and the two hints a card that cannot be shown still carries (the date the history begins, the
  nearest article ids; review of #793), English as served and French as the driver's draft. Printed from the pages' own sentences by
  `node web/scripts/refusal-sentences.mjs`; a test holds every served sentence to one draft. Nothing
  French ships until the owner's reviewed wording replaces the drafts.
- The live pages' French interface copy, drafted for review (Decision 41: French chrome ships only
  once reviewed): every entry of the chrome table so far (each page's title, eyebrow, heading and
  introduction, the forms' labels and buttons), printed by `node web/scripts/live-chrome-fr-draft.mjs`
  (PR #800). Since then it has grown to every sentence of the live screens (PRs #803, #804, #806),
  the evaluation card (PR #807), the refusal card (PR #809) and Trust and Coverage (PR #810): the
  whole of the live pages' interface, as the chrome scan holds it. That includes the absence note
  ("This is what this service holds, and does not hold. It is not evidence that the instrument or the
  law does not exist.") and the live banner, both claims the owner reviews.

## Blocked on the owner

Only money, legal or public claims, credentials and going live (ruling 7):
- Azure production credentials and the signing identity, needed by week 5.
- The weekly 30-minute checkpoint slot.

## Housekeeping done 2026-09-27 (owner's instruction)

- 219 registered worktrees with clean trees removed, 5 stale registrations pruned, 332 local
  branches already merged into `v3/integration` deleted. `C:\lex-v3\scratch` (probe outputs whose
  useful bytes are checked-in fixtures; it also held 17 registered probe worktrees with uncommitted
  files from 2026-08-28 to 2026-09) and the retired seat-protocol droppings under `C:\lex-v3`
  deleted. Free space went from 0.2 GB to 10.0 GB. Kept: the main checkout, this worktree, and 18
  worktrees with uncommitted changes outside scratch (`git -C C:\lex worktree list`). Not touched:
  `C:\lex-v3\eu-population-run-1..9` (6.0 GB; runs 1 to 8 are 5.3 GB of partial runs superseded
  by run 9) and the other run and evidence directories under `C:\lex-v3`.

## Known defects carried

- `V3PlatformSchemaTests` renders the tracked schemas and then verifies them when
  `V3_RENDER_PLATFORM_SCHEMAS=1`, so a render passes unconditionally. CI never sets it. Fix: fail after
  rendering.
- The corrigendum tripwire classifies a French corrigendum as `within_served_body_languages` while no
  French body is served (Decision 89 section 4). True once the French expressions land.
