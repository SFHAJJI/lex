// An EU act's titles and Official Journal references (`titles` and `official_journal`, beside the expressions of an EU
// dossier, as_of, evidence_bundle and search over an EU index /7; Release 3's item 1), and the EU acts a search that names no
// work found by their titles (`europe_titles`), read for the live screens. A title is the publisher's own, with its source:
// the publisher's metadata record (its title and short title) or the act's Formex package (its markup removed); an Official
// Journal reference is the package's own markup, read field by field, and the site composes its words here, never the
// platform. An answer over an older EU index carries neither member, and this reads it as null; a row that breaks the
// answer's own rules is drift and is thrown, never rendered. It renders nothing itself.

import { fillText } from './live-chrome.mjs';

/** The sources of a title, in the order an answer lists them, and the kind each gives. */
export const EUROPE_TITLE_SOURCES = Object.freeze({
  cellar_expression_title: 'title',
  cellar_expression_title_short: 'short_title',
  formex_title: 'title',
});

/** The kinds of an Official Journal reference: the act's own publication, or that of the act a consolidated version consolidates. */
export const EUROPE_REFERENCE_KINDS = Object.freeze(['act_publication', 'consolidated_act_publication']);

/** The number forms: series and issue number (before October 2023), or the act's own number (since). */
export const EUROPE_NUMBER_FORMS = Object.freeze(['series_number', 'act_number']);

const DIGEST = /^[0-9a-f]{64}$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const LANGUAGES = Object.freeze(['eng', 'fra']);

function reader(where) {
  const own = (holder, key, at) => {
    if (holder === null || typeof holder !== 'object' || Array.isArray(holder)) throw new Error(`${at} is not an object`);
    if (!Object.hasOwn(holder, key)) throw new Error(`${at} has no ${key}`);
    return holder[key];
  };
  const text = (holder, key, at) => {
    const found = own(holder, key, at);
    if (typeof found !== 'string' || found.length === 0) throw new Error(`${at}.${key} is not text`);
    return found;
  };
  const textOrNull = (holder, key, at) => {
    const found = own(holder, key, at);
    if (found !== null && (typeof found !== 'string' || found.length === 0)) throw new Error(`${at}.${key} is neither text nor null`);
    return found;
  };
  const list = (holder, key, at) => {
    const found = own(holder, key, at);
    if (!Array.isArray(found)) throw new Error(`${at}.${key} is not a list`);
    return found;
  };
  const digest = (holder, key, at) => {
    const found = text(holder, key, at);
    if (!DIGEST.test(found)) throw new Error(`${at}.${key} is not a digest`);
    return found;
  };
  const language = (holder, key, at) => {
    const found = text(holder, key, at);
    if (!LANGUAGES.includes(found)) throw new Error(`${at}.${key} is ${JSON.stringify(found)}, not English or French`);
    return found;
  };
  return { own, text, textOrNull, list, digest, language, where };
}

/**
 * The titles an EU answer carries, or null when it carries none (an answer over an EU index /6 or older).
 *
 * @returns {object|null} a frozen view `{rule, entries, withoutTitle}`, each entry `{expressionIri, workIri, language, kind,
 *   source, text, identity}`
 */
export function readEuropeTitles(answer, where) {
  if (!Object.hasOwn(answer, 'titles')) return null;
  const read = reader(where);
  const label = `${where} titles`;
  const value = answer.titles;
  const seen = new Set();
  const entries = Object.freeze(read.list(value, 'entries', label).map((row, index) => {
    const at = `${label}.entries[${index}]`;
    const source = read.text(row, 'source', at);
    if (!Object.hasOwn(EUROPE_TITLE_SOURCES, source)) throw new Error(`${at} is read from ${source}, which no title is`);
    const kind = read.text(row, 'kind', at);
    if (kind !== EUROPE_TITLE_SOURCES[source]) throw new Error(`${at} is a ${kind} from ${source}, which gives a ${EUROPE_TITLE_SOURCES[source]}`);
    const identity = read.digest(row, 'title_identity_sha256', at);
    if (seen.has(identity)) throw new Error(`${at} repeats a title the answer already said`);
    seen.add(identity);
    return Object.freeze({
      expressionIri: read.text(row, 'publisher_expression_id', at),
      workIri: read.text(row, 'publisher_work_id', at),
      language: read.language(row, 'language', at),
      kind,
      source,
      text: read.text(row, 'text', at),
      identity,
    });
  }));
  const withoutTitle = Object.freeze(read.list(value, 'expressions_without_title', label).map((item, index) => {
    if (typeof item !== 'string' || item.length === 0) throw new Error(`${label}.expressions_without_title[${index}] is not an expression`);
    if (entries.some((entry) => entry.expressionIri === item)) throw new Error(`${label} says ${item} holds no title, and lists one of it`);
    return item;
  }));
  return Object.freeze({ rule: read.text(value, 'rule', label), entries, withoutTitle });
}

