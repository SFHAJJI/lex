// Two real consolidations of one real Luxembourg regulation, rendered.
//
// Every other test in this suite runs on fixtures I wrote, which means when my understanding is
// wrong the input and the expectation are wrong together and agree. That has happened three
// times: a zero-hit row set of one-of-one, a preview with no row set at all, and a test that
// asserted the false whole-corpus claim. A fixture cannot catch a misunderstanding it shares.
//
// So this file holds bytes from the live service instead: RGD of 10 May 1999 defining the
// illnesses of exceptional gravity under the parental leave law, in its 2020-03-14 and
// 2020-09-25 consolidations, fetched from the production MCP endpoint on 2026-09-01.
//
// What it caught on the first run, which no fixture here contained:
//
//   - Legilux labels EVERY consolidated version of a work with the LATEST consolidation date, so
//     the state applicable 2020-03-14 to 2020-09-25 carries the title "Version consolidee
//     applicable au 25/09/2020". Intervals are half-open, so that title asserts applicability on
//     the first day the state does not cover. The screen called it agreement.
//   - A state applicable from 2020-09-25 whose publication_date is 2024-11-11. The two clocks
//     are four years apart on a real record; the fixtures had them weeks apart.
//   - extraction_profile "akn-lu/2". Every fixture in this suite says "akn-lu/1".
//   - Titles and snippets full of apostrophes, which HTML-escape differently in the two
//     renderers.

import assert from 'node:assert/strict';
import test from 'node:test';

import { renderTimeline } from '../scripts/timeline.mjs';
import { renderSearchResults } from '../scripts/search-results.mjs';

/**
 * The record, verbatim.
 *
 * Fields this interface does not consume are kept anyway, because trimming a captured record to
 * what the code currently reads is how a capture stops being evidence and becomes another
 * fixture.
 */
const WORK = 'lu-legilux:rgd-1999-05-10-n1';
const TITLE =
  'Version consolidée applicable au 25/09/2020 : Règlement grand-ducal du 10 mai 1999 ' +
  "définissant les maladies ou déficiences d'une gravité exceptionnelle en application de " +
  "l'article 15, alinéa 2 de la loi du 12 février 1999 portant création d'un congé parental " +
  "et d'un congé pour raisons familiales.";

const LATER = {
  lex_id: `${WORK}:2020-09-25--eafdfe3856519f94803ea0aa13436be59075c886559ee81793725e413ecbe4be`,
  valid_from: '2020-09-25',
  valid_to: null,
  publication_date: '2024-11-11',
  observed_from: '2026-08-14T23:05:14Z',
  extraction_profile: 'akn-lu/2',
  text_available: true,
  hash: 'eafdfe3856519f94803ea0aa13436be59075c886559ee81793725e413ecbe4be',
  withdrawn: false,
  title: TITLE,
  // The record answers for the language of its own title. The live hit reports language
  // 'fr' for the expression; the timeline asks for title_language separately because a
  // title is not always in the expression's language.
  title_language: 'fr',
  language: 'fr',
};

const EARLIER = {
  ...LATER,
  lex_id: `${WORK}:2020-03-14--7ed7e3de193a9dd3633a5f00c4b89003f9e342c8861101c45c9ee9dbde0cd296`,
  valid_from: '2020-03-14',
  valid_to: '2020-09-25',
  publication_date: '2024-11-05',
  hash: '7ed7e3de193a9dd3633a5f00c4b89003f9e342c8861101c45c9ee9dbde0cd296',
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

test('a real timeline shows both clocks, four years apart', () => {
  const html = renderTimeline({
    states: [EARLIER, LATER],
    asOf: '2026-09-01',
    totalCount: 2,
    population: POPULATION,
  });
  // The legal clock and the record clock, both present and both the publisher's.
  assert.equal(html.includes('2020-09-25'), true);
  assert.equal(html.includes('2024-11-11'), true);
  assert.equal(html.includes('Applicable from 2020-09-25'), true);
  // Contiguous states leave no gap, and the screen must not invent one.
  assert.equal(html.includes('This corpus holds no state covering'), false, 'a gap was invented between contiguous states');
});

test("the publisher's reused title is flagged against the state it does not cover", () => {
  // The whole reason this file exists. Both states carry a title claiming applicability on
  // 25/09/2020. For the later state that is its own start date and agreement. For the earlier
  // state, whose interval ends on that day, it asserts applicability on the first day the state
  // does not cover, and the screen used to call that agreement because the string matched
  // valid_to.
  const html = renderTimeline({
    states: [EARLIER, LATER],
    asOf: '2026-09-01',
    totalCount: 2,
    population: POPULATION,
  });
  assert.equal(
    html.includes('timeline-title-distrust'),
    true,
    'the reused publisher title was accepted as agreeing with a state it does not cover',
  );
  assert.equal(html.includes('up to but not including 2020-09-25'), true);
});

test('a real search row is described in its own publisher terms', () => {
  const hit = {
    lex_id: LATER.lex_id,
    valid_from: LATER.valid_from,
    valid_to: LATER.valid_to,
    publication_date: LATER.publication_date,
    text_available: true,
    permalink:
      'https://law.soufien.lu/lu-legilux/rgd-1999-05-10-n1/2020-09-25--' +
      'eafdfe3856519f94803ea0aa13436be59075c886559ee81793725e413ecbe4be#art_2',
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
});
