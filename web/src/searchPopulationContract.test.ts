import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import {
  contributesToQueryPopulation, normalizeSearchResponse, POPULATION_BOUNDS,
  populationExclusions, queriedDenominator, queriedPopulationTotal, searchPopulations,
  unqueriedPopulations, validateSearchPopulation,
} from "./searchPopulation.ts";
import type { NormalizedSearchResponse, PublisherPopulation } from "./searchPopulation.ts";
import {
  classifyEnvelope, parseGovernedResponse, partitionGovernedResponse, partitionOf,
  projectSearchResponse, searchAbsenceState,
} from "./limitations.ts";
import { publisherIdentity } from "./publisherIdentity.ts";

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

/**
 * The one door, and the shape production now uses.
 *
 * `normalizeSearchResponse` and `searchPopulations` no longer accept raw bytes or a classifier
 * callback: they take the `GovernedResponse` that `parseGovernedResponse` produced. Parsing here,
 * once, per test is what keeps these tests exercising a path Search.tsx can actually reach.
 */
const parse = (raw: unknown) => parseGovernedResponse("search", raw);
const normalizeOf = (raw: unknown) => normalizeSearchResponse(parse(raw));
const populationsOf = (raw: unknown) => searchPopulations(parse(raw));

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
  const rows = populationsOf(
    [entry("lu-legilux", "ok", { population: { ...contract.statuses.ok, works_in_scope: 1250,
                                               known_exclusions: ["a"] } }),
     entry("eu-eurlex", "ok", { population: { ...contract.statuses.ok, works_in_scope: 300,
                                              known_exclusions: ["b"] } })]);
  assert.equal(rows.length, 2);
  assert.equal(queriedPopulationTotal(rows), 1550);
  // CHANGED ORDER, deliberately. Populations now arrive in the parse's own publisher order
  // rather than transport order, because `GovernedResponse.units` is sorted ordinally so the
  // footer renders identically between processes and runs. The exclusions are a set either way,
  // so the fact under test is membership; the order is asserted separately rather than smuggled
  // into a set comparison.
  assert.deepEqual(rows.map((r) => r.publisher), ["eu-eurlex", "lu-legilux"]);
  assert.deepEqual([...populationExclusions(rows)].sort(), ["a", "b"]);
});

test("a refused publisher discloses its scope but never joins the queried denominator", () => {
  const rows = populationsOf([
    entry("lu-legilux", "ok"),
    { ...entry("eu-eurlex", "filter_not_supported_by_index"), unsupported_filters: ["domain"] },
  ]);
  assert.equal(queriedPopulationTotal(rows), 100, "the refused scope was added to the query claim");
  const unqueried = unqueriedPopulations(rows);
  assert.equal(unqueried.length, 1);
  assert.equal(unqueried[0].publisher, "eu-eurlex");
  assert.equal(unqueried[0].population.basis, "mounted_scope_before_unsupported_filters");
});

test("an all-refused response yields no queried denominator at all", () => {
  const rows = populationsOf([
    { ...entry("lu-legilux", "filter_not_supported_by_index"), unsupported_filters: ["domain"] },
  ]);
  // Not zero. Zero would assert an empty corpus was searched, which is exactly rule N3.
  assert.equal(queriedPopulationTotal(rows), undefined);
  assert.equal(unqueriedPopulations(rows).length, 1);
});

test("a retrieval-mode refusal discloses the selected scope and states it did not run", () => {
  const rows = populationsOf([
    entry("lu-legilux", "retrieval_mode_unavailable",
      { retrieval_mode: undefined, requested_retrieval_mode: "hybrid" }),
  ]);
  assert.equal(rows.length, 1);
  assert.equal(rows[0].population.query_ran, false);
  assert.equal(rows[0].population.basis, "selected_metadata_scope");
  assert.equal(queriedPopulationTotal(rows), undefined);
});

test("an invalid sibling contributes no population fact", () => {
  const rows = populationsOf([
    entry("lu-legilux", "ok"),
    // Incoherent: claims success while saying the query never ran.
    entry("eu-eurlex", "ok", { population: { ...contract.statuses.ok, query_ran: false,
                                             works_in_scope: 999, known_exclusions: [] } }),
  ]);
  assert.equal(rows.length, 1, "an incoherent population was rendered");
  assert.equal(queriedPopulationTotal(rows), 100);
});

