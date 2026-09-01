// The work dossier, and the two things it must not let a reader assume.
//
// This is the only screen where the publisher's current-state flag belongs. Everywhere else in
// this interface a row carrying `binding_status` is refused, because a flag about now, printed
// against a historical interval, dates a claim the publisher never made about that date. Here
// it is required, and it is required to carry its caption, because the caption is the whole
// reason it is allowed to appear at all. The held GDPR state applicable from 2016-04-27 carries
// `in_force` while the regulation did not apply until 2018-05-25; without the caption that chip
// is simply false.
//
// The second thing is absence. A dossier has slots the corpus cannot fill yet: the responsible
// ministry, the historical identifiers, the Union entry-into-force and application axioms. A
// blank slot reads as a fact about the law, so an unfilled one says what it is, that it is not
// ingested, and where the publisher does keep it. That is the same rule as the population
// disclosure on a list: a number with no denominator and a field with no explanation are the
// same omission wearing different clothes.

import { isCalendarDate, isOrderedInterval, isUtcInstant } from './temporal.mjs';
import { escapeHtml } from './render.mjs';
import { publisherIdentifier } from './routes.mjs';

/**
 * The caption the status chip cannot appear without.
 *
 * Fixed, because it is the sentence that makes the chip honest rather than a decoration around
 * it. A held state predating entry into force carries this flag, so the chip alone asserts
 * something the publisher did not.
 */
export const STATUS_CAPTION = 'current-state flag, not a historical statement';

/** What an unfilled slot says instead of nothing. */
export const NOT_INGESTED = 'not yet ingested';

/**
 * Date roles this screen knows how to label.
 *
 * Closed, because a role nobody named is a date nobody can interpret, and the whole table is an
 * argument about which clock each date belongs to.
 */
export const DATE_ROLES = Object.freeze([
  'publication',
  'applicable_from',
  'applicable_to',
  'entry_into_force',
  'application',
  'observed_from',
]);

const ROLE_LABEL = new Map([
  ['publication', 'published'],
  ['applicable_from', 'applicable from'],
  ['applicable_to', 'applicable to'],
  ['entry_into_force', 'entry into force'],
  ['application', 'application'],
  ['observed_from', 'first observed'],
]);

function renderDateRow(row, index) {
  const where = `date row ${index + 1}`;
  if (!ROLE_LABEL.has(row?.role)) {
    throw new Error(
      `${where} has role ${JSON.stringify(row?.role)}; the set is closed at ` +
        `${DATE_ROLES.join(', ')}, because a date whose role nobody named cannot be read`,
    );
  }
  if (typeof row.source !== 'string' || row.source.trim().length === 0) {
    throw new Error(
      `${where} does not say where its date came from; a date with no source is this service's ` +
        "assertion wearing the publisher's authority",
    );
  }

  // An absent date is declared, never omitted. A row that disappears takes the reader's chance
  // to notice it was ever expected, which is exactly what the axiom rows are for.
  if (row.date === null) {
    if (typeof row.awaiting !== 'string' || row.awaiting.trim().length === 0) {
      throw new Error(
        `${where} has no date and does not say what it is waiting for; naming the exact source ` +
          'is what separates a gap in this corpus from a gap in the law',
      );
    }
    return (
      `<tr class="dossier-date dossier-date-absent"><td>${escapeHtml(ROLE_LABEL.get(row.role))}</td>` +
      `<td>${escapeHtml(NOT_INGESTED)}</td>` +
      `<td>${escapeHtml(row.awaiting)}</td></tr>`
    );
  }

  // The clock a role belongs to decides its shape. Accepting either lost the UTC instant the
  // record clock requires and gave the legal clock a time of day the publisher never stated.
  const wantsInstant = row.role === 'observed_from';
  const ok = wantsInstant ? isUtcInstant(row.date) : isCalendarDate(row.date);
  if (!ok) {
    throw new Error(
      `${where} carries ${JSON.stringify(row.date)}; ${ROLE_LABEL.get(row.role)} is ` +
        (wantsInstant
          ? 'the record clock and is a UTC instant, verbatim'
          : 'the legal clock and is a calendar date, with no time of day the publisher did not state'),
    );
  }
  return (
    `<tr class="dossier-date"><td>${escapeHtml(ROLE_LABEL.get(row.role))}</td>` +
    `<td>${escapeHtml(row.date)}</td><td>${escapeHtml(row.source)}</td></tr>`
  );
}

/**
 * The status strip: the publisher's flag, and the sentence that makes it readable.
 */
