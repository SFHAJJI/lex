# Contributing to Lex

Thanks for looking under the hood. The project optimizes for one thing:
**answers about past law that can be verified by anyone, forever**. Every
contribution is judged against that.

## High-leverage contributions

1. **A new publisher adapter** — any jurisdiction with an official
   machine-readable channel. Implement `ISourceAdapter`
   ([src/Lex.Law/Model.cs](src/Lex.Law/Model.cs)): enumerate works, fetch
   versions, fetch verbatim bodies. Adapters never write files, never touch
   git, and never leak publisher names into lower layers (a fitness test
   enforces this). The Legilux adapter is the reference: SPARQL + paced,
   sequential fetches from robots-permitted endpoints only.
2. **Eval cases** — [evals/cases.json](evals/cases.json). A good case is a
   natural question where the AI should construct a better tool call, or
   refuse better. Cases assert tool-argument construction and grounding, not
   phrasing.
3. **Extraction improvements** — published profiles (`akn-lu/1`,
   `xhtml-eu/1`, `fmx4-eu/1`) are immutable: improvements ship as a **new**
   profile beside the old, with a frozen-fingerprint test. See
   [src/Lex.Derive](src/Lex.Derive).
4. **Bug reports with a permalink** — every rendered article has one; a URL +
   expected-vs-actual is a complete report.

## Ground rules

- **Official endpoints only.** No SPA-API reverse engineering, no scraping
  around robots.txt, ever. This is a hard project invariant.
- **Determinism.** Nothing in ingest/derive/index may depend on a clock, an
  LLM, or ordering luck. `lex verify derive` must stay byte-stable.
- **Honest refusals over plausible answers.** If a change makes the system
  guess where it used to refuse, it will be rejected.
- **Tests:** `dotnet test` must stay green; fitness tests encode the
  architecture (layering, publisher purity).

## Mechanics

Sign your commits with the Developer Certificate of Origin (`git commit -s`).
Code is Apache-2.0; corpus data carries its own publisher licences (see each
corpus repo's `NOTICE`).
