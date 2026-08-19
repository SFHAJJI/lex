# Retrieval

Most apparent RAG hallucinations begin before generation. Lex therefore treats legal identity as
an authorization step and ranking as a later discovery step.

![A query passes through subject resolution, temporal and scope filters, keyword or gated hybrid retrieval, bounded top-k selection and typed evidence before any optional generation.](/built/diagrams/retrieval.svg)

[Open the retrieval diagram at full size](/built/diagrams/retrieval.svg)

| Box | Responsibility | Implementation |
|---|---|---|
| Subject preflight | Resolve official work identity or return ambiguity before rank can choose | `Lex.Index/WorkSearch` and `Lex.Ask` subject rules |
| Time and scope | Apply publisher, work, language and closed date-interval constraints | `Lex.Index` interval predicates and typed operation arguments |
| Exact coordinate | Return one known instrument, state or provision without similarity | shared `Lex.Mcp` legal operations |
| Bounded discovery | Search FTS by default; add weak official metadata and only gated hybrid vectors | `Lex.Index/WorkSearch`, FTS5 and the pinned local encoder |
| Result shaping | Deduplicate, enforce fairness and stop at the fixed evidence budget | `Lex.Index` query and response contracts |
| Typed evidence | Return rows, hashes, permalinks and match reasons to MCP, UI or assistant | `Lex.Mcp` envelopes and `Lex.Ask` evidence ledger |

## Retrieval funnel

1. Parse identifiers, article numbers and comparison intent; dates arrive as typed arguments
   guarded upstream.
2. Resolve the named subject against official work identity before asking the planner.
3. In the assistant, clarify zero or several credible subjects instead of letting rank silently
   choose one; direct MCP callers receive ranked hits labeled ambiguous.
4. Apply publisher, work, language and point-in-time scope.
5. Serve an exactly named coordinate without similarity when the question names one.
6. Search article text with FTS5/BM25 by default, weighted title 10, number 4, heading 6, text 1;
   official metadata contributes a weaker work discovery signal and an explicit match reason.
7. Deduplicate text states and anchors, apply bounded per-work fairness only for unscoped discovery,
   cut snippets from the content-addressed text store, then return typed rows and provenance.

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

The semantic encoder, local vectors and rank fusion exist, but activation is evidence-gated.
Offline signed benchmarks authorize vector mounting during deployment and startup; benchmark logic never switches an individual query.
An API caller explicitly chooses keyword or hybrid; the shipped surfaces default
to keyword and the assistant planner's hybrid choice is quarantined until signed activation. An
explicit hybrid request receives a typed unavailable result for any publisher whose exact signed
candidate did not pass. A compatible signed report binds relevance, latency, memory and size to that
candidate before its vectors may mount. Keyword remains the default. A measured rejection is a valid
architecture result, not a failed demo: the current signed holdout reports measure hybrid at 75.5
percent of keyword nDCG@10 on the EU corpus, answering even the engineered no-answer cases, so the
gate holds vectors off for relevance while latency passes.
