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

/**
 * The producer's retrieval unit is the article, not the document. McpCore.cs deduplicates hits
 * on (work, anchor) and only then caps per work, so several articles of one law arrive sharing
 * one document-level lex_id, on purpose. The rows below therefore have to reach the reader as
 * two results.
 */
type SearchRow = {
  lex_id: string;
  anchor?: string | null;
  provision_num?: string;
  title?: string;
};

test("two articles of one law from one publisher both survive fusion", () => {
  const result = fusePublisherHits<SearchRow>([
    {
      envelope: { publisher: "lu-legilux", jurisdiction: "LU" },
      hits: [
        { lex_id: "lu-legilux:code-travail:2024-01-01", anchor: "art_5", provision_num: "5" },
        { lex_id: "lu-legilux:code-travail:2024-01-01", anchor: "art_12", provision_num: "12" },
      ],
    },
  ]);

  assert.equal(result.length, 2);
  assert.deepEqual(result.map(hit => hit.anchor), ["art_5", "art_12"]);
  assert.deepEqual(result.map(hit => hit.provision_num), ["5", "12"]);
});

test("a document-level hit and an article of the same document stay two results", () => {
  const result = fusePublisherHits<SearchRow>([
    {
      envelope: { publisher: "lu-legilux", jurisdiction: "LU" },
      hits: [
        { lex_id: "lu-legilux:loi-2020:2020-01-01", anchor: null },
        { lex_id: "lu-legilux:loi-2020:2020-01-01", anchor: "art_3" },
      ],
    },
  ]);

  assert.equal(result.length, 2);
  assert.deepEqual(result.map(hit => hit.anchor), [null, "art_3"]);
});

test("the same article of one law from two publishers still collapses to a single row", () => {
  const result = fusePublisherHits<SearchRow>([
    { envelope: { publisher: "a" }, hits: [
      { lex_id: "shared:gdpr:2018-05-25", anchor: "art_6" },
      { lex_id: "shared:gdpr:2018-05-25", anchor: "art_9" },
    ] },
    { envelope: { publisher: "b" }, hits: [
      { lex_id: "other:x:2020-01-01", anchor: "art_1" },
      { lex_id: "shared:gdpr:2018-05-25", anchor: "art_6" },
    ] },
  ]);

  assert.deepEqual(result.map(hit => `${hit.lex_id}#${hit.anchor}`), [
    "shared:gdpr:2018-05-25#art_6",
    "other:x:2020-01-01#art_1",
    "shared:gdpr:2018-05-25#art_9",
  ]);
});

test("a hit with no anchor fuses on document identity however the absence is spelled", () => {
  const result = fusePublisherHits<SearchRow>([
    { envelope: { publisher: "a" },
      hits: [{ lex_id: "eu-eurlex:32016r0679:2018-05-25", anchor: null }] },
    { envelope: { publisher: "b" },
      hits: [{ lex_id: "eu-eurlex:32016r0679:2018-05-25" }] },
  ]);

  assert.equal(result.length, 1);
  assert.equal(result[0].anchor, null);
});

/**
 * The separator is what makes the pair encoding injective. Nothing the producer emits can
 * reach this case, because a lex_id ends in a version coordinate and an anchor is a provision
 * slug, but "identity is the (document, provision) pair" is only true if the two fields cannot
 * borrow each other's characters. Concatenating them compiles and passes every realistic case.
 */
test("the document and the provision cannot borrow each other's characters", () => {
  const result = fusePublisherHits<SearchRow>([
    { envelope: { publisher: "a" }, hits: [
      { lex_id: "x", anchor: "yz" },
      { lex_id: "xy", anchor: "z" },
    ] },
  ]);

  assert.equal(result.length, 2);
});

/**
 * Why the new scores are right and not merely different. An RRF contribution is one retriever's
 * vote for one result. Collapsing a law's articles into a single row summed two different
 * results' ranks into one vote, which is double counting, and it inverted the publisher's own
 * ranking: the rows below came back as second, top, with the producer's rank 0 demoted by a
 * lower-ranked law that happened to match twice.
 */
test("a law matching twice cannot outrank a publisher's own top result", () => {
  const result = fusePublisherHits<SearchRow>([
    { envelope: { publisher: "p" }, hits: [
      { lex_id: "p:top:v", anchor: "art_1" },
      { lex_id: "p:second:v", anchor: "art_1" },
      { lex_id: "p:second:v", anchor: "art_2" },
    ] },
  ]);

  assert.deepEqual(result.map(hit => `${hit.lex_id}#${hit.anchor}`), [
    "p:top:v#art_1", "p:second:v#art_1", "p:second:v#art_2",
  ]);
});
