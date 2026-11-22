// What the assistant shows of an answer in the engine's contract (`lex-next/TRACE-CONTRACT.md`), as plain values: the state
// and what it means, the fields Lex understood, each block with its role, its version and its dates and the lines it
// quotes, the list's completeness, the texts not held, the evidence of a "not found", the clarification's options, and the
// trace (the funnel's levels, each row by code or by a model, the model calls measured, the cost, the replay token).
// `app/NextDock.jsx` draws them; nothing here knows React.
//
// Every word is the copy table's (`next-copy.mjs`, section `contract`); every count, date, name and reason is the answer's.
// A value the answer does not carry is left out, never invented. A key the copy does not know is shown as the engine wrote
// it, inside the trace only (which is technical detail, behind a fold).
//
// An answer is in the contract when it carries a `state` (a string); the earlier shape (`outcome`) is `next-view.mjs`'s.
//
// This module is data and pure functions only.

import { countedForm, decimal, fillNext, groupedCount, nextCopy, numericDate } from './next-copy.mjs';
import { identifierOf, isIsoDate, oneLine } from './next-view.mjs';

/** The states of the contract, in the order of how much they assert. */
export const CONTRACT_STATES = Object.freeze(['answer', 'answer_with_exception', 'partial', 'not_held', 'not_found', 'candidates', 'clarify', 'out_of_scope']);

/** The states that assert an answer: the first block's article is then opened in the page. */
export const CONTRACT_ASSERTED = Object.freeze(['answer', 'answer_with_exception', 'partial']);

const list = (value) => (Array.isArray(value) ? value : []);
const text = (value) => (typeof value === 'string' && value.trim().length > 0 ? value.trim() : null);
const isCount = (value) => typeof value === 'number' && Number.isFinite(value);
const objectOr = (value) => (value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null);

/** A text's title to show: on one line, without the publisher's "Version consolidée applicable au …:" before it. */
export function actTitle(value) {
  const one = text(oneLine(value));
  // A routing line ("Loi … [LOI; loi_de_fond; …] — summary") is cut at its bracket: the title, and the part named, stay.
  return one === null ? null : text(one.replace(/^Version (?:consolid|rectifi)[^:]{0,60}:\s*/iu, '').replace(/\s*\[[^\]]*(?:\]|$)[\s\S]*$/u, ''));
}

/** Whether a value is an answer in the contract: a `state` the copy knows, or any other string state. */
export function isContractAnswer(value) {
  return objectOr(value) !== null && typeof value.state === 'string' && value.state.length > 0;
}

/**
 * Whether an answer carries the contract's whole trace (the funnel's levels, or its model calls), and is drawn by the
 * contract's view. The earlier pipeline's answers carry the contract's fields beside their own (`contract_from_topk`), with a
 * trace of one decision level: they are drawn by the earlier view, which reads their own fuller trace.
 */
export function isFullContract(value) {
  if (!isContractAnswer(value)) return false;
  const trace = objectOr(value.trace);
  if (list(trace?.levels).some((level) => level?.level !== 'decision')) return true;
  const earlier = typeof value.outcome === 'string' && objectOr(value.row) !== null && list(trace?.steps).length > 0;
  return !earlier;
}

/**
 * An answer of the earlier pipeline given the contract's fields, read back in its own shape for the earlier view: each
 * block's line objects (moved to `quote`), and the text not held for the date asked as one object (the contract lists it by
 * name), rebuilt from the step that says from when the text is held.
 */
export function legacyAnswer(result) {
  const blocks = list(result?.blocks).map((block) => (Array.isArray(block?.quote) ? { ...block, lines: block.quote } : block));
  let notHeld = result?.not_held;
  if (Array.isArray(notHeld)) {
    const step = list(result?.trace?.steps).find((one) => one?.step === 'version_held');
    const earliest = step?.detail?.earliest_version_held;
    const date = result?.row?.date;
    notHeld = list(result?.notes).includes('text_not_held_for_that_date') && isIsoDate(earliest) && blocks.length > 0 && isIsoDate(date?.from) && isIsoDate(date?.to)
      ? { act_title: blocks[0].act_title, earliest_version: earliest, period: [date.from, date.to] } : null;
  }
  return { ...result, blocks, not_held: notHeld };
}

/** The current site's name of a text from the engine's act id: `/eli/etat/leg/loi/1997/07/27/n10` is `loi-1997-07-27-n10`. */
export function workOf(actId) {
  if (typeof actId !== 'string') return null;
  if (actId.startsWith('celex:')) return text(actId.slice('celex:'.length));
  if (!actId.startsWith('/eli/')) return null;
  const tail = actId.split('/eli/etat/leg/').at(-1);
  return tail.split('/').filter((part) => part.length > 0).map((part) => part.replaceAll('_', '-')).join('-') || null;
}

/**
 * The state, said at the head of the answer: `{ key, badge, lead, more }`. `more` is what the state names: the missing
 * piece of a partial answer, the "it does not mean the law is silent" of a not-found. Null for a state the copy does not know.
 */
export function contractStateOf(answer, locale) {
  const copy = nextCopy(locale).contract;
  const key = answer?.state;
  if (!Object.hasOwn(copy.states, key)) return null;
  const state = copy.states[key];
  let more = state.more ?? null;
  let moreBy = null;
  if (key === 'partial') {
    // pages2: a compound question answered one funnel per part says its missing parts by code ("partie 2: not_found"): said
    // in words from the parts' own states; any other `missing` is the reading model's sentence.
    const coded = /^partie [0-9]+: [a-z_]+(?:; partie [0-9]+: [a-z_]+)*$/u.test(text(answer.missing) ?? '');
    if (coded) {
      const parts = text(answer.missing).split('; ').map((piece) => /^partie ([0-9]+): ([a-z_]+)$/u.exec(piece)).filter((match) => match !== null)
        .map((match) => fillNext(nextCopy(locale).contract2.parts.missingPart, { n: match[1], state: copy.states[match[2]]?.badge ?? match[2] }));
      more = fillNext(copy.missing, { missing: parts.join(nextCopy(locale).clauseSeparator) });
      moreBy = 'code';
    } else {
      more = text(answer.missing) === null ? copy.missingUnsaid : fillNext(copy.missing, { missing: text(answer.missing) });
      moreBy = text(answer.missing) === null ? null : 'model';
    }
  }
  return { key, badge: state.badge, lead: state.lead, more, moreBy };
}

/**
 * pages2 (K-GEN-7): who wrote a block's reason. The engine's `why_by` when it says; otherwise by the reason's own form: code
 * writes "ajouté par la référence…", "ajouté parce que…", "élément de la liste…", "« … » est défini par ce texte", "désigné
 * par la lecture" and "Art. N déroge à Art. M (…)"; any other reason is a model's (the reading's or the arbiter's).
 */
export function reasonBy(block) {
  if (block?.why_by === 'code' || block?.why_by === 'model') return block.why_by;
  const why = text(block?.why) ?? '';
  if (/^(?:ajouté par la référence|ajouté parce que|élément de la liste|désigné par la lecture)/u.test(why)) return 'code';
  if (/^«[^»]+»\s+est défini par ce texte/u.test(why)) return 'code';
  if (/^(?:Art\.|Article)\s[^—]{0,40}\s(?:écarte|déroge à|réserve|rend applicable|renvoie à|cite)\s(?:Art\.|Article)\s/u.test(why)) return 'code';
  return 'model';
}

/**
 * A phrase of the engine (a reason, a filter, a rule) in the page's language: from the copy's list of the engine's phrases, by
 * the whole phrase or by its start; null when the copy does not know it (the caller then shows it as written, in the trace
 * only, or not at all).
 */
export function enginePhrase(phrase, locale, kind = 'reasons') {
  const copy = nextCopy(locale).contract.engine;
  const value = text(phrase);
  if (value === null) return null;
  const table = copy[kind] ?? {};
  if (Object.hasOwn(table, value)) return table[value];
  if (kind === 'reasons') {
    const start = Object.keys(copy.reasonPrefixes).find((prefix) => value.startsWith(prefix));
    if (start !== undefined) return copy.reasonPrefixes[start];
  }
  return null;
}

/**
 * The texts not held that the answer names, each `{ name, why }` said in one line; whatever the state. A reason the copy does
 * not know is shown on an English page as the engine wrote it, and left out on a French one (French pages stay French).
 */
/**
 * pages2: the engine's English reasons for a text not held at the date asked, said in the page's language with their dates
 * as dd/mm/yyyy ("held only from 2025-02-10; the date asked is 2024-01-01", "the wording held applies from … until …; the
 * wording in force on … is not held"); null for another reason.
 */
export function notHeldWhy(why, locale) {
  const copy = nextCopy(locale).contract2.notHeldWhy;
  const value = text(why);
  if (value === null) return null;
  const day = (date) => (isIsoDate(date) ? numericDate(date) : date);
  let match = /^held only from ([0-9]{4}-[0-9]{2}-[0-9]{2}); the date asked is ([0-9]{4}-[0-9]{2}-[0-9]{2})$/u.exec(value);
  if (match !== null) return fillNext(copy.onlyFrom, { from: day(match[1]), asked: day(match[2]) });
  match = /^the wording held applies from ([0-9]{4}-[0-9]{2}-[0-9]{2}) until ([0-9]{4}-[0-9]{2}-[0-9]{2})(?: \([^)]*\))?; the wording in force on ([0-9]{4}-[0-9]{2}-[0-9]{2}) is not held$/u.exec(value);
  if (match !== null) return fillNext(copy.wording, { from: day(match[1]), until: day(match[2]), asked: day(match[3]) });
  match = /^held only from ([0-9]{4}-[0-9]{2}-[0-9]{2})$/u.exec(value);
  if (match !== null) return fillNext(copy.onlyFromBare, { from: day(match[1]) });
  return null;
}

