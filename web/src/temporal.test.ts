import assert from "node:assert/strict";
import test from "node:test";
import {
  intervalLabel, latestStateLabel, temporalStatusLabel, futureStateLabel,
} from "./temporal.ts";

test("Luxembourg dates retain applicability language", () => {
  const work = "lu-legilux:loi-2020-01-01-a1";
  assert.equal(intervalLabel(work, "2020-01-01"), "in force 2020-01-01 → open");
  assert.equal(temporalStatusLabel(work, undefined), "in force");
  assert.equal(futureStateLabel(work, 2), "2 not yet in force");
  assert.equal(latestStateLabel(work), "today");
});

test("EU consolidation states never claim an entry-into-force date", () => {
  const work = "eu-eurlex:32016R0679";
  assert.equal(intervalLabel(work, "2016-05-04"), "publisher version 2016-05-04 → latest held");
  assert.equal(temporalStatusLabel(work, undefined), "latest publisher version");
  assert.equal(temporalStatusLabel(work, "2018-05-24"), "earlier publisher version");
  assert.equal(futureStateLabel(work, 2), "2 publisher versions dated after today");
  assert.equal(latestStateLabel(work), "latest held");
});
