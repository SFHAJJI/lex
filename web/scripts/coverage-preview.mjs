// The coverage page, in the two states that matter: a build that finished and one that did not.
//
// The numbers here are synthetic and deliberately small, because the point of the page is the
// shape of the disclosure rather than the size of the corpus. What is kept from the live shape
// is the proportion that makes it worth having: one document type where almost every held
// state has its text, one where almost none does, and the row the publisher gave no type for,
// which has none at all. A table with only the first kind of row would be a comfortable table.

import { page } from './render.mjs';
import { renderCoverage } from './coverage.mjs';
import { skinFor } from './shells.mjs';

const BUILT_AT = '2026-08-15T09:22:08Z';

const COMPLETE = {
  envelope: { freshness: { built_at: BUILT_AT, stamp_signature_valid: true } },
  publisher_name: 'Synthetic preview publisher',
  works: 40,
  scope_expected_works: 40,
  build_inventory_status: 'complete',
  build_complete: true,
  build_issues: [],
  versions: 120,
  valid_from_earliest: '1849-03-14',
  valid_from_latest: '2030-09-15',
  document_types: [
    { code: 'LOI', versions: 52, versions_with_text: 51 },
    { code: 'RGD', versions: 30, versions_with_text: 30 },
    { code: 'RECUEIL', versions: 25, versions_with_text: 3 },
    { code: null, versions: 13, versions_with_text: 0 },
  ],
  document_types_total: 4,
  facets_truncated: false,
  languages: [
    { code: 'fr', works: 40, versions: 120 },
    { code: 'de', works: 1, versions: 1 },
  ],
  text: { versions_with_text_served: 84, versions_without_text: 36 },
  known_gaps: [
    'never-consolidated acts are not ingested; the reviewed corpus is dated consolidations only',
    'coverage density follows the publisher own digitised consolidations: dense recently, ' +
      'sparse before, isolated snapshots earlier, forward-dated to the publisher horizon',
  ],
};

const INCOMPLETE = {
  ...COMPLETE,
  build_inventory_status: 'partial',
  build_complete: false,
  build_issues: ['one publisher endpoint did not respond', 'one manifest failed verification'],
};

/** The coverage preview, in the Gateway shell, because its reader is checking the service. */
export function renderCoveragePreview({ locale = 'en' } = {}) {
  return page({
    state: 'coverage',
    title: 'Coverage',
    locale,
    shell: 'dev',
    density: skinFor('dev').density,
    main:
      '      <p class="eyebrow">Gateway</p>\n' +
      '      <h1>Coverage</h1>\n' +
      '      <p>This is the page whose job is to say what is missing, so its failure mode is ' +
      'not a wrong answer but a comfortable one: a count with no date, a total with no ' +
      'denominator, a type row that says how many states are held and not how many have ' +
      'text.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      '      <section class="coverage-case"><h2>A build that finished</h2>' +
      renderCoverage({ coverage: COMPLETE }) +
      '</section>\n' +
      '      <section class="coverage-case"><h2>A build that did not</h2>' +
      '<p class="coverage-case-note">No counts at all. A build that did not finish is not a ' +
      'smaller corpus, it is an unknown one, and its figures would read as measurements of ' +
      'what is held.</p>' +
      renderCoverage({ coverage: INCOMPLETE }) +
      '</section>\n',
  });
}
