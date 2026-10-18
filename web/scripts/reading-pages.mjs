// Reading by article and by page (the plan's item 3.7; the specification of record's R73 and R107): what a page of a
// version says of itself, read and held to the API's rule, and the pages a reading puts together.
//
// The API answers `evidence_bundle` one bounded page of a version at a time (a Luxembourg state or an EU wording; at most
// 50 articles and 96 KiB of text, `page_rule`): each version carries `page` (the page served, the version's pages, its
// positions and the version's counts), `pages` (the index of its pages: each page's first and last article) and, when
// asked, `contents` (every article's position, publisher id, EU heading and page, no text). Every article and every
// article held without text carries its position in the version. This module reads those members for the two readers
// (`reading-answer.mjs`), and every rule here is one the answer states about itself, so a page that breaks one is drift
// and is thrown, never rendered:
//  - the page's number is one of the version's pages, the index lists every page once, in order, and the served page's
//    entry names the page's first and last article and its count;
//  - the page's articles, quoted and without text, are exactly its positions, each once, each list in order;
//  - the version's counts are at least the page's, and the index's counts add up to the version's;
//  - the contents, when present, are every position once, in order, each on the page the index puts it on.
//
// It also builds what the pages need beyond one answer: the request for another page, the site address of a page
// (`?page=N` on the version's address, R64's article segment left for the cited article), and one reading assembled from
// several pages (the next articles a reader loads, and an export of a whole act, which asks every page). Data and pure
// functions only: the pages, their client entries, the server renderer and the export share it.

import { parseSitePath } from './permalinks.mjs';

function fail(message) {
  throw new Error(message);
}

function own(object, key, where) {
  if (object === null || typeof object !== 'object' || !Object.hasOwn(object, key)) fail(`${where} does not carry ${key}`);
  return object[key];
}

function whole(value, where, minimum = 0) {
  if (!Number.isInteger(value) || value < minimum) fail(`${where} is ${JSON.stringify(value)} rather than a whole number from ${minimum}`);
  return value;
}

function text(value, where) {
  if (typeof value !== 'string' || value.length === 0) fail(`${where} is not an id this page can print`);
  return value;
}

/**
 * Reads a version's `page`, `pages` and `contents` (`headings`: an EU wording's, whose index and contents name headings),
 * against its page's articles (`quoted`, each with `position` and `publisherId`; `withoutText`, the same). Returns the
 * frozen `{ page, pages, contents }` the view carries.
 */
