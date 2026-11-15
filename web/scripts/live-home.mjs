// The home page's facts: today in Luxembourg and the coverage line (the specification of record's R13 and R11).
//
// The home page is rendered by the server from the API's coverage answer, as a law's page is from its own (R67): both
// servers ask `coverage` for `/fr/` and `/en/` (`sitePageFor`), render the page from that envelope (`ssr-entry.jsx`), and
// write into it the facts the page shows, which the browser hydrates the same tree from. The facts, not the envelope: a
// coverage answer counts every measured capability cell, which on a population runs to thousands of rows the home page
// never shows, so the page carries the few values it says, read from the answer by the coverage reader's own rules
// (`readCoverage`). A page served as built (no renderer, or a render that failed) asks `coverage` itself once loaded, and
// takes today from this browser's clock (`todayInLuxembourg`).
//
// - **Today** is the calendar date in Luxembourg when the server answered: the answer's `context.freshness.observed_at`,
//   read by the coded rule (`luxembourgDate`, plan 0.8), never the build's time.
// - **The coverage line** says only what the answer counts (R11: no count is a literal): the works, states and articles
//   of the mounted Luxembourg index (`totals`), "as of the index built on" the date of the build the mounted log records
//   as mounted (`history.snapshots_with_text`, retained as `mounted`), that build time's date in Luxembourg, as today is.
//   A mount whose log records no build says the counts with no date and says why; with no corpus mounted, the line is the
//   coverage page's own sentence for that refusal; an answer that cannot be read shows no line. A counted line carries the
//   digest of the Luxembourg index it counts (`mounted.index_sha256`), which the page holds its example searches to
//   (`home-examples.mjs`): a build's examples are shown only over the index they were derived from.
//
// Data and pure functions only: the page, its client entry and the server renderer share it.

import { LIVE_COVERAGE_REFUSAL_SENTENCES, coverageOutcome, startLiveCoverage } from "./live-coverage.mjs";
import { readCoverage } from "./coverage.mjs";
import { answerHandedWith, askV3 } from "./v3-client.mjs";
import { isCalendarDate, luxembourgDate } from "./temporal.mjs";
import { actTitleOf } from "./law-page.mjs";

/** The id of the data block that carries the facts the home page was rendered from. */
export const HOME_FACTS_ID = "lex-home-facts";

/** The coverage line's states: counted, no corpus mounted, or nothing to say (an answer that could not be read). */
export const COVERAGE_LINE_STATES = Object.freeze(["counted", "unmounted", "unavailable"]);

/** The coverage refusal sentence the line says with no corpus mounted, as the coverage page says it. */
export const UNMOUNTED_SENTENCE = LIVE_COVERAGE_REFUSAL_SENTENCES.no_corpus_mounted;

function dateInLuxembourg(instant) {
  try {
    return luxembourgDate(instant);
  } catch {
    return null;
  }
}

/** The coverage line, from a coverage view state (`coverageOutcome`). */
export function coverageLineOf(outcome) {
  if (outcome?.state === "success") {
    const view = readCoverage(outcome.answer);
    const mounted = view.history?.logRecordsBuilds === true
      ? view.history.snapshotsWithText.find((snapshot) => snapshot.retainedAs.includes("mounted"))
      : undefined;
    return Object.freeze({
      state: "counted",
      works: view.totals.works,
      states: view.totals.states,
      articles: view.totals.articles,
      builtOn: mounted === undefined ? null : dateInLuxembourg(mounted.builtAt),
      indexSha256: view.mounted.index_sha256,
    });
  }
  if (outcome?.state === "refusal" && outcome.code === "no_corpus_mounted") return Object.freeze({ state: "unmounted" });
  return Object.freeze({ state: "unavailable" });
}

/**
 * The home page's facts from the coverage envelope the server answered (`contract`, the census's): `{ today, coverage }`,
 * `today` the calendar date in Luxembourg when it answered, or null when the envelope names no instant the rule reads.
 */
export function homeFacts(envelope, contract) {
  const outcome = coverageOutcome(answerHandedWith("coverage", envelope, { contract, objectType: "coverage_report" }));
  const observedAt = envelope?.context?.freshness?.observed_at;
  return Object.freeze({
    today: typeof observedAt === "string" ? dateInLuxembourg(observedAt) : null,
    coverage: coverageLineOf(outcome),
    // The assistant's entry, only while the server reports it switched on: absent otherwise, so the facts, and the page
    // rendered from them, are byte for byte what they are while it is off (R47, R52).
    ...(assistantOn(outcome) ? { assistant: true } : {}),
  });
}

