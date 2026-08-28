import assert from "node:assert/strict";
import test from "node:test";
import { envelopeStripRows, indexFreshnessLabel } from "./envelopeStrip.ts";

const envelope = (publisher: string, over: Record<string, unknown> = {}) => ({
  envelope: {
    publisher,
    timeline_semantics: "publisher_applicability",
    freshness: {
      built_at: "2026-08-15T09:01:06Z",
      corpus_commit: "e9c4df09",
      stamp_signature_valid: true,
    },
    artifact: { code_commit: "abc123", manifest_set_id: "m-1", content_digest: "d-1" },
    ...over,
  },
});

test("the strip carries the identity fields the envelope already published", () => {
  const [row] = envelopeStripRows([envelope("lu-legilux")]);
  assert.equal(row.publisher, "lu-legilux");
  assert.equal(row.builtAt, "2026-08-15T09:01:06Z");
  assert.equal(row.signatureValid, true);
  assert.equal(row.corpusCommit, "e9c4df09");
  assert.equal(row.codeCommit, "abc123");
  assert.equal(row.manifestSetId, "m-1");
  assert.equal(row.contentDigest, "d-1");
});

test("freshness stays per publisher and is never collapsed into one build date", () => {
  // Two indexes built a week apart have two answers. Picking one asserts a freshness the other
  // does not have, which is the whole failure mode rule 8 names.
  const rows = envelopeStripRows([
    envelope("lu-legilux"),
    envelope("eu-eurlex", { freshness: { built_at: "2026-08-08T00:00:00Z" } }),
  ]);
  assert.equal(rows.length, 2);
  assert.deepEqual(rows.map((r) => r.publisher), ["eu-eurlex", "lu-legilux"]);
  assert.notEqual(rows[0].builtAt, rows[1].builtAt);
});

test("a publisher repeated across entries yields one strip row", () => {
  assert.equal(envelopeStripRows([envelope("lu-legilux"), envelope("lu-legilux")]).length, 1);
});

test("a missing build date is stated, never omitted into an undated current", () => {
  const [row] = envelopeStripRows([envelope("lu-legilux", { freshness: {} })]);
  assert.equal(row.builtAt, undefined);
  assert.equal(indexFreshnessLabel(row.builtAt), "index build date unavailable");
  assert.equal(indexFreshnessLabel("2026-08-15T09:01:06Z"), "index built 2026-08-15T09:01:06Z");
});

test("a value of the wrong type becomes absent rather than rendered", () => {
  const [row] = envelopeStripRows([envelope("lu-legilux", {
    freshness: { built_at: 20260815, stamp_signature_valid: "yes", corpus_commit: "" },
    artifact: { code_commit: null, manifest_set_id: "x".repeat(129) },
  })]);
  assert.equal(row.builtAt, undefined);
  assert.equal(row.signatureValid, undefined, "a non-boolean must not become a signature claim");
  assert.equal(row.corpusCommit, undefined);
  assert.equal(row.codeCommit, undefined);
  assert.equal(row.manifestSetId, undefined, "an unbounded identity must not render");
});

test("a malformed response yields no strip rather than a fabricated one", () => {
  for (const raw of [null, undefined, {}, "envelope", [null], [{}], [{ envelope: [] }],
                     [{ envelope: { publisher: "" } }]]) {
    assert.deepEqual(envelopeStripRows(raw), [], `fabricated a row from ${JSON.stringify(raw)}`);
  }
});
