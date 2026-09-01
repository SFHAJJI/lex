// Reviewed interface localization, and authenticity bound to the exact resource.
//
// Two rules here, and the second one replaces the first version of this module wholesale.
//
// Localization. Product spec section 7 ships the UI in FR, DE, EN and LB; issue #349 adds PT
// to the reviewed refusal templates and names the behaviour when a string is missing:
// `localization_unavailable`, never silent fallback and never machine-translated
// authoritative copy. A requested locale is never answered with another locale's text.
//
// Provenance of the strings themselves. The first version stored specification citations in
// a field called `reviewed_by` while the API and the comments claimed a named human had
// reviewed them. That was a claim to evidence the module did not possess, which is the same
// defect it exists to prevent, aimed at itself. A string now carries either a source master,
// meaning the specification ships this exact wording, or a human review receipt with a
// reviewer identity and a real date. Never both, and `reviewed_by` is emitted only for the
// second.
//
// Authenticity. The first version mapped the publisher key `lu-legilux` to French-only and
// treated every other publisher as needing no qualification. That is authenticity inferred
// from a parent, which Decision 58 forbids, and it also refuses the handful of held
// German-language LU expressions by asserting they cannot exist. Authenticity is now a typed
// fact about one exact resource, supplied with the quotation and checked; when it is absent
// the quotation fails closed rather than defaulting to "no qualification needed".

import { isCalendarDate, isUtcInstant } from './temporal.mjs';
import { mark } from './design-tokens.mjs';
import { publisherSourceUri } from './routes.mjs';

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
 * versioned registry lists nineteen codes and does not include this one; it comes from the
 * #349 acceptance list. Admitting it to the Gateway registry is a versioned API contract
 * change and belongs to #348, so it stays named here and is not quietly appended.
 */
export const LOCALIZATION_UNAVAILABLE = 'localization_unavailable';

export const RESOURCE_AUTHENTICITY_SCHEMA = 'lex-v3-resource-authenticity/1';

/**
 * The shipped strings.
 *
 * `source_basis` means the specification ships this exact wording. `reviewed_by` with
 * `reviewed_on` means a named person reviewed a translation on a real date. An entry may
 * carry one or the other, never both, and an entry carrying neither is absent.
 */
const STRINGS = Object.freeze({
  'refusal.advice_boundary.sentence': Object.freeze({
    en: Object.freeze({
      text:
        'I can show you exactly what the published text says, at any date, and how it ' +
        'changed, with citations. I cannot apply the law to your situation; under ' +
        'Luxembourg law that assessment is a legal consultation reserved to qualified ' +
        'professionals. Here is the governing text in full as of [date], and here is who ' +
        'can advise you.',
      source_basis: '33-product-spec, fixed refusal templates, EN master',
    }),
    fr: Object.freeze({
      text:
        'Je peux vous montrer exactement ce que dit le texte publié, à toute date, et ce ' +
        'qui a changé, avec citations. Je ne peux pas appliquer le droit à votre ' +
        'situation; cette appréciation relève de la consultation juridique réservée. ' +
        'Voici le texte applicable en entier au [date], et voici qui peut vous conseiller.',
      source_basis: '33-product-spec, fixed refusal templates, FR master',
    }),
  }),
  // The note a sole-authentic-language resource carries. `{language}` and `{basis}` are
  // filled from the resource's own authenticity evidence, never from a publisher key.
  'law.sole_authentic_note': Object.freeze({
    en: Object.freeze({
      text: 'Only the {language} text is authentic ({basis}).',
      source_basis: '33-product-spec section 7 rule 1',
    }),
    fr: Object.freeze({
      text: 'Seul le texte en {language} fait foi ({basis}).',
      source_basis: '33-product-spec section 7 rule 1',
    }),
  }),
});

/** True when a named person reviewed this entry on a real date. */
export function isReviewed(entry) {
  // Own properties only. An entry whose prototype supplies `reviewed_by` and `reviewed_on`
  // would otherwise become claimed human-review evidence, which is the same defect as a
  // closed vocabulary reached through `toString`, aimed at the field that says a person
  // looked at this.
  return Boolean(
    entry &&
      Object.hasOwn(entry, 'reviewed_by') &&
      Object.hasOwn(entry, 'reviewed_on') &&
      typeof entry.reviewed_by === 'string' &&
      entry.reviewed_by.trim().length > 0 &&
      isCalendarDate(entry.reviewed_on),
  );
}

