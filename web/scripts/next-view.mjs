// What the side build's assistant shows of one answer of the new engine (`GET /api/next/ask`), as plain values: the chips
// of what Lex understood, the outcome's sentences, each block's label, lines and citation, the notes, the funnel, one row a
// step, the foot line and the link that replays the question. `app/NextDock.jsx` draws them; nothing here knows React.
//
// Every word is the copy table's (`next-copy.mjs`); every count, date and name is the answer's. A value the answer does not
// carry is left out, never invented, and a step or a note this module does not know is shown by its name with no summary.
//
// This module is data and pure functions only.

import { countedForm, decimal, fillNext, groupedCount, nextCopy, numericDate } from './next-copy.mjs';

/** The outcomes that assert an answer: the first block's article is then opened in the page. */
export const ASSERTED_OUTCOMES = Object.freeze(['answer', 'answer_with_exception']);

/** The outcomes drawn as a card of their own (nothing is asserted). */
const CARD_OUTCOMES = Object.freeze(['partial', 'candidates', 'not_found', 'not_a_legal_question']);

const ISO_DATE = /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u;

/** Whether a value is a calendar date as the engine writes one. */
export const isIsoDate = (value) => typeof value === 'string' && ISO_DATE.test(value);

/** Whether a value has the shape of an answer of the engine: an outcome and the row of what was understood. */
export function isAnswer(value) {
  return value !== null && typeof value === 'object' && typeof value.outcome === 'string' && value.row !== null && typeof value.row === 'object';
}

const list = (value) => (Array.isArray(value) ? value : []);
const text = (value) => (typeof value === 'string' && value.trim().length > 0 ? value.trim() : null);

/** A text with one final full stop: "Code du travail" and "Loi du 27 juillet 1997." both end with one. */
export const withStop = (value) => `${String(value).replace(/[.\s]+$/u, '')}.`;

/** A name with no final full stop, to stand inside a sentence: "Art. L. 124-3." is "Art. L. 124-3". */
const bare = (value) => String(value).replace(/[.\s]+$/u, '');

/**
 * The date Lex understood, in words: a year, a month, a day (dd/mm/yyyy), or today when the question named no date and
 * the reader chose none (`chosen` is the day the reader picked, or null).
 */
export function whenOf(date, locale, chosen = null) {
  const copy = nextCopy(locale).dock;
  if (date === null || typeof date !== 'object' || !isIsoDate(date.from)) return null;
  if (date.grain === 'year') return fillNext(copy.whenYear, { year: date.from.slice(0, 4) });
  if (date.grain === 'month') {
    const month = nextCopy(locale).months[Number(date.from.slice(5, 7)) - 1];
    return month === undefined ? numericDate(date.from) : fillNext(copy.whenMonth, { month, year: date.from.slice(0, 4) });
  }
  if (isIsoDate(date.to) && date.to !== date.from) return fillNext(copy.whenPeriod, { from: numericDate(date.from), to: numericDate(date.to) });
  return text(date.text) === null && chosen === null ? copy.whenToday : numericDate(date.from);
}

/** The chips of what Lex understood, in order: the date (the one a reader can change), the language, the kind, and the rest. */
export function understoodChips(result, locale, chosen = null) {
  const table = nextCopy(locale);
  const copy = table.dock;
  const row = result?.row ?? {};
  const chips = [];
  const when = whenOf(row.date, locale, chosen);
  if (when !== null) chips.push({ key: 'date', text: fillNext(copy.chipDate, { when }) });
  if (text(row.language) !== null) chips.push({ key: 'language', text: fillNext(copy.chipLanguage, { language: table.languages[row.language] ?? row.language }) });
  if (Object.hasOwn(table.questionTypes, row.question_type)) chips.push({ key: 'type', text: fillNext(copy.chipType, { type: table.questionTypes[row.question_type] }) });
  if (row.personal_case === true) chips.push({ key: 'personal', text: copy.chipPersonal });
  const unplaced = list(row.unplaced).map((word) => (typeof word === 'string' ? word : text(word?.text))).filter((word) => text(word) !== null);
  if (unplaced.length > 0) chips.push({ key: 'unplaced', text: fillNext(copy.chipUnplaced, { words: unplaced.join(table.listSeparator) }) });
  return chips;
}

