import assert from "node:assert/strict";
import test from "node:test";
import { HISTORICAL_DENSITY, historicalDensityApplies } from "./notices.ts";

test("density notice applies to a pre-2017 window scoped to Luxembourg", () => {
  assert.equal(historicalDensityApplies("2010-01-01", "lu", []), true);
  assert.equal(historicalDensityApplies("2016-12-31", "lu-legilux", []), true);
  assert.equal(historicalDensityApplies("2016-12-31", "Luxembourg", []), true);
});

test("density notice applies to a pre-2017 unscoped window with Luxembourg rows", () => {
  assert.equal(historicalDensityApplies("2015-06-01", undefined, ["eu", "lu"]), true);
  assert.equal(historicalDensityApplies("2015-06-01", undefined, [undefined, "lu"]), true);
});

test("density notice never applies at or after the 2017 boundary", () => {
  assert.equal(historicalDensityApplies("2017-01-01", "lu", ["lu"]), false);
  assert.equal(historicalDensityApplies("2020-03-01", "lu", ["lu"]), false);
});

test("density notice never applies without Luxembourg in scope", () => {
  assert.equal(historicalDensityApplies("2010-01-01", "eu", ["eu"]), false);
  assert.equal(historicalDensityApplies("2010-01-01", undefined, ["eu", undefined]), false);
  assert.equal(historicalDensityApplies("2010-01-01", undefined, []), false);
});

test("a scoped non-Luxembourg report ignores stray row jurisdictions", () => {
  // The reader explicitly scoped the report; row-level fallback must not override that.
  assert.equal(historicalDensityApplies("2010-01-01", "eu", ["lu"]), false);
});

test("an empty window never applies", () => {
  assert.equal(historicalDensityApplies("", "lu", ["lu"]), false);
});

test("the frozen Decision 41 copy carries its wording and both actions", () => {
  assert.equal(HISTORICAL_DENSITY.heading, "Historical coverage is less dense");
  assert.ok(HISTORICAL_DENSITY.body.includes("fewer dated consolidation states"));
  assert.ok(HISTORICAL_DENSITY.body.includes("not every legal change"));
  assert.ok(HISTORICAL_DENSITY.body.includes("may reflect coverage"));
  assert.deepEqual(HISTORICAL_DENSITY.actions.map((a) => a.label), [
    "View coverage for this period",
    "Open the official publisher",
  ]);
});
