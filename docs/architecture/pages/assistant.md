# Assistant

**Architectural decision: bounded plan -> validate and correct once -> freeze -> execute.** This is
ReWOO-inspired reasoning without an observation loop, not adaptive ReAct. Retrieval constrains what the
model can know, so exact identity and time resolution happen before planning; the planner proposes a
typed operation plan, while application code authorizes, executes and returns the normal cited result.

## Ownership and trust boundary

![A clean component view: the bounded assistant agent owns thread context, subject authority, planning and validation, the canonical catalog and frozen executor, and typed outcomes with evidence. The deterministic legal core and signed index stay outside. Public MCP is a separate projection of the same canonical operations, while composer and judge are optional prose roles.](/built/diagrams/assistant-boundary.svg)

[Open the ownership diagram at full size](/built/diagrams/assistant-boundary.svg)

## Boundary at a glance

| Concern | Inside the bounded assistant agent | Outside the agent boundary |
|---|---|---|
| Conversation | Server-owned bounded thread context and deterministic subject authority | Browser-visible transcript, opaque token and request admission |
| Planning | Planner LLM, canonical tool catalog, typed adapter and frozen executor | No model can call the index or legal core directly |
| Legal truth | Closed tool calls and outcome routing only | Shared deterministic `McpCore` and signed `Lex.Index` own dates, text, hashes, retrieval and gaps |
| Normal answer | Typed outcome router builds direct replies, cards, citations and disclosures | Web app renders typed effects without asking a model to rewrite them |
| Optional prose | Bounded evidence ledger supplies accepted evidence | Composer and judge are separate logical Agent Framework roles on the same configured Azure OpenAI deployment |

The canonical `LegalOperationCatalog` is the shared contract. The planner receives its closed typed
schemas but never calls MCP, SQL, vectors or the index. After validation and freeze, the executor
calls the deterministic legal core. Public MCP projects the same operations independently for
external clients; it is not the assistant's internal transport.

## One bounded turn, time downward

![A conventional sequence diagram with continuous lifelines for reader, web, deterministic admission and subject authority, planner and plan gate, executor and outcome routing, the legal core and signed index, and separate optional composer and judge roles. Time runs downward through deterministic resolution, one plan, one possible correction, freeze, execution without observations, a direct typed result and optional prose.](/built/diagrams/assistant.svg)

[Open the sequence diagram at full size](/built/diagrams/assistant.svg)

| Phase | Authority crossing | Closed result |
|---|---|---|
| Resolve deterministically | Official catalog identity, date semantics and bounded thread context enter planning | One authorized subject reference, clarification context, or no subject authority |
| Plan once | Planner sees only the question, bounded history and canonical typed operation schemas | A proposed plan; no tool or index access |
| Validate, correct once, freeze | Application code checks names and typed arguments; at most one contract correction | An immutable plan capped at eight operations, or typed invalid request |
| Execute without observations | Frozen executor calls the deterministic legal core, which queries the signed index | Closed outcomes, rows, hashes, publisher links and typed gaps |
| Typed result | Outcome router assembles cards, citations, disclosures and the bounded evidence ledger | Direct usable answer, `NeedsClarification`, gap or refusal without generated prose |
| Optional compose and judge | Explicit prose request may activate two separate logical Agent Framework roles | Composer draft; judge only factual Answer or Gap claims; fallback keeps the deterministic result |

## Why this design

| Approach | Decision | Product consequence |
|---|---|---|
| Bounded plan, freeze and execute | Chosen | Identity and time resolve first; typed and cited outcomes stay useful even without generated prose. |
| Open-ended ReAct | Rejected | Observing refusals and replanning can drift to another law while adding unbounded latency and model cost. |
| Naive RAG with LLM-selected identity | Rejected | Ambiguous retrieval can silently choose the wrong instrument and turn missing evidence into unsupported claims. |

The catalog, plan schema, executor, outcomes and evidence contracts are explicit application code;
no LangChain or LlamaIndex loop hides chunking, tool selection or retry behavior. The planner cannot
observe tool results and replan. The judge verifies only optional generated factual prose;
deterministic typed results need no LLM judge.

## Control flow

1. The web boundary validates the bounded request, idempotency key and evaluation admission when
   present, then acquires one server thread lease. Admission applies request ownership,
   concurrency, daily cost and first-result deadline limits.
