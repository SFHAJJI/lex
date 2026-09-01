import assert from 'node:assert/strict';
import test from 'node:test';

import { CHROME_LOCALES } from '../scripts/localization.mjs';
import {
  REVIEWED_CHROME_LOCALES,
  renderLocaleUnavailable,
} from '../scripts/locale-unavailable.mjs';

test('the unavailable page is itself in a language it is honestly labelled with', () => {
  // The one thing this page must not do is the thing it exists to report.
  for (const requested of CHROME_LOCALES.filter((one) => !REVIEWED_CHROME_LOCALES.includes(one))) {
    const html = renderLocaleUnavailable({ requested });
    assert.ok(html.includes('<html lang="en"'), `${requested} page mislabelled its own language`);
    assert.ok(!html.includes(`<html lang="${requested}"`));
  }
});

test('it names the code, the locale asked for, and what is actually reviewed', () => {
  const html = renderLocaleUnavailable({ requested: 'de' });
  assert.ok(html.includes('localization_unavailable'), 'the machine code is not shown');
  assert.ok(html.includes('German'), 'the reader is not told which language was refused');
  assert.ok(html.includes('English (en)'), 'no reviewed language is offered');

  // And it says why, because "not available" without a reason reads as an omission rather
  // than a decision.
  assert.ok(html.includes('evidence-based legal-language review'));
  assert.ok(html.includes('Machine translation is not sufficient'));
});

test('it separates the interface language from the language of the law', () => {
  // A reader told "no German" must not conclude that German law texts are withheld.
  const html = renderLocaleUnavailable({ requested: 'de' });
  assert.ok(html.includes('It does not affect the law'));
  assert.ok(html.includes('the language the publisher published it in'));
});

test('a reviewed locale is not refused, and a locale outside the four is not offered', () => {
  for (const reviewed of REVIEWED_CHROME_LOCALES) {
    assert.throws(
      () => renderLocaleUnavailable({ requested: reviewed }),
      /has reviewed copy, so this page would be refusing something that exists/,
      `${reviewed} was refused although it is reviewed`,
    );
  }
  for (const bad of ['pt', 'es', '', undefined]) {
    assert.throws(
      () => renderLocaleUnavailable({ requested: bad }),
      /is not one of the four chrome locales/,
      `${JSON.stringify(bad)} was given an unavailability page`,
    );
  }
});

test('the reviewed set is honest about how little has been reviewed', () => {
  // If this ever grows, it grows because a legal-language review happened, not because a
  // string got translated. Asserting the exact set makes that a deliberate edit.
  assert.deepEqual([...REVIEWED_CHROME_LOCALES], ['en']);
  for (const one of REVIEWED_CHROME_LOCALES) {
    assert.ok(CHROME_LOCALES.includes(one), `${one} is reviewed but is not a chrome locale`);
  }
});

test('values are escaped rather than trusted', () => {
  // The requested locale is closed to the four, so nothing hostile reaches the page; this
  // asserts the closure rather than the escaping, because the closure is what protects it.
  assert.throws(
    () => renderLocaleUnavailable({ requested: '"><img src=x onerror=alert(1)>' }),
    /is not one of the four chrome locales/,
  );
});
