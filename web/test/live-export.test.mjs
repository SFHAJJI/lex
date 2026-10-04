// The live export composer, measured against what the platform really sent.
//
// The screen asks `evidence_bundle` through the reading's client module when the reader submits, lists
// the articles to pin, and composes the export of what is pinned in the page. These tests drive it
// with an injected fetch that answers the census envelopes (`schemas/v3-platform/envelope-samples.json`:
// the fixture state's bundle and no mount), and render the panel for the pins a reader would make. The
// census holds no EU bundle yet, so an EU reading is the census envelope carrying the hand-built EU
// bundle the web tests share (`scripts/europe-bundle-sample.mjs`), with the EU context.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { ExportAnswerView, ExportPanel, LiveExport, RefusalCard, renderLiveExportPage } from "../.react-build/app.mjs";
import { loadLiveReading, readingOutcome } from "../scripts/live-reading.mjs";
import { EUROPE_EXPORT_SCHEMA, EXPORT_WATERMARK, exportCsv, exportJson } from "../scripts/export-build.mjs";
import { exportPdf } from "../scripts/export-pdf.mjs";
import { EUROPE_SAMPLE, europeBundle, sha256 } from "../scripts/europe-bundle-sample.mjs";
import { EUROPE_TEXT_ACKNOWLEDGEMENT } from "../scripts/reading-answer.mjs";
import {
  EXPORT_FORMATS,
  LIVE_EXPORT_IDLE,
  NOTHING_PINNED,
  exportFileName,
  exportState,
  pinKey,
  saveExport,
} from "../scripts/live-export.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "evidence_bundle" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the evidence_bundle envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const ANSWER = "the work on its state's date";
const REQUEST = { identifier: "/lu-legilux/loi-1991-08-10-n3", date: "2024-02-01", language: "" };

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

const unescape = (markup) => markup.replaceAll("&#x27;", "'").replaceAll("&quot;", '"').replaceAll("&lt;", "<").replaceAll("&gt;", ">").replaceAll("&amp;", "&");

async function answered() {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const outcome = await loadLiveReading({ contract, fetchImpl, request: REQUEST });
  assert.equal(outcome.state, "success", outcome.sentence);
  return { calls, outcome };
}

test("the server renders the form in its idle state with no named control and no export, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const markup = renderToStaticMarkup(h(LiveExport, { contract, fetchImpl }));
  assert.match(markup, /data-answer-state="idle"/);
  assert.ok(markup.includes(LIVE_EXPORT_IDLE));
  assert.ok(!markup.includes("data-export-state"), "no export before a reading");
  assert.equal(calls.length, 0);
  assert.doesNotMatch(markup.slice(markup.indexOf("<form"), markup.indexOf("</form>")), /\sname=/);
  assert.ok(markup.includes("Read for export"));
  assert.ok(renderLiveExportPage().includes('<script src="/client-live-export.js" defer=""></script>'));
});

test("a reading lists every article with a pin and no name, and nothing pinned says so", async () => {
  const { calls, outcome } = await answered();
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "/api/v3/evidence_bundle");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "evidence_bundle", parameters: { identifier: REQUEST.identifier, date: REQUEST.date } });
  const [state] = outcome.view.states;
  const markup = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins: new Set(), onPin: () => {} }));
  assert.equal([...markup.matchAll(/data-pin=""/g)].length, state.articles.length + state.articlesWithoutText.length);
  assert.doesNotMatch(markup, /\sname=/, "a pin carries no name");
  assert.ok(markup.includes(`<blockquote lang="fr">`), "the text is quoted in its state's language");
  assert.ok(!markup.includes(" until "), "the next state's date is its start, never this state's end");
  const panel = renderToStaticMarkup(h(ExportPanel, { outcome, pins: new Set(), onSave: () => {} }));
  assert.equal(panel, `<section data-export-state="empty"><h2>Export</h2><p role="status">${NOTHING_PINNED}</p></section>`);
});

