import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { envelopeStripRows, indexFreshnessLabel } from "./envelopeStrip.ts";
import {
  governedStripRows, parseGovernedResponse, partitionGovernedResponse,
  projectGovernedEmptiness, searchAbsenceState, validateLimitation,
  type GovernedResponse,
} from "./limitations.ts";

/**
 * THROUGH THE PARSE, ALWAYS (O1).
 *
 * Every fixture here is a governed search response and every assertion reads the strip's view of
 * the one parse, because that is now the strip's only input. The file used to hand
 * `envelopeStripRows` raw entry objects, which is precisely the defect the review found: the
 * strip checked its own field shapes and never asked whether the unit carrying them was a valid
 * unit of the requested tool, so a response the table rejected as unusable still displayed a
 * confident build date and a valid-signature badge.
 *
 * The identity fields are unchanged and every grammar case below is the same case it was; what
 * changed is that each one now rides on a unit the table admits, so the tests exercise the shipped
 * path rather than a validator no production caller reaches.
 */
const OK_POPULATION = {
  basis: "selected_metadata_scope",
  works_in_scope: 1250,
  scope_filters_applied: true,
  query_ran: true,
  known_exclusions: [],
};

/** A search unit the table ADMITS, carrying the identity fields the strip renders. */
const unit = (publisher: unknown, over: Record<string, unknown> = {}) => ({
  envelope: {
    publisher,
    status: "ok",
    timeline_semantics: "publisher_applicability",
    freshness: {
      built_at: "2026-08-15T09:01:06Z",
      corpus_commit: "e9c4df09",
      stamp_signature_valid: true,
    },
    artifact: { code_commit: "abc123", manifest_set_id: "m-1", content_digest: "d-1" },
    ...over,
  },
  retrieval_mode: "keyword",
  hits: [],
  population: OK_POPULATION,
});

/** The production path: one parse, then the strip's view of it. */
const stripOf = (entries: unknown) => envelopeStripRows(parseGovernedResponse("search", entries));

test("the strip carries the identity fields the envelope already published", () => {
  const [row] = stripOf([unit("lu-legilux")]);
  assert.equal(row.publisher, "lu-legilux");
  assert.equal(row.builtAt, "2026-08-15T09:01:06Z");
  assert.equal(row.signatureValid, true);
  assert.equal(row.corpusCommit, "e9c4df09");
  assert.equal(row.codeCommit, "abc123");
  assert.equal(row.manifestSetId, "m-1");
  assert.equal(row.contentDigest, "d-1");
  // The timeline semantics comes from the parse's own bounded validator now, not a second one
  // inside the strip. One field, one validator, whichever surface renders it.
  assert.equal(row.timelineSemantics, "publisher_applicability");
});

test("freshness stays per publisher and is never collapsed into one build date", () => {
  // Two indexes built a week apart have two answers. Picking one asserts a freshness the other
  // does not have, which is the whole failure mode rule 8 names.
  const rows = stripOf([
    unit("lu-legilux"),
    unit("eu-eurlex", { freshness: { built_at: "2026-08-08T00:00:00Z" } }),
  ]);
  assert.equal(rows.length, 2);
  assert.deepEqual(rows.map((r) => r.publisher), ["eu-eurlex", "lu-legilux"]);
  assert.notEqual(rows[0].builtAt, rows[1].builtAt);
});

test("a missing build date is stated, never omitted into an undated current", () => {
  const [row] = stripOf([unit("lu-legilux", { freshness: {} })]);
  assert.equal(row.builtAt, undefined);
  assert.equal(indexFreshnessLabel(row.builtAt), "index build date unavailable");
  assert.equal(indexFreshnessLabel("2026-08-15T09:01:06Z"), "index built 2026-08-15T09:01:06Z");
});

test("a value of the wrong type becomes absent rather than rendered", () => {
  const [row] = stripOf([unit("lu-legilux", {
    freshness: { built_at: 20260815, stamp_signature_valid: "yes", corpus_commit: "" },
    artifact: { code_commit: null, manifest_set_id: "x".repeat(129) },
  })]);
  assert.equal(row.builtAt, undefined);
  assert.equal(row.signatureValid, undefined, "a non-boolean must not become a signature claim");
  assert.equal(row.corpusCommit, undefined);
  assert.equal(row.codeCommit, undefined);
  assert.equal(row.manifestSetId, undefined, "an unbounded identity must not render");
});

// The producer mints built_at as now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ") in
// IndexFromCorpus.cs, and McpCore.cs copies that stamp entry into the envelope unchanged. Both
// values the live coverage tool returned on 2026-08-28 have exactly this shape.
const PRODUCER_BUILT_AT = "2026-08-15T09:22:08Z";

const builtAtOf = (value: unknown) =>
  stripOf([unit("lu-legilux", { freshness: { built_at: value } })])[0].builtAt;

