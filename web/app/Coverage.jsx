// Coverage, as React: the page whose job is to say what is missing.
//
// Every other screen answers a question. This one exists to be checked against, so its failure
// mode is not a wrong answer but a comfortable one: a number with no denominator, a count with
// no date, a type row saying how many states are held and not how many have text. Each of those
// reads as completeness and none of them says so out loud.
//
// Nothing here is a literal. Two hand-transcribed counts of the same thing already disagreed on
// the same day, so every figure arrives from the build that measured it and this component has
// no default to fall back on. A versions count cannot be rendered without its versions-with-text
// partner, because 752 held states of which 72 have text is the honest number and 752 alone is
// not.
//
// The publisher's own gap strings are reproduced verbatim. They are the sentences this service
// publishes about its own limits, and a renderer that tidied them would be editing the
// disclosure rather than showing it.
//
// The rule this port adds is `reconcileFacets`, and the reason it is two rules rather than one
// is the whole point of it. A facet table is a breakdown of a headline number, and the two
// tables on this page reconcile with their headline differently:
//
//   - Document types PARTITION. The publisher gives a state at most one type and the untyped row
//     takes the rest, so a complete table accounts for every state exactly once and must sum to
//     the headline exactly.
//   - Languages OVERLAP. A state exists as an expression in each language it was published in,
//     so the rows may sum far past the headline and only the per-row bound holds: no row may
//     count more than the whole it is drawn from.
//
// That distinction is measured, not theoretical. Luxembourg's live language rows sum to 1,406
// works against 1,402 held, and the Union's to 4,652 versions against 2,366. Applying the
// partition rule to languages would make both live coverage pages refuse to render, which is
// how a rule invented for one table quietly deletes a page describing another.
//
// Why the guards below also exist in `scripts/coverage.mjs`: that module exports no validator,
// unlike `dossier.mjs` and `refusal-card.mjs` whose React ports call `validateDossier` and
// `validateRefusal` and re-derive nothing. Until one is extracted there, the two copies are held
// together by `test/coverage-react.test.mjs`, which feeds every guard to both renderers and
// asserts they refuse the same inputs.

import { RETENTION_SENTENCE, UNTYPED_LABEL } from '../scripts/coverage.mjs';
import { isCalendarDate, isUtcInstant } from '../scripts/temporal.mjs';

/**
 * The row a language has when the publisher recorded none.
 *
 * React renders `null` and `undefined` as nothing, so an unguarded cell would simply be blank,
 * and a blank cell in a column of language codes reads as a language this corpus holds rather
 * than as this service failing to say. Labelled for the same reason `UNTYPED_LABEL` exists: the
 * row is real and the states in it are exactly the ones most likely to be missing something.
 */
export const UNCODED_LANGUAGE_LABEL = 'language not recorded by the publisher';

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
 * Check a facet table against the headline it breaks down.
 *
 * Both kinds share the per-row bound and only a partition sums. Truncation weakens the sum and
 * only the sum: a table showing some of its rows cannot be expected to account for the whole,
 * but no row of it can exceed the whole either.
 *
 * `kind` is fixed at each call site rather than taken from the payload, because which kind a
 * table is follows from what the publisher assigns, not from what a caller believes. A payload
 * that could declare languages a partition could make a correct page refuse.
 *
 * @param {object}  input
 * @param {Array}   input.rows      the facet rows served
 * @param {string}  input.field     which count on each row is being reconciled
 * @param {number}  input.headline  the total this facet is a breakdown of
 * @param {'partition'|'overlapping'} input.kind
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
 * One document type row, and the pair that makes it honest.
 *
 * The partner is not optional and not a second column somebody may add. 752 held states with
 * text for 72 of them is the fact; 752 alone is a different and untrue one, and it is the shape
 * a table naturally grows into when one column is easier to fill than the other.
 */
function TypeRow({ row, index }) {
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
    <tr>
      <td>{code}</td>
      <td>{row.versions}</td>
      <td>{row.versions_with_text}</td>
    </tr>
  );
}

/** One language row. Works and states, both counted, neither derived from the other. */
function LanguageRow({ row, index }) {
  requireCount(row?.works, `language row ${index + 1} works`);
  requireCount(row?.versions, `language row ${index + 1} versions`);
  const code = row.code === null || row.code === undefined ? UNCODED_LANGUAGE_LABEL : row.code;
  return (
    <tr>
      <td>{code}</td>
      <td>{row.works}</td>
      <td>{row.versions}</td>
    </tr>
  );
}

