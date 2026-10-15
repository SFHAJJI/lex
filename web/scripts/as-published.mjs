// A Luxembourg act held only as first published, read from the answers that serve its states.
//
// The plan's item 0.6 (the specification of record's R76, R18 and R20): almost the whole Luxembourg population is
// held as the act was first published in the Journal officiel, with no consolidated text, and every surface that
// shows such an act says so. The platform says it in `as_published` on `as_of`, `evidence_bundle`, `timeline`,
// `dossier` and `diff` (null when a consolidated state of the work is held): the label, what the index holds about later
// versions (the consolidated versions the publisher's own facts name, and its end-of-force facts), and an
// authenticity sentence whose case is read from the publisher's publication date of the act as published. This
// file reads that member; it renders nothing.
//
// Every rule is one the answer states about itself, so an answer that breaks one is drift and is thrown:
//  - the label is the label, and the act as published is the work's Journal officiel legal resource (`{work}/jo`),
//    of which every state the answer lists is an expression;
//  - later versions are never held (the not-held row `later_versions`), and the sentence about them is the one the facts listed give (`laterVersionsSentence`,
//    the platform's own rule, held here word for word): with no fact, only that later versions are not held;
//  - each consolidation listed is a legal resource one segment below the work's `/consolide/`, dated by a civil date
//    or null, and the authenticity sentence is the one its case names, with the act as published in it;
//  - an answer with a not-held list carries the later-versions row exactly when it carries `as_published`.

import { isCalendarDate } from './temporal.mjs';

/** The label every surface shows on an act held only as published (R76), as the platform sends it. */
export const AS_PUBLISHED_LABEL = 'as published; may have been amended since; no consolidated text';

/** The not-held row an answer with a not-held list carries for later versions. */
export const LATER_VERSIONS_ITEM = 'later_versions';

/** What is said of later versions when the index holds no later-version or end-of-force fact. */
export const LATER_VERSIONS_NOT_HELD = 'later versions of this act are not held';

/**
 * The authenticity sentence for each case, as the platform words it, with `{act}` for the act as published. The case is
 * read from the publisher's publication date of the act as published: on or after 2017-01-01, before it, or not stated.
 * The wording is a draft pending the lawyer's read (the owner's item O4).
 */
export const AUTHENTICITY_SENTENCES = Object.freeze({
  electronic_journal_officiel:
    'This text was extracted and normalised by Lex, for information; it is not authentic. Since 1 January 2017, only the Journal officiel ' +
    'du Grand-Duché de Luxembourg (Mémorial A) as published electronically is authentic (loi du 23 décembre 2016, art. 5). The act as ' +
    'published: {act}.',
  printed_memorial:
    'This text was extracted and normalised by Lex, for information; it is not authentic. For an act published before 1 January 2017, the ' +
    "printed Mémorial is authentic, and the publisher's PDF is its digital reproduction. The act as published: {act}.",
  publication_date_not_stated:
    "This text was extracted and normalised by Lex, for information; it is not authentic. The publisher's record gives no single " +
    'publication date for this act, so Lex does not say which edition is authentic. The act as published: {act}.',
});

const DIGEST = /^[0-9a-f]{64}$/;

function requireOwn(object, key, where) {
  if (object === null || typeof object !== 'object' || !Object.hasOwn(object, key)) {
    throw new Error(`${where} does not carry ${key}`);
  }
  return object[key];
}

function requireText(value, where) {
  if (typeof value !== 'string' || value.trim().length === 0) throw new Error(`${where} is not a value this page can print`);
  return value;
}

function requireList(value, where) {
  if (!Array.isArray(value)) throw new Error(`${where} is a list, even an empty one`);
  return value;
}

function dateOrNull(value, where) {
  if (value !== null && !isCalendarDate(value)) throw new Error(`${where} is a civil date or null, not ${JSON.stringify(value)}`);
  return value;
}

/** One publisher fact as an answer serves it: its subject and its value, as written, with the digest of its evidence. */
function readFact(fact, where) {
  requireText(requireOwn(fact, 'subject_iri', where), `${where}.subject_iri`);
  const evidence = requireOwn(fact, 'evidence_sha256', where);
  if (typeof evidence !== 'string' || !DIGEST.test(evidence)) throw new Error(`${where}.evidence_sha256 is not a SHA-256 digest`);
  return requireText(requireOwn(fact, 'value', where), `${where}.value`);
}

function joinWithAnd(values) {
  return values.length === 1 ? values[0] : `${values.slice(0, -1).join(', ')} and ${values.at(-1)}`;
}

/**
 * What the platform says of later versions, from the facts it lists and nothing else: the consolidated versions the
 * publisher's record names (each `{applicabilityDate, endApplicabilityDate}`, either null) and the distinct
 * end-of-force values. The platform's own rule (`V3CorpusMount.LaterVersionsSentence`), word for word.
 */
