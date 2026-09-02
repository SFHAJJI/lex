// Arming a comparison.
//
// The rule under test is that a control does not offer an action it will refuse. compare.mjs
// already refuses two different works; applying the same rule at arming time means the reader
// learns why while both rows are still in front of them.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { CompareArming, armedBy, armingRefusal, compareIfArmed } from '../.react-build/app.mjs';

const A = { lex_id: 'lu-legilux:code-travail:2021-01-26' };
const B = { lex_id: 'lu-legilux:code-travail:2021-04-23' };
const OTHER = { lex_id: 'lu-legilux:code-civil:2021-01-26' };

const render = (selected) =>
  renderToStaticMarkup(h(CompareArming, { selected, onCompare: () => {} }));

test('two states of one work arm the control', () => {
  assert.equal(armingRefusal([A, B]), null);
  const html = render([A, B]);
  assert.equal(html.includes('Two states of one work selected'), true);
  assert.equal(html.includes('aria-disabled="false"'), true, 'a legal pair did not arm');
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
  assert.equal(html.includes('aria-disabled="true"'), true, 'an illegal pair armed the control');
  assert.equal(html.includes('two different works'), true);
});

test('fewer or more than two says what is needed, and does not arm', () => {
  assert.equal(armingRefusal([]), null);
  assert.equal(armingRefusal([A]), null);
  assert.equal(render([]).includes('Select two states'), true);
  assert.equal(render([A]).includes('Select a second'), true);
  assert.equal(render([]).includes('aria-disabled="true"'), true);
  assert.equal(render([A]).includes('aria-disabled="true"'), true);

  const three = armingRefusal([A, B, OTHER]);
  assert.equal(three.includes('between two states'), true);
  assert.equal(render([A, B, OTHER]).includes('aria-disabled="true"'), true);
});

test('a row that does not name a work cannot arm', () => {
  const refusal = armingRefusal([A, { lex_id: 'garbage' }]);
  assert.equal(refusal.includes('does not name a publisher, a work and a state'), true);
  assert.equal(render([A, { lex_id: 'garbage' }]).includes('aria-disabled="true"'), true);
});

test('the selection state is announced', () => {
  // A reader who cannot see the button change needs to hear what the selection now permits.
  assert.equal(render([A]).includes('aria-live="polite"'), true);
});

test('an unavailable Compare stays reachable by keyboard and says why', () => {
  // `disabled` removes a button from the tab order, so a reader moving by keyboard never arrives
  // at it: they are never told comparison exists and never hear why this pair cannot be compared.
  // That is the opposite of the reason this control refuses early. The browser run measured it on
  // the composed screen, fifteen focusable elements and fourteen reachable by Tab, and this is
  // that finding in a form that does not need a browser.
  const html = render([A, OTHER]);
  assert.equal(/<button[^>]*\sdisabled/.test(html), false, 'the button left the tab order');
  assert.equal(html.includes('aria-disabled="true"'), true);
  // And it points at the sentence that explains it, so reaching it is worth something.
  const described = /aria-describedby="([^"]+)"/.exec(html)?.[1];
  assert.ok(described, 'the button explains nothing to the reader who reaches it');
  assert.ok(html.includes(`id="${described}"`), 'aria-describedby points at nothing');
});

test('a selection that cannot arm refuses the action, not only the attribute', () => {
  // aria-disabled announces unavailability and does not stop a click the way `disabled` does,
  // which is the price of keeping the control in the tab order. So the rule has to refuse, and
  // this drives the rule rather than the markup that describes it.
  let compared = 0;
  const onCompare = () => {
    compared += 1;
  };
  for (const refused of [[], [A], [A, OTHER], [A, B, OTHER], [A, { lex_id: 'garbage' }]]) {
    assert.equal(armedBy(refused), false, `${JSON.stringify(refused)} armed the control`);
    assert.equal(compareIfArmed(refused, onCompare), false);
  }
  assert.equal(compared, 0, 'a refused selection was compared anyway');

  assert.equal(armedBy([A, B]), true);
  assert.equal(compareIfArmed([A, B], onCompare), true);
  assert.equal(compared, 1, 'a legal pair could not be compared');
});

test('what the markup says is unavailable is exactly what the rule refuses', () => {
  // Otherwise the button and the guard are two opinions, and the one a reader acts on is the
  // button's.
  for (const selected of [[], [A], [A, B], [A, OTHER], [A, B, OTHER], [A, { lex_id: 'garbage' }]]) {
    const html = render(selected);
    assert.equal(
      html.includes(`aria-disabled="${!armedBy(selected)}"`),
      true,
      `the markup and the rule disagree about ${JSON.stringify(selected)}`,
    );
  }
});
