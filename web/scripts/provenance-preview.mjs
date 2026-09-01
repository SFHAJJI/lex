// Provenance, in the four shapes where a proof chain says more than it can show.
//
// One page per record, named after the record, because a provenance link that resolved to a
// page about some other record would be worse than the 404 it replaces. The preview links to
// exactly three states, so this file holds exactly those three plus the refusal, and
// `PREVIEW_RECORDS` is the list the harness route and the build both work from.
//
// The four shapes: an ordinary record whose body was replaced once, so the observation history
// has a closed window above the open one; a state the publisher scheduled for a date that has
// not arrived, served with an event chain the service truncated, so every count on the page is
// a floor rather than a total; a record this corpus holds no text for, whose observation
// history is legitimately empty and whose index stamp did not verify; and the refusal, which
// is the page a reader lands on after following a link to a record this corpus does not hold.
//
// Every value here is synthetic and none of it is law. The shapes are the ones the live
// service returns; the values are not.

import { page } from './render.mjs';
import { provenancePageName, renderProvenance } from './provenance.mjs';
import { skinFor } from './shells.mjs';

const PUBLISHER = 'preview-synthetic';
const WORK = 'synthetic-preview-work';
const ORIGIN = 'https://preview.invalid';

function digest(seed) {
  return seed.repeat(64).slice(0, 64);
}

/**
 * What this synthetic corpus holds.
 *
 * The figures are the fixture's own: three states of one work. They are here rather than in
 * `provenance.mjs` because a census is a fact about a deployment, and a renderer that carried
 * its own would keep printing it after the corpus changed.
 */
const HOLDINGS = Object.freeze([
  Object.freeze({
    publisher: PUBLISHER,
    publisher_name: 'Synthetic preview publisher',
    works: 1,
    versions: 3,
  }),
]);

const STAMP = Object.freeze({
  algorithm: 'ECDSA-P256-SHA256',
  public_key:
    '-----BEGIN PUBLIC KEY-----\n'
    + 'U1lOVEhFVElDIFBSRVZJRVcgS0VZLiBOT1QgQSBSRUFMIFBVQkxJQyBLRVku\n'
    + '-----END PUBLIC KEY-----',
  signature: 'U1lOVEhFVElDIFBSRVZJRVcgU0lHTkFUVVJFLiBOT1QgQSBSRUFMIFNJR05BVFVSRS4=',
});

function envelope({ stampValid }) {
  return {
    publisher: PUBLISHER,
    tier: 'A',
    history_begins: 'publisher',
    status: 'ok',
    provisional: false,
    freshness: {
      corpus_commit: digest('a'),
      built_at: '2026-08-15T09:22:08Z',
      last_confirmed_at: '2026-08-15T09:22:08Z',
      last_confirmed_source: 'index-build',
      stamp_signature_valid: stampValid,
    },
    jurisdiction: 'LU',
    timeline_semantics: 'publisher_applicability',
    artifact: {
      manifest_set_id: digest('b'),
      content_digest: digest('c'),
      index_format: null,
    },
  };
}

function document({ validFrom, bodyDigest, recordDigest, textAvailable, publicationDate }) {
  return {
    lex_id: `${PUBLISHER}:${WORK}:${validFrom}`,
    version_key: validFrom,
    work: WORK,
    work_identifier: `${ORIGIN}/eli/${WORK}`,
    document_type: 'LOI',
    extraction_profile: textAvailable ? 'preview-synthetic/1' : null,
    language: 'fr',
    valid_from: validFrom,
    valid_to: null,
    valid_time_source: 'publisher',
    publication_date: publicationDate,
    title: 'Loi synthetique de demonstration',
    withdrawn: false,
    text_available: textAvailable,
    record_sha256: recordDigest,
    body_sha256: bodyDigest,
    source_uri: `${ORIGIN}/${WORK}/${validFrom}/fr`,
    observed_from: '2026-08-14T23:05:14Z',
    text: null,
    permalink: `${ORIGIN}/${PUBLISHER}/${WORK}/${validFrom}`,
  };
}

const REPLACED_BODY = digest('1');
const CURRENT_BODY = digest('2');

const ORDINARY = {
  lexId: `${PUBLISHER}:${WORK}:2021-01-26`,
  heading: 'A record whose body this service replaced once',
  note:
    'The publisher replaced the file and this service observed the new bytes. The old digest '
    + 'stays on the page with the window it was held for, because a digest that disappears '
    + 'takes with it the only evidence that a citation made against it was ever correct.',
  record: {
    envelope: envelope({ stampValid: true }),
    document: document({
      validFrom: '2021-01-26',
      bodyDigest: CURRENT_BODY,
      recordDigest: digest('3'),
      textAvailable: true,
      publicationDate: '2021-01-26',
    }),
    truncated: false,
    events: [
      {
        event: 'first_sighting',
        scope: 'version',
        observed_from: '2026-08-14T23:05:14Z',
        detail: null,
      },
      {
        event: 'body_replaced',
        scope: 'fr',
        observed_from: '2026-08-15T04:50:52Z',
        detail: 'language=fr',
      },
    ],
    observations: [
      {
        language: 'fr',
        expr_valid_from: '2021-01-26',
        sha256: REPLACED_BODY,
        observed_from: '2026-08-14T23:05:14Z',
        observed_to: '2026-08-15T04:50:52Z',
      },
      {
        language: 'fr',
        expr_valid_from: '2021-01-26',
        sha256: CURRENT_BODY,
        observed_from: '2026-08-15T04:50:52Z',
        observed_to: null,
      },
    ],
    stamp: { signature_valid: true, ...STAMP },
  },
};

