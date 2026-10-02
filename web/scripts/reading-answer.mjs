// The V3 Luxembourg evidence bundle, read for the reading screen.
//
// The reading screen's renderer (`reading.mjs`) was written before V3 against provision records the
// platform does not send. The text of a state, with what a quotation needs, is the `evidence_bundle`
// answer (object type `evidence_bundle`, publisher `lu-legilux`). This file reads that answer, held
// to the one captured by driving the real handler (`schemas/v3-platform/answer-samples.json`), and
// turns it into one view a live screen can render. It renders nothing itself.
//
// Every rule here is one the answer states about itself, so an answer that breaks one is drift and
// is thrown, never rendered:
//  - text is served only under the admitting rights disposition, and every source says so;
//  - one state per served language, in the languages' ordinal order, the one that applies on the
//    date asked (from on or before it, to a next date after it or none), in the language asked when
//    one was asked;
//  - each state's coordinate and permalink name its work, date and digest, and each article's
//    permalink is the state's with the publisher's article id after `#`;
//  - each quoted article has text, in its state's language, whose UTF-8 length is the one stated,
//    read from one of the state's sources and pointing at its official source; its validity flag is
//    exactly "its own date is stated and differs from the state's", and the state's count is theirs;
//  - an article without text is named apart from the quoted ones, never served as a quote.
// The text digests are not recomputed here (the reader is synchronous and the browser's digest is
// not); `V3ReplayGuaranteesTests` recomputes them from the publisher's file, and the tests of this
// reader check the captured answer's digests with Node's own SHA-256.
//
// An EU work's bundle (publisher `eu-eurlex`) quotes the original wording the EU index holds, and has
// its own shape, read by `readEuropeEvidenceBundle`; `readEvidenceBundleAnswer` sends an answer to the
// reader its publisher names. Its rules, each the answer's own:
//  - it carries Decision 95's acknowledgement, exactly, and the authenticity statement, and holds no
//    consolidation;
//  - each wording is of the work, in a language it is held in (the one asked, when one was asked), in
//    the languages' order, dated by the date asked (the original wording answers only its own date),
//    and pinned: `/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}`, its coordinate the
//    same without the digest;
//  - every source was acquired; each quoted article has text in its wording's language, whose UTF-8
//    length is the one stated, read from one of the wording's sources, pointing at an official source,
//    with the wording's permalink and the provision after `#`, escaped as the platform escapes it.

import { isCalendarDate } from './temporal.mjs';
import { escapeProvision } from './search-answer.mjs';

/** The acknowledgement every EU text served carries (Decision 95). */
export const EUROPE_TEXT_ACKNOWLEDGEMENT = '© European Union, https://eur-lex.europa.eu';

const DIGEST = /^[0-9a-f]{64}$/;

/** The one rights disposition under which the platform serves text (`agreed_same_run_cc_by`). */
export const ADMITTING_RIGHTS_DISPOSITION = 'agreed_same_run_cc_by';

const encoder = new TextEncoder();

function requireOwn(object, key, where) {
  if (object === null || typeof object !== 'object' || !Object.hasOwn(object, key)) {
    throw new Error(`${where} does not carry ${key}`);
  }
  return object[key];
}

