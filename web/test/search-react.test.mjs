// The search screen, and the five rules that survive being composed.
//
// The idiom is the one the refusal card established: render both the string renderer and the
// React component and assert they agree. A framework that quietly becomes a second home for the
// truth rules is the worst available outcome of adopting one, so wherever this screen and
// `scripts/search-results.mjs` describe the same thing, the test binds them at the words rather
// than at a resemblance.
//
// Where the two deliberately differ, the difference is asserted rather than left to be discovered:
// the React screen derives the heading vocabulary and the returned-row count from the records, so
// it accepts multi-publisher result sets the string renderer refuses, and it prints the row set
// unconditionally where the string renderer prints it only when the list was cut.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import {
  BADGE_LABELS,
  Interpretation,
  NoHitCard,
  REASON_EVIDENCES,
  RelaxationDisclosures,
  ResultList,
  SearchScreen,
  interpretationOf,
  requireRelaxationAccount,
  requireSameOriginSearchPath,
  renderSearchScreenPage,
} from '../.react-build/app.mjs';
import { renderNoHitCard } from '../scripts/no-hit-card.mjs';
import { RELAXATIONS, renderRelaxationDisclosures } from '../scripts/relaxation.mjs';
import { DATE_SCOPE, INTERVAL_SENTENCE } from '../scripts/publisher-vocabulary.mjs';
import { MATCH_REASONS, renderSearchResults } from '../scripts/search-results.mjs';

const AS_OF = '2026-09-01';
const WORK = 'lu-legilux:code-travail';
const OTHER = 'lu-legilux:code-civil';
const UNION = 'eu-eurlex:32016R0679';

const POPULATION = {
  searchable_works: [
    { what: 'consolidated LU works held by this corpus', count: 1402, counted_at: '2026-08-15' },
    { what: 'reviewed EU works held by this corpus', count: 1250, counted_at: '2026-08-15' },
  ],
  not_searchable: [
    { what: 'LU acts that never receive a consolidated edition', count: 23370, counted_at: '2026-08-15' },
  ],
};

const OFF = Object.freeze({
  fuzzy: { applied: false },
  crosswalk: { applied: false },
  semantic: { applied: false },
});

const LAYERS = [
  { name: 'work_resolution', outcome: 'ran', language: 'en' },
  { name: 'exact_identifier', outcome: 'ran', language: 'en' },
  { name: 'keyword', outcome: 'ran', language: 'en' },
  { name: 'lay_vocabulary_bridge', outcome: 'ran', language: 'en' },
  { name: 'semantic', outcome: 'ran', language: 'en' },
];

const ROUTES = [
  { label: 'Search the publisher directly', publisher: 'lu-legilux', uri: 'https://legilux.public.lu/' },
];

/** A row the React screen and the string renderer will both accept. */
function hit(overrides = {}) {
  // The identifier and the permalink are derived from the same date as the row, so a fixture
  // cannot hand the screen a row whose link names a different state. Hardcoding the permalink
  // meant every override of `valid_from` silently produced a mismatched pair, which the row
  // binding then refused before the test reached the rule it was written for.
  const validFrom = overrides.valid_from ?? '2001-01-01';
  return {
    lex_id: `${WORK}:${validFrom}`,
    title: 'Code du travail, article 1',
    language: 'fr',
    valid_from: validFrom,
    valid_to: null,
    publication_date: '2000-12-01',
    text_available: true,
    permalink: `https://law.soufien.lu/lu-legilux/code-travail/${validFrom}--${'a'.repeat(64)}`,
    match_reasons: ['keyword'],
    provision_num: 'Art. 1',
    ...overrides,
  };
}

const noop = () => {};

function screen(props = {}) {
  return renderToStaticMarkup(
    h(SearchScreen, {
      query: 'conges payes',
      today: AS_OF,
      asOf: AS_OF,
      hits: [hit()],
      matchingTotal: 1,
      population: POPULATION,
      relaxations: OFF,
      searchPath: '/ask/search',
      filters: [],
      layers: LAYERS,
      routes: ROUTES,
      onOpen: noop,
      onSubmitDate: noop,
      onCompare: noop,
      ...props,
    }),
  );
}

/** The same result set through the string renderer, for the parts that must read alike. */
function strings(props = {}) {
  const hits = props.hits ?? [hit()];
  return renderSearchResults({
    query: 'conges payes',
    asOf: AS_OF,
    timeScope: 'as_of',
    hits,
    rowSet: { returned: hits.length, total: props.matchingTotal ?? hits.length },
    population: POPULATION,
    relaxations: props.relaxations ?? OFF,
    searchPath: '/ask/search',
    expansions: (props.relaxations ?? OFF).fuzzy.applied
      ? (props.relaxations ?? OFF).fuzzy.expansions
      : [],
    layers: LAYERS,
    routes: ROUTES,
  });
}

// ---------------------------------------------------------------------------------------------
// Rule one: the relaxation account is required and closed, on every path.
// ---------------------------------------------------------------------------------------------

test('an omitted relaxation account refuses the screen rather than disclosing nothing', () => {
  // The failure this guards is not a wrong disclosure, it is a skipped one. Defaulted to an empty
  // value, every check downstream was written over the account's own keys, so an omission walked
  // past the contract instead of failing it, and a screen that discloses only when handed
  // something to disclose cannot be told apart from one that never discloses.
  for (const absent of [undefined, null, [], 'none', 0]) {
    assert.throws(
      () => screen({ relaxations: absent }),
      /a caller who did not say/,
      `${JSON.stringify(absent)} was accepted as an account`,
    );
  }
});

