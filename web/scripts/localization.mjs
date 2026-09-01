// Reviewed interface localization, and the refusal that replaces a silent fallback.
//
// Product spec section 7 rule 1: the UI ships in FR, DE, EN and LB; Luxembourg statute is
// always quoted in French with the standing note that only the French text is authentic;
// EU expressions are equally authentic per language, so no such note belongs there. Rule 4
// adds that the Luxembourgish refusal is itself in Luxembourgish. Issue #349 adds PT to the
// reviewed refusal templates and names the behaviour when a string is missing:
// `localization_unavailable`, never silent fallback and never machine-translated
// authoritative copy.
//
// Three things are made structural here rather than promised.
//
// A string is served only if a named human reviewed it on a named date. An entry without
// both is treated as absent, which is what "never machine-translated authoritative copy"
// means once it has to survive somebody pasting a translation in at midnight.
//
// A requested locale is never answered with another locale's text. The result is a typed
// refusal carrying the locales the string was actually reviewed in, so a caller can offer a
// real choice instead of quietly serving English to a Luxembourgish reader.
//
// Chrome language and quoted law are separated by having no channel between them. There is
// no module-level current locale, and `quotedLaw` takes the expression's own language, so
// the chrome switcher cannot reach the statute. That is UX spec section 1: "The chrome
// switcher NEVER changes quoted law."
//
// What is deliberately absent: DE, LB and PT reviewed strings. The architect pack carries
// verbatim EN and FR masters and nothing else, so shipping the other three would mean
// inventing authoritative legal copy. They are missing, the refusal fires, and the gap is
// editorial work for a reviewed translator rather than something code can close.

/** UI chrome ships in these four, product spec section 7 rule 1. */
export const CHROME_LOCALES = Object.freeze(['fr', 'de', 'en', 'lb']);

/** Reviewed refusal templates add PT, issue #349 acceptance. */
export const REFUSAL_TEMPLATE_LOCALES = Object.freeze(['fr', 'de', 'en', 'lb', 'pt']);

/** The two the pack ships verbatim; every other locale is a translation of one of these. */
export const MASTER_LOCALES = Object.freeze(['en', 'fr']);

/**
 * The code returned when a reviewed string does not exist.
 *
 * It is NOT a member of the closed refusal registry in `refusal-card.mjs`. The pack's
 * versioned registry (31-v3-spec line 128) lists nineteen codes and does not include this
 * one; it comes from the #349 acceptance list. Admitting it to the Gateway registry is a
 * versioned API contract change and belongs to #348, so it stays named here and is not
 * quietly appended to a published closed set.
 */
export const LOCALIZATION_UNAVAILABLE = 'localization_unavailable';

/**
 * Reviewed strings. An entry needs `text`, `reviewed_by` and `reviewed_on` to be served.
 *
 * The two advice-boundary sentences are the pack's fixed templates, quoted verbatim from
 * 33-product-spec section "Fixed refusal templates".
 */
const REVIEWED = Object.freeze({
  'refusal.advice_boundary.sentence': Object.freeze({
    en: Object.freeze({
      text:
        'I can show you exactly what the published text says, at any date, and how it ' +
        'changed, with citations. I cannot apply the law to your situation; under ' +
        'Luxembourg law that assessment is a legal consultation reserved to qualified ' +
        'professionals. Here is the governing text in full as of [date], and here is who ' +
        'can advise you.',
      reviewed_by: '33-product-spec fixed refusal template, EN master',
      reviewed_on: '2026-08-27',
    }),
    fr: Object.freeze({
      text:
        'Je peux vous montrer exactement ce que dit le texte publié, à toute date, et ce ' +
        'qui a changé, avec citations. Je ne peux pas appliquer le droit à votre ' +
        'situation; cette appréciation relève de la consultation juridique réservée. ' +
        'Voici le texte applicable en entier au [date], et voici qui peut vous conseiller.',
      reviewed_by: '33-product-spec fixed refusal template, FR master',
      reviewed_on: '2026-08-27',
    }),
  }),
  'law.lu.authenticity_note': Object.freeze({
    en: Object.freeze({
      text: 'Only the French text is authentic (loi du 24 février 1984, art. 2).',
      reviewed_by: '33-product-spec section 7 rule 1, grounding L1984',
      reviewed_on: '2026-08-27',
    }),
    fr: Object.freeze({
      text: 'Seul le texte français fait foi (loi du 24 février 1984, art. 2).',
      reviewed_by: '33-product-spec section 7 rule 1, grounding L1984',
      reviewed_on: '2026-08-27',
    }),
  }),
});

