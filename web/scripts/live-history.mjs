// The provision history screen, asked of the live API.
//
// The fifth screen that reads a served answer. It asks `article_history` with the work identifier
// and the publisher's article id the reader typed and, when one is chosen, a language, through the
// one client module, and turns what comes back into one view state. Every rule about what a lineage
// may say stays in `history-answer.mjs` (`readArticleHistory`), and every rule about a refusal in
// `refusal-card.mjs`; this file builds the request, decides which of them a state goes to, and holds
// the sentences a page needs for the states that carry no answer.

import { askV3 } from "./v3-client.mjs";
import { readArticleHistory } from "./history-answer.mjs";
import { validateRefusal } from "./refusal-card.mjs";
import { nearestAnchorsHint, noCorpusMountedSentence, tableRefusalSentence, unshownRefusal } from "./live-refusals.mjs";
import { englishStatement, fillText, liveChrome, refusalTemplate } from "./live-chrome.mjs";

/**
 * The languages the form offers besides "any": the platform answers a language the work is not held
 * in with `language_not_available`, naming the ones it is.
 */
export const HISTORY_LANGUAGES = Object.freeze([
  Object.freeze({ code: "fra", label: liveChrome().common.languageNames.fra }),
  Object.freeze({ code: "deu", label: liveChrome().common.languageNames.deu }),
  Object.freeze({ code: "eng", label: liveChrome().common.languageNames.eng }),
]);

/**
 * The one sentence per refusal code a request from this page can meet besides `no_corpus_mounted`,
 * whose sentence names the missing index from its payload. Any other code gets a sentence that names
 * it.
 */
export const LIVE_HISTORY_REFUSAL_SENTENCES = Object.freeze({
  identifier_unknown: "This index holds no work under that identifier.",
  language_not_available: "This work is not held in the language asked for.",
  anchor_not_in_version: "No state of this work that this index holds carries that article id.",
  retrieval_mode_unavailable: "This index cannot trace this work's articles.",
});

export const LIVE_HISTORY_IDLE = liveChrome().history.idle;
export const LIVE_HISTORY_LOADING = liveChrome().common.loading;

const UNEXPECTED = "The provision history was refused with {code}.";
const UNSHOWN = "The provision history was refused with {code}, and its card cannot be shown: {reason}.";

export function unexpectedRefusalSentence(code) {
  return fillText(refusalTemplate("history", "unexpected", UNEXPECTED), { code });
}

// A transport failure, an answer this page cannot read and a request it will not send have no reviewed
// wording but the English: a page in another language says them in English, marked English
// (`englishStatement`).
export function transportFailureSentence(code) {
  if (code === "request_schema_invalid") {
    return "This server refused the provision history request as it was asked (request_schema_invalid).";
  }
  return `The provision history could not be reached (${code}).`;
}

export function unshownRefusalSentence(code, reason) {
  return unshownRefusal("history", UNSHOWN, code, reason).sentence;
}

export function invalidAnswerSentence(reason) {
  return `The answer could not be read as a provision history: ${reason}.`;
}

/**
 * The request parameters for one provision history, checked before anything is sent: an identifier
 * and an article id that are not blank, each sent as typed, and a language only when one is chosen.
 * Nothing else is sent.
 */
export function historyParameters({ identifier, anchor, language = "" }) {
  if (typeof identifier !== "string" || identifier.trim().length === 0) {
    throw new Error("a provision history needs the identifier of a work");
  }
  if (typeof anchor !== "string" || anchor.trim().length === 0) {
    throw new Error("a provision history needs the publisher's article id");
  }
  if (language === "") return { identifier, anchor };
  if (!HISTORY_LANGUAGES.some((offered) => offered.code === language)) {
    throw new Error(`${JSON.stringify(language)} is not a language this form offers`);
  }
  return { identifier, anchor, language };
}

/**
 * What an absence whose card cannot be shown still carries for the reader: for an article id no
 * held state carries, the nearest ids the latest state does carry (the payload's `nearest_anchors`).
 */
function retryHint(code, payload) {
  if (code === "anchor_not_in_version" && Array.isArray(payload?.nearest_anchors) && payload.nearest_anchors.length > 0
    && payload.nearest_anchors.every((anchor) => typeof anchor === "string" && anchor.length > 0)) {
    return nearestAnchorsHint(payload.nearest_anchors);
  }
  return null;
}

/**
 * Maps what `askV3` returned to the view: `success` with the lineage view (read by
 * `readArticleHistory`), `refusal` with the refusal card's inputs, or a state that carries a sentence.
 */
export function historyOutcome(asked) {
  if (asked.state === "success") {
    try {
      return { state: "success", view: readArticleHistory(asked.envelope.result.value), context: asked.envelope.context };
    } catch (error) {
      return { state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(error.message)) };
    }
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    const sentence = code === "no_corpus_mounted"
      ? noCorpusMountedSentence(payload)
      : tableRefusalSentence(LIVE_HISTORY_REFUSAL_SENTENCES, code, unexpectedRefusalSentence);
    try {
      validateRefusal({ code, sentence, payload });
    } catch (error) {
      return {
        state: "refusal",
        code,
        card: false,
        ...unshownRefusal("history", UNSHOWN, code, error.message, retryHint(code, payload)),
        context: asked.envelope.context,
      };
    }
    return { state: "refusal", code, card: true, sentence, payload, context: asked.envelope.context };
  }

  if (asked.state === "transport_failure") {
    return { state: "transport_failure", ...englishStatement(transportFailureSentence(asked.code)) };
  }

  return { state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(asked.reason)) };
}

/** Asks the live API one provision history and returns the view state. */
export async function loadLiveHistory({ contract, fetchImpl, signal, request }) {
  const asked = await askV3("article_history", historyParameters(request), { contract, objectType: "provision_history", fetchImpl, signal });
  return historyOutcome(asked);
}

/**
 * One reader's provision history requests on one screen: `ask` sends one (cancelling the one in
 * flight) and `cancel` abandons whatever is in flight. A request the form will not send is said as a
 * state and nothing is asked. After a cancel nothing settles.
 */
export function createHistorySession({ contract, fetchImpl, onOutcome }) {
  let controller = null;
  return {
    ask(request) {
      controller?.abort();
      controller = null;
      try {
        historyParameters(request);
      } catch (error) {
        onOutcome({ state: "invalid_request", ...englishStatement(`${error.message}.`) });
        return false;
      }
      const own = new AbortController();
      controller = own;
      onOutcome({ state: "loading", sentence: LIVE_HISTORY_LOADING });
      loadLiveHistory({ contract, fetchImpl, signal: own.signal, request })
        .then((next) => {
          if (!own.signal.aborted) onOutcome(next);
        })
        .catch((error) => {
          if (error?.name !== "AbortError" && !own.signal.aborted) {
            onOutcome({ state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(String(error?.message ?? error))) });
          }
        });
      return true;
    },
    cancel() {
      controller?.abort();
      controller = null;
    },
  };
}
