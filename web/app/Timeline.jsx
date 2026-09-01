// The timeline, as React: two clocks drawn as one picture.
//
// The set theory is not here. `holesBetween` and `overlapsIn` are imported from
// `scripts/timeline.mjs`, because the reasoning in their comments was paid for with real
// defects: a state nested inside a longer one used to manufacture a hole over an interval the
// corpus holds, a state at a gap's edge used to swallow the gap, and one record listed twice
// used to be reported as the publisher contradicting itself. A second implementation here would
// be a second place for those to come back.
//
// Three rules decide what this component may be told and what it must work out for itself.
//
// A timeline is the history of ONE work. That is enforced here before anything is computed,
// because everything below it depends on it: gaps and overlaps are comparisons between
// intervals, and comparing the intervals of two unrelated instruments still produces sentences.
// "Both cover part of the same period." "The publisher ranks neither state." Across two works
// those read as findings and they are false, and false in the worst direction, because they
// report a contradiction the publisher never made.
//
// The date vocabulary is NOT a parameter. Luxembourg says a state was applicable; the Union says
// a consolidated wording state ran between two dates. Which one applies is a property of the
// publisher of the work being drawn, and the records name their publisher, so this component
// derives it. A caller who passes one is refused rather than ignored: silently overriding them
// leaves them believing they chose, and the whole point is that this choice is not a choice.
//
// What a caller genuinely holds and the records cannot supply is required, closed and
// cross-checked instead. `asOf` is the date "provisional" is measured against and is a
// parameter rather than a clock read, so the same records render the same page tomorrow.
// `totalCount` is how many states the publisher's history holds, which a list of held states
// cannot know. `truncated` is derived from those two and a caller's declaration is checked
// against the records rather than believed.
//
// The chart is decoration and says so. The table is the structure, it is wide, and it scrolls
// inside its own box rather than making the page scroll sideways at 320 pixels. A scrollable
// box is keyboard focusable whether or not it asks to be, so it carries a role and a name.
//
// Why the row guards below also exist in `scripts/timeline.mjs`: that module exports no
// validator, unlike `dossier.mjs` and `refusal-card.mjs` whose React ports call
// `validateDossier` and `validateRefusal` and re-derive nothing. Until one is extracted there,
// the two copies are held together by `test/timeline-react.test.mjs`, which feeds every guard
// to both renderers and asserts they refuse the same inputs.

import { INTERVAL_SENTENCE, LEGENDS, semanticsOf } from '../scripts/publisher-vocabulary.mjs';
import { oneWorkAcross } from '../scripts/record-identity.mjs';
import { isCalendarDate, isUtcInstant } from '../scripts/temporal.mjs';
import { PROVISIONAL_MARK, holesBetween, overlapsIn } from '../scripts/timeline.mjs';

/**
 * The open interval, for sorting only.
 *
 * A state the publisher never ended is `null`. This value exists so an open state sorts after
 * every closed one; it is never rendered, because "applicable to 9999-12-31" would put an end
 * date on an interval the publisher left open.
 */
const OPEN = '9999-12-31';

/** Said beside every gap, because a gap is this service's arithmetic and not a publisher claim. */
export const DERIVED_HOLE =
  'derived from the held intervals, not asserted by the publisher. Absence of a held state is ' +
  'not evidence the law was unchanged.';

/** Said beside every overlap, for the same reason and with the same force. */
export const DERIVED_OVERLAP =
  'derived from the held intervals, not asserted by the publisher. The publisher ranks neither ' +
  'state, and neither does this.';

/** Said beside every date this service read out of a publisher's title. */
export const DERIVED_TITLE =
  'these dates were read out of the title mechanically, by this service and not by the ' +
  'publisher, and the reading can be wrong.';

// dd/mm/yyyy and yyyy-mm-dd, bounded so a date is a date and not a slice of a longer number.
// The boundaries are the load-bearing part and the reasoning for them is written out in
// `scripts/timeline.mjs`: unanchored, this pattern cut 2345-06-30 out of "Acte n. 12345-06-30",
// read 2024-03-20 out of the five-digit year in "20/03/20245", and pulled the date half out of
// an observation instant. Each printed a date this service had invented under a sentence
// attributing it to the publisher.
const TITLE_DATE =
  /(?<![\d/])(\d{1,2})\/(\d{1,2})\/(\d{4})(?![\d/])|(?<![\d-])(\d{4})-(\d{2})-(\d{2})(?![\dT-])/g;

