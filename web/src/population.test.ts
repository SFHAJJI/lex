import test from "node:test";
import assert from "node:assert/strict";
import { fuzzyModeFor, populationCoverageLabel, populationScopeLabel, retainedForQuery,
  changeCountLabels, summedCount, summedPopulation,
  unionKnownExclusions } from "./api.ts";
import { MAX_PRODUCER_COUNT } from "./searchPopulation.ts";

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
  //
  // 2147483648 is the case this helper used to miss. It is an exact integer, so
  // Number.isSafeInteger said yes, and it is one more than the largest value
  // SearchPopulationTotal or Coverage(1).Groups can return. The parser refused it and this
  // header rendered it, which is the disagreement the cutover removes.
  for (const bad of ["1200", 12.5, -1, Number.NaN, Number.MAX_SAFE_INTEGER + 1,
                     Number.MAX_SAFE_INTEGER, 2147483648, null, {}])
    assert.equal(summedPopulation([pop(1200), pop(bad)], "works_in_scope"), undefined, String(bad));
  // A bound, not a narrowing: Int32.MaxValue is a value the producer can mint.
  assert.equal(summedPopulation([pop(2147483647)], "works_in_scope"), 2147483647);
});

test("two individually valid counts that overflow refuse a total", () => {
  // The addends are now capped at Int32, so the overflow guard is unreachable from a validated
  // response and this exercises the exported helper directly, which is where it can still fire.
  const max = MAX_PRODUCER_COUNT;
  assert.equal(summedPopulation([pop(max), pop(max)], "works_in_scope"), max * 2);
  assert.equal(summedPopulation([pop(Number.MAX_SAFE_INTEGER), pop(1)], "works_in_scope"),
    undefined);
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

// The counts beside the rows, rather than inside the population object: works changed, new
// versions, total works in force. Same refusal, same reason. These are legal counts a reader is
// invited to check an answer against, and the producers are C# int, so a value outside that range
// is not merely unsafe, it is one the producer could not have sent.

const count = (works: unknown) => ({ works_changed: works });

test("counts sum across the publishers that stated one", () => {
  assert.equal(summedCount([count(40), count(2)], "works_changed"), 42);
});

test("an entry with no count contributes nothing rather than a zero", () => {
  assert.equal(summedCount([count(40), {}], "works_changed"), 40);
  assert.equal(summedCount([{}, {}], "works_changed"), undefined);
});

test("a count the producer could not have sent refuses the whole total", () => {
  for (const bad of ["40", 4.5, -1, Number.NaN, 1e20, Number.MAX_SAFE_INTEGER, 2147483648,
                     null, {}])
    assert.equal(summedCount([count(40), count(bad)], "works_changed"), undefined, String(bad));
  assert.equal(summedCount([count(2147483647)], "works_changed"), 2147483647,
    "the producer's own maximum was refused");
});

test("two valid counts that overflow the safe range refuse a total", () => {
  assert.equal(summedCount([count(Number.MAX_SAFE_INTEGER), count(1)], "works_changed"),
    undefined);
  assert.equal(summedCount([count(MAX_PRODUCER_COUNT), count(0)], "works_changed"),
    MAX_PRODUCER_COUNT);
});

test("refusing a count does not depend on arrival order", () => {
  assert.equal(summedCount([count(-1), count(40)], "works_changed"),
    summedCount([count(40), count(-1)], "works_changed"));
});

// The two change counts measure different things. ChangeTotals returns (int Works, int Versions),
// and the header rendered the WORK count as "received publisher versions", so a reader comparing
// the two numbers was comparing versions to versions and one of them was works. A false dimension
// is not a wording problem.

test("each change count keeps the grain the producer measured", () => {
  const [works, versions] = changeCountLabels(12, 40);
  assert.ok(works!.includes("12") && works!.includes("work"), works);
  assert.ok(!works!.includes("40"), "the work label carried the version count");
  assert.ok(versions!.includes("40") && versions!.includes("version"), versions);
  assert.ok(!versions!.includes("12"), "the version label carried the work count");
});

test("swapping the two inputs swaps the two numbers and nothing else", () => {
  // The discriminating case. If either label read from the wrong argument, one of these two
  // assertions would still pass and the other would not.
  const [worksA, versionsA] = changeCountLabels(12, 40);
  const [worksB, versionsB] = changeCountLabels(40, 12);
  assert.ok(worksA!.includes("12") && worksB!.includes("40"));
  assert.ok(versionsA!.includes("40") && versionsB!.includes("12"));
});

test("a count that could not be stated contributes no label at all", () => {
  assert.deepEqual(changeCountLabels(undefined, undefined), []);
  assert.equal(changeCountLabels(12, undefined).length, 1);
  assert.equal(changeCountLabels(undefined, 40).length, 1);
});

test("the labels say a version was dated, never that wording changed", () => {
  // The producer does not claim a textual change, only that a version carries this date, and the
  // meaning of a version differs between publishers. Timeline semantics are disclosed separately.
  const labels = changeCountLabels(1, 1).join(" ");
  assert.ok(!labels.includes("amend") && !labels.includes("chang"), labels);
  assert.ok(labels.includes("1 work with a new publisher version"), labels);
  assert.ok(labels.includes("1 publisher version dated"), labels);
});
