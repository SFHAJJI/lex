// The text page's data (the owner's brief of 2026-11-15, section 4): what the page draws, read from the API's answers.
//
// Pure functions, no request and no DOM, so each rule is tested on real answers (`test/text-page.test.mjs`):
//   - the text's own title (trap 2: a work's dossier lists the titles of the acts that amended it too);
//   - the versions held, each with the articles changed or added in it (one `changes_in_period` answer for the whole text);
//   - an article's number and heading, and its paragraphs and numbered points rebuilt from its text (the API serves an
//     article's text as one string, the heading and paragraphs run together: "Article 44Délai de prescription1.Le délai…");
//   - the time axis: each version placed in proportion to the calendar, from the first held to today;
//   - which versions changed each article (the pairwise comparisons' statuses);
//   - the word-level comparison of two wordings, paragraph aligned to paragraph, ' and ’ read as one.

const DATE = /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u;

/** A date's day number since 1970 (UTC), for placing it on the axis. */
export function dayNumber(date) {
  if (typeof date !== 'string' || !DATE.test(date)) return null;
  const [y, m, d] = date.split('-').map(Number);
  return Math.floor(Date.UTC(y, m - 1, d) / 86400000);
}

/** The prefix a consolidation's title carries ("Version consolidée applicable au 25/12/2006 : "). */
const CONSOLIDATION_PREFIX = /^Version consolid[ée]e applicable au [0-9]{2}\/[0-9]{2}\/[0-9]{4}\s*:\s*/u;

/** A title as shown: its consolidation prefix and its final full stop taken off, its spaces kept. */
export function cleanTitle(title) {
  if (typeof title !== 'string') return null;
  const shown = title.replace(CONSOLIDATION_PREFIX, '').trim().replace(/\.$/u, '').trim();
  return shown.length === 0 ? null : shown;
}

/**
 * The text's own title (trap 2), from its dossier (`dossier`'s value): the titles of the text's own versions
 * (`titles[].titles[].title`, the latest version's first), never the titles in `publisher_relations` or a state's
 * relations, which name the acts that amended it. Null when the dossier holds none.
 */
export function ownTitleOf(dossier) {
  const entries = Array.isArray(dossier?.titles) ? dossier.titles : [];
  for (const entry of [...entries].reverse()) {
    for (const item of [...(entry?.titles ?? []), ...(entry?.short_titles ?? [])]) {
      const title = cleanTitle(item?.title);
      if (title !== null) return title;
    }
  }
  return null;
}

/**
 * The versions held, oldest first, from the text's `changes_in_period` answer over its whole history: each with its date,
 * the date it applied until (null for the version in force), and the articles changed and added in it (null for the first
 * version held, which has nothing before it to be compared with).
 */
export function versionsOf(changes) {
  const rows = Array.isArray(changes?.changes) ? changes.changes : [];
  const versions = [];
  for (const row of rows) {
    const state = row?.state;
    if (state === null || typeof state !== 'object' || !DATE.test(state.applicability_date ?? '')) continue;
    const counts = row.counts ?? null;
    versions.push(Object.freeze({
      date: state.applicability_date,
      until: DATE.test(state.applicable_to ?? '') ? state.applicable_to : (DATE.test(state.next_applicability_date ?? '') ? state.next_applicability_date : null),
      articleCount: Number.isInteger(state.article_count) ? state.article_count : null,
      first: row.reason === 'first_held_state' || counts === null,
      changed: counts === null ? null : (counts.changed ?? 0),
      added: counts === null ? null : (counts.added ?? 0),
      removed: counts === null ? null : (counts.removed ?? 0),
      unchanged: counts === null ? null : (counts.unchanged ?? 0),
      bar: counts === null ? null : (counts.changed ?? 0) + (counts.added ?? 0),
      previous: DATE.test(row?.baseline?.applicability_date ?? '') ? row.baseline.applicability_date : null,
      textHeld: state.text_status === undefined || state.text_status === null || state.text_status === 'held',
    }));
  }
  versions.sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0));
  return Object.freeze(versions);
}

/** The version a date falls in (the latest one applying from on or before it), or null before the first. */
export function versionAt(versions, date) {
  let found = null;
  for (const version of versions) if (version.date <= date) found = version;
  return found;
}

