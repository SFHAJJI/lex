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
import { escapeHtml } from './render.mjs';
import { INTERVAL_SENTENCE, LEGENDS, semanticsOf } from './publisher-vocabulary.mjs';

// The vocabulary lives in one module now, keyed by publisher, so no screen can pass the
// wrong one. Re-exported here because callers of this screen already import these names.
export {
  INTERVAL_TERM,
  LEGENDS,
  STATE_PHRASE,
  requireSemantics,
} from './publisher-vocabulary.mjs';

/** One state's interval, as a sentence, in the publisher's own vocabulary. */
export const TIMELINE_SEMANTICS = INTERVAL_SENTENCE;

/** Fixed by the spec. Never colour alone. */
export const PROVISIONAL_MARK = 'PROVISIONAL, publisher-scheduled';

const DERIVED_HOLE =
  'derived from the held intervals, not asserted by the publisher. Absence of a held state is ' +
  'not evidence the law was unchanged.';

const DERIVED_OVERLAP =
  'derived from the held intervals, not asserted by the publisher. The publisher ranks neither ' +
  'state, and neither does this.';

const DERIVED_TITLE =
  'these dates were read out of the title mechanically, by this service and not by the ' +
  'publisher, and the reading can be wrong.';

// dd/mm/yyyy and yyyy-mm-dd, bounded so a date is a date and not a slice of a longer number.
//
// Unanchored, this cut 2345-06-30 out of "Acte n. 12345-06-30", read 2024-03-20 out of the
// five-digit year in "20/03/20245", and pulled the date half out of an observation instant.
// Each printed a date this service had invented, under a sentence attributing it to the
// publisher, which is worse than not reading the title at all. The day is one digit or two,
// because a single-digit day was being missed entirely.
const TITLE_DATE =
  /(?<![\d/])(\d{1,2})\/(\d{1,2})\/(\d{4})(?![\d/])|(?<![\d-])(\d{4})-(\d{2})-(\d{2})(?![\dT-])/g;

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
  if (state.valid_to !== null && state.valid_to <= state.valid_from) {
    throw new Error(
      `${where} ends on or before the day it begins, so it covers no day at all; such a state ` +
        'sitting at the edge of a gap made the gap disappear',
    );
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
  // Sixty-four of anything was the whole check, so a row could carry sixty-four spaces or
  // an uppercase digest and still print a permalink chip. A digest is a grammar.
  // The open interval is represented by null and by nothing else. 9999-12-31 is this
  // module's internal sentinel for sorting, so a caller supplying it literally would sort
  // as open while rendering as a real end date, and the row would read "applicable to
  // 9999-12-31" instead of "no end recorded" for a state the publisher never ended.
  if (state.valid_to === '9999-12-31') {
    throw new Error(
      `${where} carries 9999-12-31 as its end date; an open interval is null, and this ` +
        'value is the internal sentinel this screen sorts by',
    );
  }
  if (typeof state.hash !== 'string' || !/^[0-9a-f]{64}$/.test(state.hash)) {
    throw new Error(
      `${where} needs its digest as lowercase hex SHA-256, which is what makes its ` +
        'permalink stable; sixty-four characters of anything is not a digest',
    );
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

  // A withdrawn state is struck, and a strike with no date is a rumour. Two predicates used
  // to guard this, one strict here and one truthy in the renderer, and they agreed only on
  // the single value the tests passed; withdrawn: 'yes' struck the row and dated it undefined.
  if (typeof state.withdrawn !== 'boolean') {
    throw new Error(
      `${where} does not say whether the publisher withdrew it; ${JSON.stringify(state.withdrawn)} ` +
        'is neither withdrawn nor held',
    );
  }
  if (state.withdrawn && !isCalendarDate(state.withdrawn_from_source)) {
    throw new Error(`${where} is withdrawn and does not say when the publisher withdrew it`);
  }

  // A title travels with the language it is written in. Defaulting to French labelled every
  // English EU title as French, and lang is about the text, not about the corpus it came from.
  if (Object.hasOwn(state, 'title')) {
    if (typeof state.title !== 'string' || state.title.trim().length === 0) {
      throw new Error(`${where} carries a title that is not a string`);
    }
    if (typeof state.title_language !== 'string' || !/^[a-z]{2}$/.test(state.title_language)) {
      throw new Error(
        `${where} carries a title and does not say what language it is in; a default would ` +
          'label every title of one publisher as the language of the other',
      );
    }
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
    // Padded, because the publisher writes "au 1/08/2024" and an unpadded day failed the
    // calendar check and was dropped in silence, which reads exactly like agreement.
    const iso = match[4]
      ? `${match[4]}-${match[5]}-${match[6]}`
      : `${match[3]}-${match[2].padStart(2, '0')}-${match[1].padStart(2, '0')}`;
    if (isCalendarDate(iso)) claimed.push(iso);
  }
  // Deduplicated: one date written twice in a title is one claim, not two.
  // Intervals here are half-open: valid_to is the first day the state does not cover. So a
  // title date equal to valid_to is a disagreement, not agreement, and treating it as
  // agreement hid a real one. Legilux labels every consolidated version of a work with the
  // latest consolidation date, so the state applicable 2020-03-14 to 2020-09-25 carries the
  // title "Version consolidee applicable au 25/09/2020", asserting applicability on a day
  // it does not cover. Found against the live record, not against a fixture.
  const disagreeing = [...new Set(claimed.filter((one) => one !== state.valid_from))];
  return disagreeing.length > 0 ? disagreeing : null;
}

function compare(a, b) {
  if (a === b) return 0;
  return a < b ? -1 : 1;
}

/**
 * The gaps, computed from the intervals.
 *
 * Derived rather than supplied, because a caller who forgets to supply them gets a continuous
 * history, and a continuous history is the false picture this whole screen exists to avoid.
 */
export function holesBetween(states) {
  // Merge what is covered, then read the spaces. The first version walked each closed state to
  // the next state beginning at or after it, which never asked whether anything already covered
  // the space between; a state nested inside a longer one produced a hole over an interval the
  // corpus holds, and a state at a gap's edge could consume the gap. Both are the false picture
  // this function exists to prevent, one inventing an absence and one hiding it.
  //
  // Taking the union instead makes duplicates, nesting, overlap and input order stop mattering,
  // because a covered day is covered however many records say so.
  const OPEN = '9999-12-31';
  const spans = states
    .map((state) => ({ from: state.valid_from, to: state.valid_to ?? OPEN }))
    .filter((span) => span.to > span.from)
    .sort((a, b) => (a.from === b.from ? (a.to < b.to ? -1 : 1) : a.from < b.from ? -1 : 1));

  const merged = [];
  for (const span of spans) {
    const last = merged[merged.length - 1];
    if (last && span.from <= last.to) {
      if (span.to > last.to) last.to = span.to;
    } else {
      merged.push({ ...span });
    }
  }

  const holes = [];
  for (let i = 0; i + 1 < merged.length; i += 1) {
    holes.push({ from: merged[i].to, to: merged[i + 1].from });
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
    ? '<p class="timeline-title-distrust">The publisher\'s title contains ' +
      `${escapeHtml(disagreeing.join(', '))}; this record is dated ` +
      `${escapeHtml(state.valid_from)} up to ` +
      `${state.valid_to === null ? 'no recorded end' : `but not including ${escapeHtml(state.valid_to)}`}. ` +
      'Both strings are the publisher\'s. The record\'s dates place this row; the title never ' +
      `does. <span class="timeline-derived">${escapeHtml(DERIVED_TITLE)}</span></p>`
    : '';
  const title =
    typeof state.title === 'string' && state.title.length > 0
      ? `<p class="timeline-title" lang="${escapeHtml(state.title_language ?? 'fr')}">` +
        `${escapeHtml(state.title)}</p>`
      : '';

  return (
    `<tr class="timeline-row"${state.withdrawn ? ' data-withdrawn="true"' : ''}>` +
    `<td><code>${escapeHtml(state.lex_id)}</code></td>` +
    `<td><span class="timeline-interval">${escapeHtml(legalTime)}</span>${provisional}${withdrawn}` +
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
  if (!Number.isInteger(totalCount) || totalCount < 1) {
    throw new Error(
      'a timeline says how many states the publisher history holds; without it a list that ' +
        'simply stops reads as a complete one, and there is nothing to compare these rows to',
    );
  }
  if (totalCount < states.length) {
    throw new Error(
      `${states.length} states were given against a total of ${totalCount}; one of those two ` +
        'numbers is wrong and this screen must not choose which',
    );
  }
  // Derived, so a caller cannot say complete and be believed. The caller may still declare it,
  // and a declaration that disagrees with the records is refused rather than preferred.
  const isTruncated = totalCount > states.length;
  if (truncated !== undefined && truncated !== isTruncated) {
    throw new Error(
      `this timeline declares truncated ${JSON.stringify(truncated)} while holding ` +
        `${states.length} of ${totalCount} states`,
    );
  }

  states.forEach(requireState);
  // A total ordering. The old comparator never returned 0, so states sharing a valid_from kept
  // the caller's order, which is exactly the ambiguous_version shape and exactly where "the
  // record places the row" has to mean something.
  const ordered = [...states].sort(
    (a, b) =>
      compare(a.valid_from, b.valid_from) ||
      compare(a.valid_to ?? '9999-12-31', b.valid_to ?? '9999-12-31') ||
      compare(a.lex_id, b.lex_id),
  );

  // A hole is a universal claim: "no publisher state covers this span". A truncated
  // enumeration cannot support it, because the state that fills the gap may simply be one
  // of the ones not held. Truncation is a fact about this page, and absence of law is not.
  const holes = isTruncated ? [] : holesBetween(ordered);
  const overlaps = overlapsIn(ordered);

  const rows = [
    ...ordered.map((state) => ({ at: state.valid_from, second: 0, html: renderRow(state, { semantics, asOf }) })),
    ...holes.map((hole) => ({ at: hole.from, second: 1, html: renderHoleRow(hole) })),
  ]
    .sort((a, b) => compare(a.at, b.at) || a.second - b.second)
    .map((entry) => entry.html)
    .join('');

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

  const pager = isTruncated
    ? `<p class="timeline-pager">Showing ${ordered.length} of ${totalCount} states.</p>`
    : '';

  const single =
    ordered.length === 1 && !isTruncated
      // "publisher history begins X" is a claim about where the publisher's record starts.
      // One held state and a nontruncated count of one says only that this corpus holds one
      // state; the publisher may hold earlier ones that were never ingested. The live
      // envelope carries history_begins for exactly this question, and this screen does not
      // receive it, so the honest sentence is about the corpus and says what would settle
      // it.
      ? `<p class="timeline-single">This corpus holds one state of this work, beginning ` +
        `${escapeHtml(ordered[0].valid_from)}. Whether the publisher's record begins there ` +
        'is not something this page can tell you.</p>'
      : '';

  return (
    '<section class="timeline">' +
    `<p class="timeline-as-of">Drawn as of ${escapeHtml(asOf)}.</p>` +
    `<p class="timeline-legend">${escapeHtml(LEGENDS[semantics])}</p>` +
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
    `</tr></thead><tbody>${rows}</tbody></table></div>` +
    overlapSection +
    pager +
    `<p class="timeline-population">${escapeHtml(population)}</p>` +
    '</section>'
  );
}
