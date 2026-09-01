// The timeline, which is two clocks drawn as one picture.
//
// Every other screen states the two clocks in words. This one is the two clocks, so the ways
// it can lie are the ways a picture lies: by drawing something that is not there, or by
// leaving a shape where an absence should be.
//
// The gaps are the reason this file computes rather than renders. A hole between two held
// states is not a publisher assertion and not a decoration; it is the place where a reader
// concludes the law did not change, and that conclusion is wrong. So the holes are derived
// here from the intervals rather than passed in, because a caller who forgets to pass them
// gets a continuous history, which is exactly the false picture. Being derived, they say so.
//
// The vocabulary is the publisher's. Luxembourg says a state was applicable; the Union says a
// consolidated wording state ran from one date to another. They are different claims and this
// file will not choose one, so the semantics arrive from the envelope and there is no default.
//
// "In force" appears nowhere. A held EU state that predates entry into force already carries
// the publisher's own in_force flag, so printing that flag on a state row would put a date on
// a claim the publisher did not make about that date. A state carrying it is refused here and
// pointed at the dossier status strip, which has the caption for it.
//
// What this does not do: the zoom brush, two-row multi-select and copy buttons in the spec all
// need a client, and these pages ship no script. The chart is decoration and the table is the
// structure, which is the spec's own accessibility rule and happens to survive that constraint
// intact.

import { isCalendarDate, isUtcInstant } from './temporal.mjs';

/** The two vocabularies, and there is no third and no default. */
export const TIMELINE_SEMANTICS = Object.freeze({
  publisher_applicability: (from, to) =>
    `Applicable from ${from} to ${to === null ? 'no end recorded' : to} (publisher)`,
  official_consolidation_state: (from, to) =>
    `Consolidated wording state from ${from} to ${to === null ? 'no end recorded' : to}`,
});

/** Fixed by the spec, three sentences, because the screen is the two clocks. */
export const LEGEND =
  'Top: when the publisher says the state applied. Bottom: when the publisher published it. ' +
  'These routinely differ.';

/** Fixed by the spec. Never colour alone. */
export const PROVISIONAL_MARK = 'PROVISIONAL, publisher-scheduled';

const DERIVED_HOLE =
  'derived from the held intervals, not asserted by the publisher. Absence of a held state is ' +
  'not evidence the law was unchanged.';

const DERIVED_OVERLAP =
  'derived from the held intervals, not asserted by the publisher. The publisher ranks neither ' +
  'state, and neither does this.';

// dd/mm/yyyy and yyyy-mm-dd, which is mechanical extraction rather than reading the title.
const TITLE_DATE = /(\d{2})\/(\d{2})\/(\d{4})|(\d{4})-(\d{2})-(\d{2})/g;

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function requireState(state, index) {
  const where = `state ${index + 1}`;

  if (typeof state?.lex_id !== 'string' || state.lex_id.trim().length === 0) {
    throw new Error(`${where} has no lex_id`);
  }
  if (!isCalendarDate(state.valid_from)) {
    throw new Error(`${where} valid_from is not a calendar date: ${JSON.stringify(state.valid_from)}`);
  }
  if (state.valid_to !== null && !isCalendarDate(state.valid_to)) {
    throw new Error(
      `${where} valid_to is neither null nor a calendar date; an open state ends in null, and ` +
        'filling it with today would close an interval the publisher left open',
    );
  }
  if (state.valid_to !== null && state.valid_to < state.valid_from) {
    throw new Error(`${where} ends before it begins`);
  }
  if (!isCalendarDate(state.publication_date)) {
    throw new Error(`${where} publication_date is not a calendar date`);
  }
  if (!isUtcInstant(state.observed_from)) {
    throw new Error(`${where} observed_from is not a UTC instant`);
  }
  if (typeof state.extraction_profile !== 'string' || state.extraction_profile.length === 0) {
    throw new Error(
      `${where} does not name its extraction profile; two profiles cannot be compared, and a ` +
        'row that will not say which one it came from cannot be checked for that',
    );
  }
  if (typeof state.text_available !== 'boolean') {
    throw new Error(
      `${where} does not say whether its text is held; 1,493 held LU versions have no text, so ` +
        'a row that is silent about it reads as a row that has it',
    );
  }
  if (typeof state.hash !== 'string' || state.hash.length !== 64) {
    throw new Error(`${where} needs its digest, which is what makes its permalink stable`);
  }

  // The publisher's current-state flag is not a statement about a historical interval. A held
  // GDPR state applicable before entry into force carries in_force, so printing it against
  // that interval would date a claim the publisher never made about that date.
  if (Object.hasOwn(state, 'binding_status')) {
    throw new Error(
      `${where} carries binding_status, which is the publisher's current-state flag and not a ` +
        'historical statement; it belongs in the dossier status strip under its own caption, ' +
        'never on a state row',
    );
  }

  // A withdrawn state is struck, and a strike with no date is a rumour.
  if (state.withdrawn === true && !isCalendarDate(state.withdrawn_from_source)) {
    throw new Error(
      `${where} is withdrawn and does not say when the publisher withdrew it`,
    );
  }
  return state;
}