2. The thread registry restores at most six accepted turns and deterministic subject context from
   a SHA-256 token digest. The browser presents the opaque token but owns no durable memory.
3. Subject preflight queries the signed work catalog before the planning model is called. Exact
   identifiers and exact publisher-provided short titles may authorize a work. Weaker official
   classifications remain discovery evidence only. Ambiguity is kept as clarification context,
   never converted into a silently selected law.
4. The bounded planning agent receives the question, bounded history, closed operation schemas and
   only opaque `subject_ref` values authorized by preflight. Without authority, work-specific
   operations receive no subject reference.
5. The canonical plan gate validates operation names and typed arguments. One bounded corrective
   turn is allowed for a contract-shape error; a second invalid plan becomes a typed invalid request.
6. The plan is frozen at no more than eight operations. There is no model observation or replanning
   after this point.
7. The shared legal core executes each operation against the signed index and returns a closed
   outcome, rows, hashes and publisher links. `NeedsClarification`, legal-boundary and transport
   gaps stay typed rather than being rewritten as apparent answers.
8. The outcome router builds the normal answer, result cards and citations deterministically. Its
   evidence ledger is bounded to 64 entries and 96,000 characters.
9. The planner may set `synthesis=true` only when the reader explicitly asks to explain, describe
   or summarise the accepted results. There is no hidden UI toggle. For example: "Show Article 6
   on 1 Jan 2021 and explain it."
10. Runtime additionally requires no displayed clarification, no `NeedsClarification` result and no
    transport failure. The grounded composer and conditional judge are two separate logical
    Microsoft Agent Framework agents with separate sessions over the same configured Azure OpenAI
    chat client and deployment used by Ask. They are roles, not a second deployment or an
    autonomous observation loop.
11. The composer receives the deterministic draft plus typed evidence and gets at most one format
    or evidence-contract correction. The judge runs only for Answer or Gap drafts with factual
    claims, then returns Pass, Repair or Refuse. A synthesis deadline or outage preserves the
    deterministic verified result.

## Authority matrix

| Concern | Model may | Deterministic code owns | Implementation |
|---|---|---|---|
| Subject | Receive the resolution state and reason over the remaining wording | Catalog candidates, official short-title authority, ambiguity and opaque authorization refs | `WorkSearch`, `WorkResolutionGuard` and `WorkSubjectRule` |
| Plan | Choose among offered operations and fill typed arguments | Canonical operation catalog, validation, one correction, cap and freeze | `AskService`, `OperationPlan` and `OperationArguments` |
| Execution | Nothing | Dates, SQL, FTS, vectors, comparisons and closed outcomes | shared `McpCore` and `Lex.Index` |
| Presentation | Draft optional prose and cite evidence ids | Direct result cards, links, citations and UI effects | `OperationAnswerPolicy` and `UiMapper` |
| Optional prose | Compose or judge only after authoritative results exist | Activation condition, evidence-kind validation, fallback and budgets | two `AIAgent` roles in `AgentAnswerFinalizer` |
| Safety | Propose one correction before plan freeze or one prose repair | No post-freeze replanning, typed refusal and deadlines | `OperationPlan` and `AgentAnswerFinalizer` |

## Conversation memory

![The browser keeps only a visible transcript and opaque token while bounded server memory stores six accepted turns and deterministic subject context, expiring after thirty idle minutes.](/built/diagrams/memory.svg)

[Open the memory boundary diagram at full size](/built/diagrams/memory.svg)

| Boundary | Responsibility | Implementation |
|---|---|---|
| Browser component memory | Keep the visible transcript and opaque capability for this tab only | `web/src/AssistantController.tsx` |
| Server thread registry | Bound accepted turns, deterministic subject context, waiters, expiry and eviction | `Lex.Web/AskThreadRegistry` |
| Planner request | Receive only the restored bounded transcript and authorized subject references | `Lex.Ask` operation planning |

Conversation memory is server-owned and ephemeral: at most 1,024 threads, six accepted turns,
32 KiB per thread, 16 MiB globally, two waiters per thread and a 30-minute idle lifetime. The
browser holds the visible transcript and opaque capability in component memory, not local storage.
Only the token's SHA-256 digest is retained server-side. Restart, expiry, eviction or reset loses
the thread safely; an unknown token never falls through to another conversation.

The public default admits 200 accepted turns per ingress-derived client address and 400 globally
per UTC day. Those process-local controls are honest cost and abuse limits, not user identity.
