// One calendar and one clock, shared by everything that renders a date.
//
// These lived in `urls.mjs` and nowhere else, so the URL builders refused `2026-99-99` while
// the state banner rendered "Applicable from 2026-99-99" and the envelope strip rendered
// "First observed not-a-timestamp". A validator that only one caller uses is a validator the
// other callers do not have, and the screens that show legal time are exactly the ones where
// an impossible date is worst: a reader cannot tell a publisher's odd date from our own
// broken one.

const ISO_DATE = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/;

// The contracted instant shape: UTC, second precision, Z. The live envelopes carry
// `2026-08-14T23:05:14Z`, and an observation timestamp is evidence, so it is checked for
// shape and then rendered verbatim rather than reformatted.
const UTC_INSTANT = /^([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})Z$/;

function isLeapYear(year) {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

function monthLength(year, month) {
  return [31, isLeapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
}

/**
 * A date that exists. `2026-99-99` and `2025-02-29` match the ISO shape and are not days.
 * Leap years are decidable, so they are decided rather than approximated.
 */
export function isCalendarDate(value) {
  if (typeof value !== 'string') return false;
  const match = ISO_DATE.exec(value);
  if (!match) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || day < 1) return false;
  return day <= monthLength(year, month);
}

/** An instant in the contracted shape: a real calendar day, a real time of day, UTC. */
export function isUtcInstant(value) {
  if (typeof value !== 'string') return false;
  const match = UTC_INSTANT.exec(value);
  if (!match) return false;
  if (!isCalendarDate(`${match[1]}-${match[2]}-${match[3]}`)) return false;
  const hours = Number(match[4]);
  const minutes = Number(match[5]);
  const seconds = Number(match[6]);
  // 60 is a leap second and the publishers do not emit one; admitting it would mean
  // admitting a value nothing in this system can order against the others.
  return hours <= 23 && minutes <= 59 && seconds <= 59;
}

/** True when `from` is on or before `to`. Both must already be calendar dates. */
export function isOrderedInterval(from, to) {
  if (!isCalendarDate(from)) return false;
  if (to === null || to === undefined) return true;
  if (!isCalendarDate(to)) return false;
  return from <= to; // ISO dates compare correctly as strings.
}

/** Throws with the field named, so a caller cannot render an impossible date by accident. */
export function requireCalendarDate(value, field) {
  if (!isCalendarDate(value)) {
    throw new Error(
      `${field} is not a calendar date: ${JSON.stringify(value)}; a date that does not exist ` +
        'reads to a reader as the publisher having recorded it',
    );
  }
  return value;
}

export function requireUtcInstant(value, field) {
  if (!isUtcInstant(value)) {
    throw new Error(
      `${field} is not a UTC instant in the contracted shape: ${JSON.stringify(value)}`,
    );
  }
  return value;
}

// "Today" in Luxembourg (the specification of record's R13): the calendar date in Europe/Luxembourg at a UTC instant,
// by the European Union's summer-time rule coded here as the API codes it (`LuxembourgCalendar`, plan 0.8), never from
// a zone database: the answer is the same in a browser anywhere, in Node under any TZ, and in the server's image, which
// carries none. UTC+01:00, and UTC+02:00 from 01:00 UTC on the last Sunday of March to 01:00 UTC on the last Sunday of
// October (Directive 2000/84/EC, Articles 2 and 3); an instant at a change already has the new offset. Held from
// 1996-01-01T00:00:00Z (before 1996 summer time ended in September) to 2100-01-01T00:00:00Z. One table holds this and
// the API's rule to the same answers: tests/Lex.V3.Tests/Fixtures/LuxembourgToday/luxembourg-today.json.

const LUXEMBOURG_FROM = Date.UTC(1996, 0, 1);
const LUXEMBOURG_UNTIL = Date.UTC(2100, 0, 1);

// An instant as the API writes it (seven fractional digits) or as a page does (none, or three), always UTC. The fraction
// is truncated, never rounded: 21:59:59.9999999Z is still before 22:00, and no change falls between two seconds.
const LUXEMBOURG_INSTANT = /^([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.[0-9]+)?Z$/;

function luxembourgMilliseconds(instant) {
  const match = typeof instant === 'string' ? LUXEMBOURG_INSTANT.exec(instant) : null;
  if (!match || !isUtcInstant(`${match[1]}-${match[2]}-${match[3]}T${match[4]}:${match[5]}:${match[6]}Z`)) {
    throw new Error(`not a UTC instant: ${JSON.stringify(instant)}`);
  }
  const [year, month, day, hours, minutes, seconds] = match.slice(1, 7).map(Number);
  const milliseconds = Date.UTC(year, month - 1, day, hours, minutes, seconds);
  if (milliseconds < LUXEMBOURG_FROM || milliseconds >= LUXEMBOURG_UNTIL) {
    throw new RangeError(`Luxembourg's date is held from 1996-01-01T00:00:00Z to 2100-01-01T00:00:00Z, not at ${instant}`);
  }
  return milliseconds;
}

/** 01:00 UTC on the last Sunday of `month` (1 to 12) of `year`, in milliseconds. */
function lastSundayAtOneUtc(year, month) {
  if (!Number.isInteger(year) || year < 1996 || year > 2099) {
    throw new RangeError(`Luxembourg's summer time is held from 1996 to 2099, not in ${year}`);
  }
  const last = new Date(Date.UTC(year, month, 0));
  return Date.UTC(year, month - 1, last.getUTCDate() - last.getUTCDay(), 1);
}

const utcInstant = (milliseconds) => new Date(milliseconds).toISOString().replace('.000Z', 'Z');

/** When summer time starts in `year`, as a UTC instant: 01:00 UTC on the last Sunday of March. */
export function luxembourgSummerTimeStarts(year) {
  return utcInstant(lastSundayAtOneUtc(year, 3));
}

/** When summer time ends in `year`, as a UTC instant: 01:00 UTC on the last Sunday of October. */
export function luxembourgSummerTimeEnds(year) {
  return utcInstant(lastSundayAtOneUtc(year, 10));
}

function luxembourgOffsetHours(milliseconds) {
  const year = new Date(milliseconds).getUTCFullYear();
  return milliseconds >= lastSundayAtOneUtc(year, 3) && milliseconds < lastSundayAtOneUtc(year, 10) ? 2 : 1;
}

/** Luxembourg's offset from UTC at a UTC instant: '+02:00' in summer time, '+01:00' otherwise. */
export function luxembourgOffset(instant) {
  return `+0${luxembourgOffsetHours(luxembourgMilliseconds(instant))}:00`;
}

/** The calendar date in Luxembourg at a UTC instant, as `YYYY-MM-DD`: "today" for an answer observed then. */
export function luxembourgDate(instant) {
  const milliseconds = luxembourgMilliseconds(instant);
  return new Date(milliseconds + luxembourgOffsetHours(milliseconds) * 3_600_000).toISOString().slice(0, 10);
}
