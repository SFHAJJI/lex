// The timeline ported to React, measured against the string renderer rather than asserted.
//
// The claim under test is not that React works. It is that both renderers apply the same rules
// and refuse the same inputs, because a framework quietly becoming a second home for legal rules
// is the worst available outcome of adopting one. So every shape of this screen is rendered
// twice and compared byte for byte, and every guard is fed to both.
//
// Two rules are stronger in the React port than in the string renderer, deliberately, and both
// are tested here in their own right:
//
//   - A timeline is the history of ONE work. `scripts/timeline.mjs` on this branch does not
//     enforce it: handed two unrelated instruments it computes gaps and overlaps across them and
//     prints "both cover part of the same period" and "the publisher ranks neither state", which
//     report a contradiction the publisher never made. The React port refuses the input instead.
//   - The date vocabulary is derived from the records' own publisher, never passed in. The
//     string renderer takes it as a parameter and prints one fixed legend whichever it is given,
//     so a Union work is legended with Luxembourg's applicability claim. That divergence is
//     pinned below rather than left to be discovered.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { DERIVED_HOLE, DERIVED_OVERLAP, DERIVED_TITLE, Timeline } from '../.react-build/app.mjs';
import { LEGENDS } from '../scripts/publisher-vocabulary.mjs';
import { PROVISIONAL_MARK, holesBetween, overlapsIn, renderTimeline } from '../scripts/timeline.mjs';

const LU = 'lu-legilux:work-one';
const EU = 'eu-eurlex:work-eu';

function state(overrides) {
  return {
    lex_id: `${LU}:${overrides.valid_from ?? '2001-01-01'}`,
    valid_from: '2001-01-01',
    valid_to: '2004-01-01',
    publication_date: '2000-12-01',
    observed_from: '2026-01-01T00:00:00Z',
    extraction_profile: 'akn-lu/1',
    text_available: true,
    hash: 'a'.repeat(64),
    withdrawn: false,
    ...overrides,
  };
}

const digest = (seed) => seed.repeat(64).slice(0, 64);
const POPULATION = 'within the 1,402 consolidated LU works held by this corpus';

const GOOD = {
  states: [
    state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('a') }),
    state({ valid_from: '2004-01-01', valid_to: null, hash: digest('b') }),
  ],
  asOf: '2026-09-01',
  population: POPULATION,
  totalCount: 2,
};

const react = (props) => renderToStaticMarkup(h(Timeline, props));
const string = (props) => renderTimeline(props);

/**
 * Compare two renderings of the same records.
 *
 * Two differences are spellings of identical HTML rather than differences in what is said, and
 * normalising them is what lets everything else be compared exactly:
 *
 *   - React writes an apostrophe as `&#x27;`; `escapeHtml` writes `&#39;` and the string
 *     renderer's own prose leaves it bare. All three parse to one character.
 *   - React 18 emits the DOM property spelling `colSpan`. HTML attribute names are ASCII
 *     case-insensitive, so it parses as `colspan`.
 *
 * Nothing else is normalised. In particular `<` and `&` are left alone, so the escaping test
 * below still measures escaping.
 */
function normalise(html) {
  return html
    .replaceAll('&#x27;', "'")
    .replaceAll('&#39;', "'")
    .replaceAll('colSpan=', 'colspan=');
}

/** Every shape of this screen where the two renderers must agree exactly. */
const SHAPES = {
  'two states, no surprises': GOOD,
  'a gap between two held states': {
    ...GOOD,
    states: [
      state({ valid_from: '1993-04-05', valid_to: '2004-04-02', hash: digest('c') }),
      state({ valid_from: '2024-12-28', valid_to: null, hash: digest('d'), text_available: false }),
    ],
  },
  'a truncated history, which cannot speak about gaps': {
    ...GOOD,
    totalCount: 12,
    states: [
      state({ valid_from: '1993-04-05', valid_to: '2004-04-02', hash: digest('c') }),
      state({ valid_from: '2024-12-28', valid_to: null, hash: digest('d') }),
    ],
  },
  'overlapping states and titles naming another state': {
    ...GOOD,
    totalCount: 3,
    states: [
      state({
        valid_from: '2020-03-14',
        valid_to: '2020-09-25',
        hash: digest('e'),
        title: 'Version consolidee applicable au 25/09/2020 : acte synthetique',
        title_language: 'fr',
      }),
      state({
        valid_from: '2001-01-01',
        valid_to: '2020-03-14',
        hash: digest('f'),
        title: 'Version consolidee applicable au 1/08/2024 : acte synthetique',
        language: 'fr',
      }),
      state({ valid_from: '2020-01-01', valid_to: '2020-12-31', hash: digest('1') }),
    ],
  },
  'a state the publisher withdrew': {
    ...GOOD,
    states: [
      state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('4') }),
      state({
        valid_from: '2004-01-01',
        valid_to: null,
        hash: digest('5'),
        withdrawn: true,
        withdrawn_from_source: '2026-02-01',
      }),
    ],
  },
  'a state scheduled for a date that has not arrived': {
    ...GOOD,
    states: [
      state({ valid_from: '2016-04-27', valid_to: '2016-05-03', hash: digest('2') }),
      state({ valid_from: '2029-03-29', valid_to: null, hash: digest('3') }),
    ],
  },
  'one held state': {
    ...GOOD,
    totalCount: 1,
    states: [state({ valid_from: '2001-01-01', valid_to: null, hash: digest('9') })],
  },
};

