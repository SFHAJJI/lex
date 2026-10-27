// The exports the live composer hands a reader: JSON and CSV, composed from one V3 evidence bundle.
//
// A reader pins articles of the states an evidence bundle served (`reading-answer.mjs`,
// `readEvidenceBundle`), and this module composes what they take away. The launch contract's line is
// that exports "preserve citations, rights, watermarks and exclusions", so every export carries, for
// every item: its citation (the hash-pinned article permalink, the state permalink and the text and
// body digests), the official source it was read from, and the rights disposition under which the
// platform served its text; and, for the export as a whole: the watermark, the platform's rights rule,
// the digests of the corpus, index and registry that answered, and when the platform says the
// answering snapshot was observed (the envelope's `context.freshness.observed_at`; the envelope
// carries no time of answering, so none is claimed). A pinned article
// the bundle holds without text is excluded, and the exclusion travels in the export with its reason,
// never as an empty quotation. Nothing here is asserted beyond what the bundle served.
//
// An act held only as first published (the plan's item 0.6, `as-published.mjs`) says so in its export as on its
// pages: the export carries the platform's label, its sentence about later versions and its authenticity sentence, in
// the platform's English (`as_published` in the JSON, three columns on every CSV row, a paragraph of the PDF), and
// null or empty for an act whose consolidated text is held.
//
// Every item and every exclusion carries its citations (`citation_forms`), from the one citation generator
// (`citation.mjs`; the plan's item 3.3, R65), in the language of the text cited: the usual form, the usual form with the
// dated permalink, and the evidence form, which an export keeps (R65: "exports keep this form"), with the date Lex was
// consulted the answering snapshot's own (the envelope's `observed_at`, in Luxembourg). `citation` stays the article's
// hash-pinned permalink, which `verify` takes as it is. A text in a language no citation is composed in carries null.
//
// The watermark is the composer's own (`export-composer.mjs`), so the preview and the export say the
// same sentence.
//
// An article whose wording holds quoted wording or wording the publisher marks deleted (content /3) carries its segments
// (`segments` in the JSON and a column of the CSV, empty for any other article): its text segments, the quotation
// boundaries with the publisher's mark evidence (`marks_in_text`: the publisher writes the marks in its text), and the
// deleted wording, which is never part of `text`, its digest or a citation. The PDF quotes the text with its quotation
// marks and lists the deleted wording apart, under a label, never inside the text. A note whose reference the publisher
// wrote without a marker is `unmarked`, with no marker.
//
// A text served on the publisher's catalogue alone (one of the owner's rulings L1, L1b or L2: a single-channel admission,
// `reading-answer.mjs`, `DISCLOSED_RIGHTS_DISPOSITIONS`) carries its rights disclosure into every export as onto its page:
// each item read from such a source carries `rights_disclosure` (the code, the licence page's digest, the date it was observed
// and whether live, and the platform's English sentence, the reading page's L5 sentence), and the export as a whole lists the
// bundle's disclosures once each (`rights_disclosures`); the CSV says it in a last column, `rights_disclosure`, and the PDF in a
// paragraph beside the rights rule and on each item. An export of a text with no disclosure keeps its JSON and PDF byte for
// byte; its CSV gains the last column, empty (the review of #1002).
//
// An EU reading (`readEuropeEvidenceBundle`) is composed by `composeEuropeExport` into a model of its
// own (`lex-v3-export-eu/1`), never through the Luxembourg one: an EU article is dated by its wording,
// whose date is never an applicability date, and its text is served with Decision 95's acknowledgement
// and authenticity statement, not under a rights disposition. Every EU item carries the
// acknowledgement, its wording's kind and date, its citation (the article permalink, which pins the
// wording), the wording's permalink, its text and body digests and its official source; the export as a
// whole carries the authenticity statement, the platform's rights rule, its statement of what a wording
// date is, and the same watermark, observation time and digests. A pinned article the wording holds
// without text is excluded, and so is every annex of a wording an article is pinned from: an annex's
// text is never held, so the export lists each of the wording's annex rows (`annexes_not_served`: the
// count, the platform's reason and the official source), never a text. `exportJson` and `exportCsv`
// write either model, by its schema.

