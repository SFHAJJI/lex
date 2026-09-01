// Arming a comparison.
//
// The rule under test is that a control does not offer an action it will refuse. compare.mjs
// already refuses two different works; applying the same rule at arming time means the reader
// learns why while both rows are still in front of them.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { CompareArming, armingRefusal } from '../.react-build/app.mjs';

const A = { lex_id: 'lu-legilux:code-travail:2021-01-26' };
const B = { lex_id: 'lu-legilux:code-travail:2021-04-23' };
const OTHER = { lex_id: 'lu-legilux:code-civil:2021-01-26' };

const render = (selected) =>
  renderToStaticMarkup(h(CompareArming, { selected, onCompare: () => {} }));

test('two states of one work arm the control', () => {
  assert.equal(armingRefusal([A, B]), null);
  const html = render([A, B]);
  assert.equal(html.includes('Two states of one work selected'), true);
  assert.equal(html.includes('disabled'), false, 'a legal pair did not arm');
});

test('two different works cannot arm, and the reason is the one compare gives', () => {
  // The refusal arrives at arming time rather than after the reader has pressed Compare. A
  // control that offers an action it will refuse teaches a reader that refusals are noise.
  const refusal = armingRefusal([A, OTHER]);
  assert.equal(refusal.includes('two different works'), true);
  assert.equal(
    refusal.includes('not states of each other'),
    true,
    'the arming refusal used different words than the comparison refusal',
  );
  const html = render([A, OTHER]);
  assert.equal(html.includes('disabled'), true, 'an illegal pair armed the control');
  assert.equal(html.includes('two different works'), true);
});

test('fewer or more than two says what is needed, and does not arm', () => {
  assert.equal(armingRefusal([]), null);
  assert.equal(armingRefusal([A]), null);
  assert.equal(render([]).includes('Select two states'), true);
  assert.equal(render([A]).includes('Select a second'), true);
  assert.equal(render([]).includes('disabled'), true);
  assert.equal(render([A]).includes('disabled'), true);

  const three = armingRefusal([A, B, OTHER]);
  assert.equal(three.includes('between two states'), true);
  assert.equal(render([A, B, OTHER]).includes('disabled'), true);
});

test('a row that does not name a work cannot arm', () => {
  const refusal = armingRefusal([A, { lex_id: 'garbage' }]);
  assert.equal(refusal.includes('does not name a publisher, a work and a state'), true);
  assert.equal(render([A, { lex_id: 'garbage' }]).includes('disabled'), true);
});

test('the selection state is announced', () => {
  // A reader who cannot see the button change needs to hear what the selection now permits.
  assert.equal(render([A]).includes('aria-live="polite"'), true);
});
