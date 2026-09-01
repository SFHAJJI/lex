// Filter chips.
//
// aria-pressed is the contract, and the count of what is hidden is the trust rule: a filtered
// page that reports only what it shows cannot be told apart from an unfiltered one.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { FilterChips } from '../.react-build/app.mjs';

const FILTERS = [
  { key: 'loi', label: 'LOI', active: true, hides: 20 },
  { key: 'rgd', label: 'RGD', active: false, hides: 0 },
];

const render = (props = {}) =>
  renderToStaticMarkup(
    h(FilterChips, { filters: FILTERS, total: 47, shown: 12, onToggle: () => {}, ...props }),
  );

test('every chip states whether it is on', () => {
  // A sighted reader gets fill, border and position. A screen reader gets none of those, so
  // without aria-pressed it is told a filter exists and not whether it is applied.
  const html = render();
  assert.equal((html.match(/aria-pressed="true"/g) ?? []).length, 1);
  assert.equal((html.match(/aria-pressed="false"/g) ?? []).length, 1);
});

test('the page says how many rows the filters are hiding, not only how many remain', () => {
  // "Showing 12" is a fact about the page. "12 of 47, 35 hidden" is a fact about the corpus and
  // the filters together, and only the second lets a reader judge whether to turn one off.
  const html = render();
  assert.equal(html.includes('Showing 12 of 47'), true);
  assert.equal(html.includes('35 hidden by filters'), true);
});

test('an unfiltered page says so rather than staying silent', () => {
  // Silence is what a broken disclosure looks like, so the honest case speaks too.
  const html = render({
    filters: FILTERS.map((f) => ({ ...f, active: false })),
    total: 47,
    shown: 47,
  });
  assert.equal(html.includes('Showing all 47'), true);
  assert.equal(html.includes('No filter is active'), true);
  assert.equal(html.includes('hidden by filters'), false);
});

test('a count that cannot be true is refused', () => {
  // More shown than held is a page describing a corpus it does not have.
  assert.throws(() => render({ total: 10, shown: 12 }), /count before and after/);
  assert.throws(() => render({ total: undefined }), /count before and after/);
});

test('an empty filter group is refused', () => {
  assert.throws(() => render({ filters: [] }), /chrome that does nothing/);
});
