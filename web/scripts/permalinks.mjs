// The site's own addresses: its pages under each interface language, and its addresses for law, a work's dossier and a
// state's (or an EU wording's) reading, the permalinks the API mints (UX spec section 1, URLs; the launch contract's first
// promise: a permalink pins work, article, date, language and digest).
//
// One permalink shape (the specification of record's R64): /{publisher}/{work}/{date}--{digest}/{language}/{anchor}.
//
//   /lu-legilux/{work}                                              a Luxembourg work's dossier
//   /lu-legilux/{work}/{yyyy-mm-dd}--{state sha256}/{lng}[/{art}]   a Luxembourg state's reading, at an article
//   /eu-eurlex/{celex}                                              an EU act's dossier
//   /eu-eurlex/{celex}/{yyyy-mm-dd}--{sha256}/{lng}[/{provision}]   an EU wording's reading, at a provision
//
// `{lng}` is the text's language, the publisher's three letters (`fra`, `deu`, `eng`), and the digest is that language's:
// a Luxembourg state is one expression in one language, and an EU wording one expression too. The article (or provision)
// is a path segment, escaped as the platform escapes it (`escapeSegment`); "#" is left for a position inside an article.
// The interface language never enters a permalink.
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

/**
 * A Luxembourg work key as the API takes it (`IsWorkKey`): lower-case letters, digits, hyphen and underscore, here starting
 * with a letter or digit, as every key the publisher's catalogue yields does. A key the API refuses opens no page.
 */
const WORK = '[a-z0-9][a-z0-9_-]*';
/** A CELEX number as the API mints it (sector, year, type, number, and an optional suffix such as (01) or -20160504). */
const CELEX = '[0-9A-Za-z][0-9A-Za-z()._-]*';
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

export const PERMALINK_PATTERNS = Object.freeze({
  luxembourgWork: new RegExp(`^/lu-legilux/(${WORK})$`),
  luxembourgState: new RegExp(`^/lu-legilux/(${WORK})/${PIN}/${LANGUAGE}(?:/${ANCHOR})?$`),
  europeWork: new RegExp(`^/eu-eurlex/(${CELEX})$`),
  europeWording: new RegExp(`^/eu-eurlex/(${CELEX})/${PIN}/${LANGUAGE}(?:/${ANCHOR})?$`),
});

/** An EU wording's permalink in the shape before R64, `/eu-eurlex/{celex}/{lng}/{date}--{sha256}`, moved to R64's. */
const EUROPE_WORDING_BEFORE_R64 = new RegExp(`^/eu-eurlex/(${CELEX})/${LANGUAGE}/${PIN}$`);

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
  return `/eu-eurlex/${celex}/${date}--${wordingSha256}/${language}`;
}

/** An article's (or provision's) permalink, as the API mints it: its state's (or wording's), and the id as a segment. */
export function articlePermalink(permalink, publisherId) {
  return `${permalink}/${escapeSegment(publisherId)}`;
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
 * What a site path names, or null: `{ kind: 'work' | 'state', publisher, identifier, locale, date?, digest?, language?,
 * anchor?, permalink?, address? }`. `locale` is the interface language the path stands under, or null for an address with
 * none (as the API mints it). `identifier` is what the API is asked with: a Luxembourg work's coordinate
 * (`/lu-legilux/{work}`), or an EU act's CELEX (the EU index resolves a CELEX, a work or expression IRI, not
 * `/eu-eurlex/{celex}`). For a state (or an EU wording): `language` is the text's, `anchor` the article's segment as the
 * path writes it (escaped; `anchorText` reads it) or null, `permalink` the state's permalink and `address` the whole
 * address, the article's included, both without the interface language. The fragment (`#…`) is not part of a path.
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
      identifier: publisher === 'lu-legilux' ? `/lu-legilux/${work}` : work,
      date,
      digest,
      language,
      anchor,
      locale,
      permalink: anchor === null ? path : path.slice(0, path.length - anchor.length - 1),
      address: path,
    });
  }
  if (PERMALINK_PATTERNS.luxembourgWork.test(path)) return Object.freeze({ kind: 'work', publisher: 'lu-legilux', identifier: path, locale });
  const match = PERMALINK_PATTERNS.europeWork.exec(path);
  if (match) return Object.freeze({ kind: 'work', publisher: 'eu-eurlex', identifier: match[1], locale });
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
  return `${named.locale}/${named.kind === 'state' ? 'reading.html' : 'dossier.html'}`;
}

/**
 * What the server renders a law's address under an interface language from (R67): the page (`reading`, `dossier`), the
 * operation and the parameters the page itself would ask, exactly as its form sends them (a reading asks by the work (a
 * Luxembourg coordinate or an EU CELEX), the date and the text's language the permalink names; a dossier by the work), so
 * the page the server renders and the page a reader asks show one answer. An interface language's home page (`/en/`,
 * `/en/index.html`) is rendered from `coverage`, asked with no parameters, as the page as built asks it once loaded
 * (`live-home.mjs`). Null for any other path.
 */
export function sitePageFor(pathname) {
  const home = INTERFACE_LANGUAGES.find((language) => pathname === `/${language}/` || pathname === `/${language}/index.html`);
  if (home !== undefined) return Object.freeze({ language: home, kind: 'home', operation: 'coverage', parameters: Object.freeze({}) });
  const named = parseSitePath(pathname);
  if (named === null || named.locale === null) return null;
  if (named.kind === 'work') {
    return Object.freeze({ language: named.locale, kind: 'dossier', operation: 'dossier', parameters: Object.freeze({ identifier: named.identifier }) });
  }
  const parameters = { identifier: named.identifier, date: named.date, language: named.language };
  return Object.freeze({ language: named.locale, kind: 'reading', operation: 'evidence_bundle', parameters: Object.freeze(parameters) });
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
 * Where a permalink of the shape before R64 has moved for good: an EU wording's `/eu-eurlex/{celex}/{lng}/{date}--{sha256}`
 * to `/eu-eurlex/{celex}/{date}--{sha256}/{lng}`, under the interface language it stood under, if any (a browser keeps the
 * `#provision` it carried, which the reading page still brings into view). The query goes along. Null for any other path,
 * a Luxembourg state's of that time included: it names no language, so its new address cannot be computed from it alone.
 */
export function movedFor(pathname, search) {
  if (typeof pathname !== 'string') return null;
  const { locale, path } = withoutLocale(pathname);
  const match = EUROPE_WORDING_BEFORE_R64.exec(path);
  if (match === null) return null;
  const [, celex, language, date, wordingSha256] = match;
  return `${locale === null ? '' : `/${locale}`}${europePermalink({ celex, date, wordingSha256, language })}${search ?? ''}`;
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
 * The links a page at a law's address carries in its head (R61, R64, R67), for `lawPath`, the address with no interface
 * language: the canonical link to that address itself (the permalink Lex prints and cites, which sends each reader to
 * their language), and the address in each interface language, with the address itself for any other (`x-default`), all
 * under `origin`.
 */
export function lawPageLinks(lawPath, origin) {
  return Object.freeze({
    canonical: `${origin}${lawPath}`,
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
