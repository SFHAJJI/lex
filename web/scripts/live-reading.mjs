// The reading screen, asked of the live API.
//
// The fourth screen that reads a served answer. It asks `evidence_bundle` with the work identifier
// and the date the reader typed and, when one is chosen, a language, through the one client module,
// and turns what comes back into one view state. Every rule about what a bundle may say stays in
// `reading-answer.mjs` (`readEvidenceBundle`), and every rule about a refusal in `refusal-card.mjs`;
// this file builds the request, decides which of them a state goes to, and holds the sentences a page
// needs for the states that carry no answer.

import { askV3 } from "./v3-client.mjs";
import { readEvidenceBundle } from "./reading-answer.mjs";
import { validateRefusal } from "./refusal-card.mjs";
import { noCorpusMountedSentence, historyBeginsHint } from "./live-refusals.mjs";
import { isCalendarDate } from "./temporal.mjs";

/**
 * The languages the form offers besides "any": the platform answers a language the work is not held
 * in with `language_not_available`, naming the ones it is.
 */
export const READING_LANGUAGES = Object.freeze([
  Object.freeze({ code: "fra", label: "French" }),
  Object.freeze({ code: "deu", label: "German" }),
  Object.freeze({ code: "eng", label: "English" }),
]);

/**
 * The language tag a quotation carries, from the platform's ISO 639-2 code: the shortest ISO 639
 * code, which is what BCP 47 asks. A code with no two-letter form is carried as it is.
 */
export const QUOTATION_LANGUAGE_TAGS = Object.freeze({ fra: "fr", deu: "de", eng: "en", ltz: "lb" });

export function quotationLanguageTag(code) {
  return QUOTATION_LANGUAGE_TAGS[code] ?? code;
}

/**
 * The one sentence per refusal code a request from this page can meet besides `no_corpus_mounted`,
 * whose sentence names the missing index from its payload. Any other code gets a sentence that names
 * it.
 */
export const LIVE_READING_REFUSAL_SENTENCES = Object.freeze({
  identifier_unknown: "This index holds no work under that identifier.",
  language_not_available: "This work is not held in the language asked for.",
  no_version_for_date: "No state of this work that this index holds applies on that date.",
  ambiguous_version: "Several states of this work apply on that date, and none is chosen.",
  text_withheld: "This state's text is withheld: its rights did not admit it.",
  text_not_available: "This index holds this state but no text for it.",
  retrieval_mode_unavailable: "This index cannot read this work's text.",
});

export const LIVE_READING_IDLE = "Type a work identifier and a date to read the text that applied on it.";
export const LIVE_READING_LOADING = "Asking this server.";

export function unexpectedRefusalSentence(code) {
  return `The reading was refused with ${code}.`;
}

export function transportFailureSentence(code) {
  if (code === "request_schema_invalid") {
    return "This server refused the reading request as it was asked (request_schema_invalid).";
  }
  return `The reading could not be reached (${code}).`;
}

export function unshownRefusalSentence(code, reason) {
  return `The reading was refused with ${code}, and its card cannot be shown: ${reason}.`;
}

export function invalidAnswerSentence(reason) {
  return `The answer could not be read as the text of a state: ${reason}.`;
}

/**
 * The request parameters for one reading, checked before anything is sent: an identifier that is not
 * blank, sent as typed; a calendar date written yyyy-mm-dd; and a language only when one is chosen.
 * Nothing else is sent.
 */
export function readingParameters({ identifier, date, language = "" }) {
  if (typeof identifier !== "string" || identifier.trim().length === 0) {
    throw new Error("a reading needs the identifier of a work");
  }
  if (!isCalendarDate(date)) {
    throw new Error("a reading needs a calendar date, written yyyy-mm-dd");
  }
  if (language === "") return { identifier, date };
  if (!READING_LANGUAGES.some((offered) => offered.code === language)) {
    throw new Error(`${JSON.stringify(language)} is not a language this form offers`);
  }
  return { identifier, date, language };
}

/**
 * Maps what `askV3` returned to the view: `success` with the reading view (read by
 * `readEvidenceBundle`), `refusal` with the refusal card's inputs, or a state that carries a sentence.
 */
export function readingOutcome(asked) {
  if (asked.state === "success") {
    try {
      return { state: "success", view: readEvidenceBundle(asked.envelope.result.value), context: asked.envelope.context };
    } catch (error) {
      return { state: "invalid_envelope", sentence: invalidAnswerSentence(error.message) };
    }
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    const sentence = code === "no_corpus_mounted"
      ? noCorpusMountedSentence(payload)
      : LIVE_READING_REFUSAL_SENTENCES[code] ?? unexpectedRefusalSentence(code);
    try {
      validateRefusal({ code, sentence, payload });
    } catch (error) {
      // The date a reader needs to ask again travels with the refusal even when its card cannot be
      // shown (review of #777): the work's history as this index holds it begins on history_begins.
      const retry = code === "no_version_for_date" && isCalendarDate(payload?.history_begins)
        ? ` ${historyBeginsHint(payload.history_begins)}`
        : "";
      return {
        state: "refusal",
        code,
        card: false,
        sentence: `${unshownRefusalSentence(code, error.message)}${retry}`,
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

/** Asks the live API one reading and returns the view state. */
export async function loadLiveReading({ contract, fetchImpl, signal, request }) {
  const asked = await askV3("evidence_bundle", readingParameters(request), { contract, objectType: "evidence_bundle", fetchImpl, signal });
  return readingOutcome(asked);
}

/**
 * One reader's reading requests on one screen: `ask` sends one (cancelling the one in flight) and
 * `cancel` abandons whatever is in flight. A request the form will not send is said as a state and
 * nothing is asked. After a cancel nothing settles.
 */
export function createReadingSession({ contract, fetchImpl, onOutcome }) {
  let controller = null;
  return {
    ask(request) {
      controller?.abort();
      controller = null;
      try {
        readingParameters(request);
      } catch (error) {
        onOutcome({ state: "invalid_request", sentence: `${error.message}.` });
        return false;
      }
      const own = new AbortController();
      controller = own;
      onOutcome({ state: "loading", sentence: LIVE_READING_LOADING });
      loadLiveReading({ contract, fetchImpl, signal: own.signal, request })
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
