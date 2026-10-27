// The site's own addresses: its pages under each interface language, and its addresses for law, a work's dossier and a
// state's (or an EU wording's) reading, the permalinks the API mints (UX spec section 1, URLs; the launch contract's first
// promise: a permalink pins work, article, date, language and digest).
//
// One permalink shape (the specification of record's R64): /{publisher}/{work}/{date}--{digest}/{language}/{anchor}.
//
//   /lu-legilux/{work}                                              a Luxembourg work's dossier
//   /lu-legilux/{work}/{yyyy-mm-dd}--{state sha256}/{lng}[/{art}]   a Luxembourg state's reading, at an article
//   /lu-legilux/{work}/{lng}[/{art}]                                the work's current version, at an article
//   /eu-eurlex/{celex}                                              an EU act's dossier
//   /eu-eurlex/{celex}/{yyyy-mm-dd}--{sha256}/{lng}[/{provision}]   an EU wording's reading, at a provision
//   /eu-eurlex/{celex}/{lng}[/{provision}]                          the act's current wording, at a provision
//
// `{lng}` is the text's language, the publisher's three letters (`fra`, `deu`, `eng`), and the digest is that language's:
// a Luxembourg state is one expression in one language, and an EU wording one expression too. The article (or provision)
// is a path segment, escaped as the platform escapes it (`escapeSegment`); "#" is left for a position inside an article.
// The interface language never enters a permalink.
//
// The readable address of an act and of an article (the specification of record's R67: "Every work and article has a
// readable address that resolves to its current version") names the work and the text's language, with no date and no
// digest, and opens the reading of the version that answers today in Luxembourg (R13): its third segment is a language,
// never a pin, so it cannot be taken for a permalink. Its article is never a calendar date, with or without a digest after
// it: `/eu-eurlex/{celex}/{lng}/{date}` is an EU stable coordinate and `/eu-eurlex/{celex}/{lng}/{date}--{sha256}` the EU
// permalink shape before R64 (`movedFor`), and neither names an article. A permalink cites one version for good; a
// readable address follows the act. A consolidated version's CELEX (`0{yyyy}{type}{number}-{yyyymmdd}`) names one wording, not
// the act today, so its readable address has moved for good to its act's (`3{yyyy}{type}{number}`), whose reading answers
// today (review of #972: it quoted another wording under its own address).
//
// Every page stands under its interface language, /fr/ or /en/ (the specification of record's R61): /fr/search.html,
// /en/lu-legilux/{work}. An address the API mints names no interface language, so it, and the site's root, are answered
// with a redirect to the reader's language: the one the browser prefers of the two, else French (`negotiateLanguage`).
// The fragment never reaches a server: the page reads it, and a browser keeps it across a redirect.
//
// An EU wording's permalink of the shape before R64 (`/eu-eurlex/{celex}/{lng}/{date}--{sha256}`) names everything the
// new one does, so it is moved there for good (`movedFor`), under the interface language it had. A Luxembourg state's of
// that time (`/lu-legilux/{work}/{date}--{sha256}`) names no language, so no address can be computed from it alone: it
// is answered as any address this site does not know.
//
// This module is data and pure functions only: the live pages' bundles, the live server and the tests share it, and the
// API holds the same patterns and the same choice (V3WebRoot), held to these by a test.
//
// A readable address keeps the date it was read at in its query, `?date=yyyy-mm-dd` (the plan's item 3.4; R13: today is
// "written into the URL"): its page writes it there once it has read the act, so a link a reader copies opens the act as
// this site holds it for that date, and both servers render that address at that date (`dateOfQuery`). It stays a readable
// address, never a permalink: it names no digest, so it pins no version, and it says the act this site holds for its date
// (a version recorded later for that date is the one it then shows); the permalink still cites one version for good. Its
// canonical link is the readable address with no date when the version shown is today's, else that version's permalink
// (`readingAddressShown`). A date is digits and dashes: no address this site builds carries a word in its query (R109).

import { isCalendarDate } from './temporal.mjs';

/**
 * A Luxembourg work key as the API takes it (`IsWorkKey`): lower-case letters, digits, hyphen and underscore, here starting
 * with a letter or digit, as every key the publisher's catalogue yields does. A key the API refuses opens no page.
 */
