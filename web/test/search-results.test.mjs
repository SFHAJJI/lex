import assert from 'node:assert/strict';
import test from 'node:test';

import { DATE_SCOPE, MATCH_REASONS, renderSearchResults } from '../scripts/search-results.mjs';

const PERMALINK =
  'https://law.soufien.lu/preview-synthetic/synthetic-preview-work/2001-01-01--' + 'a'.repeat(64);

function hit(overrides = {}) {
  return {
    lex_id: 'preview-synthetic:synthetic-preview-work:2001-01-01',
    valid_from: '2001-01-01',
    valid_to: '2004-01-01',
    publication_date: '2000-12-01',
    text_available: true,
    permalink: PERMALINK,
    match_reasons: ['keyword'],
    provision_num: 'Art. 1',
    chapter_path: 'Title I, Chapter 2',
    ...overrides,
  };
}

const POPULATION = {
  searchable_works: [
    { what: 'consolidated LU works held by this corpus', count: 1402, counted_at: '2026-08-15' },
    { what: 'reviewed EU works held by this corpus', count: 1250, counted_at: '2026-08-15' },
  ],
  not_searchable: [
    {
      what: 'LU acts of a 24,622 LOI and RGD population that never receive a consolidated edition',
      count: 23370,
      counted_at: '2026-08-15',
    },
  ],
};

const RELAXATIONS = {
  fuzzy: { applied: false },
  crosswalk: { applied: false },
  semantic: { applied: false },
};

const GOOD = {
  query: 'security deposit how many months landlord',
  semantics: 'publisher_applicability',
  asOf: '2026-09-01',
  hits: [hit()],
  rowSet: { returned: 1, total: 1 },
  population: POPULATION,
  relaxations: RELAXATIONS,
  searchPath: '/ask/search?q=deposit',
};

test('results are scoped to an explicit date in the publisher vocabulary', () => {
  assert.deepEqual(Object.keys(DATE_SCOPE), [
    'publisher_applicability',
    'official_consolidation_state',
  ]);

  const lu = renderSearchResults(GOOD);
  assert.ok(lu.includes('Provisions as applicable on 2026-09-01'));
  assert.ok(!lu.includes('Wording states covering'), 'the EU vocabulary leaked onto a LU search');

  const eu = renderSearchResults({ ...GOOD, semantics: 'official_consolidation_state' });
  assert.ok(eu.includes('Wording states covering 2026-09-01'));
  assert.ok(!eu.includes('Provisions as applicable'), 'the LU vocabulary leaked onto an EU search');

  for (const bad of [undefined, '', 'in_force', 'toString']) {
    assert.throws(() => renderSearchResults({ ...GOOD, semantics: bad }), /is not one of/);
  }

  // The date is explicit even when it is today, because today is the date nobody checks.
  for (const bad of [undefined, '', 'today', '2026-99-99']) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, asOf: bad }),
      /explicitly, even when it is today/,
      `asOf=${JSON.stringify(bad)} was rendered`,
    );
  }
});

test('a hit list carries the same population disclosure an empty one does', () => {
  const html = renderSearchResults(GOOD);
  assert.ok(html.includes('1402 consolidated LU works'));
  assert.ok(html.includes('23370'), 'what is not searchable must be disclosed beside what is');
  assert.ok(html.includes('counted 2026-08-15'), 'a count with no date outlives its measurement');

  // A reader who got results is exactly the reader who stops checking, so the list with hits
  // cannot disclose less than the list without.
  for (const field of ['searchable_works', 'not_searchable']) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, population: { ...POPULATION, [field]: [] } }),
      new RegExp(`needs ${field}`),
    );
  }
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        population: {
          ...POPULATION,
          searchable_works: [{ what: 'works', count: 1402 }],
        },
      }),
    /must say when it was counted/,
  );
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        population: {
          ...POPULATION,
          searchable_works: [{ count: 1402, counted_at: '2026-08-15' }],
        },
      }),
    /must say what it counts/,
  );
});

