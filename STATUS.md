# Lex V3 status

Updated 2026-09-30 by the driver. This file replaces the issue-comment ledgers. It is rewritten in
every pull request that changes what is served, what is next or what is blocked.

## Heads

- `v3/integration`: `34dac1ac` (2026-09-30, PR #770 merged). Build 45 s. Fast lane
  (`eng/test-fast.ps1`): 3,038 tests, 3,037 pass, 1 skipped. Ingest suite: green on CI for PR #760
  (the CI `dotnet` job runs the whole solution on every pull request, about 7 min on the runner;
  green for PR #770);
  locally about 15 min. 812 web tests pass. CI's web job can flake in `keyboard-walk.test.mjs`
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
`verify` need an EU permalink grammar (owner questions below). EU `dossier` is the next slice that
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
forbids production source naming it) and is an owner question below. The two "not held" sentences
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
contacted. The same card over MCP (`tools/call ask`). The envelope verdict is `point` for now and is
an owner question below (review of PR #759: the catalogue's POINT delivers an instrument, an
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

Web: 29 React components, 812 tests. Preview screens render fixtures. The V3 Luxembourg search
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
search screen replaces it. **The first browser journey
step passes (PR #766, run locally 2026-09-30):** `node scripts/journey.mjs --api <Lex.V3.Api build
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
the live banner's wording is a driver draft before the owner's), hydrated by its own bundle
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
has no release, image or snapshot identity (item 7) and no signature. Where it is published is an
owner question below.

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
- **EUR-Lex legal notice (Decision 88): producer built (PR #746), fixtures only.** `EuLegalNoticeRouteProducer`
  issues the one GET through the acquisition session under a new source profile
  (`european_union_legal_notice`) and hands back the route under the corpus run identity, which is
  what the Stage 3 envelope checks. Live facts observed 2026-09-27 and pinned in the profile:
  `GET https://eur-lex.europa.eu/robots.txt` answers 200 directly, with no `Content-Type` header
  and `Crawl-delay: 10`; the notice path is not disallowed. Unknown until the bounded live run:
  whether the notice page itself answers 200 `text/html` to the `Lex/0.1` user agent behind the
  EUR-Lex WAF (Decision 23). A challenge page, a block, an off-origin redirect and a truncated body
  are all the typed refusal `notice_route_invalid` (`EuLegalNoticeEvidence.FromRoute` now requires
  a complete route; review finding on this pull request).
- Decision taken by the driver, reversible: the legal-notice route follows same-origin redirects
  (as `FromRoute` already admitted and as the Cellar route does); the terminal is pinned to the
  host and port and robots is evaluated for the redirected path. Decision 88's "one GET" is read
  as one logical request whose hops count against the wire budget. Say the word and the profile
  refuses redirects instead.
- The session now exposes the custody write receipt of every hop an executed attempt sealed
  (`HopWriteReceiptsByObservationId`), which the Formex ZIP binding needs as well.
- **Formex package population: enumeration (PR #747) and acquisition (this pull request), fixtures
  only.** `EuFormexPackagePopulationProducer` runs the real manifestation enumeration for every
  expression of a complete run (all languages, one robots session and four requests each), closes
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
  `not_acquired` with a reason (`body_not_held`: the run holds no body for the expression, today
  every language but English, Decision 89, and the corpus binds every Formex outcome to one held
  body; `language_not_addressable`, `manifestation_not_singular`, `identity_not_admitted`; and the
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
- **What this means.** Decision 88's one legal-notice GET cannot succeed from an automated client
  while EUR-Lex challenges it, and `LexCorpus6Builder` refuses the whole corpus without that
  evidence (`EuropeRightsEvidenceMissing`), so no mount, Luxembourg included, can be built. Two
  fixes, neither built yet:
  1. Fail fast (driver's call, no scope change): send the legal-notice GET first, before the EU
     acquisition, so a challenge costs one request instead of the whole EU acquisition. Not a
     reorder: the notice route binds the corpus run identity, which the adapter mints during the
     EU run, so the identity has to exist before the adapter runs (a preflight GET would be a
     second notice request, against Decision 88's one). Under option (a) below a refused notice no
     longer stops the build, which lowers this fix's value.
  2. A way past the missing notice (owner's call, it touches Decision 88): either (a) the builder
     accepts a typed `eu_rights_evidence_unavailable` disposition and withholds every EU body as
     text while still serving EU identity and `resolve`, which keeps rights fail-closed and lets the
     Luxembourg side mount; or (b) the rights evidence comes from the same Commission reuse policy
     (Decision 2011/833/EU) at an address that does not challenge, such as the Publications Office
     legal notice, which needs a numbered Decision replacing Decision 88's exact URL. The driver
     recommends (a) now and (b) later. Sending a browser user agent or solving the challenge is not
     an option: it would evade the publisher's protection.

## Next, in order

1. Unblock the first mount after the EUR-Lex challenge (see Data, attempt 2): option (a) or (b) as
   the owner rules, and fail fast if the notice stays mandatory (it needs the run identity before
   the adapter runs; the 619 spent against the requests custody holds is reconciled there). Then the bounded live run again with
   the tool: one EU work in EN and
   FR (the manifestation enumerations and the legal-notice GET; no Formex package request), one
   Luxembourg act with a consolidated publisher PDF. Record the wire counts, the refusals met and
   the five digests in STATUS.md; mount the directory under the API and answer `resolve` from it.
   Then decide whether the one-process design carries the full population or needs a
   serialisation boundary between acquisition and build.
2. Formex, the rest: French bodies (Decision 89) so French packages are held and acquired; then
   every acquired main body feeds the EU index for the temporal and search operations (item 5).
   Designed on 2026-09-28 (read-only survey, recorded in the driver's notes): the adapter mints one
   English fetch ladder per work and the corpus record set holds exactly one body per observed
   object (the work), so a held French body means a second fetch pass with `Accept-Language: fra`
   and a second held record per work keyed by the French expression, which touches the record set
   and its completion checks, the content-class binding, the annex binder's lineage, the builder,
   the index and `resolve` (a work identifier or CELEX then matches two equally authentic
   expressions and today's `ambiguous_identifier` needs the language rule Decision 89 lists for
   Stage 4). Two questions for the owner before the driver starts it: (a) the frozen corpus record
   schema `lex-v3-source-corpus-record/6` holds one body per object; may it change (a body per
   language, or an expression-keyed record) or must the French body travel beside it; (b) the
   `resolve` language rule for a work identifier (the driver would default to the English
   expression with the French one named as an alternate, and a `language` parameter selecting
   either). The driver proceeds with item 4 meanwhile.
3. Define and run the Luxembourg population and the complete EU population (owner authorisation per
   run).
4. Serve the unserved operations for Luxembourg (`verify` and `relations` served by PR #753, MCP
   over streamable HTTP by PR #754, `evidence_bundle` by PR #755, `classification` and
   `manifestation` by PR #756, `status_on` and `browse` by PR #757; a request to an unserved
   operation answers the transport failure `operation_not_served`, PR #758; `ask` answers the
   contained `assistant_v3_unavailable` card, PR #759; `events` and `answer_drift` over a genesis
   log, PR #760). Four remain (`as_observed`, `knowable_on`, `concepts`, `transposition`), all
   needing data the ingest does not produce; owner question below. The event log's next step,
   predecessor chaining with observation times, needs a second build, so it follows the first mount.
5. EU parity: every temporal and search operation from the EU index; French expressions. EU
   `search` in one work served by PR #761, EU `dossier` by PR #762; the temporal operations,
   `provenance`, `evidence_bundle` and `verify` wait on consolidation acquisition, an EU permalink
   grammar and the owner questions below.
6. Wire the eight launch screens to `/api/v3`; journeys J1 to J8 in a real browser. PR #763: the
   envelope reader and the client module; PR #764: the live Trust and Coverage component; PR #765:
   its page, the live build and the one-origin server; PR #766: the first browser journey step,
   passing against the real API with and without a mount; PR #771: the V3 search reader
   `readSearch`, held to five search answers the census now samples. Next: the live search
   screen (a component, its page and a journey step over `readSearch`'s view). Then dossier and
   reading, whose readers still read pre-V3 shapes and must first be held to served samples. Also
   J1 to J8 restated as V3 steps (owner question): they exist only in the pre-V3 pack
   (`05-user-journeys.md`).
7. Release pipeline: build, sign, image, zero-traffic deploy, probes. Then acceptance and promotion.
8. Machine gates (launch contract, Evaluation): the temporal, refusal and retrieval case sets run
   against the real handler, and all three shuffled controls are caught (PRs #767 and #768). The
   evaluation card is rendered from them with the statistical rows `not_yet_labelled` (PR #769);
   publishing it waits on the owner. Replay G1 to G5 (`33-product-spec.md`): G2 snapshot
   determinism and G5 independent verifiability run on the real handler (PR #770). G1 version
   immutability, G3 bitemporal completeness and G4 as-observed answering need predecessor
   chaining with observation times, so they follow the first mount and the event-log ruling. What
   is left of the launch contract's machine-gates line after that is "V2 absent from the image",
   which belongs to the release pipeline (item 7).

## Blocked or waiting on the owner

- Evaluation card (PR #769): the launch contract says "published". Section 6 of
  `36-ideal-evaluation.md` names signed JSON at a stable route and an HTML rendering on the Trust
  surface. Both are public claims. (a) Should the card be served by the API (a new operation or a
  static route) and shown on the Trust and Coverage page, or published beside the release assets
  only? (b) May a launch card carry fixture-only machine gates? The alternative is to wait for the
  first real mount, so that the gates also run over the mounted corpus. (c) Its signature comes
  from the release pipeline (item 7). Until then the card is an unsigned census in the repository.

- Web hosting (PR #763): the page's CSP allows `connect-src 'self'` and the API has no CORS, so the
  page and the API share an origin. Either the API serves the web bundle, or an ingress routes
  `/api/v3` and `/mcp` to the API and the rest to static files; which layer sends
  `frame-ancestors`, HSTS and `Referrer-Policy`; what a live page's banner says (the synthetic
  banner would be false on a real mount); whether J1 to J8 are restated as V3 steps; who reviews one
  refusal sentence per code in FR and EN.

- EU parity (PR #761): (a) is the Formex act date (`wording_date`) the "EU wording-state date" the
  launch contract means, and does `official_consolidation_state` fit answers over an original
  wording; (b) may the original wording answer EU `as_of` for dates after it when no consolidation
  is held (the driver's default is no), and if not, is consolidation acquisition in scope for
  launch, and which of the twenty codes refuses meanwhile (none says "a later wording may exist and
  is not held"); (c) search across all EU works needs a `jurisdiction` request parameter (a reviewed
  schema change) or stays scoped to one work at launch; (d) under the legal-notice option (a), may
  search match over withheld EU bodies, given that hits carry no text.

- The event log (PR #760): (a) is the mintable registry twelve names, the coverage event never
  minted under B42 finding 5.2, or should the scope-line gate be amended to admit a name that is
  never emitted; (b) may launch ship genesis-only logs (append-only within one log; a rebuild or a
  rollback starts a new log whose cursors are new), or must predecessor chaining land first; (c) a
  cursor from a retired log refuses `snapshot_unknown` (the driver's default) rather than restarting
  from seq 1 silently.

- The EUR-Lex legal notice answered the tool's one Decision 88 GET with an empty HTTP 202, so no
  corpus can be built (Data, attempt 2): (a) the builder accepts a typed
  `eu_rights_evidence_unavailable` disposition, withholds every EU body as text and lets the
  Luxembourg side mount, or (b) a numbered Decision names an address that does not challenge (the
  Publications Office legal notice, same Commission reuse policy). The driver recommends (a) now and
  (b) later, and builds neither before the owner's word.
- The verdict of `ask`'s containment card: keep `point` (the closed set's only verdict that sends the
  reader elsewhere, though the catalogue's POINT also names an instrument, a link and a human counter
  the card does not have, and the scope line requires REFUSE of question 2), or rule a presentation
  verdict as Decision 63(a) did for `localization_unavailable` (a seventh verdict, an envelope schema
  change). The driver's default is `point` until the owner rules.

- Four registered operations with no data (`as_observed`, `knowable_on`, `concepts`,
  `transposition`): the launch contract wants each "served or refusing with a typed reason its
  capability manifest states". None of the twenty refusal codes means "this mount does not produce
  that data" (the nearest, `snapshot_unknown`, `retrieval_mode_unavailable`, `not_transposable`,
  each says something else). Options: (a) keep the typed transport failure `operation_not_served`
  and have the capability manifest state, per operation, that it is not served and which data would
  serve it; (b) a twenty-first refusal code, which is a versioned registry change and needs its own
  ruling on Decision 91's "stays at its twenty"; (c) build the data (`transposition` from the
  Legilux `jolux:transposes` relations is the nearest). The driver's default is (a); nothing is built
  for it before the owner answers.
- Codex role: weekly cold read plus the two release gates, or none.
- Run authorisation per run: Luxembourg population; French EU expressions; the complete Stage 7 run.
  The bounded first-mount run is authorised.
- Azure production credentials and the signing identity, needed by week 5.
- Weekly 30-minute checkpoint slot.

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
