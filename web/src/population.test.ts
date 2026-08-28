import test from "node:test";
import assert from "node:assert/strict";
import { fuzzyModeFor, populationCoverageLabel, populationScopeLabel, retainedForQuery,
  summedPopulation,
  unionKnownExclusions } from "./api.ts";

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

// retainedForQuery has been the site of the same defect twice, once on the exact-words override
// and once on the publisher metadata filter. The rule it encodes is that state a reader bound to
// one question is discarded when a different question is submitted. Hiding it is not discarding
// it: a hidden value is dormant, and returning to the earlier question reapplies a narrowing the
// reader authorised once, on a visit they never authorised.
//
// What these cases do NOT prove, stated because a green test that stands behind an unreachable
// production path is the exact defect this lane keeps finding. The search surface is now keyed by
// the trimmed question, and the metadata filter stores the question it was chosen for, so within
// one mount the stored question and the submitted one cannot disagree and this function's
// mismatch branch is unreachable through that path. The remount is the primary mechanism and it
// is covered by a browser test: removing the key lets the override survive a change of question
// and flips a recorded request argument.
//
// This function is kept as a second, independent guard, because the remount is one line in a
// render tree that a later edit could delete without touching this file, and the failure would be
// silent. That is a different case from a bound the grammar already enforces, which would be
// unkillable dead code. These cases pin the rule; they are not evidence about the component.

const filter = { query: "conge parental", metadata: { kind: "eurovoc_domain" } };

test("state bound to a question survives that question", () => {
  assert.deepEqual(retainedForQuery(filter, "conge parental"), filter);
});

test("state bound to a question is discarded by a different question", () => {
  assert.equal(retainedForQuery(filter, "travial salarie"), undefined);
  assert.equal(retainedForQuery(filter, ""), undefined);
  // A prefix is a different question, not the same one partially typed.
  assert.equal(retainedForQuery(filter, "conge"), undefined);
});

test("padding is not a different question", () => {
  // The request carries the trimmed question and the search surface is keyed by it. Comparing
  // raw strings here while the key trimmed left a padded resubmission neither remounted nor
  // discarded, so the unpadded question reactivated it. One notion of identity, not three.
  assert.deepEqual(retainedForQuery(filter, "  conge parental  "), filter);
  assert.deepEqual(
    retainedForQuery({ ...filter, query: "  conge parental  " }, "conge parental"),
    { ...filter, query: "  conge parental  " });
});

test("nothing bound means nothing to retain", () => {
  assert.equal(retainedForQuery(undefined, "conge parental"), undefined);
});

// A population figure is a legal scope claim: it tells the reader how much of the corpus an answer
// speaks for. These values cross the runtime boundary from a transport object, not from the
// producer, so every one of them is hostile until checked. The previous form summed with a zero
// default, which turned a string, a fraction, a negative or a missing value into a silently
// smaller denominator rather than into no denominator at all.

const pop = (works: unknown) => ({ population: { works_in_scope: works } });

test("a population sums across the publishers that stated one", () => {
  assert.equal(summedPopulation([pop(1200), pop(1137)], "works_in_scope"), 2337);
});

test("an entry with no population contributes nothing rather than a zero", () => {
  assert.equal(summedPopulation([pop(1200), {}], "works_in_scope"), 1200);
  assert.equal(summedPopulation([{}, {}], "works_in_scope"), undefined);
});

test("a count the producer could not have minted refuses the whole total", () => {
  // Refuses rather than drops the bad entry: a total missing one publisher is not a smaller
  // truth, it is a different and unstated scope.
  for (const bad of ["1200", 12.5, -1, Number.NaN, Number.MAX_SAFE_INTEGER + 1, null, {}])
    assert.equal(summedPopulation([pop(1200), pop(bad)], "works_in_scope"), undefined, String(bad));
});

test("two individually valid counts that overflow refuse a total", () => {
  const max = Number.MAX_SAFE_INTEGER;
  assert.equal(summedPopulation([pop(max), pop(max)], "works_in_scope"), undefined);
  assert.equal(summedPopulation([pop(max), pop(1)], "works_in_scope"), undefined);
  assert.equal(summedPopulation([pop(max), pop(0)], "works_in_scope"), max);
});

test("refusal does not depend on arrival order", () => {
  assert.equal(summedPopulation([pop(-1), pop(1200)], "works_in_scope"),
    summedPopulation([pop(1200), pop(-1)], "works_in_scope"));
});

test("an oversized or overlong exclusions set is refused, not truncated", () => {
  const many = { population: { known_exclusions: Array.from({ length: 21 }, (_, i) => `e${i}`) } };
  assert.deepEqual(unionKnownExclusions([many]), []);
  const long = { population: { known_exclusions: ["x".repeat(301)] } };
  assert.deepEqual(unionKnownExclusions([long]), []);
  const ok = { population: { known_exclusions: ["withdrawn acts", "withdrawn acts"] } };
  assert.deepEqual(unionKnownExclusions([ok]), ["withdrawn acts"]);
});