test('a list that was cut names its total', () => {
  const html = renderSearchResults({ ...GOOD, rowSet: { returned: 1, total: 47 } });
  assert.ok(html.includes('Showing 1 of 47 matching passages.'));
  assert.ok(!renderSearchResults(GOOD).includes('Showing'), 'a complete list claimed truncation');

  // The row set is checked against the rows, so a caller cannot say complete and be believed.
  assert.throws(
    () => renderSearchResults({ ...GOOD, rowSet: { returned: 3, total: 47 } }),
    /one of those two numbers is wrong/,
  );
  assert.throws(
    () => renderSearchResults({ ...GOOD, rowSet: { returned: 1, total: 0 } }),
    /returned more rows than it holds/,
  );
  for (const bad of [undefined, { total: 1 }, { returned: 1 }, { returned: 1, total: 'many' }]) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, rowSet: bad }),
      /how many rows it returned and how many there were/,
      `rowSet=${JSON.stringify(bad)} was rendered`,
    );
  }
});

test('every row says why it matched, from the closed set', () => {
  assert.deepEqual([...MATCH_REASONS], ['exact_title', 'keyword', 'interpreted', 'semantic']);

  const html = renderSearchResults({
    ...GOOD,
    hits: [hit({ match_reasons: ['exact_title'] })],
  });
  assert.ok(html.includes('matched on title, not wording'));

  const interpreted = renderSearchResults({
    ...GOOD,
    hits: [hit({ match_reasons: ['interpreted'] })],
  });
  assert.ok(interpreted.includes('interpreted (editorial layer, versioned, non-official)'));

  for (const bad of [undefined, [], ['fuzzy'], 'keyword']) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ match_reasons: bad })] }),
      /does not say why it matched|is not a match reason/,
      `match_reasons=${JSON.stringify(bad)} was rendered`,
    );
  }
});

test('a relaxation that ran cannot be silent', () => {
  // The expansions are the evidence one ran. A reader who asked about a deposit and was
  // answered about a different word has not been answered.
  assert.throws(
    () => renderSearchResults({ ...GOOD, expansions: ['many -> mady', 'many -> man'] }),
    /no relaxation is declared as applied/,
  );

  const disclosed = renderSearchResults({
    ...GOOD,
    expansions: ['many -> mady'],
    relaxations: { ...RELAXATIONS, fuzzy: { applied: true, expansions: ['many -> mady'] } },
  });
  // Escaped, as any publisher-supplied token is.
  assert.ok(disclosed.includes('many -&gt; mady'));

  // And a relaxation that does not declare itself is refused, because a screen that does not
  // know cannot disclose.
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        relaxations: { fuzzy: { applied: false }, crosswalk: { applied: false } },
      }),
    /must declare whether it was applied/,
  );
});

test('there is at most one governing instrument and it says why', () => {
  const html = renderSearchResults({
    ...GOOD,
    governing: {
      lex_id: 'preview-synthetic:synthetic-preview-work',
      why: 'Your question names this instrument by title.',
    },
  });
  assert.ok(html.includes('The instrument your question names'));
  assert.ok(html.includes('not a second answer'));
  // And it comes before the ranked rows, because keyword ranking alone puts an unrelated
  // instrument above the governing one.
  assert.ok(html.indexOf('governing') < html.indexOf('<ol class="hits">'));

  assert.throws(
    () => renderSearchResults({ ...GOOD, governing: [{ lex_id: 'a' }, { lex_id: 'b' }] }),
    /two cards are two answers to one question/,
  );
  assert.throws(
    () => renderSearchResults({ ...GOOD, governing: { lex_id: 'a' } }),
    /says why it is the answer/,
  );
});