test("one publisher repeated across entries stands behind nothing", () => {
  // WAS "one publisher repeated across entries is counted once", asserting that the duplicate
  // collapsed to one surviving population. That is the footer half of O13: the row half has
  // always withheld every claim a repeated publisher made, so the two halves disagreed and the
  // reader was shown a scope count for a source the page showed nothing from.
  const rows = populationsOf([entry("lu-legilux", "ok"), entry("lu-legilux", "ok")]);
  assert.deepEqual(rows, []);
  assert.equal(queriedPopulationTotal(rows), undefined);
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
  const rows = populationsOf([entry("lu-legilux", "ok"), invalidSibling]);
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
  assert.deepEqual(populationsOf([anonymous]), []);
  const mixed = populationsOf([entry("lu-legilux", "ok"), anonymous]);
  assert.equal(mixed.length, 1);
  assert.equal(queriedPopulationTotal(mixed), 100, "an unattributed scope reached the denominator");
});

test("two entries for one publisher that disagree drop that publisher entirely", () => {
  // Keeping the first would let arrival order decide what the reader is told.
  const rows = populationsOf([
    entry("lu-legilux", "ok"),
    entry("lu-legilux", "ok", { population: { ...contract.statuses.ok, works_in_scope: 7,
                                              known_exclusions: [] } }),
    entry("eu-eurlex", "ok"),
  ]);
  assert.deepEqual(rows.map((r) => r.publisher), ["eu-eurlex"]);
  assert.equal(queriedPopulationTotal(rows), 100);
});

test("two identical entries for one publisher are a conflict, not a duplicate", () => {
  // WAS "two identical entries for one publisher are simply de-duplicated". Byte identity does
  // not make a second unit legitimate: the reader registry is keyed by collection, so the
  // producer emits at most one unit per publisher and a second one is a shape it cannot emit
  // whatever it says. See parseGovernedResponse, which has always decided it that way.
  const rows = populationsOf([entry("lu-legilux", "ok"), entry("lu-legilux", "ok")]);
  assert.deepEqual(rows, []);
  assert.equal(queriedPopulationTotal(rows), undefined);
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

/**
 * The two accessors a caller codes against, after the cutover.
 *
 * `entriesOf` is GONE, and so is the entry list it read. It existed because
 * `normalizeSearchResponse` used to return a filtered copy of the raw response for the projector
 * to parse a second time, and the two-branch naming was the device that forced a caller to read
 * `complete` before touching rows. Rows now come from the SAME parse the populations come from,
 * so there is no second entry list to hand out and no second parse to guard against. The
 * disclosure obligation has not gone anywhere: it is carried by the absence state, which types
 * the same response as `partial_results` or `incomplete_response`.
 */
// The two causes are typed apart now (O3) and this helper joins them again, deliberately:
// every assertion below is about WHICH publisher was withheld, not about why, and the tests
// that are about why name the cause explicitly.
const withheldOf = (n: NormalizedSearchResponse): string[] =>
  n.complete ? [] : [...n.withheld.conflicted, ...n.withheld.unreadableScope].sort();

/**
 * The lex_ids the projector would render, read from ONE parse of the raw response.
 *
 * It used to take the entry list `normalizeSearchResponse` returned and parse it again. That was
 * O13 in the test file itself: the row check and the population check ran two passes over the
 * same bytes, so a test could pass while the two passes disagreed on screen.
 */
const lexIdsOf = (raw: unknown): string[] =>
  partitionOf(parse(raw)).ranUnits
    .flatMap((unit) => unit.rows.map((row) => String(row.lex_id)));

test("a coherent response is complete and reaches the projector entry for entry", () => {
  const lu = ranEntry("lu-legilux", 100, ["a"]);
  const eu = ranEntry("eu-eurlex", 42, ["b"]);
  const response = [lu, eu];
  const normalized = normalizeOf(response);
  assert.equal(normalized.complete, true);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex", "lu-legilux"]);
  assert.equal(queriedPopulationTotal(normalized.populations), 142);
  assert.deepEqual(lexIdsOf(response), ["lu-legilux:w0", "eu-eurlex:w0"]);
});

test("reversing the arrival order of two conflicting entries changes nothing", () => {
  // The O2 defect verbatim: two individually valid entries naming one publisher and reporting 100
  // and 999 must not produce 100 or 999 by arrival order. Neither survives.
  const hundred = ranEntry("lu-legilux", 100);
  const nineHundred = ranEntry("lu-legilux", 999);
  const other = ranEntry("eu-eurlex", 42, ["b"]);
  const forward = normalizeOf([hundred, nineHundred, other]);
  const reverse = normalizeOf([nineHundred, hundred, other]);

  assert.deepEqual(forward.populations, reverse.populations);
  assert.deepEqual(withheldOf(forward), withheldOf(reverse));
  assert.deepEqual(lexIdsOf([hundred, nineHundred, other]),
    lexIdsOf([nineHundred, hundred, other]));
  assert.deepEqual(forward.populations.map((r) => r.publisher), ["eu-eurlex"]);
  assert.equal(queriedPopulationTotal(forward.populations), 42);
  assert.equal(queriedPopulationTotal(reverse.populations), 42);
  assert.deepEqual(populationsOf([hundred, nineHundred, other]),
    populationsOf([nineHundred, hundred, other]));
});

test("a conflicting duplicate withholds that publisher's rows, not only its population", () => {
  // Dropping the population row while still rendering the hits is the repair the reviewer ruled
  // out: the reader is then shown a publisher's results with no denominator to check them by.
  const response = [
    ranEntry("lu-legilux", 100),
    ranEntry("lu-legilux", 999),
    ranEntry("eu-eurlex", 42, ["b"]),
  ];
  const normalized = normalizeOf(response);
  assert.equal(normalized.complete, false);
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"]);
  assert.deepEqual(lexIdsOf(response), ["eu-eurlex:w0"],
    "hits rendered for a publisher whose denominator was refused");
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex"]);
});

test("two entries agreeing on the count but not on the exclusions conflict", () => {
  // Same denominator, different statements about what it leaves out. Before the cutover this
  // was decided by comparing the two disclosures field by field; it is now decided by the unit
  // COUNT alone, one module up, which reaches the same verdict here for a stronger reason: the
  // reader registry is keyed by collection, so a second unit for one publisher is a shape the
  // producer cannot emit whatever the two units say.
  const normalized = normalizeOf([
    ranEntry("lu-legilux", 100, ["never-consolidated acts"]),
    ranEntry("lu-legilux", 100, ["never-consolidated acts", "pre-1990 gaps"]),
    ranEntry("eu-eurlex", 42, ["b"]),
  ]);
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"]);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex"]);
  assert.deepEqual(populationExclusions(normalized.populations), ["b"],
    "an exclusion from a withheld publisher was still disclosed");
});

