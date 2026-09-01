// The search screen: the one consumer of the four interactive components.
//
// Four controls existed, were tested in isolation, and were mounted on nothing. A component that
// is never composed is a component whose contract nobody has had to satisfy, and every one of the
// defects repaired to build this screen was invisible from a fixture written for a single
// component: the results list took a finished badge sentence, the interpretation banner took the
// rewrite as its own parameter rather than reading the account, and the roving tabindex was
// defeated by a button inside every row that no assertion counted.
//
// The screen holds five rules that are not layout preferences.
//
// The relaxation account is required and closed, one entry per member of RELAXATIONS, on every
// path including the zero-hit one. An omitted account is not "nothing ran", it is a caller who
// did not say. Defaulting it made the whole disclosure contract skippable rather than failing it,
// because every check downstream was written over the account's own keys.
//
// Every badge that is evidence a relaxation ran is cross-checked against that account, inside the
// list, where the badge is rendered. A row badged "semantic match" in a result set declaring that
// semantic never ran is a page contradicting itself, and the reader believes the badge.
//
// The interpretation banner precedes the results in DOM order, not merely in layout, so a screen
// reader user hears that their query was rewritten before they hear the answers to the rewritten
// question. Both are inside `ResultList`, so no composition can put anything between them.
//
// The date field shows today as a removable chip before any query runs, and announces the
// resolved state through a live region with the interval named. Today is the date a reader will
// not think to check, precisely because it is the one they would have assumed.
//
// Both row counts, always. "Showing 12" is a fact about the page; "Showing 12 of 47" is a fact
// about the corpus, and only the second lets a reader tell a page boundary from the end of the
// law. The string renderer prints this line only when the list was cut, so a complete list and a
// broken pager look the same; here it speaks unconditionally, in the same words when it is cut.
//
// What the screen does not take: the vocabulary its heading is written in, the number of rows it
// returned, whether the search was narrowed to a date, and what the query was rewritten into. All
// four are properties of the records or of the account, so there is nothing for a caller to get
// wrong. What it does take, it takes required, closed and cross-checked: the corpus total no row
// can supply, the operative date, and the account itself.

import { useCallback, useMemo, useState } from 'react';

import { DATE_SCOPE, sharedSemantics } from '../scripts/publisher-vocabulary.mjs';
import { publisherOf } from '../scripts/record-identity.mjs';
import { isCalendarDate } from '../scripts/temporal.mjs';
import { CompareArming, useCompareSelection } from './CompareArming.jsx';
import { DateField } from './DateField.jsx';
import { FilterChips } from './FilterChips.jsx';
import { NoHitCard, Population, requirePopulation } from './NoHitCard.jsx';
import { Interpretation, ResultList } from './ResultList.jsx';
import {
  RelaxationDisclosures,
  requireRelaxationAccount,
  requireSameOriginSearchPath,
} from './RelaxationDisclosures.jsx';

/** Half-open, the same reading the resolver uses: a state covers [valid_from, valid_to). */
function covers(hit, date) {
  return hit.valid_from <= date && (hit.valid_to === null || date < hit.valid_to);
}

/**
 * The heading, which is a claim about the rows underneath it.
 *
 * The string renderer takes `timeScope` and `semantics` as parameters and refuses the result set
 * when they disagree with the rows. Both are derivable from the rows, so here they are derived
 * and the disagreement is unrepresentable rather than rejected.
 *
 * A date-scoped heading is available only when every row actually covers that date, because
 * "Provisions as applicable on 2026-09-01" over a long-superseded state asserts something the
 * publisher never said. And it may use one publisher's words only when every row is on that
 * publisher's clock: Luxembourg dates when a state applied, the Union dates the wording state of
 * a consolidation and makes no applicability claim. Over a mixed list the neutral sentence is not
 * a hedge, it is the only true one available.
 */
function headingFor(hits, asOf) {
  const narrowed = hits.every((hit) => covers(hit, asOf));
  if (!narrowed) {
    return `Every state held for these rows, not narrowed to ${asOf}. Each row carries its own interval.`;
  }
  const scope = sharedSemantics(
    hits.map((hit, index) => publisherOf(hit.lex_id, `hit ${index + 1}`)),
    'the result heading',
  );
  return scope === null ? `States covering ${asOf}, each in its own publisher's terms` : DATE_SCOPE[scope](asOf);
}

