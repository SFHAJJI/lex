import assert from 'node:assert/strict';
import test from 'node:test';

import {
  LEGENDS,
  PROVISIONAL_MARK,
  TIMELINE_SEMANTICS,
  holesBetween,
  overlapsIn,
  renderTimeline,
} from '../scripts/timeline.mjs';

const HASH_A = 'a'.repeat(64);
const HASH_B = 'b'.repeat(64);
const HASH_C = 'c'.repeat(64);

function state(overrides) {
  return {
    lex_id: `preview-synthetic:synthetic-preview-work:${overrides.valid_from}`,
    valid_from: '2001-01-01',
    valid_to: '2004-01-01',
    publication_date: '2000-12-01',
    observed_from: '2026-01-01T00:00:00Z',
    extraction_profile: 'akn-lu/1',
    text_available: true,
    hash: HASH_A,
    withdrawn: false,
    ...overrides,
  };
}

const A = state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: HASH_A });
const B = state({
  valid_from: '2004-01-01',
  valid_to: null,
  hash: HASH_B,
  extraction_profile: 'xhtml-eu/1',
});

const GOOD = {
  semantics: 'publisher_applicability',
  states: [A, B],
  asOf: '2026-09-01',
  population: 'within the 1,402 consolidated LU works held by this corpus',
  totalCount: 2,
};

/** Interval helper for the two exported set functions. */
const span = (from, to) => ({ valid_from: from, valid_to: to, lex_id: `w:${from}` });

test('the legal-time vocabulary comes from the envelope and has no default', () => {
  assert.deepEqual(Object.keys(TIMELINE_SEMANTICS), [
    'publisher_applicability',
    'official_consolidation_state',
  ]);

  const lu = renderTimeline(GOOD);
  assert.ok(lu.includes('Applicable from 2001-01-01 to 2004-01-01 (publisher)'));
  assert.ok(!lu.includes('Consolidated wording state'), 'the EU vocabulary leaked onto a LU work');

  const eu = renderTimeline({ ...GOOD, semantics: 'official_consolidation_state' });
  assert.ok(eu.includes('Consolidated wording state from 2001-01-01 to 2004-01-01'));
  assert.ok(!eu.includes('(publisher)'), 'the LU vocabulary leaked onto an EU work');

  // Including a key that exists on every object. A prototype lookup would have rendered
  // [object Object] as the legal-time claim.
  for (const bad of [undefined, '', 'in_force', 'applicability', 'toString', 'constructor']) {
    assert.throws(
      () => renderTimeline({ ...GOOD, semantics: bad }),
      /not one of publisher_applicability/,
      `${String(bad)} was accepted as a vocabulary`,
    );
  }
});

test('the words in force never reach a state row', () => {
  const html = renderTimeline(GOOD);
  assert.ok(!html.includes('in force'), 'a state row said in force');

  for (const value of ['in_force', null, false, 'not_in_force']) {
    assert.throws(
      () => renderTimeline({ ...GOOD, states: [{ ...A, binding_status: value }, B] }),
      /belongs in the dossier status strip/,
      `binding_status=${JSON.stringify(value)} reached a row`,
    );
  }
});

test('a gap is the space the held records do not cover', () => {
  // The union of the intervals, not a walk from each state to the next one starting after it.
  // That walk invented a gap over a state nested inside a longer one, and let a state sitting
  // at a gap's edge swallow the gap entirely.
  assert.deepEqual(
    holesBetween([span('2000-01-01', '2010-01-01'), span('2005-01-01', '2006-01-01'), span('2020-01-01', null)]),
    [{ from: '2010-01-01', to: '2020-01-01' }],
    'a state nested inside another produced a gap over an interval the corpus holds',
  );
  assert.deepEqual(
    holesBetween([span('2000-01-01', '2005-01-01'), span('2003-01-01', '2012-01-01'), span('2020-01-01', null)]),
    [{ from: '2012-01-01', to: '2020-01-01' }],
    'overlapping states produced a gap over covered days',
  );
  assert.deepEqual(
    holesBetween([span('2000-01-01', '2005-01-01'), span('2001-01-01', '2005-01-01'), span('2010-01-01', null)]),
    [{ from: '2005-01-01', to: '2010-01-01' }],
    'two records of one interval produced the same gap twice',
  );
  assert.deepEqual(holesBetween([A, B]), [], 'abutting states are not a gap');
  assert.deepEqual(
    holesBetween([span('2020-01-01', null), span('2000-01-01', '2005-01-01')]),
    [{ from: '2005-01-01', to: '2020-01-01' }],
    'the input order changed the answer',
  );
});

