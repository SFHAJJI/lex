// The refusal catalog: the closed registry rendered as public API surface.
//
// UX spec section 11 asks for "a refusal catalog page" documenting "the closed, versioned
// set with one live example payload each". This is that, and it ships in the product rather
// than living in a test, because a catalog only a maintainer can see is documentation of the
// wrong thing: the audience is a developer deciding how to handle a code they have never
// received.
//
// The page states, per code, whether its payload is fixed by the architect pack or not yet
// settled. That second column is the honest part. Nine codes carry payload keys the pack
// names; one carries its obligation as governing text plus a counter; nine carry nothing yet
// and say so. A catalog that presented all nineteen as equally specified would be a nicer
// page and a false one, and a developer would discover the difference by shipping against it.

import { page } from './render.mjs';
import { REFUSAL_CODES, REQUIRED_PAYLOAD, RETRYABLE, renderRefusalCard } from './refusal-card.mjs';
import { readingUrl } from './urls.mjs';
import { skinFor } from './shells.mjs';
import { RESOURCE_AUTHENTICITY_SCHEMA } from './localization.mjs';

const PUBLISHER = 'preview-synthetic';
const WORK = 'synthetic-preview-work';
const CANDIDATE_A = 'dedbcbe0f53f5e2b41fd98551d5913b0ed56525ec35f7b26a6c0fa9eaad4ba3c';
const CANDIDATE_B = 'cfc9fe90f4f020e99f8da43c8d9e5f74c570eced2ad5d303c6dee7b485eb0212';

function candidate(hash, publicationDate, withdrawn = false) {
  return {
    valid_from: '2004-01-01',
    hash,
    publication_date: publicationDate,
    withdrawn,
    href: readingUrl({ publisher: PUBLISHER, work: WORK, validFrom: '2004-01-01', hash }),
  };
}

const SYNTHETIC_LAW =
  'LEX V3 SYNTHETIC PREVIEW. Article 1. This text is synthetic, has no legal authority, ' +
  'and must not be used for legal research.';

// Authenticity travels with the quotation, per Decision 58, rather than being read off a
// publisher key. The catalog's example carries evidence for its own synthetic resource.
const SYNTHETIC_AUTHENTICITY = {
  schema: RESOURCE_AUTHENTICITY_SCHEMA,
  resource_id: `${PUBLISHER}:${WORK}:2001-01-01`,
  authentic_languages: ['en'],
  basis: 'synthetic preview evidence',
  asserted_by: 'synthetic preview publisher',
  observed_at: '2026-01-01T00:00:00Z',
};

/**
 * One worked example per code, and the set is checked against the registry rather than
 * maintained beside it. Every value is synthetic: a catalog whose examples carry real
 * coordinates teaches a reader to copy them.
 */
