import assert from 'node:assert/strict';
import test from 'node:test';

import {
  LEGEND,
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
const B = state({ valid_from: '2004-01-01', valid_to: null, hash: HASH_B });

const GOOD = {
  semantics: 'publisher_applicability',
  states: [A, B],
  asOf: '2026-09-01',
  population: 'within the 1,402 consolidated LU works held by this corpus',
  truncated: false,
};

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

  for (const bad of [undefined, '', 'in_force', 'applicability']) {
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

  // The publisher's own flag is refused rather than rendered, because a held state applicable
  // before entry into force carries it, and printing it here would date a claim the publisher
  // never made about that date.
  assert.throws(
    () => renderTimeline({ ...GOOD, states: [{ ...A, binding_status: 'in_force' }, B] }),
    /belongs in the dossier status strip/,
  );
  assert.throws(
    () => renderTimeline({ ...GOOD, states: [{ ...A, binding_status: null }, B] }),
    /belongs in the dossier status strip/,
    'a null flag is still a flag on the row',
  );
});

test('a gap is derived, stated in full, and cannot be forgotten', () => {
  const gapped = {
    ...GOOD,
    states: [A, state({ valid_from: '2024-12-28', valid_to: null, hash: HASH_C })],
  };
  const html = renderTimeline(gapped);

  assert.ok(html.includes('GAP 2004-01-01 to 2024-12-28'));
  assert.ok(html.includes('No publisher state covers 2004-01-01 to 2024-12-28'));
  assert.ok(html.includes('Absence of a held state is not evidence the law was unchanged'));
  assert.ok(html.includes('not asserted by the publisher'), 'the gap is derived and must say so');

  // The caller cannot omit it, because the caller does not supply it.
  assert.deepEqual(holesBetween(gapped.states), [{ from: '2004-01-01', to: '2024-12-28' }]);
  // And two states that meet exactly are not a gap.
  assert.deepEqual(holesBetween([A, B]), []);
  assert.ok(!renderTimeline(GOOD).includes('GAP '), 'a gap appeared between abutting states');
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

  // Abutting states are not overlapping: the interval is half-open, the same reading the
  // resolver uses, so a shared boundary date is not a publisher conflict.
  assert.deepEqual(overlapsIn([A, B]), []);
  assert.ok(!renderTimeline(GOOD).includes('Overlapping states'));
});

test('a future state is provisional against a supplied date, not the machine clock', () => {
  const future = { ...GOOD, states: [A, state({ valid_from: '2030-09-15', valid_to: null, hash: HASH_C })] };

  const before = renderTimeline({ ...future, asOf: '2026-09-01' });
  assert.ok(before.includes(PROVISIONAL_MARK));
  assert.equal(PROVISIONAL_MARK, 'PROVISIONAL, publisher-scheduled');

  const after = renderTimeline({ ...future, asOf: '2031-01-01' });
  assert.ok(!after.includes(PROVISIONAL_MARK), 'a past state was still marked provisional');

  // The date is required. Reading it from the machine would make a state stop being
  // provisional without the publisher having done anything.
  for (const bad of [undefined, '', 'today', '2026-99-99']) {
    assert.throws(() => renderTimeline({ ...future, asOf: bad }), /drawn as of/);
  }
  assert.ok(before.includes('Drawn as of 2026-09-01'));
});

test('an open state ends in null and is never closed with today', () => {
  const html = renderTimeline(GOOD);
  assert.ok(html.includes('Applicable from 2004-01-01 to no end recorded (publisher)'));
  assert.ok(!html.includes('2026-09-01 (publisher)'), 'today closed an open interval');

  // Closing it explicitly is a different record, and renders as one rather than as the same
  // state with a computed end.
  const closed = renderTimeline({ ...GOOD, states: [A, { ...B, valid_to: '2026-09-01' }] });
  assert.ok(closed.includes('Applicable from 2004-01-01 to 2026-09-01 (publisher)'));
  assert.ok(!closed.includes('no end recorded'));
});

