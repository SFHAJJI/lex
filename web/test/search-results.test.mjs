import assert from 'node:assert/strict';
import test from 'node:test';

import {
  DATE_SCOPE,
  MATCH_REASONS,
  TIME_SCOPES,
  renderSearchResults,
} from '../scripts/search-results.mjs';
import { canonicalStateHref } from '../scripts/routes.mjs';

const HASH = 'a'.repeat(64);

// Minted through the builder the route policy checks against, so the fixture cannot assert a
// grammar the policy has stopped producing.
const permalinkFor = ({ publisher, work, validFrom }) =>
  canonicalStateHref({ publisher, work, validFrom, hash: HASH });

const PERMALINK = permalinkFor({
  publisher: 'preview-synthetic',
  work: 'synthetic-preview-work',
  validFrom: '2001-01-01',
});

function hit({ publisher = 'preview-synthetic', work = 'synthetic-preview-work', ...overrides } = {}) {
  const validFrom = overrides.valid_from ?? '2001-01-01';
  return {
    lex_id: `${publisher}:${work}:${validFrom}`,
    // Covers the operative date the heading claims. It did not, and every fixture in this file
    // was therefore a row listed as applicable on a date it does not cover.
    valid_from: validFrom,
    valid_to: null,
    publication_date: '2000-12-01',
    text_available: true,
    permalink: permalinkFor({ publisher, work, validFrom }),
    match_reasons: ['keyword'],
    provision_num: 'Art. 1',
    chapter_path: 'Title I, Chapter 2',
    ...overrides,
  };
}

/** A Union row, so the other vocabulary is a record rather than a flag. */
const euHit = (overrides = {}) =>
  hit({ publisher: 'eu-eurlex', work: '32016R0679', ...overrides });

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
  timeScope: 'as_of',
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

  // The same call over Union rows: the words change because the records did, not because a
  // caller said so.
  const eu = renderSearchResults({ ...GOOD, hits: [euHit()] });
  assert.ok(eu.includes('Wording states covering 2026-09-01'));
  assert.ok(!eu.includes('Provisions as applicable'), 'the LU vocabulary leaked onto an EU search');

  // Passing one is refused rather than ignored, including the value that used to be correct.
  for (const declared of ['publisher_applicability', 'official_consolidation_state', 'toString', null]) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, semantics: declared }),
      /do not take a date vocabulary/,
      `${String(declared)} was accepted as a vocabulary`,
    );
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

test('a mixed list gets neutral wording rather than one publisher words', () => {
  // A list drawn from two publishers has no single vocabulary, and a heading that picks one
  // states a claim about the rows it does not describe. Every row still keeps its own words.
  const mixed = renderSearchResults({
    ...GOOD,
    hits: [hit(), euHit()],
    rowSet: { returned: 2, total: 2 },
  });
  assert.ok(mixed.includes("States covering 2026-09-01, each in its own publisher&#39;s terms"));
  assert.ok(!mixed.includes('Provisions as applicable on'), 'one publisher words over both');
  assert.ok(!mixed.includes('Wording states covering 2026'), 'one publisher words over both');
  assert.ok(mixed.includes('Applicable from 2001-01-01'), 'the LU row lost its own words');
  assert.ok(mixed.includes('Consolidated wording state from 2001-01-01'), 'the EU row lost its own words');

  const mixedAll = renderSearchResults({
    ...GOOD,
    timeScope: 'all_versions',
    hits: [hit(), euHit()],
    rowSet: { returned: 2, total: 2 },
  });
  assert.ok(mixedAll.includes('not narrowed to one date'));
  assert.ok(mixedAll.includes("each in its own publisher&#39;s terms"));
});

