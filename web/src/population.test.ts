import test from "node:test";
import assert from "node:assert/strict";
import { fuzzyModeFor, populationCoverageLabel, populationScopeLabel, unionKnownExclusions } from "./api.ts";

// Trust rule 6 is about a denominator the reader can check. These bind the exact presentation of
// a population the producer published, so a number can never appear describing a scope it did not
// measure. Each assertion was watched failing against the pre-repair code before being kept.

test("no published population produces no label, never a zero", () => {
  // A missing denominator must read as absent. Rendering 0 would assert an empty corpus.
  assert.equal(populationCoverageLabel(undefined, "versioned works only", true), undefined);
});

// Digit grouping follows the host locale, so the expected separator is taken from the platform
// rather than hard-coded. Asserting "1,250" passes on an en-US runner and fails on a machine that
// groups with a space, which would make this suite report a defect that does not exist.
const grouped = (1250).toLocaleString();

test("a covered population renders with the producer's own basis", () => {
  assert.equal(populationCoverageLabel(1250, "versioned works only", true),
    `${grouped} works covered, versioned works only`);
});

test("a denominator that predates the request's filters says so", () => {
  // works_covered is Coverage(1).Groups and is never narrowed by metadata filters. Presented
  // silently beside a filtered list it would imply the filters reduced it.
  assert.equal(populationCoverageLabel(1250, "versioned works only", false),
    `${grouped} works covered before the selected filters, versioned works only`);
});

test("a basis the producer did not state is never invented", () => {
  assert.equal(populationCoverageLabel(42, undefined, true), "42 works covered");
  assert.equal(populationCoverageLabel(42, "   ", true), "42 works covered");
});

test("an implausibly long basis is dropped rather than rendered", () => {
  assert.equal(populationCoverageLabel(42, "x".repeat(121), true), "42 works covered");
});

test("the scope label and the coverage label stay distinct claims", () => {
  // in_force_on's works_covered and changes_in_period's works_in_scope measure different things.
  // If these two ever render identically, one surface is making the other's claim.
  assert.notEqual(populationScopeLabel(1250), populationCoverageLabel(1250, undefined, true));
});

test("known exclusions de-duplicate across publishers", () => {
  const united = unionKnownExclusions([
    { population: { known_exclusions: ["never-consolidated acts"] } },
    { population: { known_exclusions: ["never-consolidated acts", "pre-1990 gaps"] } },
  ]);
  assert.deepEqual(united, ["never-consolidated acts", "pre-1990 gaps"]);
});

test("a publisher with no exclusions contributes no empty segment", () => {
  // The pre-repair union kept a truthy [] and rendered "Known exclusions:" followed by a stray
  // separator, which reads as a withheld exclusion that does not exist.
  const united = unionKnownExclusions([
    { population: { known_exclusions: [] } },
    { population: { known_exclusions: ["pre-1990 gaps"] } },
  ]);
  assert.deepEqual(united, ["pre-1990 gaps"]);
  assert.equal(united.join(" · "), "pre-1990 gaps");
});

test("a publisher that published no population at all is skipped", () => {
  assert.deepEqual(unionKnownExclusions([{}, null, { population: {} }]), []);
});

// Trust rule 9: the one-tap revert, and the binding that stops it leaking onto another query.

test("no override means the default relaxation still applies", () => {
  assert.equal(fuzzyModeFor(undefined, "travial salarie"), "auto");
});

test("an override applies to the exact query it was chosen for", () => {
  assert.equal(fuzzyModeFor("travial salarie", "travial salarie"), "off");
  assert.equal(fuzzyModeFor("travial salarie", "  travial salarie  "), "off");
});

test("an override never survives a change of question", () => {
  // A reader who turned off spelling fallback for one question has said nothing about the next.
  // Carrying it forward would silently narrow a search they never narrowed.
  assert.equal(fuzzyModeFor("travial salarie", "conge parental"), "auto");
  assert.equal(fuzzyModeFor("travial salarie", ""), "auto");
  assert.equal(fuzzyModeFor("travial salarie", "travial"), "auto");
});