test('a title that disagrees with the record is shown, and never moves the row', () => {
  // The live shape: a state running 2020-03-14 to 2020-09-25 titled for its own end date, and
  // the sibling states of one work all carrying that same title.
  const titled = {
    ...GOOD,
    states: [
      state({
        valid_from: '2020-03-14',
        valid_to: '2020-09-25',
        hash: HASH_A,
        title: 'Version consolidee applicable au 25/09/2020 : Reglement grand-ducal',
      }),
      state({
        valid_from: '2001-01-01',
        valid_to: '2020-03-14',
        hash: HASH_B,
        title: 'Version consolidee applicable au 25/09/2020 : Reglement grand-ducal',
      }),
    ],
  };
  const html = renderTimeline(titled);

  // The boundary date in its own title is not a disagreement.
  const rows = html.split('<tr class="timeline-row">').slice(1);
  assert.equal(rows.length, 2);
  assert.ok(!rows[1].includes('timeline-title-distrust'), 'a title naming its own end date was flagged');
  assert.ok(rows[0].includes('timeline-title-distrust'), 'a title naming another state was not flagged');
  assert.ok(html.includes("The record's dates place this row; the title never does"));

  // Placement is by the record. The earlier state comes first even though the titles are equal
  // and the caller passed them in the other order.
  assert.ok(rows[0].includes('2001-01-01'), 'the rows were not ordered by the record');
});

test('a withdrawn state is struck and says when', () => {
  const html = renderTimeline({
    ...GOOD,
    states: [A, { ...B, withdrawn: true, withdrawn_from_source: '2026-02-01' }],
  });
  assert.ok(html.includes('data-withdrawn="true"'));
  assert.ok(html.includes('Withdrawn by the publisher on 2026-02-01'));

  assert.throws(
    () => renderTimeline({ ...GOOD, states: [A, { ...B, withdrawn: true }] }),
    /does not say when the publisher withdrew it/,
  );
});

test('every row says whether its text is held and which profile made it', () => {
  const html = renderTimeline({
    ...GOOD,
    states: [A, { ...B, text_available: false }],
  });
  assert.ok(html.includes('text held'));
  assert.ok(html.includes('no text held'));
  assert.ok(html.includes('akn-lu/1'));

  assert.throws(
    () => renderTimeline({ ...GOOD, states: [A, { ...B, text_available: undefined }] }),
    /does not say whether its text is held/,
  );
  assert.throws(
    () => renderTimeline({ ...GOOD, states: [A, { ...B, extraction_profile: '' }] }),
    /does not name its extraction profile/,
  );
});

test('a truncated list names its total and a complete one says so', () => {
  const html = renderTimeline({ ...GOOD, truncated: true, totalCount: 12 });
  assert.ok(html.includes('Showing 2 of 12 states.'));

  assert.throws(
    () => renderTimeline({ ...GOOD, truncated: true }),
    /a list that simply stops reads as a complete one/,
  );
  assert.ok(!renderTimeline(GOOD).includes('Showing'));
});

test('a single held state says so rather than drawing one segment', () => {
  const html = renderTimeline({ ...GOOD, states: [A] });
  assert.ok(html.includes('One held state; publisher history begins 2001-01-01.'));
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
  assert.ok(html.includes(LEGEND));
  assert.equal(
    LEGEND,
    'Top: when the publisher says the state applied. Bottom: when the publisher published it. ' +
      'These routinely differ.',
  );
  assert.ok(html.includes('<div class="timeline-chart" aria-hidden="true">'));
  assert.ok(html.includes('<table class="timeline-table">'), 'the table is the structure');
});

test('a state that cannot be placed is refused rather than placed anyway', () => {
  for (const [field, value, pattern] of [
    ['lex_id', '', /has no lex_id/],
    ['valid_from', '2001-13-01', /valid_from is not a calendar date/],
    ['valid_to', '2001-13-01', /neither null nor a calendar date/],
    ['valid_to', '2000-01-01', /ends before it begins/],
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
    states: [{ ...A, title: '<img src=x onerror=alert(1)>' }, B],
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
});