test('zero hits is a card that names what ran, never an empty list', () => {
  const html = renderSearchResults({
    ...GOOD,
    hits: [],
    // Zero rows and a zero total. This fixture inherited one of one from GOOD, which is
    // the defect O9 names: an empty page of a nonempty result set read as a corpus miss.
    rowSet: { returned: 0, total: 0 },
    layers: [
      { name: 'work_resolution', outcome: 'not_run', language: 'en' },
      { name: 'exact_identifier', outcome: 'ran', language: 'en' },
      { name: 'keyword', outcome: 'ran', language: 'en' },
      { name: 'lay_vocabulary_bridge', outcome: 'not_applicable', language: 'en' },
      { name: 'semantic', outcome: 'unavailable', language: 'en' },
    ],
    expansions: ['many -> mady', 'many -> man'],
    routes: [
      { label: 'Search Legilux', publisher: 'lu-legilux', uri: 'https://legilux.public.lu/' },
    ],
  });
  assert.ok(!html.includes('<ol class="hits">'), 'an empty hit list rendered');
  assert.ok(html.includes('many -&gt; mady'), 'the query was silently rewritten');
  assert.ok(html.includes('23370'), 'the population is missing from the one result that needs it');
  assert.ok(html.includes('legilux.public.lu'), 'a dead end with no next step');
});

test('the words in force never reach a hit row', () => {
  assert.ok(!renderSearchResults(GOOD).includes('in force'));
  for (const value of ['in_force', null, false]) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ binding_status: value })] }),
      /belongs in the dossier status strip/,
      `binding_status=${JSON.stringify(value)} reached a row`,
    );
  }
});

test('a row carries its hash-carrying permalink and whether its text is held', () => {
  const html = renderSearchResults({ ...GOOD, hits: [hit({ text_available: false })] });
  assert.ok(html.includes('no text held'));
  assert.ok(renderSearchResults(GOOD).includes('>text held<'));

  for (const bad of [undefined, 'https://law.soufien.lu/lu/work/2001-01-01', '']) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ permalink: bad })] }),
      /needs its hash-carrying permalink/,
      `permalink=${JSON.stringify(bad)} was offered as stable`,
    );
  }
  assert.throws(
    () => renderSearchResults({ ...GOOD, hits: [hit({ text_available: undefined })] }),
    /does not say whether its text is held/,
  );
});

test('a row title carries the language it is written in', () => {
  const html = renderSearchResults({
    ...GOOD,
    hits: [hit({ title: 'An English title of a Union act', title_language: 'en' })],
  });
  assert.ok(html.includes('lang="en"'));
  assert.ok(!html.includes('lang="fr"'), 'defaulted to French');

  for (const bad of [undefined, '', 'french']) {
    assert.throws(
      () =>
        renderSearchResults({ ...GOOD, hits: [hit({ title: 'A title', title_language: bad })] }),
      /does not say what language it is in/,
    );
  }
});

test('a row that cannot be placed is refused rather than listed', () => {
  for (const [field, value, pattern] of [
    ['lex_id', '', /has no lex_id/],
    ['valid_from', '2001-13-01', /valid_from is not a calendar date/],
    ['valid_to', 'soon', /neither null nor a calendar date/],
    ['publication_date', undefined, /publication_date is not a calendar date/],
  ]) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ [field]: value })] }),
      pattern,
      `${field}=${String(value)} was listed`,
    );
  }
});

test('results echo the query they answer', () => {
  assert.ok(renderSearchResults(GOOD).includes('security deposit how many months landlord'));
  for (const bad of [undefined, '   ']) {
    assert.throws(() => renderSearchResults({ ...GOOD, query: bad }), /echo the query/);
  }
});

test('values are escaped rather than trusted', () => {
  const html = renderSearchResults({
    ...GOOD,
    query: '<img src=x onerror=alert(1)> & more',
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
  assert.ok(html.includes('&amp; more'));
});

test('an empty page of a nonempty result set is not a corpus miss', () => {
  // O9. The no-hit branch was reached before the row set was validated, so zero rows out of a
  // nine-row result set rendered the card that says nothing in the corpus matches. That is a
  // page boundary being published to the reader as an absence of law.
  for (const rowSet of [
    { returned: 0, total: 9 },
    { returned: 9, total: 9 },
    { returned: 0, total: 1 },
  ]) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [], rowSet }),
      /empty page of a nonempty result set/,
      `${JSON.stringify(rowSet)} rendered a corpus miss`,
    );
  }
});

