// The Changes page, asked of the live API (the owner's brief of 2026-11-15, V4Changes).
//
// It asks `changes_in_period` for a period, through the one client module, and turns what comes back into one view state;
// it asks `dossier` for the own title of each text it shows (the brief's trap 2), a few at a time, for the rows on screen
// only. Every rule about what a radar may say stays in `radar-answer.mjs` (`readChanges`), every rule about a refusal in
// `refusal-card.mjs`; this file builds the requests, decides the period and its windows, reads a text's own title, and
// holds the sentences a page needs for the states that carry no answer.
//
// The period. The presets (the last 30 days, this year, last year) count back from the newest date the corpus holds a
// version of when that is before today in Luxembourg, and from today otherwise (`anchorOf`): a corpus that stopped before
// today would otherwise show an empty "last 30 days". The page says which date it counted from.
//
// The pages. The API serves a window's rows oldest first and continues later (`continue_from`); the page shows the newest
// first and goes back in time with "Show earlier changes". So a period is asked in windows of 30 days, the latest first
// (`windowsOf`); a window holding more versions than one answer serves is completed by its `continue_from` (at most
// `PAGES_PER_WINDOW` answers); "Show earlier changes" shows the next rows already held, and asks the next window back when
// they run out. The rows are shown `ROWS_PER_PAGE` at a time, and only their titles are asked: the first screen asks one
// changes_in_period and at most `ROWS_PER_PAGE` dossiers.

import { askV3 } from "./v3-client.mjs";
import { readChanges } from "./radar-answer.mjs";
import { validateRefusal } from "./refusal-card.mjs";
import { noCorpusMountedSentence, tableRefusalSentence, unshownRefusal } from "./live-refusals.mjs";
import { isCalendarDate } from "./temporal.mjs";
import { CHROME_LOCALE, countedEntry, englishStatement, fillText, liveChrome, refusalTemplate } from "./live-chrome.mjs";

/**
 * The languages a request may name besides "any": the platform answers a language it holds no state in with
 * `language_not_available`, naming the ones it does.
 */
export const RADAR_LANGUAGES = Object.freeze([
  Object.freeze({ code: "fra", label: liveChrome().common.languageNames.fra }),
  Object.freeze({ code: "deu", label: liveChrome().common.languageNames.deu }),
  Object.freeze({ code: "eng", label: liveChrome().common.languageNames.eng }),
]);

/**
 * The one sentence per refusal code a request from this page can meet besides `no_corpus_mounted`,
 * whose sentence names the missing index from its payload. Any other code gets a sentence that names
 * it.
 */
export const LIVE_RADAR_REFUSAL_SENTENCES = Object.freeze({
  identifier_unknown: "This index holds no work under that identifier.",
  language_not_available: "This index holds no state in the language asked for.",
  retrieval_mode_unavailable: "This index cannot list this work's changes.",
});

export const LIVE_RADAR_LOADING = liveChrome().common.loading;

/**
 * The caveat the API sends with every radar, in its own words (`V3CorpusMount.ChangesInPeriodCaveat`): the page says it in
 * its own language (`radar.caveat`) when the answer's is this one, and says any other as the API wrote it, marked English.
 */
export const RADAR_PLATFORM_CAVEAT = "a version row does not by itself assert a wording change, legal effect, or entry into force";

/** The rows shown at a time, and so the most titles one screen asks for. */
export const ROWS_PER_PAGE = 12;
/** The days one window of a period spans (the "last 30 days" preset is one window). */
export const WINDOW_DAYS = 30;
/** The most rows one changes_in_period answer serves (the API's maximum). */
export const RADAR_LIMIT = 200;
/** The most answers one window is asked in, following `continue_from`; a window holding more says it is cut. */
export const PAGES_PER_WINDOW = 3;
/** The most title requests in flight at once. */
const TITLE_CONCURRENCY = 4;

const UNEXPECTED = "The change radar was refused with {code}.";
const UNSHOWN = "The change radar was refused with {code}, and its card cannot be shown: {reason}.";

