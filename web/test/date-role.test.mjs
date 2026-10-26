// A Luxembourg state's date said with its role (R21; slice S7a of the Luxembourg data design). On
// lex-v3-luxembourg-index/9 every state the API serves names its date's role (`date_role`) and an act's entry into force
// beside it (`entry_into_force`); the reading, the dossier and the citation generator read it (`date-role.mjs`), so an act
// dated by its entry into force is never said as applicable from that date, in English or in French, and an answer of an
// index of schema 8 or 6, which names no role, is said as before. The reading also says, in place, the places of the
// publisher's document a version holds no article at (`missing_articles`), never skipping them in silence. Built from the
// census bundle and dossier the real handler sent (`envelope-samples.json`, `answer-samples.json`).

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import test from "node:test";
import { build } from "esbuild";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { DossierAnswerView, HistoryAnswerView, Provenance, RadarAnswerView, ReadingAnswerView, SearchAnswerView } from "../.react-build/app.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { searchOutcome } from "../scripts/live-search.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { readEvidenceBundle } from "../scripts/reading-answer.mjs";
import { holdsToday, readDateFacts, readDateRole } from "../scripts/date-role.mjs";
import { answersToday } from "../scripts/initial-answer.mjs";
import { radarOutcome } from "../scripts/live-radar.mjs";
import { historyOutcome } from "../scripts/live-history.mjs";
import { renderProvenance } from "../scripts/provenance.mjs";
import { citationForms } from "../scripts/citation.mjs";
import { LIVE_CHROME, fillText } from "../scripts/live-chrome.mjs";
import { exportState, pinKey } from "../scripts/live-export.mjs";
import { CSV_COLUMNS, exportCsv } from "../scripts/export-build.mjs";

const web = new URL("../", import.meta.url);
const census = JSON.parse(await readFile(new URL("../schemas/v3-platform/envelope-samples.json", web), "utf8"));
const EN = LIVE_CHROME.en;
const FR = LIVE_CHROME.fr;
const OBSERVED = "2026-09-18T07:00:00.0000000Z";
const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'" };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|#x27);/g, (entity) => ENTITIES[entity]);

function sample(operation, scenario) {
  const entry = census.envelopes.find((candidate) => candidate.operation === operation && candidate.scenario.startsWith(scenario));
  assert.ok(entry, `${operation}: ${scenario}`);
  return structuredClone(entry.envelope);
}

const bundle = () => sample("evidence_bundle", "the work on its state's date");

/**
 * An answer of an index of schema 8 or 6 as the real handler sent it before index 9 (the legacy mount's recorded answers,
 * `base-answers.json`, replayed byte for byte by the platform's own tests): what "an older index names no role" is held to,
 * rather than a census answer of index 9 compared with itself (the review of #987).
 */
const legacyAnswers = JSON.parse(await readFile(new URL("../tests/Lex.V3.Ingest.Tests/Fixtures/LegacySchema8Mount/base-answers.json", web), "utf8"));
function legacy(operation, schema = 8) {
  const entry = legacyAnswers[`schema${schema}_answers`].find((candidate) => candidate.operation === operation);
  assert.ok(entry, `the schema-${schema} mount's recorded ${operation}`);
  const envelope = JSON.parse(entry.body);
  assert.ok(envelope.result, `the schema-${schema} ${operation} is an answer`);
  return envelope;
}

/** The French app, built as the French pages are (`__LEX_CHROME_LOCALE__`). */
async function frenchApp() {
  const out = fileURLToPath(new URL(".react-build/app-fr-date-role-radar-test.mjs", web));
  await build({
    stdin: { contents: "export * from './app/index.jsx';", resolveDir: fileURLToPath(web), sourcefile: "date-role-radar-french-test.jsx", loader: "jsx" },
    outfile: out,
    bundle: true,
    format: "esm",
    platform: "node",
    jsx: "automatic",
    packages: "external",
    logLevel: "silent",
    define: { __LEX_CHROME_LOCALE__: JSON.stringify("fr") },
  });
  return import(`${pathToFileURL(out).href}?${Date.now()}`);
}