test('the account is closed: every relaxation declares itself, and nothing else may', () => {
  for (const missing of RELAXATIONS) {
    const partial = { ...OFF };
    delete partial[missing];
    assert.throws(
      () => screen({ relaxations: partial }),
      new RegExp(`${missing} must declare whether it was applied`),
      `${missing} could be left out of the account`,
    );
    // The same words the string renderer uses, so the two cannot drift apart.
    assert.throws(
      () => renderRelaxationDisclosures({ searchPath: '/ask/search', relaxations: partial }),
      new RegExp(`${missing} must declare whether it was applied`),
    );
  }

  const invented = { ...OFF, transliteration: { applied: true } };
  assert.throws(() => screen({ relaxations: invented }), /is not a relaxation this interface/);
  assert.throws(
    () => renderRelaxationDisclosures({ searchPath: '/ask/search', relaxations: invented }),
    /is not a relaxation this interface/,
  );
});

test('the zero-hit path requires the account too, which is where it matters most', () => {
  // Zero hits is the screen most likely to be read as "the law does not say so", and the one
  // where a silent rewrite matters most: the live service answers this exact English query by
  // expanding "many" into nonsense.
  //
  // This assertion used to be `doesNotThrow`, recording that the string renderer returned the
  // no-hit card before validating the account and so could render with nothing declared. That
  // hole was closed on the other surface while this port was in flight, so the assertion is now
  // that both refuse, which is what was wanted all along.
  assert.throws(
    () =>
      renderSearchResults({
        query: 'security deposit',
        asOf: AS_OF,
        timeScope: 'as_of',
        hits: [],
        rowSet: { returned: 0, total: 0 },
        population: POPULATION,
        relaxations: {},
        layers: LAYERS,
        routes: ROUTES,
        searchPath: '/ask/search',
      }),
    /must declare whether it was applied/,
    'the zero-hit string renderer rendered without an account',
  );
  assert.throws(
    () => screen({ hits: [], matchingTotal: 0, relaxations: {} }),
    /must declare whether it was applied/,
    'the zero-hit screen rendered without an account',
  );
});

// ---------------------------------------------------------------------------------------------
// Rule two: a badge that is evidence a relaxation ran is cross-checked against the account.
// ---------------------------------------------------------------------------------------------

test('a badge that evidences a relaxation the account denies refuses the screen', () => {
  // A row badged "semantic match" inside a result set declaring that semantic never ran is the
  // page contradicting itself, and the badge is the half a reader believes.
  for (const [reason, relaxation] of Object.entries(REASON_EVIDENCES)) {
    const rows = [hit({ match_reasons: [reason] })];
    assert.throws(
      () => screen({ hits: rows, matchingTotal: 1 }),
      new RegExp(`is badged "${reason}".*${relaxation}`, 's'),
      `${reason} was rendered against an account that denies ${relaxation}`,
    );
    assert.throws(
      () => strings({ hits: rows }),
      new RegExp(`${reason}.*${relaxation}`, 's'),
      `the string renderer allowed ${reason} against a denied ${relaxation}`,
    );

    // Declared, and both renderers accept it. A cross-check that refused everything would pass
    // the tests above while making the badge unreachable.
    const declared = {
      ...OFF,
      crosswalk: {
        applied: true,
        understood_as: 'garantie locative',
        version: 'crosswalk/1',
        reviewed_on: '2026-08-15',
      },
      semantic: { applied: true, encoder: 'synthetic/1', benchmark: 'bench/1' },
    };
    assert.ok(screen({ hits: rows, matchingTotal: 1, relaxations: declared }).length > 0);
  }

  // The reasons that are the reader's own words evidence nothing and stay renderable with an
  // account that declares everything off. Without this the cross-check could refuse every badge
  // and still pass every assertion above it.
  const ownWords = MATCH_REASONS.filter((one) => REASON_EVIDENCES[one] === undefined);
  assert.ok(ownWords.length > 0, 'every match reason evidences a relaxation, which cannot be');
  for (const reason of ownWords) {
    assert.ok(screen({ hits: [hit({ match_reasons: [reason] })], matchingTotal: 1 }).length > 0);
  }
});

test('the badge label is derived from the row, not handed to it', () => {
  // Each row used to arrive carrying a finished sentence, so a row whose reasons said `semantic`
  // could be badged "matched your words" and nothing on the page would disagree. The label a
  // caller supplies is now ignored entirely, and the reasons decide.
  const html = renderToStaticMarkup(
    h(ResultList, {
      hits: [hit({ match_reasons: ['exact_title'], match_label: 'matched your words' })],
      relaxations: OFF,
      selected: [],
      onOpen: noop,
      onToggleSelect: noop,
    }),
  );
  assert.ok(html.includes('matched on title, not wording'), 'the reason did not decide the badge');
  assert.ok(!html.includes('matched your words'), 'a supplied label reached the page');

  // And the words are the string renderer's, for every reason in the enum.
  for (const reason of MATCH_REASONS) {
    const relaxations = {
      fuzzy: { applied: false },
      crosswalk: {
        applied: true,
        understood_as: 'garantie locative',
        version: 'crosswalk/1',
        reviewed_on: '2026-08-15',
      },
      semantic: { applied: true, encoder: 'synthetic/1', benchmark: 'bench/1' },
    };
    const rows = [hit({ match_reasons: [reason] })];
    const react = renderToStaticMarkup(
      h(ResultList, { hits: rows, relaxations, selected: [], onOpen: noop, onToggleSelect: noop }),
    );
    const string = strings({ hits: rows, relaxations });
    const label = /<li class="hit-badge">([^<]+)<\/li>/.exec(string)?.[1];
    assert.ok(label, `the string renderer emitted no badge for ${reason}`);
    assert.ok(react.includes(label), `${reason} reads "${label}" in one renderer and not the other`);
  }
});

