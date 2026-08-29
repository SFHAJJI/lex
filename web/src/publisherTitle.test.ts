import assert from "node:assert/strict";
import test from "node:test";
import { publisherStatedDate, titleDateDisagreement } from "./publisherTitle.ts";

test("a day-first publisher date is read as an ISO date", () => {
  assert.equal(
    publisherStatedDate("Version consolidee applicable au 02/11/1999 : Arrete du Gouvernement"),
    "1999-11-02",
  );
});

test("a single-digit day and month still parse", () => {
  assert.equal(publisherStatedDate("Version consolidee applicable au 5/3/1986 : Arrete"), "1986-03-05");
});

test("an ISO publisher date is read as itself", () => {
  assert.equal(publisherStatedDate("Version consolidee applicable au 1999-11-02 : Arrete"), "1999-11-02");
});

test("a title with no publisher date yields nothing", () => {
  assert.equal(publisherStatedDate("Loi du 1er aout 2018 relative a la protection des donnees"), undefined);
  assert.equal(publisherStatedDate(undefined), undefined);
});

test("an impossible date is rejected rather than reported", () => {
  assert.equal(publisherStatedDate("Version consolidee applicable au 32/13/1999 : Arrete"), undefined);
});

// The case this module exists for. Measured at 272 of 2,782 records on the mounted LU index.
test("a publisher date differing from the displayed date is reported", () => {
  assert.deepEqual(
    titleDateDisagreement("Version consolidee applicable au 02/11/1999 : Arrete", "2000-12-16"),
    { publisher: "1999-11-02", displayed: "2000-12-16" },
  );
});

test("agreement reports nothing, so the prefix stays noise when it is noise", () => {
  assert.equal(
    titleDateDisagreement("Version consolidee applicable au 16/12/2000 : Arrete", "2000-12-16"),
    undefined,
  );
});

test("a displayed timestamp compares on its date part", () => {
  assert.equal(
    titleDateDisagreement("Version consolidee applicable au 16/12/2000 : Arrete", "2000-12-16T00:00:00Z"),
    undefined,
  );
});

test("nothing is claimed when either side is missing", () => {
  assert.equal(titleDateDisagreement("Loi du 1er aout 2018", "2018-08-01"), undefined);
  assert.equal(titleDateDisagreement("Version consolidee applicable au 02/11/1999 : Arrete", undefined), undefined);
});
