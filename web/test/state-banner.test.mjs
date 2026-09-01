import assert from 'node:assert/strict';
import test from 'node:test';

import {
  OPEN_ENDED_SENTINEL,
  TIMELINE_SEMANTICS,
  renderPublisherStatusFlag,
  renderStateBanner,
} from '../scripts/state-banner.mjs';

// The Luxembourg rent-law state and the GDPR state used throughout the specs, so the tests
// exercise shapes the publishers actually produce rather than shapes invented here.
const LU_STATE = {
  valid_from: '2021-01-26',
  valid_to: '2023-07-19',
  publication_date: '2021-01-20',
  observed_from: '2026-08-14T23:05:14Z',
};

const EU_STATE = {
  valid_from: '2016-05-04',
  valid_to: '2018-05-25',
  publication_date: '2016-05-04',
  observed_from: '2026-08-14T23:05:14Z',
};

test('legal-time phrasing comes from the publisher vocabulary, not from the renderer', () => {
  const lu = renderStateBanner({
    envelope: { timeline_semantics: 'publisher_applicability' },
    state: LU_STATE,
  });
  assert.ok(lu.includes('Applicable from 2021-01-26 to 2023-07-19 (publisher)'));
  assert.ok(!lu.includes('Consolidated wording state'));

  const eu = renderStateBanner({
    envelope: { timeline_semantics: 'official_consolidation_state' },
    state: EU_STATE,
  });
  assert.ok(eu.includes('Consolidated wording state from 2016-05-04 to 2018-05-25'));
  assert.ok(!eu.includes('Applicable from'));
});

test('the words "in force" never appear on a state row of either corpus', () => {
  for (const semantics of TIMELINE_SEMANTICS) {
    const html = renderStateBanner({
      envelope: { timeline_semantics: semantics },
      // The live trap: the GDPR row carries binding_status in_force on a state that
      // predates entry into force. A renderer that reaches for it states something false.
      state: { ...EU_STATE, binding_status: 'in_force' },
    });
    assert.ok(!/in force/i.test(html), `"in force" leaked into a ${semantics} state row`);
    assert.ok(!html.includes('in_force'), `binding_status leaked into a ${semantics} row`);
  }
});

test('an unknown or missing timeline_semantics is refused rather than defaulted', () => {
  assert.throws(
    () => renderStateBanner({ envelope: {}, state: LU_STATE }),
    /unknown timeline_semantics/,
  );
  assert.throws(
    () => renderStateBanner({ envelope: { timeline_semantics: 'in_force' }, state: LU_STATE }),
    /unknown timeline_semantics/,
  );
});

test('the open-ended sentinel is not rendered as an end date', () => {
  const html = renderStateBanner({
    envelope: { timeline_semantics: 'publisher_applicability' },
    state: { ...LU_STATE, valid_to: OPEN_ENDED_SENTINEL },
  });
  assert.ok(!html.includes('9999'), 'the sentinel was printed as a date');
  assert.ok(html.includes('with no end recorded by the publisher'));
});

test('a null valid_to reads the same as the sentinel, because they mean the same thing', () => {
  const sentinel = renderStateBanner({
    envelope: { timeline_semantics: 'publisher_applicability' },
    state: { ...LU_STATE, valid_to: OPEN_ENDED_SENTINEL },
  });
  const nulled = renderStateBanner({
    envelope: { timeline_semantics: 'publisher_applicability' },
    state: { ...LU_STATE, valid_to: null },
  });
  assert.equal(sentinel, nulled);
});

test('the observation timestamp is rendered verbatim', () => {
  const html = renderStateBanner({
    envelope: { timeline_semantics: 'publisher_applicability' },
    state: LU_STATE,
  });
  assert.ok(html.includes('2026-08-14T23:05:14Z'), 'the UTC instant was reformatted');
});

test('both clocks are present and both carry an icon and a label', () => {
  const html = renderStateBanner({
    envelope: { timeline_semantics: 'publisher_applicability' },
    state: LU_STATE,
  });
  assert.ok(html.includes('state-banner-legal'));
  assert.ok(html.includes('state-banner-record'));
  assert.equal(html.split('token-icon').length - 1, 2, 'a clock lost its icon');
  assert.equal(html.split('token-label').length - 1, 2, 'a clock lost its label');
});

