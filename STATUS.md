# Lex V3 status

Updated 2026-09-27 by the driver. This file replaces the issue-comment ledgers. It is rewritten in
every pull request that changes what is served, what is next or what is blocked.

## Heads

- `v3/integration`: `b15e40f4` (2026-09-28, PR #755 merged). Build 45 s. Fast lane
  (`eng/test-fast.ps1`): 3,032 tests pass, 1 skipped, 45 s. Ingest suite: green on CI for PR #755
  (the CI `dotnet` job runs the whole solution on every pull request, about 6 min on the runner);
  locally about 15 min. 771 web tests pass.
- Plan: `C:\lex-v3\V3-FINISH-PLAN-2026-09-27.md` (owner's copy). Decision 94 (one driver, one queue,
  one review per pull request) merged in lex-governance on 2026-09-27. Launch target 2026-11-07.

## Served today

REST at `/api/v3/`, from a mounted `v3-corpus` directory, **Luxembourg only**: `resolve`, `as_of`,
`timeline`, `article_history`, `diff`, `changes_in_period`, `in_force_on`, `search` (strict and relaxed
lanes), `coverage`, `provenance`, `dossier`, `citation`, `cited_by`, `verify` (PR #753: a
hash-pinned permalink verified against the state digest the index holds; a work identifier or a
stable coordinate answers the current digests), `relations` (PR #753: the one edge table in both
directions, `cites` edges only, what `citation` and `cited_by` serve as one ordered, paged list),
`evidence_bundle` (PR #755), and since this pull request `classification` and `manifestation`.
Without a mounted corpus every route answers `no_corpus_mounted`. EU serves `resolve` only.

`classification` and `manifestation` (this pull request) read a new index table, `work_facts`: the
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
fixture that does).

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

Registered and not served: `ask`, `answer_drift`, `as_observed`, `browse`, `concepts`, `events`,
`knowable_on`, `status_on`, `transposition`. A request for one of them is the transport failure
`unknown_route` (HTTP 404, no envelope); `coverage` names them. `ask` answers the typed
`assistant_v3_unavailable` result by design until after launch. Survey of 2026-09-28 (read-only):
`status_on` and `browse` read the `work_facts` table this pull request adds (in-force status and
dates, document types across works); the rest of the original note stands: the four needed a new
index table over Stage 3 inputs the ingest already holds (`TypedAssertions`: in-force status and dates, `typeDocument`, `isEmbodiedBy`,
`userFormat`), which bumps the index schema; `concepts`, `transposition`, `as_observed`,
`knowable_on`, `events` and `answer_drift` need data the ingest does not produce.

MCP: served over streamable HTTP at `POST /mcp` (PR #754), the request half of the
transport: one JSON-RPC message in, one JSON document out (`initialize`, `tools/list`, `tools/call`;
a notification answers 202 with no body; a JSON-RPC error is an HTTP 200; a GET is 405; a body over
the platform's 1 MiB ceiling is 413 before parsing; no server-initiated stream is offered). One tool
per served REST operation, named by its operation id, whose `inputSchema` is the reviewed request
schema's `parameters` shape, and whose call runs the same dispatch the REST route runs
(`V3ApiHandler.ExecuteFor`), so the two transports answer one envelope for one request; the
endpoint test proves it for all eighteen. The launch-contract line "REST and MCP derive identical
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
- **The bounded live run: started 2026-09-27 21:26 UTC, stopped by the session harness, not
  restarted.** With the tool at `8ec1fdb8` (GDPR `32016R0679`; the Luxembourg act
  `loi/2017/03/14/a439`, range `.../a439/jo` to `.../a439/jp`; custody
  `C:\lex-v3\first-mount-run-1\custody`; ceiling 800). It held the eight renderer sources and was
  inside the EU adapter run when the Claude Code harness killed the process at 21:27:47 UTC because
  the machine was critically low on memory (1.1 GB free of 15.7 GB, held by other applications);
  473 custody files (5.1 MB) in 94 s, no milestone line reached, no wire count, refusal or digest
  to record. The harness asks that a process it stopped for memory pressure is not restarted
  unprompted, so the driver did not. To run it: free memory (or start the CLI with
  `CLAUDE_CODE_DISABLE_BG_SHELL_PRESSURE_REAP=1`), then from `C:\lex-v3\first-mount-run-1` run the
  command recorded on PR #750, logging to `build.log`; with this pull request merged the run also
  acquires the GDPR Formex package (about three more requests per held expression). The run
  record (wire counts, refusals, five digests, `resolve` from the mount) is still item 1.

## Next, in order

1. The bounded live run (authorised by the owner on 2026-09-27) with the tool: one EU work in EN and
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
   `manifestation` by this pull request; nine remain, see Served today): next `status_on` and
   `browse` over the `work_facts` table (`status_on` needs a fixture with in-force facts); then a
   typed answer for a request to an unserved operation (today the transport failure
   `unknown_route`).
5. EU parity: every temporal and search operation from the EU index; French expressions.
6. Wire the eight launch screens to `/api/v3`; journeys J1 to J8 in a real browser.
7. Release pipeline: build, sign, image, zero-traffic deploy, probes. Then acceptance and promotion.

## Blocked or waiting on the owner

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