const WORK = '[a-z0-9][a-z0-9_-]*';
/**
 * A CELEX number as the API mints it (sector, year, type, number, and an optional suffix such as (01) or -20160504), as one
 * path segment: a solidus is written `%2F` (the Treaties' and the Charter's `12016E/TXT` is `12016E%2FTXT`,
 * `europeWorkSegment`), and a lower-case `%2f` reads the same.
 */
const CELEX = '[0-9A-Za-z](?:[0-9A-Za-z()._-]|%2[Ff])*';

/**
 * A CELEX as the one path segment R64's `{work}` is (the API's `EuropeWorkSegment`): unchanged unless it carries a solidus,
 * which RFC 3986 writes `%2F` inside a segment. Every CELEX with none keeps its bytes, and a segment already written so is
 * left as it is.
 */
export function europeWorkSegment(celex) {
  return String(celex).replaceAll('/', '%2F');
}

/** The CELEX a path segment names (`CELEX`), its `%2F` read as the solidus it writes (the API's `EuropeCelexOfSegment`). */
export function europeCelexOfSegment(segment) {
  return segment.replace(/%2[Ff]/g, '/');
}
/** A pin: the date, `--` and the digest. */
const PIN = '([0-9]{4}-[0-9]{2}-[0-9]{2})--([0-9a-f]{64})';
/** The text's language: the publisher's three lower-case letters. */
const LANGUAGE = '([a-z]{3})';
/**
 * An article or provision: one path segment of RFC 3986's characters (`pchar`), anything else percent-encoded, and never
 * one that is `.` or `..` once decoded (`%2e` or `%2E` for a dot, as a URL parser reads it), which a browser and the live
 * server resolve away (review of #967: the API served `%2E%2E` as an article).
 */
const ANCHOR = "(?!(?:\\.|%2[eE]){1,2}$)((?:[0-9A-Za-z._~!$&'()*+,;=:@-]|%[0-9A-Fa-f]{2})+)";
/** A readable address's article: an article segment that is never a calendar date, with or without a digest after it. */
const CURRENT_ANCHOR = `(?![0-9]{4}-[0-9]{2}-[0-9]{2}(?:--[0-9a-f]{64})?$)${ANCHOR}`;

export const PERMALINK_PATTERNS = Object.freeze({
  luxembourgWork: new RegExp(`^/lu-legilux/(${WORK})$`),
  luxembourgState: new RegExp(`^/lu-legilux/(${WORK})/${PIN}/${LANGUAGE}(?:/${ANCHOR})?$`),
  luxembourgCurrent: new RegExp(`^/lu-legilux/(${WORK})/${LANGUAGE}(?:/${CURRENT_ANCHOR})?$`),
  europeWork: new RegExp(`^/eu-eurlex/(${CELEX})$`),
  europeWording: new RegExp(`^/eu-eurlex/(${CELEX})/${PIN}/${LANGUAGE}(?:/${ANCHOR})?$`),
  europeCurrent: new RegExp(`^/eu-eurlex/(${CELEX})/${LANGUAGE}(?:/${CURRENT_ANCHOR})?$`),
});

/** An EU wording's permalink in the shape before R64, `/eu-eurlex/{celex}/{lng}/{date}--{sha256}`, moved to R64's. */
const EUROPE_WORDING_BEFORE_R64 = new RegExp(`^/eu-eurlex/(${CELEX})/${LANGUAGE}/${PIN}$`);

/**
 * A consolidated version's CELEX as EUR-Lex writes it: sector 0, the consolidated act's year, type and number (with a
 * corrigendum's suffix), and the consolidation's date; the act's own CELEX is the same in sector 3.
 */
const CONSOLIDATED_CELEX = /^0([0-9]{4}[A-Z]{1,2}[0-9]{4,}(?:\([0-9]{2}\))?)-[0-9]{8}$/;

