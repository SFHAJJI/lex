import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import {
  boundedPublisher, contributesToQueryPopulation, normalizeSearchResponse, POPULATION_BOUNDS,
  populationExclusions, queriedDenominator, queriedPopulationTotal, searchPopulations,
  unqueriedPopulations, validateSearchPopulation,
} from "./searchPopulation.ts";
import type { NormalizedSearchResponse, PublisherPopulation } from "./searchPopulation.ts";
import { classifyEnvelope, partitionGovernedResponse } from "./limitations.ts";

/**
 * Binds the shipped population validator to the jointly accepted contract file, and binds that
 * file to the status contract, so no side hand-writes the rule.
 *
 * The deviations below are generated from the contract rather than enumerated by hand. Every
 * previous round of this work failed the same way: a predicate written from a partial reading of
 * the producer passed its own hand-picked cases. A test that enumerates cases inherits exactly
 * the blind spot of whoever wrote the list.
 */

const url = (p: string) => new URL(p, import.meta.url);
const contract = JSON.parse(
  readFileSync(url("../../tests/Lex.Tests/search-population-contract.json"), "utf8"));
const statusContract = JSON.parse(
  readFileSync(url("../../tests/Lex.Tests/governed-status-contract.json"), "utf8"));

const coherent = (status: string) => ({
  ...contract.statuses[status],
  works_in_scope: 1250,
  known_exclusions: ["never-consolidated acts"],
});

test("the population contract covers exactly the statuses search can emit", () => {
  // A new search status must arrive with a population rule, or this fails before any screen does.
  assert.deepEqual(
    Object.keys(contract.statuses).sort(),
    Object.keys(statusContract.tools.search).sort());
});

test("the shipped bounds match the contract", () => {
  assert.equal(POPULATION_BOUNDS.maxExclusions, contract.bounds.max_exclusions);
  assert.equal(POPULATION_BOUNDS.maxExclusionLength, contract.bounds.max_exclusion_length);
});

test("every declared status accepts its own coherent population", () => {
  for (const status of Object.keys(contract.statuses)) {
    const verdict = validateSearchPopulation(status, coherent(status));
    assert.equal(verdict.valid, true, `${status} rejected its own contract tuple`);
  }
});

test("every single-field deviation from a declared status is rejected", () => {
  // Generated: for each status, flip each coherence field to every other declared value and
  // require refusal. This is the check that catches a validator agreeing with only some rows.
  const bases = [...new Set(Object.values(contract.statuses).map((s: any) => s.basis))];
  let checked = 0;
  for (const status of Object.keys(contract.statuses)) {
    const good = coherent(status) as any;
    for (const basis of bases) {
      if (basis === good.basis) continue;
      const v = validateSearchPopulation(status, { ...good, basis });
      assert.equal(v.valid, false, `${status} accepted foreign basis ${basis}`);
      checked++;
    }
    for (const field of ["scope_filters_applied", "query_ran"]) {
      const v = validateSearchPopulation(status, { ...good, [field]: !good[field] });
      assert.equal(v.valid, false, `${status} accepted flipped ${field}`);
      checked++;
    }
  }
  assert.ok(checked >= 9, `expected a real matrix, only checked ${checked}`);
});

test("a denominator that is not a non-negative safe integer is refused", () => {
  for (const works of [-1, 1.5, Number.NaN, Number.POSITIVE_INFINITY,
                       Number.MAX_SAFE_INTEGER + 1, "1250", null, undefined]) {
    const v = validateSearchPopulation("ok", { ...coherent("ok"), works_in_scope: works });
    assert.equal(v.valid, false, `accepted works_in_scope ${String(works)}`);
  }
  assert.equal(validateSearchPopulation("ok", { ...coherent("ok"), works_in_scope: 0 }).valid, true);
});

test("a missing or non-object population is refused, never defaulted", () => {
  for (const raw of [undefined, null, [], "population", 0]) {
    assert.equal(validateSearchPopulation("ok", raw).valid, false, `accepted ${String(raw)}`);
  }
});

test("a status with no population rule is refused rather than guessed", () => {
  assert.equal(validateSearchPopulation("no_result", coherent("ok")).valid, false);
  assert.equal(validateSearchPopulation(undefined, coherent("ok")).valid, false);
});

