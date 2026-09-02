// Coverage, which is the page whose job is to say what is missing.
//
// Every other screen answers a question. This one exists to be checked against, so its
// failure mode is not a wrong answer but a comfortable one: a number with no denominator, a
// count with no date, a type row that says how many states are held and not how many have
// text. Each of those reads as completeness and none of them says so out loud.
//
// Two rules do most of the work. Nothing here is a literal, because two hand-transcribed
// counts of the same thing already disagreed on the same day, so every figure arrives from
// the build that measured it and the renderer has no defaults to fall back to. And a versions
// count cannot be rendered without its versions-with-text partner, because 752 held states of
// which 72 have text is the honest number and 752 on its own is not.
//
// The publisher's own gap strings are reproduced byte for byte. They are the sentences this
// service publishes about its own limits, and a renderer that tidied them would be editing the
// disclosure rather than showing it. Where one of them is wrong, it is wrong at the source and
// gets fixed there.

import { isCalendarDate, isUtcInstant } from './temporal.mjs';

/** The row a publisher code has when the publisher did not give one. */
export const UNTYPED_LABEL = 'untyped (publisher code absent)';

/**
 * The same, for a language the publisher did not record.
 *
 * A row is still a row. Without this the code was interpolated raw, so a payload carrying no
 * language code printed the word `undefined` into a table of counts, which a reader reads as a
 * language rather than as this service failing to say.
 */
export const UNCODED_LANGUAGE_LABEL = 'language not recorded by the publisher';

/**
 * A facet table is a breakdown of a headline number, and it has to add up to one.
 *
 * The page rendered a document type claiming 99 held states beside a headline of 30, and a
 * language claiming 9,999 of the same 30. Every individual figure passed, because every check
 * here tested one field at a time and nothing tested the relationships between them. A set of
 * numbers that cannot all be true is worse on this page than anywhere else in the product: it is
 * the page whose entire job is to be the thing a reader checks the others against.
 *
 * There are two kinds of breakdown and they reconcile differently, which is why one rule would
 * not have worked:
 *
 * - A **partition** assigns each thing to exactly one row. The publisher gives a state at most
 *   one document type, and the untyped row catches the rest, so the rows account for every state
 *   exactly once and a complete table must sum to the headline exactly.
 * - An **overlapping** breakdown lets one thing appear in several rows. A dated state exists as
 *   an expression in each language it was published in, so 24 language rows over 30 states can
 *   legitimately sum far past 30. Summing them and demanding the headline would be inventing a
 *   constraint the record does not have. What still holds is that no single row can count more
 *   than the whole it is drawn from.
 *
 * Truncation weakens the partition rule and only that rule: a table showing some of its rows
 * cannot be expected to sum to the whole, but it still cannot exceed it.
 *
 * @param {object} input
 * @param {Array}   input.rows      the facet rows served
 * @param {string}  input.field     which count on each row is being reconciled
 * @param {number}  input.headline  the total this facet is a breakdown of
 * @param {string}  input.kind      'partition' or 'overlapping'
 * @param {boolean} input.truncated whether rows were left out
 * @param {string}  input.what      what to name in the error
 */
function reconcileFacets({ rows, field, headline, kind, truncated, what }) {
  for (const [index, row] of rows.entries()) {
    if (row[field] > headline) {
      throw new Error(
        `${what} row ${index + 1} counts ${row[field]} ${field} against a total of ${headline}; ` +
          'a part cannot be larger than the whole it is drawn from, so one of those two figures ' +
          'is wrong and this page must not choose which',
      );
    }
  }
  if (kind !== 'partition' || truncated) return;
  const served = rows.reduce((sum, row) => sum + row[field], 0);
  if (served !== headline) {
    throw new Error(
      `${what} accounts for ${served} ${field} against a total of ${headline}, and every row is ` +
        'shown; a complete breakdown that does not add up to its own headline means one of the ' +
        'two was measured against something else',
    );
  }
}

/**
 * Fixed by Decision 41, exactly these words.
 *
 * It is the sentence that stops a reader taking a short observation history for a short legal
 * history, and it is frozen because "we have been watching since August" and "the law begins
 * in August" are one careless paraphrase apart.
 */
export const RETENTION_SENTENCE =
  'Observation history begins August 2026; replay depth grows from here.';

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function requireCount(value, what) {
  if (!Number.isInteger(value) || value < 0) {
    throw new Error(
      `${what} is ${JSON.stringify(value)} rather than a count; this page has no defaults, ` +
        'because a figure the renderer supplies is a figure nobody measured',
    );
  }
  return value;
}

/**
 * A type row, and the pair that makes it honest.
 *
 * The partner is not optional and not a second column somebody may add. 752 held states with
 * text for 72 of them is the fact; 752 alone is a different and untrue one, and it is the
 * shape a table naturally grows into when one column is easier to fill than the other.
 */