/**
 * Publisher flag values this screen will print.
 *
 * The value used to be any non-empty string, so "REPEALED (lex derived)" rendered in monospace
 * beneath a caption certifying it as the publisher's own flag. A derived value presented as a
 * publisher assertion is the one thing the caption exists to prevent. The grammar below is not
 * the publisher's vocabulary and does not pretend to be: the closed set belongs in the contract
 * both sides read, and until it exists this refuses anything that is not a bare publisher token.
 */
const PUBLISHER_FLAG = /^[a-z][a-z0-9_]*$/;

function renderStatusStrip(status) {
  if (typeof status?.binding_status !== 'string' || status.binding_status.length === 0) {
    throw new Error(
      'the status strip carries the publisher flag verbatim; this is the one screen where it ' +
        'belongs, and a strip with no flag is a caption about nothing',
    );
  }
  if (!PUBLISHER_FLAG.test(status.binding_status)) {
    throw new Error(
      `${JSON.stringify(status.binding_status)} is not a bare publisher flag token; a value ` +
        "this service derived, printed under a caption calling it the publisher's, is the " +
        'assertion that caption exists to prevent',
    );
  }
  return (
    '<section class="dossier-status">' +
    `<p class="dossier-status-chip"><code>${escapeHtml(status.binding_status)}</code></p>` +
    `<p class="dossier-status-caption">${escapeHtml(STATUS_CAPTION)}</p>` +
    '</section>'
  );
}

/**
 * The coverage strip: how many states, how many have text, and what is missing between them.
 */
function renderCoverageStrip(coverage) {
  for (const field of ['states_held', 'states_with_text']) {
    if (!Number.isInteger(coverage?.[field]) || coverage[field] < 0) {
      throw new Error(`the coverage strip needs ${field} as a whole count`);
    }
  }
  if (coverage.states_with_text > coverage.states_held) {
    throw new Error('the coverage strip holds text for more states than it holds');
  }
  if (!Array.isArray(coverage.holes)) {
    throw new Error(
      'the coverage strip declares its holes, even as an empty list; a strip that is silent ' +
        'about gaps reads as a strip with none',
    );
  }

  // "No gap" is a claim about a record that exists. With nothing held there is no record to be
  // continuous, and a reader told a work has no gaps concludes the corpus has its whole history.
  if (coverage.states_held === 0) {
    return (
      '<section class="dossier-coverage">' +
      '<p class="dossier-coverage-counts">No state of this work is held by this corpus.</p>' +
      '<p class="dossier-holes">Nothing here says whether the publisher has states for it. ' +
      'Absence from this corpus is not absence from the record, and it is not absence of ' +
      'law.</p></section>'
    );
  }

  const holes =
    coverage.holes.length === 0
      ? '<p class="dossier-holes">No gap between the states held.</p>'
      : '<ul class="dossier-holes">' +
        coverage.holes
          .map((hole) => {
            if (!isCalendarDate(hole?.from) || !isCalendarDate(hole?.to)) {
              throw new Error('a coverage hole names two calendar dates');
            }
            // Strictly ordered, which is narrower than the shared helper on purpose: that one
            // permits a zero-length interval, which is a legitimate shape for a state and is
            // not one for a gap. A gap covering no day is not a gap in the record.
            if (!isOrderedInterval(hole.from, hole.to) || hole.from === hole.to) {
              throw new Error(
                `a coverage hole runs from ${hole.from} to ${hole.to}, which is backwards or ` +
                  'empty; a gap that ends before it begins is not a gap in the record',
              );
            }
            return (
              // This screen knows what this corpus holds and nothing else. Saying no
              // publisher state covers the period claims the publisher's record is empty
              // there, and the publisher may hold a state that was never ingested. The
              // second sentence already said absence is not evidence; the first was
              // contradicting it.
              `<li>This corpus holds no state covering ${escapeHtml(hole.from)} to ` +
              `${escapeHtml(hole.to)}. Absence here is not absence from the publisher's ` +
              'record, and not evidence the law was unchanged.</li>'
            );
          })
          .join('') +
        '</ul>';

  return (
    '<section class="dossier-coverage">' +
    `<p class="dossier-coverage-counts">${coverage.states_held} states held, text for ` +
    `${coverage.states_with_text} of ${coverage.states_held}.</p>` +
    holes +
    '</section>'
  );
}

/**
 * The work dossier.
 *
 * @param {object} input
 * @param {object} input.identity   title, work identifier, document type, publisher
 * @param {Array}  input.dates      one row per date role, absent dates declared
 * @param {object} input.status     the publisher's current-state flag
 * @param {object} input.coverage   states held, states with text, holes
 * @param {Array}  [input.slots]    fields the corpus cannot fill yet, each naming itself
 */
