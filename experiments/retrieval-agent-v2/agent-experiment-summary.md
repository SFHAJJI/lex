# Agent architecture experiment

## Verdict

Advance the typed-plan Microsoft Agent Framework design. Reject the unconstrained direct
tool-calling variant.

The selected path separates responsibilities:

1. Agent Framework turns the conversation into a bounded bilingual plan or a typed clarification.
2. Deterministic work retrieval resolves reviewed names, publisher metadata and weaker semantic
   concepts.
3. `McpCore` retrieves and validates the exact dated provision.
4. A later composition stage may explain only that validated evidence; deterministic citation
   checks and a conditional judge remain required before production.

Literal names and tags participate in work-level FTS. Separate work/concept vectors allow hybrid
queries to match related wording. Neither source is blended into authoritative provision text or
its vectors. Discovery scores can nominate a work but cannot become legal evidence.

## Typed-plan result

The report contains 18 runs and 21 turns over every assistant scenario, three times each.

| Measure | Result |
| --- | ---: |
| Planner failures | 0 |
| Positive work failures | 0 |
| Positive anchor failures | 0 |
| Unknown-work false selections | 0 |
| Known corpus-gap false selections | 0 |
| Session restoration failures | 0 |
| Planning latency p50 / p95 | 10,942.6 / 19,161.6 ms |
| Deterministic execution latency p50 / p95 | 767.7 / 1,191.2 ms |
| Planner input / output tokens | 11,718 / 31,130 |

Evidence:

- `typed-plan-v1.json`, SHA-256
  `4c5b528224643b2f73c2e67f509e3c8cc55529f3d6eacaa4118efba7e7450258`
- `typed-plan-exec-v5.json`, SHA-256
  `641c3332062f0159a898caf669cd164bae423ae059c18f4851e712632af2781e`

The photovoltaic target, Regulation (EU) 2024/1735, is absent from the frozen corpus. Correct
behavior is therefore an explicit corpus gap, not a substitute law.

## Rejected direct-agent result

The final direct variant attempted 18 scenario runs. Its host-enforced enum, query bounds, two-call
cap and citation identity checks prevented unsafe output, but it still failed the product gate. In
two of three photovoltaic runs, the model inserted Directive 2014/24/EU into its own reformulation.
The work resolver correctly treated that official title as exact, and the agent returned Article 18
of the wrong instrument instead of the known corpus gap. A separate RGPD run was blocked because
the model's claimed work did not match deterministic evidence.

| Measure | Result |
| --- | ---: |
| Framework/run failures | **1 of 18** |
| Positive work failures | 0 |
| Positive anchor failures | 0 |
| Unknown-work false selections | 0 |
| Known corpus-gap false selections | **2 of 3** |
| Session restoration failures | 0 |
| End-to-end latency p50 / p95 | 16,243.8 / 35,129.5 ms |
| Input / output tokens for completed turns | 72,878 / 31,050 |

Evidence: `direct-agent-v2.json`, SHA-256
`9410255cc12e30ab9cc142ca1f0050eefe7fcef5ad89b94bbb17a6df1785de55`.

This is not a retrieval defect. It is an ownership defect: a tool-calling model was allowed to
name a candidate instrument before deterministic resolution. Prompting the model harder or adding
one-off photovoltaic rules would be a workaround. The typed plan prevents the behavior by contract:
it may formulate subject and provision queries but may not choose an unmentioned work, CELEX number
or legal answer.

## Latency implication

Normal keyword and hybrid search remains deterministic and separate from the agent. Lawyers using
the search bar do not pay model-planning latency. The assistant is an optional research workflow;
its current planner latency is too high for an unqualified production-default claim and must be
measured again after composition and judge gates.

## Grounding and judge result

The selected executor's validated turns were passed to a typed composer and conditional judge.
Citation identity is enforced outside the model across work, anchor, date, text hash and official
source URI. Corpus gaps and clarification turns bypass generation.

| Measure | Result |
| --- | ---: |
| Run failures | 0 |
| Correct deterministic gaps / clarifications | 6 / 3 |
| Judge passes / repairs | 10 / 1 |
| Honest refusals after two invalid drafts | 1 |
| Citation-contract escapes | 0 |
| Session restoration failures | 0 |
| Composer latency p50 / p95 | 8,896.7 / 16,153.5 ms |
| Judge latency p50 / p95 | 3,603.6 / 14,238.6 ms |
| Composer plus judge input / output tokens | 41,847 / 14,651 |

Evidence: `grounded-answer-v3.json`, SHA-256
`9f74e5868a3faa3d9293137720d61da97e632cdd7cc9566dd4a1a90fd71b0c6e`.

The one refused request was the exact command “show me GDPR.” That is an exact navigation case,
which the production contract already routes to a deterministic work card or outline without
generative composition. It therefore does not justify weakening the citation gate or adding more
model retries. Synthesized prose gets one correction attempt and then refuses.

## Reuse and caching boundary

Accepted aliases, concepts, descriptions and provenance become compact signed work cards in the
index. Both search and the assistant retrieve those cards on demand. They are the durable reuse
mechanism; a model's hidden cache is not evidence, memory or an artifact.

Azure prompt caching may reduce latency and input cost for stable prompt prefixes, tool definitions
and structured-output schemas. It is automatic for supported models, requires at least 1,024
identical leading tokens, and in-memory entries are normally cleared after inactivity and always
within one hour. Lex may measure `cached_tokens`, but correctness and deployment must not depend on
cache hits. See [Microsoft's prompt-caching documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/prompt-caching).

All judgments in this experiment are engineer-authored and are not lawyer-reviewed. No candidate
index or report is signed, published or deployed.
