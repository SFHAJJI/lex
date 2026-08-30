import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { classifyMatchLane, laneOfServedReasons, METADATA_ONLY_BODY,
  METADATA_ONLY_DISCLOSURE, METADATA_ONLY_HEADING, metadataOnlyResponse, metadataOnlyState, officialSearchHref, } from "./matchLanes.ts";

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

test("every producer reason carries a deliberate single-reason ruling in the table", () => {
  // semantic_work and semantic_concept are DELIBERATELY unclassified_render (Codex B2
  // review O3), so deliberateness is table coverage, not a non-unclassified lane.
  const ruled = new Set(table.cases
    .filter((item) => item.reasons.length === 1)
    .map((item) => item.reasons[0]));
  for (const reason of table.producer_vocabulary) {
    assert.ok(ruled.has(reason), `producer reason ${reason} needs its own table case`);
  }
  assert.equal(classifyMatchLane([`${table.ambiguous_prefix}exact_title`]), "identity");
});

test("wire reason shapes fail closed to unclassified_render, never metadata", () => {
  assert.equal(laneOfServedReasons(undefined), "unclassified_render");
  assert.equal(laneOfServedReasons("work_metadata"), "unclassified_render");
  assert.equal(laneOfServedReasons([42]), "unclassified_render");
  assert.equal(laneOfServedReasons([{ reason: "work_metadata" }]), "unclassified_render");
  assert.equal(laneOfServedReasons(["work_metadata", null]), "unclassified_render");
  assert.equal(laneOfServedReasons(["work_metadata"]), "metadata");
});

test("the response decision reads the whole population before any cap", () => {
  const metadata = (work: string) => ({ work, reasons: ["work_metadata"] });
  const eightMetadata = Array.from({ length: 8 }, (_, index) => metadata(`p:w-${index}`));

  // A ninth text or identity hit beyond the display cap forbids the state (Codex O2).
  assert.equal(metadataOnlyResponse([...eightMetadata, { work: "p:w-9", reasons: ["keyword"] }]), false);
  assert.equal(metadataOnlyResponse([...eightMetadata, { work: "p:w-9", reasons: ["exact_title"] }]), false);
  assert.equal(metadataOnlyResponse(eightMetadata), true);

  // Hits collapsing to one logical work contribute the UNION of their reasons, both orders.
  const duplicated = [
    { work: "p:w-1", reasons: ["work_metadata"] },
    { work: "p:w-1", reasons: ["keyword"] },
  ];
  assert.equal(metadataOnlyResponse(duplicated), false);
  assert.equal(metadataOnlyResponse([...duplicated].reverse()), false);
  assert.equal(metadataOnlyResponse([
    { work: "p:w-1", reasons: ["work_metadata"] },
    { work: "p:w-1", reasons: ["work_metadata"] },
  ]), true);

  // A wrong-typed reason shape forbids the claim outright; empty responses never claim.
  assert.equal(metadataOnlyResponse([...eightMetadata, { work: "p:w-9", reasons: [7] }]), false);
  assert.equal(metadataOnlyResponse([]), false);
});

test("official search actions use the reviewed exact hosts only", () => {
  assert.equal(officialSearchHref("lu-legilux"), "https://legilux.public.lu");
  assert.equal(officialSearchHref("eu-eurlex"), "https://eur-lex.europa.eu");
  assert.equal(officialSearchHref("evil-collection"), "/search");
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

// ---------------------------------------------------------------------------
// The raw-response population helpers that used to live here are gone. The governed parse is the
// only authority now, and its projector is tested against the real production path in
// browser/metadata-only-authority.spec.ts. Lane classification and metadataOnlyResponse stay,
// because those are the lane policy itself and are bound by match-lane-cases.json.
// ---------------------------------------------------------------------------