export function unexpectedRefusalSentence(code) {
  return fillText(refusalTemplate("radar", "unexpected", UNEXPECTED), { code });
}

// A transport failure, an answer this page cannot read and a request it will not send have no reviewed
// wording but the English: a page in another language says them in English, marked English
// (`englishStatement`).
export function transportFailureSentence(code) {
  if (code === "request_schema_invalid") {
    return "This server refused the change radar request as it was asked (request_schema_invalid).";
  }
  return `The change radar could not be reached (${code}).`;
}

export function unshownRefusalSentence(code, reason) {
  return unshownRefusal("radar", UNSHOWN, code, reason).sentence;
}

export function invalidAnswerSentence(reason) {
  return `The answer could not be read as a change radar: ${reason}.`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Dates: written dd/mm/yyyy in both languages on the V4 pages (the brief, section 9), and typed the same way.

/** A calendar date (yyyy-mm-dd) written day first, dd/mm/yyyy; any other value as it is. */
export function numericDate(isoDate) {
  const match = typeof isoDate === "string" ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoDate) : null;
  return match === null ? isoDate : `${match[3]}/${match[2]}/${match[1]}`;
}

/** A date typed day first (dd/mm/yyyy; one or two digits for the day and month; `.` or `-` also), as yyyy-mm-dd; else null. */
export function typedDate(text) {
  const match = typeof text === "string" ? /^\s*(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})\s*$/.exec(text) : null;
  if (match === null) return null;
  const iso = `${match[3]}-${match[2].padStart(2, "0")}-${match[1].padStart(2, "0")}`;
  return isCalendarDate(iso) ? iso : null;
}

/** The calendar date `days` days after `isoDate` (before it when negative). */
export function addDays(isoDate, days) {
  const at = new Date(`${isoDate}T00:00:00Z`);
  at.setUTCDate(at.getUTCDate() + days);
  return at.toISOString().slice(0, 10);
}

/**
 * The date the presets count back from: today in Luxembourg, unless the newest date the corpus holds a version of
 * (`population.last_date_held`) is before it, then that date. Null while today is not known.
 */
export function anchorOf(today, lastDateHeld = null) {
  if (!isCalendarDate(today)) return null;
  return isCalendarDate(lastDateHeld) && lastDateHeld < today ? lastDateHeld : today;
}

/** The presets, in the order the page offers them; `dates` is the reader's own period. */
export const PRESETS = Object.freeze(["last30", "thisYear", "lastYear", "dates"]);

/** The period a preset names, counted back from `anchor` (`anchorOf`): `{ from, to }`, or null for `dates` or no anchor. */
export function periodOf(preset, anchor) {
  if (!isCalendarDate(anchor)) return null;
  const year = Number(anchor.slice(0, 4));
  if (preset === "last30") return { from: addDays(anchor, -(WINDOW_DAYS - 1)), to: anchor };
  if (preset === "thisYear") return { from: `${year}-01-01`, to: anchor };
  if (preset === "lastYear") return { from: `${year - 1}-01-01`, to: `${year - 1}-12-31` };
  return null;
}

/** The windows a period is asked in, the latest first: `WINDOW_DAYS` days each, the earliest cut at the period's start. */
export function windowsOf({ from, to }) {
  const windows = [];
  for (let end = to; end >= from; end = addDays(end, -WINDOW_DAYS)) {
    const start = addDays(end, -(WINDOW_DAYS - 1));
    windows.push(Object.freeze({ from: start < from ? from : start, to: end }));
  }
  return windows;
}

/**
 * The window the changes page opened on from the old menu (the owner's order of 2026-11-13, section C): the last twelve
 * months to `today`, the same day a year before (the 28th of February for the 29th); null when `today` is no calendar date.
 */
