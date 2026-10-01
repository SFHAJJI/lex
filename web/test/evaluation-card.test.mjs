// The evaluation card on the Trust and Coverage page, held to the card the platform renders.
//
// The card is `schemas/v3-platform/evaluation-card.json`, rendered by `EvaluationCard.Render` from the
// machine gates run against the real handler (`V3_RENDER_EVALUATION_CARD=1`). `readEvaluationCard`
// must read it whole, recompute its intervals and bounds, and refuse each rule of the card broken; the
// page must print it with its target sentence first.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { EvaluationCardView, renderLiveCoveragePage } from "../.react-build/app.mjs";
import { CARD_ROUTE, EMPTY_CASES_SHA256, NOT_YET_LABELLED, readEvaluationCard, ruleOfThree, wilson95 } from "../scripts/evaluation-card.mjs";

const card = JSON.parse(await readFile(new URL("../../schemas/v3-platform/evaluation-card.json", import.meta.url), "utf8"));
const mutate = (change) => { const copy = structuredClone(card); change(copy); return copy; };
// The page is rendered for hydration, so React marks text boundaries with empty comments; they are not text.
const unescape = (markup) => markup.replaceAll("<!-- -->", "").replaceAll("&#x27;", "'").replaceAll("&quot;", '"').replaceAll("&amp;", "&");

test("the intervals are the platform's: Wilson 95 percent to four places, and the rule of three", () => {
  assert.deepEqual(wilson95(8, 8), [0.6756, 1]);
  assert.deepEqual(wilson95(18, 18), [0.8241, 1]);
  assert.deepEqual(wilson95(4, 4), [0.5101, 1]);
  assert.deepEqual(wilson95(3, 3), [0.4385, 1]);
  assert.deepEqual(wilson95(0, 5), [0, 0.4345]);
  assert.deepEqual(wilson95(5, 10), [0.2366, 0.7634]);
  assert.equal(ruleOfThree(8), 0.375);
  assert.equal(ruleOfThree(9), 0.3333);
  assert.equal(ruleOfThree(3), 1, "never above certainty");
});

test("the rendered card reads whole: every set, its control, the statistical rows not yet labelled, the negative result", () => {
  const view = readEvaluationCard(card);
  assert.match(view.target, /THE MOUNT IS A FIXTURE/);
  assert.deepEqual(view.sets.map((set) => `${set.set}: ${set.arm}`), card.machine_gates.map((set) => `${set.set}: ${set.arm}`));
  assert.equal(view.controls.length, view.sets.length);
  assert.ok(view.statisticalRows.length >= 8 && view.statisticalRows.every((row) => row.status === NOT_YET_LABELLED));
  assert.equal(view.negativeResults.length, 1);
  assert.deepEqual(view.gatesNotPassing, [], "every machine gate passes on the rendered card");
  assert.deepEqual(view.controlsNotCaught, [], "every control caught its shuffle");
  const ndcg = view.sets.flatMap((set) => set.gates).find((gate) => gate.gate === "anchor_ndcg_at_10");
  assert.equal(ndcg.wilson95, null, "a graded mean has no binomial interval");
  assert.equal(ndcg.ruleOfThree, ruleOfThree(ndcg.n));
});

