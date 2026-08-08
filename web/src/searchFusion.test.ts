import assert from "node:assert/strict";
import test from "node:test";
import { fusePublisherHits } from "./searchFusion.ts";

test("cross-publisher fusion preserves local ranks and gives both jurisdictions visible slots", () => {
  const result = fusePublisherHits([
    {
      envelope: { publisher: "eu-eurlex", jurisdiction: "EU", timeline_semantics: "official_consolidation_state" },
      hits: Array.from({ length: 12 }, (_, i) => ({ lex_id: `eu-eurlex:e${i}:v:art_${i}` })),
    },
    {
      envelope: { publisher: "lu-legilux", jurisdiction: "LU" },
      hits: Array.from({ length: 12 }, (_, i) => ({ lex_id: `lu-legilux:l${i}:v:art_${i}` })),
    },
  ]);

  assert.deepEqual(result.slice(0, 6).map(hit => hit._jurisdiction), ["EU", "LU", "EU", "LU", "EU", "LU"]);
  assert.equal(result[0]._timelineSemantics, "official_consolidation_state");
  assert.equal(result[1]._timelineSemantics, undefined);
  assert.deepEqual(result.slice(0, 4).map(hit => hit.lex_id), [
    "eu-eurlex:e0:v:art_0", "lu-legilux:l0:v:art_0",
    "eu-eurlex:e1:v:art_1", "lu-legilux:l1:v:art_1",
  ]);
});

test("fusion neither invents empty-publisher results nor disturbs a single publisher", () => {
  const result = fusePublisherHits([
    { envelope: { publisher: "eu-eurlex", jurisdiction: "EU" }, hits: [] },
    { envelope: { publisher: "lu-legilux", jurisdiction: "LU" }, hits: [
      { lex_id: "lu-legilux:first:v:art_1" },
      { lex_id: "lu-legilux:second:v:art_1" },
    ] },
  ]);

  assert.deepEqual(result.map(hit => hit.lex_id), [
    "lu-legilux:first:v:art_1", "lu-legilux:second:v:art_1",
  ]);
});

test("the same exact hit returned by two retrievers is collapsed and receives both votes", () => {
  const result = fusePublisherHits([
    { envelope: { publisher: "a" }, hits: [{ lex_id: "shared" }, { lex_id: "a-only" }] },
    { envelope: { publisher: "b" }, hits: [{ lex_id: "b-only" }, { lex_id: "shared" }] },
  ]);

  assert.deepEqual(result.map(hit => hit.lex_id), ["shared", "b-only", "a-only"]);
});