test('both renderers draw the same timeline, in every shape that makes one lie', () => {
  for (const [name, props] of Object.entries(SHAPES)) {
    assert.equal(
      normalise(react(props)),
      normalise(string(props)),
      `${name}: the React port and the string renderer disagree`,
    );
  }
  // Non-trivial: an empty baseline agrees with an empty baseline forever.
  assert.ok(react(GOOD).length > 700, 'the React timeline rendered almost nothing');
  assert.ok(react(SHAPES['a gap between two held states']).includes(DERIVED_HOLE));
});

test('a timeline is the history of one work, and two works are refused rather than compared', () => {
  // The whole screen rests on this. A gap and an overlap are comparisons between intervals, and
  // across two unrelated instruments those comparisons still produce sentences that read as
  // findings. Refusing the input is the only correct answer, because there is no true version of
  // "these two cover part of the same period" to print about two different laws.
  const mixed = {
    ...GOOD,
    states: [
      state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('a') }),
      {
        ...state({ valid_from: '2002-01-01', valid_to: null, hash: digest('b') }),
        lex_id: 'lu-legilux:work-two:2002-01-01',
      },
    ],
  };
  assert.throws(() => react(mixed), /mixes 2 works \(lu-legilux:work-one, lu-legilux:work-two\)/);
  assert.throws(() => react(mixed), /statement about one work/);

  // Two publishers is the same defect wearing the other publisher's clothes.
  const twoPublishers = {
    ...GOOD,
    states: [
      state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('a') }),
      {
        ...state({ valid_from: '2002-01-01', valid_to: null, hash: digest('b') }),
        lex_id: `${EU}:2002-01-01`,
      },
    ],
  };
  assert.throws(() => react(twoPublishers), /mixes 2 works/);

  // And the sentences it exists to prevent are never produced.
  for (const bad of [mixed, twoPublishers]) {
    let html = null;
    try {
      html = react(bad);
    } catch {
      html = null;
    }
    assert.equal(html, null, 'a mixed-work timeline rendered rather than refusing');
  }
});

test('a row that does not name a publisher, a work and a state cannot be placed', () => {
  const nameless = {
    ...GOOD,
    states: [
      state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('a') }),
      { ...state({ valid_from: '2004-01-01', valid_to: null, hash: digest('b') }), lex_id: 'garbage' },
    ],
  };
  assert.throws(() => react(nameless), /does not name a publisher, a work and a state/);
});

test('the date vocabulary is the publisher own and is never a parameter', () => {
  // Passed rather than ignored, because a caller who believes they chose has misunderstood the
  // contract and a silent override leaves them believing it worked.
  assert.throws(
    () => react({ ...GOOD, semantics: 'publisher_applicability' }),
    /does not take a date vocabulary/,
  );
  assert.throws(
    () => react({ ...GOOD, semantics: 'official_consolidation_state' }),
    /does not take a date vocabulary/,
  );

  // Luxembourg dates when a state applied.
  const lu = react(GOOD);
  assert.ok(lu.includes('Applicable from 2001-01-01 to 2004-01-01 (publisher)'));
  assert.ok(!lu.includes('Consolidated wording state'), 'the EU vocabulary leaked onto a LU work');
  assert.ok(lu.includes(LEGENDS.publisher_applicability));

  // The Union dates the wording state of a consolidation and makes no applicability claim. The
  // component was told nothing; it read the publisher out of the records.
  const union = react({
    ...GOOD,
    states: GOOD.states.map((one) => ({
      ...one,
      lex_id: one.lex_id.replace(LU, EU),
      extraction_profile: 'xhtml-eu/1',
    })),
  });
  assert.ok(union.includes('Consolidated wording state from 2001-01-01 to 2004-01-01'));
  assert.ok(!union.includes('(publisher)'), 'the LU vocabulary leaked onto an EU work');
  assert.ok(union.includes(LEGENDS.official_consolidation_state));
});