test("a canonical producer build date round-trips into the strip unchanged", () => {
  assert.equal(builtAtOf(PRODUCER_BUILT_AT), PRODUCER_BUILT_AT);
  assert.equal(
    indexFreshnessLabel(builtAtOf(PRODUCER_BUILT_AT)),
    `index built ${PRODUCER_BUILT_AT}`,
  );
  // Boundary instants the producer can legitimately mint must survive too, or the validation
  // would be refusing real builds and calling a dated index undated.
  for (const stamp of ["2024-02-29T00:00:00Z", "2026-12-31T23:59:59Z", "2026-01-01T00:00:00Z"]) {
    assert.equal(builtAtOf(stamp), stamp, `refused a build date the producer can emit: ${stamp}`);
  }
  // A two-digit year is inside the frozen grammar. Date.UTC maps 0-99 onto 1900-1999, so the
  // calendar check has to be made against the year the producer actually wrote. Returned
  // verbatim, never silently reinterpreted as 1926 and never refused because of that mapping.
  assert.equal(builtAtOf("0026-02-28T09:22:08Z"), "0026-02-28T09:22:08Z");
});

test("a bounded string that is not the producer's grammar never becomes a build date", () => {
  // The failure this closes: any bounded string was taken as a build date, so a synthetic
  // envelope could print "index built tomorrow" while the module claimed to fail closed.
  for (const value of [
    "tomorrow",
    "now",
    "2026-08-15",
    "15/08/2026",
    "2026-08-15T09:22:08",
    "2026-08-15T09:22:08.123Z",
    "2026-08-15T09:22:08+02:00",
    "2026-08-15t09:22:08z",
    "2026-08-15 09:22:08Z",
    " 2026-08-15T09:22:08Z",
    "2026-08-15T09:22:08Z ",
    "2026-08-15T09:22:08Z\n",
  ]) {
    assert.equal(builtAtOf(value), undefined, `rendered a build date from ${JSON.stringify(value)}`);
    assert.equal(indexFreshnessLabel(builtAtOf(value)), "index build date unavailable");
  }
});

test("a date that is well formed but does not exist is refused, not rolled over", () => {
  // new Date("2026-02-31") answers 3 March rather than failing, so the grammar alone would print
  // a day that never happened. Refusing means the round trip catches the rollover.
  for (const value of [
    "2026-02-31T09:22:08Z",
    "2025-02-29T09:22:08Z",
    "0026-02-31T09:22:08Z",
    "2026-13-01T09:22:08Z",
    "2026-00-10T09:22:08Z",
    "2026-04-31T09:22:08Z",
    "2026-08-00T09:22:08Z",
    "2026-08-32T09:22:08Z",
    "2026-08-15T24:00:00Z",
    "2026-08-15T09:60:08Z",
    "2026-08-15T09:22:60Z",
  ]) {
    assert.equal(builtAtOf(value), undefined, `printed an impossible instant: ${value}`);
  }
  // And it must not normalize one into a different identity either.
  assert.notEqual(builtAtOf("2026-02-31T09:22:08Z"), "2026-03-03T09:22:08Z");
});

test("a year the producer's own type cannot mint is refused", () => {
  // Not an arbitrary narrowing of the observed format: built_at is minted from a .NET DateTime,
  // and DateTime.MinValue is 0001-01-01, so 0000 is unmintable at the mint site. It is also the
  // only sub-0001 year four digits can express, so this closes the whole range below the
  // producer's. Reachable at all only because the year correction stops Date.UTC folding it
  // into 1900, which is why the two must sit side by side.
  assert.equal(builtAtOf("0000-01-01T00:00:00Z"), undefined);
  assert.equal(indexFreshnessLabel(builtAtOf("0000-01-01T00:00:00Z")), "index build date unavailable");
  assert.equal(builtAtOf("0000-12-31T23:59:59Z"), undefined);
  // The first instant the producer's type can actually mint is on the accepted side of the bound.
  assert.equal(builtAtOf("0001-01-01T00:00:00Z"), "0001-01-01T00:00:00Z");
  // DateTime.MaxValue is 9999-12-31, which four anchored digits already cap, so the top of the
  // range needs no guard of its own and a five-digit year cannot reach the check at all.
  assert.equal(builtAtOf("9999-12-31T23:59:59Z"), "9999-12-31T23:59:59Z");
  assert.equal(builtAtOf("10000-01-01T00:00:00Z"), undefined);
});

test("an overlong build date is refused, padded around a real stamp or not", () => {
  assert.equal(builtAtOf("2".repeat(300)), undefined);
  assert.equal(builtAtOf("x".repeat(129)), undefined);
  // A real stamp buried in junk must not be salvaged out of it; the grammar is anchored.
  assert.equal(builtAtOf(`${"x".repeat(200)}${PRODUCER_BUILT_AT}`), undefined);
  assert.equal(builtAtOf(`${PRODUCER_BUILT_AT}${"x".repeat(200)}`), undefined);
});

test("a build date that is not a string at all is refused", () => {
  for (const value of [20260815, 0, null, undefined, true, {}, [], [PRODUCER_BUILT_AT],
                       new Date("2026-08-15T09:22:08Z")]) {
    assert.equal(builtAtOf(value), undefined, `rendered a build date from ${typeof value}`);
  }
});

test("a malformed build date does not suppress the rest of that publisher's row", () => {
  // Fail closed on the date, not on the row: the other envelope facts are still true and rule 4
  // wants them on screen. Dropping the row would hide the stamp validity as well.
  const [row] = stripOf([unit("lu-legilux", {
    freshness: { built_at: "tomorrow", corpus_commit: "e9c4df09", stamp_signature_valid: true },
  })]);
  assert.equal(row.publisher, "lu-legilux");
  assert.equal(row.builtAt, undefined);
  assert.equal(indexFreshnessLabel(row.builtAt), "index build date unavailable");
  assert.equal(row.corpusCommit, "e9c4df09");
  assert.equal(row.signatureValid, true);
  assert.equal(row.timelineSemantics, "publisher_applicability");
  assert.equal(row.codeCommit, "abc123");
});

