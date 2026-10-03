// The live composer's exports, composed from the evidence bundle the real handler sent.
//
// The bundle comes from `schemas/v3-platform/answer-samples.json` (operation `evidence_bundle`),
// read by `readEvidenceBundle`. The launch contract asks that exports "preserve citations, rights,
// watermarks and exclusions"; each of those is checked in both formats. An EU export is composed from
// the hand-built EU bundle the web tests share (`scripts/europe-bundle-sample.mjs`), until the census
// captures one, and is held to the same line with Decision 95's acknowledgement in place of a rights
// disposition, its annexes excluded, and its wording dates never written as Luxembourg dates.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { EUROPE_TEXT_ACKNOWLEDGEMENT, readEuropeEvidenceBundle, readEvidenceBundle } from "../scripts/reading-answer.mjs";
import {
  CSV_COLUMNS,
  EUROPE_CSV_COLUMNS,
  EUROPE_EXPORT_SCHEMA,
  EUROPE_WITHOUT_TEXT,
  EXPORT_SCHEMA,
  EXPORT_WATERMARK,
  composeEuropeExport,
  composeExport,
  exportCsv,
  exportJson,
} from "../scripts/export-build.mjs";
import { WATERMARK_PREVIEW } from "../scripts/export-composer.mjs";
import { EUROPE_SAMPLE, europeBundle } from "../scripts/europe-bundle-sample.mjs";
import { escapeProvision } from "../scripts/search-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === "<varies-per-run>") { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

async function bundle() {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  return withDigests(parsed.sampled.find((sample) => sample.operation === "evidence_bundle").answer);
}

/** A CSV reader good enough to check what the writer wrote: RFC 4180 quoting, CRLF rows. */
function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = "";
  let quoted = false;
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (quoted) {
      if (char === '"' && text[index + 1] === '"') { field += '"'; index += 1; } else if (char === '"') quoted = false; else field += char;
    } else if (char === '"') quoted = true;
    else if (char === ",") { row.push(field); field = ""; } else if (char === "\r" && text[index + 1] === "\n") { row.push(field); rows.push(row); row = []; field = ""; index += 1; } else field += char;
  }
  return rows;
}

const OBSERVED = "2026-09-30T08:00:00.0000000Z";

test("an export carries every pinned article's citation, text, digests, official source and rights, and the watermark", async () => {
  const view = readEvidenceBundle(await bundle());
  const [state] = view.states;
  const pins = state.articles.slice(0, 3).map((article) => ({ stateSha256: state.stateSha256, publisherId: article.publisherId }));
  const model = composeExport({ view, pinned: pins, observedAt: OBSERVED });
  assert.equal(model.items.length, 3);
  assert.equal(EXPORT_WATERMARK, WATERMARK_PREVIEW, "the composer's preview and the export say one sentence");

  const json = JSON.parse(exportJson(model));
  assert.equal(json.schema, EXPORT_SCHEMA);
  assert.equal(json.watermark, EXPORT_WATERMARK);
  assert.equal(json.observed_at, OBSERVED);
  assert.equal(json.rights_disposition, "agreed_same_run_cc_by");
  assert.ok(json.rights_rule.length > 0);
  assert.deepEqual(Object.keys(json.verified_by), ["corpus_sha256", "index_sha256", "registry_sha256"]);
  for (const [index, item] of json.items.entries()) {
    const source = state.articles[index];
    assert.equal(item.citation, `${state.permalink}#${source.publisherId}`, "the citation is the hash-pinned article permalink");
    assert.equal(item.text, source.text);
    assert.equal(createHash("sha256").update(item.text, "utf8").digest("hex"), item.text_sha256, "the text still hashes to its digest");
    assert.equal(item.official_source, state.publisherLegalResourceIri);
    assert.equal(item.rights_disposition, "agreed_same_run_cc_by");
  }

  const csv = parseCsv(exportCsv(model));
  assert.deepEqual(csv[0], [...CSV_COLUMNS]);
  assert.equal(csv.length, 4);
  for (const [index, row] of csv.slice(1).entries()) {
    const record = Object.fromEntries(CSV_COLUMNS.map((column, position) => [column, row[position]]));
    assert.equal(record.citation, json.items[index].citation);
    assert.equal(record.text, json.items[index].text, "the text survives quoting whole");
    assert.equal(record.status, "exported");
    assert.equal(record.watermark, EXPORT_WATERMARK, "each row carries the watermark");
    assert.equal(record.rights_disposition, "agreed_same_run_cc_by");
    // Review of #789: a row copied out alone still says when the snapshot was observed, under what
    // rule the text was served, and which corpus, index and registry answered.
    assert.equal(record.observed_at, OBSERVED);
    assert.equal(record.rights_rule, view.rightsRule);
    assert.deepEqual([record.corpus_sha256, record.index_sha256, record.registry_sha256], [view.corpusSha256, view.indexSha256, view.registrySha256]);
  }
});

