import test from "node:test";
import assert from "node:assert/strict";
import { jurisdictionLabel } from "./facets.ts";

// A malformed publisher answer must not be able to blank the workspace. searchResults groups a
// hit with no jurisdiction under the literal "Other", and Intl.DisplayNames throws a RangeError
// on an ill-formed region subtag rather than returning undefined, so this path was a crash
// reachable from a single bad envelope.

test("a known region renders its English name", () => {
  assert.equal(jurisdictionLabel("LU"), "Luxembourg");
  assert.equal(jurisdictionLabel("lu"), "Luxembourg");
});

test("the union is named directly, not through the region table", () => {
  assert.equal(jurisdictionLabel("EU"), "European Union");
  assert.equal(jurisdictionLabel("eu"), "European Union");
});

test("an ill-formed code is shown as given rather than throwing", () => {
  // The exact value searchResults substitutes when an envelope omits jurisdiction.
  assert.equal(jurisdictionLabel("Other"), "Other");
  assert.equal(jurisdictionLabel("X"), "X");
  assert.equal(jurisdictionLabel("12"), "12");
  assert.equal(jurisdictionLabel(""), "");
});

test("a well-formed but unknown region falls back to the code", () => {
  // These reach Intl and come back undefined or a placeholder rather than throwing, so the
  // guard above must not be what is covering them.
  assert.equal(typeof jurisdictionLabel("ZZ"), "string");
  assert.equal(typeof jurisdictionLabel("999"), "string");
});
