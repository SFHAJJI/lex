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

import { DossierAnswerView, ReadingAnswerView, SearchAnswerView } from "../.react-build/app.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { searchOutcome } from "../scripts/live-search.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { readEvidenceBundle } from "../scripts/reading-answer.mjs";
import { readDateFacts, readDateRole } from "../scripts/date-role.mjs";
import { citationForms } from "../scripts/citation.mjs";
import { LIVE_CHROME, fillText } from "../scripts/live-chrome.mjs";

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

  // An answer of an index of schema 8 or 6 names no role, and the page says what it said before.
  const plain = searchOutcome({ state: "success", envelope: structuredClone(entry.envelope) });
  assert.equal(plain.view.hits[0].dateRole ?? null, entry.envelope.result.value.hits[0].date_role ?? null);
});