test("a pinned article held without text is excluded with its reason in both formats, never quoted empty", async () => {
  const answer = await bundle();
  const state = answer.states[0];
  const [moved] = state.articles.splice(0, 1);
  if (moved.validity_conflict) state.validity_conflict_count -= 1;
  state.articles_without_text = [{ article_identity_sha256: moved.article_identity_sha256, publisher_id: moved.publisher_id, reason: "no_text_tokens" }];
  const view = readEvidenceBundle(answer);
  const model = composeExport({ view, pinned: [{ stateSha256: state.state_sha256, publisherId: moved.publisher_id }, { stateSha256: state.state_sha256, publisherId: state.articles[0].publisher_id }], observedAt: OBSERVED });
  assert.equal(model.items.length, 1);
  assert.deepEqual(model.excluded.map((entry) => [entry.publisherId, entry.reason]), [[moved.publisher_id, "no_text_tokens"]]);
  const json = JSON.parse(exportJson(model));
  assert.deepEqual(json.excluded.map((entry) => entry.article), [moved.publisher_id]);
  assert.ok(!json.items.some((item) => item.article === moved.publisher_id));
  const citation = `${view.states[0].permalink}#${moved.publisher_id}`;
  assert.equal(json.excluded[0].citation, citation, "an excluded article keeps its citation");
  const rows = parseCsv(exportCsv(model)).slice(1);
  const excludedRow = Object.fromEntries(CSV_COLUMNS.map((column, position) => [column, rows.find((row) => row[3] === moved.publisher_id)[position]]));
  assert.equal(excludedRow.status, "excluded: no_text_tokens");
  assert.equal(excludedRow.citation, citation, "review of #789: the excluded row is cited, not left blank");
  assert.equal(excludedRow.text, "", "no text for an excluded article");
  assert.equal(excludedRow.watermark, EXPORT_WATERMARK);
  assert.equal(excludedRow.observed_at, OBSERVED);
  assert.equal(excludedRow.rights_rule, view.rightsRule);
  assert.equal(excludedRow.registry_sha256, view.registrySha256);
});

test("an export never names an article the reader was not shown, and needs a pin and a time", async () => {
  const view = readEvidenceBundle(await bundle());
  const state = view.states[0];
  assert.throws(() => composeExport({ view, pinned: [{ stateSha256: state.stateSha256, publisherId: "art_999" }], observedAt: OBSERVED }), /holds no article/);
  assert.throws(() => composeExport({ view, pinned: [{ stateSha256: "0".repeat(64), publisherId: state.articles[0].publisherId }], observedAt: OBSERVED }), /holds no article/);
  assert.throws(() => composeExport({ view, pinned: [], observedAt: OBSERVED }), /at least one pinned article/);
  assert.throws(() => composeExport({ view, pinned: [{ stateSha256: state.stateSha256, publisherId: state.articles[0].publisherId }], observedAt: "" }), /when the answering snapshot was observed/);
});

test("CSV quoting keeps commas, quotes and line breaks inside a field", async () => {
  const answer = await bundle();
  answer.states[0].articles[0].text = 'Il dit « oui », puis "non",\nencore.';
  answer.states[0].articles[0].text_byte_length = Buffer.byteLength(answer.states[0].articles[0].text, "utf8");
  answer.states[0].articles[0].text_sha256 = createHash("sha256").update(answer.states[0].articles[0].text, "utf8").digest("hex");
  const view = readEvidenceBundle(answer);
  const model = composeExport({ view, pinned: [{ stateSha256: view.states[0].stateSha256, publisherId: view.states[0].articles[0].publisherId }], observedAt: OBSERVED });
  const rows = parseCsv(exportCsv(model));
  assert.equal(rows[1][12], 'Il dit « oui », puis "non",\nencore.');
  assert.equal(rows.length, 2, "a line break inside a quoted field does not start a row");
});