test("each rule of the card, broken, is refused with that rule's reason", () => {
  const firstGate = (copy) => copy.machine_gates[0].gates[0];
  const ndcgGate = (copy) => copy.machine_gates.flatMap((set) => set.gates).find((gate) => gate.gate === "anchor_ndcg_at_10");
  const cases = [
    ["another schema", (c) => { c.schema = "lex-v3-evaluation-card/2"; }, /is not lex-v3-evaluation-card\/1/],
    ["no target", (c) => { c.target = " "; }, /target is not text/],
    ["a verdict outside the vocabulary", (c) => { firstGate(c).verdict = "passed"; }, /verdict "passed" is not one of pass, fail, not_measured/],
    ["a pass below its threshold", (c) => { Object.assign(firstGate(c), { value: 0.875, wilson_95: wilson95(7, 8) }); delete firstGate(c).rule_of_three_failure_upper_95; }, /says pass for 0.875 against its threshold 1/],
    ["a fail at its threshold", (c) => { firstGate(c).verdict = "fail"; }, /says fail for 1 against its threshold 1/],
    ["not measured with a value", (c) => { Object.assign(firstGate(c), { verdict: "not_measured", not_measured_reason: "no_measurable_query" }); }, /is not measured, so it carries no value/],
    ["not measured without a reason", (c) => { Object.assign(firstGate(c), { verdict: "not_measured", value: null }); delete firstGate(c).wilson_95; delete firstGate(c).rule_of_three_failure_upper_95; }, /not_measured_reason null is not one of/],
    ["a reason on a pass", (c) => { firstGate(c).not_measured_reason = "no_measurable_query"; }, /only a gate not measured carries a reason/],
    ["an interval that is not the value's", (c) => { firstGate(c).wilson_95 = [0.7, 1]; }, /Wilson interval is not 0.6756 to 1, the interval of 8 of 8/],
    ["a rate with no interval", (c) => { delete firstGate(c).wilson_95; }, /does not carry wilson_95/],
    ["an interval on a graded mean", (c) => { ndcgGate(c).wilson_95 = [0.7, 1]; }, /graded mean, not a rate/],
    ["a 1 without its rule-of-three bound", (c) => { delete firstGate(c).rule_of_three_failure_upper_95; }, /does not carry rule_of_three_failure_upper_95/],
    ["a bound that is not 3/n", (c) => { firstGate(c).rule_of_three_failure_upper_95 = 0.3; }, /its rule-of-three bound is 0.375/],
    ["a value that is no share of its cases", (c) => { Object.assign(firstGate(c), { value: 0.9, verdict: "fail", wilson_95: [0, 1] }); delete firstGate(c).rule_of_three_failure_upper_95; }, /is not a share of its 8 cases/],
    ["a stratum larger than its set", (c) => { firstGate(c).n = 99; }, /counts 99 cases in its stratum, and its set holds 8/],
    ["a digest that is not one", (c) => { c.machine_gates[0].cases_sha256 = "x"; }, /cases_sha256 is not a SHA-256 digest/],
    ["a missing control", (c) => { c.shuffled_controls.pop(); }, /shuffled controls for 6 case sets, and each set has one/],
    ["controls out of order", (c) => { c.shuffled_controls.reverse(); }, /is the control of .*, and the set in its place is/],
    ["a control verdict outside the vocabulary", (c) => { c.shuffled_controls[0].verdict = "caught"; }, /verdict "caught" is not one of caught_the_shuffle/],
    ["a control over fewer cases, unexplained", (c) => { delete c.shuffled_controls[0].note; }, /ran over fewer cases than its set, and does not say why/],
    ["a statistical row labelled", (c) => { c.statistical_rows[0].status = "labelled"; }, /is not_yet_labelled until its dataset is labelled \(Decision 92\)/],
    ["a negative result without its reversal", (c) => { delete c.negative_results[0].what_would_reverse_it; }, /does not carry what_would_reverse_it/],
    // Review of #792: a set that carries another set's gate, or lacks one, or runs another set's control.
    ["a temporal set carrying a retrieval gate", (c) => { const gate = firstGate(c); gate.gate = "anchor_ndcg_at_10"; delete gate.wilson_95; }, /is a temporal set, so its gates are temporal_exactness, and it carries anchor_ndcg_at_10/],
    ["a retrieval set missing a gate", (c) => { c.machine_gates.find((set) => set.set === "retrieval").gates.pop(); }, /is a retrieval set, so its gates are anchor_ndcg_at_10, no_hit_accuracy, resolver_exactness, and it carries anchor_ndcg_at_10, no_hit_accuracy$/],
    ["a set of no known kind", (c) => { c.machine_gates[0].set = "speed"; }, /set "speed" is not one of temporal, refusal, retrieval/],
    ["a control of another set", (c) => { c.shuffled_controls[0].control = "qrels_shuffle"; }, /control for a temporal set "qrels_shuffle" is not one of date_shuffle/],
  ];
  for (const [what, change, reason] of cases) assert.throws(() => readEvaluationCard(mutate(change)), reason, what);
});

test("the page prints the card after the answer, its target first, every table whole, and asks nothing for it", () => {
  const html = renderLiveCoveragePage();
  const root = html.indexOf('<div id="live-coverage-root">');
  const at = html.indexOf("data-evaluation-card");
  assert.ok(root > 0 && at > root, "the card follows the answer");
  const section = unescape(html.slice(at, html.indexOf("</section>", html.lastIndexOf("data-negative-results"))));
  const target = section.indexOf(`Run over: ${card.target}`);
  assert.ok(target >= 0 && target < section.indexOf("<table"), "what the card was run over, before any number");
  assert.ok(section.includes("Every machine gate on this card passes, and every shuffled control caught its shuffle."));
  assert.equal([...section.matchAll(/<table data-set=/g)].length, card.machine_gates.length);
  for (const set of card.machine_gates) assert.ok(section.includes(`<caption>${set.set}, ${set.arm}: ${set.cases} cases, digest <code>${set.cases_sha256}</code></caption>`));
  assert.ok(section.includes("<td>0.6756 to 1</td>") && section.includes("<td>failure rate below 0.375</td>"));
  assert.ok(section.includes("<td>not a rate</td>"), "anchor nDCG says it is not a rate");
  assert.equal([...section.matchAll(/: not yet labelled\./g)].length, card.statistical_rows.length);
  assert.ok(section.includes(card.negative_results[0].decision));
  assert.ok(!/color|colour/i.test(section), "no verdict is a colour");
  assert.ok(section.includes(`<a href="${CARD_ROUTE}">The same card for machines, as JSON</a>`), "the card links its JSON at the stable route (36 s6)");
});

