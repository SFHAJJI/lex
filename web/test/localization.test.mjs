import assert from 'node:assert/strict';
import test from 'node:test';

import * as localization from '../scripts/localization.mjs';
import {
  CHROME_LOCALES,
  LOCALIZATION_UNAVAILABLE,
  MASTER_LOCALES,
  REFUSAL_TEMPLATE_LOCALES,
  RESOURCE_AUTHENTICITY_SCHEMA,
  isReviewed,
  isSourceMaster,
  provenanceOf,
  quotedLaw,
  readStoredLocale,
  renderLocalizationUnavailable,
  requireResourceAuthenticity,
  resolveChromeLocale,
  servableLocales,
  servableText,
  writeStoredLocale,
} from '../scripts/localization.mjs';
import { REFUSAL_CODES } from '../scripts/refusal-card.mjs';

const ADVICE = 'refusal.advice_boundary.sentence';
const LU_TEXT = 'APERCU SYNTHETIQUE. Article 1er. Ce texte est synthetique.';

/** Sole authentic language, the Luxembourg statute case, as evidence about one resource. */
const SOLE = Object.freeze({
  schema: RESOURCE_AUTHENTICITY_SCHEMA,
  resource_id: 'preview-synthetic:synthetic-preview-work:2001-01-01',
  authentic_languages: ['fr'],
  basis: 'loi du 24 fevrier 1984, art. 2',
  asserted_by: 'synthetic preview publisher',
  observed_at: '2026-01-01T00:00:00Z',
});

/** Equally authentic per language, the EU expression case. */
const EQUAL = Object.freeze({
  ...SOLE,
  resource_id: 'preview-synthetic:synthetic-regulation:2001-01-01',
  authentic_languages: ['en', 'fr', 'de'],
  basis: 'every language expression is equally authentic',
});

/** A held expression whose sole authentic language is not French. */
const GERMAN = Object.freeze({
  ...SOLE,
  resource_id: 'preview-synthetic:synthetic-german-act:2001-01-01',
  authentic_languages: ['de'],
});

test('the chrome locales are the four, and PT joins only the refusal templates', () => {
  assert.deepEqual([...CHROME_LOCALES], ['fr', 'de', 'en', 'lb']);
  assert.deepEqual([...REFUSAL_TEMPLATE_LOCALES], ['fr', 'de', 'en', 'lb', 'pt']);
  assert.ok(!CHROME_LOCALES.includes('pt'), 'PT was promoted to chrome without a ruling');
  assert.deepEqual([...MASTER_LOCALES], ['en', 'fr']);
});

test('localization_unavailable is not slipped into the closed refusal registry', () => {
  assert.ok(!REFUSAL_CODES.includes(LOCALIZATION_UNAVAILABLE));
  assert.equal(REFUSAL_CODES.length, 19);
});

test('a source master is served as a source master, never as a human review', () => {
  // The first version stored specification citations in a field called `reviewed_by` while
  // claiming a named human had reviewed them. That was a claim to evidence this module did
  // not possess, which is the failure it exists to prevent, aimed at itself.
  for (const locale of MASTER_LOCALES) {
    const result = servableText(ADVICE, locale);
    assert.equal(result.status, 'ok');
    assert.equal(result.provenance.kind, 'source_master');
    assert.match(result.provenance.source_basis, /product-spec/);
    assert.ok(!('reviewed_by' in result.provenance), 'a specification citation claimed a reviewer');
  }
});

test('a human review needs a reviewer and a date that is a date', () => {
  assert.ok(isReviewed({ text: 'x', reviewed_by: 'A Reviewer', reviewed_on: '2026-08-27' }));
  for (const entry of [
    { text: 'x', reviewed_by: 'A Reviewer', reviewed_on: '2026-99-99' },
    { text: 'x', reviewed_by: 'A Reviewer', reviewed_on: '2025-02-29' },
    { text: 'x', reviewed_by: 'A Reviewer', reviewed_on: 'last tuesday' },
    { text: 'x', reviewed_by: '  ', reviewed_on: '2026-08-27' },
    { text: 'x', reviewed_on: '2026-08-27' },
  ]) {
    assert.ok(!isReviewed(entry), `${JSON.stringify(entry)} was accepted as reviewed`);
  }
  assert.ok(isSourceMaster({ text: 'x', source_basis: '33-product-spec' }));
  assert.ok(!isSourceMaster({ text: 'x', source_basis: '   ' }));
});