/** The EU sample read as an EU reading, with `change` applied to its bundle, and its export of what `pick` pins. */
function europeExport(change = () => {}, pick = (wording) => [...wording.articles, ...wording.articlesWithoutText]) {
  const view = readEuropeEvidenceBundle(europeBundle(change));
  const pinned = view.wordings.flatMap((wording) => pick(wording).map((article) => ({ wordingSha256: wording.wordingSha256, publisherId: article.publisherId })));
  return { view, model: composeEuropeExport({ view, pinned, observedAt: OBSERVED, registrySha256: EUROPE_SAMPLE.registrySha256 }) };
}

/** A French wording beside the sample's English one, with no annex. */
function withFrench(bundle) {
  const french = structuredClone(bundle.wordings[0]);
  const permalink = `/eu-eurlex/${EUROPE_SAMPLE.celex}/fra/${EUROPE_SAMPLE.wordingDate}--${"6".repeat(64)}`;
  Object.assign(french, {
    publisher_expression_id: `${EUROPE_SAMPLE.work}.0008`, language: "fra", wording_sha256: "6".repeat(64), permalink,
    stable_coordinate: `/eu-eurlex/${EUROPE_SAMPLE.celex}/fra/${EUROPE_SAMPLE.wordingDate}`, annexes_not_served: [],
  });
  for (const article of french.articles) Object.assign(article, { language: "fra", article_permalink: `${permalink}#${escapeProvision(article.publisher_id)}` });
  Object.assign(bundle, { requested_language: null, available_languages: ["eng", "fra"], served_languages: ["eng", "fra"] });
  bundle.wordings.push(french);
}

const recordOf = (columns, row) => Object.fromEntries(columns.map((column, position) => [column, row[position]]));

test("an EU export carries each article's citation, text, digests, language, wording date, official source and acknowledgement, in both formats", () => {
  const { view, model } = europeExport();
  const [wording] = view.wordings;
  const json = JSON.parse(exportJson(model));
  assert.equal(json.schema, EUROPE_EXPORT_SCHEMA);
  assert.equal(json.watermark, EXPORT_WATERMARK);
  assert.equal(json.observed_at, OBSERVED);
  assert.equal(json.acknowledgement, EUROPE_TEXT_ACKNOWLEDGEMENT, "Decision 95's acknowledgement, exactly");
  assert.equal(json.authenticity, view.authenticity);
  assert.equal(json.rights_rule, view.rightsRule);
  assert.equal(json.wording_date_semantics, view.dateSemantics, "the file says what its dates are");
  assert.deepEqual(json.verified_by, { corpus_sha256: view.corpusSha256, index_sha256: view.indexSha256, registry_sha256: EUROPE_SAMPLE.registrySha256 });
  assert.deepEqual(json.items.map((item) => item.article), ["001", "002"]);
  for (const [index, item] of json.items.entries()) {
    const source = wording.articles[index];
    assert.equal(item.citation, `${wording.permalink}#${source.publisherId}`, "the citation is the article permalink, which pins the wording");
    assert.equal(item.wording_permalink, wording.permalink);
    assert.deepEqual([item.language, item.wording_kind, item.wording_date], ["eng", "original_wording", "2016-04-27"]);
    assert.equal(item.heading, source.heading);
    assert.equal(item.text, source.text);
    assert.equal(createHash("sha256").update(item.text, "utf8").digest("hex"), item.text_sha256, "the text still hashes to its digest");
    assert.equal(Buffer.byteLength(item.text, "utf8"), item.text_byte_length);
    assert.equal(item.body_sha256, source.bodySha256);
    assert.equal(item.official_source, source.officialSource);
    assert.equal(item.acknowledgement, EUROPE_TEXT_ACKNOWLEDGEMENT, "every item carries the acknowledgement");
    assert.equal(item.authenticity, view.authenticity, "and the authenticity statement, so an item taken out alone still says it (review of #920)");
  }

  const csv = parseCsv(exportCsv(model));
  assert.deepEqual(csv[0], [...EUROPE_CSV_COLUMNS]);
  assert.equal(csv.length, 5, "two items, one excluded article, one annex row");
  for (const row of csv.slice(1)) {
    const record = recordOf(EUROPE_CSV_COLUMNS, row);
    // A row copied out alone still says its act, its wording's date, what it was served with and what answered it.
    assert.deepEqual([record.celex, record.language, record.wording_date], [EUROPE_SAMPLE.celex, "eng", "2016-04-27"]);
    assert.equal(record.watermark, EXPORT_WATERMARK);
    assert.equal(record.acknowledgement, EUROPE_TEXT_ACKNOWLEDGEMENT, "every row carries the acknowledgement");
    assert.equal(record.authenticity, view.authenticity, "and the authenticity statement");
    assert.equal(record.observed_at, OBSERVED);
    assert.equal(record.rights_rule, view.rightsRule);
    assert.deepEqual([record.corpus_sha256, record.index_sha256, record.registry_sha256], [view.corpusSha256, view.indexSha256, EUROPE_SAMPLE.registrySha256]);
  }
  for (const [index, row] of csv.slice(1, 3).entries()) {
    const record = recordOf(EUROPE_CSV_COLUMNS, row);
    assert.equal(record.status, "exported");
    assert.equal(record.citation, json.items[index].citation);
    assert.equal(record.text, json.items[index].text, "the text survives quoting whole");
    assert.equal(record.text_sha256, json.items[index].text_sha256);
  }
});

