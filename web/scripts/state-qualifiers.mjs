// The three qualifications a dated object can carry, and the reason each is mandatory.
//
// Each of these is a case where the publisher's record does not say what a reader will
// assume it says, and each is common rather than exotic.
//
// A validity conflict is two publisher dates on one thing. Measured live today: four of the
// five held wording states of the flagship article carry a per-article date of 2020-11-01
// inside enclosing states applicable from four later dates. Across the Luxembourg corpus it
// is 39.8 percent of provision states. Both dates are the publisher's, neither is derived,
// and the product does not decide which controlled. So the badge shows both and says so.
//
// A provisional state is one the publisher has scheduled and which has not begun. There are
// twenty-three works whose valid_from is in the future, out to 2030. Rendered without a
// mark, a future state reads as current law.
//
// A hole is a period no held state covers. Hatched, captioned, and never closed by
// implication: the continuity a reader infers across a gap is their inference, not the
// publisher's assertion, and the caption says which.
//
// All three take their token, so the meaning arrives as an icon and a label rather than as a
// colour, and none of them takes a "hide" parameter.

import { mark } from './design-tokens.mjs';
import { isOrderedInterval, requireCalendarDate } from './temporal.mjs';

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/**
 * What a validity conflict says: two publisher dates on one wording, resolved as neither.
 *
 * The sentence lives here rather than inside the markup so that the string renderer and the
 * React component say one thing, checked once. Both dates, in text, in the order the
 * publisher records them. No arithmetic, no preference, and no clause that could be read as
 * this product resolving it.
 *
 * @param {object} input
 * @param {string} input.stateValidFrom    the enclosing state's applicability date
 * @param {string} input.wordingValidFrom  the publisher's date on this wording
 */
export function validityConflictSentence({ stateValidFrom, wordingValidFrom }) {
  requireCalendarDate(stateValidFrom, 'the state valid_from');
  requireCalendarDate(wordingValidFrom, 'the wording valid_from');
  if (stateValidFrom === wordingValidFrom) {
    throw new Error(
      'these two dates agree, so there is no conflict to badge; a conflict badge on agreeing ' +
        'dates teaches a reader to ignore it where it matters',
    );
  }
  return (
    `The publisher dates this wording ${wordingValidFrom} inside a state applicable from ` +
    `${stateValidFrom}. Both are the publisher's. Not resolved.`
  );
}

/**
 * Two publisher dates on one wording, shown as both, resolved as neither.
 *
 * @param {object} input
 * @param {string} input.stateValidFrom    the enclosing state's applicability date
 * @param {string} input.wordingValidFrom  the publisher's date on this wording
 */
export function renderValidityConflict({ stateValidFrom, wordingValidFrom }) {
  return (
    '<p class="validity-conflict">' +
    mark('--conflict', validityConflictSentence({ stateValidFrom, wordingValidFrom })) +
    '</p>'
  );
}

/**
 * A state the publisher has scheduled and which has not begun.
 *
 * The comparison date is a parameter rather than the machine clock, because whether a state
 * is provisional is a fact about the reader's chosen date and the publisher's record, and a
 * frontend that consulted its own clock would answer a question nobody asked.
 */
export function provisionalSentence({ validFrom, asOf }) {
  requireCalendarDate(validFrom, 'the state valid_from');
  requireCalendarDate(asOf, 'the comparison date');
  if (validFrom <= asOf) {
    throw new Error(
      `a state applicable from ${validFrom} has begun as of ${asOf}, so marking it provisional ` +
        'would be false; the mark is for a state the publisher has scheduled and not started',
    );
  }
  return `Publisher-scheduled state, applicable from ${validFrom}. As of ${asOf} it has not begun.`;
}

/** The watermark itself. @see provisionalSentence for the rule it carries. */
export function renderProvisional({ validFrom, asOf }) {
  return (
    '<p class="provisional-watermark" data-provisional="true">' +
    mark('--provisional', provisionalSentence({ validFrom, asOf })) +
    '</p>'
  );
}

/** Why a period has no held state. Both are absences; they are not the same absence. */
export const HOLE_KINDS = Object.freeze(['no_state_held', 'continuity_inferred']);

const HOLE_CAPTION = new Map([
  [
    'no_state_held',
    // This screen knows what this corpus holds and nothing else, and the kind is named
    // no_state_HELD for that reason, so the caption overstated its own kind. The publisher
    // may hold a state here that was never ingested.
    (from, to) =>
      `This corpus holds no state covering ${from} to ${to}. Absence here is not absence ` +
      "from the publisher's record.",
  ],
  [
    'continuity_inferred',
    (from, to) =>
      `Continuity from ${from} to ${to} is inferred from the absence of a later held state. ` +
      'The publisher does not assert it.',
  ],
]);

/**
 * The caption a period with no held state carries.
 *
 * The two kinds are different claims and the caption says which. "No state covers this" is a
 * fact about the record. "The previous wording continued" is an inference this product makes
 * from the absence of a later state, and presenting it as the publisher's would turn a
 * missing record into an assertion about the law. Choosing the caption is therefore a rule
 * and not a presentation detail, so it lives here and both renderers call it.
 */
export function holeSentence({ kind, from, to }) {
  const caption = HOLE_CAPTION.get(kind);
  if (caption === undefined) {
    throw new Error(
      `${JSON.stringify(kind)} is not a hole kind; the two are ${HOLE_KINDS.join(', ')} and ` +
        'they are different claims, so the caption may not be chosen after the fact',
    );
  }
  requireCalendarDate(from, 'the hole start');
  requireCalendarDate(to, 'the hole end');
  if (!isOrderedInterval(from, to) || from === to) {
    throw new Error(
      `${from} to ${to} is not a period; a hole with no duration is a boundary, not a gap`,
    );
  }
  return caption(from, to);
}

/**
 * A period no held state covers, hatched and captioned.
 *
 * @see holeSentence, which decides which of the two claims this is and refuses a third.
 */
export function renderHole({ kind, from, to }) {
  const sentence = holeSentence({ kind, from, to });
  return (
    `<p class="hole hole-${escapeHtml(kind)}" data-hole-kind="${escapeHtml(kind)}">` +
    mark('--hole', sentence) +
    '</p>'
  );
}
