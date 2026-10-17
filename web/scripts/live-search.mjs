// The search screen, asked of the live API.
//
// The second screen that reads a served answer. It asks `search` with the phrase the reader typed,
// the language chosen, the work to search within if the reader named one and, for a later page, the
// cursor the previous page handed over, through the one client module, and turns what comes back
// into one view state. Every rule about what a search answer may say stays in `search-answer.mjs`
// (`readSearchAnswer`: Luxembourg's answer, or the EU's in one named work), and every rule about a
// refusal in `refusal-card.mjs`; this file builds the request, decides which of them a state goes
// to, and holds the few sentences a page needs for the states that carry no answer.
//
// The request carries the query, the language, the work identifier, if any, and the date, if any, and
// nothing else about the reader: the client sends no cookie and no referrer, and this file stores
// nothing. The query is sent as typed, never trimmed or folded, because the platform matches it byte
// for byte; so is the identifier, as the dossier screen sends it. An EU work is searched only within
// it: the platform serves no search across EU works, so an EU answer comes from naming the work.
//
// The date is the platform's own `date` parameter, the one date mechanism a search has: with it, each
// Luxembourg work contributes the version that applies on that date (or, named, is answered as `as_of`
// answers it), and the answer echoes it as `requested_date`, which the page prints. The home page's
// date field starts at today, the calendar date in Luxembourg (R13; `todayInLuxembourg`, by the coded
// rule of `temporal.mjs`); the search page's starts empty, because an EU act is searched in the one
// wording held and the platform refuses it a date (`retrieval_mode_unavailable`), which the page says.
//
// Exact references first (the plan's item 4.2): the platform reads the query as an exact reference before any word is
// searched (an ELI, a CELEX, a Mémorial citation, a permalink, a citation of an article of an act at a date), and a
// reference it holds answers with the address its act opens at. That is the `opens` state, which carries the address as a
// link on this page (`href`); the screen goes there. A reference that opens nothing (a date that names no calendar day,
// two dates that disagree) is `not_opened`; a refusal of the phase carries what was recognised (`recognition`) and is
// said with the phase's own sentences (`LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES`, or for an EU act, as the refusal's
// context names its publisher, `LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES`), so the page says what it read. A
// reference that may name several acts lists each (`candidates`), linked when this site holds it, and opens none.

import { askV3 } from "./v3-client.mjs";
import { readReferenceCandidates, readRefusalRecognition, readSearchAnswer } from "./search-answer.mjs";
import { LIVE_READING_EUROPE_REFUSAL_SENTENCES } from "./live-reading.mjs";
import { validateRefusal } from "./refusal-card.mjs";
import { noCorpusMountedSentence, tableRefusalSentence, unshownRefusal } from "./live-refusals.mjs";
import { CHROME_LOCALE, englishStatement, fillText, liveChrome, refusalTemplate } from "./live-chrome.mjs";
import { permalinkHref } from "./permalinks.mjs";
import { isCalendarDate, luxembourgDate } from "./temporal.mjs";

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
  Object.freeze({ code: "fra", label: liveChrome().common.languageNames.fra }),
  Object.freeze({ code: "deu", label: liveChrome().common.languageNames.deu }),
  Object.freeze({ code: "eng", label: liveChrome().common.languageNames.eng }),
]);

/**
 * The one sentence per refusal code a request from this page can meet besides `no_corpus_mounted`,
 * whose sentence names the missing index from its payload (`noCorpusMountedSentence`): a language
 * the mount (or the named work) holds no searchable text in, a work identifier the indexes do not
 * hold, and one that names more than one held work or expression. Any other code gets a sentence
 * that names it.
 */
export const LIVE_SEARCH_REFUSAL_SENTENCES = Object.freeze({
  language_not_available: "This index holds no searchable text in the language asked for.",
  identifier_unknown: "This index holds no work under that identifier.",
  ambiguous_identifier: "That identifier names more than one held work or expression, so none is searched.",
  // A named Luxembourg work and a date are `as_of`'s question, and these are its refusals, said as the reading says them.
  no_version_for_date: "No state of this work that this index holds applies on that date.",
  ambiguous_version: "Several states of this work apply on that date, and none is chosen.",
  // An EU work and a date: the search reads the one wording held, and the platform refuses it a date.
  retrieval_mode_unavailable: "An EU act is searched in the wording this server holds, not at a date: leave the date empty to search it.",
});