export function laterVersionsSentence(consolidations, endOfForce) {
  let sentence = LATER_VERSIONS_NOT_HELD;
  if (consolidations.length > 0) {
    const dated = consolidations.filter((entry) => entry.applicabilityDate !== null)
      .map((entry) => (entry.endApplicabilityDate === null ? `from ${entry.applicabilityDate}` : `from ${entry.applicabilityDate} to ${entry.endApplicabilityDate}`));
    const undated = consolidations.length - dated.length;
    const named = consolidations.length === 1 ? '1 consolidated version of it' : `${consolidations.length} consolidated versions of it`;
    const parts = [];
    if (dated.length > 0) parts.push(`applicable ${joinWithAnd(dated)} (publisher)`);
    if (undated > 0) parts.push(dated.length === 0 ? 'with no single applicability date stated' : `and ${undated} with no single applicability date stated`);
    const whose = consolidations.length === 1 ? 'its' : 'their';
    sentence += `: the publisher's record names ${named}, ${parts.join(', ')}; this index holds none of ${whose} text`;
  }
  if (endOfForce.length > 0) {
    sentence += `; the publisher's record gives ${joinWithAnd(endOfForce)} as the act's end-of-force date (dateNoLongerInForce)`;
  }
  return sentence;
}

/**
 * Of what `as_published` lists, the facts dated between two dates asked, in either order (a comparison's
 * `later_version_facts_between_dates`; the review of #964): the consolidated versions whose single applicability date is
 * after the earlier date and on or before the later one, in their order, and the distinct end-of-force values that are
 * such a civil date. The platform's own rule (`V3CorpusMount.BetweenDates`).
 */
export function laterVersionFactsBetween(asPublished, dateFrom, dateTo) {
  const [after, onOrBefore] = dateFrom <= dateTo ? [dateFrom, dateTo] : [dateTo, dateFrom];
  const within = (date) => date !== null && isCalendarDate(date) && date > after && date <= onOrBefore;
  return Object.freeze({
    after,
    onOrBefore,
    consolidations: Object.freeze(asPublished.laterVersions.consolidations.filter((entry) => within(entry.applicabilityDate))),
    endOfForce: Object.freeze(asPublished.laterVersions.endOfForce.filter(within)),
  });
}

/**
 * What a comparison of an act held only as published says when both dates select one state (the review of #964): the
 * one text held (or, when its language holds another state too, the same text held), the act as published, with its
 * publisher applicability start, and that later versions are not held (or, beside another held state, that no
 * consolidated version is held); then the facts dated between the two dates, from
 * `laterVersionFactsBetween`. Never whether the text changed. The platform's own rule
 * (`V3CorpusMount.AsPublishedSameTextNote`), word for word.
 */
export function sameTextHeldNote(applicabilityDate, onlyStateHeld, between) {
  // With another state of the language held, "later versions are not held" would contradict its sides' next state.
  let note = `Both dates select ${onlyStateHeld ? 'the one text held' : 'the same text held'}, the act as published, applicable from ` +
    `${applicabilityDate} (publisher); ${onlyStateHeld ? 'later versions are not held' : 'no consolidated version is held'}`;
  const { after, onOrBefore, consolidations, endOfForce } = between;
  if (consolidations.length > 0) {
    const named = consolidations.length === 1
      ? '1 consolidated version of it whose applicability date falls'
      : `${consolidations.length} consolidated versions of it whose applicability dates fall`;
    const dated = consolidations.map((entry) => (entry.endApplicabilityDate === null
      ? `from ${entry.applicabilityDate}`
      : `from ${entry.applicabilityDate} to ${entry.endApplicabilityDate}`));
    note += `; the publisher's record names ${named} after ${after} and on or before ${onOrBefore}, applicable ${joinWithAnd(dated)} (publisher)`;
  }
  if (endOfForce.length > 0) {
    note += `; the publisher's record gives ${joinWithAnd(endOfForce)} as the act's end-of-force date (dateNoLongerInForce), which ` +
      `${endOfForce.length === 1 ? 'falls' : 'fall'} after ${after} and on or before ${onOrBefore}`;
  }
  return note;
}

/**
 * Reads `as_published` from one Luxembourg answer: null when the platform says a consolidated state of the work is held,
 * else a frozen view. `states` are the answer's listed states (each with `expressionIri` and `publisherLegalResourceIri`),
 * `workIri` the work's publisher IRI when the answer names it, and `notHeld` the answer's not-held rows when it has them.
 *
 * @returns {object|null}
 */
