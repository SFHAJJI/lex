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

import { STATE_PHRASE, requireSemantics } from './timeline.mjs';
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
 * Two publisher dates on one wording, shown as both, resolved as neither.
 *
 * @param {object} input
 * @param {string} input.stateValidFrom    the enclosing state's applicability date
 * @param {string} input.wordingValidFrom  the publisher's date on this wording
 */
export function renderValidityConflict({ stateValidFrom, wordingValidFrom, semantics }) {
  requireCalendarDate(stateValidFrom, 'the state valid_from');
  requireCalendarDate(wordingValidFrom, 'the wording valid_from');
  // The publisher's own vocabulary, not this screen's. Hardcoding applicability made an EU
  // consolidation state read as an applicability claim the publisher never made. Checked
  // after the dates, because placed first it shadowed both date guards and every test
  // feeding a malformed date passed on this error instead.
  requireSemantics(semantics, 'a validity conflict');
  if (stateValidFrom === wordingValidFrom) {
    throw new Error(
      'these two dates agree, so there is no conflict to badge; a conflict badge on agreeing ' +
        'dates teaches a reader to ignore it where it matters',
    );
  }

  // Both dates, in text, in the order the publisher records them. No arithmetic, no
  // preference, and no sentence that could be read as this product resolving it.
  return (
    '<p class="validity-conflict">' +
    mark(
      '--conflict',
      `The publisher dates this wording ${wordingValidFrom} inside ${STATE_PHRASE[semantics]} ` +
        `${stateValidFrom}. Both are the publisher's. Not resolved.`,
    ) +
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
export function renderProvisional({ validFrom, asOf, semantics }) {
  requireCalendarDate(validFrom, 'the state valid_from');
  requireCalendarDate(asOf, 'the comparison date');
  if (validFrom <= asOf) {
    throw new Error(
      `a state applicable from ${validFrom} has begun as of ${asOf}, so marking it provisional ` +
        'would be false; the mark is for a state the publisher has scheduled and not started',
    );
  }
  // After the date guards, so neither is shadowed by this one.
  requireSemantics(semantics, 'a provisional watermark');
  return (
    '<p class="provisional-watermark" data-provisional="true">' +
    mark(
      '--provisional',
      `Publisher-scheduled state, ${STATE_PHRASE[semantics]} ${validFrom}. As of ${asOf} it ` +
        'has not begun.',
    ) +
    '</p>'
  );
}

/** Why a period has no held state. Both are absences; they are not the same absence. */
export const HOLE_KINDS = Object.freeze(['no_state_held', 'continuity_inferred']);

const HOLE_CAPTION = new Map([
  [
    'no_state_held',
    (from, to) => `No publisher state covers ${from} to ${to}.`,
  ],
  [
    'continuity_inferred',
    (from, to) =>
      `Continuity from ${from} to ${to} is inferred from the absence of a later held state. ` +
      'The publisher does not assert it.',
  ],
]);

/**
 * A period no held state covers, hatched and captioned.
 *
 * The two kinds are different claims and the caption says which. "No state covers this" is a
 * fact about the record. "The previous wording continued" is an inference this product makes
 * from the absence of a later state, and presenting it as the publisher's would turn a
 * missing record into an assertion about the law.
 */
export function renderHole({ kind, from, to }) {
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
  return (
    `<p class="hole hole-${escapeHtml(kind)}" data-hole-kind="${escapeHtml(kind)}">` +
    mark('--hole', caption(from, to)) +
    '</p>'
  );
}
