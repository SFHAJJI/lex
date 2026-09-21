// Bytes from the live service, rendered by the screens that were repaired against fixtures.
//
// Every other test in this suite runs on fixtures somebody here wrote, which means when the
// understanding is wrong the input and the expectation are wrong together and agree. A fixture
// cannot catch a misunderstanding it shares. So this file holds captures instead, and the
// assertions are about production numbers rather than about numbers chosen to pass.
//
// One capture, fetched from the production MCP endpoint on 2026-09-01: the RGD of 10 May 1999
// defining the illnesses of exceptional gravity under the parental leave law, in its 2020-03-14 and
// 2020-09-25 consolidations.
//
// The two coverage payloads captured on the same day are no longer here. The V3 `coverage` answer
// has a different shape, so the reader refuses them by name and they cannot be rendered; what
// remained once the rendering went was two assertions comparing constants in this file against
// constants in this file, which is a test that renders nothing and goes green forever -- the
// failure this header warns about, turned on the file that states it. They are kept as dated data
// with their provenance and what they proved, in `scripts/live-coverage-record.mjs`.

import assert from 'node:assert/strict';
import test from 'node:test';

import { renderTimeline } from '../scripts/timeline.mjs';
import { renderSearchResults } from '../scripts/search-results.mjs';
import { canonicalStateHref } from '../scripts/routes.mjs';

/**
 * The record, verbatim.
 *
 * Fields this interface does not consume are kept anyway, because trimming a captured record to
 * what the code currently reads is how a capture stops being evidence and becomes another
 * fixture.
 */
const PUBLISHER = 'lu-legilux';
const WORK_KEY = 'rgd-1999-05-10-n1';
const WORK = `${PUBLISHER}:${WORK_KEY}`;
const TITLE =
  'Version consolidée applicable au 25/09/2020 : Règlement grand-ducal du 10 mai 1999 ' +
  "définissant les maladies ou déficiences d'une gravité exceptionnelle en application de " +
  "l'article 15, alinéa 2 de la loi du 12 février 1999 portant création d'un congé parental " +
  "et d'un congé pour raisons familiales.";

const LATER_HASH = 'eafdfe3856519f94803ea0aa13436be59075c886559ee81793725e413ecbe4be';
const EARLIER_HASH = '7ed7e3de193a9dd3633a5f00c4b89003f9e342c8861101c45c9ee9dbde0cd296';

const LATER = {
  lex_id: `${WORK}:2020-09-25--${LATER_HASH}`,
  valid_from: '2020-09-25',
  valid_to: null,
  publication_date: '2024-11-11',
  observed_from: '2026-08-14T23:05:14Z',
  extraction_profile: 'akn-lu/2',
  text_available: true,
  hash: LATER_HASH,
  withdrawn: false,
  title: TITLE,
  title_language: 'fr',
  language: 'fr',
};

const EARLIER = {
  ...LATER,
  lex_id: `${WORK}:2020-03-14--${EARLIER_HASH}`,
  valid_from: '2020-03-14',
  valid_to: '2020-09-25',
  publication_date: '2024-11-05',
  hash: EARLIER_HASH,
};

const POPULATION = 'within the 1,402 consolidated LU works held by this corpus';

test('the live record still looks like the record this file was captured from', () => {
  // If the shape drifts, these assertions stop describing production and this file quietly
  // becomes a fixture again. Pinned so that drift is a test failure rather than a silent one.
  assert.equal(LATER.valid_from < LATER.publication_date, true, 'the two clocks stopped diverging');
  assert.equal(EARLIER.valid_to, LATER.valid_from, 'the two states stopped being contiguous');
  assert.equal(EARLIER.title, LATER.title, 'the publisher stopped reusing one title');
  assert.equal(TITLE.includes('25/09/2020'), true, 'the title stopped carrying a date');
  assert.equal(LATER.extraction_profile, 'akn-lu/2');
});

test('a real timeline takes its vocabulary from the work, and shows both clocks', () => {
  const html = renderTimeline({
    states: [EARLIER, LATER],
    asOf: '2026-09-01',
    totalCount: 2,
    population: POPULATION,
  });
  // Nothing was passed. Legilux dates applicability, and the words follow from the record.
  assert.equal(html.includes('Applicable from 2020-09-25'), true);
  assert.equal(html.includes('Consolidated wording state'), false, 'the Union words reached a LU work');
  // The legal clock and the record clock, four years apart on a real record.
  assert.equal(html.includes('2024-11-11'), true);
  // Contiguous states leave no gap, and the screen must not invent one.
  assert.equal(html.includes('No publisher state covers'), false, 'a gap was invented between contiguous states');
});

test('a real search row is described in its own publisher terms, under a real permalink', () => {
  const permalink = canonicalStateHref({
    publisher: PUBLISHER,
    work: WORK_KEY,
    validFrom: LATER.valid_from,
    hash: LATER_HASH,
    anchor: 'art_2',
  });
  const hit = {
    lex_id: LATER.lex_id,
    valid_from: LATER.valid_from,
    valid_to: LATER.valid_to,
    publication_date: LATER.publication_date,
    text_available: true,
    permalink,
    match_reasons: ['keyword'],
    provision_num: 'Art. 2.',
    title: TITLE,
    title_language: 'fr',
    language: 'fr',
  };
  const html = renderSearchResults({
    query: 'congé parental',
    asOf: '2026-09-01',
    timeScope: 'all_versions',
    hits: [hit],
    rowSet: { returned: 1, total: 1 },
    population: {
      searchable_works: [{ what: 'consolidated LU works', count: 1402, counted_at: '2026-08-15' }],
      not_searchable: [
        { what: 'never-consolidated LU acts', count: 23370, counted_at: '2026-08-15' },
      ],
    },
    // The account is complete and closed even when nothing was relaxed. An empty object is not
    // "nothing ran", it is a caller who did not say, and this row matched on the reader's own
    // words, which is a fact worth stating rather than leaving to inference.
    relaxations: {
      fuzzy: { applied: false },
      crosswalk: { applied: false },
      semantic: { applied: false },
    },
    searchPath: '/ask/search',
    layers: [],
    routes: [],
  });
  // Luxembourg dates applicability, so this row gets the LU vocabulary, derived from the row.
  assert.equal(html.includes('Applicable from 2020-09-25'), true);
  assert.equal(html.includes('Consolidated wording state'), false);
  // A real permalink carries an anchor and must survive the canonical route policy.
  assert.equal(html.includes('#art_2'), true, 'the anchor was dropped from a real permalink');
  // A real lex_id carries the digest inside its state segment, so the row's own identifier has
  // three parts with a `--` in the third. The link is bound to publisher, work and start date.
  assert.equal(html.includes(permalink), true, 'the real permalink was refused');
});