/**
 * Whether a coverage view state says the assistant is switched on: the answer's capability flag (`assistant.state`, a member
 * the server adds only while it is on). Anything else, an answer without it included, is off.
 */
export function assistantOn(outcome) {
  return outcome?.state === "success" && outcome.answer?.assistant?.state === "on";
}

const isCount = (value) => Number.isInteger(value) && value >= 0;
const isDigest = (value) => typeof value === "string" && /^[0-9a-f]{64}$/.test(value);

/**
 * Facts read back from a page's block, held to their shape, or null when they are not the facts a home page is rendered
 * from (the page then hydrates as the page as built, and the browser reports the hydration that changed it).
 */
export function readHomeFactsValue(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return null;
  const { today, coverage } = value;
  if (!(today === null || isCalendarDate(today))) return null;
  if (coverage === null || typeof coverage !== "object" || !COVERAGE_LINE_STATES.includes(coverage.state)) return null;
  if (!(value.assistant === undefined || value.assistant === true)) return null;
  const assistant = value.assistant === true ? { assistant: true } : {};
  if (coverage.state === "counted") {
    if (![coverage.works, coverage.states, coverage.articles].every(isCount)) return null;
    if (!(coverage.builtOn === null || isCalendarDate(coverage.builtOn))) return null;
    // The index's digest, which a renderer before the example searches did not write: none, and the page shows no example.
    if (!(coverage.indexSha256 === undefined || coverage.indexSha256 === null || isDigest(coverage.indexSha256))) return null;
    return Object.freeze({
      today,
      coverage: Object.freeze({
        state: "counted", works: coverage.works, states: coverage.states, articles: coverage.articles, builtOn: coverage.builtOn,
        indexSha256: coverage.indexSha256 ?? null,
      }),
      ...assistant,
    });
  }
  return Object.freeze({ today, coverage: Object.freeze({ state: coverage.state }), ...assistant });
}

/** The facts a home page carries, as its client entry reads them, or null when the page carries none. */
export function readHomeFacts(document) {
  const block = document.getElementById(HOME_FACTS_ID);
  if (block === null) return null;
  try {
    return readHomeFactsValue(JSON.parse(block.textContent ?? ""));
  } catch {
    return null;
  }
}

/**
 * What a home page served as built does once loaded: asks `coverage` once and hands the coverage line to `onLine` when it
 * settles; returns the cancel the page calls when it unmounts.
 */
export function startHomeCoverage({ contract, fetchImpl, onLine, onAssistant = () => {} }) {
  return startLiveCoverage({
    contract,
    fetchImpl,
    onOutcome: (outcome) => {
      onLine(coverageLineOf(outcome));
      if (assistantOn(outcome)) onAssistant(true);
    },
  });
}

// ---- V4 Home (the owner's brief of 2026-11-15, section 5, and the mockup V1Home) -------------------------------------
//
// The six starting texts, each its versions drawn on one shared scale from 2006 to 2026, and the three newest changes of
// wording. The starting texts' rows are the release's record (`home-texts-record.mjs`, written from the live API's `timeline`
// and `dossier` answers by `write-home-texts.mjs`): they are drawn at first paint, with no request; when the Luxembourg index
// the coverage answer counts is not the one the record was read from, the page asks the six timelines again after first
// paint, one at a time, and redraws the rows from them (`startHomeTimelines`). The changes are asked once loaded
// (`startRecentChanges`): `changes_in_period` over the last weeks, then each row's dossier for the text's own title.

/** A calendar date as V4 pages write it in both languages: dd/mm/yyyy (the brief's section 9; the driver unifies it). */
export function numericDate(date) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(String(date));
  return match === null ? "" : `${match[3]}/${match[2]}/${match[1]}`;
}

/** The shared scale of the starting texts: 1 January 2006 to 1 January 2027, 300 units wide, so 2026 is labelled inside it. */
export const HOME_SCALE = Object.freeze({ from: "2006-01-01", to: "2027-01-01", width: 300, years: Object.freeze([2006, 2011, 2016, 2021, 2026]) });

const dayOf = (date) => Date.UTC(Number(date.slice(0, 4)), Number(date.slice(5, 7)) - 1, Number(date.slice(8, 10))) / 86400000;

