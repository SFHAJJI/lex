// The answer for a locale this interface has no reviewed copy in.
//
// Until now, asking for German returned English prose under `lang="de"`, on every page in the
// build. That is the substitution nobody can see: the reader gets a language they did not ask
// for, wearing the tag of the one they did, and a screen reader reads English in a German
// voice. Decision 63 says this product never substitutes a locale, and the reason it is worth
// a whole page rather than a footnote is that the substitution is invisible precisely to the
// reader it fails.
//
// So an unreviewed locale gets a refusal, and the refusal is written in a language it is
// honestly labelled with. Decision 41 fixes why: FR, DE, LB and PT surfaces require
// evidence-based legal-language review before promotion, and machine translation alone is not
// sufficient. This page exists to say that out loud rather than to paper over it, and it
// offers the locales that do have reviewed copy instead of picking one.

import { page } from './render.mjs';
import { CHROME_LOCALES, LOCALIZATION_UNAVAILABLE } from './localization.mjs';

/**
 * Locales whose chrome copy has actually been reviewed.
 *
 * One entry, and it is honest. Nothing in this build has been through legal-language review in
 * any other language, so listing more would be the claim this page exists to avoid making.
 */
export const REVIEWED_CHROME_LOCALES = Object.freeze(['en']);

const LOCALE_NAME = Object.freeze({
  fr: 'French',
  de: 'German',
  en: 'English',
  lb: 'Luxembourgish',
});

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/**
 * The page a request for an unreviewed locale gets.
 *
 * @param {object} input
 * @param {string} input.requested  the chrome locale the reader asked for
 */
export function renderLocaleUnavailable({ requested }) {
  if (!CHROME_LOCALES.includes(requested)) {
    throw new Error(
      `${JSON.stringify(requested)} is not one of the four chrome locales, so this page has ` +
        'nothing to be unavailable in',
    );
  }
  if (REVIEWED_CHROME_LOCALES.includes(requested)) {
    throw new Error(
      `${requested} chrome has reviewed copy, so this page would be refusing something that ` +
        'exists; serve the reviewed copy instead',
    );
  }

  // Written in English and labelled English. The one thing this page must not do is the thing
  // it exists to report.
  const name = LOCALE_NAME[requested];
  const available = REVIEWED_CHROME_LOCALES.map(
    (one) => `${LOCALE_NAME[one]} (${one})`,
  ).join(', ');

  return page({
    state: 'localization-unavailable',
    title: 'Interface language unavailable',
    locale: 'en',
    copyLocale: 'en',
    main:
      '      <h1>This interface has no reviewed copy in ' +
      `${escapeHtml(name)}</h1>\n` +
      `      <p class="locale-code"><code>${escapeHtml(LOCALIZATION_UNAVAILABLE)}</code></p>\n` +
      `      <p>You asked for ${escapeHtml(name)}. This page is in English and is labelled as ` +
      'English, because showing you English under a ' +
      `${escapeHtml(requested)} tag would be handing you a language you did not ask for while ` +
      'telling your browser and your screen reader it was the one you did.</p>\n' +
      '      <p>Interface copy in French, German, Luxembourgish and Portuguese requires ' +
      'evidence-based legal-language review before it is served. Machine translation is not ' +
      'sufficient for it, because the words that carry the most risk here are the ones that ' +
      'say what this service will not tell you.</p>\n' +
      `      <p>Reviewed interface languages today: ${escapeHtml(available)}.</p>\n` +
      '      <p>This affects the interface around the law. It does not affect the law: ' +
      'publisher text is always served in the language the publisher published it in, with ' +
      'that language on the text itself.</p>\n',
  });
}