test("an EU export excludes an article held without text, cited, and lists its wording's annexes with their reason, never a text", () => {
  const { view, model } = europeExport();
  const [wording] = view.wordings;
  const json = JSON.parse(exportJson(model));
  assert.deepEqual(json.excluded, [{
    celex: EUROPE_SAMPLE.celex, language: "eng", wording_kind: "original_wording", wording_date: "2016-04-27", article: "099",
    citation: `${wording.permalink}#099`, wording_permalink: wording.permalink, reason: EUROPE_WITHOUT_TEXT,
  }], "an excluded article keeps its citation and says why");
  const [annex] = wording.annexesNotServed;
  assert.deepEqual(json.annexes_not_served, [{
    celex: EUROPE_SAMPLE.celex, language: "eng", wording_kind: "original_wording", wording_date: "2016-04-27", wording_permalink: wording.permalink,
    official_identity: annex.officialIdentity, disposition: "annex_text_not_available", annexes: 2, annex_identities_sha256: [...annex.identities],
    served_as: "text_not_available", reason: annex.reason, official_source: annex.officialSource,
  }], "each annex row as the platform lists it, and nothing else: no text, no heading");
  assert.ok(!json.items.some((item) => item.article === "099"), "never quoted empty");

  const rows = parseCsv(exportCsv(model)).slice(1).map((row) => recordOf(EUROPE_CSV_COLUMNS, row));
  const excludedRow = rows.find((record) => record.article === "099");
  assert.equal(excludedRow.status, `excluded: ${EUROPE_WITHOUT_TEXT}`);
  assert.equal(excludedRow.citation, `${wording.permalink}#099`, "the excluded row is cited");
  assert.equal(excludedRow.text, "");
  const annexRow = rows.find((record) => record.status.startsWith("excluded: annex_"));
  assert.equal(annexRow.status, "excluded: annex_text_not_available");
  assert.deepEqual([annexRow.annexes, annexRow.reason, annexRow.official_source], ["2", annex.reason, annex.officialSource]);
  assert.deepEqual([annexRow.article, annexRow.citation, annexRow.text, annexRow.text_sha256], ["", "", "", ""], "an annex row names no article and holds no text");
  assert.equal(annexRow.wording_permalink, wording.permalink);

  // A provision held without text is cited as the platform escapes it in a permalink.
  const { model: escaped } = europeExport((b) => { b.wordings[0].articles_without_text[0].publisher_id = "Article 1(2)"; });
  assert.equal(escaped.excluded[0].citation, `${wording.permalink}#Article%201%282%29`);

  // The annexes travel with an export of their own wording only.
  const { model: french } = europeExport(withFrench, (held) => (held.language === "fra" ? held.articles.slice(0, 1) : []));
  assert.deepEqual(french.items.map((item) => [item.language, item.publisherId]), [["fra", "001"]]);
  assert.deepEqual(french.annexesNotServed, [], "the annexes of the English wording are not the French wording's");
  const { model: english } = europeExport(withFrench, (held) => (held.language === "eng" ? held.articles.slice(0, 1) : []));
  assert.deepEqual(english.annexesNotServed.map((row) => [row.language, row.count]), [["eng", 2]]);
});