export function lastTwelveMonths(today) {
  if (!isCalendarDate(today)) return null;
  const [year, month, day] = today.split("-").map(Number);
  const earlier = month === 2 && day === 29 ? 28 : day;
  const from = `${String(year - 1).padStart(4, "0")}-${String(month).padStart(2, "0")}-${String(earlier).padStart(2, "0")}`;
  return { dateFrom: from, dateTo: today };
}

// ---------------------------------------------------------------------------------------------------------------------
// Rows.

/** Whether a row is a new version in which an article changed, was added or was removed: what the switch keeps. */
export function isWordingChange(row) {
  return row.reason === null && row.wordingChanged === true;
}

/** A count written with the language's grouping (1,181 in English; 1 181 in French). */
export function groupedCount(count, locale = CHROME_LOCALE) {
  return count.toLocaleString(locale === "fr" ? "fr-FR" : "en-GB");
}

/**
 * A compared row's articles in words, the non-zero kinds only, in the order added, changed, removed, identical, the first
 * naming the articles ("1 article added, 1 changed, 233 identical"); "No article changed" first when none changed.
 */
export function countsSentence(counts, locale = CHROME_LOCALE) {
  const copy = liveChrome(locale).radar;
  const said = (table, kind) => fillText(countedEntry(table[kind], counts[kind], locale), { count: groupedCount(counts[kind], locale) });
  const kinds = ["added", "changed", "removed", "unchanged"].filter((kind) => counts[kind] > 0);
  const first = kinds[0] === undefined || kinds[0] === "unchanged" ? copy.noArticleChanged : said(copy.counts, kinds[0]);
  const rest = first === copy.noArticleChanged ? kinds : kinds.slice(1);
  return rest.reduce((sentence, kind) => fillText(copy.countsJoin, { first: sentence, next: said(copy.countsNext, kind) }), first);
}

/** The identifier the API and the text page name a row's text by. */
export function textIdentifierOf(row) {
  return `/lu-legilux/${row.workKey}`;
}

/** Text folded for the title filter: no accents, lower case, apostrophes alike, spaces collapsed. */
export function foldTitle(text) {
  return String(text ?? "").normalize("NFD").replace(/\p{M}/gu, "").replace(/[’‘ʼ]/gu, "'").replace(/\s+/gu, " ").trim().toLowerCase();
}

/** The rows the page shows, newest first: those the switch keeps, whose title as shown holds the filter's words. */
export function shownRows(rows, { hideUnchanged = false, filter = "", titleOf = () => "" } = {}) {
  const wanted = foldTitle(filter);
  return rows
    .filter((row) => !hideUnchanged || isWordingChange(row))
    .filter((row) => wanted === "" || foldTitle(titleOf(row)).includes(wanted))
    .slice()
    .reverse();
}

// ---------------------------------------------------------------------------------------------------------------------
// A text's own title (the brief's trap 2).

const FRENCH_MONTHS = Object.freeze({
  janvier: 1, fevrier: 2, mars: 3, avril: 4, mai: 5, juin: 6, juillet: 7, aout: 8, septembre: 9, octobre: 10, novembre: 11, decembre: 12,
});
const FRENCH_DATE = /(\d{1,2})(?:er)?\s+(janvier|f[ée]vrier|mars|avril|mai|juin|juillet|ao[uû]t|septembre|octobre|novembre|d[ée]cembre)\s+(\d{4})/iu;
/** The publisher's prefix of a consolidated version's title ("Version consolidée applicable au 25/12/2006 : "). */
const VERSION_PREFIX = /^\s*(?:version\s+consolid[ée]e|texte\s+coordonn[ée])(?=[\s:])[^:]{0,80}:\s*/iu;
/** A work key that names its act's date (`loi-2018-07-18-a621`). */
const DATED_KEY = /^[a-z]+-(\d{4}-\d{2}-\d{2})-[a-z0-9]+$/u;
/** A line break as the publisher writes one inside a title: a real one, or the two characters backslash and n. */
const TITLE_LINE_BREAK = /\\n|\r?\n/u;