export function readAsPublished(answer, { states, workIri = null, notHeld = null }) {
  const where = 'as_published';
  const value = requireOwn(answer, 'as_published', 'this answer');
  const laterRows = notHeld === null ? [] : notHeld.filter((row) => row.item === LATER_VERSIONS_ITEM);
  if (value === null) {
    if (laterRows.length > 0) throw new Error('this answer says no act held only as published, and lists later versions as not held');
    return null;
  }

  if (requireOwn(value, 'label', where) !== AS_PUBLISHED_LABEL) {
    throw new Error(`${where}.label is ${JSON.stringify(value.label)}, not the label ${JSON.stringify(AS_PUBLISHED_LABEL)}`);
  }
  const journalOfficielIri = requireText(requireOwn(value, 'journal_officiel_iri', where), `${where}.journal_officiel_iri`);
  if (!journalOfficielIri.endsWith('/jo')) throw new Error(`${where}.journal_officiel_iri ${journalOfficielIri} is not a Journal officiel legal resource (/jo)`);
  const work = journalOfficielIri.slice(0, -'/jo'.length);
  if (workIri !== null && work !== workIri) throw new Error(`${where} names the act as published ${journalOfficielIri}, which is not the work ${workIri}'s`);
  for (const [index, state] of states.entries()) {
    if (state.publisherLegalResourceIri !== journalOfficielIri || !state.expressionIri.startsWith(`${journalOfficielIri}/`)) {
      throw new Error(`state ${index + 1} (${state.expressionIri}) is not an expression of the act as published, ${journalOfficielIri}`);
    }
  }

  const later = requireOwn(value, 'later_version_facts', where);
  const prefix = `${work}/consolide/`;
  const consolidations = Object.freeze(requireList(requireOwn(later, 'consolidations', `${where}.later_version_facts`), `${where}.later_version_facts.consolidations`)
    .map((entry, index) => {
      const at = `${where}.later_version_facts.consolidations[${index}]`;
      const iri = requireText(requireOwn(entry, 'legal_resource_iri', at), `${at}.legal_resource_iri`);
      if (!iri.startsWith(prefix) || iri.length === prefix.length || iri.slice(prefix.length).includes('/')) {
        throw new Error(`${at} is ${iri}, not a consolidated version of ${work}`);
      }
      return Object.freeze({
        legalResourceIri: iri,
        applicabilityDate: dateOrNull(requireOwn(entry, 'applicability_date', at), `${at}.applicability_date`),
        endApplicabilityDate: dateOrNull(requireOwn(entry, 'end_applicability_date', at), `${at}.end_applicability_date`),
      });
    }));
  const endOfForce = Object.freeze([...new Set(requireList(requireOwn(later, 'end_of_force', `${where}.later_version_facts`), `${where}.later_version_facts.end_of_force`)
    .map((fact, index) => readFact(fact, `${where}.later_version_facts.end_of_force[${index}]`)))]);
  const sentence = requireText(requireOwn(later, 'sentence', `${where}.later_version_facts`), `${where}.later_version_facts.sentence`);
  const expected = laterVersionsSentence(consolidations, endOfForce);
  if (sentence !== expected) throw new Error(`${where}.later_version_facts.sentence is not what its facts say: ${JSON.stringify(sentence)}, not ${JSON.stringify(expected)}`);
  if (notHeld !== null && (laterRows.length !== 1 || laterRows[0].reason !== sentence)) {
    throw new Error(`the not-held list carries the later-versions row exactly once, with its sentence, for an act held only as published`);
  }

  const authenticity = requireOwn(value, 'authenticity', where);
  const basis = requireOwn(authenticity, 'basis', `${where}.authenticity`);
  if (!Object.hasOwn(AUTHENTICITY_SENTENCES, basis)) throw new Error(`${where}.authenticity.basis ${JSON.stringify(basis)} is not one of ${Object.keys(AUTHENTICITY_SENTENCES).join(', ')}`);
  const authenticitySentence = AUTHENTICITY_SENTENCES[basis].replace('{act}', journalOfficielIri);
  if (requireOwn(authenticity, 'sentence', `${where}.authenticity`) !== authenticitySentence) {
    throw new Error(`${where}.authenticity.sentence is not the ${basis} sentence for ${journalOfficielIri}`);
  }
  const publicationDates = Object.freeze(requireList(requireOwn(authenticity, 'publication_dates', `${where}.authenticity`), `${where}.authenticity.publication_dates`)
    .map((fact, index) => readFact(fact, `${where}.authenticity.publication_dates[${index}]`)));
  const single = [...new Set(publicationDates)];
  const read = single.length === 1 && isCalendarDate(single[0])
    ? (single[0] >= '2017-01-01' ? 'electronic_journal_officiel' : 'printed_memorial')
    : 'publication_date_not_stated';
  if (read !== basis) throw new Error(`${where}.authenticity.basis is ${basis}, and the publication dates it lists (${JSON.stringify(publicationDates)}) make it ${read}`);
  const servedManifestations = Object.freeze(requireList(requireOwn(authenticity, 'served_manifestations', `${where}.authenticity`), `${where}.authenticity.served_manifestations`)
    .map((file, index) => {
      const at = `${where}.authenticity.served_manifestations[${index}]`;
      return Object.freeze({
        manifestationIri: requireText(requireOwn(file, 'manifestation_iri', at), `${at}.manifestation_iri`),
        formatIri: requireText(requireOwn(file, 'format_iri', at), `${at}.format_iri`),
        legalValues: Object.freeze(requireList(requireOwn(file, 'legal_values', at), `${at}.legal_values`).map((fact, factIndex) => readFact(fact, `${at}.legal_values[${factIndex}]`))),
      });
    }));

  return Object.freeze({
    label: AS_PUBLISHED_LABEL,
    journalOfficielIri,
    laterVersions: Object.freeze({ sentence, consolidations, endOfForce }),
    authenticity: Object.freeze({ basis, sentence: authenticitySentence, publicationDates, servedManifestations }),
  });
}
