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
import { renderCoverage } from '../scripts/coverage.mjs';

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

// The two live coverage payloads, fetched from the production MCP endpoint on 2026-09-01.
//
// These are here because the facet reconciliation rules were a design decision I could have got
// wrong in a way no synthetic fixture would have shown me. The thin fixture has one language row,
// so under it the language table and the document-type table look like the same kind of thing,
// and applying one rule to both looks obviously right.
//
// Real data says otherwise, and says it loudly. Luxembourg's language rows sum to 1,406 works
// against 1,402 held, because 3 works are multilingual. The Union's sum to 4,652 versions against
// 2,366, because 1,212 of its works carry two languages. Had the partition rule been applied to
// languages, both live coverage pages would have refused to render.
//
// The document-type tables do sum exactly, on both publishers, which is the other half of the
// same decision and the reason the stricter rule is worth having where it applies.
const LIVE_LU_COVERAGE = {
  envelope: { freshness: { built_at: '2026-08-15T09:22:08Z', stamp_signature_valid: true } },
  publisher_name: 'Service central de legislation (Legilux)',
  works: 1402,
  scope_expected_works: 1402,
  build_inventory_status: 'complete',
  build_complete: true,
  build_issues: [],
  versions: 4656,
  valid_from_earliest: '1849-03-14',
  valid_from_latest: '2030-09-15',
  document_types: [
    { code: 'LOI', versions: 1536, versions_with_text: 1510 },
    { code: 'RGD', versions: 1200, versions_with_text: 1192 },
    { code: 'RECUEIL', versions: 752, versions_with_text: 72 },
    { code: 'CODE_RECUEIL', versions: 711, versions_with_text: 15 },
    { code: 'CODE', versions: 192, versions_with_text: 189 },
    { code: null, versions: 80, versions_with_text: 0 },
    { code: 'AGD', versions: 39, versions_with_text: 39 },
    { code: 'Constitution', versions: 37, versions_with_text: 37 },
    { code: 'RMIN', versions: 32, versions_with_text: 32 },
    { code: 'RI', versions: 21, versions_with_text: 21 },
    { code: 'AMIN', versions: 17, versions_with_text: 17 },
    { code: 'RGC', versions: 13, versions_with_text: 13 },
    { code: 'PA', versions: 6, versions_with_text: 6 },
    { code: 'CONV', versions: 4, versions_with_text: 4 },
    { code: 'ORD', versions: 3, versions_with_text: 3 },
    { code: 'AGC', versions: 3, versions_with_text: 3 },
    { code: 'TC', versions: 2, versions_with_text: 2 },
    { code: 'ARGD', versions: 2, versions_with_text: 2 },
    { code: 'ST', versions: 1, versions_with_text: 1 },
    { code: 'REG', versions: 1, versions_with_text: 1 },
    { code: 'RBCL', versions: 1, versions_with_text: 1 },
    { code: 'PROT', versions: 1, versions_with_text: 1 },
    { code: 'DIV', versions: 1, versions_with_text: 1 },
    { code: 'A', versions: 1, versions_with_text: 1 },
  ],
  document_types_total: 24,
  facets_truncated: false,
  languages: [
    { code: 'fr', works: 1402, versions: 4656 },
    { code: 'en', works: 2, versions: 2 },
    { code: 'lb', works: 1, versions: 1 },
    { code: 'de', works: 1, versions: 1 },
  ],
  text: { versions_with_text_served: 3163, versions_without_text: 1493 },
  known_gaps: [
    'never-consolidated LU acts (~24,579 as-published lois/RGD) are not ingested; ingestion scheduled, see coverage',
    "coverage density follows the publisher's own digitised consolidations: dense from 2017 onward; sparse before; isolated snapshots back to 1849; forward-dated to 2030",
  ],
};

const LIVE_EU_COVERAGE = {
  envelope: { freshness: { built_at: '2026-08-15T09:01:06Z', stamp_signature_valid: true } },
  publisher_name: 'Publications Office of the EU (EUR-Lex / Cellar)',
  works: 1250,
  scope_expected_works: 1250,
  build_inventory_status: 'complete',
  build_complete: true,
  build_issues: [],
  versions: 2366,
  valid_from_earliest: '1957-03-25',
  valid_from_latest: '2029-03-29',
  document_types: [
    { code: 'DIR', versions: 774, versions_with_text: 774 },
    { code: 'REG', versions: 646, versions_with_text: 646 },
    { code: 'REG_DEL', versions: 457, versions_with_text: 457 },
    { code: 'REG_IMPL', versions: 383, versions_with_text: 383 },
    { code: 'TREATY', versions: 54, versions_with_text: 54 },
    { code: 'CORRIGENDUM', versions: 26, versions_with_text: 26 },
    { code: 'DIR_DEL', versions: 8, versions_with_text: 8 },
    { code: 'DEC_IMPL', versions: 7, versions_with_text: 7 },
    { code: 'DEC', versions: 7, versions_with_text: 7 },
    { code: 'DEC_ENTSCHEID', versions: 2, versions_with_text: 2 },
    { code: 'DIR_IMPL', versions: 1, versions_with_text: 1 },
    { code: 'DEC_DEL', versions: 1, versions_with_text: 1 },
  ],
  document_types_total: 12,
  facets_truncated: false,
  languages: [
    { code: 'fr', works: 1246, versions: 2360 },
    { code: 'en', works: 1216, versions: 2292 },
  ],
  text: { versions_with_text_served: 2366, versions_without_text: 0 },
  known_gaps: [
    '1,250 EU works from the reviewed scope are currently mounted; the wider acquis is not yet ingested, see coverage',
    "coverage follows the publisher's consolidation practice; future-dated versions are provisional",
  ],
};

test('both live coverage payloads render, which is what makes the two facet rules right', () => {
  for (const [name, live] of [
    ['lu-legilux', LIVE_LU_COVERAGE],
    ['eu-eurlex', LIVE_EU_COVERAGE],
  ]) {
    assert.equal(typeof renderCoverage({ coverage: live }), 'string', `${name} refused to render`);
  }

  // The measurements the two rules rest on, asserted rather than described, so that a future
  // change to either rule has to argue with production numbers.
  for (const [name, live] of [
    ['lu-legilux', LIVE_LU_COVERAGE],
    ['eu-eurlex', LIVE_EU_COVERAGE],
  ]) {
    const typeSum = live.document_types.reduce((total, row) => total + row.versions, 0);
    assert.equal(typeSum, live.versions, `${name} document types stopped partitioning`);

    const languageSum = live.languages.reduce((total, row) => total + row.works, 0);
    assert.ok(
      languageSum > live.works,
      `${name} languages stopped overlapping, so this fixture no longer tests the distinction`,
    );
    for (const row of live.languages) {
      assert.ok(row.works <= live.works, `${name} ${row.code} claims more works than are held`);
      assert.ok(row.versions <= live.versions, `${name} ${row.code} claims more states than held`);
    }
  }
});
