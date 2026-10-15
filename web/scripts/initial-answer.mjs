// The answer a page carries when the server rendered it from its own answer (the specification of record's R67: the
// dossier and reading pages are complete HTML from the server, readable without JavaScript, then enhanced).
//
// The server writes the API's envelope into the page, beside the root it rendered, as an inert data block
// (`<script type="application/json">`, which no Content-Security-Policy treats as a script); the browser reads it and
// renders the same tree from it, so hydration changes nothing and the page asks the API nothing as it loads. The block
// stands outside the hydrated root, whose markup must be exactly the tree's.
//
// Data and pure functions only: the pages, their client entries and the server-render bundle share it.

import { anchorText, articlePermalink, parseSitePath } from './permalinks.mjs';

/** The id of the data block that carries the answer a page was rendered from. */
export const INITIAL_ANSWER_ID = 'lex-initial-answer';

/**
 * A value as JSON that cannot end the block it stands in: `<`, `>` and `&` written as Unicode escapes (so no `</script>`
 * and no comment opener), and the two line separators JavaScript once refused in strings, escaped too.
 */
export function jsonInScript(value) {
  return JSON.stringify(value)
    .replace(/</g, '\\u003c')
    .replace(/>/g, '\\u003e')
    .replace(/&/g, '\\u0026')
    .replace(/\u2028/g, '\\u2028')
    .replace(/\u2029/g, '\\u2029');
}

/** The address of the law a page stands at, with its interface language taken off (`/en/lu-legilux/x` is `/lu-legilux/x`). */
export function lawPathOf(path) {
  const match = /^\/(?:en|fr)(\/(?:lu-legilux|eu-eurlex)\/.*)$/.exec(path ?? '');
  return match === null ? null : match[1];
}

/**
 * The address a reading page names as its own (canonical, and in each language), with no interface language: an address
 * the platform mints, taken from the answer the page shows and never from the request (review of #967: any article
 * segment, `/fra/no-such-article` or an escape the platform never writes, `art%5F15`, named itself canonical). `address`
 * is the address the page stands at with no interface language, an article's included, and `view` the reading it shows
 * (null when it shows none, as a refusal). When the view shows the state (or EU wording) the address pins: the minted
 * permalink of the article the address names, if that state quotes it or holds it without text (its publisher id equal
 * to the segment decoded), else the state's own. When it shows another version in its place, as a stale permalink's page
 * does (review of #959: any digest named itself canonical): that version's permalink, with no article. With no view: the
 * pinned state's permalink, with no article. Any other address is returned as it is.
 */
export function readingAddressShown(view, address) {
  const named = parseSitePath(address);
  if (named === null || named.kind !== 'state') return address;
  const shown = (view?.publisher === 'eu-eurlex' ? view.wordings : view?.states) ?? [];
  const pinned = shown.find((one) => one.permalink === named.permalink);
  if (pinned === undefined) return shown[0]?.permalink ?? named.permalink;
  const publisherId = anchorText(named.anchor);
  if (publisherId === null) return pinned.permalink;
  const quoted = (pinned.articles ?? []).find((article) => article.publisherId === publisherId);
  if (quoted !== undefined) return quoted.permalink;
  const withoutText = (pinned.articlesWithoutText ?? []).some((article) => article.publisherId === publisherId);
  return withoutText ? articlePermalink(pinned.permalink, publisherId) : pinned.permalink;
}

/**
 * The address a law's page as built names as its own before it holds an answer (`client.jsx`), for the path it stands at:
 * a work's address, or a reading's state's permalink with no article, the article segment left out until the reading's
 * answer says the state holds it (`readingAddressShown`). Null for a path that is no law's address.
 */
export function addressBeforeAnswer(path) {
  const address = lawPathOf(path);
  const named = parseSitePath(address);
  if (named === null) return null;
  return named.kind === 'state' ? named.permalink : address;
}

/**
 * The answer a page was rendered from, as its client entry reads it: `{ envelope, path }`, or null when the page carries
 * none (a page the server did not render with an answer, or a block that is not JSON, which then hydrates as the empty
 * form and is reported as a hydration that changed the page).
 */
export function readInitialAnswer(document, location) {
  const block = document.getElementById(INITIAL_ANSWER_ID);
  if (block === null) return null;
  try {
    return { envelope: JSON.parse(block.textContent ?? ''), path: location.pathname };
  } catch {
    return null;
  }
}
