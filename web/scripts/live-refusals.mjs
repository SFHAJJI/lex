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

/** The sentence a screen's table (English, keyed by code) says for a code, or the one naming a code it has none for. */
export function tableRefusalSentence(table, code, unexpected) {
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