test("known exclusions are bounded, de-duplicated, and never interpreted", () => {
  const ok = validateSearchPopulation("ok",
    { ...coherent("ok"), known_exclusions: ["a", "a", " a ", "b", ""] });
  assert.equal(ok.valid, true);
  assert.deepEqual(ok.valid && ok.population.known_exclusions, ["a", "b"]);

  const single = validateSearchPopulation("ok", { ...coherent("ok"), known_exclusions: "just one" });
  assert.deepEqual(single.valid && single.population.known_exclusions, ["just one"]);

  for (const bad of [
    Array(POPULATION_BOUNDS.maxExclusions + 1).fill("x"),
    ["x".repeat(POPULATION_BOUNDS.maxExclusionLength + 1)],
    [1],
    [null],
    { note: "not a list" },
  ]) {
    assert.equal(validateSearchPopulation("ok", { ...coherent("ok"), known_exclusions: bad }).valid,
      false, `accepted exclusions ${JSON.stringify(bad)}`);
  }
});

test("only a publisher that ran the query contributes to a query population", () => {
  assert.equal(contributesToQueryPopulation(validateSearchPopulation("ok", coherent("ok"))), true);
  for (const status of ["retrieval_mode_unavailable", "filter_not_supported_by_index"]) {
    assert.equal(
      contributesToQueryPopulation(validateSearchPopulation(status, coherent(status))), false,
      `${status} contributed its scope to a query-ran claim`);
  }
  assert.equal(contributesToQueryPopulation({ valid: false, reason: "x" }), false);
});

// The browser contract's rendering rules, exercised against the shipped classifier rather than a
// stand-in, so a publisher can never count as having answered here while being withheld there.

const entry = (publisher: string, status: string, over: Record<string, unknown> = {}) => ({
  envelope: { status, publisher },
  retrieval_mode: "keyword",
  hits: [],
  population: { ...contract.statuses[status], works_in_scope: 100,
                known_exclusions: [`${publisher} gap`] },
  ...over,
});

test("two publishers that ran both contribute to the queried denominator", () => {
  const rows = searchPopulations(
    [entry("lu-legilux", "ok", { population: { ...contract.statuses.ok, works_in_scope: 1250,
                                               known_exclusions: ["a"] } }),
     entry("eu-eurlex", "ok", { population: { ...contract.statuses.ok, works_in_scope: 300,
                                              known_exclusions: ["b"] } })],
    classifyEnvelope);
  assert.equal(rows.length, 2);
  assert.equal(queriedPopulationTotal(rows), 1550);
  assert.deepEqual(populationExclusions(rows), ["a", "b"]);
});

test("a refused publisher discloses its scope but never joins the queried denominator", () => {
  const rows = searchPopulations([
    entry("lu-legilux", "ok"),
    { ...entry("eu-eurlex", "filter_not_supported_by_index"), unsupported_filters: ["domain"] },
  ], classifyEnvelope);
  assert.equal(queriedPopulationTotal(rows), 100, "the refused scope was added to the query claim");
  const unqueried = unqueriedPopulations(rows);
  assert.equal(unqueried.length, 1);
  assert.equal(unqueried[0].publisher, "eu-eurlex");
  assert.equal(unqueried[0].population.basis, "mounted_scope_before_unsupported_filters");
});

test("an all-refused response yields no queried denominator at all", () => {
  const rows = searchPopulations([
    { ...entry("lu-legilux", "filter_not_supported_by_index"), unsupported_filters: ["domain"] },
  ], classifyEnvelope);
  // Not zero. Zero would assert an empty corpus was searched, which is exactly rule N3.
  assert.equal(queriedPopulationTotal(rows), undefined);
  assert.equal(unqueriedPopulations(rows).length, 1);
});

test("a retrieval-mode refusal discloses the selected scope and states it did not run", () => {
  const rows = searchPopulations([
    entry("lu-legilux", "retrieval_mode_unavailable",
      { retrieval_mode: undefined, requested_retrieval_mode: "hybrid" }),
  ], classifyEnvelope);
  assert.equal(rows.length, 1);
  assert.equal(rows[0].population.query_ran, false);
  assert.equal(rows[0].population.basis, "selected_metadata_scope");
  assert.equal(queriedPopulationTotal(rows), undefined);
});

