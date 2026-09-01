import assert from 'node:assert/strict';
import test from 'node:test';

import * as localization from '../scripts/localization.mjs';
import {
  CHROME_LOCALES,
  LOCALIZATION_UNAVAILABLE,
  MASTER_LOCALES,
  REFUSAL_TEMPLATE_LOCALES,
  isReviewed,
  quotedLaw,
  readStoredLocale,
  renderLocalizationUnavailable,
  resolveChromeLocale,
  reviewedLocales,
  reviewedText,
  writeStoredLocale,
} from '../scripts/localization.mjs';
import { REFUSAL_CODES } from '../scripts/refusal-card.mjs';

const ADVICE = 'refusal.advice_boundary.sentence';
const LU_TEXT =
  'Art. L. 121-6. Le salarié incapable de travailler pour cause de maladie ou d’accident...';

test('the chrome locales are the four, and PT joins only the refusal templates', () => {
  assert.deepEqual([...CHROME_LOCALES], ['fr', 'de', 'en', 'lb']);
  assert.deepEqual([...REFUSAL_TEMPLATE_LOCALES], ['fr', 'de', 'en', 'lb', 'pt']);
  assert.ok(!CHROME_LOCALES.includes('pt'), 'PT was promoted to chrome without a ruling');
  assert.deepEqual([...MASTER_LOCALES], ['en', 'fr']);
});

test('localization_unavailable is not slipped into the closed refusal registry', () => {
  // The pack's versioned registry has nineteen codes and this is not one of them. Adding it
  // is a Gateway contract change for #348, not something this module does quietly.
  assert.ok(!REFUSAL_CODES.includes(LOCALIZATION_UNAVAILABLE));
  assert.equal(REFUSAL_CODES.length, 19);
});

test('a reviewed string is served with its reviewer and date', () => {
  for (const locale of MASTER_LOCALES) {
    const result = reviewedText(ADVICE, locale);
    assert.equal(result.status, 'ok');
    assert.equal(result.locale, locale);
    assert.ok(result.text.length > 80);
    assert.ok(result.reviewed_by.length > 0);
    assert.match(result.reviewed_on, /^\d{4}-\d{2}-\d{2}$/);
  }
});

test('a missing locale is refused, and no other locale is substituted', () => {
  const english = reviewedText(ADVICE, 'en').text;
  const french = reviewedText(ADVICE, 'fr').text;

  for (const locale of ['de', 'lb', 'pt']) {
    const result = reviewedText(ADVICE, locale);
    assert.equal(result.status, LOCALIZATION_UNAVAILABLE, `${locale} was served something`);
    assert.equal(result.locale, locale);
    assert.deepEqual(result.reviewed_in, ['fr', 'en']);
    assert.ok(!('text' in result), `${locale} carried text it has no reviewed string for`);
    const serialised = JSON.stringify(result);
    assert.ok(!serialised.includes(english.slice(0, 40)), `${locale} was quietly given English`);
    assert.ok(!serialised.includes(french.slice(0, 40)), `${locale} was quietly given French`);
  }
});

test('an unreviewed entry is absent, whatever text it carries', () => {
  assert.ok(isReviewed({ text: 'x', reviewed_by: 'someone', reviewed_on: '2026-08-27' }));
  for (const entry of [
    { text: 'machine translated overnight' },
    { text: 'x', reviewed_by: 'someone' },
    { text: 'x', reviewed_on: '2026-08-27' },
    { text: 'x', reviewed_by: '  ', reviewed_on: '2026-08-27' },
    { text: '   ', reviewed_by: 'someone', reviewed_on: '2026-08-27' },
    { text: 'x', reviewed_by: 'someone', reviewed_on: 'last tuesday' },
  ]) {
    assert.ok(!isReviewed(entry), `${JSON.stringify(entry)} was accepted as reviewed`);
  }

  const bundle = { 'k.unreviewed': { lb: { text: 'Eng Iwwersetzung ouni Iwwerpréifung' } } };
  const result = reviewedText('k.unreviewed', 'lb', bundle);
  assert.equal(result.status, LOCALIZATION_UNAVAILABLE);
  assert.deepEqual(result.reviewed_in, []);
  assert.deepEqual(reviewedLocales('k.unreviewed', bundle), []);
});

test('a stored choice wins, then Accept-Language, then French', () => {
  assert.equal(resolveChromeLocale({ stored: 'lb', acceptLanguage: 'en-GB' }), 'lb');
  assert.equal(resolveChromeLocale({ acceptLanguage: 'de-DE,de;q=0.9,en;q=0.8' }), 'de');
  // Quality ordering decides, not the order they were written in.
  assert.equal(resolveChromeLocale({ acceptLanguage: 'en;q=0.2,lb-LU;q=0.9' }), 'lb');
  // An unsupported language is skipped rather than mapped onto a neighbour.
  assert.equal(resolveChromeLocale({ acceptLanguage: 'nl-BE,nl;q=0.9' }), 'fr');
  assert.equal(resolveChromeLocale({ acceptLanguage: 'pt-PT' }), 'fr');
  assert.equal(resolveChromeLocale({}), 'fr');
  assert.equal(resolveChromeLocale(), 'fr');
  // q=0 means not acceptable.
  assert.equal(resolveChromeLocale({ acceptLanguage: 'de;q=0,en;q=0.5' }), 'en');
  // A stored value outside the four is not honoured.
  assert.equal(resolveChromeLocale({ stored: 'pt', acceptLanguage: 'en' }), 'en');
});

