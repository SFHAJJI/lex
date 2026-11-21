// The Changes page (the owner's brief of 2026-11-15, V4Changes), measured against what the platform really sent.
//
// The page asks `changes_in_period` for a period window by window, newest first, and `dossier` for the own title of each
// text it shows. These tests drive it with an injected fetch that answers the census envelopes
// (`schemas/v3-platform/envelope-samples.json`), render the two-state radar the answer census holds through the same view,
// and hold the title rule (the brief's trap 2) to the titles the live API served on 2026-11-15.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { FailureFrame, LiveRadar, RadarAnswerView, RefusalCard, renderLiveRadarPage } from "../.react-build/app.mjs";
import {
  ROWS_PER_PAGE,
  addDays,
  anchorOf,
  cleanTitle,
  countsSentence,
  createChangesSession,
  createRadarSession,
  loadLiveRadar,
  numericDate,
  ownTitleOf,
  periodOf,
  radarOutcome,
  radarParameters,
  shownRows,
  typedDate,
  windowsOf,
} from "../scripts/live-radar.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { actNameOf } from "../scripts/act-names.mjs";
import { textHref } from "../scripts/text-address.mjs";

/** A refusal's card as the screen shows it: one about this service (R60) inside the failure panel, under its line; any other alone. */
const asShown = (outcome, card) => renderToStaticMarkup(h(FailureFrame, { outcome }, h("lex-card"))).replace("<lex-card></lex-card>", card);

