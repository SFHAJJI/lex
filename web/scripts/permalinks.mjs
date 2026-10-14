// The site's own addresses: its pages under each interface language, and its addresses for law, a work's dossier and a
// state's (or an EU wording's) reading, the permalinks the API mints (UX spec section 1, URLs; the launch contract's first
// promise: a permalink pins work, article, date, language and digest).
//
//   /lu-legilux/{work}                                   a Luxembourg work's dossier
//   /lu-legilux/{work}/{yyyy-mm-dd}--{state sha256}      a Luxembourg state's reading (#{article id} names the article)
//   /eu-eurlex/{celex}                                   an EU act's dossier
//   /eu-eurlex/{celex}/{lng}/{yyyy-mm-dd}--{sha256}      an EU wording's reading (#{provision} names the provision)
//
// Every page stands under its interface language, /fr/ or /en/ (the specification of record's R61): /fr/search.html,
// /en/lu-legilux/{work}. An address the API mints names no interface language, so it, and the site's root, are answered
// with a redirect to the reader's language: the one the browser prefers of the two, else French (`negotiateLanguage`).
// The fragment never reaches a server: the page reads it, and a browser keeps it across the redirect.
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
const PIN = '([0-9]{4}-[0-9]{2}-[0-9]{2})--([0-9a-f]{64})';

export const PERMALINK_PATTERNS = Object.freeze({
  luxembourgWork: new RegExp(`^/lu-legilux/(${WORK})$`),
  luxembourgState: new RegExp(`^/lu-legilux/(${WORK})/${PIN}$`),
  europeWork: new RegExp(`^/eu-eurlex/(${CELEX})$`),
  europeWording: new RegExp(`^/eu-eurlex/(${CELEX})/([a-z]{3})/${PIN}$`),
});

/** The interface languages, each under its own path (R61); the first is the one a reader with no preference gets. */
export const INTERFACE_LANGUAGES = Object.freeze(['fr', 'en']);

/**
 * The interface language a reader starts in (R61): of French and English, the one the browser's `Accept-Language` prefers
 * (the highest weight, the first listed on a tie; a weight of 0 refuses), else French. Only the primary subtag counts, so
 * `fr-LU` is French and `en-GB` English; any other language counts for neither.
 */
export function negotiateLanguage(acceptLanguage) {
  let best = null;
  let bestWeight = 0;
  for (const part of String(acceptLanguage ?? '').split(',')) {
    const [range, ...parameters] = part.trim().split(';');
    const primary = range.trim().toLowerCase().split('-')[0];
    if (!INTERFACE_LANGUAGES.includes(primary)) continue;
    let weight = 1;
    for (const parameter of parameters) {
      const [name, value] = parameter.trim().split('=');
      if (name?.trim().toLowerCase() === 'q') weight = /^(?:0(?:\.\d{0,3})?|1(?:\.0{0,3})?)$/.test(value?.trim() ?? '') ? Number(value.trim()) : 0;
    }
    if (weight > bestWeight) {
      best = primary;
      bestWeight = weight;
    }
  }
  return best ?? INTERFACE_LANGUAGES[0];
}

/**
 * What a site path names, or null: `{ kind: 'work' | 'state', publisher, identifier, locale, date?, digest?, language?,
 * permalink? }`. `locale` is the interface language the path stands under, or null for an address with none (as the API
 * mints it). `identifier` is what the API is asked with: a Luxembourg work's coordinate (`/lu-legilux/{work}`), or an EU
 * act's CELEX (the EU index resolves a CELEX, a work or expression IRI, not `/eu-eurlex/{celex}`); `permalink` is the
 * pinned address without its language prefix. The fragment (`#article`) is not part of a path.
 */
export function parseSitePath(pathname) {
  if (typeof pathname !== 'string') return null;
  let locale = null;
  let path = pathname;
  for (const prefix of INTERFACE_LANGUAGES) {
    if (path.startsWith(`/${prefix}/`)) {
      locale = prefix;
      path = path.slice(prefix.length + 1);
      break;
    }
  }
  let match = PERMALINK_PATTERNS.luxembourgState.exec(path);
  if (match) {
    return Object.freeze({ kind: 'state', publisher: 'lu-legilux', identifier: `/lu-legilux/${match[1]}`, date: match[2], digest: match[3], language: null, locale, permalink: path });
  }
  match = PERMALINK_PATTERNS.europeWording.exec(path);
  if (match) {
    return Object.freeze({ kind: 'state', publisher: 'eu-eurlex', identifier: match[1], date: match[3], digest: match[4], language: match[2], locale, permalink: path });
  }
  match = PERMALINK_PATTERNS.luxembourgWork.exec(path);
  if (match) return Object.freeze({ kind: 'work', publisher: 'lu-legilux', identifier: path, locale });
  match = PERMALINK_PATTERNS.europeWork.exec(path);
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
 * Where a path with no interface language is sent, for a reader whose browser says `acceptLanguage`: the root to that
 * language's home page (`/fr/`), and an address for law to the same address under that language
 * (`/fr/lu-legilux/{work}`); null for any other path. The query string goes along unchanged.
 */
export function redirectFor(pathname, search, acceptLanguage) {
  if (typeof pathname !== 'string') return null;
  const language = negotiateLanguage(acceptLanguage);
  if (pathname === '/' || pathname === '/index.html') return `/${language}/`;
  if (INTERFACE_LANGUAGES.includes(pathname.slice(1))) return `${pathname}/${search ?? ''}`;
  const named = parseSitePath(pathname);
  if (named === null || named.locale !== null) return null;
  return `/${language}${pathname}${search ?? ''}`;
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

/** The article a reading's address names after `#`, decoded, or null. */
export function anchorOf(hash) {
  if (typeof hash !== 'string' || hash.length < 2 || hash[0] !== '#') return null;
  try {
    return decodeURIComponent(hash.slice(1));
  } catch {
    return null;
  }
}