/** A readable address under a consolidated version's CELEX, moved to its act's (`movedFor`). */
const EUROPE_CONSOLIDATED_CURRENT = new RegExp(`^/eu-eurlex/0([0-9]{4}[A-Z]{1,2}[0-9]{4,}(?:\\([0-9]{2}\\))?)-[0-9]{8}/${LANGUAGE}(?:/${CURRENT_ANCHOR})?$`);

/** The interface languages, each under its own path (R61); the first is the one a reader with no preference gets. */
export const INTERFACE_LANGUAGES = Object.freeze(['fr', 'en']);

/**
 * An article id or provision as one path segment, as the platform escapes it (.NET's `Uri.EscapeDataString`, RFC 3986's
 * unreserved characters only): `encodeURIComponent` leaves `!'()*` as they are, and the platform does not.
 */
export function escapeSegment(value) {
  return encodeURIComponent(value).replace(/[!'()*]/g, (character) => `%${character.charCodeAt(0).toString(16).toUpperCase()}`);
}

/** A Luxembourg state's permalink, as the API mints it: the work, the date, the state's digest and its language. */
export function luxembourgPermalink({ workKey, date, stateSha256, language }) {
  return `/lu-legilux/${workKey}/${date}--${stateSha256}/${language}`;
}

/** An EU wording's permalink, as the API mints it: the CELEX, the wording date, the wording digest and its language. */
export function europePermalink({ celex, date, wordingSha256, language }) {
  return `/eu-eurlex/${europeWorkSegment(celex)}/${date}--${wordingSha256}/${language}`;
}

/** An article's (or provision's) permalink, as the API mints it: its state's (or wording's), and the id as a segment. */
export function articlePermalink(permalink, publisherId) {
  return `${permalink}/${escapeSegment(publisherId)}`;
}

/**
 * The readable address of an act's current version in a text's language (R67), and of one of its articles when `publisherId`
 * is given: `/lu-legilux/{work}/{lng}[/{art}]` for a Luxembourg work key, `/eu-eurlex/{celex}/{lng}[/{provision}]` for an EU
 * act, the article escaped as a permalink's is.
 */
export function currentAddress({ publisher, work, language, publisherId = null }) {
  const act = publisher === 'eu-eurlex' ? `/eu-eurlex/${europeWorkSegment(work)}/${language}` : `/lu-legilux/${work}/${language}`;
  return publisherId === null ? act : articlePermalink(act, publisherId);
}

/** A header value's optional whitespace taken off its ends: HTTP's own, space and tab, as `V3WebRoot` takes it off. */
const trimmed = (text) => text.replace(/^[ \t]+|[ \t]+$/g, '');

/**
 * The interface language a reader starts in (R61): of French and English, the one the browser's `Accept-Language` prefers
 * (the highest weight, the first listed on a tie; a weight of 0 refuses), else French. Only the primary subtag counts, so
 * `fr-LU` is French and `en-GB` English; any other language counts for neither. Only spaces and tabs are whitespace, as
 * HTTP says (review of #958: JavaScript's and .NET's own trims disagree on U+0085 and U+FEFF).
 */
export function negotiateLanguage(acceptLanguage) {
  let best = null;
  let bestWeight = 0;
  for (const part of String(acceptLanguage ?? '').split(',')) {
    const [range, ...parameters] = trimmed(part).split(';');
    const primary = trimmed(range).toLowerCase().split('-')[0];
    if (!INTERFACE_LANGUAGES.includes(primary)) continue;
    let weight = 1;
    for (const parameter of parameters) {
      const [name, value] = trimmed(parameter).split('=');
      if (trimmed(name ?? '').toLowerCase() === 'q') weight = /^(?:0(?:\.\d{0,3})?|1(?:\.0{0,3})?)$/.test(trimmed(value ?? '')) ? Number(trimmed(value)) : 0;
    }
    if (weight > bestWeight) {
      best = primary;
      bestWeight = weight;
    }
  }
  return best ?? INTERFACE_LANGUAGES[0];
}

/** A path with the interface language it stands under taken off: `{ locale, path }`, `locale` null when it names none. */
function withoutLocale(pathname) {
  for (const prefix of INTERFACE_LANGUAGES) {
    if (pathname.startsWith(`/${prefix}/`)) return { locale: prefix, path: pathname.slice(prefix.length + 1) };
  }
  return { locale: null, path: pathname };
}

/**
 * What a site path names, or null: `{ kind: 'work' | 'state' | 'current', publisher, identifier, locale, date?, digest?,
 * language?, anchor?, permalink?, work?, act?, address? }`. `locale` is the interface language the path stands under, or null for
 * an address with none (as the API mints it). `identifier` is what the API is asked with: a Luxembourg work's coordinate
 * (`/lu-legilux/{work}`), or an EU act's CELEX (the EU index resolves a CELEX, a work or expression IRI, not
 * `/eu-eurlex/{celex}`). For a state (or an EU wording): `language` is the text's, `anchor` the article's segment as the
 * path writes it (escaped; `anchorText` reads it) or null, `permalink` the state's permalink and `address` the whole
 * address, the article's included, both without the interface language. For an act's readable address (`current`, R67):
 * `language`, `anchor` and `address` as for a state, `work` the work key or CELEX, and `act` the act's readable address in
 * that language, with no article. The fragment (`#…`) is not part of a path.
 */
export function parseSitePath(pathname) {
  if (typeof pathname !== 'string') return null;
  const { locale, path } = withoutLocale(pathname);
  for (const [publisher, pattern] of [['lu-legilux', PERMALINK_PATTERNS.luxembourgState], ['eu-eurlex', PERMALINK_PATTERNS.europeWording]]) {
    const match = pattern.exec(path);
    if (match === null) continue;
    const [, work, date, digest, language, anchor = null] = match;
    return Object.freeze({
      kind: 'state',
      publisher,
      identifier: publisher === 'lu-legilux' ? `/lu-legilux/${work}` : europeCelexOfSegment(work),
      date,
      digest,
      language,
      anchor,
      locale,
      permalink: anchor === null ? path : path.slice(0, path.length - anchor.length - 1),
      address: path,
    });
  }
  for (const [publisher, pattern] of [['lu-legilux', PERMALINK_PATTERNS.luxembourgCurrent], ['eu-eurlex', PERMALINK_PATTERNS.europeCurrent]]) {
    const match = pattern.exec(path);
    if (match === null) continue;
    const [, segment, language, anchor = null] = match;
    const work = publisher === 'eu-eurlex' ? europeCelexOfSegment(segment) : segment;
    // A consolidated version's readable address names no page here: it has moved to its act's (`movedFor`).
    if (publisher === 'eu-eurlex' && CONSOLIDATED_CELEX.test(work)) return null;
    return Object.freeze({
      kind: 'current',
      publisher,
      work,
      identifier: publisher === 'lu-legilux' ? `/lu-legilux/${work}` : work,
      language,
      anchor,
      locale,
      act: anchor === null ? path : path.slice(0, path.length - anchor.length - 1),
      address: path,
    });
  }
  if (PERMALINK_PATTERNS.luxembourgWork.test(path)) return Object.freeze({ kind: 'work', publisher: 'lu-legilux', identifier: path, locale });
  const match = PERMALINK_PATTERNS.europeWork.exec(path);
  if (match) return Object.freeze({ kind: 'work', publisher: 'eu-eurlex', identifier: europeCelexOfSegment(match[1]), locale });
  return null;
}

/**
 * The page a site path under an interface language is served by: a state's reading or a work's dossier, under that
 * language (`fr/reading.html`, `en/dossier.html`); null for any other path, an address with no language included (it is
 * redirected, `redirectFor`).
 */
export function pageForSitePath(pathname) {
  const named = parseSitePath(pathname);
  if (named === null || named.locale === null) return null;
  return `${named.locale}/${named.kind === 'work' ? 'dossier.html' : 'reading.html'}`;
}

/** The greatest page number a reading's request may name (the API's request schema). */
export const MAXIMUM_PAGE = 100000;

/**
 * The page a reading's address names in its query (`?page=N`, its first `page` parameter; the plan's item 3.7): N a whole
 * number from 2 to `MAXIMUM_PAGE` written without leading zeros, else null (page 1 is the address with no page). Anything
 * else in the query is ignored, as before. `V3WebRoot.PageOfQuery` reads it the same way.
 */
export function pageOfQuery(search) {
  if (typeof search !== 'string' || search.length < 2 || search[0] !== '?') return null;
  for (const part of search.slice(1).split('&')) {
    const equals = part.indexOf('=');
    const name = equals < 0 ? part : part.slice(0, equals);
    if (name !== 'page') continue;
    const value = equals < 0 ? '' : part.slice(equals + 1);
    if (!/^[1-9][0-9]{0,5}$/.test(value)) return null;
    const number = Number(value);
    return number >= 2 && number <= MAXIMUM_PAGE ? number : null;
  }
  return null;
}

/**
 * The date a readable address names in its query (`?date=yyyy-mm-dd`, its first `date` parameter; the plan's item 3.4, R13:
 * today is written into the address, so a link keeps the date it was read at): a calendar date, else null (the address
 * then reads today). Anything else in the query is ignored. A permalink names its date in its path, so this is read at a
 * readable address only. `V3WebRoot.DateOfQuery` reads it the same way.
 */
export function dateOfQuery(search) {
  if (typeof search !== 'string' || search.length < 2 || search[0] !== '?') return null;
  for (const part of search.slice(1).split('&')) {
    const equals = part.indexOf('=');
    const name = equals < 0 ? part : part.slice(0, equals);
    if (name !== 'date') continue;
    const value = equals < 0 ? '' : part.slice(equals + 1);
    return isCalendarDate(value) ? value : null;
  }
  return null;
}

/**
 * A query with the date a reading was read at written into it (`date=yyyy-mm-dd`, last), every `date` parameter it held
 * taken out first and every other part kept as it was: what a readable address's page writes into its own address once it
 * has read the act (R13). A date and digits only, never a word.
 */
export function withDateQuery(search, date) {
  if (!isCalendarDate(date)) throw new Error(`${JSON.stringify(date)} is not a calendar date`);
  const parts = typeof search === 'string' && search.length > 1 && search[0] === '?'
    ? search.slice(1).split('&').filter((part) => part !== '' && (part.indexOf('=') < 0 ? part : part.slice(0, part.indexOf('='))) !== 'date')
    : [];
  return `?${[...parts, `date=${date}`].join('&')}`;
}

/**
 * The address of one page of a reading at an address that keeps a date (`base`, a readable address with no article): `?page=N`
 * after the first, and the date it was read at, so a page link keeps the version the page shows. With no date, the page alone.
 */
export function readingPageAddress(base, number, date = null) {
  const page = Number.isInteger(number) && number > 1 ? `page=${number}` : null;
  const parts = [page, isCalendarDate(date) ? `date=${date}` : null].filter((part) => part !== null);
  return parts.length === 0 ? base : `${base}?${parts.join('&')}`;
}

/**
 * What the server renders a law's address under an interface language from (R67): the page (`reading`, `dossier`), the
 * operation and the parameters the page itself would ask (`readingRequestFor`), so the page the server renders and the page
 * a reader asks show one answer. A reading is read one page at a time (the plan's item 3.7): the address's article asks the
 * page that holds it, and with no article, the query's `page=N` (`search`, with its `?`) asks that page. A permalink's reading asks by the permalink itself (R64: the version it pins, or why
 * not), with the date and the text's language it names; a readable address's reading asks by the work (a Luxembourg
 * coordinate or an EU CELEX), the date its query names (`date=yyyy-mm-dd`, the plan's item 3.4) or else `today`
 * (Luxembourg's date, R13, which the server's clock gives), and the text's language it names, and with neither it is not
 * rendered (the page as built asks for itself); a dossier asks by the work. An
 * interface language's home page (`/en/`, `/en/index.html`) is rendered from `coverage`, asked with no parameters, as the
 * page as built asks it once loaded (`live-home.mjs`). Null for any other path.
 */
export function sitePageFor(pathname, { today = null, search = '' } = {}) {
  const home = INTERFACE_LANGUAGES.find((language) => pathname === `/${language}/` || pathname === `/${language}/index.html`);
  if (home !== undefined) return Object.freeze({ language: home, kind: 'home', operation: 'coverage', parameters: Object.freeze({}) });
  const named = parseSitePath(pathname);
  if (named === null || named.locale === null) return null;
  if (named.kind === 'work') {
    return Object.freeze({ language: named.locale, kind: 'dossier', operation: 'dossier', parameters: Object.freeze({ identifier: named.identifier }) });
  }
  const parameters = readingRequestFor(named, dateOfQuery(search) ?? today, pageOfQuery(search));
  if (parameters === null) return null;
  return Object.freeze({ language: named.locale, kind: 'reading', operation: 'evidence_bundle', parameters });
}

/**
 * The request a reading at a law's address sends (`parseSitePath`'s answer): at a permalink, the permalink with no article,
 * its date and its language; at a readable address, the work, `today` (the date its query names, else today in Luxembourg)
 * and the language, or null with no `today`. Null for
 * a work's address. The page asked (the plan's item 3.7): the article the address names (`article`, decoded; a segment that
 * does not decode names none), so the page that holds the cited article is served; else `page` (2 or more), the page the
 * address's query names; else neither, the first page.
 */
export function readingRequestFor(named, today = null, page = null) {
  let base;
  if (named?.kind === 'state') base = { identifier: named.permalink, date: named.date, language: named.language };
  else if (named?.kind !== 'current' || typeof today !== 'string' || !/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(today)) return null;
  else base = { identifier: named.identifier, date: today, language: named.language };
  const article = anchorText(named.anchor);
  if (named.anchor !== null) return Object.freeze(article === null ? base : { ...base, article });
  return Object.freeze(Number.isInteger(page) && page >= 2 ? { ...base, page } : base);
}

/**
 * Where a path with no interface language is sent, for a reader whose browser says `acceptLanguage`: the root to that
 * language's home page (`/fr/`), and an address for law to the same address under that language
 * (`/fr/lu-legilux/{work}`); null for any other path. The query string goes along unchanged, from the root too.
 */
export function redirectFor(pathname, search, acceptLanguage) {
  if (typeof pathname !== 'string') return null;
  const language = negotiateLanguage(acceptLanguage);
  if (pathname === '/' || pathname === '/index.html') return `/${language}/${search ?? ''}`;
  if (INTERFACE_LANGUAGES.includes(pathname.slice(1))) return `${pathname}/${search ?? ''}`;
  const named = parseSitePath(pathname);
  if (named === null || named.locale !== null) return null;
  return `/${language}${pathname}${search ?? ''}`;
}

/**
 * Where an address has moved for good, computed from it alone: an EU wording's permalink of the shape before R64,
 * `/eu-eurlex/{celex}/{lng}/{date}--{sha256}`, to `/eu-eurlex/{celex}/{date}--{sha256}/{lng}` (a browser keeps the
 * `#provision` it carried, which the reading page still brings into view); and a consolidated version's readable address,
 * `/eu-eurlex/0{act}-{yyyymmdd}/{lng}[/{provision}]`, to its act's, `/eu-eurlex/3{act}/{lng}[/{provision}]` (review of
 * #972). Each under the interface language it stood under, if any, the query along. Null for any other path, a Luxembourg
 * state's permalink before R64 included: it names no language, so its new address cannot be computed from it alone.
 */
export function movedFor(pathname, search) {
  if (typeof pathname !== 'string') return null;
  const { locale, path } = withoutLocale(pathname);
  const prefix = locale === null ? '' : `/${locale}`;
  const before = EUROPE_WORDING_BEFORE_R64.exec(path);
  if (before !== null) {
    const [, celex, language, date, wordingSha256] = before;
    return `${prefix}${europePermalink({ celex, date, wordingSha256, language })}${search ?? ''}`;
  }
  const consolidated = EUROPE_CONSOLIDATED_CURRENT.exec(path);
  if (consolidated !== null) {
    const [, act, language, anchor = null] = consolidated;
    return `${prefix}/eu-eurlex/3${act}/${language}${anchor === null ? '' : `/${anchor}`}${search ?? ''}`;
  }
  return null;
}

/**
 * The page at `pathname` in another interface language: the same path under that language's prefix (`/fr/search.html` and
 * `/en/search.html`; `/fr/lu-legilux/{work}` and `/en/lu-legilux/{work}`), or that language's home page when the path
 * stands under no language. The language menu leads there once a page has loaded (`client.jsx`).
 */
export function sameDocumentIn(language, pathname) {
  const match = /^\/(?:en|fr)(\/.*)$/.exec(pathname ?? '');
  return match === null ? `/${language}/` : `/${language}${match[1]}`;
}

/**
 * Where the language menu leads once a page has loaded (`client.jsx`, and again whenever the page writes its own address):
 * the same page in `language` (`sameDocumentIn`), with what the site's own address keeps of its query, read and written
 * again so nothing else in the query is carried: at a reading's address, the page it names (`?page=N`) and, at a readable
 * address, the date it keeps (`?date=`, the plan's item 3.4); and the fragment as it is: a position inside an article, or the
 * search a search page keeps there (R109), which no server receives.
 */
export function sameAddressIn(language, { pathname = '', search = '', hash = '' } = {}) {
  const named = parseSitePath(pathname);
  const page = named?.kind === 'state' || named?.kind === 'current' ? pageOfQuery(search) : null;
  const date = named?.kind === 'current' ? dateOfQuery(search) : null;
  return `${readingPageAddress(sameDocumentIn(language, pathname), page, date)}${hash ?? ''}`;
}

/**
 * The links a page at a law's address carries in its head (R61, R64, R67), for `lawPath`, the address it names as its own
 * with no interface language, on a page in the interface language `locale`: the canonical link to that address in the
 * page's own language, a page that answers itself (the plan's item 3.6, revisiting #967's canonical with no interface
 * language: that address answers a redirect by the reader's language, so it is no page of its own, and a sitemap lists only
 * canonical pages), and the address in each interface language, with the address itself for any other (`x-default`, which
 * sends each reader to their language), all under `origin`. The permalink Lex prints and cites still names no interface
 * language (R64): a citation is not a canonical link.
 */
export function lawPageLinks(lawPath, origin, locale) {
  if (!INTERFACE_LANGUAGES.includes(locale)) throw new Error(`a law's page stands under an interface language, not ${JSON.stringify(locale)}`);
  return Object.freeze({
    canonical: `${origin}/${locale}${lawPath}`,
    alternates: Object.freeze([
      ...INTERFACE_LANGUAGES.map((language) => Object.freeze({ hrefLang: language, href: `${origin}/${language}${lawPath}` })),
      Object.freeze({ hrefLang: 'x-default', href: `${origin}${lawPath}` }),
    ]),
  });
}

/** Whether a permalink (with or without `#…`) is an address this site serves a page at. */
export function isSiteAddress(permalink) {
  if (typeof permalink !== 'string') return false;
  const hash = permalink.indexOf('#');
  return parseSitePath(hash < 0 ? permalink : permalink.slice(0, hash)) !== null;
}

/**
 * A permalink the API minted (`/lu-legilux/…`, `/eu-eurlex/…`, with or without `#…`) as a link on a page in `locale`:
 * under that language's path. Anything else is returned unchanged, so a link is never built to an address this site does
 * not serve.
 */
export function permalinkHref(permalink, locale) {
  if (!isSiteAddress(permalink) || !INTERFACE_LANGUAGES.includes(locale)) return permalink;
  const hash = permalink.indexOf('#');
  // An address already under a language keeps it: the API mints none, and a page links what it was handed.
  if (parseSitePath(hash < 0 ? permalink : permalink.slice(0, hash)).locale !== null) return permalink;
  return `/${locale}${permalink}`;
}

/** An article's segment as a reading's address writes it (`parseSitePath`), decoded, or null. */
export function anchorText(segment) {
  if (typeof segment !== 'string' || segment.length === 0) return null;
  try {
    return decodeURIComponent(segment);
  } catch {
    return null;
  }
}

/**
 * The article a reading's address names after `#`, decoded, or null: an address without an article segment that a
 * browser kept a `#provision` on across the move of a permalink of the shape before R64 (`movedFor`).
 */
export function anchorOf(hash) {
  if (typeof hash !== 'string' || hash.length < 2 || hash[0] !== '#') return null;
  return anchorText(hash.slice(1));
}