test('the legend is the one divergence from the string renderer, and it is the string renderer that is wrong', () => {
  // Pinned rather than left to be found. `scripts/timeline.mjs` exports a single LEGEND constant
  // and prints it whichever vocabulary it was handed, so a Union work is legended "when the
  // publisher says the state applied" over a publisher that makes no applicability claim. The
  // React port takes the legend from the same table the interval sentence comes from.
  //
  // Once every difference of legend is removed, the two renderings of a Union work are identical,
  // which is what makes this a single defect in one sentence rather than a diverged port.
  const union = {
    ...GOOD,
    states: GOOD.states.map((one) => ({
      ...one,
      lex_id: one.lex_id.replace(LU, EU),
      extraction_profile: 'xhtml-eu/1',
    })),
  };
  // This test used to record a divergence: the string renderer had one hardcoded Luxembourg
  // legend and printed it over a Union work, which is an applicability claim about a publisher
  // that makes none. Its own failure message said to delete it once that was fixed. It has been,
  // so the assertion is now the stronger one: no divergence at all, legend included.
  const fromString = normalise(string(union));
  assert.ok(
    fromString.includes(LEGENDS.official_consolidation_state),
    'the string renderer lost the Union legend',
  );
  assert.ok(
    !fromString.includes(LEGENDS.publisher_applicability),
    'the Luxembourg legend is back over a Union work',
  );
  assert.equal(
    normalise(react(union)),
    fromString,
    'the React port and the string renderer no longer agree on a Union work',
  );
});

test('a publisher nobody classified fails closed rather than borrowing a neighbour vocabulary', () => {
  for (const publisher of ['unclassified-publisher', 'constructor', 'toString', '__proto__']) {
    const unknown = {
      ...GOOD,
      states: GOOD.states.map((one) => ({
        ...one,
        lex_id: one.lex_id.replace('lu-legilux', publisher),
      })),
    };
    assert.throws(
      () => react(unknown),
      /is not a publisher this interface has classified|does not name a publisher/,
      `${publisher} was allowed to inherit another publisher's vocabulary`,
    );
  }
});

test('the gaps drawn are exactly the ones the shared function derives, not a second reading', () => {
  // Bound to `holesBetween` rather than to a hand-written expectation. The reasoning in that
  // function was paid for with real defects, and a copy of it here would be a second place for
  // them to come back.
  const props = SHAPES['a gap between two held states'];
  const holes = holesBetween(props.states);
  assert.equal(holes.length, 1);
  const html = react(props);
  for (const hole of holes) {
    assert.ok(html.includes(`GAP ${hole.from} to ${hole.to}.`), 'a derived gap was not drawn');
  }
  assert.equal(html.split('class="timeline-hole"').length - 1, holes.length);
  assert.ok(html.includes(DERIVED_HOLE));
  // And the wording is the string renderer's wording, so the two cannot drift apart in prose.
  assert.ok(string(props).includes(DERIVED_HOLE));
});

test('a state nested inside another produces no gap, and one record listed twice no overlap', () => {
  // Both are properties of the imported set functions. They are asserted through the component
  // because the component is what would print the invented absence or the invented contradiction.
  const nested = {
    ...GOOD,
    totalCount: 2,
    states: [
      state({ valid_from: '2001-01-01', valid_to: '2020-01-01', hash: digest('a') }),
      state({ valid_from: '2005-01-01', valid_to: '2006-01-01', hash: digest('b') }),
    ],
  };
  assert.equal(holesBetween(nested.states).length, 0);
  assert.ok(!react(nested).includes('GAP '), 'a nested state manufactured a gap');

  const listedTwice = {
    ...GOOD,
    totalCount: 2,
    states: [
      state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('a') }),
      state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('a') }),
    ],
  };
  assert.equal(overlapsIn(listedTwice.states).length, 0);
  assert.ok(!react(listedTwice).includes('Overlapping states'), 'a duplicate read as a conflict');
});

test('overlapping states are listed as the shared function pairs them, and neither is preselected', () => {
  const props = SHAPES['overlapping states and titles naming another state'];
  const pairs = overlapsIn(props.states);
  assert.ok(pairs.length > 0, 'the overlap fixture holds no overlap');
  const html = react(props);
  assert.ok(html.includes('Overlapping states'));
  assert.ok(html.includes(DERIVED_OVERLAP));
  assert.ok(string(props).includes(DERIVED_OVERLAP));
  assert.equal(html.split('both cover part of the same period').length - 1, pairs.length);
  assert.ok(html.includes('Neither is preselected.'));
  assert.ok(!html.includes('checked'), 'an overlapping state was preselected');
});

