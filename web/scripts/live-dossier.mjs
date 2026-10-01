// The dossier screen, asked of the live API.
//
// The third screen that reads a served answer. It asks `dossier` with the work identifier the reader
// typed and, when one is chosen, a language, through the one client module, and turns what comes
// back into one view state. Every rule about what a dossier answer may say stays in
// `dossier-answer.mjs` (`readDossierAnswer`), and every rule about a refusal in `refusal-card.mjs`; this
// file builds the request, decides which of them a state goes to, and holds the few sentences a page
// needs for the states that carry no answer.
//
// An EU work's dossier is answered by the platform in another shape, read by its own reader
// (`readDossierAnswer` sends each answer to the reader its publisher names), never as a Luxembourg one.

import { askV3 } from "./v3-client.mjs";
import { readDossierAnswer } from "./dossier-answer.mjs";
import { validateRefusal } from "./refusal-card.mjs";
import { noCorpusMountedSentence } from "./live-refusals.mjs";
import { liveChrome } from "./live-chrome.mjs";

/**
 * The languages the form offers besides "any": the platform answers a language the work is not
 * held in with `language_not_available`, naming the ones it is, so the list is what a reader may
 * ask for, not a claim about what is held.
 */
export const DOSSIER_LANGUAGES = Object.freeze([
  Object.freeze({ code: "fra", label: liveChrome().common.languageNames.fra }),
  Object.freeze({ code: "deu", label: liveChrome().common.languageNames.deu }),
  Object.freeze({ code: "eng", label: liveChrome().common.languageNames.eng }),
]);

/**
 * The one sentence per refusal code a request from this page can meet besides `no_corpus_mounted`,
 * whose sentence names the missing index from its payload (`noCorpusMountedSentence`): a work the
 * index does not hold, a language the work is not held in, or an identifier that names more than one
 * held work. Any other code gets a sentence that names it.
 */
export const LIVE_DOSSIER_REFUSAL_SENTENCES = Object.freeze({
  identifier_unknown: "This index holds no work under that identifier.",
  language_not_available: "This work is not held in the language asked for.",
  ambiguous_identifier: "That identifier names more than one held work, so no dossier is chosen.",
});

export const LIVE_DOSSIER_IDLE = liveChrome().dossier.idle;
export const LIVE_DOSSIER_LOADING = liveChrome().common.loading;

export function unexpectedRefusalSentence(code) {
  return `The dossier was refused with ${code}.`;
}

export function transportFailureSentence(code) {
  if (code === "request_schema_invalid") {
    return "This server refused the dossier request as it was asked (request_schema_invalid).";
  }
  return `The dossier could not be reached (${code}).`;
}

export function unshownRefusalSentence(code, reason) {
  return `The dossier was refused with ${code}, and its card cannot be shown: ${reason}.`;
}

export function invalidAnswerSentence(reason) {
  return `The answer could not be read as a dossier: ${reason}.`;
}

/**
 * The request parameters for one dossier, checked before anything is sent: an identifier that is not
 * blank, sent as typed, and a language only when one is chosen from the list. Nothing else is sent.
 */
export function dossierParameters({ identifier, language = "" }) {
  if (typeof identifier !== "string" || identifier.trim().length === 0) {
    throw new Error("a dossier needs the identifier of a work");
  }
  if (language === "") return { identifier };
  if (!DOSSIER_LANGUAGES.some((offered) => offered.code === language)) {
    throw new Error(`${JSON.stringify(language)} is not a language this form offers`);
  }
  return { identifier, language };
}

/**
 * Maps what `askV3` returned to the view: `success` with the dossier view (read by
 * `readDossierAnswer`, whose `publisher` says which publisher's view it is), `refusal` with the
 * refusal card's inputs, or a state that carries a sentence.
 */
export function dossierOutcome(asked) {
  if (asked.state === "success") {
    try {
      return { state: "success", view: readDossierAnswer(asked.envelope.result.value), context: asked.envelope.context };
    } catch (error) {
      return { state: "invalid_envelope", sentence: invalidAnswerSentence(error.message) };
    }
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    const sentence = code === "no_corpus_mounted"
      ? noCorpusMountedSentence(payload)
      : LIVE_DOSSIER_REFUSAL_SENTENCES[code] ?? unexpectedRefusalSentence(code);
    try {
      validateRefusal({ code, sentence, payload });
    } catch (error) {
      return {
        state: "refusal",
        code,
        card: false,
        sentence: unshownRefusalSentence(code, error.message),
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

/** Asks the live API one dossier and returns the view state. */
export async function loadLiveDossier({ contract, fetchImpl, signal, request }) {
  const asked = await askV3("dossier", dossierParameters(request), { contract, objectType: "work_record", fetchImpl, signal });
  return dossierOutcome(asked);
}

/**
 * One reader's dossier requests on one screen: `ask` sends one (cancelling the one in flight) and
 * `cancel` abandons whatever is in flight. A request the form will not send is said as a state and
 * nothing is asked. Every state goes to `onOutcome`: loading when a request is sent, then what it
 * settled to. After a cancel nothing settles.
 */
export function createDossierSession({ contract, fetchImpl, onOutcome }) {
  let controller = null;
  return {
    ask(request) {
      controller?.abort();
      controller = null;
      try {
        dossierParameters(request);
      } catch (error) {
        onOutcome({ state: "invalid_request", sentence: `${error.message}.` });
        return false;
      }
      const own = new AbortController();
      controller = own;
      onOutcome({ state: "loading", sentence: LIVE_DOSSIER_LOADING });
      loadLiveDossier({ contract, fetchImpl, signal: own.signal, request })
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