test('an entry claiming both provenances is refused rather than resolved', () => {
  assert.equal(
    provenanceOf({
      text: 'x',
      source_basis: '33-product-spec',
      reviewed_by: 'A Reviewer',
      reviewed_on: '2026-08-27',
    }),
    null,
  );
  assert.equal(provenanceOf({ text: 'x' }), null);
  assert.equal(provenanceOf({ source_basis: '33-product-spec' }), null);
});

test('a missing locale is refused, and no other locale is substituted', () => {
  const english = servableText(ADVICE, 'en').text;
  const french = servableText(ADVICE, 'fr').text;

  for (const locale of ['de', 'lb', 'pt']) {
    const result = servableText(ADVICE, locale);
    assert.equal(result.status, LOCALIZATION_UNAVAILABLE, `${locale} was served something`);
    assert.equal(result.locale, locale);
    assert.deepEqual(result.servable_in, ['fr', 'en']);
    assert.ok(!('text' in result), `${locale} carried text it has no string for`);
    const serialised = JSON.stringify(result);
    assert.ok(!serialised.includes(english.slice(0, 40)), `${locale} was quietly given English`);
    assert.ok(!serialised.includes(french.slice(0, 40)), `${locale} was quietly given French`);
  }
});

test('an unservable entry is absent, whatever text it carries', () => {
  const bundle = { 'k.unreviewed': { lb: { text: 'Eng Iwwersetzung ouni Iwwerpreifung' } } };
  const result = servableText('k.unreviewed', 'lb', {}, bundle);
  assert.equal(result.status, LOCALIZATION_UNAVAILABLE);
  assert.deepEqual(result.servable_in, []);
  assert.deepEqual(servableLocales('k.unreviewed', bundle), []);
});

test('the string bundle is closed against the prototype', () => {
  for (const key of ['toString', 'constructor', '__proto__']) {
    assert.equal(servableText(key, 'en').status, LOCALIZATION_UNAVAILABLE, `${key} resolved`);
  }
});