test("a second unit for one publisher is withheld even when the two agree exactly", () => {
  // CHANGED BEHAVIOUR, and this test replaces two that pinned the opposite: "one publisher
  // repeated across entries is counted once" and "an exclusions set that differs only by order
  // is one entry, not a conflict". Both asserted that a logically identical duplicate collapses
  // into one surviving population.
  //
  // That was O13 exactly. `parseGovernedResponse` has always treated a second claim-bearing unit
  // for one publisher as a conflict and withheld every row it sent, because the reader registry
  // is keyed by collection and the producer emits at most one unit per publisher. The footer
  // disagreed and published the collapsed denominator anyway, so the reader was shown a scope
  // count for a source the page was showing nothing from. One parse makes the two answers one.
  const identical = [ranEntry("lu-legilux", 100, ["a"]), ranEntry("lu-legilux", 100, ["a"])];
  const reordered = [ranEntry("lu-legilux", 100, ["a", "b"]),
                     ranEntry("lu-legilux", 100, ["b", "a"])];
  for (const [label, response] of [["identical", identical], ["reordered", reordered]] as
       [string, unknown[]][]) {
    const normalized = normalizeOf(response);
    assert.equal(normalized.complete, false, `${label}: a duplicate was collapsed`);
    assert.deepEqual(withheldOf(normalized), ["lu-legilux"], `${label}: not withheld`);
    assert.deepEqual(normalized.populations, [],
      `${label}: a denominator survived for a publisher showing no rows`);
    assert.deepEqual(lexIdsOf(response), [], `${label}: rows survived the conflict`);
  }
});