test('a hits value that is not a list is a transport fact, not an absence of law', () => {
  for (const hits of [undefined, null, {}, 'none', 0]) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits, rowSet: { returned: 0, total: 0 } }),
      /not a list/,
      `${JSON.stringify(hits)} was rendered as an absence`,
    );
  }
});

test('the row set is validated even when no rows came back', () => {
  for (const rowSet of [undefined, null, { returned: 0 }, { total: 0 }, { returned: -1, total: 0 }]) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [], rowSet }),
      /how many rows it returned|counts rows/,
      `${JSON.stringify(rowSet)} skipped row-set validation on the empty path`,
    );
  }
});

test('O1: a permalink is validated by the shared route policy, not by containing a separator', () => {
  // The guard was `permalink.includes('--')`. Every string below satisfies that and was
  // rendered as a working href a few lines later.
  const hostile = [
    'javascript:alert(1)--x',
    'JavaScript:alert(1)--' + 'a'.repeat(64),
    'data:text/html,<script>1</script>--' + 'a'.repeat(64),
    '//evil.example/preview-synthetic/w/2001-01-01--' + 'a'.repeat(64),
    'https://evil.example/preview-synthetic/w/2001-01-01--' + 'a'.repeat(64),
    'http://law.soufien.lu/preview-synthetic/w/2001-01-01--' + 'a'.repeat(64),
    'https://user:pw@law.soufien.lu/preview-synthetic/w/2001-01-01--' + 'a'.repeat(64),
    'https://law.soufien.lu:8443/preview-synthetic/w/2001-01-01--' + 'a'.repeat(64),
    'https://law.soufien.lu/preview-synthetic/w/2001-01-01--short',
    'https://law.soufien.lu/preview-synthetic/w/not-a-date--' + 'a'.repeat(64),
    'https://law.soufien.lu/preview-synthetic/w/2001-01-01--' + 'A'.repeat(64),
    '--' + 'a'.repeat(64),
  ];
  for (const permalink of hostile) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ permalink })] }),
      /canonical same-origin state URL/,
      `${permalink} was accepted as a state permalink`,
    );
  }
});

test('O1: a permalink must name the state its own row describes', () => {
  // Well formed and pointing somewhere else. Every other field on the row stays true, so
  // nothing but this check notices that the link and the row disagree.
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        hits: [
          hit({
            permalink:
              'https://law.soufien.lu/preview-synthetic/synthetic-preview-work/1999-01-01--' +
              'a'.repeat(64),
          }),
        ],
      }),
    /links to preview-synthetic:synthetic-preview-work applicable from 1999-01-01/,
  );
});

test('O1-R2: a permalink is bound to the row work, not only to its date', () => {
  // Codex's probe: same start date, different work. Consolidations published together share a
  // start date routinely, so comparing valid_from alone sent a reader to a different instrument
  // with every field on the row still true of the row.
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        hits: [
          hit({
            permalink:
              'https://law.soufien.lu/preview-synthetic/a-different-work/2001-01-01--' +
              'a'.repeat(64),
          }),
        ],
      }),
    /links to preview-synthetic:a-different-work/,
    'a link to another work was accepted because the dates agreed',
  );

  // And a different publisher, same work name and date.
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        hits: [
          hit({
            permalink:
              'https://law.soufien.lu/eu-eurlex/synthetic-preview-work/2001-01-01--' +
              'a'.repeat(64),
          }),
        ],
      }),
    /links to eu-eurlex:synthetic-preview-work/,
  );
});

test('O1-R2: an explicit default port is refused, not normalised away', () => {
  // URL reports an empty port for https://host:443/, so checking parsed.port made the claim
  // that explicit ports are refused simply false.
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        hits: [
          hit({
            permalink:
              'https://law.soufien.lu:443/preview-synthetic/synthetic-preview-work/2001-01-01--' +
              'a'.repeat(64),
          }),
        ],
      }),
    /canonical same-origin state URL/,
  );
});