test('a stored choice wins, then Accept-Language, then French', () => {
  assert.equal(resolveChromeLocale({ stored: 'lb', acceptLanguage: 'en-GB' }), 'lb');
  assert.equal(resolveChromeLocale({ acceptLanguage: 'de-DE,de;q=0.9,en;q=0.8' }), 'de');
  assert.equal(resolveChromeLocale({ acceptLanguage: 'en;q=0.2,lb-LU;q=0.9' }), 'lb');
  assert.equal(resolveChromeLocale({ acceptLanguage: 'nl-BE,nl;q=0.9' }), 'fr');
  assert.equal(resolveChromeLocale({ acceptLanguage: 'pt-PT' }), 'fr');
  assert.equal(resolveChromeLocale({}), 'fr');
  assert.equal(resolveChromeLocale(), 'fr');
  assert.equal(resolveChromeLocale({ acceptLanguage: 'de;q=0,en;q=0.5' }), 'en');
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

test('quoting law without authenticity evidence fails closed', () => {
  // The first version inferred authenticity from a publisher key, which Decision 58 forbids
  // and which also declared held German LU statute impossible. There is no publisher table
  // in the module any more, so there is nothing left to infer from.
  assert.throws(
    () => quotedLaw({ language: 'fr', text: LU_TEXT }),
    /requires typed authenticity evidence/,
  );
  const source = JSON.stringify(localization);
  assert.ok(!source.includes('lu-legilux'), 'a publisher key survived in the module');
});

test('authenticity evidence is checked, not taken', () => {
  for (const [broken, pattern] of [
    [{ ...SOLE, schema: 'something/1' }, /must declare/],
    [{ ...SOLE, resource_id: '' }, /must name the exact resource/],
    [{ ...SOLE, authentic_languages: [] }, /at least one authentic language/],
    [{ ...SOLE, authentic_languages: ['french'] }, /must be language tags/],
    [{ ...SOLE, authentic_languages: ['fr', 'fr'] }, /repeats a language/],
    [{ ...SOLE, basis: '' }, /the ground for its claim/],
    [{ ...SOLE, asserted_by: '' }, /must name who asserts it/],
    [{ ...SOLE, observed_at: 'yesterday' }, /when it was observed/],
    [{ ...SOLE, observed_at: '2026-99-99T00:00:00Z' }, /when it was observed/],
  ]) {
    assert.throws(() => requireResourceAuthenticity(broken), pattern);
  }
  assert.equal(requireResourceAuthenticity(SOLE), SOLE);
});

test('the note follows the resource evidence, not a publisher key', () => {
  const sole = quotedLaw({ authenticity: SOLE, language: 'fr', text: LU_TEXT });
  assert.ok(sole.includes('lang="fr"'));
  assert.ok(sole.includes('Only the fr text is authentic (loi du 24 fevrier 1984, art. 2)'));

  const equal = quotedLaw({ authenticity: EQUAL, language: 'en', text: 'Article 5.' });
  assert.ok(equal.includes('lang="en"'));
  assert.ok(!equal.includes('authentic'), 'an equally authentic expression was qualified');

  // The case the publisher-key version declared impossible.
  const german = quotedLaw({ authenticity: GERMAN, language: 'de', text: 'Artikel 1.' });
  assert.ok(german.includes('lang="de"'));
  assert.ok(german.includes('Only the de text is authentic'));
});

test('a language outside the resource authentic set is not quoted as law', () => {
  assert.throws(
    () => quotedLaw({ authenticity: SOLE, language: 'en', text: 'An English rendering' }),
    /is unofficial and must be labelled as one/,
  );
});

test('a quoted span without its own language is refused', () => {
  for (const bad of [undefined, '', 'french', 'FR', 'fr-LU']) {
    assert.throws(
      () => quotedLaw({ authenticity: EQUAL, language: bad, text: 'x y z' }),
      /carries its own language attribute/,
      `${JSON.stringify(bad)} was accepted as a language tag`,
    );
  }
});

test('the chrome switcher has no channel to quoted law', () => {
  const setters = Object.keys(localization).filter((name) => /^(set|use)/.test(name));
  assert.deepEqual(setters, [], `a global locale setter appeared: ${setters.join(', ')}`);

  const quoted = CHROME_LOCALES.map(() =>
    quotedLaw({ authenticity: SOLE, language: 'fr', text: LU_TEXT }),
  );
  assert.equal(new Set(quoted).size, 1, 'the quotation varied with something it must not see');
});

test('the note refuses rather than substituting when its locale is missing', () => {
  const luxembourgish = quotedLaw({
    authenticity: SOLE,
    language: 'fr',
    text: LU_TEXT,
    noteLocale: 'lb',
  });
  assert.ok(luxembourgish.includes('lang="fr"'));
  assert.ok(luxembourgish.includes(LOCALIZATION_UNAVAILABLE));
  assert.ok(!luxembourgish.includes('Only the fr text is authentic'));
  assert.ok(!luxembourgish.includes('Seul le texte'));

  const french = quotedLaw({
    authenticity: SOLE,
    language: 'fr',
    text: LU_TEXT,
    noteLocale: 'fr',
  });
  assert.ok(french.includes('Seul le texte en fr fait foi'));
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
    servable_in: [],
  });
  assert.ok(!html.includes('<img'));
  assert.ok(!html.includes('<script>'));

  const quoted = quotedLaw({
    authenticity: EQUAL,
    language: 'en',
    text: '<img src=x onerror=alert(1)>',
  });
  assert.ok(!quoted.includes('<img'));
  assert.ok(quoted.includes('&lt;img'));

  // The note is filled from evidence, so the evidence has to be escaped too.
  const injected = quotedLaw({
    authenticity: { ...SOLE, basis: '<img src=x onerror=alert(1)>' },
    language: 'fr',
    text: LU_TEXT,
  });
  assert.ok(!injected.includes('<img'));
  assert.ok(injected.includes('&lt;img'));
});
