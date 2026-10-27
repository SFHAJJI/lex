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
//  - each state's coordinate and permalink name its work, date and digest, the permalink its language too
//    (R64), and each article's permalink is the state's with the publisher's article id as a last segment;
//  - each quoted article has text, in its state's language, whose UTF-8 length is the one stated,
//    read from one of the state's sources and pointing at its official source; its validity flag is
//    exactly "its own date is stated and differs from the state's", and the state's count is theirs;
//  - an article without text is named apart from the quoted ones, never served as a quote;
//  - an article whose wording holds quoted wording or wording the publisher marks deleted (content /3) carries its
//    segments: text segments that concatenate to exactly its text, quotation boundaries that pair, and deleted
//    wording, which is never part of the text; a note whose reference the publisher wrote without a marker says so
//    (`unmarked`) and has no marker;
//  - a state is read one page at a time (the plan's item 3.7): its page, its index of pages and, when asked, its contents
//    hold to the rules `reading-pages.mjs` reads them by, and every article carries its position in the state.
// The text digests are not recomputed here (the reader is synchronous and the browser's digest is
// not); `V3ReplayGuaranteesTests` recomputes them from the publisher's file, and the tests of this
// reader check the captured answer's digests with Node's own SHA-256.
//
// An EU work's bundle (publisher `eu-eurlex`) quotes the original wording the EU index holds, and has
// its own shape, read by `readEuropeEvidenceBundle`; `readEvidenceBundleAnswer` sends an answer to the
// reader its publisher names. Its rules, each the answer's own:
//  - it carries Decision 95's acknowledgement, exactly, and the authenticity statement its act's own dates choose (the
//    owner's decision of 2026-10-18, O4: the electronic edition's for an act dated on or after 1 July 2013, the dated
//    sentence otherwise), the platform's word for word, and holds no consolidation;
//  - each wording is of the work, in a language it is held in (the one asked, when one was asked), in
//    the languages' order, dated by the date asked (the original wording answers only its own date),
//    and pinned: `/eu-eurlex/{celex}/{wording date}--{wording sha256}/{language}` (R64), its coordinate
//    `/eu-eurlex/{celex}/{language}/{wording date}`;
//  - every source was acquired; each quoted article has text in its wording's language, whose UTF-8
//    length is the one stated, read from one of the wording's sources, pointing at an official source,
//    with the wording's permalink and the provision as a last segment, escaped as the platform escapes it.

import { isCalendarDate } from './temporal.mjs';
import { articlePermalink, europePermalink, luxembourgPermalink } from './permalinks.mjs';
import { readAnnexesNotServed } from './europe-annexes.mjs';
import { readEuropeForceDates } from './europe-force-dates.mjs';
import { readAsPublished } from './as-published.mjs';
import { readVersionPages } from './reading-pages.mjs';
import { readCatalogueFacts, readInterval } from './date-role.mjs';

/** The acknowledgement every EU text served carries (Decision 95). */
export const EUROPE_TEXT_ACKNOWLEDGEMENT = '© European Union, https://eur-lex.europa.eu';

/**
 * The first day on which only the electronic edition of the Official Journal is authentic: Regulation (EU) No 216/2013
 * applies from it (the specification of record's R20).
 */
export const EUROPE_ELECTRONIC_EDITION_FROM = '2013-07-01';

/**
 * The authenticity statement every EU text served carries, for each basis, as the platform words it
 * (`V3CorpusMount.EuropeTextAuthenticity`, in the owner's words of 2026-10-18 on PR #975's question, item O4). The basis is
 * read from the act's own dates the answer lists (`authenticity_act_dates`; the index holds no Official Journal
 * publication date): `electronic_edition` when one is listed and the earliest is on or after 1 July 2013, and
 * `edition_by_publication_date` otherwise, whose sentence states which edition is authentic on either side of that day.
 */