const TRUNCATED = {
  lexId: `${PUBLISHER}:${WORK}:2030-09-15`,
  heading: 'A chain the service truncated',
  note:
    'The payload declares that it cut these lists, so every count on the page is a floor and '
    + 'says so. A truncated chain rendered as a total is a page asserting that nothing else '
    + 'ever happened to this record.',
  record: {
    envelope: envelope({ stampValid: true }),
    document: document({
      validFrom: '2030-09-15',
      bodyDigest: digest('4'),
      recordDigest: digest('5'),
      textAvailable: true,
      publicationDate: '2026-06-30',
    }),
    truncated: true,
    events: [
      {
        event: 'metadata_revised',
        scope: 'version',
        observed_from: '2026-08-15T04:50:52Z',
        detail: 'fields=publisher_metadata',
      },
    ],
    observations: [
      {
        language: 'fr',
        expr_valid_from: '2030-09-15',
        sha256: digest('4'),
        observed_from: '2026-08-14T23:05:14Z',
        observed_to: null,
      },
    ],
    stamp: { signature_valid: true, ...STAMP },
  },
};

const NO_TEXT = {
  lexId: `${PUBLISHER}:${WORK}:2001-01-01`,
  heading: 'A record this corpus holds no text for, under a stamp that did not verify',
  note:
    'Two absences on one page, and neither is about the law. This service ingested the record '
    + 'and not its text, so the observation history is empty rather than missing; and the index '
    + 'stamp did not verify, which the strip says in words rather than in a colour.',
  record: {
    envelope: envelope({ stampValid: false }),
    document: document({
      validFrom: '2001-01-01',
      bodyDigest: null,
      recordDigest: digest('6'),
      textAvailable: false,
      publicationDate: '2001-01-01',
    }),
    truncated: false,
    events: [
      {
        event: 'first_sighting',
        scope: 'version',
        observed_from: '2026-08-14T23:05:14Z',
        detail: null,
      },
    ],
    observations: [],
    stamp: { signature_valid: false, ...STAMP },
  },
};

/** The records the preview links to, in the order the pages are built. */
export const PREVIEW_RECORDS = Object.freeze([ORDINARY, TRUNCATED, NO_TEXT]);

/** The corpus census the preview pages disclose. Exported so a test can read the figures. */
export const PREVIEW_HOLDINGS = HOLDINGS;

/** The identifier the refusal page is addressed to. It is not a record this corpus holds. */
export const REFUSED_LEX_ID = `${PUBLISHER}:${WORK}:1900-01-01`;

const REFUSAL = Object.freeze({
  status: 'unknown_work',
  lex_id: REFUSED_LEX_ID,
});

function shell({ title, heading, note, body }) {
  return page({
    state: 'provenance',
    title,
    shell: 'dev',
    density: skinFor('dev').density,
    main:
      '      <p class="eyebrow">Developer</p>\n'
      + `      <h1>${heading}</h1>\n`
      + `      <p>${note}</p>\n`
      + '      <p>Every value on this page is synthetic and none of it is law.</p>\n'
      + `      ${body}\n`,
  });
}

/**
 * One page per linked record, plus the refusal.
 *
 * Returned as `[name, html]` pairs so the build can push them straight into its page list and
 * `pages.json` keeps naming exactly what was written.
 */
export function provenancePreviewPages() {
  const pages = PREVIEW_RECORDS.map((preview) => [
    provenancePageName(preview.lexId),
    shell({
      title: 'Provenance',
      heading: preview.heading,
      note: preview.note,
      body: renderProvenance({
        requested: { lex_id: preview.lexId, language: null },
        record: preview.record,
        holdings: HOLDINGS,
      }),
    }),
  ]);

  pages.push([
    provenancePageName(REFUSED_LEX_ID),
    shell({
      title: 'Provenance',
      heading: 'A link to a record this corpus does not hold',
      note:
        'The service refuses with one status for two different situations, so the page says '
        + 'so rather than telling a reader the instrument does not exist. What it can state is '
        + 'the size of what was searched.',
      body: renderProvenance({
        requested: { lex_id: REFUSED_LEX_ID, language: null },
        record: REFUSAL,
        holdings: HOLDINGS,
      }),
    }),
  ]);

  return pages;
}