/**
 * The sentence per refusal code of a search whose query was read as an exact reference to a Luxembourg act (the payload's
 * `recognised_*` members say what was read): the act it names is not held, it may name several (held or not: the review
 * of #974 found the list names acts this site does not hold), the version the date selects holds no article of that
 * number, or the act is not held in the language asked. A date before the history and several versions on the date are
 * said with the search's own sentences above.
 */
export const LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES = Object.freeze({
  identifier_unknown: "This site holds no act the reference names, so nothing was opened and no word was searched.",
  ambiguous_identifier: "The reference names more than one act or article, and none is chosen.",
  anchor_not_in_version: "The version the date selects holds no article with that number, and no other article is opened in its place.",
  language_not_available: "This work is not held in the language asked for.",
  no_version_for_date: LIVE_SEARCH_REFUSAL_SENTENCES.no_version_for_date,
  ambiguous_version: LIVE_SEARCH_REFUSAL_SENTENCES.ambiguous_version,
});

/**
 * The same refusals of a reference to an EU act (the refusal's context names eu-eurlex; the review of #974, F4): an EU
 * date is the date of a wording, never an applicability date, so the time view's refusals are said as the reading says
 * them for an EU act (`LIVE_READING_EUROPE_REFUSAL_SENTENCES`), the wording the date selects holds no article of that
 * number, and an act held outside the census answers no date.
 */
export const LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES = Object.freeze({
  identifier_unknown: LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES.identifier_unknown,
  ambiguous_identifier: LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES.ambiguous_identifier,
  anchor_not_in_version: "The wording the date selects holds no article with that number, and no other article is opened in its place.",
  language_not_available: LIVE_READING_EUROPE_REFUSAL_SENTENCES.language_not_available,
  no_version_for_date: LIVE_READING_EUROPE_REFUSAL_SENTENCES.no_version_for_date,
  ambiguous_version: LIVE_READING_EUROPE_REFUSAL_SENTENCES.ambiguous_version,
  text_not_available: LIVE_READING_EUROPE_REFUSAL_SENTENCES.text_not_available,
  retrieval_mode_unavailable: "This server holds this EU act in one wording only, and answers no date for it: leave the date out of the reference.",
});

/** The phase's sentences for a refusal, by the publisher its context names. */
export function referenceRefusalSentences(context) {
  return context?.publisher === "eu-eurlex" ? LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES : LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES;
}

export const LIVE_SEARCH_IDLE = liveChrome().search.idle;
export const LIVE_SEARCH_LOADING = liveChrome().common.loading;

const UNEXPECTED = "The search was refused with {code}.";
const UNSHOWN = "The search was refused with {code}, and its card cannot be shown: {reason}.";

export function unexpectedRefusalSentence(code) {
  return fillText(refusalTemplate("search", "unexpected", UNEXPECTED), { code });
}

// A transport failure, an answer this page cannot read and a request it will not send have no reviewed
// wording but the English: a page in another language says them in English, marked English
// (`englishStatement`).
export function transportFailureSentence(code) {
  // The server was reached and refused the request as asked: in practice a cursor from a result this
  // server no longer holds, since the form checks the phrase's own limits before sending it.
  if (code === "request_schema_invalid") {
    return "This server refused the search as it was asked (request_schema_invalid); ask it again from the first page.";
  }
  return `The search could not be reached (${code}).`;
}

export function unshownRefusalSentence(code, reason) {
  return unshownRefusal("search", UNSHOWN, code, reason).sentence;
}

export function invalidAnswerSentence(reason) {
  return `The answer could not be read as a search answer: ${reason}.`;
}

/**
 * A calendar date (yyyy-mm-dd) as the page's language writes it in words, so a reference's date is printed back as it was
 * read (15/03/2021 is 15 March 2021, never 3 May): the day (French writes the first "1er"), the month's name and the year.
 */
export function longDate(date, locale = CHROME_LOCALE) {
  if (!isCalendarDate(date)) throw new Error(`${JSON.stringify(date)} is not a calendar date`);
  const common = liveChrome(locale).common;
  const [year, month, day] = date.split("-").map(Number);
  return fillText(common.longDate, {
    day: day === 1 ? fillText(common.firstDay, { day: "1" }) : String(day),
    month: common.months[month - 1],
    year: String(year),
  });
}

/**
 * Today, the calendar date in Luxembourg at `now` (milliseconds, the browser's clock where no server said it), by the
 * coded rule (`luxembourgDate`, the specification of record's R13), never the build's time; null for a clock outside the
 * years the rule holds, which then names no date rather than a wrong one.
 */
