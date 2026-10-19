// What a Luxembourg state's date is (R21; slice S7a of the Luxembourg data design): on lex-v3-luxembourg-index/9 every state
// the API serves names its date's role (`date_role`), and an act's entry into force beside it (`entry_into_force`). The role
// names the date's source:
//   - `applicability`: the publisher catalogue's dateApplicability of a consolidation or code state;
//   - `applicability_in_file`: the act's own file's dateApplicability;
//   - `entry_into_force`: the act's catalogue dateEntryInForce, used when its file states no date. An entry into force, never
//     an applicability date: it is never said as "applicable from".
// An index of schema 8 or 6 names no role (its dates are all the files' own applicability dates), and the readers here give
// null: the pages say what they said before. Every screen that says a Luxembourg state's date (the reading, the dossier, the
// search results and the citation generator) reads the role here, so none says an entry into force as an application.

export const DATE_ROLES = Object.freeze(['applicability', 'applicability_in_file', 'entry_into_force']);

const DATE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * The state's date role, or null when the answer names none (an index of schema 8 or 6). A role outside the three is refused,
 * so a new one is worded before it is shown.
 */
export function readDateRole(state, where) {
  if (!Object.hasOwn(state, 'date_role')) return null;
  const role = state.date_role;
  if (!DATE_ROLES.includes(role)) {
    throw new Error(`${where} names the date role ${JSON.stringify(role)}; a Luxembourg state's date is ${DATE_ROLES.join(', ')}`);
  }
  return role;
}

/** The act's catalogue entry into force the state carries beside its date, or null (none stated, or no role served). */
export function readEntryIntoForce(state, where) {
  if (!Object.hasOwn(state, 'entry_into_force')) return null;
  const value = state.entry_into_force;
  if (value === null) return null;
  if (typeof value !== 'string' || !DATE.test(value)) {
    throw new Error(`${where} entry_into_force is ${JSON.stringify(value)}, not a date`);
  }
  return value;
}

/**
 * The role and entry into force of one state, checked against each other: a state dated by its entry into force carries that
 * date as its entry into force, so the two can never be said as two different dates of one fact.
 */
export function readDateFacts(state, applicabilityDate, where) {
  const dateRole = readDateRole(state, where);
  const entryIntoForce = readEntryIntoForce(state, where);
  if (dateRole === 'entry_into_force' && entryIntoForce !== applicabilityDate) {
    throw new Error(`${where} is dated by its entry into force ${applicabilityDate}, and names ${entryIntoForce} as its entry into force`);
  }
  return Object.freeze({ dateRole, entryIntoForce });
}

/**
 * The end of a state's interval on index 9 (`applicable_to`: the publisher's end, else the next dated state's start, which
 * may be a state whose text is not held), with `endKnown` true; on an index of schema 8 or 6, which names none, `endKnown`
 * false and the next held state's date is the only bound (the review of #987, F3).
 */
export function readInterval(state, where) {
  if (!Object.hasOwn(state, 'applicable_to')) return Object.freeze({ applicableTo: null, endKnown: false });
  const value = state.applicable_to;
  if (value !== null && (typeof value !== 'string' || !DATE.test(value))) {
    throw new Error(`${where} applicable_to is ${JSON.stringify(value)}, not a date or null`);
  }
  return Object.freeze({ applicableTo: value, endKnown: true });
}

/**
 * Whether a Luxembourg state answers `today`: dated on or before it, and on index 9 its interval's end after it (none when
 * the publisher states none and no later state is catalogued), else, on an index of schema 8 or 6, no later held state on
 * or before it. A closed state, or an act followed only by states whose text is not held, does not answer today.
 */
export function holdsToday(state, today) {
  if (!(state.applicabilityDate <= today)) return false;
  // A later held state on or before today replaces it on every index; on index 9 its interval's end bounds it too (no later
  // than that state's start on a consistent answer).
  if (state.nextApplicabilityDate !== null && state.nextApplicabilityDate !== undefined && !(state.nextApplicabilityDate > today)) return false;
  return state.endKnown !== true || state.applicableTo === null || state.applicableTo > today;
}

/** Whether a state's date is an entry into force, which no page says as "applicable from" (R21). */
export const datedByEntryIntoForce = (state) => state?.dateRole === 'entry_into_force';
