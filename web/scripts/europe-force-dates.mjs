// The EU act's dates as the publisher records them (`force_dates`, beside the wordings of an EU dossier, timeline, as_of
// and evidence_bundle over an EU index /6; the plan's item 2.1, R79's E1, R21), read for the live screens. Each date is a
// value of its own with its role (entry into force, application, end of validity, the publisher's deadline, signature, or
// a date the site gives no role), the value exactly as served, the publisher's code and qualifier word for word, and the
// statement it was read from. They are the act's dates: never a wording's, never used to choose one. An answer over an
// older EU index carries no `force_dates`, and this reads it as null; a row that breaks the answer's own rules is drift
// and is thrown, never rendered. It renders nothing itself.

/** The roles a force date takes, in the order an answer says them. */
export const EUROPE_FORCE_DATE_ROLES = Object.freeze([
  'entry_into_force',
  'application',
  'end_of_validity',
  'deadline',
  'signature',
  'not_pinned',
]);

/** The four roles the plan's item 2.1 names, which an answer says are not held when the act has no date of them. */
export const EUROPE_FORCE_DATE_NAMED_ROLES = Object.freeze(['entry_into_force', 'application', 'end_of_validity', 'deadline']);

/** The publisher's open end: a statement that validity does not end, never a date. */
export const EUROPE_OPEN_END = '9999-12-31';

const XSD_DATE = 'http://www.w3.org/2001/XMLSchema#date';
const DIGEST = /^[0-9a-f]{64}$/;
const LEXICAL = /^\d{4}(?:-\d{2}(?:-\d{2})?)?$/;
const CARRIER = /\{([^{}|]+)\|[^{}]*\}/g;

/** The publisher's note with each `{CODE|IRI}` carrier written as its code, nothing else changed (the answer's note rule). */
export function noteCodes(note) {
  return note === null ? null : note.replace(CARRIER, (_, code) => code);
}

/**
 * The act's force dates an EU answer carries, or null when it carries none (an answer over an EU index /5 or older).
 *
 * @param {object} answer the `result.value` of an EU dossier, timeline, as_of or evidence_bundle
 * @param {string} where how a thrown error names the answer
 * @returns {object|null} a frozen view `{workIri, seedCelex, dates, rolesNotHeld, conflicts, scope, roleRule, noteRule, notHeldRule}`
 */