/** Where a calendar date stands on the scale (0 to `width`, one decimal), or null when it falls outside it. */
export function scaleX(date, scale = HOME_SCALE) {
  if (!isCalendarDate(date)) return null;
  const from = dayOf(scale.from);
  const to = dayOf(scale.to);
  const day = dayOf(date);
  if (day < from || day >= to) return null;
  return Math.round(((day - from) / (to - from)) * scale.width * 10) / 10;
}

/** The text language a starting text is drawn in on a page of `locale`: English on an English page when held, else French. */
export function textLanguageFor(locale, languages) {
  if (locale === "en" && languages.includes("eng")) return "eng";
  return languages.includes("fra") ? "fra" : languages[0] ?? null;
}

/**
 * A starting text's row from its `timeline` answer (`value`, the result's value): the dates of its versions whose text is
 * held, oldest first, per text language, and the article count of the version that applies on `today` (else the latest).
 * A Luxembourg text's versions are its held states (`states`, text held); an EU act's are its wordings whose text is held and
 * answers its date (`basis` not `different_texts`, which a date refuses as ambiguous), per language. Null for an answer that
 * is neither.
 */
export function homeTextOfTimeline(value, today = null) {
  if (value === null || typeof value !== "object") return null;
  const byLanguage = {};
  const add = (language, date, articles) => {
    if (typeof language !== "string" || !isCalendarDate(date)) return;
    (byLanguage[language] ??= []).push({ date, articles });
  };
  if (value.publisher === "lu-legilux" && Array.isArray(value.states)) {
    for (const state of value.states) {
      if (state?.text_status === "held") add(state.language, state.applicability_date, Array.isArray(state.articles) ? state.articles.length : null);
    }
  } else if (value.publisher === "eu-eurlex" && Array.isArray(value.languages)) {
    for (const language of value.languages) {
      for (const wording of language?.wordings ?? []) {
        if (wording?.text_held === true && wording.basis !== "different_texts") {
          add(language.language, wording.wording_date, Number.isInteger(wording.article_count) ? wording.article_count : null);
        }
      }
    }
  } else {
    return null;
  }
  const languages = Object.keys(byLanguage).sort();
  if (languages.length === 0) return null;
  const versions = {};
  const articles = {};
  for (const language of languages) {
    const dates = byLanguage[language].sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0));
    versions[language] = [...new Set(dates.map((entry) => entry.date))];
    const current = (isCalendarDate(today) ? dates.filter((entry) => entry.date <= today) : dates).at(-1) ?? dates.at(-1);
    articles[language] = current.articles;
  }
  return { versions, articles, index_sha256: typeof value.index_sha256 === "string" ? value.index_sha256 : null };
}

// The French months, to find an act's own date in a title ("Loi du 18 juillet 2018 ..."): matching only, never shown.
const FRENCH_MONTHS = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];
const DATED_KEY = /^[a-z]+-(\d{4})-(\d{2})-(\d{2})-[a-z0-9]+$/u;

/** A title as a row shows it: the publisher's version prefix removed, its first line only, no final full stop. */
export function titleShown(title) {
  const own = actTitleOf(title);
  if (own === null) return null;
  const line = own.split(/\\n|\n/u)[0].trim().replace(/\s*[.;:,]$/u, "").trim();
  return line.length > 0 ? line : null;
}

/**
 * The text's own title from its `dossier` answer (`value`), for the rows that name a text by its work key alone (trap 2 of
 * the brief: a dossier can list first the title of an act that modified it). The driver swaps this for the shared rule
 * (`ownTitleOf`) at merge. The rule: the expressions newest first, French first; in each its short titles, then its titles,
 * each without the version prefix and cut at its first line; for a work key that carries a date (`loi-2018-07-18-a621`), the
 * first that names that date in words ("18 juillet 2018", "1er" for the first); for any other key (a code), the first. Null
 * when none qualifies: the row is then not shown, never an identifier or a date in its place.
 */
export function recentTitleOf(value, workKey) {
  const groups = Array.isArray(value?.titles) ? [...value.titles].reverse() : [];
  const ordered = [...groups.filter((group) => group?.language === "fra"), ...groups.filter((group) => group?.language !== "fra")];
  const dated = DATED_KEY.exec(String(workKey));
  const own = dated === null ? null : `${Number(dated[3]) === 1 ? "1er" : Number(dated[3])} ${FRENCH_MONTHS[Number(dated[2]) - 1]} ${dated[1]}`;
  for (const group of ordered) {
    for (const entry of [...(group.short_titles ?? []), ...(group.titles ?? [])]) {
      const shown = titleShown(entry?.title);
      if (shown === null) continue;
      if (own === null || shown.toLowerCase().includes(own)) return Object.freeze({ title: shown, language: group.language ?? null });
    }
  }
  return null;
}