/** True when the specification itself ships this exact wording. */
export function isSourceMaster(entry) {
  return Boolean(
    entry
      && Object.hasOwn(entry, 'source_basis')
      && typeof entry.source_basis === 'string'
      && entry.source_basis.trim().length > 0,
  );
}

/**
 * What may be served, and under which claim. An entry that is both a source master and a
 * human review is refused rather than resolved: the two are different provenances and a
 * string cannot be served under both.
 */
export function provenanceOf(entry) {
  if (
    !entry
    || !Object.hasOwn(entry, 'text')
    || typeof entry.text !== 'string'
    || entry.text.trim().length === 0
  ) {
    return null;
  }
  const master = isSourceMaster(entry);
  const reviewed = isReviewed(entry);
  if (master === reviewed) return null;
  return master
    ? { kind: 'source_master', source_basis: entry.source_basis }
    : { kind: 'human_review', reviewed_by: entry.reviewed_by, reviewed_on: entry.reviewed_on };
}

/** The locales a key is genuinely servable in, in the order the interface offers them. */
export function servableLocales(key, bundle = STRINGS) {
  const entries = Object.hasOwn(bundle, key) ? bundle[key] : {};
  return REFUSAL_TEMPLATE_LOCALES.filter(
    (locale) => provenanceOf(Object.hasOwn(entries, locale) ? entries[locale] : undefined) !== null,
  );
}

/**
 * A servable string with its provenance, or a typed refusal naming where it does exist.
 *
 * @param {string} key
 * @param {string} locale
 * @param {object} [values]  substitutions for `{name}` placeholders, escaped by the caller
 * @param {object} [bundle]
 */
export function servableText(key, locale, values = {}, bundle = STRINGS) {
  const entries = Object.hasOwn(bundle, key) ? bundle[key] : {};
  const entry = Object.hasOwn(entries, locale) ? entries[locale] : undefined;
  const provenance = provenanceOf(entry);
  if (provenance !== null) {
    return {
      status: 'ok',
      locale,
      text: fill(entry.text, values),
      provenance,
    };
  }
  // No fallback. Serving English under a request for Luxembourgish is the failure the
  // acceptance names, and it is invisible to everyone except the reader it fails.
  return {
    status: LOCALIZATION_UNAVAILABLE,
    locale,
    key,
    servable_in: servableLocales(key, bundle),
  };
}

function fill(template, values) {
  return template.replaceAll(/\{([a-z_]+)\}/g, (whole, name) =>
    Object.hasOwn(values, name) ? String(values[name]) : whole,
  );
}

const TAG = /^([A-Za-z]{2,3})(?:-[A-Za-z0-9]+)*$/;