/**
 * The outcome, said above the cards: `{ key, card, lead, more }`. A text not held for the date asked is said first, whatever
 * the outcome; an answer is one line, any other outcome a card of its own with what it means.
 */
export function outcomeOf(result, locale) {
  const copy = nextCopy(locale).dock.outcomes;
  const held = result?.not_held;
  if (held !== null && typeof held === 'object' && result.outcome !== 'not_found' && result.outcome !== 'not_a_legal_question') {
    const complete = text(held.act_title) !== null && isIsoDate(held.earliest_version) && isIsoDate(held.period?.[0]) && isIsoDate(held.period?.[1]);
    return {
      key: 'not_held',
      card: true,
      lead: copy.not_held.lead,
      more: complete ? fillNext(copy.not_held.more, {
        title: bare(held.act_title), earliest: numericDate(held.earliest_version), from: numericDate(held.period[0]), to: numericDate(held.period[1]),
      }) : null,
    };
  }
  const outcome = result?.outcome;
  if (!Object.hasOwn(copy, outcome) || outcome === 'not_held' || outcome === 'european_text_not_read') return null;
  if (outcome === 'not_found') {
    const found = result.not_found ?? {};
    const complete = Number.isFinite(found.searched_rows) && isIsoDate(found.period?.[0]) && isIsoDate(found.period?.[1]);
    return {
      key: outcome,
      card: true,
      lead: copy.not_found.lead,
      more: complete ? fillNext(copy.not_found.more, { rows: groupedCount(found.searched_rows, locale), from: numericDate(found.period[0]), to: numericDate(found.period[1]) }) : null,
    };
  }
  return { key: outcome, card: CARD_OUTCOMES.includes(outcome), lead: copy[outcome].lead, more: copy[outcome].more ?? null };
}

/** A CELEX number, as the engine names a European text's work ("32016R0679"): no Luxembourg work is named so. */
const CELEX = /^[0-9CE][0-9]{4}[A-Z]{1,2}[0-9]{4}/u;

/**
 * The text's address as the current site names it, from the engine's name of the work (and its act's id, when given), or
 * null: a European text (an act id "celex:…", or a work that is a CELEX number) by its CELEX alone, as the reading page
 * and the current dock name it; a Luxembourg text as `/lu-legilux/{work}`.
 */
export function identifierOf(work, actId = null) {
  const european = typeof actId === 'string' && actId.startsWith('celex:');
  const name = text(work) ?? (european ? text(actId.slice('celex:'.length)) : null);
  if (name === null) return null;
  return european || CELEX.test(name) ? name : `/lu-legilux/${name}`;
}

/** A text on one line: a title the publisher writes over several lines (or with a written "\n") is read as one. */
export const oneLine = (value) => String(value ?? '').replace(/\\n/gu, ' ').replace(/\s+/gu, ' ').trim();

/** A text's title to stand in a sentence: on one line, with no final full stop, an ellipsis the engine cut it with set against its last word. */
export const cleanTitle = (value) => bare(oneLine(value)).replace(/\s+…$/u, '…');

/** How long a title of a text stands in a list of the dock, at most, before it is cut. */
export const SHORT_TITLE = 60;

/** A title on one line with no final full stop, cut at a word near `max` characters and closed with "…" when longer. */
export function shortTitle(value, max = SHORT_TITLE) {
  const whole = cleanTitle(value);
  if (whole.length <= max) return whole;
  const head = whole.slice(0, max + 1);
  const space = head.lastIndexOf(' ');
  const kept = (space >= Math.floor(max * 0.6) ? head.slice(0, space) : whole.slice(0, max)).replace(/[\s,;:.–—-]+$/u, '');
  return `${kept}…`;
}