/** How many recent changes Home lists. */
export const RECENT_CHANGES = 3;

/** The windows Home asks for its recent changes, in days back from today, the next only when the last held too few. */
export const RECENT_WINDOWS = Object.freeze([45, 120, 365]);

/**
 * The rows of a `changes_in_period` answer (`value`) whose wording changed, dated on or before `today`, newest first: each
 * `{ workKey, date, from, counts }`, `from` the version it is compared with (the row's diff).
 */
export function recentChangesOf(value, today) {
  const rows = Array.isArray(value?.changes) ? value.changes : [];
  return rows
    .filter((row) => row?.wording_changed === true && typeof row.work_key === "string" && isCalendarDate(row.state?.applicability_date)
      && (!isCalendarDate(today) || row.state.applicability_date <= today) && row.counts !== null && typeof row.counts === "object")
    .map((row) => Object.freeze({
      workKey: row.work_key,
      date: row.state.applicability_date,
      from: isCalendarDate(row.diff?.date_from) ? row.diff.date_from : isCalendarDate(row.baseline?.applicability_date) ? row.baseline.applicability_date : null,
      counts: Object.freeze({ added: row.counts.added ?? 0, changed: row.counts.changed ?? 0, removed: row.counts.removed ?? 0 }),
    }))
    .sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0));
}

const daysBefore = (date, days) => new Date((dayOf(date) - days) * 86400000).toISOString().slice(0, 10);

/**
 * What Home does once loaded for its recent changes: asks `changes_in_period` for the last RECENT_WINDOWS[0] days up to
 * `today` (a wider window only when that one held too few changes of wording), then the dossier of each of the newest rows for
 * the text's own title (`titleOf`, the swappable rule), until RECENT_CHANGES rows have one. Hands `onRows` the rows (each with
 * its `title`) or null when nothing could be read. Returns the cancel. At most 3 + 5 requests, one at a time.
 */
export function startRecentChanges({ contract, fetchImpl, today, onRows, titleOf = recentTitleOf }) {
  const controller = new AbortController();
  const ask = (operation, parameters, objectType) => askV3(operation, parameters, { contract, objectType, fetchImpl, signal: controller.signal });
  const settle = (rows) => {
    if (!controller.signal.aborted) onRows(rows);
  };
  (async () => {
    if (!isCalendarDate(today)) return settle(null);
    let rows = [];
    for (const days of RECENT_WINDOWS) {
      const asked = await ask("changes_in_period", { date_from: daysBefore(today, days), date_to: today, limit: 200 }, "change_list");
      if (asked.state !== "success") return settle(null);
      rows = recentChangesOf(asked.envelope.result.value, today);
      // A window the answer cut holds its oldest rows only, so a wider one would not help: the narrow one comes first.
      if (rows.length >= RECENT_CHANGES * 2 || asked.envelope.result.value.truncated === true) break;
    }
    const shown = [];
    for (const row of rows.slice(0, RECENT_CHANGES + 2)) {
      if (shown.length === RECENT_CHANGES) break;
      const asked = await ask("dossier", { identifier: `/lu-legilux/${row.workKey}` }, "work_record");
      const title = asked.state === "success" ? titleOf(asked.envelope.result.value, row.workKey) : null;
      if (title !== null) shown.push(Object.freeze({ ...row, title: title.title, titleLanguage: title.language }));
    }
    return settle(shown);
  })().catch((error) => {
    if (error?.name !== "AbortError") settle(null);
  });
  return () => controller.abort();
}

/**
 * The starting texts' rows asked again from the live `timeline`, one text at a time after first paint, for a page whose
 * Luxembourg index is not the record's: `onText(identifier, row)` for each answer read. Returns the cancel.
 */
export function startHomeTimelines({ contract, fetchImpl, texts, today, onText }) {
  const controller = new AbortController();
  (async () => {
    for (const text of texts) {
      const asked = await askV3("timeline", { identifier: text.identifier }, { contract, objectType: "timeline", fetchImpl, signal: controller.signal });
      if (controller.signal.aborted) return;
      const row = asked.state === "success" ? homeTextOfTimeline(asked.envelope.result.value, today) : null;
      if (row !== null) onText(text.identifier, row);
    }
  })().catch(() => {});
  return () => controller.abort();
}
