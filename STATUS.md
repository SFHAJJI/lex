# Lex V3 status

Updated 2026-09-27 by the driver. This file replaces the issue-comment ledgers. It is rewritten in
every pull request that changes what is served, what is next or what is blocked.

## Heads

- `v3/integration`: `159a3979` (2026-09-27, PR #745 merged). Build 45 s. Fast lane
  (`eng/test-fast.ps1`): 3,025 tests pass, 1 skipped, 55 s. Ingest suite: the CI `dotnet` job runs
  it on every pull request (about 15 min); the local full run on 2026-09-27 was stopped by the
  harness when the disk reached 0.2 GB free, before the cleanup below. 771 web tests pass.
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
- **EUR-Lex legal notice (Decision 88): producer built, fixtures only.** `EuLegalNoticeRouteProducer`
  issues the one GET through the acquisition session under a new source profile
  (`european_union_legal_notice`) and hands back the route under the corpus run identity, which is
  what the Stage 3 envelope checks. Live facts observed 2026-09-27 and pinned in the profile:
  `GET https://eur-lex.europa.eu/robots.txt` answers 200 directly, with no `Content-Type` header
  and `Crawl-delay: 10`; the notice path is not disallowed. Unknown until the bounded live run:
  whether the notice page itself answers 200 `text/html` to the `Lex/0.1` user agent behind the
  EUR-Lex WAF (Decision 23). A challenge page, a block, an off-origin redirect and a truncated body
  are all the typed refusal `notice_route_invalid` (`EuLegalNoticeEvidence.FromRoute` now requires
  a complete route; review finding on PR #746).
- Decision taken by the driver, reversible: the legal-notice route follows same-origin redirects
  (as `FromRoute` already admitted and as the Cellar route does); the terminal is pinned to the
  host and port and robots is evaluated for the redirected path. Decision 88's "one GET" is read
  as one logical request whose hops count against the wire budget. Say the word and the profile
  refuses redirects instead.
- The session now exposes the custody write receipt of every hop an executed attempt sealed
  (`HopWriteReceiptsByObservationId`), which the Formex ZIP binding needs as well.

## Next, in order

1. The remaining corpus-build pieces, tested on fixtures first: the Formex package population per
   expression (enumeration, ZIP acquisition, annex inventory, one outcome per expression,
   reconciliation), then the `src/Lex.V3.Tool` `build` verb composing the envelope and writing
   `lex-corpus-6.json`, the indexes and the capability manifests.
2. One bounded live run (authorised by the owner on 2026-09-27): one EU work in EN and FR with its
   Formex packages and the legal-notice GET, plus one Luxembourg act. Produces the first real mount.
   Then decide whether the one-process design carries the full population or needs a
   serialisation boundary between acquisition and build.
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
  `C:\lex-v3\eu-population-run-1..9` and the other run and evidence directories under `C:\lex-v3`.

## Known defects carried

- `V3PlatformSchemaTests` renders the tracked schemas and then verifies them when
  `V3_RENDER_PLATFORM_SCHEMAS=1`, so a render passes unconditionally. CI never sets it. Fix: fail after
  rendering.
- The corrigendum tripwire classifies a French corrigendum as `within_served_body_languages` while no
  French body is served (Decision 89 section 4). True once the French expressions land.