test("pinned articles compose the export the file will carry: counts, watermark, rights, citations, digests, and the JSON itself", async () => {
  const { outcome } = await answered();
  const [state] = outcome.view.states;
  const chosen = [state.articles[2], state.articles[0]];
  const pins = new Set(chosen.map((article) => pinKey(state.stateSha256, article.publisherId)));
  const panel = exportState(outcome, pins);
  assert.equal(panel.state, "composed");
  assert.deepEqual(panel.model.items.map((item) => item.publisherId), [state.articles[0].publisherId, state.articles[2].publisherId], "in the bundle's order");
  assert.equal(panel.model.observedAt, envelopeOf(ANSWER).context.freshness.observed_at, "the time is the snapshot's observation, as the envelope says");

  const markup = renderToStaticMarkup(h(ExportPanel, { outcome, pins, onSave: () => {} }));
  const text = unescape(markup);
  assert.match(markup, /data-export-state="composed"/);
  assert.ok(text.includes("2 articles pinned: 2 exported with text, 0 excluded."));
  assert.ok(text.includes(EXPORT_WATERMARK));
  assert.ok(text.includes(`Text served under ${outcome.view.rightsDisposition}.`));
  for (const article of chosen) {
    assert.ok(text.includes(article.permalink), `the citation of ${article.publisherId}`);
    assert.ok(text.includes(article.textSha256), `the whole text digest of ${article.publisherId}`);
  }
  assert.ok(text.includes(`<pre>${exportJson(panel.model)}</pre>`), "the JSON shown is the JSON saved");
  assert.deepEqual([...markup.matchAll(/data-save="([a-z]+)"/g)].map((match) => match[1]), ["json", "csv", "pdf"]);
  assert.ok(!markup.includes("data-format-refused"));
});

test("a PDF the standard fonts cannot set is not offered, and the panel says why", async () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  const article = envelope.result.value.states[0].articles[0];
  const { createHash } = await import("node:crypto");
  article.text = "Art. 1 漢";
  article.text_byte_length = Buffer.byteLength(article.text, "utf8");
  article.text_sha256 = createHash("sha256").update(article.text, "utf8").digest("hex");
  const outcome = readingOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const pins = new Set([pinKey(outcome.view.states[0].stateSha256, article.publisher_id)]);
  const markup = renderToStaticMarkup(h(ExportPanel, { outcome, pins, onSave: () => {} }));
  assert.deepEqual([...markup.matchAll(/data-save="([a-z]+)"/g)].map((match) => match[1]), ["json", "csv"], "JSON and CSV carry any text");
  assert.ok(markup.includes('<p data-format-refused="pdf">PDF is not offered for this export: its text holds characters the standard PDF fonts cannot set (U+6F22).</p>'));
});

test("a pinned article held without text is shown as excluded with its reason", async () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  const state = envelope.result.value.states[0];
  const [moved] = state.articles.splice(0, 1);
  if (moved.validity_conflict) state.validity_conflict_count -= 1;
  state.articles_without_text = [{ article_identity_sha256: moved.article_identity_sha256, publisher_id: moved.publisher_id, reason: "no_text_tokens" }];
  const outcome = readingOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const pins = new Set([pinKey(state.state_sha256, moved.publisher_id)]);
  const list = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins, onPin: () => {} }));
  assert.ok(list.includes("held without text; an export records it as excluded, with its reason."));
  const text = unescape(renderToStaticMarkup(h(ExportPanel, { outcome, pins, onSave: () => {} })));
  assert.ok(text.includes("1 article pinned: 0 exported with text, 1 excluded."));
  assert.ok(text.includes(`${moved.publisher_id}</strong> (`) && text.includes("excluded, no_text_tokens"));
});

test("an export that cannot be composed says why instead of failing the page", async () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  delete envelope.context.freshness;
  const outcome = { ...readingOutcome({ state: "success", envelope }), context: {} };
  const [state] = outcome.view.states;
  const panel = exportState(outcome, new Set([pinKey(state.stateSha256, state.articles[0].publisherId)]));
  assert.equal(panel.state, "failed");
  assert.equal(panel.sentence, "This export cannot be composed: an export says when the answering snapshot was observed.");
});

test("no mount is the reading's card, with no export panel", async () => {
  const outcome = await loadLiveReading({ contract, fetchImpl: answering(200, "application/json", envelopeOf("no corpus mounted")).fetchImpl, request: REQUEST });
  assert.equal(outcome.card, true);
  const markup = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins: new Set(), onPin: () => {} }));
  const card = renderToStaticMarkup(h(RefusalCard, { code: outcome.code, sentence: outcome.sentence, payload: outcome.payload }));
  assert.equal(markup, `<section data-answer-state="refusal">${card}</section>`);
  assert.equal(renderToStaticMarkup(h(ExportPanel, { outcome, pins: new Set(["x#y"]), onSave: () => {} })), "");
});

