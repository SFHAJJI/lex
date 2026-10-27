// The platform's text folding as this site reads an answer by it (`TextFolding` in Lex.V3.Contracts; the plan's item 4.4):
// the search matches a query and an article's text once both are folded, so that capitals and accents never decide a match.
// This file folds only to check what an answer says it matched: the platform's fold is the rule, and every match an extract
// marks is one the platform found. The fold here is at least as coarse as the platform's (each letter to its canonical
// decomposition without its combining marks and in lower case, then decomposed again and its marks dropped again; œ and æ as
// oe and ae; the typographic apostrophes as the straight one, the no-break spaces as a space, and a soft hyphen dropped, as the
// review of #1005 has the platform fold them), so two texts the platform folds alike fold alike here too, and a marked part
// that holds the query in the platform's fold holds it in this one.

const COMBINING = /\p{Mn}/gu;
const APOSTROPHES = /[\u2019\u2018\u02BC]/gu;
const NO_BREAK_SPACES = /[\u00A0\u202F\u2007]/gu;
const SOFT_HYPHEN = /\u00AD/gu;

/** A text folded: decomposed, its non-spacing marks dropped, lowered, decomposed again without its marks, ligatures as two letters. */
export function foldText(value) {
  if (typeof value !== 'string') throw new Error(`${JSON.stringify(value)} is not text to fold`);
  return value.replace(APOSTROPHES, "'").replace(NO_BREAK_SPACES, ' ').replace(SOFT_HYPHEN, '')
    .normalize('NFD').replace(COMBINING, '').toLowerCase().normalize('NFD').replace(COMBINING, '')
    .replaceAll('œ', 'oe').replaceAll('æ', 'ae');
}