/**
 * The time axis's geometry for `versions` drawn `width` wide, from the first held version to `today`: each version's x, the
 * year ticks (every year, or every second or fifth when crowded), and the scale. The left and right margins leave room for
 * the "first held" and "today" labels.
 */
export function axisGeometry(versions, today, width, { left = 46, right = 46 } = {}) {
  const first = versions.length === 0 ? dayNumber(today) - 365 : dayNumber(versions[0].date);
  const last = Math.max(dayNumber(today), first + 30);
  const span = last - first;
  const x0 = left;
  const x1 = Math.max(width - right, left + 10);
  const at = (date) => x0 + ((dayNumber(date) - first) / span) * (x1 - x0);
  const firstYear = Number(versions[0]?.date.slice(0, 4) ?? today.slice(0, 4));
  const lastYear = Number(today.slice(0, 4));
  const years = lastYear - firstYear;
  const step = years <= 12 ? (width < 700 ? 2 : 1) : years <= 26 ? 2 : years <= 60 ? 5 : 10;
  const ticks = [];
  for (let year = firstYear + 1; year <= lastYear; year += 1) {
    if (year % step !== 0) continue;
    const x = at(`${year}-01-01`);
    if (x > x0 + 24 && x < x1 - 40) ticks.push(Object.freeze({ year, x }));
  }
  return Object.freeze({ x0, x1, at, ticks: Object.freeze(ticks) });
}

/** The number an article is cited by, from the publisher's id: `art_44` 44, `art_1er` 1er, `art_62-11` 62-11. */
export function articleNumberOf(publisherId) {
  if (typeof publisherId !== 'string') return '';
  return publisherId.replace(/^(?:art|article)[_-]?/iu, '').replaceAll('_', ' ');
}

const LATIN_ADVERBS = '(?:bis|ter|quater|quinquies|sexies|septies|octies|novies|nonies|decies)';
const ARTICLE_LEAD = new RegExp(`^\\s*(?:Article|Art\\.)\\s*(?:[0-9]+(?:er|e)?(?:\\s?${LATIN_ADVERBS})?(?:[-–.][0-9]+(?:\\s?${LATIN_ADVERBS})?)*|[IVXLC]+|unique)\\s*\\.?\\s*`, 'u');
const UPPER = /[A-ZÀ-ÖØ-ÞŒ«]/u;
const LOWER = /[a-zß-öø-ÿœ]/u;
const POINT = /^(?:([0-9]{1,3})\s?(?:°|\.)(?=\s?\S)|([a-z])\)(?=\s?\S)|([0-9]{1,3})\)(?=\s?\S))/u;

/**
 * Where a run-together text starts a new block: a sentence's or a heading's end directly followed, with no space, by a
 * capital letter or a numbered point ("…trois ans.Le délai", "prescription1.Le", "garantieLorsque"). Never inside a word
 * after an apostrophe or a hyphen ("l'Union"), never in an acronym ("CSSF").
 */
function boundaries(text) {
  const cuts = [];
  for (let i = 1; i < text.length; i += 1) {
    const prev = text[i - 1];
    const cur = text[i];
    if (/\s/u.test(prev) || /\s/u.test(cur)) continue;
    if (/['’\-(/«"]/u.test(prev)) continue;
    const rest = text.slice(i, i + 6);
    const point = POINT.test(rest);
    if (UPPER.test(cur)) {
      if (LOWER.test(prev) || /[.;:)»]/u.test(prev)) cuts.push(i);
    } else if (point && /[0-9]/u.test(cur)) {
      if (LOWER.test(prev) || /[.;:)»]/u.test(prev)) cuts.push(i);
    } else if (point && /[.;:]/u.test(prev)) {
      cuts.push(i);
    }
  }
  return cuts;
}

/**
 * An article's text as the page shows it: its heading (or null), and its paragraphs, each with the marker of its numbered
 * point ("1.", "2°", "a)") apart, or none. The words are the text's own, in its order: only where a block starts is read.
 */