export const EUROPE_AUTHENTICITY_SENTENCES = Object.freeze({
  electronic_edition:
    'Only the Official Journal of the European Union published in electronic form is authentic and produces legal effects (Regulation (EU) ' +
    "No 216/2013, Article 1(2)); this text is a reproduction read from the Publications Office's Formex package, not the authentic edition.",
  edition_by_publication_date:
    'For an act published before 1 July 2013, the printed Official Journal of the European Union is authentic; the electronic edition is ' +
    "authentic since that date (Regulation (EU) No 216/2013). This text is a reproduction read from the Publications Office's Formex " +
    'package, not the authentic edition.',
});

/** The basis an act's own dates choose, as the platform's rule chooses it: the earliest decides, and none is the dated sentence. */
export function europeAuthenticityBasis(actDates) {
  const earliest = [...actDates].sort()[0];
  return earliest !== undefined && earliest >= EUROPE_ELECTRONIC_EDITION_FROM ? 'electronic_edition' : 'edition_by_publication_date';
}

/**
 * An EU bundle's authenticity statement: its basis, one of the two, chosen by the act's dates it lists (in order, once each,
 * each a calendar date), and its sentence, the platform's for that basis word for word.
 */
function readEuropeAuthenticity(answer, where) {
  const basis = requireOwn(answer, 'authenticity_basis', where);
  if (!Object.hasOwn(EUROPE_AUTHENTICITY_SENTENCES, basis)) {
    throw new Error(`authenticity_basis ${JSON.stringify(basis)} is not one of ${Object.keys(EUROPE_AUTHENTICITY_SENTENCES).join(', ')}`);
  }
  const actDates = Object.freeze(requireList(requireOwn(answer, 'authenticity_act_dates', where), 'authenticity_act_dates')
    .map((date, index) => requireDate(date, `authenticity_act_dates[${index}]`)));
  for (const [index, date] of actDates.entries()) {
    if (index > 0 && date <= actDates[index - 1]) throw new Error('authenticity_act_dates lists each date once, in order');
  }
  const read = europeAuthenticityBasis(actDates);
  if (read !== basis) {
    throw new Error(`authenticity_basis is ${basis}, and the act's dates it lists (${JSON.stringify(actDates)}) make it ${read}`);
  }
  const sentence = requireText(requireOwn(answer, 'authenticity', where), 'authenticity');
  if (sentence !== EUROPE_AUTHENTICITY_SENTENCES[basis]) throw new Error(`authenticity is not the platform's ${basis} statement`);
  return Object.freeze({
    sentence,
    basis,
    actDates,
    rule: requireText(requireOwn(answer, 'authenticity_rule', where), 'authenticity_rule'),
  });
}

const DIGEST = /^[0-9a-f]{64}$/;

/** The rights disposition under which every corpus serves text: the two channels of one run agreeing CC BY 4.0. */
export const ADMITTING_RIGHTS_DISPOSITION = 'agreed_same_run_cc_by';

/**
 * The rights dispositions under which the platform serves Luxembourg text: the two channels agreeing CC BY 4.0, in the same
 * run (`agreed_same_run_cc_by`) or as in-file reading /2 reads the file's own block in a re-derivation from custody
 * (`agreed_cc_by_rederived`, served by a corpus whose admission policy is A). The API holds each corpus to its own policy;
 * this page refuses anything that is not one of these two agreements.
 */
export const ADMITTING_RIGHTS_DISPOSITIONS = Object.freeze([ADMITTING_RIGHTS_DISPOSITION, 'agreed_cc_by_rederived']);
const ADMITTING_SAID = ADMITTING_RIGHTS_DISPOSITIONS.join(' or ');

/**
 * The single-channel dispositions a corpus serves only under one of the owner's rulings (L1, L1b, L2): the file does not
 * state its own licence, and the text rests on the publisher's catalogue declaration alone. A source of one is read only
 * with its `rights_disclosure` (the licence page it rests on and when it was observed), which the page shows beside the text.
 */
