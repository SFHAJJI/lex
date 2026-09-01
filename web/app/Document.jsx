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
 * @param {React.ReactNode} props.children the page body, as components
 */
export function Document({
  state,
  title,
  locale = 'en',
  copyLocale = 'en',
  shell = null,
  density = null,
  children,
}) {
  if (!CHROME_LOCALES.includes(locale)) {
    throw new Error(`${JSON.stringify(locale)} is not one of the reviewed chrome locales`);
  }
  if (!CHROME_LOCALES.includes(copyLocale)) {
    throw new Error(`${JSON.stringify(copyLocale)} is not one of the reviewed chrome locales`);
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

  const shellAttributes =
    shell === null ? {} : { 'data-shell': shell, 'data-density': density ?? '' };

  return (
    <html lang={locale} data-product-line="lex-v3" data-preview-state={state} {...shellAttributes}>
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>{`${title} - Lex V3 preview`}</title>
        <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
        <link rel="stylesheet" href="/styles.css" />
      </head>
      <body>
        <SyntheticBanner />
        <main id="main">{children}</main>
      </body>
    </html>
  );
}
