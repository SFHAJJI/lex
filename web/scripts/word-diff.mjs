// The wording of an article between two versions, word by word (the specification of record's R57 and R16; the audit of
// 2026-10-27, gap 2): what the compare page shows inline for a changed article, from the two texts as the API serves them
// (`diff`'s `text` on each side, R57's `text_rule`), and nothing else.
//
// A text is read as tokens: a word (letters and digits, with the apostrophes inside it), a run of white space, or one other
// character (a punctuation mark, a symbol). The two token lists are compared by the shortest edit script (Myers, 1986)
// after their common beginning and end are set aside, and the result is a list of parts, each the same on both sides,
// deleted from the earlier text, or inserted in the later one, in reading order, a deletion before the insertion that
// replaces it. Joining the parts that are the same or deleted gives the earlier text exactly; the same or inserted, the later
// one: nothing is normalised, so a change of a space, a capital or an accent is shown as the change it is.
//
// A comparison that would take more than `maxEdits` edits is shown as the whole earlier text deleted and the whole later
// one inserted, which is still exact and is said as such (`complete: false`): a bound, so one long article cannot hold a
// page's renderer.
//
// Data and pure functions only: the server renderer and the browser share it.

/** A word, a run of white space, or any one other character. */
const TOKEN = /[\p{L}\p{M}\p{N}]+(?:['’][\p{L}\p{M}\p{N}]+)*|\s+|[^\s\p{L}\p{M}\p{N}]/gu;

/** The edits past which a comparison is shown whole (see above). */
export const MAX_EDITS = 2000;

/** A text's tokens, which joined give the text back. */
export function wordTokens(text) {
  if (typeof text !== 'string') throw new Error('a text to compare is a string');
  return text.match(TOKEN) ?? [];
}

/**
 * The shortest edit script between two token lists, as parts of `same`, `del` and `ins` tokens, or null when it takes more
 * than `maxEdits` edits.
 */
function editScript(a, b, maxEdits) {
  const n = a.length;
  const m = b.length;
  const max = Math.min(n + m, maxEdits);
  const offset = max + 1;
  const v = new Int32Array(2 * max + 3);
  // The furthest x on each diagonal k, as it stood before each round d: diagonals -d to d, at k + d. Kept per round so the
  // script can be walked back; d rounds keep d squared values, which the bound keeps small.
  const trace = [];
  let found = -1;
  for (let d = 0; d <= max && found < 0; d += 1) {
    trace.push(v.slice(offset - d, offset + d + 1));
    for (let k = -d; k <= d; k += 2) {
      let x = k === -d || (k !== d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
      let y = x - k;
      while (x < n && y < m && a[x] === b[y]) {
        x += 1;
        y += 1;
      }
      v[offset + k] = x;
      if (x >= n && y >= m) {
        found = d;
        break;
      }
    }
  }
  if (found < 0) return null;

  // Walk back from the end: in round d, the snake (the same tokens) after the one edit that reached diagonal k.
  const steps = [];
  let x = n;
  let y = m;
  for (let d = found; d > 0; d -= 1) {
    const before = trace[d];
    const at = (k) => before[k + d];
    const k = x - y;
    const down = k === -d || (k !== d && at(k - 1) < at(k + 1));
    const previousK = down ? k + 1 : k - 1;
    const previousX = at(previousK);
    const previousY = previousX - previousK;
    while (x > previousX && y > previousY) {
      steps.push(['same', a[x - 1]]);
      x -= 1;
      y -= 1;
    }
    if (down) {
      steps.push(['ins', b[y - 1]]);
      y -= 1;
    } else {
      steps.push(['del', a[x - 1]]);
      x -= 1;
    }
  }
  while (x > 0 && y > 0) {
    steps.push(['same', a[x - 1]]);
    x -= 1;
    y -= 1;
  }
  steps.reverse();
  return steps;
}

/**
 * Steps as parts: a run of tokens the same on both sides is one part, and a run of changes is one deletion (its deleted
 * tokens in order) before one insertion (its inserted tokens in order), which give the run's earlier and later text. White
 * space alone between two changes is read as part of both, so "peut accorder" against "accorde" reads as one replacement
 * rather than two around a space.
 */
function merged(steps) {
  const runs = [];
  for (const [kind, token] of steps) {
    const last = runs[runs.length - 1];
    if (last !== undefined && last.kind === kind) last.text += token;
    else runs.push({ kind, text: token });
  }
  for (let index = 1; index < runs.length - 1; index += 1) {
    if (runs[index].kind === 'same' && /^\s+$/u.test(runs[index].text) && runs[index - 1].kind !== 'same' && runs[index + 1].kind !== 'same') {
      runs.splice(index, 1, { kind: 'del', text: runs[index].text }, { kind: 'ins', text: runs[index].text });
    }
  }
  const parts = [];
  let deleted = '';
  let inserted = '';
  const flush = () => {
    if (deleted.length > 0) parts.push({ kind: 'del', text: deleted });
    if (inserted.length > 0) parts.push({ kind: 'ins', text: inserted });
    deleted = '';
    inserted = '';
  };
  for (const run of runs) {
    if (run.kind === 'del') deleted += run.text;
    else if (run.kind === 'ins') inserted += run.text;
    else {
      flush();
      parts.push(run);
    }
  }
  flush();
  return parts;
}

/**
 * The wording of `before` against `after`, word by word: `{ parts, complete }`, `parts` a frozen list of `{ kind, text }`
 * (`same`, `del`, `ins`), `complete` false when the comparison was shown whole past `maxEdits` (see above).
 */
export function wordDiff(before, after, { maxEdits = MAX_EDITS } = {}) {
  const a = wordTokens(before);
  const b = wordTokens(after);
  let start = 0;
  while (start < a.length && start < b.length && a[start] === b[start]) start += 1;
  let endA = a.length;
  let endB = b.length;
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) {
    endA -= 1;
    endB -= 1;
  }
  const middleA = a.slice(start, endA);
  const middleB = b.slice(start, endB);
  const script = editScript(middleA, middleB, maxEdits);
  const steps = [
    ...a.slice(0, start).map((token) => ['same', token]),
    ...(script ?? [...middleA.map((token) => ['del', token]), ...middleB.map((token) => ['ins', token])]),
    ...a.slice(endA).map((token) => ['same', token]),
  ];
  const parts = merged(steps).filter((part) => part.text.length > 0);
  return Object.freeze({ parts: Object.freeze(parts.map((part) => Object.freeze(part))), complete: script !== null });
}

/** The earlier text the parts give back (`same` and `del`), and the later one (`same` and `ins`). */
export function sidesOf(parts) {
  return Object.freeze({
    before: parts.filter((part) => part.kind !== 'ins').map((part) => part.text).join(''),
    after: parts.filter((part) => part.kind !== 'del').map((part) => part.text).join(''),
  });
}