export function notHeldLines(answer, locale) {
  const copy = nextCopy(locale).contract;
  return list(answer?.not_held).filter((entry) => text(entry?.name) !== null).map((entry) => {
    const why = enginePhrase(entry.why, locale) ?? notHeldWhy(entry.why, locale) ?? (locale === 'en' ? text(entry.why) : /^(?:cité|nommé|publié)/u.test(text(entry.why) ?? '') ? text(entry.why) : null);
    return { name: text(entry.name), line: why === null ? text(entry.name) : fillNext(copy.notHeldItem, { name: text(entry.name), why }) };
  });
}

/** The clarification: the question and its options, or null. */
export function clarifyOf(answer) {
  const clarify = objectOr(answer?.clarify);
  if (clarify === null) return null;
  const options = list(clarify.options).map(text).filter((option) => option !== null);
  return options.length === 0 && text(clarify.question) === null ? null : { question: text(clarify.question), options };
}

/**
 * The question asked again when the reader picks an option: the option itself when it is a whole question (it ends with a
 * question mark), otherwise the first question with the option after it.
 */
export function clarifiedQuestion(question, option) {
  const picked = String(option ?? '').trim();
  if (/[?？]\s*$/u.test(picked)) return picked;
  return `${String(question ?? '').trim().replace(/\s*[?？]\s*$/u, '')} (${picked})`.trim();
}

/** The date the law was read on, said with where it came from: the question, or today. Null when the answer gives none. */
export function dateLine(answer, locale) {
  if (!isIsoDate(answer?.date_used)) return null;
  const copy = nextCopy(locale).contract;
  return fillNext(answer.date_said === true ? copy.dateAsked : copy.dateToday, { date: numericDate(answer.date_used) });
}

/** A field's value as words: a list joined, a yes said by the field's own name, a date as dd/mm/yyyy. */
function fieldValue(value, locale) {
  const table = nextCopy(locale);
  if (Array.isArray(value)) return value.map((one) => fieldValue(one, locale)).filter((one) => one !== null).join(table.listSeparator) || null;
  if (typeof value === 'boolean') return value ? table.yes : null;
  if (typeof value === 'number') return groupedCount(value, locale);
  if (isIsoDate(value)) return numericDate(value);
  return text(value);
}

/**
 * What Lex understood, from `trace.question.fields`: each `{ key, name, value, by }`, the name in words when the copy knows
 * it. A field with no value is left out; `by` is "code" or "model" as the engine says.
 */
export function understoodFields(answer, locale) {
  const table = nextCopy(locale);
  const copy = table.contract;
  // The fields a reader can check, in words: the language and the kind of question named, the terms, the texts and articles
  // named, a text named and not held, the person's own situation. The engine's working fields (its French rendering, the
  // scope, the parts) stay in the trace.
  const said = {
    language: (value) => table.languages[value] ?? null,
    question_type: (value) => table.questionTypes[value] ?? null,
    type: (value) => table.questionTypes[value] ?? null,
    legal_terms: (value) => fieldValue(value, locale), terms: (value) => fieldValue(value, locale),
    named_acts: (value) => fieldValue(value, locale), acts: (value) => fieldValue(value, locale), act: (value) => fieldValue(value, locale),
    named_articles: (value) => fieldValue(value, locale), articles: (value) => fieldValue(value, locale), article: (value) => fieldValue(value, locale),
    // engine2: the words of the question found in no text held (K-QUEST-4).
    vocabulary_misses: (value) => fieldValue(value, locale),
    // repair-pages (K-QUEST-3): the dictionary rows the question's words matched, each with the law's term and its status.
    dictionary_rows: (value) => {
      const said = list(value).filter((row) => text(row?.matched) !== null && text(row?.legal_term) !== null).map((row) => fillNext(table.contract2.dictionaryRow, {
        matched: text(row.matched), term: text(row.legal_term),
        status: row.status === 'validated' && text(row.validator) !== null ? fillNext(table.contract2.dictionaryValidated, { validator: text(row.validator) }) : table.contract2.dictionaryDraft,
      }));
      return said.length === 0 ? null : said.join(table.listSeparator);
    },
    personal_case: (value) => (value === true ? '' : null),
  };
  return list(answer?.trace?.question?.fields).filter((field) => text(field?.name) !== null && Object.hasOwn(said, field.name)).flatMap((field) => {
    const value = said[field.name](field.value);
    if (value === null || value === undefined) return [];
    const name = copy.fields[field.name] ?? field.name.replaceAll('_', ' ');
    const by = field.by === 'model' ? 'model' : field.by === 'code' ? 'code' : null;
    return [{ key: field.name, name, value: value === '' ? null : value, by }];
  });
}

/** The version's sentence: from and until, from with no later version known, from and not known whether it still applies. */
export function versionLine(version, locale) {
  const copy = nextCopy(locale).contract;
  const value = objectOr(version);
  if (value === null || !isIsoDate(value.from)) return copy.versionUndated;
  if (isIsoDate(value.until)) return fillNext(copy.versionSpan, { from: numericDate(value.from), until: numericDate(value.until) });
  // `still_applies` (engine2): the store says this wording applies today, with no end recorded.
  return fillNext(value.until_known === false && value.still_applies !== true ? copy.versionUnsure : copy.versionOpen, { from: numericDate(value.from) });
}

/**
 * The lines a block quotes, in order: `{ n, text }` from the parallel lists `lines` (numbers) and `text` (the words, copied by
 * code), a gap where numbers were skipped. When the two lists differ in length, the words alone, unnumbered.
 */
export function quotedLines(block) {
  const numbers = list(block?.lines);
  const words = list(block?.text);
  const paired = numbers.length === words.length && numbers.every(isCount);
  const out = [];
  let previous = null;
  words.forEach((word, at) => {
    if (text(word) === null) return;
    const n = paired ? numbers[at] : null;
    if (previous !== null && n !== null && n > previous + 1) out.push({ gap: true });
    out.push({ gap: false, n, text: word });
    previous = n;
  });
  return out;
}

/**
 * One block as its card shows it: the role's label, the article's label and act, the version's sentence, the reason, the
 * lines, and where the article opens in the page (by the names the current site gives the text and the article: the block's
 * `work` and `anchor` when it carries them, else the work from its act id and the anchor resolved later).
 */
export function contractBlockView(block, locale, state = null) {
  const copy = nextCopy(locale).contract;
  const role = typeof block?.role === 'string' ? block.role : null;
  const nearest = state === 'not_found';
  const work = text(block?.work) ?? workOf(block?.act_id);
  const identifier = identifierOf(work, block?.act_id ?? null);
  const anchor = text(block?.anchor);
  const version = objectOr(block?.version);
  return {
    role,
    label: nearest ? copy.roles.nearest : state === 'candidates' ? copy.roles.candidate : copy.roles[role] ?? copy.roles.candidate,
    articlePk: isCount(block?.article_pk) ? block.article_pk : null,
    cite: text(block?.label),
    title: actTitle(block?.act_title),
    version: versionLine(version, locale),
    why: text(block?.why),
    lines: quotedLines(block),
    heading: text(block?.heading),
    missingNow: list(block?.lines_missing_now).length > 0 || block?.lines_missing_now === true,
    identifier,
    anchor,
    date: isIsoDate(version?.from) ? version.from : null,
    official: /^https:\/\//u.test(block?.official ?? '') ? block.official : null,
  };
}

/**
 * A reason the reading wrote, its articles named as the reader saw them ("A1", "A8": the place in the list it was handed,
 * `meta.read`) said by their own names when the answer names them.
 */
export function readableReason(reason, answer) {
  const value = text(reason);
  if (value === null) return null;
  const read = list(answer?.meta?.read);
  const name = articleNamer(answer);
  // repair-pages: a date a reason carries (yyyy-mm-dd) is said dd/mm/yyyy.
  return value.replace(/\bA([0-9]{1,3})\b/gu, (whole, n) => name(read[Number(n) - 1]?.article_pk) ?? whole)
    .replace(/\b([0-9]{4})-([0-9]{2})-([0-9]{2})\b/gu, '$3/$2/$1');
}

/** Where an asserted answer opens in the page: its first block's article, when its name is known; or null. */
export function contractOpening(answer, locale) {
  if (!CONTRACT_ASSERTED.includes(answer?.state)) return null;
  const first = list(answer.blocks)[0];
  if (first === undefined) return null;
  const view = contractBlockView(first, locale, answer.state);
  return view.identifier === null || view.anchor === null ? null : { identifier: view.identifier, article: view.anchor, date: view.date };
}

/** The lines an answer quotes in one text, as the reading page marks them (`lex:mark`). */
export function contractMarks(answer, identifier, locale) {
  return list(answer?.blocks).map((block) => contractBlockView(block, locale, answer.state))
    .filter((view) => view.identifier === identifier && view.anchor !== null)
    .flatMap((view) => view.lines.filter((line) => !line.gap).map((line) => ({ article: view.anchor, text: line.text })));
}

/** The list's statement of completeness, by code from the structure (`list`); null when the answer is no list. */
export function listStatement(answer, locale) {
  const copy = nextCopy(locale).contract.list;
  const value = objectOr(answer?.list);
  if (value === null || !isCount(value.items_shown)) return null;
  const items = (count) => countedForm(copy.items, count, locale);
  const heading = text(value.heading);
  const total = isCount(value.items_total) ? value.items_total : value.items_shown;
  const complete = value.complete === true && total === value.items_shown;
  const main = complete
    ? fillNext(heading === null ? copy.completeBare : copy.complete, { shown: items(value.items_shown), ...(heading === null ? {} : { heading }) })
    : fillNext(heading === null ? copy.incompleteBare : copy.incomplete, { shown: groupedCount(value.items_shown, locale), total: items(total), ...(heading === null ? {} : { heading }) });
  const stated = isCount(value.count_stated_by_text) ? fillNext(copy.stated, { stated: items(value.count_stated_by_text), total: groupedCount(total, locale) }) : null;
  return { complete, main, stated };
}