// ---------------------------------------------------------------------------------------------
// Rule three: the interpretation banner precedes the results in the document.
// ---------------------------------------------------------------------------------------------

test('the interpretation banner comes before the results in DOM order, not just above them', () => {
  // A screen reader user hears document order. The spec puts this first so they learn their query
  // was rewritten before they hear answers to the rewritten question.
  const relaxations = {
    ...OFF,
    fuzzy: { applied: true, expansions: ['many -> mady'] },
  };
  const html = screen({ relaxations });
  const banner = html.indexOf('results-interpretation');
  const list = html.indexOf('results-list');
  assert.notEqual(banner, -1, 'the composed screen dropped the banner');
  assert.notEqual(list, -1);
  assert.ok(banner < list, 'the results were announced before the rewrite was');
  assert.ok(html.includes('Your query was changed before it ran'));
  assert.ok(html.includes('many -&gt; mady'), 'the substitution was not shown verbatim');
  assert.ok(/<p class="results-interpretation" aria-live="polite">/.test(html));
});

test('the banner speaks when nothing was relaxed, and reads the account rather than a prop', () => {
  // Silence is what a broken disclosure looks like, so the honest case speaks too. And the banner
  // cannot disagree with the disclosures beside it, because both come out of one account.
  assert.ok(screen().includes('Your exact words were searched'));

  assert.deepEqual(interpretationOf(OFF), { expansions: [], understoodAs: null });
  assert.deepEqual(
    interpretationOf({
      fuzzy: { applied: true, expansions: ['many -> man'] },
      crosswalk: {
        applied: true,
        understood_as: 'garantie locative',
        version: 'crosswalk/1',
        reviewed_on: '2026-08-15',
      },
      semantic: { applied: false },
    }),
    { expansions: ['many -> man'], understoodAs: 'garantie locative' },
  );

  // A relaxation that claims to have run and will not say what it did is refused in both places.
  const silent = { ...OFF, fuzzy: { applied: true } };
  assert.throws(() => interpretationOf(silent), /must list the expansions it applied/);
  assert.throws(
    () => renderRelaxationDisclosures({ searchPath: '/ask/search', relaxations: silent }),
    /must list the expansions it applied/,
  );
});

test('the banner still speaks when every row is hidden by a filter the reader turned on', () => {
  // Not the no-hit card: that would state a corpus miss the search never found. Not an empty
  // listbox either, which states nothing. The rewrite is still a rewrite when none of its answers
  // are on screen.
  const relaxations = { ...OFF, fuzzy: { applied: true, expansions: ['many -> mady'] } };
  const html = renderToStaticMarkup(
    h(Interpretation, { relaxations }),
  );
  assert.ok(html.includes('Your query was changed before it ran'));
});

// ---------------------------------------------------------------------------------------------
// Rule four: the date field has no silent default, and announces what it resolved to.
// ---------------------------------------------------------------------------------------------

test('the screen shows today as a removable chip before any query is scoped to it', () => {
  const html = screen();
  assert.ok(html.includes('No date entered'), 'the screen resolved to today without saying so');
  assert.ok(html.includes(`as it stands on ${AS_OF}`), 'the default date was not named');
  assert.ok(html.includes('Remove this default'), 'the default could not be removed');
  // Explicit even when it is today: today is the one date a reader will not think to check.
  assert.ok(html.includes(`Operative date: ${AS_OF}`));
});

test('the resolution is announced through a live region, with the interval named', () => {
  const quiet = screen();
  assert.ok(quiet.includes('class="date-resolution" aria-live="polite"'), 'no live region');
  assert.ok(!quiet.includes('Resolved to'), 'a resolution was announced before one existed');

  const announced = screen({
    resolved: {
      lex_id: `${WORK}:2001-01-01`,
      valid_from: '2001-01-01',
      valid_to: '2004-01-01',
      publication_date: '2000-12-01',
    },
  });
  assert.ok(
    announced.includes('Resolved to the state applicable from 2001-01-01 to 2004-01-01'),
    'the announcement did not name the interval',
  );
  // The Union does not date applicability, and nobody passes that fact: it is read off the
  // resolved record's own identifier.
  const union = screen({
    resolved: {
      lex_id: `${UNION}:2018-05-25`,
      valid_from: '2018-05-25',
      valid_to: null,
      publication_date: '2016-05-04',
    },
  });
  assert.ok(union.includes('Resolved to the consolidated wording state from 2018-05-25'));
  assert.ok(!union.includes('Resolved to the state applicable'), 'the LU vocabulary reached the Union');
});

// ---------------------------------------------------------------------------------------------
// Rule five: both row counts, always.
// ---------------------------------------------------------------------------------------------

