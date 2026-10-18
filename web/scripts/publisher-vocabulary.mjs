// Which clock a publisher's dates are on, and the words for it.
//
// This module exists because the same defect was repaired five times in one day, in five
// different screens, and it was never five defects. It was one design error.
//
// The vocabulary used to be a free parameter: every renderer took `semantics` alongside its
// data, so every renderer had to remember to apply it, and every renderer could get it wrong on
// its own. The timeline rows were repaired and the legend above them kept asserting the old
// thing. The qualifiers were repaired and the evidence bundle kept exporting the old thing. And
// a search list, which is inherently multi-publisher, could not be right at all with one value:
// it rendered "Applicable from ... (publisher)" over an EUR-Lex row, attributing to the Union
// publisher a claim that publisher does not make.
//
// So the vocabulary is not an argument here. It is a property of the publisher, derived from the
// record's own publisher wherever it is needed. A screen cannot pass the wrong one because a
// screen does not pass one. That is what makes the class of defect gone rather than fixed.
//
// Luxembourg dates when a state applied. The Union dates the wording state of a consolidation
// and makes no applicability claim. Those are different assertions about different things, and
// printing one publisher's word over the other's record is the failure this product exists to
// prevent.

/** The two vocabularies, and there is no third and no default. */
export const SEMANTICS = Object.freeze(['publisher_applicability', 'official_consolidation_state']);

/**
 * A lookup that holds only what it was given.
 *
 * An object literal inherits from `Object.prototype`, so a table written to fail closed quietly
 * answers `constructor`, `toString`, `valueOf`, `__proto__` and `hasOwnProperty` with inherited
 * members. `semanticsOf('constructor')` returned the Object constructor, which is not
 * `undefined`, so it passed the very check whose purpose is to refuse a publisher nobody
 * classified. The module built to make a defect class impossible contained the class.
 *
 * The repair belongs to the prototype rather than to the call site. `Object.hasOwn` at each
 * lookup also works, but it must be remembered every time and by everyone, and this module
 * exists precisely because "remember to apply it" failed five times. A table with no prototype
 * cannot answer for a key it was never given, so being wrong here stops being possible rather
 * than being caught.
 */
function closedTable(entries) {
  return Object.freeze(Object.assign(Object.create(null), entries));
}

/**
 * Which vocabulary each publisher's dates are in.
 *
 * Closed. A publisher nobody has classified fails closed rather than inheriting a neighbour's
 * claim, because inheriting is exactly how an EU consolidation state came to be labelled
 * applicable.
 */
const BY_PUBLISHER = closedTable({
  'lu-legilux': 'publisher_applicability',
  'eu-eurlex': 'official_consolidation_state',
  // The synthetic publisher stands in for a Luxembourg record in previews. Named rather than
  // defaulted, so adding a publisher is a deliberate act.
  'preview-synthetic': 'publisher_applicability',
});

/**
 * The vocabulary this publisher's dates are in.
 *
 * @param {unknown} publisher
 * @param {string} where  what to name in the error
 */
export function semanticsOf(publisher, where) {
  const semantics = BY_PUBLISHER[publisher ?? ''];
  if (semantics === undefined) {
    throw new Error(
      `${where}: ${JSON.stringify(publisher)} is not a publisher this interface has classified, ` +
        'so the clock its dates are on is unknown; a publisher that inherits another publisher ' +
        "one's vocabulary is how a consolidation state comes to be labelled applicable",
    );
  }
  return semantics;
}

/** Every publisher this interface can render dates for. */
export const CLASSIFIED_PUBLISHERS = Object.freeze(Object.keys(BY_PUBLISHER));

/** What the interval is called, as a column or field label. */
export const INTERVAL_TERM = closedTable({
  publisher_applicability: 'applicable',
  official_consolidation_state: 'consolidated wording',
});

/** The phrase a qualifier uses for the state it is attached to. */
export const STATE_PHRASE = closedTable({
  publisher_applicability: 'applicable from',
  official_consolidation_state: 'a consolidated wording state from',
});

/** One state's interval, as a sentence. */
export const INTERVAL_SENTENCE = closedTable({
  publisher_applicability: (from, to) =>
    `Applicable from ${from} to ${to === null ? 'no end recorded' : to} (publisher)`,
  official_consolidation_state: (from, to) =>
    `Consolidated wording state from ${from} to ${to === null ? 'no end recorded' : to}`,
});

/**
 * What a submitted date resolved to, announced.
 *
 * One sentence per vocabulary rather than a phrase composed at the call site, because the two
 * publishers' words do not fit one sentence frame: "the state applicable from" reads correctly
 * and "the state a consolidated wording state from" does not. A caller assembling this out of
 * parts would have to know that, and would eventually get it wrong.
 */
export const RESOLUTION_SENTENCE = closedTable({
  publisher_applicability: (from, to, published) =>
    `Resolved to the state applicable from ${from} to ${to === null ? 'no end recorded' : to}, ` +
    `published ${published}.`,
  official_consolidation_state: (from, to, published) =>
    `Resolved to the consolidated wording state from ${from} to ` +
    `${to === null ? 'no end recorded' : to}, published ${published}.`,
});