test("the live build carries the card it is handed, as a release build will hand it (review of #792)", async () => {
  const { buildLive } = await import("../scripts/build-live.mjs");
  const { mkdtemp, rm } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const destination = await mkdtemp(join(tmpdir(), "lex-card-build-"));
  try {
    const release = mutate((c) => { c.target = "RELEASE CARD TARGET: the release's own mounted corpus."; });
    await buildLive(new URL(`file:///${destination.replaceAll("\\", "/")}/`), { card: release });
    const index = await readFile(join(destination, "index.html"), "utf8");
    assert.ok(index.includes("RELEASE CARD TARGET: the release"), "the handed card is the one printed");
    assert.ok(!index.includes("THE MOUNT IS A FIXTURE"), "and not the platform's rendered one");
    const served = JSON.parse(await readFile(join(destination, CARD_ROUTE.slice(1)), "utf8"));
    assert.deepEqual(served, release, "the stable route holds the same card, for machines");
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});

test("a card whose gate fails says so above its tables, and the page will not print a card that breaks its rules", () => {
  const failing = mutate((c) => {
    const gate = c.machine_gates[4].gates[0];
    Object.assign(gate, { verdict: "fail", value: 0.9444, wilson_95: wilson95(17, 18) });
    delete gate.rule_of_three_failure_upper_95;
    c.shuffled_controls[1].verdict = "missed_the_shuffle";
  });
  const markup = unescape(renderToStaticMarkup(h(EvaluationCardView, { view: readEvaluationCard(failing) })));
  assert.ok(markup.includes(`1 gate does not pass (verdict_exact_match in ${failing.machine_gates[4].set}, ${failing.machine_gates[4].arm}: fail)`));
  assert.ok(markup.includes(`1 shuffled control did not catch the shuffle (${failing.shuffled_controls[1].control} in ${failing.shuffled_controls[1].set}, ${failing.shuffled_controls[1].arm}: missed the shuffle)`));
  assert.throws(() => renderLiveCoveragePage({ card: mutate((c) => { c.statistical_rows[0].status = "labelled"; }) }), /Decision 92/);
});

test("a set the gates found nothing to ask of reads as not measured, and only that way (the gates over a mounted corpus)", async () => {
  // The first temporal set emptied as a mount with no Luxembourg state empties it: no case, every gate not measured
  // for no measurable query, and the control not applicable, saying why.
  const empty = (change = () => {}) => mutate((c) => {
    Object.assign(c.machine_gates[0], { cases: 0, cases_sha256: EMPTY_CASES_SHA256 });
    c.machine_gates[0].gates = c.machine_gates[0].gates.map((gate) => ({ gate: gate.gate, verdict: "not_measured", value: null, threshold: gate.threshold, n: 0, not_measured_reason: "no_measurable_query" }));
    Object.assign(c.shuffled_controls[0], { verdict: "not_applicable", cases: 0, cases_sha256: EMPTY_CASES_SHA256, reason: "there is no temporal case to shift: the mount holds no Luxembourg state for this arm" });
    delete c.shuffled_controls[0].note;
    change(c);
  });
  const read = readEvaluationCard(empty());
  assert.equal(read.sets[0].cases, 0);
  assert.equal(read.sets[0].gates[0].verdict, "not_measured");
  assert.equal(read.controls[0].verdict, "not_applicable");

  assert.throws(() => readEvaluationCard(empty((c) => {
    Object.assign(c.machine_gates[0].gates[0], { verdict: "pass", value: 1, not_measured_reason: undefined });
    delete c.machine_gates[0].gates[0].not_measured_reason;
  })), /counts 0 cases|not a whole number of at least 1/, "a scored gate over no case");
  assert.throws(() => readEvaluationCard(empty((c) => { c.machine_gates[0].gates[0].not_measured_reason = "stratum_below_floor"; })),
    /not a whole number of at least 1/, "only no measurable query counts no case");
  assert.throws(() => readEvaluationCard(empty((c) => { Object.assign(c.shuffled_controls[0], { verdict: "caught_the_shuffle" }); })),
    /cases is not a whole number of at least 1/, "a control over no case cannot have caught anything");
  // The review of #841: no case carries the digest of no case, and nothing else.
  const { createHash } = await import("node:crypto");
  assert.equal(EMPTY_CASES_SHA256, createHash("sha256").update("[]").digest("hex"), "the SHA-256 of the empty case list");
  assert.throws(() => readEvaluationCard(empty((c) => { c.machine_gates[0].cases_sha256 = "f".repeat(64); })), /holds no case, so its cases_sha256 is 4f53cda1/);
  assert.throws(() => readEvaluationCard(empty((c) => { c.shuffled_controls[0].cases_sha256 = "e".repeat(64); })), /ran over no case, so its cases_sha256 is 4f53cda1/);
  assert.throws(() => readEvaluationCard(mutate((c) => { c.machine_gates[0].cases = 0; })), /counts \d+ cases in its stratum, and its set holds 0|holds no case/,
    "a set of no case whose gates are measured");
});