// ---------------------------------------------------------------------------------------------------------------------------
// pages2 (21/11/2026): the fields TRACE-CONTRACT.md added that the panel did not draw yet: the parts of a compound question,
// the items of a list made of headings, the engine's notes, the checks by code, "see also", how a block was found, the lines
// the reading pointed at against those code added, and where a text's words come from (its PDF, or a character recognition).

/**
 * The parts of a compound question (`parts`, two or more): each `{ n, question, status, label, blocks }`, `blocks` the
 * indexes of the answer's blocks that carry this part (`block.part`, 1-based); `loose` the blocks that carry no part. A part
 * is "answered", "not answered" or "not said" (`answered`), or has its own state (one funnel per part). Null for one question.
 */
export function partsView(answer, locale) {
  const copy = nextCopy(locale).contract2.parts;
  const states = nextCopy(locale).contract.states;
  const parts = list(answer?.parts).filter((part) => text(part?.question) !== null);
  if (parts.length < 2) return null;
  const blocks = list(answer?.blocks);
  const grouped = blocks.some((block) => isCount(block?.part));
  const rows = parts.map((part, at) => {
    const n = at + 1;
    let status = 'unsaid';
    if (typeof part.state === 'string') status = CONTRACT_ASSERTED.includes(part.state) && part.state !== 'partial' ? 'answered' : part.state === 'partial' ? 'partial' : 'not_answered';
    else if (part.answered === true) status = 'answered';
    else if (part.answered === false) status = 'not_answered';
    const own = typeof part.state === 'string' && Object.hasOwn(states, part.state) ? states[part.state].badge : null;
    return {
      n, question: text(part.question), status, label: own ?? copy.status[status],
      blocks: grouped ? blocks.map((block, index) => (block?.part === n ? index : null)).filter((index) => index !== null) : [],
    };
  });
  const loose = grouped ? blocks.map((block, index) => (isCount(block?.part) && block.part >= 1 && block.part <= parts.length ? null : index)).filter((index) => index !== null) : blocks.map((_, index) => index);
  const open = rows.filter((row) => row.status !== 'answered').length;
  return {
    heading: fillNext(copy.heading, { count: groupedCount(parts.length, locale) }),
    rows, grouped, loose,
    complete: open === 0,
    summary: open === 0 ? copy.allAnswered : countedForm(copy.open, open, locale),
  };
}

/**
 * The items of a list the engine built from the headings of an outline node (`list.items`): each `{ key, heading, articles,
 * cite, block }`, `block` the index of the block that stands for the item (`block.list_item`, or its article), so the item is a
 * link to its article. Also the items the law deleted (`list.deleted_items`, a count or a list). Null when the list has no items.
 */
export function listItemsView(answer, locale) {
  const copy = nextCopy(locale).contract2.list;
  const value = objectOr(answer?.list);
  if (value === null) return null;
  const blocks = list(answer?.blocks);
  const items = list(value.items).filter((item) => text(item?.heading) !== null).map((item, at) => {
    const index = blocks.findIndex((block) => (isCount(item.article_pk) && block?.article_pk === item.article_pk) || text(block?.list_item) === text(item.heading));
    return {
      key: String(item.node_id ?? at),
      heading: text(item.heading),
      articles: isCount(item.articles) && item.articles > 0 ? countedForm(copy.articles, item.articles, locale) : null,
      first: text(item.first_article),
      block: index >= 0 ? index : null,
    };
  });
  const deletedCount = isCount(value.deleted_items) ? value.deleted_items : Array.isArray(value.deleted_items) ? value.deleted_items.length : 0;
  const deleted = deletedCount > 0 ? countedForm(copy.deleted, deletedCount, locale) : null;
  if (items.length === 0 && deleted === null) return null;
  return { heading: text(value.heading), items, deleted };
}

/** The engine's notes (`notes`) said in words: each `{ key, text, caution }`; a note the copy does not know is left out. */
export function notesView(answer, locale) {
  const copy = nextCopy(locale).contract2.notes;
  const caution = new Set(['reading_unavailable', 'question_model_unavailable', 'corpus_model_unavailable', 'arbiter_unavailable', 'conflicting_evidence', 'cost_cap_reached_no_second_reading']);
  const seen = new Set();
  return list(answer?.notes).filter((note) => typeof note === 'string' && Object.hasOwn(copy, note) && !seen.has(note) && seen.add(note))
    .map((note) => ({ key: note, text: copy[note], caution: caution.has(note) }));
}

/**
 * The checks by code about the first block (`checks`), as yes/no facts and never a percentage (K-UI-6): `lead`, one sentence
 * a reader can weigh (found by meaning alone: to be checked; or the searches that found it), and `rows` for the trace.
 */
export function checksView(answer, locale) {
  const copy = nextCopy(locale).contract2.checks;
  const checks = objectOr(answer?.checks);
  if (checks === null) return null;
  const yes = (value) => (value === true ? copy.yes : value === false ? copy.no : copy.unknown);
  const rows = [];
  for (const key of ['exact_path', 'found_by_outline', 'found_by_words', 'meaning_only', 'meaning_only_verified', 'list_complete_by_structure', 'conflicting_evidence']) {
    if (!Object.hasOwn(checks, key) || !Object.hasOwn(copy.names, key)) continue;
    if (key === 'list_complete_by_structure' && checks[key] === null) continue;
    rows.push({ key, name: copy.names[key], value: yes(checks[key]) });
  }
  if (isCount(checks.references_followed)) rows.push({ key: 'references_followed', name: copy.names.references_followed, value: groupedCount(checks.references_followed, locale) });
  let lead = null;
  if (CONTRACT_ASSERTED.includes(answer?.state) && list(answer?.blocks).length > 0) {
    if (checks.meaning_only === true) lead = { text: copy.meaningOnly, caution: true };
    else {
      const by = ['exact_path', 'found_by_outline', 'found_by_words'].filter((key) => checks[key] === true).map((key) => copy.by[key]);
      if (by.length > 0) lead = { text: fillNext(copy.foundBy, { searches: joinWords(by, locale) }), caution: false };
    }
  }
  return rows.length === 0 && lead === null ? null : { lead, rows };
}

/** Words joined as a sentence lists them: "a, b and c". */
function joinWords(words, locale) {
  const table = nextCopy(locale);
  if (words.length <= 1) return words.join('');
  return `${words.slice(0, -1).join(table.listSeparator)}${table.pairSeparator ?? ' and '}${words.at(-1)}`;
}

/**
 * engine2's fields (TRACE-CONTRACT.md, "Fields added by engine2"), said in words:
 *   - `shortAnswer`: the words of the law that give the short answer, kept by code only when they stand in a quoted line, with
 *     the reader's reading of their kind (said as the model's);
 *   - `repealed`: each text the person named that no longer applies on the date asked, with the text that repealed it;
 *   - `transposedBy`: the Luxembourg texts held that transpose a directive the answer first quoted;
 *   - `euNotHeld`: the sentence for a European text named and not held, in the question's language.
 */
export function engineSaid(answer, locale) {
  const table = nextCopy(locale);
  const copy = table.contract2;
  const short = objectOr(answer?.short_answer);
  const shortAnswer = short !== null && text(short.words) !== null
    ? {
      line: fillNext(copy.shortAnswer.line, { words: text(short.words), label: text(short.label) ?? articleNamer(answer)(short.article_pk) ?? '' }).replace(/\s*\(\)$/u, ''),
      kind: typeof short.kind === 'string' && Object.hasOwn(copy.shortAnswer.kinds, short.kind) ? fillNext(copy.shortAnswer.kind, { kind: copy.shortAnswer.kinds[short.kind] }) : null,
    } : null;
  const repealed = list(answer?.repealed_named).filter((entry) => text(entry?.act_title) !== null || text(entry?.named_as) !== null).map((entry) => {
    const title = actTitle(entry.act_title) ?? text(entry.named_as);
    const by = actTitle(entry.by_title) ?? text(entry.by);
    if (isIsoDate(entry.from) && by !== null) {
      return fillNext(entry.status === 'repealed_in_part' ? table.dock.repealedNamedPartial : table.dock.repealedNamed, { title, date: numericDate(entry.from), by, article: text(entry.article) ?? '' }).replace(/\s*\(\)/u, '');
    }
    return title;
  });
  const directive = objectOr(answer?.directive_transposed_by);
  const transposedBy = directive === null ? [] : list(directive.transposed_by).map((entry) => actTitle(entry?.title) ?? text(entry?.act_id)).filter((one) => one !== null);
  const euNotHeld = list(answer?.eu_not_held).map((entry) => text(entry?.sentence)).filter((one) => one !== null);
  return { shortAnswer, repealed, transposedBy, euNotHeld };
}

/** "See also" (`see_also`): each `{ key, article, why }`, in words; empty when the answer names none. */
export function seeAlsoView(answer) {
  // The answer's own list; else the one the reference level kept (a pointer to several articles at once, not followed).
  const own = list(answer?.see_also);
  const fromLevel = list(answer?.trace?.levels).filter((level) => level?.level === 'references').flatMap((level) => list(level.see_also));
  return (own.length > 0 ? own : fromLevel).filter((entry) => text(entry?.article) !== null || text(entry?.heading) !== null)
    .map((entry, at) => ({ key: `${entry.article ?? 'see'}-${at}`, article: text(entry.article) ?? text(entry.heading), heading: text(entry.article) === null ? null : text(entry.heading), why: text(entry.why) }));
}