test('a publisher this interface has not classified is refused rather than given words', () => {
  for (const publisher of ['xx-unknown', 'constructor', 'toString']) {
    assert.throws(
      () =>
        renderSearchResults({
          ...GOOD,
          hits: [hit({ publisher, work: 'some-work' })],
        }),
      /is not a publisher this interface has classified/,
      `${JSON.stringify(publisher)} was given a vocabulary`,
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
    relaxations: {
      ...RELAXATIONS,
      crosswalk: { applied: true, understood_as: 'garantie locative', version: 'crosswalk/1', reviewed_on: '2026-08-15' },
    },
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
    /fuzzy expansion is not declared as applied/,
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

test('a permalink is a canonical same-origin state URL, bound to the row it sits on', () => {
  // The control. A refusal that also refuses the true case is not a check, so the legitimate
  // link is asserted to render before anything hostile is asserted to be refused.
  const control = renderSearchResults(GOOD);
  assert.ok(control.includes(PERMALINK), 'the real permalink was refused');
  assert.ok(control.includes('Read this state'));

  // Containing the digest separator was the entire guard, so any host carrying "--" passed and
  // was rendered as a working href. Every one of these describes the row correctly on every
  // visible field while the link goes somewhere else.
  const hostile = [
    ['another host', `https://evil.example/preview-synthetic/synthetic-preview-work/2001-01-01--${HASH}`],
    ['a scheme that is not https', `http://law.soufien.lu/preview-synthetic/synthetic-preview-work/2001-01-01--${HASH}`],
    ['protocol-relative', `//evil.example/preview-synthetic/synthetic-preview-work/2001-01-01--${HASH}`],
    ['a javascript URL carrying the separator', 'javascript:alert(1)--x'],
    // `URL` normalises the default port away, so `parsed.port` is empty here and a check on it
    // alone would pass this. The raw authority is what says the host is evil.example.
    ['userinfo dressed as this host', `https://law.soufien.lu:443@evil.example/preview-synthetic/synthetic-preview-work/2001-01-01--${HASH}`],
    ['userinfo with no port', `https://law.soufien.lu@evil.example/preview-synthetic/synthetic-preview-work/2001-01-01--${HASH}`],
    ['a query string', `https://law.soufien.lu/preview-synthetic/synthetic-preview-work/2001-01-01--${HASH}?next=https://evil.example`],
    ['a backslash', `https://law.soufien.lu/preview-synthetic\\synthetic-preview-work/2001-01-01--${HASH}`],
    ['no digest', 'https://law.soufien.lu/preview-synthetic/synthetic-preview-work/2001-01-01'],
  ];
  for (const [what, permalink] of hostile) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ permalink })] }),
      /canonical same-origin state URL/,
      `${what} was rendered as a permalink`,
    );
  }

  // Bound on every coordinate, not the date alone. Consolidations published together routinely
  // share a start date, so the same-date-different-work case is the common one.
  const bound = [
    [
      'a different work, same host and same date',
      permalinkFor({ publisher: 'preview-synthetic', work: 'another-preview-work', validFrom: '2001-01-01' }),
    ],
    [
      'a different publisher on a Luxembourg row',
      permalinkFor({ publisher: 'eu-eurlex', work: 'synthetic-preview-work', validFrom: '2001-01-01' }),
    ],
    [
      'a different state of the same work',
      permalinkFor({ publisher: 'preview-synthetic', work: 'synthetic-preview-work', validFrom: '2004-01-01' }),
    ],
  ];
  for (const [what, permalink] of bound) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ permalink })] }),
      /the link and the row must name one state/,
      `${what} was linked from a row describing something else`,
    );
  }

  // A row whose identifier cannot be read cannot have its link bound to it at all.
  for (const lex_id of ['garbage', 'preview-synthetic:work', 'a:b:']) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ lex_id })] }),
      /does not name a publisher, a work and a state/,
      `lex_id=${JSON.stringify(lex_id)} was listed`,
    );
  }

  // The publisher's own anchor survives, because a permalink to a provision is the useful one.
  const anchored = canonicalStateHref({
    publisher: 'preview-synthetic',
    work: 'synthetic-preview-work',
    validFrom: '2001-01-01',
    hash: HASH,
    anchor: 'art_2',
  });
  assert.ok(
    renderSearchResults({ ...GOOD, hits: [hit({ permalink: anchored })] }).includes('#art_2'),
    'the anchor was dropped from a legitimate permalink',
  );
});

test('a row title carries the language it is written in', () => {
  const html = renderSearchResults({
    ...GOOD,
    hits: [hit({ title: 'An English title of a Union act', title_language: 'en' })],
  });
  assert.ok(html.includes('lang="en"'));
  assert.ok(!html.includes('lang="fr"'), 'defaulted to French');

  // The record answers for its own title when the wire carries no separate field, which is
  // every live hit today. That is reading the expression's own claim, not guessing: a
  // hardcoded constant was the defect this guard was written for, and it is a different thing.
  const fromRecord = renderSearchResults({
    ...GOOD,
    hits: [hit({ title: 'Version consolidee', language: 'fr' })],
  });
  assert.ok(fromRecord.includes('lang="fr"'), 'the record language did not reach the title');

  // An explicit title language still wins, for an expression served under a title the
  // publisher writes in another language.
  const explicit = renderSearchResults({
    ...GOOD,
    hits: [hit({ title: 'An English title', language: 'fr', title_language: 'en' })],
  });
  assert.ok(explicit.includes('lang="en"'), 'the explicit title language was ignored');

  // Neither present is still refused, because then nothing has said anything.
  for (const bad of ['', 'french', 'FR']) {
    assert.throws(
      () =>
        renderSearchResults({
          ...GOOD,
          hits: [hit({ title: 'A title', language: bad, title_language: undefined })],
        }),
      /neither it nor the record says what language it is in/,
      `language=${JSON.stringify(bad)} was accepted for a title`,
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

test('a row under a date-scoped heading must actually cover that date', () => {
  // The heading asserts "Provisions as applicable on X" and nothing compared the rows to X, so
  // a long-superseded state could be listed among them under that exact claim.
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        hits: [hit({ valid_from: '2001-01-01', valid_to: '2004-01-01' })],
      }),
    /does not cover 2026-09-01, while the heading says these rows are the ones applicable/,
    'a superseded state was listed as applicable on a date it does not cover',
  );

  // Half-open, the same reading the resolver uses: the end date itself is not covered.
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        hits: [hit({ valid_from: '2001-01-01', valid_to: '2026-09-01' })],
      }),
    /does not cover 2026-09-01/,
  );
  // And the start date itself is.
  assert.ok(
    renderSearchResults({
      ...GOOD,
      hits: [hit({ valid_from: '2026-09-01', valid_to: null })],
    }).includes('Provisions as applicable on 2026-09-01'),
  );
});

