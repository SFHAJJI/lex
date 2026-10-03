// An EU `evidence_bundle` answer, built by hand in the shape `V3CorpusMount.EvidenceBundleEurope` sends, for the tests
// of the screens that read one (the reading screen, the export composer, the chrome scan, the date speech).
//
// The API's own tests hold that shape to the real handler on the GDPR fixture. This sample stands in until the answer
// census (`schemas/v3-platform/answer-samples.json`) captures an EU bundle; that capture replaces it. It is one module
// rather than a copy in each test, so the tests read one answer, and it lives here because `web/test` admits only test
// files (`eng/verify-v3-tree.ps1`). No page imports it.
//
// The sample's one English wording quotes two articles, holds one article without text and lists one annex row (two
// annexes of one disposition, not served as text), so a test of what a page or an export says about each has one to
// read. Its envelope carries what a page reads of the platform's envelope besides the answer: the context (the
// publisher and when the answering snapshot was observed) and the digest of the registry it is bound to, a stand-in
// here (a real envelope carries the census contract's).

import { createHash } from 'node:crypto';

import { escapeProvision } from './search-answer.mjs';

/** The SHA-256 of a text's UTF-8 bytes, as the platform digests a quoted text. */
export function sha256(text) {
  return createHash('sha256').update(text, 'utf8').digest('hex');
}

/** The work, wording and body the sample answers, and when its snapshot was observed. */
export const EUROPE_SAMPLE = Object.freeze({
  celex: '32016R0679',
  work: 'http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1',
  wordingDate: '2016-04-27',
  wordingSha256: '5'.repeat(64),
  bodySha256: 'b'.repeat(64),
  permalink: `/eu-eurlex/32016R0679/eng/2016-04-27--${'5'.repeat(64)}`,
  officialSource: 'http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.02/DOC_1',
  /** The quoted articles, as `[publisher id, heading, text]`. */
  articles: Object.freeze([
    Object.freeze(['001', 'Subject-matter and objectives', 'This Regulation lays down rules relating to the protection of natural persons with regard to the processing of personal data.']),
    Object.freeze(['002', 'Material scope', 'This Regulation applies to the processing of personal data wholly or partly by automated means.']),
  ]),
  /** The article the wording holds without text. */
  withoutText: '099',
  observedAt: '2026-10-03T08:00:00.0000000Z',
  registrySha256: '3'.repeat(64),
});

/** One annex row as the platform lists it beside an EU wording or expression, with `change` applied. */
export function europeAnnexRow(change = {}) {
  return {
    disposition: 'annex_text_not_available',
    annexes: 1,
    annex_identities_sha256: ['a'.repeat(64)],
    served_as: 'text_not_available',
    official_identity: `${EUROPE_SAMPLE.work}.0006`,
    official_source: 'https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.02',
    reason: 'every page of the publisher PDF the annex maps to is an image with no text layer: the annex is image-only, so there is no text of it to serve',
    ...change,
  };
}

/** The EU bundle as the platform sends it, with `change` applied to a fresh copy. */
export function europeBundle(change = () => {}) {
  const { celex, work, wordingDate, wordingSha256, bodySha256, permalink, officialSource } = EUROPE_SAMPLE;
  const bundle = {
    scope: 'the evidence a reader needs to quote the original wording of an EU work the mounted EU index holds',
    requested_identifier: celex,
    requested_date: wordingDate,
    requested_language: 'eng',
    publisher: 'eu-eurlex',
    publisher_work_id: work,
    celex,
    available_languages: ['eng'],
    served_languages: ['eng'],
    wordings: [{
      publisher_expression_id: `${work}.0006`,
      language: 'eng',
      wording_date: wordingDate,
      wording_sha256: wordingSha256,
      stable_coordinate: `/eu-eurlex/${celex}/eng/${wordingDate}`,
      permalink,
      sources: [{ object_ref_sha256: 'c'.repeat(64), outcome: 'acquired', body_sha256: bodySha256, body_byte_length: 1024, body_receipt_sha256: 'd'.repeat(64) }],
      articles: EUROPE_SAMPLE.articles.map(([id, heading, text]) => ({
        article_identity_sha256: sha256(`identity ${id}`),
        publisher_id: id,
        heading,
        language: 'eng',
        text,
        text_sha256: sha256(text),
        text_byte_length: Buffer.byteLength(text, 'utf8'),
        body_sha256: bodySha256,
        package_entry: 'L_2016119EN.01000101.xml',
        package_sha256: 'e'.repeat(64),
        source_entry_sha256: 'f'.repeat(64),
        official_source: officialSource,
        article_permalink: `${permalink}#${escapeProvision(id)}`,
        provision_coordinate: `${work}.0006#lex-provision=${escapeProvision(id)}`,
      })),
      articles_without_text: [{ article_identity_sha256: sha256(`identity ${EUROPE_SAMPLE.withoutText}`), publisher_id: EUROPE_SAMPLE.withoutText }],
      annexes_not_served: [europeAnnexRow({ annexes: 2, annex_identities_sha256: ['7'.repeat(64), '8'.repeat(64)] })],
    }],
    acknowledgement: '© European Union, https://eur-lex.europa.eu',
    authenticity: "Only the Official Journal of the European Union published in electronic form is authentic and produces legal effects (Regulation (EU) No 216/2013, Article 1(2)); this text is a reproduction read from the Publications Office's Formex package, not the authentic edition.",
    rights_rule: 'rights are enforced when the bundle is composed, before any text is read',
    date_rule: "the EU index holds one wording of each expression, the original act's, dated by its Formex act date",
    date_semantics: "the wording date is the date the publisher's Formex package gives the act",
    digest_rule: "the SHA-256, under the domain lex-v3-eu-wording/1, of the work's CELEX",
    consolidations_held: false,
    not_held: [
      { item: 'later_wordings', reason: 'no consolidated version is held, so only the original wording is served, and only for its own wording date' },
      { item: 'force_dates', reason: 'no entry-into-force, application or end-of-validity date is held; the wording date is none of them' },
    ],
    corpus_sha256: '1'.repeat(64),
    index_sha256: '2'.repeat(64),
  };
  const copy = structuredClone(bundle);
  change(copy);
  return copy;
}

/** A bundle as a page reads it from its envelope: the answer, the context and the registry digest. */
export function europeEnvelope(value = europeBundle()) {
  return {
    registry_sha256: EUROPE_SAMPLE.registrySha256,
    operation_id: 'evidence_bundle',
    result: { object_type: 'evidence_bundle', value },
    context: { publisher: 'eu-eurlex', freshness: { observed_at: EUROPE_SAMPLE.observedAt, upstream_health: 'current' } },
  };
}
