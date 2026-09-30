// Sentences for refusals the live screens share.
//
// `no_corpus_mounted` names the corpus the request needed (`required_corpus`), and a server can hold
// one publisher's index and not the other's. "This build has no index mounted" is then false: the
// reader may have read a Luxembourg answer from this server a moment before asking about an EU work.
// So the sentence names the index that is missing, which is true whether the server holds the other
// publisher's index or none at all (review of #775).

const CORPUS_NAMES = Object.freeze({ lu: "Luxembourg", eu: "EU" });

/**
 * The hint an absence whose card cannot be shown still carries (review of #777): the date the held history
 * begins, so the reader can ask again. Said by the reading, export and compare pages.
 */
export function historyBeginsHint(date) {
  return `The history this index holds for this work begins on ${date}.`;
}

/** The hint for an article id no held state carries: the nearest ids the latest state does carry. */
export function nearestAnchorsHint(ids) {
  return `The nearest article ids this index holds are ${ids.join(", ")}.`;
}

/** The sentence for a `no_corpus_mounted` refusal, naming the index its payload says is missing. */
export function noCorpusMountedSentence(payload) {
  const name = CORPUS_NAMES[payload?.required_corpus];
  return name === undefined
    ? "This build has no index mounted for this request's publisher."
    : `This build has no ${name} index mounted.`;
}