import { citationForms } from './citation.mjs';
import { WATERMARK_PREVIEW } from './export-composer.mjs';
import { EUROPE_ANNEX_SERVED_AS } from './europe-annexes.mjs';
import { articlePermalink } from './permalinks.mjs';
import { LIVE_CHROME, fillText } from './live-chrome.mjs';

export const EXPORT_SCHEMA = 'lex-v3-export/1';
export const EUROPE_EXPORT_SCHEMA = 'lex-v3-export-eu/1';
export const EXPORT_WATERMARK = WATERMARK_PREVIEW;

/**
 * Why a pinned EU article is excluded: the bundle holds it under `articles_without_text`, which is the
 * name it gives; it sends no reason code.
 */
export const EUROPE_WITHOUT_TEXT = 'articles_without_text';

/**
 * The CSV columns, in order: each row is one pinned article, whole on its own. The five before the last repeat on every row
 * what the export as a whole carries (the snapshot's observation time, the rights rule, the corpus, index and registry
 * digests), so a row copied out alone loses none of it (review of #789). The last, `segments`, is the JSON of an article's
 * segments (content /3), empty for any other; it comes last so every column before it keeps its place under
 * `lex-v3-export/1` (the review of #981, F4). After it, `date_role`: what `applies_from` is on index 9 (`applicability`,
 * `applicability_in_file`, or `entry_into_force`, an act's entry into force, which the state does not apply from; R21), empty
 * on an index of schema 8 or 6, which names none; appended, so every column before it keeps its place (the review of #987).
 */
export const CSV_COLUMNS = Object.freeze([
  'work_key', 'language', 'applies_from', 'article', 'status', 'citation', 'citation_conventional', 'citation_with_permalink', 'citation_evidence', 'state_permalink',
  'text_sha256', 'body_sha256', 'official_source', 'rights_disposition', 'article_valid_from', 'text', 'watermark',
  'as_published', 'later_versions', 'authenticity',
  'observed_at', 'rights_rule', 'corpus_sha256', 'index_sha256', 'registry_sha256',
  'segments',
  'date_role',
  'rights_disclosure',
]);

/**
 * A rights disclosure as an export carries it (`readDisclosure`'s view, `reading-answer.mjs`): its code, the licence page it
 * rests on and when that page was observed, and the platform's English sentence for it, the reading page's (the L5 single-channel
 * disclosure; drafts the owner revises). Null for null.
 */
export function exportDisclosure(disclosure) {
  if (disclosure === null || disclosure === undefined) return null;
  const template = LIVE_CHROME.en.reading.disclosure[disclosure.code];
  if (typeof template !== 'string') throw new Error(`no sentence says the rights disclosure ${disclosure.code}`);
  return Object.freeze({
    code: disclosure.code,
    licencePageSha256: disclosure.licencePageSha256,
    observedOn: disclosure.observedOn,
    observedLive: disclosure.observedLive,
    sentence: fillText(template, { date: disclosure.observedOn }),
  });
}

/** A disclosure as the JSON and the CSV write it: snake_case. */
export function disclosureJson(disclosure) {
  return {
    code: disclosure.code,
    licence_page_sha256: disclosure.licencePageSha256,
    licence_page_observed_on: disclosure.observedOn,
    licence_page_observed_live: disclosure.observedLive,
    sentence: disclosure.sentence,
  };
}

/**
 * What an export says of an act held only as first published, from the reading's view (`readAsPublished`): the
 * platform's label, later-versions sentence and authenticity sentence, and the act as published; null otherwise.
 */
function asPublishedOf(view) {
  if (!Object.hasOwn(view, 'asPublished')) throw new Error('an export says whether its act is held only as published, and this reading does not');
  const value = view.asPublished;
  return value === null ? null : Object.freeze({
    label: value.label,
    laterVersions: value.laterVersions.sentence,
    authenticity: value.authenticity.sentence,
    journalOfficielIri: value.journalOfficielIri,
  });
}

