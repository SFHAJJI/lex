// The React document shell.
//
// Written against the compiled output of app/index.jsx, which is what the build ships, so a
// test and a served page measure the same bytes rather than two compilations that agree today.
//
// These assertions deliberately avoid the shape that has failed this project repeatedly: a file
// of `html.includes(...)` checks survives an element being hidden, a contradicting sentence
// appearing beside it, an attribute being hardcoded, or a constant being redefined to say
// something false. So exported copy is pinned to a literal, rendered values are bound to the
// inputs that produced them, and every guard is proved to fire on its own input rather than
// assumed reachable.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';

import { Document, SYNTHETIC_MARKER, renderDocument } from '../.react-build/app.mjs';

function render(props = {}) {
  return renderDocument(
    h(Document, { state: 'proof', title: 'Title', ...props }, h('h1', null, 'Heading')),
  );
}

test('the synthetic marker is the literal the DOM is searched for', () => {
  // Pinned to a literal. Asserted against itself, this constant could be redefined to anything
  // and every downstream assertion would still pass, which is how a self-referential fixture
  // let a corrupted absence string through on this project before.
  assert.equal(SYNTHETIC_MARKER, 'lex-v3-synthetic-preview');
});

test('a rendered page is a whole document, doctype first', () => {
  const html = render();
  assert.equal(html.startsWith('<!doctype html>\n<html '), true);
  assert.equal(html.endsWith('</html>\n'), true);
});

test('the shell carries the banner, the preview state and the body', () => {
  const html = render({ state: 'dossier' });
  assert.equal(html.includes(`data-synthetic="${SYNTHETIC_MARKER}"`), true);
  assert.equal(html.includes('data-preview-state="dossier"'), true);
  assert.equal(html.includes('<main id="main">'), true);
  assert.equal(html.includes('<h1>Heading</h1>'), true);
});

test('the state and title rendered are the ones passed, not defaults', () => {
  // Binds output to input. A shell that hardcoded either would satisfy an includes() check
  // against a fixture that happened to use the same word.
  const html = render({ state: 'timeline', title: 'Chronologie' });
  assert.equal(html.includes('data-preview-state="timeline"'), true);
  assert.equal(html.includes('<title>Chronologie - Lex V3 preview</title>'), true);
  assert.equal(html.includes('data-preview-state="proof"'), false);
});

test('every asset reference is root-absolute', () => {
  // The defect this exists for: resolved against the page's own path, the same page served at
  // /w/<work>/<version> loads a stylesheet that is not there and renders unstyled.
  const hrefs = [...render().matchAll(/<link[^>]+href="([^"]+)"/g)].map((match) => match[1]);
  assert.equal(hrefs.length > 0, true, 'the shell stopped emitting asset links entirely');
  for (const href of hrefs) {
    assert.equal(href.startsWith('/'), true, `${href} depends on how deep the page is served`);
  }
});

test('the language tag is the page locale, and the copy locale must agree', () => {
  assert.equal(render({ locale: 'fr', copyLocale: 'fr' }).includes('<html lang="fr"'), true);
  // Being one of the reviewed locales is not the same as being the language the copy is written
  // in, and only the second is what the tag asserts. This exact defect served English prose
  // under lang="de" on every page in an earlier build.
  assert.throws(
    () => render({ locale: 'de', copyLocale: 'en' }),
    /labelled de while its copy is written in en/,
  );
});

test('an unreviewed locale is refused on either axis, and says which', () => {
  // Asserting the axis, not just the sentence. Both guards used to raise identical text, so
  // feeding an unreviewed value to both could not distinguish them: deleting the page-locale
  // check left the copy-locale check producing the same message and this test still passed.
  // A mutation sweep over this file found it; nothing else would have.
  assert.throws(() => render({ locale: 'zz', copyLocale: 'zz' }), /the page locale "zz"/);
  assert.throws(() => render({ locale: 'en', copyLocale: 'zz' }), /the copy locale "zz"/);
});

test('a page with no state or no title is refused', () => {
  assert.throws(() => render({ state: '' }), /data-preview-state is not optional/);
  assert.throws(() => render({ title: '' }), /a page carries a title/);
});

test('shell attributes appear only when a shell is named', () => {
  const skinned = render({ shell: 'w', density: 'reading' });
  assert.equal(skinned.includes('data-shell="w"'), true);
  assert.equal(skinned.includes('data-density="reading"'), true);
  assert.equal(render().includes('data-shell'), false);
});

test('a fragment is refused, because the shell carries the trust marks', () => {
  // A page that renders around the shell loses the synthetic banner, the preview state and the
  // asset links at once, and does so silently.
  assert.throws(() => renderDocument(h('p', null, 'loose')), /not a whole document/);
});

test('the title is escaped by the shell, so callers pass plain text', () => {
  // Escaping twice is a recorded failure here: a caller that pre-escaped produced
  // Gro&#223;herzogtums in the browser tab and in search results.
  const html = render({ title: 'Recht & Ordnung <hr>' });
  assert.equal(html.includes('<title>Recht &amp; Ordnung &lt;hr&gt; - Lex V3 preview</title>'), true);
  assert.equal(html.includes('<hr>'), false);
});
