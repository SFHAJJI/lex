# Assistant

**Architectural decision: bounded plan -> validate and correct once -> guard dates -> freeze -> execute.**
ReWOO-inspired reasoning without an observation loop, not adaptive ReAct. Identity resolves in
code before any model runs; the planner proposes one typed plan over a closed catalog; code
validates, freezes and executes it; optional prose is composed and judged only on explicit
request and can only lose to the typed reply, never replace it.

![A conventional sequence diagram with continuous lifelines for reader, web, deterministic admission and subject authority, planner and plan gate, executor and outcome routing, the legal core and signed index, and separate optional composer and judge roles. Time runs downward through deterministic resolution, one plan, one possible correction, a date guard, freeze, execution without observations, a typed response contract and optional prose.](/built/diagrams/assistant.svg)

[Open the sequence diagram at full size](/built/diagrams/assistant.svg)

## What crosses each boundary

Every component hands the next one a typed object, never prose. **Code** marks deterministic
application code; **model** marks a bounded Azure OpenAI call with a typed output. Cards reach
the reader as each operation completes, but the plan is frozen before the first one runs and the
reply is assembled once at the end.

| From | To | Object | Carries | Cannot carry |
|---|---|---|---|---|
| Subject preflight, code | Planner, model | `SubjectAuthority` | opaque `subject_1..N` refs, up to eight, with the works they stand for; an optional article number; an optional runner-up disclosure; or a clarification that ends the turn | a law name or identifier the planner could rewrite |
| Planner, model | Plan gate, code | proposed plan | tool names from the closed catalog and typed arguments; `work` may only be an offered ref | anything outside the schema: one correction, then a typed invalid request |
| Plan gate and date guard, code | Executor, code | `OperationPlan` | one to eight frozen operations, value-copied, each with its declared effects and recorded repairs; a bare year widened to its window or turned into a clarification | a later change; the plan is sealed |
| Executor, code | Outcome router, code | execution results | per operation a closed legal outcome, transport outcome, rows, hashes, permalinks or a typed gap; the evidence ledger, 64 entries and 96,000 characters | an effect outside the frozen declaration, which throws |
| Outcome router, code | Composer, model, optional | draft and ledger | the already final typed reply and its evidence items with ids | text the ledger does not hold |
| Composer, model | Judge, model | `AgentAnswerDraft` | claims bound to evidence ids kind to kind, permalinks byte-identical to evidence, already validated in code | a number or article absent from the cited excerpts |
| Judge, model | Outcome router, code | `AgentGroundingJudgment` | Pass, Repair or Refuse | a replacement reply; anything but Pass keeps the typed result |
| Outcome router, code | Web | `AskOutcome` | the named reply, per-operation results, closed effect cards, forced disclosures, trace, timings | model prose without its typed result underneath |

## What the model may never do

- Choose the law. Identity is resolved against the signed catalog before planning; a decided
  ambiguity proceeds with the runner-up disclosed, an undecidable one asks.
- Observe a result and replan. One correction before freeze for a contract-shape error; nothing after.
- Pick a date by itself. The date guard re-derives every planned instant from the question.
- Call the index, the legal core or public MCP. The executor calls the in-process core; MCP is a
  separate projection of the same operations, not the planner's transport.
- Emit a confidence number or an unchecked claim. Prose claims cite evidence ids; permalinks,
  numbers and article names are checked in code; the judge returns Pass, Repair or Refuse.

Code owns these through `WorkResolutionGuard`, `WorkSubjectRule`, `OperationPlan`,
`OperationArguments`, `DateIntentGuard`, `UiMapper`, `AgentAnswerContract` and `AgentAnswerFinalizer`.

## Bounds

- Admission: 200 accepted turns per client address and 400 globally per UTC day by default,
  four concurrent; idempotency key and evaluation admission checked at the web boundary.
- Deadlines: planner 12 s, first typed result 25 s, optional synthesis 45 s; on expiry the
  deterministic result is returned as is.
- Synthesis activates only when the planner set `synthesis=true` because the reader explicitly
  asked to explain, and the flag is reconciled against the frozen plan, never against the
  reader's words, which quoted content controls: a plan of inventories alone (`coverage`,
  `cited_by`, `in_force_on`) has it cleared, because those lists are already rendered in full.
  There is no hidden UI toggle.
- Composer and judge are two logical Microsoft Agent Framework roles with separate sessions on
  the same configured Azure OpenAI deployment used by Ask; one format or evidence correction for
  the composer; the judge runs only for Answer or Gap drafts with factual claims.
- Two prompt-injection canaries, a current-turn and a restored-transcript probe, run against
  every candidate revision before promotion.
- No LangChain or LlamaIndex: catalog, plan schema, executor, outcomes and evidence contracts
  are explicit application code.

## Conversation memory

![The browser keeps only a visible transcript and opaque token while bounded server memory stores six accepted turns and deterministic subject context, expiring after thirty idle minutes.](/built/diagrams/memory.svg)

[Open the memory boundary diagram at full size](/built/diagrams/memory.svg)

| Boundary | Holds | Bound |
|---|---|---|
| Browser | The visible transcript and the opaque token, in component memory, never local storage | Lost on reload; an unknown token never falls through to another conversation |
| Server thread registry | Accepted turns and deterministic subject context, keyed by the token's SHA-256 digest only | 1,024 threads, six turns, 32 KiB per thread, 16 MiB globally, two waiters, 30-minute idle lifetime |
| Planner request | The restored bounded transcript and the authorized subject refs | Nothing persistent; restart, expiry, eviction or reset loses the thread safely |
