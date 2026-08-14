# Retrieval

Most apparent RAG hallucinations begin before generation. Lex therefore treats legal identity as
an authorization step and ranking as a later discovery step.

![A query passes through subject resolution, temporal and scope filters, keyword or gated hybrid retrieval, bounded top-k selection and typed evidence before any optional generation.](/built/diagrams/retrieval.svg)

[Open the retrieval diagram at full size](/built/diagrams/retrieval.svg)

| Box | Responsibility | Implementation |
|---|---|---|
| Subject preflight | Resolve official work identity or return ambiguity before rank can choose | `Lex.Index/WorkSearch` and `Lex.Ask` subject rules |
| Time and scope | Apply publisher, work, language and closed date-interval constraints | `Lex.Temporal`, `Lex.Index` and typed operation arguments |
| Exact coordinate | Return one known instrument, state or provision without similarity | shared `Lex.Mcp` legal operations |
| Bounded discovery | Search FTS by default; add weak official metadata and only gated hybrid vectors | `Lex.Index/WorkSearch`, FTS5 and the pinned local encoder |
| Result shaping | Deduplicate, enforce fairness and stop at the fixed evidence budget | `Lex.Index` query and response contracts |
| Typed evidence | Return rows, hashes, permalinks and match reasons to MCP, UI or assistant | `Lex.Mcp` envelopes and `Lex.Ask` evidence ledger |

## Retrieval funnel

1. Parse explicit dates, identifiers, article numbers and comparison intent.
2. Resolve the named subject against official work identity before asking the planner.
3. Clarify zero or several credible subjects instead of letting rank silently choose one.
4. Apply publisher, work, language and point-in-time scope.
5. Search article text with FTS5/BM25 by default; official metadata contributes a weaker work
   discovery signal and an explicit match reason.
6. Deduplicate text states and anchors, apply bounded per-work fairness only for unscoped discovery,
   then return typed rows and provenance.

## Top-1 and top-k are different product decisions

| Question shape | Selection | Why |
|---|---|---|
| Exact official identifier plus date | One deterministic work and interval | Similarity would weaken an exact coordinate |
| Unique exact official short title | One work, with its source literal | Identity comes from publisher data, not rank |
| Theme or conceptual discovery | Bounded top-k works or provisions | Several results are the useful answer |
| One theme matches several laws | Clarification or result set | Metadata must not silently authorize one law |
| Answer requires several provisions | Bounded top-k evidence, then deterministic assembly | Top-1 would bury necessary context |
| No credible evidence | Typed gap or refusal | Expanding until something looks plausible is unsafe |

There is no generation loop that reads page one, asks the model whether it is satisfied and keeps
searching. The application decides the evidence budget before execution. This makes latency,
population and failure behavior testable.

## Failure taxonomy

| Failure | What goes wrong | Guard or measurement |
|---|---|---|
| Not retrieved | The right passage never enters the candidate set | Recall and negative cases |
| Wrong passage | A nearby passage outranks the answer | MRR and nDCG by question category |
| Buried evidence | The passage exists below the evidence budget | Recall at k and evidence-budget tests |
| Right passage, wrong instrument | The answer is faithful to a different law | Identity preflight, instrument disclosure and ambiguity tests |
| Publisher gap | No safe text exists for that state | Typed availability outcome, never substitute text |

## Why keyword remains the default

The semantic encoder, local vectors and rank fusion exist, but activation is evidence-gated. The
frozen 200-case catalog fixes tuning and holdout judgments before measurement. The current v2
baseline deliberately says `pending_signed_production_artifacts`; the fresh v4 candidate must
publish relevance, latency, memory and size before hybrid can be promoted. Until then, keyword is
the production default. A measured rejection is a valid architecture result, not a failed demo.
