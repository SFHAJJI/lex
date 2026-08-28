import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  classifyMatchLane, METADATA_ONLY_BODY, METADATA_ONLY_DISCLOSURE, METADATA_ONLY_HEADING,
  metadataOnlyState,
} from "./matchLanes.ts";

// The one normative case table shared with the C# classifier: parity is proven by both
// sides passing every case of this exact file, never by two hand-kept copies.
const here = dirname(fileURLToPath(import.meta.url));
const table = JSON.parse(readFileSync(
  join(here, "..", "..", "tests", "Lex.Tests", "match-lane-cases.json"), "utf8")) as {
  producer_vocabulary: string[];
  ambiguous_prefix: string;
  cases: { reasons: string[]; lane: string; note?: string }[];
};

test("every normative case classifies identically in TypeScript", () => {
  assert.ok(table.cases.length >= 28, "the table must keep its full case set");
  for (const item of table.cases) {
    assert.equal(classifyMatchLane(item.reasons), item.lane,
      `case ${JSON.stringify(item.reasons)}${item.note ? ` (${item.note})` : ""}`);
  }
});

test("every producer reason is deliberately classified, never unclassified", () => {
  for (const reason of table.producer_vocabulary) {
    assert.notEqual(classifyMatchLane([reason]), "unclassified_render",
      `producer reason ${reason} must be deliberately classified (Codex Q1 ruling)`);
  }
  assert.equal(classifyMatchLane([`${table.ambiguous_prefix}exact_title`]), "identity");
});

test("metadata_only requires at least one hit and every hit positively metadata", () => {
  assert.equal(metadataOnlyState([]), false);
  assert.equal(metadataOnlyState([["work_metadata"]]), true);
  assert.equal(metadataOnlyState([["work_metadata"], ["work_metadata"]]), true);
  assert.equal(metadataOnlyState([["work_metadata"], ["keyword"]]), false);
  assert.equal(metadataOnlyState([["work_metadata"], ["exact_title"]]), false);
  assert.equal(metadataOnlyState([["work_metadata"], ["never_seen_reason"]]), false);
  assert.equal(metadataOnlyState([[]]), false);
});

test("the frozen metadata_only copy is byte-equal to Decision 41", () => {
  assert.equal(METADATA_ONLY_HEADING, "No held text match");
  assert.equal(METADATA_ONLY_BODY,
    "Lex found records that match only in metadata. They are not shown as text answers. "
    + "This is not evidence that the named instrument or law does not exist. Check the name "
    + "or identifier, review coverage and known gaps, or search the official publisher.");
  assert.equal(METADATA_ONLY_DISCLOSURE, "Matched only in metadata");
});