/**
 * A facet table, in its own scroll box.
 *
 * The box is keyboard focusable whether or not it asks to be, because a scrollable region is,
 * so it carries a role and an accessible name rather than becoming a tab stop that announces
 * nothing. The caption carries the build instant, so a table read on its own still says when it
 * was measured.
 */
function FacetTable({ caption, head, builtAt, children }) {
  return (
    <div
      className="coverage-scroll"
      role="region"
      tabIndex={0}
      aria-label={`${caption}, scrollable`}
    >
      <table className="coverage-table">
        <caption>{`${caption}. Counts as of index build ${builtAt}.`}</caption>
        <thead>
          <tr>
            {head.map((cell) => (
              <th scope="col" key={cell}>
                {cell}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

/**
 * The coverage page for one publisher.
 *
 * @param {object} props
 * @param {object} props.coverage the served coverage payload, verbatim
 */
export function Coverage({ coverage }) {
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
  if (!Number.isInteger(coverage.document_types_total) || coverage.document_types_total < 0) {
    throw new Error(
      `document_types_total is ${JSON.stringify(coverage.document_types_total)} rather than a ` +
        'count; this page has no defaults, because a figure the renderer supplies is a figure ' +
        'nobody measured',
    );
  }
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
      <section className="coverage coverage-incomplete">
        <h2>{coverage.publisher_name}</h2>
        <p className="coverage-build">
          {'This index build did not complete, so the counts below would describe an unknown ' +
            'fraction of what this corpus holds and are not shown. Build status: ' +
            `${String(coverage.build_inventory_status ?? 'unknown')}, ${issues} recorded ` +
            `issue${issues === 1 ? '' : 's'}, measured ${builtAt}.`}
        </p>
      </section>
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

  const languages = coverage.languages ?? [];
  const typeRows = types.map((row, index) => (
    <TypeRow key={`type:${row?.code ?? UNTYPED_LABEL}:${index}`} row={row} index={index} />
  ));
  const languageRows = languages.map((row, index) => (
    <LanguageRow key={`language:${row?.code ?? UNCODED_LANGUAGE_LABEL}:${index}`} row={row} index={index} />
  ));

  // Last, after every row has proved itself a row, so a malformed row reports itself in its own
  // terms rather than as an arithmetic disagreement. The two tables are checked under the two
  // different rules named at the top of this file, and passing the wrong one to either would
  // either invent a constraint the record does not have or drop the one it does.
  reconcileFacets({
    rows: types,
    field: 'versions',
    headline: coverage.versions,
    kind: 'partition',
    truncated: truncatedTypes,
    what: 'the document type breakdown',
  });
  reconcileFacets({
    rows: languages,
    field: 'versions',
    headline: coverage.versions,
    kind: 'overlapping',
    truncated: false,
    what: 'the language breakdown',
  });
  reconcileFacets({
    rows: languages,
    field: 'works',
    headline: coverage.works,
    kind: 'overlapping',
    truncated: false,
    what: 'the language breakdown',
  });

  return (
    <section className="coverage">
      <h2>{coverage.publisher_name}</h2>
      <p className="coverage-held">
        {`${coverage.works} works, ${coverage.versions} dated states. Text is held for ` +
          `${coverage.text.versions_with_text_served} of them and not for ` +
          `${coverage.text.versions_without_text}.`}
      </p>
      <p className="coverage-range">
        {`States run from ${coverage.valid_from_earliest} to ${coverage.valid_from_latest}, ` +
          'the later date being publisher-scheduled rather than current.'}
      </p>
      <p className="coverage-as-of">{`Counts as of index build ${builtAt}.`}</p>
      <p className="coverage-retention">{RETENTION_SENTENCE}</p>
      <h3>What this corpus does not hold</h3>
      <ul className="coverage-gaps">
        {gaps.map((gap) => (
          <li key={gap}>{gap}</li>
        ))}
      </ul>
      <h3>By document type</h3>
      <FacetTable
        caption="Held states by publisher document type"
        head={['type', 'states held', 'states with text']}
        builtAt={builtAt}
      >
        {typeRows}
      </FacetTable>
      {truncatedTypes ? (
        <p className="coverage-truncated">
          {`Showing ${types.length} of ${coverage.document_types_total} types.`}
        </p>
      ) : null}
      <h3>By language</h3>
      <FacetTable
        caption="Held works and states by language"
        head={['language', 'works', 'states']}
        builtAt={builtAt}
      >
        {languageRows}
      </FacetTable>
    </section>
  );
}
