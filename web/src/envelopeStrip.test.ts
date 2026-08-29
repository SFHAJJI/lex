import assert from "node:assert/strict";
import test from "node:test";
import { envelopeStripRows, indexFreshnessLabel } from "./envelopeStrip.ts";
import {
  parseGovernedResponse, partitionGovernedResponse, type GovernedResponse,
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
