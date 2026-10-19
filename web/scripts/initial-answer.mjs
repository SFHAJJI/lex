// The answer a page carries when the server rendered it from its own answer (the specification of record's R67: the
// dossier and reading pages are complete HTML from the server, readable without JavaScript, then enhanced).
//
// The server writes the API's envelope into the page, beside the root it rendered, as an inert data block
// (`<script type="application/json">`, which no Content-Security-Policy treats as a script); the browser reads it and
// renders the same tree from it, so hydration changes nothing and the page asks the API nothing as it loads. The block
// stands outside the hydrated root, whose markup must be exactly the tree's.
//
// Data and pure functions only: the pages, their client entries and the server-render bundle share it.

import { anchorText, articlePermalink, currentAddress, dateOfQuery, pageOfQuery, parseSitePath, readingRequestFor } from './permalinks.mjs';
import { isCalendarDate, luxembourgDateOrNull } from './temporal.mjs';

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
 * holds it without text, escaped as the platform escapes it, else the act's readable address in that language, each built
 * from the act the answer names (its work key, or its EU act's CELEX), never the request's (review of #972); with no view,
 * the act's readable address the request names. At a readable address that keeps another date than `today` (`?date=`, the
 * plan's item 3.4) and shows a version that is not the one its act's readable address shows today, that version's own
 * permalink, as at a permalink: the readable address with no date shows another version, so it is not this page's
 * address. Any other address is returned as it is.
 *
 * At a permalink whose pinned version is the one its act's readable address shows `today` (Luxembourg's date, R13;
 * `answersToday`): that readable address, the article's when the version holds it (R67: "identical version pages point
 * their canonical link there"; the plan's item 3.6, revisiting the decision of #967 that such a permalink names itself).
 * With no `today`, a permalink names itself as above.
 *
 * A page of a version after its first (the plan's item 3.7), shown at an address that names no article, names itself with
 * its page: the version's permalink, or the act's readable address, and `?page=N`, N the page the answer served.
 */
export function readingAddressShown(view, address, refusal = null, today = null) {
  const named = parseSitePath(address);
  if (named === null) return address;
  const shown = (view?.publisher === 'eu-eurlex' ? view.wordings : view?.states) ?? [];
  if (named.kind === 'current') {
    const version = shown.find((one) => one.language === named.language);
    if (version === undefined) return named.act;
    if (isCalendarDate(today) && view.date !== today && !answersToday(view, version, today)) return versionAddress(version, anchorText(named.anchor));
    const work = view.publisher === 'eu-eurlex' ? view.celex : view.workKey;
    const publisherId = anchorText(named.anchor);
    const held = publisherId !== null && holdsArticle(version, publisherId);
    const act = currentAddress({ publisher: view.publisher === 'eu-eurlex' ? 'eu-eurlex' : 'lu-legilux', work, language: version.language, publisherId: held ? publisherId : null });
    return held ? act : `${act}${pageQuery(version)}`;
  }
  if (named.kind !== 'state') return address;
  if (view === null && refusal?.code === 'pinned_digest_mismatch') {
    const current = parseSitePath(refusal.payload?.current_hash_pinned_url);
    if (current?.kind === 'state' && current.locale === null && current.anchor === null) return current.address;
  }
  const pinned = shown.find((one) => one.permalink === named.permalink);
  if (pinned === undefined) return shown[0]?.permalink ?? named.permalink;
  const publisherId = anchorText(named.anchor);
  if (answersToday(view, pinned, today)) {
    const europe = view.publisher === 'eu-eurlex';
    const held = publisherId !== null && holdsArticle(pinned, publisherId);
    const act = currentAddress({
      publisher: europe ? 'eu-eurlex' : 'lu-legilux',
      work: europe ? view.celex : view.workKey,
      language: pinned.language,
      publisherId: held ? publisherId : null,
    });
    return held ? act : `${act}${pageQuery(pinned)}`;
  }
  return versionAddress(pinned, publisherId);
}

/**
 * A version's own address for a page that shows it: the permalink of the article `publisherId` names when the version quotes
 * it (the platform's) or holds it without text, else the version's permalink, with the page shown after its first.
 */
function versionAddress(version, publisherId) {
  if (publisherId === null) return `${version.permalink}${pageQuery(version)}`;
  const quoted = (version.articles ?? []).find((article) => article.publisherId === publisherId);
  if (quoted !== undefined) return quoted.permalink;
  return holdsArticle(version, publisherId) ? articlePermalink(version.permalink, publisherId) : version.permalink;
}

/**
 * Whether `version`, a state (or EU wording) of the reading `view`, is the one its act's readable address shows on `today`
 * (Luxembourg's date, R13), so the two pages show one version (R67). A Luxembourg state applicable on or before today with
 * no later state held applicable on or before it (an act held only as published included: its readable address shows that
 * state too). An EU wording in the time view, dated on or before today with no later wording dated on or before it: a later
 * wording whose text is not held in the language still bounds it (the readable address then refuses), and a wording outside
 * the time view (no `nextDate`) answers only its own date, so its act's readable address shows it on no other day. False
 * with no `today`.
 */
export function answersToday(view, version, today) {
  if (!isCalendarDate(today) || view === null || version === undefined || version === null) return false;
  if (view.publisher === 'eu-eurlex') {
    if (version.nextDate === undefined) return false;
    return version.wordingDate <= today && (version.nextDate === null || version.nextDate > today);
  }
  return version.applicabilityDate <= today && (version.nextApplicabilityDate === null || version.nextApplicabilityDate > today);
}

/** Today in Luxembourg (R13) at an answer's own instant (`context.freshness.observed_at`), or null when it names none. */
export function todayAt(instant) {
  return luxembourgDateOrNull(instant);
}

/** The query a page of a version after its first names itself with (`?page=N`), or nothing for the first. */
function pageQuery(version) {
  const number = version?.page?.number;
  return Number.isInteger(number) && number > 1 ? `?page=${number}` : '';
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
 * (its requested date), else the day its server answered (`context.freshness.observed_at`); with no answer, the date the
 * address keeps in its query (`?date=`, the plan's item 3.4), else `now`, this browser's clock. A path under no interface
 * language names no reading here: it is sent to the reader's language first. Whether the reading is of another day than
 * today is the answer's to say (`readsAnotherDay`).
 *
 * The page asked (the plan's item 3.7): the article the address names, else the page its query names (`search`), else the
 * page the answer the page carries was asked for (the server renders from the path alone, and its answer says the page).
 */
export function readingPinOf(pathname, envelope = null, now = () => new Date().toISOString(), search = '') {
  const named = parseSitePath(pathname);
  if (named === null || named.locale === null || (named.kind !== 'state' && named.kind !== 'current')) return null;
  const date = named.kind === 'state' ? named.date : (envelope === null ? dateOfQuery(search) : null) ?? answeredOn(envelope, now);
  const askedPage = envelope?.result?.value?.requested_page ?? envelope?.refusal?.helpful_payload?.requested_page;
  const request = readingRequestFor(named, date, pageOfQuery(search) ?? (Number.isInteger(askedPage) ? askedPage : null));
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

/**
 * Whether a reading at an act's readable address (`pin`, `readingPinOf`) reads the act on another day than today (the plan's
 * item 3.4, R13): the date it was asked at is not the day in Luxembourg the answer (`outcome`, a reading view state) was
 * given on. That is an address that keeps a date (`?date=`), or, at midnight in Luxembourg, a page asked one day and
 * answered the next. The answer says it alone, so the page the server rendered (which reads no query) and the page the
 * browser hydrates say one thing. False at a permalink, which names its date, and for an answer that names no day.
 */
export function readsAnotherDay(pin, outcome) {
  if (pin?.kind !== 'current') return false;
  const today = luxembourgDateOrNull(outcome?.context?.freshness?.observed_at);
  return today !== null && pin.request.date !== today;
}

/** The day a readable address's reading is asked for: the answer's own date, the day its server answered, or `now`'s. */
function answeredOn(envelope, now) {
  const asked = envelope?.result?.value?.requested_date ?? envelope?.refusal?.helpful_payload?.requested_date;
  if (isCalendarDate(asked)) return asked;
  return luxembourgDateOrNull(envelope === null ? now() : envelope?.context?.freshness?.observed_at);
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