function requireText(value, where) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${where} is not a value this page can print`);
  }
  return value;
}

function requireTextOrNull(value, where) {
  return value === null ? null : requireText(value, where);
}

function requireDigest(value, where) {
  if (typeof value !== 'string' || !DIGEST.test(value)) {
    throw new Error(`${where} is not a SHA-256 digest: ${JSON.stringify(value)}`);
  }
  return value;
}

function requireCount(value, where) {
  if (!Number.isInteger(value) || value < 0) {
    throw new Error(`${where} is ${JSON.stringify(value)} rather than a count`);
  }
  return value;
}

function requireList(value, where) {
  if (!Array.isArray(value)) {
    throw new Error(`${where} is a list, even an empty one`);
  }
  return value;
}

function requireDate(value, where) {
  if (!isCalendarDate(value)) {
    throw new Error(`${where} is not a calendar date: ${JSON.stringify(value)}`);
  }
  return value;
}

function requireBoolean(value, where) {
  if (typeof value !== 'boolean') {
    throw new Error(`${where} is true or false, not ${JSON.stringify(value)}`);
  }
  return value;
}

function readArticle(article, index, state, where) {
  const at = `${where} article ${index + 1}`;
  const publisherId = requireText(requireOwn(article, 'publisher_id', at), `${at} publisher_id`);
  const text = requireText(requireOwn(article, 'text', at), `${at} text`);
  const textByteLength = requireCount(requireOwn(article, 'text_byte_length', at), `${at} text_byte_length`);
  if (encoder.encode(text).length !== textByteLength) {
    throw new Error(`${at} (${publisherId}) states ${textByteLength} bytes of text and carries ${encoder.encode(text).length}`);
  }
  const language = requireText(requireOwn(article, 'language', at), `${at} language`);
  if (language !== state.language) {
    throw new Error(`${at} (${publisherId}) is in ${language}, in a state held in ${state.language}`);
  }
  const permalink = requireText(requireOwn(article, 'article_permalink', at), `${at} article_permalink`);
  if (permalink !== `${state.permalink}#${publisherId}`) {
    throw new Error(`${at}'s permalink ${permalink} is not its state's permalink with its id`);
  }
  const bodySha256 = requireDigest(requireOwn(article, 'body_sha256', at), `${at} body_sha256`);
  if (!state.bodySha256s.includes(bodySha256)) {
    throw new Error(`${at} (${publisherId}) was read from a body that is not one of its state's sources`);
  }
  const officialSource = requireText(requireOwn(article, 'official_source', at), `${at} official_source`);
  if (officialSource !== state.publisherLegalResourceIri) {
    throw new Error(`${at} (${publisherId}) points at ${officialSource}, not at its state's official source`);
  }
  const validFrom = requireOwn(article, 'article_valid_from', at);
  if (validFrom !== null) requireDate(validFrom, `${at} article_valid_from`);
  const validityConflict = requireBoolean(requireOwn(article, 'validity_conflict', at), `${at} validity_conflict`);
  if (validityConflict !== (validFrom !== null && validFrom !== state.applicabilityDate)) {
    throw new Error(
      `${at} (${publisherId}) says validity_conflict is ${validityConflict} for its date ${validFrom} in a state of ${state.applicabilityDate}`,
    );
  }
  const notes = requireList(requireOwn(article, 'notes', at), `${at} notes`).map((note, noteIndex) => Object.freeze({
    marker: requireText(requireOwn(note, 'marker', `${at} note ${noteIndex + 1}`), `${at} note ${noteIndex + 1} marker`),
    text: requireText(requireOwn(note, 'text', `${at} note ${noteIndex + 1}`), `${at} note ${noteIndex + 1} text`),
  }));
  return Object.freeze({
    publisherId,
    publisherWid: requireTextOrNull(requireOwn(article, 'publisher_wid', at), `${at} publisher_wid`),
    articleIdentitySha256: requireDigest(requireOwn(article, 'article_identity_sha256', at), `${at} article_identity_sha256`),
    language,
    text,
    textByteLength,
    textSha256: requireDigest(requireOwn(article, 'text_sha256', at), `${at} text_sha256`),
    wordingSha256: requireDigest(requireOwn(article, 'wording_sha256', at), `${at} wording_sha256`),
    bodySha256,
    officialSource,
    validFrom,
    validityConflict,
    notes: Object.freeze(notes),
    permalink,
  });
}