/** The census bundle's state dated by its entry into force, as index 9 serves an act its file does not date. */
function byEntryIntoForce(envelope) {
  const state = envelope.result.value.states[0];
  state.date_role = "entry_into_force";
  state.entry_into_force = state.applicability_date;
  return envelope;
}

test("the role is read where the answer names one, refused when it is none of the three, and absent on an older index", () => {
  assert.equal(readDateRole({}, "a schema-8 state"), null, "an index of schema 8 or 6 names no role");
  for (const role of ["applicability", "applicability_in_file", "entry_into_force"]) {
    assert.equal(readDateRole({ date_role: role }, "state"), role);
  }
  assert.throws(() => readDateRole({ date_role: "in_force" }, "state"), /names the date role "in_force"/);
  assert.throws(() => readDateFacts({ date_role: "entry_into_force", entry_into_force: "2001-01-02" }, "2001-01-01", "state"),
    /is dated by its entry into force 2001-01-01, and names 2001-01-02/);
  assert.deepEqual({ ...readDateFacts({ date_role: "applicability_in_file", entry_into_force: "2019-03-12" }, "2019-01-01", "state") },
    { dateRole: "applicability_in_file", entryIntoForce: "2019-03-12" }, "a file's date and the entry into force are two values");
});

test("the reading heads a state dated by its entry into force as that, never as applying from it", () => {
  const plain = readingOutcome({ state: "success", envelope: bundle() });
  const plainMarkup = decode(renderToStaticMarkup(h(ReadingAnswerView, { outcome: plain })));
  const state = bundle().result.value.states[0];
  const workKey = bundle().result.value.work_key;
  assert.ok(plainMarkup.includes(fillText(EN.reading.stateHeading, { work: workKey, language: state.language, from: state.applicability_date })),
    "a state dated by applicability is headed as before");

  const outcome = readingOutcome({ state: "success", envelope: byEntryIntoForce(bundle()) });
  const markup = decode(renderToStaticMarkup(h(ReadingAnswerView, { outcome })));
  assert.ok(markup.includes(fillText(EN.reading.stateHeadingEntryIntoForce, { work: workKey, language: state.language, from: state.applicability_date })));
  assert.ok(markup.includes('data-date-role="entry_into_force"'));
  const heading = markup.slice(markup.indexOf("<h2 data-date-role"), markup.indexOf("</h2>", markup.indexOf("<h2 data-date-role")));
  assert.doesNotMatch(heading, /applying from|applicable from/, "an entry into force is never said as application (R21)");
});

test("the citation generator says a version dated by its entry into force as that, in its usual and its evidence forms", () => {
  const view = readEvidenceBundle(byEntryIntoForce(bundle()).result.value);
  const version = view.states[0];
  const article = version.articles[0];
  const forms = citationForms({ view, version, article, observedAt: OBSERVED });
  const words = forms.language === "fra" ? FR.citation : EN.citation;
  assert.ok(forms.evidenceLines.includes(fillText(words.evidence.versionEntryIntoForce, { date: version.applicabilityDate })), forms.evidenceLines.join("\n"));
  assert.ok(!forms.evidenceLines.includes(fillText(words.evidence.version, { date: version.applicabilityDate })));
  const plain = readEvidenceBundle(bundle().result.value);
  const plainForms = citationForms({ view: plain, version: plain.states[0], article: plain.states[0].articles[0], observedAt: OBSERVED });
  assert.ok(plainForms.evidenceLines.includes(fillText(words.evidence.version, { date: version.applicabilityDate })), "applicability is said as before");
});

