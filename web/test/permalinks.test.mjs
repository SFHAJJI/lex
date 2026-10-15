// The site's own addresses (`permalinks.mjs`): which page each is served or where it is sent, what a law's address
// names, the language a reader starts in, and the links the pages build. The tables are shared with the API's tests
// (V3WebRootTests), so the live server and the API answer one set of addresses.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { Permalink, TableRegion } from '../.react-build/app.mjs';
import {
  INTERFACE_LANGUAGES, PERMALINK_PATTERNS, anchorOf, anchorText, articlePermalink, escapeSegment, europePermalink, isSiteAddress, lawPageLinks,
  luxembourgPermalink, movedFor, negotiateLanguage, pageForSitePath, parseSitePath, permalinkHref, redirectFor, sameDocumentIn, sitePageFor,
} from '../scripts/permalinks.mjs';

const TABLE = JSON.parse(await readFile(new URL('../../tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json', import.meta.url), 'utf8'));
const DIGEST = '0123456789abcdef'.repeat(4);

test('each path of the shared table is served the page the table names, or sent or moved where it says, and no other is claimed', () => {
  assert.ok(TABLE.paths.length > 70, 'the table the API is held to as well');
  for (const { path, page, redirect, moved } of TABLE.paths) {
    assert.equal(pageForSitePath(path), page, path);
    assert.equal(redirectFor(path, '', undefined), redirect, path);
    assert.equal(movedFor(path, ''), moved, path);
    assert.ok([page, redirect, moved].filter((value) => value !== null).length <= 1, `${path}: served, sent or moved, never two`);
    // A move lands on R64's shape, which is served (under a language) or sent (with none), and never moved again.
    if (moved !== null) assert.ok(pageForSitePath(moved) !== null || redirectFor(moved, '', undefined) !== null, `${path} moves to ${moved}, an address this site answers`);
  }
  assert.ok(TABLE.paths.filter(({ moved }) => moved !== null).length >= 3, 'the shape before R64, with and without an interface language');
  const served = TABLE.paths.filter(({ page }) => page !== null).map(({ page }) => page);
  assert.deepEqual([...new Set(served)].sort(), ['en/dossier.html', 'en/reading.html', 'fr/dossier.html', 'fr/reading.html'], 'both pages, in both interfaces');
  for (const kind of ['luxembourgWork', 'luxembourgState', 'europeWork', 'europeWording']) {
    assert.ok(TABLE.paths.some(({ path, page }) => page !== null && PERMALINK_PATTERNS[kind].test(path.replace(/^\/(?:en|fr)\//, '/'))), `the table serves a ${kind} address`);
    assert.ok(TABLE.paths.some(({ path, redirect }) => redirect !== null && PERMALINK_PATTERNS[kind].test(path)), `the table sends a ${kind} address`);
  }
  assert.equal(redirectFor('/lu-legilux/loi-2017-03-14-a439', '?a=1', 'en'), '/en/lu-legilux/loi-2017-03-14-a439?a=1', 'the query goes along');
  assert.equal(redirectFor('/', '?from=card', 'en'), '/en/?from=card', 'from the root too (review of #958)');
  assert.equal(redirectFor('/index.html', '?from=card', undefined), '/fr/?from=card');
  assert.equal(
    movedFor(`/fr/eu-eurlex/32016R0679/eng/2016-05-04--${DIGEST}`, '?a=1'),
    `/fr/eu-eurlex/32016R0679/2016-05-04--${DIGEST}/eng?a=1`,
    'a move keeps the interface language and the query',
  );
  for (const value of [undefined, null, 42, `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}`, `/eu-eurlex/32016R0679/2016-05-04--${DIGEST}/eng`]) {
    assert.equal(movedFor(value, ''), null, `${String(value)}: R64's shape, or a Luxembourg permalink naming no language, is not moved`);
  }
});

test('what the server renders a law page from is what the shared table says, the text language included', () => {
  for (const { path, render } of TABLE.paths) assert.deepEqual(sitePageFor(path) === null ? null : JSON.parse(JSON.stringify(sitePageFor(path))), render, path);
});

test('a reader starts in the interface language the browser prefers of French and English, else French (R61)', () => {
  assert.deepEqual(INTERFACE_LANGUAGES, ['fr', 'en'], 'French first: a reader with no preference gets it');
  assert.ok(TABLE.languages.length >= 10);
  for (const { accept, language } of TABLE.languages) assert.equal(negotiateLanguage(accept), language, JSON.stringify(accept));
  assert.equal(negotiateLanguage(undefined), 'fr');
  assert.equal(negotiateLanguage(null), 'fr');
});


test('a Luxembourg state permalink names the work, the date, the digest and the text language it pins, and the interface it stands under', () => {
  const path = `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}/fra`;
  assert.deepEqual({ ...parseSitePath(path) }, {
    kind: 'state', publisher: 'lu-legilux', identifier: '/lu-legilux/loi-2017-03-14-a439', date: '2024-01-01', digest: DIGEST, language: 'fra', anchor: null,
    locale: null, permalink: path, address: path,
  });
  assert.ok(Object.isFrozen(parseSitePath(path)));
  for (const locale of ['fr', 'en']) {
    assert.deepEqual({ ...parseSitePath(`/${locale}${path}`) }, { ...parseSitePath(path), locale }, `under /${locale}/, the same permalink, without its prefix`);
  }
  assert.equal(path, luxembourgPermalink({ workKey: 'loi-2017-03-14-a439', date: '2024-01-01', stateSha256: DIGEST, language: 'fra' }), 'as the API mints it');
});

test('an article is the last segment of its permalink, escaped as the platform escapes it, and "#" is left for a position inside it', () => {
  const state = `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}/fra`;
  assert.deepEqual({ ...parseSitePath(`/fr${state}/art_1er`) }, { ...parseSitePath(state), locale: 'fr', anchor: 'art_1er', address: `${state}/art_1er` });
  assert.equal(articlePermalink(state, 'art_1er'), `${state}/art_1er`);
  assert.equal(articlePermalink(state, "Art. 5(1)*'!"), `${state}/Art.%205%281%29%2A%27%21`, 'what .NET\'s Uri.EscapeDataString writes');
  assert.equal(escapeSegment('a/b#c?d'), 'a%2Fb%23c%3Fd', 'no separator, fragment or query survives in a segment');
  assert.equal(parseSitePath(`${state}/Art.%201er`).anchor, 'Art.%201er', 'the segment as the address writes it');
  assert.equal(anchorText('Art.%201er'), 'Art. 1er');
  assert.equal(anchorText('%E9'), null, 'not UTF-8 percent-encoding');
  for (const value of ['', null, undefined]) assert.equal(anchorText(value), null, String(value));
  for (const value of [`${state}/`, `${state}/.`, `${state}/..`, `${state}/art_1/par_2`, `${state}/art%2`, `${state}/art 1`, `${state}/art_1#par_2`]) {
    assert.equal(parseSitePath(value), null, value);
  }
});

test('an EU wording permalink names the act, the wording date, the digest and the language it pins, the act asked by its CELEX', () => {
  const path = `/eu-eurlex/32016R0679/2016-05-04--${DIGEST}/fra`;
  assert.deepEqual({ ...parseSitePath(`/en${path}/047`) }, {
    kind: 'state', publisher: 'eu-eurlex', identifier: '32016R0679', date: '2016-05-04', digest: DIGEST, language: 'fra', anchor: '047', locale: 'en',
    permalink: path, address: `${path}/047`,
  });
  assert.equal(path, europePermalink({ celex: '32016R0679', date: '2016-05-04', wordingSha256: DIGEST, language: 'fra' }), 'as the API mints it');
  assert.equal(parseSitePath(`/eu-eurlex/02016R0679-20160504/2016-05-04--${DIGEST}/eng`).identifier, '02016R0679-20160504', 'a consolidated text by its own CELEX');
  assert.equal(parseSitePath(`/eu-eurlex/32016R0679/fra/2016-05-04--${DIGEST}`), null, 'the shape before R64 is moved, not read');
});

test("a work's address names the work, asked by its coordinate", () => {
  assert.deepEqual({ ...parseSitePath('/lu-legilux/loi-2017-03-14-a439') }, { kind: 'work', publisher: 'lu-legilux', identifier: '/lu-legilux/loi-2017-03-14-a439', locale: null });
  assert.deepEqual({ ...parseSitePath('/fr/eu-eurlex/32016R0679') }, { kind: 'work', publisher: 'eu-eurlex', identifier: '32016R0679', locale: 'fr' }, 'an EU act by its CELEX, as the API resolves it');
  for (const value of [undefined, null, 42, '', '/', '/en/', '/reading.html', '/lu-legilux/', `/lu-legilux/x/2024-01-01--${DIGEST}`, `/lu-legilux/x/2024-01-01--${DIGEST}/fra#art_1`]) {
    assert.equal(parseSitePath(value), null, String(value));
  }
});

test("a permalink is linked under the page's interface language, with its article; anything else is left as it is", () => {
  const permalink = `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}/fra`;
  assert.equal(permalinkHref(permalink, 'en'), `/en${permalink}`);
  assert.equal(permalinkHref(`${permalink}/art_15`, 'fr'), `/fr${permalink}/art_15`);
  assert.equal(permalinkHref(`${permalink}/art_15#par_2`, 'fr'), `/fr${permalink}/art_15#par_2`, 'a position inside the article goes along');
  assert.equal(permalinkHref('/eu-eurlex/32016R0679', 'fr'), '/fr/eu-eurlex/32016R0679');
  assert.equal(permalinkHref(`/fr${permalink}`, 'en'), `/fr${permalink}`, 'an address already under a language keeps it');
  assert.equal(permalinkHref(permalink, 'de'), permalink, 'no interface in a language not reviewed');
  for (const other of [
    'https://law.soufien.lu/lu-legilux/x', '/reading.html', '/lu-legilux/x/2024-01-01', 'http://data.legilux.public.lu/eli/etat/leg/loi/2016/12/23/n25/jo',
    `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}`,
  ]) {
    assert.equal(permalinkHref(other, 'fr'), other, `${other} is not an address this site serves a page at`);
  }
  assert.equal(permalinkHref(undefined, 'fr'), undefined);
});

test('a permalink is printed as code, inside a link only when this site serves a page at its address', () => {
  const article = `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}/fra/art_1`;
  assert.equal(isSiteAddress(article), true);
  assert.equal(renderToStaticMarkup(h(Permalink, { value: article })), `<a href="/en${article}"><code>${article}</code></a>`);
  for (const other of ['https://law.soufien.lu/lu-legilux/x', '/lu-legilux/x/2024-01-01', '/reading.html', `/lu-legilux/x/2024-01-01--${DIGEST}#art_1`, 42, null]) {
    assert.equal(isSiteAddress(other), false, String(other));
  }
  for (const other of ['https://law.soufien.lu/lu-legilux/x', '/lu-legilux/x/2024-01-01']) {
    assert.equal(renderToStaticMarkup(h(Permalink, { value: other })), `<code>${other}</code>`, `${other}: no link to an address this site serves no page at`);
  }
});

test('the language menu leads to the same page in the other language, its article included', () => {
  const article = `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}/fra/art_1er`;
  assert.equal(sameDocumentIn('fr', '/en/search.html'), '/fr/search.html');
  assert.equal(sameDocumentIn('en', `/fr${article}`), `/en${article}`);
  assert.equal(sameDocumentIn('en', '/fr/'), '/en/');
  assert.equal(sameDocumentIn('fr', '/locale-de.html'), '/fr/', 'a page under no language leads to the home page');
});

test("a law page's head names the address with no interface language as canonical, and the address in each language", () => {
  const article = `/eu-eurlex/32016R0679/2016-05-04--${DIGEST}/eng/047`;
  assert.deepEqual(JSON.parse(JSON.stringify(lawPageLinks(article, 'https://law.soufien.lu'))), {
    canonical: `https://law.soufien.lu${article}`,
    alternates: [
      { hrefLang: 'fr', href: `https://law.soufien.lu/fr${article}` },
      { hrefLang: 'en', href: `https://law.soufien.lu/en${article}` },
      { hrefLang: 'x-default', href: `https://law.soufien.lu${article}` },
    ],
  });
});

test('a wide table scrolls in a region of its own, in the tab order and named by the sentence above it', () => {
  assert.equal(
    renderToStaticMarkup(h(TableRegion, { labelledBy: 'dossier-states' }, h('table'))),
    '<div class="table-scroll" role="region" tabindex="0" aria-labelledby="dossier-states"><table></table></div>',
  );
});

test("the provision a moved address carries after # is read decoded, and a malformed one names none", () => {
  assert.equal(anchorOf('#art_15'), 'art_15');
  assert.equal(anchorOf('#Art.%201er'), 'Art. 1er');
  assert.equal(anchorOf('#%E9'), null, 'not UTF-8 percent-encoding');
  for (const value of ['', '#', 'art_15', null, undefined]) assert.equal(anchorOf(value), null, String(value));
});
