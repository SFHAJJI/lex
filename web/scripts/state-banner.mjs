// The two-clocks StateBanner of UX spec section 1, mandatory on every dated object.
//
// Three rules live here, and each of them is a thing the product would otherwise say that is
// not true:
//
//  1. Legal-time phrasing comes from `envelope.timeline_semantics`, never from a constant in
//     this file. Luxembourg publishes applicability; the EU publishes consolidated wording
//     states. Rendering one in the other's words misdescribes the publisher.
//  2. The words "in force" never appear on a state row of either corpus. The EU corpus
//     carries `binding_status: in_force` on states that predate entry into force, so a
//     renderer that echoes the publisher's flag onto the row states something false about
//     the law. The flag belongs in the dossier status strip with its caption, not here.
//  3. The open-ended sentinel is not a date. `9999-12-31` means the publisher recorded no
//     end, and printing it as an end turns "validity does not end" into "validity ended in
//     the year 9999".
//
// A missing or unknown `timeline_semantics` throws. There is no default, because every
// default here is a claim about which publisher's vocabulary applies.

import { mark } from './design-tokens.mjs';
import {
  isOrderedInterval,
  requireCalendarDate,
  requireUtcInstant,
} from './temporal.mjs';

export const OPEN_ENDED_SENTINEL = '9999-12-31';

// A Map, not an object literal. An object literal is not a closed vocabulary: it inherits
// `toString`, `constructor` and the rest, so `timeline_semantics: "toString"` found a
// function, passed the truthiness check that was standing in for membership, and rendered
// `[object Undefined]` as legal time. A Map has no prototype chain to walk.
const LEGAL_TIME_PHRASING = new Map([
  [
    'publisher_applicability',
    (from, to) =>
      to === null
        ? `Applicable from ${from}, with no end recorded by the publisher`
        : `Applicable from ${from} to ${to} (publisher)`,
  ],
  [
    'official_consolidation_state',
    (from, to) =>
      to === null
        ? `Consolidated wording state from ${from}, with no end recorded by the publisher`
        : `Consolidated wording state from ${from} to ${to}`,
  ],
]);

export const TIMELINE_SEMANTICS = Object.freeze([...LEGAL_TIME_PHRASING.keys()]);

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function required(value, field) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`state banner requires ${field}`);
  }
  return value;
}

/**
 * One state's legal time, as one sentence, in the publisher's own vocabulary.
 *
 * Extracted from the banner so a second screen cannot become a second place where legal time
 * is phrased. The provenance page renders in two runtimes, and a React component composing
 * this sentence itself would put two spellings of one publisher claim into the product; the
 * one that drifts is always the one nobody tested.
 *
 * @param {object} input
 * @param {string} input.semantics       a member of TIMELINE_SEMANTICS
 * @param {string} input.validFrom       the publisher's start date
 * @param {string|null} [input.validTo]  the end date, or the open-ended sentinel, or none
 */
export function legalTimeSentence({ semantics, validFrom, validTo = null }) {
  const phrase = LEGAL_TIME_PHRASING.get(semantics);
  if (phrase === undefined) {
    throw new Error(
      `unknown timeline_semantics ${JSON.stringify(semantics)}; ` +
        'the publisher vocabulary is not something this renderer may choose',
    );
  }

  // Legal time is checked before it is printed. The hostile probe rendered "Applicable from
  // 2026-99-99" and "First observed not-a-timestamp", and a reader cannot tell a publisher's
  // odd date from our own broken one, so an impossible date reads as a recorded fact.
  const from = requireCalendarDate(required(validFrom, 'valid_from'), 'valid_from');
  const rawValidTo = validTo ?? null;
  const to = rawValidTo === null || rawValidTo === OPEN_ENDED_SENTINEL ? null : rawValidTo;
  if (to !== null) requireCalendarDate(to, 'valid_to');
  if (!isOrderedInterval(from, to)) {
    throw new Error(
      `valid_from ${from} is after valid_to ${to}; an inverted interval is not a ` +
        'state the publisher can have recorded',
    );
  }
  return phrase(from, to);
}

/**
 * @param {object} input
 * @param {{ timeline_semantics?: string }} input.envelope
 * @param {{ valid_from: string, valid_to?: string|null,
 *           publication_date?: string|null, observed_from: string }} input.state
 */
export function renderStateBanner({ envelope, state }) {
  const legal = legalTimeSentence({
    semantics: envelope?.timeline_semantics,
    validFrom: state?.valid_from,
    validTo: state?.valid_to,
  });

  const observedFrom = requireUtcInstant(
    required(state?.observed_from, 'observed_from'), 'observed_from');
  const publicationDate = state?.publication_date ?? null;
  if (publicationDate !== null) requireCalendarDate(publicationDate, 'publication_date');

  // The record clock is rendered verbatim. An observation timestamp is evidence, and
  // reformatting evidence is the same class of error as normalising an identifier.
  const record = publicationDate
    ? `Published ${publicationDate} / First observed ${observedFrom}`
    : `Publication date not recorded by the publisher / First observed ${observedFrom}`;

  return (
    '<div class="state-banner">' +
    `<p class="state-banner-legal">${mark('--time-legal', legal)}</p>` +
    `<p class="state-banner-record">${mark('--time-record', record)}</p>` +
    '</div>'
  );
}

/**
 * The publisher's own status flag, which may only appear in the dossier status strip and
 * always with its caption. Exported separately so that a caller reaching for it has to
 * choose the strip rather than reach it accidentally from a state row.
 */
export function renderPublisherStatusFlag(bindingStatus) {
  const value = required(bindingStatus, 'binding_status');
  return (
    '<p class="status-strip-flag">' +
    `<span class="status-strip-value">${escapeHtml(value)}</span>` +
    '<span class="status-strip-caption">publisher status flag, current-state flag, ' +
    'not a historical statement</span>' +
    '</p>'
  );
}