test("an invalid sibling contributes no population fact", () => {
  const rows = searchPopulations([
    entry("lu-legilux", "ok"),
    // Incoherent: claims success while saying the query never ran.
    entry("eu-eurlex", "ok", { population: { ...contract.statuses.ok, query_ran: false,
                                             works_in_scope: 999, known_exclusions: [] } }),
  ], classifyEnvelope);
  assert.equal(rows.length, 1, "an incoherent population was rendered");
  assert.equal(queriedPopulationTotal(rows), 100);
});

test("one publisher repeated across entries is counted once", () => {
  const rows = searchPopulations([entry("lu-legilux", "ok"), entry("lu-legilux", "ok")],
    classifyEnvelope);
  assert.equal(rows.length, 1);
  assert.equal(queriedPopulationTotal(rows), 100);
});

test("an envelope the classifier calls invalid contributes nothing, even with a valid population", () => {
  // Distinct from the incoherent-population case above: here the population is perfectly well
  // formed and the ENVELOPE is invalid. A refusal naming no recognized filter classifies invalid,
  // and an invalid envelope authorizes neither rows nor a denominator. Added after a mutation
  // that removed the classification guard killed no test, which meant this was unprotected.
  const invalidSibling = {
    envelope: { status: "filter_not_supported_by_index", publisher: "eu-eurlex" },
    unsupported_filters: ["not_a_governed_filter"],
    population: { ...contract.statuses.filter_not_supported_by_index, works_in_scope: 999,
                  known_exclusions: ["should not appear"] },
  };
  assert.equal(classifyEnvelope("search", invalidSibling).kind, "invalid",
    "the fixture no longer classifies invalid; this test would prove nothing");
  const rows = searchPopulations([entry("lu-legilux", "ok"), invalidSibling], classifyEnvelope);
  assert.equal(rows.length, 1);
  assert.equal(queriedPopulationTotal(rows), 100);
  assert.deepEqual(populationExclusions(rows), ["lu-legilux gap"]);
});

// Repairs from Codex's exact-head pre-freeze attack on 0d8632f.

test("a population with no named publisher is not shown at all", () => {
  // A denominator nobody is named for cannot be attributed, checked or corrected. Rule 6 asks
  // whose population this is, not only how large it is.
  const anonymous = {
    envelope: { status: "ok" },
    retrieval_mode: "keyword",
    hits: [],
    population: { ...contract.statuses.ok, works_in_scope: 500, known_exclusions: [] },
  };
  assert.deepEqual(searchPopulations([anonymous], classifyEnvelope), []);
  const mixed = searchPopulations([entry("lu-legilux", "ok"), anonymous], classifyEnvelope);
  assert.equal(mixed.length, 1);
  assert.equal(queriedPopulationTotal(mixed), 100, "an unattributed scope reached the denominator");
});

test("two entries for one publisher that disagree drop that publisher entirely", () => {
  // Keeping the first would let arrival order decide what the reader is told.
  const rows = searchPopulations([
    entry("lu-legilux", "ok"),
    entry("lu-legilux", "ok", { population: { ...contract.statuses.ok, works_in_scope: 7,
                                              known_exclusions: [] } }),
    entry("eu-eurlex", "ok"),
  ], classifyEnvelope);
  assert.deepEqual(rows.map((r) => r.publisher), ["eu-eurlex"]);
  assert.equal(queriedPopulationTotal(rows), 100);
});

test("two identical entries for one publisher are simply de-duplicated", () => {
  const rows = searchPopulations([entry("lu-legilux", "ok"), entry("lu-legilux", "ok")],
    classifyEnvelope);
  assert.equal(rows.length, 1);
  assert.equal(queriedPopulationTotal(rows), 100);
});


// O2: rows, population and absence authority consume ONE normalized response set.
//
// The defect this section closes is not a wrong number. It is a screen that renders a publisher's
// hits while the denominator behind them was refused, or that reports 100 or 999 depending on
// which of two contradictory entries arrived first. Every test below was watched failing against
// a deliberately broken copy of the module before it was kept.