test("the dossier marks a date that is an entry into force and says once what the mark means", () => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "dossier" && candidate.envelope.result?.value?.publisher === "lu-legilux");
  assert.ok(entry, "the census holds a Luxembourg dossier");
  const envelope = structuredClone(entry.envelope);
  const value = envelope.result.value;
  value.states[0].date_role = "entry_into_force";
  value.states[0].entry_into_force = value.states[0].applicability_date;
  const markup = decode(renderToStaticMarkup(h(DossierAnswerView, { outcome: dossierOutcome({ state: "success", envelope }) })));
  assert.ok(markup.includes(fillText(EN.dossier.entryIntoForceDate, { date: value.states[0].applicability_date })));
  assert.ok(markup.includes(`<p data-entry-into-force-note="">${EN.dossier.entryIntoForceNote}</p>`));
});

test("the reading says in place an article of the publisher's document its version does not hold", () => {
  const envelope = bundle();
  const state = envelope.result.value.states[0];
  assert.ok(state.articles.length >= 2, "the census state quotes two articles or more");
  state.articles.forEach((article, index) => { article.document_position = index === 0 ? 1 : index + 2; });
  state.missing_articles = [{ from_position: 2, to_position: 2, count: 1, after_publisher_id: state.articles[0].publisher_id, before_publisher_id: state.articles[1].publisher_id }];
  const view = readEvidenceBundle(envelope.result.value);
  assert.deepEqual(view.states[0].articles.slice(0, 2).map((article) => article.documentPosition), [1, 3]);
  const markup = decode(renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope }) })));
  const second = markup.indexOf(`data-article="${state.articles[1].publisher_id}"`);
  const notice = markup.indexOf('data-missing-articles="1"');
  assert.ok(notice > second && notice < markup.indexOf("<h3>", second), "the notice stands in the article after the gap, before its heading");
  assert.ok(markup.includes(EN.reading.missingBefore.one));

  const broken = bundle();
  broken.result.value.states[0].missing_articles = [{ from_position: 2, to_position: 3, count: 1, after_publisher_id: null, before_publisher_id: "art_1" }];
  assert.throws(() => readEvidenceBundle(broken.result.value), /runs from 2 to 3 and counts 1/);
});

test("the French pages say the entry into force, the missing article and the dossier's mark in the reviewed French", async () => {
  const out = fileURLToPath(new URL(".react-build/app-fr-date-role-test.mjs", web));
  await build({
    stdin: { contents: "export * from './app/index.jsx';", resolveDir: fileURLToPath(web), sourcefile: "date-role-french-test.jsx", loader: "jsx" },
    outfile: out,
    bundle: true,
    format: "esm",
    platform: "node",
    jsx: "automatic",
    packages: "external",
    logLevel: "silent",
    define: { __LEX_CHROME_LOCALE__: JSON.stringify("fr") },
  });
  const french = await import(`${pathToFileURL(out).href}?${Date.now()}`);
  const envelope = byEntryIntoForce(bundle());
  const state = envelope.result.value.states[0];
  state.articles.forEach((article, index) => { article.document_position = index === 0 ? 1 : index + 2; });
  state.missing_articles = [{ from_position: 2, to_position: 2, count: 1, after_publisher_id: state.articles[0].publisher_id, before_publisher_id: state.articles[1].publisher_id }];
  const markup = decode(renderToStaticMarkup(h(french.ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope }) })));
  assert.ok(markup.includes(fillText(FR.reading.stateHeadingEntryIntoForce, { work: envelope.result.value.work_key, language: state.language, from: state.applicability_date })), "the French heading");
  assert.ok(markup.includes(FR.reading.missingBefore.one), "the French notice");
});