test('results say whether they were narrowed to a date at all', () => {
  assert.deepEqual([...TIME_SCOPES], ['as_of', 'all_versions']);

  // The live service answers all_versions by default, and this screen printed the date-scoped
  // heading over every result set regardless.
  const all = renderSearchResults({
    ...GOOD,
    timeScope: 'all_versions',
    hits: [hit({ valid_from: '2001-01-01', valid_to: '2004-01-01' })],
  });
  assert.ok(all.includes('not narrowed to one date'));
  assert.ok(!all.includes('Provisions as applicable on'), 'a date scope was claimed anyway');
  assert.ok(all.includes('Operative date: 2026-09-01'), 'the operative date is still explicit');
  assert.ok(all.includes('These rows were not narrowed to it'));

  // And a superseded row is allowed there, because nothing claims it was applicable that day.
  assert.ok(all.includes('Art. 1'));

  for (const bad of [undefined, '', 'as-of', 'latest']) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, timeScope: bad }),
      /results say whether they were narrowed to a date/,
      `timeScope=${JSON.stringify(bad)} was accepted`,
    );
  }
});

test('a row cannot claim a layer this screen says did not run', () => {
  // A badge saying "semantic match" beside a disclosure saying semantic retrieval was off is
  // the page contradicting itself, and the badge is the half a reader believes.
  for (const [reason, needs] of [['semantic', 'semantic'], ['interpreted', 'crosswalk']]) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, hits: [hit({ match_reasons: [reason] })] }),
      new RegExp(`badged "${reason}", which is evidence that the ${needs} relaxation ran`),
      `${reason} was claimed on a screen that says it did not run`,
    );
    // Declared applied, the same row renders.
    assert.ok(
      renderSearchResults({
        ...GOOD,
        relaxations: {
          ...RELAXATIONS,
          [needs]: {
            applied: true,
            expansions: [],
            understood_as: 'garantie locative',
            version: 'crosswalk/1',
            reviewed_on: '2026-08-15',
            encoder: 'local-encoder/1',
            benchmark: 'benchmark/1',
          },
        },
        hits: [hit({ match_reasons: [reason] })],
      }).includes('Art. 1'),
    );
  }
});

test('an absent relaxation set is a caller who did not say, not none applied', () => {
  for (const bad of [undefined, [], null, 'off']) {
    assert.throws(
      () => renderSearchResults({ ...GOOD, relaxations: bad }),
      /an absent set is not "none ran"/,
      `relaxations=${JSON.stringify(bad)} was read as none applied`,
    );
  }
  // Complete, not merely present: a relaxation missing from the account is a relaxation this
  // screen cannot disclose, and the disclosure block is where that is decided for both.
  assert.throws(
    () =>
      renderSearchResults({
        ...GOOD,
        relaxations: { fuzzy: { applied: false }, crosswalk: { applied: false } },
      }),
    /must declare whether it was applied/,
  );
  assert.throws(
    () => renderSearchResults({ ...GOOD, relaxations: { ...RELAXATIONS, rerank: { applied: false } } }),
    /is not a relaxation this interface can disclose/,
  );

  // And it is refused before anything reads it, not merely by the disclosure block at the end.
  // The badge cross-check indexes the account directly, so on a row that carries a badge an
  // unvalidated account fails there instead: a TypeError about undefined, published in place of
  // the honest sentence, on the one screen whose job is to say what it does not know.
  for (const bad of [[], 'off', null]) {
    assert.throws(
      () =>
        renderSearchResults({
          ...GOOD,
          relaxations: bad,
          hits: [hit({ match_reasons: ['semantic'] })],
        }),
      /an absent set is not "none ran"/,
      `relaxations=${JSON.stringify(bad)} was read before it was checked`,
    );
  }
});
