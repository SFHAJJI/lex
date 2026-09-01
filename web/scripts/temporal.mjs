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
