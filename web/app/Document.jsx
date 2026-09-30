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
import { LocaleNav } from './LocaleNav.jsx';
import { CHROME_LOCALE, fillText, liveChrome } from '../scripts/live-chrome.mjs';

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
 * The banner a live page carries instead of the synthetic one, whose sentence ("describes no real
 * legal record") would be false on a real mount. A driver's draft, before the owner's wording:
 * it claims only what the page does, and names the digests as the way to know which corpus it is.
 */
export function LiveBanner() {
  const shell = liveChrome().shell;
  return (
    <aside className="synthetic" role="note" data-live={LIVE_MARKER}>
      {/* One text node after the lead, as before the move to the table, so the markup is unchanged. */}
      <strong>{shell.bannerLead}</strong>{` ${shell.banner}`}
    </aside>
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
      </head>
      <body>
        {banner === 'live' ? <LiveBanner /> : <SyntheticBanner />}
        {/* The live pages offer the interface languages; an unreviewed one answers localization_unavailable. */}
        {banner === 'live' ? <LocaleNav current={locale} /> : null}
        <main id="main">{children}</main>
      </body>
    </html>
  );
}