function renderTypeRow(row, index) {
  const where = `document type row ${index + 1}`;
  requireCount(row?.versions, `${where} versions`);
  if (!Object.hasOwn(row ?? {}, 'versions_with_text')) {
    throw new Error(
      `${where} carries a versions count with no versions_with_text; the pair is the honest ` +
        'figure, and the count on its own reads as text this corpus does not hold',
    );
  }
  requireCount(row.versions_with_text, `${where} versions_with_text`);
  if (row.versions_with_text > row.versions) {
    throw new Error(`${where} holds text for more states than it holds`);
  }

  // A null code is a real row: the publisher gave no type for those states. Dropping it would
  // remove exactly the states most likely to be missing their text.
  const code = row.code === null || row.code === undefined ? UNTYPED_LABEL : row.code;
  return (
    `<tr><td>${escapeHtml(code)}</td><td>${row.versions}</td>` +
    `<td>${row.versions_with_text}</td></tr>`
  );
}

function renderTable({ caption, head, rows, builtAt }) {
  return (
    '<div class="coverage-scroll" role="region" tabindex="0" ' +
    `aria-label="${escapeHtml(caption)}, scrollable">` +
    `<table class="coverage-table"><caption>${escapeHtml(caption)}. Counts as of index build ` +
    `${escapeHtml(builtAt)}.</caption><thead><tr>` +
    head.map((cell) => `<th scope="col">${escapeHtml(cell)}</th>`).join('') +
    `</tr></thead><tbody>${rows}</tbody></table></div>`
  );
}

/**
 * The coverage page for one publisher.
 *
 * @param {object} input
 * @param {object} input.coverage  the served coverage payload, verbatim
 */
