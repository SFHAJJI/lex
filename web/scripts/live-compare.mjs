// The compare screen, asked of the live API.
//
// The sixth screen that reads a served answer. It asks `diff` with the work identifier and the two
// dates the reader typed and, when one is chosen, a language, through the one client module, and
// turns what comes back into one view state. Every rule about what a comparison may say stays in
// `compare-answer.mjs` (`readDiff`), and every rule about a refusal in `refusal-card.mjs`; this file
// builds the request, decides which of them a state goes to, and holds the sentences a page needs for
// the states that carry no answer.

import { askV3 } from "./v3-client.mjs";
import { readDiff } from "./compare-answer.mjs";
import { validateRefusal } from "./refusal-card.mjs";
import { noCorpusMountedSentence, historyBeginsHint } from "./live-refusals.mjs";
import { isCalendarDate } from "./temporal.mjs";

/**
 * The languages the form offers besides "any": the platform answers a language the work is not held
 * in with `language_not_available`, naming the ones it is.
 */
export const COMPARE_LANGUAGES = Object.freeze([
  Object.freeze({ code: "fra", label: "French" }),
  Object.freeze({ code: "deu", label: "German" }),
  Object.freeze({ code: "eng", label: "English" }),
]);

/**
 * The one sentence per refusal code a request from this page can meet besides `no_corpus_mounted`,
 * whose sentence names the missing index from its payload. Any other code gets a sentence that names
 * it.
 */
export const LIVE_COMPARE_REFUSAL_SENTENCES = Object.freeze({
  identifier_unknown: "This index holds no work under that identifier.",
  language_not_available: "This work is not held in the language asked for.",
  no_version_for_date: "No state of this work that this index holds applies on one of the dates.",
  ambiguous_version: "Several states of this work apply on one of the dates, and none is chosen.",
  profiles_differ: "The two states were read under different rule profiles, so they are not compared.",
  retrieval_mode_unavailable: "This index cannot compare this work's states.",
});

export const LIVE_COMPARE_IDLE = "Type a work identifier and two dates to compare the states that applied on them.";
export const LIVE_COMPARE_LOADING = "Asking this server.";

export function unexpectedRefusalSentence(code) {
  return `The comparison was refused with ${code}.`;
}

export function transportFailureSentence(code) {
  if (code === "request_schema_invalid") {
    return "This server refused the comparison as it was asked (request_schema_invalid).";
  }
  return `The comparison could not be reached (${code}).`;
}

export function unshownRefusalSentence(code, reason) {
  return `The comparison was refused with ${code}, and its card cannot be shown: ${reason}.`;
}

export function invalidAnswerSentence(reason) {
  return `The answer could not be read as a comparison: ${reason}.`;
}

/**
 * The request parameters for one comparison, checked before anything is sent: an identifier that is
 * not blank, sent as typed; two calendar dates written yyyy-mm-dd; and a language only when one is
 * chosen. Nothing else is sent.
 */
export function compareParameters({ identifier, dateFrom, dateTo, language = "" }) {
  if (typeof identifier !== "string" || identifier.trim().length === 0) {
    throw new Error("a comparison needs the identifier of a work");
  }
  if (!isCalendarDate(dateFrom) || !isCalendarDate(dateTo)) {
    throw new Error("a comparison needs two calendar dates, written yyyy-mm-dd");
  }
  const parameters = { identifier, date_from: dateFrom, date_to: dateTo };
  if (language === "") return parameters;
  if (!COMPARE_LANGUAGES.some((offered) => offered.code === language)) {
    throw new Error(`${JSON.stringify(language)} is not a language this form offers`);
  }
  return { ...parameters, language };
}

/** What an absence whose card cannot be shown still carries: the date the held history begins. */
function retryHint(code, payload) {
  return code === "no_version_for_date" && isCalendarDate(payload?.history_begins)
    ? ` ${historyBeginsHint(payload.history_begins)}`
    : "";
}

/**
 * Maps what `askV3` returned to the view: `success` with the comparison view (read by `readDiff`),
 * `refusal` with the refusal card's inputs, or a state that carries a sentence.
 */
export function compareOutcome(asked) {
  if (asked.state === "success") {
    try {
      return { state: "success", view: readDiff(asked.envelope.result.value), context: asked.envelope.context };
    } catch (error) {
      return { state: "invalid_envelope", sentence: invalidAnswerSentence(error.message) };
    }
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    const sentence = code === "no_corpus_mounted"
      ? noCorpusMountedSentence(payload)
      : LIVE_COMPARE_REFUSAL_SENTENCES[code] ?? unexpectedRefusalSentence(code);
    try {
      validateRefusal({ code, sentence, payload });
    } catch (error) {
      return {
        state: "refusal",
        code,
        card: false,
        sentence: `${unshownRefusalSentence(code, error.message)}${retryHint(code, payload)}`,
        context: asked.envelope.context,
      };
    }
    return { state: "refusal", code, card: true, sentence, payload, context: asked.envelope.context };
  }

  if (asked.state === "transport_failure") {
    return { state: "transport_failure", sentence: transportFailureSentence(asked.code) };
  }

  return { state: "invalid_envelope", sentence: invalidAnswerSentence(asked.reason) };
}

/** Asks the live API one comparison and returns the view state. */
export async function loadLiveCompare({ contract, fetchImpl, signal, request }) {
  const asked = await askV3("diff", compareParameters(request), { contract, objectType: "diff", fetchImpl, signal });
  return compareOutcome(asked);
}

/**
 * One reader's comparisons on one screen: `ask` sends one (cancelling the one in flight) and `cancel`
 * abandons whatever is in flight. A request the form will not send is said as a state and nothing is
 * asked. After a cancel nothing settles.
 */
export function createCompareSession({ contract, fetchImpl, onOutcome }) {
  let controller = null;
  return {
    ask(request) {
      controller?.abort();
      controller = null;
      try {
        compareParameters(request);
      } catch (error) {
        onOutcome({ state: "invalid_request", sentence: `${error.message}.` });
        return false;
      }
      const own = new AbortController();
      controller = own;
      onOutcome({ state: "loading", sentence: LIVE_COMPARE_LOADING });
      loadLiveCompare({ contract, fetchImpl, signal: own.signal, request })
        .then((next) => {
          if (!own.signal.aborted) onOutcome(next);
        })
        .catch((error) => {
          if (error?.name !== "AbortError" && !own.signal.aborted) {
            onOutcome({ state: "invalid_envelope", sentence: invalidAnswerSentence(String(error?.message ?? error)) });
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