test("a malformed response yields no strip rather than a fabricated one", () => {
  for (const raw of [null, undefined, {}, "envelope", [null], [{}], [{ envelope: [] }],
                     [{ envelope: { publisher: "" } }]]) {
    assert.deepEqual(stripOf(raw), [], `fabricated a row from ${JSON.stringify(raw)}`);
  }
});

// ---------------------------------------------------------------------------
// O1: the strip may claim only what the parse admitted
// ---------------------------------------------------------------------------

const UNESTABLISHED = {
  publisher: "lu-legilux",
  timelineSemantics: undefined,
  builtAt: undefined,
  signatureValid: undefined,
  corpusCommit: undefined,
  codeCommit: undefined,
  manifestSetId: undefined,
  contentDigest: undefined,
};

test("a unit the parser rejected authorizes no build date and no signature claim", () => {
  // THE REVIEWER'S OWN PROBE, and the reason O1 was raised. `hits: [null]` is a row shape the
  // table refuses outright, and the very same object carries a producer-shaped build date and
  // `stamp_signature_valid: true`. Two authorities over one response reached opposite verdicts:
  // the parse called it unusable and the strip printed a confident index identity beside it.
  const rejected = { ...unit("lu-legilux"), hits: [null] };
  const parsed = parseGovernedResponse("search", [rejected]);
  assert.deepEqual(envelopeStripRows(parsed), [],
    "a rejected unit still published a build date and a signature verdict");
  // And the same bytes handed over unparsed, which is what the shipped code was doing at three
  // call sites, authorize nothing either: there is no second reader left to publish the badge
  // from an entry the table refused.
  assert.deepEqual(envelopeStripRows([rejected] as unknown as GovernedResponse), [],
    "the rejected unit published its index identity through a raw call");
  // Non-triviality, in both directions: the parse really did refuse this fixture, and the same
  // fixture with a valid row really does produce the claim.
  assert.equal(parsed.units.length, 0,
    "the fixture was admitted after all, so this test would prove nothing");
  assert.equal(parsed.unusable, 1);
  const admitted = stripOf([unit("lu-legilux", {})]);
  assert.equal(admitted.length, 1);
  assert.equal(admitted[0].signatureValid, true);
});

test("raw response bytes authorize no strip claim", () => {
  // THE NAMED TEST for the raw call. `tool<any>` makes `any` the static type of every raw MCP
  // response in this workspace, so a restored `envelopeStripRows(res)` compiles: nothing but this
  // can catch it. Raw transport carries no `units`, so it authorizes nothing rather than walking
  // the array for itself.
  const raw: unknown = [unit("lu-legilux"), unit("eu-eurlex")];
  assert.deepEqual(envelopeStripRows(raw as GovernedResponse), [],
    "raw response bytes minted a strip row, so the strip is parsing responses again");
  // And the same bytes through the one parse do produce two rows, so the refusal above is about
  // the input being unparsed rather than about the fixture being empty.
  assert.equal(stripOf(raw).length, 2);
});

test("the partition offers exactly the strip rows the parse authorized", () => {
  // App.tsx holds a partition rather than the parse, so the rows the changes and in-force pages
  // render come from here. ONE computation behind both, so the two surfaces cannot drift the way
  // the strip and the table did.
  const entries = [unit("lu-legilux"), { ...unit("eu-eurlex"), hits: [null] }];
  const parsed = parseGovernedResponse("search", entries);
  const partition = partitionGovernedResponse("search", entries);
  assert.deepEqual(partition.stripRows, envelopeStripRows(parsed));
  assert.deepEqual(partition.stripRows.map((row) => row.publisher), ["lu-legilux"],
    "a rejected sibling contributed a strip row");
});

test("a publisher whose scope could not be read states only that its identity is unavailable", () => {
  // The producer publishes a population on every search path, so a unit whose population will not
  // validate authorizes nothing. The publisher is still named, because "something was withheld"
  // with nobody to ask about is a worse disclosure than none.
  const parsed = parseGovernedResponse("search",
    [{ ...unit("lu-legilux"), population: "1250" }]);
  assert.deepEqual(parsed.unreadable, ["lu-legilux"]);
  assert.deepEqual(envelopeStripRows(parsed), [UNESTABLISHED],
    "an unreadable scope kept its build date and signature badge");
});

test("an admitted refusal still discloses the index that refused", () => {
  // Rule 4 has no exception for a coverage refusal. The publisher is mounted, its index has a
  // build date and a stamp verdict, and `Envelope` stamps both on every path it builds, so a
  // strip reading ran units only would blank a row the response really carried.
  const refusal = {
    envelope: {
      ...unit("lu-legilux").envelope, status: "filter_not_supported_by_index",
    },
    unsupported_filters: ["domain"],
    population: {
      basis: "mounted_scope_before_unsupported_filters",
      works_in_scope: 1250,
      scope_filters_applied: false,
      query_ran: false,
      known_exclusions: [],
    },
  };
  const [row] = stripOf([refusal]);
  assert.equal(row.builtAt, "2026-08-15T09:01:06Z");
  assert.equal(row.signatureValid, true);
  assert.equal(row.corpusCommit, "e9c4df09");
});