test('a truncated list cannot say what lies between the states it did not list', () => {
  const props = SHAPES['a truncated history, which cannot speak about gaps'];
  const html = react(props);
  assert.ok(!html.includes('GAP '), 'pagination alone manufactured an absence in the record');
  assert.ok(html.includes('Gaps are not shown'));
  assert.ok(html.includes('Showing 2 of 12 states.'));
  // And the same records, complete, do show the gap. Otherwise the assertion above passes for a
  // renderer that never draws gaps at all.
  assert.ok(react({ ...props, totalCount: 2 }).includes('GAP '));
});

test('truncation is a fact about the records and a declaration that disagrees is refused', () => {
  assert.throws(() => react({ ...GOOD, truncated: true }), /declares truncated true while holding 2 of 2/);
  assert.throws(() => string({ ...GOOD, truncated: true }), /declares truncated true while holding 2 of 2/);
  const short = { ...GOOD, totalCount: 9 };
  assert.throws(() => react({ ...short, truncated: false }), /declares truncated false/);
  assert.throws(() => string({ ...short, truncated: false }), /declares truncated false/);
  // A declaration that agrees is allowed, and changes nothing.
  assert.equal(normalise(react({ ...short, truncated: true })), normalise(react(short)));
});

test('provisional is measured against the supplied date and not a clock', () => {
  const props = SHAPES['a state scheduled for a date that has not arrived'];
  assert.ok(react(props).includes(PROVISIONAL_MARK));
  // The same records drawn as of a later date carry no provisional mark, which is only possible
  // because the date is a parameter.
  assert.ok(!react({ ...props, asOf: '2030-01-01' }).includes(PROVISIONAL_MARK));
  assert.throws(() => react({ ...props, asOf: undefined }), /needs the date it is drawn as of/);
  assert.throws(() => react({ ...props, asOf: '2026-02-30' }), /needs the date it is drawn as of/);
});

test('a title never moves a row, and the dates read out of it say who read them', () => {
  const props = SHAPES['overlapping states and titles naming another state'];
  const html = react(props);
  assert.ok(html.includes(DERIVED_TITLE));
  assert.ok(string(props).includes(DERIVED_TITLE));
  // Padded: the publisher writes "au 1/08/2024" and an unpadded day used to be dropped in
  // silence, which reads exactly like agreement.
  assert.ok(html.includes('2024-08-01'));
  // Bounded: a date-shaped slice of a longer number is not a date.
  const embedded = {
    ...GOOD,
    totalCount: 1,
    states: [
      state({
        valid_from: '2001-01-01',
        valid_to: null,
        hash: digest('a'),
        title: 'Acte n. 12345-06-30 du registre, 20/03/20245',
        title_language: 'fr',
      }),
    ],
  };
  // Asserted through the distrust paragraph rather than through the page, because the page
  // reproduces the publisher's title verbatim and "12345-06-30" contains "2345-06-30" as a
  // substring. What must not happen is that slice being read as a date and attributed to the
  // publisher, and the paragraph is the only place this screen would say so.
  const bounded = react(embedded);
  assert.ok(!bounded.includes('timeline-title-distrust'), 'a date was cut out of a longer number');
  assert.ok(!string(embedded).includes('timeline-title-distrust'));
  // The positive control: a genuine out-of-band date in the same position does produce one.
  const genuine = react({
    ...embedded,
    states: [{ ...embedded.states[0], title: 'Acte du registre, 20/03/2024' }],
  });
  assert.ok(genuine.includes('timeline-title-distrust'));
  assert.ok(genuine.includes('2024-03-20'));
});

test('a title carries the language it is written in, with no constant default', () => {
  const titled = (overrides) => ({
    ...GOOD,
    totalCount: 1,
    states: [state({ valid_from: '2001-01-01', valid_to: null, hash: digest('a'), ...overrides })],
  });
  assert.ok(react(titled({ title: 'Loi du 1er janvier', title_language: 'fr' })).includes('lang="fr"'));
  // The record's own language answers when no title language is declared, because a title is
  // published as part of the expression it belongs to.
  assert.ok(react(titled({ title: 'Consolidated text', language: 'en' })).includes('lang="en"'));
  for (const bad of [{}, { language: 'french' }, { title_language: 'FR' }]) {
    assert.throws(() => react(titled({ title: 'Loi du 1er janvier', ...bad })), /what language it is in/);
    assert.throws(() => string(titled({ title: 'Loi du 1er janvier', ...bad })), /what language it is in/);
  }
});

