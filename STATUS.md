# Lex V3 status

Updated 2026-09-27 by the driver. This file replaces the issue-comment ledgers. It is rewritten in
every pull request that changes what is served, what is next or what is blocked.

## Heads

- `v3/integration`: `acf1c5ce` (2026-09-27, PR #749 merged). Build 45 s. Fast lane
  (`eng/test-fast.ps1`): 3,025 tests pass, 1 skipped, 50 s. Ingest suite: green on CI for PR #749
  (the CI `dotnet` job runs the whole solution on every pull request, about 6 min on the runner);
  locally about 15 min. 771 web tests pass.
- Plan: `C:\lex-v3\V3-FINISH-PLAN-2026-09-27.md` (owner's copy). Decision 94 (one driver, one queue,
  one review per pull request) merged in lex-governance on 2026-09-27. Launch target 2026-11-07.

## Served today

REST at `/api/v3/`, from a mounted `v3-corpus` directory, **Luxembourg only**: `resolve`, `as_of`,
`timeline`, `article_history`, `diff`, `changes_in_period`, `in_force_on`, `search` (strict and relaxed
lanes), `coverage`, `provenance`, `dossier`, `citation`, `cited_by`. Without a mounted corpus every route
answers `no_corpus_mounted`. EU serves `resolve` only.

Registered and not served: `ask`, `answer_drift`, `as_observed`, `browse`, `classification`,
`concepts`, `events`, `evidence_bundle`, `knowable_on`, `manifestation`, `relations`, `status_on`,
`transposition`, `verify`. `ask` answers the typed `assistant_v3_unavailable` result by design until
after launch.

MCP: JSON-RPC dispatcher (`initialize`, `tools/list`, `tools/call` for `resolve`) exists as a pure
function; wired to no transport.

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
- **Formex package population: producer built (this pull request), acquisition deferred.**
  `EuFormexPackagePopulationProducer` runs the real manifestation enumeration for every expression
  of a complete run (all languages, one robots session and four requests each), closes eligibility,
  emits one typed outcome per expression (`not_eligible` where the office lists no `fmx4`;
  `refused/observation_not_executed` where it does, with a fixed deferral detail on the in-process
  outcome) and reconciles against the run. Proven on the harness run through the envelope and
  `LexCorpus6Builder`: a corpus builds from it, with EU records and no EU articles, which is what
  is served today. Stated exactly: in `lex-corpus-6.json` the held EU member's stage 3 outcome is
  `europe_formex_main_body` / `formex_main_body_package_refused` with no detail text, so a deferred
  acquisition is not distinguishable there from a real transport refusal. The first mount serves no
  stage 3 outcome; a typed deferred outcome member comes before any public claim rests on that
  field (next item 2). The builder test pins today's shape.
  Why no `acquired` yet (investigated 2026-09-27): a package needs the manifestation's Cellar
  items observed with their stream names, and no item enumeration exists; the transport binding
  pins a single-hop Item URI while the live `fmx4` route is a manifestation URI redirected to
  `{manifestation}/zip`; only ENG and FRA are addressable while every language needs an outcome;
  no outcome member names a deferred or scope-excluded acquisition. Each is a contract change.
- Decision taken by the driver, reversible: build and mount the first corpus with Formex deferred,
  then build Formex acquisition as its own slices (item enumeration, binding change, a typed
  deferred outcome). The plan's largest risk is the real build, and it does not need EU articles to
  be retired.

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

## Next, in order

1. The bounded live run (authorised by the owner on 2026-09-27) with the tool: one EU work in EN and
   FR (the manifestation enumerations and the legal-notice GET; no Formex package request), one
   Luxembourg act with a consolidated publisher PDF. Record the wire counts, the refusals met and
   the five digests in STATUS.md; mount the directory under the API and answer `resolve` from it.
   Then decide whether the one-process design carries the full population or needs a
   serialisation boundary between acquisition and build.
2. Formex acquisition (EU parity): the Cellar item enumeration per manifestation, the transport
   binding for the manifestation-level `fmx4` route, the ZIP GET with the receipts door, a typed
   deferred outcome; then `acquired` outcomes feed the main-body producer and the EU index.
3. Define and run the Luxembourg population and the complete EU population (owner authorisation per
   run).
4. Serve the fourteen unserved operations for Luxembourg; wire MCP to a streamable HTTP endpoint.
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