/**
 * Every rule a dossier must satisfy, decided once and shared.
 *
 * Split out so the React runtime cannot become a second place where a legal rule lives. The
 * component calls this and renders what it returns; it re-derives nothing. A rule repaired here
 * is repaired in both renderers, because there is only one.
 *
 * Returns the normalised inputs a renderer needs. Throws on anything a dossier must not show.
 */
export function validateDossier({ identity, dates, status, coverage, slots = [] }) {
  if (typeof identity?.title !== 'string' || identity.title.trim().length === 0) {
    throw new Error('a dossier names the work as the publisher titles it');
  }
  if (typeof identity.title_language !== 'string' || !/^[a-z]{2}$/.test(identity.title_language)) {
    throw new Error(
      'the published title carries its own language; the chrome around it may be another one ' +
        'and the title is not translated',
    );
  }
  // A name, not a link. Both identifiers the pack cites as live are http, and routing them
  // through the outbound-link policy meant this screen could not render a single real work.
  const workIdentifier = publisherIdentifier({
    publisher: identity.publisher,
    uri: identity.work_identifier,
  });
  if (typeof identity.document_type !== 'string' || identity.document_type.length === 0) {
    throw new Error('a dossier names the publisher document type it was given');
  }

  if (!Array.isArray(dates) || dates.length === 0) {
    throw new Error(
      'a dossier states its dates by role; a work whose dates are unstated is a work whose ' +
        'clocks a reader has to guess between',
    );
  }
  const seen = new Set();
  for (const row of dates) {
    if (seen.has(row?.role)) throw new Error(`the date table lists ${row.role} twice`);
    seen.add(row?.role);
  }

  slots.forEach((slot, index) => {
    if (typeof slot?.what !== 'string' || slot.what.trim().length === 0) {
      throw new Error(`unfilled slot ${index + 1} does not say what it is`);
    }
    if (typeof slot?.where !== 'string' || slot.where.trim().length === 0) {
      throw new Error(
        `unfilled slot ${index + 1} does not say where the publisher keeps it; "not held" ` +
          'without a route is indistinguishable from "does not exist"',
      );
    }
  });

  return { identity, workIdentifier, dates, status, coverage, slots };
}

export function renderDossier({ identity, dates, status, coverage, slots = [] }) {
  // Every rule lives in validateDossier and is applied once. This function decides only how
  // the validated result looks, which is what lets the React runtime share the rules rather
  // than reimplement them beside a copy that can drift.
  const { workIdentifier } = validateDossier({ identity, dates, status, coverage, slots });

  // A slot the corpus cannot fill says so and says where the publisher keeps it. A blank one
  // reads as a fact about the law rather than a fact about this corpus.
  const slotHtml = slots
    .map((slot, index) => {
      if (typeof slot?.what !== 'string' || slot.what.trim().length === 0) {
        throw new Error(`unfilled slot ${index + 1} does not say what it is`);
      }
      if (typeof slot?.where !== 'string' || slot.where.trim().length === 0) {
        throw new Error(
          `unfilled slot ${index + 1} does not say where the publisher keeps it; "not held" ` +
            'without a route is indistinguishable from "does not exist"',
        );
      }
      return (
        `<li class="dossier-slot">${escapeHtml(slot.what)}: ${escapeHtml(NOT_INGESTED)}. ` +
        `${escapeHtml(slot.where)}</li>`
      );
    })
    .join('');

  return (
    '<section class="dossier">' +
    '<header class="dossier-identity">' +
    `<h2 class="dossier-title" lang="${escapeHtml(identity.title_language)}">` +
    `${escapeHtml(identity.title)}</h2>` +
    `<p class="dossier-type">${escapeHtml(identity.document_type)}</p>` +
    `<p class="dossier-identifier"><code>${escapeHtml(workIdentifier)}</code></p>` +
    '</header>' +
    renderStatusStrip(status) +
    '<h3>Dates</h3>' +
    '<div class="dossier-scroll" role="region" tabindex="0" aria-label="Date table, scrollable">' +
    '<table class="dossier-dates"><thead><tr><th scope="col">role</th>' +
    '<th scope="col">date</th><th scope="col">source</th></tr></thead>' +
    `<tbody>${dates.map(renderDateRow).join('')}</tbody></table></div>` +
    renderCoverageStrip(coverage) +
    (slotHtml === ''
      ? ''
      : `<h3>Not held by this corpus</h3><ul class="dossier-slots">${slotHtml}</ul>`) +
    '</section>'
  );
}
