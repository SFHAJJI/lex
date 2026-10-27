// The platform's text folding as this site reads an answer by it (`TextFolding` in Lex.V3.Contracts; the plan's item 4.4):
// the search matches a query and an article's text once both are folded, so that capitals and accents never decide a match.
// This file folds only to check what an answer says it matched: the platform's fold is the rule, and every match an extract
// marks is one the platform found. The fold here is at least as coarse as the platform's (each letter to its canonical
// decomposition without its combining marks and in lower case, then decomposed again and its marks dropped again; œ and æ as
// oe and ae), so two texts the platform folds alike fold alike here too, and a marked part that holds the query in the
// platform's fold holds it in this one.

const COMBINING = /\p{Mn}/gu;

/** A text folded: decomposed, its non-spacing marks dropped, lowered, decomposed again without its marks, ligatures as two letters. */
export function foldText(value) {
  if (typeof value !== 'string') throw new Error(`${JSON.stringify(value)} is not text to fold`);
  return value.normalize('NFD').replace(COMBINING, '').toLowerCase().normalize('NFD').replace(COMBINING, '')
    .replaceAll('œ', 'oe').replaceAll('æ', 'ae');
}
