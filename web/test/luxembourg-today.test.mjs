// "Today" in Luxembourg by the coded EU rule (`temporal.mjs`, plan 0.8; R13), held to the table the API's rule
// (`LuxembourgCalendar`) is held to, and the table itself held to the IANA zone database through Intl, which Node
// carries: so the two coded rules and the zone database agree, and neither rule reads a zone of its own.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import { luxembourgDate, luxembourgOffset, luxembourgSummerTimeEnds, luxembourgSummerTimeStarts } from "../scripts/temporal.mjs";

const TABLE = JSON.parse(readFileSync(new URL("../../tests/Lex.V3.Tests/Fixtures/LuxembourgToday/luxembourg-today.json", import.meta.url), "utf8"));

// The zone database's answer at an instant, in milliseconds (the 7-digit instants' last four digits truncated).
const ZONE = new Intl.DateTimeFormat("en-CA", {
  timeZone: "Europe/Luxembourg", year: "numeric", month: "2-digit", day: "2-digit", timeZoneName: "longOffset",
});
function zoneAt(milliseconds) {
  const parts = Object.fromEntries(ZONE.formatToParts(milliseconds).map(({ type, value }) => [type, value]));
  return { date: `${parts.year}-${parts.month}-${parts.day}`, offset: parts.timeZoneName.replace("GMT", "") };
}
const millisecondsOf = (instant) => Date.parse(instant.replace(/(\.\d{3})\d+Z$/, "$1Z"));

test("every instant of the shared table has its Luxembourg date", () => {
  assert.equal(TABLE.instants.length, 191);
  for (const row of TABLE.instants) assert.equal(luxembourgDate(row.instant), row.date, `${row.instant}: ${row.case}`);
});

test("every instant of the shared table has its offset", () => {
  for (const row of TABLE.instants) assert.equal(luxembourgOffset(row.instant), row.offset, `${row.instant}: ${row.case}`);
});

test("summer time starts and ends where the shared table says, each year it names", () => {
  assert.equal(TABLE.summer_time.length, 17);
  for (const { year, starts, ends } of TABLE.summer_time) {
    assert.deepEqual([luxembourgSummerTimeStarts(year), luxembourgSummerTimeEnds(year)], [starts, ends], String(year));
  }
});

test("an instant outside the rule's range is refused", () => {
  assert.equal(TABLE.refused.length, 2);
  for (const row of TABLE.refused) assert.throws(() => luxembourgDate(row.instant), RangeError, row.case);
});

test("the shared table is the IANA zone database's, at every instant it holds", () => {
  for (const row of TABLE.instants) {
    assert.deepEqual(zoneAt(millisecondsOf(row.instant)), { date: row.date, offset: row.offset }, `${row.instant}: ${row.case}`);
  }
});

// The drift alarm, as the API's test is one: should the Union or Luxembourg change the rule, the zone database follows
// and this fails, and the rule is changed. Each night at 22:30 UTC, the only hour whose date the season's offset
// decides, from 1996 to 2099, and the millisecond before and at each change.
test("the rule is the IANA zone database's each night and at each change from 1996 to 2099", () => {
  const differing = [];
  const instants = [];
  for (let day = Date.UTC(1996, 0, 1, 22, 30); day < Date.UTC(2100, 0, 1); day += 86_400_000) instants.push(day);
  for (let year = 1996; year <= 2099; year += 1) {
    for (const change of [luxembourgSummerTimeStarts(year), luxembourgSummerTimeEnds(year)]) {
      instants.push(Date.parse(change) - 1, Date.parse(change));
    }
  }
  assert.equal(instants.length, 37_986 + 4 * 104);
  for (const milliseconds of instants) {
    const instant = new Date(milliseconds).toISOString();
    const coded = { date: luxembourgDate(instant), offset: luxembourgOffset(instant) };
    if (JSON.stringify(coded) !== JSON.stringify(zoneAt(milliseconds))) differing.push(instant);
  }
  assert.deepEqual(differing.slice(0, 5), []);
});

test("the date does not depend on the host's own zone", () => {
  const before = process.env.TZ;
  try {
    for (const [zone, minutesBehind] of [["Pacific/Kiritimati", -840], ["America/Adak", 600], ["UTC", 0]]) {
      process.env.TZ = zone;
      assert.equal(new Date(Date.UTC(2026, 0, 15)).getTimezoneOffset(), minutesBehind, `the host now keeps ${zone}`);
      for (const row of TABLE.instants) assert.equal(luxembourgDate(row.instant), row.date, `${zone}, ${row.instant}: ${row.case}`);
    }
  } finally {
    if (before === undefined) delete process.env.TZ;
    else process.env.TZ = before;
  }
});