/**
 * What counts as reviewed: text, a named reviewer and a review date. This is the contract,
 * not an implementation detail, which is why it is exported and directly testable. An entry
 * that has only text is a translation somebody pasted in, and serving it as authoritative
 * copy is the exact thing #349 forbids.
 */
export function isReviewed(entry) {
  return Boolean(
    entry &&
      typeof entry.text === 'string' &&
      entry.text.trim().length > 0 &&
      typeof entry.reviewed_by === 'string' &&
      entry.reviewed_by.trim().length > 0 &&
      /^\d{4}-\d{2}-\d{2}$/.test(entry.reviewed_on ?? ''),
  );
}

/** The locales a key is genuinely reviewed in, in the order the interface offers them. */
export function reviewedLocales(key, bundle = REVIEWED) {
  const entries = bundle[key] ?? {};
  return REFUSAL_TEMPLATE_LOCALES.filter((locale) => isReviewed(entries[locale]));
}

/**
 * A reviewed string, or a typed refusal naming where it does exist.
 *
 * @returns {{status: 'ok', locale: string, text: string, reviewed_by: string,
 *            reviewed_on: string}
 *          | {status: 'localization_unavailable', locale: string, key: string,
 *             reviewed_in: string[]}}
 */
export function reviewedText(key, locale, bundle = REVIEWED) {
  const entry = bundle[key]?.[locale];
  if (isReviewed(entry)) {
    return {
      status: 'ok',
      locale,
      text: entry.text,
      reviewed_by: entry.reviewed_by,
      reviewed_on: entry.reviewed_on,
    };
  }
  // No fallback. Serving English under a request for Luxembourgish is the failure the
  // acceptance names, and it is invisible to everyone except the reader it fails.
  return {
    status: LOCALIZATION_UNAVAILABLE,
    locale,
    key,
    reviewed_in: reviewedLocales(key, bundle),
  };
}

const TAG = /^([A-Za-z]{2,3})(?:-[A-Za-z0-9]+)*$/;

function parseAcceptLanguage(header) {
  if (typeof header !== 'string' || header.trim().length === 0) return [];
  return header
    .split(',')
    .map((part) => {
      const [tag, ...params] = part.trim().split(';');
      const q = params
        .map((param) => /^\s*q=([0-9.]+)\s*$/.exec(param))
        .find(Boolean);
      const quality = q ? Number.parseFloat(q[1]) : 1;
      const match = TAG.exec(tag.trim());
      return match && Number.isFinite(quality) && quality > 0
        ? { language: match[1].toLowerCase(), quality }
        : null;
    })
    .filter(Boolean)
    .sort((a, b) => b.quality - a.quality)
    .map((entry) => entry.language);
}

/**
 * The chrome locale for a request.
 *
 * A stored choice wins, then Accept-Language among the four, then French. French is the
 * fallback because it is the sole authentic language of Luxembourg statute and one of the
 * two master locales; the pack states the four and the ordering rule but does not name a
 * terminal default, so this is a stated choice rather than a quotation.
 */
export function resolveChromeLocale({ stored, acceptLanguage } = {}) {
  const supported = new Set(CHROME_LOCALES);
  if (supported.has(stored)) return stored;
  for (const language of parseAcceptLanguage(acceptLanguage)) {
    if (supported.has(language)) return language;
  }
  return 'fr';
}