/**
 * The export's paragraph on an act held only as first published, as the preview and the PDF say it: the label, the
 * later-versions sentence and the authenticity sentence, each a sentence. Empty for null.
 */
export function asPublishedParagraph(asPublished) {
  if (asPublished === null) return '';
  const sentence = (text) => `${text.charAt(0).toUpperCase()}${text.slice(1)}${/[.!?]$/.test(text) ? '' : '.'}`;
  return [asPublished.label, asPublished.laterVersions, asPublished.authenticity].map(sentence).join(' ');
}

/**
 * The EU CSV columns, in order: each row is one pinned article, or one annex row of a wording an
 * article is pinned from, whole on its own. The wording's date stands where a Luxembourg row has
 * its applicability date, an annex row says its count and the platform's reason (`annexes`, `reason`),
 * and the last eight repeat on every row what the export as a whole carries: the watermark, Decision
 * 95's acknowledgement and authenticity statement, the snapshot's observation time, the rights rule
 * and the corpus, index and registry digests.
 */
export const EUROPE_CSV_COLUMNS = Object.freeze([
  'celex', 'language', 'wording_kind', 'wording_date', 'article', 'heading', 'status', 'citation', 'citation_conventional', 'citation_with_permalink', 'citation_evidence', 'wording_permalink',
  'text_sha256', 'body_sha256', 'official_source', 'annexes', 'reason', 'text', 'watermark', 'acknowledgement',
  'authenticity', 'observed_at', 'rights_rule', 'corpus_sha256', 'index_sha256', 'registry_sha256',
]);

/** One article's citations as an export model holds them (`citationForms`), or null for a text no citation is composed in. */
function citationsOf(view, version, article, observedAt) {
  const forms = citationForms({ view, version, article, observedAt });
  return forms === null ? null : Object.freeze({
    language: forms.language,
    conventional: forms.conventional,
    withPermalink: forms.withPermalink,
    evidence: forms.evidence,
  });
}

/** The citations as the JSON export writes them: snake_case, or null. */
function citationsJson(citations) {
  return citations === null ? null : {
    language: citations.language,
    conventional: citations.conventional,
    with_permalink: citations.withPermalink,
    evidence: citations.evidence,
  };
}

/** The three CSV fields of an item's citations: the usual form, with the permalink, and the evidence form (empty for null). */
function citationsCsv(citations) {
  return citations === null ? ['', '', ''] : [citations.conventional ?? '', citations.withPermalink, citations.evidence];
}

/**
 * The export model for the articles `pinned` names (each `{stateSha256, publisherId}`), in the order
 * the bundle lists them. Throws for a pin the bundle does not hold, so an export never claims an
 * article the reader was not shown.
 */