test("a successful entry with no publisher is withheld from rows and from the denominator", () => {
  const anonymous = { ...ranEntry("lu-legilux", 500), envelope: { status: "ok" } };
  const named = ranEntry("eu-eurlex", 42, ["b"]);
  assert.equal(classifyEnvelope("search", anonymous).kind, "ran",
    "the fixture no longer classifies ran; this test would prove nothing");
  const normalized = normalizeOf([anonymous, named]);
  assert.equal(normalized.complete, false);
  assert.equal(normalized.complete === false && normalized.withheld.unattributed, 1);
  assert.deepEqual(withheldOf(normalized), [], "an unnamed entry cannot be named as withheld");
  assert.deepEqual(lexIdsOf([anonymous, named]), ["eu-eurlex:w0"],
    "an unattributable entry's rows reached the reader");
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex"]);
  assert.equal(queriedPopulationTotal(normalized.populations), 42,
    "an unattributed scope reached the denominator");
});

test("a publisher identity outside the shipped grammar is not an identity", () => {
  // ONE validator across the strip, this footer and the limitation list; see
  // publisherIdentity.ts for the producer evidence. It never trims and never case-folds,
  // because a repaired value is a DIFFERENT logical identity from the one the producer minted,
  // and admitting it would let one response carry two spellings of one publisher, each passing
  // the duplicate check the other should have failed.
  assert.equal(publisherIdentity("lu-legilux"), "lu-legilux");
  for (const bad of [" lu-legilux ", " lu-legilux", "lu-legilux ", "LU-LEGILUX", "LU-Legilux",
                     "lu-legiluX", "?", "lu legilux", "lu:legilux", "", "x".repeat(65), 7, null,
                     undefined]) {
    assert.equal(publisherIdentity(bad), undefined, `accepted publisher ${JSON.stringify(bad)}`);
  }
  const padded = { ...ranEntry("lu-legilux", 999), envelope: { status: "ok",
                                                               publisher: " lu-legilux " } };
  const normalized = normalizeOf([ranEntry("lu-legilux", 100), padded]);
  assert.equal(normalized.complete === false && normalized.withheld.unattributed, 1);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["lu-legilux"]);
  assert.equal(queriedPopulationTotal(normalized.populations), 100);

  // The case alias attacks the same seam from the other side. Folding it would attribute a
  // second denominator to lu-legilux; keeping the `i` flag would attribute a denominator to a
  // publisher identity the producer's ordinal registry cannot mint.
  const aliased = { ...ranEntry("lu-legilux", 999), envelope: { status: "ok",
                                                                publisher: "LU-Legilux" } };
  const cased = normalizeOf([ranEntry("lu-legilux", 100), aliased]);
  assert.equal(cased.complete === false && cased.withheld.unattributed, 1);
  assert.deepEqual(cased.populations.map((r) => r.publisher), ["lu-legilux"]);
  assert.equal(queriedPopulationTotal(cased.populations), 100);
});

test("a publisher whose population is invalid has its rows withheld too", () => {
  // A row list with no denominator behind it invites the reader to check an answer against a
  // number nothing stands behind. The envelope itself is otherwise valid here; only its
  // population is not.
  //
  // CHANGED MECHANISM, same verdict. This used to be decided in searchPopulation.ts, which
  // classified the entry as `ran`, validated its population separately, and then reached back to
  // remove the entry from the list it handed the projector. The whole CLAIM is now invalid at
  // the one door, so the unit never exists, and the publisher is named through `unreadable`.
  const broken = { ...ranEntry("lu-legilux", 100),
    population: { ...contract.statuses.ok, works_in_scope: -1, known_exclusions: [] } };
  const good = ranEntry("eu-eurlex", 42, ["b"]);
  assert.equal(classifyEnvelope("search", broken).kind, "invalid",
    "an unreadable required scope no longer invalidates the claim that carries it");
  const normalized = normalizeOf([broken, good]);
  assert.equal(normalized.complete, false);
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"]);
  assert.deepEqual(lexIdsOf([broken, good]), ["eu-eurlex:w0"],
    "hits rendered for a publisher with no valid denominator");
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex"]);
});

/** A coherent filter refusal with a valid population of its own. */
const refusalEntry = (publisher: string, works = 77) => ({
  envelope: { status: "filter_not_supported_by_index", publisher },
  unsupported_filters: ["domain"],
  population: { ...contract.statuses.filter_not_supported_by_index, works_in_scope: works,
                known_exclusions: [] },
});