// Duplicate publisher identities. A second claim-bearing unit for one publisher is a shape the
// producer cannot emit (the reader registry is keyed by collection), so the parse withholds every
// claim that publisher made and the strip states that its identity is unavailable. It used to
// compare the two disclosures field by field and collapse them when they agreed; that comparison
// is gone, because agreeing about a build date does not make a second unit legitimate.

const conflictPair = () => [
  unit("lu-legilux"),
  unit("lu-legilux", {
    freshness: {
      built_at: "2026-08-08T00:00:00Z",
      corpus_commit: "0000ffff",
      stamp_signature_valid: false,
    },
  }),
];

test("conflicting envelopes for one publisher state no identity rather than the first one", () => {
  const rows = stripOf(conflictPair());
  assert.equal(rows.length, 1);
  assert.deepEqual(rows[0], UNESTABLISHED, "picked one conflicting disclosure and rendered it");
  assert.equal(indexFreshnessLabel(rows[0].builtAt), "index build date unavailable");
});

test("arrival order cannot decide the displayed identity", () => {
  const [first, second] = conflictPair();
  assert.deepEqual(stripOf([first, second]), stripOf([second, first]));
  // Named explicitly as well, so a regression cannot pass by making both orders equally wrong.
  for (const order of [[first, second], [second, first]]) {
    const [row] = stripOf(order);
    assert.equal(row.builtAt, undefined, "arrival order produced a confident build date");
    assert.equal(row.corpusCommit, undefined, "arrival order produced a confident corpus commit");
    assert.equal(row.signatureValid, undefined, "arrival order produced a signature claim");
  }
});

test("a conflicted publisher is disclosed, never dropped from the strip", () => {
  // Dropping it would hide that the index answered at all, which reads as "not mounted" and is a
  // different, false statement. The reader has to be told the identity could not be established.
  const rows = stripOf(conflictPair());
  assert.deepEqual(rows.map((r) => r.publisher), ["lu-legilux"]);
  assert.equal(indexFreshnessLabel(rows[0].builtAt), "index build date unavailable");
});

test("a second unit from one publisher withholds the identity however alike the two look", () => {
  // Byte-identical, differing in one field, or differing in all of them: the verdict is the same,
  // because the count is what makes the second unit illegitimate. The old rule compared the two
  // disclosures and kept the agreeing ones, so two identical units rendered a confident row for a
  // response the parse was refusing every claim from.
  const alike: [string, unknown[]][] = [
    ["byte-identical", [unit("lu-legilux"), unit("lu-legilux")]],
    ["one field apart", [unit("lu-legilux"),
      unit("lu-legilux", { timeline_semantics: "official_consolidation_state" })]],
    ["a field present on one only", [unit("lu-legilux"),
      unit("lu-legilux", { freshness: { built_at: "2026-08-15T09:01:06Z",
                                        stamp_signature_valid: true } })]],
    ["both failing closed alike", [
      unit("lu-legilux", { freshness: { built_at: "tomorrow" }, artifact: { code_commit: null } }),
      unit("lu-legilux", { freshness: { built_at: 20260815 },
                           artifact: { code_commit: "x".repeat(129) } })]],
    ["three units, two agreeing", [unit("lu-legilux"), unit("lu-legilux"),
      unit("lu-legilux", { freshness: { built_at: "2026-08-08T00:00:00Z" } })]],
  ];
  for (const [label, entries] of alike) {
    const rows = stripOf(entries);
    assert.equal(rows.length, 1, label);
    assert.deepEqual(rows[0], UNESTABLISHED, `a repeated publisher kept an identity: ${label}`);
  }
});

test("a repeated publisher among the units is refused, never collapsed", () => {
  // The parse admits at most one unit per publisher, so this branch is unreachable THROUGH it
  // and is exercised directly instead. It is kept for the reason `queriedDenominator` keeps
  // its own: the function is exported, a caller can concatenate two lists, and one index
  // counted twice is a disclosure nobody made. An untested guard is a comment.
  const parsed = parseGovernedResponse("search", [unit("lu-legilux")]);
  assert.equal(parsed.units.length, 1);
  const doubled: GovernedResponse = {
    ...parsed, units: [...parsed.units, ...parsed.units],
  };
  assert.deepEqual(envelopeStripRows(doubled), [UNESTABLISHED],
    "one index counted twice kept an identity nobody disclosed twice");
});

test("one publisher's conflict does not disturb another publisher's row", () => {
  const rows = stripOf([
    ...conflictPair(),
    unit("eu-eurlex", { freshness: { built_at: "2026-08-08T00:00:00Z",
                                     corpus_commit: "aa11bb22", stamp_signature_valid: true } }),
  ]);
  // Ordinal sort survives the conflict handling, so the strip renders identically between runs.
  assert.deepEqual(rows.map((r) => r.publisher), ["eu-eurlex", "lu-legilux"]);
  assert.equal(rows[0].builtAt, "2026-08-08T00:00:00Z");
  assert.equal(rows[0].corpusCommit, "aa11bb22");
  assert.equal(rows[0].signatureValid, true);
  assert.deepEqual(rows[1], UNESTABLISHED);
});

