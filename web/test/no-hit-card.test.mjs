import assert from 'node:assert/strict';
import test from 'node:test';

import { LAYERS, LAYER_OUTCOMES, renderNoHitCard } from '../scripts/no-hit-card.mjs';

// Shaped after the live zero-hit measured on the running service: an English lay query
// against French statute, the resolver lane not requested, and two nonsense expansions.
const LAYERS_RUN = [
  { name: 'work_resolution', outcome: 'not_run', language: 'en' },
  { name: 'keyword', outcome: 'ran', language: 'en' },
  { name: 'semantic', outcome: 'unavailable', language: 'en' },
];

// 23,370, not 24,579. 31-v3-spec records the live wording as wrong as phrased: the LOI+RGD
// population is 24,622, of which 1,252 have consolidations, so the never-consolidated set is
// 23,370. The live service still serves the superseded figure, which is exactly why a count
// here has to carry the date it was counted.
const POPULATION = {
  searchable_works: [
    { what: 'consolidated Luxembourg works', count: 1402, counted_at: '2026-08-15' },
    { what: 'European Union works', count: 1250, counted_at: '2026-08-15' },
  ],
  not_searchable: [
    {
      what: 'never-consolidated Luxembourg acts',
      count: 23370,
      note: 'of a 24,622 LOI and RGD population, as published rather than consolidated',
      counted_at: '2026-08-15',
    },
  ],
};

const ROUTES = [
  {
    label: 'Search Legilux',
    publisher: 'lu-legilux',
    uri: 'https://legilux.public.lu/search',
  },
  {
    label: 'Search EUR-Lex',
    publisher: 'eu-eurlex',
    uri: 'https://eur-lex.europa.eu/search.html',
  },
];

const GOOD = {
  query: 'security deposit how many months landlord',
  layers: LAYERS_RUN,
  population: POPULATION,
  expansions: ['many -> mady', 'many -> man'],
  routes: ROUTES,
};

test('a no-hit card cannot be rendered without saying what ran', () => {
  for (const layers of [undefined, [], null]) {
    assert.throws(
      () => renderNoHitCard({ ...GOOD, layers }),
      /must name which layers ran/,
      `${JSON.stringify(layers)} was accepted`,
    );
  }
});

test('every layer names its outcome and its language', () => {
  // A reader who typed English and got nothing is owed the fact that the statute is French.
  assert.throws(
    () => renderNoHitCard({ ...GOOD, layers: [{ name: 'keyword', outcome: 'ran' }] }),
    /must say which language it ran in/,
  );
  assert.throws(
    () =>
      renderNoHitCard({ ...GOOD, layers: [{ name: 'keyword', outcome: 'ok', language: 'en' }] }),
    /an unreported outcome reads as success/,
  );
  assert.throws(
    () =>
      renderNoHitCard({
        ...GOOD,
        layers: [{ name: 'guesswork', outcome: 'ran', language: 'en' }],
      }),
    /is not a retrieval layer this interface can name/,
  );
  assert.throws(
    () =>
      renderNoHitCard({
        ...GOOD,
        layers: [
          { name: 'keyword', outcome: 'ran', language: 'en' },
          { name: 'keyword', outcome: 'not_run', language: 'fr' },
        ],
      }),
    /reported twice/,
  );
});

test('a layer that did not run is reported, not omitted', () => {
  // The live query_plan says work_resolution_status "not_requested". Omitting it would read
  // as though the resolver had run and found nothing, which is a different fact.
  const html = renderNoHitCard(GOOD);
  assert.ok(html.includes('resolved the query against work titles'));
  assert.ok(html.includes('did not run'));
  assert.ok(html.includes('was unavailable'));
  assert.deepEqual([...LAYER_OUTCOMES], ['ran', 'not_run', 'unavailable']);
  assert.ok(LAYERS.includes('lay_vocabulary_bridge'));
});

test('the population says what is not searchable, not only what is', () => {
  const html = renderNoHitCard(GOOD);
  assert.ok(html.includes('1402'));
  assert.ok(html.includes('23370'));
  assert.ok(html.includes('are not'));

  // A disclosure listing only what is held is an advertisement.
  assert.throws(
    () =>
      renderNoHitCard({
        ...GOOD,
        population: { ...POPULATION, not_searchable: [] },
      }),
    /needs not_searchable/,
  );
  assert.throws(
    () =>
      renderNoHitCard({ ...GOOD, population: { ...POPULATION, searchable_works: [] } }),
    /needs searchable_works/,
  );
});

