# Lex V3 status

Updated 2026-09-29 by the driver. This file replaces the issue-comment ledgers. It is rewritten in
every pull request that changes what is served, what is next or what is blocked.

## Heads

- `v3/integration`: `cd478f3c` (2026-09-29, PR #758 merged). Build 45 s. Fast lane
  (`eng/test-fast.ps1`): 3,035 tests with PR #759, 3,034 pass, 1 skipped (PR #759 adds one and
  rewrites two). Ingest suite: green on CI for PR #758 (the CI `dotnet` job runs the whole solution on every
  pull request, about 6 min on the runner); locally about 15 min. 771 web tests pass.
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
`browse` (PR #757), and `ask` as the contained assistant (PR #759). Without a mounted corpus every
route answers `no_corpus_mounted`. EU serves `resolve` only.

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
and the observation digest. The index schema is `lex-v3-luxembourg-index/5` (`user_version` 5; the
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

Registered and not served: `answer_drift`, `as_observed`, `concepts`, `events`,
`knowable_on`, `transposition`. Since PR #758 a request for one of them is the typed
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
endpoint test proves it for all twenty-one. The launch-contract line "REST and MCP derive identical
envelopes from the registry" is the owner's to tick.

Web: 28 React components, 771 tests; no call to `/api/v3` yet. Screens render fixtures.

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
  acquisition runs and 619 durable write receipts. The tool's "spent 619" equals the receipt count,
  not the request count, which the fail-fast slice checks. Memory was not a problem this time (the
  process stayed near 90 MB). Because the build is one process, the acquisition is spent and nothing
  was written; the Luxembourg act was never reached.
- **What this means.** Decision 88's one legal-notice GET cannot succeed from an automated client
  while EUR-Lex challenges it, and `LexCorpus6Builder` refuses the whole corpus without that
  evidence (`EuropeRightsEvidenceMissing`), so no mount, Luxembourg included, can be built. Two
  fixes, neither built yet:
  1. Fail fast (driver's call, no scope change): send the legal-notice GET first, before the EU
     acquisition, so a challenge costs one request instead of the whole EU acquisition.
  2. A way past the missing notice (owner's call, it touches Decision 88): either (a) the builder
     accepts a typed `eu_rights_evidence_unavailable` disposition and withholds every EU body as
     text while still serving EU identity and `resolve`, which keeps rights fail-closed and lets the
     Luxembourg side mount; or (b) the rights evidence comes from the same Commission reuse policy
     (Decision 2011/833/EU) at an address that does not challenge, such as the Publications Office
     legal notice, which needs a numbered Decision replacing Decision 88's exact URL. The driver
     recommends (a) now and (b) later. Sending a browser user agent or solving the challenge is not
     an option: it would evade the publisher's protection.

## Next, in order

1. Unblock the first mount after the EUR-Lex challenge (see Data, attempt 2): fail fast first (the
   driver's next slice, no publisher traffic, with the spent figure checked against the requests
   custody holds), then option (a) or (b) as the owner rules. Then the bounded live run again with
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
   contained `assistant_v3_unavailable` card, PR #759). Six remain, all needing data the ingest does
   not produce. `events` and `answer_drift` are launch-contract lines of their own ("append-only log,
   cursor polling at-least-once, `answer_drift`"), so they must be served, not refused: next, the
   driver designs the event log over what a build holds (a single build has no revision to report,
   so the design question is what the first event is). For the other four (`as_observed`,
   `knowable_on`, `concepts`, `transposition`) there is an owner question below.
5. EU parity: every temporal and search operation from the EU index; French expressions.
6. Wire the eight launch screens to `/api/v3`; journeys J1 to J8 in a real browser.
7. Release pipeline: build, sign, image, zero-traffic deploy, probes. Then acceptance and promotion.

## Blocked or waiting on the owner

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