/** How a block came into the answer, in words: through a reference, the "cited by" index, or code widening on what was missing; null for the first search. */
export function blockVia(block, locale) {
  const copy = nextCopy(locale).contract2.via;
  // The engine names the article as its number is printed ("Art. 12."): its final full stop is left to the sentence.
  const from = text(String(block?.referenced_from ?? '').replace(/\.\s*$/u, ''));
  // repair-pages: the article after the main one, which sets the consequence of its case (repair-engine's `consequence`).
  if (block?.consequence === true) return from === null ? nextCopy(locale).contract3.consequenceBare : fillNext(nextCopy(locale).contract3.consequence, { from });
  if (block?.inbound === true) return from === null ? copy.inboundBare : fillNext(copy.inbound, { from });
  if (block?.retrieved_via === 'reference') return from === null ? copy.referenceBare : fillNext(copy.reference, { from });
  if (block?.retrieved_via === 'widened') return copy.widened;
  return null;
}

/**
 * engine2's marks on a block, in words: the act a compilation reproduces (`compiled_from`), the original wording held and the
 * provisions that amended it since (`original_wording`, `amends_this_text`). Each a sentence, or null.
 */
export function blockMarks(block, locale) {
  const copy = nextCopy(locale).contract2.marks;
  const compiled = objectOr(block?.compiled_from);
  const title = actTitle(compiled?.title);
  // repair-pages: an original wording a later text replaced (`replaced_by`), and the text that replaced it (`replaces`).
  const extra = nextCopy(locale).contract3;
  const replacedBy = objectOr(block?.replaced_by);
  const replaces = objectOr(block?.replaces);
  return {
    compiled: title === null ? null : actTitle(compiled.compilation_title) === null ? fillNext(copy.compiledBare, { title }) : fillNext(copy.compiled, { title, compilation: actTitle(compiled.compilation_title) }),
    original: block?.original_wording === true
      ? (replacedBy !== null && text(replacedBy.label) !== null ? fillNext(isIsoDate(replacedBy.from) ? extra.replacedBy : extra.replacedByBare, { label: text(replacedBy.label), ...(isIsoDate(replacedBy.from) ? { date: numericDate(replacedBy.from) } : {}) }) : copy.original)
      : null,
    amends: block?.amends_this_text === true ? copy.amends : replaces !== null && text(replaces.label) !== null ? fillNext(extra.replaces, { label: text(replaces.label) }) : null,
  };
}

/** engine2's `amended_since` (the texts that amended an original wording held): their names, or an empty list. */
export function amendedSince(answer, locale = 'fr') {
  const value = objectOr(answer?.amended_since);
  const held = list(value?.acts).map((entry) => (typeof entry === 'string' ? actTitle(entry) : actTitle(entry?.title) ?? text(entry?.act_id))).filter((one) => one !== null);
  // repair-pages: the amending texts Lex does not hold, named and said not held (repair-engine's `not_held_acts`).
  const unheld = list(value?.not_held_acts).map((entry) => (typeof entry === 'string' ? actTitle(entry) : actTitle(entry?.title) ?? text(entry?.act_id))).filter((one) => one !== null)
    .map((title) => fillNext(nextCopy(locale).contract3.notHeldAmending, { title }));
  return [...held, ...unheld];
}

/** Where a block's words come from, as the engine marks it: a character recognition (OCR) or the publisher's PDF; or null. */
export function blockSource(block) {
  const value = objectOr(block?.source) ?? objectOr(block?.version?.source) ?? objectOr(block?.text_source);
  const how = typeof block?.source === 'string' ? block.source : typeof block?.identity_from === 'string' ? block.identity_from : typeof value?.identity_from === 'string' ? value.identity_from : '';
  if (block?.ocr === true || value?.ocr === true || /ocr/iu.test(how)) return 'ocr';
  if (value?.pdf === true || /pdf/iu.test(how)) return 'pdf';
  return null;
}

/**
 * repair-pages (22/11/2026): the language a sentence is written in, told by its commonest small words: "fr", "en", "de", or
 * null when it cannot be told (too short). Used to mark a model's reason with its language (the engine writes reasons in
 * the question's language, or in French), never to translate it.
 */
export function sentenceLanguage(sentence) {
  const value = ` ${String(sentence ?? '').toLowerCase().replace(/[«»"“”(),.;:!?]/gu, ' ')} `;
  const count = (words) => words.reduce((sum, word) => sum + (value.split(` ${word} `).length - 1), 0);
  const scores = {
    fr: count(['le', 'la', 'les', 'des', 'du', 'de', 'un', 'une', 'est', 'et', 'qui', 'que', 'pour', 'dans', 'sur', 'par', 'au', 'aux', 'ne', 'pas', 'cet', 'cette', 'il', 'se']),
    en: count(['the', 'of', 'and', 'is', 'to', 'in', 'for', 'this', 'that', 'it', 'on', 'by', 'not', 'are', 'be', 'an', 'with', 'which']),
    de: count(['der', 'die', 'das', 'und', 'ist', 'nicht', 'des', 'den', 'dem', 'für', 'wird', 'ein', 'eine', 'mit', 'auf', 'von', 'zu', 'im', 'bei', 'oder']),
  };
  const best = Object.entries(scores).sort((a, b) => b[1] - a[1]);
  return best[0][1] >= 2 && best[0][1] > best[1][1] ? best[0][0] : null;
}

/**
 * repair-pages: the `lang` a block's reason is marked with on a page of `locale`: the engine's word on the reason's language
 * when it gives one (`why_lang`, or the answer's `reasons_language`), else the language the reason's words are in; undefined
 * when it is the page's own (French when nothing can be told, as the engine's reasons were before it wrote them in the
 * question's language).
 */
export function reasonLang(block, answer, locale) {
  const map = { deu: 'de', de: 'de', fra: 'fr', fr: 'fr', eng: 'en', en: 'en' };
  const said = [block?.why_lang, block?.why_language, answer?.reasons_language].find((value) => typeof value === 'string' && Object.hasOwn(map, value.toLowerCase()));
  const lang = said !== undefined ? map[said.toLowerCase()] : (sentenceLanguage(block?.why) ?? 'fr');
  return lang === locale ? undefined : lang;
}

const CODE_VERBS_EN = Object.freeze({
  'renvoie pour la définition à': 'refers for the definition to', réserve: 'preserves', 'rend applicable': 'makes applicable', 'renvoie à': 'refers to', cite: 'cites',
  'déroge à': 'departs from', écarte: 'sets aside',
});

/**
 * repair-pages: a reason code wrote (refs.py, funnel.py: "cité par Art. 69, par. 1 (ligne 1): « … » — renvoie à Art. 70", "élément
 * de la liste « … »: …", "ajouté parce que la lecture dit qu'il manque: « … » …", "« … » est défini par ce texte", "Art. 24 écarte
 * Art. 21 (…)") said in English on an English page, the law's quoted words left as they are; null for any other sentence.
 */
