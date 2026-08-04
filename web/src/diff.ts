// Word-level diff. A legal amendment is usually a handful of words inside a long
// paragraph, so a line-based diff shows the whole paragraph as changed and hides the
// actual edit. Classic LCS over word tokens, which is fast enough at article scale.

export type Piece = { t: string; k: " " | "+" | "-" };

const split = (s: string) => s.split(/(\s+)/).filter((x) => x.length > 0);

/**
 * How two tokens are judged the same.
 *
 * Publishers reset their typography without touching a word of law: the typewriter apostrophe
 * becomes the typographic one, a hyphen becomes an en dash, a space becomes a non-breaking space.
 * Comparing raw bytes makes every one of those a change, and a real amendment then arrives buried
 * in twenty of them. Measured on the 2015 remuneration law: one article had a single genuine edit,
 * 2025 to 2026, surrounded by about twenty apostrophe swaps shown in red and green beside it.
 *
 * So tokens are MATCHED on this form and DISPLAYED in their original one. Nothing stored is
 * altered and no hash moves; a difference that is only typographic simply stops being called a
 * difference, which is the honest answer to "what changed in this article".
 */
export const sameWord = (t: string) =>
  t.replace(/[’ʼʹ]/g, "'")
   .replace(/[–—−]/g, "-")
   .replace(/[   ]/g, " ")
   .replace(/[“”«»]/g, '"')
   .replace(/œ/g, "oe")
   .toLowerCase();

/**
 * The LCS matrix is O(n*m) cells, so it cannot be run over a long article directly: the AIFM
 * law's Article 14 grew from about 1,200 tokens to 3,500, which is 4.2 million cells, past the
 * ceiling. The old answer was to give up and mark the whole article removed-then-added, which is
 * how an article whose first four paragraphs are untouched came to be painted entirely red beside
 * an entirely green copy of itself.
 *
 * Raising the ceiling only moves the failure. Diffing paragraphs first fixes it: an article has
 * tens of paragraphs, not thousands of words, so matching those is trivial, and the word diff then
 * runs only inside a paragraph that actually changed, where it is small and exact. Legal text is
 * paragraph-structured to begin with, so this follows the document rather than fighting it.
 */
const CELL_CAP = 4_000_000;

export function diffWords(before: string, after: string): Piece[] {
  const a = split(before);
  const b = split(after);
  if (a.length * b.length <= CELL_CAP) return lcsWords(a, b);
  return byParagraph(before, after);
}

function lcsWords(a: string[], b: string[]): Piece[] {
  const n = a.length;
  const m = b.length;

  // Compare on the normalised form, keep the original for display.
  const ka = a.map(sameWord);
  const kb = b.map(sameWord);

  const L: Uint32Array[] = Array.from({ length: n + 1 }, () => new Uint32Array(m + 1));
  for (let i = n - 1; i >= 0; i--)
    for (let j = m - 1; j >= 0; j--)
      L[i][j] = ka[i] === kb[j] ? L[i + 1][j + 1] + 1 : Math.max(L[i + 1][j], L[i][j + 1]);

  const out: Piece[] = [];
  const push = (t: string, k: Piece["k"]) => {
    const last = out[out.length - 1];
    if (last && last.k === k) last.t += t;
    else out.push({ t, k });
  };

  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    // Matched tokens render in the LATER version's spelling: it is the text in force, and showing
    // the earlier one would quietly present superseded typography as current.
    if (ka[i] === kb[j]) { push(b[j], " "); i++; j++; }
    else if (L[i + 1][j] >= L[i][j + 1]) { push(a[i], "-"); i++; }
    else { push(b[j], "+"); j++; }
  }
  while (i < n) push(a[i++], "-");
  while (j < m) push(b[j++], "+");
  return out;
}

/** Paragraphs, keeping their separators so the rebuilt text reads as the publisher set it. */
const paras = (s: string) => s.split(/(\n+)/).filter((x) => x.length > 0);

function byParagraph(before: string, after: string): Piece[] {
  const a = paras(before);
  const b = paras(after);
  const ka = a.map((p) => sameWord(p).replace(/\s+/g, " ").trim());
  const kb = b.map((p) => sameWord(p).replace(/\s+/g, " ").trim());
  const n = a.length;
  const m = b.length;

  const L: Uint32Array[] = Array.from({ length: n + 1 }, () => new Uint32Array(m + 1));
  for (let i = n - 1; i >= 0; i--)
    for (let j = m - 1; j >= 0; j--)
      L[i][j] = ka[i] === kb[j] ? L[i + 1][j + 1] + 1 : Math.max(L[i + 1][j], L[i][j + 1]);

  const out: Piece[] = [];
  const push = (t: string, k: Piece["k"]) => {
    if (t.length === 0) return;
    const last = out[out.length - 1];
    if (last && last.k === k) last.t += t;
    else out.push({ t, k });
  };

  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (ka[i] === kb[j]) { push(b[j], " "); i++; j++; continue; }

    // A run of paragraphs present on one side only. Where both sides have one, they are the same
    // paragraph rewritten, so diff them word by word; the pieces come back small because a single
    // paragraph is nowhere near the ceiling.
    const di: string[] = [];
    const dj: string[] = [];
    while (i < n && (j >= m || ka[i] !== kb[j]) && !kb.slice(j).includes(ka[i])) di.push(a[i++]);
    while (j < m && (i >= n || ka[i] !== kb[j]) && !ka.slice(i).includes(kb[j])) dj.push(b[j++]);
    const pairs = Math.min(di.length, dj.length);
    for (let k = 0; k < pairs; k++) out.push(...lcsWords(split(di[k]), split(dj[k])));
    for (let k = pairs; k < di.length; k++) push(di[k], "-");
    for (let k = pairs; k < dj.length; k++) push(dj[k], "+");
    // Neither side advanced, which would spin forever: take one paragraph off each and move on.
    if (di.length === 0 && dj.length === 0) { push(a[i++], "-"); push(b[j++], "+"); }
  }
  while (i < n) push(a[i++], "-");
  while (j < m) push(b[j++], "+");
  return out;
}

export const changed = (p: Piece[]) => p.some((x) => x.k !== " ");