// Publisher identity attacks. The publisher is the row KEY, so a validator that trims or case-
// folds it does not merely admit a bad value: it decides which rows share one disclosure. The
// strip used to validate it through `str`, which trims, while the population footer and the
// limitation list refused the padded form outright. It reads the parse's identity now, which is
// the single non-normalizing validator all three share. See publisherIdentity.ts.

test("a padded publisher is refused, never trimmed into the real identity", () => {
  // Under a trimming validator the padded entry becomes a SECOND unit for lu-legilux, and because
  // its corpus commit differs it conflicts and blanks a row that no envelope disputed.
  const rows = stripOf([
    unit("lu-legilux"),
    unit(" lu-legilux ", { freshness: { built_at: "2026-08-08T00:00:00Z",
                                        corpus_commit: "0000ffff",
                                        stamp_signature_valid: false } }),
  ]);
  assert.deepEqual(rows.map((r) => r.publisher), ["lu-legilux"],
    "the padded spelling became a row of its own, or displaced the real one");
  assert.equal(rows[0].builtAt, "2026-08-15T09:01:06Z",
    "a padded entry was trimmed into this publisher and conflicted its identity away");
  assert.equal(rows[0].corpusCommit, "e9c4df09");
  assert.equal(rows[0].signatureValid, true);
});

test("a case alias is refused, never folded into the lower-case identity", () => {
  // Producer registry identities are ordinal lower-case, so LU-Legilux is a spelling the
  // producer cannot mint. Folding it conflicts the real row away; admitting it as its own key
  // splits one publisher into two rows of the strip.
  const rows = stripOf([
    unit("lu-legilux"),
    unit("LU-Legilux", { freshness: { built_at: "2026-08-08T00:00:00Z",
                                      corpus_commit: "0000ffff",
                                      stamp_signature_valid: false } }),
  ]);
  assert.deepEqual(rows.map((r) => r.publisher), ["lu-legilux"]);
  assert.equal(rows[0].builtAt, "2026-08-15T09:01:06Z");
  assert.equal(rows[0].corpusCommit, "e9c4df09");
});

test("the strip refuses every publisher spelling the producer cannot mint", () => {
  // The strip previously bounded this field at 128, the length it uses for commit hashes. The
  // producer's own publisher bound is 64 (UiEffects.Identifier, MaximumShortLength), and "?" is
  // IndexReader.Collection's missing-stamp sentinel, not a publisher.
  for (const bad of [" lu-legilux ", "lu-legilux ", "LU-LEGILUX", "lu-legiluX", "?",
                     "lu legilux", "x".repeat(65), 7, null, {}]) {
    assert.deepEqual(stripOf([unit(bad)]), [],
      `rendered a row for publisher ${JSON.stringify(bad)}`);
  }
  assert.equal(stripOf([unit("x".repeat(64))]).length, 1,
    "the producer's own bound must remain renderable");
});

// ---------------------------------------------------------------------------
// The law and timeline surfaces (trust rule 4)
// ---------------------------------------------------------------------------
//
// These are the two most-read screens in the product and they disclosed no index identity at
// all: every law-path call to `setStrip` cleared it and none filled it. The evidence was never
// missing. `Envelope` (McpCore 223) is the single mint site for every envelope this server
// sends and stamps `freshness.built_at`, `corpus_commit` and `stamp_signature_valid` on every
// path it builds, `as_of` (947, 970, 978, 1052 and the 597 mutation) and `timeline` (1075,
// 1080) included. The client read none of it.
//
// THROUGH THE SAME DOOR AS EVERYTHING ELSE. Every fixture below goes to `governedStripRows`,
// which is `parseGovernedResponse` plus `partitionOf` and adds nothing: one validator, one
// admission rule, one strip projection. What these pin is that the two new schema rows admit
// exactly what the producer can emit, and that everything else reaches the reader as an absent
// identity rather than as a confident one.

const FRESH = {
  built_at: "2026-08-15T09:22:08Z",
  corpus_commit: "e9c4df09",
  stamp_signature_valid: true,
};
const ARTIFACT = { code_commit: "abc123", manifest_set_id: "m-1", content_digest: "d-1" };

const LAW_ENVELOPE = {
  publisher: "lu-legilux",
  status: "ok",
  timeline_semantics: "publisher_applicability",
  freshness: FRESH,
  artifact: ARTIFACT,
  // The producer stamps this on every envelope it builds. It is carried in the fixtures because
  // real responses carry it, and it is deliberately NOT a strip field: see the key-set test.
  provisional: false,
};

const RAIL_ENVELOPE = {
  publisher: "eu-eurlex",
  status: "ok",
  timeline_semantics: "official_consolidation_state",
  freshness: FRESH,
  artifact: ARTIFACT,
  provisional: false,
};

/**
 * An `as_of` answer the producer can emit. Entry fields and envelope fields are overridden
 * separately, so an envelope override cannot silently replace the whole envelope.
 */
const asOfUnit = (
  over: Record<string, unknown> = {},
  env: Record<string, unknown> = {},
) => ({
  document: { title: "Code penal", valid_from: "2024-01-01" },
  // The producer's real shape, response-size facts included. `total_provisions` and `truncated`
  // are about ONE document's provisions rather than a corpus page (McpCore 992-993, 1033-1034),
  // and the schema reads neither as paging.
  total_provisions: 1,
  truncated: false,
  provisions: [{ anchor: "art_1", heading: "Article 1", text: "..." }],
  ...over,
  envelope: { ...LAW_ENVELOPE, ...env },
});