/**
 * The Official Journal references an EU answer carries, or null when it carries none (an answer over an EU index /6 or older).
 *
 * @returns {object|null} a frozen view `{rule, references, withoutReference}`, each reference `{expressionIri, workIri,
 *   language, kind, numberForm, series, ojNumber, actNumber, year, firstPage, publicationDate, identity}`
 */
export function readEuropeOfficialJournal(answer, where) {
  if (!Object.hasOwn(answer, 'official_journal')) return null;
  const read = reader(where);
  const label = `${where} official_journal`;
  const value = answer.official_journal;
  const seen = new Set();
  const references = Object.freeze(read.list(value, 'references', label).map((row, index) => {
    const at = `${label}.references[${index}]`;
    const kind = read.text(row, 'kind', at);
    if (!EUROPE_REFERENCE_KINDS.includes(kind)) throw new Error(`${at} is a ${kind}, which no reference is`);
    const numberForm = read.text(row, 'number_form', at);
    if (!EUROPE_NUMBER_FORMS.includes(numberForm)) throw new Error(`${at} is numbered ${numberForm}, which no reference is`);
    const ojNumber = read.textOrNull(row, 'oj_number', at);
    const actNumber = read.textOrNull(row, 'act_number', at);
    if ((numberForm === 'series_number') !== (ojNumber !== null) || (numberForm === 'act_number') !== (actNumber !== null)) {
      throw new Error(`${at} is numbered ${numberForm} and gives the issue ${ojNumber} and the act ${actNumber}`);
    }
    const year = read.text(row, 'year', at);
    if (!/^\d{4}$/.test(year)) throw new Error(`${at}.year is ${JSON.stringify(year)}`);
    const publicationDate = read.textOrNull(row, 'publication_date', at);
    if (publicationDate !== null && !DATE.test(publicationDate)) throw new Error(`${at}.publication_date is ${JSON.stringify(publicationDate)}`);
    const identity = read.digest(row, 'reference_identity_sha256', at);
    if (seen.has(identity)) throw new Error(`${at} repeats a reference the answer already said`);
    seen.add(identity);
    read.digest(row, 'package_sha256', at);
    read.text(row, 'package_entry', at);
    return Object.freeze({
      expressionIri: read.text(row, 'publisher_expression_id', at),
      workIri: read.text(row, 'publisher_work_id', at),
      language: read.language(row, 'language', at),
      kind,
      numberForm,
      series: read.text(row, 'series', at),
      ojNumber,
      actNumber,
      year,
      firstPage: read.textOrNull(row, 'first_page', at),
      publicationDate,
      identity,
    });
  }));
  const withoutReference = Object.freeze(read.list(value, 'expressions_without_reference', label).map((item, index) => {
    if (typeof item !== 'string' || item.length === 0) throw new Error(`${label}.expressions_without_reference[${index}] is not an expression`);
    if (references.some((reference) => reference.expressionIri === item)) throw new Error(`${label} says ${item} holds no reference, and lists one of it`);
    return item;
  }));
  return Object.freeze({ rule: read.text(value, 'rule', label), references, withoutReference });
}

/**
 * The title shown for one expression: the publisher's metadata title, else its Formex package's; null when none is held or the
 * answer holds no titles.
 */
export function europeTitleOf(titles, expressionIri) {
  if (titles === null) return null;
  const own = titles.entries.filter((entry) => entry.expressionIri === expressionIri && entry.kind === 'title');
  return own.find((entry) => entry.source === 'cellar_expression_title') ?? own.find((entry) => entry.source === 'formex_title') ?? null;
}

/** The publisher's short titles of one expression, as served (one literal may name several). */
export function europeShortTitlesOf(titles, expressionIri) {
  if (titles === null) return [];
  return titles.entries.filter((entry) => entry.expressionIri === expressionIri && entry.kind === 'short_title');
}

