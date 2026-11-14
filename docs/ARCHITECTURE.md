# Lex: architecture on one page

Lex (law.soufien.lu) shows Luxembourg and EU law as it read on a chosen date, and answers questions about it with an
assistant. Its rule: **the model plans; code retrieves, checks and quotes.** No sentence of an answer is written by a model.

## From the publisher to the answer

1. **Acquisition.** Two publishers only: Legilux (Luxembourg, every consolidated version with its dates) and EUR-Lex (EU acts,
   their original wording and consolidated versions). Each file is fetched, hashed (SHA-256) and kept in custody with its
   licence status; a file whose publisher note names a restrictive licence is never shown.
2. **Index.** A SQLite index per publisher: acts, versions (each with its applicability dates), articles, and each article's
   searchable text. Built in a release job on Azure, signed, and compared with the serving release before it can replace it.
3. **Operations.** A small fixed API (`search`, `as_of`, `evidence_bundle`, `diff`, `article_history`, `dossier`, `verify`,
   ...), the same over REST and MCP. Every answer is an envelope that states its rules (how search matched and ranked, what
   a date means), the data snapshot, and the commits that built and serve it. A refusal is typed (no free-text errors).
4. **Search.** Whole words (never part of a word), French, German and English stopwords dropped, plural or singular; a
   token holding a digit ("121-6", "1er") kept whole. Two lanes: the exact phrase first, then every word. Within a lane,
   articles whose heading holds the rarest query words come first, then by the text. The result says whether its first
   hit is **clearly ahead** (`best_hit`: its heading holds two of the question's words or the rarest, and it leads the next
   hit), or close (with the first three candidates).
5. **Assistant.** The question is masked (e-mail, phone, IBAN, identity numbers removed), then a model (Azure OpenAI,
   gpt-4.1-mini, EU data zone) returns a **plan**: which operations to run, with arguments that point to spans of the
   question or to closed choices. Code validates the plan against a grammar, runs the operations, and builds the answer
   from **fixed sentence templates** and **verbatim quotations**.

## What the model may and may not do

- May: choose operations and their order, pick spans of the question as queries, pick a language or a date from the
  question, name the part of a question that asks for legal advice.
- May not: write any sentence the reader sees; choose an article that the search did not rank clearly ahead; invent an
  identifier (an identifier argument must be a span of the question or a prior step's result); answer a personal-situation
  question (that is refused with where to get help).
- A search hit is quoted alone only when it is clearly ahead; otherwise the answer asks which article, offering the three
  best. A step that quotes nothing is never labelled as an answer.

## How a citation is verified

Each quotation carries its article's permanent address (act, version date, version digest, language, article) and the
SHA-256 of the article's text. The `verify` operation recomputes the digest from the held text; the evaluation harness
calls it for every quotation of every answer. The official source (the version's ELI on Legilux, or EUR-Lex) is linked.

## How quality is measured

- **lex-eval** (Python, public: github.com/SFHAJJI/lex-eval): 56 development questions, each with the expected act and
  article (25 on the 1997 insurance-contract law, 21 on GDPR, the AI Act and DORA, in French and English), plus 15
  held-out questions scored separately and never tuned against (their file's SHA-256 is committed in CHANGES.md).
- Each result is one kind: right article, **wrong article** (the headline; target zero), not found, wrongly refused.
- MLflow logs per run: the Lex commit, the index snapshot, the planner prompt version, the model deployment, latency p50
  and p95, an estimated cost per question, and one trace per question (plan, operations, search hits, chosen article).
- Gates: a release is not promoted if any answer on the insurance set quotes a wrong article, if a previously passing
  question fails, if a quotation does not verify, or if a sentence outside the templates appears.
- Before promotion, every release passes: the .NET and web test suites (about 8,700 tests), browser journeys on GitHub's
  runner, a data comparison with the serving release, probes of the candidate at its own address, and a backup.

## Known limits

- Coverage is what the two publishers serve and the index holds: about 3,400 Luxembourg files are hidden by their licence
  note; codes and acts outside it are refused with the official source.
- Search is lexical (no embeddings): a question in words the law does not use can miss; the answer then says so and
  offers candidates, it does not guess.
- The planner model can still choose a weak query; the gates catch it in evaluation, not at answer time.
- Not legal advice, and not reviewed by a lawyer yet: the answer says so.