/** The first date a French title names, as yyyy-mm-dd, or null. */
export function firstFrenchDateOf(title) {
  const match = FRENCH_DATE.exec(title);
  if (match === null) return null;
  const month = FRENCH_MONTHS[match[2].normalize("NFD").replace(/\p{M}/gu, "").toLowerCase()];
  return `${match[3]}-${String(month).padStart(2, "0")}-${match[1].padStart(2, "0")}`;
}

/** The clause a title's first line ends with when the acts it amends follow as a list ("… et portant modification :"). */
const AMENDING_TAIL = /\s+et\s+(?:portant|modifiant|abrogeant|compl[ée]tant)\b[^:]*:\s*$/iu;
/** A list item's marker at the start of a title's line ("1°", "2.", "-", "*"). */
const LIST_MARKER = /^\s*(?:\d+\s*°|\d+\.|[-*•])\s*/u;
/** A heading that is only an act's kind and date ("Loi du 4 juillet 2014"), its subject on the next line. */
const KIND_AND_DATE_ONLY = /^[^\d]{0,60}\d{1,2}(?:er)?\s+\p{L}+\s+\d{4}[\s,.;:-]*$/u;
/** The longest heading a row shows; a longer title is cut at a word and ends with an ellipsis. */
export const TITLE_MAX_LENGTH = 220;

/**
 * A publisher's title as a heading: its version prefix removed; its first line (the publisher lists the acts it amends on
 * the lines after), without the clause that announces that list when the line says what the text is before it; the next
 * lines joined, their list markers dropped, while the heading is only the act's kind and date or ends in a colon; spaces
 * collapsed; no closing punctuation; at most `TITLE_MAX_LENGTH` characters, cut at a word.
 */
export function cleanTitle(title) {
  if (typeof title !== "string") return null;
  const lines = title.replace(VERSION_PREFIX, "").split(TITLE_LINE_BREAK).map((line) => line.replace(/\s+/gu, " ").trim()).filter((line) => line.length > 0);
  if (lines.length === 0) return null;
  let heading = lines[0];
  if (/:\s*$/u.test(heading)) {
    const cut = heading.replace(AMENDING_TAIL, "");
    if (cut !== heading && !KIND_AND_DATE_ONLY.test(cut)) heading = cut;
  }
  for (let next = 1; next < lines.length && (/:\s*$/u.test(heading) || KIND_AND_DATE_ONLY.test(heading)); next += 1) {
    heading = `${heading} ${lines[next].replace(LIST_MARKER, "")}`;
  }
  heading = heading.replace(/\s+/gu, " ").replace(/[\s.;,:]+$/u, "").trim();
  if (heading.length > TITLE_MAX_LENGTH) {
    const cut = heading.slice(0, TITLE_MAX_LENGTH);
    heading = `${cut.slice(0, Math.max(cut.lastIndexOf(" "), 1)).replace(/[\s.;,:]+$/u, "")}…`;
  }
  return heading.length > 0 ? heading : null;
}

/**
 * A text's own title, from the title groups an answer serves for its expressions (`dossier`'s `titles`, or
 * `evidence_bundle`'s `citation_facts.titles`: `{ expression_iri, language, short_titles: [{ title }], titles: [{ title }] }`),
 * for the version a row names (`expressionIri`, `language`):
 *
 *  1. the groups in this order: the version's own expression, then the text's other expressions in its language, then the
 *     rest, each in the answer's order;
 *  2. in a group, its short titles before its titles, each cleaned (`cleanTitle`);
 *  3. a title is the text's own only when the first date it names is the date its work key names (`loi-2018-07-18-a621`:
 *     18 July 2018), or it names none: a title naming another date first is an amending act's (trap 2: "Loi du 29 juillet
 *     2020 portant modification de la loi modifiée du 18 juillet 2018 sur la Police grand-ducale"). A code's key names no
 *     date.
 *
 * Returns `{ title, language }` (the group's language, `fra`), or null when the answer holds no title of the text's own.
 * Pure: Home and the assistant's clarify card can call it on the same groups.
 */