test('every guard refuses the same input in both renderers', () => {
  // The two implementations are separate, because `scripts/timeline.mjs` exports no validator.
  // This is what holds them together: if either drifts, one of these stops throwing.
  const one = (overrides) => ({
    ...GOOD,
    totalCount: 1,
    states: [state({ valid_from: '2001-01-01', valid_to: null, hash: digest('a'), ...overrides })],
  });
  const cases = [
    [one({ lex_id: '   ' }), /has no lex_id/],
    [one({ valid_from: 'yesterday' }), /valid_from is not a calendar date/],
    [one({ valid_to: '2026-13-01' }), /neither null nor a calendar date/],
    [one({ valid_from: '2004-01-01', valid_to: '2004-01-01' }), /covers no day at all/],
    [one({ publication_date: '2000-12' }), /publication_date is not a calendar date/],
    [one({ observed_from: '2026-01-01' }), /observed_from is not a UTC instant/],
    [one({ extraction_profile: '' }), /does not name its extraction profile/],
    [one({ text_available: 'yes' }), /does not say whether its text is held/],
    [one({ hash: 'short' }), /needs its digest/],
    [one({ binding_status: 'in_force' }), /carries binding_status/],
    [one({ withdrawn: 'yes' }), /neither withdrawn nor held/],
    [one({ withdrawn: true }), /does not say when the publisher withdrew it/],
    [one({ title: '   ', title_language: 'fr' }), /carries a title that is not a string/],
    [{ ...GOOD, states: [] }, /a timeline with no states is not an empty chart/],
    [{ ...GOOD, population: '  ' }, /states the population it was drawn from/],
    [{ ...GOOD, totalCount: 0 }, /says how many states the publisher history holds/],
    [{ ...GOOD, totalCount: 1 }, /one of those two numbers is wrong/],
    [{ ...GOOD, asOf: 'today' }, /needs the date it is drawn as of/],
  ];
  for (const [props, pattern] of cases) {
    assert.throws(() => react(props), pattern, `the React port accepted ${pattern}`);
    assert.throws(() => string(props), pattern, `the string renderer accepted ${pattern}`);
  }
});

test('the words in force never reach a state row', () => {
  // A held state applicable before entry into force already carries the publisher's own flag, so
  // printing it against a historical interval dates a claim the publisher never made.
  for (const props of Object.values(SHAPES)) {
    assert.ok(!/in force/i.test(react(props)), 'a state row spoke about force');
  }
});

test('the chart is decoration and the table is the structure', () => {
  const html = react(GOOD);
  assert.ok(html.includes('<div class="timeline-chart" aria-hidden="true"></div>'));
  // The chart carries nothing a reader would lose by not seeing it.
  assert.equal(/<div class="timeline-chart"[^>]*>([\s\S]*?)<\/div>/.exec(html)[1], '');
  for (const column of ['state', 'both clocks', 'text', 'extraction profile', 'digest']) {
    assert.ok(html.includes(`<th scope="col">${column}</th>`), `the ${column} column is missing`);
  }
});

test('the wide table scrolls in its own box, and the box says what it is', () => {
  // A scrollable box is keyboard focusable whether or not it asks to be, so it is a tab stop
  // either way. A tab stop that announces nothing is a tab stop a reader cannot place.
  const html = react(GOOD);
  const box = /<div class="timeline-scroll"[^>]*>/.exec(html);
  assert.ok(box, 'the table is not inside its own scroll box');
  assert.ok(box[0].includes('role="region"'));
  assert.ok(box[0].includes('tabindex="0"'));
  assert.ok(box[0].includes('aria-label="State history table, scrollable"'));
  assert.ok(html.indexOf(box[0]) < html.indexOf('<table class="timeline-table">'));
});

test('values are escaped rather than trusted, in the React port too', () => {
  const hostile = {
    ...GOOD,
    totalCount: 1,
    population: '<script>alert(1)</script>',
    states: [
      state({
        valid_from: '2001-01-01',
        valid_to: null,
        hash: digest('a'),
        extraction_profile: '<img src=x onerror=alert(1)>',
        title: '<b>Loi</b>',
        title_language: 'fr',
      }),
    ],
  };
  for (const html of [react(hostile), string(hostile)]) {
    assert.ok(!html.includes('<script>'), 'a script tag survived');
    assert.ok(!html.includes('<img src=x'), 'an image tag survived');
    assert.ok(!html.includes('<b>Loi</b>'), 'markup in a title survived');
    assert.ok(html.includes('&lt;script&gt;'));
  }
});
