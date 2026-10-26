// The catalogue's chain and the textless versions said on the pages (slices S7b and S7c of the Luxembourg data design). On
// lex-v3-luxembourg-index/9 the refusal card says, in its own words and the payload's values, why no version covers a date
// (the six reasons) and which version covering it this site holds no text of; the dossier lists the catalogue's versions
// held without text and its gaps, overlaps, same-date groups, unclear end days and invalid intervals; the history says where
// no version with held text covers its dates; the reading labels an expression in another language and says what a
// consolidated version or a code's version is not; and Compare, the export and the dated search say an act dated by its entry
// into force as that. In English and in the reviewed French. Built from the census answers the real handler sent
// (`envelope-samples.json`), changed only where index 9's facts are set, as the platform sets them; every value synthetic.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import test from "node:test";
import { build } from "esbuild";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { CompareAnswerView, DossierAnswerView, ExportAnswerView, ExportPanel, HistoryAnswerView, ReadingAnswerView, RefusalCard, SearchAnswerView } from "../.react-build/app.mjs";
import { REFUSAL_CARD_COPY, reasonView, renderRefusalCard, validateRefusal } from "../scripts/refusal-card.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { historyOutcome } from "../scripts/live-history.mjs";
import { compareOutcome } from "../scripts/live-compare.mjs";
import { searchOutcome } from "../scripts/live-search.mjs";
import { exportState, pinKey } from "../scripts/live-export.mjs";
import { exportJson } from "../scripts/export-build.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { LIVE_CHROME, entriesOf, fillText } from "../scripts/live-chrome.mjs";

const web = new URL("../", import.meta.url);
const census = JSON.parse(await readFile(new URL("../schemas/v3-platform/envelope-samples.json", web), "utf8"));
const refusalSamples = JSON.parse(await readFile(new URL("../schemas/v3-platform/refusal-payload-samples.json", web), "utf8"));

/**
 * The payload the platform's handler sent for an exact reference whose ELI names the act as published, at a date a later
 * consolidation covers (`outside_named_version`, the review of #987, F2), as the refusal census recorded it.
 */