/**
 * Dates the title claims, compared against the dates the record carries.
 *
 * Mechanical: every dd/mm/yyyy and yyyy-mm-dd in the string. The publisher's own titles carry
 * a date that is often the interval's end and sometimes neither end, and all twelve states of
 * one work can share one title. So a title date that is neither boundary is shown beside them
 * rather than trusted, and no title ever moves a row.
 */
function titleDisagreement(state) {
  if (typeof state.title !== 'string') return null;
  const claimed = [];
  for (const match of state.title.matchAll(TITLE_DATE)) {
    const iso = match[4] ? `${match[4]}-${match[5]}-${match[6]}` : `${match[3]}-${match[2]}-${match[1]}`;
    if (isCalendarDate(iso)) claimed.push(iso);
  }
  const disagreeing = claimed.filter((one) => one !== state.valid_from && one !== state.valid_to);
  return disagreeing.length > 0 ? disagreeing : null;
}

/**
 * The gaps, computed from the intervals.
 *
 * Derived rather than supplied, because a caller who forgets to supply them gets a continuous
 * history, and a continuous history is the false picture this whole screen exists to avoid.
 */
export function holesBetween(states) {
  const closed = states
    .filter((state) => state.valid_to !== null)
    .sort((a, b) => (a.valid_from < b.valid_from ? -1 : 1));
  const holes = [];
  for (const state of closed) {
    const next = states
      .filter((other) => other.valid_from >= state.valid_to)
      .sort((a, b) => (a.valid_from < b.valid_from ? -1 : 1))[0];
    if (next && next.valid_from > state.valid_to) {
      holes.push({ from: state.valid_to, to: next.valid_from });
    }
  }
  return holes;
}

/**
 * The overlapping pairs, computed the same way and for the same reason.
 *
 * Two states covering one date is the publisher disagreeing with itself. Merging them, or
 * letting the later one win, would be this product resolving a conflict it cannot resolve.
 */
export function overlapsIn(states) {
  const overlaps = [];
  for (let i = 0; i < states.length; i += 1) {
    for (let j = i + 1; j < states.length; j += 1) {
      const a = states[i];
      const b = states[j];
      // Half-open intervals, the same reading the resolver uses: a state covers [from, to).
      const aEnd = a.valid_to ?? '9999-12-31';
      const bEnd = b.valid_to ?? '9999-12-31';
      if (a.valid_from < bEnd && b.valid_from < aEnd) {
        overlaps.push({ left: a, right: b });
      }
    }
  }
  return overlaps;
}

function renderRow(state, { semantics, asOf }) {
  const legalTime = TIMELINE_SEMANTICS[semantics](state.valid_from, state.valid_to);
  const provisional =
    state.valid_from > asOf
      ? `<p class="timeline-provisional">${escapeHtml(PROVISIONAL_MARK)}</p>`
      : '';
  const withdrawn = state.withdrawn
    ? `<p class="timeline-withdrawn">Withdrawn by the publisher on ` +
      `${escapeHtml(state.withdrawn_from_source)}.</p>`
    : '';
  const disagreeing = titleDisagreement(state);
  const distrust = disagreeing
    ? '<p class="timeline-title-distrust">The publisher\'s title says ' +
      `${escapeHtml(disagreeing.join(', '))}; this record is dated ` +
      `${escapeHtml(state.valid_from)} to ${escapeHtml(state.valid_to ?? 'no end recorded')}. ` +
      'Both are the publisher\'s. The record\'s dates place this row; the title never does.</p>'
    : '';
  const title =
    typeof state.title === 'string' && state.title.length > 0
      ? `<p class="timeline-title" lang="${escapeHtml(state.title_language ?? 'fr')}">` +
        `${escapeHtml(state.title)}</p>`
      : '';

  return (
    `<tr class="timeline-row"${state.withdrawn ? ' data-withdrawn="true"' : ''}>` +
    `<td><code>${escapeHtml(state.lex_id)}</code></td>` +
    `<td>${escapeHtml(legalTime)}${provisional}${withdrawn}` +
    `<p class="timeline-record-time">Published ${escapeHtml(state.publication_date)} / ` +
    `First observed ${escapeHtml(state.observed_from)}</p>${title}${distrust}</td>` +
    `<td>${state.text_available ? 'text held' : 'no text held'}</td>` +
    `<td>${escapeHtml(state.extraction_profile)}</td>` +
    `<td><code>${escapeHtml(state.hash.slice(0, 8))}</code></td>` +
    '</tr>'
  );
}