/** A ran envelope with real hits, so withholding rows is observable and not merely asserted. */
const ranEntry = (publisher: string, works: number, exclusions: string[] = ["a"], hits = 1) => ({
  envelope: { status: "ok", publisher },
  retrieval_mode: "keyword",
  hits: Array.from({ length: hits }, (_, i) => ({ lex_id: `${publisher}:w${i}`, title: "t" })),
  population: { ...contract.statuses.ok, works_in_scope: works, known_exclusions: exclusions },
});

// The two accessors the Search.tsx writer codes against: the entry list is named differently in
// the two branches on purpose, so reaching the rows of an incomplete response cannot be done
// without reading `complete` first.
const entriesOf = (n: NormalizedSearchResponse): unknown[] =>
  n.complete ? n.entries : n.entriesAfterWithholding;
const withheldOf = (n: NormalizedSearchResponse): string[] =>
  n.complete ? [] : n.withheldPublishers;
const lexIdsOf = (entries: unknown[]): string[] =>
  partitionGovernedResponse("search", entries).ran
    .flatMap((e: any) => (Array.isArray(e.hits) ? e.hits : []).map((h: any) => String(h.lex_id)));

test("a coherent response is complete and reaches the projector entry for entry", () => {
  const lu = ranEntry("lu-legilux", 100, ["a"]);
  const eu = ranEntry("eu-eurlex", 42, ["b"]);
  const normalized = normalizeSearchResponse([lu, eu], classifyEnvelope);
  assert.equal(normalized.complete, true);
  assert.deepEqual(entriesOf(normalized), [lu, eu]);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["lu-legilux", "eu-eurlex"]);
  assert.equal(queriedPopulationTotal(normalized.populations), 142);
  assert.deepEqual(lexIdsOf(entriesOf(normalized)), ["lu-legilux:w0", "eu-eurlex:w0"]);
});

test("reversing the arrival order of two conflicting entries changes nothing", () => {
  // The O2 defect verbatim: two individually valid entries naming one publisher and reporting 100
  // and 999 must not produce 100 or 999 by arrival order. Neither survives.
  const hundred = ranEntry("lu-legilux", 100);
  const nineHundred = ranEntry("lu-legilux", 999);
  const other = ranEntry("eu-eurlex", 42, ["b"]);
  const forward = normalizeSearchResponse([hundred, nineHundred, other], classifyEnvelope);
  const reverse = normalizeSearchResponse([nineHundred, hundred, other], classifyEnvelope);

  assert.deepEqual(forward.populations, reverse.populations);
  assert.deepEqual(withheldOf(forward), withheldOf(reverse));
  assert.deepEqual(entriesOf(forward), entriesOf(reverse));
  assert.deepEqual(forward.populations.map((r) => r.publisher), ["eu-eurlex"]);
  assert.equal(queriedPopulationTotal(forward.populations), 42);
  assert.equal(queriedPopulationTotal(reverse.populations), 42);
  assert.deepEqual(searchPopulations([hundred, nineHundred, other], classifyEnvelope),
    searchPopulations([nineHundred, hundred, other], classifyEnvelope));
});

test("a conflicting duplicate withholds that publisher's rows, not only its population", () => {
  // Dropping the population row while still rendering the hits is the repair the reviewer ruled
  // out: the reader is then shown a publisher's results with no denominator to check them by.
  const normalized = normalizeSearchResponse([
    ranEntry("lu-legilux", 100),
    ranEntry("lu-legilux", 999),
    ranEntry("eu-eurlex", 42, ["b"]),
  ], classifyEnvelope);
  assert.equal(normalized.complete, false);
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"]);
  assert.deepEqual(lexIdsOf(entriesOf(normalized)), ["eu-eurlex:w0"],
    "hits rendered for a publisher whose denominator was refused");
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex"]);
});