function readState(state, index, context) {
  const where = `state ${index + 1}`;
  const applicabilityDate = requireDate(requireOwn(state, 'applicability_date', where), `${where} applicability_date`);
  if (applicabilityDate > context.date) {
    throw new Error(`${where} applies from ${applicabilityDate}, after the date asked (${context.date})`);
  }
  const next = requireOwn(state, 'next_applicability_date', where);
  if (next !== null && (requireDate(next, `${where} next_applicability_date`) <= context.date)) {
    throw new Error(`${where} is followed from ${next}, so it does not apply on the date asked (${context.date})`);
  }
  const stateSha256 = requireDigest(requireOwn(state, 'state_sha256', where), `${where} state_sha256`);
  const coordinate = requireText(requireOwn(state, 'stable_coordinate', where), `${where} stable_coordinate`);
  if (coordinate !== `/lu-legilux/${context.workKey}/${applicabilityDate}`) {
    throw new Error(`${where}'s stable coordinate ${coordinate} does not name its work and date`);
  }
  const permalink = requireText(requireOwn(state, 'permalink', where), `${where} permalink`);
  if (permalink !== `${coordinate}--${stateSha256}`) {
    throw new Error(`${where}'s permalink ${permalink} does not pin its coordinate and digest`);
  }

  const sources = requireList(requireOwn(state, 'sources', where), `${where} sources`).map((source, sourceIndex) => {
    const at = `${where} source ${sourceIndex + 1}`;
    const disposition = requireText(requireOwn(source, 'rights_disposition', at), `${at} rights_disposition`);
    if (disposition !== ADMITTING_RIGHTS_DISPOSITION) {
      throw new Error(`${at} was acquired under ${disposition}; text is served only under ${ADMITTING_RIGHTS_DISPOSITION}`);
    }
    const body = requireOwn(source, 'body_sha256', at);
    return Object.freeze({
      outcome: requireText(requireOwn(source, 'outcome', at), `${at} outcome`),
      bodySha256: body === null ? null : requireDigest(body, `${at} body_sha256`),
      bodyByteLength: requireOwn(source, 'body_byte_length', at),
      rightsDisposition: disposition,
    });
  });
  const bodySha256s = requireList(requireOwn(state, 'body_sha256s', where), `${where} body_sha256s`)
    .map((digest, digestIndex) => requireDigest(digest, `${where} body_sha256s[${digestIndex}]`));
  const expectedBodies = [...new Set(sources.map((source) => source.bodySha256).filter((digest) => digest !== null))].sort();
  if (JSON.stringify(bodySha256s) !== JSON.stringify(expectedBodies)) {
    throw new Error(`${where}'s body digests are not its sources' bodies`);
  }

  const partial = {
    language: requireText(requireOwn(state, 'language', where), `${where} language`),
    applicabilityDate,
    permalink,
    bodySha256s,
    publisherLegalResourceIri: requireText(
      requireOwn(state, 'publisher_legal_resource_iri', where), `${where} publisher_legal_resource_iri`),
  };
  const articles = requireList(requireOwn(state, 'articles', where), `${where} articles`)
    .map((article, articleIndex) => readArticle(article, articleIndex, partial, where));
  if (articles.length === 0) {
    throw new Error(`${where} quotes no article; a state with no text is refused text_not_available, not answered`);
  }
  const without = requireList(requireOwn(state, 'articles_without_text', where), `${where} articles_without_text`)
    .map((entry, entryIndex) => {
      const at = `${where} article without text ${entryIndex + 1}`;
      if (requireOwn(entry, 'reason', at) !== 'no_text_tokens') {
        throw new Error(`${at} is without text for no_text_tokens, the one reason the platform names`);
      }
      return Object.freeze({
        publisherId: requireText(requireOwn(entry, 'publisher_id', at), `${at} publisher_id`),
        articleIdentitySha256: requireDigest(requireOwn(entry, 'article_identity_sha256', at), `${at} article_identity_sha256`),
      });
    });
  const identities = new Set();
  for (const item of [...articles, ...without]) {
    if (identities.has(item.articleIdentitySha256)) {
      throw new Error(`${where} lists the article ${item.publisherId} twice, or both as quoted and as without text`);
    }
    identities.add(item.articleIdentitySha256);
  }

  const conflictCount = requireCount(requireOwn(state, 'validity_conflict_count', where), `${where} validity_conflict_count`);
  const counted = articles.filter((article) => article.validityConflict).length;
  if (conflictCount !== counted) {
    throw new Error(`${where}'s validity_conflict_count is ${conflictCount}, and ${counted} of its articles are flagged`);
  }

  return Object.freeze({
    ...partial,
    nextApplicabilityDate: next,
    stateSha256,
    stableCoordinate: coordinate,
    expressionIri: requireText(requireOwn(state, 'expression_iri', where), `${where} expression_iri`),
    ruleProfileSha256s: Object.freeze(requireList(requireOwn(state, 'rule_profile_sha256s', where), `${where} rule_profile_sha256s`)
      .map((digest, digestIndex) => requireDigest(digest, `${where} rule_profile_sha256s[${digestIndex}]`))),
    articleIdentitiesSha256: requireDigest(requireOwn(state, 'article_identities_sha256', where), `${where} article_identities_sha256`),
    sources: Object.freeze(sources),
    articles: Object.freeze(articles),
    articlesWithoutText: Object.freeze(without),
    articlesNotAdmitted: requireCount(requireOwn(state, 'articles_not_admitted', where), `${where} articles_not_admitted`),
    validityConflictCount: conflictCount,
    validityConflictRule: requireText(requireOwn(state, 'validity_conflict_rule', where), `${where} validity_conflict_rule`),
  });
}