test('a cut list names both numbers, in the words the string renderer uses', () => {
  const html = screen({ matchingTotal: 47 });
  assert.ok(html.includes('Showing 1 of 47 matching passages.'));
  const string = strings({ matchingTotal: 47 });
  assert.ok(
    string.includes('Showing 1 of 47 matching passages.'),
    'the two renderers describe a cut list differently',
  );
});

test('a complete list names both numbers too, where the string renderer stays silent', () => {
  // A deliberate divergence, asserted rather than discovered. The string renderer prints the row
  // set only when the list was cut, so a complete list and a broken pager look identical. Silence
  // is what a broken disclosure looks like, and a reader cannot tell a page boundary from the end
  // of the corpus by its absence.
  const html = screen({ matchingTotal: 1 });
  assert.ok(html.includes('Showing 1 of 1 matching passages.'));
  assert.ok(!strings({ matchingTotal: 1 }).includes('Showing 1 of'));
});

test('the returned count is derived from the rows, so it cannot disagree with them', () => {
  // The string renderer takes it and refuses the mismatch; here there is nothing to mismatch.
  assert.throws(
    () =>
      renderSearchResults({
        query: 'conges payes',
            asOf: AS_OF,
        timeScope: 'as_of',
        hits: [hit()],
        rowSet: { returned: 9, total: 47 },
        population: POPULATION,
        relaxations: OFF,
        searchPath: '/ask/search',
      }),
    /the row set says 9 rows and 1 were given/,
  );
  assert.ok(screen({ matchingTotal: 47 }).includes('Showing 1 of 47'));

  // What the rows cannot supply is still required, closed and cross-checked.
  for (const bad of [undefined, null, -1, 1.5, '47']) {
    assert.throws(() => screen({ matchingTotal: bad }), /how many passages matched/);
  }
  assert.throws(
    () => screen({ hits: [hit(), hit()], matchingTotal: 1 }),
    /cannot hold more of a result set than the result set has/,
  );
});

test('an empty page of a nonempty result set is refused, never published as a corpus miss', () => {
  assert.throws(
    () => screen({ hits: [], matchingTotal: 9 }),
    /not evidence that the corpus holds nothing/,
  );
});

test('the filter chips describe the page while the row set describes the corpus', () => {
  const rows = [hit(), hit({ lex_id: `${OTHER}:2001-01-01`, match_reasons: ['exact_title'] })];
  const filters = [
    { key: 'travail', label: 'Code du travail', keeps: (one) => one.lex_id.startsWith(WORK) },
  ];
  const html = screen({ hits: rows, matchingTotal: 47, filters });
  assert.ok(html.includes('Showing 2 of 47 matching passages.'), 'the corpus fact went missing');
  assert.ok(html.includes('Showing all 2. No filter is active.'), 'the page fact went missing');
  assert.ok(html.includes('aria-pressed="false"'), 'the chip did not state its own state');
});

// ---------------------------------------------------------------------------------------------
// The heading, derived rather than supplied.
// ---------------------------------------------------------------------------------------------

test('the heading is the publisher vocabulary of the rows, not a parameter', () => {
  const html = screen();
  assert.equal(html.includes(DATE_SCOPE.publisher_applicability(AS_OF)), true);
  assert.ok(strings().includes(DATE_SCOPE.publisher_applicability(AS_OF)));

  // A Union row is on the other clock, and each row says which in its own terms.
  const union = hit({
    lex_id: `${UNION}:2018-05-25`,
    valid_from: '2018-05-25',
    permalink: `https://law.soufien.lu/eu-eurlex/32016R0679/2018-05-25--${'b'.repeat(64)}`,
  });
  const mixed = screen({ hits: [hit(), union], matchingTotal: 2 });
  assert.ok(
    mixed.includes("each in its own publisher&#x27;s terms"),
    'a mixed list was headed in one publisher words',
  );
  assert.ok(mixed.includes(INTERVAL_SENTENCE.publisher_applicability('2001-01-01', null)));
  assert.ok(mixed.includes(INTERVAL_SENTENCE.official_consolidation_state('2018-05-25', null)));
});

test('a date-scoped heading is only available when every row covers that date', () => {
  // The string renderer takes the scope as a parameter and refuses the rows that contradict it.
  // Here the rows decide, so the heading cannot assert something the rows deny.
  const superseded = hit({ valid_from: '1999-01-01', valid_to: '2001-01-01' });
  assert.throws(() => strings({ hits: [superseded] }), /does not cover 2026-09-01/);

  const html = screen({ hits: [superseded], matchingTotal: 1 });
  assert.ok(html.includes(`not narrowed to ${AS_OF}`), 'a superseded row was headed as applicable');
  assert.ok(!html.includes(DATE_SCOPE.publisher_applicability(AS_OF)));
});

// ---------------------------------------------------------------------------------------------
// The listbox: one tab stop, and focus is not selection.
// ---------------------------------------------------------------------------------------------

test('the whole result list is one tab stop, however many rows it has', () => {
  // The property, not the attribute. Counting `tabindex="0"` was the old assertion and it was
  // satisfied while every row carried a nested button, which is tabbable with no tabindex at all:
  // fifty rows were fifty tab stops and nothing could see it.
  const rows = Array.from({ length: 50 }, (_, index) =>
    hit({ provision_num: `Art. ${index + 1}`, title: `Code du travail, article ${index + 1}` }),
  );
  const html = screen({ hits: rows, matchingTotal: 50 });
  const opened = html.indexOf('<ul class="results-list"');
  assert.notEqual(opened, -1, 'the composed screen rendered no listbox');
  const list = html.slice(opened, html.lastIndexOf('</ul>'));
  assert.equal((list.match(/tabindex="0"/g) ?? []).length, 1, 'more than one row is tabbable');
  assert.equal((list.match(/tabindex="-1"/g) ?? []).length, 49);
  for (const focusable of ['<button', '<a ', '<input', '<select', '<textarea', 'contenteditable']) {
    assert.ok(!list.includes(focusable), `${focusable} inside the list is another tab stop`);
  }
});

