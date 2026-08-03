# Increment B, written prediction (BEFORE any EU code)

Date: 2026-08-01
Rule (spec §14.2): if the real numbers land far above these, the neutral model
was wrong and increment C stops until it is fixed.

## Prediction

| Project | Predicted lines changed to add EUR-Lex | Rationale |
|---|---|---|
| **Lex.Law** | **≤ 10** | The model already carries Expression-level validity, multi-language expressions, observation records, per-field valid_time_source, and a TextIncluded flag. EU should need nothing new. |
| **Lex.Temporal** | **0** | Interval algebra is jurisdiction-free by construction. |

Everything else is expected to land where publisher knowledge belongs:
a new adapter (`Lex.Sources.EurLex`, ~250 lines), body-writing support in the
corpus writer (~40 lines, app layer), body columns already exist in the index,
and text rendering + diff in Lex.Web (~80 lines, app layer).

## Verification

After increment B ships, run `git diff --stat <this-commit>..HEAD -- src/Lex.Law src/Lex.Temporal`
and record the actual numbers below.

**Actual (measured 2026-08-01, `git diff --stat abc37a4..HEAD`):**
**Lex.Law: 1 insertion** (the `TextPublic` flag on `PublisherDescriptor`) ·
**Lex.Temporal: 0.**

**Verdict: the neutral model passed its falsification test.** Everything else
landed where publisher knowledge belongs: `Lex.Sources.EurLex` (adapter),
body support in the corpus writer and index mapper (app layer), text/diff
rendering in Lex.Web. Increment C may proceed.
