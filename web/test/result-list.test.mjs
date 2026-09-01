// The results listbox.
//
// Two spec rules under test, and both are about what a reader who cannot see the layout gets.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { ResultList } from '../.react-build/app.mjs';

const HITS = [
  {
    lex_id: 'lu-legilux:code-travail:2021-01-26',
    title: 'Code du travail',
    valid_from: '2021-01-26',
    valid_to: '2021-04-23',
    match_label: 'matched your words',
  },
  {
    lex_id: 'eu-eurlex:32016R0679:2018-05-25',
    title: 'Reglement general sur la protection des donnees',
    valid_from: '2018-05-25',
    valid_to: null,
    match_label: 'matched your words',
  },
];

const render = (props = {}) =>
  renderToStaticMarkup(h(ResultList, { hits: HITS, onOpen: () => {}, ...props }));

test('the interpretation announcement comes before the results in the document', () => {
  // Not merely above them in the layout. A screen reader user hears document order, and the
  // spec puts this first so they learn their query was rewritten before they hear answers to
  // the rewritten question.
  const html = render({ expansions: ['many -> mady'] });
  const banner = html.indexOf('results-interpretation');
  const list = html.indexOf('results-list');
  assert.notEqual(banner, -1);
  assert.notEqual(list, -1);
  assert.equal(banner < list, true, 'the results were announced before the rewrite was');
  assert.equal(html.includes('aria-live="polite"'), true);
});

test('the announcement speaks even when nothing was relaxed', () => {
  // A screen that only speaks when it rewrote something teaches a reader that silence means
  // their words were used, and silence is also what a broken disclosure looks like.
  const html = render();
  assert.equal(html.includes('Your exact words were searched'), true);

  const rewritten = render({ expansions: ['many -> mady'], understoodAs: 'garantie locative' });
  assert.equal(rewritten.includes('Your query was changed before it ran'), true);
  assert.equal(rewritten.includes('garantie locative'), true);
  // Escaped in the served bytes: the arrow becomes -&gt;, so a literal never matches. The
  // expansion must still appear verbatim to the reader, which is what this checks.
  assert.equal(rewritten.includes('many -&gt; mady'), true);
});

test('exactly one row is reachable by Tab', () => {
  // A listbox with fifty tabbable rows makes Tab useless: reaching the content after the results
  // costs fifty presses. Roving tabindex is what makes that content reachable at all.
  const html = render();
  assert.equal((html.match(/tabindex="0"/g) ?? []).length, 1, 'more than one tab stop');
  assert.equal((html.match(/tabindex="-1"/g) ?? []).length, HITS.length - 1);
});

test('focus is not selection', () => {
  // Moving through candidates is not choosing one. A listbox that marked the focused row as
  // selected would answer on the reader's behalf, which is the same defect as a preselected
  // interstitial candidate.
  const html = render();
  assert.equal((html.match(/aria-selected="false"/g) ?? []).length, HITS.length);
  assert.equal(html.includes('aria-selected="true"'), false, 'a row was marked selected');
});

test('each row is described in its own publisher terms', () => {
  const html = render();
  assert.equal(html.includes('Applicable from 2021-01-26 to 2021-04-23 (publisher)'), true);
  assert.equal(html.includes('Consolidated wording state from 2018-05-25'), true);
});

test('every match badge carries a text label', () => {
  // Colour and position are not the message.
  const html = render();
  assert.equal((html.match(/matched your words/g) ?? []).length, HITS.length);
});

test('an empty list is not this component', () => {
  assert.throws(() => render({ hits: [] }), /the no-hit card/);
});