/**
 * Reads one V3 Luxembourg `evidence_bundle` answer into the view a live reading screen renders.
 *
 * @param {object} answer the `result.value` of an evidence_bundle envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readEvidenceBundle(answer) {
  const where = 'this evidence bundle';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'lu-legilux') {
    throw new Error(`this evidence bundle is ${JSON.stringify(publisher)}'s; this reader reads the Luxembourg (lu-legilux) bundle only`);
  }
  const rightsDisposition = requireText(requireOwn(answer, 'rights_disposition', where), 'rights_disposition');
  if (rightsDisposition !== ADMITTING_RIGHTS_DISPOSITION) {
    throw new Error(`a bundle serves text only under ${ADMITTING_RIGHTS_DISPOSITION}, not ${rightsDisposition}`);
  }
  const workKey = requireText(requireOwn(answer, 'work_key', where), 'work_key');
  const date = requireDate(requireOwn(answer, 'requested_date', where), 'requested_date');
  const language = requireTextOrNull(requireOwn(answer, 'requested_language', where), 'requested_language');
  const availableLanguages = Object.freeze(requireList(requireOwn(answer, 'available_languages', where), 'available_languages')
    .map((item, index) => requireText(item, `available_languages[${index}]`)));
  if (language !== null && !availableLanguages.includes(language)) {
    throw new Error(`the language asked for, ${language}, is not one the work is held in`);
  }

  const states = requireList(requireOwn(answer, 'states', where), 'states')
    .map((state, index) => readState(state, index, { workKey, date }));
  if (states.length === 0) {
    throw new Error('a bundle is answered with the state that applies in each served language; none is refused, not answered');
  }
  const languages = new Set();
  for (const [index, state] of states.entries()) {
    if (languages.has(state.language)) throw new Error(`the bundle holds two states in ${state.language}; it selects one per language`);
    languages.add(state.language);
    if (index > 0 && state.language < states[index - 1].language) {
      throw new Error(`the state in ${state.language} follows the one in ${states[index - 1].language}; the states are in the languages' order`);
    }
    if (!availableLanguages.includes(state.language)) {
      throw new Error(`a state is in ${state.language}, which the work is not said to be held in`);
    }
    if (language !== null && state.language !== language) {
      throw new Error(`a state is in ${state.language}, and the bundle was asked in ${language}`);
    }
  }

  const items = new Set();
  const notHeld = Object.freeze(requireList(requireOwn(answer, 'not_held', where), 'not_held').map((row, index) => {
    const item = requireText(requireOwn(row, 'item', `not_held[${index}]`), `not_held[${index}].item`);
    if (items.has(item)) throw new Error(`not_held names ${item} twice`);
    items.add(item);
    return Object.freeze({ item, reason: requireText(requireOwn(row, 'reason', `not_held[${index}]`), `not_held[${index}].reason`) });
  }));
  const verifiedBy = requireOwn(answer, 'verified_by', where);

  return Object.freeze({
    identifier: requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    workKey,
    date,
    language,
    availableLanguages,
    rightsDisposition,
    rightsRule: requireText(requireOwn(answer, 'rights_rule', where), 'rights_rule'),
    states: Object.freeze(states),
    notHeld,
    scope: requireText(requireOwn(answer, 'scope', where), 'scope'),
    articlesNotAdmittedNote: requireText(requireOwn(answer, 'articles_not_admitted_note', where), 'articles_not_admitted_note'),
    corpusSha256: requireDigest(requireOwn(verifiedBy, 'corpus_sha256', 'verified_by'), 'verified_by.corpus_sha256'),
    indexSha256: requireDigest(requireOwn(verifiedBy, 'index_sha256', 'verified_by'), 'verified_by.index_sha256'),
    registrySha256: requireDigest(requireOwn(verifiedBy, 'registry_sha256', 'verified_by'), 'verified_by.registry_sha256'),
  });
}

function readEuropeWording(wording, index, { celex, date }) {
  const where = `wording ${index + 1}`;
  const expressionIri = requireText(requireOwn(wording, 'publisher_expression_id', where), `${where} publisher_expression_id`);
  const language = requireText(requireOwn(wording, 'language', where), `${where} language`);
  const wordingDate = requireDate(requireOwn(wording, 'wording_date', where), `${where} wording_date`);
  // A wording answers the dates from its own until the next wording held (the EU time view); it never answers a date
  // before its own. An index with no states serves only the original wording, on its own date, which this rule admits.
  if (wordingDate > date) {
    throw new Error(`${where} is the wording of ${wordingDate}, and the bundle was asked for ${date}; a wording never answers a date before its own`);
  }
  const kind = Object.hasOwn(wording, 'kind') ? requireText(wording.kind, `${where} kind`) : 'original_wording';
  if (kind !== 'original_wording' && kind !== 'consolidated_version') {
    throw new Error(`${where} is a ${JSON.stringify(kind)}; an EU wording is the original wording or a consolidated version`);
  }
  if (!Object.hasOwn(wording, 'kind') && wordingDate !== date) {
    throw new Error(`${where} is the wording of ${wordingDate}, and the bundle was asked for ${date}; the original wording answers only its own date`);
  }
  // Present on the time view's wordings: the next wording's date, or null for the latest held.
  const nextDate = Object.hasOwn(wording, 'next_date')
    ? (wording.next_date === null ? null : requireDate(wording.next_date, `${where} next_date`))
    : undefined;
  if (typeof nextDate === 'string' && !(date < nextDate)) {
    throw new Error(`${where} holds until ${nextDate}, and the bundle was asked for ${date}; the next wording answers that date`);
  }
  const wordingSha256 = requireDigest(requireOwn(wording, 'wording_sha256', where), `${where} wording_sha256`);
  const permalink = requireText(requireOwn(wording, 'permalink', where), `${where} permalink`);
  const expected = `/eu-eurlex/${celex}/${language}/${wordingDate}--${wordingSha256}`;
  if (permalink !== expected) {
    throw new Error(`${where} carries the permalink ${JSON.stringify(permalink)}, not the wording it pins (${expected})`);
  }
  const stableCoordinate = requireText(requireOwn(wording, 'stable_coordinate', where), `${where} stable_coordinate`);
  if (stableCoordinate !== `/eu-eurlex/${celex}/${language}/${wordingDate}`) {
    throw new Error(`${where} carries the coordinate ${JSON.stringify(stableCoordinate)}, not its permalink's without the digest`);
  }

  const sources = Object.freeze(requireList(requireOwn(wording, 'sources', where), `${where} sources`).map((source, at) => {
    const label = `${where} sources[${at}]`;
    const outcome = requireText(requireOwn(source, 'outcome', label), `${label}.outcome`);
    if (outcome !== 'acquired') throw new Error(`${label} is ${outcome}; text is quoted only from acquired sources`);
    return Object.freeze({
      objectRefSha256: requireDigest(requireOwn(source, 'object_ref_sha256', label), `${label}.object_ref_sha256`),
      outcome,
      bodySha256: requireDigest(requireOwn(source, 'body_sha256', label), `${label}.body_sha256`),
    });
  }));
  if (sources.length === 0) throw new Error(`${where} names no source, and a quotation needs the body it was read from`);
  const bodies = new Set(sources.map((source) => source.bodySha256));

  const articles = Object.freeze(requireList(requireOwn(wording, 'articles', where), `${where} articles`).map((article, at) => {
    const label = `${where} article ${at + 1}`;
    const publisherId = requireText(requireOwn(article, 'publisher_id', label), `${label} publisher_id`);
    const text = requireText(requireOwn(article, 'text', label), `${label} text`);
    const articleLanguage = requireText(requireOwn(article, 'language', label), `${label} language`);
    if (articleLanguage !== language) throw new Error(`${label} is in ${articleLanguage}, and its wording is in ${language}`);
    const textByteLength = requireCount(requireOwn(article, 'text_byte_length', label), `${label} text_byte_length`);
    if (encoder.encode(text).length !== textByteLength) {
      throw new Error(`${label} states ${textByteLength} bytes of text and carries ${encoder.encode(text).length}`);
    }
    const bodySha256 = requireDigest(requireOwn(article, 'body_sha256', label), `${label} body_sha256`);
    if (!bodies.has(bodySha256)) throw new Error(`${label} was read from a body its wording does not name as a source`);
    const articlePermalink = requireText(requireOwn(article, 'article_permalink', label), `${label} article_permalink`);
    if (articlePermalink !== `${permalink}#${escapeProvision(publisherId)}`) {
      throw new Error(`${label} carries the permalink ${JSON.stringify(articlePermalink)}, not its wording's with ${publisherId} after #`);
    }
    const heading = requireOwn(article, 'heading', label);
    if (typeof heading !== 'string') throw new Error(`${label} heading is not text`);
    return Object.freeze({
      articleIdentitySha256: requireDigest(requireOwn(article, 'article_identity_sha256', label), `${label} article_identity_sha256`),
      publisherId,
      heading,
      text,
      textSha256: requireDigest(requireOwn(article, 'text_sha256', label), `${label} text_sha256`),
      textByteLength,
      bodySha256,
      officialSource: requireText(requireOwn(article, 'official_source', label), `${label} official_source`),
      permalink: articlePermalink,
    });
  }));
  const ids = new Set();
  for (const article of articles) {
    if (ids.has(article.publisherId)) throw new Error(`${where} quotes ${article.publisherId} twice`);
    ids.add(article.publisherId);
  }
  const articlesWithoutText = Object.freeze(requireList(requireOwn(wording, 'articles_without_text', where), `${where} articles_without_text`).map((article, at) => {
    const label = `${where} articles_without_text[${at}]`;
    return Object.freeze({
      articleIdentitySha256: requireDigest(requireOwn(article, 'article_identity_sha256', label), `${label}.article_identity_sha256`),
      publisherId: requireText(requireOwn(article, 'publisher_id', label), `${label}.publisher_id`),
    });
  }));
  if (articles.length === 0) throw new Error(`${where} quotes no article; a wording with no text is refused, not answered`);

  return Object.freeze({ expressionIri, language, kind, wordingDate, nextDate, wordingSha256, permalink, stableCoordinate, sources, articles, articlesWithoutText });
}

/**
 * Reads one V3 EU `evidence_bundle` answer into the view a live reading screen renders: the original
 * wording of each held expression on its wording date, with the acknowledgement and authenticity
 * statement Decision 95 requires beside the text.
 *
 * @param {object} answer the `result.value` of an evidence_bundle envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readEuropeEvidenceBundle(answer) {
  const where = 'this evidence bundle';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'eu-eurlex') {
    throw new Error(`this evidence bundle is ${JSON.stringify(publisher)}'s; this reader reads the EU (eu-eurlex) bundle only`);
  }
  const acknowledgement = requireText(requireOwn(answer, 'acknowledgement', where), 'acknowledgement');
  if (acknowledgement !== EUROPE_TEXT_ACKNOWLEDGEMENT) {
    throw new Error(`EU text is served with the acknowledgement ${JSON.stringify(EUROPE_TEXT_ACKNOWLEDGEMENT)}, not ${JSON.stringify(acknowledgement)}`);
  }
  const consolidationsHeld = requireBoolean(requireOwn(answer, 'consolidations_held', where), 'consolidations_held');
  const celex = requireText(requireOwn(answer, 'celex', where), 'celex');
  const date = requireDate(requireOwn(answer, 'requested_date', where), 'requested_date');
  const language = requireTextOrNull(requireOwn(answer, 'requested_language', where), 'requested_language');
  const availableLanguages = Object.freeze(requireList(requireOwn(answer, 'available_languages', where), 'available_languages')
    .map((item, index) => requireText(item, `available_languages[${index}]`)));
  if (language !== null && !availableLanguages.includes(language)) {
    throw new Error(`the language asked for, ${language}, is not one the work is held in`);
  }

  const wordings = requireList(requireOwn(answer, 'wordings', where), 'wordings')
    .map((wording, index) => readEuropeWording(wording, index, { celex, date }));
  if (wordings.length === 0) throw new Error('a bundle is answered with at least one wording; none is refused, not answered');
  const expressions = new Set();
  for (const [index, wording] of wordings.entries()) {
    if (expressions.has(wording.expressionIri)) throw new Error(`the expression ${wording.expressionIri} is quoted twice`);
    expressions.add(wording.expressionIri);
    if (index > 0 && wording.language < wordings[index - 1].language) {
      throw new Error(`the wording in ${wording.language} follows the one in ${wordings[index - 1].language}; the wordings are in the languages' order`);
    }
    if (!availableLanguages.includes(wording.language)) throw new Error(`a wording is in ${wording.language}, which the work is not said to be held in`);
    if (language !== null && wording.language !== language) throw new Error(`a wording is in ${wording.language}, and the bundle was asked in ${language}`);
  }

  const items = new Set();
  const notHeld = Object.freeze(requireList(requireOwn(answer, 'not_held', where), 'not_held').map((row, index) => {
    const item = requireText(requireOwn(row, 'item', `not_held[${index}]`), `not_held[${index}].item`);
    if (items.has(item)) throw new Error(`not_held names ${item} twice`);
    items.add(item);
    return Object.freeze({ item, reason: requireText(requireOwn(row, 'reason', `not_held[${index}]`), `not_held[${index}].reason`) });
  }));

  return Object.freeze({
    publisher,
    identifier: requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    celex,
    publisherWorkIri: requireText(requireOwn(answer, 'publisher_work_id', where), 'publisher_work_id'),
    date,
    language,
    availableLanguages,
    acknowledgement,
    authenticity: requireText(requireOwn(answer, 'authenticity', where), 'authenticity'),
    consolidationsHeld,
    wordings: Object.freeze(wordings),
    rightsRule: requireText(requireOwn(answer, 'rights_rule', where), 'rights_rule'),
    dateRule: requireText(requireOwn(answer, 'date_rule', where), 'date_rule'),
    dateSemantics: requireText(requireOwn(answer, 'date_semantics', where), 'date_semantics'),
    digestRule: requireText(requireOwn(answer, 'digest_rule', where), 'digest_rule'),
    notHeld,
    scope: requireText(requireOwn(answer, 'scope', where), 'scope'),
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  });
}

/** Reads an evidence bundle with the reader its publisher names: Luxembourg's, or the EU's. */
export function readEvidenceBundleAnswer(answer) {
  return answer !== null && typeof answer === 'object' && answer.publisher === 'eu-eurlex'
    ? readEuropeEvidenceBundle(answer)
    : readEvidenceBundle(answer);
}
