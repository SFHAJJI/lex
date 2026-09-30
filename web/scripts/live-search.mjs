// The search screen, asked of the live API.
//
// The second screen that reads a served answer. It asks `search` with the phrase the reader typed,
// the language chosen and, for a later page, the cursor the previous page handed over, through the
// one client module, and turns what comes back into one view state. Every rule about what a search
// answer may say stays in `search-answer.mjs` (`readSearch`), and every rule about a refusal in
// `refusal-card.mjs`; this file builds the request, decides which of them a state goes to, and
// holds the few sentences a page needs for the states that carry no answer.
//
// The request carries the query and the language and nothing else about the reader: the client
// sends no cookie and no referrer, and this file stores nothing. The query is sent as typed, never
// trimmed or folded, because the platform matches it byte for byte.

import { askV3 } from "./v3-client.mjs";
import { readSearch } from "./search-answer.mjs";
import { validateRefusal } from "./refusal-card.mjs";

/** The ceiling on a query's characters, the platform's own (`SearchMaxQueryCharacters`). */
export const SEARCH_QUERY_MAX = 512;

/** The ceiling on a query's distinct terms, the platform's own (`SearchMaxTerms`). */
export const SEARCH_TERMS_MAX = 32;

// What .NET's `string.Split(null)` splits on (`Char.IsWhiteSpace`): the Unicode space, line and
// paragraph separators and the control whitespace. Not JavaScript's `\s`, which also splits on
// U+FEFF, where the platform does not.
const PLATFORM_WHITESPACE = /[\p{Zs}\p{Zl}\p{Zp}\t\n\v\f\r\u0085]+/u;

/** The distinct terms the platform will count in a query, as it counts them. */
export function searchTerms(query) {
  return [...new Set(query.split(PLATFORM_WHITESPACE).filter((term) => term.length > 0))];
}

/**
 * The languages the form offers. A language the mount holds no text in is answered by the
 * platform (a `language_not_available` refusal naming the ones it holds), so the list is what a
 * reader may ask for, not a claim about what is held.
 */
export const SEARCH_LANGUAGES = Object.freeze([
  Object.freeze({ code: "fra", label: "French" }),
  Object.freeze({ code: "deu", label: "German" }),
  Object.freeze({ code: "eng", label: "English" }),
]);

/**
 * The one sentence per refusal code a request from this page can meet: no Luxembourg index
 * mounted (the refusal catalog's sentence, held equal by a test), or a language the mount holds no
 * searchable text in. Any other code gets a sentence that names it.
 */
export const LIVE_SEARCH_REFUSAL_SENTENCES = Object.freeze({
  no_corpus_mounted: "This build has no index mounted.",
  language_not_available: "This index holds no searchable text in the language asked for.",
});

export const LIVE_SEARCH_IDLE = "Type a phrase to search the article text this server holds.";
export const LIVE_SEARCH_LOADING = "Asking this server.";

export function unexpectedRefusalSentence(code) {
  return `The search was refused with ${code}.`;
}

export function transportFailureSentence(code) {
  // The server was reached and refused the request as asked: in practice a cursor from a result this
  // server no longer holds, since the form checks the phrase's own limits before sending it.
  if (code === "request_schema_invalid") {
    return "This server refused the search as it was asked (request_schema_invalid); ask it again from the first page.";
  }
  return `The search could not be reached (${code}).`;
}

export function unshownRefusalSentence(code, reason) {
  return `The search was refused with ${code}, and its card cannot be shown: ${reason}.`;
}

export function invalidAnswerSentence(reason) {
  return `The answer could not be read as a search answer: ${reason}.`;
}

/**
 * The request parameters for one search, checked before anything is sent: a query that is not
 * blank and within the platform's two ceilings (characters and distinct terms), a language the form
 * offers, and a cursor only when a later page is asked for. Nothing else is sent.
 */
export function searchParameters({ query, language, after = null }) {
  if (typeof query !== "string" || query.trim().length === 0) {
    throw new Error("a search needs a phrase to look for");
  }
  if (query.length > SEARCH_QUERY_MAX) {
    throw new Error(`a phrase is at most ${SEARCH_QUERY_MAX} characters`);
  }
  const terms = searchTerms(query).length;
  if (terms > SEARCH_TERMS_MAX) {
    throw new Error(`a phrase is at most ${SEARCH_TERMS_MAX} different words, and this one has ${terms}`);
  }
  if (!SEARCH_LANGUAGES.some((offered) => offered.code === language)) {
    throw new Error(`${JSON.stringify(language)} is not a language this form offers`);
  }
  if (after !== null && (typeof after !== "string" || after.length === 0)) {
    throw new Error("a later page is asked for with the cursor the previous page handed over");
  }
  return after === null ? { query, language } : { query, language, after };
}

/**
 * Maps what `askV3` returned to the view: `success` with the search view (read by `readSearch`),
 * `refusal` with the refusal card's inputs, or a state that carries a sentence.
 */
export function searchOutcome(asked) {
  if (asked.state === "success") {
    try {
      return { state: "success", view: readSearch(asked.envelope.result.value), context: asked.envelope.context };
    } catch (error) {
      return { state: "invalid_envelope", sentence: invalidAnswerSentence(error.message) };
    }
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    const sentence = LIVE_SEARCH_REFUSAL_SENTENCES[code] ?? unexpectedRefusalSentence(code);
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

/** Asks the live API one search and returns the view state. */
export async function loadLiveSearch({ contract, fetchImpl, signal, request }) {
  const asked = await askV3("search", searchParameters(request), { contract, objectType: "quote", fetchImpl, signal });
  return searchOutcome(asked);
}

/**
 * Asks one search, hands the view state to `onOutcome` when it settles, and returns the cancel the
 * screen calls when the reader asks another search or the screen unmounts. After cancel the request
 * is aborted and `onOutcome` is never called; a failure is handed over as a state, never retried.
 */
export function startLiveSearch({ contract, fetchImpl, request, onOutcome }) {
  const controller = new AbortController();
  loadLiveSearch({ contract, fetchImpl, signal: controller.signal, request })
    .then((next) => {
      if (!controller.signal.aborted) onOutcome(next);
    })
    .catch((error) => {
      if (error?.name !== "AbortError" && !controller.signal.aborted) {
        onOutcome({ state: "invalid_envelope", sentence: invalidAnswerSentence(String(error?.message ?? error)) });
      }
    });
  return () => controller.abort();
}

/**
 * One reader's searches on one screen: `ask` sends a search (cancelling the one in flight), `next`
 * asks the page after the last one with the cursor that page handed over, and `cancel` abandons
 * whatever is in flight. A request the form will not send is said as a state and nothing is asked.
 * Every state goes to `onOutcome`: loading when a search is sent, then what it settled to.
 */
export function createSearchSession({ contract, fetchImpl, onOutcome }) {
  let cancelInFlight = null;
  let last = null;
  const session = {
    ask(request) {
      cancelInFlight?.();
      cancelInFlight = null;
      try {
        searchParameters(request);
      } catch (error) {
        onOutcome({ state: "invalid_request", sentence: `${error.message}.` });
        return false;
      }
      last = request;
      onOutcome({ state: "loading", sentence: LIVE_SEARCH_LOADING });
      cancelInFlight = startLiveSearch({ contract, fetchImpl, request, onOutcome });
      return true;
    },
    next(after) {
      return last !== null && session.ask({ ...last, after });
    },
    cancel() {
      cancelInFlight?.();
      cancelInFlight = null;
    },
  };
  return session;
}
