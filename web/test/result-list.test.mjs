// The results listbox.
//
// Three spec rules under test, and all three are about what a reader who cannot see the layout
// gets: the banner precedes the results in the document, every badge carries a text label that the
// row itself decided, and the whole list costs one Tab press.
//
// The fixture no longer carries `match_label`, because the component no longer accepts one. A row
// that arrives with a finished badge sentence can be badged "matched your words" while its own
// reasons say `semantic`, and nothing on the page would disagree. What the composed screen proves
// about that rule is in search-react.test.mjs; what this file proves is that the list applies it.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { ResultList } from '../.react-build/app.mjs';

const OFF = {
  fuzzy: { applied: false },
  crosswalk: { applied: false },
  semantic: { applied: false },
};

const HITS = [
  {
    lex_id: 'lu-legilux:code-travail:2021-01-26',
    title: 'Code du travail',
    language: 'fr',
    valid_from: '2021-01-26',
    valid_to: '2021-04-23',
    match_reasons: ['keyword'],
  },
  {
    lex_id: 'eu-eurlex:32016R0679:2018-05-25',
    title: 'Reglement general sur la protection des donnees',
    language: 'fr',
    valid_from: '2018-05-25',
    valid_to: null,
    match_reasons: ['keyword'],
  },
];

const render = (props = {}) =>
  renderToStaticMarkup(
    h(ResultList, {
      hits: HITS,
      relaxations: OFF,
      selected: [],
      onOpen: () => {},
      onToggleSelect: () => {},
      ...props,
    }),
  );

test('the interpretation announcement comes before the results in the document', () => {
  // Not merely above them in the layout. A screen reader user hears document order, and the spec
  // puts this first so they learn their query was rewritten before they hear answers to the
  // rewritten question.
  const html = render({
    relaxations: { ...OFF, fuzzy: { applied: true, expansions: ['many -> mady'] } },
  });
  const banner = html.indexOf('results-interpretation');
  const list = html.indexOf('results-list');
  assert.notEqual(banner, -1);
  assert.notEqual(list, -1);
  assert.equal(banner < list, true, 'the results were announced before the rewrite was');
  assert.equal(html.includes('aria-live="polite"'), true);
});

test('the announcement speaks even when nothing was relaxed', () => {
  // A screen that only speaks when it rewrote something teaches a reader that silence means their
  // words were used, and silence is also what a broken disclosure looks like.
  assert.equal(render().includes('Your exact words were searched'), true);

  const rewritten = render({
    relaxations: {
      fuzzy: { applied: true, expansions: ['many -> mady'] },
      crosswalk: {
        applied: true,
        understood_as: 'garantie locative',
        version: 'crosswalk/1',
        reviewed_on: '2026-08-15',
      },
      semantic: { applied: false },
    },
  });
  assert.equal(rewritten.includes('Your query was changed before it ran'), true);
  assert.equal(rewritten.includes('garantie locative'), true);
  // Escaped in the served bytes: the arrow becomes -&gt;, so a literal never matches. The
  // expansion must still appear verbatim to the reader, which is what this checks.
  assert.equal(rewritten.includes('many -&gt; mady'), true);
});

test('exactly one thing in the list is reachable by Tab', () => {
  // A listbox with fifty tabbable rows makes Tab useless: reaching the content after the results
  // costs fifty presses. The old assertion counted `tabindex="0"` and was satisfied while every
  // row carried a nested Read button, which is tabbable with no tabindex at all.
  const html = render();
  assert.equal((html.match(/tabindex="0"/g) ?? []).length, 1, 'more than one tab stop');
  assert.equal((html.match(/tabindex="-1"/g) ?? []).length, HITS.length - 1);
  for (const focusable of ['<button', '<a ', '<input']) {
    assert.equal(html.includes(focusable), false, `${focusable} in a row is another tab stop`);
  }
});

test('focus is not selection', () => {
  // Moving through candidates is not choosing one. A listbox that marked the focused row as
  // selected would answer on the reader's behalf, which is the same defect as a preselected
  // interstitial candidate.
  const html = render();
  assert.equal((html.match(/aria-selected="false"/g) ?? []).length, HITS.length);
  assert.equal(html.includes('aria-selected="true"'), false, 'a row was marked selected');

  // What does select is the reader saying so, and the list states it.
  const armed = render({ selected: [HITS[1]] });
  assert.equal((armed.match(/aria-selected="true"/g) ?? []).length, 1);
  assert.equal(armed.includes('aria-multiselectable="true"'), true);
});

test('each row is described in its own publisher terms', () => {
  const html = render();
  assert.equal(html.includes('Applicable from 2021-01-26 to 2021-04-23 (publisher)'), true);
  assert.equal(html.includes('Consolidated wording state from 2018-05-25'), true);
});

test('every match badge carries a text label the row itself decided', () => {
  // Colour and position are not the message, and neither is a sentence the caller wrote.
  const html = render();
  assert.equal((html.match(/matched your words/g) ?? []).length, HITS.length);
  assert.equal(
    render({ hits: [{ ...HITS[0], match_reasons: ['exact_title'] }] }).includes(
      'matched on title, not wording',
    ),
    true,
  );
});

test('a row that will not say why it matched is refused', () => {
  for (const bad of [undefined, [], 'keyword']) {
    assert.throws(
      () => render({ hits: [{ ...HITS[0], match_reasons: bad }] }),
      /does not say why it matched/,
    );
  }
  assert.throws(
    () => render({ hits: [{ ...HITS[0], match_reasons: ['vibes'] }] }),
    /is not a match reason/,
  );
});

test('an empty list is not this component', () => {
  assert.throws(() => render({ hits: [] }), /the no-hit card/);
});