test('a population count is a whole number, not an impression', () => {
  for (const count of ['about 24,579', 24579.5, -1, null, undefined]) {
    assert.throws(
      () =>
        renderNoHitCard({
          ...GOOD,
          population: {
            ...POPULATION,
            not_searchable: [{ what: 'never-consolidated acts', count, counted_at: '2026-08-15' }],
          },
        }),
      /must carry a whole count/,
      `${JSON.stringify(count)} was accepted as a count`,
    );
  }
});

test('a count carries the date it was counted, so it can be checked against the index', () => {
  // The failure this prevents is on the live service right now: a hand-written "~24,579"
  // outliving the measurement that produced it, with nothing on the page to date it.
  const html = renderNoHitCard(GOOD);
  assert.ok(html.includes('counted 2026-08-15'));
  for (const countedAt of [undefined, '', '2026-99-99', 'at build', '2026-08']) {
    assert.throws(
      () =>
        renderNoHitCard({
          ...GOOD,
          population: {
            ...POPULATION,
            not_searchable: [{ what: 'never-consolidated acts', count: 23370, counted_at: countedAt }],
          },
        }),
      /must say when it was counted/,
      `${JSON.stringify(countedAt)} was accepted as a count date`,
    );
  }
});

test('an expansion the search applied is shown verbatim', () => {
  // The live service answers this query with ["many -> mady", "many -> man"], which is
  // nonsense. A reader who cannot see it has no way to understand the zero hits.
  const html = renderNoHitCard(GOOD);
  assert.ok(html.includes('many -&gt; mady'));
  assert.ok(html.includes('many -&gt; man'));
  assert.ok(html.includes('applied by the search, not by you'));

  const none = renderNoHitCard({ ...GOOD, expansions: [] });
  assert.ok(!none.includes('was expanded before it ran'));
});

test('a publisher route goes through the one route policy', () => {
  const html = renderNoHitCard(GOOD);
  assert.ok(html.includes('href="https://legilux.public.lu/search"'));
  assert.ok(html.includes('href="https://eur-lex.europa.eu/search.html"'));

  // A route this surface cannot vouch for becomes inert text with the reason, never a link.
  const bad = renderNoHitCard({
    ...GOOD,
    routes: [{ label: 'Search Legilux', publisher: 'lu-legilux', uri: 'https://evil.example/x' }],
  });
  assert.ok(!bad.includes('<a href'));
  assert.ok(bad.includes('not linked'));

  assert.throws(() => renderNoHitCard({ ...GOOD, routes: [] }), /must offer the publisher/);
});

test('the card says the absence is about the corpus, not about the law', () => {
  const html = renderNoHitCard(GOOD);
  assert.ok(html.includes('not evidence that the instrument or the law does not exist'));
  assert.ok(html.includes('security deposit how many months landlord'));
  // Styled as an absence, not as an error, and never announced as one.
  assert.ok(html.includes('token--hole'));
  assert.ok(!html.includes('role="alert"'));
  assert.ok(!html.includes('aria-live'));
});

test('values are escaped rather than trusted', () => {
  const html = renderNoHitCard({ ...GOOD, query: '<img src=x onerror=alert(1)>' });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
});

test('a card whose layers all failed to run makes no claim about the corpus', () => {
  // O10. Every layer reporting not_run or unavailable still printed "Nothing in the held
  // records matches", which is an absence claim resting on a search that never happened. A
  // reader cannot tell that sentence apart from a real corpus miss.
  const html = renderNoHitCard({
    ...GOOD,
    layers: [
      { name: 'exact_identifier', outcome: 'not_run', language: 'en' },
      { name: 'keyword', outcome: 'not_run', language: 'en' },
      { name: 'semantic', outcome: 'unavailable', language: 'en' },
    ],
  });
  assert.equal(
    html.includes('Nothing in the held records matches'),
    false,
    'the card claimed the corpus holds no match while nothing was searched',
  );
  assert.equal(html.includes('No search of the held records completed'), true);
  assert.equal(html.includes('Nothing was searched'), true);
  // The disclaimer is invariant and must survive every branch.
  assert.equal(html.includes('not evidence that the instrument or the law does not exist'), true);
});

test('a card whose layers partly ran scopes its sentence to what ran', () => {
  const html = renderNoHitCard({
    ...GOOD,
    layers: [
      { name: 'keyword', outcome: 'ran', language: 'fr' },
      { name: 'semantic', outcome: 'not_run', language: 'fr' },
    ],
  });
  assert.equal(
    html.includes('Nothing in the held records matches'),
    false,
    'a partial search was reported as a statement about all held records',
  );
  assert.equal(html.includes('in the searches that ran'), true);
  assert.equal(html.includes('searched provision wording by keyword'), true);
  assert.equal(html.includes('not evidence that the instrument or the law does not exist'), true);
});
