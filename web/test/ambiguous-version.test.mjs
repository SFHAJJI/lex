// The ambiguous-version interstitial.
//
// The rules under test are trust rules wearing interaction clothes. A default selection would
// answer a question the publisher left open; Escape that chooses would answer it by accident;
// focus escaping the dialog lets a reader act on content that assumes a version was chosen.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { AmbiguousVersion } from '../.react-build/app.mjs';

const CANDIDATES = [
  { valid_from: '2025-01-01', hash: 'a'.repeat(64), publication_date: '2024-12-01' },
  { valid_from: '2025-06-01', hash: 'b'.repeat(64), publication_date: '2025-05-01' },
];

const noop = () => {};
const render = (props = {}) =>
  renderToStaticMarkup(
    h(AmbiguousVersion, {
      publisher: 'lu-legilux',
      candidates: CANDIDATES,
      requestedDate: '2025-07-01',
      onDismiss: noop,
      onRead: noop,
      onCompare: noop,
      ...props,
    }),
  );

test('the dialog is an alertdialog and names both candidates in full', () => {
  const html = render();
  assert.equal(html.includes('role="alertdialog"'), true);
  assert.equal(html.includes('aria-modal="true"'), true);
  // Both candidates, each with its date, its digest and its publication date. The spec says in
  // full because a reader choosing between two states needs what distinguishes them.
  for (const candidate of CANDIDATES) {
    assert.equal(html.includes(candidate.valid_from), true);
    assert.equal(html.includes(candidate.hash.slice(0, 8)), true);
    assert.equal(html.includes(candidate.publication_date), true);
  }
});

test('nothing is preselected', () => {
  // A default selection answers the question the publisher left open. The spec forbids it and
  // forbids remembering a choice, which would answer it again silently on every later visit.
  const html = render();
  // Attributes, not substrings. The first version of this test asserted the bare word
  // "selected" was absent and failed on the component's own sentence "nothing here is
  // preselected", which is exactly the substring weakness this suite keeps finding
  // elsewhere.
  assert.equal(/\schecked(=|\s|>)/.test(html), false, 'a candidate was checked');
  assert.equal(/\sselected(=|\s|>)/.test(html), false, 'a candidate was selected');
  assert.equal(/aria-selected="true"/.test(html), false, 'a candidate was marked selected');
  assert.equal(/\sautofocus(=|\s|>)/.test(html), false, 'focus was placed on one candidate');
  assert.equal(/remember/i.test(html), false, 'the dialog offered to remember a choice');
});

test('the dialog says the service will not choose', () => {
  // The reader is here because the record is ambiguous. Saying so is the point of the screen.
  const html = render();
  assert.equal(html.includes('has not ranked them'), true);
  assert.equal(html.includes('will not choose between them for you'), true);
  assert.equal(html.includes('nothing here is preselected'), true);
});

test('a candidate is described in its own publisher terms', () => {
  const lu = render({ publisher: 'lu-legilux' });
  assert.equal(lu.includes('applicable from 2025-01-01'), true);

  const eu = render({ publisher: 'eu-eurlex' });
  assert.equal(eu.includes('a consolidated wording state from 2025-01-01'), true);
  assert.equal(
    eu.includes('applicable from'),
    false,
    'an EU candidate was offered as applicable from a date',
  );
});

test('fewer than two candidates is not an ambiguity', () => {
  // A dialog asking a reader to resolve nothing teaches them the control is noise.
  for (const candidates of [[], [CANDIDATES[0]], undefined]) {
    assert.throws(() => render({ candidates }), /there is no ambiguity/);
  }
});

test('closing without choosing is offered explicitly, not only by Escape', () => {
  // Escape is the keyboard route. A visible control is the one a pointer user has, and both
  // must lead to the same place: out, with nothing chosen.
  assert.equal(render().includes('Close without choosing'), true);
});
