// Word-level diff. A legal amendment is usually a handful of words inside a long
// paragraph, so a line-based diff shows the whole paragraph as changed and hides the
// actual edit. Classic LCS over word tokens, which is fast enough at article scale.

export type Piece = { t: string; k: " " | "+" | "-" };

const split = (s: string) => s.split(/(\s+)/).filter((x) => x.length > 0);

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

  const L: Uint32Array[] = Array.from({ length: n + 1 }, () => new Uint32Array(m + 1));
  for (let i = n - 1; i >= 0; i--)
    for (let j = m - 1; j >= 0; j--)
      L[i][j] = a[i] === b[j] ? L[i + 1][j + 1] + 1 : Math.max(L[i + 1][j], L[i][j + 1]);

  const out: Piece[] = [];
  const push = (t: string, k: Piece["k"]) => {
    const last = out[out.length - 1];
    if (last && last.k === k) last.t += t;
    else out.push({ t, k });
  };

  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (a[i] === b[j]) { push(a[i], " "); i++; j++; }
    else if (L[i + 1][j] >= L[i][j + 1]) { push(a[i], "-"); i++; }
    else { push(b[j], "+"); j++; }
  }
  while (i < n) push(a[i++], "-");
  while (j < m) push(b[j++], "+");
  return out;
}

export const changed = (p: Piece[]) => p.some((x) => x.k !== " ");