/** A total ordering, so two states sharing a date are placed by the record and not by arrival. */
function compare(a, b) {
  if (a === b) return 0;
  return a < b ? -1 : 1;
}

/**
 * Everything one state row must say before it can be drawn.
 *
 * @param {object} state
 * @param {number} index  the row's position, so an error names which row
 */
function requireState(state, index) {
  const where = `state ${index + 1}`;

  if (typeof state?.lex_id !== 'string' || state.lex_id.trim().length === 0) {
    throw new Error(`${where} has no lex_id`);
  }
  if (!isCalendarDate(state.valid_from)) {
    throw new Error(
      `${where} valid_from is not a calendar date: ${JSON.stringify(state.valid_from)}`,
    );
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
  if (typeof state.hash !== 'string' || state.hash.length !== 64) {
    throw new Error(`${where} needs its digest, which is what makes its permalink stable`);
  }

  // The publisher's current-state flag is not a statement about a historical interval. A held
  // GDPR state applicable before entry into force carries in_force, so printing it against that
  // interval would date a claim the publisher never made about that date. The dossier status
  // strip is the one screen where the flag belongs, under its own caption.
  if (Object.hasOwn(state, 'binding_status')) {
    throw new Error(
      `${where} carries binding_status, which is the publisher's current-state flag and not a ` +
        'historical statement; it belongs in the dossier status strip under its own caption, ' +
        'never on a state row',
    );
  }

  // A withdrawn state is struck, and a strike with no date is a rumour. Strict rather than
  // truthy: `withdrawn: 'yes'` once struck a row and dated it undefined.
  if (typeof state.withdrawn !== 'boolean') {
    throw new Error(
      `${where} does not say whether the publisher withdrew it; ${JSON.stringify(state.withdrawn)} ` +
        'is neither withdrawn nor held',
    );
  }
  if (state.withdrawn && !isCalendarDate(state.withdrawn_from_source)) {
    throw new Error(`${where} is withdrawn and does not say when the publisher withdrew it`);
  }

  // A title travels with the language it is written in, and the record already says what that
  // is. A constant default labelled every English Union title as French; falling back to this
  // state's own `language` reads the expression's own claim instead of guessing, because a
  // title is published as part of the expression it belongs to. An explicit `title_language`
  // still wins, for a publisher serving an expression in one language under a title it
  // publishes only in another. Neither present stays refused.
  if (Object.hasOwn(state, 'title')) {
    if (typeof state.title !== 'string' || state.title.trim().length === 0) {
      throw new Error(`${where} carries a title that is not a string`);
    }
    const declared = state.title_language ?? state.language;
    if (typeof declared !== 'string' || !/^[a-z]{2}$/.test(declared)) {
      throw new Error(
        `${where} carries a title and neither it nor the record says what language it is in; ` +
          'a constant default would label every title of one publisher as the other',
      );
    }
  }
  return state;
}

/**
 * Dates the title claims that the record's own dates do not.
 *
 * Mechanical, and treated as such. A publisher's title often carries the interval's end and
 * sometimes neither end, and all twelve states of one work can share one title, so a title date
 * that is neither boundary is shown beside the record's dates rather than trusted. No title
 * ever moves a row.
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
  const disagreeing = [
    ...new Set(claimed.filter((one) => one !== state.valid_from && one !== state.valid_to)),
  ];
  return disagreeing.length > 0 ? disagreeing : null;
}

/** One held state, on both clocks. */
function StateRow({ state, semantics, asOf }) {
  const disagreeing = titleDisagreement(state);
  const titleLanguage = state.title_language ?? state.language;
  return (
    <tr className="timeline-row" data-withdrawn={state.withdrawn ? 'true' : undefined}>
      <td>
        <code>{state.lex_id}</code>
      </td>
      <td>
        <span className="timeline-interval">
          {INTERVAL_SENTENCE[semantics](state.valid_from, state.valid_to)}
        </span>
        {/* Measured against the supplied date, never a clock: a state must not stop being
            provisional without the publisher having done anything. */}
        {state.valid_from > asOf ? (
          <p className="timeline-provisional">{PROVISIONAL_MARK}</p>
        ) : null}
        {state.withdrawn ? (
          <p className="timeline-withdrawn">
            {`Withdrawn by the publisher on ${state.withdrawn_from_source}.`}
          </p>
        ) : null}
        <p className="timeline-record-time">
          {`Published ${state.publication_date} / First observed ${state.observed_from}`}
        </p>
        {typeof state.title === 'string' && state.title.length > 0 ? (
          <p className="timeline-title" lang={titleLanguage}>
            {state.title}
          </p>
        ) : null}
        {disagreeing === null ? null : (
          <p className="timeline-title-distrust">
            {`The publisher's title contains ${disagreeing.join(', ')}; this record is dated ` +
              `${state.valid_from} to ${state.valid_to ?? 'no end recorded'}. Both strings are ` +
              "the publisher's. The record's dates place this row; the title never does. "}
            <span className="timeline-derived">{DERIVED_TITLE}</span>
          </p>
        )}
      </td>
      <td>{state.text_available ? 'text held' : 'no text held'}</td>
      <td>{state.extraction_profile}</td>
      <td>
        <code>{state.hash.slice(0, 8)}</code>
      </td>
    </tr>
  );
}

/** A span no held state covers, drawn where the absence is. */
function HoleRow({ hole }) {
  return (
    <tr className="timeline-hole">
      <td colSpan={5}>
        {/* What this corpus holds, not what the publisher holds. The note beside it says absence
            of a held state is not evidence; a claim that no publisher state exists contradicts it
            in the same cell. */}
        {`GAP ${hole.from} to ${hole.to}. This corpus holds no state covering ${hole.from} to ${hole.to}. `}
        <span className="timeline-derived">{DERIVED_HOLE}</span>
      </td>
    </tr>
  );
}

/**
 * The timeline.
 *
 * @param {object} props
 * @param {undefined} [props.semantics] refused; the vocabulary is the publisher's, derived below
 * @param {Array}   props.states        held states, any order; this sorts them
 * @param {string}  props.asOf          the date "provisional" is measured against
 * @param {number}  props.totalCount    how many states the publisher's history holds
 * @param {boolean} [props.truncated]   optional declaration, cross-checked against the records
 * @param {string}  props.population    what this list was drawn from
 */
export function Timeline({ semantics: declaredSemantics, states, asOf, totalCount, truncated, population }) {
  // Refused rather than ignored. A caller who believes they are choosing the vocabulary has
  // misunderstood the contract, and quietly overriding them leaves them believing it worked.
  if (declaredSemantics !== undefined) {
    throw new Error(
      'a timeline does not take a date vocabulary; it is a property of the publisher of the ' +
        'work being drawn and is derived from the records, so a caller cannot pass one that ' +
        'disagrees with them',
    );
  }
  // The clock is a parameter, so the same index and the same URL render the same page tomorrow.
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
  // Derived, so a caller cannot say complete and be believed. A declaration is still allowed,
  // and one that disagrees with the records is refused rather than preferred.
  const isTruncated = totalCount > states.length;
  if (truncated !== undefined && truncated !== isTruncated) {
    throw new Error(
      `this timeline declares truncated ${JSON.stringify(truncated)} while holding ` +
        `${states.length} of ${totalCount} states`,
    );
  }

  states.forEach(requireState);

  // One work, and the publisher's own words for its dates. Derived after every row has
  // validated itself, so a malformed row is reported in its own terms rather than as a
  // mixed-work refusal.
  const identity = oneWorkAcross(states, 'a timeline');
  const semantics = semanticsOf(identity.publisher, 'a timeline');

  const ordered = [...states].sort(
    (a, b) =>
      compare(a.valid_from, b.valid_from) ||
      compare(a.valid_to ?? OPEN, b.valid_to ?? OPEN) ||
      compare(a.lex_id, b.lex_id),
  );

  // Gaps are only knowable from a complete history. Derived from a truncated list, "no
  // publisher state covers X to Y" is asserted across exactly the states the same page admits
  // it is not showing, so pagination alone would manufacture an absence in the record.
  const holes = isTruncated ? [] : holesBetween(ordered);
  const overlaps = overlapsIn(ordered);

  // Holes are drawn where the absence is, after the state that ends there. `second` is the
  // tie-break that puts a gap beginning on a date after a state beginning on the same date.
  const rows = [
    ...ordered.map((state) => ({
      at: state.valid_from,
      second: 0,
      node: <StateRow key={`state:${state.lex_id}`} state={state} semantics={semantics} asOf={asOf} />,
    })),
    ...holes.map((hole) => ({
      at: hole.from,
      second: 1,
      node: <HoleRow key={`hole:${hole.from}:${hole.to}`} hole={hole} />,
    })),
  ]
    .sort((a, b) => compare(a.at, b.at) || a.second - b.second)
    .map((entry) => entry.node);

  return (
    <section className="timeline">
      <p className="timeline-as-of">{`Drawn as of ${asOf}.`}</p>
      {/* The publisher's own legend. The two clocks are the same two clocks, but what the top
          one measures is an applicability claim for one publisher and a consolidated wording
          state for the other, and only one of those sentences is true of a given record. */}
      <p className="timeline-legend">{LEGENDS[semantics]}</p>
      {/* Decoration, and it says so. The table below is the structure, which is both the
          accessibility rule and the only version of this screen that survives without a
          client. */}
      <div className="timeline-chart" aria-hidden="true" />
      {/* "publisher history begins X" is a claim about where the publisher's record starts.
          One held state and a nontruncated count of one says only that this corpus holds one
          state; the publisher may hold earlier ones that were never ingested. The live envelope
          carries history_begins for exactly this question and this screen does not receive it,
          so the honest sentence is about the corpus and says what would settle it. */}
      {ordered.length === 1 && !isTruncated ? (
        <p className="timeline-single">
          {`This corpus holds one state of this work, beginning ${ordered[0].valid_from}. ` +
            "Whether the publisher's record begins there is not something this page can " +
            'tell you.'}
        </p>
      ) : null}
      {/* The table is wide and it is the accessible structure, so it scrolls in its own box
          rather than making the page scroll sideways at 320 pixels. A scrollable box is
          keyboard-focusable whether or not it is asked to be, so it carries a role and a
          name: a tab stop that announces nothing is a tab stop a reader cannot place. */}
      <div className="timeline-scroll" role="region" tabIndex={0} aria-label="State history table, scrollable">
        <table className="timeline-table">
          <thead>
            <tr>
              <th scope="col">state</th>
              <th scope="col">both clocks</th>
              <th scope="col">text</th>
              <th scope="col">extraction profile</th>
              <th scope="col">digest</th>
            </tr>
          </thead>
          <tbody>{rows}</tbody>
        </table>
      </div>
      {overlaps.length === 0 ? null : (
        <section className="timeline-overlaps">
          <h3>Overlapping states</h3>
          <p className="timeline-derived">{DERIVED_OVERLAP}</p>
          <ul>
            {overlaps.map((pair) => (
              <li key={`${pair.left.lex_id}|${pair.right.lex_id}`}>
                <code>{pair.left.lex_id}</code>
                {' and '}
                <code>{pair.right.lex_id}</code>
                {' both cover part of the same period. Neither is preselected.'}
              </li>
            ))}
          </ul>
        </section>
      )}
      {isTruncated ? (
        <p className="timeline-holes-unknown">
          {'Gaps are not shown: this is part of the history, and what lies between states ' +
            'nobody listed cannot be read from the states listed here.'}
        </p>
      ) : null}
      {isTruncated ? (
        <p className="timeline-pager">{`Showing ${ordered.length} of ${totalCount} states.`}</p>
      ) : null}
      <p className="timeline-population">{population}</p>
    </section>
  );
}