/** A coherent retrieval-mode refusal, the third claim-bearing class. */
const modeEntry = (publisher: string, works = 55) => ({
  envelope: { status: "retrieval_mode_unavailable", publisher },
  population: { ...contract.statuses.retrieval_mode_unavailable, works_in_scope: works,
                known_exclusions: [] },
});

test("a same-publisher status conflict withholds every claim, the refusal included", () => {
  // REPLACES a test that pinned the opposite. It asserted that the refusal survives a ran/
  // refused conflict, on the rationale that dropping it would hide a limitation. Its own
  // fixture contradicted its expected copy: the projector then emitted all_refused, the screen
  // said "No selected publisher ran this query" and LIMITATION_EXPLANATION said this publisher
  // did not run this query, about a response that ALSO said it ran. The product does not know
  // which side is true and must not assert one. The withholding notice already discloses the
  // incoherence, so dropping the refusal hides nothing.
  const response = [ranEntry("lu-legilux", 100), refusalEntry("lu-legilux")];
  const normalized = normalizeOf(response);
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"]);
  assert.deepEqual(normalized.populations, [], "a contradicted publisher stood behind a number");
  const partition = partitionGovernedResponse("search", response);
  assert.deepEqual(partition.conflictedPublishers, ["lu-legilux"]);
  assert.equal(partition.limitations.length, 0,
    "the refusal side of a self-contradicting response was asserted as fact");
  assert.deepEqual(lexIdsOf(response), [],
    "the ran side of a self-contradicting response was asserted as fact");
  assert.equal(partition.allRefused, false,
    "all_refused says no publisher ran, about a response that said one did");
  assert.equal(searchAbsenceState(partition, 0), "incomplete_response");
});

test("order cannot decide which side of a conflict survives", () => {
  // Callers hand this raw transport order. Both orders must reach the same verdict, for every
  // pairing of the three claim-bearing classes.
  const pairs: [string, unknown, unknown][] = [
    ["ran vs refused", ranEntry("lu-legilux", 100), refusalEntry("lu-legilux")],
    ["ran vs mode_unavailable", ranEntry("lu-legilux", 100), modeEntry("lu-legilux")],
    ["refused vs mode_unavailable", refusalEntry("lu-legilux"), modeEntry("lu-legilux")],
  ];
  for (const [label, a, b] of pairs) {
    for (const order of [[a, b], [b, a]]) {
      const normalized = normalizeOf(order);
      assert.deepEqual(withheldOf(normalized), ["lu-legilux"], `${label}: not withheld`);
      assert.deepEqual(normalized.populations, [], `${label}: a denominator survived`);
      const partition = partitionGovernedResponse("search", order);
      assert.deepEqual(partition.conflictedPublishers, ["lu-legilux"], `${label}: not detected`);
      assert.equal(partition.limitations.length, 0, `${label}: a limitation survived`);
      assert.deepEqual(lexIdsOf(order), [], `${label}: rows survived`);
      assert.equal(partition.modeUnavailableCount, 0, `${label}: a mode claim survived`);
      assert.equal(searchAbsenceState(partition, 0), "incomplete_response", `${label}: claimed`);
    }
  }
});

test("a lone refusal whose population alone is invalid keeps no limitation either", () => {
  // CHANGED BEHAVIOUR, and it replaces a test that asserted the limitation survives. That test
  // was written under the old rule, where an unreadable population voided the DENOMINATOR and
  // left the claim standing, so a lone refusal still reached all_refused and still printed
  // LIMITATION_EXPLANATION.
  //
  // The corrected rule is that an unreadable required scope invalidates the whole claim. Search
  // publishes a population on all three of its paths, including this one
  // (`refusal["population"] = SearchPopulation(reader, filter, false, false)`), so a refusal
  // whose population will not validate is not a coherent refusal with a bad number attached: it
  // is an object that failed to be the thing it claims to be. Standing behind half of it is the
  // marker-without-a-guarantee defect this refactor exists to remove.
  //
  // Nothing is silently dropped. The publisher is named through `withheldPublishers`, the
  // response is incomplete rather than all-refused, and `incomplete_response` claims nothing.
  const broken = { ...refusalEntry("lu-legilux"),
    population: { ...contract.statuses.filter_not_supported_by_index, works_in_scope: -1,
                  known_exclusions: [] } };
  assert.equal(classifyEnvelope("search", broken).kind, "invalid",
    "a refusal with an unreadable population still classified as a refusal");
  const normalized = normalizeOf([broken]);
  assert.deepEqual(withheldOf(normalized), ["lu-legilux"], "the void denominator was not named");
  assert.deepEqual(normalized.populations, []);
  const partition = partitionGovernedResponse("search", [broken]);
  assert.deepEqual(partition.conflictedPublishers, []);
  assert.equal(partition.limitations.length, 0,
    "a limitation was published from a claim that did not validate");
  assert.equal(partition.allRefused, false,
    "all_refused speaks for a refusal this response never coherently made");
  assert.equal(searchAbsenceState(partition, 0), "incomplete_response");
});

