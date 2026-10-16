// The answer a page carries when the server rendered it from its own answer (the specification of record's R67: the
// dossier and reading pages are complete HTML from the server, readable without JavaScript, then enhanced).
//
// The server writes the API's envelope into the page, beside the root it rendered, as an inert data block
// (`<script type="application/json">`, which no Content-Security-Policy treats as a script); the browser reads it and
// renders the same tree from it, so hydration changes nothing and the page asks the API nothing as it loads. The block
// stands outside the hydrated root, whose markup must be exactly the tree's.
//
// Data and pure functions only: the pages, their client entries and the server-render bundle share it.

import { anchorText, articlePermalink, currentAddress, parseSitePath, readingRequestFor } from './permalinks.mjs';
import { isCalendarDate, luxembourgDate } from './temporal.mjs';

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
 * is the address the page stands at with no interface language, an article's included, `view` the reading it shows (null
 * when it shows none, as a refusal), and `refusal` the refusal it shows instead (`{ code, payload }`), if any.
 *
 * At a permalink: when the view shows the state (or EU wording) the address pins, the minted permalink of the article the
 * address names, if that state quotes it or holds it without text (its publisher id equal to the segment decoded), else
 * the state's own. When it shows another version in its place (review of #959: any digest named itself canonical): that
 * version's permalink, with no article. With no view: for `pinned_digest_mismatch`, the current state's permalink the
 * refusal names (R64: "with the current link"), else the pinned state's permalink, with no article.
 *
 * At an act's readable address (R67): the readable address of the article it names when the version shown quotes it or
 * holds it without text, escaped as the platform escapes a segment, else the act's readable address in that language. Any
 * other address is returned as it is.
 */
export function readingAddressShown(view, address, refusal = null) {
  const named = parseSitePath(address);
  if (named === null) return address;
  const shown = (view?.publisher === 'eu-eurlex' ? view.wordings : view?.states) ?? [];
  if (named.kind === 'current') {
    const version = shown.find((one) => one.language === named.language);
    const publisherId = anchorText(named.anchor);
    if (version === undefined || publisherId === null || !holdsArticle(version, publisherId)) return named.act;
    return currentAddress({ publisher: named.publisher, work: named.work, language: named.language, publisherId });
  }
  if (named.kind !== 'state') return address;
  if (view === null && refusal?.code === 'pinned_digest_mismatch') {
    const current = parseSitePath(refusal.payload?.current_hash_pinned_url);
    if (current?.kind === 'state' && current.locale === null && current.anchor === null) return current.address;
  }
  const pinned = shown.find((one) => one.permalink === named.permalink);
  if (pinned === undefined) return shown[0]?.permalink ?? named.permalink;
  const publisherId = anchorText(named.anchor);
  if (publisherId === null) return pinned.permalink;
  const quoted = (pinned.articles ?? []).find((article) => article.publisherId === publisherId);
  if (quoted !== undefined) return quoted.permalink;
  return holdsArticle(pinned, publisherId) ? articlePermalink(pinned.permalink, publisherId) : pinned.permalink;
}

/** Whether a state (or EU wording) quotes the article with this publisher id, or holds it without text. */
function holdsArticle(version, publisherId) {
  return (version.articles ?? []).some((article) => article.publisherId === publisherId)
    || (version.articlesWithoutText ?? []).some((article) => article.publisherId === publisherId);
}

/**
 * What a reading page's address names for the reading it shows (`LiveReading.jsx`), or null at an address that names no
 * reading: `kind` (`state` at a permalink, `current` at an act's readable address, R67), `permalink` (the state's or EU
 * wording's, with no article; null at a readable address), `act` (the act's readable address in the text's language; null
 * at a permalink), `address` (the whole address, its article included), `digest`, `anchor` (the article's segment as the
 * path writes it, or null) and `language` (the text's); `request`, what the page asks (`readingRequestFor`: at a permalink
 * the permalink itself, R64); and `form`, what its form shows (the work, the date and the language).
 *
 * At a readable address the date is today in Luxembourg (R13), taken from the answer the page carries when it carries one,
 * so the page the server rendered and the page a browser hydrates from it name one date: the date that answer was asked for
 * (its requested date), else the day its server answered (`context.freshness.observed_at`); with no answer, `now`, this
 * browser's clock. A path under no interface language names no reading here: it is sent to the reader's language first.
 */
export function readingPinOf(pathname, envelope = null, now = () => new Date().toISOString()) {
  const named = parseSitePath(pathname);
  if (named === null || named.locale === null || (named.kind !== 'state' && named.kind !== 'current')) return null;
  const date = named.kind === 'state' ? named.date : answeredOn(envelope, now);
  const request = readingRequestFor(named, date);
  if (request === null) return null;
  return Object.freeze({
    kind: named.kind,
    permalink: named.kind === 'state' ? named.permalink : null,
    act: named.kind === 'current' ? named.act : null,
    address: named.address,
    digest: named.digest ?? null,
    anchor: named.anchor,
    language: named.language,
    request,
    form: Object.freeze({ identifier: named.identifier, date, language: named.language }),
  });
}

/** The day a readable address's reading is asked for: the answer's own date, the day its server answered, or `now`'s. */
function answeredOn(envelope, now) {
  const asked = envelope?.result?.value?.requested_date ?? envelope?.refusal?.helpful_payload?.requested_date;
  if (isCalendarDate(asked)) return asked;
  const instant = envelope === null ? now() : envelope?.context?.freshness?.observed_at;
  try {
    return luxembourgDate(instant);
  } catch {
    return null;
  }
}

/**
 * The address a law's page as built names as its own before it holds an answer (`client.jsx`), for the path it stands at:
 * a work's address, a reading's state's permalink with no article, or an act's readable address with no article, the
 * article segment left out until the reading's answer says the version holds it (`readingAddressShown`). Null for a path
 * that is no law's address.
 */
export function addressBeforeAnswer(path) {
  const address = lawPathOf(path);
  const named = parseSitePath(address);
  if (named === null) return null;
  if (named.kind === 'state') return named.permalink;
  return named.kind === 'current' ? named.act : address;
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
