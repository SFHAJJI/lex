// Sentences for refusals the live screens share.
//
// `no_corpus_mounted` names the corpus the request needed (`required_corpus`), and a server can hold
// one publisher's index and not the other's. "This build has no index mounted" is then false: the
// reader may have read a Luxembourg answer from this server a moment before asking about an EU work.
// So the sentence names the index that is missing, which is true whether the server holds the other
// publisher's index or none at all (review of #775).
//
// Each sentence is held here in English, where the checkpoint list collects it (`refusal-sentences.mjs`),
// and said in the bundle's language (`refusalSentence`, `refusalHint`): a reviewed language's own
// wording, never the English in its place.

import { fillParts, fillText, englishRun, refusalHint, refusalSentence, refusalTemplate, statement } from "./live-chrome.mjs";

const CORPUS_NAMES = Object.freeze({ lu: "Luxembourg", eu: "EU" });

/**
 * The hint an absence whose card cannot be shown still carries (review of #777): the date the held history
 * begins, so the reader can ask again. Said by the reading, export and compare pages.
 */
export function historyBeginsHint(date) {
  return fillText(refusalHint("The history this index holds for this work begins on {date}."), { date });
}

/** The hint for an article id no held state carries: the nearest ids the latest state does carry. */
export function nearestAnchorsHint(ids) {
  return fillText(refusalHint("The nearest article ids this index holds are {ids}."), { ids: ids.join(", ") });
}

/** The sentence for a `no_corpus_mounted` refusal, naming the index its payload says is missing. */
export function noCorpusMountedSentence(payload) {
  const name = CORPUS_NAMES[payload?.required_corpus];
  return refusalSentence(name === undefined
    ? "This build has no index mounted for this request's publisher."
    : `This build has no ${name} index mounted.`);
}

/**
 * What every screen says of a work R92 withholds (`text_withheld` whose payload's `reason` is `names_private_individuals`,
 * the names record beside the mount; the plan's item 1.11): the act names private individuals, so Lex shows neither its text
 * nor its titles and points to the publisher, through the official link the card gives. It names no person and offers no
 * contact (the owner's order: the site names no one and gives no contact channel).
 */
export const NAMES_WITHHELD_SENTENCE =
  "According to Lex's check, this act names private individuals, so Lex shows neither its text nor its titles: read it at the publisher, by following the official link.";

/** The reason a `text_withheld` payload gives for a work R92 withholds. */
export const NAMES_WITHHELD_REASON = "names_private_individuals";

/** Whether a refusal is R92's for a work the names record withholds. */
export function namesWithheld(code, payload) {
  return code === "text_withheld" && payload?.reason === NAMES_WITHHELD_REASON;
}

/**
 * What every screen says of an EU act outside the EU population the server holds (`out_of_corpus_scope` whose payload's class is
 * `eu_act_outside_population`; R80): unlike the list's classes, it is not a kind of instrument Lex never holds, so the screen's
 * sentence for the list's refusals would be false of it. The card then names the rule, EUR-Lex and who to ask.
 */
export const OUTSIDE_EU_POPULATION_SENTENCE =
  "Lex does not hold this EU act: it is outside the EU acts Lex's population rule selects. Where it is published, and who to ask, follow.";

/** The class an `out_of_corpus_scope` payload gives an EU act outside the EU population (`V3CorpusMount.EuropePopulationClass`). */
export const OUTSIDE_EU_POPULATION_CLASS = "eu_act_outside_population";

/** Whether a refusal is R80's, for an EU act outside the EU population. */
export function outsideEuropePopulation(code, payload) {
  return code === "out_of_corpus_scope" && payload?.out_of_scope_class === OUTSIDE_EU_POPULATION_CLASS;
}

/**
 * The sentence a screen's table (English, keyed by code) says for a code, or the one naming a code it has none for; a work R92
 * withholds is said in the one sentence every screen says of it (`NAMES_WITHHELD_SENTENCE`), never the licence's, and an EU act
 * outside the EU population in its own (`OUTSIDE_EU_POPULATION_SENTENCE`), never the out-of-scope list's.
 */
export function tableRefusalSentence(table, code, unexpected, payload = null) {
  if (namesWithheld(code, payload)) return refusalSentence(NAMES_WITHHELD_SENTENCE);
  if (outsideEuropePopulation(code, payload)) return refusalSentence(OUTSIDE_EU_POPULATION_SENTENCE);
  return Object.hasOwn(table, code) ? refusalSentence(table[code]) : unexpected(code);
}

/**
 * A refusal whose card cannot be shown, as the screen (`search`, `reading`, ...) says it: its sentence naming
 * the refusal by its code, with the reason the card refused it, and then the hint the reader still needs, if any.
 * The reason is the card's own English message, so on a page in another language it is a run marked English
 * (`statement`). The English template is the screen's own; `unshown` is its key in a reviewed language.
 */
export function unshownRefusal(screen, englishTemplate, code, reason, hint = null) {
  const parts = fillParts(refusalTemplate(screen, "unshown", englishTemplate), { code, reason: englishRun(reason) });
  return statement(parts, hint === null ? [] : [` ${hint}`]);
}
