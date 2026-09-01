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

export const OPEN_ENDED_SENTINEL = '9999-12-31';

const LEGAL_TIME_PHRASING = Object.freeze({
  publisher_applicability: (from, to) =>
    to === null
      ? `Applicable from ${from}, with no end recorded by the publisher`
      : `Applicable from ${from} to ${to} (publisher)`,
  official_consolidation_state: (from, to) =>
    to === null
      ? `Consolidated wording state from ${from}, with no end recorded by the publisher`
      : `Consolidated wording state from ${from} to ${to}`,
});

export const TIMELINE_SEMANTICS = Object.freeze(Object.keys(LEGAL_TIME_PHRASING));

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
 * @param {object} input
 * @param {{ timeline_semantics?: string }} input.envelope
 * @param {{ valid_from: string, valid_to?: string|null,
 *           publication_date?: string|null, observed_from: string }} input.state
 */
export function renderStateBanner({ envelope, state }) {
  const semantics = envelope?.timeline_semantics;
  const phrase = LEGAL_TIME_PHRASING[semantics];
  if (!phrase) {
    throw new Error(
      `unknown timeline_semantics ${JSON.stringify(semantics)}; ` +
        'the publisher vocabulary is not something this renderer may choose',
    );
  }

  const validFrom = required(state?.valid_from, 'valid_from');
  const rawValidTo = state?.valid_to ?? null;
  const validTo = rawValidTo === null || rawValidTo === OPEN_ENDED_SENTINEL ? null : rawValidTo;
  const observedFrom = required(state?.observed_from, 'observed_from');
  const publicationDate = state?.publication_date ?? null;

  const legal = phrase(validFrom, validTo);

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