/** The heading over a set of states scoped to one date. */
export const DATE_SCOPE = closedTable({
  publisher_applicability: (date) => `Provisions as applicable on ${date}`,
  official_consolidation_state: (date) => `Wording states covering ${date}`,
});

/** The legend explaining the two clocks. */
export const LEGENDS = closedTable({
  publisher_applicability:
    'Top: when the publisher says the state applied. Bottom: when the publisher published it. ' +
    'These routinely differ.',
  official_consolidation_state:
    'Top: the wording state the publisher consolidated. Bottom: when the publisher published ' +
    'it. These routinely differ.',
});

/**
 * Refuse anything outside the two vocabularies.
 *
 * Kept for the few places that still receive a vocabulary rather than a publisher, so those
 * places fail closed while they are converted.
 */
export function requireSemantics(semantics, where) {
  if (!SEMANTICS.includes(semantics ?? '')) {
    throw new Error(
      `${where} renders in the publisher's own vocabulary and ${JSON.stringify(semantics)} is ` +
        `not one of ${SEMANTICS.join(', ')}; the two publishers make different claims and this ` +
        'product does not choose between them',
    );
  }
  return semantics;
}

/**
 * The one vocabulary a set of records shares, or null when they disagree.
 *
 * A list drawn from several publishers has no single vocabulary, and a heading that picks one
 * states a claim about rows it does not describe. Callers use null to choose neutral wording
 * rather than to choose a winner.
 */
export function sharedSemantics(publishers, where) {
  const distinct = new Set(publishers.map((publisher) => semanticsOf(publisher, where)));
  return distinct.size === 1 ? [...distinct][0] : null;
}

/**
 * What a comparison says when both dates select one state, in each publisher's clock: Luxembourg's version with its
 * publisher applicability start (R21's "applicable from X (publisher)"), the Union's consolidated wording state with its
 * date (the plan's item 2.5). It used to say "the same version applied on both dates", which R21 never says.
 */
export const SAME_STATE_SENTENCE = closedTable({
  publisher_applicability: (from) => `Both dates fall within the same version, applicable from ${from} (publisher)`,
  official_consolidation_state: (from) => `Both dates fall within the same consolidated wording state (from ${from})`,
});

/**
 * The words R21 never says of a date or an interval, in any language: "in force" and "en vigueur", and "applied" with
 * every past form of the French "appliquer" ("appliqué", "appliquée", "s’appliquait", "qui s’y appliquaient",
 * "appliquèrent"), with or without "s’". R21's own words are not among them: "applicable", the publisher's for a
 * Luxembourg version, and the present "s’applique", "applies", "applying" and "appliquant", which say what applies on a
 * date asked and are no past interval; a typed publisher date (an end-of-force date, "date de fin de vigueur", and an
 * entry into force, "entry into force" and the noun "entrée(s) en vigueur": index 9's date role, S7a) is spoken as its own
 * value, so "en vigueur" is refused except right after the word "entrée" or "entrées" (the French review of S7a; "entré en
 * vigueur", "rentrée en vigueur" and "est en vigueur" stay refused). Each word stands alone: no letter before or after it, in
 * any script. It is matched against `vocabularyText`, never against the raw text.
 */
export const FORBIDDEN_TIME_WORDS =
  /(?<!\p{L})(?:in force|(?<!(?:^|[^\p{L}])entrées? )en vigueur|applied|appliqu(?:é|ée|és|ées|ai|ais|ait|aient|a|as|âmes|âtes|èrent))(?!\p{L})/iu;

/**
 * A text as the vocabulary check reads it (the review of #964, which found each of these let through): composed to NFC,
 * so a decomposed "appliquée" is "appliquée"; its invisible format characters (a soft hyphen, a zero-width space
 * or joiner) taken out; every run of spaces and dashes (a no-break or narrow no-break space, a hyphen, a non-breaking
 * hyphen, an en dash) folded to one space, so "in-force" and "in force" are "in force"; and every apostrophe-like
 * mark folded to ’, so "sʼappliquait" (a modifier letter, which is a letter) is "s’appliquait".
 */
export function vocabularyText(text) {
  return String(text)
    .normalize('NFC')
    .replace(/\p{Cf}+/gu, '')
    .replace(/[\s\p{Pd}]+/gu, ' ')
    .replace(/['‘’ʼ`´′]/gu, '’');
}

/**
 * The vocabulary check (R21): throws, naming the word and where it was said, when a text says one of the forbidden
 * words in any of their forms; returns the text otherwise. The Compare card and its page's copy are held to it in both
 * languages (`compare-vocabulary.test.mjs`), which also feeds it the old note and every variant the review of #964 found
 * to show it refusing, and so are the as-published block and its copy (`as-published.test.mjs`).
 */
export function assertTimeVocabulary(text, where) {
  const found = vocabularyText(text).match(FORBIDDEN_TIME_WORDS);
  if (found !== null) {
    throw new Error(`${where} says "${found[0]}", which R21 never says of a date or an interval: say what the publisher dates ("applicable from", or "consolidated wording state")`);
  }
  return text;
}