export function composeExport({ view, pinned, observedAt }) {
  if (!Array.isArray(pinned) || pinned.length === 0) throw new Error('an export needs at least one pinned article');
  if (typeof observedAt !== 'string' || observedAt.length === 0) throw new Error('an export says when the answering snapshot was observed');
  const wanted = new Set(pinned.map((pin) => `${pin.stateSha256}#${pin.publisherId}`));
  const items = [];
  const excluded = [];
  for (const state of view.states) {
    // The disclosure of the source each article is read from, by its body (a single-channel admission's; null otherwise).
    const disclosureOf = new Map((state.sources ?? []).filter((source) => source.bodySha256 !== null)
      .map((source) => [source.bodySha256, exportDisclosure(source.disclosure ?? null)]));
    for (const article of state.articles) {
      if (!wanted.delete(`${state.stateSha256}#${article.publisherId}`)) continue;
      items.push(Object.freeze({
        workKey: view.workKey,
        language: state.language,
        appliesFrom: state.applicabilityDate,
        // A state dated by its entry into force, which the PDF never says as applying from it (R21; the review of #987).
        datedByEntryIntoForce: state.dateRole === 'entry_into_force',
        // The date's role (index 9; null on an index of schema 8 or 6): an entry into force is never said as applying (R21).
        dateRole: state.dateRole ?? null,
        publisherId: article.publisherId,
        citation: article.permalink,
        citations: citationsOf(view, state, article, observedAt),
        statePermalink: state.permalink,
        text: article.text,
        textSha256: article.textSha256,
        textByteLength: article.textByteLength,
        bodySha256: article.bodySha256,
        officialSource: article.officialSource,
        rightsDisposition: view.rightsDisposition,
        articleValidFrom: article.validFrom,
        validityConflict: article.validityConflict,
        notes: article.notes,
        segments: article.segments ?? null,
        rightsDisclosure: disclosureOf.get(article.bodySha256) ?? null,
      }));
    }
    for (const entry of state.articlesWithoutText) {
      if (!wanted.delete(`${state.stateSha256}#${entry.publisherId}`)) continue;
      excluded.push(Object.freeze({
        workKey: view.workKey,
        language: state.language,
        appliesFrom: state.applicabilityDate,
        datedByEntryIntoForce: state.dateRole === 'entry_into_force',
        dateRole: state.dateRole ?? null,
        publisherId: entry.publisherId,
        // The article is held in its state, without text, so it is cited as any article of that
        // state is: the state's hash-pinned permalink and the publisher's article id as a last segment (R64).
        citation: articlePermalink(state.permalink, entry.publisherId),
        citations: citationsOf(view, state, { publisherId: entry.publisherId }, observedAt),
        statePermalink: state.permalink,
        reason: 'no_text_tokens',
      }));
    }
  }
  if (wanted.size > 0) {
    throw new Error(`the bundle holds no article ${[...wanted].join(', ')}; an export never names one the reader was not shown`);
  }
  return Object.freeze({
    schema: EXPORT_SCHEMA,
    watermark: EXPORT_WATERMARK,
    observedAt,
    identifier: view.identifier,
    date: view.date,
    rightsDisposition: view.rightsDisposition,
    rightsRule: view.rightsRule,
    // Every disclosure the bundle's text rests on, once each (the reading page says the same sentences).
    rightsDisclosures: Object.freeze((view.disclosures ?? []).map(exportDisclosure)),
    asPublished: asPublishedOf(view),
    verifiedBy: Object.freeze({ corpusSha256: view.corpusSha256, indexSha256: view.indexSha256, registrySha256: view.registrySha256 }),
    items: Object.freeze(items),
    excluded: Object.freeze(excluded),
  });
}

/**
 * The EU export model for the articles `pinned` names (each `{wordingSha256, publisherId}`), from an
 * EU reading view (`readEuropeEvidenceBundle`), in the order the bundle lists them. The bundle names
 * its corpus and index; the registry that answered is its envelope's (`registrySha256`). Throws for a
 * pin the bundle does not hold, so an export never claims an article the reader was not shown.
 */
