import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import {
  contributesToQueryPopulation, POPULATION_BOUNDS, validateSearchPopulation,
} from "./searchPopulation.ts";

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
