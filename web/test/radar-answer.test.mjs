// The V3 change radar reader, held to the answers the real handler sent.
//
// The answers come from `schemas/v3-platform/answer-samples.json` (operation `changes_in_period`),
// captured by driving the real handler: a window holding the one-state fixture's first held state,
// and a window holding both states of the two-state fixture (the later one compared with the first:
// one article changed, one added, one removed). Each rule the answer states about itself is then
// broken once.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { RADAR_REASONS, readChanges } from "../scripts/radar-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

async function capturedAnswers() {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  const rows = parsed.sampled.filter((sample) => sample.operation === "changes_in_period");
  assert.equal(rows.length, 2, "the census holds the two radars this reader is written against");
  for (const row of rows) assert.equal(row.object_type, "change_list");
  const [first, both] = rows.map((row) => withDigests(row.answer));
  return { first, both };
}

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === PLACEHOLDER) { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

function mutate(answer, change) {
  const copy = structuredClone(answer);
  change(copy);
  return copy;
}

test("a window holding a first held state reads: one row, no baseline, its reason", async () => {
  const { first } = await capturedAnswers();
  const view = readChanges(first);
  assert.equal(view.rows.length, 1);
  assert.equal(view.rows[0].reason, "first_held_state");
  assert.equal(view.rows[0].baseline, null);
  assert.equal(view.rows[0].wordingChanged, null);
  assert.ok(view.caveat.includes("does not by itself assert"));
});

test("a window holding both states reads: the later one compared with its baseline, with the diff that asks for the pair", async () => {
  const { both } = await capturedAnswers();
  const view = readChanges(both);
  assert.deepEqual(view.rows.map((row) => row.reason), ["first_held_state", null]);
  const later = view.rows[1];
  assert.equal(later.wordingChanged, true);
  assert.deepEqual(later.counts, { unchanged: 47, changed: 1, added: 1, removed: 1 });
  assert.deepEqual(later.diff, { identifier: `/lu-legilux/${later.workKey}`, dateFrom: later.baseline.applicabilityDate, dateTo: later.state.applicabilityDate, language: "fra" });
  assert.equal(view.population.versionsInWindow, 2);
  assert.deepEqual(RADAR_REASONS, ["first_held_state", "ambiguous_version", "ambiguous_baseline", "profiles_differ"]);
});

test("each rule the radar states about itself is refused when broken, with that rule's reason", async () => {
  const { first, both } = await capturedAnswers();
  const later = (a) => a.changes[1];
  const permalinkLike = (a, digit) => `/lu-legilux/${a.changes[0].work_key}/${a.changes[0].state.applicability_date}--${digit.repeat(64)}`;
  const cases = [
    ["another publisher's radar", both, (a) => { a.publisher = "eu-eurlex"; }, /Luxembourg \(lu-legilux\) radar only/],
    ["a window that is not the dates'", both, (a) => { a.window_to = "2030-01-01"; }, /closed interval between the two dates/],
    ["a state outside the window", both, (a) => { a.requested_date_to = a.window_to = "2025-03-06"; }, /outside the window/],
    ["a resolve to another state", both, (a) => { later(a).resolve.identifier = a.changes[0].resolve.identifier; }, /resolves to another state/],
    ["a change against its counts", both, (a) => { later(a).wording_changed = false; }, /says wording_changed is false, and its counts say otherwise/],
    ["a diff that asks for another pair", both, (a) => { later(a).diff.date_from = "2020-01-01"; }, /diff parameters do not ask for its baseline and its state/],
    ["a compared row with a reason", both, (a) => { later(a).reason = "profiles_differ"; }, /carries no wording change, counts or diff/],
    ["a compared row without its baseline", both, (a) => { later(a).baseline = null; }, /has the one baseline it was compared with/],
    ["a baseline not followed by its state", both, (a) => { later(a).baseline.next_applicability_date = null; }, /not by the state it is the baseline of/],
    ["profiles that differ, compared", both, (a) => { later(a).state.rule_profile_sha256s = ["a".repeat(64)]; }, /different rule profiles/],
    ["a reason outside the vocabulary", first, (a) => { a.changes[0].reason = "not_interesting"; }, /is not one of/],
    ["a first held state with a baseline", both, (a) => { a.changes[0].baseline = structuredClone(later(a).baseline); a.changes[0].baseline.next_applicability_date = a.changes[0].state.applicability_date; a.changes[0].baseline.applicability_date = "2020-01-01"; a.changes[0].baseline.stable_coordinate = `/lu-legilux/${a.changes[0].work_key}/2020-01-01`; a.changes[0].baseline.permalink = `${a.changes[0].baseline.stable_coordinate}--${a.changes[0].baseline.state_sha256}`; }, /first held state, with no baseline/],
    ["an ambiguity without candidates", first, (a) => { a.changes[0].reason = "ambiguous_version"; }, /names candidate states for ambiguous_version/],
    ["an ambiguity not naming its own state", first, (a) => { a.changes[0].reason = "ambiguous_version"; a.changes[0].candidates = [permalinkLike(a, "a"), permalinkLike(a, "b")]; }, /its own state is among the candidates/],
    ["candidates on a first held state", first, (a) => { a.changes[0].candidates = [permalinkLike(a, "a"), permalinkLike(a, "b")]; }, /names no candidate states for first_held_state/],
    ["rows out of date order", both, (a) => { a.changes.reverse(); }, /publisher date order/],
    ["a truncated page without its next date", both, (a) => { a.truncated = true; }, /exactly when it is truncated/],
    ["a population that is not the page's", both, (a) => { a.population.versions_in_window = 3; }, /counts 3 versions in the window, and the untruncated page holds 2/],
    ["an overlap flag against the dates", both, (a) => { a.population.window_overlaps_what_is_held = false; }, /window meets the held dates/],
    ["a missing member", both, (a) => { delete later(a).state.permalink; }, /does not carry permalink/],
    ["a digest the census left unfilled", both, (a) => { a.index_sha256 = PLACEHOLDER; }, /index_sha256 is not a SHA-256 digest/],
  ];
  for (const [what, base, change, reason] of cases) {
    assert.throws(() => readChanges(mutate(base, change)), reason, what);
  }
});