/** The one resolved instrument, or none. Two cards would be two answers to one question. */
function Governing({ governing }) {
  if (governing === null) return null;
  if (Array.isArray(governing)) {
    throw new Error(
      'there is at most one governing instrument on a result screen; two cards are two answers ' +
        'to one question and the reader is left to pick',
    );
  }
  if (typeof governing.lex_id !== 'string' || governing.lex_id.length === 0) {
    throw new Error('the governing instrument must name itself');
  }
  if (typeof governing.why !== 'string' || governing.why.trim().length === 0) {
    throw new Error(
      'the governing instrument says why it is the answer rather than appearing above the ' +
        'ranked rows without explanation',
    );
  }
  return (
    <section className="governing">
      <h2>The instrument your question names</h2>
      <p className="governing-id">
        <code>{governing.lex_id}</code>
      </p>
      <p className="governing-why">{governing.why}</p>
      <p className="governing-note">
        The rows below are ranked passages within this corpus, not a second answer.
      </p>
    </section>
  );
}

/** A facet the reader can turn off, and the rows it keeps. */
function requireFilters(filters) {
  if (!Array.isArray(filters)) {
    throw new Error(
      'a search screen states which facets it offers, even when there are none; an absent list ' +
        'is a caller who did not say whether this result set can be narrowed',
    );
  }
  for (const filter of filters) {
    if (typeof filter?.key !== 'string' || filter.key.length === 0) {
      throw new Error(`a filter must name itself: ${JSON.stringify(filter)}`);
    }
    if (typeof filter.label !== 'string' || filter.label.trim().length === 0) {
      throw new Error(`filter ${filter.key} has nothing a reader could read on its chip`);
    }
    // The rows a filter keeps, rather than the number it hides. A count supplied beside the rows
    // is a fact the rows already state, and the chips were documented as taking one while never
    // reading it, so nothing would have noticed it being wrong.
    if (typeof filter.keeps !== 'function') {
      throw new Error(
        `filter ${filter.key} does not say which rows it keeps; a chip that cannot decide that ` +
          'is chrome that teaches a reader controls do nothing',
      );
    }
    // Whether the reader arrives with this facet already on. A real search restores it from the
    // URL, so it is the caller's to state; absent means off, which is the direction that hides
    // nothing and therefore cannot make a page read as narrower than it is.
    if (filter.active !== undefined && typeof filter.active !== 'boolean') {
      throw new Error(
        `filter ${filter.key} says it is ${JSON.stringify(filter.active)}, which is neither on ` +
          'nor off; a chip whose state nobody stated is announced as one of them anyway',
      );
    }
  }
  return filters;
}

/**
 * The search screen.
 *
 * @param {object} props
 * @param {string} props.query        what the reader asked, verbatim
 * @param {string} props.today        the date this page was rendered for, never read from a clock
 * @param {string} props.asOf         the operative date these results are scoped to, explicit
 *                                    even when it is today
 * @param {Array}  props.hits         the rows this page returned; `returned` is their count
 * @param {number} props.matchingTotal how many passages matched in the corpus, which no row can
 *                                    supply and this screen therefore has to be told
 * @param {object} props.population   what was searched and what was not, as dated counts
 * @param {object} props.relaxations  one entry per member of RELAXATIONS, each with `applied`
 * @param {string} props.searchPath   the same-origin path the reverts are built from
 * @param {Array}  props.filters      `{ key, label, keeps }`; empty means no facets
 * @param {Array}  [props.layers]     every retrieval layer; required when there are no rows, and
 *                                    refused there when absent rather than defaulted to a plan
 *                                    nobody stated
 * @param {Array}  [props.routes]     publisher search routes; required when there are no rows
 * @param {object|null} [props.governing] the one instrument the query named, when it named one
 * @param {object|null} [props.resolved]  the state the last submitted date resolved to
 * @param {Function} props.onOpen        called with the row the reader opened
 * @param {Function} props.onSubmitDate  called with an ISO date
 * @param {Function} props.onCompare     called with the two armed states
 */