/** A `timeline` answer the producer can emit: three versions, so the status must be `ok`. */
const timelineUnit = (
  over: Record<string, unknown> = {},
  env: Record<string, unknown> = {},
) => ({
  work: "eu-eurlex:32016R0679",
  total_count: 3,
  truncated: false,
  versions: [{ valid_from: "2016-05-04", language: "en" }],
  ...over,
  envelope: { ...RAIL_ENVELOPE, ...env },
});

test("a law answer discloses the index that answered it", () => {
  // The row this whole change exists for: it did not exist, so the most-read screen in the
  // product stated no build date and no signature verdict for the index it was reading from.
  // The response is a single object rather than a list, because `as_of` answers from one reader.
  const [row, ...rest] = governedStripRows("as_of", asOfUnit());
  assert.deepEqual(rest, [], "one reader answered, so there is exactly one identity to state");
  assert.equal(row.publisher, "lu-legilux");
  assert.equal(row.builtAt, "2026-08-15T09:22:08Z");
  assert.equal(row.signatureValid, true);
  assert.equal(row.corpusCommit, "e9c4df09");
  assert.equal(row.codeCommit, "abc123");
  assert.equal(row.manifestSetId, "m-1");
  assert.equal(row.contentDigest, "d-1");
  assert.equal(row.timelineSemantics, "publisher_applicability");
  assert.equal(indexFreshnessLabel(row.builtAt), "index built 2026-08-15T09:22:08Z");
});

test("the version rail discloses the index that answered it", () => {
  const [row, ...rest] = governedStripRows("timeline", timelineUnit());
  assert.deepEqual(rest, []);
  assert.equal(row.publisher, "eu-eurlex");
  assert.equal(row.builtAt, "2026-08-15T09:22:08Z");
  assert.equal(row.signatureValid, true);
  assert.equal(row.timelineSemantics, "official_consolidation_state");
});

test("a strip row states index identity only, never a fact about the question", () => {
  // EVERY FIELD HERE IS A PROPERTY OF THE INDEX, and the law surface is why that has to stay
  // true. Two effects write this strip for one work, `as_of` on the reading key and `timeline`
  // on the work key, and both answer from the reader `Resolve` returned, so they agree on all
  // eight values however they interleave.
  //
  // The envelope's `provisional` flag is the field this excludes on purpose. `ProvisionalFor`
  // (McpCore 511-515) compares the REQUEST to the build date, and the two calls compare
  // different things: `as_of` uses the date being read (978), `timeline` the version dates it
  // found (1076-1077). On a per-index row that value would be decided by whichever response
  // landed last. A provisional answer still has to be disclosed; beside the answer, not here.
  const [row] = governedStripRows("as_of", asOfUnit());
  assert.deepEqual(Object.keys(row).sort(), [
    "builtAt", "codeCommit", "contentDigest", "corpusCommit",
    "manifestSetId", "publisher", "signatureValid", "timelineSemantics",
  ]);
});

test("every envelope status the law tools can emit is admitted", () => {
  // Derived from the producer, not from the happy path. `as_of` reaches an envelope from four
  // call sites and one mutation, and admitting fewer statuses than it can emit would blank the
  // strip on exactly the answers a reader most needs to situate: no version for that date, an
  // unknown work, withheld text, an anchor this version does not have.
  for (const status of ["ok", "text_withheld", "text_not_available", "anchor_not_in_version",
    "no_version_for_date", "unknown_work", "ambiguous_version"]) {
    const rows = governedStripRows("as_of", asOfUnit({}, { status }));
    assert.equal(rows.length, 1, `as_of status ${status} disclosed no index`);
    assert.equal(rows[0].builtAt, "2026-08-15T09:22:08Z",
      `as_of status ${status} lost its build date`);
  }
});

test("a timeline answer that found no versions still discloses its index", () => {
  // McpCore 1075 returns `unknown_work` with an envelope, a `work` and nothing else: no
  // `total_count` and no `versions`. Requiring either would refuse the producer's own answer and
  // take the identity off the version rail, which is the disclosure this change exists to add.
  const [row, ...rest] = governedStripRows("timeline", {
    work: "eu-eurlex:absent",
    envelope: { ...RAIL_ENVELOPE, status: "unknown_work" },
  });
  assert.deepEqual(rest, []);
  assert.equal(row.publisher, "eu-eurlex");
  assert.equal(row.builtAt, "2026-08-15T09:22:08Z");
});

test("a timeline status that contradicts its own count is refused", () => {
  // McpCore 1075 is `if (total == 0) return ... UnknownWork` and every other return of that case
  // is `Ok`, so the status and the count determine each other exactly. Both directions are
  // shapes the producer cannot emit, and either would have carried a confident build date.
  assert.deepEqual(governedStripRows("timeline", timelineUnit({ total_count: 0 })), [],
    "an ok timeline reporting no versions authorized an identity");
  assert.deepEqual(
    governedStripRows("timeline",
      timelineUnit({ total_count: 3 }, { status: "unknown_work" })),
    [],
    "an unknown_work timeline reporting versions authorized an identity");
});