const census = JSON.parse(await readFile(new URL("../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const EN = liveChrome("en").radar;
const envelopeOf = (operation, scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === operation && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the ${operation} envelope "${scenarioStart}..."`);
  return structuredClone(entry.envelope);
};
const REQUEST = { dateFrom: "2024-02-01", dateTo: "2024-02-01" };

const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'" };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|#x27);/g, (entity) => ENTITIES[entity]);
const textOf = (markup) => decode(markup.replace(/<[^>]+>/g, " ")).replace(/\s+/g, " ");

function answering(status, contentType, body) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    return {
      status,
      headers: { get: (name) => (name.toLowerCase() === "content-type" ? contentType : null) },
      text: async () => (typeof body === "string" ? body : JSON.stringify(body)),
    };
  };
  return { calls, fetchImpl };
}

/** A fetch answering each operation with its own envelope (a function of the request's parameters), recording the requests. */
function routing(routes) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    const { operation_id: operation, parameters } = JSON.parse(init.body);
    calls.push({ operation, parameters });
    const body = routes[operation](parameters);
    return { status: 200, headers: { get: (name) => (name.toLowerCase() === "content-type" ? "application/json" : null) }, text: async () => JSON.stringify(body) };
  };
  return { calls, fetchImpl };
}

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === "<varies-per-run>") { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

/** The census radar holding both states of the two-state work: the first held, and the later one compared with it. */
function twoStateOutcome(change = () => {}) {
  const row = answers.sampled.filter((sample) => sample.operation === "changes_in_period")[1];
  const envelope = envelopeOf("changes_in_period", "a window holding");
  envelope.result.value = withDigests(row.answer);
  change(envelope.result.value);
  const outcome = radarOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  return outcome;
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 20));

test("the server renders the V4 page: the heading, the four controls in the table's words, the switch on, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf("changes_in_period", "a window holding"));
  const markup = renderToStaticMarkup(h(LiveRadar, { contract, fetchImpl }));
  assert.equal(calls.length, 0, "nothing is asked while rendering");
  assert.match(markup, /data-answer-state="loading"/);
  for (const words of ["Last 30 days", EN.presets.thisYear, EN.presets.dates, EN.from, EN.to, EN.filterLabel, EN.hideUnchanged]) {
    assert.ok(decode(markup).includes(words), words);
  }
  assert.match(markup, /<button type="button" class="lx-segment" aria-pressed="true">Last 30 days<\/button>/, "the last 30 days, chosen");
  assert.match(markup, /<input type="checkbox" role="switch" class="lx-visually-hidden" checked=""/, "the switch is on by default");
  assert.ok(decode(markup).includes(`placeholder="${EN.datePlaceholder}"`), "the dates are typed day first");
  assert.doesNotMatch(markup, /\sname=/, "no control is named: nothing goes into an address");
  assert.doesNotMatch(markup, /identifier|<select/i, "no form asks for an identifier or a language");
  assert.doesNotMatch(markup, /style=/, "no inline style (style-src 'self')");

  const page = renderLiveRadarPage();
  assert.match(page, /<html[^>]* data-layout="v4"/, "the V4 layout");
  assert.match(page, /<aside class="lx-dock"/, "the assistant docked beside it");
  assert.ok(page.includes(`<h1>${EN.heading}</h1>`) && page.includes(`<p>${EN.intro}</p>`));
  assert.ok(page.includes('<script src="/en/client-live-radar.js" defer=""></script>'));
  assert.ok(page.includes("<title>Changes - Lex</title>"));
});

test("the request carries the two dates, and an identifier, a language and a limit only when given", () => {
  assert.deepEqual(radarParameters(REQUEST), { date_from: "2024-02-01", date_to: "2024-02-01" });
  assert.deepEqual(radarParameters({ ...REQUEST, identifier: "/lu-legilux/x", language: "fra", limit: 200 }), { date_from: "2024-02-01", date_to: "2024-02-01", identifier: "/lu-legilux/x", language: "fra", limit: 200 });
  assert.deepEqual(radarParameters({ ...REQUEST, identifier: "   " }), { date_from: "2024-02-01", date_to: "2024-02-01" }, "a blank identifier is no identifier");
  assert.throws(() => radarParameters({ ...REQUEST, dateTo: "" }), /two calendar dates/);
  assert.throws(() => radarParameters({ ...REQUEST, language: "ltz" }), /not a language this form offers/);
  assert.throws(() => radarParameters({ ...REQUEST, limit: 201 }), /a limit is a whole number/);
});

test("dates are written and typed day first; the presets count back from today, or from the newest date held when it is earlier", () => {
  assert.equal(numericDate("2026-08-03"), "03/08/2026");
  assert.equal(typedDate("25/07/2026"), "2026-07-25");
  assert.equal(typedDate(" 3.8.2026 "), "2026-08-03");
  assert.equal(typedDate("31/02/2026"), null, "no such day");
  assert.equal(typedDate("2026-08-03"), null, "day first only");
  assert.equal(addDays("2026-03-01", -1), "2026-02-28");

  assert.equal(anchorOf("2026-11-15", "2030-09-15"), "2026-11-15", "a corpus holding later versions counts back from today");
  assert.equal(anchorOf("2026-11-15", "2026-08-03"), "2026-08-03", "a corpus that stops before today counts back from its newest date");
  assert.equal(anchorOf(null, "2026-08-03"), null, "nothing before today is known");
  assert.deepEqual(periodOf("last30", "2026-08-03"), { from: "2026-07-05", to: "2026-08-03" }, "30 days, both ends counted");
  assert.deepEqual(periodOf("thisYear", "2026-11-15"), { from: "2026-01-01", to: "2026-11-15" });
  assert.deepEqual(periodOf("lastYear", "2026-11-15"), { from: "2025-01-01", to: "2025-12-31" });
  assert.equal(periodOf("dates", "2026-11-15"), null);

  const windows = windowsOf({ from: "2026-07-25", to: "2026-08-03" });
  assert.deepEqual(windows, [{ from: "2026-07-25", to: "2026-08-03" }], "acceptance item 8's period is one window");
  const year = windowsOf(periodOf("thisYear", "2026-11-15"));
  assert.deepEqual(year[0], { from: "2026-10-17", to: "2026-11-15" }, "the latest window first");
  assert.equal(year.at(-1).from, "2026-01-01", "the earliest cut at the period's start");
  assert.ok(year.every((window, index) => index === 0 || addDays(window.to, 1) === year[index - 1].from), "the windows meet, day after day");
});

test("a text's own title (trap 2): never an amending act's, the short title first, the version prefix and the amended acts' list taken off", () => {
  // The Police law's groups as the live dossier served them on 2026-11-15, with a title naming its amending act put first.
  const police = [
    { expression_iri: "eli/etat/leg/loi/2018/07/18/a621/consolide/20260724/fr", language: "fra", short_titles: [], titles: [{ title: "Loi du 29 juillet 2020 portant modification de la loi modifiée du 18 juillet 2018 sur la Police grand-ducale." }] },
    { expression_iri: "eli/etat/leg/loi/2018/07/18/a621/consolide/20260725/fr", language: "fra", short_titles: [{ title: "Loi du 18 juillet 2018 sur la Police grand-ducale." }], titles: [{ title: "Version consolidée applicable au 25/07/2026 : Loi du 18 juillet 2018 sur la Police grand-ducale\\net portant modification :\\n1° du Code de procédure pénale ;" }] },
  ];
  assert.deepEqual(ownTitleOf(police, { workKey: "loi-2018-07-18-a621" }), { title: "Loi du 18 juillet 2018 sur la Police grand-ducale", language: "fra" },
    "a title naming another date first is the amending act's, and is passed over");
  assert.equal(ownTitleOf([police[1]], { workKey: "loi-2018-07-18-a621", expressionIri: police[1].expression_iri }).title, "Loi du 18 juillet 2018 sur la Police grand-ducale");
  assert.equal(ownTitleOf([{ ...police[1], short_titles: [] }], { workKey: "loi-2018-07-18-a621" }).title, "Loi du 18 juillet 2018 sur la Police grand-ducale",
    "with no short title, the title's first line, its version prefix taken off");
  assert.equal(ownTitleOf([police[0]], { workKey: "loi-2018-07-18-a621" }), null, "no title of its own: none, never the amending act's");
  assert.equal(ownTitleOf([{ language: "fra", short_titles: [{ title: "Code du travail" }], titles: [] }], { workKey: "code-travail" }).title, "Code du travail", "a code's key names no date");
  assert.equal(ownTitleOf(null, { workKey: "code-travail" }), null);

  // The publisher's other title shapes, as served on 2026-11-15.
  assert.equal(cleanTitle("Version consolidée applicable au 20/10/2026 : Loi du 4 juillet 2014\\n- portant réorganisation de l’Institut luxembourgeois de la normalisation"),
    "Loi du 4 juillet 2014 portant réorganisation de l’Institut luxembourgeois de la normalisation", "a heading that is only the kind and date takes its next line");
  assert.equal(cleanTitle("Loi du 24 juillet 2026 sur la promotion du journalisme professionnel et du débat démocratique et portant modification :\\n1° de la loi modifiée du 8 juin 2004"),
    "Loi du 24 juillet 2026 sur la promotion du journalisme professionnel et du débat démocratique", "the clause announcing the amended acts is taken off");
  assert.equal(cleanTitle("Règlement grand-ducal du 22 juin 2022 fixant :\\n1° les conditions et les modalités ;\\n2° les modalités d’examen"),
    "Règlement grand-ducal du 22 juin 2022 fixant : les conditions et les modalités", "a colon with nothing before it to stand alone takes its next line");
  const long = cleanTitle(`Loi du 1er août 2018 ${"relative à un sujet ".repeat(20)}`);
  assert.ok(long.length <= 221 && long.endsWith("…") && !long.includes("  "), "a very long title is cut at a word");
});

test("a row's articles in words: the non-zero kinds only, the counts grouped as the language groups them", () => {
  assert.equal(countsSentence({ added: 1, changed: 1, removed: 0, unchanged: 233 }, "en"), "1 article added, 1 changed, 233 identical");
  assert.equal(countsSentence({ added: 0, changed: 7, removed: 0, unchanged: 1181 }, "en"), "7 articles changed, 1,181 identical");
  assert.equal(countsSentence({ added: 0, changed: 0, removed: 1, unchanged: 14 }, "en"), "1 article removed, 14 identical");
  assert.equal(countsSentence({ added: 0, changed: 0, removed: 0, unchanged: 6 }, "en"), "No article changed, 6 identical");
  assert.equal(countsSentence({ added: 1, changed: 1, removed: 0, unchanged: 233 }, "fr"), "1 article ajouté, 1 modifié, 233 identiques");
  assert.equal(countsSentence({ added: 0, changed: 7, removed: 0, unchanged: 1181 }, "fr"), "7 articles modifiés, 1 181 identiques");
});

test("a period's rows: newest first, dated day first, the compared one opening the text page in compare mode, no permalink or hash", () => {
  const outcome = twoStateOutcome();
  const [first, later] = outcome.view.rows;
  const markup = renderToStaticMarkup(h(RadarAnswerView, { outcome }));
  const items = [...markup.matchAll(/<li class="lx-change" data-reason="([a-z_]+)">/g)].map((match) => match[1]);
  assert.deepEqual(items, ["changed", "first_held_state"], "the later version first");
  assert.ok(markup.includes(`<span class="lx-change-date">${numericDate(later.state.applicabilityDate)}</span>`));
  const href = textHref({ identifier: `/lu-legilux/${later.workKey}`, from: later.baseline.applicabilityDate, to: later.state.applicabilityDate }, "en").replaceAll("&", "&amp;");
  assert.ok(href.includes(later.baseline.applicabilityDate) && href.includes(later.state.applicabilityDate), "the pair, in the address");
  assert.ok(markup.includes(`<a class="lx-change-link" href="${href}" data-compare-link="">See what changed</a>`), "the compare mode of the text page");
  assert.equal([...markup.matchAll(/data-compare-link/g)].length, 1, "only a compared row compares");
  assert.ok(textOf(markup).includes(`${EN.reason.first_held_state}`), "a first version says why it is not compared");
  assert.ok(textOf(markup).includes(countsSentence(later.counts, "en")));
  assert.ok(!markup.includes(first.state.stateSha256) && !markup.includes(later.state.permalink), "no hash, no permalink");
  assert.doesNotMatch(textOf(markup).replaceAll(actNameOf(later.workKey, "en"), ""), /\b\d{4}-\d{2}-\d{2}\b/, "no ISO date (but in the act's name the table gives when no title is held)");
  assert.ok(markup.includes(`<p data-caveat="">${EN.caveat}</p>`), "the platform's caveat, one quiet line");

  const hidden = renderToStaticMarkup(h(RadarAnswerView, { outcome, hideUnchanged: true }));
  assert.deepEqual([...hidden.matchAll(/data-reason="([a-z_]+)"/g)].map((match) => match[1]), ["changed"], "the switch hides the versions where no article changed");
  const titles = { [later.workKey]: { title: "Loi du 10 août 1991 sur la profession d’avocat", language: "fra" } };
  const titled = renderToStaticMarkup(h(RadarAnswerView, { outcome, titles }));
  assert.ok(titled.includes('<span class="lx-change-title" lang="fr" data-title="own">Loi du 10 août 1991 sur la profession d’avocat</span>'), "the own title, in its language");
  const filtered = renderToStaticMarkup(h(RadarAnswerView, { outcome, titles, filter: "AVOCAT" }));
  assert.equal([...filtered.matchAll(/<li /g)].length, 2, "the filter reads the titles shown, without case or accents (both rows are one text)");
  const none = renderToStaticMarkup(h(RadarAnswerView, { outcome, titles, filter: "bail" }));
  assert.ok(none.includes(EN.noMatch));
  assert.equal(shownRows(outcome.view.rows, { hideUnchanged: true }).length, 1);
});

test("an empty period says so with its dates; a window before anything held says what is held", async () => {
  const empty = await loadLiveRadar({ contract, fetchImpl: answering(200, "application/json", envelopeOf("changes_in_period", "a window before anything held")).fetchImpl, request: { dateFrom: "1990-01-01", dateTo: "1990-12-31" } });
  assert.equal(empty.state, "success");
  assert.ok(renderToStaticMarkup(h(RadarAnswerView, { outcome: empty })).includes("Lex holds versions from 01/02/2024 to 01/02/2024; this period is outside them."));
  const quiet = twoStateOutcome((value) => { value.changes = []; value.population.versions_in_window = 0; value.population.works_in_window = 0; });
  assert.ok(renderToStaticMarkup(h(RadarAnswerView, { outcome: quiet, hideUnchanged: true })).includes(`No text changed its wording between ${numericDate(quiet.view.window.from)} and ${numericDate(quiet.view.window.to)}.`));
});

test("no mount is the card naming the missing index; an unreadable answer is said as such", async () => {
  const none = await loadLiveRadar({ contract, fetchImpl: answering(200, "application/json", envelopeOf("changes_in_period", "no corpus mounted")).fetchImpl, request: REQUEST });
  assert.equal(none.card, true);
  assert.equal(none.sentence, "This build has no Luxembourg index mounted.");
  const card = renderToStaticMarkup(h(RefusalCard, { code: none.code, sentence: none.sentence, payload: none.payload }));
  assert.equal(renderToStaticMarkup(h(RadarAnswerView, { outcome: none })), `<section data-answer-state="refusal">${asShown(none, card)}</section>`);
  const broken = envelopeOf("changes_in_period", "a window holding");
  broken.result.value.population.versions_in_window = 0;
  assert.equal(radarOutcome({ state: "success", envelope: broken }).state, "invalid_envelope", "a page listing more versions than it counts");
});

test("the page's session asks a period's latest window, then the titles of the rows shown, then an earlier window when asked", async () => {
  const radar = envelopeOf("changes_in_period", "a window holding");
  const dossier = envelopeOf("dossier", "the work, in the language");
  dossier.result.value.titles = [{ expression_iri: radar.result.value.changes[0].state.expression_iri, language: "fra", short_titles: [{ title: "Loi du 10 août 1991 sur la profession d’avocat." }], titles: [] }];
  const { calls, fetchImpl } = routing({
    changes_in_period: ({ date_from: from, date_to: to }) => {
      const answer = structuredClone(radar);
      Object.assign(answer.result.value, { requested_date_from: from, requested_date_to: to, window_from: from, window_to: to });
      const holds = from <= "2024-02-01" && to >= "2024-02-01";
      if (!holds) Object.assign(answer.result.value, { changes: [], population: { ...answer.result.value.population, versions_in_window: 0, works_in_window: 0, window_overlaps_what_is_held: false } });
      return answer;
    },
    dossier: () => dossier,
  });
  const states = [];
  const titles = {};
  const session = createChangesSession({ contract, fetchImpl, onState: (state) => states.push(state), onTitle: (key, title) => { titles[key] = title; } });
  assert.equal(session.open({ from: "2024-01-20", to: "2024-03-15" }), true);
  await settle();
  assert.deepEqual(calls.map((call) => call.operation), ["changes_in_period"], "one window asked");
  assert.deepEqual(calls[0].parameters, { date_from: "2024-02-15", date_to: "2024-03-15", limit: 200 }, "the latest 30 days of the period first");
  assert.equal(states.at(-1).outcome.state, "success");
  assert.equal(states.at(-1).outcome.view.rows.length, 0);
  assert.equal(states.at(-1).windowsLeft, 1);

  assert.equal(session.earlier(), true);
  await settle();
  assert.deepEqual(calls[1].parameters, { date_from: "2024-01-20", date_to: "2024-02-14", limit: 200 }, "the window before it, cut at the period's start");
  const shown = states.at(-1);
  assert.equal(shown.windowsLeft, 0);
  assert.equal(shown.outcome.view.rows.length, 1);
  assert.equal(shown.loadedFrom, "2024-01-20");
  assert.equal(session.earlier(), false, "no window left");

  session.titles(shown.outcome.view.rows);
  session.titles(shown.outcome.view.rows);
  await settle();
  assert.deepEqual(calls.slice(2).map((call) => call.parameters), [{ identifier: "/lu-legilux/loi-1991-08-10-n3" }], "one dossier per text, asked once");
  assert.deepEqual(titles, { "loi-1991-08-10-n3": { title: "Loi du 10 août 1991 sur la profession d’avocat", language: "fra" } });
  assert.ok(ROWS_PER_PAGE + 1 <= 15, "the first screen asks at most about 15 requests: one window and a title per row shown");
  session.cancel();
});

test("a window holding more versions than one answer serves is completed by its continue_from", async () => {
  const radar = envelopeOf("changes_in_period", "a window holding");
  const { calls, fetchImpl } = routing({
    changes_in_period: ({ date_from: from, date_to: to }) => {
      const answer = structuredClone(radar);
      Object.assign(answer.result.value, { requested_date_from: from, requested_date_to: to, window_from: from, window_to: to });
      // The first answer serves the row and says the next date not served; the second serves nothing more.
      if (from === "2024-01-05") Object.assign(answer.result.value, { truncated: true, continue_from: "2024-02-02" });
      else Object.assign(answer.result.value, { changes: [], population: { ...answer.result.value.population, versions_in_window: 0, works_in_window: 0, window_overlaps_what_is_held: false } });
      return answer;
    },
  });
  const states = [];
  const session = createChangesSession({ contract, fetchImpl, onState: (state) => states.push(state) });
  session.open({ from: "2024-01-05", to: "2024-02-03" });
  await settle();
  assert.deepEqual(calls.map((call) => call.parameters.date_from), ["2024-01-05", "2024-02-02"], "the window, then from its continue_from");
  assert.equal(states.at(-1).outcome.view.rows.length, 1, "the rows of both answers, each once");
  assert.equal(session.open({ from: "2024-02-03", to: "2024-01-05" }), false, "a period whose first date is after its second is not asked");
  assert.equal(states.at(-1).outcome.state, "invalid_request");
});

test("a session asks when told, says a request it will not send, and a new request cancels the one in flight", async () => {
  const outcomes = [];
  const settled = answering(200, "application/json", envelopeOf("changes_in_period", "a window holding"));
  let done;
  const session = createRadarSession({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome.state); if (outcome.state !== "loading") done?.(); } });
  assert.equal(session.ask({ dateFrom: "x", dateTo: "2024-01-01" }), false);
  assert.equal(settled.calls.length, 0);
  const arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.ask(REQUEST), true);
  await arrived;
  assert.deepEqual(outcomes, ["invalid_request", "loading", "success"]);

  const aborted = [];
  const hanging = async (url, init) => new Promise((resolve, reject) => {
    init.signal.addEventListener("abort", () => { aborted.push(JSON.parse(init.body).parameters.date_to); reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
  });
  const later = [];
  const second = createRadarSession({ contract, fetchImpl: hanging, onOutcome: (outcome) => later.push(outcome.state) });
  second.ask({ dateFrom: "2024-01-01", dateTo: "2024-01-02" });
  second.ask({ dateFrom: "2024-01-01", dateTo: "2024-01-03" });
  second.cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(aborted, ["2024-01-02", "2024-01-03"]);
  assert.deepEqual(later, ["loading", "loading"]);
});

test("two periods ending on the same day ask their latest window once (the journeys' J8: the window was asked twice)", async () => {
  const radar = envelopeOf("changes_in_period", "a window holding");
  const { calls, fetchImpl } = routing({
    changes_in_period: ({ date_from: from, date_to: to }) => {
      const answer = structuredClone(radar);
      Object.assign(answer.result.value, { requested_date_from: from, requested_date_to: to, window_from: from, window_to: to });
      return answer;
    },
  });
  const states = [];
  const session = createChangesSession({ contract, fetchImpl, onState: (state) => states.push(state) });
  session.open({ from: "2024-01-05", to: "2024-02-03" });
  session.open({ from: "2024-01-05", to: "2024-02-03" });
  session.open({ from: "2023-06-01", to: "2024-02-03" });
  await settle();
  assert.deepEqual(calls.map((call) => [call.parameters.date_from, call.parameters.date_to]), [["2024-01-05", "2024-02-03"]], "the latest window, asked once");
  assert.equal(states.at(-1).outcome.state, "success");
  session.retry();
  await settle();
  assert.equal(calls.length, 2, "a retry asks anew");
  session.cancel();
});
