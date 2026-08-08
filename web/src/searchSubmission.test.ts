import assert from "node:assert/strict";
import test from "node:test";
import { searchSubmission } from "./searchSubmission.ts";

test("a bare date becomes one atomic search-state update", () => {
  assert.deepEqual(searchSubmission("2022-03-15"), {
    query: "",
    asOf: "2022-03-15",
  });
});

test("ordinary text remains a query and does not replace the selected date", () => {
  assert.deepEqual(searchSubmission("capital ratios"), {
    query: "capital ratios",
  });
});