export function readVersionPages(version, { quoted, withoutText, headings }, where) {
  const page = own(version, 'page', where);
  const at = `${where} page`;
  const number = whole(own(page, 'number', at), `${at}.number`, 1);
  const count = whole(own(page, 'pages', at), `${at}.pages`, 1);
  if (number > count) fail(`${at} is page ${number} of ${count}`);
  const firstPosition = whole(own(page, 'first_position', at), `${at}.first_position`, 1);
  const lastPosition = whole(own(page, 'last_position', at), `${at}.last_position`, firstPosition);
  const articlesInVersion = whole(own(page, 'articles_in_version', at), `${at}.articles_in_version`, lastPosition);
  const quotedInVersion = whole(own(page, 'quoted_in_version', at), `${at}.quoted_in_version`, 1);
  const withoutTextInVersion = whole(own(page, 'without_text_in_version', at), `${at}.without_text_in_version`);
  if (quotedInVersion + withoutTextInVersion !== articlesInVersion) {
    fail(`${at} counts ${quotedInVersion} quoted and ${withoutTextInVersion} without text in a version of ${articlesInVersion}`);
  }
  if (quoted.length > quotedInVersion || withoutText.length > withoutTextInVersion) {
    fail(`${at} holds more articles than its version does`);
  }
  const validityConflictsInVersion = headings ? null : whole(own(page, 'validity_conflicts_in_version', at), `${at}.validity_conflicts_in_version`);

  // The page's articles are exactly its positions, each once, and each list is in the version's order.
  for (const [name, list] of [['articles', quoted], ['articles without text', withoutText]]) {
    for (let index = 1; index < list.length; index += 1) {
      if (list[index].position <= list[index - 1].position) fail(`${where}'s ${name} are not in the version's order`);
    }
  }
  const byPosition = new Map();
  for (const article of [...quoted, ...withoutText]) {
    if (article.position < firstPosition || article.position > lastPosition) {
      fail(`${where} serves ${article.publisherId} at position ${article.position}, outside its page (${firstPosition} to ${lastPosition})`);
    }
    if (byPosition.has(article.position)) fail(`${where} serves two articles at position ${article.position}`);
    byPosition.set(article.position, article);
  }
  if (byPosition.size !== lastPosition - firstPosition + 1) {
    fail(`${where}'s page holds positions ${firstPosition} to ${lastPosition} and serves ${byPosition.size} articles`);
  }

  const index = Object.freeze(own(version, 'pages', where).map((row, rowIndex) => {
    const label = `${where} pages[${rowIndex}]`;
    if (whole(own(row, 'number', label), `${label}.number`, 1) !== rowIndex + 1) fail(`${label} is not page ${rowIndex + 1}`);
    return Object.freeze({
      number: rowIndex + 1,
      firstPublisherId: text(own(row, 'first_publisher_id', label), `${label}.first_publisher_id`),
      lastPublisherId: text(own(row, 'last_publisher_id', label), `${label}.last_publisher_id`),
      articles: whole(own(row, 'articles', label), `${label}.articles`, 1),
      firstHeading: headings ? own(row, 'first_heading', label) : null,
      lastHeading: headings ? own(row, 'last_heading', label) : null,
    });
  }));
  if (index.length !== count) fail(`${where} has ${count} pages and indexes ${index.length}`);
  if (index.reduce((sum, row) => sum + row.articles, 0) !== articlesInVersion) fail(`${where}'s page index does not count its version's articles`);
  const served = index[number - 1];
  if (served.articles !== lastPosition - firstPosition + 1
    || served.firstPublisherId !== byPosition.get(firstPosition).publisherId
    || served.lastPublisherId !== byPosition.get(lastPosition).publisherId) {
    fail(`${where}'s page index does not name the page it serves`);
  }
  const before = index.slice(0, number - 1).reduce((sum, row) => sum + row.articles, 0);
  if (before + 1 !== firstPosition) fail(`${where}'s page ${number} starts at position ${firstPosition}, and its index puts it at ${before + 1}`);

  const contentsValue = own(version, 'contents', where);
  let contents = null;
  if (contentsValue !== null) {
    if (!Array.isArray(contentsValue)) fail(`${where}'s contents are a list or null`);
    if (contentsValue.length !== articlesInVersion) fail(`${where}'s contents list ${contentsValue.length} of its ${articlesInVersion} articles`);
    let pageAt = 1;
    let left = index[0].articles;
    contents = Object.freeze(contentsValue.map((row, rowIndex) => {
      const label = `${where} contents[${rowIndex}]`;
      if (whole(own(row, 'position', label), `${label}.position`, 1) !== rowIndex + 1) fail(`${label} is not position ${rowIndex + 1}`);
      if (left === 0) {
        pageAt += 1;
        left = index[pageAt - 1].articles;
      }
      left -= 1;
      if (own(row, 'page', label) !== pageAt) fail(`${label} is on page ${row.page}, and the index puts it on page ${pageAt}`);
      const textHeld = own(row, 'text_held', label);
      if (typeof textHeld !== 'boolean') fail(`${label}.text_held is true or false`);
      return Object.freeze({
        position: rowIndex + 1,
        publisherId: text(own(row, 'publisher_id', label), `${label}.publisher_id`),
        heading: headings ? own(row, 'heading', label) : null,
        textHeld,
        page: pageAt,
      });
    }));
  }

  return Object.freeze({
    page: Object.freeze({
      number, pages: count, firstPosition, lastPosition, articlesInVersion, quotedInVersion, withoutTextInVersion, validityConflictsInVersion,
    }),
    pages: index,
    contents,
  });
}

/** The versions of a reading view: a Luxembourg view's states, an EU view's wordings. */
export function versionsOf(view) {
  return view.publisher === 'eu-eurlex' ? view.wordings : view.states;
}

/** The digest that names a version: a Luxembourg state's, an EU wording's. */
function digestOf(version) {
  return version.stateSha256 ?? version.wordingSha256;
}

/**
 * The pages a version of a view shows, as one run: `{ first, last }`, the first page served and the last one added
 * (`mergeReadingPage`); a view read from one answer shows the one page it served.
 */
export function pagesShown(version) {
  return version.shown ?? Object.freeze({ first: version.page.number, last: version.page.number });
}

/** Whether a view shows every page of every version it reads. */
export function showsWholeVersions(view) {
  return versionsOf(view).every((version) => {
    const shown = pagesShown(version);
    return shown.first === 1 && shown.last === version.page.pages;
  });
}

/** Whether a version has a page after the last one its view shows. */
export function hasNextPage(version) {
  return pagesShown(version).last < version.page.pages;
}

/**
 * A view with the next page of its versions appended (`next`, the view read from the answer to `nextPageRequest`): each
 * version `next` reads is one of the view's, at the page after the last one the view shows, and its articles follow the
 * shown ones, in the version's order; a version `next` does not read is left as it is. The version's own facts are the
 * first answer's. Anything else is not one reading, and this throws.
 */