test('focus is not selection, and selection is stated', () => {
  const rows = [hit(), hit({ lex_id: `${WORK}:2004-01-01`, valid_from: '2004-01-01' })];
  const none = screen({ hits: rows, matchingTotal: 2 });
  assert.equal((none.match(/aria-selected="false"/g) ?? []).length, 2);
  assert.ok(!none.includes('aria-selected="true"'), 'arriving at a row selected it');

  // Two rows can be armed at once and the list says so; without this a screen reader announces a
  // single-choice list and the second selection reads as a mistake.
  assert.ok(none.includes('aria-multiselectable="true"'));

  const armed = renderToStaticMarkup(
    h(ResultList, {
      hits: rows,
      relaxations: OFF,
      selected: [rows[0]],
      onOpen: noop,
      onToggleSelect: noop,
    }),
  );
  assert.equal((armed.match(/aria-selected="true"/g) ?? []).length, 1);
});

test('a row this list cannot place is refused rather than rendered', () => {
  // Through the one strict reading of a lex_id. Splitting and taking the first part made
  // `garbage` a publisher, and `compare.mjs` had already learned that lesson alone.
  for (const bad of ['garbage', 'lu-legilux', '', ':::', 'lu-legilux:code-travail']) {
    assert.throws(
      () => screen({ hits: [hit({ lex_id: bad })], matchingTotal: 1 }),
      /does not name a publisher, a work and a state|is not a publisher this interface/,
      `${JSON.stringify(bad)} was rendered as a row`,
    );
  }
});

test('a title with no stated language is refused rather than read in the wrong voice', () => {
  const { language, ...noLanguage } = hit();
  assert.throws(
    () => screen({ hits: [noLanguage], matchingTotal: 1 }),
    /neither it nor the record says what language it is in/,
  );
  // The record's own language answers when there is no separate title language, and an explicit
  // one still wins.
  assert.ok(screen({ hits: [hit({ language: 'fr' })], matchingTotal: 1 }).includes('lang="fr"'));
  assert.ok(
    screen({ hits: [hit({ language: 'fr', title_language: 'de' })], matchingTotal: 1 }).includes(
      'lang="de"',
    ),
  );
});

// ---------------------------------------------------------------------------------------------
// The disclosures, and the revert links that carry them.
// ---------------------------------------------------------------------------------------------

test('every applied relaxation gets its own block and its own revert', () => {
  const relaxations = {
    fuzzy: { applied: true, expansions: ['many -> mady'] },
    crosswalk: {
      applied: true,
      understood_as: 'garantie locative',
      version: 'crosswalk/1',
      reviewed_on: '2026-08-15',
    },
    semantic: { applied: true, encoder: 'synthetic/1', benchmark: 'bench/1' },
  };
  const react = renderToStaticMarkup(
    h(RelaxationDisclosures, { searchPath: '/ask/search', relaxations }),
  );
  const string = renderRelaxationDisclosures({ searchPath: '/ask/search', relaxations });

  // Three independent reverts, not one that undoes everything: a reader who wants their own words
  // back is not also asking to turn off semantic ranking.
  for (const relaxation of RELAXATIONS) {
    assert.ok(react.includes(`data-relaxation="${relaxation}"`), `${relaxation} has no block`);
    assert.ok(string.includes(`data-relaxation="${relaxation}"`));
  }
  for (const href of ['fuzzy=off', 'crosswalk=off', 'retrieval_mode=keyword']) {
    assert.ok(react.includes(href), `${href} revert is missing from the React disclosures`);
    assert.ok(string.includes(href));
  }
  // The editorial label is the component's own words, and a caller cannot phrase it away.
  assert.ok(react.includes('Editorial crosswalk, not official'));
  assert.ok(string.includes('Editorial crosswalk, not official'));
  assert.ok(react.includes('reviewed 2026-08-15'));

  // Nothing applied means no block at all, and a screen that then shows one is disclosing a
  // relaxation that did not happen.
  assert.equal(renderToStaticMarkup(h(RelaxationDisclosures, { searchPath: '/ask/search', relaxations: OFF })), '');
});

test('a crosswalk with no review date is refused in both renderers', () => {
  // It is editorial and not official, so when somebody last looked at it is part of the claim.
  const relaxations = {
    ...OFF,
    crosswalk: { applied: true, understood_as: 'garantie locative', version: 'crosswalk/1' },
  };
  assert.throws(
    () => renderToStaticMarkup(h(RelaxationDisclosures, { searchPath: '/ask/search', relaxations })),
    /must carry its review date/,
  );
  assert.throws(
    () => renderRelaxationDisclosures({ searchPath: '/ask/search', relaxations }),
    /must carry its review date/,
  );
});

