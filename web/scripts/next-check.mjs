// The side build's checks, with no browser:
//
//   1. the copy tables (`next-copy.mjs`): French and English hold the same keys, in the same shapes; the French is held to
//      its typography (a no-break space before : ; ! ? % » and after «, no straight apostrophe or quotation mark); the
//      digits typed in the copy are listed, and any outside the known list is a fault (counts come from the data);
//   2. the dock rendered to a string (react-dom/server) for every sample answer of the new engine, in both languages, and
//      each rendering read against its answer: the question, the outcome's sentence, one card a block with every line and
//      the link that opens the article in the page, one row a step; no inline style; no English sentence on a French page;
//   3. the three counted pieces (How it works, Results, the corpus section) and the references panel rendered from their
//      fixtures, in both languages; the Results page's fresh test (`results.fresh`) read against the data it was drawn
//      from (`freshFaults`), from the fixture, from the engine's file of today, and from synthetic files with keys missing,
//      of the wrong type or not known to the copy.
//
// `node scripts/next-check.mjs` runs them all, writes each rendering under `.react-build/next-check/` and prints a report;
// `test/next.test.mjs` runs the same functions under `npm test`.

import { readFileSync, readdirSync } from 'node:fs';
import { mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

import { NEXT_COPY, countedForm, decimal, fillNext, groupedCount, nextCopy, numericDate } from './next-copy.mjs';
import { ASSERTED_OUTCOMES, identifierOf, outcomeOf } from './next-view.mjs';
import { legacyAnswer } from './next-contract.mjs';

/** The sample answers of the new engine (the contract the dock is built to): `lex-next/web-samples/`, beside this folder. */
export const SAMPLES = new URL('../../web-samples/', import.meta.url);
/** The engine's current scored test (`GET /api/next/results`): `lex-next/app/results.json`, rewritten at each run. */
export const RESULTS_EXAMPLE = new URL('../../app/results.json', import.meta.url);
/** A copy of it kept as the contract the Results page is checked against, its fresh test included (that of 21/11/2026). */
export const RESULTS_FIXTURE = new URL('../test/next-fixtures/results.json', import.meta.url);
/** The corpus counts read once from the engine's database (the shape of `GET /api/next/corpus`), and a synthetic article. */
export const CORPUS_FIXTURE = new URL('../test/next-fixtures/corpus.json', import.meta.url);
export const ARTICLE_FIXTURE = new URL('../test/next-fixtures/article.json', import.meta.url);
export const LINKS_FIXTURE = new URL('../test/next-fixtures/links.json', import.meta.url);
/** Where the renderings are written, to be read. */
export const OUTPUT = new URL('../.react-build/next-check/', import.meta.url);

const LOCALES = Object.freeze(['fr', 'en']);

/** Every leaf of a table with its path: `[path, value]`. */
export function leavesOf(table, path = []) {
  if (table !== null && typeof table === 'object') return Object.entries(table).flatMap(([key, value]) => leavesOf(value, [...path, key]));
  return [[path.join('.'), table]];
}

/** Where the two tables differ in shape: a key one lacks, a leaf of another type, a list of another length. */
export function copyKeyDifferences(a = NEXT_COPY.en, b = NEXT_COPY.fr, path = []) {
  const at = path.join('.') || '(root)';
  const kind = (value) => (Array.isArray(value) ? 'list' : value === null ? 'null' : typeof value);
  if (kind(a) !== kind(b)) return [`${at}: ${kind(a)} in English, ${kind(b)} in French`];
  if (kind(a) === 'list' && a.length !== b.length) return [`${at}: ${a.length} entries in English, ${b.length} in French`];
  if (kind(a) !== 'list' && kind(a) !== 'object') return [];
  const keys = [...new Set([...Object.keys(a), ...Object.keys(b)])];
  return keys.flatMap((key) => (!Object.hasOwn(a, key) ? [`${[...path, key].join('.')}: missing in English`]
    : !Object.hasOwn(b, key) ? [`${[...path, key].join('.')}: missing in French`]
      : copyKeyDifferences(a[key], b[key], [...path, key])));
}

/** The places a template names, in order of name: both languages must name the same. */
const placesOf = (text) => [...String(text).matchAll(/\{([a-zA-Z]+)\}/gu)].map((match) => match[1]).sort().join(',');

/** The leaves whose templates do not name the same places in both languages. */
export function copyPlaceDifferences() {
  const french = new Map(leavesOf(NEXT_COPY.fr));
  return leavesOf(NEXT_COPY.en)
    .filter(([path, value]) => typeof value === 'string' && typeof french.get(path) === 'string' && placesOf(value) !== placesOf(french.get(path)))
    .map(([path, value]) => `${path}: {${placesOf(value)}} in English, {${placesOf(french.get(path))}} in French`);
}

const NO_BREAK = '  ';

/** The faults of French typography in a text: each `{ fault, at }`. */
export function frenchFaultsIn(text) {
  const faults = [];
  const value = String(text);
  if (/['"]/u.test(value)) faults.push({ fault: 'a straight apostrophe or quotation mark', at: value.search(/['"]/u) });
  for (const match of value.matchAll(/[:;!?%»]/gu)) {
    // A mark that opens the text, or follows another mark of the same family ("?!"), needs no space of its own.
    if (match.index === 0) continue;
    const before = value[match.index - 1];
    if (!NO_BREAK.includes(before)) faults.push({ fault: `no no-break space before ${JSON.stringify(match[0])}`, at: match.index });
  }
  for (const match of value.matchAll(/«/gu)) {
    if (!NO_BREAK.includes(value[match.index + 1] ?? '')) faults.push({ fault: 'no no-break space after «', at: match.index });
  }
  return faults;
}

/** Every fault of French typography in the French table: `path: fault: text`. */
export function frenchTypographyFaults(table = NEXT_COPY.fr) {
  return leavesOf(table)
    .filter(([, value]) => typeof value === 'string')
    .flatMap(([path, value]) => frenchFaultsIn(value).map(({ fault, at }) => `${path}: ${fault} (at ${at}): ${JSON.stringify(value)}`));
}

/**
 * The digits typed in the copy that are not counts of the data, by the path of their entry: the example questions (a
 * reader's words), the law quoted as an example and the year in an example, the confidence level of the interval, the
 * licence's version, and the engine's threshold for an article too short to be searched.
 */
export const KNOWN_DIGITS = Object.freeze([
  /^dock\.examples\.\d+\.question$/u,
  /^how\.choices\.\d+\.why$/u,
  /^how\.commitments\.\d+$/u,
  /^how\.limits\.leftOutReasons\.tiny$/u,
  /^corpus\.leftOutReasons\.tiny$/u,
  /^results\.firstNote$/u,
  /^results\.fresh\.columns\.interval$/u,
  /^decisions2\.items\.\d+\.(?:gain|title)$/u,
  /^results2\.(?:wrongLine|totalsColumns\.interval|slicesColumns\.interval|ablationColumns\.interval|baselineColumns\.interval)$/u,
]);

/** The entries of either table that hold a digit outside the known list. */
export function unexpectedDigits() {
  return LOCALES.flatMap((locale) => leavesOf(NEXT_COPY[locale])
    .filter(([path, value]) => typeof value === 'string' && /[0-9]/u.test(value) && !KNOWN_DIGITS.some((known) => known.test(path)))
    .map(([path, value]) => `${locale}.${path}: ${JSON.stringify(value)}`));
}

/** Every entry of either table that holds a digit, known or not (the report lists them). */
export function digitsInCopy() {
  return LOCALES.flatMap((locale) => leavesOf(NEXT_COPY[locale]).filter(([, value]) => typeof value === 'string' && /[0-9]/u.test(value)).map(([path, value]) => `${locale}.${path}: ${JSON.stringify(value)}`));
}

/** A text as React writes it in markup. */
export const inMarkup = (text) => String(text).replace(/&/gu, '&amp;').replace(/</gu, '&lt;').replace(/>/gu, '&gt;').replace(/"/gu, '&quot;').replace(/'/gu, '&#x27;');

/** The English sentences of the table (fourteen characters or more, and not the same in French): none belongs on a French page. */
export function englishSentences() {
  const french = new Map(leavesOf(NEXT_COPY.fr));
  return leavesOf(NEXT_COPY.en)
    .filter(([path, value]) => typeof value === 'string' && value.length >= 14 && !value.includes('{') && french.get(path) !== value && !/^dock\.examples\./u.test(path))
    .map(([, value]) => value);
}

/** The sample answers, by file name. */
export async function readSamples() {
  const names = (await readdir(SAMPLES)).filter((name) => name.endsWith('.json')).sort();
  return Promise.all(names.map(async (name) => ({ name: name.replace(/\.json$/u, ''), answer: JSON.parse(await readFile(new URL(name, SAMPLES), 'utf8')) })));
}

/** The render entry (`app/next-check-entry.jsx`), bundled once for each interface language. */
export async function loadRenderers(tag = '') {
  const { bundle } = await import('./react-build.mjs');
  const suffix = tag ? `.${tag}` : '';
  const en = await import(pathToFileURL(await bundle('app/next-check-entry.jsx', `next-check${suffix}.mjs`)).href);
  const fr = await import(pathToFileURL(await bundle('app/next-check-entry.jsx', `next-check${suffix}.fr.mjs`, { define: { __LEX_CHROME_LOCALE__: JSON.stringify('fr') } })).href);
  return { en, fr };
}

/**
 * A synthetic answer to a question that names a European text Lex does not hold, in the engine's shape (`core.py` since
 * release-a: the step `european_text` with its `not_held` list, the note `european_text_not_held`, and `eu_not_held`, the
 * sentence in the question's language), made from a real sample answer: the Luxembourg answer is kept whole.
 */
export function syntheticEuropeanAnswer(answer) {
  const steps = Array.isArray(answer.trace?.steps) ? answer.trace.steps : [];
  const step = { step: 'european_text', who: 'code', detail: { named_in_question: 'Solvabilité II', held: [], not_held: ['32009L0138'], read_by_the_assistant: false }, ms: null };
  return {
    ...answer,
    notes: [...(answer.notes ?? []).filter((note) => note !== 'european_text_not_held'), 'european_text_not_held'],
    eu_not_held: [{ celex: '32009L0138', named_as: 'Solvabilité II', transposed_by_the_text_shown: true, sentence: 'La directive Solvabilité II (2009/138/CE) n’est pas détenue par Lex ; voici la loi luxembourgeoise qui la transpose.' }],
    trace: { ...(answer.trace ?? {}), steps: [...steps.slice(0, 1), step, ...steps.slice(1)] },
  };
}

/**
 * Synthetic parts of a results answer, in the shape `make_results.py` writes (`switched_off`, `all.with_exception_block`,
 * `independent`), for the rendering that must not depend on what the engine's own file holds today.
 */
export function syntheticResults(results) {
  return {
    ...results,
    all: { ...results.all, with_exception_block: 34 },
    switched_off: ['kinds', 'gates', 'references', 'a_brick_of_tomorrow'].map((what, at) => ({ what, n: 284, asserted: 230 + at, asserted_with_expected: 223, asserted_without_expected: 7 + at, with_exception_block: 33 + at })),
    independent: {
      label: 'Questions du testeur indépendant.', n: 61, right: 45, wrong: 1, shown_not_asserted: 2, not_found: 13,
      live: { right: 14, shown_not_asserted: 31, wrong: 3, not_found: 13 },
      by_law: [{ law: 'contrat-assurance', n: 27, right: 24 }, { law: 'a-law-of-tomorrow', n: 1, right: 0 }],
      note: 'Note du test.',
    },
  };
}

/**
 * Synthetic fresh tests (`results.fresh`), for a page that must stand whatever the file holds: `partial`, keys missing or
 * of the wrong type (no sentence may then be said with a hole, nor a value that is none); `unknown`, a style, a text and a
 * degree of certainty the copy does not name yet (the first two shown by their key, the third left out); `empty`, nothing
 * known (no section at all).
 */
export function syntheticFresh(results, kind) {
  const fresh = results.fresh ?? {};
  if (kind === 'empty') return { ...results, fresh: {} };
  if (kind === 'unknown') {
    return {
      ...results,
      fresh: {
        ...fresh,
        by_style: [...(fresh.by_style ?? []), { style: 'a-style-of-tomorrow', n: 2, right: 1, wrong: 1 }],
        by_law: [...(fresh.by_law ?? []), { law: 'a-law-of-tomorrow', n: 3, right: 3, wrong: 0 }],
        by_confidence: [...(fresh.by_confidence ?? []), { confidence: 'a-certainty-of-tomorrow', asserted: 7, not_right: 0 }, { confidence: 'named', asserted: 4, not_right: 0 }],
      },
    };
  }
  return {
    ...results,
    fresh: {
      label: '  ', note: null, n: 130, n_answer: '120', right: 99, right_interval: [74.7], wrong: 4, wrong_interval: 'x', wrong_article: 3, wrong_version: null,
      shown_not_asserted: undefined, not_found: Number.NaN, asserted: null, no_answer_right: 10, n_no_answer: 0,
      exceptions: { n: 13, right: 11 }, dated: 'none', by_style: [{ style: 'lay', n: 18 }, null, 'x', { style: '' }, { n: 3 }],
      by_law: 'none', by_confidence: [{ confidence: 'firm', asserted: 50 }, { confidence: 'single' }], median_ms: 6184, usd_per_answer: '0.0156',
    },
  };
}

/** The part of a rendering between a mark and the end of the element it opens (`tag`), the first such; '' when absent. */
function elementWith(html, mark, tag) {
  const at = html.indexOf(mark);
  if (at < 0) return '';
  const start = html.lastIndexOf(`<${tag}`, at);
  const end = html.indexOf(`</${tag}>`, at);
  return start < 0 || end < 0 ? '' : html.slice(start, end + tag.length + 3);
}

/**
 * What the Results page must hold of the fresh test it was drawn from (`results.fresh`), read against the data in `locale`:
 * the section before the earlier questions and the bench; its heading; the label and the note marked French; one row for
 * each known count of the questions that have an answer, with its interval; each sentence whose counts are all known, and
 * none whose counts are not; the rows by certainty, by style and by text, each named in words when the copy names it.
 */
export function freshFaults(html, results, locale) {
  const fresh = results !== null && typeof results === 'object' && results.fresh !== null && typeof results.fresh === 'object' ? results.fresh : null;
  const isCount = (value) => typeof value === 'number' && Number.isFinite(value);
  const named = (value, key) => (Array.isArray(value) ? value.filter((row) => row !== null && typeof row === 'object' && typeof row[key] === 'string' && row[key].length > 0) : []);
  const section = elementWith(html, 'data-results-fresh=""', 'section');
  if (fresh === null) return section === '' ? [] : ['a fresh section drawn with no fresh test'];
  const copy = nextCopy(locale).results.fresh;
  const laws = nextCopy(locale).results.laws;
  const count = (value) => groupedCount(value, locale);
  const label = typeof fresh.label === 'string' ? fresh.label.trim().replace(/[.\s]+$/u, '') : '';
  const note = typeof fresh.note === 'string' ? fresh.note.trim() : '';
  const exceptions = fresh.exceptions !== null && typeof fresh.exceptions === 'object' ? fresh.exceptions : {};
  const dated = fresh.dated !== null && typeof fresh.dated === 'object' ? fresh.dated : {};
  const time = isCount(fresh.median_ms) && isCount(fresh.p90_ms) ? fillNext(copy.time, { median: decimal(fresh.median_ms / 1000, locale, 1), slow: decimal(fresh.p90_ms / 1000, locale, 1) }) : null;
  const cost = isCount(fresh.usd_per_answer) ? fillNext(copy.cost, { cost: decimal(fresh.usd_per_answer, locale) }) : null;
  const known = {
    asserted: isCount(fresh.asserted) && isCount(fresh.right),
    noAnswer: isCount(fresh.no_answer_right) && isCount(fresh.n_no_answer) && fresh.n_no_answer > 0,
    exceptions: [exceptions.right, exceptions.n, exceptions.right_and_other_article_shown].every(isCount),
    dated: [dated.right, dated.n, dated.wrong_version].every(isCount),
  };
  const certaintyKnown = (row) => Object.hasOwn(copy.confidence.rows, row.confidence) && (isCount(row.asserted) || isCount(row.not_right));
  const anything = label.length > 0 || note.length > 0 || ['right', 'wrong', 'shown_not_asserted', 'not_found'].some((field) => isCount(fresh[field]))
    || Object.values(known).some(Boolean) || time !== null || cost !== null || named(fresh.by_confidence, 'confidence').some(certaintyKnown)
    || named(fresh.by_style, 'style').length > 0 || named(fresh.by_law, 'law').length > 0;
  if (!anything) return section === '' ? [] : ['a fresh section drawn with nothing known to say'];
  if (section === '') return ['the fresh test is not drawn'];
  const faults = [];
  const end = html.indexOf(section) + section.length;
  for (const later of ['data-results-independent', 'data-results-intro', 'data-results-bench']) {
    if (html.includes(later) && html.indexOf(later) < end) faults.push(`${later} stands before the end of the fresh test`);
  }
  if (!section.includes(`<h2 id="results-fresh">${inMarkup(copy.heading)}</h2>`)) faults.push('the heading of the fresh test is missing');
  if (label.length > 0 && !section.includes(`lang="fr" data-fresh-label="">${inMarkup(label)}.</p>`)) faults.push('the label is not said, marked French');
  if (note.length > 0 && !section.includes(`<p lang="fr" data-fresh-note="">${inMarkup(note)}</p>`)) faults.push('the note is not said, marked French');
  // The questions that have an answer: a row for each known count, with its interval when the test gives one.
  const pair = (value) => (Array.isArray(value) && isCount(value[0]) && isCount(value[1]) ? value : null);
  for (const [key, field, range] of [['right', 'right', 'right_interval'], ['wrong', 'wrong', 'wrong_interval'], ['shown', 'shown_not_asserted', null], ['notFound', 'not_found', null]]) {
    const row = elementWith(section, `<tr data-fresh="${field}">`, 'tr');
    if (!isCount(fresh[field])) {
      if (row !== '') faults.push(`a row for ${field}, which the test does not count`);
      continue;
    }
    if (!row.includes(`<th scope="row">${inMarkup(copy.rows[key])}`) || !row.includes(`<td>${count(fresh[field])}</td>`)) faults.push(`the row ${field} does not say ${count(fresh[field])}`);
    const interval = range === null ? null : pair(fresh[range]);
    if (interval !== null && !row.includes(`<td>${inMarkup(fillNext(copy.interval, { low: decimal(interval[0], locale), high: decimal(interval[1], locale) }))}</td>`)) faults.push(`the interval of ${field} is not said`);
  }
  const answered = elementWith(section, 'data-fresh-answer=""', 'table');
  if (answered !== '' && isCount(fresh.n_answer) && !answered.includes(`<th scope="col">${inMarkup(countedForm(copy.outOf, fresh.n_answer, locale))}</th>`)) faults.push('the number of questions that have an answer is not said');
  const anyInterval = [fresh.right, fresh.wrong].some(isCount) && [['right', 'right_interval'], ['wrong', 'wrong_interval']].some(([field, range]) => isCount(fresh[field]) && pair(fresh[range]) !== null);
  if (anyInterval !== answered.includes(`<th scope="col">${inMarkup(copy.columns.interval)}</th>`)) faults.push(anyInterval ? 'the column of the intervals is missing' : 'a column of intervals with no interval to show');
  const split = [['article', 'wrong_article'], ['version', 'wrong_version'], ['lines', 'wrong_lines']].filter(([, field]) => isCount(fresh[field]));
  const splitLine = elementWith(section, 'data-fresh-split=""', 'span');
  if (isCount(fresh.wrong) && split.length > 0) {
    for (const [key, field] of split) if (!splitLine.includes(inMarkup(fillNext(copy.split[key], { count: count(fresh[field]) })))) faults.push(`the wrong answers by ${key} are not said`);
  } else if (splitLine !== '') faults.push('the wrong answers are split with no count to split them by');
  // The sentences: each said when all its counts are known, and only then.
  const sentence = (mark, known, text) => {
    const element = elementWith(section, mark, 'p');
    if (known && !element.includes(inMarkup(text()))) faults.push(`the line ${mark} is not said as it should be`);
    if (!known && element !== '') faults.push(`the line ${mark} is said with a count missing`);
  };
  sentence('data-fresh-asserted=""', known.asserted, () => fillNext(copy.asserted, { asserted: countedForm(copy.assertedCount, fresh.asserted, locale), right: countedForm(copy.rightCount, fresh.right, locale) }));
  sentence('data-fresh-line="no-answer"', known.noAnswer, () => fillNext(copy.noAnswer, { right: count(fresh.no_answer_right), n: count(fresh.n_no_answer) }));
  sentence('data-fresh-line="exceptions"', known.exceptions, () => fillNext(copy.exceptions, { right: count(exceptions.right), n: count(exceptions.n), cases: countedForm(copy.cases, exceptions.right_and_other_article_shown, locale) }));
  sentence('data-fresh-line="dated"', known.dated, () => fillNext(copy.dated, { right: count(dated.right), n: count(dated.n), wrongVersion: count(dated.wrong_version) }));
  sentence('data-fresh-time=""', time !== null || cost !== null, () => [time, cost].filter((line) => line !== null).join(' '));
  // By the degree of certainty shown: the rows the copy names, in its order; a degree it does not name is left out.
  const certainty = named(fresh.by_confidence, 'confidence');
  const order = Object.keys(copy.confidence.rows);
  const drawn = [...section.matchAll(/<tr data-confidence="([^"]*)">/gu)].map((match) => match[1]);
  if (drawn.join(',') !== [...drawn].sort((a, b) => order.indexOf(a) - order.indexOf(b)).join(',')) faults.push(`the degrees of certainty are not in the page's order: ${drawn.join(', ')}`);
  for (const row of certainty) {
    const line = elementWith(section, `<tr data-confidence="${row.confidence}">`, 'tr');
    if (!certaintyKnown(row)) {
      if (line !== '') faults.push(`a row for the certainty ${row.confidence}, which the copy does not name or the test does not count`);
      continue;
    }
    const cell = (value) => `<td>${isCount(value) ? count(value) : ''}</td>`;
    if (line !== `<tr data-confidence="${row.confidence}"><th scope="row">${inMarkup(copy.confidence.rows[row.confidence])}</th>${cell(row.asserted)}${cell(row.not_right)}</tr>`) faults.push(`the certainty ${row.confidence} is not said as the test counts it`);
  }
  // By style and by text, folded: each row named in words when the copy names it, by its key otherwise, with its counts.
  for (const [field, key, mark, names] of [['by_style', 'style', 'data-fresh-styles=""', copy.styles], ['by_law', 'law', 'data-fresh-laws=""', laws]]) {
    const tableHtml = elementWith(section, mark, 'table');
    const rows = named(fresh[field], key);
    if (rows.length > 0 && !elementWith(section, 'data-fresh-detail=""', 'details').includes(mark)) faults.push(`the rows ${field} are not in the folded part`);
    if ((tableHtml.match(/<tr data-/gu) ?? []).length !== rows.length) faults.push(`${(tableHtml.match(/<tr data-/gu) ?? []).length} rows ${field} for ${rows.length} in the test`);
    for (const row of rows) {
      const name = Object.hasOwn(names, row[key]) ? names[row[key]] : row[key];
      const cell = (value) => `<td>${isCount(value) ? count(value) : ''}</td>`;
      if (!tableHtml.includes(`<tr data-${key}="${inMarkup(row[key])}"><th scope="row">${inMarkup(name)}</th>${cell(row.n)}${cell(row.right)}${cell(row.wrong)}</tr>`)) faults.push(`the ${key} ${row[key]} is not said as the test counts it`);
    }
  }
  return faults;
}

/**
 * Synthetic variants of a real asserted answer, for what no sample holds yet: two searches or one in agreement, a wording
 * with the change that made it (its text held, or not), and a wording that is not the first held.
 */
export function syntheticConfidence(answer, confidence) {
  const found = confidence === 'supported' ? ['keywords', 'meaning'] : ['outline'];
  const note = confidence === 'supported' ? 'found_by_two_detectors' : 'found_by_one_detector';
  const blocks = (answer.blocks ?? []).map((block, at) => ({
    ...block,
    wording_since: '2013-03-01', wording_no: 2, wording_first_held: 0,
    last_change: at === 0 ? { kind: 'replacement', act_id: '/eli/etat/leg/loi/2012/12/21/n9', effective: '2013-03-01', title: 'Loi du 21 décembre 2012 portant\nmodification de la loi sur le contrat d’assurance.' }
      : at === 1 ? { kind: 'a_kind_of_tomorrow', act_id: '/eli/etat/leg/loi/2012/12/21/n9', effective: '2013-03-01', title: null } : null,
  }));
  return { ...answer, confidence, found_by: found, blocks, notes: [...(answer.notes ?? []).filter((one) => !/^found_by_|^article_named/u.test(one)), note] };
}

/** A synthetic "not found", with what was searched: a word that no article holds, and no passage to show. */
export function syntheticNotFound(answer) {
  return {
    ...answer, outcome: 'not_found', blocks: [], confidence: undefined, found_by: undefined, notes: [],
    not_found: { searched_rows: 85594, period: ['2014-01-01', '2014-12-31'], reason: 'no article applicable on that date among the closest texts' },
    searched: { ...(answer.searched ?? {}), kinds_of_text: ['general', 'repealed', 'a_kind_of_tomorrow'], passages_in_those_kinds: 72702, period: ['2014-01-01', '2014-12-31'], words: { durait: 0, essai: 460, periode: 5395 }, closest: undefined },
  };
}

/**
 * Synthetic answers in the shapes release-a added to the engine (21/11/2026): a block of a text that a held text repeals
 * (`repeal`) with the note for a text the question names (`repealed_named`), and a "not found" that says what Lex does not
 * hold (`coverage`, the counts of `app/coverage_gaps.json`).
 */
export function syntheticRepealed(answer) {
  const repeal = { from: '2016-01-01', by: '/eli/etat/leg/loi/2015/12/07/n1', article: 'Art. 323', partial: false, by_title: 'Loi du 7 décembre 2015 sur le secteur des assurances.' };
  return {
    ...answer,
    blocks: (answer.blocks ?? []).map((block, at) => (at === 0 ? { ...block, repeal, version_to: '2016-01-01' } : block)),
    notes: [...(answer.notes ?? []), 'named_text_repealed'],
    repealed_named: [{ act_id: '/eli/etat/leg/loi/1991/12/06/n2', act_title: 'Loi du 6 décembre 1991 sur le secteur des assurances.', ...repeal }],
  };
}

export function syntheticCoverage(answer) {
  return {
    ...syntheticNotFound(answer),
    coverage: {
      catalogue: { listed: 24644, held: 8360, not_held: 16284 }, eu_held: 77,
      not_held: ['solvency_ii', 'idd', 'priips', 'caa_regulations', 'social_security_code', 'road_code', 'a_text_of_tomorrow'],
      codes: [{ title: 'Code du travail', first: '2020-11-01' }, { title: 'Code civil', first: '2016-09-01' }],
    },
  };
}

/**
 * A synthetic answer whose blocks are of a European text, in the shape the engine will send once European texts are in the
 * data (`act_id` "celex:…", the CELEX as the work, the article's three digits as the anchor): no sample holds one yet.
 */
export function syntheticEuropeanBlocks(answer) {
  return {
    ...answer,
    blocks: (answer.blocks ?? []).map((block, at) => ({
      ...block,
      act_id: 'celex:32016R0679', work: '32016R0679', anchor: ['033', '034', '004'][at % 3], date: '2018-05-25',
      article: `Article ${['33', '34', '4'][at % 3]}`, act_title: 'Règlement (UE) 2016/679 du Parlement européen et du Conseil du 27 avril 2016 (règlement général sur la protection des données)',
      official: 'https://eur-lex.europa.eu/eli/reg/2016/679/oj', last_change: null,
    })),
  };
}

/** A synthetic answer that sets a text aside because it is made for one group only (the note `text_for_a_special_group`). */
export function syntheticSpecialGroup(answer) {
  return {
    ...answer, outcome: 'candidates', confidence: undefined, found_by: undefined, notes: ['text_for_a_special_group'],
    blocks: (answer.blocks ?? []).slice(0, 1).map((block) => ({ ...block, role: 'candidate', why: null })),
  };
}

/** The corpus counts with synthetic values for what the engine's database does not hold yet (each may be null in an answer). */
export function syntheticCorpus(corpus) {
  return {
    ...corpus,
    wordings: { total: 400000, articles_with_several: 12000, unverified: 30 },
    deleted_by_publisher: { articles_all_versions: 5000, articles_current: 900 },
    articles_left_out: [...(Array.isArray(corpus.articles_left_out) ? corpus.articles_left_out : []), ['deleted_by_publisher', 641], ['a_reason_of_tomorrow', 3]],
    units_by_kind_current: [['article', 90000], ['annex', 4000], ['conclusion', 9770], ['under_heading', 471], ['other', 15000], ['a_kind_of_tomorrow', 7]],
    keyword_index: { articles: 78000 },
    conservation: { files: 10870, passed: 10868, failed: 2, body_words: 61000000, stored_law_words: 60000000, deleted_words: 400000, not_stored_words: 600000, numbers_missing: 0, a_key_of_tomorrow: 'x' },
  };
}

/**
 * The pages' own code and the browser's storage, as the cookies page says it is tested: no source of a page sets a cookie
 * or uses any storage but the tab's session storage, and that only in the assistant's dock, under one key, read once and
 * written once. The current site's dock (`AssistantDock.jsx`) stays in place, wired nowhere: no other source may name it.
 */
export function storageFaults() {
  const faults = [];
  const folder = new URL('../app/', import.meta.url);
  const sources = readdirSync(folder).filter((name) => /\.(?:jsx|mjs)$/u.test(name)).map((name) => [name, readFileSync(new URL(name, folder), 'utf8')]);
  for (const [name, source] of sources) {
    if (/document\.cookie\s*=|localStorage|indexedDB/u.test(source)) faults.push(`app/${name} sets a cookie or uses a storage other than the session's`);
    const session = source.match(/(?<![A-Za-z])sessionStorage(?![A-Za-z])/gu)?.length ?? 0;
    if (name === 'NextDock.jsx') {
      if (session !== 2 || !/sessionStorage\.getItem\(MEMORY_KEY\)/u.test(source) || !/sessionStorage\.setItem\(MEMORY_KEY, /u.test(source)) faults.push('app/NextDock.jsx does not read and write its one key, each once');
    } else if (name === 'AssistantDock.jsx') {
      if (sources.some(([other, text]) => other !== name && /from '\.\/AssistantDock\.jsx'/u.test(text))) faults.push('app/AssistantDock.jsx is wired into a page again');
    } else if (session !== 0) faults.push(`app/${name} uses the session storage`);
  }
  return faults;
}

/** The sources of the dock and of what it asks: none may write a question into a request's address or a page's query. */
export function questionAddressFaults() {
  const faults = [];
  for (const file of ['../app/NextDock.jsx', './next-view.mjs']) {
    const source = readFileSync(new URL(file, import.meta.url), 'utf8').replace(/^\s*\/\/.*$/gmu, '').replace(/\/\*[\s\S]*?\*\//gu, '');
    for (const pattern of [/\?q=/u, /[?&]question=/u, /ask\?/u, /method:\s*'GET'/u, /location\.search/u]) {
      if (pattern.test(source)) faults.push(`${file} holds ${pattern}`);
    }
  }
  return faults;
}

/** The faults common to any rendering: an inline style, a place left unfilled, a value that is not one. */
export function markupFaults(html) {
  const faults = [];
  if (/\sstyle=/u.test(html)) faults.push('an inline style (the policy forbids them)');
  if (/\{[a-zA-Z0-9]+\}/u.test(html.replace(/<pre[\s\S]*?<\/pre>/gu, ''))) faults.push('a place left unfilled in a sentence');
  for (const word of ['undefined', 'NaN', '[object Object]', 'null']) if (new RegExp(`>[^<]*\\b${word.replace(/[[\]]/gu, '\\$&')}\\b[^<]*<`, 'u').test(html)) faults.push(`the value ${word} shown`);
  return faults;
}

/** What a dock rendering must hold of the answer it was drawn from; the faults found. */
export function dockFaults(html, answer, locale) {
  const faults = markupFaults(html);
  const copy = nextCopy(locale).dock;
  const blocks = Array.isArray(answer.blocks) ? answer.blocks : [];
  const steps = Array.isArray(answer.trace?.steps) ? answer.trace.steps : [];
  const count = (pattern) => (html.match(pattern) ?? []).length;
  if (!html.includes(`<p class="lx-dock-bubble" data-dock-question="">${inMarkup(answer.question)}</p>`)) faults.push('the question bubble is missing');
  if (!html.includes(inMarkup(copy.understood))) faults.push('"Lex understood" is missing');
  const outcome = outcomeOf(answer, locale);
  if (outcome === null || !html.includes(inMarkup(outcome.lead))) faults.push(`the outcome's sentence is missing (${answer.outcome})`);
  if (count(/data-dock-card="block"/gu) !== blocks.length) faults.push(`${count(/data-dock-card="block"/gu)} block cards for ${blocks.length} blocks`);
  const notFound = answer.outcome === 'not_found';
  for (const block of blocks) {
    const label = notFound ? copy.roles.nearest : copy.roles[block.role];
    if (!html.includes(`<span class="lx-dock-verdict">${inMarkup(label)}</span>`)) faults.push(`the role ${block.role} is not named (${label})`);
    for (const line of block.lines ?? []) if (!html.includes(inMarkup(line.text))) faults.push(`a line of ${block.article} is missing: ${line.text.slice(0, 40)}`);
    // The link that opens the article in the page, for a block that names its text and its article (an annex may name
    // neither): a Luxembourg text by `/lu-legilux/{work}`, a European one by its CELEX alone.
    const identifier = identifierOf(block.work, block.act_id ?? null);
    if (identifier !== null && typeof block.anchor === 'string' && block.anchor.length > 0) {
      const href = `/${locale}/text.html?text=${encodeURIComponent(identifier)}${/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(block.date ?? '') ? `&amp;date=${block.date}` : ''}&amp;article=${encodeURIComponent(block.anchor)}`;
      if (!html.includes(`href="${href}"`)) faults.push(`no link opens ${block.article} in the page (${href})`);
      if (identifier.startsWith('/lu-legilux/') === (typeof block.act_id === 'string' && block.act_id.startsWith('celex:'))) faults.push(`${block.article}: a European text named as a Luxembourg one, or the reverse (${identifier})`);
    }
    if (typeof block.official === 'string' && /^https:\/\//u.test(block.official) && !html.includes(`href="${inMarkup(block.official)}"`)) faults.push(`no link to the official text of ${block.article}`);
  }
  // Nothing found: the sentence, then what was searched, then the passages under their heading; never a passage before.
  if (notFound && blocks.length > 0) {
    const evidence = html.indexOf('data-dock-card="searched"');
    const heading = html.indexOf('data-dock-passages=""');
    const first = html.indexOf('data-dock-card="block"');
    const said = html.indexOf('data-dock-card="outcome"');
    if (heading < 0) faults.push('the heading over the closest passages is missing');
    if (!(said >= 0 && said < first && (evidence < 0 || evidence < first) && heading < first)) faults.push('a passage stands before the sentence, the evidence or its heading');
  }
  if (steps.length > 0) {
    if (!html.includes(inMarkup(copy.built))) faults.push('"How this answer was built" is missing');
    if (count(/<li class="lx-dock-row lx-dock-step" data-stage="/gu) !== steps.length) faults.push(`${count(/<li class="lx-dock-row lx-dock-step" data-stage="/gu)} step rows for ${steps.length} steps`);
    // Each step on two lines: its name, its tag and its time first; nothing known by its engine's key alone.
    for (const step of steps) if (Object.hasOwn(copy.steps, step.step) && !html.includes(`<span class="lx-dock-row-name">${inMarkup(copy.steps[step.step])}</span>`)) faults.push(`the step ${step.step} is not named`);
    if (!html.includes(`<a class="lx-dock-link" href="/${locale}/#q=${inMarkup(encodeURIComponent(answer.question))}&amp;on=`)) faults.push('the replay link (the question after "#") is missing');
    if (!html.includes('data-dock-cost=""')) faults.push('the foot line (model calls, time, cost) is missing');
  }
  // The table of contents read: a short list, one text a line, each title cut short.
  const walks = (steps.find((step) => step.step === 'outline_walk')?.detail?.walks ?? []).filter((walk) => typeof walk?.title === 'string' && walk.title.trim().length > 0);
  if (walks.length > 0) {
    const listed = /<ul class="lx-dock-step-list">([\s\S]*?)<\/ul>/u.exec(html)?.[1] ?? '';
    if ((listed.match(/<li>/gu) ?? []).length !== walks.length) faults.push(`${(listed.match(/<li>/gu) ?? []).length} texts listed in the table-of-contents step for ${walks.length} read`);
    for (const title of listed.matchAll(/<li><span(?: lang="fr")?>([^<]*)<\/span>/gu)) if (title[1].length > 64) faults.push(`a title in the table-of-contents step is not cut short: ${title[1]}`);
  }
  if (html.includes('\\n')) faults.push('a written "\\n" is shown');
  for (const key of ['deleted_by_publisher', 'no_answer_in_the_articles_read', 'text_for_a_special_group', 'code+embedding']) if (html.includes(`>${key}<`) || html.includes(` ${key} `)) faults.push(`the engine's key ${key} is shown`);
  if (/href="[^"#]*[?&]q=/u.test(html)) faults.push('a link holds a question in its query');
  const asserted = ASSERTED_OUTCOMES.includes(answer.outcome);
  if (asserted && typeof answer.confidence === 'string' && !html.includes('data-dock-confidence="')) faults.push('how sure the answer is (the searches that found the article) is not said');
  if (!asserted && html.includes('data-dock-confidence="')) faults.push('a confidence is said of an answer that asserts nothing');
  const dated = blocks.filter((block) => typeof block.wording_since === 'string').length;
  if (count(/data-dock-wording=""/gu) !== dated) faults.push(`${count(/data-dock-wording=""/gu)} wording lines for ${dated} blocks that carry one`);
  const evidence = ['candidates', 'partial', 'not_found'].includes(answer.outcome) && answer.searched !== null && typeof answer.searched === 'object';
  if (evidence !== html.includes('data-dock-card="searched"')) faults.push(evidence ? '"What Lex searched" is missing' : '"What Lex searched" is shown beside an asserted answer');
  if (evidence) for (const word of Object.keys(answer.searched.words ?? {})) if (!html.includes(`>${inMarkup(word)}</th>`)) faults.push(`the word searched ${word} is missing`);
  if (answer.build && !html.includes('data-dock-build=""')) faults.push('the versions of the code and the data are not said');
  if (locale === 'fr') for (const sentence of englishSentences()) if (html.includes(inMarkup(sentence))) faults.push(`an English sentence on the French page: ${sentence}`);
  return faults;
}

/** The answers in the engine's contract (`TRACE-CONTRACT.md`), one a state, and the context of an article to widen to. */
export const CONTRACT_FIXTURES = new URL('../test/next-fixtures/contract/', import.meta.url);

/** The contract's fixtures, by file name (the context, which is no answer, apart). */
export async function readContractFixtures() {
  const names = (await readdir(CONTRACT_FIXTURES)).filter((name) => name.endsWith('.json') && !name.startsWith('context-')).sort();
  return Promise.all(names.map(async (name) => ({ name: name.replace(/\.json$/u, ''), answer: JSON.parse(await readFile(new URL(name, CONTRACT_FIXTURES), 'utf8')) })));
}

/** The engine's keys that must never stand as words in an answer drawn (the trace may show an unknown one, in its fold). */
const CONTRACT_KEYS = Object.freeze(['answer_with_exception', 'not_held', 'not_found', 'out_of_scope', 'small_node', 'list_node', 'outline node', 'question shape', 'exception index', 'pending_references flag', 'meaning_only']);

/**
 * What a dock rendering must hold of an answer in the contract: the question; the state (its badge, its lead, never a
 * percentage); the missing piece of a partial answer; each text not held; a clarification's question and each option as a
 * button; the list's statement; one card a block with its role, its lines, its version's sentence and the link that opens it
 * (when the block names its article); for a "not found", the evidence before the passages; the trace folded (a details with
 * no open attribute) with each level's name, each model call, the foot line counting the calls, and the replay link.
 */
export function contractFaults(html, answer, locale) {
  const faults = markupFaults(html);
  const table = nextCopy(locale);
  const copy = table.contract;
  const blocks = Array.isArray(answer.blocks) ? answer.blocks : [];
  const count = (pattern) => (html.match(pattern) ?? []).length;
  if (typeof answer.question === 'string' && answer.question.length > 0 && !html.includes(`<p class="lx-dock-bubble" data-dock-question="">${inMarkup(answer.question)}</p>`)) faults.push('the question bubble is missing');
  const state = copy.states[answer.state];
  if (state === undefined) faults.push(`a state the copy does not know: ${answer.state}`);
  else {
    if (!html.includes(`data-state-badge="${answer.state}">${inMarkup(state.badge)}</span>`)) faults.push(`the state's badge is missing (${answer.state})`);
    if (!html.includes(inMarkup(state.lead))) faults.push(`the state's lead is missing (${answer.state})`);
  }
  // The law’s own lines may hold a percentage (a confidence level of 99.5 % in the insurance sector law): only Lex’s words are read.
  const own = html.replace(/<div class="lx-dock-extract[^"]*"[\s\S]*?<\/div>/gu, '');
  if (/[0-9]\s?%|[0-9] %/u.test(own)) faults.push('a percentage is shown: confidence is said as a state');
  if (answer.state === 'partial') {
    const said = typeof answer.missing === 'string' && answer.missing.trim().length > 0 ? fillNext(copy.missing, { missing: answer.missing.trim() }) : copy.missingUnsaid;
    if (!html.includes(inMarkup(said))) faults.push('the partial answer does not name what is missing');
  }
  for (const entry of Array.isArray(answer.not_held) ? answer.not_held : []) {
    if (typeof entry?.name === 'string' && !html.includes(inMarkup(entry.name))) faults.push(`the text not held is not named: ${entry.name}`);
  }
  if (answer.clarify) {
    if (typeof answer.clarify.question === 'string' && !html.includes(inMarkup(answer.clarify.question))) faults.push('the clarification\'s question is missing');
    for (const option of answer.clarify.options ?? []) if (!html.includes(`data-clarify-option="">${inMarkup(option)}</button>`)) faults.push(`the option ${option} is not a button`);
  }
  if (answer.list && Number.isFinite(answer.list.items_shown)) {
    const complete = answer.list.complete === true && (answer.list.items_total ?? answer.list.items_shown) === answer.list.items_shown;
    if (!html.includes(`data-dock-list="${complete ? 'complete' : 'incomplete'}"`)) faults.push('the list\'s completeness is not said');
  }
  if (count(/data-dock-card="block"/gu) !== blocks.length) faults.push(`${count(/data-dock-card="block"/gu)} block cards for ${blocks.length} blocks`);
  for (const block of blocks) {
    for (const line of block.text ?? []) if (!html.includes(inMarkup(line))) faults.push(`a line of ${block.label} is missing: ${String(line).slice(0, 40)}`);
    const version = block.version ?? {};
    if (/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(version.from ?? '') && !html.includes(numericDate(version.from))) faults.push(`the version of ${block.label} is not dated`);
    if (typeof block.anchor === 'string' && !html.includes(`article=${encodeURIComponent(block.anchor)}"`)) faults.push(`no link opens ${block.label} in the page`);
  }
  if (answer.state === 'not_found' && blocks.length > 0) {
    const evidence = html.indexOf('data-dock-evidence=""');
    const heading = html.indexOf('data-dock-passages=""');
    const first = html.indexOf('data-dock-card="block"');
    if (!(evidence >= 0 && evidence < heading && heading < first)) faults.push('a passage stands before the evidence or its heading');
  }
  if (answer.evidence && !['answer', 'answer_with_exception', 'partial'].includes(answer.state)) {
    for (const term of answer.evidence.terms ?? []) if (!html.includes(`>${inMarkup(term.term)}</th>`)) faults.push(`the word searched ${term.term} is missing`);
  }
  const trace = answer.trace ?? {};
  if (Array.isArray(trace.levels) && trace.levels.length > 0) {
    const details = /<details class="lx-dock-card lx-dock-built lx-dock-trace"([^>]*)>/u.exec(html);
    if (details === null) faults.push('the trace is not folded in a details');
    else if (/\sopen/u.test(details[1])) faults.push('the trace is drawn open');
    for (const level of trace.levels) if (Object.hasOwn(copy.trace.levels, level.level) && !html.includes(inMarkup(copy.trace.levels[level.level]))) faults.push(`the level ${level.level} is not named`);
    for (const call of trace.calls ?? []) if (!html.includes(`<code>${inMarkup(call.model)}</code>`)) faults.push(`the model call ${call.step} is not listed`);
    if (Array.isArray(trace.calls) && Number.isFinite(trace.ms) && Number.isFinite(trace.cost_usd) && !html.includes(inMarkup(countedForm(table.dock.calls, trace.calls.length, locale)))) faults.push('the foot line does not count the model calls');
  }
  if (typeof answer.meta?.replay === 'string' && !html.includes(`href="/${locale}/#r=${inMarkup(encodeURIComponent(answer.meta.replay))}"`)) faults.push('the replay link (the token after "#r=") is missing');
  for (const key of CONTRACT_KEYS) if (html.includes(`>${key}<`) || new RegExp(`[>\\s]${key.replace(/ /gu, '\\s')}[.<]`, 'u').test(html)) faults.push(`the engine's key ${key} is shown`);
  if (locale === 'fr') for (const sentence of englishSentences()) if (html.includes(inMarkup(sentence))) faults.push(`an English sentence on the French page: ${sentence}`);
  return faults;
}

/** The results of an evaluation in the schema `lex-results/2` (`web/RESULTS-SCHEMA.md`), synthetic. */
export const RESULTS2_FIXTURE = new URL('../test/next-fixtures/results-v2.json', import.meta.url);

/**
 * What the Results page must hold of an evaluation in `lex-results/2`: the wrong answers asserted before every other count,
 * each with its question; each total with its count; each slice with its count and its interval; the misses by cause; the
 * bricks switched off; the naive pipeline with where it did fine; whether the questions were seen before.
 */
export function results2Faults(html, results, locale) {
  const faults = markupFaults(html);
  const copy = nextCopy(locale).results2;
  const count = (value) => groupedCount(value, locale);
  const at = (mark) => html.indexOf(mark);
  if (at('data-results2-section="wrong"') < 0) faults.push('the wrong answers asserted are not drawn');
  for (const later of ['totals', 'slices', 'misses', 'ablation', 'baseline']) {
    if (at(`data-results2-section="${later}"`) >= 0 && at(`data-results2-section="${later}"`) < at('data-results2-section="wrong"')) faults.push(`${later} stands before the wrong answers`);
  }
  for (const row of results.wrong ?? []) if (!html.includes(`>${inMarkup(row.question)}</th>`)) faults.push(`the wrong answer ${row.id} is not listed`);
  for (const [key, value] of Object.entries(results.totals ?? {})) {
    if (key === 'intervals' || !Number.isFinite(value)) continue;
    if (!html.includes(`<tr data-row="${key}"><th scope="row">${inMarkup(copy.states[key])}</th><td>${count(value)}</td>`)) faults.push(`the total ${key} is not said as ${value}`);
  }
  for (const row of results.slices ?? []) {
    const said = fillNext(copy.interval, { low: decimal(row.interval[0], locale), high: decimal(row.interval[1], locale) });
    const name = copy.slices[row.slice] ?? row.slice;
    if (!html.includes(`<tr data-row="${inMarkup(row.slice)}"><th scope="row">${inMarkup(name)}</th><td>${count(row.n)}</td><td>${count(row.right)}</td><td>${count(row.wrong)}</td><td>${count(row.not_found)}</td><td>${inMarkup(said)}</td></tr>`)) faults.push(`the slice ${row.slice} is not said as the file counts it`);
  }
  if (results.misses) for (const key of Object.keys(copy.missesLabels)) if (Number.isFinite(results.misses[key]) && !html.includes(`<tr data-row="${key}"><th scope="row">${inMarkup(copy.missesLabels[key])}</th><td>${count(results.misses[key])}</td></tr>`)) faults.push(`the misses by ${key} are not said`);
  for (const row of results.ablation?.rows ?? []) if (!html.includes(`<tr data-row="${row.what}"><th scope="row">${inMarkup(copy.ablation[row.what] ?? row.what)}</th><td>${count(row.right)}</td>`)) faults.push(`the run ${row.what} is not said`);
  if (results.baseline && Number.isFinite(results.baseline.naive_right_lex_not) && !html.includes('data-results2-baseline-wins=""')) faults.push('where the naive pipeline did fine is not said');
  if (results.set && !html.includes(`data-results2-used="${results.set.used_before === true ? 'before' : 'once'}"`)) faults.push('whether the questions were seen before is not said');
  if (results.scoring?.lenient?.length > 0 && !html.includes('data-results2-lenient=""')) faults.push('the lenient rule is not said next to the numbers');
  if (locale === 'fr') for (const sentence of englishSentences()) if (html.includes(inMarkup(sentence))) faults.push(`an English sentence on the French page: ${sentence}`);
  return faults;
}

/**
 * Renders everything, in both languages: `{ renderings: [{ name, locale, html, faults }] }`. With `write`, each rendering is
 * written under `.react-build/next-check/`.
 */
export async function renderAll({ write = false, tag = '' } = {}) {
  const renderers = await loadRenderers(tag);
  const samples = await readSamples();
  const corpus = JSON.parse(await readFile(CORPUS_FIXTURE, 'utf8'));
  const results = JSON.parse(await readFile(RESULTS_FIXTURE, 'utf8'));
  // The engine's file as it stands today, when it is there: drawn and checked too, beside the fixture.
  let current = null;
  try {
    current = JSON.parse(await readFile(RESULTS_EXAMPLE, 'utf8'));
  } catch {
    current = null;
  }
  const article = JSON.parse(await readFile(ARTICLE_FIXTURE, 'utf8'));
  const links = JSON.parse(await readFile(LINKS_FIXTURE, 'utf8'));
  const contracts = await readContractFixtures();
  // Answers the funnel really gave (experiments/funnel/ask.py, 21/11/2026), kept as fixtures: the pages are read against the engine's own output.
  const realFolder = new URL('../test/next-fixtures/funnel-real/', import.meta.url);
  const reals = await Promise.all((await readdir(realFolder)).filter((name) => name.endsWith('.json')).sort()
    .map(async (name) => ({ name: `real-${name.replace(/\.json$/u, '')}`, answer: JSON.parse(await readFile(new URL(name, realFolder), 'utf8')) })));
  const topkFolder = new URL('../test/next-fixtures/topk-contract/', import.meta.url);
  const topks = await Promise.all((await readdir(topkFolder)).filter((name) => name.endsWith('.json')).sort()
    .map(async (name) => ({ name: name.replace(/\.json$/u, ''), answer: JSON.parse(await readFile(new URL(name, topkFolder), 'utf8')) })));
  const results2 = JSON.parse(await readFile(RESULTS2_FIXTURE, 'utf8'));
  const config = JSON.parse(await readFile(new URL('../test/next-fixtures/config.json', import.meta.url), 'utf8'));
  const coverage = JSON.parse(await readFile(new URL('../test/next-fixtures/coverage.json', import.meta.url), 'utf8'));
  const outlineInsurance = JSON.parse(await readFile(new URL('../test/next-fixtures/outline-insurance.json', import.meta.url), 'utf8'));
  const outlineLabour = JSON.parse(await readFile(new URL('../test/next-fixtures/outline-labour-2014.json', import.meta.url), 'utf8'));
  const context = JSON.parse(await readFile(new URL('context-art16.json', CONTRACT_FIXTURES), 'utf8'));
  const renderings = [];
  for (const locale of LOCALES) {
    const render = renderers[locale];
    for (const { name, answer } of samples) {
      const html = render.renderDockAnswer(answer, { locale, today: '2026-11-20' });
      renderings.push({ name: `dock-${name}`, locale, html, faults: dockFaults(html, answer, locale) });
    }
    const base = samples.find((sample) => sample.name === 'answer_with_exception')?.answer ?? samples[0].answer;
    const synthetics = [
      ['supported', syntheticConfidence(base, 'supported')], ['single', syntheticConfidence(base, 'single')], ['not-found', syntheticNotFound(base)],
      ['european-blocks', syntheticEuropeanBlocks(base)], ['special-group', syntheticSpecialGroup(base)],
      ['eu-not-held', syntheticEuropeanAnswer(base)], ['repealed', syntheticRepealed(base)], ['coverage', syntheticCoverage(base)],
    ];
    for (const [name, synthetic] of synthetics) {
      const html = render.renderDockAnswer(synthetic, { locale, today: '2026-11-20' });
      renderings.push({ name: `dock-synthetic-${name}`, locale, html, faults: dockFaults(html, synthetic, locale) });
    }
    // The earlier pipeline's answers given the contract's fields (`contract_from_topk`): drawn by the earlier view, read in
    // their own shape, with nothing lost (the lines, the text not held for the date asked, the steps).
    for (const { name, answer } of topks) {
      const html = render.renderDockAnswer(answer, { locale, today: '2026-11-20' });
      const faults = dockFaults(html, legacyAnswer(answer), locale);
      if (html.includes('data-dock-trace=""')) faults.push('drawn by the contract\'s view, which would lose its steps');
      renderings.push({ name: `topk-${name}`, locale, html, faults });
    }
    // The answers in the contract, one a state, read against what they carry; a block widened to the alinéa, the article
    // and the section from the context the server answers.
    for (const { name, answer } of [...contracts, ...reals]) {
      const html = render.renderDockAnswer(answer, { locale, today: '2026-11-22' });
      renderings.push({ name: `contract-${name}`, locale, html, faults: contractFaults(html, answer, locale) });
    }
    const listed = contracts.find((one) => one.name === 'list_incomplete')?.answer;
    if (listed !== undefined) {
      for (const level of ['alinea', 'article', 'section']) {
        const html = render.renderContractBlock(listed.blocks[0], { locale, level, context });
        const faults = markupFaults(html);
        if (!html.includes(`data-dock-wide="${level}"`)) faults.push(`the quotation is not widened to the ${level}`);
        if (!html.includes('<mark class="lx-dock-mark">')) faults.push('the lines quoted are not marked in the wider text');
        if (!html.includes(`data-widen="${level}" aria-pressed="true"`) && !html.includes(`aria-pressed="true" data-widen="${level}"`)) faults.push(`the choice ${level} is not pressed`);
        renderings.push({ name: `contract-widen-${level}`, locale, html, faults });
      }
    }
    // How it works (the funnel and the architecture), the design choices, and what Lex holds and does not.
    {
      const copy = nextCopy(locale);
      const how = render.renderHowFunnel(corpus, config, { locale });
      const faults = markupFaults(how);
      if ((how.match(/class="lx-how-stage"/gu) ?? []).length !== copy.how2.stages.length) faults.push('a level of the funnel is not drawn');
      for (const row of config.models) if (!how.includes(`<code>${inMarkup(row.deployment)}</code>`)) faults.push(`the model ${row.deployment} is not listed`);
      if (!how.includes('data-how-residency=""')) faults.push('where the calls run is not said');
      if (!how.includes(inMarkup(copy.how2.keptNothing))) faults.push('what the server keeps is not said');
      if (!how.includes(inMarkup(copy.how2.dataText))) faults.push('that the text typed is read as data is not said');
      if (/<details[^>]*data-how-counts=""[^>]*\sopen/u.test(how)) faults.push('the corpus counts are not behind a fold');
      for (const banned of ['sans base de données vectorielle', 'ne lit pas encore les textes européens', 'qui ne se voient pas', 'indépendants']) if (how.includes(banned)) faults.push(`the page says what the engine does not do: ${banned}`);
      if (locale === 'fr') for (const sentence of englishSentences()) if (how.includes(inMarkup(sentence))) faults.push(`an English sentence on the French page: ${sentence}`);
      renderings.push({ name: 'how-funnel', locale, html: how, faults });
      const failed = render.renderHowFunnel(null, null, { locale, state: 'failed', configState: 'failed' });
      renderings.push({ name: 'how-funnel-failed', locale, html: failed, faults: [...markupFaults(failed), ...(failed.includes(inMarkup(copy.how2.configFailed)) ? [] : ['a failed configuration is not said'])] });
      // The earlier pipeline (topk) answering: How it works is its page, never the funnel's.
      const topk = render.renderHowFunnel(corpus, { ...config, pipeline: 'topk' }, { locale });
      const tFaults = markupFaults(topk);
      if (topk.includes('data-how-version="funnel"') || copy.how2.stages.some((stage) => topk.includes(inMarkup(stage.text)))) tFaults.push('the funnel is described while the earlier pipeline answers');
      if (!topk.includes(inMarkup(copy.how.heading))) tFaults.push('the earlier pipeline\'s page is not drawn');
      renderings.push({ name: 'how-topk', locale, html: topk, faults: tFaults });
      const loading = render.renderHowFunnel(null, null, { locale, state: 'loading', configState: 'loading' });
      renderings.push({ name: 'how-loading-config', locale, html: loading, faults: [...markupFaults(loading), ...(copy.how2.stages.some((stage) => loading.includes(inMarkup(stage.text))) ? ['a design described before the configuration is read'] : [])] });
      for (const design of ['funnel', 'topk']) {
        const decisions = render.renderDecisions({ locale, pipeline: design });
        const dFaults = markupFaults(decisions);
        const items = copy.decisions2.items.filter((item) => item.only === undefined || item.only === design);
        if ((decisions.match(/class="lx-dec-item"/gu) ?? []).length !== items.length) dFaults.push('a design choice is not drawn, or one of another design is');
        for (const item of items) if (!decisions.includes(inMarkup(item.cost))) dFaults.push(`the cost of ${item.key} is not said`);
        for (const item of copy.decisions2.items.filter((one) => one.only !== undefined && one.only !== design)) if (decisions.includes(inMarkup(item.title))) dFaults.push(`the choice ${item.key} of another design is said`);
        if (locale === 'fr') for (const sentence of englishSentences()) if (decisions.includes(inMarkup(sentence))) dFaults.push(`an English sentence on the French page: ${sentence}`);
        renderings.push({ name: `decisions-${design}`, locale, html: decisions, faults: dFaults });
      }
      const waiting = render.renderDecisions({ locale, pipeline: null, state: 'loading' });
      renderings.push({ name: 'decisions-loading', locale, html: waiting, faults: [...markupFaults(waiting), ...(waiting.includes('class="lx-dec-item"') ? ['a design choice said before the configuration is read'] : [])] });
      const cover = render.renderCoverage(coverage, { locale });
      const cFaults = markupFaults(cover);
      for (const row of coverage.by_type) if (!cover.includes(`data-type="${row.type}"`)) cFaults.push(`the type ${row.type} is not drawn`);
      for (const row of coverage.not_held_named) { const name = typeof row === 'string' ? row : row.name; if (!cover.includes(inMarkup(name))) cFaults.push(`the text not held ${name} is not named`); }
      if (!cover.includes(inMarkup(fillNext(copy.coverage.inForce, { held: groupedCount(7311, locale), listed: groupedCount(17830, locale), share: '41' })))) cFaults.push('the share in force is not said with its two counts');
      for (const row of coverage.date_floor) if (!cover.includes(numericDate(row.first_held))) cFaults.push(`the date floor of ${row.title} is not said`);
      if (!cover.includes('data-coverage="source"')) cFaults.push('the source lines are not said');
      if (locale === 'fr') for (const sentence of englishSentences()) if (cover.includes(inMarkup(sentence))) cFaults.push(`an English sentence on the French page: ${sentence}`);
      renderings.push({ name: 'coverage', locale, html: cover, faults: cFaults });
      const none = render.renderCoverage({ none: true }, { locale });
      renderings.push({ name: 'coverage-none', locale, html: none, faults: none === '' ? [] : ['a coverage section drawn with no file'] });
    }
    // The text's table of contents: one link a heading to its first article, the version's dates, the earliest version held.
    for (const [name, outline] of [['outline-insurance', outlineInsurance], ['outline-labour-2014', outlineLabour]]) {
      const html = render.renderOutline(outline, { identifier: name === 'outline-insurance' ? '/lu-legilux/loi-1997-07-27-n10' : '/lu-legilux/code-travail' });
      const faults = markupFaults(html);
      const copy = nextCopy(locale).outline;
      const linked = outline.outline.filter((row) => typeof row.first_anchor === 'string' && row.label.trim().length > 0);
      if ((html.match(/<li data-depth=/gu) ?? []).length !== outline.outline.filter((row) => row.label.trim().length > 0).length) faults.push('a heading of the outline is not drawn');
      for (const row of linked) if (!html.includes(`article=${encodeURIComponent(row.first_anchor)}`)) faults.push(`the heading ${row.node_id} does not open its first article`);
      if (!html.includes(inMarkup(fillNext(copy.floor, { date: numericDate(outline.held.first), versions: countedForm(copy.versions, outline.held.versions, locale) })))) faults.push('the earliest version held is not said');
      if (outline.before_first_held !== html.includes('data-outline-before=""')) faults.push('a date before the earliest version held is not said, or said when it is not');
      if (!html.includes('data-outline-version=""')) faults.push('the version read is not dated');
      if (locale === 'fr') for (const sentence of englishSentences()) if (html.includes(inMarkup(sentence))) faults.push(`an English sentence on the French page: ${sentence}`);
      renderings.push({ name, locale, html, faults });
    }
    // The Results page of an evaluation in the schema of RESULTS-SCHEMA.md, read against its file.
    {
      const html = render.renderResults(results2, { locale });
      renderings.push({ name: 'results-v2', locale, html, faults: results2Faults(html, results2, locale) });
      const bare = { schema: results2.schema, run: results2.run, totals: results2.totals };
      const thin = render.renderResults(bare, { locale });
      renderings.push({ name: 'results-v2-thin', locale, html: thin, faults: [...markupFaults(thin), ...(thin.includes('data-results2-section="slices"') ? ['a slice section drawn with no slice'] : [])] });
    }
    for (const state of ['first', 'pending', 'failure', 'rate', 'folded', 'kept']) {
      const html = render.renderDockState(state, { locale });
      renderings.push({ name: `dock-state-${state}`, locale, html, faults: markupFaults(html) });
    }
    // Each Results rendering with the answer it was drawn from, read against it by `freshFaults`.
    const resultsCases = [
      ['results', results],
      ...(current === null ? [] : [['results-current', current]]),
      ['results-synthetic-independent', syntheticResults(results)],
      ['results-no-independent', { ...results, independent: undefined, switched_off: undefined }],
      ['results-no-fresh', { ...results, fresh: undefined }],
      ['results-fresh-only', { fresh: results.fresh }],
      ['results-fresh-partial', syntheticFresh(results, 'partial')],
      ['results-fresh-unknown', syntheticFresh(results, 'unknown')],
      ['results-fresh-empty', syntheticFresh(results, 'empty')],
    ];
    const drawnResults = new Map(resultsCases.map(([name, data]) => [name, data]));
    const pieces = [
      ['how', render.renderHow(corpus, { locale })],
      ['how-loading', render.renderHow(null, { locale, state: 'loading' })],
      ...resultsCases.map(([name, data]) => [name, render.renderResults(data, { locale })]),
      ['results-loading', render.renderResults(null, { locale, state: 'loading' })],
      ['results-none', render.renderResults({ none: true }, { locale })],
      ['corpus', render.renderCorpus(corpus, { locale })],
      ['corpus-failed', render.renderCorpus(null, { locale, state: 'failed' })],
      ['how-synthetic-counts', render.renderHow(syntheticCorpus(corpus), { locale })],
      ['corpus-synthetic-counts', render.renderCorpus(syntheticCorpus(corpus), { locale })],
      ['references', render.renderReferences(article, '/lu-legilux/loi-1997-07-27-n10')],
      ['links', render.renderLinks(links)],
      ['links-none', render.renderLinks({ links_out: [], links_in: [] })],
    ];
    for (const [name, html] of pieces) {
      const faults = markupFaults(html);
      // No key of the engine is shown in place of its words (an unknown one is left out, or counted with the others).
      for (const key of ['deleted_by_publisher', 'a_reason_of_tomorrow', 'a_kind_of_tomorrow', 'conclusion', 'under_heading']) if (html.includes(`>${key}<`) || new RegExp(`[>·] ?${key}\\b`, 'u').test(html) || html.includes(`: ${key}<`)) faults.push(`the engine's key ${key} is shown`);
      if (locale === 'fr') for (const sentence of englishSentences()) if (html.includes(inMarkup(sentence))) faults.push(`an English sentence on the French page: ${sentence}`);
      if (drawnResults.has(name)) faults.push(...freshFaults(html, drawnResults.get(name), locale));
      renderings.push({ name, locale, html, faults });
    }
  }
  if (write) {
    await mkdir(OUTPUT, { recursive: true });
    for (const { name, locale, html } of renderings) await writeFile(new URL(`${name}.${locale}.html`, OUTPUT), `${html}\n`, 'utf8');
  }
  return { renderings, samples: samples.length };
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const report = [];
  const group = (title, faults) => {
    report.push(`${faults.length === 0 ? 'ok  ' : 'FAIL'} ${title}${faults.length === 0 ? '' : `\n     ${faults.join('\n     ')}`}`);
    return faults.length;
  };
  let failed = 0;
  failed += group('copy: French and English hold the same keys', copyKeyDifferences());
  failed += group('copy: the templates name the same places in both languages', copyPlaceDifferences());
  failed += group('copy: French typography (no-break spaces, no straight apostrophe)', frenchTypographyFaults());
  failed += group('copy: no digit typed outside the known list', unexpectedDigits());
  failed += group('requests: no question in a request\'s address or a page\'s query', questionAddressFaults());
  failed += group('storage: no cookie, and only the conversation in the tab\'s session storage', storageFaults());
  const { renderings, samples } = await renderAll({ write: true });
  for (const { name, locale, faults } of renderings) failed += group(`render: ${name} (${locale})`, faults);
  console.log(report.join('\n'));
  console.log(`\nthe digits typed in the copy, all of them:\n  ${digitsInCopy().join('\n  ')}`);
  console.log(`\n${samples} sample answers, ${renderings.length} renderings written to ${OUTPUT.pathname}; ${failed} fault(s)`);
  process.exitCode = failed === 0 ? 0 : 1;
}