test("two entries agreeing on the count but not on the exclusions conflict", () => {
  // Same denominator, different statements about what it leaves out. That is a disagreement about
  // the denominator, so the complete exclusions set is part of the identity comparison.
  const normalized = normalizeSearchResponse([
    ranEntry("lu-legilux", 100, ["never-consolidated acts"]),
    ranEntry("lu-legilux", 100, ["never-consolidated acts", "pre-1990 gaps"]),
    ranEntry("eu-eurlex", 42, ["b"]),
  ], classifyEnvelope);
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"]);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex"]);
  assert.deepEqual(populationExclusions(normalized.populations), ["b"],
    "an exclusion from a withheld publisher was still disclosed");
});

test("an exclusions set that differs only by order is one entry, not a conflict", () => {
  // The comparison is a set comparison. Comparing positionally would drop a publisher that never
  // disagreed with itself, which is a fabricated conflict rather than a detected one.
  const normalized = normalizeSearchResponse([
    ranEntry("lu-legilux", 100, ["a", "b"]),
    ranEntry("lu-legilux", 100, ["b", "a"]),
  ], classifyEnvelope);
  assert.equal(normalized.complete, true);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["lu-legilux"]);
  assert.equal(queriedPopulationTotal(normalized.populations), 100);
});

test("a successful entry with no publisher is withheld from rows and from the denominator", () => {
  const anonymous = { ...ranEntry("lu-legilux", 500), envelope: { status: "ok" } };
  const named = ranEntry("eu-eurlex", 42, ["b"]);
  assert.equal(classifyEnvelope("search", anonymous).kind, "ran",
    "the fixture no longer classifies ran; this test would prove nothing");
  const normalized = normalizeSearchResponse([anonymous, named], classifyEnvelope);
  assert.equal(normalized.complete, false);
  assert.equal(normalized.complete === false && normalized.unattributedEntries, 1);
  assert.deepEqual(withheldOf(normalized), [], "an unnamed entry cannot be named as withheld");
  assert.deepEqual(entriesOf(normalized), [named]);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex"]);
  assert.equal(queriedPopulationTotal(normalized.populations), 42,
    "an unattributed scope reached the denominator");
});

test("a publisher identity outside the shipped bound is not an identity", () => {
  // Mirrors the classifier's own bound rather than trimming: accepting " lu-legilux " would let
  // one response carry two spellings of one publisher, each passing the duplicate check the other
  // should have failed.
  assert.equal(boundedPublisher("lu-legilux"), "lu-legilux");
  for (const bad of [" lu-legilux ", "lu legilux", "lu:legilux", "", "x".repeat(65), 7, null,
                     undefined]) {
    assert.equal(boundedPublisher(bad), undefined, `accepted publisher ${JSON.stringify(bad)}`);
  }
  const padded = { ...ranEntry("lu-legilux", 999), envelope: { status: "ok",
                                                               publisher: " lu-legilux " } };
  const normalized = normalizeSearchResponse([ranEntry("lu-legilux", 100), padded],
    classifyEnvelope);
  assert.equal(normalized.complete === false && normalized.unattributedEntries, 1);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["lu-legilux"]);
  assert.equal(queriedPopulationTotal(normalized.populations), 100);
});

test("a publisher whose population is invalid has its rows withheld too", () => {
  // A row list with no denominator behind it invites the reader to check an answer against a
  // number nothing stands behind. The envelope itself is valid here; only its population is not.
  const broken = { ...ranEntry("lu-legilux", 100),
    population: { ...contract.statuses.ok, works_in_scope: -1, known_exclusions: [] } };
  const good = ranEntry("eu-eurlex", 42, ["b"]);
  assert.equal(classifyEnvelope("search", broken).kind, "ran",
    "the fixture no longer classifies ran; this test would prove nothing");
  const normalized = normalizeSearchResponse([broken, good], classifyEnvelope);
  assert.equal(normalized.complete, false);
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"]);
  assert.ok(!entriesOf(normalized).includes(broken), "the entry survived its refused denominator");
  assert.deepEqual(lexIdsOf(entriesOf(normalized)), ["eu-eurlex:w0"],
    "hits rendered for a publisher with no valid denominator");
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex"]);
});