test("a conflict across distinct publishers is not a conflict at all", () => {
  // One publisher ran and a DIFFERENT one refused. Nothing contradicts itself, so both
  // disclosures stand and the response is complete.
  const response = [ranEntry("lu-legilux", 100), refusalEntry("eu-eurlex")];
  const normalized = normalizeOf(response);
  assert.equal(normalized.complete, true, "two coherent publishers were treated as a conflict");
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["eu-eurlex", "lu-legilux"]);
  const partition = partitionGovernedResponse("search", response);
  assert.deepEqual(partition.conflictedPublishers, []);
  assert.equal(partition.limitations.length, 1);
  assert.deepEqual(lexIdsOf(response), ["lu-legilux:w0"]);
});

test("an envelope the classifier rejects contributes nothing and hides nothing", () => {
  // Its rows never render and its population never counts, and the response is still known to be
  // incomplete: the invalid count the projector builds its absence state from still sees it, so
  // a response that hid something cannot report a confident claim that nothing matched.
  const invalidSibling = {
    envelope: { status: "filter_not_supported_by_index", publisher: "eu-eurlex" },
    unsupported_filters: ["not_a_governed_filter"],
    population: { ...contract.statuses.filter_not_supported_by_index, works_in_scope: 999,
                  known_exclusions: [] },
  };
  assert.equal(classifyEnvelope("search", invalidSibling).kind, "invalid",
    "the fixture no longer classifies invalid; this test would prove nothing");
  const response = [ranEntry("lu-legilux", 100), invalidSibling];
  const normalized = normalizeOf(response);
  // Complete: an unrecognized filter name is not an identity or a scope failure, so nobody is
  // named as withheld. The projector still sees it.
  assert.equal(normalized.complete, true, "an invalid envelope is the projector's business");
  assert.equal(partitionGovernedResponse("search", response).invalidCount, 1);
  assert.deepEqual(lexIdsOf(response), ["lu-legilux:w0"]);
  assert.deepEqual(normalized.populations.map((r) => r.publisher), ["lu-legilux"]);
});

test("a denominator past the producer's Int32 range never reaches the totaller", () => {
  // WAS "two maximum safe denominators refuse a total instead of overflowing", which built its
  // fixture from Number.MAX_SAFE_INTEGER and asserted both entries were individually valid.
  //
  // They were, and that WAS O15. `works_in_scope` is minted from `SearchPopulationTotal` or
  // `PopulationTotal`, both declared `public int`, so 2^53 - 1 is a number nothing in that chain
  // can produce. The validator accepted it and the search footer published it, one module away
  // from a parser that refused the same value. The ceiling is now in the validator, so the
  // entries are refused before any arithmetic and the whole publisher is withheld.
  //
  // On EVERY population-bearing status, not one representative: the producer attaches a
  // population to all three search paths and each has its own coherence triple, so a ceiling
  // applied on only one of them leaves the other two open.
  for (const status of Object.keys(contract.statuses)) {
    const population = { ...contract.statuses[status], works_in_scope: 2147483648,
                         known_exclusions: [] };
    assert.equal(validateSearchPopulation(status, population).valid, false,
      `${status} accepted a denominator past Int32.MaxValue`);
    // Int32.MaxValue itself is a bound, not a narrowing: it is a value the producer can mint.
    assert.equal(validateSearchPopulation(
      status, { ...population, works_in_scope: 2147483647 }).valid, true,
      `${status} refused the producer's own maximum`);
  }
  const rows = populationsOf([
    ranEntry("lu-legilux", Number.MAX_SAFE_INTEGER, ["a"]),
    ranEntry("eu-eurlex", Number.MAX_SAFE_INTEGER, ["b"]),
  ]);
  assert.deepEqual(rows, [], "an unmintable denominator survived to the footer");
  assert.equal(queriedPopulationTotal(rows), undefined);
});

