// The document shell, as the one component every V3 page passes through.
//
// This is the React form of what `scripts/render.mjs` did as a string. The rules it carries
// are not presentational and did not survive the port by accident; each one is here because
// leaving it out produced a wrong page at some point:
//
//   - `lang` is the page's own language, never the subject's. A work page is English chrome
//     about a French law and stays `en`. A page of French statute is `fr`.
//   - A page labelled in one locale whose copy is written in another is refused outright.
//     Being one of the reviewed locales is not the same as being the language the copy is
//     actually in, and only the second is what the tag asserts.
//   - The shell rides on the root element as data attributes and nowhere else, so a
//     stylesheet can select on them and no render path can branch on them.
//   - Asset hrefs are root-absolute. Resolved against the page's own path, the same page
//     served at /w/<work>/<version> loads a stylesheet that is not there.

import { CHROME_LOCALES } from '../scripts/localization.mjs';
import { cspValue } from '../scripts/csp.mjs';
import { LocaleNav, localeHome } from './LocaleNav.jsx';
import { CHROME_LOCALE, fillText, liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { INTERFACE_LANGUAGES, lawPageLinks } from '../scripts/permalinks.mjs';
import { CANONICAL_HOST } from '../scripts/routes.mjs';

/** The marker that says, in the DOM, that nothing on this page is law. */
export const SYNTHETIC_MARKER = 'lex-v3-synthetic-preview';

/**
 * The banner every preview page carries.
 *
 * It lives in the shell rather than in each page because a page that builds its own head
 * forgets it. The trust surface did exactly that, and the browser run caught it.
 */
export function SyntheticBanner() {
  return (
    <aside className="synthetic" role="note" data-synthetic={SYNTHETIC_MARKER}>
      <strong>Synthetic preview.</strong> This page is generated from a synthetic fixture. It is
      not law, not promotable, and describes no real legal record.
    </aside>
  );
}

/** The marker that says, in the DOM, that this page shows what the server it came from answers. */
export const LIVE_MARKER = 'lex-v3-live-mount';

/**
 * The site's menu: each entry's copy key, its page, and the page that is it (the home page is the search screen, UX
 * spec section 2). Every screen is in it: until search hits and dossier rows link into a work's own screens, a reader
 * reaches reading, the dossier, history and compare from here (review of #955). The search entry is the home page, so it
 * is current only there; search.html is the same screen under its own title.
 */
const MENU = Object.freeze([
  Object.freeze({ key: 'search', page: 'index.html', states: Object.freeze(['live-home']) }),
  Object.freeze({ key: 'reading', page: 'reading.html', states: Object.freeze(['live-reading']) }),
  Object.freeze({ key: 'dossier', page: 'dossier.html', states: Object.freeze(['live-dossier']) }),
  Object.freeze({ key: 'history', page: 'history.html', states: Object.freeze(['live-history']) }),
  Object.freeze({ key: 'compare', page: 'compare.html', states: Object.freeze(['live-compare']) }),
  Object.freeze({ key: 'radar', page: 'radar.html', states: Object.freeze(['live-radar']) }),
  Object.freeze({ key: 'export', page: 'export.html', states: Object.freeze(['live-export']) }),
  Object.freeze({ key: 'coverage', page: 'coverage.html', states: Object.freeze(['live-coverage']) }),
]);

/** The origin the site is published at, which the alternate-language links name (search engines want them whole). */
export const SITE_ORIGIN = `https://${CANONICAL_HOST}`;

/**
 * The page each live page is in every interface language, by its state, for the alternate-language links (R61, hreflang).
 * The reading and dossier pages are absent: either may be standing at a law's address, which its own path names, and a
 * link to the page's file in the other language would name a different page.
 */
const ALTERNATE_PAGES = Object.freeze({
  'live-home': '',
  'live-search': 'search.html',
  'live-history': 'history.html',
  'live-compare': 'compare.html',
  'live-radar': 'radar.html',
  'live-export': 'export.html',
  'live-coverage': 'coverage.html',
});

/**
 * The alternate-language links of a live page: the same page in each interface language, and for a reader of any other
 * language (`x-default`) the page a reader with no preference gets: the home page's root, which sends each reader to their
 * language, and for any other page that page in French (`negotiateLanguage`'s default; review of #958: the root named a
 * different page). A page the server rendered at a law's address (`lawPath`, the address with no language, as the API
 * mints it) names that address in each language, and for any other the address itself, which sends a reader to theirs
 * (`lawPageLinks`, which the page as built writes once loaded, `client.jsx`).
 */
export function alternateLinks(state, lawPath = null) {
  if (lawPath !== null) return lawPageLinks(lawPath, SITE_ORIGIN).alternates;
  const page = ALTERNATE_PAGES[state];
  if (page === undefined) return [];
  return [
    ...INTERFACE_LANGUAGES.map((language) => ({ hrefLang: language, href: `${SITE_ORIGIN}/${language}/${page}` })),
    { hrefLang: 'x-default', href: page === '' ? `${SITE_ORIGIN}/` : `${SITE_ORIGIN}/${INTERFACE_LANGUAGES[0]}/${page}` },
  ];
}

/**
 * The header every live page carries: the site's name (to the home page of the page's language), its menu
 * with the current screen marked, and the interface languages. It carries the live marker the development
 * build's banner carried, which says what a live page is (it asks the server it came from) and is how the
 * pages and their tests tell a live page from a preview.
 */
export function SiteHeader({ state, locale }) {
  const site = liveChrome().site;
  return (
    <header className="site-header" data-live={LIVE_MARKER}>
      {/* First on the page, so a keyboard reader passes the menu in one key press (WCAG 2.4.1 bypass blocks). */}
      <a className="skip-link" href="#main">{site.skip}</a>
      <a className="site-brand" href={localeHome(CHROME_LOCALE)}>
        <span className="site-name">{site.brand}</span>
        <span className="site-tagline">{site.tagline}</span>
      </a>
      <nav aria-label={site.menu} className="site-nav">
        <ul>
          {MENU.map((entry) => (
            <li key={entry.key}>
              <a href={entry.page === 'index.html' ? localeHome(CHROME_LOCALE) : livePath(entry.page)} aria-current={entry.states.includes(state) ? 'page' : undefined}>
                {site[entry.key]}
              </a>
            </li>
          ))}
        </ul>
      </nav>
      <LocaleNav current={locale} />
    </header>
  );
}

/**
 * The footer every live page carries: what the site is not (advice), which texts are authentic, and where the
 * texts come from, then what it holds. The development build's banner said "not a release and not legal
 * advice"; the first half was the build's and goes with it, the second is the product's and stays here.
 */
export function SiteFooter() {
  const site = liveChrome().site;
  return (
    <footer className="site-footer">
      <p>{site.advice}</p>
      <p>{site.sources}</p>
      <p>
        <a href={livePath('coverage.html')}>{site.coverage}</a>
      </p>
    </footer>
  );
}

/**
 * The full document.
 *
 * @param {object} props
 * @param {string} props.state       what this page is, exposed as data-preview-state
 * @param {string} props.title       plain text; this component escapes it, callers must not
 * @param {string} [props.locale]    the language this page is labelled as
 * @param {string} [props.copyLocale] the language the copy is actually written in
 * @param {string|null} [props.shell] which skin, or null
 * @param {string|null} [props.density]
 * @param {'synthetic'|'live'} [props.banner] which banner the page carries; synthetic unless the
 *   page asks the mounted API, whose answers the synthetic banner would misdescribe
 * @param {React.ReactNode} props.children the page body, as components
 */
export function Document({
  state,
  title,
  banner = 'synthetic',
  // A live page is labelled, and written, in the language its bundle was built for (`CHROME_LOCALE`);
  // a preview page in English. Each still names itself, so the guard below can tell them apart.
  locale = banner === 'live' ? CHROME_LOCALE : 'en',
  copyLocale = banner === 'live' ? CHROME_LOCALE : 'en',
  shell = null,
  density = null,
  // The address of the law a page the server rendered stands at, with no language (`/lu-legilux/{work}/...`), or null.
  lawPath = null,
  children,
}) {
  // Each axis names itself. Both guards used to raise the same sentence, so a test feeding an
  // unreviewed value to both could not tell which fired, and the first was unprovable: removing
  // it left the second producing byte-identical output. Found by mutating this file.
  if (!CHROME_LOCALES.includes(locale)) {
    throw new Error(
      `the page locale ${JSON.stringify(locale)} is not one of the reviewed chrome locales`,
    );
  }
  if (!CHROME_LOCALES.includes(copyLocale)) {
    throw new Error(
      `the copy locale ${JSON.stringify(copyLocale)} is not one of the reviewed chrome ` +
        'locales',
    );
  }
  if (locale !== copyLocale) {
    throw new Error(
      `this page would be labelled ${locale} while its copy is written in ${copyLocale}; a ` +
        'screen reader would read one language in the voice of another, and a reader would ' +
        'have been served a locale nobody reviewed',
    );
  }
  if (typeof state !== 'string' || state.length === 0) {
    throw new Error('a page says what it is; data-preview-state is not optional');
  }
  if (typeof title !== 'string' || title.length === 0) {
    throw new Error('a page carries a title');
  }
  if (banner !== 'synthetic' && banner !== 'live') {
    throw new Error(`a page carries the synthetic or the live banner, not ${JSON.stringify(banner)}`);
  }

  const shellAttributes =
    shell === null ? {} : { 'data-shell': shell, 'data-density': density ?? '' };

  return (
    <html lang={locale} data-product-line="lex-v3" data-preview-state={state} {...shellAttributes}>
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        {/* Enumerated in csp.mjs and asserted by the browser run against that exact
            object. A hydrated client ships scripts, so "no scripts" stops being evidence
            of anything; what has to hold is that nothing executes which was not reviewed
            and served from this origin. */}
        <meta httpEquiv="Content-Security-Policy" content={cspValue()} />
        <title>{banner === 'live' ? fillText(liveChrome().shell.title, { title }) : `${title} - Lex V3 preview`}</title>
        <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
        <link rel="stylesheet" href="/styles.css" />
        {/* A page at a law's address names as canonical the address with no interface language, the permalink Lex prints
            and cites (R64, R67), which sends each reader to their language; each language's page is an alternate. */}
        {banner === 'live' && lawPath !== null ? <link rel="canonical" href={lawPageLinks(lawPath, SITE_ORIGIN).canonical} /> : null}
        {banner === 'live'
          ? alternateLinks(state, lawPath).map((link) => <link key={link.hrefLang} rel="alternate" hrefLang={link.hrefLang} href={link.href} />)
          : null}
      </head>
      <body>
        {/* A live page sits in the site (its header, with the interface languages, an unreviewed one answering
            localization_unavailable, and its footer); a preview page under the synthetic banner. */}
        {banner === 'live' ? <SiteHeader state={state} locale={locale} /> : <SyntheticBanner />}
        <main id="main">{children}</main>
        {banner === 'live' ? <SiteFooter /> : null}
      </body>
    </html>
  );
}
