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
   .replace(/[   ]/g, " ")
   .replace(/[“”«»]/g, '"')
   .replace(/œ/g, "oe")
   .toLowerCase();

export function diffWords(before: string, after: string): Piece[] {
  const a = split(before);
  const b = split(after);
  const n = a.length;
  const m = b.length;

  // LCS table, capped: past a few thousand tokens fall back to a whole-block change
  // rather than spending seconds building an O(n*m) matrix in the browser.
  if (n * m > 4_000_000) {
    return [{ t: before, k: "-" }, { t: after, k: "+" }];
  }

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

export const changed = (p: Piece[]) => p.some((x) => x.k !== " ");