export const DISCLOSED_RIGHTS_DISPOSITIONS = Object.freeze([
  'sparql_cc_by_in_file_names_other_identity',
  'sparql_cc_by_in_file_own_block_empty',
  'sparql_cc_by_in_file_silent',
]);

/** The disclosure codes, one per single-channel disposition, as the API sends them. */
const DISCLOSURE_CODES = Object.freeze({
  sparql_cc_by_in_file_names_other_identity: 'lu_rights_publisher_catalogue_only_file_names_other_identity',
  sparql_cc_by_in_file_own_block_empty: 'lu_rights_publisher_catalogue_only_own_block_empty',
  sparql_cc_by_in_file_silent: 'lu_rights_publisher_catalogue_only_file_silent',
});

function readDisclosure(source, disposition, at) {
  if (!Object.hasOwn(source, 'rights_disclosure')) {
    if (DISCLOSED_RIGHTS_DISPOSITIONS.includes(disposition)) {
      throw new Error(`${at} was served under ${disposition} without its rights_disclosure`);
    }
    return null;
  }
  if (!DISCLOSED_RIGHTS_DISPOSITIONS.includes(disposition)) {
    throw new Error(`${at} carries a rights_disclosure under ${disposition}, which needs none`);
  }
  const disclosure = requireOwn(source, 'rights_disclosure', at);
  const code = requireText(requireOwn(disclosure, 'code', `${at} rights_disclosure`), `${at} rights_disclosure code`);
  if (code !== DISCLOSURE_CODES[disposition]) {
    throw new Error(`${at}'s disclosure ${code} is not ${disposition}'s`);
  }
  const live = requireOwn(disclosure, 'licence_page_observed_live', `${at} rights_disclosure`);
  if (typeof live !== 'boolean') throw new Error(`${at}'s disclosure does not say whether its licence page was observed live`);
  return Object.freeze({
    code,
    licencePageSha256: requireDigest(requireOwn(disclosure, 'licence_page_sha256', `${at} rights_disclosure`), `${at} licence_page_sha256`),
    observedOn: requireDate(requireOwn(disclosure, 'licence_page_observed_on', `${at} rights_disclosure`), `${at} licence_page_observed_on`),
    observedLive: live,
  });
}

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

function requirePosition(value, where) {
  if (!Number.isInteger(value) || value < 1) {
    throw new Error(`${where} is ${JSON.stringify(value)} rather than a position in its version, from 1`);
  }
  return value;
}

function requireBoolean(value, where) {
  if (typeof value !== 'boolean') {
    throw new Error(`${where} is true or false, not ${JSON.stringify(value)}`);
  }
  return value;
}

/** The quotation elements the publisher writes (content /3), which a quotation segment names. */
const QUOTATIONS = new Set(['embeddedText', 'quotedText', 'embeddedStructure', 'quotedStructure']);

/**
 * An article's segments (content /3), or null when it carries none: its text (`text`), quotation boundaries
 * (`quotation_start`, `quotation_end`, each naming its element, whether the publisher writes the marks in the wording and
 * the publisher's own mark when it gives one) and deleted wording (`deleted_text`). The text segments concatenate to
 * exactly the article's text, so what is quoted, cited and digested is never changed by them, and deleted wording is never
 * part of it.
 */