test('a gap is rendered where the gap is, in full, and says it is derived', () => {
  const gapped = {
    ...GOOD,
    totalCount: 3,
    states: [
      A,
      state({ valid_from: '2024-12-28', valid_to: null, hash: HASH_C }),
      state({ valid_from: '2030-01-01', valid_to: null, hash: HASH_B }),
    ],
  };
  const html = renderTimeline(gapped);

  assert.ok(html.includes('GAP 2004-01-01 to 2024-12-28'));
  assert.ok(html.includes('No publisher state covers 2004-01-01 to 2024-12-28'));
  assert.ok(html.includes('Absence of a held state is not evidence the law was unchanged'));
  assert.ok(html.includes('not asserted by the publisher'), 'the gap is derived and must say so');

  // In position: the gap sits between the state that ends and the one that begins, not in a
  // list of absences appended after every row.
  const gapAt = html.indexOf('GAP 2004-01-01');
  assert.ok(gapAt > html.indexOf('2001-01-01'), 'the gap rendered before the state it follows');
  assert.ok(gapAt < html.indexOf('2024-12-28'), 'the gap rendered after the state it precedes');

  assert.ok(!renderTimeline(GOOD).includes('GAP '), 'a gap appeared between abutting states');
});

test('a state that covers no day at all is refused', () => {
  // One sitting at the edge of a gap made the gap disappear.
  assert.throws(
    () =>
      renderTimeline({
        ...GOOD,
        totalCount: 3,
        states: [A, state({ valid_from: '2004-01-01', valid_to: '2004-01-01', hash: HASH_C }), B],
      }),
    /covers no day at all/,
  );
});

test('overlapping states are stacked, labelled derived, and never preselected', () => {
  const overlapping = {
    ...GOOD,
    states: [A, state({ valid_from: '2003-01-01', valid_to: '2005-01-01', hash: HASH_C })],
  };
  const html = renderTimeline(overlapping);

  assert.ok(html.includes('both cover part of the same period'));
  assert.ok(html.includes('Neither is preselected'));
  assert.ok(html.includes('The publisher ranks neither state, and neither does this'));
  assert.equal(overlapsIn(overlapping.states).length, 1);

  // The pair is named by its records, so the section cannot name the wrong two states.
  assert.ok(html.includes(A.lex_id));
  assert.ok(html.includes('preview-synthetic:synthetic-preview-work:2003-01-01'));

  // Abutting states are not overlapping: the interval is half-open, the same reading the
  // resolver uses, so a shared boundary date is not a publisher conflict.
  assert.deepEqual(overlapsIn([A, B]), []);
  assert.ok(!renderTimeline(GOOD).includes('Overlapping states'));
});