/** Names said one after another, the last joined by "and". */
function saidTogether(names, table) {
  return names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(table.listSeparator)}${table.pairSeparator}${names.at(-1)}`;
}

/** The searches that found an article, in words, from the answer's list or the trace's "a+b+c". */
export function detectorNames(found, locale) {
  const table = nextCopy(locale);
  const keys = Array.isArray(found) ? found : typeof found === 'string' ? found.split('+') : [];
  return keys.filter((key) => Object.hasOwn(table.detectors, key)).map((key) => table.detectors[key]);
}

/**
 * Since when an article's wording is unchanged, and what changed it last: from `wording_since`, `wording_until`,
 * `wording_no`, `wording_first_held` and `last_change`, as a block and the article's own answer carry them. Null when the
 * wording's start is not known. The wording of the first version held may be older than that version: said "at least".
 * The change is always said "amended by" its text, whatever kind the publisher records (its kinds are not reliable).
 */
export function wordingLine(entity, locale) {
  const copy = nextCopy(locale).wording;
  if (entity === null || typeof entity !== 'object' || !isIsoDate(entity.wording_since)) return null;
  const date = numericDate(entity.wording_since);
  const wording = isIsoDate(entity.wording_until) ? fillNext(copy.span, { from: date, to: numericDate(entity.wording_until) })
    : Number(entity.wording_first_held) === 1 && Number(entity.wording_no) === 1 ? fillNext(copy.sinceAtLeast, { date })
      : fillNext(copy.since, { date });
  const change = entity.last_change;
  if (change === null || typeof change !== 'object') return fillNext(copy.plain, { wording });
  // A title the engine cut short ends with "…": the sentence then ends with it, not with a full stop after it.
  return text(oneLine(change.title)) === null ? fillNext(copy.changeUnheld, { wording }) : fillNext(copy.change, { wording, title: cleanTitle(change.title) }).replace(/…\.$/u, '…');
}

/**
 * How sure an asserted answer is, said under the outcome: `{ key, text, caution }`. An article the person named is said
 * so; else all three searches, two (a caution, with their names) or one (a caution). Null when nothing is asserted.
 */
export function confidenceOf(result, locale) {
  if (!ASSERTED_OUTCOMES.includes(result?.outcome)) return null;
  const table = nextCopy(locale);
  const copy = table.dock.confidence;
  if (list(result.found_by).includes('named_by_the_person') || list(result.notes).includes('article_named_by_the_person')) return { key: 'named', text: copy.named, caution: false };
  if (result.confidence === 'firm') return { key: 'firm', text: copy.firm, caution: false };
  if (result.confidence !== 'supported' && result.confidence !== 'single') return null;
  const names = detectorNames(result.found_by, locale);
  const said = names.length === 0 ? copy[result.confidence].replace(' ({names})', '') : fillNext(copy[result.confidence], { names: saidTogether(names, table) });
  return { key: result.confidence, text: said, caution: true };
}

/**
 * What Lex searched, shown when nothing is asserted (`candidates`, `partial`, `not_found`): the kinds of text, the number
 * of passages and the period in one sentence, the texts whose table of contents was read, each word with the number of
 * articles that hold it (the rarest first: a word no article holds matters most), and what the closest passage holds and
 * lacks. Null for any other outcome, or when the answer carries no `searched`.
 */
export function searchedView(result, locale) {
  const table = nextCopy(locale);
  const copy = table.dock.searched;
  const searched = result?.searched;
  if (!['candidates', 'partial', 'not_found'].includes(result?.outcome) || searched === null || typeof searched !== 'object') return null;
  const kinds = list(searched.kinds_of_text).filter((kind) => text(kind) !== null).map((kind) => copy.kinds[kind] ?? kind.replaceAll('_', ' '));
  const [from, to] = list(searched.period);
  const period = !isIsoDate(from) ? null : !isIsoDate(to) || to === from ? fillNext(copy.periodDay, { date: numericDate(from) }) : fillNext(copy.periodSpan, { from: numericDate(from), to: numericDate(to) });
  const scope = Number.isFinite(searched.passages_in_those_kinds) && kinds.length > 0 && period !== null
    ? fillNext(copy.scope, { passages: countedForm(copy.passages, searched.passages_in_those_kinds, locale), kinds: saidTogether(kinds, table), period })
    : null;
  const words = Object.entries(searched.words !== null && typeof searched.words === 'object' ? searched.words : {})
    .filter(([, count]) => Number.isFinite(count))
    .map(([word, count]) => ({ word, count, said: countedForm(copy.holding, count, locale) }))
    .sort((a, b) => a.count - b.count);
  const near = searched.closest;
  let closest = null;
  if (near !== null && typeof near === 'object' && text(near.article) !== null) {
    const holds = list(near.holds).filter((word) => text(word) !== null);
    const lacks = list(near.lacks).filter((word) => text(word) !== null);
    const values = { article: bare(near.article), title: shortTitle(near.act_title ?? '') };
    if (holds.length > 0 || lacks.length > 0) {
      closest = lacks.length === 0 ? fillNext(copy.closestAll, values)
        : holds.length === 0 ? fillNext(copy.closestNone, values)
          : fillNext(copy.closest, { ...values, holds: holds.join(table.listSeparator), lacks: lacks.join(table.listSeparator) });
    }
  }
  const outlines = list(searched.texts_whose_table_of_contents_was_read).filter((title) => text(title) !== null).map((title) => shortTitle(title));
  return scope === null && words.length === 0 && closest === null && outlines.length === 0 ? null : { scope, outlines, words, closest };
}

/** The versions an answer was built with: the code's and the data's, as the answer names them. Null when it names none. */
export function buildLine(result, locale) {
  const build = result?.build;
  if (build === null || typeof build !== 'object' || text(build.code) === null || text(build.data) === null) return null;
  return fillNext(nextCopy(locale).dock.build, { code: build.code, data: build.data });
}

/**
 * One block as its card shows it: the role's label, the reason, the lines in order (a gap where lines were skipped), the
 * article and its heading, the act's title, the path, the version's sentence, the official address and where it opens.
 * Under an answer that found nothing (`outcome` "not_found"), a passage is labelled a close one, never a proposed one.
 */
export function blockView(block, locale, outcome = null) {
  const copy = nextCopy(locale).dock;
  const lines = [];
  let previous = null;
  for (const line of list(block?.lines)) {
    if (text(line?.text) === null) continue;
    if (previous !== null && Number.isFinite(line.n) && Number.isFinite(previous) && line.n > previous + 1) lines.push({ gap: true });
    lines.push({ gap: false, n: line.n ?? null, text: line.text });
    previous = line.n ?? null;
  }
  const article = text(oneLine(block?.article));
  const heading = text(oneLine(block?.heading));
  const version = isIsoDate(block?.version_from)
    ? (isIsoDate(block.version_to)
      ? fillNext(copy.versionSpan, { from: numericDate(block.version_from), to: numericDate(block.version_to) })
      : fillNext(copy.versionSince, { from: numericDate(block.version_from) }))
    : null;
  const identifier = identifierOf(block?.work, block?.act_id ?? null);
  return {
    repeal: repealLine(block?.repeal, locale),
    role: block?.role ?? null,
    label: outcome === 'not_found' ? copy.roles.nearest : copy.roles[block?.role] ?? copy.roles.candidate,
    why: text(block?.why),
    lines,
    cite: article === null ? null : heading === null ? fillNext(copy.citeNoHeading, { article: bare(article) }) : fillNext(copy.cite, { article: bare(article), heading: bare(heading) }),
    title: text(oneLine(block?.act_title)) === null ? null : withStop(oneLine(block.act_title)),
    path: text(oneLine(block?.path)) === null ? null : oneLine(block.path).split(/\s+>\s+/u).join(' › '),
    version,
    wording: wordingLine(block, locale),
    official: /^https:\/\//u.test(block?.official ?? '') ? block.official : null,
    opening: identifier === null || text(block?.anchor) === null ? null : { identifier, article: block.anchor, date: isIsoDate(block.date) ? block.date : null },
  };
}

/**
 * The repeal of a text that the publisher's records miss, as the engine names it (`repeal`: the day, the repealing text's
 * title, its article, whether some provisions stay in force): one sentence, or null when the text carries none.
 */
export function repealLine(repeal, locale) {
  const copy = nextCopy(locale).dock;
  if (repeal === null || typeof repeal !== 'object' || !isIsoDate(repeal.from) || text(repeal.by_title) === null || text(repeal.article) === null) return null;
  return fillNext(repeal.partial === true ? copy.repealPartial : copy.repeal, { date: numericDate(repeal.from), title: cleanTitle(repeal.by_title), article: bare(repeal.article) });
}

/**
 * The European texts the question names and Lex does not hold: the engine's own sentence for each, in the question's
 * language (`eu_not_held[].sentence`), with that language. Empty when the answer names none.
 */
export function euNotHeldLines(result) {
  const language = text(result?.row?.language) ?? 'fr';
  return list(result?.eu_not_held).filter((entry) => text(entry?.sentence) !== null)
    .map((entry, at) => ({ key: `${text(entry.celex) ?? 'eu'}-${at}`, text: entry.sentence.trim(), lang: ['fr', 'en', 'de'].includes(language) ? language : 'fr' }));
}

/**
 * What Lex does not hold, from the counts `make_coverage_gaps.py` wrote (`coverage` of a "not found", `coverage_gaps` of the
 * corpus answer): the texts named, the laws and regulations of the catalogue not held, and the codes' first dates held.
 * Empty when the counts are not there; a name the copy does not know is left out, never shown as the engine's key.
 */
export function coverageLines(gaps, locale) {
  const table = nextCopy(locale);
  const copy = table.dock.coverage;
  if (gaps === null || typeof gaps !== 'object') return [];
  const lines = [];
  const named = list(gaps.not_held).filter((key) => Object.hasOwn(copy.names, key)).map((key) => copy.names[key]);
  const catalogue = gaps.catalogue ?? {};
  if (named.length > 0 && Number.isFinite(catalogue.not_held) && Number.isFinite(catalogue.listed)) {
    lines.push({ key: 'not-held', text: fillNext(copy.line, { named: named.join(table.listSeparator), notHeld: groupedCount(catalogue.not_held, locale), listed: groupedCount(catalogue.listed, locale) }) });
  }
  const floors = list(gaps.codes).filter((code) => text(code?.title) !== null && isIsoDate(code?.first))
    .sort((a, b) => a.title.localeCompare(b.title, locale))
    .map((code) => fillNext(copy.floor, { title: oneLine(code.title), date: numericDate(code.first) }));
  if (floors.length > 0) lines.push({ key: 'floors', text: fillNext(copy.floors, { list: floors.join(table.clauseSeparator) }) });
  return lines;
}

/** Where an asserted answer opens in the page: its first block's article, or null (nothing asserted, nothing opened). */
export function answerOpening(result, locale) {
  if (!ASSERTED_OUTCOMES.includes(result?.outcome)) return null;
  const first = list(result.blocks)[0];
  return first === undefined ? null : blockView(first, locale).opening;
}

/** The lines an answer quotes in one text, as the reading page marks them (`lex:mark`): each with its article and its words. */
export function marksOf(result, identifier) {
  return list(result?.blocks)
    .filter((block) => identifierOf(block?.work, block?.act_id ?? null) === identifier && text(block?.anchor) !== null)
    .flatMap((block) => list(block.lines).filter((line) => text(line?.text) !== null).map((line) => ({ article: block.anchor, text: line.text })));
}

/** The sentences under the cards: the notes that have one, then "See also" as one line. */
export function noteLines(result, locale) {
  const table = nextCopy(locale);
  const copy = table.dock;
  const notes = new Set(list(result?.notes));
  if (result?.row?.personal_case === true && list(result?.blocks).length > 0) notes.add('personal_case');
  const lines = Object.keys(copy.notes).filter((note) => notes.has(note)).map((note) => ({ key: note, text: copy.notes[note] }));
  const also = list(result?.see_also).filter((entry) => text(entry?.article) !== null).map((entry) => {
    const why = text(entry.why) ?? '';
    const said = text(entry.heading) === null
      ? fillNext(copy.seeAlsoItemNoHeading, { article: bare(entry.article), why })
      : fillNext(copy.seeAlsoItem, { article: bare(entry.article), heading: bare(entry.heading), why });
    return said.replace(/,\s*$/u, '');
  });
  if (also.length > 0) lines.push({ key: 'see_also', text: fillNext(copy.seeAlso, { list: also.join(table.clauseSeparator) }) });
  // A text the question names that a held text repeals: said with the day, the repealing text and its article.
  list(result?.repealed_named).forEach((entry, at) => {
    if (entry === null || typeof entry !== 'object' || !isIsoDate(entry.from) || text(entry.act_title) === null || text(entry.by_title) === null || text(entry.article) === null) return;
    lines.push({
      key: `repealed-${at}`,
      text: fillNext(entry.partial === true ? copy.repealedNamedPartial : copy.repealedNamed, { title: cleanTitle(entry.act_title), date: numericDate(entry.from), by: cleanTitle(entry.by_title), article: bare(entry.article) }),
    });
  });
  return lines;
}

const stepsOf = (result) => list(result?.trace?.steps).filter((step) => step !== null && typeof step === 'object' && typeof step.step === 'string');
const detailOf = (result, name) => stepsOf(result).find((step) => step.step === name)?.detail ?? null;

/**
 * The funnel: from every passage in the search down to the lines copied, each a count and its words. Empty when the answer
 * made no search (a question that is not about the law).
 */
export function funnelRows(result, locale) {
  const copy = nextCopy(locale).dock.funnel;
  const found = detailOf(result, 'find_meaning');
  if (found === null) return [];
  const read = detailOf(result, 'read');
  const blocks = list(result?.blocks);
  // Nothing found: the closest passages shown are no answer's quotation, and none of their lines is counted as copied.
  const answered = result?.outcome !== 'not_found';
  const counts = [
    ['index', found.index_rows],
    ['scope', found.rows_in_scope],
    ['proposed', found.articles_kept],
    ['read', read?.articles_read],
    ['quoted', answered ? blocks.length : null],
    ['lines', answered ? blocks.reduce((sum, block) => sum + list(block?.lines).length, 0) : null],
  ];
  return counts.filter(([, count]) => Number.isFinite(count)).map(([key, count]) => ({ key, count: groupedCount(count, locale), label: countedForm(copy[key], count, locale) }));
}

/** A step's time: milliseconds under a second, else seconds with one decimal; null when the step carries none. */
export function timeOf(ms, locale) {
  const table = nextCopy(locale);
  if (!Number.isFinite(ms)) return null;
  return ms < 1000 ? fillNext(table.milliseconds, { value: groupedCount(Math.round(ms), locale) }) : fillNext(table.seconds, { value: decimal(ms / 1000, locale, 1) });
}

/** One step's line: what was done, from the step's own detail. '' for a step this module does not know. */
function summaryOf(step, result, locale, chosen) {
  const table = nextCopy(locale);
  const copy = table.dock.summaries;
  const detail = step.detail ?? {};
  const counted = (entry, count, values) => countedForm(entry, count, locale, values);
  const yesNo = (value) => (value === true ? table.yes : table.no);
  switch (step.step) {
    case 'question_code':
      return fillNext(copy.questionCode, {
        when: whenOf(detail.date, locale, chosen) ?? '',
        articles: counted(copy.namedArticles, list(detail.named_articles).length),
        acts: counted(copy.namedActs, list(detail.named_acts).length),
      });
    case 'question_model': {
      if (detail.in_scope === false) return copy.notInScope;
      const type = table.questionTypes[detail.question_type] ?? table.questionTypes.other;
      const terms = list(detail.legal_terms).filter((term) => text(term) !== null);
      return terms.length === 0 ? fillNext(copy.questionModelNoTerms, { type }) : fillNext(copy.questionModel, { type, terms: terms.join(table.listSeparator) });
    }
    case 'find_exact':
      return counted(copy.findExact, detail.named_found);
    case 'find_words':
      return fillNext(copy.findWords, {
        words: counted(copy.wordsAsked, list(detail.words_of_the_question).length),
        terms: counted(copy.termsAsked, list(detail.terms_a_statute_would_use).length),
        kept: counted(copy.articlesRetained, detail.articles_kept),
      });
    case 'outline_walk': {
      const walks = list(detail.walks).filter((walk) => walk !== null && typeof walk === 'object' && text(oneLine(walk.title)) !== null);
      const texts = counted(copy.outlineTexts, Number.isFinite(detail.texts_walked) ? detail.texts_walked : walks.length);
      const whole = detail.whole_part_for_a_list === true ? copy.outlineWhole : '';
      if (walks.length === 0) return `${texts}${whole}`;
      // A short list: each text read by its title, cut short, and the parts named in it. Both are the law's own words.
      const items = walks.map((walk) => {
        const parts = list(walk.parts_named).filter((part) => text(oneLine(part)) !== null).map((part) => shortTitle(part));
        return { title: shortTitle(walk.title), parts: parts.length === 0 ? null : parts.join(table.listSeparator) };
      });
      return { line: fillNext(copy.outlineWalk, { texts, whole }), items };
    }
    case 'find_meaning':
      // One sentence: the funnel above already says how many passages were searched, and how many in the right kinds.
      return fillNext(list(detail.kinds_of_text_searched).length > 1 ? copy.findMeaningWide : copy.findMeaning, {
        kept: counted(copy.meaningKept, detail.rows_kept),
        articles: counted(copy.articlesKept, detail.articles_kept),
        acts: counted(copy.actsKept, detail.acts),
      });
    case 'absence':
      if (!Number.isFinite(detail.articles_read) || !Number.isFinite(detail.texts_read)) return '';
      return fillNext(detail.answer_seen_in_the_articles_read === true ? copy.absenceSeen : copy.absence, {
        articles: counted(copy.absenceArticles, detail.articles_read),
        texts: counted(copy.absenceTexts, detail.texts_read),
      });
    case 'date': {
      const [from, to] = list(detail.period);
      const values = { from: numericDate(from), to: numericDate(to), moved: counted(copy.moved, detail.candidates_moved_to_another_version), excluded: counted(copy.excluded, detail.not_applicable_then) };
      return from === to ? fillNext(copy.dateDay, { from: values.from, moved: values.moved, excluded: values.excluded }) : fillNext(copy.datePeriod, values);
    }
    case 'european_text': {
      if (text(detail.named_in_question) === null) return copy.europeanTextBare;
      const missing = list(detail.not_held).filter((celex) => text(celex) !== null);
      return missing.length === 0 ? fillNext(copy.europeanText, { words: detail.named_in_question.trim() })
        : fillNext(copy.europeanTextMissing, { words: detail.named_in_question.trim(), missing: missing.join(table.listSeparator) });
    }
    case 'version_held':
      return isIsoDate(detail.earliest_version_held) ? fillNext(copy.versionHeld, { date: numericDate(detail.earliest_version_held) }) : '';
    case 'read': {
      const lines = list(detail.lines);
      // The reading names an article by its place in the list it was handed ("A1"): said here by the article's own name.
      const named = list(result?.candidates).find((candidate) => candidate?.label === detail.pointed)?.article ?? null;
      const pointed = text(detail.pointed) === null ? copy.pointedNone
        : fillNext(copy.pointed, { article: bare(named ?? detail.pointed), lines: counted(copy.linesPointed, lines.length, { list: lines.join(table.listSeparator) }) });
      const line = fillNext(copy.read, { read: counted(copy.articlesRead, detail.articles_read), pointed, found: yesNo(detail.answer_found), complete: yesNo(detail.complete_answer_found) });
      if (detail.found_by === 'named_by_the_person') return fillNext(copy.readNamed, { line });
      const finders = detectorNames(detail.found_by, locale);
      return finders.length === 0 ? line : fillNext(copy.readFoundBy, { line, names: finders.join(table.listSeparator) });
    }
    case 'copy_and_check':
      return fillNext(copy.copyAndCheck, { copied: counted(copy.copied, detail.lines_copied), refused: counted(copy.refused, list(detail.line_numbers_refused).length) });
    case 'reference_step':
      return fillNext(copy.referenceStep, { inbound: counted(copy.inbound, detail.inbound_checked), outbound: counted(copy.outbound, detail.outbound_checked), followed: counted(copy.followed, detail.followed) });
    case 'exception_decision':
      return fillNext(copy.exceptionDecision, { applies: copy.applies[detail.applies] ?? copy.applies.unknown, read: counted(copy.otherArticlesRead, detail.articles_read) });
    case 'reach_gate':
      return text(detail.who_is_covered) === null ? copy.reachGateBare : fillNext(copy.reachGate, { who: bare(detail.who_is_covered) });
    case 'complete_gate':
      return copy.completeGate;
    case 'outcome': {
      const outcome = table.dock.outcomeNames[detail.outcome] ?? detail.outcome ?? '';
      const notes = list(detail.notes).map((note) => table.dock.noteNames[note] ?? note);
      return notes.length === 0 ? fillNext(copy.outcome, { outcome }) : fillNext(copy.outcomeWithNotes, { outcome, notes: notes.join(table.listSeparator) });
    }
    default:
      return '';
  }
}

/**
 * The steps, one row each: its name, who did it (`code`, `model` or `both`), its time, and what was done: one line, and for
 * the table of contents a short list of the texts read (`items`: each `{ title, parts }`, the law's own words; `parts` null
 * when none was named).
 */
export function stepRows(result, locale, chosen = null) {
  const copy = nextCopy(locale).dock;
  return stepsOf(result).map((step, at) => {
    let line = '';
    let items = [];
    try {
      const said = summaryOf(step, result, locale, chosen);
      if (typeof said === 'string') line = said;
      else if (said !== null && typeof said === 'object') {
        line = said.line ?? '';
        items = Array.isArray(said.items) ? said.items : [];
      }
    } catch {
      line = '';
      items = [];
    }
    return {
      key: `${step.step}-${at}`,
      step: step.step,
      name: copy.steps[step.step] ?? step.step.replaceAll('_', ' '),
      who: step.who === 'model' ? 'model' : step.who === 'code' ? 'code' : 'both',
      line,
      items,
      time: timeOf(step.ms, locale),
    };
  });
}

/** "code alone" or "code + N model calls", from the trace's own count of model calls. */
export function builtBy(result, locale) {
  const calls = result?.trace?.model_calls;
  return Number.isFinite(calls) ? countedForm(nextCopy(locale).dock.builtBy, calls, locale) : null;
}

/** The foot line: the model calls, the whole time and the cost at list price, said as not yet compared with the bill. */
export function footLine(result, locale) {
  const table = nextCopy(locale);
  const copy = table.dock;
  const trace = result?.trace ?? {};
  if (!Number.isFinite(trace.model_calls) || !Number.isFinite(trace.ms) || !Number.isFinite(trace.cost_usd_list_price)) return null;
  return fillNext(copy.foot, {
    calls: countedForm(copy.calls, trace.model_calls, locale),
    seconds: fillNext(table.seconds, { value: decimal(trace.ms / 1000, locale, 1) }),
    cost: fillNext(copy.costUsd, { value: decimal(trace.cost_usd_list_price, locale) }),
  });
}

/** The question as it is sent and replayed: every no-break space as a plain one, trimmed. */
export const plainQuestion = (question) => String(question ?? '').replace(/[  ]/gu, ' ').trim();

/** The engine's address for a question: the same for every question, since no word of a question goes in an address. */
export const ASK_ADDRESS = '/api/next/ask';

/**
 * The request that asks a question: a POST to `ASK_ADDRESS` whose JSON body holds the question and the day to read the law
 * on (null when the reader chose none). The site's rule (its privacy notice): a reader's words travel in the body of the
 * request, never in the request's address.
 */
export function askRequest(question, date = null) {
  return Object.freeze({
    address: ASK_ADDRESS,
    init: Object.freeze({
      method: 'POST',
      headers: Object.freeze({ 'content-type': 'application/json', accept: 'application/json' }),
      body: JSON.stringify({ question: plainQuestion(question), date: isIsoDate(date) ? date : null }),
    }),
  });
}

/**
 * The link that replays a question at the same date: the home page of the language, the question and the day after "#",
 * in the part of an address a browser never sends (as a link that keeps a search does).
 */
export function replayHref(question, day, locale) {
  if (text(question) === null || !isIsoDate(day)) return null;
  return `/${locale}/#q=${encodeURIComponent(plainQuestion(question))}&on=${day}`;
}

