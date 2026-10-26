// The interface-language choice on the live pages.
//
// The launch contract's line is "Chrome in FR and EN; DE and LB answer localization_unavailable".
// Each language is named in itself and tagged with its own `lang`, so a screen reader says
// "Deutsch" as German rather than as an English word. English and French chrome are reviewed
// (`REVIEWED_CHROME_LOCALES`) and each leads to its own pages, under its own path (R61: `/en/`,
// `/fr/`); German and Luxembourgish lead to the page that says they are not, in English and
// labelled English (`live-locale-page.jsx`), never to English copy under another language's tag.
// Written by the build, a link leads to the other language's home page; once the page has loaded,
// `client.jsx` points it at the same page in that language (`sameDocumentIn`, `permalinks.mjs`). A page with no script (the
// legal pages) names its own file, so the link leads to the same page in that language as built.

import { REVIEWED_CHROME_LOCALES } from '../scripts/locale-unavailable.mjs';
import { liveChrome } from '../scripts/live-chrome.mjs';

const LOCALES = Object.freeze([
  Object.freeze({ code: 'en', name: 'English' }),
  Object.freeze({ code: 'fr', name: 'Français' }),
  Object.freeze({ code: 'de', name: 'Deutsch' }),
  Object.freeze({ code: 'lb', name: 'Lëtzebuergesch' }),
]);

/** A language's home page: its own root (`/en/`, `/fr/`), which both servers answer with its index page. */
export function localeHome(code) {
  return `/${code}/`;
}

/**
 * Where a language's link leads: a reviewed language's home page, or `page` in it when the page names its file (a page with
 * no script, which cannot point the link at itself once loaded: the legal pages); an unreviewed language's refusal page.
 */
export function localeHref(code, page = null) {
  if (!REVIEWED_CHROME_LOCALES.includes(code)) return `/locale-${code}.html`;
  return page === null ? localeHome(code) : `/${code}/${page}`;
}

export function LocaleNav({ current = 'en', page = null }) {
  return (
    <nav aria-label={liveChrome().shell.localeNav} className="locale-nav" data-locale-nav="">
      <ul>
        {LOCALES.map((locale) => (
          <li key={locale.code}>
            {/* The name is in its own language (lang); the destination's language is hrefLang, which for an
                unreviewed language is English, the page that says so (review of #797). */}
            <a
              href={localeHref(locale.code, page)}
              lang={locale.code}
              hrefLang={REVIEWED_CHROME_LOCALES.includes(locale.code) ? locale.code : 'en'}
              aria-current={locale.code === current ? 'true' : undefined}
              data-same-document={REVIEWED_CHROME_LOCALES.includes(locale.code) ? locale.code : undefined}
            >
              {locale.name}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}