test('a future state is provisional against a supplied date, not the machine clock', () => {
  const future = {
    ...GOOD,
    totalCount: 3,
    states: [A, B, state({ valid_from: '2030-09-15', valid_to: null, hash: HASH_C })],
  };

  const before = renderTimeline({ ...future, asOf: '2026-09-01' });
  assert.ok(before.includes(PROVISIONAL_MARK));
  assert.equal(PROVISIONAL_MARK, 'PROVISIONAL, publisher-scheduled');

  const after = renderTimeline({ ...future, asOf: '2031-01-01' });
  assert.ok(!after.includes(PROVISIONAL_MARK), 'a past state was still marked provisional');

  // The boundary. A state applicable exactly today is applicable, not scheduled.
  const onTheDay = renderTimeline({ ...future, asOf: '2030-09-15' });
  assert.ok(!onTheDay.includes(PROVISIONAL_MARK), 'a state applicable today was called scheduled');

  for (const bad of [undefined, '', 'today', '2026-99-99']) {
    assert.throws(() => renderTimeline({ ...future, asOf: bad }), /drawn as of/);
  }
  assert.ok(before.includes('Drawn as of 2026-09-01'));
});

test('an open state ends in null and is never closed with today', () => {
  const html = renderTimeline(GOOD);
  assert.ok(html.includes('Applicable from 2004-01-01 to no end recorded (publisher)'));
  assert.ok(!html.includes('2026-09-01 (publisher)'), 'today closed an open interval');

  const closed = renderTimeline({ ...GOOD, states: [A, { ...B, valid_to: '2026-09-01' }] });
  assert.ok(closed.includes('Applicable from 2004-01-01 to 2026-09-01 (publisher)'));
  assert.ok(!closed.includes('no end recorded'));
});

test('a date read out of a title is bounded, deduplicated and labelled derived', () => {
  const titled = (title, overrides = {}) =>
    renderTimeline({
      ...GOOD,
      totalCount: 2,
      states: [
        state({
          valid_from: '2020-03-14',
          valid_to: '2020-09-25',
          hash: HASH_A,
          title,
          title_language: 'fr',
          ...overrides,
        }),
        B,
      ],
    });

  // A date inside a longer number is not a date. Each of these printed a date this service
  // invented, under a sentence attributing it to the publisher.
  for (const title of [
    'Acte n. 12345-06-30 de la serie',
    'Dossier 20/03/20245 renvoi',
    'Observed 2026-08-14T23:05:14Z',
  ]) {
    assert.ok(
      !titled(title).includes('timeline-title-distrust'),
      `${title} was read as a date the publisher stated`,
    );
  }

  // A single-digit day is a date, and was being missed.
  assert.ok(titled('applicable au 1/08/2024').includes('timeline-title-distrust'));
  // An ISO date in a title is read too.
  assert.ok(titled('consolidated 2024-08-01').includes('timeline-title-distrust'));

  // The start date is agreement. The end date is not: intervals are half-open, so valid_to
  // is the first day the state does not cover, and a title claiming applicability on that
  // day contradicts the record. This was asserted the other way until a live Legilux record
  // showed why it matters: Legilux labels every consolidated version of a work with the
  // latest consolidation date, so the state applicable 2020-03-14 to 2020-09-25 carries the
  // title 'Version consolidee applicable au 25/09/2020' and the screen called it agreement.
  assert.ok(
    titled('Version au 25/09/2020').includes('timeline-title-distrust'),
    'a title claiming applicability on the first uncovered day read as agreement',
  );
  assert.ok(!titled('Version au 14/03/2020').includes('timeline-title-distrust'), 'start date');

  // One date written twice is one claim.
  const twice = titled('Version au 30/06/2019 (rectifie le 30/06/2019)');
  assert.equal((twice.match(/2019-06-30/g) ?? []).length, 1, 'one date was reported twice');

  // And the reading is ours, so it says so.
  assert.ok(twice.includes('read out of the title mechanically'));
  assert.ok(twice.includes('by this service and not by the publisher'));
  assert.ok(twice.includes("The record's dates place this row; the title never does"));
});

