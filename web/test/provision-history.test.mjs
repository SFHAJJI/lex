// S4, one provision's history.
//
// The fixture is a real payload: `article_history` on `lu-legilux:loi-2020-07-17-a624`, anchor
// `art_18`, from_date 2024-01-01, fetched from the production MCP endpoint on 2026-09-01. It was
// chosen because the index shows that article carrying 25 version rows while the service returns
// 2 distinct texts over the range, which is the whole reason this screen has to say what its
// intervals count. It also carries a real `validity_conflict`: the provision takes effect
// 2023-04-01 inside a version applicable from 2023-07-01.

import assert from 'node:assert/strict';
import test from 'node:test';

import {
  ANCHOR_EVENTS,
  EMPTY_NOTE,
  RENUMBER_BASIS,
  TEXT_INTERVAL_NOTE,
  renderProvisionHistory,
} from '../scripts/provision-history.mjs';

const A = 'ba22fa7dfe1d85cddcaaa5ee0ace567accc6b5dc89d4a63ffb34d743fa948c2e';
const B = 'c44875875ad4ef48283cb16474143c600167642d323c30ce4424fde0afbcd5c3';
const link = (date, seed) =>
  `https://law.soufien.lu/lu-legilux/loi-2020-07-17-a624/${date}--${seed.repeat(64)}`;

const LIVE = Object.freeze({
  work: 'lu-legilux:loi-2020-07-17-a624',
  anchor: 'art_18',
  truncated: false,
  distinctTexts: 2,
  states: [
    {
      valid_from: '2023-07-01',
      valid_to: '2024-06-30',
      text_sha256: A,
      article_valid_from: '2023-04-01',
      validity_conflict: true,
      permalink: link('2023-07-01', 'e'),
    },
    {
      valid_from: '2024-07-01',
      valid_to: null,
      text_sha256: B,
      article_valid_from: '2024-07-01',
      permalink: link('2024-07-01', '3'),
    },
  ],
});

test('the rows are distinct wordings, and the screen says so', () => {
  // 25 version rows in the index, 2 distinct texts from the service. A reader counting rows is
  // counting changes to this article, not consolidations of the work, and nothing else on the
  // page distinguishes the two.
  const html = renderProvisionHistory(LIVE);
  assert.ok(html.includes(TEXT_INTERVAL_NOTE));
  assert.ok(TEXT_INTERVAL_NOTE.includes('not consolidations of the work'));
  assert.equal((html.match(/class="provision-state"/g) ?? []).length, 2);
});

test('a validity conflict shows both publisher dates, and only where there is one', () => {
  const html = renderProvisionHistory(LIVE);
  assert.ok(html.includes('2023-04-01'), 'the provision date is missing');
  assert.ok(html.includes('2023-07-01'), 'the version date is missing');
  assert.ok(html.includes('Both are shown because both are the publisher'));

  // The agreeing row says nothing. One conflict block, not two.
  assert.equal((html.match(/two dates for this text/g) ?? []).length, 1);
});

test('the conflict is derived from the dates, not believed from the flag', () => {
  // The live payload omits `validity_conflict` on the agreeing row rather than setting it false,
  // so a screen that trusted the flag would read a missing field as agreement. It derives.
  const [first, second] = LIVE.states;
  const withoutFlag = renderProvisionHistory({
    ...LIVE,
    states: [{ ...first, validity_conflict: undefined }, second],
  });
  assert.ok(withoutFlag.includes('two dates for this text'), 'the conflict vanished with the flag');

  // And a flag that contradicts its own dates is refused rather than rendered either way.
  assert.throws(
    () =>
      renderProvisionHistory({
        ...LIVE,
        states: [first, { ...second, validity_conflict: true }],
      }),
    /declares a validity conflict while its two dates agree/,
  );
});

