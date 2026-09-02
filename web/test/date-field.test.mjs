// The as-of date control.
//
// Two rules here are not interaction preferences, and they are what this file is mostly about.
//
// "No silent default anywhere": today is the date a reader will not think to check, precisely
// because it is the one they would have assumed. A field that quietly resolves to now answers a
// question about now to someone who may have been asking about a contract signed in 2019, and
// nothing on the page says which question was answered.
//
// "On submit the field announces the resolved state via aria-live and visible text": a reader
// using assistive technology hears the resolved interval before the content. Hearing the text
// without the interval leaves no way to know which state is being read.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { DateField, parseAsOf, resolutionSentence } from '../.react-build/app.mjs';

const LU = {
  lex_id: 'lu-legilux:code-travail:2021-01-26',
  valid_from: '2021-01-26',
  valid_to: '2021-04-23',
  publication_date: '2021-01-26',
};

const EU = {
  lex_id: 'eu-eurlex:32016R0679:2021-01-26',
  valid_from: '2021-01-26',
  valid_to: '2021-04-23',
  publication_date: '2021-01-26',
};

const render = (props) =>
  renderToStaticMarkup(h(DateField, { today: '2026-09-01', onSubmit: () => {}, ...props }));

test('a date is read the way a reader wrote it, or refused rather than guessed', () => {
  assert.equal(parseAsOf('2021-01-26'), '2021-01-26');
  assert.equal(parseAsOf('26/01/2021'), '2021-01-26');
  assert.equal(parseAsOf('1/8/2024'), '2024-08-01', 'an unpadded day was not read');
  assert.equal(parseAsOf('  2021-01-26  '), '2021-01-26');

  // Refused, not coerced. A coerced date answers a question nobody asked, and the reader is
  // never told which question that was.
  for (const bad of ['', 'yesterday', '2021', '01/2021', 'jan 26 2021', undefined, null, {}]) {
    assert.equal(parseAsOf(bad), null, `${JSON.stringify(bad)} was read as a date`);
  }

  // Dates that parse and are not days. 2021-02-30 is the shape a fat finger makes, and reading
  // it as 2 March would place a reader in a state they did not ask for.
  for (const impossible of ['2021-02-30', '2021-13-01', '2021-04-31', '31/02/2021']) {
    assert.equal(parseAsOf(impossible), null, `${impossible} was accepted as a day`);
  }
});

test('the resolution sentence is the publisher own words, derived from the record', () => {
  // The UX spec worked example, exactly.
  assert.equal(
    resolutionSentence(LU),
    'Resolved to the state applicable from 2021-01-26 to 2021-04-23, published 2021-01-26.',
  );

  // The Union dates a wording state and makes no applicability claim, so it does not get the
  // Luxembourg sentence. Nobody passes this: it comes from the record's own identifier.
  assert.equal(
    resolutionSentence(EU),
    'Resolved to the consolidated wording state from 2021-01-26 to 2021-04-23, ' +
      'published 2021-01-26.',
  );
  assert.ok(!resolutionSentence(EU).includes('applicable'), 'the LU vocabulary reached the Union');

  // An open interval says so rather than being closed with a sentinel or with today.
  assert.ok(resolutionSentence({ ...LU, valid_to: null }).includes('no end recorded'));

  // A publisher nobody classified fails closed, including keys that exist on every object.
  for (const publisher of ['nobody', 'constructor', 'toString', '__proto__']) {
    assert.throws(
      () => resolutionSentence({ ...LU, lex_id: `${publisher}:work:2021-01-26` }),
      /is not a publisher this interface has classified|does not name a publisher/,
      `${publisher} was given a vocabulary`,
    );
  }
});

test('the default is shown before it is used, and can be removed', () => {
  const html = render({});
  assert.ok(html.includes('2026-09-01'), 'the default date was not shown');
  assert.ok(
    html.includes('No date entered'),
    'the field resolved to today without saying it was going to',
  );
  assert.ok(html.includes('Remove this default'), 'the default could not be removed');
});

test('the field is told what today is rather than reading a clock', () => {
  // A control that consults its own clock answers a question the reader did not ask and cannot
  // reproduce: the same URL gives a different answer tomorrow with no record having changed.
  for (const bad of [undefined, null, '', 'today', '2026-9-1', 1756684800000]) {
    assert.throws(
      () => render({ today: bad }),
      /told what today is rather than reading a clock/,
      `${JSON.stringify(bad)} was accepted as today`,
    );
  }
});

test('the resolution is announced, and says nothing before there is one', () => {
  const announced = render({ resolved: LU });
  assert.ok(announced.includes('aria-live="polite"'), 'the resolution was not announced');
  assert.ok(
    announced.includes('Resolved to the state applicable from 2021-01-26 to 2021-04-23'),
    'the announcement did not name the interval',
  );

  // The live region exists before there is anything to say, because a region added at the moment
  // it gains content is not reliably announced. It just says nothing.
  const quiet = render({});
  assert.ok(quiet.includes('aria-live="polite"'), 'the live region was created on demand');
  assert.ok(!quiet.includes('Resolved to'), 'a resolution was announced before one existed');
});
