import assert from "node:assert/strict";
import test from "node:test";
import { assessAnchorPopulation, scopeOutline } from "./comparison.ts";

const provision = (anchor: string) => ({ anchor });

test("whole-document comparison refuses wholesale publisher anchor churn", () => {
  const before = [
    ...Array.from({ length: 12 }, (_, i) => provision(`old_${i}`)),
    ...Array.from({ length: 3 }, (_, i) => provision(`stable_${i}`)),
  ];
  const after = [
    ...Array.from({ length: 12 }, (_, i) => provision(`new_${i}`)),
    ...Array.from({ length: 3 }, (_, i) => provision(`stable_${i}`)),
  ];

  assert.equal(assessAnchorPopulation(before, after).comparable, false);
});

test("ordinary insertions remain comparable when most anchors are continuous", () => {
  const before = Array.from({ length: 12 }, (_, i) => provision(`art_${i}`));
  const after = [...before, provision("art_12"), provision("art_13")];

  assert.equal(assessAnchorPopulation(before, after).comparable, true);
});

test("small documents without anchor continuity are refused too", () => {
  assert.equal(assessAnchorPopulation([provision("old")], [provision("new")]).comparable, false);
});

test("an article-scoped comparison cannot leak the rest of an outline", () => {
  const outline = [provision("art_N10060"), provision("unrelated_1"), provision("unrelated_2")];

  assert.deepEqual(scopeOutline(outline, "art_N10060"), [provision("art_N10060")]);
});
