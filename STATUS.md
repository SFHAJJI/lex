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

## Next, in order

1. `lex-v3 build` command: load a population run directory into the Stage 3 envelope, build
   `lex-corpus-6.json`, both indexes and both capability manifests into a mount directory, and mount
   them in the API. Prove it on the EU run-9 data first.
2. Define and run the Luxembourg population (needs the owner's run authorisation).
3. Serve the fourteen unserved operations for Luxembourg; wire MCP to a streamable HTTP endpoint.
4. EU parity: every temporal and search operation from the EU index; French expressions.
5. Wire the eight launch screens to `/api/v3`; journeys J1 to J8 in a real browser.
6. Release pipeline: build, sign, image, zero-traffic deploy, probes. Then acceptance and promotion.

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