function renderHoleRow(hole) {
  return (
    '<tr class="timeline-hole"><td colspan="5">' +
    `GAP ${escapeHtml(hole.from)} to ${escapeHtml(hole.to)}. No publisher state covers ` +
    `${escapeHtml(hole.from)} to ${escapeHtml(hole.to)}. ` +
    `<span class="timeline-derived">${escapeHtml(DERIVED_HOLE)}</span>` +
    '</td></tr>'
  );
}

/**
 * The timeline.
 *
 * @param {object} input
 * @param {string} input.semantics      the envelope's timeline_semantics, no default
 * @param {Array}  input.states         held states, any order; this sorts them
 * @param {string} input.asOf           the date "provisional" is measured against, a parameter
 * @param {number} input.totalCount     how many states the publisher's history holds
 * @param {boolean} input.truncated
 * @param {string} input.population     what this list was drawn from
 */
export function renderTimeline({ semantics, states, asOf, totalCount, truncated, population }) {
  if (!Object.hasOwn(TIMELINE_SEMANTICS, semantics ?? '')) {
    throw new Error(
      `a timeline renders in the publisher's own vocabulary and ${JSON.stringify(semantics)} is ` +
        `not one of ${Object.keys(TIMELINE_SEMANTICS).join(', ')}; the two publishers make ` +
        'different claims and this product does not choose between them',
    );
  }
  // The clock is a parameter, so the same index and the same URL render the same page tomorrow.
  // Reading it from the machine would make "provisional" change without the record changing.
  if (!isCalendarDate(asOf)) {
    throw new Error(
      'a timeline needs the date it is drawn as of; taking it from the machine clock would ' +
        'make a state stop being provisional without the publisher having done anything',
    );
  }
  if (!Array.isArray(states) || states.length === 0) {
    throw new Error(
      'a timeline with no states is not an empty chart; a work with no held history is a ' +
        'refusal that says which, and an empty axis with a legend asserts that the law has none',
    );
  }
  if (typeof population !== 'string' || population.trim().length === 0) {
    throw new Error(
      'a timeline states the population it was drawn from; a count with no population reads as ' +
        'the number of states the law has had rather than the number this corpus holds',
    );
  }
  if (truncated === true && !Number.isInteger(totalCount)) {
    throw new Error(
      'a truncated timeline names its total; a list that simply stops reads as a complete one',
    );
  }

  states.forEach(requireState);
  const ordered = [...states].sort((a, b) => (a.valid_from < b.valid_from ? -1 : 1));

  const holes = holesBetween(ordered);
  const overlaps = overlapsIn(ordered);

  const rows = ordered.map((state) => renderRow(state, { semantics, asOf })).join('');
  const holeRows = holes.map(renderHoleRow).join('');

  const overlapSection =
    overlaps.length > 0
      ? '<section class="timeline-overlaps"><h3>Overlapping states</h3>' +
        `<p class="timeline-derived">${escapeHtml(DERIVED_OVERLAP)}</p><ul>` +
        overlaps
          .map(
            (pair) =>
              `<li><code>${escapeHtml(pair.left.lex_id)}</code> and ` +
              `<code>${escapeHtml(pair.right.lex_id)}</code> both cover part of the same period. ` +
              'Neither is preselected.</li>',
          )
          .join('') +
        '</ul></section>'
      : '';

  const pager = truncated
    ? `<p class="timeline-pager">Showing ${ordered.length} of ${totalCount} states.</p>`
    : '';

  const single =
    ordered.length === 1 && !truncated
      ? `<p class="timeline-single">One held state; publisher history begins ` +
        `${escapeHtml(ordered[0].valid_from)}.</p>`
      : '';

  return (
    '<section class="timeline">' +
    `<p class="timeline-as-of">Drawn as of ${escapeHtml(asOf)}.</p>` +
    `<p class="timeline-legend">${escapeHtml(LEGEND)}</p>` +
    // Decoration, and it says so. The table below is the structure, which is both the
    // accessibility rule and the only version of this screen that survives without a client.
    '<div class="timeline-chart" aria-hidden="true"></div>' +
    single +
    // The table is wide and it is the accessible structure, so it scrolls in its own box
    // rather than making the page scroll sideways at 320 pixels. A scrollable box is
    // keyboard-focusable whether or not it is asked to be, so it carries a role and a name.
    '<div class="timeline-scroll" role="region" tabindex="0" ' +
    'aria-label="State history table, scrollable">' +
    '<table class="timeline-table"><thead><tr>' +
    '<th scope="col">state</th><th scope="col">both clocks</th><th scope="col">text</th>' +
    '<th scope="col">extraction profile</th><th scope="col">digest</th>' +
    `</tr></thead><tbody>${rows}${holeRows}</tbody></table></div>` +
    overlapSection +
    pager +
    `<p class="timeline-population">${escapeHtml(population)}</p>` +
    '</section>'
  );
}
