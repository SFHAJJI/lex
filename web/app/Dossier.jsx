// The work dossier, as React.
//
// Second port, same split as the refusal card: every rule stays in `scripts/dossier.mjs` and is
// applied by `validateDossier`. This file decides how a validated dossier looks and re-derives
// nothing.
//
// Two rules are visible in the markup rather than in the validator, and both are load-bearing.
// The status chip cannot appear without its caption, because the caption is the only reason the
// chip is allowed on the page at all: a held state applicable before entry into force carries
// `in_force`, and without the caption that chip is simply false. And the title carries its own
// language, because the chrome around it is often another one and the title is not translated.

import {
  DATE_ROLES,
  NOT_INGESTED,
  STATUS_CAPTION,
  validateDossier,
} from '../scripts/dossier.mjs';
import { isCalendarDate, isOrderedInterval, isUtcInstant } from '../scripts/temporal.mjs';

const ROLE_LABEL = new Map([
  ['publication', 'published'],
  ['applicable_from', 'applicable from'],
  ['applicable_to', 'applicable to'],
  ['entry_into_force', 'entry into force'],
  ['application', 'application'],
  ['observed_from', 'first observed'],
]);

const PUBLISHER_FLAG = /^[a-z][a-z0-9_]*$/;

/** One date row. An absent date is declared, never omitted. */
function DateRow({ row, index }) {
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

  // A row that disappears takes the reader's chance to notice it was ever expected.
  if (row.date === null) {
    if (typeof row.awaiting !== 'string' || row.awaiting.trim().length === 0) {
      throw new Error(
        `${where} has no date and does not say what it is waiting for; naming the exact source ` +
          'is what separates a gap in this corpus from a gap in the law',
      );
    }
    return (
      <tr className="dossier-date dossier-date-absent">
        <td>{ROLE_LABEL.get(row.role)}</td>
        <td>{NOT_INGESTED}</td>
        <td>{row.awaiting}</td>
      </tr>
    );
  }

  // The clock a role belongs to decides its shape. Accepting either lost the UTC instant the
  // record clock requires and gave the legal clock a time of day the publisher never stated.
  const wantsInstant = row.role === 'observed_from';
  if (!(wantsInstant ? isUtcInstant(row.date) : isCalendarDate(row.date))) {
    throw new Error(
      `${where} carries ${JSON.stringify(row.date)}; ${ROLE_LABEL.get(row.role)} is ` +
        (wantsInstant
          ? 'the record clock and is a UTC instant, verbatim'
          : 'the legal clock and is a calendar date, with no time of day the publisher did not state'),
    );
  }
  return (
    <tr className="dossier-date">
      <td>{ROLE_LABEL.get(row.role)}</td>
      <td>{row.date}</td>
      <td>{row.source}</td>
    </tr>
  );
}

/** The publisher's flag, and the sentence that makes it readable. */
function StatusStrip({ status }) {
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
    <section className="dossier-status">
      <p className="dossier-status-chip">
        <code>{status.binding_status}</code>
      </p>
      <p className="dossier-status-caption">{STATUS_CAPTION}</p>
    </section>
  );
}

/** How many states, how many have text, and what is missing between them. */
function CoverageStrip({ coverage }) {
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
      <section className="dossier-coverage">
        <p className="dossier-coverage-counts">No state of this work is held by this corpus.</p>
        <p className="dossier-holes">
          Nothing here says whether the publisher has states for it. Absence from this corpus is
          not absence from the record, and it is not absence of law.
        </p>
      </section>
    );
  }

  for (const hole of coverage.holes) {
    if (!isCalendarDate(hole?.from) || !isCalendarDate(hole?.to)) {
      throw new Error('a coverage hole names two calendar dates');
    }
    // Strictly ordered, narrower than the shared helper on purpose: a zero-length interval is a
    // legitimate shape for a state and is not one for a gap.
    if (!isOrderedInterval(hole.from, hole.to) || hole.from === hole.to) {
      throw new Error(
        `a coverage hole runs from ${hole.from} to ${hole.to}, which is backwards or empty; a ` +
          'gap that ends before it begins is not a gap in the record',
      );
    }
  }

  return (
    <section className="dossier-coverage">
      <p className="dossier-coverage-counts">
        {coverage.states_held} states held, text for {coverage.states_with_text} of{' '}
        {coverage.states_held}.
      </p>
      {coverage.holes.length === 0 ? (
        <p className="dossier-holes">No gap between the states held.</p>
      ) : (
        <ul className="dossier-holes">
          {coverage.holes.map((hole) => (
            <li key={`${hole.from}:${hole.to}`}>
              This corpus holds no state covering {hole.from} to {hole.to}. Absence here is
              not absence from the publisher&#39;s record, and not evidence the law was
              unchanged.
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** The work dossier. */
export function Dossier({ identity, dates, status, coverage, slots = [] }) {
  const card = validateDossier({ identity, dates, status, coverage, slots });
  return (
    <section className="dossier">
      <header className="dossier-identity">
        <h2 className="dossier-title" lang={card.identity.title_language}>
          {card.identity.title}
        </h2>
        <p className="dossier-type">{card.identity.document_type}</p>
        <p className="dossier-identifier">
          <code>{card.workIdentifier}</code>
        </p>
      </header>
      <StatusStrip status={card.status} />
      <h3>Dates</h3>
      <div className="dossier-scroll" role="region" tabIndex={0} aria-label="Date table, scrollable">
        <table className="dossier-dates">
          <thead>
            <tr>
              <th scope="col">role</th>
              <th scope="col">date</th>
              <th scope="col">source</th>
            </tr>
          </thead>
          <tbody>
            {card.dates.map((row, index) => (
              <DateRow key={row.role} row={row} index={index} />
            ))}
          </tbody>
        </table>
      </div>
      <CoverageStrip coverage={card.coverage} />
      {card.slots.length === 0 ? null : (
        <>
          <h3>Not held by this corpus</h3>
          <ul className="dossier-slots">
            {card.slots.map((slot) => (
              <li className="dossier-slot" key={slot.what}>
                {slot.what}: {NOT_INGESTED}. {slot.where}
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