test("an EU export's files name no Luxembourg date or rights disposition, and a Luxembourg export's no EU wording", async () => {
  const fieldsOf = (node, found = new Set()) => {
    if (Array.isArray(node)) node.forEach((item) => fieldsOf(item, found));
    else if (node !== null && typeof node === "object") {
      for (const [key, value] of Object.entries(node)) {
        found.add(key);
        fieldsOf(value, found);
      }
    }
    return found;
  };
  const LUXEMBOURG_ONLY = ["work_key", "applies_from", "state_permalink", "rights_disposition", "article_valid_from", "validity_conflict", "notes"];
  const EUROPE_ONLY = ["celex", "wording_kind", "wording_date", "wording_permalink", "wording_date_semantics", "acknowledgement", "authenticity", "heading", "annexes_not_served", "annexes"];

  const { model } = europeExport();
  const europeJson = exportJson(model);
  const europeCsv = exportCsv(model);
  const borrowed = (field) => LUXEMBOURG_ONLY.includes(field) || /applicab|state/.test(field);
  assert.deepEqual([...fieldsOf(JSON.parse(europeJson))].filter(borrowed), [], "no Luxembourg date or disposition in the EU JSON");
  assert.deepEqual(EUROPE_CSV_COLUMNS.filter(borrowed), [], "nor among the EU CSV's columns");
  for (const file of [europeJson, europeCsv]) {
    assert.ok(!file.includes("agreed_same_run_cc_by"), "EU text is never said to be served under Luxembourg's rights disposition");
  }

  const view = readEvidenceBundle(await bundle());
  const [state] = view.states;
  const luxembourg = composeExport({ view, pinned: state.articles.slice(0, 2).map((article) => ({ stateSha256: state.stateSha256, publisherId: article.publisherId })), observedAt: OBSERVED });
  const europeOnly = (field) => EUROPE_ONLY.includes(field) || /wording/.test(field);
  assert.deepEqual([...fieldsOf(JSON.parse(exportJson(luxembourg)))].filter(europeOnly), [], "no EU wording in the Luxembourg JSON");
  assert.deepEqual(CSV_COLUMNS.filter(europeOnly), [], "nor among the Luxembourg CSV's columns");
});

test("an EU export never names an article the reader was not shown, and needs a pin, a time and the registry that answered", async () => {
  const view = readEuropeEvidenceBundle(europeBundle());
  const [wording] = view.wordings;
  const compose = (change) => () => composeEuropeExport({
    view, pinned: [{ wordingSha256: wording.wordingSha256, publisherId: "001" }], observedAt: OBSERVED, registrySha256: EUROPE_SAMPLE.registrySha256, ...change,
  });
  assert.throws(compose({ pinned: [{ wordingSha256: wording.wordingSha256, publisherId: "999" }] }), /holds no article/);
  assert.throws(compose({ pinned: [{ wordingSha256: "0".repeat(64), publisherId: "001" }] }), /holds no article/, "a pin names its wording");
  assert.throws(compose({ pinned: [] }), /at least one pinned article/);
  assert.throws(compose({ observedAt: "" }), /when the answering snapshot was observed/);
  assert.throws(compose({ registrySha256: undefined }), /names the registry that answered/);
  assert.throws(compose({ view: readEvidenceBundle(await bundle()) }), /composed from an EU reading/, "a Luxembourg reading is never composed as EU text");
  assert.equal(compose({})().items.length, 1);
});