test('a title carries the language it is written in, with no default', () => {
  const withTitle = (extra) =>
    renderTimeline({
      ...GOOD,
      states: [{ ...A, title: 'An English title of a Union act', ...extra }, B],
    });

  assert.ok(withTitle({ title_language: 'en' }).includes('lang="en"'));
  assert.ok(!withTitle({ title_language: 'en' }).includes('lang="fr"'), 'defaulted to French');

  for (const bad of [undefined, '', 'french', { a: 1 }]) {
    assert.throws(
      () => withTitle({ title_language: bad }),
      /does not say what language it is in/,
      `title_language=${JSON.stringify(bad)} was accepted`,
    );
  }
  assert.throws(
    () => renderTimeline({ ...GOOD, states: [{ ...A, title: ['x'], title_language: 'fr' }, B] }),
    /carries a title that is not a string/,
  );
});

test('placement is by the record, and is the same whatever order the records arrive in', () => {
  const shared = [
    state({ valid_from: '2010-01-01', valid_to: '2011-01-01', hash: HASH_A }),
    state({ valid_from: '2010-01-01', valid_to: '2012-01-01', hash: HASH_B }),
    state({ valid_from: '2010-01-01', valid_to: null, hash: HASH_C }),
  ];
  const render = (states) => renderTimeline({ ...GOOD, totalCount: 3, states });

  // Three states sharing a start date used to keep the caller's order, which is exactly the
  // ambiguous_version shape and exactly where the record has to place the row.
  const first = render(shared);
  for (const order of [
    [shared[2], shared[0], shared[1]],
    [shared[1], shared[2], shared[0]],
    [...shared].reverse(),
  ]) {
    assert.equal(render(order), first, 'the caller order changed the page');
  }
});

test('a withdrawn state is struck on its interval and says when', () => {
  const html = renderTimeline({
    ...GOOD,
    states: [A, { ...B, withdrawn: true, withdrawn_from_source: '2026-02-01' }],
  });
  assert.ok(html.includes('data-withdrawn="true"'));
  assert.ok(html.includes('Withdrawn by the publisher on 2026-02-01'));
  // The strike is on the interval, which every state has, not on a title which it may not.
  assert.ok(html.includes('<span class="timeline-interval">'));

  assert.throws(
    () => renderTimeline({ ...GOOD, states: [A, { ...B, withdrawn: true }] }),
    /does not say when the publisher withdrew it/,
  );
  // Truthy is not true. Two predicates used to guard this and they agreed only on the one
  // value the tests passed, so 'yes' struck the row and dated it undefined.
  for (const bad of ['yes', 1, undefined, null]) {
    assert.throws(
      () => renderTimeline({ ...GOOD, states: [A, { ...B, withdrawn: bad }] }),
      /is neither withdrawn nor held/,
      `withdrawn=${JSON.stringify(bad)} was accepted`,
    );
  }
});

test('every row says whether its text is held and which profile made it', () => {
  const html = renderTimeline({ ...GOOD, states: [A, { ...B, text_available: false }] });
  // Asserted as whole cells: "text held" is a substring of "no text held", so a renderer that
  // reported every state as textless satisfied both loose assertions.
  assert.ok(html.includes('<td>text held</td>'));
  assert.ok(html.includes('<td>no text held</td>'));

  // Two different profiles, so a constant cannot satisfy the assertion.
  assert.ok(html.includes('<td>akn-lu/1</td>'));
  assert.ok(html.includes('<td>xhtml-eu/1</td>'));

  // And each row is named by its own record.
  assert.ok(html.includes(A.lex_id));
  assert.ok(html.includes(B.lex_id));

  assert.throws(
    () => renderTimeline({ ...GOOD, states: [A, { ...B, text_available: undefined }] }),
    /does not say whether its text is held/,
  );
  assert.throws(
    () => renderTimeline({ ...GOOD, states: [A, { ...B, extraction_profile: '' }] }),
    /does not name its extraction profile/,
  );
});