test("every form this slice adds says no application word of an entry into force, in English and in French", () => {
  for (const [language, table] of [["en", EN], ["fr", FR]]) {
    const forms = {
      "reading.stateHeadingEntryIntoForce": table.reading.stateHeadingEntryIntoForce,
      "reading.stateHeadingEntryIntoForceNext": table.reading.stateHeadingEntryIntoForceNext,
      "reading.missingBefore.one": table.reading.missingBefore.one,
      "reading.missingBefore.other": table.reading.missingBefore.other,
      "dossier.entryIntoForceDate": table.dossier.entryIntoForceDate,
      "dossier.entryIntoForceNote": table.dossier.entryIntoForceNote,
      "citation.luxembourg.versionEntryIntoForce": table.citation.luxembourg.versionEntryIntoForce,
      "citation.evidence.versionEntryIntoForce": table.citation.evidence.versionEntryIntoForce,
      "search.versionAsPublishedEntryIntoForce": table.search.versionAsPublishedEntryIntoForce,
      "search.sameWording.one": table.search.sameWording.one,
      "search.sameWording.other": table.search.sameWording.other,
    };
    for (const [key, form] of Object.entries(forms)) {
      assert.equal(typeof form, "string", `${language} ${key}`);
      assertTimeVocabulary(form, `${language} ${key}`);
      assert.doesNotMatch(form, /applicable from|applying from|applicable à partir|s’applique à partir du \{from\}/i, `${language} ${key}: an entry into force is never an application`);
    }
  }
  // The guard refuses "en vigueur" as a state still, and lets through only the noun of the typed date.
  assert.throws(() => assertTimeVocabulary("la version en vigueur", "a test"), /R21 never says/);
  assert.equal(assertTimeVocabulary("entrée en vigueur le 2024-02-01", "a test"), "entrée en vigueur le 2024-02-01");
});

test("the search page says a version dated by its entry into force as that, and the versions a passage's wording was folded from", () => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "search" && candidate.scenario.startsWith("the phrase as the site asks it"));
  assert.ok(entry, "the census holds the page's search");
  const envelope = structuredClone(entry.envelope);
  const hits = envelope.result.value.hits;
  assert.ok(hits.length > 0, "the census search has hits");
  for (const hit of hits) {
    hit.date_role = "entry_into_force";
    hit.entry_into_force = hit.applicability_date;
  }
  hits[0].same_wording_states = [{
    applicability_date: "2001-01-01", date_role: "applicability", state_sha256: "a".repeat(64),
    article_identity_sha256: "b".repeat(64), permalink: `/lu-legilux/${hits[0].work_key}/2001-01-01--${"a".repeat(64)}/fra`,
  }];
  const outcome = searchOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  assert.equal(outcome.view.hits[0].dateRole, "entry_into_force");
  assert.equal(outcome.view.hits[0].sameWording.length, 1);
  const markup = decode(renderToStaticMarkup(h(SearchAnswerView, { outcome })));
  assert.ok(markup.includes(fillText(EN.search.versionAsPublishedEntryIntoForce, { date: hits[0].applicability_date })), "the version line");
  assert.ok(!markup.includes(fillText(EN.search.versionAsPublished, { date: hits[0].applicability_date })), "never applicable from it");
  assert.ok(markup.includes('data-same-wording="1"'));
  assert.ok(markup.includes(fillText(EN.search.sameWording.one, { count: 1 })));

  // An answer of an index of schema 8 names no role, and the page says what it said before: the version line of its date.
  const older = legacy("search");
  assert.ok(older.result.value.hits.length > 0 && older.result.value.hits.every((hit) => !Object.hasOwn(hit, "date_role")), "schema 8 names no role");
  const plain = searchOutcome({ state: "success", envelope: older });
  assert.equal(plain.state, "success", plain.sentence);
  assert.ok(plain.view.hits.every((hit) => hit.dateRole === null));
  const plainMarkup = decode(renderToStaticMarkup(h(SearchAnswerView, { outcome: plain, onNextPage: () => {} })));
  assert.ok(!plainMarkup.includes("dated by its entry into force"), "no role is said where none is served");
});