test("saving hands the page's own file over, named for the work and date, and sends nothing", async () => {
  const { outcome } = await answered();
  const [state] = outcome.view.states;
  const { model } = exportState(outcome, new Set([pinKey(state.stateSha256, state.articles[0].publisherId)]));
  assert.equal(exportFileName(model, "json"), "lex-v3-export-lu-legilux-loi-1991-08-10-n3-2024-02-01.json");

  for (const format of EXPORT_FORMATS) {
    const events = [];
    let blob = null;
    const anchor = { click: () => events.push("click"), remove: () => events.push("remove") };
    const doc = { createElement: (tag) => { events.push(`create ${tag}`); return anchor; }, body: { append: (node) => events.push(node === anchor ? "append" : "append other") } };
    const urls = { createObjectURL: (value) => { blob = value; return "blob:x"; }, revokeObjectURL: (url) => events.push(`revoke ${url}`) };
    const later = [];
    const name = saveExport(model, format, { doc, urls, later: (callback, ms) => later.push({ callback, ms }) });
    assert.equal(name, `lex-v3-export-lu-legilux-loi-1991-08-10-n3-2024-02-01.${format.extension}`);
    assert.equal(anchor.href, "blob:x");
    assert.equal(anchor.download, name);
    assert.equal(blob.type, format.mediaType);
    const expected = { json: exportJson(model), csv: exportCsv(model), pdf: exportPdf(model) }[format.id];
    const saved = new Uint8Array(await blob.arrayBuffer());
    assert.deepEqual(saved, typeof expected === "string" ? new TextEncoder().encode(expected) : expected, "the file is the export, byte for byte");
    assert.deepEqual(events, ["create a", "append", "click", "remove"]);
    assert.equal(later.length, 1);
    assert.ok(later[0].ms > 0, "the URL is revoked after the browser has taken the file");
    later[0].callback();
    assert.deepEqual(events.slice(-1), ["revoke blob:x"]);
  }
});

/** The census envelope carrying the EU sample bundle, with `change` applied to it, under the EU context. */
function europeEnvelope(change) {
  const envelope = structuredClone(envelopeOf(ANSWER));
  envelope.context = { ...envelope.context, publisher: "eu-eurlex", jurisdiction: "eu", timeline_semantics: "official_consolidation_state" };
  envelope.result.value = europeBundle(change);
  return envelope;
}
const EU_REQUEST = { identifier: EUROPE_SAMPLE.celex, date: EUROPE_SAMPLE.wordingDate, language: "" };

/** An EU reading asked through the client module, as the page asks it. */
async function europeAnswered(change) {
  const { calls, fetchImpl } = answering(200, "application/json", europeEnvelope(change));
  const outcome = await loadLiveReading({ contract, fetchImpl, request: EU_REQUEST });
  assert.equal(outcome.state, "success", outcome.sentence);
  return { calls, outcome };
}

/** Every pin of an EU reading: each article of each wording, quoted or held without text. */
const everyEuropePin = (view) => new Set(view.wordings.flatMap((wording) => [...wording.articles, ...wording.articlesWithoutText]
  .map((article) => pinKey(wording.wordingSha256, article.publisherId))));

test("an EU reading lists a pin for every article, quoted or held without text, and nothing pinned says so", async () => {
  const { calls, outcome } = await europeAnswered();
  assert.equal(calls.length, 1, "the reading is the one request");
  assert.equal(outcome.registrySha256, contract.registry_sha256, "the reading carries the registry its envelope is bound to");
  const [wording] = outcome.view.wordings;
  const markup = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins: new Set(), onPin: () => {} }));
  assert.equal([...markup.matchAll(/data-pin=""/g)].length, wording.articles.length + wording.articlesWithoutText.length);
  assert.ok(markup.includes(`<p data-acknowledgement="">${EUROPE_TEXT_ACKNOWLEDGEMENT}</p>`));
  const panel = renderToStaticMarkup(h(ExportPanel, { outcome, pins: new Set(), onSave: () => {} }));
  assert.equal(panel, `<section data-export-state="empty"><h2>Export</h2><p role="status">${NOTHING_PINNED}</p></section>`);
});