export function composeEuropeExport({ view, pinned, observedAt, registrySha256 }) {
  if (view.publisher !== 'eu-eurlex') throw new Error('an EU export is composed from an EU reading');
  if (!Array.isArray(pinned) || pinned.length === 0) throw new Error('an export needs at least one pinned article');
  if (typeof observedAt !== 'string' || observedAt.length === 0) throw new Error('an export says when the answering snapshot was observed');
  if (typeof registrySha256 !== 'string' || !/^[0-9a-f]{64}$/.test(registrySha256)) throw new Error('an export names the registry that answered');
  const wanted = new Set(pinned.map((pin) => `${pin.wordingSha256}#${pin.publisherId}`));
  const items = [];
  const excluded = [];
  const annexes = [];
  for (const wording of view.wordings) {
    // What every entry read in this wording carries: its act, its language, and its wording's kind, date and permalink.
    const read = { celex: view.celex, language: wording.language, wordingKind: wording.kind, wordingDate: wording.wordingDate, wordingPermalink: wording.permalink };
    let pinnedHere = false;
    for (const article of wording.articles) {
      if (!wanted.delete(`${wording.wordingSha256}#${article.publisherId}`)) continue;
      pinnedHere = true;
      items.push(Object.freeze({
        ...read,
        publisherId: article.publisherId,
        heading: article.heading,
        citation: article.permalink,
        citations: citationsOf(view, wording, article, observedAt),
        text: article.text,
        textSha256: article.textSha256,
        textByteLength: article.textByteLength,
        bodySha256: article.bodySha256,
        officialSource: article.officialSource,
        acknowledgement: view.acknowledgement,
        authenticity: view.authenticity,
      }));
    }
    for (const entry of wording.articlesWithoutText) {
      if (!wanted.delete(`${wording.wordingSha256}#${entry.publisherId}`)) continue;
      pinnedHere = true;
      excluded.push(Object.freeze({
        ...read,
        publisherId: entry.publisherId,
        // The article is held in its wording, without text, so it is cited as any article of that wording
        // is: the wording's permalink and the provision as a last segment, escaped as the platform escapes it (R64).
        citation: articlePermalink(wording.permalink, entry.publisherId),
        citations: citationsOf(view, wording, { publisherId: entry.publisherId }, observedAt),
        reason: EUROPE_WITHOUT_TEXT,
      }));
    }
    // The annexes of a wording an article is pinned from travel with the export as exclusions: each row's count,
    // reason and official source, and never a text, since none is held.
    if (!pinnedHere) continue;
    for (const row of wording.annexesNotServed) {
      annexes.push(Object.freeze({
        ...read,
        officialIdentity: row.officialIdentity,
        disposition: row.disposition,
        count: row.count,
        identities: row.identities,
        servedAs: EUROPE_ANNEX_SERVED_AS,
        reason: row.reason,
        officialSource: row.officialSource,
      }));
    }
  }
  if (wanted.size > 0) {
    throw new Error(`the bundle holds no article ${[...wanted].join(', ')}; an export never names one the reader was not shown`);
  }
  return Object.freeze({
    schema: EUROPE_EXPORT_SCHEMA,
    watermark: EXPORT_WATERMARK,
    observedAt,
    identifier: view.identifier,
    date: view.date,
    celex: view.celex,
    acknowledgement: view.acknowledgement,
    authenticity: view.authenticity,
    rightsRule: view.rightsRule,
    wordingDateSemantics: view.dateSemantics,
    verifiedBy: Object.freeze({ corpusSha256: view.corpusSha256, indexSha256: view.indexSha256, registrySha256 }),
    items: Object.freeze(items),
    excluded: Object.freeze(excluded),
    annexesNotServed: Object.freeze(annexes),
  });
}

/**
 * The JSON export: the model, with snake_case members, indented, with a final newline. An EU model is
 * written with its own members (`europeExportJson`).
 */
export function exportJson(model) {
  if (model.schema === EUROPE_EXPORT_SCHEMA) return europeExportJson(model);
  const document = {
    schema: model.schema,
    watermark: model.watermark,
    observed_at: model.observedAt,
    requested_identifier: model.identifier,
    requested_date: model.date,
    rights_disposition: model.rightsDisposition,
    rights_rule: model.rightsRule,
    // Only where the text rests on the publisher's catalogue alone, so every other export's JSON is byte for byte what it was.
    ...((model.rightsDisclosures ?? []).length === 0 ? {} : { rights_disclosures: model.rightsDisclosures.map(disclosureJson) }),
    as_published: model.asPublished === null ? null : {
      label: model.asPublished.label,
      later_versions: model.asPublished.laterVersions,
      authenticity: model.asPublished.authenticity,
      journal_officiel_iri: model.asPublished.journalOfficielIri,
    },
    verified_by: {
      corpus_sha256: model.verifiedBy.corpusSha256,
      index_sha256: model.verifiedBy.indexSha256,
      registry_sha256: model.verifiedBy.registrySha256,
    },
    items: model.items.map((item) => ({
      work_key: item.workKey,
      language: item.language,
      applies_from: item.appliesFrom,
      // Index 9 names what that date is (R21): an entry into force is a date of its own, never one the state applies from.
      ...(item.dateRole === null ? {} : { date_role: item.dateRole }),
      article: item.publisherId,
      citation: item.citation,
      citation_forms: citationsJson(item.citations),
      state_permalink: item.statePermalink,
      text: item.text,
      text_sha256: item.textSha256,
      text_byte_length: item.textByteLength,
      body_sha256: item.bodySha256,
      official_source: item.officialSource,
      rights_disposition: item.rightsDisposition,
      ...(item.rightsDisclosure === null || item.rightsDisclosure === undefined ? {} : { rights_disclosure: disclosureJson(item.rightsDisclosure) }),
      article_valid_from: item.articleValidFrom,
      validity_conflict: item.validityConflict,
      notes: item.notes.map(noteJson),
      ...(item.segments === null ? {} : { segments: segmentsJson(item.segments) }),
    })),
    excluded: model.excluded.map((entry) => ({
      work_key: entry.workKey,
      language: entry.language,
      applies_from: entry.appliesFrom,
      ...(entry.dateRole === null ? {} : { date_role: entry.dateRole }),
      article: entry.publisherId,
      citation: entry.citation,
      citation_forms: citationsJson(entry.citations),
      state_permalink: entry.statePermalink,
      reason: entry.reason,
    })),
  };
  return `${JSON.stringify(document, null, 2)}\n`;
}