test("the CSV export says what its date is in a date_role column after the segments, and nothing on an older index (R21; the review of #987)", () => {
  // The column is appended, so every column before it keeps its place.
  assert.deepEqual(CSV_COLUMNS.slice(-2), ["segments", "date_role"]);
  const csvOf = (envelope) => {
    const outcome = readingOutcome({ state: "success", envelope });
    assert.equal(outcome.state, "success", outcome.sentence);
    const state = outcome.view.states[0];
    const pins = new Set([...state.articles, ...state.articlesWithoutText].map((article) => pinKey(state.stateSha256, article.publisherId)));
    const rows = exportCsv(exportState(outcome, pins).model).split("\r\n").filter((line) => line.length > 0);
    assert.equal(rows[0], CSV_COLUMNS.join(","));
    return rows.slice(1).map((row) => row.slice(row.lastIndexOf(",") + 1));
  };
  // A state dated by its entry into force: its applies_from cell is that date, and the role says what it is.
  const dated = csvOf(byEntryIntoForce(bundle()));
  assert.ok(dated.length > 0 && dated.every((role) => role === "entry_into_force"), JSON.stringify(dated));
  // The census state dated by its own applicability date.
  assert.ok(csvOf(bundle()).every((role) => role !== "entry_into_force" && role.length > 0));
  // An index of schema 8 names no role: the cell is empty.
  const older = legacy("evidence_bundle");
  assert.ok(older.result.value.states.every((state) => !Object.hasOwn(state, "date_role")), "schema 8 names no role");
  assert.ok(csvOf(older).every((role) => role === ""));
});

test("the radar says a row whose state is an act dated by its entry into force as that, in English and in French (R21; the review of #987)", async () => {
  const envelope = structuredClone(census.envelopes.find((entry) => entry.operation === "changes_in_period" && entry.envelope.result?.value?.changes?.length).envelope);
  const row = envelope.result.value.changes[0];
  Object.assign(row.state, { date_role: "entry_into_force", entry_into_force: row.state.applicability_date, state_kind: "as_published" });
  const date = row.state.applicability_date;
  const outcome = radarOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  assert.equal(outcome.view.rows[0].state.dateRole, "entry_into_force");
  const english = decode(renderToStaticMarkup(h(RadarAnswerView, { outcome })));
  assert.ok(english.replace(/<[^>]+>/g, "").includes(`the act as published, dated by its entry into force on ${date} (publisher)`), "the row's role");
  assert.ok(!english.replace(/<[^>]+>/g, "").includes(`from ${date}:`), "never a bare from of it");
  const french = await frenchApp();
  const frenchText = decode(renderToStaticMarkup(h(french.RadarAnswerView, { outcome }))).replace(/<[^>]+>/g, "");
  assert.ok(frenchText.includes(`l’acte tel que publié, daté de son entrée en vigueur le ${date} (éditeur)`), "the row's role, in French");
  assert.ok(!frenchText.includes(`à partir du ${date}`));
  // A row not compared after the act agrees with it (`radar.notComparedAct`; the French review of #987's repair round).
  if (outcome.view.rows[0].reason !== null) {
    assert.ok(frenchText.includes("(éditeur)\u00a0: non comparé\u00a0:"), frenchText);
    assert.ok(!frenchText.includes("(éditeur)\u00a0: non comparée"), "never feminine after the act");
  }
  assert.notEqual(outcome.view.rows[0].reason, null, "the census row is one not compared, so the agreement is said");
  // An older index names no role, and the row says what it said before.
  const older = legacy("changes_in_period");
  const plain = radarOutcome({ state: "success", envelope: older });
  assert.equal(plain.state, "success", plain.sentence);
  assert.ok(plain.view.rows.every((one) => one.state.dateRole === null));
});