test("the overflow guard still refuses a sum that has stopped being one", () => {
  // The guard in `queriedDenominator` is now unreachable from a validated response, because
  // eight Int32 addends cannot approach 2^53. It is not dead: `queriedDenominator` is exported
  // and a row can reach it from somewhere else, so the guard is exercised on hand-built rows,
  // which is the only way the defect it stops can now arise.
  const overflowing = [row("lu-legilux", Number.MAX_SAFE_INTEGER),
                       row("eu-eurlex", Number.MAX_SAFE_INTEGER)];
  assert.equal(queriedPopulationTotal(overflowing), undefined);
  // The failure mode is a number, not a crash: the naive sum is representable and false.
  assert.notEqual(queriedPopulationTotal(overflowing), Number.MAX_SAFE_INTEGER * 2);
  const denominator = queriedDenominator(overflowing);
  assert.equal(denominator.kind, "not_summable");
  assert.deepEqual(denominator.kind === "not_summable" ? denominator.publishers : [],
    ["lu-legilux", "eu-eurlex"]);
});

test("a total that lands exactly on the safe ceiling is still a total", () => {
  // The refusal is an overflow guard, not a size limit. A ceiling checked one step too early
  // would refuse a sum that is exactly representable. Hand-built for the same reason as above:
  // the validator will no longer mint an addend anywhere near this size.
  const rows = [row("lu-legilux", Number.MAX_SAFE_INTEGER - 42), row("eu-eurlex", 42)];
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
  const unqueried = populationsOf([{
    envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" },
    requested_retrieval_mode: "hybrid",
    population: { ...contract.statuses.retrieval_mode_unavailable, works_in_scope: 100,
                  known_exclusions: [] },
  }]);
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
  // The guard costs a legitimate caller nothing: the parse emits at most one unit per publisher,
  // so the refusal above can only ever fire on a set assembled somewhere else.
  //
  // CHANGED EXPECTATION. The repeated publisher used to collapse into one row and contribute
  // 1200 to the total; it is now a conflict, so lu-legilux contributes nothing and the total is
  // eu-eurlex alone. Both readings satisfy the one-row-per-publisher invariant this test is
  // about, and only the second agrees with the rows on screen, which show nothing from
  // lu-legilux either.
  const normalized = normalizeOf([
    ranEntry("lu-legilux", 1200), ranEntry("lu-legilux", 1200), ranEntry("eu-eurlex", 42, ["b"]),
  ]);
  const publishers = normalized.populations.map((r) => r.publisher);
  assert.deepEqual(publishers, [...new Set(publishers)]);
  assert.deepEqual(publishers, ["eu-eurlex"], "a repeated publisher stood behind a number");
  assert.equal(queriedDenominator(normalized.populations).kind, "total");
  assert.equal(queriedPopulationTotal(normalized.populations), 42);
});

// ---------------------------------------------------------------------------
// O13/O14: one parse, proved rather than asserted
// ---------------------------------------------------------------------------

test("the raw response is read once, and never again by any projection", () => {
  // INSTRUMENTATION, not a reading of the code. Each entry is a Proxy that counts every property
  // read, so a second walk over the response is observable rather than argued about. Before the
  // cutover the same bytes were walked three times inside one `.then`: once for the index strip,
  // once for the populations, and once more inside partitionGovernedResponse for rows, mode,
  // expansions and absence.
  let reads = 0;
  const watch = <T extends object>(value: T): T => new Proxy(value, {
    get(target, key, receiver) {
      reads += 1;
      return Reflect.get(target, key, receiver);
    },
  });
  const response = [
    watch(ranEntry("lu-legilux", 100, ["a"], 2)),
    watch(ranEntry("eu-eurlex", 42, ["b"], 1)),
  ];

  const parsed = parseGovernedResponse("search", response);
  const afterParse = reads;
  assert.ok(afterParse > 0, "the parse read nothing, so this test measures nothing");

  // Every downstream view of that one parse.
  const answer = normalizeSearchResponse(parsed);
  const partition = partitionOf(parsed);
  const projected = projectSearchResponse<{ id: string }, never>(parsed, (units) => ({
    works: units.flatMap((unit) => unit.rows.map((row) => ({ id: String(row.lex_id) }))),
    articles: [],
    ranHitCount: units.reduce((sum, unit) => sum + unit.rows.length, 0),
  }));

  assert.equal(reads, afterParse,
    "a projection read the raw response again, so two parses can still disagree");
  // And the views actually produced something, so the count above is not zero work.
  assert.equal(answer.populations.length, 2);
  assert.equal(partition.ranUnits.length, 2);
  assert.equal(projected.works.length, 3);
  assert.equal(projected.absence, "has_results");
});