export function mergeReadingPage(view, next) {
  if (view.publisher !== next.publisher) fail('the next page is of another publisher');
  const added = new Map(versionsOf(next).map((version) => [digestOf(version), version]));
  if (added.size === 0) fail('the next page reads no version');
  const merged = versionsOf(view).map((version) => {
    const page = added.get(digestOf(version));
    if (page === undefined) return version;
    added.delete(digestOf(version));
    if (page.permalink !== version.permalink) fail(`the next page of ${version.permalink} names ${page.permalink}`);
    const shown = pagesShown(version);
    if (page.page.number !== shown.last + 1) fail(`the next page of ${version.permalink} is page ${page.page.number}, after page ${shown.last}`);
    return Object.freeze({
      ...version,
      articles: Object.freeze([...version.articles, ...page.articles]),
      articlesWithoutText: Object.freeze([...version.articlesWithoutText, ...page.articlesWithoutText]),
      ...(Object.hasOwn(version, 'validityConflictCount') ? { validityConflictCount: version.validityConflictCount + page.validityConflictCount } : {}),
      shown: Object.freeze({ first: shown.first, last: page.page.number }),
    });
  });
  if (added.size > 0) fail(`the next page reads a version this reading does not: ${[...added.values()][0].permalink}`);
  return Object.freeze({ ...view, [view.publisher === 'eu-eurlex' ? 'wordings' : 'states']: Object.freeze(merged) });
}

/**
 * The request for the next page of the first version of a view that has one, from the request that asked the view
 * (`request`: the identifier, the date and the language as asked): the same request in that version's language, with
 * that page's number and no article, so a reading of two languages paged apart asks each its own pages. Null when every
 * version is shown to its last page.
 */
export function nextPageRequest(request, view) {
  const pending = versionsOf(view).find(hasNextPage);
  if (pending === undefined) return null;
  const { article: _article, page: _page, ...asked } = request;
  return Object.freeze({ ...asked, language: pending.language, page: pagesShown(pending).last + 1 });
}

/**
 * The site address of one page of a version's reading, from the reading's address (`address`, with no interface language:
 * a permalink, or an act's readable address, an article's included): the address with no article, and `?page=N` after
 * it for a page after the first. The article segment is the cited article's (R64), which a page leaves.
 */
export function pageAddress(address, number) {
  const named = parseSitePath(address);
  const base = named?.kind === 'state' ? named.permalink : named?.kind === 'current' ? named.act : address;
  return number > 1 ? `${base}?page=${number}` : base;
}

/**
 * The page after the ones one version of a view shows, read and added (`mergeReadingPage`): asked with `ask(request)` (the
 * view read from the answer, or a throw), the request the view was asked with in that version's language at that page.
 * Returns the view with it, and the version as it now stands.
 */
export async function readNextPage({ view, version, request, ask }) {
  const { article: _article, page: _page, ...asked } = request;
  const merged = mergeReadingPage(view, await ask(Object.freeze({ ...asked, language: version.language, page: pagesShown(version).last + 1 })));
  const digest = digestOf(version);
  return { view: merged, version: versionsOf(merged).find((entry) => digestOf(entry) === digest) };
}

/**
 * How the pages asked in place stand, by version digest (`paging`, or the export's `assembling`), once a page of `digest`
 * is asked and the one asked before for `stopped` is stopped (one is asked at a time): a version still loading when it was
 * stopped goes back to no state, so its link or control can be used again (the review of #978: it stayed loading, and dead).
 */
export function withoutStopped(states, stopped, digest) {
  if (stopped === null || stopped === digest || states[stopped]?.state !== 'loading') return states;
  const { [stopped]: _stopped, ...rest } = states;
  return rest;
}

/** The view an asked reading's answer is read into, or a throw naming why there is none (`ask` for `readNextPage`). */
export function viewOf(outcome) {
  if (outcome?.state !== 'success') throw new Error(`the page was not answered (${outcome?.code ?? outcome?.state})`);
  return outcome.view;
}

/**
 * Every page of a reading put together, from the view of its first page served (`first`) and its request: each next page
 * asked with `ask(request)` (which returns the view read from the answer, or throws), until every version is shown to its
 * last page. Returns the whole view. A reading that fits one page is returned as it is, so an export of it is the export of
 * the reading shown.
 */
export async function assembleReading({ first, request, ask, signal }) {
  // A reading opened at a later page (an article's, or `?page=N`) is put together from the first page.
  const fromStart = versionsOf(first).every((version) => pagesShown(version).first === 1);
  const { article: _article, page: _page, ...asked } = request;
  let view = fromStart ? first : await ask(Object.freeze({ ...asked, page: 1 }));
  if (!versionsOf(view).every((version) => pagesShown(version).first === 1)) fail('the first page asked is not the first page');
  let next = nextPageRequest(request, view);
  while (next !== null) {
    if (signal?.aborted) fail('the reading was abandoned before every page was read');
    view = mergeReadingPage(view, await ask(next));
    next = nextPageRequest(request, view);
  }
  return view;
}