test("a state answers today by its interval's end on index 9, so a closed state is never today's (the review of #987, F3)", () => {
  const today = "2026-10-18";
  const view = readEvidenceBundle(bundle().result.value);
  const state = view.states[0];
  const closed = { ...state, endKnown: true, applicableTo: "2015-01-01", nextApplicabilityDate: null };
  const open = { ...state, endKnown: true, applicableTo: null, nextApplicabilityDate: null };
  const later = { ...state, endKnown: true, applicableTo: "2030-01-01", nextApplicabilityDate: null };
  const legacy = { ...state, endKnown: false, applicableTo: null, nextApplicabilityDate: null };
  assert.equal(holdsToday(closed, today), false, "closed before today");
  assert.equal(holdsToday(open, today), true);
  assert.equal(holdsToday(later, today), true);
  assert.equal(holdsToday(legacy, today), true, "schema 8 or 6: the next held state bounds it, and there is none");
  assert.equal(answersToday(view, closed, today), false, "its readable address does not show a closed state");
  assert.equal(answersToday(view, open, today), true);

  // The usual citation of a closed state names its version's date; today's is the bare usual form.
  const notPublished = { ...view, asPublished: null };
  const article = state.articles[0];
  const current = citationForms({ view: notPublished, version: open, article, observedAt: OBSERVED });
  const dated = citationForms({ view: notPublished, version: closed, article, observedAt: OBSERVED });
  assert.equal(typeof current.conventional, "string", "the census state has a usual citation");
  assert.notEqual(dated.conventional, current.conventional, "a closed state's usual form is dated, today's is not");
  assert.ok(dated.conventional.startsWith(current.conventional), "the dated form is the usual one with its version");
});

test("an article's own date beside a state dated by its entry into force is never said as applying (the review of #987, F5)", () => {
  const envelope = byEntryIntoForce(bundle());
  const state = envelope.result.value.states[0];
  const conflicting = state.articles.filter((article) => article.validity_conflict);
  assert.ok(conflicting.length > 0, "the census bundle holds articles whose own date differs from the state's");
  const markup = decode(renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope }) })));
  const first = conflicting[0];
  assert.ok(markup.includes(fillText(EN.reading.validityConflictEntryIntoForce, { own: first.article_valid_from, state: state.applicability_date })));
  assert.ok(!markup.includes(fillText(EN.reading.validityConflict, { own: first.article_valid_from, state: state.applicability_date })), "never 'its state applies from'");
});

test("the provenance page heads a state by its date's role, in both renderers (the review of #987, F7)", async () => {
  const samples = JSON.parse(await readFile(new URL("../schemas/v3-platform/answer-samples.json", web), "utf8"));
  const row = samples.sampled.find((candidate) => candidate.operation === "provenance");
  assert.ok(row, "the census holds a provenance answer");
  let n = 0;
  const filled = JSON.parse(JSON.stringify(row.answer), (key, value) =>
    value === "<varies-per-run>" ? (String((n += 1)) + "0123456789abcdef".repeat(4)).slice(0, 64) : value);
  assert.ok(Object.hasOwn(filled.states[0], "date_role"), "the census answer is index 9's");
  const date = filled.states[0].applicability_date;
  const render = (answer) => [decode(renderProvenance(answer)), decode(renderToStaticMarkup(h(Provenance, { answer })))];

  const byForce = structuredClone(filled);
  Object.assign(byForce.states[0], { date_role: "entry_into_force", entry_into_force: date, in_file_date: null });
  for (const markup of render(byForce)) {
    assert.ok(markup.includes(`the act as published, dated by its entry into force on ${date} (publisher)`));
    assert.ok(!markup.includes(`applicable from ${date}`), "never said as applicable from its entry into force");
  }
  for (const markup of render(filled)) {
    assert.ok(markup.includes(`applicable from ${date}`), "a state dated by its applicability is said so");
  }
});

test("the radar says a change it cannot compare for want of a held baseline, in English and in French (the review of #987, F6)", async () => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "changes_in_period" && candidate.envelope.result?.value?.changes?.length > 0);
  assert.ok(entry);
  const envelope = structuredClone(entry.envelope);
  const row = envelope.result.value.changes[0];
  Object.assign(row, { reason: "no_held_baseline", baseline: null, wording_changed: null, counts: null, diff: null, candidates: null });
  const outcome = radarOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = decode(renderToStaticMarkup(h(RadarAnswerView, { outcome })));
  assert.ok(markup.includes(EN.radar.reason.no_held_baseline), "the reason is said");
  const broken = structuredClone(envelope);
  broken.result.value.changes[0].candidates = ["a", "b"];
  assert.notEqual(radarOutcome({ state: "success", envelope: broken }).state, "success", "a no-baseline row names no candidates");
});