export function SearchScreen({
  query,
  today,
  asOf,
  hits,
  matchingTotal,
  population,
  relaxations,
  searchPath,
  filters,
  layers,
  routes,
  governing = null,
  resolved = null,
  onOpen,
  onSubmitDate,
  onCompare,
}) {
  if (typeof query !== 'string' || query.trim().length === 0) {
    throw new Error('a result screen echoes the query it answers');
  }
  // Never a silent default. The operative date is explicit even when it is today, because today
  // is the one date a reader will not think to check.
  if (!isCalendarDate(asOf)) {
    throw new Error(
      'results carry the date they are scoped to, explicitly, even when it is today; an ' +
        'implicit date is the one a reader never checks',
    );
  }
  if (!Array.isArray(hits)) {
    throw new Error(
      'the hits are not a list; a response this screen cannot read is a transport fact, and ' +
        'rendering it as an absence of law states something the service never said',
    );
  }
  const returned = hits.length;
  if (!Number.isInteger(matchingTotal) || matchingTotal < 0) {
    throw new Error(
      'a result screen says how many passages matched as well as how many it returned; a list ' +
        'that simply ends reads as a complete one',
    );
  }
  if (matchingTotal < returned) {
    throw new Error(
      `this page returned ${returned} rows out of a stated ${matchingTotal} matching passages; ` +
        'a page cannot hold more of a result set than the result set has',
    );
  }
  requirePopulation(population);
  // Stated here as a precondition of the screen, and enforced again by the disclosure block
  // below, which renders on every path. So removing this line changes no behaviour today and no
  // test can see it go: it is a tripwire against a future edit that drops the disclosures, not a
  // guard that fires. Said plainly rather than left for a reader to trust wrongly.
  requireRelaxationAccount(relaxations);
  requireSameOriginSearchPath(searchPath);
  requireFilters(filters);

  const [active, setActive] = useState(
    () => new Set(filters.filter((one) => one.active).map((one) => one.key)),
  );
  const selection = useCompareSelection();

  const toggleFilter = useCallback((key) => {
    setActive((current) => {
      const next = new Set(current);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  }, []);

  const visible = useMemo(
    () => hits.filter((hit) => filters.every((one) => !active.has(one.key) || one.keeps(hit))),
    [active, filters, hits],
  );

  // Zero rows is the result most likely to be read as "the law does not say so", so it is never
  // an empty list. It is the card that names every layer that ran and every word the query was
  // turned into. It is also only honest when the result set itself is empty: no rows out of a
  // nonzero total is a page boundary, and publishing that as a corpus miss is this product's most
  // expensive available mistake.
  if (returned === 0) {
    if (matchingTotal !== 0) {
      throw new Error(
        `no rows were given while ${matchingTotal} passages are said to match; an empty page of ` +
          'a nonempty result set is not evidence that the corpus holds nothing',
      );
    }
    return (
      <section className="results results-none">
        <p className="results-query">You asked: {query}</p>
        {/* Which date found nothing. "Nothing matched" is a different statement about 2019 than
            about today, and the screen that says neither leaves the reader to assume the one
            they had in mind. */}
        <p className="results-operative-date">Operative date: {asOf}.</p>
        <DateField today={today} resolved={resolved} onSubmit={onSubmitDate} />
        <RelaxationDisclosures searchPath={searchPath} relaxations={relaxations} />
        {/* Before the result, here as everywhere. The card names every layer and every
            substitution, which is the fuller account; this is the one sentence that speaks when
            there was nothing to substitute, and silence there reads as "your words were used"
            whether or not they were. */}
        <Interpretation relaxations={relaxations} />
        <NoHitCard
          query={query}
          layers={layers}
          population={population}
          relaxations={relaxations}
          routes={routes}
        />
      </section>
    );
  }

  return (
    <section className="results">
      <h2 className="results-scope">{headingFor(hits, asOf)}</h2>
      <p className="results-query">You asked: {query}</p>
      <p className="results-operative-date">Operative date: {asOf}.</p>
      <DateField today={today} resolved={resolved} onSubmit={onSubmitDate} />
      <RelaxationDisclosures searchPath={searchPath} relaxations={relaxations} />
      <Governing governing={governing} />
      {/* Both numbers, unconditionally. Silence here is indistinguishable from a complete list,
          and a reader who cannot tell a page boundary from the end of the corpus reads a cut list
          as everything there is. */}
      <p className="results-pager">
        Showing {returned} of {matchingTotal} matching passages.
      </p>
      {filters.length === 0 ? null : (
        <FilterChips
          filters={filters.map((one) => ({
            key: one.key,
            label: one.label,
            active: active.has(one.key),
          }))}
          total={returned}
          shown={visible.length}
          onToggle={toggleFilter}
        />
      )}
      <CompareArming selected={selection.selected} onCompare={onCompare} />
      {visible.length === 0 ? (
        // Every row hidden by a filter the reader turned on. Not the no-hit card, which would
        // state a corpus miss the search never found, and not an empty listbox, which states
        // nothing at all. The interpretation still speaks, because a rewritten query is a
        // rewritten query whether or not any of its answers are on screen.
        <>
          <Interpretation relaxations={relaxations} />
          <p className="results-all-filtered" aria-live="polite">
            All {returned} rows on this page are hidden by filters you turned on. This says
            nothing about what the corpus holds; turn a filter off to see them.
          </p>
        </>
      ) : (
        <ResultList
          hits={visible}
          relaxations={relaxations}
          selected={selection.selected}
          onOpen={onOpen}
          onToggleSelect={selection.toggle}
        />
      )}
      <Population population={population} />
    </section>
  );
}
