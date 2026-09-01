// What a `lex_id` identifies, parsed in one place.
//
// A `lex_id` is `publisher:work:state`. Five screens split that string by hand, each with its
// own idea of what counted as valid, and the weakest idea decided what the strictest screen
// could be handed. `compare.mjs` had already learned the lesson the hard way: splitting and
// taking the first two parts made any two strings comparable, so `garbage` and `garbage` named
// the same work and a diff between them was legal. It fixed that locally. The four other call
// sites did not, because there was nothing for them to reuse.
//
// So the identity is a concept with a name and one implementation, and it is the strict one.
// Segments are checked against the same rule the routable URL builders use, which keeps every
// screen's notion of a work identical to the one a permalink can address. A screen cannot be
// more permissive than the URL space it links into.
//
// The distinction that matters most here is the last function. A set of records that all belong
// to one work is a different thing from a set of records that merely arrived together, and only
// the first supports a claim about that work's history. A gap and an overlap are both claims
// about one publisher's record of one instrument; computed across two unrelated works they are
// arithmetic about nothing, and they render as the publisher contradicting itself.

import { isSafeSegment } from './urls.mjs';

/**
 * The publisher, work and state a `lex_id` names.
 *
 * The state segment keeps its colons. Truncating at the second one would silently rename a
 * state rather than refuse an identifier this module does not understand.
 *
 * @param {unknown} lexId
 * @param {string} where  what to name in the error
 */
export function identityOf(lexId, where) {
  const parts = String(lexId ?? '').split(':');
  const state = parts.slice(2).join(':');
  if (
    parts.length < 3 ||
    !isSafeSegment(parts[0]) ||
    !isSafeSegment(parts[1]) ||
    state.length === 0
  ) {
    throw new Error(
      `${where} has lex_id ${JSON.stringify(lexId)}, which does not name a publisher, a work ` +
        'and a state',
    );
  }
  return Object.freeze({
    publisher: parts[0],
    work: parts[1],
    state,
    /** The work, as one string, for comparing two records without re-splitting them. */
    workKey: `${parts[0]}:${parts[1]}`,
  });
}

/**
 * The publisher a record belongs to.
 *
 * A convenience over `identityOf`, kept because "which publisher is this row" is the single
 * most common question and writing it out invites the two-part split this module exists to end.
 */
export function publisherOf(lexId, where) {
  return identityOf(lexId, where).publisher;
}

/**
 * The one work every record belongs to, or a refusal naming the works that were mixed.
 *
 * This is the precondition for any statement about a work's history. A timeline's gaps and
 * overlaps are derived by comparing intervals, and comparing the intervals of two unrelated
 * instruments produces sentences that look like findings: "both cover part of the same period",
 * "the publisher ranks neither". Said across two works those are false, and they are false in
 * the worst direction, because they attribute to a publisher a contradiction it never made.
 *
 * @param {Array} records  anything carrying a `lex_id`
 * @param {string} where   what to name in the error
 */
export function oneWorkAcross(records, where) {
  if (!Array.isArray(records) || records.length === 0) {
    throw new Error(`${where} needs at least one record to identify the work it describes`);
  }
  const identities = records.map((record, index) =>
    identityOf(record?.lex_id, `${where} record ${index + 1}`),
  );
  const distinct = [...new Set(identities.map((identity) => identity.workKey))];
  if (distinct.length !== 1) {
    throw new Error(
      `${where} mixes ${distinct.length} works (${distinct.join(', ')}); a statement about a ` +
        "work's history is a statement about one work, and intervals compared across two " +
        'unrelated instruments render as the publisher contradicting itself',
    );
  }
  return identities[0];
}
