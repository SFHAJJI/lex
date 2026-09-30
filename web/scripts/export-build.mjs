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
// The watermark is the composer's own (`export-composer.mjs`), so the preview and the export say the
// same sentence.

import { WATERMARK_PREVIEW } from './export-composer.mjs';

export const EXPORT_SCHEMA = 'lex-v3-export/1';
export const EXPORT_WATERMARK = WATERMARK_PREVIEW;

/**
 * The CSV columns, in order: each row is one pinned article, whole on its own. The last five repeat on
 * every row what the export as a whole carries (the snapshot's observation time, the rights rule, the
 * corpus, index and registry digests), so a row copied out alone loses none of it (review of #789).
 */
export const CSV_COLUMNS = Object.freeze([
  'work_key', 'language', 'applies_from', 'article', 'status', 'citation', 'state_permalink',
  'text_sha256', 'body_sha256', 'official_source', 'rights_disposition', 'article_valid_from', 'text', 'watermark',
  'observed_at', 'rights_rule', 'corpus_sha256', 'index_sha256', 'registry_sha256',
]);

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
    for (const article of state.articles) {
      if (!wanted.delete(`${state.stateSha256}#${article.publisherId}`)) continue;
      items.push(Object.freeze({
        workKey: view.workKey,
        language: state.language,
        appliesFrom: state.applicabilityDate,
        publisherId: article.publisherId,
        citation: article.permalink,
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
      }));
    }
    for (const entry of state.articlesWithoutText) {
      if (!wanted.delete(`${state.stateSha256}#${entry.publisherId}`)) continue;
      excluded.push(Object.freeze({
        workKey: view.workKey,
        language: state.language,
        appliesFrom: state.applicabilityDate,
        publisherId: entry.publisherId,
        // The article is held in its state, without text, so it is cited as any article of that
        // state is: the state's hash-pinned permalink and the publisher's article id.
        citation: `${state.permalink}#${entry.publisherId}`,
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
    verifiedBy: Object.freeze({ corpusSha256: view.corpusSha256, indexSha256: view.indexSha256, registrySha256: view.registrySha256 }),
    items: Object.freeze(items),
    excluded: Object.freeze(excluded),
  });
}

/** The JSON export: the model, with snake_case members, indented, with a final newline. */
export function exportJson(model) {
  const document = {
    schema: model.schema,
    watermark: model.watermark,
    observed_at: model.observedAt,
    requested_identifier: model.identifier,
    requested_date: model.date,
    rights_disposition: model.rightsDisposition,
    rights_rule: model.rightsRule,
    verified_by: {
      corpus_sha256: model.verifiedBy.corpusSha256,
      index_sha256: model.verifiedBy.indexSha256,
      registry_sha256: model.verifiedBy.registrySha256,
    },
    items: model.items.map((item) => ({
      work_key: item.workKey,
      language: item.language,
      applies_from: item.appliesFrom,
      article: item.publisherId,
      citation: item.citation,
      state_permalink: item.statePermalink,
      text: item.text,
      text_sha256: item.textSha256,
      text_byte_length: item.textByteLength,
      body_sha256: item.bodySha256,
      official_source: item.officialSource,
      rights_disposition: item.rightsDisposition,
      article_valid_from: item.articleValidFrom,
      validity_conflict: item.validityConflict,
      notes: item.notes.map((note) => ({ marker: note.marker, text: note.text })),
    })),
    excluded: model.excluded.map((entry) => ({
      work_key: entry.workKey,
      language: entry.language,
      applies_from: entry.appliesFrom,
      article: entry.publisherId,
      citation: entry.citation,
      state_permalink: entry.statePermalink,
      reason: entry.reason,
    })),
  };
  return `${JSON.stringify(document, null, 2)}\n`;
}

function csvField(value) {
  const text = value === null || value === undefined ? '' : String(value);
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

/**
 * The CSV export (RFC 4180, CRLF line ends): a header, then one row per pinned article, the excluded
 * ones included with their status and no text, and every row carrying its citation, rights, the
 * watermark, the observation time, the rights rule and the three digests, so a row copied out on its
 * own still says what it is and what answered it.
 */
export function exportCsv(model) {
  const provenance = [model.observedAt, model.rightsRule, model.verifiedBy.corpusSha256, model.verifiedBy.indexSha256, model.verifiedBy.registrySha256];
  const rows = [
    ...model.items.map((item) => [
      item.workKey, item.language, item.appliesFrom, item.publisherId, 'exported', item.citation, item.statePermalink,
      item.textSha256, item.bodySha256, item.officialSource, item.rightsDisposition, item.articleValidFrom, item.text, model.watermark,
      ...provenance,
    ]),
    ...model.excluded.map((entry) => [
      entry.workKey, entry.language, entry.appliesFrom, entry.publisherId, `excluded: ${entry.reason}`, entry.citation, entry.statePermalink,
      '', '', '', model.rightsDisposition, '', '', model.watermark,
      ...provenance,
    ]),
  ];
  return [CSV_COLUMNS, ...rows].map((row) => row.map(csvField).join(',')).join('\r\n') + '\r\n';
}