function parseAcceptLanguage(header) {
  if (typeof header !== 'string' || header.trim().length === 0) return [];
  return header
    .split(',')
    .map((part) => {
      const [tag, ...params] = part.trim().split(';');
      const q = params.map((param) => /^\s*q=([0-9.]+)\s*$/.exec(param)).find(Boolean);
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
 * The chrome locale for a request: a stored choice, then Accept-Language among the four,
 * then French. French because it is the sole authentic language of Luxembourg statute and
 * one of the two master locales; the pack states the four and the ordering rule but names no
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
 * Read and write the persisted chrome locale. Every access is wrapped: a private window,
 * cleared site data or a browser set to block storage makes the accessor itself throw, and a
 * language switcher that takes the page down is worse than one that forgets.
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

const LANGUAGE_TAG = /^[a-z]{2}$/;

/**
 * Typed authenticity for one exact resource.
 *
 * Decision 58 binds authenticity to the resource and forbids lifting it from a parent,
 * sibling, expression or publisher. So this is checked, not inferred, and there is no
 * publisher table anywhere in this module for anything to be inferred from.
 */
export function requireResourceAuthenticity(evidence) {
  if (!evidence || typeof evidence !== 'object') {
    throw new Error(
      'quoting law requires typed authenticity evidence for that exact resource; without it ' +
        'the only honest rendering is an unofficial one, and defaulting to "no qualification ' +
        'needed" is a claim nobody made',
    );
  }
  if (evidence.schema !== RESOURCE_AUTHENTICITY_SCHEMA) {
    throw new Error(
      `authenticity evidence must declare ${RESOURCE_AUTHENTICITY_SCHEMA}, not ` +
        JSON.stringify(evidence.schema),
    );
  }
  if (typeof evidence.resource_id !== 'string' || evidence.resource_id.trim().length === 0) {
    throw new Error('authenticity evidence must name the exact resource it describes');
  }
  const languages = evidence.authentic_languages;
  if (!Array.isArray(languages) || languages.length === 0) {
    throw new Error('authenticity evidence must name at least one authentic language');
  }
  if (!languages.every((one) => typeof one === 'string' && LANGUAGE_TAG.test(one))) {
    throw new Error(
      `authentic_languages must be language tags: ${JSON.stringify(languages)}`,
    );
  }
  if (new Set(languages).size !== languages.length) {
    throw new Error('authentic_languages repeats a language');
  }
  if (typeof evidence.basis !== 'string' || evidence.basis.trim().length === 0) {
    throw new Error('authenticity evidence must carry the ground for its claim');
  }
  if (typeof evidence.asserted_by !== 'string' || evidence.asserted_by.trim().length === 0) {
    throw new Error('authenticity evidence must name who asserts it');
  }
  if (!isUtcInstant(evidence.observed_at)) {
    throw new Error(
      `authenticity evidence must carry when it was observed: ${JSON.stringify(evidence.observed_at)}`,
    );
  }
  return evidence;
}

/**
 * A quoted statutory span, carrying its own language and, where the resource's own evidence
 * says so, the authenticity note.
 *
 * The note is not a caller's option and not a publisher's property. It appears when the
 * resource has exactly one authentic language, because then a reader is looking at either
 * the authentic text or an unofficial rendering, and it does not appear when every held
 * expression is equally authentic, because there the note would be false.
 *
 * A language outside the resource's authentic set is refused here. It is an unofficial
 * rendering and belongs in a component that labels it as one, not in the one that quotes law.
 *
 * @param {object} input
 * @param {string} input.resourceId    the resource this text is from
 * @param {object} input.authenticity  typed evidence for that exact resource
 * @param {string} input.language      the expression's own language, not the chrome locale
 * @param {string} input.text          the publisher's text
 * @param {string} [input.noteLocale]  the locale to render the authenticity note in
 */
export function quotedLaw({ resourceId, authenticity, language, text, noteLocale = 'en' }) {
  const evidence = requireResourceAuthenticity(authenticity);

  // The evidence names a resource and the quotation names a resource, and until they were
  // compared, valid evidence for one resource could render another resource's text as law.
  // Validating the evidence in isolation checked that somebody had done the work; it did not
  // check that they had done it for this text.
  if (typeof resourceId !== 'string' || resourceId.trim().length === 0) {
    throw new Error(
      'a quotation must name the resource it is from, so its authenticity evidence can be ' +
        'checked against it rather than merely checked',
    );
  }
  if (resourceId !== evidence.resource_id) {
    throw new Error(
      `this text is from ${resourceId} and the authenticity evidence is for ` +
        `${evidence.resource_id}; evidence for one resource says nothing about another`,
    );
  }

  if (typeof text !== 'string' || text.trim().length === 0) {
    throw new Error('a quoted span needs the publisher text');
  }
  if (typeof language !== 'string' || !LANGUAGE_TAG.test(language)) {
    throw new Error(
      `a quoted statutory span carries its own language attribute; ${JSON.stringify(language)} ` +
        'is not a language tag, and an unmarked span makes a screen reader read French as English',
    );
  }
  if (!evidence.authentic_languages.includes(language)) {
    throw new Error(
      `${evidence.resource_id} is authentic in ${evidence.authentic_languages.join(', ')}, so a ` +
        `${language} rendering is unofficial and must be labelled as one rather than quoted as law`,
    );
  }

  const quote =
    `<blockquote class="law" lang="${escapeHtml(language)}">${escapeHtml(text)}</blockquote>`;
  if (evidence.authentic_languages.length !== 1) return quote;

  const note = servableText('law.sole_authentic_note', noteLocale, {
    language: escapeHtml(evidence.authentic_languages[0]),
    basis: escapeHtml(evidence.basis),
  });
  return (
    quote +
    (note.status === 'ok'
      ? `<p class="law-authenticity" lang="${escapeHtml(note.locale)}">${note.text}</p>`
      : renderLocalizationUnavailable(note))
  );
}

/**
 * The state a missing string renders as. It says which locale was asked for, which locales
 * the string exists in, and that nothing was substituted, because a reader who sees English
 * where they asked for Luxembourgish deserves to know it was not a translation.
 */
export function renderLocalizationUnavailable({ locale, key, servable_in: servableIn = [] }) {
  const available =
    servableIn.length > 0
      ? `Available in: ${servableIn.map((one) => escapeHtml(one)).join(', ')}.`
      : 'It is not available in any language yet.';
  return (
    '<p class="localization-unavailable" ' +
    `data-code="${LOCALIZATION_UNAVAILABLE}" data-locale="${escapeHtml(locale)}">` +
    `<code>${LOCALIZATION_UNAVAILABLE}</code> ` +
    `This text is not available in ${escapeHtml(locale)}, and nothing was substituted for it. ` +
    `${available} ` +
    `<span class="localization-key">${escapeHtml(key)}</span></p>`
  );
}

/**
 * A body that is not the authentic text: an unofficial rendering, or a transcription of a
 * body the publisher serves only as an image.
 *
 * `quotedLaw` refuses these, and a refusal with nowhere to go would mean the interface simply
 * cannot show them, which is worse: the corpus holds bodies in languages that are not
 * authentic and annexes that exist only as PDFs, and a reader is better served by labelled
 * text plus the official route than by nothing.
 *
 * So this is the other path, and it is the opposite of the quotation in every way that
 * matters. It carries the UNOFFICIAL token, which is an icon and a label rather than a
 * colour. It names the exact official route, checked by the one route policy, so the reader
 * can reach the text that does count. And it says on its face that it is excluded from
 * evidence exports, because a labelled convenience that quietly enters a bundle stops being
 * labelled at the moment it matters.
 */
export function renderUnofficialRendering({
  resourceId,
  authenticity,
  language,
  text,
  publisher,
  officialUri,
}) {
  const evidence = requireResourceAuthenticity(authenticity);
  if (resourceId !== evidence.resource_id) {
    throw new Error(
      `this rendering is of ${resourceId} and the evidence is for ${evidence.resource_id}`,
    );
  }
  if (typeof text !== 'string' || text.trim().length === 0) {
    throw new Error('an unofficial rendering needs its text');
  }
  if (typeof language !== 'string' || !LANGUAGE_TAG.test(language)) {
    throw new Error(
      `an unofficial rendering carries its own language attribute; ${JSON.stringify(language)} ` +
        'is not a language tag',
    );
  }
  // The authentic text is not unofficial. Routing it here would relabel the law.
  if (evidence.authentic_languages.includes(language)) {
    throw new Error(
      `${language} is authentic for ${evidence.resource_id}, so this is the law and belongs in ` +
        'quotedLaw; labelling authentic text as unofficial is the same error in the other ' +
        'direction',
    );
  }

  const official = publisherSourceUri({ publisher, uri: officialUri });

  return (
    '<section class="unofficial-rendering">' +
    `<p class="unofficial-head">${mark('--unofficial', `Rendering in ${language}`)}</p>` +
    `<blockquote class="body" lang="${escapeHtml(language)}">${escapeHtml(text)}</blockquote>` +
    '<p class="unofficial-note">This is not the authentic text. ' +
    `${escapeHtml(evidence.resource_id)} is authentic in ` +
    `${escapeHtml(evidence.authentic_languages.join(', '))} ` +
    `(${escapeHtml(evidence.basis)}). This rendering is excluded from evidence exports.</p>` +
    `<p class="unofficial-official"><a href="${escapeHtml(official)}" rel="external">` +
    'The authentic text, at the publisher</a></p>' +
    '</section>'
  );
}