test("a refusal keeps its limitation even when that publisher is withheld", () => {
  // Withholding is scoped to row-bearing entries. Silencing a refusal would hide a disclosed
  // limitation, which trades one fail-open for another.
  const refusal = {
    envelope: { status: "filter_not_supported_by_index", publisher: "lu-legilux" },
    unsupported_filters: ["domain"],
    population: { ...contract.statuses.filter_not_supported_by_index, works_in_scope: 77,
                  known_exclusions: [] },
  };
  const normalized = normalizeSearchResponse([ranEntry("lu-legilux", 100), refusal],
    classifyEnvelope);
  // ran and refused for one publisher is a disagreement about what that publisher did.
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"]);
  assert.ok(entriesOf(normalized).includes(refusal), "a refusal was silenced by withholding");
  assert.equal(partitionGovernedResponse("search", entriesOf(normalized)).limitations.length, 1);
  assert.deepEqual(lexIdsOf(entriesOf(normalized)), []);
  assert.deepEqual(normalized.populations, []);
});

test("an envelope the classifier rejects stays in the projected set", () => {
  // Removing it would shrink the invalid count the projector builds its absence state from,
  // turning a response that hid something into a confident claim that nothing matched.
  const invalidSibling = {
    envelope: { status: "filter_not_supported_by_index", publisher: "eu-eurlex" },
    unsupported_filters: ["not_a_governed_filter"],
    population: { ...contract.statuses.filter_not_supported_by_index, works_in_scope: 999,
                  known_exclusions: [] },
  };
  assert.equal(classifyEnvelope("search", invalidSibling).kind, "invalid",
    "the fixture no longer classifies invalid; this test would prove nothing");
  const normalized = normalizeSearchResponse([ranEntry("lu-legilux", 100), invalidSibling],
    classifyEnvelope);
  assert.equal(normalized.complete, true, "an invalid envelope is the projector's business");
  assert.equal(entriesOf(normalized).length, 2);
  assert.equal(partitionGovernedResponse("search", entriesOf(normalized)).invalidCount, 1);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["lu-legilux"]);
});

test("two maximum safe denominators refuse a total instead of overflowing", () => {
  const rows = searchPopulations([
    ranEntry("lu-legilux", Number.MAX_SAFE_INTEGER, ["a"]),
    ranEntry("eu-eurlex", Number.MAX_SAFE_INTEGER, ["b"]),
  ], classifyEnvelope);
  assert.equal(rows.length, 2, "both entries are individually valid; only their sum is not");
  assert.equal(queriedPopulationTotal(rows), undefined);
  // The failure mode is a number, not a crash: the naive sum is representable and false.
  assert.notEqual(queriedPopulationTotal(rows), Number.MAX_SAFE_INTEGER * 2);
  const denominator = queriedDenominator(rows);
  assert.equal(denominator.kind, "not_summable");
  assert.deepEqual(denominator.kind === "not_summable" ? denominator.publishers : [],
    ["lu-legilux", "eu-eurlex"]);
});

test("a total that lands exactly on the safe ceiling is still a total", () => {
  // The refusal is an overflow guard, not a size limit. A ceiling checked one step too early
  // would refuse a sum that is exactly representable.
  const rows = searchPopulations([
    ranEntry("lu-legilux", Number.MAX_SAFE_INTEGER - 42, ["a"]),
    ranEntry("eu-eurlex", 42, ["b"]),
  ], classifyEnvelope);
  assert.equal(queriedPopulationTotal(rows), Number.MAX_SAFE_INTEGER);
  assert.equal(queriedDenominator(rows).kind, "total");
});

test("an unsafe denominator on a hand-built row is refused rather than added", () => {
  // queriedPopulationTotal is exported, so a row can reach it without passing through
  // normalizeSearchResponse. Every addend is re-checked at the point of addition.
  const rows = [
    { publisher: "lu-legilux", kind: "ran" as const,
      population: { ...contract.statuses.ok, works_in_scope: 100, known_exclusions: [] } },
    { publisher: "eu-eurlex", kind: "ran" as const,
      population: { ...contract.statuses.ok, works_in_scope: Number.NaN, known_exclusions: [] } },
  ] as PublisherPopulation[];
  assert.equal(queriedPopulationTotal(rows), undefined);
  assert.equal(queriedDenominator(rows).kind, "not_summable");
});

