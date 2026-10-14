// The site's own addresses for law: a work's dossier and a state's (or an EU wording's) reading, the permalinks the API
// mints (UX spec section 1, URLs; the launch contract's first promise: a permalink pins work, article, date, language and
// digest). Before this, the site printed permalinks and served nothing at them (review of the front door, #955).
//
//   /lu-legilux/{work}                                   a Luxembourg work's dossier
//   /lu-legilux/{work}/{yyyy-mm-dd}--{state sha256}      a Luxembourg state's reading (#{article id} names the article)
//   /eu-eurlex/{celex}                                   an EU act's dossier
//   /eu-eurlex/{celex}/{lng}/{yyyy-mm-dd}--{sha256}      an EU wording's reading (#{provision} names the provision)
//
// Each may stand under /fr/ for the French interface. The fragment never reaches a server: the page reads it.
//
// This module is data and pure functions only: the live pages' bundles, the live server and the tests share it, and the
// API holds the same patterns (V3WebRoot), held to these by a test.

/** A Luxembourg work key as the API mints it: letters, digits, dot, underscore and hyphen, starting with a letter or digit. */
const WORK = '[A-Za-z0-9][A-Za-z0-9._-]*';
/** A CELEX number as the API mints it (sector, year, type, number, and an optional suffix such as (01) or -20160504). */
const CELEX = '[0-9A-Za-z][0-9A-Za-z()._-]*';
const PIN = '([0-9]{4}-[0-9]{2}-[0-9]{2})--([0-9a-f]{64})';

export const PERMALINK_PATTERNS = Object.freeze({
  luxembourgWork: new RegExp(`^/lu-legilux/(${WORK})$`),
  luxembourgState: new RegExp(`^/lu-legilux/(${WORK})/${PIN}$`),
  europeWork: new RegExp(`^/eu-eurlex/(${CELEX})$`),
  europeWording: new RegExp(`^/eu-eurlex/(${CELEX})/([a-z]{3})/${PIN}$`),
});

/** The interface languages whose pages stand under their own path (English is at the root). */
const LOCALE_PREFIXES = Object.freeze(['fr']);

/**
 * What a site path names, or null: `{ kind: 'work' | 'state', publisher, identifier, locale, date?, digest?, language?,
 * permalink? }`. `identifier` is what the API is asked with: a Luxembourg work's coordinate (`/lu-legilux/{work}`), or
 * an EU act's CELEX (the EU index resolves a CELEX, a work or expression IRI, not `/eu-eurlex/{celex}`); `permalink` is
 * the pinned address without its locale prefix. The fragment (`#article`) is not part of a path.
 */
export function parseSitePath(pathname) {
  if (typeof pathname !== 'string') return null;
  let locale = 'en';
  let path = pathname;
  for (const prefix of LOCALE_PREFIXES) {
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

/** The page a site path is served by: a state's reading or a work's dossier, under the path's interface language. */
export function pageForSitePath(pathname) {
  const named = parseSitePath(pathname);
  if (named === null) return null;
  const page = named.kind === 'state' ? 'reading.html' : 'dossier.html';
  return named.locale === 'en' ? page : `${named.locale}/${page}`;
}

/** Whether a permalink (with or without `#…`) is an address this site serves a page at. */
export function isSiteAddress(permalink) {
  if (typeof permalink !== 'string') return false;
  const hash = permalink.indexOf('#');
  return parseSitePath(hash < 0 ? permalink : permalink.slice(0, hash)) !== null;
}

/**
 * A permalink the API minted (`/lu-legilux/…`, `/eu-eurlex/…`, with or without `#…`) as a link on a page in `locale`:
 * under the locale's path for any interface but English. Anything else is returned unchanged, so a link is never built
 * to an address this site does not serve.
 */
export function permalinkHref(permalink, locale = 'en') {
  if (!isSiteAddress(permalink)) return permalink;
  return locale === 'en' ? permalink : `/${locale}${permalink}`;
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