export function todayInLuxembourg(now = Date.now()) {
  try {
    return luxembourgDate(new Date(now).toISOString());
  } catch {
    return null;
  }
}

/**
 * The request parameters for one search, checked before anything is sent: a query that is not
 * blank and within the platform's two ceilings (characters and distinct terms), a language the form
 * offers, a work identifier only when the reader named one (a blank one is no identifier), a date
 * only when the field holds one, which must then be a calendar date written yyyy-mm-dd (a blank one
 * is no date), and a cursor only when a later page is asked for. Nothing else is sent.
 */
export function searchParameters({ query, language, identifier = "", date = "", after = null }) {
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
  if (typeof identifier !== "string") {
    throw new Error("a work identifier is text");
  }
  if (typeof date !== "string") {
    throw new Error("a date is text");
  }
  const dated = date.trim().length > 0;
  if (dated && !isCalendarDate(date)) {
    throw new Error("a search's date is a calendar date, written yyyy-mm-dd");
  }
  if (after !== null && (typeof after !== "string" || after.length === 0)) {
    throw new Error("a later page is asked for with the cursor the previous page handed over");
  }
  return {
    query,
    language,
    ...(identifier.trim().length === 0 ? {} : { identifier }),
    ...(dated ? { date } : {}),
    ...(after === null ? {} : { after }),
  };
}

/**
 * Maps what `askV3` returned to the view: `success` with the search view (read by `readSearchAnswer`,
 * whose `publisher` says which publisher's view it is), `refusal` with the refusal card's inputs, or a
 * state that carries a sentence.
 */
export function searchOutcome(asked) {
  if (asked.state === "success") {
    let view;
    try {
      view = readSearchAnswer(asked.envelope.result.value);
    } catch (error) {
      return { state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(error.message)) };
    }
    if (view.kind === "reference") {
      // The act opens at its address on this site, under this page's interface language; the screen goes there.
      return view.outcome === "opens"
        ? { state: "opens", view, href: permalinkHref(view.opens.address, CHROME_LOCALE), context: asked.envelope.context }
        : { state: "not_opened", view, context: asked.envelope.context };
    }
    return { state: "success", view, context: asked.envelope.context };
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    let recognition = null;
    let candidates = null;
    try {
      recognition = readRefusalRecognition(payload);
      // The acts a reference may name, each linked under this page's language when this site holds it; none is chosen.
      candidates = recognition !== null && code === "ambiguous_identifier"
        ? readReferenceCandidates(payload).map((candidate) => Object.freeze({
          ...candidate,
          href: candidate.held === true ? permalinkHref(candidate.identifier, CHROME_LOCALE) : null,
        }))
        : null;
    } catch (error) {
      return { state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(error.message)) };
    }
    const table = recognition === null ? LIVE_SEARCH_REFUSAL_SENTENCES : referenceRefusalSentences(asked.envelope.context);
    const sentence = code === "no_corpus_mounted"
      ? noCorpusMountedSentence(payload)
      : tableRefusalSentence(table, code, unexpectedRefusalSentence);
    try {
      validateRefusal({ code, sentence, payload });
    } catch (error) {
      return {
        state: "refusal",
        code,
        card: false,
        recognition,
        ...unshownRefusal("search", UNSHOWN, code, error.message),
        context: asked.envelope.context,
      };
    }
    return { state: "refusal", code, card: true, sentence, payload, recognition, candidates, context: asked.envelope.context };
  }

  if (asked.state === "transport_failure") {
    return { state: "transport_failure", ...englishStatement(transportFailureSentence(asked.code)) };
  }

  return { state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(asked.reason)) };
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
        onOutcome({ state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(String(error?.message ?? error))) });
      }
    });
  return () => controller.abort();
}

/**
 * One reader's searches on one screen: `ask` sends a search (cancelling the one in flight), `next`
 * asks the page after the last one with the cursor that page handed over, `retry` asks the last
 * search sent again (the failure panel's retry, R60), and `cancel` abandons whatever is in flight.
 * A request the form will not send is said as a state and nothing is asked.
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
        onOutcome({ state: "invalid_request", ...englishStatement(`${error.message}.`) });
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
    retry() {
      return last !== null && session.ask(last);
    },
    cancel() {
      cancelInFlight?.();
      cancelInFlight = null;
    },
  };
  return session;
}