test('storage that throws does not take the page down', () => {
  const hostile = {
    getItem() {
      throw new Error('site data blocked');
    },
    setItem() {
      throw new Error('site data blocked');
    },
  };
  assert.equal(readStoredLocale(hostile), null);
  assert.equal(writeStoredLocale(hostile, 'de'), false);
  assert.equal(readStoredLocale(undefined), null);

  const store = new Map();
  const working = {
    getItem: (key) => store.get(key) ?? null,
    setItem: (key, value) => store.set(key, value),
  };
  assert.equal(writeStoredLocale(working, 'lb'), true);
  assert.equal(readStoredLocale(working), 'lb');

  store.set('lex.chrome-locale', 'klingon');
  assert.equal(readStoredLocale(working), null, 'a junk stored value was trusted');
  assert.throws(() => writeStoredLocale(working, 'pt'), /not one of the four chrome locales/);
});

test('LU statute always carries the authenticity note and EU never does', () => {
  const lu = quotedLaw({ publisher: 'lu-legilux', language: 'fr', text: LU_TEXT });
  assert.ok(lu.includes('lang="fr"'));
  assert.ok(lu.includes('Only the French text is authentic'));

  const eu = quotedLaw({
    publisher: 'eu-eurlex',
    language: 'en',
    text: 'Article 5. Personal data shall be processed lawfully, fairly and transparently.',
  });
  assert.ok(eu.includes('lang="en"'));
  // Each EU language expression is equally authentic; the note would be false there.
  assert.ok(!eu.includes('authentic'), 'an EU expression was labelled as non-authentic');
});

test('LU statute cannot be quoted as law in a language it is not authentic in', () => {
  assert.throws(
    () => quotedLaw({ publisher: 'lu-legilux', language: 'en', text: 'An English rendering' }),
    /authentic in fr alone/,
  );
});

test('a quoted span without its own language is refused', () => {
  for (const bad of [undefined, '', 'french', 'FR', 'fr-LU']) {
    assert.throws(
      () => quotedLaw({ publisher: 'eu-eurlex', language: bad, text: 'x y z' }),
      /carries its own language attribute/,
      `${JSON.stringify(bad)} was accepted as a language tag`,
    );
  }
});

test('the chrome switcher has no channel to quoted law', () => {
  // Not a promise: there is no module-level locale and no setter to reach it through, so a
  // chrome language change cannot alter a statutory quotation.
  const setters = Object.keys(localization).filter((name) => /^(set|use)/.test(name));
  assert.deepEqual(setters, [], `a global locale setter appeared: ${setters.join(', ')}`);

  const quoted = CHROME_LOCALES.map(() =>
    quotedLaw({ publisher: 'lu-legilux', language: 'fr', text: LU_TEXT }),
  );
  assert.equal(new Set(quoted).size, 1, 'the quotation varied with something it must not see');
});

test('the authenticity note follows the chrome locale, and refuses rather than substituting', () => {
  const french = quotedLaw({
    publisher: 'lu-legilux',
    language: 'fr',
    text: LU_TEXT,
    noteLocale: 'fr',
  });
  assert.ok(french.includes('Seul le texte français fait foi'));

  const luxembourgish = quotedLaw({
    publisher: 'lu-legilux',
    language: 'fr',
    text: LU_TEXT,
    noteLocale: 'lb',
  });
  // The law is still quoted in French; only the note is missing, and it says so.
  assert.ok(luxembourgish.includes('lang="fr"'));
  assert.ok(luxembourgish.includes(LOCALIZATION_UNAVAILABLE));
  assert.ok(!luxembourgish.includes('Only the French text is authentic'));
  assert.ok(!luxembourgish.includes('Seul le texte'));
});

test('the unavailable state names the locale, the key and where the string does exist', () => {
  const html = renderLocalizationUnavailable(reviewedText(ADVICE, 'pt'));
  assert.ok(html.includes(LOCALIZATION_UNAVAILABLE));
  assert.ok(html.includes('data-locale="pt"'));
  assert.ok(html.includes('nothing was substituted'));
  assert.ok(html.includes('Reviewed in: fr, en'));
  assert.ok(html.includes(ADVICE));
});

test('the unavailable state is honest when nothing is reviewed anywhere', () => {
  const html = renderLocalizationUnavailable(reviewedText('k.absent', 'lb'));
  assert.ok(html.includes('not reviewed in any language yet'));
});

test('the page carries the chrome language, and only a reviewed one', async () => {
  const { page } = await import('../scripts/render.mjs');
  for (const locale of CHROME_LOCALES) {
    const html = page({ state: 'trust-surface', title: 'T', main: '<h1>T</h1>', locale });
    assert.ok(html.includes(`<html lang="${locale}"`), `${locale} did not reach html lang`);
  }
  assert.ok(page({ state: 's', title: 'T', main: '<h1>T</h1>' }).includes('<html lang="en"'));
  for (const bad of ['pt', 'es', 'EN', '']) {
    assert.throws(
      () => page({ state: 's', title: 'T', main: '<h1>T</h1>', locale: bad }),
      /not one of the four chrome locales/,
      `${JSON.stringify(bad)} was accepted as a chrome locale`,
    );
  }
});

test('values are escaped rather than trusted', () => {
  const html = renderLocalizationUnavailable({
    locale: '"><img src=x onerror=alert(1)>',
    key: '<script>',
    reviewed_in: [],
  });
  assert.ok(!html.includes('<img'));
  assert.ok(!html.includes('<script>'));

  const quoted = quotedLaw({
    publisher: 'eu-eurlex',
    language: 'en',
    text: '<img src=x onerror=alert(1)>',
  });
  assert.ok(!quoted.includes('<img'));
  assert.ok(quoted.includes('&lt;img'));
});