/**
 * The JSON of an EU export. Each item and each exclusion is dated by its wording (`wording_kind`,
 * `wording_date`), never by an applicability date, and each item carries the acknowledgement it was
 * served with; the annex rows keep the platform's member names, and carry no text.
 */
function europeExportJson(model) {
  const document = {
    schema: model.schema,
    watermark: model.watermark,
    observed_at: model.observedAt,
    requested_identifier: model.identifier,
    requested_date: model.date,
    celex: model.celex,
    acknowledgement: model.acknowledgement,
    authenticity: model.authenticity,
    rights_rule: model.rightsRule,
    wording_date_semantics: model.wordingDateSemantics,
    verified_by: {
      corpus_sha256: model.verifiedBy.corpusSha256,
      index_sha256: model.verifiedBy.indexSha256,
      registry_sha256: model.verifiedBy.registrySha256,
    },
    items: model.items.map((item) => ({
      celex: item.celex,
      language: item.language,
      wording_kind: item.wordingKind,
      wording_date: item.wordingDate,
      article: item.publisherId,
      heading: item.heading,
      citation: item.citation,
      citation_forms: citationsJson(item.citations),
      wording_permalink: item.wordingPermalink,
      text: item.text,
      text_sha256: item.textSha256,
      text_byte_length: item.textByteLength,
      body_sha256: item.bodySha256,
      official_source: item.officialSource,
      acknowledgement: item.acknowledgement,
      authenticity: item.authenticity,
    })),
    excluded: model.excluded.map((entry) => ({
      celex: entry.celex,
      language: entry.language,
      wording_kind: entry.wordingKind,
      wording_date: entry.wordingDate,
      article: entry.publisherId,
      citation: entry.citation,
      citation_forms: citationsJson(entry.citations),
      wording_permalink: entry.wordingPermalink,
      reason: entry.reason,
    })),
    annexes_not_served: model.annexesNotServed.map((row) => ({
      celex: row.celex,
      language: row.language,
      wording_kind: row.wordingKind,
      wording_date: row.wordingDate,
      wording_permalink: row.wordingPermalink,
      official_identity: row.officialIdentity,
      disposition: row.disposition,
      annexes: row.count,
      annex_identities_sha256: [...row.identities],
      served_as: row.servedAs,
      reason: row.reason,
      official_source: row.officialSource,
    })),
  };
  return `${JSON.stringify(document, null, 2)}\n`;
}