test('a missing publication date is stated, not silently omitted', () => {
  const html = renderStateBanner({
    envelope: { timeline_semantics: 'publisher_applicability' },
    state: { ...LU_STATE, publication_date: null },
  });
  assert.ok(html.includes('Publication date not recorded by the publisher'));
});

test('a state without valid_from or observed_from is refused', () => {
  assert.throws(
    () =>
      renderStateBanner({
        envelope: { timeline_semantics: 'publisher_applicability' },
        state: { ...LU_STATE, valid_from: '' },
      }),
    /requires valid_from/,
  );
  assert.throws(
    () =>
      renderStateBanner({
        envelope: { timeline_semantics: 'publisher_applicability' },
        state: { ...LU_STATE, observed_from: undefined },
      }),
    /requires observed_from/,
  );
});

test('the publisher status flag always carries its caption', () => {
  const html = renderPublisherStatusFlag('in_force');
  assert.ok(html.includes('in_force'));
  assert.ok(
    html.includes('publisher status flag, current-state flag, not a historical statement'),
  );
});

test('the timeline vocabulary is closed against the object prototype', () => {
  // `LEGAL_TIME_PHRASING` was an object literal and the membership check was truthiness, so
  // an inherited member found a function and rendered `[object Undefined]` as legal time.
  for (const semantics of ['toString', 'constructor', 'hasOwnProperty', 'valueOf', '__proto__']) {
    assert.throws(
      () =>
        renderStateBanner({
          envelope: { timeline_semantics: semantics },
          state: {
            valid_from: '2001-01-01',
            valid_to: null,
            publication_date: '2000-12-01',
            observed_from: '2026-01-01T00:00:00Z',
          },
        }),
      /unknown timeline_semantics/,
      `${semantics} reached a phrasing function`,
    );
  }
});

test('legal time is checked before it is printed', () => {
  const envelope = { timeline_semantics: 'publisher_applicability' };
  const good = {
    valid_from: '2001-01-01',
    valid_to: '2002-01-01',
    publication_date: '2000-12-01',
    observed_from: '2026-01-01T00:00:00Z',
  };

  // The hostile probe rendered "Applicable from 2026-99-99" and "First observed
  // not-a-timestamp". A reader cannot tell a publisher's odd date from our own broken one.
  for (const [field, value, pattern] of [
    ['valid_from', '2026-99-99', /valid_from is not a calendar date/],
    ['valid_from', '2025-02-29', /valid_from is not a calendar date/],
    ['valid_to', '2026-13-01', /valid_to is not a calendar date/],
    ['publication_date', 'yesterday', /publication_date is not a calendar date/],
    ['observed_from', 'not-a-timestamp', /observed_from is not a UTC instant/],
    ['observed_from', '2026-01-01', /observed_from is not a UTC instant/],
    ['observed_from', '2026-01-01T00:00:00+01:00', /observed_from is not a UTC instant/],
  ]) {
    assert.throws(
      () => renderStateBanner({ envelope, state: { ...good, [field]: value } }),
      pattern,
      `${field}=${value} was rendered`,
    );
  }

  assert.ok(renderStateBanner({ envelope, state: good }).includes('2001-01-01'));
  // 2024 is a leap year, so this is a day and must render.
  assert.ok(
    renderStateBanner({
      envelope,
      state: { ...good, valid_from: '2024-02-29', valid_to: null },
    }).includes('2024-02-29'),
  );
});

test('an inverted interval is refused rather than printed backwards', () => {
  assert.throws(
    () =>
      renderStateBanner({
        envelope: { timeline_semantics: 'publisher_applicability' },
        state: {
          valid_from: '2002-01-01',
          valid_to: '2001-01-01',
          publication_date: '2000-12-01',
          observed_from: '2026-01-01T00:00:00Z',
        },
      }),
    /is after valid_to/,
  );
});