/** The Official Journal reference of one expression's wording, or null (none held, or none in the answer). */
export function europeReferenceOf(officialJournal, expressionIri) {
  if (officialJournal === null) return null;
  const own = officialJournal.references.filter((reference) => reference.expressionIri === expressionIri);
  return own.find((reference) => reference.kind === 'act_publication') ?? own[0] ?? null;
}

/** A publication date as the Official Journal's references write it: "4.5.2016". */
export function journalDate(date) {
  const [year, month, day] = date.split('-');
  return `${Number(day)}.${Number(month)}.${year}`;
}

/**
 * A reference in its usual form, in the words of one language's table (`citation.europe.journal`): "OJ L 119, 4.5.2016,
 * p. 1", "OJ L, 2024/1689, 12.7.2024", "JO L 119 du 4.5.2016, p. 1". An issue number is written without its leading zeros
 * ("011" is 11), as the Official Journal prints it; a field the package does not state is left out, and a reference with no
 * publication date is said with its year.
 */
export function officialJournalWords(reference, words) {
  const journal = words.europe.journal;
  const series = reference.series;
  const dated = reference.publicationDate !== null;
  const date = dated ? journalDate(reference.publicationDate) : null;
  if (reference.numberForm === 'act_number') {
    return dated
      ? fillText(journal.act, { series, number: reference.actNumber, date })
      : fillText(journal.actUndated, { series, number: reference.actNumber });
  }
  const number = /^\d+$/.test(reference.ojNumber) ? String(Number(reference.ojNumber)) : reference.ojNumber;
  const page = reference.firstPage;
  if (dated) {
    return page === null ? fillText(journal.issueNoPage, { series, number, date }) : fillText(journal.issue, { series, number, date, page });
  }
  const year = reference.year;
  return page === null ? fillText(journal.issueUndatedNoPage, { series, number, year }) : fillText(journal.issueUndated, { series, number, year, page });
}

/**
 * The EU acts a search that names no work found by their titles (`europe_titles`, beside its `work_resolution`), or null when
 * the answer carries none (an EU index /6 or older, or a search that named a work).
 *
 * @returns {object|null} a frozen view `{outcome, acts, matching, rule}`, each act `{seedCelex, originalWorkIri, title,
 *   titleLanguage, titleKind, titleSource, matchReason, dossierIdentifier}`
 */
export function readEuropeTitleResolution(answer, where) {
  if (!Object.hasOwn(answer, 'europe_titles')) return null;
  const read = reader(where);
  const label = `${where} europe_titles`;
  const value = answer.europe_titles;
  if (read.text(value, 'retrieval_lane', label) !== 'eu_title_discovery') throw new Error(`${label} is lane eu_title_discovery`);
  const outcome = read.text(value, 'outcome', label);
  const acts = Object.freeze(read.list(value, 'acts', label).map((row, index) => {
    const at = `${label}.acts[${index}]`;
    const seedCelex = read.text(row, 'seed_celex', at);
    const matchReason = read.text(row, 'match_reason', at);
    if (!['whole_title', 'phrase_in_title'].includes(matchReason)) throw new Error(`${at} matched by ${matchReason}`);
    const titleSource = read.text(row, 'title_source', at);
    if (!Object.hasOwn(EUROPE_TITLE_SOURCES, titleSource)) throw new Error(`${at} matched a title from ${titleSource}`);
    const dossier = read.own(row, 'dossier', at);
    const dossierIdentifier = read.text(dossier, 'identifier', `${at}.dossier`);
    if (dossierIdentifier !== seedCelex) throw new Error(`${at} opens ${dossierIdentifier}'s dossier, and is ${seedCelex}`);
    return Object.freeze({
      seedCelex,
      originalWorkIri: read.textOrNull(row, 'original_work_id', at),
      title: read.text(row, 'matched_title', at),
      titleLanguage: read.language(row, 'matched_title_language', at),
      titleKind: read.text(row, 'title_kind', at),
      titleSource,
      matchReason,
      dossierIdentifier,
    });
  }));
  const expected = acts.length === 0 ? 'no_title_match' : acts.length === 1 ? 'one_act' : 'several_acts';
  if (outcome !== expected) throw new Error(`${label}.outcome is ${outcome}, and it lists ${acts.length} acts`);
  if (new Set(acts.map((act) => act.seedCelex)).size !== acts.length) throw new Error(`${label} lists an act twice`);
  return Object.freeze({ outcome, acts, matching: read.text(value, 'matching', label), rule: read.text(value, 'rule', label) });
}