export const REFUSAL_EXAMPLES = Object.freeze({
  identifier_unknown: {
    sentence: 'That identifier does not resolve to a held work.',
    payload: {
      population_disclosure:
        '1,402 consolidated LU works and 1,250 EU works are searchable; 23,370 ' +
        'never-consolidated LU acts, of a 24,622 LOI and RGD population, are not.',
      what_would_answer: ['corrected_identifier', 'expanded_official_scope'],
      asserts_absence_of_law: false,
    },
  },
  ambiguous_identifier: {
    sentence: 'That citation matches more than one instrument.',
    payload: { candidates_named: 'synthetic-preview-work-a, synthetic-preview-work-b' },
  },
  out_of_corpus_scope: {
    sentence: 'That instrument is outside the reviewed corpus.',
    payload: {
      population_disclosure: 'The reviewed corpus holds Legilux and EUR-Lex legislation only.',
      what_would_answer: ['expanded_official_scope'],
      asserts_absence_of_law: false,
    },
  },
  no_version_for_date: {
    sentence: 'No publisher state covers 1999-06-01.',
    payload: {
      history_begins: '2001-01-01',
      nearest_earlier: 'none held',
      nearest_later: '2001-01-01',
      what_would_answer: ['new_official_observation'],
      asserts_absence_of_law: false,
    },
  },
  ambiguous_version: {
    sentence: 'Two publisher states cover 2004-06-01.',
    payload: {
      publisher: PUBLISHER,
      work: WORK,
      candidates: [candidate(CANDIDATE_A, '2003-12-01'), candidate(CANDIDATE_B, '2003-12-15')],
    },
  },
  anchor_not_in_version: {
    sentence: 'art_1 is not an anchor in this version.',
    payload: {
      nearest_anchors: ['art_1er', 'art_1er__2'],
      what_would_answer: ['corrected_identifier'],
      asserts_absence_of_law: false,
    },
  },
  language_not_available: {
    sentence: 'This work is held in French only.',
    payload: { languages_held: ['fr'] },
  },
  text_not_available: {
    sentence: 'The publisher records this state but serves no text for it.',
    payload: {
      official_uri: 'https://preview.invalid/synthetic-preview-work/2001-01-01',
      gazette_chain: 'Synthetic gazette A 2001 no 1',
      what_would_answer: ['new_official_observation'],
      asserts_absence_of_law: false,
    },
  },
  text_withheld: {
    sentence: 'The publisher licence does not permit serving this text.',
    payload: { licence: 'synthetic-licence' },
  },
  format_not_available: {
    sentence: 'This state is held as PDF only.',
    payload: { formats_held: ['pdf'] },
  },
  profiles_differ: {
    sentence: 'These two states came from different extraction profiles.',
    payload: { profiles: ['synthetic-pdf/1', 'synthetic-akn/1'] },
  },
  not_transposable: {
    sentence: 'A regulation is not transposed.',
    payload: { execution_acts: ['synthetic-execution-act'] },
  },
  derivation_refused: {
    sentence: 'The transitional provision is served verbatim rather than derived.',
    payload: { reason: 'no official source models transitional provisions as data' },
  },
  retrieval_mode_unavailable: {
    sentence: 'Semantic retrieval is not serving; this search ran on keywords.',
    payload: { fallback_mode: 'keyword' },
  },
  no_corpus_mounted: {
    sentence: 'This build has no index mounted.',
    payload: { mounted_indexes: '0' },
  },
  snapshot_unknown: {
    sentence: 'That snapshot identity is not one this build holds.',
    payload: { snapshots_held: '2026-01-01' },
  },
  upstream_unreachable: {
    sentence: 'The publisher did not answer.',
    payload: { host: 'preview.invalid' },
  },
  rate_limited: {
    sentence: 'Too many requests reached the publisher.',
    payload: { retry_after: '30s' },
  },
  advice_boundary: {
    sentence: 'I cannot apply the law to your situation.',
    governingText: {
      resourceId: SYNTHETIC_AUTHENTICITY.resource_id,
      authenticity: SYNTHETIC_AUTHENTICITY,
      language: 'en',
      text: SYNTHETIC_LAW,
      coverage: 'complete_provision',
      as_of: '2001-01-01',
    },
    // Decision 41 settles the ending as a referral list, not one counter.
    handoff: [
      { label: 'Synthetic counter one', href: 'https://handoff.invalid/one' },
      { label: 'Synthetic counter two', href: 'https://handoff.invalid/two' },
      { label: 'A lawyer', href: 'https://handoff.invalid/lawyer' },
    ],
  },
});

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/** What the contract says this code must carry, in words a developer can act on. */
export function payloadContractOf(code) {
  const requirement = REQUIRED_PAYLOAD[code];
  if (requirement.keys.length > 0) {
    return {
      state: 'specified',
      keys: [...requirement.keys],
      basis: requirement.basis,
    };
  }
  if (requirement.unspecified === true) {
    return { state: 'unspecified', keys: [], basis: requirement.basis };
  }
  return { state: 'enforced elsewhere', keys: [], basis: requirement.basis };
}

function entry(code) {
  const contract = payloadContractOf(code);
  const keys =
    contract.keys.length > 0
      ? contract.keys.map((key) => `<code>${escapeHtml(key)}</code>`).join(', ')
      : 'none required';

  return (
    '<section class="catalog-entry">' +
    `<h2><code>${escapeHtml(code)}</code></h2>` +
    '<dl class="catalog-facts">' +
    `<div class="strip-row"><dt>retryable</dt><dd>${RETRYABLE.has(code) ? 'yes' : 'no'}</dd></div>` +
    `<div class="strip-row"><dt>payload</dt><dd>${escapeHtml(contract.state)}</dd></div>` +
    `<div class="strip-row"><dt>required</dt><dd>${keys}</dd></div>` +
    `<div class="strip-row"><dt>basis</dt><dd>${escapeHtml(contract.basis)}</dd></div>` +
    '</dl>' +
    renderRefusalCard({ code, ...REFUSAL_EXAMPLES[code] }) +
    '</section>'
  );
}

/** The catalog page, in the Gateway shell, because its reader is consuming the API. */
export function renderRefusalCatalog({ locale = 'en' } = {}) {
  const specified = REFUSAL_CODES.filter((code) => payloadContractOf(code).state === 'specified');
  const unspecified = REFUSAL_CODES.filter(
    (code) => payloadContractOf(code).state === 'unspecified',
  );

  return page({
    state: 'refusal-catalog',
    title: 'Refusal catalog',
    locale,
    shell: 'dev',
    density: skinFor('dev').density,
    main:
      '      <p class="eyebrow">Gateway</p>\n' +
      '      <h1>Refusal catalog</h1>\n' +
      `      <p>The closed registry, ${REFUSAL_CODES.length} codes, with one worked example ` +
      'each. A refusal is an answer: it carries a helpful payload, or the governing text, or ' +
      'a route to a human, and never nothing.</p>\n' +
      `      <p class="catalog-honesty">${specified.length} codes have payload keys fixed by ` +
      `the specification. ${unspecified.length} do not yet, and say so below rather than ` +
      'appearing settled. Build against an unspecified payload and it may gain required ' +
      'fields; the code itself is versioned and will not.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      REFUSAL_CODES.map(entry).join(''),
  });
}
