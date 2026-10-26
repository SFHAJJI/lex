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
import { jsonInScript } from '../scripts/initial-answer.mjs';
import { CANONICAL_HOST } from '../scripts/routes.mjs';
import { LEGAL_PAGES } from '../scripts/legal-pages.mjs';
import { FreshnessStripSlot, StalenessBannerSlot } from './Freshness.jsx';

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
  // The five legal pages (the plan's item 7.1), each the same page in each language.
  ...Object.fromEntries(LEGAL_PAGES.map((page) => [page.state, page.file])),
});

/**
 * The pages the release's switch opens that stand at no law's address, by their state, each naming itself canonical in its
 * interface language: the home page at its language's root (also served at `index.html`), and Trust and Coverage (review of
 * #976, F2: neither named a canonical).
 */
const CANONICAL_PAGES = Object.freeze({ 'live-home': '', 'live-coverage': 'coverage.html' });

/**
 * The canonical link of a live page in `locale`: at a law's address (`lawPath`), the address it shows in that language
 * (`lawPageLinks`); for the home page and Trust and Coverage, their own address in that language; else none.
 */
export function canonicalLink(state, locale, lawPath = null) {
  if (lawPath !== null) return lawPageLinks(lawPath, SITE_ORIGIN, locale).canonical;
  const page = CANONICAL_PAGES[state];
  return page === undefined || !INTERFACE_LANGUAGES.includes(locale) ? null : `${SITE_ORIGIN}/${locale}/${page}`;
}

/**
 * The alternate-language links of a live page: the same page in each interface language, and for a reader of any other
 * language (`x-default`) the page a reader with no preference gets: the home page's root, which sends each reader to their
 * language, and for any other page that page in French (`negotiateLanguage`'s default; review of #958: the root named a
 * different page). A page the server rendered at a law's address (`lawPath`, the address with no language, as the API
 * mints it) names that address in each language, and for any other the address itself, which sends a reader to theirs
 * (`lawPageLinks`, which the page as built writes once loaded, `client.jsx`).
 */
export function alternateLinks(state, lawPath = null) {
  if (lawPath !== null) return lawPageLinks(lawPath, SITE_ORIGIN, CHROME_LOCALE).alternates;
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
export function SiteHeader({ state, locale, localePage = null }) {
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
      <LocaleNav current={locale} page={localePage} />
    </header>
  );
}

/**
 * The footer every live page carries: what Lex is not (the specification of record's R50, word for word, which replaced
 * "Documentation, not legal advice."), which texts are authentic, and where the texts come from, then what it holds, and
 * the five legal pages (the plan's item 7.1: "linked from every footer"), the page that is one of them marked current, and
 * last how current that is and which commits built and serve it (R87, R23; `Freshness.jsx`), from `freshness`, an answer's
 * facts (`freshnessFacts`), or as built. The development build's banner said "not a release and not legal advice"; the
 * first half was the build's and went with it, the second is the product's and is said here.
 */
export function SiteFooter({ state = null, freshness = null }) {
  const copy = liveChrome();
  const site = copy.site;
  return (
    <footer className="site-footer">
      <p data-not-advice="">{site.notAdvice}</p>
      <p>{site.authenticity}</p>
      <p>{site.sources}</p>
      <p>
        <a href={livePath('coverage.html')}>{site.coverage}</a>
      </p>
      <nav aria-label={copy.legal.menu} className="legal-nav" data-legal-nav="">
        <ul>
          {LEGAL_PAGES.map((page) => (
            <li key={page.key}>
              <a href={livePath(page.file)} aria-current={page.state === state ? 'page' : undefined}>{copy[page.key].title}</a>
            </li>
          ))}
        </ul>
      </nav>
      <FreshnessStripSlot facts={freshness} />
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
  // What a live page says to search engines (R67; `search-engines.mjs`): `never`, a page no search engine is asked to index
  // (search, export, the forms, a law's page as built or refused, the 404 and maintenance pages), or `switch`, a page the
  // release's switch opens (the home pages, Trust and Coverage, a law's page rendered whole). Either way the page as rendered
  // is kept out; the server removes the kept-out marker when the switch is on and the page may be indexed.
  indexing = 'never',
  // A law's page's schema.org Legislation markup (`structured-data.mjs`), written as JSON-LD in the head, or null.
  structuredData = null,
  // The page's file under each interface language, for a page that ships no script to point its language links at itself
  // in the other language (the legal pages), or null: the links then lead to each language's home page as built, and the
  // page's script points them at the same page once it has loaded (`client.jsx`).
  localePage = null,
  // What the answer a page was rendered from says of the law's freshness and the commits behind it (`freshnessFacts`, R87
  // and R23), or null for a page as built: the footer's strip, and the staleness banner under the header when it is stale.
  freshness = null,
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
  if (indexing !== 'never' && indexing !== 'switch') {
    throw new Error(`a page is never indexed or opened by the switch, not ${JSON.stringify(indexing)}`);
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
        {/* Kept out of search engines as rendered (R67, O27): `search-engines.mjs` says which marker a server removes. */}
        {banner === 'live'
          ? (indexing === 'switch' ? <meta name="robots" content="noindex" data-search-engines="kept-out" /> : <meta name="robots" content="noindex" />)
          : null}
        <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
        <link rel="stylesheet" href="/styles.css" />
        {/* A page at a law's address names as canonical the address it shows in the page's own interface language, a page
            that answers itself (R67; the plan's item 3.6), and each language's page and the address with no interface
            language (which sends each reader to theirs) as alternates; the home page and Trust and Coverage name their own
            address in the page's language (`canonicalLink`). */}
        {banner === 'live' && canonicalLink(state, locale, lawPath) !== null ? <link rel="canonical" href={canonicalLink(state, locale, lawPath)} /> : null}
        {banner === 'live'
          ? alternateLinks(state, lawPath).map((link) => <link key={link.hrefLang} rel="alternate" hrefLang={link.hrefLang} href={link.href} />)
          : null}
        {/* Data, not a script: no Content-Security-Policy runs it, as the answer's own block (`initial-answer.mjs`). */}
        {banner === 'live' && structuredData !== null
          ? <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonInScript(structuredData) }} />
          : null}
      </head>
      <body>
        {/* A live page sits in the site (its header, with the interface languages, an unreviewed one answering
            localization_unavailable, and its footer); a preview page under the synthetic banner. */}
        {banner === 'live' ? <SiteHeader state={state} locale={locale} localePage={localePage} /> : <SyntheticBanner />}
        {banner === 'live' ? <StalenessBannerSlot facts={freshness} /> : null}
        <main id="main">{children}</main>
        {banner === 'live' ? <SiteFooter state={state} freshness={freshness} /> : null}
      </body>
    </html>
  );
}