export function ownTitleOf(groups, { workKey, expressionIri = null, language = null } = {}) {
  if (!Array.isArray(groups)) return null;
  const keyDate = typeof workKey === "string" ? DATED_KEY.exec(workKey)?.[1] ?? null : null;
  const rank = (group) => (expressionIri !== null && group?.expression_iri === expressionIri ? 0 : language !== null && group?.language === language ? 1 : 2);
  const ordered = groups.map((group, index) => ({ group, index })).sort((a, b) => rank(a.group) - rank(b.group) || a.index - b.index);
  for (const { group } of ordered) {
    const candidates = [...(group?.short_titles ?? []), ...(group?.titles ?? [])].map((entry) => cleanTitle(entry?.title));
    for (const title of candidates) {
      if (title === null) continue;
      const named = firstFrenchDateOf(title);
      if (keyDate !== null && named !== null && named !== keyDate) continue;
      return Object.freeze({ title, language: typeof group.language === "string" ? group.language : null });
    }
  }
  return null;
}

/** The `lang` of a text in one of the publisher's languages (`fra`, `deu`, `eng`, `ltz`), or undefined for any other. */
export function languageTagOf(language) {
  return { fra: "fr", deu: "de", eng: "en", ltz: "lb" }[language];
}

/** Asks one text's dossier and reads its own title for a row's version (`ownTitleOf`); null when none can be had. */
export async function loadOwnTitle({ contract, fetchImpl, signal, row }) {
  const asked = await askV3("dossier", { identifier: textIdentifierOf(row) }, { contract, objectType: "work_record", fetchImpl, signal });
  if (asked.state !== "success") return null;
  return ownTitleOf(asked.envelope.result?.value?.titles, { workKey: row.workKey, expressionIri: row.state.expressionIri, language: row.state.language });
}

// ---------------------------------------------------------------------------------------------------------------------
// One request and its outcome.

/**
 * The request parameters for one radar, checked before anything is sent: two calendar dates written yyyy-mm-dd, a work
 * identifier only when one is given (sent as typed), a language only when one is chosen, and a limit only when one is set
 * (1 to 200). Nothing else is sent.
 */
export function radarParameters({ dateFrom, dateTo, identifier = "", language = "", limit = null }) {
  if (!isCalendarDate(dateFrom) || !isCalendarDate(dateTo)) {
    throw new Error("a change radar needs two calendar dates, written yyyy-mm-dd");
  }
  const parameters = { date_from: dateFrom, date_to: dateTo };
  if (typeof identifier !== "string") throw new Error("a work identifier is text");
  if (identifier.trim().length > 0) parameters.identifier = identifier;
  if (language !== "") {
    if (!RADAR_LANGUAGES.some((offered) => offered.code === language)) {
      throw new Error(`${JSON.stringify(language)} is not a language this form offers`);
    }
    parameters.language = language;
  }
  if (limit !== null) {
    if (!Number.isInteger(limit) || limit < 1 || limit > RADAR_LIMIT) throw new Error(`a limit is a whole number from 1 to ${RADAR_LIMIT}`);
    parameters.limit = limit;
  }
  return parameters;
}

/**
 * Maps what `askV3` returned to the view: `success` with the radar view (read by `readChanges`),
 * `refusal` with the refusal card's inputs, or a state that carries a sentence.
 */
