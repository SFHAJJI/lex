import assert from 'node:assert/strict';
import test from 'node:test';

import { REFUSAL_CODES, RETRYABLE, renderRefusalCard } from '../scripts/refusal-card.mjs';

const GOVERNING = 'Art. L. 121-6. Le salarié incapable de travailler pour cause de maladie...';

test('the registry is closed at the nineteen product-spec codes', () => {
  assert.equal(REFUSAL_CODES.length, 19);
  assert.equal(new Set(REFUSAL_CODES).size, 19, 'a code is listed twice');
  for (const code of ['identifier_unknown', 'no_version_for_date', 'advice_boundary']) {
    assert.ok(REFUSAL_CODES.includes(code));
  }
});

test('an unknown code is refused rather than rendered as a generic error', () => {
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'something_went_wrong',
        sentence: 'x',
        payload: { a: 'b' },
      }),
    /the registry is closed/,
  );
  // The UX spec's informal names are not in the versioned registry, and must not silently work.
  assert.throws(
    () => renderRefusalCard({ code: 'unknown_work', sentence: 'x', payload: { a: 'b' } }),
    /the registry is closed/,
  );
});

test('a sterile refusal cannot be constructed', () => {
  assert.throws(
    () => renderRefusalCard({ code: 'no_version_for_date', sentence: 'No state covers that date.' }),
    /carries no payload, no governing text and no handoff/,
  );
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'no_version_for_date',
        sentence: 'No state covers that date.',
        payload: {},
      }),
    /carries no payload, no governing text and no handoff/,
  );
});

test('every code in the registry can be rendered with a payload', () => {
  for (const code of REFUSAL_CODES) {
    const card = renderRefusalCard({
      code,
      sentence: 'One human sentence.',
      payload: { work: 'loi-2006-07-31-n2' },
      // advice_boundary additionally requires the governing text, so give every card one.
      governingText: GOVERNING,
    });
    assert.ok(card.includes(code), `${code} lost its machine code chip`);
  }
});

test('advice_boundary must co-deliver the governing provisions', () => {
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'advice_boundary',
        sentence: 'I cannot apply the law to your situation.',
        payload: { handoff: 'CSL, ITM, SAIJ' },
      }),
    /must co-deliver the governing provisions/,
  );

  const good = renderRefusalCard({
    code: 'advice_boundary',
    sentence: 'I cannot apply the law to your situation.',
    payload: { handoff: 'CSL, ITM, SAIJ' },
    governingText: GOVERNING,
  });
  assert.ok(good.includes('The published text, in full'));
  assert.ok(good.includes('L. 121-6'));
});

test('a refusal is not announced as an error', () => {
  const card = renderRefusalCard({
    code: 'profiles_differ',
    sentence: 'These two states came from different extraction profiles.',
    payload: { profiles: ['pdf-lu/1', 'akn-lu/1'] },
  });
  assert.ok(!card.includes('role="alert"'), 'a refusal was announced as an alert');
  assert.ok(!card.includes('aria-live'), 'a refusal was put in a live region');
  assert.ok(card.includes('refusal-card'));
});

test('the machine code is rendered as code, and the sentence carries the token', () => {
  const card = renderRefusalCard({
    code: 'text_withheld',
    sentence: 'The publisher licence does not permit serving this text.',
    payload: { licence: 'licenceSCL' },
  });
  assert.ok(card.includes('<code class="refusal-code">text_withheld</code>'));
  assert.ok(card.includes('token-icon'), 'the refusal token lost its icon');
  assert.ok(card.includes('token-label'), 'the refusal token lost its label');
});

test('retryable refusals say so, and the rest do not', () => {
  assert.deepEqual([...RETRYABLE].sort(), ['rate_limited', 'upstream_unreachable']);
  const retryable = renderRefusalCard({
    code: 'upstream_unreachable',
    sentence: 'The publisher did not answer.',
    payload: { host: 'legilux.public.lu' },
  });
  assert.ok(retryable.includes('worth retrying'));

  const terminal = renderRefusalCard({
    code: 'out_of_corpus_scope',
    sentence: 'That instrument is outside the reviewed corpus.',
    payload: { classified_as: 'CSSF circular' },
  });
  assert.ok(!terminal.includes('worth retrying'));
});

test('quoted statutory text carries its own language attribute', () => {
  const card = renderRefusalCard({
    code: 'advice_boundary',
    sentence: 'I cannot apply the law to your situation.',
    governingText: GOVERNING,
  });
  assert.ok(card.includes('lang="fr"'), 'French statute was not marked as French');
});

test('payload values are escaped rather than trusted', () => {
  const card = renderRefusalCard({
    code: 'identifier_unknown',
    sentence: 'That identifier does not resolve.',
    payload: { echoed: '<img src=x onerror=alert(1)>' },
  });
  assert.ok(!card.includes('<img'));
  assert.ok(card.includes('&lt;img'));
});

test('a card without a human sentence is refused', () => {
  assert.throws(
    () => renderRefusalCard({ code: 'rate_limited', sentence: '   ', payload: { a: 'b' } }),
    /requires one human sentence/,
  );
});