export function articleParts(text) {
  if (typeof text !== 'string' || text.trim().length === 0) return Object.freeze({ heading: null, paragraphs: Object.freeze([]) });
  const body = text.replace(ARTICLE_LEAD, '');
  const cuts = boundaries(body);
  const blocks = [];
  let start = 0;
  for (const cut of [...cuts, body.length]) {
    const block = body.slice(start, cut).trim();
    if (block.length > 0) blocks.push(block);
    start = cut;
  }
  // A numbered point cut off alone ("1." before "Le délai…") is the marker of the block after it.
  for (let k = blocks.length - 2; k >= 0; k -= 1) {
    if (/^(?:[0-9]{1,3}\s?[.°]|[a-z0-9]\))$/u.test(blocks[k])) blocks.splice(k, 2, `${blocks[k]}${blocks[k + 1]}`);
  }
  let heading = null;
  if (blocks.length > 1 && blocks[0].length <= 140 && !/[.;:]$/u.test(blocks[0]) && !POINT.test(blocks[0])) heading = blocks.shift();
  const paragraphs = blocks.map((block) => {
    const match = POINT.exec(block);
    if (match === null) return Object.freeze({ marker: null, text: block });
    const marker = match[1] !== undefined ? `${match[1]}${block[match[1].length] === '°' ? '°' : '.'}` : match[2] !== undefined ? `${match[2]})` : `${match[3]})`;
    return Object.freeze({ marker, text: block.slice(match[0].length).trim() });
  });
  return Object.freeze({ heading, paragraphs: Object.freeze(paragraphs) });
}

/** Text compared for sameness: apostrophes read as one, spaces as one. */
export function comparable(text) {
  return String(text ?? '').replace(/[’‘ʼ]/gu, "'").replace(/[  ]/gu, ' ').replace(/\s+/gu, ' ').trim();
}

/**
 * Which versions changed each article, from the pairwise comparisons of each version with the one before (`diff` values,
 * any order): a map from the publisher's id to the dates of the versions where its wording changed or it was added.
 */
export function changeIndex(diffs) {
  const index = new Map();
  for (const value of diffs) {
    const comparison = value?.comparisons?.[0];
    const to = comparison?.to?.applicability_date ?? value?.requested_date_to;
    if (!comparison || !DATE.test(to ?? '')) continue;
    for (const article of comparison.articles ?? []) {
      if (article.status !== 'changed' && article.status !== 'added') continue;
      const dates = index.get(article.publisher_id) ?? [];
      if (!dates.includes(to)) dates.push(to);
      dates.sort();
      index.set(article.publisher_id, dates);
    }
  }
  return index;
}

/** The words of a paragraph, each with the space after it, for the word-level comparison. */
function words(text) {
  return text.match(/\S+\s*/gu) ?? [];
}

/**
 * Two wordings compared word by word (longest common subsequence): the left runs (`same` or `removed`) and the right runs
 * (`same` or `added`), each a list of `{ kind, text }` with neighbours of one kind joined. ' and ’ compare as one.
 */
export function wordDiff(before, after) {
  const a = words(before);
  const b = words(after);
  const key = (word) => comparable(word);
  const n = a.length;
  const m = b.length;
  // A table of the longest common suffix lengths; small enough for an article's paragraph.
  const table = new Uint32Array((n + 1) * (m + 1));
  for (let i = n - 1; i >= 0; i -= 1) {
    for (let j = m - 1; j >= 0; j -= 1) {
      table[i * (m + 1) + j] = key(a[i]) === key(b[j]) ? table[(i + 1) * (m + 1) + j + 1] + 1 : Math.max(table[(i + 1) * (m + 1) + j], table[i * (m + 1) + j + 1]);
    }
  }
  // The edit script, word by word: '=' both sides, '-' left only, '+' right only.
  const ops = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (key(a[i]) === key(b[j])) {
      ops.push({ op: '=', a: a[i], b: b[j] });
      i += 1;
      j += 1;
    } else if (table[(i + 1) * (m + 1) + j] >= table[i * (m + 1) + j + 1]) {
      ops.push({ op: '-', a: a[i] });
      i += 1;
    } else {
      ops.push({ op: '+', b: b[j] });
      j += 1;
    }
  }
  while (i < n) ops.push({ op: '-', a: a[i++] });
  while (j < m) ops.push({ op: '+', b: b[j++] });
  // A short island of common words (three or fewer) between two edits reads as part of one change, as a person marks it:
  // "en [envoyant…] à l'assureur [au moins…]" is one phrase replaced, not four.
  let k = 0;
  while (k < ops.length) {
    if (ops[k].op !== '=') {
      k += 1;
      continue;
    }
    let end = k;
    while (end < ops.length && ops[end].op === '=') end += 1;
    const before = k > 0 && ops[k - 1].op !== '=';
    const after = end < ops.length && ops[end].op !== '=';
    if (before && after && end - k <= 3) {
      const island = ops.splice(k, end - k);
      ops.splice(k, 0, ...island.map((o) => ({ op: '-', a: o.a })), ...island.map((o) => ({ op: '+', b: o.b })));
      k += island.length * 2;
    } else {
      k = end;
    }
  }
  const left = [];
  const right = [];
  const push = (runs, kind, text) => {
    const last = runs.at(-1);
    if (last && last.kind === kind) last.text += text;
    else runs.push({ kind, text });
  };
  for (const o of ops) {
    if (o.op === '=') {
      push(left, 'same', o.a);
      push(right, 'same', o.b);
    } else if (o.op === '-') push(left, 'removed', o.a);
    else push(right, 'added', o.b);
  }
  return { left, right };
}