export function codeReasonInEnglish(reason) {
  const value = text(reason);
  if (value === null) return null;
  // "ajouté par la référence: …" (refs.py): the frame, then the reason it carries.
  const prefixed = /^ajouté par la référence\s?: (.+)$/u.exec(value);
  if (prefixed !== null) {
    const inner = codeReasonInEnglish(prefixed[1]);
    if (inner !== null) return `added by the reference: ${inner}`;
    const relation = /^(modifie|abroge|transpose|complète) (.+)$/u.exec(prefixed[1]);
    return relation === null ? null : `added by the reference: ${{ modifie: 'amends', abroge: 'repeals', transpose: 'transposes', complète: 'completes' }[relation[1]]} ${relation[2]}`;
  }
  const unit = (place) => place.replace(/\bal\./gu, 'subpara.').replace(/\bpar\./gu, 'par.').replace(/\blettre\b/gu, 'letter').replace(/\bligne\b/gu, 'line');
  const tail = (words) => words.replace(/ pour les assurances sur la vie/u, ' for life insurance').replace(/ \(renvoi entrant\)/u, ' (inbound reference)');
  const verbs = Object.keys(CODE_VERBS_EN).sort((a, b) => b.length - a.length).map((verb) => verb.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&')).join('|');
  let match = new RegExp(`^cité par (.+?) \\(ligne ([0-9]+)\\)(?:: (« … .* … »))?(?: — (${verbs}) (.+))?$`, 'u').exec(value);
  if (match !== null) return `cited by ${unit(match[1])} (line ${match[2]})${match[3] ? `: ${match[3]}` : ''}${match[4] ? ` — ${CODE_VERBS_EN[match[4]]} ${match[5]}` : ''}`;
  match = /^cité par (.+?) \((.+)\)$/u.exec(value);
  if (match !== null) return `cited by ${unit(match[1])} (${match[2]})`;
  match = /^élément de la liste « ?(.+?) ?»\s?: (.+)$/u.exec(value);
  if (match !== null) return `item of the list « ${match[1]} »: ${match[2]}`;
  match = /^ajouté parce que la lecture dit qu['’]il manque\s?: (« .* »)(?: \(cherché par ses mots dans les textes choisis\))?$/u.exec(value);
  if (match !== null) return `added because the reading says this is missing: ${match[1]} (searched by its words in the texts picked)`;
  match = /^(« ?[^»]+ ?») est défini par ce texte(.*)$/u.exec(value);
  if (match !== null) return `${match[1]} is defined by this text${match[2]}`;
  match = /^(« ?[^»]+ ?») est défini par ((?:Art\.|Article)\s.+?) de ce texte(.*)$/u.exec(value);
  if (match !== null) return `${match[1]} is defined by ${match[2]} of this text${match[3]}`;
  if (/^désigné par la lecture/u.test(value)) return value.replace(/^désigné par la lecture/u, 'pointed at by the reading');
  // repair-engine's reasons (22/11/2026): an amending text, an original wording, the consequence of a rule.
  match = /^modifie ce texte \(en vigueur le ([^)]+)\)$/u.exec(value);
  if (match !== null) return `amends this text (in force on ${match[1]})`;
  match = /^texte d['’]origine de (.+?), que (.+?) modifie ou remplace \(en vigueur le ([^)]+)\)$/u.exec(value);
  if (match !== null) return `the original wording of ${match[1]}, which ${match[2]} amends or replaces (in force on ${match[3]})`;
  match = /^texte d['’]origine, modifié depuis le ([^ ]+) \(non consolidé\)$/u.exec(value);
  if (match !== null) return `the original wording, amended since ${match[1]} (no consolidation held)`;
  match = /^article suivant dans la même partie\s?: il règle la conséquence du cas visé par (.+?) \((« .*? »)\s?; mots repris de l['’]intitulé\s?: (.*)\)$/u.exec(value);
  if (match !== null) return `the next article in the same part: it sets the consequence of the case ${match[1]} covers (${match[2]}; words taken up from the heading: ${match[3]})`;
  match = new RegExp(`^((?:Art\\.|Article)\\s[^—]{0,40}?)\\s(${verbs})\\s((?:Art\\.|Article|ANNEXE|Annexe)\\s.+)$`, 'u').exec(value);
  if (match !== null) return `${unit(match[1])} ${CODE_VERBS_EN[match[2]]} ${tail(match[3])}`;
  return null;
}

/**
 * repair-pages (22/11/2026): the language a block's words are in, as the engine or the store says it (`block.language`,
 * `block.source.language`, or the version's language from `/api/next/sources`: "deu", "fra", "eng"): "de", "fr", "en"; null
 * when nothing says it (the law's words are then French, as the store holds nearly every text).
 */
export function blockLanguage(block, versionSource = null) {
  const said = [block?.authentic_language, block?.language, block?.lang, objectOr(block?.source)?.language, objectOr(block?.version)?.language, versionSource?.language]
    .find((value) => typeof value === 'string' && value.trim().length > 0);
  if (said === undefined) return null;
  const value = said.trim().toLowerCase();
  return { deu: 'de', de: 'de', ger: 'de', fra: 'fr', fr: 'fr', fre: 'fr', eng: 'en', en: 'en' }[value] ?? null;
}

const LANGS = Object.freeze({ deu: 'de', de: 'de', fra: 'fr', fr: 'fr', eng: 'en', en: 'en', lb: 'lb', ltz: 'lb' });
/** The `lang` a piece in language `language` is marked with on a page of `locale`: undefined when it is the page's own. */
const markedLang = (language, locale) => {
  const lang = typeof language === 'string' ? LANGS[language.toLowerCase()] ?? null : null;
  return lang === null || lang === locale ? undefined : lang;
};

/**
 * repair-pages (22/11/2026, K-GEN-3): the typed answer repair-engine adds on a firm answer (`typed_answer`): the value code
 * formatted, the law's words that answer with where they stand, the reader's kind, the conditions and exceptions verbatim, and
 * the model's summary of the quoted lines when code found every number of it in them. Each piece `{ text, lang }`, `lang` the
 * language to mark it with on this page. Null when the answer carries none.
 */
export function typedAnswerView(answer, locale) {
  const typed = objectOr(answer?.typed_answer);
  if (typed === null) return null;
  const table = nextCopy(locale);
  const copy = table.contract3.typed;
  const asked = markedLang(typed.language, locale);
  const words = objectOr(typed.law_words);
  const value = objectOr(typed.value);
  const summary = objectOr(typed.summary) ?? objectOr(answer?.summary);
  const lawLang = locale === 'fr' ? undefined : 'fr';
  const german = list(answer?.blocks).some((block) => block?.article_pk === words?.article_pk && block?.authentic_language === 'de');
  const kind = typeof typed.kind === 'string' && Object.hasOwn(table.contract2.shortAnswer.kinds, typed.kind) ? fillNext(table.contract2.shortAnswer.kind, { kind: table.contract2.shortAnswer.kinds[typed.kind] }) : null;
  return {
    value: text(value?.text) === null ? null : { frame: fillNext(copy.value, { value: '\u0000' }).split('\u0000'), text: text(value.text), lang: asked },
    words: text(words?.text) === null ? null : { text: text(words.text), where: text(words?.label), lang: german ? 'de' : lawLang, by: /^the first line/u.test(text(words?.by) ?? '') ? copy.firstLine : copy.words },
    kind,
    conditions: list(typed.conditions_and_exceptions).filter((entry) => text(entry?.text) !== null).map((entry, at) => {
      // The engine keeps the first words of a long line: said with "…" when the quoted line is longer.
      const full = list(answer?.blocks).find((block) => text(block?.label) === text(entry.label));
      const line = text(list(full?.text)[0]);
      const cut = line !== null && line.length > text(entry.text).length + 2;
      return {
        key: `${entry.label ?? 'c'}-${at}`,
        role: table.contract.roles[entry.role] ?? text(entry.role) ?? '',
        label: text(entry.label) ?? '',
        text: cut ? `${text(entry.text)} …` : text(entry.text),
      };
    }),
    summary: text(summary?.text) === null ? null : { text: text(summary.text), lang: asked, label: copy.summaryLabel },
  };
}

/** repair-pages (K-GEN-4): two texts that disagree (`conflict`): the two articles named, or null. */
export function conflictView(answer) {
  const conflict = objectOr(answer?.conflict);
  if (conflict === null) return null;
  const articles = list(conflict.articles).map((entry) => text(entry?.label)).filter((one) => one !== null);
  return { articles, pks: list(conflict.articles).map((entry) => entry?.article_pk).filter(isCount) };
}

/** repair-pages (K-GEN-8): the independent check of a pick found by meaning alone (`verification`), in words; null when none ran. */
export function verificationLine(answer, locale) {
  const check = objectOr(answer?.verification);
  if (check === null || check.needed !== true) return null;
  const copy = nextCopy(locale).contract3.verified;
  const reason = text(check.reason) ?? '';
  if (check.passed !== true) return { text: copy.failed, caution: true, reasonLang: undefined };
  const byModel = /^model/u.test(text(check.by) ?? '');
  return { text: fillNext(byModel ? copy.model : copy.code, { reason }), caution: false, reasonLang: byModel ? markedLang(sentenceLanguage(reason), locale) : undefined };
}

/** repair-pages (K-TIME-4): the original wordings no held amending text targets (`unchanged_by_later_amendments`): their sentences. */
export function unchangedLines(answer, locale) {
  return list(answer?.unchanged_by_later_amendments).filter((entry) => text(entry?.sentence) !== null)
    .map((entry) => ({ text: text(entry.sentence).replace(/\b([0-9]{4})-([0-9]{2})-([0-9]{2})\b/gu, '$3/$2/$1'), lang: markedLang(sentenceLanguage(entry.sentence), locale) }));
}

/** The lines the reading pointed at, when code widened them to their whole paragraph (`lines_pointed_by_reading`); null otherwise. */
export function pointedLines(block) {
  const pointed = list(block?.lines_pointed_by_reading).filter(isCount);
  const added = list(block?.lines_added_by_code).filter(isCount);
  return pointed.length > 0 && added.length > 0 ? new Set(pointed) : null;
}

/** An article named by its pk, from the blocks and the candidates the answer carries; null when none names it. */
export function articleNamer(answer) {
  const names = new Map();
  const add = (pk, label) => {
    if (isCount(pk) && text(label) !== null && !names.has(pk)) names.set(pk, text(label));
  };
  for (const block of list(answer?.blocks)) add(block?.article_pk, block?.label);
  for (const candidate of list(answer?.meta?.candidates)) add(candidate?.article_pk, candidate?.label ?? candidate?.article);
  for (const read of list(answer?.meta?.read)) add(read?.article_pk, read?.label ?? read?.article);
  for (const level of list(answer?.trace?.levels)) for (const candidate of list(level?.candidates)) add(candidate?.article_pk, candidate?.label ?? candidate?.article);
  return (pk) => names.get(pk) ?? null;
}

/**
 * The evidence of a "not found" (or of proposed passages), in plain words: what Lex holds (the coverage), each word searched
 * with its forms and the number of articles that hold it (the rarest first), how much the searches went through, and the
 * closest passage with why it does not answer. Null when the answer carries none.
 */
export function evidenceView(answer, locale) {
  const copy = nextCopy(locale).contract.evidence;
  const evidence = objectOr(answer?.evidence);
  if (evidence === null) return null;
  const terms = list(evidence.terms).filter((term) => text(term?.term) !== null && isCount(term?.hits))
    // A form the index searches by its start ("patern*") is said in words (K-UI-4): "words starting with « patern »".
    .map((term) => {
      // repair-pages: each form says whether it is the law's word (marked in the law's language) or the page's own phrase.
      const forms = list(term.variants).map(text).filter((one) => one !== null && one !== text(term.term))
        .map((one) => (/^[^*\s]+\*$/u.test(one) ? { text: fillNext(nextCopy(locale).contract2.startsWith, { stem: one.slice(0, -1) }), law: false } : { text: one, law: true }));
      // repair-pages: where the word came from (a draft dictionary row, a model's legal term, the question's words).
      const source = text(term.source) ?? '';
      const from = /^dictionary/u.test(source) ? 'dictionary' : /^model \(legal/u.test(source) ? 'model' : /^question words/u.test(source) ? 'question' : /^model \(French rendering/u.test(source) ? 'rendering' : null;
      return {
        term: text(term.term),
        variants: forms.map((form) => form.text),
        forms,
        hits: term.hits,
        said: countedForm(copy.hits, term.hits, locale),
        source: from === null ? null : nextCopy(locale).contract3.termSources[from],
      };
    })
    .sort((a, b) => a.hits - b.hits);
  const sweep = objectOr(evidence.sweep);
  const gradeKey = typeof sweep?.grade === 'string' ? sweep.grade.split(':')[0].trim() : null;
  const grade = gradeKey === null ? null : copy.grades[gradeKey] ?? null;
  // pages2: the sweep counts the articles of the whole store that hold every word searched together (`funnel.sweep`), not
  // the articles gone through: said so, with those in the texts picked; the grade only when it adds to the count.
  const extra = nextCopy(locale).contract2.sweep;
  let swept = grade;
  if (sweep !== null && isCount(sweep.acts) && isCount(sweep.articles)) {
    const together = sweep.articles === 0 ? extra.none : countedForm(extra.together, sweep.articles, locale, { acts: countedForm(copy.acts, sweep.acts, locale) });
    const picked = isCount(sweep.articles_in_the_texts_picked) && sweep.articles > 0 ? countedForm(extra.picked, sweep.articles_in_the_texts_picked, locale) : null;
    swept = [together, picked, gradeKey === 'medium' && sweep.articles === 0 ? null : grade].filter((part) => part !== null).join(' ');
  }
  const closest = objectOr(evidence.closest);
  const named = closest === null ? null : articleNamer(answer)(closest.article_pk) ?? text(closest.label);
  const near = closest === null || named === null ? null
    : text(closest.why_not) === null ? fillNext(copy.closestBare, { article: named }) : fillNext(copy.closest, { article: named, why: text(closest.why_not) });
  const coverage = text(evidence.coverage) ?? text(objectOr(evidence.coverage)?.summary) ?? text(objectOr(evidence.coverage)?.text);
  // pages2 (K-SCOPE-4): the date floor of the texts picked (engine2's `coverage_detail`), each named by its title in the answer.
  const titles = new Map();
  for (const level of list(answer?.trace?.levels)) for (const act of list(level?.acts)) if (text(act?.act_id) !== null && actTitle(act?.title) !== null) titles.set(act.act_id, actTitle(act.title));
  for (const block of list(answer?.blocks)) if (text(block?.act_id) !== null && actTitle(block?.act_title) !== null) titles.set(block.act_id, actTitle(block.act_title));
  const floors = list(objectOr(evidence.coverage_detail)?.date_floor_of_the_texts_picked)
    .filter((entry) => isIsoDate(entry?.first_held) && titles.has(entry?.act_id))
    .map((entry) => fillNext(nextCopy(locale).contract2.floor, { title: titles.get(entry.act_id).replace(/\s*>.*$/u, ''), date: numericDate(entry.first_held) }));
  if (coverage === null && terms.length === 0 && swept === null && near === null && floors.length === 0) return null;
  // repair-pages (K-GEN-5): what the grade rests on (draft words), and the question's own words in another language, apart.
  const extra3 = nextCopy(locale).contract3;
  const basis = text(sweep?.grade_basis) === null ? null : extra3.gradeBasis;
  const ownWords = list((objectOr(sweep?.question_words_in_another_language) ?? objectOr(evidence.question_words_in_another_language))?.words).filter((entry) => text(entry?.term) !== null && isCount(entry?.hits));
  const other = ownWords.length === 0 ? null : fillNext(extra3.otherWords, { words: ownWords.map((entry) => fillNext(extra3.otherWord, { term: entry.term, hits: countedForm(copy.hits, entry.hits, locale) })).join(nextCopy(locale).listSeparator) });
  return { coverage, terms, swept, closest: near, floors, basis, other };
}

// ---------------------------------------------------------------------------------------------------------------------------
// The trace.

const byOf = (value) => (value === 'model' ? 'model' : value === 'code' ? 'code' : typeof value === 'string' && /model|embedding/u.test(value) ? 'both' : 'code');

/** A row of the trace: `{ key, text, by, items, table }`; `items` lines under it, `table` a small table. */
function row(key, said, by, extra = {}) {
  return { key, text: said, by, items: [], table: null, ...extra };
}

/**
 * The funnel's levels, one section each, in the engine's order: `{ level, name, by, rows }`. Each row says what was done
 * there, with its counts; the code or the model that did it. A level the copy does not name is shown by its key.
 */
export function traceLevels(answer, locale) {
  const table = nextCopy(locale);
  const copy = table.contract.trace;
  const extra = table.contract2.trace;
  const name = articleNamer(answer);
  const article = (pk) => name(pk) ?? (isCount(pk) ? `#${pk}` : '');
  const titles = new Map();
  for (const level of list(answer?.trace?.levels)) for (const act of list(level?.acts)) if (text(act?.act_id) !== null && actTitle(act?.title) !== null) titles.set(act.act_id, actTitle(act.title));
  for (const block of list(answer?.blocks)) if (text(block?.act_id) !== null && actTitle(block?.act_title) !== null && !titles.has(block.act_id)) titles.set(block.act_id, actTitle(block.act_title));
  return list(answer?.trace?.levels).filter((level) => text(level?.level) !== null).map((level, at) => {
    const key = level.level;
    const rows = [];
    switch (key) {
      case 'corpus': {
        if (isCount(level.considered) && isCount(level.kept)) {
          const considered = countedForm(copy.considered, level.considered, locale);
          const kept = countedForm(copy.kept, level.kept, locale);
          rows.push(row('counts', isCount(level.shown_to_model)
            ? fillNext(copy.shownToModel, { considered, shown: countedForm(copy.shownLines, level.shown_to_model, locale), kept })
            : fillNext(copy.corpusLine, { considered, kept }), byOf(level.by)));
        }
        for (const filter of list(level.filters)) {
          if (text(filter?.rule) === null || !isCount(filter?.removed) || filter.removed === 0) continue;
          const rule = enginePhrase(filter.rule, locale, 'filters') ?? enginePhrase(filter.rule, locale) ?? filter.rule;
          rows.push(row('filter', fillNext(copy.filter, { rule, removed: countedForm(copy.removed, filter.removed, locale) }), byOf(filter.by)));
        }
        const acts = list(level.acts).filter((act) => text(act?.title) !== null || text(act?.act_id) !== null);
        // A reason code wrote ("named in the question") is said in the page's language; a model's own reason as it wrote it.
        if (acts.length > 0) rows.push(row('acts', '', 'code', { items: acts.map((act) => ({ title: actTitle(act.title) ?? act.act_id, reason: enginePhrase(act.reason, locale) ?? text(act.reason), by: byOf(act.by) })) }));
        // pages2: where the lines shown to the model came from, each source with its count (by code), and a text the model
        // named that is not in the list it was shown.
        const sources = objectOr(level.sources);
        if (sources !== null) {
          const said = Object.entries(sources).filter(([key, value]) => isCount(value) && value > 0 && Object.hasOwn(extra.sources, key))
            .map(([key, value]) => fillNext(extra.sourceItem, { source: extra.sources[key], count: groupedCount(value, locale) }));
          if (said.length > 0) rows.push(row('sources', fillNext(extra.sourcesLine, { sources: said.join(table.listSeparator) }), 'code'));
        }
        if (text(level.missing_text) !== null) rows.push(row('missing-text', fillNext(extra.missingText, { name: text(level.missing_text) }), 'model'));
        // repair-pages: what code made of that name (found in the catalogue or the store, or dropped).
        const resolved = objectOr(level.missing_text_resolved);
        if (resolved !== null) rows.push(row('missing-text-resolved', resolved.dropped === true ? table.contract3.missingTextDropped : actTitle(resolved.title) === null ? '' : fillNext(table.contract3.missingTextResolved, { title: actTitle(resolved.title) }), 'code'));
        // repair-pages (K-GEN-6, K-SCOPE-5): a name a model gave that matches no text of the catalogue or the store, dropped by code.
        for (const dropped of list(answer?.trace?.names_dropped).filter((entry) => text(entry?.name) !== null)) {
          rows.push(row('name-dropped', fillNext(table.contract3.namesDropped, { name: text(dropped.name) }), 'code'));
        }
        break;
      }
      // repair-pages (K-RETR-3): a part too big to read whole, opened at the article level: what was shown, what the model picked.
      case 'articles': {
        for (const group of list(level.groups).filter((entry) => objectOr(entry) !== null)) {
          if (![group.shown, group.articles].every(isCount)) continue;
          rows.push(row('group', fillNext(table.contract3.articlesShown, {
            shown: groupedCount(group.shown, locale), articles: groupedCount(group.articles, locale), node: text(group.node) ?? '',
            picked: groupedCount(isCount(group.picked) ? group.picked : 0, locale),
          }), 'code'));
        }
        if (isCount(level.at_most)) rows.push(row('at-most', fillNext(table.contract3.articlesAtMost, { count: groupedCount(level.at_most, locale) }), 'code'));
        const picked = list(level.picked).filter((entry) => isCount(entry?.article_pk));
        if (picked.length > 0) {
          rows.push(row('picked', '', byOf(level.by), {
            items: picked.map((entry) => ({ title: text(entry.label) ?? article(entry.article_pk), tally: null, reason: text(entry.reason), by: byOf(level.by) })),
          }));
        }
        break;
      }
      case 'document': {
        const title = titles.get(level.act_id) ?? text(level.act_id);
        if (title !== null) rows.push(row('of', fillNext(copy.documentOf, { title }), byOf(level.by)));
        for (const step of list(level.steps)) {
          if (!isCount(step?.depth)) continue;
          const shown = isCount(step.shown) ? countedForm(copy.entries, step.shown, locale) : null;
          const picked = list(step.picked).filter((pick) => text(pick?.heading) !== null)
            .map((pick) => ({ title: text(pick.heading), tally: isCount(pick.tally) ? fillNext(copy.tally, { count: groupedCount(pick.tally, locale) }) : null, reason: text(pick.reason), by: byOf(level.by) }));
          rows.push(row('step', shown === null ? '' : fillNext(copy.depth, { depth: groupedCount(step.depth, locale), shown }), byOf(level.by), { items: picked }));
        }
        const parts = list(level.final).filter((part) => text(part?.heading) !== null);
        if (parts.length > 0) {
          rows.push(row('parts', copy.partsRead, 'code', { items: parts.map((part) => ({ title: isCount(part.articles) ? fillNext(copy.part, { heading: text(part.heading), articles: countedForm(copy.partArticles, part.articles, locale) }) : text(part.heading), reason: null, by: 'code' })) }));
        }
        // The engine may write several stops joined ("leaf,list_node"): each is said.
        const stops = String(level.stopped ?? '').split(',').map((one) => one.trim()).filter((one) => one.length > 0)
          .map((one) => copy.stopped[one] ?? table.contract.engine.stopped[one] ?? null).filter((one) => one !== null);
        for (const stop of [...new Set(stops)]) rows.push(row('stopped', stop, 'code'));
        // pages2: where the headings came from (the outline table, or the version's own headings counted by code), and a
        // text held only from a later date than the one asked.
        if (Object.hasOwn(extra.outlineSource, level.outline_source ?? '')) rows.push(row('outline-source', extra.outlineSource[level.outline_source], 'code'));
        else if (typeof level.outline_source === 'string' && level.outline_source.startsWith('node table')) rows.push(row('outline-source', extra.outlineSource.node, 'code'));
        if (level.held_only_later === true) rows.push(row('held-later', extra.heldOnlyLater, 'code'));
        break;
      }
      case 'detectors': {
        // pages2: the whole-store searches of the combined pipeline, and the consolidated article holding an amended wording.
        const more = ['words_all', 'meaning_all', 'amended_text', 'dictionary', 'replacing_text'].filter((column) => list(level.candidates).some((candidate) => isCount(candidate?.found_by?.[column])));
        const columns = ['exact', 'outline', 'words', 'meaning', ...more];
        const candidates = list(level.candidates).filter((candidate) => isCount(candidate?.article_pk));
        if (candidates.length > 0) {
          rows.push(row('table', '', 'code', {
            table: {
              columns: ['article', ...columns].map((column) => copy.detectorColumns[column] ?? extra.detectorColumns[column]),
              rows: candidates.map((candidate) => ({
                key: String(candidate.article_pk),
                head: article(candidate.article_pk),
                cells: columns.map((column) => (isCount(candidate.found_by?.[column]) ? fillNext(copy.rank, { rank: groupedCount(candidate.found_by[column], locale) }) : copy.notFoundBy)),
                note: candidate.meaning_only === true ? copy.meaningOnly : candidate.amending === true ? extra.amending : null,
              })),
            },
          }));
        }
        // engine2: articles read in the wording their own dates give (a dated question), counted by code.
        if (isCount(level.wordings_by_article_dates) && level.wordings_by_article_dates > 0) rows.push(row('wordings', countedForm(extra.wordingsByDates, level.wordings_by_article_dates, locale), 'code'));
        for (const skipped of list(level.skipped)) {
          if (text(skipped?.detector) === null) continue;
          const detector = copy.detectorNames[skipped.detector] ?? skipped.detector;
          const why = enginePhrase(skipped.why, locale) ?? text(skipped.why);
          rows.push(row('skipped', why === null ? detector : fillNext(copy.skipped, { detector, why }), 'code'));
        }
        break;
      }
      case 'arbiter': {
        const candidates = list(level.candidates).filter((candidate) => isCount(candidate?.article_pk));
        if (candidates.length > 0) {
          rows.push(row('table', '', byOf(level.by), {
            table: {
              columns: ['article', 'role', 'reason'].map((column) => copy.arbiterColumns[column]),
              rows: candidates.map((candidate) => ({
                key: String(candidate.article_pk), head: article(candidate.article_pk),
                cells: [copy.arbiterRoles[candidate.role] ?? text(candidate.role) ?? '', readableReason(candidate.reason, answer) ?? ''], note: candidate.meaning_only === true ? copy.meaningOnly : null,
              })),
            },
          }));
        }
        break;
      }
      case 'read': {
        if (isCount(level.articles_read) && isCount(level.lines_quoted)) {
          const fixed = /^fixed k = ([0-9]+)$/u.exec(level.read_amount_set_by ?? '');
          // repair-pages: a reason the copy does not know is said as the engine wrote it on the English page, and in general words
          // on the French one (never English there).
          const setBy = copy.setBy[level.read_amount_set_by] ?? table.contract.engine.setBy[level.read_amount_set_by]
            ?? (fixed === null ? null : fillNext(table.contract.engine.fixedK, { count: groupedCount(Number(fixed[1]), locale) }))
            ?? (text(level.read_amount_set_by) === null ? null : locale === 'en' ? text(level.read_amount_set_by) : table.contract3.setByOther);
          if (setBy !== null) rows.push(row('read', fillNext(copy.read, { articles: countedForm(copy.readArticles, level.articles_read, locale), setBy, lines: countedForm(copy.readLines, level.lines_quoted, locale) }), byOf(level.by)));
        }
        // pages2: each round of reading, what it found (an answer, complete or not; what it said was missing), by the model.
        const rounds = list(level.rounds).filter((round) => objectOr(round) !== null);
        if (rounds.length > 0) {
          rows.push(row('rounds', countedForm(extra.rounds, rounds.length, locale), byOf(level.by), {
            items: rounds.map((round, at) => {
              const found = round.complete_answer_found === true ? extra.roundComplete : round.answer_found === true ? extra.roundPartial : extra.roundNone;
              const parts = list(round.parts_answered).filter((one) => typeof one === 'boolean');
              const answered = parts.length > 0 ? fillNext(extra.roundParts, { answered: groupedCount(parts.filter(Boolean).length, locale), parts: groupedCount(parts.length, locale) }) : null;
              return { title: fillNext(extra.round, { n: groupedCount(at + 1, locale), found }), reason: [answered, text(round.missing) === null ? null : fillNext(extra.roundMissing, { missing: text(round.missing) })].filter((one) => one !== null).join(' ') || null, by: 'model' };
            }),
          }));
        }
        break;
      }
      case 'references': {
        const trigger = copy.triggers[level.trigger] ?? table.contract.engine.triggers[level.trigger] ?? text(level.trigger);
        if (trigger !== null && isCount(level.budget)) rows.push(row('trigger', fillNext(copy.references, { trigger, budget: countedForm(copy.budget, level.budget, locale) }), byOf(level.by)));
        const hops = list(level.hops).filter((hop) => text(hop?.from) !== null && text(hop?.to) !== null);
        if (hops.length > 0) {
          rows.push(row('hops', '', byOf(level.by), {
            items: hops.map((hop) => ({ title: fillNext(hop.held === false ? copy.hopUnheld : copy.hop, { from: hop.from === 'missing' ? extra.fromMissing : text(hop.from), to: text(hop.to), why: text(hop.why) ?? text(hop.kind) ?? '' }).replace(/\s*:\s*$| :\s*$/u, ''), reason: hop.direction === 'inbound' ? extra.inboundHop : null, by: byOf(level.by) })),
          }));
        }
        // pages2: what the resolver refused (a pointer it could not tie, a text not held), counted; the texts not held it met;
        // the loops run.
        const refused = isCount(level.refused_count) ? level.refused_count : list(level.refused).length;
        if (refused > 0) rows.push(row('refused', countedForm(extra.refused, refused, locale), 'code'));
        const unheld = list(level.not_held).map((entry) => (typeof entry === 'string' ? text(entry) : text(entry?.name) ?? text(entry?.title) ?? text(entry?.act_id))).filter((name) => name !== null);
        if (unheld.length > 0) rows.push(row('not-held', '', 'code', { items: [...new Set(unheld)].map((name) => ({ title: fillNext(extra.refNotHeld, { name }), reason: null, by: 'code' })) }));
        if (isCount(level.loops) && level.loops > 0) rows.push(row('loops', countedForm(extra.loops, level.loops, locale), 'code'));
        break;
      }
      case 'decision':
        if (text(level.rule) !== null) rows.push(row('rule', fillNext(copy.decision, { rule: enginePhrase(level.rule, locale, 'rules') ?? text(level.rule) }), 'code'));
        break;
      // pages2: the check by code that a passage beside the answer applies on the date asked (engine2, 21/11/2026).
      case 'consistency': {
        const dropped = list(level.dropped);
        if (isCount(level.checked)) rows.push(row('checked', fillNext(extra.consistency, { checked: countedForm(extra.consistencyChecked, level.checked, locale), dropped: countedForm(extra.consistencyDropped, dropped.length, locale) }), 'code'));
        const rule = text(level.rule);
        const said = rule === null ? null : extra.consistencyRules[rule] ?? (locale === 'en' ? rule : null);
        if (said !== null) rows.push(row('rule', said, 'code'));
        if (dropped.length > 0) {
          rows.push(row('dropped', '', 'code', {
            items: dropped.map((entry) => {
              const name = typeof entry === 'string' ? text(entry) : article(entry?.article_pk) || text(entry?.label) || text(entry?.article) || text(entry?.act_title) || '';
              const why = typeof entry === 'object' && entry !== null ? enginePhrase(entry.why, locale) ?? notHeldWhy(entry.why, locale) ?? (locale === 'en' ? text(entry.why) : null) : null;
              return { title: name, reason: why, by: 'code' };
            }).filter((item) => item.title.length > 0),
          }));
        }
        break;
      }
      default:
        break;
    }
    // The searches side by side: by meaning (a model's vectors), by words and names (code), by the table of contents (a model).
    const by = key === 'detectors' && level.by === undefined ? 'both' : byOf(level.by);
    return { key: `${key}-${at}`, level: key, name: copy.levels[key] ?? extra.levels[key] ?? (key === 'articles' ? table.contract3.articlesLevel : key.replaceAll('_', ' ')), by, rows };
  });
}

/** The model calls, measured: each `{ step, model, time, tokens }`, and their count. */
export function callRows(answer, locale) {
  const table = nextCopy(locale);
  const copy = table.contract.trace;
  return list(answer?.trace?.calls).filter((call) => objectOr(call) !== null).map((call, at) => ({
    key: `${call.step ?? 'call'}-${at}`,
    step: text(call.step) ?? '',
    model: text(call.model) ?? '',
    time: isCount(call.ms) ? (call.ms < 1000 ? fillNext(table.milliseconds, { value: groupedCount(Math.round(call.ms), locale) }) : fillNext(table.seconds, { value: decimal(call.ms / 1000, locale, 1) })) : '',
    tokens: isCount(call.tokens_in) && isCount(call.tokens_out) ? fillNext(copy.tokens, { in: groupedCount(call.tokens_in, locale), out: groupedCount(call.tokens_out, locale) }) : '',
    // pages2: a retry, the fallback deployment, a failed call, the wait for the in-flight limit, each said (K-OPS-5).
    note: [
      call.failed === true ? table.contract2.calls.failed : null,
      call.fallback === true || (typeof call.fallback === 'string' && call.fallback.length > 0) ? table.contract2.calls.fallback : null,
      isCount(call.tries) && call.tries > 1 ? countedForm(table.contract2.calls.tries, call.tries, locale) : null,
      isCount(call.queue_ms) && call.queue_ms >= 100 ? fillNext(table.contract2.calls.queued, { value: decimal(call.queue_ms / 1000, locale, 1) }) : null,
    ].filter((one) => one !== null).join(table.listSeparator),
  }));
}

/** The versions of the routing lines and of the prompts the answer was built with (`meta.build.routing`, `meta.prompts.version`); null when unsaid. */
export function contractVersions(answer, locale) {
  const copy = nextCopy(locale).contract2.trace;
  const routing = text(answer?.meta?.build?.routing);
  const prompts = text(answer?.meta?.prompts?.version);
  const parts = [
    routing === null ? null : fillNext(copy.routing, { date: isIsoDate(routing.slice(0, 10)) ? numericDate(routing.slice(0, 10)) : routing }),
    prompts === null ? null : fillNext(copy.prompts, { version: prompts }),
  ].filter((one) => one !== null);
  return parts.length === 0 ? null : parts.join(nextCopy(locale).clauseSeparator);
}

/** The trace's foot line: the calls counted from the list of calls, the whole time, the cost at list price. Null when unknown. */
export function contractFoot(answer, locale) {
  const table = nextCopy(locale);
  const trace = objectOr(answer?.trace);
  if (trace === null) return null;
  // An answer shown again from its token: no model was called now; the first answer's own time and cost, as the token keeps them.
  const original = objectOr(trace.original);
  if (original !== null && isCount(original.ms) && isCount(original.cost_usd)) {
    const replayed = objectOr(answer?.meta?.replayed);
    const changed = replayed === null ? null : replayed.same_data === false ? table.contract.trace.replayedDataChanged : replayed.same_code === false ? table.contract.trace.replayedCodeChanged : null;
    return [fillNext(table.contract.trace.replayed, {
      seconds: fillNext(table.seconds, { value: decimal(original.ms / 1000, locale, 1) }),
      cost: fillNext(table.dock.costUsd, { value: decimal(original.cost_usd, locale) }),
    }), changed].filter((part) => part !== null).join(' ');
  }
  if (!Array.isArray(trace.calls) || !isCount(trace.ms) || !isCount(trace.cost_usd)) return null;
  return fillNext(table.contract.trace.foot, {
    calls: countedForm(table.dock.calls, trace.calls.length, locale),
    seconds: fillNext(table.seconds, { value: decimal(trace.ms / 1000, locale, 1) }),
    cost: fillNext(table.dock.costUsd, { value: decimal(trace.cost_usd, locale) }),
  });
}

/** The versions the answer was built with, as `meta.build` names them; null when it names none. */
export function contractBuild(answer, locale) {
  const build = objectOr(answer?.meta?.build);
  if (build === null || text(build.code) === null || text(build.data) === null) return null;
  return fillNext(nextCopy(locale).dock.build, { code: build.code, data: build.data });
}

/** The replay token the server can show the same answer again from, or null. */
export const replayToken = (answer) => text(answer?.meta?.replay);

/** The link that shows an answer again from its token: the home page of the language, the token after "#r=". */
export function replayTokenHref(token, locale) {
  const value = text(token);
  return value === null ? null : `/${locale}/#r=${encodeURIComponent(value)}`;
}

/** The token a page's fragment asks the dock to show again (`#r=...`), or null. */
export function replayTokenOf(hash) {
  const match = /^#r=([^&]+)$/u.exec(typeof hash === 'string' ? hash : '');
  if (match === null) return null;
  try {
    const token = decodeURIComponent(match[1]);
    return token.length >= 8 && token.length <= 200000 ? token : null;
  } catch {
    return null;
  }
}

/** The engine's address that shows an answer again from its token, and the request (the token in the body of a POST). */
export const REPLAY_ADDRESS = '/api/next/replay';
export function replayRequest(token) {
  return Object.freeze({
    address: REPLAY_ADDRESS,
    init: Object.freeze({ method: 'POST', headers: Object.freeze({ 'content-type': 'application/json', accept: 'application/json' }), body: JSON.stringify({ token }) }),
  });
}

/**
 * pages2: the address that says where the words of the versions an answer quotes come from (`GET /api/next/sources`), for
 * the blocks that do not say it themselves; null when every block says it, or none names its version.
 */
export function sourcesAddress(answer) {
  // repair-pages (22/11/2026): every version an answer quotes is asked about once, for the facts the data builders flag on a text
  // (held only in German, OCR, amounts in francs), which no block carries; a block's own `source` still comes first.
  const ids = [...new Set(list(answer?.blocks).filter((block) => isCount(block?.version?.id)).map((block) => block.version.id))].slice(0, 40);
  return ids.length === 0 ? null : `/api/next/sources?${ids.map((id) => `version=${id}`).join('&')}`;
}

/** The address that answers an article's lines and its section (to widen a quotation). */
export const contextAddress = (articlePk) => (isCount(articlePk) ? `/api/next/context?article_pk=${articlePk}` : null);

/**
 * The alinéa a line stands in, from its unit (`art_16/p2/a2/pt1` stands in `art_16/p2/a2`): the unit up to its first alinéa,
 * or up to its first paragraph when it has none; the whole article when the line has no unit.
 */
export function alineaOf(unit) {
  if (typeof unit !== 'string' || unit.length === 0) return '';
  const parts = unit.split('/');
  const alinea = parts.findIndex((part, at) => at > 0 && /^a[0-9]+$/u.test(part));
  if (alinea > 0) return parts.slice(0, alinea + 1).join('/');
  const paragraph = parts.findIndex((part, at) => at > 0 && /^p[0-9]+$/u.test(part));
  return paragraph > 0 ? parts.slice(0, paragraph + 1).join('/') : parts[0];
}

/**
 * A quotation widened: the lines to show for `level` ("alinea", "article" or "section") from the context the server
 * answered (`/api/next/context`), each `{ n, text, quoted }`, `quoted` true for a line the answer quotes. For a section,
 * a list of articles, each with its label and lines. Null when the context does not hold what is asked.
 */
export function widened(context, level, quotedNumbers) {
  const article = objectOr(context?.article);
  if (article === null) return null;
  const quoted = new Set(list(quotedNumbers).filter(isCount));
  const own = list(article.lines).filter((line) => isCount(line?.n) && text(line?.text) !== null);
  const mark = (line, mine = true) => ({ n: line.n, text: line.text, quoted: mine && quoted.has(line.n) });
  if (level === 'article') return { kind: 'article', lines: own.map((line) => mark(line)) };
  if (level === 'alinea') {
    const keys = new Set(own.filter((line) => quoted.has(line.n)).map((line) => alineaOf(line.unit)));
    if (keys.size === 0) return { kind: 'article', lines: own.map((line) => mark(line)) };
    return { kind: 'alinea', lines: own.filter((line) => keys.has(alineaOf(line.unit))).map((line) => mark(line)) };
  }
  if (level === 'section') {
    const section = objectOr(context.section);
    if (section === null) return null;
    return {
      kind: 'section',
      label: text(section.label),
      cut: section.cut === true,
      articles: list(section.articles).filter((entry) => objectOr(entry) !== null).map((entry) => ({
        key: String(entry.article_pk ?? entry.anchor ?? ''),
        label: text(entry.article) ?? '',
        heading: text(entry.heading),
        anchor: text(entry.anchor),
        lines: list(entry.lines).filter((line) => isCount(line?.n) && text(line?.text) !== null).map((line) => mark(line, entry.article_pk === article.article_pk)),
      })),
    };
  }
  return null;
}
