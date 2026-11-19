# Lex: architecture on one page

Lex (law.soufien.lu) shows Luxembourg and EU law as it read on a chosen date, and answers questions about it with an
assistant. Its rule: **code reads the question, finds the lines and quotes them; a model only describes a question or marks
lines that code found.** No sentence of an answer is written by a model, and no model chooses what is run.

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
5. **Assistant (the bricks, since 2026-11-19).** The question is masked (e-mail, phone, IBAN, identity numbers removed).
   Then, in this order:
   - **Question parsing (code).** The question is read against a dictionary of one domain's terms (private insurance
     first: each concept with the law's own word, the words readers use and how the law answers) and a corpus index (the
     domain's acts by the names people give them). The result is a typed row: the concepts, who acts, the acts named, the
     kind of answer asked (a definition, a rule, a period, a list...).
   - **Dispatcher (code).** Fixed rules choose the route from that row: a term the law defines; one article of a known
     act cited by its number; one fact; a rule and its cases; a list; a text Lex does not hold; a personal case (refused,
     with the law beside it). The same question on the same data takes the same route.
   - **Retrieval (code).** Each act is two tables built from its stored text: the outline (labels, headings) and the lines
     (each a position in an article's text). Detectors that can each be shown filter and rank the lines: the term on the
     line, the heading that names it, who acts, the form of the answer, a heading the question writes out whole. A sweep of
     the whole act backs a list or an absence.
   - **A model, only where code cannot decide** (Azure OpenAI, gpt-4.1-mini, EU data zone): to describe a question the
     dictionary did not read, from a closed list; to sort candidate lines; to point at the lines that answer, by their
     number. Each reply is held to a closed form and checked by code: a line pointed at must be one that was sent, and a
     value must be written on its line.
   - **The answer (code).** Code writes a plan, the pipeline validates and runs it, and the answer is **fixed sentence
     templates** and **verbatim quotations**: a quotation is the exact characters at a position of the stored text, bound to
     that text's digest. Every answer carries the record of how it was reached, and says what a model did for it.

## What the model may and may not do

- May: say what a question asks, from a closed list; copy the words of the question that name an act, an article or a
  subject (code finds the copy in the question and uses the reader's own words at that place); name one concept of the
  dictionary; give candidate lines a role; point at lines by their number; name the part of a question that asks for
  legal advice.
- May not: choose an operation or its order; write any sentence the reader sees; write a quotation or a value (code cuts
  both from the stored text); point at a line it was not given; answer a personal-situation question (that is refused
  with where to get help).
- With no model, a model that fails or a reply outside its form, code answers alone, and the answer says that no model
  took part. A term the law defines and a cited article of a known act never ask a model.
- A search hit is quoted alone only when it is clearly ahead; otherwise the answer asks which article, offering the three
  best. A step that quotes nothing is never labelled as an answer.

## How a citation is verified

Each quotation carries its article's permanent address (act, version date, version digest, language, article) and the
SHA-256 of the article's text. The `verify` operation recomputes the digest from the held text; the evaluation harness
calls it for every quotation of every answer. The official source (the version's ELI on Legilux, or EUR-Lex) is linked.

## How quality is measured

- **lex-eval** (Python, public: github.com/SFHAJJI/lex-eval): 56 development questions, each with the expected act and
  article (25 on the 1997 insurance-contract law, 21 on GDPR, the AI Act and DORA, in French and English); 79 questions
  on private insurance, each with the line of the law expected; plus 15 held-out questions scored separately and never
  tuned against (their file's SHA-256 is committed in CHANGES.md).
- Each result is one kind: right article, **wrong article** (the headline; target zero), not found, wrongly refused.
- MLflow logs per run: the Lex commit, the index snapshot, the engine's identity (the model asked and the digest of the
  instructions, the dictionary and the corpus index), the model deployment, latency p50
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
- The dictionary covers private insurance today. A question on another domain is described by the model and searched
  by its words, as before the bricks: it is answered when it cites an article or when the search finds one clearly ahead.
- A model's reply can vary from one run to the next; code checks every reply, and the gates check the outcome.
- Not legal advice, and not reviewed by a lawyer yet: the answer says so.