test("a law envelope carrying a population states no identity rather than a confident one", () => {
  // `as_of` publishes no population on any path: `SearchPopulation` is reached only from search,
  // `PopulationTotal` only from changes_in_period and `Coverage(1).Groups` only from
  // in_force_on. One arriving is not the producer's answer, so the scope is unreadable, the unit
  // is invalidated whole, and the publisher is named with every field absent. Dropping the row
  // instead would read as "not mounted", which is a different and false statement.
  const rows = governedStripRows("as_of", asOfUnit({
    population: { basis: "selected_metadata_scope", works_in_scope: 12, known_exclusions: [] },
  }));
  assert.equal(rows.length, 1);
  assert.equal(rows[0].publisher, "lu-legilux");
  assert.equal(rows[0].builtAt, undefined, "a rejected unit minted a build date");
  assert.equal(rows[0].signatureValid, undefined, "a rejected unit minted a signature verdict");
  assert.equal(rows[0].corpusCommit, undefined);
  assert.equal(rows[0].timelineSemantics, undefined);
  assert.equal(indexFreshnessLabel(rows[0].builtAt), "index build date unavailable");
});

test("a law envelope carrying a receipt its producer never stamps authorizes no row", () => {
  // `MarkPublisherSet` (McpCore 701-712) and `MarkResponseRows` (713-725) are reached only from
  // search (1575-1576), in_force_on (1170-1171) and changes_in_period (1865-1882). A receipt on
  // a law answer did not come from this producer, and a well-formed one is the dangerous case:
  // it would otherwise be stored as evidence about a response that never carried it.
  for (const receipt of [
    { publisher_result_set: { total: 2, returned: 2, maximum: 8, truncated: false } },
    { response_row_set: { maximum: 8, returned: 1, truncated: false } },
  ]) {
    const name = Object.keys(receipt)[0];
    assert.deepEqual(governedStripRows("as_of", asOfUnit(receipt)), [],
      `a forged ${name} authorized an as_of strip row`);
    assert.deepEqual(governedStripRows("timeline", timelineUnit(receipt)), [],
      `a forged ${name} authorized a timeline strip row`);
  }
});

test("a law envelope outside the producer's status set authorizes nothing", () => {
  // Not the identity-unavailable row. An entry whose status this tool cannot emit never
  // established that this publisher answered the question at all, and `no_changes_in_period` and
  // `no_result` are other tools' statuses entirely.
  for (const status of ["no_changes_in_period", "no_result", "ok ", "OK", ""]) {
    assert.deepEqual(governedStripRows("as_of", asOfUnit({}, { status })), [],
      `as_of admitted the status ${JSON.stringify(status)}`);
  }
});

test("two law answers for one publisher state no identity rather than the first one", () => {
  // `Resolve` returns at most one reader, so one call is one envelope and a second is a shape
  // this producer cannot emit whatever it says. Two identities for one index establish neither,
  // and keeping the readable one would be the product choosing a side of an incoherence.
  const rows = governedStripRows("as_of", [
    asOfUnit(),
    asOfUnit({}, { freshness: { ...FRESH, built_at: "2026-01-01T00:00:00Z" } }),
  ]);
  assert.equal(rows.length, 1);
  assert.equal(rows[0].publisher, "lu-legilux");
  assert.equal(rows[0].builtAt, undefined, "a conflicted publisher kept a build date");
  assert.equal(rows[0].signatureValid, undefined);
});

test("a law answer from a server holding no law states no index identity", () => {
  // The terminal object is global and carries no envelope, so there is no index to name. It also
  // names the operation it answered (McpCore 901), and a terminal object for another tool may
  // not speak here: an absence asserted about the wrong subject is the worst failure this
  // surface has.
  const terminal = (tool: string) => ({
    status: "no_corpus_mounted", detail: "no verified indexes", tool_called: tool,
  });
  assert.deepEqual(governedStripRows("as_of", terminal("as_of")), []);
  assert.deepEqual(governedStripRows("as_of", terminal("search")), []);
  assert.equal(parseGovernedResponse("as_of", terminal("as_of")).noCorpus, true);
  assert.equal(parseGovernedResponse("as_of", terminal("search")).noCorpus, false,
    "a terminal object for another tool was accepted as this tool's answer");
  // An envelope smuggled into the terminal object asserts a mounted index in the same breath as
  // claiming nothing is mounted.
  assert.equal(
    parseGovernedResponse("as_of",
      { ...terminal("as_of"), envelope: LAW_ENVELOPE }).noCorpus,
    false);
});

test("a law entry with no envelope discloses nothing, which is the honest answer", () => {
  // McpCore 936 and 1071 return a bare `{ status: unknown_work }` when `Resolve` found no reader
  // at all, and `UnmountedFilterResult` (263-278) a bare `{ status: unknown_publisher }`. No
  // reader was selected, so there is no index identity in existence to state.
  assert.deepEqual(governedStripRows("as_of",
    { status: "unknown_work", work: "lu-legilux:nope" }), []);
  assert.deepEqual(governedStripRows("timeline",
    { status: "unknown_publisher", tool_called: "timeline", requested_filter: "publisher" }), []);
});

