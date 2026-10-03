# Lex V3 status: the web lane

Updated 2026-10-01. The web lane's progress, split out of STATUS.md (which keeps the heads, the owner's open
items and the pointers) by the standing order of 2026-10-01 13:50 UTC. Every pull request of the web lane
updates this file, not STATUS.md.

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
`lex-v3-luxembourg-index/6`, `user_version` 6, the fixed-input byte pin moved; `/7` since PR #864, below). A build is one
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
evidence, and every revision event. The web lane claimed predecessor chaining on 2026-10-01
(PR #862, the web lane's order under "Next, in order").

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

Registered and not served: `concepts`, `knowable_on`, `transposition` (`as_observed` is served by
build snapshot since PR #874). Since PR
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

Web: 41 React components, 980 tests. Preview screens render fixtures. The V3 Luxembourg search
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

An EU search answer (`publisher: "eu-eurlex"`) has another shape and is refused by this reader;
since PR #853 `readEuropeSearch` reads it, beside this one.
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

An EU dossier (`publisher: "eu-eurlex"`) has another shape and is refused by this reader; since PR
#856 `readEuropeDossier` reads it, beside this one. The fixture holds one
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
- An EU work's dossier is answered in another shape. Until PR #856 it was said as not shown on this
  screen; it is now laid out in its own words (the EU dossier paragraph below).
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

A reviewed interface language builds its own live pages (PR #813), so French can ship the day its
table is reviewed, with no other change.
- `live-chrome.mjs` knows the language its bundle was built for (`CHROME_LOCALE`, from esbuild's
  `define` of `__LEX_CHROME_LOCALE__`, English otherwise). `liveChrome()`, the plural rule and
  `livePath` default to it.
- A live page is labelled in that language (`Document`'s `locale` and `copyLocale`), and loads its
  script from that language's path.
- `build-live.mjs` builds English at the root as before, and every other reviewed language under its
  own path (`/fr/`), each page and its hydrating script compiled together for it. The locale
  navigation links a reviewed language to its home by file (`/fr/index.html`): both servers map only
  the root to an index page, and the review of #813 found `/fr/` was a 404.
- None but English is reviewed, so the product build is unchanged: its HTML pages are byte-identical,
  and its scripts differ only by the locale code they now carry. The journey passes with them, plain
  and served.
- `live-locale-build.test.mjs` builds a stand-in second language from the French draft, which the
  product never imports or serves. It holds:
  - every page and script under `/fr/`, labelled `fr`, saying the table's words and loading its own
    script, and each served by the live server, the language's home included;
  - the English pages byte-identical whether or not a second language is built;
  - no `fr/` directory from a product build.

  Its builds are tagged (`buildTag`), so their intermediate bundles never collide with another test
  file's build.
- To ship French after the owner's review: turn the draft into the reviewed table beside English in
  `LIVE_CHROME`, and add `fr` to `REVIEWED_CHROME_LOCALES`.

The eight live screens against the real bounded mount (PR #815). The data lane's first mount
(`C:\lex-v3\first-mount-decision95-restart-20260930\v3-corpus`, built twice and equal) is the
first real build the pages have met; until now every journey ran on the test fixture's mount.
- `journey.mjs --real-mount` runs the eight steps against a real build. Its contents are not known in
  advance, so each step asks the API its page's own request first (`expectedFromEnvelope`). The page
  is then held to that answer (a success, or that refusal by its code) and to every invariant a run
  checks: requests, headers, recording, citations, quotes, live regions, colour and hydration. The
  coverage page must name the corpus and Luxembourg index by the digests the mount's
  `build-report.json` records.
- On this mount the Luxembourg side holds no state (10 members: 1 acquired, 9 unavailable), and the
  EU index holds 99 searchable English articles, which no live screen asks yet.
  - Trust and Coverage and the radar answer; the other six refuse (`language_not_available`,
    `identifier_unknown`) and show their cards.
  - Every web reader accepted the real build's answers.
  - All eight runs pass, and all eight with `--served-by-api`.
- The first run found a flaw in the citation rule of #805: the radar's empty window answered with
  nothing to cite, and the rule required one citation. Such an answer is now excused when the API's
  own answer holds nothing to cite (no search hit, no radar row) and the page says so. The page's
  word alone excuses nothing: the review of #815 showed a page could hide a hit behind "no hits".
  Now a page that says it is empty while the API's answer holds something fails, and every other
  answer of a citing step must still cite.

The release pipeline's image steps, rehearsed with no production credential (PR #821; item 7, which
the owner's proxy gave the web lane on 2026-09-30 while the data lane works on the populations).
`node web/scripts/image-rehearsal.mjs --mount <v3-corpus>` is one command that:
- builds the live pages;
- builds the one-server OCI image without a container daemon (`dotnet publish -t:PublishContainer`),
  with the API, the live pages under `v3-web` and the mount under `v3-corpus`, on the base image
  `Lex.V3.Api.csproj` pins by digest (chiseled Ubuntu, non-root user 1654). `LexImageWebRoot` and
  `LexImageMount` are inert unless a build names them;
- verifies what the image holds:
  - every blob named by the digest and size it carries;
  - the API, every live page file and every mount file in the app layer, byte for byte;
  - each file of the mount's build report at its recorded digest;
  - linux/amd64, a non-root user, the API as the entrypoint, and the base named by digest;
- signs the manifest digest with a rehearsal identity (an ECDSA P-256 key made for the run and never
  kept, over a container-signature payload that says it is a rehearsal) and verifies the signature,
  the digest and that label;
- removes the archive, its work directory and the publish directory, and records that they are gone.

On the real bounded mount: a 61 MB image of 6 layers (manifest
`sha256:1029adee894c12831e1abc2d5668af02f8ab4a85a13eb8d9d76159c05ecf7a0d`), 25 live page files and
6 mount files verified, the build report's 5 digests matched, the signature verified, everything
removed. `image-rehearsal.test.mjs` holds each check to fail on the image it must refuse:
- a layer whose bytes changed, a missing blob, and two manifests;
- a live page left out, a mount file that is not the one built from, and a digest that is not the
  build report's;
- a root user, another entrypoint, and a base not named by digest;
- another image's digest, a payload changed after signing, and another key.

Running the image and probing it (health, API, browser, privacy, security headers) is the next slice.

The image run and its zero-traffic probes (PR #825).
- The rehearsal's one command now runs the image before removing it, on this machine's WSL Ubuntu,
  with no daemon and nothing installed (`image-run.mjs`):
  - the layers are unpacked in the manifest's order into a root filesystem, whiteouts applied;
  - the API starts as the image says (user 1654, its environment, working directory and entrypoint);
  - it runs in a private mount namespace, its root filesystem read-only and `/tmp` a private tmpfs,
    as a hardened deployment runs it. `/proc` is read-only, and `/dev` is a read-only tmpfs holding
    only `null`, `zero`, `random` and `urandom`. Binding the host's whole `/dev` gave the container a
    writable `/dev/shm` the watcher did not see (review of #825). Before the API starts, the mount
    table under the root is checked: any writable mount but the watched `/tmp` and those devices
    refuses the start.
- The eight live screens are then probed against the image, served by the image itself, through the
  journey's real-mount steps (`journey.mjs` `run` now takes a server; the host API and the image are
  two). The probes:
  - health: it answers;
  - API: each page's request, answered from the mount it carries;
  - browser: every screen, hydration, live regions and colour;
  - security headers: CSP with `frame-ancestors 'none'`, HSTS, `Referrer-Policy: no-referrer`,
    `nosniff`;
  - privacy: nothing written after the first answer, on its output or its `/tmp`, which an inotify
    watcher in the same namespace records, so a file written and deleted counts. Nothing can be
    written outside `/tmp`: the start proves the root filesystem refuses a write.
- On the real bounded mount all eight probes pass (Trust and Coverage and the radar answer; six
  screens refuse and show their cards). At startup the API wrote 11 events on `/tmp`, none after its
  first answer. The container's root filesystem is removed with the rest.
- A deployment requirement it found: the image needs a writable, private `/tmp`. Mounting a corpus
  verifies its index into a private temporary file (`LuxembourgIndexBuilder`, deleted on dispose). On
  a fully read-only filesystem the mount is refused and every answer is `no_corpus_mounted`.
- Not yet reproducible then: two builds of the same sources and mount gave different manifest
  digests. PR #826 makes the image reproducible (below).

The reproducible image (PR #826).
- Why two builds differed: every file in them was byte for byte the same, but the .NET SDK stamps
  the config's `created`, the app layer's history entry and every file's modification time with the
  time it ran, and names each pax header after its own process id.
- `image-reproducible.mjs` rewrites only what the SDK stamped:
  - the app layer is written again from the same entries (path, kind, mode, owner, bytes), sorted by
    path, each at the source date, with no pax header unless a path needs one, and uncompressed;
  - the config's `created` and the app layer's history entry take the source date;
  - the manifest and index name the new blobs; the base layers are kept byte for byte.
  The source date is `SOURCE_DATE_EPOCH`, else the commit's time, and the report records the commit
  and whether the tree was clean. The repack refuses an entry that is neither a file nor a directory,
  a layer that does not match its diff id, and a history that does not map one entry to each layer.
- Each build restores and compiles every project afresh in `artifacts/image-rehearsal` inside the
  checkout, emptied before and after, with source paths mapped (`ContinuousIntegrationBuild`). The
  first layout gave each build its own directory outside the checkout, and the rehearsal's own check
  refused it: 8 assemblies and symbol files differed, because the compiler writes the paths of its
  generated sources and symbol files into each assembly and maps only paths under the checkout.
- The rehearsal builds the image a second time from scratch and requires the same manifest digest.
  `reproductionFailures` names what differs, down to the files; `--no-reproduce` skips the check.
- On the real bounded mount, at a clean commit: manifest
  `sha256:600bbf106510837f779fe86fb2c7a6ce4595979a80dd568641bc5c4fe0c3766c`, reproduced by the second
  build. A second run on the same commit gave the same digest: four builds, one image. The checks
  passed (25 live page files, 6 mount files, the report's 5 digests) and the signature verified. All
  8 probes pass against the reproducible image, and the work directory, the artifacts directory and
  the container were removed.

The image probed on the journey's fixture mount (PR #828). On the real bounded mount six of the
eight screens refuse, because Luxembourg holds no state there. The answers themselves (hits, quotes,
citations) had been probed only against the host API, never against the image.
- The rehearsal takes the fixture mount `V3JourneyMountTests` writes (`journey-mount.json`) as well
  as a real mount. `mountReport` reads either:
  - the fixture's Luxembourg index is held to the file digest it names;
  - its corpus digest is a snapshot digest (what the coverage answer shows), not the file's bytes, so
    the corpus file is held byte for byte to the mount, and its snapshot digest to the coverage probe.
- On a fixture mount the probes are the journey's fixture steps (`fixtureMountRuns`, the expectations
  `journey.mjs` now keeps in one list, `fixtureMountExpectations`): every screen must answer with the
  texts the fixture's one work gives it, and every citation must verify.
- Against the image: all eight screens answer, and 105 citations verify through the image's own
  `verify`, each `digest_matches`: reading 50, export 50, and one each for search, dossier, history,
  compare and radar. The image was reproduced by its second build
  (`sha256:441853466bf9945f7f61db5ea0ae86f97aecf11c6dc26e8f8c5de215aca3b044` at that commit), and
  everything was removed.
- A mutation: a fixture manifest naming another corpus digest fails the coverage probe ("the page
  does not show the mounted digest").

V2 absent from the image (PR #831), the launch contract's last machine-gates item that is the
release pipeline's. V2 is the retired product on `main` (`Lex.Ask` to `Lex.Web`).
- Before signing, the rehearsal checks that V2 is absent (`v2Failures`):
  - no layer holds a Lex assembly, symbol or documentation file that is not `Lex.V3.*`;
  - the API's dependency manifest (`Lex.V3.Api.deps.json`) names no Lex library that is not V3's, so
    a V3 assembly that referenced V2 would be caught;
  - a missing dependency manifest is itself a failure.
- With the probes, the running image is asked every route V2 served (`V2_ROUTES`, 63): each
  `MapGet` and `MapPost` of `Lex.Web` on `main`, the ten pages of its `/built` table, its diagrams,
  its publisher document routes, its `/mcp/{*rest}` fallback, its static files and its four assistant
  endpoints. Each must answer 404, except where V3 serves the same path from its live pages (`/`),
  which must answer V3's own file byte for byte. The first list left out `/built` and its table
  (review of #831).
- On the real bounded mount: 526 entries across the 6 layers read, no failure; all 63 routes asked,
  `/` answering V3's own page and every other 404; the 8 probes pass; the image reproduced by its
  second build; everything removed.

The release assets published, read back and verified, and the evaluation card at its stable route
(PR #833): the release path's first line without its production credential, ruling 2's card
beside the release assets, and 36 s6's card for machines at a stable route.
- The card is served as JSON at `/evaluation-card.json`, the same card the Trust and Coverage page
  renders, and the page links it ("The same card for machines, as JSON"; drafted in French). The
  live build writes it beside the pages, and the API's web root now serves `.json`
  (`application/json`). The probes fetch it from the running image and require the bytes the
  release carries and signs.
- Once everything else has held (build, verification, V2 absence, signature, reproduction, probes),
  the rehearsal publishes a release into a directory named by its version
  (`v3-rehearsal-<source date>-<commit>`), which is never overwritten (`release-assets.mjs`):
  - `lex-v3-image.oci.tar` (the image) and `lex-v3-image.sig.json` (its signature);
  - `evaluation-card.json`: the card the image serves at its route. The rehearsal reads the
    platform's card once, checks it by the page's rules, hands it to the live build and holds the
    served file to it;
  - `mount-report.json`: the mount's build report, or the fixture's own manifest;
  - `release-manifest.json`, naming the version, the source commit, the image's manifest digest, the
    corpus digest and each asset by size and SHA-256, and `release-manifest.sig.json`. One key for
    the run signs the image and the manifest.
- Reading back (`releaseFailures`) trusts nothing the directory says about itself:
  - the key is the caller's: a manifest or image signed by another key fails, even one re-signed
    after a change;
  - every asset is hashed again: nothing unlisted, nothing missing;
  - the image is read blob by blob and must be the digest the manifest names, and its signature must
    name that digest;
  - the card must be the very card the image serves (`app/v3-web/evaluation-card.json` in its app
    layer), and read by the page's rules;
  - the directory, the manifest's version and the source it signs must name each other.
  The review of #833 found the last two were checked alone. A release could carry a valid card the
  image does not serve, or a version naming another commit than its signed source.
- On the real bounded mount: release `v3-rehearsal-20260930T213833Z-23d939b6e63c`, six files, read
  back with no failure, after the image was reproduced, V2 found absent, the 8 probes passed and the
  image served the release's card at `/evaluation-card.json` (200, `application/json`, the same
  bytes).
  Tampering with a kept release was caught:
  - a card with a statistical row rewritten to `pass` failed its size, its hash and the card's rule
    (Decision 92);
  - one flipped image byte failed the asset's hash and the blob digest of layer 3.
- Not in this step: building the corpus from custody in the same command, which is the data lane's
  build (`Lex.V3.Tool build`), and publishing anywhere but a local directory, which needs the owner's
  release identity and storage.

The credential-free deployment kit (PR #890): the owner's go-live as one command, the panel's item 3.
Nothing here logs in, deploys or signs with a production identity; those stay with the owner.
- **`deploy/main.bicep`** (built to `deploy/main.json`) is the one-server container as an Azure
  Container App in an existing managed environment:
  - the release image by digest, pulled with the user-assigned managed identity the owner names, with
    no secret and no registry password;
  - one replica, with a private writable `/tmp` (an EmptyDir volume: the image run's finding that
    mounting a corpus writes its index copy there);
  - liveness on `/`, readiness on `/evaluation-card.json`, port 8080, HTTPS only;
  - every revision kept (multiple revision mode):
    - a new revision arrives as the `candidate` label with no traffic, while `liveRevision` keeps 100
      per cent;
    - on a first deployment there is no live revision, so the candidate takes the traffic and ingress
      admits only the owner's probe address (`probeSourceCidr`) until promotion.
- **`deploy/deploy.ps1`** is the one command.
  - Without `-Apply` it only plans: it verifies the release and prints every command, changing nothing.
  - With `-Apply`, in order:
    1. it verifies the release under the signing identity's public key, and stops if it does not
       verify;
    2. it copies the image out of the release by its manifest digest with `oras`, and checks the
       pushed digest. The short-lived registry token goes from `az acr login --expose-token` on
       standard input to `oras login`, into a registry config in a fresh private directory, which
       `oras cp` reads and which is removed when the copy ends, whatever happens. It is never on a
       command line or printed. `oras cp` takes no password on stdin (review of #890); the custody
       probe's runbook did the same;
    3. it runs `az deployment group what-if`, then `create`, by digest;
    4. it probes the candidate's own URL;
    5. if the probe fails, it deactivates the candidate (the removal step) and exits non-zero.
  - Promotion is printed for the owner and never run: a traffic move, or on a first deployment lifting
    the probe-only restriction.
  - `-Remove -Revision <name>` is the removal step on its own.
- **`web/scripts/deploy-probe.mjs`** is the zero-traffic probe. First it reads the release back
  under the signing identity's key (`releaseFailures`). Then, against the revision's URL:
  - `/` must be the release image's own page byte for byte, with CSP `frame-ancestors 'none'`, HSTS,
    `Referrer-Policy: no-referrer` and `nosniff`;
  - the card at `/evaluation-card.json` must be the release's, byte for byte, as JSON;
  - `coverage` must answer from the release's corpus;
  - every V2 route must answer 404.
  - Without `--origin` it only reads the release back, which `deploy.ps1` does before touching Azure.
- **`deploy/validate.ps1`** checks offline that `main.json` is `main.bicep`'s build, that `bicep
  lint` is clean, and that `deploy.ps1` parses. The static rules run in CI in
  `web/test/deploy-kit.test.mjs`:
  - no secure parameter, secret, key listing or registry password;
  - the identity pull, one server, `/tmp`, and the zero-traffic candidate;
  - the script never logs in, never puts a token or password on a command line, keeps the registry
    token only in the private session config it removes, and never promotes;
  - the release is verified before any Azure command, and the image is deployed by digest.
- **Evidence:**
  - the probe passes a stand-in revision that answers as its release holds, and fails nine ways a
    revision can differ, each with its own reason;
  - it is never run against a release under another key;
  - the probe command's exit codes (0, 1, 2) are tested as a process. The kit's first plan run found
    the command guard running nothing and exiting 0, which would have let an unverified release
    deploy;
  - a plan run of `deploy.ps1` against a published release printed the verified release, the digest
    copy, what-if and create by digest, and the probe. Under another key it now stops at
    verification;
  - mutations fail their checks: a promotion that runs fails the static rules, and an edited
    `main.json` fails validation.
- **Runbook, for the owner:**
  1. Provide what the kit does not create:
     - the managed environment;
     - the registry;
     - a user-assigned identity with AcrPull on it;
     - the production signing identity, and a release signed by it in the rehearsal's release format.
  2. `az login` in your own session, then `pwsh -File deploy/validate.ps1` (it needs the Bicep CLI;
     no Azure is touched).
  3. Plan: `pwsh -File deploy/deploy.ps1 -Subscription <id> -ResourceGroup <rg> -EnvironmentId <id>
     -Registry <name>.azurecr.io -IdentityResourceId <id> -SigningPublicKey <identity's public key,
     PEM> -Release <release directory>`, then either `-LiveRevision <the revision serving now>`, or on
     a first deployment `-ProbeSourceCidr <your address>/32`.
  4. The same command with `-Apply` deploys the candidate and probes it. On "answers as the release
     holds", promote with the command it prints. On a failed probe the candidate is already
     deactivated.
  5. Remove a revision at any time with `pwsh -File deploy/deploy.ps1 -Subscription <id>
     -ResourceGroup <rg> -Remove -Revision <name>`.
- **Not covered:**
  - the production signing identity and its release format (the owner's);
  - a custom domain, DNS and monitoring;
  - an Azure-side check of the template, since `what-if` needs the owner's session.

The deployment kit's browser probes and rollback (kit 2, PR #902, first opened as #901), toward the launch contract's lines "zero-traffic
deploy with health, API, browser, privacy and security probes" and "rollback and forward again".
- **`deploy-probe.mjs --browser`** runs the journey's real-mount steps (`realMountRuns`, the same ten
  steps the image run and the real-mount journeys run) through Chrome or Edge against the candidate's
  own URL, after the HTTP probes pass:
  - the release's mount report stands as the build report the steps read, from a temporary directory
    removed when they end;
  - each page is held to what the API answers its request, its hydration, console, paint and security
    headers, with every citation verified;
  - the revision is reached through a remote stand-in (`remoteRevision`) that exposes no process.
- **Privacy is stated, not passed.** A deployed revision's process output and files are not the probe's
  to watch, so its privacy checks would read nothing and prove nothing. The probe prints
  `REMOTE_PRIVACY_NOTE` with every browser run instead: the image run's privacy probe (nothing written
  after the first answer, no query text, address or user agent recorded) held the same image digest.
- **`deploy.ps1`** passes `--browser` unless `-SkipBrowserProbe`, and says so when skipped. With
  `-LiveRevision` it prints, after promotion, the rollback to the live revision (kept, since every
  revision is kept) and forward again to the candidate: traffic moves, the owner's, printed and never
  run.
- **Evidence:**
  - against the API started locally on the bounded EN/FR canary mount
    (`C:/lex-v3/bounded-en-fr-canary-20260930-1/v3-corpus`) with the built live pages, the browser
    probes passed all ten steps: coverage, search, dossier, reading, compare, radar, export, history,
    EU search and EU dossier, with 0 failures;
  - the tests hold that a failing step is reported with its label, no local API is started, the
    release's mount report is what the steps read and is removed afterwards, the revision serves its
    own pages (so the security headers are held), and the remote stand-in's empty privacy record comes
    with the note;
  - the static rules hold the `--browser` default, the stated skip, and rollback and forward printed
    inside `Write-Host` only.
- **Runbook, added:** step 4 now also runs the browser probes, which need Chrome or Edge on the owner's
  machine (or `-SkipBrowserProbe`, which runs only the HTTP probes and says so). After promotion,
  roll back or go forward again with the commands the script prints.
- **Not covered:** a second revision, promotion, rollback and V2's retirement on Azure itself, which are
  the owner's go-live; privacy on the deployed revision beyond the image digest it shares with the image
  run.

The licence-blocked journey (PR #834), the launch contract's "one licence-blocked journey" for its
line "rights are enforced at compose time".
- `V3JourneyMountTests` also writes a licence-blocked mount when `V3_WRITE_LICENCE_BLOCKED_MOUNT`
  names a directory. It is the fixture mount with its member's rights recorded as
  `non_admitting_licence_scl`, and its `journey-mount.json` names that disposition and passages
  covering every article's whole body: windows of 40 characters every 20 (2,950), so any leaked run
  of 60 characters or more holds a whole window. The label and first paragraph marker are left off.
- `journey.mjs` runs the eight steps on it (`licenceBlockedRuns`, chosen when the mount names a
  rights disposition):
  - each page is held to what the API answers its request;
  - reading and export must refuse `text_withheld` (`mustRefuse`), whatever the API answered, and
    show it on the page's refusal card (the code read off `.refusal-card .refusal-code`). Every
    refusal step is now held to the card's own code, not to a mention of it anywhere on the page;
  - no page may show any passage or carry one in its markup (`absentTexts`, the page's text and its
    HTML, whitespace collapsed).
  The review of #834 found that the first version checked only each article's opening, and took any
  mention of `text_withheld` for the card.
- On the host API all eight pass, plain and with `--served-by-api`:
  - reading and export refuse `text_withheld`;
  - coverage, search, dossier, compare, radar and history answer, and none shows or carries a
    passage;
  - the citations on the answering pages verify.
- The check is not blind: run against the normal fixture mount, all 2,950 windows are found on the
  reading and export pages. The normal journey's 16 runs pass under the card-code rule.

The EU permalink grammar and EU `verify` (PR #850), the first slice of the EU half of the launch
screens. Until now EU hits cited `expressionIRI#lex-provision=NNN`, which pins nothing, so the live
screens refused EU answers by design.
- The grammar is in Luxembourg's family, with the language part of it from the start, so the French
  expressions (Decision 89) need no second grammar:
  `/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}#{provision}`. The stable
  coordinate is the permalink without its digest; the provision is the publisher's id, escaped.
- The wording digest is computed in the API from what the EU index holds, which stores none
  (`EuropeWordingDigestRule`). It is the SHA-256, under `lex-v3-eu-wording/1`, of the CELEX, the
  work and expression IRIs, the language, the wording date and every article identity in the
  publisher's order, each field length-prefixed. Each EU article identity is itself the SHA-256 of
  the article's text and tokens (`EuFormexMainBodyLegalContentProducer.IdentityOf`), so the digest
  pins the whole held wording. An expression without exactly one wording date gets no permalink.
- EU `search` answers carry `pinned_wording` (wording date, digest, permalink, the rule) and each
  hit its provision permalink.
- `verify` routes an EU permalink to the EU index:
  - `digest_matches` with the CELEX, work and expression, language, wording date, digest, stable
    coordinate and the provision coordinate EU `resolve` answers;
  - otherwise `pinned_digest_mismatch` naming the current digest, `anchor_not_in_version`,
    `language_not_available`, `identifier_unknown` (a work or wording date not held), or
    `no_corpus_mounted`.
  - The CELEX slot holds the work's CELEX and nothing else. The review of #850 found an article
    identity there resolved the same work and verified. Only expressions of the work whose CELEX is
    exactly the slot's are considered, and the wording's CELEX is checked again.
- On the GDPR fixture a hit's permalink verifies (also under the product's https origin), and the
  digest recomputed from the index by the stated rule equals the API's. Each refusal is held to
  its code.
- On the real bounded first mount, all 60 hits for "personal data" carry a permalink, and every one
  verifies as `digest_matches`.

The search screen reads EU answers (PR #853), the second slice of the EU half of the launch
screens. Until now the screen refused every EU answer as not Luxembourg's.
- The form gains an optional work identifier, sent as typed (as the dossier screen sends it), and
  only when the field is not blank. An EU work is searched by naming it: the platform serves no
  search across EU works. The next page stays within the work named.
- `readEuropeSearch` (in `search-answer.mjs`, beside the Luxembourg reader, with the lane, page and
  population rules both share) holds an EU answer to the rules it states about itself:
  - one work, one expression and one language, and no date (`requested_date` null, no ambiguous
    work);
  - every hit in the one wording the answer pins: the wording permalink
    `/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}` agrees with the wording date,
    the digest and the language asked, and each hit's permalink is it with the provision, escaped as
    the platform escapes it (RFC 3986);
  - the EU cursor `lane.article`, the population scope echoing the request, and one work with hits;
  - the hits' own work and expression IRIs are one work and one expression. The review of #853 found
    a hit of a second work read as a one-work result, because only the population's count was
    checked.
- An answer with hits and no pinned wording is refused rather than shown unpinned: a hit this page
  shows must pin its wording (driver decision, below).
- The screen says the pinned wording once above the hits, with the answer's own sentence on what the
  wording date is. Each hit gives its heading (marked in its language), the CELEX and the wording
  date, never "version" or an applicability date, and the printed permalink. What the search does
  not cover (later wordings, corrigenda not applied, other languages, no article text) is listed as
  the answer lists it.
- The censuses now hold EU search answers driven on the GDPR fixture mount:
  - the answer census has four (both lanes for "joint controllers", the first one-hit page, the page
    its cursor leads to, and no hit);
  - the envelope census has three (the answer, a language the work is not held in, and an EU
    identifier the index does not hold).
  The refusal sentences for `identifier_unknown` and `ambiguous_identifier` have French drafts.
- The captured answer from the real bounded first mount (60 hits for "personal data", three on the
  page) reads too.

The EU search journey step (PR #854), the third slice of the EU half of the launch screens, and
the panel's "journey on the real mount's GDPR".
- A ninth step, `EU_SEARCH_STEP`, is the search page asked for the GDPR by its CELEX. It types
  "personal data" and `32016R0679` and chooses English in the form's language select, since the held
  wording is English. The request body must be exactly the phrase, the language and the work.
- The verdict reads EU citations. `pinnedCitation` accepts the EU permalink grammar beside
  Luxembourg's. `verify` must answer for the EU index, name the very wording the citation pins
  (`wording_sha256`) and the provision it names, unescaped.
- The keyboard path picks from the closed select as a keyboard user does: Tab to it, then type the
  option's first letter. Then Tab on to the submit button and press Enter. The select counts among
  the fields Tab must reach.
- Where it runs:
  - the fixture runs add "eu search, with the fixture mount", which must show the refusal card
    `no_corpus_mounted` saying the EU index is the one missing;
  - `--real-mount` adds the step when the build report names an EU index (`realMountSteps`).
- On the real bounded first mount, the EU step ends in the answer with 61 citations: the wording's
  permalink and the 60 hits' permalinks, every one `digest_matches` by `verify`. That holds in both
  runs, by pointer and by keyboard (24 of 24 characters typed by key). The other eight steps behave
  as before on that mount.
- The fixture journey passes 17 of 17 steps, by pointer and by keyboard.

The EU dossier on the live screen (PR #856), the fourth slice of the EU half of the launch
screens. Until now the dossier screen said an EU dossier was not shown.
- The API: each expression of an EU `dossier` answer carries `pinned_wording` (wording date, digest,
  permalink), the same wording EU search pins. It is null when the expression holds no single
  wording date. The answer carries the `digest_rule`. The EU index is untouched.
- `readEuropeDossier` (in `dossier-answer.mjs`, beside the Luxembourg reader; `readDossierAnswer`
  dispatches on the publisher):
  - every expression is in a language the work is held in, and in the language asked when one was;
  - each is listed once, counted, and resolvable by its own IRI;
  - each is pinned by `/eu-eurlex/{celex}/{language}/{wording date}--{digest}`, whose date is its
    one wording date. An expression that pins no wording is refused rather than shown unpinned, as
    in EU search.
- The screen shows the CELEX, the work IRI, and a table of expressions: language, wording date
  (never "applies from"), articles held, permalink. It then gives the answer's sentence on what the
  wording date is, and what the dossier does not hold. The `not_shown` state is gone. The new
  refusal sentence for `ambiguous_identifier` (one CELEX naming two works) has a French draft.
- The censuses hold the EU dossier: one answer in the answer census and one envelope. The EU
  members' object references move per run, as the Luxembourg ones do, so they are normalised; the
  double-run test found them.
- The journey gains `EU_DOSSIER_STEP` (the dossier page, the GDPR by its CELEX):
  - on the real bounded first mount it answers, and its one citation (the expression's wording
    permalink) verifies, by pointer and by keyboard; all 10 real-mount steps pass in both modes;
  - on the fixture mount it shows `no_corpus_mounted` naming the EU index; the fixture journey
    passes 18 of 18 steps, by pointer and by keyboard.

The data that would serve each unserved operation (PR #857), the driver decision recorded for
STATUS item 4.
- `coverage` answers `operations.not_served_data`: one row per registered operation with no
  route, in the order of `not_served_operations`, each naming the data that would serve it
  (`V3CorpusMount.NotServedDataNeeded`). The data are the specification's own (`33-product-spec.md`):
  - `as_observed`: observation times (`observed_from`), recorded by builds chained to their
    predecessors in the event log;
  - `knowable_on`: each state's publication date beside its observation time, never the
    publisher's valid-from date;
  - `concepts`: EuroVoc descriptors, EU directory codes and subject matters;
  - `transposition`: Legilux's transposes and draftTransposes assertions and the Publications
    Office's national implementing measures for Luxembourg, each kept as its publisher asserts it.
  None of these is produced by the ingest.
- A coverage test holds the table to exactly the registered operations the mount does not serve.
  A newly served operation leaves it, and a newly unserved one cannot be listed without saying what
  it needs.
- The coverage reader requires one row per unrouted operation, in the same order, each with a
  sentence.
- Trust and Coverage shows the rows in a table, in both renderers, captioned "The data that would
  serve each operation with no route". The words have French drafts. The preview answers carry the
  platform's sentences to the character, and the census holds them.

The timeline speech test per publisher (PR #860), the evidence the launch contract names for its
promise "Luxembourg applicability dates and EU wording-state dates are never merged, in text or in
speech". Before the EU screens there was nothing EU on a page to merge. `web/test/date-speech.test.mjs`
holds the two date kinds apart in three places:
- **What the platform sends:** no EU answer in either census carries a Luxembourg state's date
  fields (`applicability_date`, `next_applicability_date`, `latest_applicability_date`,
  `history_begins`, `state_sha256`), and no Luxembourg answer carries an EU wording's
  (`wording_date`, `wording_dates`, `pinned_wording`).
- **What the interface table says**, in English and in the French draft:
  - the entries the EU views use carry no Luxembourg date word ("applies", "version", "state",
    "s'applique");
  - no other entry, page introductions apart, carries an EU one ("wording of", "Wording date",
    "dated {date}", "libellé du", "Date du libellé").
  "Wording" and "libellé" alone stay Luxembourg words too: an article's wording changes from state
  to state.
- **What each screen says and a screen reader hears**, for every census answer on all eight
  screens: the text and the human-read attributes, with the answer's own values taken out, use only
  the publisher's own date words. Each publisher's screens are seen saying their own words. The
  platform's sentences are data here, so an EU answer saying its date is never merged with a
  Luxembourg applicability date is the promise kept.
- **Mutations:** each is caught.
  - An EU hit "version of" fails the table check, the screen check and the borrowed-render check.
  - A Luxembourg hit "wording of" fails the table and screen checks.
  - An `applicability_date` added to the EU dossier census answer fails the field check.

The launch contract's first promise over EU answers (PR #860): "verify resolves every citation the
product emitted". `V3CitationVerificationTests` walked only Luxembourg answers. It now also walks the
answers the EU screens read, on the GDPR fixture: search in one work, and the dossier.
- The citations by role (each `permalink`, and any string that is exactly an EU permalink) must be
  hash-pinned EU permalinks. Each must verify as `digest_matches` for the very wording it pins
  (`wording_sha256`), and for its provision, unescaped.
- An EU `resolve.identifier` is the coordinate EU `resolve` answers (a provision or an expression),
  not a pinned citation, so it is asked of `resolve` and must answer.
- A mutant that drops the dossier permalink's digest is caught ("not a hash-pinned EU permalink").

The refusal set measures EU `verify` (PR #860). On a mount with an EU index, when the work's
expression holds one wording date, the mounted refusal set derives three more requests. The wording
digest is recomputed from the index rows by the stated rule, so `verify` answering also checks the
API's digest:
- a pinned provision: answered;
- a digest the wording does not have: `pinned_digest_mismatch`;
- a provision the wording does not hold: `anchor_not_in_version`.
On the real bounded first mount the refusal set is 8 cases (5 before), passing, with the shuffled
control caught; those two codes are no longer listed as not produced there. A mutant that lets EU
`verify` answer a provision not held fails the gate.

The retrieval set measures EU resolver exactness (PR #860). For each sampled EU work whose
expression holds one wording date, the retrieval set adds:
- its first three provisions' permalinks (the digest recomputed by the stated rule): exact cases,
  each judged to resolve to exactly that provision (`verify` names the CELEX and the provision);
- a provision the wording does not hold: a near miss, judged to find nothing.
On the real bounded first mount the card's retrieval set is 13 cases, and every gate is measured
and passes: anchor nDCG@10 1 over 9, no-hit accuracy 1 over 4, resolver exactness 1 over 3. The
judgments control now applies and catches the shuffle. Only the temporal arms are not measured
there, since the mount holds no Luxembourg state. A mutant whose EU `verify` names the first
provision for every request fails the fixture's resolver gate.

The release rehearsal over the real bounded first mount (2026-10-01, recorded by PR #862),
`node web/scripts/image-rehearsal.mjs --mount C:/lex-v3/first-mount-decision95-restart-20260930/v3-corpus`
at the head of PR #860. Every step passed:
- the one-server image (72,561,664 bytes) holds the 26 live page files, the 6 mount files and the 5
  files the mount's report lists, byte for byte, with no image failure;
- two builds from scratch give one manifest, `sha256:b330577295f5a4017715420733ff7351e9134b8d36cc917a690db9e1288ff9d6`;
- V2 is absent: 527 entries scanned, and none of V2's 63 routes fails when asked of the image. A
  V2-only route answers 404, and a path V2 and V3 share (such as `/`) answers with V3's own bytes;
- the rehearsal signature verifies;
- the image runs in WSL and is probed on all ten journey steps:
  - coverage and radar answer;
  - the six Luxembourg screens show the refusals the API gives, since the mount holds no
    Luxembourg state;
  - the EU search answers with 61 citations verified, and the EU dossier with 1;
  - no step fails;
- the card served at `/evaluation-card.json` is the release card, the gates over this mount:
  - refusal: 8 cases, pass, the shuffle caught;
  - retrieval: 13 cases, every gate passing, the shuffle caught;
  - temporal: not measured, since the mount holds no Luxembourg state;
- the release (version `v3-rehearsal-20261001T033235Z-523f28f8e84b`, manifest
  `7e6a2f08fefc8699d8e5c317073a206fff9145f0d4c123534e00d1ab3cc34681`, six assets) is published,
  read back and verified with no failure;
- the work directory, the artifacts and the container are removed.

Predecessor chaining, the first slice (PR #864): Luxembourg index schema `lex-v3-luxembourg-index/7`,
`user_version` 7. It still holds a genesis log, now in the shape a chained log needs. The changes are
confined to the event-log parts of the builder and the reader.
- `events` drops `UNIQUE(scope, key, event)`, since a replaced file can recur for one state's key;
  `seq` stays the key.
- A new `observations` table: one row per build that wrote events. Each row holds:
  - its corpus;
  - the index it carried forward (null for the first observation; a check makes this exact);
  - the events it appended, by sequence number;
  - its observation time, which a check holds null because none is held.
  A genesis log has one observation numbering every event (an empty log numbers none: first 1,
  last 0).
- A new `log_stamp`: the log's own schema, `lex-v3-event-log/1`, and the digest of exactly its
  observations and events. A later build can then verify a predecessor's log, which it will carry
  forward unchanged, across a change to the index's other tables.
- Each `first_sighting` names the digests of the publisher bodies its state's articles were read
  from (`source_body_sha256`, sorted, each once). With them a later build can tell a replaced file
  from an unchanged one by the log alone. The index's own tables hold no body digest, so:
  - the reader recomputes the whole log from the states and the bodies the log names, and refuses
    any other row, observation or stamp;
  - whoever holds the corpus checks the bodies against it (`VerifyEventLogSources`): the build, the
    mount at open, and the mount writer's verification.
- Tests:
  - the genesis log's shape;
  - seven observation and log-stamp tampers, each refused with the logical-rows stamp recomputed,
    so only the new checks can refuse;
  - a body the state was not read from passes the index alone and is refused by the corpus, by the
    mount and by the mount writer;
  - a body named twice is not the genesis log.
- The fixed-input byte pin moves, as a schema change moves it. The schema version and the pin are
  shared with the data lane.

Predecessor chaining, the second slice (PR #866): a Luxembourg index build can carry its
predecessor's event log forward.
- `LuxembourgIndexPredecessor.TryRead` reads the previous build's index:
  - its bytes must be the digest its build report names;
  - it must hold a log of this log schema (`lex-v3-event-log/1`), whatever its other tables'
    schema. An index of schema 6 or before has no log stamp and refuses
    `predecessor_schema_differs`;
  - the log must match its own stamp and be numbered as observations appending events, or it
    refuses `predecessor_mismatch`.
- `TryBuild(envelope, predecessor)`:
  - copies the predecessor's observations and events unchanged, an exact prefix (G3a);
  - appends one observation naming the predecessor's digest, with an event for each state the log
    does not hold: `first_sighting`, or `expression_added` when the log holds the same work and date
    in another language;
  - leaves a state the log holds unchanged silent, and a state the log holds that this build lacks
    silent too (absence is not a withdrawal);
  - refuses `predecessor_state_changed` for a held state with another digest or other source bodies,
    until slice 3 adds `file_replaced`.
  The genesis log is now the special case: the events appended to no log.
- The reader checks any log:
  - it is numbered as observations appending events;
  - its last observation is of the index's corpus;
  - every state is held by the log at its own digest;
  - the last observation's events are exactly those it must append to the log before it;
  - every observation's events are exactly those it appends to the log before it, replayed from the
    log alone (the states it leaves held). The review of #866 found that a carried genesis event
    renamed `expression_added` passed both the predecessor read and the reader.
  That the log before it is the predecessor's is proven by the build, which copies it.
- `VerifyEventLogSources` checks the corpus's bodies against the log's last word on each state.
- Plumbing: `V3FirstMountBuild.ReadPredecessor(directory)` and a `RunAsync` overload, with both
  builds of the index taking the same predecessor. `Lex.V3.Tool build --predecessor <v3-corpus
  dir>` reads and verifies the predecessor before the first request.
- The API described every log as a genesis log (`basis`, the predecessor and the notes) until
  PR #867, which serves `events` and `answer_drift` across the chain.
- On the fixture, a chained build is byte-stable across two builds, chains again (three
  observations), appends `first_sighting` to a predecessor whose log lacks the state, and refuses
  a changed state.
- The real bounded first mount's Luxembourg index is schema 6 with no state, so it cannot be a
  predecessor; the first chain starts with the next build.

Predecessor chaining, the third slice (PR #867): comparison events. A chained build compares its
states with its predecessor's log, key by key, over the keys the log holds and the keys it holds.
A held state this build lacks stays held: absence is not a withdrawal.
- **`file_replaced`** (G1): a held state whose source bodies differ. It names the state and bodies
  it replaces (`replaced_state_sha256`, `replaced_source_body_sha256`).
  - The state's digest is the permalink's version id, and it changes with the text, not with the
    bytes alone.
  - Through the real pipeline: the act rebuilt from its publisher file with one article reworded is
    `file_replaced` with a new digest, so a new permalink.
  - Rebuilt with one byte added and the text unchanged, it is `file_replaced` with the digest, and
    so the version, unchanged. **Owner question, asked in PR #867:** should a replaced file whose
    text is unchanged mint a new version? That would change the published permalink scheme.
    **Answered** by the panel on the owner's behalf (recorded by PR #871): no. Version identity
    follows the legal text, and the replacement is recorded as built.
- **`interval_closed`** and **`validity_revised`**, for a state the log held whose applicability
  interval this build's states move. The end is the next later date in the work and language,
  which is this derivation's, never the publisher's, so each event is marked `derived: true` and
  names `applicable_from`, `previous_to` and `new_to`.
  - The latest state, followed by a later one, closes.
  - A state whose next moved earlier, because one was inserted, is revised.
  - A held state this build lacks closes too.
- **The same bodies with another digest** are this derivation's change, not the publisher's: the
  build refuses **`predecessor_derivation_differs`**, which replaces slice 2's interim
  `predecessor_state_changed`.
- The reader's history replay (#866) covers the new events with no change: every observation's
  events are recomputed from the log alone.
- `withdrawn_from_source` and `resighted` stay unminted. They need the three-run rule (31-v3-spec
  §91(b)) and a proof of complete enumeration.
- The review of #867 found that a chained log broke the served event operations: `answer_drift`
  threw on any revising event, and `events` called every log genesis. So the fourth slice is part
  of #867:
  - `events`' log block names the basis (`genesis` or `chained`), the predecessor, the number of
    builds compared, and the **ancestor logs** whose cursors it honours, each to the last event it
    held;
  - a cursor of an ancestor reads on in this log with the same numbers, and one from any other log
    still refuses `snapshot_unknown` (a driver decision, below);
  - each row carries the log's detail verbatim, and a `file_replaced` row also carries the replaced
    permalink;
  - a chained log says what it holds (`chained_note`) and what it does not (`withdrawal_events`);
  - `answer_drift` enumerates its `interval_closed` and `validity_revised` events a page at a time.
    Each row is the dates of one work and language, from the revision's new end to its old one
    (open for the latest), whose as_of answer moved from the state before to the states applying
    from the first of them, each by its permalink, marked derived.
  On a mount whose chained log holds the act at an earlier date (crafted predecessor, real build),
  `events` and `answer_drift` show the `interval_closed` and its moved dates.

Predecessor chaining, the fifth slice, first part (PR #871): each build's time, the bound that
`as_observed` will give.
- The panel answered the chain's three owner questions on the owner's behalf (ANSWERS.md,
  2026-10-01):
  - G1: a replaced file whose text is unchanged mints no new version. Version identity follows the
    wording and text digests, so published permalinks keep resolving. The replacement is still
    recorded as `file_replaced` with both body digests, as #867 builds it.
  - `as_observed` may ship identified by a build snapshot. The answer must name the snapshot, give
    that generation's build time as an upper bound ("observed no later than"), and never state or
    infer an observation time. A time the snapshots cannot place refuses with an existing typed
    code.
  - Generations are retained by the launch contract's retention line (S7-A09):
    - every generation a published permalink or evidence bundle references, indefinitely;
    - nightly generations for 90 days;
    - one complete monthly keeper, indefinitely;
    - the retained depth reported truthfully.
  - The slice-4 decision (an ancestor's cursor reads on) stands.
- Index schema `lex-v3-luxembourg-index/8`, event log `lex-v3-event-log/2`: each observation records
  `built_at`, UTC to the second. It is hashed into the log stamp and carried forward unchanged by
  every successor. `observed_from` stays null, because a build time is not an observation time.
- The build time is a build input, not read inside the builder (a driver decision, below):
  - `LuxembourgIndexBuilder.TryBuild` takes it;
  - it must be a UTC whole second, later than the predecessor's last build and no earlier than the
    corpus's EU capture (the one observation clock the corpus holds), or the build refuses
    `build_time_invalid`;
  - nothing can check it against the Luxembourg fetches, whose times the corpus does not hold;
  - the reader requires each observation's time to be a real UTC second later than the one before.
- `V3FirstMountBuild` reads its clock once, after every fetch and derivation:
  - it rounds the time up to the next second, since rounding down could land before the last fetch;
  - it passes that one time to both builds of the index, so the twice-built comparison holds;
  - the Tool uses the system clock.
- `events` now serves `log.built_at` and each ancestor's `built_at`. The sentences that said no
  build time is held now say what is held:
  - `coverage` states none, in its `not_held` row and in the Trust and Coverage page's counts note
    (English and the French draft);
  - a build's time is not upstream health.
- An index on each event's work (`events_work_seq`) serves the per-work fold that `as_observed`
  reads.
- A `/7` predecessor refuses `predecessor_schema_differs`. No real mount was chainable: the real
  bounded first mount's index is schema 6.
- The second part is PR #874, below.

Predecessor chaining, the fifth slice, second part (PR #874): `as_observed` by build snapshot (G4).
- `POST /api/v3/as_observed` and its MCP tool. The request takes `identifier`, `date`, optionally
  `language`, and exactly one of `snapshot` (an index digest) or `at` (a time).
- The snapshot is one build of the mounted chain, named by its index digest: the mounted index
  (`events`: `log.log_id`) or an ancestor its log carries forward (`log.ancestors[].log_id`).
- The log is folded up to that build's last event, by a new per-work query over the
  `events_work_seq` index (its plan checked, never a scan). The state applying on the date is
  selected as `as_of` selects. A state once held stays held (absence is not a withdrawal), and the
  next date is the next one held at that snapshot.
- The answer names the snapshot: its digest, its observation number, its corpus and whether it is
  mounted. It gives `observed_no_later_than`, the build's time, with
  `observation_time_held: false`, and it states no observation time.
- A state the mounted index still holds is served in full, as `as_of` serves it
  (`text_held: true`). A state only an earlier build held is named by its permalink, digest and
  source bodies from the log, without text (`text_held: false`), until the generation mount
  (slice 6) can quote it.
- Every request by time refuses `snapshot_unknown`, because upper bounds alone place no instant in
  a snapshot. So does a digest that is no snapshot of this log. A date before the snapshot's history
  is `no_version_for_date`, and a language the snapshot did not hold is `language_not_available`.
- On the chained fixture (a predecessor whose log held the work at an earlier date, and the real
  build chained to it):
  - the predecessor's snapshot answers the earlier state, without text, bounded by the
    predecessor's build time;
  - the mounted snapshot quotes the act's state, the same permalink `as_of` gives;
  - between the two dates, the mounted snapshot answers the earlier state the log still holds,
    which `as_of` cannot answer.
- 24 operations are served and three remain unserved (`knowable_on`, `concepts`, `transposition`).
  The sentence for `knowable_on` now says what it still needs: each Luxembourg body's capture time.
  A build's time bounds observation only from above.
- The `answer_drift` and `events` MCP descriptions no longer call every log genesis (stale since
  #867).
- The registry digest moves with the new request schema, to `c40e23fd…`.

Schema 6 served with its build record absent (PR #878).
- **The gap.** Since #864 the reader read only the newest schema. So the API refused every Luxembourg
  index of schema 6 ("the schema differs from the exact terminal schema"). Two such indexes exist:
  - the data lane's running EU population, frozen at `6eb1d9d9`;
  - the real bounded first mount, which the web lane's journeys and release rehearsal used.

  The data lane raised it (Q-20261001-0656-codex). CI did not catch it, because the image rehearsal
  job is skipped there.
- **The panel's answer**, on the owner's behalf: the web lane adds exact schema-6 read validation for
  serving.
  - The new observation and log evidence is explicitly absent, and the operations that need it
    refuse with their typed codes.
  - `LuxembourgIndexPredecessor.TryRead` keeps refusing schema 6.
  - No log stamp and no observation time is invented.
  - The data lane does not edit these files. Its next step is "acquire once, derive many", so a
    schema change no longer forces re-acquisition.
- **What the reader accepts**, by `user_version`, exactly two schemas: 8, and 6.
  - Schema 6's tables are schema 8's but for the event log: one `events` table of `first_sighting`
    only, unique per state, with no `observations` and no `log_stamp`.
  - Any other version refuses, and so do tables that are not exactly that version's.
  - The DDL is now the shared tables plus each version's event log. It is byte for byte the same,
    and the fixed-input pin holds.
- **A schema-6 index is checked as schema 6 checked it**:
  - its log is exactly its states' genesis log as schema 6 wrote it (state digests only);
  - its stamp names schema 6;
  - its logical rows match their stamp.
- **What a schema-6 mount answers:**
  - the reader reports `SchemaIdentity`, and `RecordsBuilds` is false;
  - `events` serves its genesis log with `log.built_at` null, a `legacy_note` and a `build_record`
    not-held row. Its genesis and silence notes and its upstream-health row are schema 6's own, and
    claim no build time (review of #878);
  - `as_observed` refuses `snapshot_unknown`, saying the index names no snapshot;
  - `coverage`'s build-time row says the index records no build time either;
  - the corpus check holds only the binding: no source bodies or build time are held to check.
- **Fixtures:** the real bounded first mount's Luxembourg index, manifest and corpus, byte for byte
  the files its build report names (`c7f40548…`, `ef03cacd…`, `312d4804…`).
  - The mount opens and is served with its build record absent.
  - A schema-6 index with states (the state fixture rewritten into schema 6's tables) serves its
    genesis log, `as_of` answers, and `as_observed` refuses.
  - Seven tampers are refused, among them schema 6's tables under `user_version` 7 or 8, an
    observations table, a stamp naming schema 8, and schema 8's tables under `user_version` 6.
- **The whole ingest suite also ran with `V3_EVALUATE_MOUNT` naming the real mount**, so its
  real-mount gates ran on it.

Predecessor chaining, the sixth slice, first part (PR #880): a chained build keeps its earlier
generations, by the retention line (G3b).
- **The line.** The panel applied the launch's retention line, Stage 7 S7-A09
  (`lex-governance/stages/STAGE-7.md:37-38`), to the generation mount:
  - every generation a published permalink or evidence bundle references, indefinitely;
  - nightlies for 90 days;
  - one complete monthly keeper, indefinitely;
  - the retained depth reported truthfully.
- **`V3GenerationRetention.Decide`** decides from the log alone. "Now" is the mounted build's own
  time, never the clock, so two builds of one chain decide alike. It keeps each held generation
  that is any of these:
  - referenced;
  - the last build of its UTC day (the mounted build's day included) within 90 days;
  - its UTC month's earliest held generation.

  It reports the rest as dropped, and those no longer held as absent: never claimed.
  - Nothing records which permalinks or bundles were published. So `referenced` comes from the new
    Tool option `--referenced <file>`, a JSON array of index digests. It means the generations
    promoted to production, which is empty until the owner promotes one.
  - Every verified generation counts as "complete".
- **The writer** handles a chained build (`--predecessor`):
  - it copies the kept generations whole out of the predecessor's directory (the predecessor itself,
    or a generation it kept) into `generations/{luxembourg index digest}/`: the five mount files and
    the build report;
  - it records the decision in `generations/retention.json` and lists the generations in the build
    report;
  - nothing is deleted anywhere, and the predecessor's directory is only read.
- **`VerifyAsync`** holds each generation to the mounted log:
  - its name is an index the log names as a predecessor;
  - it holds exactly a generation's six files and is itself a mount that verifies;
  - its Luxembourg index is that digest, and its corpus the one its observation names;
  - its log is the mounted log up to its observation;
  - `retention.json` is the line's decision over the generations the writer could copy: those it
    kept, held here, and those it recorded as dropped, which have no directory;
  - a chained mount must hold `generations/retention.json`, even when it keeps no generation, so a
    mount whose generations were all removed does not verify as one that never had any. A
    generation is verified as a generation: its own earlier builds are its mount's to record.
  - The review of #880 found both gaps. With only the held generations as input, a correctly dropped
    nightly read as absent and the writer's record was refused; and a chained mount with
    `generations/` deleted verified.
- **Tests:**
  - three real first-mount builds on three days, each chained to the last: the third keeps the
    first (its day's last, and October's keeper) and the second (nightly), copied byte for byte,
    and verifies;
  - a fourth build 120 days after the first keeps only October's keeper and records the two
    nightlies, now past 90 days, as dropped; it verifies;
  - five ways a generation can be wrong are refused: a missing file, a directory that is no earlier
    build, another build's files under a generation's name, no `generations/` at all, and a
    retention record the line did not decide;
  - the policy's own tests cover the 90-day edge, a referenced old build, an absent one, and an
    earlier build of the mounted build's day.
- **Not yet:** the API reads no generation. Next come the mount's own checks of them at open, the
  depth `coverage` and `events` report, and then `as_observed` and `verify` quoting a state from a
  retained generation. An image built from such a mount carries `generations/` too. No byte budget
  is enforced yet.

Predecessor chaining, the sixth slice, second part (PR #885): the mount holds its generations to
its log when it opens, and reports the history depth it keeps (S7-A09: "reported history depth is
truthful").
- **At open,** `V3CorpusMount.OpenAsync` runs `V3CorpusMountWriter.VerifyGenerationsAsync`, the
  checks the writer's verification runs, so a mount is held to the same checks where it is built
  and where it is served.
  - A generation that does not hold, a chained mount with no retention record, or generations
    beside a mount with no Luxembourg index all fail the mount closed.
  - A chained build written without its predecessor's directory records each earlier build as
    absent (`WriteRetentionRecordAsync`), never claimed.
- **`coverage` gains `history`:**
  - whether the log records builds, how many it records and since when;
  - the retention line that decided (its id, nightly days and evaluation time);
  - each snapshot whose text the mount holds (the mounted build, and each kept generation with why
    it is kept);
  - how many it does not hold, and a note that every time is a build's, never an observation time.
  - A schema-6 index records no build and says so.
- **The Trust and Coverage page renders it,** in English with a French draft. The page's tests hold
  every leaf of it to reach the page, both renderers to agree, and every string to be escaped.
- **`events`' ancestors gain `text_held` and `retained_as`.**
- **Tests:**
  - the chain test's third build mounts and reports three builds, all three with text;
  - its fourth build, 120 days later, reports four builds, two with text and two without;
  - each damaged copy that the writer's verification refuses, the mount refuses with the same
    reason;
  - a genesis mount reports one build with the mount's own text and no retention line applied;
  - a schema-6 mount reports no build recorded;
  - the crafted chained mounts of the events and `as_observed` tests carry a retention record that
    names their predecessor absent.
- **Next:** `as_observed` and `verify` quote a state from a retained generation.

Predecessor chaining, the sixth slice, third part (PR #889): a state only a retained generation
holds is answered from it, and the permalink the product emitted for it still verifies (G3, and the
launch contract's "`verify` resolves every citation the product emitted").
- **Readers at startup.** The mount opens one verified reader per retained generation, newest first,
  so no request opens one (the image's private `/tmp` holds every index copy from startup on). The
  readers are disposed with the mount.
- **`as_observed`:**
  - a state the mounted index lacks is served in full from the newest retained generation that holds
    it, with `text_held: true` and `text_from` naming that generation;
  - `text_from` is null for a state the mounted index holds;
  - a state no held build holds stays identity only, without text;
  - a work the mounted build no longer holds at all is found through the log by its stable work
    coordinate, and answered from its generation (review of #889). An identifier the log never held
    keeps `identifier_unknown`.
- **`verify`:** a pinned permalink whose digest a later build replaced at its coordinate is verified
  in the retained generation that holds it:
  - `digest_matches`, with `held_in` naming the generation and `superseded_by` the mounted state that
    replaced it;
  - `verified_by` is the generation's own corpus and index, and the sources are its own;
  - with the generation pruned, it is `pinned_digest_mismatch` naming the current state, as before.
  - The generation is asked before any refusal over the current states. So the permalink resolves
    when the coordinate holds another state, several (one per language), or none any more, and
    `superseded_by` is null in the last case (review of #889).
  - A driver decision: the verdict stays `digest_matches`, because the digest does match the retained
    text. The superseded state is named beside it, so a citation checker keeps working and a reader
    sees that the citation is no longer the current text.
- **On two real builds of the state fixture,** the second with one article reworded:
  - `as_observed` at the first build's snapshot serves the original state from the generation;
  - the permalink emitted before the rewording verifies there, naming the reworded state;
  - with the generation pruned, the same requests answer without text and as a mismatch;
  - removing the generation lookup from `verify` fails the test;
  - a later build that holds the work no longer at all (chained, from the complete envelope, which
    holds no Luxembourg state) answers `as_observed` from the generation by the work's coordinate,
    and verifies the old permalink with `superseded_by` null. Reverting either repair fails that
    test.
- **What is left of predecessor chaining** is the data lane's part: observation times
  (`observed_from`) once a Luxembourg body's capture time reaches the corpus, then `knowable_on` and
  withdrawal.

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
- Since PR #833: served as JSON at `/evaluation-card.json` and beside the release assets, signed
  through the release manifest. Since PR #844, the release card is the gates run over the release's
  own mount.

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

The temporal gate over any mount (PR #838), the first slice of ruling 2's machine gates over the
real mounted corpus. The fixture's gates build their own states; these derive their cases from the
mount they are given.
- `V3MountedGatesTests` reads the mount's Luxembourg index: each work's held states in one language,
  by applicability date, form a timeline. The timeline says what a dated request must select on any
  day:
  - nothing before the first date (`no_version_for_date`);
  - the one state of the latest date on or before it;
  - `ambiguous_version` where two states share that date.
- The cases, for a seeded sample of up to 40 works:
  - the day before the first date;
  - each date, a day inside and the last day of each window;
  - a day 1,000 days after the latest date.
- The arms are `as_of` and `in_force_on`, in each language the sample holds, and with no language
  for every work. With no language the mount selects in every language the work holds:
  `ambiguous_version` if any language's date holds two states, `no_version_for_date` if none holds
  one yet, and otherwise each language's state. The work's timelines together give that key (the
  sorted states). The review of #838 found that the first version left multilingual works out of
  the no-language arms.
- The date control shifts every case by the median gap between held dates. It runs over the cases
  whose selection the timeline says the shift changes, which always includes each day before a first
  date.
- An arm with no case is `not_measured` (`no_measurable_query`), and its control says there was no
  case to shift. An empty stratum is reported, never scored.
- With no variable it runs on the fixture: the fixture's state, two states on one date and a latest
  state give 9 cases per arm, all passing, and every control catches the shift. When
  `V3_EVALUATE_MOUNT` names a mount it runs there, and `V3_EVALUATION_CARD_OUT` receives the card
  sets.
- Over the real bounded first mount, which holds no Luxembourg state: not measured, and it says why.
  Over the journey mount, written by another test with one state: 3 cases per arm, all pass, and every
  control catches the shift.
- A French work with a German state 200 days later gives 6 no-language cases: French alone before
  the German date and both from it. All pass, and every control catches the shift.
- Two mutations:
  - a timeline that ignores two states on one date fails the timeline test and the fixture gate;
  - the first version's single-state arm used for no language fails the multilingual test.
- Next slices: the refusal and retrieval sets derived from a mount, then the card over the release's
  mount in the rehearsal.

The refusal set reads the EU index too (PR #846), so a mount that holds only the EU still measures
its refusals. From the EU index's first work in its first language that holds a usable word in any
article (scanned in order; the review of #846 found the first version read only the first article,
so a short one left the EU unmeasured):
- a word it holds, searched in it, must be answered;
- the same in a CELEX the index does not hold: `identifier_unknown`;
- in a language the work is not held in: `language_not_available`;
- with a date, which EU search does not serve: `retrieval_mode_unavailable`.
A code an EU request produces is no longer listed as not produced. Coverage's own rule is mirrored:
it refuses `no_corpus_mounted` when the mount holds no Luxembourg index. The first run on the
EU-only GDPR fixture expected an answer there.
- On the real bounded first mount the refusal set grows from 1 request to 5 (coverage and the four
  EU ones), all answered with their code. The verdict control now applies and catches the shuffle.
- On the GDPR fixture alone: every EU request is answered with its code, and the control catches
  the shuffle.
- The release-mount run requires the EU requests whenever the EU index holds a usable word.
- Two mutations:
  - the unknown CELEX given the gold `answer` fails;
  - reading only the first article fails the short-first-article test.

The refusal set over any mount (PR #839), the second slice of ruling 2's gates over the real
mounted corpus (`V3MountedGatesTests.Refusal.cs`). Each request is built from what the mount holds,
so the one code the registry says answers it follows from the mount's own data:
- a state held alone on its date anchors most requests:
  - that state and its permalink must be answered;
  - an unknown work gives `identifier_unknown`;
  - the day before the work's first date gives `no_version_for_date`;
  - an anchor the state lacks gives `anchor_not_in_version`;
  - a wrong digest gives `pinned_digest_mismatch`;
  - a language the work is not held in gives `language_not_available`;
- mode, cursor and format requests are refused `retrieval_mode_unavailable`, `snapshot_unknown` and
  `format_not_available`;
- data-dependent codes are derived only where the data holds them:
  - two states on one date: `ambiguous_version`;
  - a title two works carry, or a beginning two works' titles share (the resolver's own rule, exact
    first, then prefix): `ambiguous_identifier`;
  - two states whose rule profiles differ: `profiles_differ`;
  - a state whose text the evidence bundle withholds by its own rule: a source not acquired, or any
    rights disposition but the admitting `agreed_same_run_cc_by`, with the tokens taken from the
    contract: `text_withheld`. The review of #839 found that the first version guessed a
    `non_admitting` prefix and missed a disposition such as `conflict`;
  - a state whose text is admitted but holds none: `text_not_available`;
  - no EU index: `no_corpus_mounted`.
- A code the mount cannot produce is named on the card as not produced, with its reason, and never
  faked.
- On the fixture (two titled works, a later state with another rule profile, two states on one
  date): 16 requests, every one answered with its code; the verdict control catches the shuffle.
  `text_withheld` and `text_not_available` need other mounts. The withheld-licence fixture derives
  `text_withheld`.
- `TheGatesOverTheMountTheReleaseNames` now runs the temporal and refusal sets over the mount
  `V3_EVALUATE_MOUNT` names and writes both to the card:
  - the journey mount: 13 requests, all answered with their code, and the control catches the
    shuffle;
  - the licence-blocked mount: 14, adding `text_withheld`;
  - the real bounded first mount: 1 (coverage, answered). Every Luxembourg code is listed as not
    produced there, since there is no Luxembourg state. `no_corpus_mounted` is not produced either,
    since the EU index exists.
  EU refusal cases derived from the EU index came later: EU search's in PR #846, and EU `verify`'s
  in PR #860. The real bounded first mount now has 8 cases.
- Two mutations:
  - the day-before-history request given the gold `answer` fails the refusal tests;
  - the first version's `non_admitting` guess fails the `conflict` case.

The page reads a card over a mount the gates found nothing to ask of (PR #841). Over the real
bounded first mount the temporal no-language arms and the retrieval set have no case, and the page's
card reader refused a set of no case. `readEvaluationCard` now accepts one only as honest emptiness:
- a gate may count no case only when it is not measured for `no_measurable_query`;
- a set of no case must have every gate that way;
- a control over no case can only be `not_applicable`, with its reason;
- a set or control of no case carries the digest of no case, `EMPTY_CASES_SHA256` (the SHA-256 of
  `[]`, as the platform renders it). The review of #841 found another digest was accepted.
A scored gate over no case, another not-measured reason over no case, a control over no case that
"caught" something, and a no-case digest that is not the empty list's are refused.
The first mount's card reads and renders. At this head it has 3 sets (the two temporal no-language
arms and the refusal set) and says "2 gates do not pass", each named not measured. With #842's
retrieval set it has 4 sets and says 5, as the review of #841 counted. The journey and
licence-blocked mounts' cards and the platform card read too.
The retrieval set reads the EU index too (PR #845), so the real bounded first mount's card measures
retrieval. EU search is served in one work and states the same matching (`EuropeSearchMatching`: a
byte-exact substring). For a seeded sample of up to two of the EU index's works in each language it
holds (the review of #845 found the first version took two work-language pairs in all, which could
leave a language unsearched):
- words held by one to five provisions are judged to find exactly those (the CELEX and the
  publisher's provision id, as an EU hit names them), searched with the work as scope;
- strings the work holds nowhere find nothing.
EU `verify` was not served then, so the EU gave no exact-identifier case. On the GDPR fixture and on
the real first mount, anchor nDCG@10 was 1 over 6 EU word cases and no-hit accuracy 1 over 3.
Resolver exactness was not measured (no measurable query), so the judgments control did not apply:
its rule needs every required gate to pass first, and the card said so. Since PR #860 the EU gives
exact cases over the EU permalink grammar, and every retrieval gate is measured there (below). A French-held work gives French
cases, which pass. Two mutations:
- EU judgments naming another provision drop nDCG@10 to 0 and fail;
- the first version's sampling (three English works and a French one) leaves French out and fails
  the sampling test.

The retrieval set over any mount (PR #842), the third slice of ruling 2's gates over the real
mounted corpus (`V3MountedGatesTests.Retrieval.cs`). The judgments are computed from the mount's
index by the search's own stated matching (`SearchMatching`: a byte-exact substring of an article's
searchable text, nothing folded):
- words of the sampled states held by one to five articles (work and publisher article id) of the
  language, with every hit on the answer's one page: each is judged to find exactly those, which
  must all rank within the first ten;
- the same word scoped to its work finds that work's articles; scoped to a work held in the language
  that lacks it, nothing. A work not held in the language is refused `language_not_available`, so it
  is never asked;
- strings held nowhere find nothing;
- three article permalinks of each held state must be accepted under their own work and anchor,
  and one naming an anchor the state lacks is refused `anchor_not_in_version` (no hit). The cases are
  keyed by state, so two states of one work keep their own cases. The review of #842 found both of
  the last two;
- a refused search ranks a marker that matches no judgment, so it fails rather than passing as
  "found nothing".
Over the fixture (two works) and the journey and licence-blocked mounts (one state, 15 cases each),
all three gates pass at 1: anchor nDCG@10 (11 cases), no-hit accuracy (4) and resolver exactness
(3). The judgments control catches the shuffle. The real first mount has no case: all three gates
are not measured and the control says why. A mutation, word judgments naming another anchor,
drops nDCG@10 to 0.57 and fails. `TheGatesOverTheMountTheReleaseNames` now writes all three sets.

The image rehearsal runs in CI (PR #847): the job `image-rehearsal` in `v3-ci.yml`, on every push
to `v3/integration`. It also runs on a pull request whose branch name contains `rehearsal`, so a
change to the rehearsal proves the job before it merges.
- It writes the journey fixture mount (`V3JourneyMountTests`) and runs
  `web/scripts/image-rehearsal.mjs --no-probe`. The WSL probes need the driver's machine and stay
  local.
- `jq` then holds the report to:
  - the second build's digest;
  - no image failure and V2 absent;
  - the rehearsal signature verified;
  - the card being the gates run over that mount;
  - the release read back with no failure;
  - the work and artifacts directories removed.
- The report is uploaded.
Push only, so a pull request's wait is unchanged, while the image pipeline is checked after every
merge, the data lane's included.

The release card is the machine gates run over the release's own mount (PR #844), the last slice of
ruling 2's gates over the real mounted corpus. The image rehearsal first runs
`V3MountedGatesTests.TheGatesOverTheMountTheReleaseNames` over the mount it bakes:
- `V3_EVALUATE_MOUNT` names the mount and `V3_EVALUATION_CARD_OUT` receives the card, built in its
  own artifacts directory;
- the card is checked by the page's own rules;
- it is handed to the live build (the Trust and Coverage page renders it) and served at
  `/evaluation-card.json`, and the release carries it beside the image under the signed manifest.
A gate that fails fails the test, and so the rehearsal: no release goes out with a failing gate.
`--platform-card` keeps the platform's fixture card.
- On the real bounded first mount the card is the gates over that mount:
  - the two temporal no-language arms have no case and are not measured, and their controls say why;
  - the refusal set holds one request (coverage) and names every code the mount cannot produce;
  - there is no retrieval case, and the page reads all of it (PR #841).
  The image serves that card at `/evaluation-card.json`, byte for byte the release's. The image was
  reproduced, the 8 probes passed and the release read back clean.
- On the journey fixture mount, every gate passes and every control catches its shuffle:
  - 4 temporal arms of 3 cases;
  - 13 refusal requests;
  - 15 retrieval cases, all three gates at 1.
  The 8 probes verify 105 citations.
- A mutation, one derived gold changed, fails the gates step, so the rehearsal stops before anything
  is built, and its work directory is still removed. The first version computed the card before the
  work directory's `try` and left it behind on such a failure.
- The test platform takes `-m:1` and `-nodeReuse:false` for its own and then runs no test, so the
  gates' `dotnet test` passes neither.

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
census and the web preview's copy. G2's detached signature comes from the release pipeline (item 7).
The mount is the fixture, so this proves the path, not a corpus.

Replay G1, G3 and G4 on the real handler (PR #897). Predecessor chaining (PRs #864 to #889) made the
other three guarantees provable, and each had been proven in parts: G1 at the builder
(`LuxembourgIndexBuilderTests`), G3 by the generation mount (`V3CorpusGenerationQuoteTests`), and G4
by `as_observed` (`V3CorpusAsObservedMountTests`). `V3ReplayGuaranteesTests` now runs all three
through the real handler, beside G2 and G5, so the launch contract's "replay G1 to G5" has one place.
- The chain: three real builds of the state fixture on three days, each chained to the one before.
  Each is written as the mount it would be, with the earlier builds kept beside it as generations and
  the retention line's record, and each verifies:
  - the first build reads the publisher's file as held;
  - the second reads it with one byte added and its text unchanged;
  - the third reads it with one article reworded.
- G1, version immutability:
  - the second build answers the same version id as the first (the panel's G1 answer: no new version
    for unchanged text), and the third answers a new one;
  - the latest log holds one `file_replaced` in each later build's events. Each names the version it
    replaced and the version it holds, and the bodies differ;
  - the first version id still verifies on the latest mount (`digest_matches`) and names the version
    that replaced it.
- G3, nothing hard-deleted:
  - each earlier build's `events` answer is the latest log's prefix, row for row, every field but the
    cursor (which names the log it was read from);
  - every state the log names, held or replaced, verifies on the latest mount: the original from a
    kept generation, the reworded one from the mounted index.
  - Which builds keep their text is the retention line's (S7-A09). A state no kept build holds is
    still named by the log, without text.
- G4, as-observed answering: `as_observed` at each build's snapshot, asked of the latest mount,
  answers the version that build's own mount answered, article for article by text digest:
  - quoted from that build's own generation, or from the mounted index for the latest;
  - bounded by that build's time (`observed_no_later_than`), claiming no observation time.
- The mounts are fixtures, so this proves the path, not a corpus. A real chained mount needs a second
  build from custody, which the data lane's offline derivation will provide.

EU text served: EU `evidence_bundle` and the reading screen over the original wording (PR #903; the
web lane's order 2, from the owner's proxy's journey corrections of 2026-10-01 14:30 UTC).
- **The answer.** EU `evidence_bundle` answers a work the mounted EU index holds from that index. It
  quotes, for each held expression in the served languages, every article of the one held wording:
  - the article's text, which is the text the index searches;
  - the digest of that text, held to the index's own stored text digest (a difference is a damaged
    index and throws, never a quote);
  - the corpus body digest, the official source, and an article permalink that EU `verify` accepts
    (the wording permalink plus the provision after `#`).
- **The date.** It answers on the wording's own Formex act date only. No consolidation is held, so a
  date before or after it refuses `no_version_for_date`, naming the held wording dates (the EU parity
  driver decision (b), PR #761). The original wording is never served as a later date's.
- **Decision 95.** Every EU answer carries the acknowledgement "© European Union,
  https://eur-lex.europa.eu" and the statement that only the Official Journal published in
  electronic form is authentic (Regulation (EU) No 216/2013, Article 1(2)).
- **Rights at compose time.** Every corpus member the articles come from must be acquired, which an
  EU build reaches only after retaining the Decision 95 receipt. Otherwise the answer refuses
  `text_withheld`.
- **Refusals.**
  - An ambiguous work or a language not held refuses as EU `dossier` does.
  - With no EU index mounted, an EU identifier keeps the refusal it had, `retrieval_mode_unavailable`,
    which the refusal census pins; EU `dossier` answers `no_corpus_mounted` there. Aligning the two is
    a follow-up, once the samples can be re-rendered.
- **The screens.** The reading screen reads the EU bundle (`readEuropeEvidenceBundle`, holding it to
  its own rules) and shows:
  - the acknowledgement and authenticity statement above the text;
  - each wording headed by its Formex act date, never an applicability date;
  - each quote with its evidence line.
  The export composer shows the EU text and says it is not composed: it pins Luxembourg states'
  articles only, so it offers no file. The chrome table gains the EU heading and counts and the export
  sentence, each with a French draft, and the chrome scan renders both EU views.
- **Evidence:**
  - `V3CorpusEuropeEvidenceBundleMountTests` on the retained GDPR fixture, through the real handler:
    - every article with text is quoted, in the index's order, with the index's own text;
    - each `text_sha256` is recomputed;
    - every article permalink verifies as `digest_matches`;
    - the day before and five years after are each refused, and German refuses;
    - with no language, the French-only fixture's one held wording is quoted under its own permalink,
      and English refuses `language_not_available` naming French. No fixture holds two languages of one
      work: a bundle with two wordings (their order, `served_languages`, the web reader's language rule)
      is exercised only by the journey's EU reading step on the real bilingual canary mount, not yet run.
  - The EU citation walk (`V3CitationVerificationTests`) now verifies the bundle's permalinks too.
  - Web: `reading-europe.test.mjs` covers the reader, fourteen broken rules, the escaped provision, the
    reading screen and the export composer. The chrome scan passes with both EU views.
  - Review of #903 (an independent Claude review; the owner's order of 2026-10-02 excludes Codex):
    - the journey's quote check read Luxembourg permalinks only, so every EU quote failed it; it now reads
      either publisher's (`pinnedCitation`), and `journey-verdict.test.mjs` holds EU quotes to it;
    - an EU refusal on the reading and export screens was said in Luxembourg's words ("applies on that
      date"); EU refusals now have their own sentences (`LIVE_READING_EUROPE_REFUSAL_SENTENCES`, French
      drafted in `refusal-sentences.mjs`), and an EU refusal card's date hints speak of wordings
      (`europeNullSentences`), Luxembourg's of states; a test holds every EU refusal sentence and card
      to no applicability, state or version word;
    - the served scope now says what `body_sha256` is (the corpus member's retained manifestation) and
      that the text is read from the Formex package (`package_sha256`, `source_entry_sha256`).
- **Not yet:**
  - the journey's EU reading step on the real canary mount (GDPR in English and French) in a browser;
  - an EU bundle captured in the answer census, which replaces the web tests' hand-built answer;
  - the EU export.
  Local builds, test runs and browser runs wait for free memory above 4 GB (standing order of 10:35
  UTC); CI runs the whole solution.

## The EU time view (PR #909, sole driver, 2026-10-02)

The owner's main use case ("users primarily want a temporal view of EU texts") over the EU index's states
table (schema 5, PR #905): every work the publisher's census discovered for a seed act, the original and
each consolidated version, with the publisher's consolidation date or a typed reason it has none.
- `timeline`, `as_of` and `evidence_bundle` at any date answer EU acts from it (`V3CorpusMount.EuropeTime.cs`);
  `verify` verifies a consolidated wording's permalink; `dossier` gains `wording_timeline`, and a
  consolidated version's work IRI, CELEX or expression names its act's dossier. `consolidations_held` and the
  `later_wordings` rows of dossier and search say what the mount holds. An EU index with no states table
  keeps the original-wording path (PR #903) and the mode refusals.
- The reading screen reads the time view's bundle: each wording headed as the original or the consolidated
  wording of its date, with the dates it answers ("to the day before {next}") or that it is the latest held.
- Driver decision (reversible), the selection rule: per language, the latest dated wording at or before the
  date answers until the next; works sharing a date answer only if every text the corpus holds for that date
  is the same (the work whose CELEX names that version represents them); two different held texts are
  refused `ambiguous_version`; a same-date or undated version whose text is not held is disclosed beside the
  answer; an undated version makes the dates ambiguous only when a held text of it differs; a date after the
  latest consolidation is answered by it as dated, and `not_held` says an amendment the publisher has not yet
  consolidated is not seen. An EU date is a wording date, never an applicability date.
- Real data that shaped it: the real consolidated GDPR mount (`C:\lex-v3\lu-consolidated-offline-20261001-3`)
  holds the original (2016-04-27) and four works dated 2016-05-04, one with CELEX `02016R0679-20160504`, two
  holding byte-identical English and French text; the GDPR from 2016-05-04 answers with the CELEX-designated
  consolidation and discloses the other three.
- Evidence: `V3CorpusEuropeTimeViewTests` on a mount derived from the consolidated fixture (the timeline by
  CELEX and by consolidated work, as_of between, after and before the wordings, the bundle and its verified
  permalinks, the original pin, a tampered digest, the dossier by consolidated CELEX); the EU mount and mode
  tests restated; the answer and envelope samples re-rendered (only the EU dossier changes).
- Review of #909 (independent Claude review), repaired:
  - the bundle now discloses, beside each wording, the works sharing its date and the versions with no
    usable date (`same_date_works`, `unplaced_versions`), and the reading screen says them;
  - the time view serves its own statements: the bundle's scope (`EuropeTimeEvidenceBundleScope`), the digest
    rule naming the seed act's CELEX (`EuropeStateDigestRule`), the dossier timeline's own date semantics, and
    the bundle's and verify's full not-held rows; the reading copy no longer calls the next wording "held";
  - `text_held` says whether a text is held for the date (it was false on a date holding two different texts);
    verify on such a date refuses `ambiguous_version` rather than naming one text as current;
  - the EU `text_not_available` payload is Luxembourg's shape, which the reading card admits, and an EU
    `ambiguous_version` carries the registry's fields with its candidates pinned (an undated one by its
    expression); the reading screen says an EU ambiguity in its EU sentence and shows no Luxembourg card;
  - `V3CorpusEuropeTimeViewBranchTests` cover the branches on mounts from a generalised consolidated fixture
    (`EuConsolidatedWorksFixture.cs`): works sharing a date with one text (the CELEX-designated work answers,
    the others disclosed), with two texts (ambiguous, none chosen, not by verify either), the latest wording
    with no text in a language (`text_not_available` there, the other language answering), and undated
    versions (disclosed when their text is the same, ambiguous at every date when it differs).
- The GDPR outcome above was read from the real consolidated mount's index by inspection, not by a test; a
  real-mount test over the full EU population follows when it lands.
- Not yet: EU `article_history`, `diff` and `changes_in_period` (the history, compare and radar screens'
  EU paths).

## Next, in order (web lane; the data lane's items 1 to 3 are in STATUS-DATA.md)

The web lane's order since the owner's proxy's journey corrections (2026-10-01 14:30 UTC,
`C:\lex-v3\lanes\STANDING-ORDERS.md` section 7, overriding the queues below):
1. Pay the merge debt: this STATUS split (PR #902: the lane files and the tree allowlist), with the deployment
   kit's browser probes (#901) folded in; #893 rebased and merged; #891 and #897 merged.
2. Serve EU text now from what the EU index holds: EU `evidence_bundle` and the EU reading screen over
   the original wording, carrying "© European Union, https://eur-lex.europa.eu" and the statement that
   only the electronic Official Journal is authentic (Decision 95), with every journey citation
   verified.
3. The EU time view (`as_of`, `timeline`, history, compare, radar) on the data lane's EU states table
   as it lands, the API and screens built against its schema early.
4. When the queue is clear: the image-only annex control case (search, quote and export keep it out,
   officially linked; a real candidate is the 2024/1620 annex the three-seed retry retained), and the
   J1 to J8 mapping.
A pull request that does not move a launch-contract line, or directly unblock one, waits. Merges take
turns through `C:\lex-v3\lanes\MERGE-LOCK`, and related work goes in one bigger pull request.

The web lane's next order, from the panel's answer to Q-20261001-1316-claude (2026-10-01 13:20 UTC),
which follows the items below:
1. **Real chained canary builds, authorised.** They run after the data lane's active EU acquisition
   ends:
   - bounded EN/FR canary scope through `Lex.V3.Tool` (GDPR, `32016R0679`, and the Luxembourg act
     range a439), at most 800 publisher requests per build, under every standing rule;
   - the 2026-09-30 canary mount is schema 6, which `LuxembourgIndexPredecessor.TryRead` refuses as a
     predecessor, so build A is a fresh build from current integration and build B is chained to it:
     two runs, at most 1,600 requests;
   - both mounts and their custody are kept as evidence;
   - the journeys, the gates, G1/G3/G4, `events`, `answer_drift`, `as_observed` and a release
     rehearsal then run over the chained mount.
   The launcher `C:\lex-v3\lanes\claude-chained-canary.ps1` is waiting for the local heavy-job slot.
   It builds the tool once from a clean `origin/v3/integration`, freezes it with the renderer sources
   and their digests, then runs both builds into `C:\lex-v3\chained-canary-20261001\{a,b}`. Its
   ledger is `C:\lex-v3\lanes\claude-web-notes.md`.
2. **Then the release path's custody half**, once the data lane names its offline derive command:
   the release command runs it twice, compares every digest, then images, signs with the rehearsal
   identity, publishes and reads back. If the command is not named by the time item 1 is done, the web
   lane asks in QUESTIONS.md.

The web lane's order, from the panel's answer to Q-20261001-0345-claude (2026-10-01 03:50 UTC),
recorded by PR #862:
1. **Predecessor chaining, claimed by the web lane** (item 4). It unblocks G1, G3, G4, `as_observed`
   and `knowable_on`. The web lane changes only the event-log and chaining parts of the builders,
   rebases onto the data lane's merges before every pull request, and gets a Codex review of each.
   Any overlap with an open data-lane pull request goes to QUESTIONS.md rather than being edited
   around. The planned slices, one pull request each:
   - index schema `/7` with the genesis log, an `observations` table and a log stamp (PR #864);
   - the predecessor as a build input (`--predecessor`), its log carried forward as an exact
     prefix (G3a) (PR #866);
   - comparison events: `first_sighting` and `expression_added` for new keys, `file_replaced` when a
     source body changes (G1), and the derived `interval_closed` and `validity_revised` (PR #867);
   - `events` and `answer_drift` across the chain (folded into PR #867 by its review);
   - each build's time in the log (PR #871), then `as_observed` by build snapshot (G4) (PR #874);
   - a generation mount (G3b), retained by S7-A09: generations written and verified (PR #880),
     held to the log by the mount with the depth reported (PR #885), and quoted by `as_observed`
     and `verify` (PR #889).
   `observed_from` stays null until a Luxembourg body's capture time reaches the corpus (data lane);
   `knowable_on` and withdrawal follow it. The owner questions (G1 when a file changes but its text
   does not, "as observed" identified by snapshot with no time, retaining every generation) were
   answered by the panel on the owner's behalf; PR #871 records the answers.
2. The index capability manifest's per-operation rows for the unserved operations: one small
   additive pull request (PR #891).
3. A credential-free deployment kit, so the owner's go-live is one command (item 7; PR #890). It holds the
   Azure definitions for the one-server container, and a deploy script that takes the subscription,
   the managed identity and the signing identity as parameters and never reads or stores a secret.
   It validates the templates offline, runs the zero-traffic probe against the deployed revision,
   removes the revision, and has a runbook here. No Azure login, deployment or production signing:
   those stay with the owner. Kit 2 adds the browser probes against the candidate (`--browser`, the
   journey's ten real-mount steps), states that privacy is not observable on a remote revision, and
   prints rollback and forward again.
4. Standing: the real-mount journeys and gates on every new mount the data lane builds, and a
   review of the data lane's pull requests that touch the API when asked.
   - 2026-10-01 (PR #893): the two data-lane mounts newer than the real bounded first mount. Both
     are schema 6, and #878 serves them again.
     - Mounts:
       - `C:\lex-v3\eu-three-seed-retry-20260930-1\v3-corpus`: 58 members; its EU index holds the
         SFDR, 32019R2088;
       - `C:\lex-v3\bounded-en-fr-canary-20260930-1\v3-corpus`: 18 members; the GDPR in English and
         French.
     - The mounted gates (`V3MountedGatesTests`) and the EU permalink check pass 21 of 21 on each,
       and on the first mount.
     - The EU permalink check searched the GDPR in English, which only some mounts hold. It now takes
       a work, its language and a word from the named mount's own EU index, and verifies every hit's
       permalink in that language. On the three-seed mount that is a French SFDR search.
     - The real-mount journey (`journey.mjs --real-mount --served-by-api`):
       - three-seed mount: 10 of 10 steps pass. Coverage and radar answer, and the other screens
         show their refusal cards; the two EU steps ask for the GDPR, which this mount does not hold;
       - canary mount: 10 of 10 steps pass; coverage and radar answer, the Luxembourg screens show their refusal cards, EU search answers with 61 citations verified, and EU dossier with 2, one per held expression (English and French).
   Reversible driver decision, 2026-10-01: with items 2 and 3 in review, the web lane runs replay G1,
   G3 and G4 on the real handler (PR #897), the launch contract's machine-gates line. The custody
   half of the release path's first line waits for the data lane's offline derivation.

The plan, in order (its items 1 to 3 are the data lane's, in STATUS-DATA.md):

4. Serve the unserved operations for Luxembourg (`verify` and `relations` served by PR #753, MCP
   over streamable HTTP by PR #754, `evidence_bundle` by PR #755, `classification` and
   `manifestation` by PR #756, `status_on` and `browse` by PR #757; a request to an unserved
   operation answers the transport failure `operation_not_served`, PR #758; `ask` answers the
   contained `assistant_v3_unavailable` card, PR #759; `events` and `answer_drift` over a genesis
   log, PR #760; `as_observed` by build snapshot, PR #874). Three remain (`knowable_on`,
   `concepts`, `transposition`), all needing data the ingest does not produce; they keep `operation_not_served`, and since PR #857
   the coverage answer names, for each, the data that would serve it (driver decision, below), which
   Trust and Coverage shows. The event log's next step, predecessor chaining with observation
   times, is the web lane's since 2026-10-01 and is claimed by PR #862 (the web lane's order,
   above).
5. EU parity: every temporal and search operation from the EU index; French expressions. EU
   `search` in one work served by PR #761, EU `dossier` by PR #762. EU `evidence_bundle` and the EU
   reading screen over the original wording do not wait on consolidation: the EU index already holds
   the original wording, so they are the web lane's next slice (the owner's proxy, 2026-10-01 14:30
   UTC, correcting this item, which said they waited on consolidation acquisition). The EU time view
   (`as_of`, `timeline`, history, compare, radar) waits on the data lane's EU states table. The parity
   details are driver decisions (below). The EU permalink grammar is the web lane's since 2026-10-01 (the panel's answer
   to Q-20261001-0108-claude). It works in the API and the web; the EU index schema and the Ingest
   builders stay the data lane's. PR #850: EU hits carry a hash-pinned permalink, and EU `verify`
   is served over it. PR #853: the search screen reads and shows EU answers in one named work. PR
   #854: the journey searches the real mount's GDPR, and `verify` confirms all 61 EU citations the
   page prints. PR #856: the EU dossier pins each expression's wording, and the dossier screen and
   journey show it. The EU screens that need dated states (history, compare, radar, and reading at a
   date) wait on the data lane's EU states table, as above.
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
   the accessibility and scope line held on the live screens; PR #813: a reviewed language builds
   its own pages; PR #815: the eight screens against the real bounded mount; PR #834: the
   licence-blocked journey. Next: the
   owner's review of the French drafts (`node web/scripts/live-chrome-fr-draft.mjs`), then the
   reviewed table in `LIVE_CHROME` and `fr` in `REVIEWED_CHROME_LOCALES`, which builds `/fr/*.html`. French ships only once
   reviewed (Decision 41). Hosting (ruling 3): `Lex.V3.Api`
   serves the live pages on the API's origin with `frame-ancestors`, HSTS and `Referrer-Policy`,
   and a live page never shows the synthetic banner on a real mount. J1 to J8 are restated as V3
   steps by the driver (they exist only in the pre-V3 pack, `05-user-journeys.md`).
7. Release pipeline: build, sign, image, zero-traffic deploy, probes. Then acceptance and promotion.
   The steps that need no production credential are the web lane's since 2026-09-30 (the owner's proxy).
   PR #821: one command builds, verifies, rehearsal-signs and removes the one-server image; PR #825:
   it runs the image (WSL, read-only root, private /tmp) and probes the eight screens against it; PR
   #826: the image is reproducible (two builds from scratch, one manifest digest); PR #828: the
   image probed on the journey's fixture mount, where every screen answers and 105 citations verify;
   PR #831: V2 absent from the image (no V2 assembly or dependency; of V2's 63 routes, a V2-only
   one answers 404 and a path both share answers with V3's own bytes); PR #833:
   the release assets (the image, its signature, the evaluation card, the mount's report, a signed
   manifest) published under a version, read back and verified, and the card served at
   `/evaluation-card.json`. The credential-free release steps
   are rehearsed end to end, and since 2026-10-01 over the real bounded first mount too (PR #862).
   Next in the web lane's order: a credential-free deployment kit. Production signing, credentials
   and deployment stay with the owner.
8. Machine gates (launch contract, Evaluation): the temporal, refusal and retrieval case sets run
   against the real handler, and all three shuffled controls are caught (PRs #767 and #768). The
   evaluation card is rendered from them with the statistical rows `not_yet_labelled` (PR #769).
   Ruling 2: it is served on the Trust and Coverage page (PR #792) and beside the release assets, and the
   launch card carries the machine gates run over the real mounted corpus, so the gates gain a
   mounted-corpus run once the first mount exists. Replay G1 to G5 (`33-product-spec.md`): G2 snapshot
   determinism and G5 independent verifiability run on the real handler (PR #770). G1 version
   immutability, G3 bitemporal completeness and G4 as-observed answering run on the real handler
   over a three-build chain (PR #897), now that predecessor chaining is merged. What
   is left of the launch contract's machine-gates line after that was "V2 absent from the image",
   which the image rehearsal now checks (PR #831, item 7). Ruling 2's gates over the real mounted
   corpus: PR #838 derives the temporal set from any mount, PR #839 the refusal set, PR #842 the
   retrieval set, and PR #844 makes the release card the gates run over the rehearsal's own mount.

## Driver decisions (reversible)

Each is the driver's call under ruling 7 and can be reversed by a later pull request that says why.

- The sole-driver period (the owner's order of 2026-10-02: one Claude driver, no Codex, no other
  Claude, full authorisation, never ask the owner): each pull request is reviewed by an independent
  Claude subagent with a fresh context, read-only, reporting only material and reproduced findings
  (`C:\lex-v3\lanes\claude-review-instructions.md`); one repair round, then merge on green CI. The
  owner's open questions are decided by the driver under that delegation and recorded here.
- The six refusal codes no operation produces at launch (the refusal census's `not_produced` list),
  each unreachable by construction, decided under that delegation (2026-10-02). The launch contract's
  refusal line holds them in the closed registry with their payloads; the refusal case set measures the
  fourteen produced codes.
  - `advice_boundary`: no model plans an answer (`ask` answers the containment card
    `assistant_v3_unavailable`, PR #759), so no question is ever classed as legal advice; a future
    assistant's planner produces it.
  - `derivation_refused`: the API serves no derived text and no operation derives at request time;
    derivation happens at build time, where a refusal is a failed build, never a served answer.
  - `not_transposable`: `transposition` is unserved (`operation_not_served`, with the data that would
    serve it in `coverage`, PR #857); a served transposition answers it for an instrument that is not
    a directive.
  - `out_of_corpus_scope`: an identifier neither index holds answers `identifier_unknown`, naming this
    service's own `search` as the way forward (`official_search_actions`); a list of publishers known to
    be out of scope (a CSSF circular, a court decision) would produce it, and none is kept at launch.
  - `rate_limited`: the one-server host applies no per-client limit at launch, since it keeps no IP
    address or user agent (the launch contract's privacy line). Cost under abuse is bounded by one
    replica per revision (`minReplicas` and `maxReplicas` 1 in `deploy/main.bicep`); the kit runs in
    multiple-revision mode, so each active revision runs its own replica. A per-client limit that needs
    no identifying record is follow-on work.
  - `upstream_unreachable`: serving never contacts a publisher: the API holds no HTTP client, and the
    only publisher client (`RoutedHttpAcquisitionSession`) is built by acquisition, which the API never
    references; a mount is served from its own files, so no upstream can be unreachable at request time.
- The rights question PR #842 surfaced, decided under that delegation (2026-10-02): a licence that does
  not admit a text keeps it out of search matching too. A search hit says which articles hold a word,
  which is information read from the text; `search` now matches only states whose text
  `evidence_bundle` would quote (every source acquired and admitted by its rights), and states that
  rule (`SearchRightsRule`). It counts nothing about the text it does not match, since a count of
  withheld matches would say the same thing. The search page says the same: its intro, its prompt and
  its no-hit sentence speak of the text this server holds and may search, and the intro says text its
  rights withhold is not searched (review of #910: on the licence-blocked journey the old no-hit
  sentence said the held text lacked a phrase it holds). The French draft says the same.

- The image rehearsal (PR #821):
  - It builds with the .NET SDK's container support, needing no daemon: this machine has no
    container runtime, and the base image stays pinned by digest in the project.
  - It bakes the mount into the image, so the image digest pins the corpus it serves.
  - It signs with a key made for the run and never kept, labelled a rehearsal. The release identity
    is the owner's.
  - Running the image comes next: the image's own filesystem under this machine's WSL Ubuntu, with
    no install and no daemon. The alternative, a CI runner with Docker, is kept for when a hosted
    run is wanted.
- The reproducible image (PR #826):
  - The app layer is stored uncompressed, so its digest depends only on its tar and not on the
    compressor a machine has (gzip output depends on the zlib build). The cost is size: 17 MB against
    the SDK's 6 MB gzip on the bounded mount. A pinned compressor can replace it if the full corpus
    makes the size matter.
  - The source date is the commit's time (the `SOURCE_DATE_EPOCH` convention): the image says when
    its sources were committed, not when it was built. The product reads no file modification time
    (`V3WebRoot` holds the pages in memory), so the choice changes no answer.

- The EU search screen (PR #853): an EU answer whose hits pin no wording is said as unreadable, not
  shown unpinned, since every citation the live screens show must pin its digest. An EU hit is a
  "wording of" its date, and the French draft calls the one held wording a "libellé".
- The EU permalink grammar (PR #850): language as a path segment, not only a parameter, so one
  grammar serves English and French expressions. The wording digest is computed in the API (the
  panel's boundary), not stored in the EU index. If the data lane later stores a wording digest, the
  API can read it instead, as long as the value stays the same.

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
  from a retired log refuses `snapshot_unknown`. Amended by PR #867: a build chained to a log carries
  it forward with the same numbers, so a cursor of an ancestor the mounted log names
  (`log.ancestors`) reads on; a log not chained to the mounted one is still retired.
- The build time of the event log (PR #871) is an explicit build input, which the first-mount
  build reads once from its clock and passes to both index builds. The builder never reads it
  itself.
  - The launch contract's two-build line still holds: two independent executions given the same
    inputs, the build time among them, give the same bytes. A build that must reproduce another
    takes the other's time.
  - Rejected: the predecessor's build-report time. It is not hashed, anyone can edit it, and it
    would leave the mounted generation with no bound.
- `ask`'s containment card keeps the `point` verdict.
- The operations with no data (`knowable_on`, `concepts`, `transposition`; `as_observed` until PR #874) keep
  the typed transport failure `operation_not_served`, and the platform states, per operation, that
  it is not served and which data would serve it. PR #857 states it in the `coverage` answer
  (`operations.not_served_data`), which the API owns, rather than in the index capability manifest,
  which the data lane's builders write. The data named are the specification's own
  (`33-product-spec.md`). The index manifest can carry the same rows later without changing them.
  PR #891 makes it so: the table is `V3UnservedOperations` in Contracts, which the API's `coverage`
  reads and both index builders state in every capability manifest they write (`not_served`).
  - Each row states the operation, the typed reason a request for it answers (`operation_not_served`,
    the transport failure; any other reason is refused) and the data that would serve it. The reason
    was added by the review of #891, since the launch line asks for the reason to be in the manifest.
  - The manifest schema stays `/1`: the field is additive and written only when stated, so an
    earlier manifest keeps its exact bytes and reads back with none. That covers the real bounded
    first mount's manifest and the data lane's running EU population's.
  - The reader's canonical re-render holds either shape exact. Malformed rows refuse
    `malformed_not_served`, and an empty list or rows out of order are not canonical.
  - A manifest's rows are what the platform did not serve when it was built; `coverage` keeps
    reporting today's routes.
  - With this, the launch line "all 27 registered names, each either served or refusing with a typed
    reason its capability manifest states" holds on a fresh build: the three unserved operations
    answer `operation_not_served`, and each manifest states that reason and the data that would serve
    them.
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

## Waiting on others

- The bounded real mount is available at
  `C:\lex-v3\first-mount-decision95-restart-20260930\v3-corpus`; the web lane's journeys run on it
  (PR #815). Its Luxembourg index is schema 6. The reader refused it from #864 until PR #878, which
  serves it with its build record absent. The machine gates derive their cases from any mount (PRs #838, #839, #842, #845, #846)
  and the release card is the gates run over the rehearsal's mount (PR #844). The real mount holds
  no Luxembourg state yet, so its temporal set is not measured, and it will be once the data lane's
  full populations land.
- EU search on the live search screen waits on an EU permalink grammar (item 5). The real mount's
  EU index answers `search` in one work (for example `32016R0679`, 60 hits for "personal data"), and
  the web search reader refuses the answer by design, reading Luxembourg's only. Each EU hit cites
  its expression IRI and provision (`…/3e485e15-…-01aa75ed71a1.0006#lex-provision=001`), not a
  hash-pinned permalink. A screen showing those citations would break the launch contract's first
  promise, which the journey holds. Once the grammar exists: the search screen gains the optional
  work identifier, the reader reads the EU answer, and the journey searches the EU work on the real
  mount.

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

- The corrigendum tripwire classifies a French corrigendum as `within_served_body_languages` while no
  French body is served (Decision 89 section 4). True once the French expressions land.