test("nobody ran and no total can be added are different answers", () => {
  // They are both undefined to queriedPopulationTotal, so a caller printing a sentence beside the
  // missing number must read the verdict: "no publisher ran this query" is false in the second.
  assert.equal(queriedDenominator([]).kind, "none_ran");
  const unqueried = searchPopulations([{
    envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" },
    requested_retrieval_mode: "hybrid",
    population: { ...contract.statuses.retrieval_mode_unavailable, works_in_scope: 100,
                  known_exclusions: [] },
  }], classifyEnvelope);
  assert.equal(queriedDenominator(unqueried).kind, "none_ran");
  assert.equal(queriedPopulationTotal(unqueried), undefined);
});

// The exported totaller is a second entry point into the same authority, so it carries the same
// invariant. Round 1 of this repair did not: it re-checked every addend and let one publisher be
// counted twice, which mints a safe integer that is false. A hardening function that reintroduces
// the defect it hardens against is worse than none, because its name says it is safe.

/** A hand-built row, as a caller that never saw normalizeSearchResponse would hold one. */
const row = (publisher: string, works: number) => ({
  publisher,
  kind: "ran",
  population: { ...contract.statuses.ok, works_in_scope: works, known_exclusions: [] },
}) as PublisherPopulation;

const unqueriedRow = (publisher: string, works: number) => ({
  publisher,
  kind: "refused",
  population: { ...contract.statuses.filter_not_supported_by_index, works_in_scope: works,
                known_exclusions: [] },
}) as PublisherPopulation;

test("two rows naming one publisher refuse a total rather than adding it twice", () => {
  const rows = [row("lu-legilux", 1200), row("lu-legilux", 900), row("eu-eurlex", 42)];
  assert.equal(queriedPopulationTotal(rows), undefined);
  assert.notEqual(queriedPopulationTotal(rows), 2142, "one publisher was counted twice");
  const denominator = queriedDenominator(rows);
  assert.equal(denominator.kind, "not_summable");
  assert.deepEqual(denominator.kind === "not_summable" ? denominator.publishers : [],
    ["lu-legilux"]);
});

test("two identical rows for one publisher refuse rather than collapsing to one", () => {
  // Collapsing here would be a guess about which reading is authoritative, made where the response
  // set that could establish it is gone. 1200 is as wrong an answer as 2400: the caller may be
  // holding one disclosure twice, or two entries nothing ever compared.
  const rows = [row("lu-legilux", 1200), row("lu-legilux", 1200)];
  assert.deepEqual(rows[0], rows[1], "the rows must be identical for this test to mean anything");
  assert.equal(queriedPopulationTotal(rows), undefined);
  assert.notEqual(queriedPopulationTotal(rows), 2400, "an identical duplicate was added twice");
  assert.notEqual(queriedPopulationTotal(rows), 1200, "an identical duplicate was collapsed");
  assert.equal(queriedDenominator(rows).kind, "not_summable");
});

test("a publisher repeated among rows that did not run is refused as well", () => {
  // The invariant is about the row set, not about the subset that ran. A caller holding a broken
  // set is not holding a denominator either, whatever the repeated rows say about running.
  const rows = [row("lu-legilux", 1200), unqueriedRow("eu-eurlex", 300),
                unqueriedRow("eu-eurlex", 300)];
  assert.equal(queriedPopulationTotal(rows), undefined);
  assert.equal(queriedDenominator(rows).kind, "not_summable");
  // The refusal is the totaller's, not a claim that the rows vanished.
  assert.equal(unqueriedPopulations(rows).length, 2);
});

test("the normalized set always satisfies what the totaller demands", () => {
  // The guard costs a legitimate caller nothing: normalizeSearchResponse keys populations by
  // publisher, so the refusal above can only ever fire on a set assembled somewhere else.
  const normalized = normalizeSearchResponse([
    ranEntry("lu-legilux", 1200), ranEntry("lu-legilux", 1200), ranEntry("eu-eurlex", 42, ["b"]),
  ], classifyEnvelope);
  const publishers = normalized.populations.map((r) => r.publisher);
  assert.deepEqual(publishers, [...new Set(publishers)]);
  assert.equal(queriedDenominator(normalized.populations).kind, "total");
  assert.equal(queriedPopulationTotal(normalized.populations), 1242);
});