function readSegments(article, text, at) {
  if (!Object.hasOwn(article, 'segments')) return null;
  const segments = requireList(article.segments, `${at} segments`);
  if (segments.length === 0) throw new Error(`${at} carries segments and none of them`);
  const open = [];
  let inForce = '';
  let deleted = 0;
  const read = segments.map((segment, segmentIndex) => {
    const where = `${at} segment ${segmentIndex + 1}`;
    const kind = requireOwn(segment, 'kind', where);
    if (kind === 'text' || kind === 'deleted_text') {
      const value = requireOwn(segment, 'text', where);
      if (typeof value !== 'string' || value.length === 0) throw new Error(`${where} is ${kind} with no text`);
      if (kind === 'text') inForce += value;
      else deleted += 1;
      return Object.freeze({ kind, text: value });
    }
    if (kind === 'quotation_start' || kind === 'quotation_end') {
      const quotation = requireOwn(segment, 'quotation', where);
      if (!QUOTATIONS.has(quotation)) throw new Error(`${where} names ${JSON.stringify(quotation)}, not a quotation element`);
      const marksInWording = requireBoolean(requireOwn(segment, 'marks_in_wording', where), `${where} marks_in_wording`);
      const marker = requireOwn(segment, 'marker', where);
      if (marker !== null && (typeof marker !== 'string' || marker.length === 0)) throw new Error(`${where} carries a marker that is not a mark`);
      if (kind === 'quotation_start') open.push({ quotation, marksInWording });
      else {
        const start = open.pop();
        if (start === undefined || start.quotation !== quotation || start.marksInWording !== marksInWording) {
          throw new Error(`${where} closes a quotation it did not open`);
        }
      }
      return Object.freeze({ kind, quotation, marksInWording, marker });
    }
    throw new Error(`${where} is ${JSON.stringify(kind)}, not a segment of wording`);
  });
  if (open.length > 0) throw new Error(`${at} leaves a quotation open`);
  if (inForce !== text) throw new Error(`${at}'s text segments are not its text`);
  return Object.freeze({ segments: Object.freeze(read), deleted });
}