test('an empty history is a statement about this corpus', () => {
  const html = renderProvisionHistory({ ...LIVE, states: [], distinctTexts: 0 });
  assert.ok(html.includes(EMPTY_NOTE));

  // The subject of the sentence is pinned, not just a phrase inside it. Asserting only that the
  // note contains its own disclaimer let a mutation rewrite the opening clause to "This provision
  // never changed" and still pass, because the disclaimer sat in the half it did not touch.
  assert.ok(
    EMPTY_NOTE.startsWith('This corpus holds no text states'),
    'the empty note stopped being a statement about this corpus',
  );
  assert.ok(EMPTY_NOTE.includes('not about whether the provision ever changed'));
  assert.ok(!html.includes('provision-states'), 'an empty list rendered a state list');
});

test('the counts must agree unless the history says it was cut', () => {
  assert.throws(
    () => renderProvisionHistory({ ...LIVE, distinctTexts: 9 }),
    /one of those two numbers is wrong/,
  );
  // Declared cut, so the count may exceed what is shown, and the page says so.
  const cut = renderProvisionHistory({ ...LIVE, truncated: true, distinctTexts: 9 });
  assert.ok(cut.includes('Showing 2 of 9 distinct wordings'));

  for (const bad of [undefined, null, 'no', 0]) {
    assert.throws(
      () => renderProvisionHistory({ ...LIVE, truncated: bad }),
      /says whether it was cut/,
      `truncated=${JSON.stringify(bad)} was accepted`,
    );
  }
});

test('every row needs its text digest and a canonical permalink', () => {
  const [first, second] = LIVE.states;
  assert.throws(
    () => renderProvisionHistory({ ...LIVE, states: [{ ...first, text_sha256: 'x' }, second] }),
    /has no text digest/,
  );

  // The citation must resolve here. A provision history is a list of citations.
  for (const bad of [
    'https://evil.example/lu-legilux/loi-2020-07-17-a624/2023-07-01--' + 'e'.repeat(64),
    '//evil.example/x',
    'not a url',
  ]) {
    assert.throws(
      () => renderProvisionHistory({ ...LIVE, states: [{ ...first, permalink: bad }, second] }),
      /canonical same-origin permalink/,
      `${bad} was accepted as a citation`,
    );
  }
});

test('the vocabulary comes from the work publisher, and an unclassified one fails closed', () => {
  assert.ok(renderProvisionHistory(LIVE).includes('Applicable from'));

  const union = renderProvisionHistory({
    ...LIVE,
    work: 'eu-eurlex:32016R0679',
    states: LIVE.states.map((state) => ({
      ...state,
      permalink: state.permalink.replace(
        'lu-legilux/loi-2020-07-17-a624',
        'eu-eurlex/32016R0679',
      ),
    })),
  });
  assert.ok(union.includes('Consolidated wording state from'));
  assert.ok(!union.includes('Applicable from'), 'the LU vocabulary reached a Union provision');

  for (const publisher of ['nobody', 'constructor', 'toString']) {
    assert.throws(
      () => renderProvisionHistory({ ...LIVE, work: `${publisher}:some-work` }),
      /is not a publisher this interface has classified|does not name a publisher/,
    );
  }
});

test('lifecycle events are a closed set, and renumbering names no method', () => {
  assert.deepEqual([...ANCHOR_EVENTS], ['inserted', 'removed', 'renumbered']);

  const html = renderProvisionHistory({
    ...LIVE,
    anchorEvents: [{ kind: 'renumbered', from: 'art_18', to: 'art_18bis' }],
  });
  assert.ok(html.includes(RENUMBER_BASIS));

  // The same rule the comparison screen holds: the screen sees two anchors and never observes how
  // the pairing was found, so it must not certify a method.
  for (const method of ['hash', 'mechanically', 'similarity', 'heuristic', 'model']) {
    assert.ok(!RENUMBER_BASIS.includes(method), `the basis certifies ${method}`);
  }
  assert.ok(RENUMBER_BASIS.includes('not publisher-asserted'));

  assert.throws(
    () => renderProvisionHistory({ ...LIVE, anchorEvents: [{ kind: 'merged' }] }),
    /is not an anchor event/,
  );
});