test("pinned EU articles compose the EU export: the acknowledgement, the authenticity statement, wording dates, the exclusions with the annexes, and the JSON itself", async () => {
  const { outcome } = await europeAnswered();
  const pins = everyEuropePin(outcome.view);
  const panel = exportState(outcome, pins);
  assert.equal(panel.state, "composed", panel.sentence);
  const { model } = panel;
  assert.equal(model.schema, EUROPE_EXPORT_SCHEMA);
  assert.equal(model.observedAt, envelopeOf(ANSWER).context.freshness.observed_at, "the time is the snapshot's observation, as the envelope says");
  assert.equal(model.verifiedBy.registrySha256, contract.registry_sha256, "the registry is the one the envelope is bound to");
  assert.deepEqual(model.items.map((item) => item.publisherId), ["001", "002"], "in the bundle's order");

  const markup = renderToStaticMarkup(h(ExportPanel, { outcome, pins, onSave: () => {} }));
  const text = unescape(markup);
  assert.match(markup, /data-export-state="composed"/);
  assert.ok(text.includes("3 articles pinned: 2 exported with text, 1 excluded."));
  assert.ok(text.includes(EXPORT_WATERMARK));
  assert.ok(text.includes(`Text served with the acknowledgement ${EUROPE_TEXT_ACKNOWLEDGEMENT}, which every exported article carries, and the authenticity statement below. ${outcome.view.rightsRule}`));
  assert.ok(text.includes(`<p data-authenticity="">${outcome.view.authenticity}</p>`));
  for (const item of model.items) {
    assert.ok(text.includes(`<strong>${item.publisherId}</strong> (eng, wording of 2016-04-27): <code>${item.citation}</code>, text digest <code>${item.textSha256}</code>`), `${item.publisherId}, dated by its wording`);
  }
  assert.ok(text.includes(`<strong>099</strong> (eng, wording of 2016-04-27): excluded, articles_without_text, <code>${EUROPE_SAMPLE.permalink}#099</code>`));
  const [annex] = outcome.view.wordings[0].annexesNotServed;
  assert.ok(text.includes(`<div data-export-annexes="1"><p data-annexes-not-served="2" data-annex-disposition="annex_text_not_available">2 annexes of the English wording are not served as text, and are never searched, quoted or exported: ${annex.reason}. Official source <code>${annex.officialSource}</code>.</p></div>`));
  assert.ok(text.includes(`<pre>${exportJson(model)}</pre>`), "the JSON shown is the JSON saved");
  assert.deepEqual([...markup.matchAll(/data-save="([a-z]+)"/g)].map((match) => match[1]), ["json", "csv", "pdf"]);
  assert.doesNotMatch(text.replace(/<pre>[\s\S]*?<\/pre>/, ""), /applying from|agreed_same_run_cc_by/, "no Luxembourg date or rights disposition");

  // A PDF the standard fonts cannot set is not offered, as for Luxembourg text.
  const { outcome: foreign } = await europeAnswered((bundle) => {
    const article = bundle.wordings[0].articles[0];
    article.text = "Article 1 − 漢";
    article.text_byte_length = Buffer.byteLength(article.text, "utf8");
    article.text_sha256 = sha256(article.text);
  });
  const refused = renderToStaticMarkup(h(ExportPanel, { outcome: foreign, pins: everyEuropePin(foreign.view), onSave: () => {} }));
  assert.deepEqual([...refused.matchAll(/data-save="([a-z]+)"/g)].map((match) => match[1]), ["json", "csv"]);
  assert.ok(refused.includes('<p data-format-refused="pdf">PDF is not offered for this export: its text holds characters the standard PDF fonts cannot set (U+2212, U+6F22).</p>'));
});

test("an EU export that cannot be composed says why: no observation time, or no registry", async () => {
  const { outcome } = await europeAnswered();
  const pins = new Set([pinKey(outcome.view.wordings[0].wordingSha256, "001")]);
  assert.equal(exportState({ ...outcome, context: {} }, pins).sentence, "This export cannot be composed: an export says when the answering snapshot was observed.");
  assert.equal(exportState({ ...outcome, registrySha256: undefined }, pins).sentence, "This export cannot be composed: an export names the registry that answered.");
  assert.equal(exportState(outcome, new Set([pinKey("0".repeat(64), "001")])).state, "failed", "a pin of another wording is never composed");
});

test("saving an EU export hands over its own files, named for the act and the date", async () => {
  const { outcome } = await europeAnswered();
  const { model } = exportState(outcome, everyEuropePin(outcome.view));
  for (const format of EXPORT_FORMATS) {
    let blob = null;
    const anchor = { click: () => {}, remove: () => {} };
    const doc = { createElement: () => anchor, body: { append: () => {} } };
    const urls = { createObjectURL: (value) => { blob = value; return "blob:x"; }, revokeObjectURL: () => {} };
    const name = saveExport(model, format, { doc, urls, later: () => {} });
    assert.equal(name, `lex-v3-export-32016r0679-2016-04-27.${format.extension}`);
    const expected = { json: exportJson(model), csv: exportCsv(model), pdf: exportPdf(model) }[format.id];
    const saved = new Uint8Array(await blob.arrayBuffer());
    assert.deepEqual(saved, typeof expected === "string" ? new TextEncoder().encode(expected) : expected, `the ${format.id} file is the EU export, byte for byte`);
  }
  assert.ok(exportCsv(model).startsWith("celex,language,wording_kind,wording_date,"), "the CSV saved is the EU CSV");
});