/**
 * Read and write the persisted chrome locale.
 *
 * Every access is wrapped: a private window, cleared site data or a browser set to block
 * storage makes the accessor itself throw, and a language switcher that takes the page down
 * is worse than one that forgets.
 */
export function readStoredLocale(storage) {
  try {
    const value = storage?.getItem('lex.chrome-locale');
    return CHROME_LOCALES.includes(value) ? value : null;
  } catch {
    return null;
  }
}

export function writeStoredLocale(storage, locale) {
  if (!CHROME_LOCALES.includes(locale)) {
    throw new Error(`${JSON.stringify(locale)} is not one of the four chrome locales`);
  }
  try {
    storage?.setItem('lex.chrome-locale', locale);
    return true;
  } catch {
    return false;
  }
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/** Publishers whose statute has one authentic language, and which one it is. */
const SOLE_AUTHENTIC_LANGUAGE = Object.freeze({ 'lu-legilux': 'fr' });

/**
 * A quoted statutory span, carrying its own language and, where the law says so, the
 * authenticity note.
 *
 * The note is not a caller's option. LU statute always carries it because French alone is
 * authentic; EU expressions never carry it because each language expression is equally
 * authentic and a note claiming otherwise would be false. There is no parameter that turns
 * either behaviour off, and no chrome locale reaches this function.
 *
 * @param {object} input
 * @param {string} input.publisher       the publisher whose statute this is
 * @param {string} input.language        the expression's own language, not the chrome locale
 * @param {string} input.text            the publisher's text
 * @param {string} [input.noteLocale]    the locale to render the authenticity note in
 */
export function quotedLaw({ publisher, language, text, noteLocale = 'en' }) {
  if (typeof text !== 'string' || text.trim().length === 0) {
    throw new Error('a quoted span needs the publisher text');
  }
  if (typeof language !== 'string' || !/^[a-z]{2}$/.test(language)) {
    throw new Error(
      `a quoted statutory span carries its own language attribute; ${JSON.stringify(language)} ` +
        'is not a language tag, and an unmarked span makes a screen reader read French as English',
    );
  }

  const authentic = SOLE_AUTHENTIC_LANGUAGE[publisher];
  if (authentic && language !== authentic) {
    throw new Error(
      `${publisher} statute is authentic in ${authentic} alone, so a quoted span in ` +
        `${language} would be an unofficial rendering and must be labelled as one rather ` +
        'than quoted as the law',
    );
  }

  const quote = `<blockquote class="law" lang="${escapeHtml(language)}">${escapeHtml(text)}</blockquote>`;
  if (!authentic) return quote;

  const note = reviewedText('law.lu.authenticity_note', noteLocale);
  const noteHtml =
    note.status === 'ok'
      ? `<p class="law-authenticity" lang="${escapeHtml(note.locale)}">${escapeHtml(note.text)}</p>`
      : renderLocalizationUnavailable(note);
  return quote + noteHtml;
}

/**
 * The state a missing reviewed string renders as. It says which locale was asked for, which
 * locales the string exists in, and that nothing was substituted, because a reader who sees
 * English where they asked for Luxembourgish deserves to know it was not a translation.
 */
export function renderLocalizationUnavailable({ locale, key, reviewed_in: reviewedIn = [] }) {
  const available =
    reviewedIn.length > 0
      ? `Reviewed in: ${reviewedIn.map((one) => escapeHtml(one)).join(', ')}.`
      : 'It is not reviewed in any language yet.';
  return (
    '<p class="localization-unavailable" ' +
    `data-code="${LOCALIZATION_UNAVAILABLE}" data-locale="${escapeHtml(locale)}">` +
    `<code>${LOCALIZATION_UNAVAILABLE}</code> ` +
    `This text is not reviewed in ${escapeHtml(locale)}, and nothing was substituted for it. ` +
    `${available} ` +
    `<span class="localization-key">${escapeHtml(key)}</span></p>`
  );
}