export function renderCoverage({ coverage }) {
  const builtAt = coverage?.envelope?.freshness?.built_at;
  if (!isUtcInstant(builtAt)) {
    throw new Error(
      'coverage carries the instant its counts were measured; a count with no date is a count ' +
        'a reader will take as current however old it is',
    );
  }
  if (typeof coverage.publisher_name !== 'string' || coverage.publisher_name.length === 0) {
    throw new Error('coverage names the publisher it describes');
  }

  requireCount(coverage.works, 'works');
  requireCount(coverage.versions, 'versions');
  requireCount(coverage.text?.versions_with_text_served, 'versions_with_text_served');
  requireCount(coverage.text?.versions_without_text, 'versions_without_text');

  if (
    coverage.text.versions_with_text_served + coverage.text.versions_without_text !==
    coverage.versions
  ) {
    throw new Error(
      'the text counts do not add up to the versions count; a total that disagrees with its ' +
        'own parts is the shape two hand-transcribed figures take',
    );
  }

  for (const field of ['valid_from_earliest', 'valid_from_latest']) {
    if (!isCalendarDate(coverage[field])) {
      throw new Error(`coverage ${field} is not a calendar date`);
    }
  }
  // Ordered. Both fields only had to be calendar dates, so a payload could report states running
  // from 2030 to 1849 and the page printed it as a range. Named earliest and latest, they are
  // not two independent dates; they are the ends of one interval, and an interval that runs
  // backwards is not a smaller range, it is a wrong one.
  if (coverage.valid_from_earliest > coverage.valid_from_latest) {
    throw new Error(
      `coverage reports states running from ${coverage.valid_from_earliest} to ` +
        `${coverage.valid_from_latest}, which ends before it begins; earliest and latest are the ` +
        'ends of one interval rather than two independent dates',
    );
  }

  // The gap strings are this service's own statement of its limits. Reproduced exactly.
  const gaps = coverage.known_gaps;
  if (!Array.isArray(gaps) || gaps.length === 0) {
    throw new Error(
      'coverage with no known gaps is a claim of completeness; the page whose job is to say ' +
        'what is missing cannot say nothing is',
    );
  }
  if (!gaps.every((gap) => typeof gap === 'string' && gap.trim().length > 0)) {
    throw new Error('every known gap is a sentence');
  }

  const types = coverage.document_types;
  if (!Array.isArray(types) || types.length === 0) {
    throw new Error('coverage lists the document types it holds');
  }
  // A count, not merely an integer. Guarded on `Number.isInteger` alone, a total of -7 was a
  // valid document-type count, and the only thing that caught it downstream was a truncation
  // check that a payload declaring itself truncated turned off.
  requireCount(coverage.document_types_total, 'document_types_total');
  const truncatedTypes =
    coverage.facets_truncated === true || types.length !== coverage.document_types_total;
  if (truncatedTypes && coverage.facets_truncated !== true) {
    throw new Error(
      `${types.length} type rows were served against a total of ${coverage.document_types_total} ` +
        'and the payload does not say it was truncated; a table that simply stops reads as a ' +
        'complete one',
    );
  }

  // A build that did not finish is not a smaller corpus, it is an unknown one.
  if (coverage.build_complete !== true) {
    const issues = Array.isArray(coverage.build_issues) ? coverage.build_issues.length : 0;
    return (
      '<section class="coverage coverage-incomplete">' +
      `<h2>${escapeHtml(coverage.publisher_name)}</h2>` +
      '<p class="coverage-build">This index build did not complete, so the counts below would ' +
      'describe an unknown fraction of what this corpus holds and are not shown. ' +
      `Build status: ${escapeHtml(String(coverage.build_inventory_status ?? 'unknown'))}, ` +
      `${issues} recorded issue${issues === 1 ? '' : 's'}, measured ` +
      `${escapeHtml(builtAt)}.</p>` +
      '</section>'
    );
  }
  if (
    Number.isInteger(coverage.scope_expected_works) &&
    coverage.scope_expected_works !== coverage.works
  ) {
    throw new Error(
      `the build expected ${coverage.scope_expected_works} works and holds ${coverage.works} ` +
        'while reporting itself complete; one of those two numbers is wrong and this page ' +
        'must not choose which',
    );
  }

  const typeRows = types.map(renderTypeRow).join('');
  // Required rather than defaulted. `?? []` rendered an empty table for a payload that named no
  // languages at all, and an empty table reads as a corpus holding none rather than as a payload
  // that did not say. It also left nothing for the reconciliation below to check.
  if (!Array.isArray(coverage.languages) || coverage.languages.length === 0) {
    throw new Error(
      'coverage lists the languages it holds; an absent list renders as an empty table, which ' +
        'reads as a corpus that holds no language rather than a payload that did not say',
    );
  }
  const languageRows = coverage.languages
    .map((row, index) => {
      requireCount(row?.works, `language row ${index + 1} works`);
      requireCount(row?.versions, `language row ${index + 1} versions`);
      // Labelled, not interpolated. A missing code printed the literal word `undefined` into a
      // column of language codes, where it reads as a language this corpus holds.
      const code = row.code === null || row.code === undefined ? UNCODED_LANGUAGE_LABEL : row.code;
      return `<tr><td>${escapeHtml(code)}</td><td>${row.works}</td><td>${row.versions}</td></tr>`;
    })
    .join('');

  // Last, after every row has validated itself, so a malformed row reports itself in its own
  // terms rather than as an arithmetic disagreement.
  //
  // The publisher gives a state at most one document type and the untyped row takes the rest, so
  // that table is a partition and a complete one must sum exactly. A state exists as an
  // expression in each language it was published in, so the language table overlaps and only the
  // per-row bound holds.
  reconcileFacets({
    rows: types,
    field: 'versions',
    headline: coverage.versions,
    kind: 'partition',
    truncated: truncatedTypes,
    what: 'the document type breakdown',
  });
  reconcileFacets({
    rows: coverage.languages,
    field: 'versions',
    headline: coverage.versions,
    kind: 'overlapping',
    truncated: false,
    what: 'the language breakdown',
  });
  reconcileFacets({
    rows: coverage.languages,
    field: 'works',
    headline: coverage.works,
    kind: 'overlapping',
    truncated: false,
    what: 'the language breakdown',
  });

  return (
    '<section class="coverage">' +
    `<h2>${escapeHtml(coverage.publisher_name)}</h2>` +
    `<p class="coverage-held">${coverage.works} works, ${coverage.versions} dated states. ` +
    `Text is held for ${coverage.text.versions_with_text_served} of them and not for ` +
    `${coverage.text.versions_without_text}.</p>` +
    `<p class="coverage-range">States run from ${escapeHtml(coverage.valid_from_earliest)} to ` +
    `${escapeHtml(coverage.valid_from_latest)}, the later date being publisher-scheduled ` +
    'rather than current.</p>' +
    `<p class="coverage-as-of">Counts as of index build ${escapeHtml(builtAt)}.</p>` +
    `<p class="coverage-retention">${escapeHtml(RETENTION_SENTENCE)}</p>` +
    '<h3>What this corpus does not hold</h3>' +
    '<ul class="coverage-gaps">' +
    gaps.map((gap) => `<li>${escapeHtml(gap)}</li>`).join('') +
    '</ul>' +
    '<h3>By document type</h3>' +
    renderTable({
      caption: 'Held states by publisher document type',
      head: ['type', 'states held', 'states with text'],
      rows: typeRows,
      builtAt,
    }) +
    (truncatedTypes
      ? `<p class="coverage-truncated">Showing ${types.length} of ` +
        `${coverage.document_types_total} types.</p>`
      : '') +
    '<h3>By language</h3>' +
    renderTable({
      caption: 'Held works and states by language',
      head: ['language', 'works', 'states'],
      rows: languageRows,
      builtAt,
    }) +
    '</section>'
  );
}
