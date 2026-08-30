import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  anyRowSetTruncated, classifyMatchLane, laneOfServedReasons, METADATA_ONLY_BODY,
  METADATA_ONLY_DISCLOSURE, METADATA_ONLY_HEADING, metadataOnlyFromResponse,
  metadataOnlyResponse, metadataOnlyState, officialSearchHref, responsePopulation,
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
// The authoritative population, read off real producer envelope shapes (B1+B2 round 2)
// ---------------------------------------------------------------------------

/** The producer's search envelope shape: McpCore emits ok or no_result per publisher. */
const envelope = (publisher: string, status: string, hits: unknown[], extra = {}) => ({
  envelope: { publisher, status },
  hits,
  ...extra,
});
const hit = (work: string, reasons: unknown, title = "A law") => ({
  lex_id: `${work}:2024-01-01`, title, valid_from: "2024-01-01", match_reasons: reasons,
});

test("only a successful envelope contributes to the population", () => {
  // O5: a refused or malformed envelope's rows are not evidence. Admitting them lets a
  // refusal suppress a real answer behind the metadata-only notice.
  const { entries } = responsePopulation([
    envelope("lu-legilux", "filter_not_supported_by_index",
      [hit("lu-legilux:refused", ["work_metadata"])]),
    envelope("lu-legilux", "ok", [hit("lu-legilux:real", ["work_metadata"])]),
  ]);
  assert.deepEqual(entries.map((entry) => entry.work), ["lu-legilux:real"]);
  assert.equal(metadataOnlyResponse(entries), true);
});

test("search never emits no_result, so it cannot authorize suppression", () => {
  // Verified against the producer: the search case emits ok, retrieval_mode_unavailable,
  // unknown_work, unknown_anchor and no_provision_history. Round 1 admitted no_result, so a
  // cross-operation envelope carrying metadata hits could suppress real answers.
  const crossOperation = metadataOnlyFromResponse([
    envelope("lu-legilux", "no_result", [hit("lu-legilux:w1", ["work_metadata"])]),
  ]);
  assert.deepEqual(crossOperation.population, [], "its rows are not search evidence");
  assert.equal(crossOperation.metadataOnly, false);
});

test("an incomplete population can never authorize the positive claim", () => {
  // Round 1 read a malformed successful hits field as empty, so a sibling metadata-only
  // response still suppressed; and a wrong-typed reasons field vanished when a duplicate
  // work later contributed work_metadata.
  const malformedSibling = metadataOnlyFromResponse([
    { envelope: { publisher: "eu-eurlex", status: "ok" }, hits: "not-an-array" },
    envelope("lu-legilux", "ok", [hit("lu-legilux:w1", ["work_metadata"])]),
  ]);
  assert.equal(malformedSibling.metadataOnly, false,
    "a malformed sibling makes suppression unreachable");

  const missingStatus = metadataOnlyFromResponse([
    { envelope: { publisher: "eu-eurlex" }, hits: [] },
    envelope("lu-legilux", "ok", [hit("lu-legilux:w1", ["work_metadata"])]),
  ]);
  assert.equal(missingStatus.metadataOnly, false);

  const badReason = metadataOnlyFromResponse([
    envelope("lu-legilux", "ok", [
      hit("lu-legilux:w1", [42]),
      hit("lu-legilux:w1", ["work_metadata"]),
    ]),
  ]);
  assert.equal(badReason.metadataOnly, false,
    "a wrong-typed reason cannot be washed out by a later duplicate");

  const nullHit = metadataOnlyFromResponse([
    { envelope: { publisher: "lu-legilux", status: "ok" }, hits: [null] },
  ]);
  assert.equal(nullHit.metadataOnly, false);

  // The clean case still suppresses.
  const clean = metadataOnlyFromResponse([
    envelope("lu-legilux", "ok", [hit("lu-legilux:w1", ["work_metadata"])]),
  ]);
  assert.equal(clean.metadataOnly, true);
});

test("reasons union per logical work before fusion can discard them", () => {
  // O2: the workspace fusion step deduplicates by identity and drops the losing hit's
  // match_reasons. A work matched by metadata in one publisher and by keyword in another
  // must never read as metadata-only.
  const { entries } = responsePopulation([
    envelope("lu-legilux", "ok", [hit("lu-legilux:w1", ["work_metadata"])]),
    envelope("eu-eurlex", "ok", [hit("lu-legilux:w1", ["keyword"])]),
  ]);
  assert.equal(entries.length, 1, "one logical work");
  assert.equal(metadataOnlyResponse(entries), false,
    "the keyword reason survives the union and forbids suppression");

  // Reverse arrival order must give the same answer.
  const reversed = responsePopulation([
    envelope("eu-eurlex", "ok", [hit("lu-legilux:w1", ["keyword"])]),
    envelope("lu-legilux", "ok", [hit("lu-legilux:w1", ["work_metadata"])]),
  ]).entries;
  assert.equal(metadataOnlyResponse(reversed), false);
});

test("the population is the whole response, not the display slice", () => {
  // O3: the notice was fed the eight-work display slice, so an eleven-work response showed
  // eight rows, no overflow line, and no official action for a publisher past slot eight.
  const many = Array.from({ length: 11 }, (_, index) =>
    hit(`lu-legilux:w${index}`, ["work_metadata"]));
  const { entries: population } = responsePopulation([
    envelope("lu-legilux", "ok", many),
    envelope("eu-eurlex", "ok", [hit("eu-eurlex:late", ["work_metadata"])]),
  ]);
  assert.equal(population.length, 12, "every logical work reaches the disclosure");
  assert.ok(population.some((entry) => entry.work.startsWith("eu-eurlex")),
    "a publisher appearing after the display cap still contributes its official action");
});

test("a truncated row set is detected so no exact overflow total is invented", () => {
  // O4: the producer carries response_row_set.truncated; I previously asserted in writing
  // that no such marker existed, without checking.
  assert.equal(anyRowSetTruncated([
    envelope("lu-legilux", "ok", [], { response_row_set: { truncated: false } }),
  ]), false);
  assert.equal(anyRowSetTruncated([
    envelope("lu-legilux", "ok", [], { response_row_set: { truncated: false } }),
    envelope("eu-eurlex", "ok", [], { response_row_set: { truncated: true } }),
  ]), true);
  assert.equal(anyRowSetTruncated([envelope("lu-legilux", "ok", [])]), false);
});

test("hostile reason members never throw and never authorize suppression", () => {
  assert.equal(metadataOnlyFromResponse([
    envelope("lu-legilux", "ok", [hit("lu-legilux:w1", [42])]),
  ]).metadataOnly, false,
    "a numeric reason is unclassified, so the hit renders instead of being suppressed");
  assert.equal(metadataOnlyFromResponse([
    envelope("lu-legilux", "ok", [hit("lu-legilux:w1", "work_metadata")]),
  ]).metadataOnly, false, "a non-array reasons field is not evidence either");
});
