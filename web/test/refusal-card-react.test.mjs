// The first end-to-end port, measured rather than asserted.
//
// #360 requires one truth module ported into React and measured before expanding. The claim
// being tested is not "React works": it is that the React runtime applies the same rules and
// produces the same marks, because a framework that quietly becomes a second home for legal
// rules is the worst available outcome of adopting one.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';

import { RefusalCard } from '../.react-build/app.mjs';
import { renderDocument } from '../.react-build/app.mjs';
import { renderRefusalCard, RETRY_SENTENCE, RETRYABLE, validateRefusal } from '../scripts/refusal-card.mjs';
import { renderToStaticMarkup } from 'react-dom/server';


const HANDOFF = [
  { label: 'Synthetic counter one', href: 'https://handoff.invalid/one' },
  { label: 'Synthetic counter two', href: 'https://handoff.invalid/two' },
];



function react(props) {
  return renderToStaticMarkup(h(RefusalCard, props));
}

test('the React card applies the same closed registry as the string renderer', () => {
  // Same rule, one implementation. If these diverge, the framework has become a second place
  // where a legal rule lives, which is exactly what the port must not do.
  for (const bad of ['not_a_code', '', 'unknown_work']) {
    assert.throws(() => react({ code: bad, sentence: 'x', handoff: HANDOFF }), /unknown refusal code/);
    assert.throws(
      () => renderRefusalCard({ code: bad, sentence: 'x', handoff: HANDOFF }),
      /unknown refusal code/,
    );
  }
});

test('the React card refuses a sterile refusal, exactly as the string renderer does', () => {
  const sterile = { code: 'upstream_unreachable', sentence: 'The publisher did not answer.' };
  assert.throws(() => react(sterile), /carries no payload, no governing text and no handoff/);
  assert.throws(() => renderRefusalCard(sterile), /carries no payload, no governing text and no handoff/);
});

test('the React card carries the code, the sentence and the retry line verbatim', () => {
  const html = react({
    code: 'upstream_unreachable',
    sentence: 'The publisher did not answer in time.',
    handoff: HANDOFF,
  });
  assert.equal(html.includes('<code class="refusal-code">upstream_unreachable</code>'), true);
  assert.equal(html.includes('The publisher did not answer in time.'), true);
  assert.equal(RETRYABLE.has('upstream_unreachable'), true);
  assert.equal(html.includes(RETRY_SENTENCE), true);
});

test('a non-retryable refusal shows no retry line in either renderer', () => {
  // Bound to the shared RETRYABLE set rather than a hardcoded list, so the two renderers cannot
  // disagree about which refusals are worth retrying.
  const props = {
    code: 'language_not_available',
    sentence: 'This work is not held in the language you asked for.',
    handoff: HANDOFF,
  };
  assert.equal(RETRYABLE.has('language_not_available'), false);
  assert.equal(react(props).includes(RETRY_SENTENCE), false);
  assert.equal(renderRefusalCard(props).includes(RETRY_SENTENCE), false);
});

test('a refusal is an answer, never an alert, in the React runtime too', () => {
  // No role="alert" and no live region. Announcing a refusal as an alert is the aural
  // equivalent of the red error toast the spec rules out.
  const html = react({
    code: 'upstream_unreachable',
    sentence: 'The publisher did not answer.',
    handoff: HANDOFF,
  });
  assert.equal(html.includes('role="alert"'), false);
  assert.equal(html.includes('aria-live'), false);
});

test('a hostile handoff refuses the card in both renderers, rather than being dropped', () => {
  // `javascript:alert(1)` escapes to a perfectly safe attribute value and remains a working
  // link, so escaping is not the guard. The contract is stronger than silently dropping the bad
  // entry: a card that quietly renders fewer next steps than it was given has edited the
  // publisher's own list without saying so.
  const hostile = {
    code: 'upstream_unreachable',
    sentence: 'The publisher did not answer.',
    handoff: [
      { label: 'Synthetic counter one', href: 'https://handoff.invalid/one' },
      { label: 'Hostile', href: 'javascript:alert(1)' },
    ],
  };
  assert.throws(() => react(hostile), /must be an https URI/);
  assert.throws(() => renderRefusalCard(hostile), /must be an https URI/);
});

test('the validator is the single source both renderers consult', () => {
  // Binds the two together at the contract rather than by resemblance: whatever the validator
  // returns is what both must show.
  const props = {
    code: 'upstream_unreachable',
    sentence: 'The publisher did not answer.',
    handoff: HANDOFF,
  };
  const card = validateRefusal(props);
  assert.equal(card.retryable, true);
  assert.equal(card.handoffs.length, 2);
  const html = react(props);
  for (const one of card.handoffs) {
    assert.equal(html.includes(one.label), true, `${one.label} was dropped by the React card`);
  }
});