test("an empty law answer never authorizes a sentence about the corpus", () => {
  // THE FIELD IT WOULD HAVE BEEN WRONG TO LEAVE UNDEFINED. Under the previous shape an absent
  // absence total carried search's reading, "decide absence from the fused hit count", so a law
  // row that wrote nothing would have inherited it and let an empty outline print `Nothing in
  // the corpus matches that.` about a corpus `as_of` never measured. Both law tools ask one
  // reader about one work and publish no denominator, so neither can speak for the corpus.
  for (const tool of ["as_of", "timeline"]) {
    const raw = tool === "as_of" ? asOfUnit({ provisions: [] }) : timelineUnit();
    const projection = projectGovernedEmptiness(tool, raw, 0);
    assert.equal(projection.partition.absenceAuthority, "no_authority");
    assert.notEqual(projection.empty, "none_matched", `${tool} claimed the corpus holds nothing`);
    assert.notEqual(projection.empty, "mixed_no_match");
    assert.equal(projection.empty, "incomplete_response");
  }
  // BOTH DOORS, not one. `searchAbsenceState` reaches the same two corpus sentences by a
  // different route and had the same gap: it takes a partition, so any caller holding one of a
  // law response could have asked it to speak for the corpus.
  for (const tool of ["as_of", "timeline"] as const) {
    const raw = tool === "as_of" ? asOfUnit({ provisions: [] }) : timelineUnit();
    const state = searchAbsenceState(partitionGovernedResponse(tool, [raw]), 0);
    assert.notEqual(state, "no_match", `${tool} claimed the corpus holds nothing`);
    assert.notEqual(state, "mixed_no_match");
    assert.equal(state, "incomplete_response");
  }
  // The three governed tools keep the authority they had, read through the same field, so this
  // fails if the new rule is applied more widely than where it belongs.
  assert.equal(
    partitionGovernedResponse("search", [unit("lu-legilux")]).absenceAuthority, "corpus_scope");
  assert.equal(
    projectGovernedEmptiness("search", [unit("lu-legilux")], 0).empty, "none_matched");
  assert.equal(
    searchAbsenceState(partitionGovernedResponse("search", [unit("lu-legilux")]), 0), "no_match");
});

test("no limitation may claim a law tool refused a filter", () => {
  // `UnsupportedFilterResult` is called from exactly three sites (McpCore 1126, 1362, 1775) and
  // neither law tool reaches any of them, so a refusal naming one is a claim the producer cannot
  // make. The tool gate is derived from each schema's own refusal scope rule rather than from
  // the set of governed tools, so growing the table does not widen what the assistant's additive
  // field accepts.
  const limitation = (tool: string) => ({
    status: "filter_not_supported_by_index",
    tool,
    publisher: "lu-legilux",
    unsupported_filters: ["domain"],
  });
  assert.equal(validateLimitation(limitation("as_of")), null);
  assert.equal(validateLimitation(limitation("timeline")), null);
  for (const tool of ["search", "changes_in_period", "in_force_on"]) {
    assert.equal(validateLimitation(limitation(tool))?.tool, tool,
      `${tool} lost its refusal contract`);
  }
});

test("the law surface reaches the strip through the door the governed pages use", () => {
  // ONE AUTHORITY, NOT TWO (O1). `governedStripRows` has to be the existing parse and partition
  // and nothing else: if it ever grows an admission rule of its own, the surface that states a
  // build date and a signature verdict is again answering from input the parser refused.
  const raw = asOfUnit();
  assert.deepEqual(governedStripRows("as_of", raw),
    partitionGovernedResponse("as_of", [raw]).stripRows);
  assert.deepEqual(governedStripRows("as_of", raw),
    envelopeStripRows(parseGovernedResponse("as_of", raw)));
  // The refused cases have to agree too, or the two doors disagree exactly where it matters.
  const forged = asOfUnit({}, { status: "no_result" });
  assert.deepEqual(governedStripRows("as_of", forged),
    partitionGovernedResponse("as_of", [forged]).stripRows);
});

test("the law and timeline surfaces feed the strip from the one parse", () => {
  // STRUCTURAL, on the precedent of the Search.tsx guard in limitations.test.ts, because no node
  // test can import a .tsx component and App.tsx is where the defect actually lived: eight calls
  // to `setStrip` and only two of them filling it, none on a law path. Without this, deleting
  // either call site again kills nothing in the suite. Comments are stripped first, so a
  // sentence explaining the defect is not a reintroduction of it.
  const source = readFileSync(new URL("./App.tsx", import.meta.url), "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/^[ \t]*\/\/.*$/gm, "");
  assert.ok(source.includes("governedStripRows(\"as_of\", res)"),
    "the law surface stopped disclosing which index answered it");
  assert.ok(source.includes("governedStripRows(\"timeline\", res)"),
    "the version rail stopped disclosing which index answered it");
  // Each call names the tool whose response it is parsing. A law answer parsed under another
  // tool's schema classifies every unit as invalid and blanks the strip, silently and with no
  // error anywhere: the reader simply stops being told which index answered.
  assert.ok(!/governedStripRows\("(search|changes_in_period|in_force_on)"/.test(source),
    "a law response is being parsed under another tool's schema");
  // And nothing on this surface reaches past the parse for a trust claim, which is the whole of
  // O1. The strip's own fields must never be read off a raw response again.
  assert.ok(!source.includes("envelopeStripRows("),
    "App.tsx reached past the parse to build strip rows itself");
  for (const field of ["freshness", "built_at", "stamp_signature_valid", "corpus_commit"]) {
    assert.ok(!source.includes(field),
      `App.tsx reads the envelope field ${field} directly instead of through the parse`);
  }
});