function outsideNamedVersion() {
  const row = refusalSamples.produced.find((candidate) => candidate.code === "no_version_for_date");
  const payload = [row.payload, ...(row.more_payloads ?? [])].find((candidate) => candidate.reason === "outside_named_version");
  assert.ok(payload, "the refusal census holds the platform's outside_named_version payload");
  return structuredClone(payload);
}
const EN = LIVE_CHROME.en;
const FR = LIVE_CHROME.fr;
const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'", "&#39;": "'" };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|#x27|#39);/g, (entity) => ENTITIES[entity]);
const DIGEST_A = "a".repeat(64);
const capitalised = (text) => `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
const DIGEST_B = "b".repeat(64);

function sample(operation, scenario) {
  const entry = census.envelopes.find((candidate) => candidate.operation === operation && candidate.scenario.startsWith(scenario));
  assert.ok(entry, `${operation}: ${scenario}`);
  return structuredClone(entry.envelope);
}

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
  const out = fileURLToPath(new URL(".react-build/app-fr-catalogue-chain-test.mjs", web));
  await build({
    // The search page's outcome too, so its head sentence is said as the French page says it.
    stdin: { contents: "export * from './app/index.jsx'; export { searchOutcome as liveSearchOutcome } from './scripts/live-search.mjs';", resolveDir: fileURLToPath(web), sourcefile: "catalogue-chain-french-test.jsx", loader: "jsx" },
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

/** The census refusal before a work's history, as index 9 sends it, with one reason's members set as the platform sets them. */
function noVersion(reason, members = {}) {
  const payload = { ...sample("evidence_bundle", "a date before the work's history").refusal.helpful_payload, reason, ...members };
  return payload;
}

const REASON_PAYLOADS = Object.freeze({
  before_history: () => noVersion("before_history"),
  catalogue_hole: () => noVersion("catalogue_hole", {
    requested_date: "2021-07-01", history_begins: "1798-11-07", nearest_earlier: "2020-01-01", nearest_later: "2022-01-01",
    hole_from: "2021-06-01", hole_to: "2022-01-01", named_states: ["/lu-legilux/w/2020-01-01"], named_state_sha256s: [DIGEST_A],
  }),
  after_closed_end: () => noVersion("after_closed_end", {
    requested_date: "2016-01-01", history_begins: "2005-01-05", nearest_earlier: "2010-01-01", nearest_later: null,
    closed_on: "2015-01-01", base_act_iri: "http://data.legilux.public.lu/eli/etat/leg/loi/2005/01/01/n1/jo",
    base_act_in_force_status: [], base_act_no_longer_in_force: ["2015-01-01"], base_act_rule: "the base act's facts, verbatim",
  }),
  publisher_end_convention_unclear: () => noVersion("publisher_end_convention_unclear", {
    requested_date: "2022-12-31", history_begins: "2020-01-01", nearest_earlier: "2020-01-01", nearest_later: "2023-01-01",
  }),
  publisher_interval_invalid: () => noVersion("publisher_interval_invalid", {
    requested_date: "2023-08-01", history_begins: "2022-12-14", nearest_earlier: "2023-07-17", nearest_later: "2024-01-01",
  }),
  no_expression_in_language: () => noVersion("no_expression_in_language", {
    requested_date: "2018-01-01", language: "eng", history_begins: "2017-07-25", nearest_earlier: "2017-07-25", nearest_later: null,
  }),
  // The platform's own payload, as its handler sent it (the review of #987).
  outside_named_version: outsideNamedVersion,
});

/** A textless version's refusal, as the platform's RefuseTextless sends it (S7a). */
function textless(status, { kind = "consolidation", role = "applicability", date = "2020-01-01", requested = "2020-06-01" } = {}) {
  const withheld = status === "withheld_rights";
  const resource = "http://data.legilux.public.lu/eli/etat/leg/loi/1798/11/03/n1/consolide/20200101";
  return {
    code: withheld ? "text_withheld" : "text_not_available",
    payload: {
      official_identity: resource,
      ...(withheld ? { official_link: resource, content_sha256: DIGEST_A } : { official_source: resource, retained_transport_evidence: "none" }),
      requested_date: requested,
      state_sha256: DIGEST_A,
      permalink: `/lu-legilux/loi-1798-11-03-n1/${date}--${DIGEST_A}/fra`,
      stable_coordinate: `/lu-legilux/loi-1798-11-03-n1/${date}`,
      language: "fra",
      applicability_date: date,
      date_role: role,
      state_kind: kind,
      applicable_to: "2021-06-01",
      applicable_to_source: "publisher",
      boundary: "half_open",
      text_status: status,
      text_status_reason: "synthetic",
      rule: "the state covering the date is in the publisher's catalogue and this index holds none of its text",
      what_would_answer: ["new_official_observation"],
      asserts_absence_of_law: false,
    },
  };
}

test("the refusal card says why no version covers the date for each of index 9's reasons, with the payload's own values", async () => {
  const french = await frenchApp();
  for (const [reason, payloadOf] of Object.entries(REASON_PAYLOADS)) {
    const payload = payloadOf();
    const card = { code: "no_version_for_date", sentence: "No state of this work that this index holds applies on that date.", payload };
    validateRefusal(card);
    const lines = reasonView(card.code, payload);
    assert.equal(lines[0].key, `reasons.${reason}`);
    for (const [value, member] of [[payload.catalogue_read_no_later_than, "read"], [payload.hole_from, "from"], [payload.closed_on, "closed"]]) {
      if (EN.refusalCard.reasons[reason].includes(`{${member}}`)) assert.ok(lines[0].text.includes(value), `${reason}: says ${member}`);
    }
    assert.doesNotMatch(lines[0].text, /\{[a-zA-Z]+\}/, `${reason}: every placeholder filled`);
    const markup = decode(renderToStaticMarkup(h(RefusalCard, card)));
    assert.ok(markup.includes(`data-reason="reasons.${reason}"`) && markup.includes(lines[0].text), `${reason}: the React card says it`);
    assert.ok(decode(renderRefusalCard(card)).includes(lines[0].text), `${reason}: the string card says it`);
    const frenchMarkup = decode(renderToStaticMarkup(h(french.RefusalCard, { ...card, copy: FR.refusalCard })));
    assert.ok(frenchMarkup.includes(reasonView(card.code, payload, FR.refusalCard)[0].text), `${reason}: the French card says it`);
    for (const [language, copy] of [["en", EN.refusalCard], ["fr", FR.refusalCard]]) {
      assertTimeVocabulary(reasonView(card.code, payload, copy).map((line) => line.text).join(" "), `${language} ${reason}`);
    }
  }
  // A closed end is said as the reviewed history says an end: the day it gives is the first not covered, so the version is
  // applicable until the day before it, never "to" it (the review of #987).
  for (const [copy, said, never] of [
    [EN.refusalCard, "is applicable until the day before 2015-01-01 (publisher)", "applicable to 2015-01-01"],
    [FR.refusalCard, "est applicable jusqu’à la veille du 2015-01-01 (éditeur)", "jusqu’au 2015-01-01"],
  ]) {
    const [line] = reasonView("no_version_for_date", REASON_PAYLOADS.after_closed_end(), copy);
    assert.ok(line.text.includes(said), line.text);
    assert.ok(!line.text.includes(never), line.text);
  }
  // On the platform's nested fixture (the 2020 consolidation to 2023, the 2021 one inside it to 2022), the version named is
  // the one that ends last, not the last to begin.
  const nested = REASON_PAYLOADS.after_closed_end();
  Object.assign(nested, { closed_on: "2023-01-01", requested_date: "2024-01-01", history_begins: "2020-01-01", nearest_earlier: "2021-01-01" });
  assert.ok(reasonView("no_version_for_date", nested)[0].text.includes("the version that ends last is applicable until the day before 2023-01-01 (publisher), and no version begins on or after 2023-01-01"));
  assert.ok(reasonView("no_version_for_date", nested, FR.refusalCard)[0].text.includes("la version dont la fin est la plus tardive est applicable jusqu’à la veille du 2023-01-01 (éditeur)"));
  assert.match(EN.history.breakTo, /to the day before \{to\}/, "the same end convention as the history's");
  assert.match(FR.history.breakTo, /à la veille du \{to\}/);
  // After a closed end, the base act's end-of-force date the publisher's record gives, and nothing said of force otherwise.
  const closed = reasonView("no_version_for_date", REASON_PAYLOADS.after_closed_end());
  assert.equal(closed[1].text, fillText(EN.refusalCard.baseActEndOfForce, { dates: "2015-01-01" }));
  assert.equal(reasonView("no_version_for_date", { ...REASON_PAYLOADS.after_closed_end(), base_act_no_longer_in_force: [] }).length, 1);
  // A reason outside the platform's closed set is refused, so it is worded before it is shown; an older index names none.
  assert.throws(() => reasonView("no_version_for_date", noVersion("in_force_gap")), /names the reason "in_force_gap"/);
  const legacy = noVersion("before_history");
  delete legacy.reason;
  assert.equal(reasonView("no_version_for_date", legacy), null);
});

test("the refusal card names the textless version covering the date by its kind and role, and why its text is not held", async () => {
  const french = await frenchApp();
  for (const status of ["withheld_rights", "not_available_pdf", "not_available_derivation", "not_available_misfiled", "not_acquired"]) {
    const { code, payload } = textless(status);
    const card = { code, sentence: "This index holds this state but no text for it.", payload };
    validateRefusal(card);
    const [line] = reasonView(code, payload);
    assert.equal(line.text, fillText(EN.refusalCard.textless, {
      version: fillText(EN.refusalCard.versions.consolidation, { date: "2020-01-01" }), why: EN.refusalCard.textNotHeld[status],
    }));
    assert.ok(decode(renderToStaticMarkup(h(RefusalCard, card))).includes(line.text));
    const [frenchLine] = reasonView(code, payload, FR.refusalCard);
    assert.ok(decode(renderToStaticMarkup(h(french.RefusalCard, { ...card, copy: FR.refusalCard }))).includes(frenchLine.text));
  }
  // An act as published dated by its entry into force is named as that, never as applicable from it (R21).
  const { code, payload } = textless("not_acquired", { kind: "as_published", role: "entry_into_force", date: "2017-07-25" });
  const [act] = reasonView(code, payload);
  assert.ok(act.text.includes(fillText(EN.refusalCard.versions.as_published_entry_into_force, { date: "2017-07-25" })));
  assert.doesNotMatch(act.text, /applicable from 2017-07-25/);
  // A permalink's refusal names the version without a date asked.
  assert.ok(reasonView(code, { ...payload, requested_date: null })[0].text.startsWith("This version, "));
  assert.throws(() => reasonView(code, { ...payload, text_status: "lost" }), /names the text status "lost"/);
});

test("an ambiguity offers an act dated by its entry into force as that", () => {
  const envelope = census.envelopes.find((entry) => entry.envelope.refusal?.code === "ambiguous_version");
  const payload = envelope
    ? structuredClone(envelope.envelope.refusal.helpful_payload)
    : null;
  const candidates = payload?.candidates ?? [
    `/lu-legilux/loi-2021-05-05-n1/2021-05-05--${DIGEST_A}/fra`, `/lu-legilux/loi-2021-05-05-n1/2021-05-05--${DIGEST_B}/fra`,
  ];
  const digests = candidates.map((candidate) => candidate.split("--")[1].split("/")[0]);
  const card = {
    code: "ambiguous_version",
    sentence: "Several states of this work apply on that date, and none is chosen.",
    payload: {
      requested_date: "2021-06-01",
      candidates,
      candidate_state_sha256s: digests,
      candidate_state_kinds: ["as_published", "consolidation"],
      candidate_date_roles: ["entry_into_force", "applicability"],
      candidate_applicability_dates: candidates.map((candidate) => candidate.split("/")[3].slice(0, 10)),
      candidate_applicable_tos: [null, null],
      candidate_applicable_to_sources: [null, null],
      candidate_boundaries: ["derived", "derived"],
      candidate_text_statuses: ["held", "held"],
      candidate_states_rule: "the candidate_ lists",
    },
  };
  const markup = decode(renderToStaticMarkup(h(RefusalCard, card)));
  assert.ok(markup.includes("the act as published, dated by its entry into force on"), "the act as published is offered by its role");
  assert.equal((markup.match(/applicable from /g) ?? []).length, 1, "only the consolidation is offered as applicable");
  assert.ok(decode(renderRefusalCard(card)).includes("the act as published, dated by its entry into force on"));
  assert.throws(() => validateRefusal({ ...card, payload: { ...card.payload, candidate_date_roles: ["entry_into_force"] } }), /lists 1 roles for 2 candidates/);
});

/** The census dossier with index 9's chain set as the platform sets it: a withheld consolidation, a gap, an overlap and more. */
function dossierWithChain() {
  const envelope = sample("dossier", "the work, in the language it is held in");
  const value = envelope.result.value;
  const work = value.work_key;
  value.textless_states = [
    {
      state_sha256: DIGEST_A, language: "fra", applicability_date: "2030-01-01", stable_coordinate: `/lu-legilux/${work}/2030-01-01`,
      permalink: `/lu-legilux/${work}/2030-01-01--${DIGEST_A}/fra`, expression_iri: "http://example.invalid/fr",
      publisher_legal_resource_iri: "http://example.invalid", state_kind: "consolidation", date_role: "applicability",
      entry_into_force: null, applicable_to: "2031-01-01", applicable_to_source: "publisher", boundary: "half_open",
      publication_date: null, text_status: "withheld_rights", text_status_reason: "synthetic", in_file_identity: null,
      in_file_date: null, translation_of: null, provisional: false,
    },
    {
      state_sha256: DIGEST_B, language: null, applicability_date: "2032-01-01", stable_coordinate: `/lu-legilux/${work}/2032-01-01`,
      permalink: null, expression_iri: null, publisher_legal_resource_iri: "http://example.invalid/b", state_kind: "consolidation",
      date_role: "applicability", entry_into_force: null, applicable_to: null, applicable_to_source: null, boundary: "derived",
      publication_date: null, text_status: "not_acquired", text_status_reason: null, in_file_identity: null, in_file_date: null,
      translation_of: null, provisional: false,
    },
  ];
  value.transitions = [{ ordinal: 0, kind: "hole", from_state_sha256: DIGEST_A, from_applicability_date: "2030-01-01", to_state_sha256: DIGEST_B, to_applicability_date: "2032-01-01", span_from: "2031-01-01", span_to: "2032-01-01" }];
  value.gaps = [{ from: "2031-01-01", to: "2032-01-01", after_state_sha256: DIGEST_A, before_state_sha256: DIGEST_B }];
  value.overlaps = [{ from: "2028-06-01", to: "2028-12-31", state_sha256s: [DIGEST_A, DIGEST_B] }];
  value.same_date = [{ applicability_date: "2027-01-01", state_sha256s: [DIGEST_A, DIGEST_B] }];
  value.unclear_end_days = [{ date: "2026-12-31", after_state_sha256: DIGEST_A, before_state_sha256: DIGEST_B }];
  value.invalid_intervals = [{ state_sha256: DIGEST_A, applicability_date: "2025-07-17", applicable_to: "2025-07-01", text_status: "held" }];
  value.catalogue_chain_rule = "on lex-v3-luxembourg-index/9 the publisher catalogue's states of this work as this index read it";
  return envelope;
}

test("the dossier lists the catalogue's versions held without text and its gaps, overlaps and same-date groups", async () => {
  const outcome = dossierOutcome({ state: "success", envelope: dossierWithChain() });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = decode(renderToStaticMarkup(h(DossierAnswerView, { outcome })));
  for (const said of [
    EN.dossier.chainHeading,
    capitalised(fillText(EN.dossier.textlessRow, { version: fillText(EN.refusalCard.versions.consolidation, { date: "2030-01-01" }), why: EN.refusalCard.textNotHeld.withheld_rights })),
    fillText(EN.dossier.gap, { from: "2031-01-01", to: "2032-01-01" }),
    fillText(EN.dossier.overlap, { from: "2028-06-01", to: "2028-12-31" }),
    fillText(EN.dossier.sameDate, { date: "2027-01-01", count: 2 }),
    fillText(EN.dossier.unclearEndDay, { date: "2026-12-31" }),
    fillText(EN.dossier.invalidInterval, { date: "2025-07-17", to: "2025-07-01" }),
    EN.dossier.chainNote,
  ]) {
    assert.ok(markup.includes(said), said);
  }
  assert.ok(markup.includes(`<code>${outcome.view.chain.textless[1].stableCoordinate}</code>`), "a version naming no expression is given by its coordinate");
  // An index of schema 8 names no chain, and the page says what it said before (the schema-8 mount's recorded dossier).
  const older = legacy("dossier");
  assert.ok(!Object.hasOwn(older.result.value, "catalogue_chain_rule"), "schema 8 names no chain");
  const plain = dossierOutcome({ state: "success", envelope: older });
  assert.equal(plain.state, "success", plain.sentence);
  assert.equal(plain.view.chain, null);
  assert.ok(!decode(renderToStaticMarkup(h(DossierAnswerView, { outcome: plain }))).includes(EN.dossier.chainHeading));
  // A held state listed as without text is drift, and refused.
  const drift = dossierWithChain();
  drift.result.value.textless_states[0].state_sha256 = drift.result.value.states[0].state_sha256;
  drift.result.value.textless_states[0].permalink = drift.result.value.textless_states[0].permalink.replace(DIGEST_A, drift.result.value.states[0].state_sha256);
  assert.equal(dossierOutcome({ state: "success", envelope: drift }).state, "invalid_envelope");

  const french = await frenchApp();
  const frenchMarkup = decode(renderToStaticMarkup(h(french.DossierAnswerView, { outcome })));
  assert.ok(frenchMarkup.includes(fillText(FR.dossier.gap, { from: "2031-01-01", to: "2032-01-01" })));
  assert.ok(frenchMarkup.includes(FR.dossier.chainNote));
});

test("the dossier's span says a first state dated by its entry into force as that", () => {
  const envelope = sample("dossier", "the work, in the language it is held in");
  const value = envelope.result.value;
  value.states[0].date_role = "entry_into_force";
  value.states[0].entry_into_force = value.states[0].applicability_date;
  const markup = decode(renderToStaticMarkup(h(DossierAnswerView, { outcome: dossierOutcome({ state: "success", envelope }) })));
  const span = markup.slice(markup.indexOf('<p data-history="" id="dossier-states">'), markup.indexOf("</p>", markup.indexOf('id="dossier-states"')));
  assert.doesNotMatch(span, /from \d{4}-\d{2}-\d{2} to/, "never a span from an entry into force (R21)");
  assert.ok(span.includes(`dated by its entry into force on ${value.states[0].applicability_date} (publisher)`), span);
});

test("the history marks a row after a break, says where no version with held text covers the dates, and dates a row by its role", async () => {
  const envelope = sample("article_history", "one article of the work");
  const value = envelope.result.value;
  const row = value.states[0];
  row.date_role = "entry_into_force";
  row.entry_into_force = row.applicability_date;
  row.after_break = false;
  value.continuity_breaks = [{ language: row.language, from: "2030-01-01", to: null, reasons: ["withheld_rights", "catalogue_hole"], named_states: [`/lu-legilux/${value.work_key}/2030-01-01`], named_state_sha256s: [DIGEST_A] }];
  value.continuity_rule = "continuity_breaks names";
  value.anchor_rule = "the lineage follows the one publisher article id asked";
  const outcome = historyOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = decode(renderToStaticMarkup(h(HistoryAnswerView, { outcome })));
  const reasons = [EN.history.breakReasons.withheld_rights, EN.history.breakReasons.catalogue_hole].join(EN.common.listSeparator);
  assert.ok(markup.includes(fillText(EN.history.breakOpen, { language: row.language, from: "2030-01-01", reasons })), markup);
  assert.ok(markup.includes(fillText(EN.dossier.entryIntoForceDate, { date: row.applicability_date })), "the column answered as the dossier answers it");
  assert.ok(markup.includes(EN.history.breaksNote));
  // A row's mark is held to the breaks: a second row after the break must say so.
  const second = structuredClone(row);
  value.states.push(Object.assign(second, { applicability_date: "2035-01-01", state_sha256: DIGEST_B, permalink: `/lu-legilux/${value.work_key}/2035-01-01--${DIGEST_B}/${row.language}`, stable_coordinate: `/lu-legilux/${value.work_key}/2035-01-01`, wording_changed: false, after_break: false, date_role: "applicability", entry_into_force: null }));
  row.next_applicability_date = "2035-01-01";
  second.next_applicability_date = null;
  value.continuity_breaks[0].to = "2035-01-01";
  assert.equal(historyOutcome({ state: "success", envelope }).state, "invalid_envelope", "a row after a break that does not say so is refused");
  second.after_break = true;
  const marked = decode(renderToStaticMarkup(h(HistoryAnswerView, { outcome: historyOutcome({ state: "success", envelope }) })));
  assert.ok(marked.includes(`${EN.history.wording.unchanged}${EN.common.listSeparator}${EN.history.afterBreak}`), "the row after the break is marked");

  const french = await frenchApp();
  const frenchMarkup = decode(renderToStaticMarkup(h(french.HistoryAnswerView, { outcome: historyOutcome({ state: "success", envelope }) })));
  assert.ok(frenchMarkup.includes(FR.history.breaksHeading) && frenchMarkup.includes(FR.history.afterBreak));
});

/** The census bundle's state as index 9 serves a consolidation, a code's version or a translation. */
function bundleAs(facts) {
  const envelope = sample("evidence_bundle", "the work on its state's date");
  Object.assign(envelope.result.value.states[0], facts);
  return envelope;
}

test("the reading labels an expression in another language and says what a consolidated version or a code's version is not", async () => {
  const consolidation = bundleAs({ state_kind: "consolidation", date_role: "applicability", entry_into_force: null });
  const state = consolidation.result.value.states[0];
  const markup = decode(renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: consolidation }) })));
  assert.ok(markup.includes(fillText(EN.reading.consolidationCaveat, { act: `<code>${state.publisher_work_iri}/jo</code>` })), "the caveat names the act as published");
  const code = decode(renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: bundleAs({ state_kind: "code" }) }) })));
  assert.ok(code.includes(EN.reading.codeCaveat));
  assert.ok(!code.includes('data-caveat="consolidation"'));
  const french = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/07/20/a674/jo/fr";
  const translation = decode(renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: bundleAs({ state_kind: "as_published", translation_of: french }) }) })));
  assert.ok(translation.includes(fillText(EN.reading.translation, { language: state.language, french: `<code>${french}</code>` })));
  assert.ok(translation.includes("The publisher's record does not state which language version is authentic."), "R18's sentence");
  assert.doesNotMatch(translation, /only the French text is authentic/i);
  // An as-published act is neither: no caveat, no label.
  const plain = decode(renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: bundleAs({ state_kind: "as_published" }) }) })));
  assert.ok(!plain.includes("data-caveat") && !plain.includes("data-translation-of"));

  const frenchApp_ = await frenchApp();
  const frenchMarkup = decode(renderToStaticMarkup(h(frenchApp_.ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: consolidation }) })));
  assert.ok(frenchMarkup.includes(fillText(FR.reading.consolidationCaveat, { act: `<code>${state.publisher_work_iri}/jo</code>` })));
});

/** The census answer's states dated by their entry into force, as index 9 dates an act its file does not date. */
function byEntryIntoForce(state) {
  state.date_role = "entry_into_force";
  state.entry_into_force = state.applicability_date;
  state.state_kind = "as_published";
}

test("Compare and the export say a state dated by its entry into force as that", () => {
  const diff = sample("diff", "the state on one date against the state on the same date");
  const comparison = diff.result.value.comparisons[0];
  byEntryIntoForce(comparison.from);
  byEntryIntoForce(comparison.to);
  comparison.note = comparison.note.replace(`applicable from ${comparison.from.applicability_date} (publisher)`, `dated by its entry into force on ${comparison.from.applicability_date} (publisher)`);
  const compared = compareOutcome({ state: "success", envelope: diff });
  assert.equal(compared.state, "success", compared.sentence);
  const markup = decode(renderToStaticMarkup(h(CompareAnswerView, { outcome: compared })));
  assert.doesNotMatch(markup, new RegExp(`(?:applying|applicable) from ${comparison.from.applicability_date}`), "neither side nor the note says it applicable (R21)");
  assert.ok(markup.includes(`the act as published, dated by its entry into force on ${comparison.from.applicability_date} (publisher)`));
  // The note is the platform's, held: an EIF state with the applicability note is drift.
  const drift = sample("diff", "the state on one date against the state on the same date");
  byEntryIntoForce(drift.result.value.comparisons[0].from);
  byEntryIntoForce(drift.result.value.comparisons[0].to);
  assert.equal(compareOutcome({ state: "success", envelope: drift }).state, "invalid_envelope");

  const bundle = sample("evidence_bundle", "the work on its state's date");
  byEntryIntoForce(bundle.result.value.states[0]);
  const reading = readingOutcome({ state: "success", envelope: bundle });
  const state = reading.view.states[0];
  const pins = new Set([...state.articles, ...state.articlesWithoutText].map((article) => pinKey(state.stateSha256, article.publisherId)));
  const panel = exportState(reading, pins);
  assert.equal(panel.state, "composed", panel.sentence);
  const exported = decode(renderToStaticMarkup(h(ExportAnswerView, { outcome: reading, pins, onPin: () => {} })) + renderToStaticMarkup(h(ExportPanel, { outcome: reading, pins, onSave: () => {} })));
  assert.doesNotMatch(exported, /applying from/, "an exported item of a state dated by its entry into force (R21)");
  assert.ok(exported.includes(`the act as published, entry into force on ${state.applicabilityDate}`));
  assert.ok(JSON.parse(exportJson(panel.model)).items.every((item) => item.date_role === "entry_into_force"), "the JSON names the date's role");
});

test("every form these slices add says no application word of an entry into force, and each French form is the English's in shape", () => {
  const added = [
    "reading.translation", "reading.consolidationCaveat", "reading.codeCaveat",
    "compare.sideEntryIntoForce", "compare.sideEntryIntoForceNext", "compare.sameVersionEntryIntoForce",
    "compare.sameTextHeldEntryIntoForce", "compare.sameTextHeldOfSeveralEntryIntoForce",
    "export.itemEntryIntoForce", "export.excludedEntryIntoForce",
    "dossier.statesEntryIntoForce.one", "dossier.statesEntryIntoForce.other", "dossier.statesFromEntryIntoForce",
    "dossier.statesInEntryIntoForce", "dossier.statesInFromEntryIntoForce",
    "refusalCard.candidateEntryIntoForce", "refusalCard.candidateEntryIntoForceWithdrawalNotStated",
    "refusalCard.versions.as_published_entry_into_force",
  ];
  for (const [language, table] of [["en", EN], ["fr", FR]]) {
    const entries = new Map(entriesOf(table));
    for (const path of added) {
      const form = entries.get(path);
      assert.equal(typeof form, "string", `${language} ${path}`);
      assertTimeVocabulary(form, `${language} ${path}`);
      // The entry into force is never said as an application; another state's start may be (the next state held, the latest).
      assert.doesNotMatch(form, /(?:applicable from|applying from|applies from|applicable à partir du|s’applique à partir du) \{(?:from|state|validFrom|date)\}/i, `${language} ${path}`);
    }
    for (const [path, form] of entries) {
      if (/^(refusalCard\.(reasons|versions|textNotHeld|textless)|dossier\.(chain|textless|gap|overlap|sameDate|unclearEndDay|invalidInterval)|history\.break)/.test(path)) {
        assertTimeVocabulary(form, `${language} ${path}`);
      }
    }
  }
  assert.deepEqual(Object.keys(REFUSAL_CARD_COPY.reasons).sort(), Object.keys(FR.refusalCard.reasons).sort());
});

test("the search page says the platform's outside_named_version refusal on its card, in English and in French (the review of #987)", async () => {
  // The search refusal the census holds, carrying the platform's own outside_named_version payload: the card the page shows.
  const template = census.envelopes.find((entry) => entry.operation === "search" && entry.envelope.refusal);
  assert.ok(template, "the census holds a search refusal");
  const payload = outsideNamedVersion();
  const envelope = structuredClone(template.envelope);
  envelope.refusal = { ...envelope.refusal, code: "no_version_for_date", helpful_payload: payload };
  const outcome = searchOutcome({ state: "refusal", envelope });
  assert.equal(outcome.state, "refusal", outcome.sentence);
  assert.ok(outcome.card, "the refusal is a card, not an unreadable answer");
  // Its head says what is true here: the version named is held and does not cover the date; never "no state applies"
  // (the French review of #987's repair round, its point 1).
  assert.equal(outcome.sentence, "This site holds the act the reference names and the version it names, but that version does not cover that date, so nothing was opened and no word was searched.");
  // The census payload's nearest dates are the work's: the covering consolidation is the nearest earlier state.
  assert.ok(payload.covering_states[0].includes(`/${payload.nearest_earlier}--`), "the nearest earlier date is the covering state's");
  const [line] = reasonView("no_version_for_date", payload);
  assert.equal(line.key, "reasons.outside_named_version");
  assert.ok(line.text.includes(payload.requested_date) && line.text.includes(payload.catalogue_read_no_later_than));
  const english = decode(renderToStaticMarkup(h(SearchAnswerView, { outcome, onNextPage: () => {} })));
  assert.ok(english.includes(line.text), "the English page says why");
  for (const state of [...payload.named_states, ...payload.covering_states]) assert.ok(english.includes(state), `the card names ${state}`);
  const french = await frenchApp();
  const frenchOutcome = french.liveSearchOutcome({ state: "refusal", envelope });
  const frenchPage = decode(renderToStaticMarkup(h(french.SearchAnswerView, { outcome: frenchOutcome, onNextPage: () => {} })));
  assert.ok(frenchPage.includes(reasonView("no_version_for_date", payload, FR.refusalCard)[0].text), "the French page says why");
  assert.ok(frenchPage.includes("mais cette version ne couvre pas cette date"), "and its head, in French");
  assert.ok(!frenchPage.includes("ne s’applique à cette date"), "never that no version applies");
  assert.ok(decode(renderRefusalCard({ code: "no_version_for_date", sentence: "x.", payload })).includes(line.text), "the string card says it");
});

/** An ambiguity on index 9 whose candidates include a state the catalogue names no expression of, as the platform sends it. */
function ambiguityWithAStateNamingNoExpression() {
  const coordinate = "/lu-legilux/loi-2003-03-03-n1/2003-03-03";
  return {
    code: "ambiguous_version",
    sentence: "Several states of this work apply on that date, and none is chosen.",
    payload: {
      requested_date: "2004-01-01",
      candidates: [coordinate, `${coordinate}--${DIGEST_B}/fra`],
      candidate_state_sha256s: [DIGEST_A, DIGEST_B],
      candidate_state_kinds: ["consolidation", "as_published"],
      candidate_date_roles: ["applicability", "applicability_in_file"],
      candidate_applicability_dates: ["2003-03-03", "2003-03-03"],
      candidate_applicable_tos: [null, null],
      candidate_applicable_to_sources: [null, null],
      candidate_boundaries: ["derived", "derived"],
      candidate_text_statuses: ["not_acquired", "held"],
      candidate_states_rule: "the candidate_ lists, in the order of the candidates",
    },
  };
}

test("an ambiguity names a state its catalogue names no expression of by its coordinate, without a link, in both renderers (the review of #987)", async () => {
  const card = ambiguityWithAStateNamingNoExpression();
  validateRefusal(card);
  const version = fillText(EN.refusalCard.versions.consolidation, { date: "2003-03-03" });
  const said = fillText(EN.refusalCard.candidateUnexpressed, { version, hash: DIGEST_A.slice(0, 8) });
  const markup = decode(renderToStaticMarkup(h(RefusalCard, card)));
  assert.ok(markup.replace(/<[^>]+>/g, "").includes(said), "the React card names it by its kind and role");
  assert.ok(markup.includes("<code>/lu-legilux/loi-2003-03-03-n1/2003-03-03</code>"), "and by its coordinate");
  assert.ok(!markup.includes('href="/lu-legilux/loi-2003-03-03-n1/2003-03-03"') && !markup.includes("2003-03-03--" + DIGEST_A), "with no link");
  assert.equal((markup.match(/<a href=/g) ?? []).length, 1, "the held state keeps its link");
  const string = decode(renderRefusalCard(card));
  assert.ok(string.replace(/<[^>]+>/g, "").includes(said), "the string card says the same");
  assert.ok(string.includes("data-candidate-unexpressed"));
  const french = await frenchApp();
  const frenchSaid = fillText(FR.refusalCard.candidateUnexpressed, {
    version: fillText(FR.refusalCard.versions.consolidation, { date: "2003-03-03" }), hash: DIGEST_A.slice(0, 8),
  });
  assert.ok(decode(renderToStaticMarkup(h(french.RefusalCard, { ...card, copy: FR.refusalCard }))).replace(/<[^>]+>/g, "").includes(frenchSaid));
  // After a version named as the act as published, the French names "cette version", never "elle" (the French review).
  const act = ambiguityWithAStateNamingNoExpression();
  act.payload.candidate_state_kinds = ["as_published", "as_published"];
  act.payload.candidate_date_roles = ["entry_into_force", "applicability_in_file"];
  const frenchAct = decode(renderToStaticMarkup(h(french.RefusalCard, { ...act, copy: FR.refusalCard }))).replace(/<[^>]+>/g, "");
  assert.ok(frenchAct.includes("l’acte tel que publié, daté de son entrée en vigueur le 2003-03-03 (éditeur), empreinte aaaaaaaa"), frenchAct);
  assert.ok(frenchAct.includes("ne propose aucun lien vers cette version") && !frenchAct.includes("vers elle"));
  assert.ok(frenchAct.includes("(/lu-legilux/loi-2003-03-03-n1/2003-03-03)"), "the coordinate set apart");

  // A coordinate names no one state: without index 9's digest at its place, or with lists not aligned on the candidates, the
  // card refuses the payload rather than guess.
  const bare = ambiguityWithAStateNamingNoExpression();
  for (const key of Object.keys(bare.payload).filter((key) => key.startsWith("candidate_"))) delete bare.payload[key];
  assert.throws(() => validateRefusal(bare), /names no one state/);
  const shifted = ambiguityWithAStateNamingNoExpression();
  shifted.payload.candidate_state_sha256s[1] = "c".repeat(64);
  assert.throws(() => validateRefusal(shifted), /aligned on the candidates/);
  const short = ambiguityWithAStateNamingNoExpression();
  short.payload.candidate_state_kinds = ["consolidation"];
  assert.throws(() => validateRefusal(short), /candidate_state_kinds lists 1 kinds for 2 candidates/);
  // A coordinate of another work is not a state of the work the choice is about.
  const stranger = ambiguityWithAStateNamingNoExpression();
  stranger.payload.candidates = [stranger.payload.candidates[1], "/lu-legilux/loi-1999-01-01-n1/2003-03-03"];
  stranger.payload.candidate_state_sha256s = [DIGEST_B, DIGEST_A];
  assert.throws(() => validateRefusal(stranger), /is not a state of lu-legilux\/loi-2003-03-03-n1/);
});