test('a revert that leaves this origin is not a revert', () => {
  // `revertPath` accepts anything starting with a slash, and `//evil.example/x` starts with one.
  // That would put a one-tap trip to another origin behind the most trusted label on the screen.
  for (const hostile of ['//evil.example/x', 'https://evil.example/x', '/ask/search#frag', '', '/ask//search', 'ask/search']) {
    assert.throws(
      () => requireSameOriginSearchPath(hostile),
      /same-origin search path/,
      `${JSON.stringify(hostile)} was accepted as a search path`,
    );
    assert.throws(() => screen({ searchPath: hostile }), /same-origin search path/);
  }
  assert.equal(requireSameOriginSearchPath('/ask/search?q=x'), '/ask/search?q=x');
});

// ---------------------------------------------------------------------------------------------
// The zero-hit card.
// ---------------------------------------------------------------------------------------------

function noHit(props = {}) {
  return renderToStaticMarkup(
    h(NoHitCard, {
      query: 'security deposit',
      layers: LAYERS,
      population: POPULATION,
      relaxations: OFF,
      routes: ROUTES,
      ...props,
    }),
  );
}

function noHitString(props = {}) {
  return renderNoHitCard({
    query: 'security deposit',
    layers: LAYERS,
    population: POPULATION,
    expansions: [],
    routes: ROUTES,
    ...props,
  });
}

test('the two zero-hit cards make the same claim about what ran', () => {
  // The sentence a reader takes as "the law does not say so" is decided by what actually ran, and
  // the two renderers must not scope it differently.
  const cases = [
    [LAYERS, 'Nothing in the held records matches'],
    [LAYERS.map((one) => ({ ...one, outcome: 'not_run' })), 'No search of the held records completed'],
    [
      LAYERS.map((one, index) => (index === 0 ? { ...one, outcome: 'unavailable' } : one)),
      'in the searches that ran',
    ],
    [
      LAYERS.map((one, index) => (index === 0 ? { ...one, outcome: 'not_applicable' } : one)),
      'Nothing in the held records matches',
    ],
  ];
  for (const [layers, expected] of cases) {
    assert.ok(noHit({ layers }).includes(expected), `React card: ${expected}`);
    assert.ok(noHitString({ layers }).includes(expected), `string card: ${expected}`);
  }

  // The disclaimer is invariant across all three accounts. An earlier draft moved it into the
  // branches and the partial case silently lost it.
  for (const [layers] of cases) {
    assert.ok(noHit({ layers }).includes('not evidence that the instrument or the law does not exist'));
  }
});

test('the zero-hit card names every layer, its outcome and its language', () => {
  const html = noHit({
    layers: LAYERS.map((one, index) => (index === 4 ? { ...one, outcome: 'unavailable', language: 'fr' } : one)),
  });
  for (const label of [
    'resolved the query against work titles',
    'looked the query up as an identifier',
    'searched provision wording by keyword',
    'expanded lay terms into legal ones',
    'ranked by meaning',
  ]) {
    assert.ok(html.includes(label), `${label} was not named`);
  }
  assert.ok(html.includes('was unavailable'));
  assert.ok(html.includes('in fr'), 'a reader who typed English was not told which language ran');
});

test('every refusal the string card makes, the React card makes', () => {
  // The string renderer is the React card validator, so this is a check that the wiring is real
  // rather than a list that has to be kept in step by hand.
  const broken = [
    [{ layers: LAYERS.slice(0, 3) }, /the execution plan omits/],
    [{ layers: [{ name: 'invented', outcome: 'ran', language: 'en' }] }, /is not a retrieval layer/],
    [{ layers: LAYERS.map((one) => ({ ...one, language: 'english' })) }, /must say which language/],
    [{ routes: [] }, /must offer the publisher/],
    [{ population: { searchable_works: [], not_searchable: [] } }, /needs searchable_works/],
    [
      { population: { ...POPULATION, not_searchable: [{ what: 'x', count: 1 }] } },
      /must say when it was counted/,
    ],
    [{ query: '   ' }, /must echo the query/],
  ];
  for (const [override, message] of broken) {
    assert.throws(() => noHit(override), message, `React card accepted ${JSON.stringify(override)}`);
    assert.throws(() => noHitString(override), message);
  }
});

test('the zero-hit card shows the substitutions, read off the account rather than beside it', () => {
  // The live case: an English lay query answered by expanding "many" to "mady" and returning
  // nothing, while the rule exists. An expansion only the log knows about is a silent edit of the
  // question. Deriving it from the account makes a card that shows substitutions no relaxation
  // claims unrepresentable rather than merely wrong.
  const relaxations = { ...OFF, fuzzy: { applied: true, expansions: ['many -> mady', 'many -> man'] } };
  const html = noHit({ relaxations });
  assert.ok(html.includes('Your query was expanded before it ran'));
  assert.ok(html.includes('many -&gt; mady'));
  assert.ok(!noHit().includes('Your query was expanded before it ran'), 'expansions appeared unclaimed');

  const composed = screen({ hits: [], matchingTotal: 0, relaxations });
  assert.ok(composed.includes('no-hit-card'), 'the zero-hit screen did not render the card');
  assert.ok(composed.includes('many -&gt; mady'));
  // And the disclosure with its revert is on the same screen, because the card alone offers no
  // way back to the reader's own words.
  assert.ok(composed.includes('fuzzy=off'));
});