export function readEuropeForceDates(answer, where) {
  if (!Object.hasOwn(answer, 'force_dates')) return null;
  const value = answer.force_dates;
  const label = `${where} force_dates`;
  if (value === null || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${label} is not an object`);
  const own = (holder, key, at) => {
    if (!Object.hasOwn(holder, key)) throw new Error(`${at} has no ${key}`);
    return holder[key];
  };
  const text = (holder, key, at) => {
    const found = own(holder, key, at);
    if (typeof found !== 'string' || found.length === 0) throw new Error(`${at}.${key} is not text`);
    return found;
  };
  const textOrNull = (holder, key, at) => {
    const found = own(holder, key, at);
    if (found !== null && (typeof found !== 'string' || found.length === 0)) throw new Error(`${at}.${key} is neither text nor null`);
    return found;
  };
  const list = (holder, key, at) => {
    const found = own(holder, key, at);
    if (!Array.isArray(found)) throw new Error(`${at}.${key} is not a list`);
    return found;
  };

  const seedCelex = text(value, 'seed_celex', label);
  for (const key of ['celex', 'seed_celex']) {
    if (Object.hasOwn(answer, key) && typeof answer[key] === 'string' && answer[key] !== seedCelex) {
      throw new Error(`${label} are ${seedCelex}'s dates, and the answer is of ${answer[key]}`);
    }
  }

  const seen = new Set();
  const dates = Object.freeze(list(value, 'dates', label).map((row, index) => {
    const at = `${label}.dates[${index}]`;
    if (row === null || typeof row !== 'object' || Array.isArray(row)) throw new Error(`${at} is not an object`);
    const role = text(row, 'role', at);
    if (!EUROPE_FORCE_DATE_ROLES.includes(role)) throw new Error(`${at} has the role ${role}, which no force date takes`);
    const date = text(row, 'date', at);
    if (!LEXICAL.test(date)) throw new Error(`${at} dates ${JSON.stringify(date)}, which is no publisher date`);
    const datatype = text(row, 'datatype', at);
    const openEnded = own(row, 'open_ended', at);
    if (typeof openEnded !== 'boolean') throw new Error(`${at}.open_ended is true or false`);
    if (openEnded !== (date === EUROPE_OPEN_END && datatype === XSD_DATE)) {
      throw new Error(`${at} says open_ended is ${openEnded} of ${date}; only the publisher's ${EUROPE_OPEN_END} at xsd:date is its open end`);
    }
    const code = textOrNull(row, 'publisher_code', at);
    const note = textOrNull(row, 'publisher_note', at);
    const codes = textOrNull(row, 'publisher_note_codes', at);
    if (codes !== noteCodes(note)) throw new Error(`${at} gives the note's codes as ${JSON.stringify(codes)}, not its note's`);
    if (role === 'entry_into_force' && code !== 'EV') throw new Error(`${at} is an entry into force with the code ${code}; the code EV gives that role`);
    if (role === 'application' && code !== 'MA') throw new Error(`${at} is an application with the code ${code}; the code MA gives that role`);
    if (role === 'not_pinned' && (code === 'EV' || code === 'MA')) throw new Error(`${at} gives no role to the code ${code}, which has one`);
    const identity = text(row, 'date_identity_sha256', at);
    if (!DIGEST.test(identity)) throw new Error(`${at}.date_identity_sha256 is not a digest`);
    if (seen.has(identity)) throw new Error(`${at} repeats a date the answer already said`);
    seen.add(identity);
    return Object.freeze({
      role, date, datatype, openEnded, code, note, codes,
      property: text(row, 'date_property', at),
      statement: text(row, 'statement', at),
      identity,
    });
  }));
  for (let index = 1; index < dates.length; index += 1) {
    const [before, after] = [dates[index - 1], dates[index]];
    const order = EUROPE_FORCE_DATE_ROLES.indexOf(before.role) - EUROPE_FORCE_DATE_ROLES.indexOf(after.role);
    if (order > 0 || (order === 0 && before.date > after.date)) {
      throw new Error(`${label} says ${after.role} ${after.date} after ${before.role} ${before.date}; the dates are in role order, then date order`);
    }
  }

  const rolesNotHeld = Object.freeze(list(value, 'roles_not_held', label).map((role, index) => {
    if (!EUROPE_FORCE_DATE_NAMED_ROLES.includes(role)) throw new Error(`${label}.roles_not_held[${index}] is ${JSON.stringify(role)}`);
    return role;
  }));
  const expected = EUROPE_FORCE_DATE_NAMED_ROLES.filter((role) => !dates.some((row) => row.role === role));
  if (JSON.stringify(rolesNotHeld) !== JSON.stringify(expected)) {
    throw new Error(`${label} says ${JSON.stringify(rolesNotHeld)} are not held, and its dates leave ${JSON.stringify(expected)} without one`);
  }

  const entries = [...new Set(dates.filter((row) => row.role === 'entry_into_force').map((row) => row.date))].sort();
  const conflicts = Object.freeze(list(value, 'conflicts', label).map((row, index) => {
    const at = `${label}.conflicts[${index}]`;
    if (text(row, 'role', at) !== 'entry_into_force') throw new Error(`${at} is not the entry into force`);
    return Object.freeze({ role: 'entry_into_force', dates: Object.freeze(list(row, 'dates', at).map((date) => String(date))) });
  }));
  if ((entries.length > 1) !== (conflicts.length === 1) || conflicts.length > 1
    || (conflicts.length === 1 && JSON.stringify(conflicts[0].dates) !== JSON.stringify(entries))) {
    throw new Error(`${label} names ${conflicts.length} conflicts, and its dates hold ${entries.length} entry-into-force dates`);
  }

  return Object.freeze({
    workIri: text(value, 'publisher_work_id', label),
    seedCelex,
    dates,
    rolesNotHeld,
    conflicts,
    scope: text(value, 'scope', label),
    roleRule: text(value, 'role_rule', label),
    noteRule: text(value, 'note_rule', label),
    notHeldRule: text(value, 'not_held_rule', label),
    conflictRule: text(value, 'conflict_rule', label),
  });
}