export function radarOutcome(asked) {
  if (asked.state === "success") {
    try {
      return { state: "success", view: readChanges(asked.envelope.result.value), context: asked.envelope.context };
    } catch (error) {
      return { state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(error.message)) };
    }
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    const sentence = code === "no_corpus_mounted"
      ? noCorpusMountedSentence(payload)
      : tableRefusalSentence(LIVE_RADAR_REFUSAL_SENTENCES, code, unexpectedRefusalSentence, payload);
    try {
      validateRefusal({ code, sentence, payload });
    } catch (error) {
      return {
        state: "refusal",
        code,
        card: false,
        ...unshownRefusal("radar", UNSHOWN, code, error.message),
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

/** Asks the live API one change radar and returns the view state. */
export async function loadLiveRadar({ contract, fetchImpl, signal, request }) {
  const asked = await askV3("changes_in_period", radarParameters(request), { contract, objectType: "change_list", fetchImpl, signal });
  return radarOutcome(asked);
}

/**
 * One reader's radar requests on one screen: `ask` sends one (cancelling the one in flight) and
 * `cancel` abandons whatever is in flight. A request the form will not send is said as a state and
 * nothing is asked. After a cancel nothing settles.
 */
export function createRadarSession({ contract, fetchImpl, onOutcome }) {
  let controller = null;
  // The last request sent, which `retry` asks again (the failure panel's retry, R60).
  let last = null;
  const session = {
    ask(request) {
      controller?.abort();
      controller = null;
      try {
        radarParameters(request);
      } catch (error) {
        onOutcome({ state: "invalid_request", ...englishStatement(`${error.message}.`) });
        return false;
      }
      last = request;
      const own = new AbortController();
      controller = own;
      onOutcome({ state: "loading", sentence: LIVE_RADAR_LOADING });
      loadLiveRadar({ contract, fetchImpl, signal: own.signal, request })
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
    retry() {
      return last !== null && session.ask(last);
    },
    cancel() {
      controller?.abort();
      controller = null;
    },
  };
  return session;
}

// ---------------------------------------------------------------------------------------------------------------------
// The Changes page's session: a period asked window by window, the rows held, the titles asked.

const byDateThenWork = (a, b) => (a.state.applicabilityDate < b.state.applicabilityDate ? -1 : a.state.applicabilityDate > b.state.applicabilityDate ? 1
  : a.workKey < b.workKey ? -1 : a.workKey > b.workKey ? 1 : 0);

/**
 * The view the answers asked for a period make: the latest answer's view (its population, caveat) with every row held so
 * far, each once, oldest first as the API serves them, and the period asked (`period`).
 */
export function mergedView(views, period) {
  const seen = new Set();
  const rows = [];
  for (const view of views) {
    for (const row of view.rows) {
      const key = `${row.state.stateSha256} ${row.state.language}`;
      if (seen.has(key)) continue;
      seen.add(key);
      rows.push(row);
    }
  }
  rows.sort(byDateThenWork);
  return Object.freeze({ ...views.at(-1), rows: Object.freeze(rows), period: Object.freeze({ from: period.from, to: period.to }) });
}

/**
 * The Changes page's requests. `open(period)` asks the period's latest window (cancelling whatever is in flight) and
 * `earlier()` the next window back; each window is followed by its `continue_from` up to `PAGES_PER_WINDOW` answers.
 * `onState` receives `{ outcome, windowsLeft, cut, loadingEarlier, earlierFailed }`: the outcome is the period's merged
 * view (`success`, `mergedView`) or the first window's refusal or failure. `titles(rows)` asks each row's own title once
 * per text (`onTitle(workKey, { title, language } | null)`), at most `TITLE_CONCURRENCY` at a time. `retry` asks the last
 * thing again; `cancel` abandons everything in flight.
 */
export function createChangesSession({ contract, fetchImpl, onState, onTitle = () => {} }) {
  let controller = null;
  let period = null;
  let windows = [];
  let views = [];
  let asked = 0;
  let cut = false;
  let lastAction = null;
  const titles = new Map();
  const queue = [];
  let running = 0;
  const titleController = new AbortController();

  const emit = (outcome, extra = {}) => onState(Object.freeze({
    outcome,
    // The earliest date the rows held reach back to: the start of the earliest window asked.
    loadedFrom: asked > 0 && windows.length > 0 ? windows[Math.min(asked, windows.length) - 1].from : null,
    windowsLeft: Math.max(0, windows.length - asked),
    cut,
    loadingEarlier: false,
    earlierFailed: null,
    ...extra,
  }));

  /** One window, asked and followed by its `continue_from`: a success outcome over the whole window, or the first failure. */
  async function askWindow(window, signal) {
    let from = window.from;
    let merged = null;
    for (let page = 0; page < PAGES_PER_WINDOW; page += 1) {
      const outcome = await loadLiveRadar({ contract, fetchImpl, signal, request: { dateFrom: from, dateTo: window.to, limit: RADAR_LIMIT } });
      if (outcome.state !== "success") return outcome;
      merged = merged === null ? outcome : { ...outcome, view: mergedView([merged.view, outcome.view], window) };
      if (!outcome.view.truncated) return merged;
      from = outcome.view.continueFrom;
    }
    cut = true;
    return merged;
  }

  function settle(promise, own, onSuccess, onFailure) {
    promise.then((outcome) => {
      if (own.signal.aborted) return;
      if (outcome.state === "success") onSuccess(outcome);
      else onFailure(outcome);
    }).catch((error) => {
      if (error?.name === "AbortError" || own.signal.aborted) return;
      onFailure({ state: "invalid_envelope", ...englishStatement(invalidAnswerSentence(String(error?.message ?? error))) });
    });
  }

  const session = {
    /** Asks a period's latest window; false, and an `invalid_request` state, when the period is not two dates in order. */
    open(next, { force = false } = {}) {
      // The same period asked again while it is in flight or held (a submit and a change of field for one period: the
      // journeys' J8 saw the window asked twice) is not asked again; a retry asks it anew.
      const same = period !== null && next?.from === period.from && next?.to === period.to;
      if (!force && same && (views.length > 0 || (controller !== null && !controller.signal.aborted))) return true;
      controller?.abort();
      controller = null;
      lastAction = () => session.open(next, { force: true });
      if (!isCalendarDate(next?.from) || !isCalendarDate(next?.to) || next.from > next.to) {
        emit({ state: "invalid_request", ...englishStatement("A period is two calendar dates, the first not after the second.") });
        return false;
      }
      period = { from: next.from, to: next.to };
      windows = windowsOf(period);
      views = [];
      asked = 1;
      cut = false;
      const own = new AbortController();
      controller = own;
      emit({ state: "loading", sentence: LIVE_RADAR_LOADING });
      settle(askWindow(windows[0], own.signal), own, (outcome) => {
        views = [outcome.view];
        emit({ ...outcome, view: mergedView(views, period) });
      }, (outcome) => {
        // A failed period is asked again when the reader asks it again.
        if (controller === own) controller = null;
        emit(outcome);
      });
      return true;
    },
    /** Asks the next window back; false when no window is left or no period is shown. */
    earlier() {
      if (period === null || views.length === 0 || asked >= windows.length) return false;
      controller?.abort();
      const own = new AbortController();
      controller = own;
      const window = windows[asked];
      lastAction = () => session.earlier();
      const current = { state: "success", view: mergedView(views, period), context: null };
      emit(current, { loadingEarlier: true });
      settle(askWindow(window, own.signal), own, (outcome) => {
        asked += 1;
        views = [...views, outcome.view];
        emit({ ...outcome, view: mergedView(views, period) });
      }, (outcome) => emit(current, { earlierFailed: outcome }));
      return true;
    },
    retry() {
      return lastAction !== null && lastAction() !== false;
    },
    /** Asks each row's own title, once per text, a few at a time. */
    titles(rows) {
      for (const row of rows) {
        if (titles.has(row.workKey)) continue;
        titles.set(row.workKey, "asked");
        queue.push(row);
      }
      const pump = () => {
        while (running < TITLE_CONCURRENCY && queue.length > 0) {
          const row = queue.shift();
          running += 1;
          loadOwnTitle({ contract, fetchImpl, signal: titleController.signal, row })
            .catch(() => null)
            .then((title) => {
              running -= 1;
              if (titleController.signal.aborted) return;
              titles.set(row.workKey, title);
              onTitle(row.workKey, title);
              pump();
            });
        }
      };
      pump();
    },
    cancel() {
      controller?.abort();
      controller = null;
      titleController.abort();
    },
  };
  return session;
}