test('a zero-hit screen says which date found nothing, and whether the words were the readers', () => {
  // "Nothing matched" is a different statement about 2019 than about today, and a screen that
  // says neither leaves the reader to assume the date they had in mind. The banner is here for
  // the same reason it is on a screen with rows: silence reads as "your words were used".
  const html = screen({ hits: [], matchingTotal: 0 });
  assert.ok(html.includes(`Operative date: ${AS_OF}`), 'the zero-hit screen named no date');
  assert.ok(html.includes('Your exact words were searched'));
  const banner = html.indexOf('results-interpretation');
  const card = html.indexOf('no-hit-card');
  assert.ok(banner !== -1 && card !== -1 && banner < card, 'the card was announced before the rewrite');
});

test('a zero-hit screen with no execution plan is refused, not given an empty one', () => {
  // The layers and the routes belong to the card that says what ran and where to look next. A
  // screen with rows never renders it and never needs them; a screen without rows cannot honestly
  // render it without them, and an empty plan would let "nothing matches" rest on nothing.
  assert.throws(
    () => screen({ hits: [], matchingTotal: 0, layers: undefined }),
    /must name which layers ran/,
  );
  assert.throws(
    () => screen({ hits: [], matchingTotal: 0, routes: undefined }),
    /must offer the publisher/,
  );
  // And a screen with rows does not need either of them.
  assert.ok(screen({ layers: undefined, routes: undefined }).includes('results-list'));
});

test('a route off the publisher own host is shown and not linked, in both cards', () => {
  const routes = [{ label: 'Elsewhere', publisher: 'lu-legilux', uri: 'https://evil.example/' }];
  assert.ok(noHit({ routes }).includes('not linked'));
  assert.ok(noHitString({ routes }).includes('not linked'));
  assert.ok(!noHit({ routes }).includes('href="https://evil.example/"'));
});

// ---------------------------------------------------------------------------------------------
// The account contract itself, and the population disclosure both screens share.
// ---------------------------------------------------------------------------------------------

test('requireRelaxationAccount returns the account it accepted', () => {
  assert.equal(requireRelaxationAccount(OFF), OFF);
});

test('a hit list discloses the same population an empty one does', () => {
  // Two shapes for one disclosure would let a hit list say less than an empty one, which is the
  // wrong way round: a reader who got results is exactly the reader who stops checking.
  const html = screen();
  assert.ok(html.includes('What this search covered'));
  assert.ok(html.includes('23370'));
  assert.ok(html.includes('counted 2026-08-15'));
  assert.throws(
    () => screen({ population: { searchable_works: POPULATION.searchable_works, not_searchable: [] } }),
    /needs not_searchable/,
  );
});

test('a filter that cannot decide which rows it keeps is refused', () => {
  // A chip that changes nothing teaches a reader that controls do nothing. The count it used to
  // be handed was never read, so a wrong one was invisible; the predicate is read on every render.
  assert.throws(
    () => screen({ filters: [{ key: 'x', label: 'X' }] }),
    /does not say which rows it keeps/,
  );
  assert.throws(() => screen({ filters: undefined }), /which facets it offers/);
});

test('filters that hide every row say so, and do not become a corpus miss', () => {
  const filters = [{ key: 'none', label: 'Nothing', keeps: () => false }];
  const html = renderToStaticMarkup(
    h(SearchScreen, {
      query: 'conges payes',
      today: AS_OF,
      asOf: AS_OF,
      hits: [hit()],
      matchingTotal: 1,
      population: POPULATION,
      relaxations: OFF,
      searchPath: '/ask/search',
      filters,
      layers: LAYERS,
      routes: ROUTES,
      onOpen: noop,
      onSubmitDate: noop,
      onCompare: noop,
    }),
  );
  // Not active yet, so the rows are there. The all-hidden branch is reachable only through the
  // reader turning a chip on, and it is asserted directly on the sentence it renders.
  assert.ok(html.includes('results-list'));
});

// ---------------------------------------------------------------------------------------------
// Compare arming, composed.
// ---------------------------------------------------------------------------------------------

test('the compare control is on the screen, disarmed, and says what is needed', () => {
  const html = screen();
  assert.ok(html.includes('Select two states to compare them.'));
  assert.ok(html.includes('aria-disabled="true"'), 'compare was armed with nothing selected');
  // Reachable while unavailable. `disabled` would take it out of the tab order, which is how the
  // browser run found fifteen focusable elements and fourteen reachable by Tab.
  assert.equal(/<button[^>]*\sdisabled/.test(html), false, 'a control left the tab order');
  assert.ok(/class="compare-arming-state"[^>]*aria-live="polite"/.test(html));
});

// ---------------------------------------------------------------------------------------------
// The whole page.
// ---------------------------------------------------------------------------------------------

test('the screen composes into a whole document, twice, without colliding with itself', () => {
  // Not in the build: `browser-evidence.mjs` fails any built page carrying a button, whatever the
  // page does about it. Rendered here so the composition is still exercised end to end, and so
  // the day that gate distinguishes a hydrated page from an inert one this is one line in
  // build.mjs rather than a page nobody has run.
  const html = renderSearchScreenPage();
  assert.ok(html.startsWith('<!doctype html>'), 'not a whole document');
  for (const probe of [
    'results-list',
    'results-interpretation',
    'no-hit-card',
    'filter-chip',
    'compare-arming',
    'date-field',
    'relaxation-fuzzy',
    'Showing 3 of 47 matching passages.',
    'aria-multiselectable="true"',
  ]) {
    assert.ok(html.includes(probe), `${probe} is missing from the composed page`);
  }

  // Two screens on one document, and every element id minted per instance. Written by hand, the
  // date field's label pointed at whichever input the parser saw first and a screen reader
  // announced the other as unlabelled.
  const ids = [...html.matchAll(/ id="([^"]+)"/g)].map((one) => one[1]);
  assert.ok(ids.length >= 4, 'the page mints no ids at all, so this proves nothing');
  assert.equal(ids.length, new Set(ids).size, 'two elements on the page share an id');

  // One tab stop for the whole result list, in the document the browser would receive.
  assert.equal((html.match(/tabindex="0"/g) ?? []).length, 1);
  assert.equal(/<button[^>]*\sdisabled/.test(html), false, 'a control left the tab order');
});

