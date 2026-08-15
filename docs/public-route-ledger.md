# Lex public route ledger

Status: normative target for the product architecture release

Every response inherits the application security headers. `public index` means the route may
appear in the sitemap and use an indexable canonical URL. `machine` routes are not indexed.
Dynamic publisher routes accept only mounted publisher IDs and validated work/date coordinates.

| Method | Pattern | Owner | Audience | Canonical behavior | Content | Indexing | Representative checks |
|---|---|---|---|---|---|---|---|
| GET | `/` | Home | legal reader | canonical `/` | HTML | public index | workspace shell succeeds with corpus; honest empty state without corpus |
| GET | `/browse` | Catalogue | legal reader | canonical `/browse` | HTML | public index | filters and pagination; invalid page is bounded |
| GET | `/go-asof` | Catalogue | legal reader | redirect to dated work | redirect | no index | valid coordinate redirects; invalid coordinate is bounded |
| GET | `/coverage` | Catalogue | reader, evaluator | canonical `/coverage` | HTML | public index | publisher counts and gaps; absent corpus disclosed |
| GET | `/in-force-on` | Catalogue | legal reader | none | HTML | noindex, follow | LU applicability and EU consolidation semantics remain distinct |
| GET | `/search` | Catalogue | legal reader | none | HTML | noindex, follow | bounded search; empty and invalid query states |
| GET | `/changed` | Catalogue | legal reader | none | HTML | noindex, follow | window, order, population and empty state |
| GET | `/find` | Catalogue | no-JavaScript reader | canonical `/find` | HTML | public index | form reaches search, date and changed routes |
| GET | `/{publisher}/{work}` | Documents | legal reader | canonical work URL | HTML | public index | timeline and text coverage; unknown work 404 |
| GET | `/{publisher}/{work}/{coordinate}` | Documents | legal reader | exact-version URL or friendly date resolver | HTML | public index | opaque version key opens exactly; a date renders one state with an exact canonical URL or returns `ambiguous_version` when several states cover it |
| GET | `/{publisher}/{work}/diff/{dateA}/{dateB}` | Documents | legal reader | canonical comparison URL | HTML | public index | comparable diff; profile mismatch refusal |
| GET | `/provenance/{*key}` | API/verification | evaluator | canonical proof URL | HTML | public index | proof chain; unknown lex ID 404 |
| GET | `/how-it-works` | Explainers | reader | canonical | HTML | public index | product method and limits |
| GET | `/built` | Explainers | evaluator | canonical | HTML | public index | interview-grade architecture overview and reading paths |
| GET | `/built/model` | Explainers | evaluator | canonical | HTML | public index | legal identity, two time axes and typed gaps |
| GET | `/built/data` | Explainers | evaluator | canonical | HTML | public index | official-only ingestion, derivation and cryptographic claims |
| GET | `/built/retrieval` | Explainers | evaluator | canonical | HTML | public index | top-1/top-k policy, failure taxonomy and evidence gates |
| GET | `/built/assistant` | Explainers | evaluator | canonical | HTML | public index | bounded plan, deterministic execution, memory and optional prose |
| GET | `/built/release` | Explainers | evaluator | canonical | HTML | public index | candidate, promotion, rollback, ACR and staging boundaries |
| GET | `/built/release/evaluation.json` | Explainers | machine, evaluator | exact runtime-bound evidence | JSON | machine | authenticated immutable-release identity and bounded signed report claims, or typed unavailable; no prompts, replies or grader reasoning |
| GET | `/built/decisions` | Explainers | evaluator | canonical | HTML | public index | curated trade-offs link to the complete decision register |
| GET | `/built/incidents` | Explainers | evaluator | canonical | HTML | public index | incidents connect root causes to permanent guards |
| GET | `/built/limits` | Explainers | evaluator | canonical | HTML | public index | current limits and observable scaling triggers |
| GET | `/built/diagrams/{name}.svg` | Explainers | evaluator | owned allowlist | SVG | machine | accessible static diagrams; unknown names 404 |
| GET | `/about` | Explainers | reader, evaluator | canonical | HTML | public index | project and author context; reachable from navigation |
| GET | `/stories` | Explainers | reader | canonical | HTML | public index | examples avoid applicability and advice claims |
| GET | `/architecture` | Explainers | evaluator | permanent redirect to `/built` | redirect | no index | old backlinks reach the canonical dossier in one hop |
| GET | `/architecture/next` | Explainers | evaluator | permanent redirect to `/built/limits` | redirect | no index | old roadmap backlinks reach the limits and delivery registry in one hop |
| GET | `/architecture/dossier` | Explainers | evaluator | self-canonical print utility | HTML | noindex, follow | print view reuses the nine canonical dossier sources |
| GET | `/decisions` | Explainers | evaluator | canonical | HTML | public index | accepted and rejected alternatives |
| GET | `/benchmarks` | Explainers | evaluator | canonical | HTML | public index | compatible reports or explicit not measured state |
| GET | `/benchmarks/latest.json` | Explainers | machine, evaluator | canonical artifact | JSON | machine | complete compatible collection set; malformed report fails closed |
| GET | `/benchmarks/cases.json` | Explainers | machine, evaluator | canonical artifact | JSON | machine | frozen cases with declared digest |
| GET | `/verify` | Explainers | evaluator | canonical | HTML | public index | verifies index, retrieval and assistant reports through trusted roots |
| GET | `/attestation.json` | Explainers | machine, evaluator | canonical artifact | JSON | machine | exact commit, image and manifest set |
| GET | `/pubkey.pem` | Explainers | machine, evaluator | canonical artifact | PEM | machine | key fingerprint matches trusted publication metadata |
| GET | `/developers` | Explainers | developer | canonical | HTML | public index | canonical MCP setup, contracts and playground |
| GET | `/ai` | Explainers | developer | permanent redirect to `/developers#assistant` | redirect | no index | one-hop redirect, no duplicate canonical page |
| GET | `/ask` | API | reader | permanent redirect to `/#ask` or `/` | redirect | no index | one-hop redirect |
| POST | `/api/ask` | API/assistant | browser client | none | JSON | machine | bounded request, typed result, typed failure |
| POST | `/api/ask/evaluation/admission` | API/assistant | release evaluator | none | JSON | machine | exchanges one signed, release-bound evaluation capability for a bounded opaque token |
| POST | `/api/ask/stream` | API/assistant | browser client | none | event stream | machine | versioned ordered events, cancellation and idempotency |
| POST | `/api/ask/thread/reset` | API/assistant | browser client | none | JSON | machine | idempotent reset of one opaque server-owned thread |
| POST | `/mcp` | MCP SDK bridge | MCP client | protocol endpoint | MCP JSON/SSE | machine | initialize, list tools, call every frozen tool through shared core |
| GET | `/healthz` | API/operations | platform | none | text | machine | process liveness only |
| GET | `/readyz` | API/operations | platform | none | JSON | machine | required publishers, inventory, signatures and manifest set |
| GET | `/robots.txt` | API | crawler | canonical | text | machine | points to the canonical sitemap |
| GET | `/sitemap.xml` | API | crawler | canonical | XML | machine | only canonical indexable routes and valid observed last-modified dates |

Static files under `/app` and `/fonts` are immutable build assets, not product routes. Unknown
routes return the framework 404 with the same security headers. Redirect helpers are never included
in the sitemap. The route fitness test compares this ledger to ASP.NET `EndpointDataSource` and the
Playwright route matrix exercises each browser-facing entry.