function readNote(note, at) {
  if (Object.hasOwn(note, 'unmarked')) {
    if (note.unmarked !== true) throw new Error(`${at} says unmarked as ${JSON.stringify(note.unmarked)}`);
    if (requireOwn(note, 'marker', at) !== null) throw new Error(`${at} is unmarked and carries a marker`);
    return Object.freeze({ marker: null, unmarked: true, text: requireText(requireOwn(note, 'text', at), `${at} text`) });
  }
  return Object.freeze({
    marker: requireText(requireOwn(note, 'marker', at), `${at} marker`),
    text: requireText(requireOwn(note, 'text', at), `${at} text`),
  });
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
  if (permalink !== articlePermalink(state.permalink, publisherId)) {
    throw new Error(`${at}'s permalink ${permalink} is not its state's permalink with its id as a last segment`);
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
  const notes = requireList(requireOwn(article, 'notes', at), `${at} notes`).map((note, noteIndex) => readNote(note, `${at} note ${noteIndex + 1}`));
  const wording = readSegments(article, text, at);
  return Object.freeze({
    position: requirePosition(requireOwn(article, 'position', at), `${at} position`),
    documentPosition: readDocumentPosition(article, at),
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
    segments: wording === null ? null : wording.segments,
    holdsDeletedWording: wording !== null && wording.deleted > 0,
    permalink,
  });
}

/** An article's place in its publisher document (index 9), or null when the answer names none. */
function readDocumentPosition(article, at) {
  return Object.hasOwn(article, 'document_position') ? requirePosition(article.document_position, `${at} document_position`) : null;
}

/**
 * The runs of its publisher document's places a state holds no article at (index 9's `missing_articles`), each with the
 * articles it lies between (`afterPublisherId` null at the document's start), or none when the answer names none. A gap is said
 * where it is, never skipped in silence (S7a).
 */
function readMissingArticles(state, where) {
  if (!Object.hasOwn(state, 'missing_articles')) return Object.freeze([]);
  return Object.freeze(requireList(state.missing_articles, `${where} missing_articles`).map((gap, gapIndex) => {
    const at = `${where} missing_articles[${gapIndex}]`;
    const from = requirePosition(requireOwn(gap, 'from_position', at), `${at}.from_position`);
    const to = requirePosition(requireOwn(gap, 'to_position', at), `${at}.to_position`);
    const count = requirePosition(requireOwn(gap, 'count', at), `${at}.count`);
    if (to < from || count !== to - from + 1) throw new Error(`${at} runs from ${from} to ${to} and counts ${count}`);
    return Object.freeze({
      fromPosition: from,
      toPosition: to,
      count,
      afterPublisherId: requireTextOrNull(requireOwn(gap, 'after_publisher_id', at), `${at}.after_publisher_id`),
      beforePublisherId: requireText(requireOwn(gap, 'before_publisher_id', at), `${at}.before_publisher_id`),
    });
  }));
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
  const language = requireText(requireOwn(state, 'language', where), `${where} language`);
  if (permalink !== luxembourgPermalink({ workKey: context.workKey, date: applicabilityDate, stateSha256, language })) {
    throw new Error(`${where}'s permalink ${permalink} does not pin its coordinate, digest and language`);
  }

  const sources = requireList(requireOwn(state, 'sources', where), `${where} sources`).map((source, sourceIndex) => {
    const at = `${where} source ${sourceIndex + 1}`;
    const disposition = requireText(requireOwn(source, 'rights_disposition', at), `${at} rights_disposition`);
    if (!ADMITTING_RIGHTS_DISPOSITIONS.includes(disposition) && !DISCLOSED_RIGHTS_DISPOSITIONS.includes(disposition)) {
      throw new Error(`${at} was acquired under ${disposition}; text is served only under ${ADMITTING_SAID}`);
    }
    const disclosure = readDisclosure(source, disposition, at);
    const body = requireOwn(source, 'body_sha256', at);
    return Object.freeze({
      outcome: requireText(requireOwn(source, 'outcome', at), `${at} outcome`),
      bodySha256: body === null ? null : requireDigest(body, `${at} body_sha256`),
      bodyByteLength: requireOwn(source, 'body_byte_length', at),
      rightsDisposition: disposition,
      disclosure,
    });
  });
  const bodySha256s = requireList(requireOwn(state, 'body_sha256s', where), `${where} body_sha256s`)
    .map((digest, digestIndex) => requireDigest(digest, `${where} body_sha256s[${digestIndex}]`));
  const expectedBodies = [...new Set(sources.map((source) => source.bodySha256).filter((digest) => digest !== null))].sort();
  if (JSON.stringify(bodySha256s) !== JSON.stringify(expectedBodies)) {
    throw new Error(`${where}'s body digests are not its sources' bodies`);
  }

  const partial = {
    language,
    applicabilityDate,
    permalink,
    bodySha256s,
    publisherLegalResourceIri: requireText(
      requireOwn(state, 'publisher_legal_resource_iri', where), `${where} publisher_legal_resource_iri`),
    // The work's ELI, which names the act's type and date (the citation generator's act name, `citation.mjs`).
    publisherWorkIri: requireText(requireOwn(state, 'publisher_work_iri', where), `${where} publisher_work_iri`),
  };
  const articles = requireList(requireOwn(state, 'articles', where), `${where} articles`)
    .map((article, articleIndex) => readArticle(article, articleIndex, partial, where));
  const without = requireList(requireOwn(state, 'articles_without_text', where), `${where} articles_without_text`)
    .map((entry, entryIndex) => {
      const at = `${where} article without text ${entryIndex + 1}`;
      if (requireOwn(entry, 'reason', at) !== 'no_text_tokens') {
        throw new Error(`${at} is without text for no_text_tokens, the one reason the platform names`);
      }
      return Object.freeze({
        position: requirePosition(requireOwn(entry, 'position', at), `${at} position`),
        documentPosition: readDocumentPosition(entry, at),
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
  // The page served, the state's pages and its contents (a state with no text at all is refused text_not_available, so it
  // quotes at least one article, on some page).
  const paged = readVersionPages(state, { quoted: articles, withoutText: without, headings: false }, where);
  if (conflictCount > paged.page.validityConflictsInVersion) {
    throw new Error(`${where}'s page flags ${conflictCount} articles, and its state ${paged.page.validityConflictsInVersion}`);
  }

  return Object.freeze({
    ...partial,
    // The date's role, the act's entry into force, the state's kind and the French expression a translation takes its interval
    // from (index 9; null on an index of schema 8 or 6): R21, the caveat and the translation label, `date-role.mjs`.
    ...readCatalogueFacts(state, applicabilityDate, where),
    // The interval's end on index 9 (`endKnown`), which says whether the state answers today (`holdsToday`).
    ...readInterval(state, where),
    missingArticles: readMissingArticles(state, where),
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
    ...paged,
  });
}

/** The page a bundle was asked for (`requested_page`, `requested_article`, never both) and the platform's paging rule. */
function readPageRequest(answer, where) {
  const page = requireOwn(answer, 'requested_page', where);
  if (page !== null && (!Number.isInteger(page) || page < 1)) throw new Error(`requested_page is ${JSON.stringify(page)}, a page number from 1 or null`);
  const article = requireTextOrNull(requireOwn(answer, 'requested_article', where), 'requested_article');
  if (page !== null && article !== null) throw new Error('a bundle is asked one page, by its number or by an article, not both');
  return { requestedPage: page, requestedArticle: article, pageRule: requireText(requireOwn(answer, 'page_rule', where), 'page_rule') };
}

/** One publisher fact's value as an answer serves it (`subject_iri`, `value`, `evidence_sha256`), when its subject is `subject`. */
function readFactOf(fact, subject, where) {
  if (requireText(requireOwn(fact, 'subject_iri', where), `${where}.subject_iri`) !== subject) {
    throw new Error(`${where} is a fact of ${fact.subject_iri}, not of ${subject}`);
  }
  requireDigest(requireOwn(fact, 'evidence_sha256', where), `${where}.evidence_sha256`);
  return requireText(requireOwn(fact, 'value', where), `${where}.value`);
}

/**
 * The facts a citation of a Luxembourg bundle's states is composed from (`citation_facts`; the plan's item 3.3, R65), each
 * the publisher's, verbatim: the titles of each listed state's expression, by language (`titles` and `shortTitles`, the
 * words alone), and the act as published (the states' work IRI followed by `/jo`) with the issues of the Mémorial A the
 * publisher says it is part of and its publication dates. A title of an expression no listed state is, or a fact of
 * another subject, is drift and thrown: it would name another act or another text.
 */
export function readCitationFacts(facts, states) {
  const where = 'citation_facts';
  const works = [...new Set(states.map((state) => state.publisherWorkIri))];
  if (works.length !== 1) throw new Error(`${where} is of one work, and the states name ${works.length}`);
  const journalOfficielIri = requireText(requireOwn(facts, 'journal_officiel_iri', where), `${where}.journal_officiel_iri`);
  if (journalOfficielIri !== `${works[0]}/jo`) throw new Error(`${where}.journal_officiel_iri ${journalOfficielIri} is not the act as published of ${works[0]}`);
  const expressions = new Set(states.map((state) => state.expressionIri));
  const titles = Object.freeze(requireList(requireOwn(facts, 'titles', where), `${where}.titles`).map((group, index) => {
    const at = `${where}.titles[${index}]`;
    const expressionIri = requireText(requireOwn(group, 'expression_iri', at), `${at}.expression_iri`);
    if (!expressions.has(expressionIri)) throw new Error(`${at} is of ${expressionIri}, which no listed state is`);
    const words = (key) => Object.freeze(requireList(requireOwn(group, key, at), `${at}.${key}`).map((row, rowIndex) => {
      requireDigest(requireOwn(row, 'evidence_sha256', `${at}.${key}[${rowIndex}]`), `${at}.${key}[${rowIndex}].evidence_sha256`);
      return requireText(requireOwn(row, 'title', `${at}.${key}[${rowIndex}]`), `${at}.${key}[${rowIndex}].title`);
    }));
    return Object.freeze({
      language: requireText(requireOwn(group, 'language', at), `${at}.language`),
      expressionIri,
      titles: words('titles'),
      shortTitles: words('short_titles'),
    });
  }));
  const factsOf = (key) => Object.freeze(requireList(requireOwn(facts, key, where), `${where}.${key}`)
    .map((fact, index) => readFactOf(fact, journalOfficielIri, `${where}.${key}[${index}]`)));
  requireText(requireOwn(facts, 'rule', where), `${where}.rule`);
  return Object.freeze({
    titles,
    journalOfficielIri,
    memorialIssues: factsOf('memorial_issues'),
    publicationDates: factsOf('publication_dates'),
  });
}

/** The bundle's distinct disclosures; a single-channel bundle disposition needs at least one source of it to carry one. */
function readBundleDisclosures(rightsDisposition, states) {
  const seen = new Map();
  for (const state of states) {
    for (const source of state.sources) {
      if (source.disclosure !== null) seen.set(`${source.disclosure.code} ${source.disclosure.observedOn}`, source.disclosure);
    }
  }
  if (DISCLOSED_RIGHTS_DISPOSITIONS.includes(rightsDisposition) &&
      ![...seen.values()].some((disclosure) => disclosure.code === DISCLOSURE_CODES[rightsDisposition])) {
    throw new Error(`a bundle served under ${rightsDisposition} names no source disclosing it`);
  }
  return Object.freeze([...seen.values()].sort((left, right) =>
    left.code.localeCompare(right.code) || left.observedOn.localeCompare(right.observedOn)));
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
  if (!ADMITTING_RIGHTS_DISPOSITIONS.includes(rightsDisposition) && !DISCLOSED_RIGHTS_DISPOSITIONS.includes(rightsDisposition)) {
    throw new Error(`a bundle serves text only under ${ADMITTING_SAID}, not ${rightsDisposition}`);
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
    publisher,
    identifier: requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    workKey,
    date,
    language,
    ...readPageRequest(answer, where),
    availableLanguages,
    rightsDisposition,
    rightsRule: requireText(requireOwn(answer, 'rights_rule', where), 'rights_rule'),
    // Every single-channel disclosure the bundle's text rests on, once each (the L5 single-channel sentence).
    disclosures: readBundleDisclosures(rightsDisposition, states),
    states: Object.freeze(states),
    notHeld,
    // An act held only as first published says so (the plan's item 0.6): `as-published.mjs` reads it, or null.
    asPublished: readAsPublished(answer, { states, notHeld }),
    // What a citation of the states needs that they do not carry (R65): read by `readCitationFacts`.
    citationFacts: readCitationFacts(requireOwn(answer, 'citation_facts', where), states),
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
  // Beside a next date, whether that wording's text is held in this language (review of #972: its date bounds this wording
  // whether or not its text is held, so a page that said a later wording "is held" said what the answer does not).
  const nextTextHeld = Object.hasOwn(wording, 'next_text_held') ? wording.next_text_held : null;
  if (typeof nextDate === 'string' ? typeof nextTextHeld !== 'boolean' : nextTextHeld !== null) {
    throw new Error(`${where} says next_text_held is ${JSON.stringify(nextTextHeld)} beside the next date ${JSON.stringify(nextDate ?? null)}; it is true or false beside a date, and null with none`);
  }
  const wordingSha256 = requireDigest(requireOwn(wording, 'wording_sha256', where), `${where} wording_sha256`);
  // The time view names the CELEX of the work its wording is read from (a consolidated version's own, `0…-{date}`), or
  // null when the publisher's record of that work names none (in the population mount, one of the two works the
  // publisher holds of many a consolidation: the speed checks of the plan's item 3.7 found MiCA's 2024-01-09 wording
  // unreadable for it); the original-wording bundle names only the act's, which is then the wording's.
  const wordingCelex = Object.hasOwn(wording, 'celex') ? requireTextOrNull(wording.celex, `${where} celex`) : celex;
  const permalink = requireText(requireOwn(wording, 'permalink', where), `${where} permalink`);
  const expected = europePermalink({ celex, date: wordingDate, wordingSha256, language });
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
    const provisionPermalink = requireText(requireOwn(article, 'article_permalink', label), `${label} article_permalink`);
    if (provisionPermalink !== articlePermalink(permalink, publisherId)) {
      throw new Error(`${label} carries the permalink ${JSON.stringify(provisionPermalink)}, not its wording's with ${publisherId} as a last segment`);
    }
    const heading = requireOwn(article, 'heading', label);
    if (typeof heading !== 'string') throw new Error(`${label} heading is not text`);
    return Object.freeze({
      position: requirePosition(requireOwn(article, 'position', label), `${label} position`),
      articleIdentitySha256: requireDigest(requireOwn(article, 'article_identity_sha256', label), `${label} article_identity_sha256`),
      publisherId,
      heading,
      text,
      textSha256: requireDigest(requireOwn(article, 'text_sha256', label), `${label} text_sha256`),
      textByteLength,
      bodySha256,
      officialSource: requireText(requireOwn(article, 'official_source', label), `${label} official_source`),
      permalink: provisionPermalink,
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
      position: requirePosition(requireOwn(article, 'position', label), `${label}.position`),
      articleIdentitySha256: requireDigest(requireOwn(article, 'article_identity_sha256', label), `${label}.article_identity_sha256`),
      publisherId: requireText(requireOwn(article, 'publisher_id', label), `${label}.publisher_id`),
    });
  }));
  // The page served, the wording's pages and its contents (a wording with no text at all is refused, so it quotes at least
  // one article, on some page).
  const paged = readVersionPages(wording, { quoted: articles, withoutText: articlesWithoutText, headings: true }, where);

  // The time view's disclosure beside the wording: the other works the census dated to its day, and the versions with no
  // usable date, each with whether its text is held here (absent on the original-wording bundle of an index with no states).
  const disclosed = (key) => (Object.hasOwn(wording, key)
    ? Object.freeze(requireList(wording[key], `${where} ${key}`).map((row, at) => {
      const label = `${where} ${key}[${at}]`;
      const textHeld = requireBoolean(requireOwn(row, 'text_held', label), `${label}.text_held`);
      return Object.freeze({ workIri: requireText(requireOwn(row, 'publisher_work_id', label), `${label}.publisher_work_id`), textHeld });
    }))
    : Object.freeze([]));
  const sameDateWorks = disclosed('same_date_works');
  const unplacedVersions = disclosed('unplaced_versions');
  // The annexes of the wording's sources, never quoted: each disposition's count, reason and official source.
  const annexesNotServed = readAnnexesNotServed(wording, where);
  return Object.freeze({
    expressionIri, language, kind, wordingDate, nextDate, nextTextHeld, wordingSha256, wordingCelex, permalink, stableCoordinate, sources, articles, articlesWithoutText,
    sameDateWorks, unplacedVersions, annexesNotServed, ...paged,
  });
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

  const authenticity = readEuropeAuthenticity(answer, where);

  return Object.freeze({
    publisher,
    identifier: requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    celex,
    publisherWorkIri: requireText(requireOwn(answer, 'publisher_work_id', where), 'publisher_work_id'),
    date,
    language,
    ...readPageRequest(answer, where),
    availableLanguages,
    acknowledgement,
    authenticity: authenticity.sentence,
    authenticityBasis: authenticity.basis,
    authenticityActDates: authenticity.actDates,
    authenticityRule: authenticity.rule,
    consolidationsHeld,
    // The act's dates as the publisher records them, over an EU index /6; null over an older one (`europe-force-dates.mjs`).
    forceDates: readEuropeForceDates(answer, where),
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