test("the second parser cannot be reintroduced without this failing", () => {
  // STRUCTURAL, and deliberately blunt. The two-parser shape was not a bug inside one function;
  // it was a second module that took raw `unknown` and re-derived publisher, population,
  // duplicate and row-withholding state of its own. This asserts the shape that made that
  // impossible, because a behavioural test cannot see a second parser that happens to agree
  // today.
  // Comments are stripped first, deliberately. These files explain the defect they closed, and
  // a sentence naming `envelope.publisher` as the thing that used to be re-read is the opposite
  // of a reintroduction. Only code is scanned.
  const source = (name: string) =>
    readFileSync(new URL(`./${name}`, import.meta.url), "utf8")
      .replace(/\/\*[\s\S]*?\*\//g, "")
      .replace(/^[ \t]*\/\/.*$/gm, "");

  const population = source("searchPopulation.ts");
  assert.ok(population.includes("normalizeSearchResponse"),
    "the stripper removed the code along with the comments, so this proves nothing");
  for (const banned of ["classifyEnvelope", "publisherIdentity(", ".envelope", "envelope.",
                        "record.population", "kindsByPublisher", "statusConflicted"]) {
    assert.ok(!population.includes(banned),
      `searchPopulation.ts reads the raw response again: found ${banned}`);
  }
  // The adapter takes a parse, never bytes and never a classifier callback.
  assert.match(population,
    /export function normalizeSearchResponse\(\s*parsed: GovernedResponse,\s*\): NormalizedSearchResponse/);
  assert.match(population,
    /export function searchPopulations\(parsed: GovernedResponse\): PublisherPopulation\[\]/);

  // And the surface parses once. `partitionGovernedResponse` takes raw bytes, so a call to it
  // here would be a second parse by definition.
  const search = source("Search.tsx");
  assert.equal(search.split("parseGovernedResponse(").length - 1, 1,
    "Search parses the response more or less than exactly once");
  assert.ok(!search.includes("partitionGovernedResponse("),
    "Search reached the raw-bytes partition, which parses the response a second time");
  assert.ok(!search.includes("normalizeSearchResponse(res"),
    "Search handed raw bytes to the population adapter");
});

test("a duplicate ran unit reaches the real search projection as nothing at all", () => {
  // O14 through the production projector rather than the module in isolation, because the defect
  // was that two modules disagreed: the footer published a collapsed denominator while the
  // projector withheld every row behind it. Both are read from one parse here.
  const response = [
    ranEntry("lu-legilux", 1200, ["a"], 2),
    ranEntry("lu-legilux", 1200, ["a"], 2),
    ranEntry("eu-eurlex", 42, ["b"], 1),
  ];
  const parsed = parseGovernedResponse("search", response);
  const answer = normalizeSearchResponse(parsed);
  const projected = projectSearchResponse<{ id: string }, never>(parsed, (units) => ({
    works: units.flatMap((unit) => unit.rows.map((row) => ({ id: String(row.lex_id) }))),
    articles: [],
    ranHitCount: units.reduce((sum, unit) => sum + unit.rows.length, 0),
  }));
  assert.deepEqual(answer.populations.map((r) => r.publisher), ["eu-eurlex"],
    "a denominator survived for a publisher the page shows nothing from");
  assert.deepEqual(withheldOf(answer), ["lu-legilux"]);
  assert.deepEqual(projected.works.map((w) => w.id), ["eu-eurlex:w0"]);
  assert.equal(projected.absence, "partial_results",
    "a response that withheld a whole publisher presented itself as complete");
  // The two views agree by construction: every publisher with a population has rows, and every
  // publisher with rows has a population.
  const shown = new Set(projected.works.map((w) => w.id.split(":")[0]));
  assert.deepEqual([...shown].sort(), answer.populations.map((r) => r.publisher).sort());
});