test("the repair round's three forms say no application word of an entry into force, in either language", () => {
  for (const [language, table] of [["en", EN], ["fr", FR]]) {
    for (const [key, form] of Object.entries({
      "reading.validityConflictEntryIntoForce": table.reading.validityConflictEntryIntoForce,
      "search.dated": table.search.dated,
      "radar.reason.no_held_baseline": table.radar.reason.no_held_baseline,
      "history.carriedEntryIntoForce.one": table.history.carriedEntryIntoForce.one,
      "history.carriedEntryIntoForce.other": table.history.carriedEntryIntoForce.other,
      "history.carriedInEntryIntoForce.one": table.history.carriedInEntryIntoForce.one,
      "history.carriedInEntryIntoForce.other": table.history.carriedInEntryIntoForce.other,
      "history.absentRowEntryIntoForce": table.history.absentRowEntryIntoForce,
    })) {
      assert.equal(typeof form, "string", `${language} ${key}`);
      assertTimeVocabulary(form, `${language} ${key}`);
    }
    assert.doesNotMatch(table.reading.validityConflictEntryIntoForce, /applies from|s’applique/, `${language}: the act dated by its entry into force is not said to apply`);
    assert.doesNotMatch(table.search.dated, /latest version|dernière version/, `${language}: the dated search's rule is index 9's`);
  }
});

test("the provision history says its first row's and an absent state's entry into force as that, never as from it (R21; the review of #987)", () => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "article_history" && candidate.envelope.result);
  assert.ok(entry, "the census holds a provision history");
  const envelope = structuredClone(entry.envelope);
  const answer = envelope.result.value;
  const row = answer.states[0];
  assert.ok(Object.hasOwn(row, "date_role"), "on index 9 the history's rows carry their catalogue facts");
  const render = (value) => decode(renderToStaticMarkup(h(HistoryAnswerView, { outcome: historyOutcome({ state: "success", envelope: value }) })));

  // As served: the row's date is the file's, said as from it.
  const asServed = render(envelope);
  const asked = answer.requested_language;
  const carried = (table) => (asked === null
    ? fillText(table.history.carried.one, { count: 1, from: row.applicability_date })
    : fillText(table.history.carriedIn.one, { count: 1, language: asked, from: row.applicability_date }));
  assert.ok(asServed.includes(carried(EN)), "a row dated by its file is said as from that date");

  // Dated by its entry into force, with a state that does not carry the article: both said with the role.
  Object.assign(row, { date_role: "entry_into_force", entry_into_force: row.applicability_date, in_file_date: null });
  const digest = "a".repeat(64);
  const earlier = "2020-01-01";
  answer.absent_in_states = [{
    language: row.language,
    applicability_date: earlier,
    state_sha256: digest,
    permalink: `/lu-legilux/${answer.work_key}/${earlier}--${digest}/${row.language}`,
    date_role: "entry_into_force",
    entry_into_force: earlier,
  }];
  const byForce = render(envelope);
  const said = asked === null
    ? fillText(EN.history.carriedEntryIntoForce.one, { count: 1, from: row.applicability_date })
    : fillText(EN.history.carriedInEntryIntoForce.one, { count: 1, language: asked, from: row.applicability_date });
  assert.ok(byForce.includes(said), "the lineage's first row is said dated by its entry into force");
  assert.ok(!byForce.includes(carried(EN)), "never as from its entry into force");
  assert.ok(byForce.includes(fillText(EN.history.absentRowEntryIntoForce, { language: row.language, from: earlier, permalink: "" }).trimEnd()));
  assert.ok(!byForce.includes(`${row.language}, from ${earlier}:`), "an absent state's entry into force is not said as from it");

  // An index of schema 8 or 6 names no role, and its rows are read as before.
  for (const state of [...answer.states, ...answer.absent_in_states]) {
    for (const key of ["date_role", "entry_into_force", "in_file_date"]) delete state[key];
  }
  assert.ok(render(envelope).includes(`${row.language}, from ${earlier}:`));
});