// ---------------------------------------------------------------------------------------------
// The contracts themselves, driven directly.
//
// Every one of these exists because the same rule is also enforced one layer down, so a mutation
// to this layer's copy left the suite green: the string renderer caught it on the disclosure path
// and the screen caught it before the list. A guard whose failure nothing can observe is a guard
// a later reader will trust wrongly, so each is driven at its own front door.
// ---------------------------------------------------------------------------------------------

test('the account contract refuses, by itself, what the disclosures refuse', () => {
  for (const absent of [undefined, null, [], 'none', 0]) {
    assert.throws(() => requireRelaxationAccount(absent), /a caller who did not say/);
  }
  for (const missing of RELAXATIONS) {
    const partial = { ...OFF };
    delete partial[missing];
    assert.throws(
      () => requireRelaxationAccount(partial),
      new RegExp(`${missing} must declare whether it was applied`),
    );
    assert.throws(() => interpretationOf(partial), new RegExp(`${missing} must declare`));
  }
  assert.throws(
    () => requireRelaxationAccount({ ...OFF, transliteration: { applied: true } }),
    /is not a relaxation this interface can disclose/,
  );
  // A relaxation added to the retrieval path and not to the disclosures is how a silent one
  // ships, so the closed set is the point rather than a tidiness rule.
  assert.equal(requireRelaxationAccount(OFF), OFF);
});

test('the results list applies the account by itself, without the screen around it', () => {
  const rows = [hit()];
  const list = (relaxations) =>
    renderToStaticMarkup(
      h(ResultList, { hits: rows, relaxations, selected: [], onOpen: noop, onToggleSelect: noop }),
    );
  assert.throws(() => list(undefined), /a caller who did not say/);
  assert.throws(() => list({ ...OFF, semantic: undefined }), /semantic must declare/);
  assert.throws(() => list({ ...OFF, invented: { applied: false } }), /is not a relaxation/);
});

test('the results list places a row by itself, through the one strict reading of a lex_id', () => {
  // The screen derives its heading from the same reading, so a screen-level test cannot tell
  // whether the list is strict or is merely standing behind something that is.
  const list = (lexId) =>
    renderToStaticMarkup(
      h(ResultList, {
        hits: [hit({ lex_id: lexId })],
        relaxations: OFF,
        selected: [],
        onOpen: noop,
        onToggleSelect: noop,
      }),
    );
  for (const bad of ['garbage', 'lu-legilux', '', ':::', 'lu-legilux:code-travail', 'lu-legilux::x']) {
    assert.throws(
      () => list(bad),
      /does not name a publisher, a work and a state|is not a publisher this interface/,
      `${JSON.stringify(bad)} was placed as a row`,
    );
  }
  assert.ok(list(`${WORK}:2001-01-01`).includes('results-list'));
});

test('every match reason the service returns has a badge, and nothing else does', () => {
  // The import-time tripwire in the list cannot be observed from a test, because no fixture can
  // add a member to a frozen enum in another module. This can: the two sets are compared.
  assert.deepEqual([...MATCH_REASONS].sort(), Object.keys(BADGE_LABELS).sort());
  for (const reason of MATCH_REASONS) {
    assert.equal(typeof BADGE_LABELS[reason], 'string');
    assert.ok(BADGE_LABELS[reason].trim().length > 0, `${reason} has an empty badge`);
  }
});

test('a filter the reader arrives with already on hides rows, and the screen says so', () => {
  // The one place a reader ends up with no rows that is neither a corpus miss nor an empty page.
  // Rendering the no-hit card here would state an absence the search never found, and rendering
  // an empty listbox would state nothing at all.
  const rows = [hit(), hit({ lex_id: `${OTHER}:2001-01-01`, match_reasons: ['exact_title'] })];
  const html = screen({
    hits: rows,
    matchingTotal: 47,
    filters: [{ key: 'none', label: 'Nothing at all', keeps: () => false, active: true }],
  });
  assert.ok(!html.includes('results-list'), 'an empty listbox was rendered');
  assert.ok(!html.includes('no-hit-card'), 'filtered rows were published as a corpus miss');
  assert.ok(html.includes('All 2 rows on this page are hidden by filters you turned on'));
  assert.ok(html.includes('says nothing about what the corpus holds'));
  // Both counts still speak: the corpus fact from the row set, the page fact from the chips.
  assert.ok(html.includes('Showing 2 of 47 matching passages.'));
  assert.ok(html.includes('Showing 0 of 2.'));
  // And the rewrite is still announced, because a rewritten query is rewritten whether or not
  // any of its answers are on screen.
  assert.ok(html.includes('results-interpretation'));

  // A chip whose state nobody stated is announced as one of them anyway.
  assert.throws(
    () => screen({ filters: [{ key: 'x', label: 'X', keeps: () => true, active: 'yes' }] }),
    /which is neither on nor off/,
  );
});
