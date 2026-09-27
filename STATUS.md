# Lex V3 status

Updated 2026-09-27 by the writer seat. This file replaces the issue-comment ledgers. It is rewritten in
every pull request that changes what is served, what is next or what is blocked.

## Heads

- `v3/integration`: `715d0f8d` (2026-09-25). Build 45 s. 4,863 .NET tests pass, 19 skipped live gates;
  771 web tests pass. Full .NET run 15 min, ingest suite 14 min 49 s.
- Plan: `C:\lex-v3\V3-FINISH-PLAN-2026-09-27.md` (owner's copy). Launch target 2026-11-07.

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
  legal-notice evidence. Their producers are unbuilt. A Luxembourg delivered run is mandatory in every
  envelope. Building the corpus therefore means a fresh, live, one-process acquisition run.

## Next, in order

1. `src/Lex.V3.Tool` `build` verb, plus two Ingest producers: the EUR-Lex legal-notice route bound to
   the corpus run identity, and the Formex package population per expression (enumeration, ZIP
   acquisition, annex inventory, outcomes). Tested on fixtures first.
2. One bounded live run (needs the owner's authorisation): one EU work in EN and FR with its Formex
   packages and the legal-notice GET, plus one Luxembourg act. Produces the first real mount.
   Then decide whether the one-process design carries the full population or needs a
   serialisation boundary between acquisition and build.
3. Define and run the Luxembourg population and the complete EU population (owner authorisation).
4. Serve the fourteen unserved operations for Luxembourg; wire MCP to a streamable HTTP endpoint.
5. EU parity: every temporal and search operation from the EU index; French expressions.
6. Wire the eight launch screens to `/api/v3`; journeys J1 to J8 in a real browser.
7. Release pipeline: build, sign, image, zero-traffic deploy, probes. Then acceptance and promotion.

## Blocked or waiting on the owner

- Adoption of the one-driver working model (plan section 4) and Decision 94 in lex-governance.
- Codex role: weekly cold read plus the two release gates, or none.
- Run authorisation: Luxembourg population; French EU expressions; the complete Stage 7 run.
- Azure production credentials and the signing identity, needed by week 5.
- Weekly 30-minute checkpoint slot.

## Known defects carried

- `V3PlatformSchemaTests` renders the tracked schemas and then verifies them when
  `V3_RENDER_PLATFORM_SCHEMAS=1`, so a render passes unconditionally. CI never sets it. Fix: fail after
  rendering.
- The corrigendum tripwire classifies a French corrigendum as `within_served_body_languages` while no
  French body is served (Decision 89 section 4). True once the French expressions land.
