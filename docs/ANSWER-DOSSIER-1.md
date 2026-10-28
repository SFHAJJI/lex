# answer_dossier/1: the assistant's written contract

This is the contract of record for the assistant's answer, `lex-v3-answer-dossier/1`, and for the advice boundary. It is the
document S4-A04 and S4-A05 are checked against (R30). The code that keeps it lives in
`src/Lex.V3.Contracts/Platform/Assistant/` and `src/Lex.V3.Api/V3CorpusMount.Assistant.cs`; the tests named in §10 hold it.

**The assistant is switched off.** `ask` answers the typed presentation result `assistant_v3_unavailable` on every
deployment of this code. Ending D91's containment does not switch the assistant on (R30): switching on is a configuration
switch, off by default, with an operator off-switch, and it cannot take effect until the people's work of §11 is done.

## 1. What the assistant is

The assistant takes a question and answers it from the operations Lex already serves (resolve, search, as_of,
evidence_bundle and the rest of §4's allowlist). A model plans; code decides (R31). The model never retrieves, ranks,
rewrites a search unseen or writes a sentence (R34). The answer says that an AI model assembled it (R33).

Every visible string of an answer is exactly one of three things (R31):

1. a template of the closed set (`lex-v3-answer-templates/1`, §7), filled by code with typed facts;
2. a verbatim quotation of held text, with the SHA-256 of the article text it is taken from, its offset, its interval and
   its links;
3. a verbatim span of the user's question.

There is no gloss, paraphrase, composer or binder. A sentence no template carries is a change to the set, reviewed.

## 2. Verdicts and presentation results

The six verdicts are a closed set (36 §1.6; F30 §2): `answer`, `answer_with_enrichment`, `point`, `clarify`, `refuse`,
`split`. Their plain labels are R28's: "From the official text", "Not held here: official source", "Needs one detail",
"Outside what this service does", "Two questions". Code never emits `answer_with_enrichment`: it holds no registry of
official machine channels to establish one, so a planner that proposes it is answered under `point`.

The two presentation results, `assistant_v3_unavailable` and `localization_unavailable`, are a type of their own beside the
six verdicts and never counted as a legal verdict (R45(a); Decision 91). Neither is a refusal code: the registry stays at
its twenty. Both travel as the `handoff_card` result of `ask`. The envelope's verdict slot must hold one of the six, and the
card has always sent `point` there. What counts is the card's `presentation_result`, and telemetry and evaluation count it
in its own bucket.

How each outcome travels in `ask`'s envelope:

| Outcome | Envelope |
|---|---|
| answer, point, clarify, split | result `answer_dossier`, verdict as named |
| refuse | refusal `advice_boundary`, whose payload carries `descriptive_maximum` (the quotations shown), `handoff` and the whole `answer_dossier` (a refusal is never a result) |
| assistant_v3_unavailable | result `handoff_card`, verdict `point`: the switched-off card, byte for byte |
| localization_unavailable | result `handoff_card`, verdict `point`, naming French and English |
| ask's own per-client limit | refusal `rate_limited` |

## 3. The request

`ask/1`'s reviewed request schema admits `question` alone, and every recorded answer carries the registry's digest, so the
schema is frozen in this slice (the driver's decision of 2026-10-28). The pipeline takes three more inputs as typed values,
and the tests drive them directly:

- `answer_locale` (`fr` or `en`). Under `ask/1` the deterministic language parser decides it from the question: French or
  English answers; German, Luxembourgish, Portuguese and any other language get `localization_unavailable`; an undetermined
  language falls to English (a driver decision).
- `context`: at most two typed chips from the last trace, a work and a date (R45(e)). No earlier question text.
- `replay`: a trace to replay (§9).

Admitting the three to the request schema is the first step of the release that switches the assistant on. It changes the
registry, and the base answers are recorded again on Linux.

## 4. The pipeline

1. **The switch** (§8). Off: the switched-off card, and nothing below runs.
2. **Intake.** The question is put in NFC and is at most 1,000 UTF-16 units (R45(f)). Code masks obvious identifiers
   before any planner sees the text (R33): e-mail addresses, phone numbers, IBANs and 13-digit national numbers become
   same-length runs of U+2022. A plan argument may not touch a masked run.
3. **Parsers**, deterministic, replacing a model router (R34). They read the question's language, its civil dates (`d1`,
   ...) and ranges (`r1.from`, `r1.to`), and the function cues of the advice boundary (§6).
4. **The planner** (`IAssistantPlanner`) is the only model seam. It is given the masked question, the answer locale, the
   opaque references, the search languages and the operation list. It is given nothing else: no address, user agent,
   cookie or history. No planner exists in `src`; a deterministic test planner lives in the tests.
5. **The validator** refuses every plan that is not typed, bound and within the caps (§5). A refused plan gets the
   switched-off card.
6. **Execution**, through the existing operations only. Code builds each request, validates it against its reviewed
   request schema and runs it through the dispatch REST and MCP use (`V3ApiHandler.ExecuteFor`), so the out-of-scope list
   and the names record (R92) apply as everywhere.
7. **Follow-ups** (R35): at most two, each fired by a typed condition on an earlier typed result, with its arguments bound
   by code.
8. **The verdict**, computed by code (§6).
9. **The composer** (§6, §7).
10. **The gates in the code path** (§10). A failed gate gets the switched-off card.

Nothing derived from the question is stored on the server: not the question, the plan, the trace nor any text (R32). The
answer and its trace go to the client.

## 5. The plan: `lex-v3-assistant-plan/1`

```json
{
  "schema": "lex-v3-assistant-plan/1",
  "parts": [{ "part_id": "p1", "span": [0, 52], "proposed_verdict": "answer" }],
  "steps": [{ "step_id": "s1", "part_id": "p1", "operation_id": "search",
              "arguments": { "query": { "span": [5, 40] }, "language": { "choice": "fra" }, "date": { "today": true } } }],
  "follow_ups": [{ "step_id": "f1", "after": "s1", "condition": "on_single_article", "operation_id": "evidence_bundle",
                   "arguments": { "identifier": { "result": "s1", "pick": "sole_work" },
                                  "article": { "result": "s1", "pick": "sole_article" },
                                  "date": { "today": true } } }]
}
```

- **Parts.** One or two (R31's split), named `p1` and `p2` in order, non-overlapping, each with a proposed verdict of the
  six.
- **Steps.** One to four, named `s1` to `s4` in order. Each belongs to a part and names one operation of the allowlist:
  resolve, search, dossier, timeline, as_of, evidence_bundle, article_history, diff, changes_in_period.
- **Follow-ups.** None to two (R35), named `f1` and `f2`. Each follows a step and names a condition, `on_single_work` or
  `on_single_article`. Its operation is one of dossier, timeline, as_of, evidence_bundle or article_history, and it reads
  the work the earlier step named. A follow-up never reads another date than its step, nor another work or language. A
  follow-up whose condition does not hold is recorded as `not_fired`.
- **Argument sources.** Exactly one per argument, and never a free-text value:

  | Source | Form |
  |---|---|
  | a span of the question | `{"span":[s,e]}` |
  | a parser output or a context chip | `{"ref":"d1"}`, `{"ref":"r1.from"}`, `{"ref":"c1"}` |
  | an earlier result, follow-ups only | `{"result":"s1","pick":"sole_work"}` or `"sole_article"` |
  | the closed set of search languages | `{"choice":"fra"}` |
  | today in Luxembourg (R13) | `{"today":true}` |

  Admitted per parameter:
  - `query` takes a span only (R34: the user's words);
  - `identifier` takes a span, a context work or a result;
  - `date` takes a date reference, a context date or today;
  - `date_from` and `date_to` take the two ends of one range;
  - `article` and `anchor` take a result;
  - `language` takes a choice.
- **Caps** (R45(f)):
  - planner output at most 16 KiB;
  - at most six operations;
  - a query span at most 400 characters, an identifier at most 300;
  - a diff range at most ten years, a changes_in_period range at most 366 days;
  - search and changes_in_period asked for at most 10 rows;
  - at most five quotations of at most 8,000 characters each;
  - an answer of at most 256 KiB.
- **Refusal.** Any unknown member, token outside a closed set, unbound argument, source a parameter does not admit, span
  out of bounds or touching a mask, or breached cap refuses the plan (`V3PlanRefusal`). The validator never repairs a plan.

## 6. The verdict and the advice boundary (S4-A05)

**The function test** (R15) decides what is refused, never a pronoun. A part asks for application when the cue parser finds
a cue of one of the four functions, or when the planner proposed `refuse`. The four functions are:
- choosing among legal options;
- concluding on a person's situation;
- predicting a decision-maker;
- computing deadlines or amounts from the asker's facts.

The cue lists are closed and French and English; a question about "I" and its twin about "an employee" meet the same
pattern. Code is biased to refusal: a refusal proposed by the planner stands, and a cue overrides a proposed answer.

The verdict, in order:
1. Two parts of different classes give `split`. Never averaged, and the answerable half never carries the refused half.
2. A part that asks for application gives `refuse`. When every part asks for application, the answer is `refuse`.
3. A part with a quotation, or another held answer, gives `answer`.
4. `ambiguous_identifier`, `ambiguous_version`, a search with hits in more than one act or article, or a reference that
   opens an act but names no article gives `clarify`: one question, with the candidates as actions.
5. Otherwise, `point`: not held, withheld, out of scope or no hit, with the official sources.

**The advice boundary** (R45(b); R14):
- Lead with the law, verbatim and dated, then one boundary line and the handoff.
- Open with the refusal only when the whole question asks for application.
- R14's master sentence ends "here is who can advise you", and the handoff registry names no adviser (none may be
  invented). The answer therefore uses the accepted variant: "Lex does not yet name who can advise you: that list must
  first be checked by a person." The master is held in the set for when reviewed advisers exist.
- An application part shows the law the plan found and nothing else of it: the assistant never applies the law to personal
  facts, resolves an ambiguity silently, invents legal effect, relabels derived facts, quotes without a hash-carrying
  citation, answers from memory or reproduces withheld text (S4-A05; withheld text is refused `text_withheld` by the
  operations themselves, R83).

## 7. The answer: `lex-v3-answer-dossier/1`

```json
{
  "schema": "lex-v3-answer-dossier/1",
  "verdict": "answer",
  "answer_locale": "fr",
  "label": { "kind": "claim", "template_id": "verdict.answer", "locale": "fr", "facts": [], "text": "…" },
  "notices": ["claim notice.assembled_by_model", "claim notice.not_legal_advice"],
  "plan": { "rendered": ["claim plan.heading", "claim plan.step.*", "…"], "plan_sha256": "…" },
  "parts": [{ "part_id": "p1", "span": [0, 52], "class": "lookup", "cues": [], "verdict": "answer", "blocks": ["…"] }],
  "gates": { "quotes": 1, "disclosures": 2, "non_template_strings": 0, "disclosure_complete": true },
  "trace": { "…": "§9" }
}
```

**Blocks**, a closed union:

| Kind | Holds |
|---|---|
| `claim` | `template_id`, `locale`, typed `facts` [{name, kind, value}], and `text`: the template with each placeholder replaced by its fact written by code (a date in words in the locale, a language by its name, an article number without `art_`, everything else verbatim) |
| `quote` | `quote_id`, `step_id`, `publisher`, `work`, `anchor`, `language` of the held expression, `text`, `offset`, `length`, `shortened`, `article_text_sha256`, `state_sha256`, `from`, `to`, `date_role`, `state_kind`, `state_permalink`, `article_permalink`, `official_source`, `journal_officiel` |
| `question_span` | `start`, `end`, `text` |
| `refusal` | the step's `code` and `payload` as the operation sent them, and its sentence as a claim (R35: a refusal stays visible) |
| `action` | an `operation_id` and bound `parameters`, offered and never run, with its sentence as a claim (nearest states, candidates) |
| `handoff` | a registry entry's `entry_id`, `role`, `link`, `reviewed_by_person`, and its sentence as a claim |
| `disclosure` | a `flag` of R38 on the quotation `object` it names, and its sentence as a claim |

**Templates** (`V3AnswerTemplates`), each in French and English:
- the five verdict labels;
- the notices R44, R33 and R50;
- the two presentation sentences;
- the plan lines: heading, split, date today, one per operation, and the follow-up notes;
- the law: quote heading; Luxembourg interval, open interval and entry-into-force dating; EU consolidated wording state and
  EU original wording; content hash; official source; permalink; quotation shortened;
- the boundary: R14's master, its no-adviser and no-text variants, the boundary line, the no-adviser line;
- the outcomes: no hit, not held, the clarifying questions, the actions, and the refusal sentences for no_version_for_date
  (Luxembourg and EU), identifier_unknown, text_withheld, text_not_available, out_of_corpus_scope, anchor_not_in_version
  and any other code;
- the handoff lines by role;
- the R38 disclosures.

Provenance:
- The English masters are the specification's (R14, R19, R21, R28, R50); the other English, R44's notice and R17's
  derogation sentence included, is drafted by an agent from the rulings.
- The French was checked by an independent AI reviewer in two rounds, and the finals were applied byte for byte. R50's
  French is J6's.
- **No template has had R43's qualified-person review**, and `V3AnswerTemplates.QualifiedReviewComplete` says so.

**Disclosures** (R38), shown whenever their flag is on a served object:
- authenticity, always: R20's sense for Luxembourg, R19 for EU;
- no legal effect, for a consolidation;
- provisional;
- validity conflict;
- corrigendum, derogation and pre-application, wherever an operation's result marks one;
- title distrust, wherever a title is shown.

## 8. The switch, the limits and the money (R30, R40, R114)

- **On only when every condition holds:**
  - `LEX_V3_ASSISTANT` is exactly `on`;
  - `LEX_V3_ASSISTANT_OFF`, the operator off-switch, is unset or empty (any value turns it off);
  - a planner is wired;
  - every template has R43's qualified-person review;
  - every handoff entry is reviewed by a person;
  - no breaker has tripped.

  Both variables are read once at start. No deployment of this code meets the conditions.
- **ask's own per-client limit:** 20 a minute (below 60, R40). It is keyed by the keyed hash the API's limiter already
  holds, never by an address, and counts only while the switch is on.
- **Money:**
  - a per-answer cost ceiling of 0.02 EUR, reserved before planning and settled with what the planner reports;
  - a planner over the ceiling is not used;
  - a monthly cap of 20 EUR (O22's recommended answer; the owner decides it).

  At the cap, `ask` answers the switched-off card and search keeps working. The budget is in memory; the provider's own
  hard cap is the backstop and the owner's to set.
- **Deadline:** a planner gets 8 s.
- **Telemetry:** in-memory counters keyed by closed vocabularies only (outcome reason, verdict, refusal code, locale,
  latency bucket, error bucket), with no sink (R32).

## 9. The trace and replay: `lex-v3-answer-trace/1`

The trace holds:
- the question and its SHA-256;
- the answer locale and the context chips;
- the planner's id;
- the canonical validated plan and its SHA-256;
- today (R13);
- the snapshot (id and SHA-256);
- the registry, template-set and handoff-registry digests;
- each step's operation, bound parameters, outcome and the SHA-256 of its result or refusal (never of its envelope's context,
  whose clock moves).

Replaying the trace against the same snapshot, with the same day and a planner that answers the trace's plan, gives
byte-identical answer JSON. Each operation runs again as on the trace's day, since an operation may read "today" (search's
exact-reference phase opens today's readable address). Another snapshot is refused (`snapshot_unknown` once replay is a request input, §3). The trace
goes to the client, which can export it; the server keeps nothing.

## 10. The gates

In the code path, before an answer is served (`V3AnswerGates`):
- zero non-template strings: every `text` re-renders from its template and facts, equals the question at its span, or is
  a quotation;
- every quotation bound: the article text it cites hashes to `article_text_sha256`, and the quotation is that text's exact
  characters at its offset;
- every required disclosure present;
- the size cap.

The machine gates' harness, in the tests, checks again with code of its own:
- **quote fidelity**, by a checker independent of the pipeline that re-reads each article through the operations and hashes
  it itself (gate 1.0);
- **zero non-template sentences**, by its own walk of the JSON;
- **byte-identical trace replay**, under two clocks.

Each gate has a deliberate-failure control. The harness runs over a small replay set that is **drafted by an agent; not D4
or D1; not a switch-on gate** (R36, R37 and R39 need people's sets).

## 11. What switching on needs, and who does it

Only people may do these, and no agent can:

1. A written legal read: GDPR arts. 6 and 9, and AI Act art. 50(1) (R33).
2. An EU-region model under a processing agreement, with no retention and no training, or a self-hosted model. The
   processor must be named in the privacy notice (R33, R49).
3. A Luxembourg avocat's review of the D4 refusal and split gold set, the advice templates and the not-legal-advice line
   (R36; O4).
4. A qualified person's review of every French and English template (R43).
5. D4, from the hashed catalogue two, and D1, 320 fresh V3 queries, both written by people (R37):
   - at least ten paraphrases per row in sections L and M, per answering language;
   - a second, independent annotator on 15 to 20 percent of each (R36).
6. The handoff registry's adviser entries by theme, with cost, limits and languages; the high-stakes blocks; a reviewer and
   date for every entry; the weekly link check (R42).
7. The owner's sign-off, the spend cap at the provider, and the R44 notice wording (O22, O4).

Then the switch-on release, on its exact tuple:
- the staged request inputs of §3;
- the provider adapter;
- the D4 gates, five runs per case (R36, R37, R40);
- quote fidelity 1.0, zero non-template sentences and byte-identical replay;
- the budget gate: p95 3 s to the verdict and 10 s to the full answer;
- R39's grader reported and never gating.