/** How alike two paragraphs are: the share of the longer one's words they have in common, in order. */
function likeness(a, b) {
  const { left, right } = wordDiff(a, b);
  const same = left.filter((run) => run.kind === 'same').reduce((sum, run) => sum + words(run.text).length, 0);
  const total = Math.max(words(a).length, words(b).length, 1);
  void right;
  return same / total;
}

/**
 * Two articles' paragraphs side by side, paragraph aligned to paragraph: rows of `{ kind, left, right }` where kind is
 * `same` (identical once compared), `changed` (with `diff`, the word-level runs), `removed` (left only: the right reads
 * "Not in this version") or `added` (right only). A paragraph's numbered point stays with it.
 */
export function alignParagraphs(before, after) {
  const a = before.map((p) => ({ ...p, key: comparable(`${p.marker ?? ''} ${p.text}`) }));
  const b = after.map((p) => ({ ...p, key: comparable(`${p.marker ?? ''} ${p.text}`) }));
  const n = a.length;
  const m = b.length;
  const table = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i -= 1) {
    for (let j = m - 1; j >= 0; j -= 1) {
      table[i][j] = a[i].key === b[j].key ? table[i + 1][j + 1] + 1 : Math.max(table[i + 1][j], table[i][j + 1]);
    }
  }
  const rows = [];
  let i = 0;
  let j = 0;
  const pending = { left: [], right: [] };
  const flush = () => {
    // Between two identical anchors, pair what is alike enough to read as one paragraph changed; the rest is removed or added.
    const left = pending.left;
    const right = pending.right;
    let r = 0;
    for (const l of left) {
      let paired = -1;
      for (let k = r; k < right.length; k += 1) {
        if (likeness(l.text, right[k].text) >= 0.4) {
          paired = k;
          break;
        }
      }
      if (paired === -1) {
        rows.push({ kind: 'removed', left: l, right: null });
        continue;
      }
      for (let k = r; k < paired; k += 1) rows.push({ kind: 'added', left: null, right: right[k] });
      rows.push({ kind: 'changed', left: l, right: right[paired], diff: wordDiff(l.text, right[paired].text) });
      r = paired + 1;
    }
    for (let k = r; k < right.length; k += 1) rows.push({ kind: 'added', left: null, right: right[k] });
    pending.left = [];
    pending.right = [];
  };
  while (i < n && j < m) {
    if (a[i].key === b[j].key) {
      flush();
      rows.push({ kind: 'same', left: a[i], right: b[j] });
      i += 1;
      j += 1;
    } else if (table[i + 1][j] >= table[i][j + 1]) {
      pending.left.push(a[i++]);
    } else {
      pending.right.push(b[j++]);
    }
  }
  while (i < n) pending.left.push(a[i++]);
  while (j < m) pending.right.push(b[j++]);
  flush();
  return rows;
}

/** Whether two wordings read the same once compared (trap 1: the digest also covers references and typography). */
export function sameVisibleWording(before, after) {
  return comparable(before) === comparable(after);
}
