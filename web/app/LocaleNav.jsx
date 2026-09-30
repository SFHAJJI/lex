// The interface-language choice on the live pages.
//
// The launch contract's line is "Chrome in FR and EN; DE and LB answer localization_unavailable".
// Each language is named in itself and tagged with its own `lang`, so a screen reader says
// "Deutsch" as German rather than as an English word. Only English chrome is reviewed today
// (`REVIEWED_CHROME_LOCALES`); every other language leads to the page that says so, in English and
// labelled English (`live-locale-page.jsx`), never to English copy under another language's tag.

import { REVIEWED_CHROME_LOCALES } from '../scripts/locale-unavailable.mjs';
import { liveChrome } from '../scripts/live-chrome.mjs';

const LOCALES = Object.freeze([
  Object.freeze({ code: 'en', name: 'English' }),
  Object.freeze({ code: 'fr', name: 'Français' }),
  Object.freeze({ code: 'de', name: 'Deutsch' }),
  Object.freeze({ code: 'lb', name: 'Lëtzebuergesch' }),
]);

/** Where a language's chrome is: the page itself when reviewed, else the page that says it is not. */
export function localeHref(code) {
  // A reviewed language's pages are built under its own path (`build-live.mjs`); English is the root.
  if (!REVIEWED_CHROME_LOCALES.includes(code)) return `/locale-${code}.html`;
  return code === 'en' ? '/' : `/${code}/`;
}

export function LocaleNav({ current = 'en' }) {
  return (
    <nav aria-label={liveChrome().shell.localeNav} className="locale-nav" data-locale-nav="">
      <ul>
        {LOCALES.map((locale) => (
          <li key={locale.code}>
            {/* The name is in its own language (lang); the destination's language is hrefLang, which for an
                unreviewed language is English, the page that says so (review of #797). */}
            <a href={localeHref(locale.code)} lang={locale.code} hrefLang={REVIEWED_CHROME_LOCALES.includes(locale.code) ? locale.code : 'en'} aria-current={locale.code === current ? 'true' : undefined}>
              {locale.name}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}