test('the digest chip is the first eight characters of the record digest', () => {
  const html = renderTimeline(GOOD);
  assert.ok(html.includes(`<code>${'a'.repeat(8)}</code>`));
  assert.ok(!html.includes(`<code>${'a'.repeat(9)}</code>`), 'more than eight characters');
  assert.ok(!html.includes(HASH_A), 'the whole digest was rendered as the chip');
});

test('the total is a fact about the records, not a flag the caller sets', () => {
  const html = renderTimeline({ ...GOOD, totalCount: 12 });
  assert.ok(html.includes('Showing 2 of 12 states.'));
  assert.ok(!renderTimeline(GOOD).includes('Showing'), 'a complete list claimed truncation');

  // A single row against a total of twelve used to render no pager at all and claim the
  // publisher history began there.
  const cut = renderTimeline({ ...GOOD, states: [A], totalCount: 12 });
  assert.ok(cut.includes('Showing 1 of 12 states.'));
  assert.ok(!cut.includes('One held state'), 'a cut list claimed to be the whole history');

  for (const bad of [undefined, 12.5, 'many', 0]) {
    assert.throws(
      () => renderTimeline({ ...GOOD, totalCount: bad }),
      /says how many states the publisher history holds/,
      `totalCount=${JSON.stringify(bad)} was accepted`,
    );
  }
  assert.throws(
    () => renderTimeline({ ...GOOD, totalCount: 1 }),
    /one of those two numbers is wrong and this screen must not choose which/,
  );
  // A declaration that disagrees with the records is refused rather than preferred.
  assert.throws(() => renderTimeline({ ...GOOD, truncated: true }), /declares truncated/);
  assert.throws(
    () => renderTimeline({ ...GOOD, totalCount: 12, truncated: false }),
    /declares truncated/,
  );
});

test('a single held state says so rather than drawing one segment', () => {
  const html = renderTimeline({ ...GOOD, states: [A], totalCount: 1 });
  // One held state says this corpus holds one, not that the publisher's record starts
  // there. The live envelope carries history_begins for exactly that question and this
  // screen does not receive it, so the sentence names the corpus and says what is unknown.
  assert.ok(html.includes('This corpus holds one state of this work, beginning 2001-01-01'));
  assert.ok(
    !html.includes('publisher history begins'),
    'one held state was reported as the start of the publisher record',
  );
});

test('the population is stated and an empty timeline is refused', () => {
  assert.ok(renderTimeline(GOOD).includes('within the 1,402 consolidated LU works'));
  for (const bad of [undefined, '  ']) {
    assert.throws(() => renderTimeline({ ...GOOD, population: bad }), /states the population/);
  }
  assert.throws(
    () => renderTimeline({ ...GOOD, states: [] }),
    /an empty axis with a legend asserts that the law has none/,
  );
});

test('the legend is the component words and the chart is decoration', () => {
  const html = renderTimeline(GOOD);
  assert.ok(html.includes(LEGENDS[GOOD.semantics]));
  // Both pinned to literals, so neither can drift into asserting the other publisher's
  // claim. The EU sentence is the one that matters: the Union dates a consolidation's
  // wording and makes no applicability claim at all.
  assert.equal(
    LEGENDS.publisher_applicability,
    'Top: when the publisher says the state applied. Bottom: when the publisher published it. ' +
      'These routinely differ.',
  );
  assert.equal(
    LEGENDS.official_consolidation_state,
    'Top: the wording state the publisher consolidated. Bottom: when the publisher ' +
      'published it. These routinely differ.',
  );
  assert.ok(html.includes('<div class="timeline-chart" aria-hidden="true">'));
  assert.ok(html.includes('<table class="timeline-table">'), 'the table is the structure');
  assert.ok(html.includes('<th scope="col">'), 'the header cells are not scoped');
});

