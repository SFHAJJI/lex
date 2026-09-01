import assert from 'node:assert/strict';
import test from 'node:test';

import {
  HOLE_KINDS,
  renderHole,
  renderProvisional,
  renderValidityConflict,
} from '../scripts/state-qualifiers.mjs';

test('a validity conflict shows both publisher dates and resolves neither', () => {
  // The live case, measured today: four of the five held wording states of the flagship
  // article carry a per-article date of 2020-11-01 inside enclosing states applicable from
  // four later dates. Corpus-wide it is 39.8 percent of Luxembourg provision states.
  const html = renderValidityConflict({
    semantics: 'publisher_applicability',
    stateValidFrom: '2021-01-01',
    wordingValidFrom: '2020-11-01',
  });
  assert.ok(html.includes('2021-01-01'));
  assert.ok(html.includes('2020-11-01'));
  // Escaped, because the component escapes its own text: the apostrophe arrives as &#39;.
  assert.ok(html.includes('Both are the publisher&#39;s'));
  assert.ok(html.includes('Not resolved'));
  assert.ok(html.includes('token--conflict'));
  assert.ok(html.includes('token-icon'));
  assert.ok(html.includes('token-label'));
});

test('a conflict badge on agreeing dates is refused', () => {
  // A badge that appears where there is no conflict teaches a reader to ignore it where
  // there is one.
  assert.throws(
    () =>
      renderValidityConflict({ stateValidFrom: '2021-01-01', wordingValidFrom: '2021-01-01', semantics: 'publisher_applicability' }),
    /no conflict to badge/,
  );
});

test('a conflict needs two dates that are dates', () => {
  for (const [state, wording] of [
    ['2026-99-99', '2020-11-01'],
    ['2021-01-01', '2025-02-29'],
    [undefined, '2020-11-01'],
  ]) {
    assert.throws(
      () => renderValidityConflict({ stateValidFrom: state, wordingValidFrom: wording }),
      /not a calendar date/,
      `${state} / ${wording} was badged`,
    );
  }
});

test('provisional is decided against the reader date, not a machine clock', () => {
  const html = renderProvisional({ validFrom: '2030-09-15', asOf: '2026-09-01', semantics: 'publisher_applicability' });
  assert.ok(html.includes('Publisher-scheduled state, applicable from 2030-09-15'));
  assert.ok(html.includes('As of 2026-09-01 it has not begun'));
  assert.ok(html.includes('token--provisional'));

  // A state that has begun is not provisional, and saying it is would be false.
  assert.throws(
    () => renderProvisional({ validFrom: '2020-01-01', asOf: '2026-09-01', semantics: 'publisher_applicability' }),
    /has begun as of/,
  );
  assert.throws(
    () => renderProvisional({ validFrom: '2026-09-01', asOf: '2026-09-01' }),
    /has begun as of/,
  );
});

test('the two kinds of hole are different claims and the caption says which', () => {
  assert.deepEqual([...HOLE_KINDS], ['no_state_held', 'continuity_inferred']);

  const absent = renderHole({ kind: 'no_state_held', from: '2004-01-01', to: '2024-01-01' });
  // This screen knows what this corpus holds and nothing else. The kind is named
  // no_state_HELD for that reason, and the caption used to overstate its own kind by
  // claiming no publisher state exists: the publisher may hold a state here that was
  // never ingested.
  assert.ok(absent.includes('This corpus holds no state covering 2004-01-01 to 2024-01-01'));
  assert.ok(absent.includes('Absence here is not absence from the publisher'));
  assert.ok(
    !absent.includes('No publisher state covers'),
    'a gap in this corpus was reported as a gap in the publisher record',
  );

  const inferred = renderHole({
    kind: 'continuity_inferred',
    from: '2004-01-01',
    to: '2024-01-01',
  });
  // "The previous wording continued" is this product's inference from an absence. Presenting
  // it as the publisher's would turn a missing record into an assertion about the law.
  assert.ok(inferred.includes('is inferred from the absence of a later held state'));
  assert.ok(inferred.includes('The publisher does not assert it'));

  assert.notEqual(absent, inferred);
  for (const html of [absent, inferred]) {
    assert.ok(html.includes('token--hole'));
  }
});

test('a hole kind cannot be chosen after the fact', () => {
  for (const kind of [undefined, 'gap', 'toString', 'constructor']) {
    assert.throws(
      () => renderHole({ kind, from: '2004-01-01', to: '2024-01-01' }),
      /is not a hole kind/,
      `${String(kind)} was captioned`,
    );
  }
});

test('a hole with no duration is a boundary, not a gap', () => {
  assert.throws(
    () => renderHole({ kind: 'no_state_held', from: '2004-01-01', to: '2004-01-01' }),
    /is not a period/,
  );
  assert.throws(
    () => renderHole({ kind: 'no_state_held', from: '2024-01-01', to: '2004-01-01' }),
    /is not a period/,
  );
});

test('values are escaped rather than trusted', () => {
  // Dates are validated, so the injection surface here is the kind, which reaches a class
  // and a data attribute.
  assert.throws(
    () => renderHole({ kind: '"><img src=x>', from: '2004-01-01', to: '2024-01-01' }),
    /is not a hole kind/,
  );
});

test('O5: qualifiers speak the publisher vocabulary, not a hardcoded one', () => {
  // Hardcoded applicability made an EU consolidation state read as an applicability claim the
  // publisher never made, on the two badges most likely to be quoted out of context.
  const luConflict = renderValidityConflict({
    stateValidFrom: '2003-01-01',
    wordingValidFrom: '2001-01-01',
    semantics: 'publisher_applicability',
  });
  assert.equal(luConflict.includes('applicable from'), true);

  const euConflict = renderValidityConflict({
    stateValidFrom: '2003-01-01',
    wordingValidFrom: '2001-01-01',
    semantics: 'official_consolidation_state',
  });
  assert.equal(euConflict.includes('a consolidated wording state from'), true);
  assert.equal(
    euConflict.includes('applicable from'),
    false,
    'a consolidation state was badged as applicability',
  );

  const euProvisional = renderProvisional({
    validFrom: '2030-09-15',
    asOf: '2026-09-01',
    semantics: 'official_consolidation_state',
  });
  assert.equal(euProvisional.includes('a consolidated wording state from'), true);
  assert.equal(euProvisional.includes('applicable from'), false);
});

test('O5: a qualifier without a declared vocabulary is refused', () => {
  for (const semantics of [undefined, null, '', 'in_force']) {
    assert.throws(
      () => renderValidityConflict({ stateValidFrom: '2003-01-01', wordingValidFrom: '2001-01-01', semantics }),
      /renders in the publisher's own vocabulary/,
    );
    assert.throws(
      () => renderProvisional({ validFrom: '2030-09-15', asOf: '2026-09-01', semantics }),
      /renders in the publisher's own vocabulary/,
    );
  }
});
