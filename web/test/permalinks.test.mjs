// The site's own addresses for law (`permalinks.mjs`): which page each is served, what it names, and the links the
// pages build to them. The table of paths is shared with the API's tests (V3WebRootTests), so the live server and the
// API serve one set of addresses.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { Permalink, TableRegion } from '../.react-build/app.mjs';
import { PERMALINK_PATTERNS, anchorOf, isSiteAddress, pageForSitePath, parseSitePath, permalinkHref } from '../scripts/permalinks.mjs';

const TABLE = JSON.parse(await readFile(new URL('../../tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json', import.meta.url), 'utf8'));
const DIGEST = '0123456789abcdef'.repeat(4);

test('each path of the shared table is served the page the table names, and no other path is claimed', () => {
  assert.ok(TABLE.paths.length > 30, 'the table the API is held to as well');
  for (const { path, page } of TABLE.paths) {
    assert.equal(pageForSitePath(path), page, path);
  }
  const served = TABLE.paths.filter(({ page }) => page !== null).map(({ page }) => page);
  assert.deepEqual([...new Set(served)].sort(), ['dossier.html', 'fr/dossier.html', 'fr/reading.html', 'reading.html'], 'both pages, in both interfaces');
  for (const kind of ['luxembourgWork', 'luxembourgState', 'europeWork', 'europeWording']) {
    assert.ok(TABLE.paths.some(({ path }) => PERMALINK_PATTERNS[kind].test(path.replace(/^\/fr\//, '/'))), `the table holds a ${kind} address`);
  }
});

test('a Luxembourg state permalink names the work, the date and the digest it pins, and is asked in any held language', () => {
  const path = `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}`;
  assert.deepEqual({ ...parseSitePath(path) }, {
    kind: 'state', publisher: 'lu-legilux', identifier: '/lu-legilux/loi-2017-03-14-a439', date: '2024-01-01', digest: DIGEST, language: null, locale: 'en', permalink: path,
  });
  assert.ok(Object.isFrozen(parseSitePath(path)));
  assert.deepEqual({ ...parseSitePath(`/fr${path}`) }, { ...parseSitePath(path), locale: 'fr' }, 'the French interface names the same permalink, without its prefix');
});

test('an EU wording permalink names the act, the language, the wording date and the digest it pins, the act asked by its CELEX', () => {
  const path = `/eu-eurlex/32016R0679/fra/2016-05-04--${DIGEST}`;
  assert.deepEqual({ ...parseSitePath(path) }, {
    kind: 'state', publisher: 'eu-eurlex', identifier: '32016R0679', date: '2016-05-04', digest: DIGEST, language: 'fra', locale: 'en', permalink: path,
  });
  assert.equal(parseSitePath(`/eu-eurlex/02016R0679-20160504/eng/2016-05-04--${DIGEST}`).identifier, '02016R0679-20160504', 'a consolidated text by its own CELEX');
});

test("a work's address names the work, asked by its coordinate", () => {
  assert.deepEqual({ ...parseSitePath('/lu-legilux/loi-2017-03-14-a439') }, { kind: 'work', publisher: 'lu-legilux', identifier: '/lu-legilux/loi-2017-03-14-a439', locale: 'en' });
  assert.deepEqual({ ...parseSitePath('/fr/eu-eurlex/32016R0679') }, { kind: 'work', publisher: 'eu-eurlex', identifier: '32016R0679', locale: 'fr' }, 'an EU act by its CELEX, as the API resolves it');
  for (const value of [undefined, null, 42, '', '/', '/reading.html', '/lu-legilux/', `/lu-legilux/x/2024-01-01--${DIGEST}#art_1`]) {
    assert.equal(parseSitePath(value), null, String(value));
  }
});

test("a permalink is linked under the page's interface, with its article; anything else is left as it is", () => {
  const permalink = `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}`;
  assert.equal(permalinkHref(permalink), permalink);
  assert.equal(permalinkHref(permalink, 'en'), permalink);
  assert.equal(permalinkHref(`${permalink}#art_15`, 'fr'), `/fr${permalink}#art_15`);
  assert.equal(permalinkHref('/eu-eurlex/32016R0679', 'fr'), '/fr/eu-eurlex/32016R0679');
  for (const other of ['https://law.soufien.lu/lu-legilux/x', '/reading.html', '/lu-legilux/x/2024-01-01', 'http://data.legilux.public.lu/eli/etat/leg/loi/2016/12/23/n25/jo']) {
    assert.equal(permalinkHref(other, 'fr'), other, `${other} is not an address this site serves a page at`);
  }
  assert.equal(permalinkHref(undefined, 'fr'), undefined);
});

test('a permalink is printed as code, inside a link only when this site serves a page at its address', () => {
  const state = `/lu-legilux/loi-2017-03-14-a439/2024-01-01--${DIGEST}`;
  assert.equal(isSiteAddress(`${state}#art_1`), true);
  assert.equal(renderToStaticMarkup(h(Permalink, { value: `${state}#art_1` })), `<a href="${state}#art_1"><code>${state}#art_1</code></a>`);
  for (const other of ['https://law.soufien.lu/lu-legilux/x', '/lu-legilux/x/2024-01-01', '/reading.html', 42, null]) {
    assert.equal(isSiteAddress(other), false, String(other));
  }
  for (const other of ['https://law.soufien.lu/lu-legilux/x', '/lu-legilux/x/2024-01-01']) {
    assert.equal(renderToStaticMarkup(h(Permalink, { value: other })), `<code>${other}</code>`, `${other}: no link to an address this site serves no page at`);
  }
});

test('a wide table scrolls in a region of its own, in the tab order and named by the sentence above it', () => {
  assert.equal(
    renderToStaticMarkup(h(TableRegion, { labelledBy: 'dossier-states' }, h('table'))),
    '<div class="table-scroll" role="region" tabindex="0" aria-labelledby="dossier-states"><table></table></div>',
  );
});

test("the article a reading's address names after # is read decoded, and a malformed one names none", () => {
  assert.equal(anchorOf('#art_15'), 'art_15');
  assert.equal(anchorOf('#Art.%201er'), 'Art. 1er');
  assert.equal(anchorOf('#%E9'), null, 'not UTF-8 percent-encoding');
  for (const value of ['', '#', 'art_15', null, undefined]) assert.equal(anchorOf(value), null, String(value));
});