test('a state that cannot be placed is refused rather than placed anyway', () => {
  for (const [field, value, pattern] of [
    ['lex_id', '', /has no lex_id/],
    ['valid_from', '2001-13-01', /valid_from is not a calendar date/],
    ['valid_to', '2001-13-01', /neither null nor a calendar date/],
    ['valid_to', '2000-01-01', /covers no day at all/],
    ['publication_date', undefined, /publication_date is not a calendar date/],
    ['observed_from', '2026-01-01', /observed_from is not a UTC instant/],
    ['hash', 'short', /needs its digest/],
  ]) {
    assert.throws(
      () => renderTimeline({ ...GOOD, states: [{ ...A, [field]: value }, B] }),
      pattern,
      `${field}=${String(value)} was placed on the axis`,
    );
  }
});

test('the record time carries the UTC instant verbatim', () => {
  const html = renderTimeline(GOOD);
  assert.ok(html.includes('Published 2000-12-01 / First observed 2026-01-01T00:00:00Z'));
});

test('values are escaped rather than trusted', () => {
  const html = renderTimeline({
    ...GOOD,
    states: [{ ...A, title: `<img src=x onerror="alert('1')"> & more`, title_language: 'fr' }, B],
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
  assert.ok(html.includes('&amp; more'), 'an ampersand was not escaped');
  assert.ok(!html.includes('onerror="alert'), 'a quote broke out of the attribute');
});

test('O4: a truncated timeline claims no gaps, because it cannot know', () => {
  // A hole is the universal claim "no publisher state covers this span". A page holding some
  // of the states cannot support it: the state that fills the gap may be one of the missing.
  const states = [
    state({ valid_from: '2001-01-01', valid_to: '2002-01-01', hash: HASH_A }),
    state({ valid_from: '2010-01-01', valid_to: null, hash: HASH_B }),
  ];
  const whole = renderTimeline({ ...GOOD, states, totalCount: 2 });
  assert.equal(whole.includes('No publisher state covers'), true, 'a complete timeline hid its gap');

  const partial = renderTimeline({ ...GOOD, states, totalCount: 9 });
  assert.equal(
    partial.includes('No publisher state covers'),
    false,
    'a truncated timeline asserted that no state covers a span it did not enumerate',
  );
});

test('O4: a digest is lowercase hex, not sixty-four of anything', () => {
  for (const hash of ['g'.repeat(64), ' '.repeat(64), 'A'.repeat(64), 'a'.repeat(63), 'a'.repeat(65)]) {
    assert.throws(
      () => renderTimeline({ ...GOOD, states: [state({ valid_from: '2001-01-01', hash })], totalCount: 1 }),
      /lowercase hex SHA-256/,
      `${JSON.stringify(hash.slice(0, 8))} was accepted as a digest`,
    );
  }
});

test('O4: the open interval is null, never the sorting sentinel', () => {
  // Supplied literally, 9999-12-31 sorts as open and renders as a real end date, so a state
  // the publisher never ended reads as one that ends on a specific day.
  assert.throws(
    () =>
      renderTimeline({
        ...GOOD,
        states: [state({ valid_from: '2001-01-01', valid_to: '9999-12-31' })],
        totalCount: 1,
      }),
    /an open interval is null/,
  );
});

test('O5-R1: the legend does not assert applicability over a consolidation timeline', () => {
  // The rows were repaired and the legend above them kept asserting what the rows had stopped
  // saying, which is worse than the original: the legend is what teaches a reader how to read
  // the column beneath it.
  const eu = renderTimeline({ ...GOOD, semantics: 'official_consolidation_state' });
  assert.equal(eu.includes('the wording state the publisher consolidated'), true);
  assert.equal(
    eu.includes('when the publisher says the state applied'),
    false,
    'an EU timeline asserted applicability in its legend',
  );
  assert.equal(eu.includes('Applicable from'), false, 'an EU timeline asserted applicability in a row');

  const lu = renderTimeline({ ...GOOD, semantics: 'publisher_applicability' });
  assert.equal(lu.includes('when the publisher says the state applied'), true);
  assert.equal(lu.includes('the wording state the publisher consolidated'), false);
});
