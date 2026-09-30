// The live composer's exports, composed from the evidence bundle the real handler sent.
//
// The bundle comes from `schemas/v3-platform/answer-samples.json` (operation `evidence_bundle`),
// read by `readEvidenceBundle`. The launch contract asks that exports "preserve citations, rights,
// watermarks and exclusions"; each of those is checked in both formats.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { readEvidenceBundle } from "../scripts/reading-answer.mjs";
import { CSV_COLUMNS, EXPORT_SCHEMA, EXPORT_WATERMARK, composeExport, exportCsv, exportJson } from "../scripts/export-build.mjs";
import { WATERMARK_PREVIEW } from "../scripts/export-composer.mjs";

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
  const rows = parseCsv(exportCsv(model)).slice(1);
  const excludedRow = rows.find((row) => row[3] === moved.publisher_id);
  assert.equal(excludedRow[4], "excluded: no_text_tokens");
  assert.equal(excludedRow[12], "", "no text for an excluded article");
  assert.equal(excludedRow[13], EXPORT_WATERMARK);
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