/**
 * What a page's fragment asks the dock to replay (`#q=...&on=YYYY-MM-DD`), or null. Only a fragment that names a question
 * and a day, and nothing else, is one: a search's fragment (`#q=...&lang=...`, which names no day) is the search page's.
 */
export function replayOf(hash) {
  const body = typeof hash === 'string' && hash.startsWith('#') ? hash.slice(1) : '';
  if (body.length === 0) return null;
  const values = new Map();
  for (const pair of body.split('&')) {
    const at = pair.indexOf('=');
    const key = at < 0 ? pair : pair.slice(0, at);
    if ((key !== 'q' && key !== 'on') || at < 0 || values.has(key)) return null;
    try {
      values.set(key, decodeURIComponent(pair.slice(at + 1)));
    } catch {
      return null;
    }
  }
  const question = text(values.get('q'));
  const day = values.get('on');
  return question === null || question.length < 2 || question.length > 600 || !isIsoDate(day) ? null : { question, day };
}

/** The day the law was read on when the reader chose none: the answer's own day when the question named no date, else `today`. */
export function referenceDay(result, chosen, today) {
  if (isIsoDate(chosen)) return chosen;
  const date = result?.row?.date;
  if (date?.grain === 'day' && text(date.text) === null && isIsoDate(date.from)) return date.from;
  return isIsoDate(today) ? today : null;
}