function csvField(value) {
  const text = value === null || value === undefined ? '' : String(value);
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

/** A note as an export writes it: its marker and text, or, for a reference the publisher wrote without one, `unmarked`. */
function noteJson(note) {
  return note.unmarked === true ? { marker: null, text: note.text, unmarked: true } : { marker: note.marker, text: note.text };
}

/**
 * An article's segments as an export writes them: the text segments and the deleted wording each with its text, each
 * quotation boundary with its element, whether the publisher writes the marks in its text, and its mark when it gives one.
 */
export function segmentsJson(segments) {
  return segments.map((segment) => (segment.kind === 'text' || segment.kind === 'deleted_text'
    ? { kind: segment.kind, text: segment.text }
    : { kind: segment.kind, quotation: segment.quotation, marks_in_text: segment.marksInWording, marker: segment.marker }));
}

/**
 * The CSV export (RFC 4180, CRLF line ends): a header, then one row per pinned article, the excluded
 * ones included with their status and no text, and every row carrying its citation, rights, the
 * watermark, the observation time, the rights rule and the three digests, so a row copied out on its
 * own still says what it is and what answered it. An EU model is written with its own columns
 * (`europeExportCsv`).
 */
export function exportCsv(model) {
  if (model.schema === EUROPE_EXPORT_SCHEMA) return europeExportCsv(model);
  const provenance = [model.observedAt, model.rightsRule, model.verifiedBy.corpusSha256, model.verifiedBy.indexSha256, model.verifiedBy.registrySha256];
  const published = model.asPublished === null
    ? ['', '', '']
    : [model.asPublished.label, model.asPublished.laterVersions, model.asPublished.authenticity];
  const rows = [
    ...model.items.map((item) => [
      item.workKey, item.language, item.appliesFrom, item.publisherId, 'exported', item.citation, ...citationsCsv(item.citations), item.statePermalink,
      item.textSha256, item.bodySha256, item.officialSource, item.rightsDisposition, item.articleValidFrom, item.text, model.watermark,
      ...published,
      ...provenance,
      item.segments === null ? '' : JSON.stringify(segmentsJson(item.segments)),
      item.dateRole ?? '',
      item.rightsDisclosure === null || item.rightsDisclosure === undefined ? '' : JSON.stringify(disclosureJson(item.rightsDisclosure)),
    ]),
    ...model.excluded.map((entry) => [
      entry.workKey, entry.language, entry.appliesFrom, entry.publisherId, `excluded: ${entry.reason}`, entry.citation, ...citationsCsv(entry.citations), entry.statePermalink,
      '', '', '', model.rightsDisposition, '', '', model.watermark,
      ...published,
      ...provenance,
      '',
      entry.dateRole ?? '',
      '',
    ]),
  ];
  return [CSV_COLUMNS, ...rows].map((row) => row.map(csvField).join(',')).join('\r\n') + '\r\n';
}

/**
 * The CSV of an EU export (`EUROPE_CSV_COLUMNS`): a row per exported article, a row per excluded
 * article with its status and its citation, and a row per annex row with its disposition in its
 * status, its count, the platform's reason and its official source; an excluded row and an annex row
 * hold no text. Every row carries the acknowledgement, the authenticity statement and what the
 * Luxembourg rows carry besides their rights disposition, which an EU text has none of.
 */
function europeExportCsv(model) {
  const carried = [
    model.watermark, model.acknowledgement, model.authenticity, model.observedAt, model.rightsRule,
    model.verifiedBy.corpusSha256, model.verifiedBy.indexSha256, model.verifiedBy.registrySha256,
  ];
  const rows = [
    ...model.items.map((item) => [
      item.celex, item.language, item.wordingKind, item.wordingDate, item.publisherId, item.heading, 'exported', item.citation,
      ...citationsCsv(item.citations), item.wordingPermalink, item.textSha256, item.bodySha256, item.officialSource, '', '', item.text,
      ...carried,
    ]),
    ...model.excluded.map((entry) => [
      entry.celex, entry.language, entry.wordingKind, entry.wordingDate, entry.publisherId, '', `excluded: ${entry.reason}`, entry.citation,
      ...citationsCsv(entry.citations), entry.wordingPermalink, '', '', '', '', '', '',
      ...carried,
    ]),
    // An annex row names no article, and has no citation: an annex is never quoted, so it is pointed to by its
    // wording's permalink and its official source.
    ...model.annexesNotServed.map((row) => [
      row.celex, row.language, row.wordingKind, row.wordingDate, '', '', `excluded: ${row.disposition}`, '',
      '', '', '', row.wordingPermalink, '', '', row.officialSource, row.count, row.reason, '',
      ...carried,
    ]),
  ];
  return [EUROPE_CSV_COLUMNS, ...rows].map((row) => row.map(csvField).join(',')).join('\r\n') + '\r\n';
}
