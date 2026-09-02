// The typed no-hit card, as React.
//
// A zero-hit result is the moment a reader decides whether the law exists. "No results" lets them
// conclude it does not, and that conclusion is wrong far more often than it is right: the corpus
// holds the publishers' own consolidations, and a large body of Luxembourg law that never
// receives one sits outside it entirely. So the card cannot be rendered without saying which
// layers ran and in which language, what is searchable and what is not as counts, and where to
// look next on the publisher's own site.
//
// Every one of those refusals lives in `scripts/no-hit-card.mjs` and is applied by calling its
// renderer and discarding the markup. That module has no separate validator to import, and a
// second copy of "a layer that does not apply must say so rather than be left out" living in a
// component is a rule that can be repaired in one renderer and left broken in the other. What
// this file decides is how a validated card looks.
//
// One parameter is gone. The card used to take `expansions` beside the relaxation account, so a
// screen could show substitutions no relaxation claimed, or claim fuzzy expansion and show none.
// The expansions are the fuzzy relaxation's own statement about itself, so they are read off it.

import { LAYERS, renderNoHitCard } from '../scripts/no-hit-card.mjs';
import { tryPublisherSourceUri } from '../scripts/routes.mjs';
import { isCalendarDate } from '../scripts/temporal.mjs';
import { Mark } from './RefusalCard.jsx';
import { interpretationOf } from './RelaxationDisclosures.jsx';

/** A lookup that holds only what it was given, so an unclassified key fails rather than inherits. */
function closedTable(entries) {
  return Object.freeze(Object.assign(Object.create(null), entries));
}

/** What each retrieval layer did, in the words the string renderer uses. */
const LAYER_LABEL = closedTable({
  work_resolution: 'resolved the query against work titles',
  exact_identifier: 'looked the query up as an identifier',
  keyword: 'searched provision wording by keyword',
  lay_vocabulary_bridge: 'expanded lay terms into legal ones',
  semantic: 'ranked by meaning',
});

const OUTCOME_LABEL = closedTable({
  ran: 'ran',
  not_run: 'did not run',
  unavailable: 'was unavailable',
  not_applicable: 'did not apply to this query',
});

// Checked against the closed layer set at import. A layer added to the retrieval plan and not to
// this table would otherwise render an empty line on the one card whose job is to say what ran.
for (const layer of LAYERS) {
  if (LAYER_LABEL[layer] === undefined) {
    throw new Error(`${layer} is a retrieval layer with no label on the no-hit card`);
  }
}

/**
 * The population, as counts, in the one shape both cards use.
 *
 * Exported because the hit list discloses the same thing. Two shapes for one disclosure would let
 * a hit list say less than an empty one, which is the wrong way round: a reader who got results
 * is exactly the reader who stops checking.
 *
 * `not_searchable` may not be zero by omission. A disclosure that lists only what is held is an
 * advertisement.
 */
export function requirePopulation(population) {
  for (const field of ['searchable_works', 'not_searchable']) {
    if (!Array.isArray(population?.[field]) || population[field].length === 0) {
      throw new Error(
        `the population disclosure needs ${field}; a result against a corpus of unknown size ` +
          'tells a reader nothing about whether the law exists',
      );
    }
  }
  for (const entry of [...population.searchable_works, ...population.not_searchable]) {
    if (typeof entry?.what !== 'string' || entry.what.trim().length === 0) {
      throw new Error(`a population entry must say what it counts: ${JSON.stringify(entry)}`);
    }
    if (!Number.isInteger(entry?.count) || entry.count < 0) {
      throw new Error(`a population entry must carry a whole count: ${JSON.stringify(entry)}`);
    }
    // A count with no build date cannot be checked against the index it claims to describe. The
    // live service still carries a figure written by hand that the measured set has moved past.
    if (!isCalendarDate(entry?.counted_at)) {
      throw new Error(
        `a population entry must say when it was counted: ${JSON.stringify(entry)}; a figure ` +
          'written by hand and never dated is how a superseded number stays on the page',
      );
    }
  }
  return population;
}

/** What this search covered, and what it did not. */
export function Population({ population }) {
  requirePopulation(population);
  const row = (entry, held) => (
    <li key={`${held}:${entry.what}`}>
      <strong>{String(entry.count)}</strong> {entry.what} {held ? 'are searchable here' : 'are not'}
      {entry.note ? ` (${entry.note})` : null}
      <span className="no-hit-counted">counted {entry.counted_at}</span>
    </li>
  );
  return (
    <div className="no-hit-population">
      <h3>What this search covered</h3>
      <ul>
        {population.searchable_works.map((entry) => row(entry, true))}
        {population.not_searchable.map((entry) => row(entry, false))}
      </ul>
    </div>
  );
}

/**
 * What this card is entitled to claim, decided by what actually ran.
 *
 * Every layer could report `not_run` and the card still said "Nothing in the held records
 * matches", which is an absence claim resting on a search that never happened. A reader cannot
 * tell that sentence apart from a real corpus miss, and on this product that is the most
 * expensive confusion available.
 *
 * `not_applicable` is a stated fact about this query rather than a gap, so it does not count
 * against the whole-corpus sentence; the plan is closed, so a layer cannot reach that state by
 * being left out.
 */
function accountOf(query, layers) {
  const applicable = layers.filter((layer) => layer.outcome !== 'not_applicable');
  const ran = applicable.filter((layer) => layer.outcome === 'ran');
  if (ran.length === 0) {
    return {
      head: `No search of the held records completed for ${query}.`,
      scope:
        'Nothing was searched, so nothing is known about whether this corpus holds a match. ' +
        'This is a fact about this request, not about the records.',
    };
  }
  if (ran.length < applicable.length) {
    return {
      head:
        `Nothing matched ${query} in the searches that ran ` +
        `(${ran.map((layer) => LAYER_LABEL[layer.name]).join(', ')}).`,
      scope:
        'The layers that did not run are listed below, and this says nothing about what they ' +
        'would have found.',
    };
  }
  return {
    head: `Nothing in the held records matches ${query}.`,
    scope: 'It is what this corpus holds, and what it does not.',
  };
}

/**
 * The zero-hit card.
 *
 * @param {object} props
 * @param {string} props.query       the query as the reader typed it
 * @param {Array}  props.layers      every layer, with its outcome and language
 * @param {object} props.population  what is searchable and what is not, as counts
 * @param {object} props.relaxations the closed relaxation account; the expansions come from it
 * @param {Array}  props.routes      publisher search routes, checked by the route policy
 */
export function NoHitCard({ query, layers, population, relaxations, routes }) {
  const { expansions } = interpretationOf(relaxations);

  // The string renderer is the validator: its markup is discarded and its refusals are kept, so
  // the two cards cannot disagree about what may be rendered at all.
  renderNoHitCard({ query, layers, population, expansions, routes });

  const account = accountOf(query, layers);
  // Invariant across all three accounts. An earlier draft moved this clause into the branches and
  // the partial case silently lost it, which made a narrower search read as a broader claim.
  const caution = `This is not evidence that the instrument or the law does not exist. ${account.scope}`;

  return (
    // Not styled as an error, and not announced as one. Nothing was found, which is a fact about
    // this corpus on this query, and it is frequently not a fact about the law.
    <section className="no-hit-card">
      <p className="no-hit-head">
        <Mark name="--hole">{account.head}</Mark>
      </p>
      <p className="no-hit-caution">{caution}</p>
      <div className="no-hit-layers">
        <h3>What ran</h3>
        <ul>
          {layers.map((layer) => (
            <li key={layer.name}>
              <span className="no-hit-layer">{LAYER_LABEL[layer.name]}</span>{' '}
              <span className="no-hit-outcome">{OUTCOME_LABEL[layer.outcome]}</span>{' '}
              <span className="no-hit-language">in {layer.language}</span>
            </li>
          ))}
        </ul>
      </div>
      {expansions.length === 0 ? null : (
        <div className="no-hit-expansions">
          <h3>Your query was expanded before it ran</h3>
          <ul>
            {expansions.map((one) => (
              <li key={one}>
                <code>{one}</code>
              </li>
            ))}
          </ul>
          <p>
            These substitutions were applied by the search, not by you. If one of them is wrong,
            that is a reason the search found nothing.
          </p>
        </div>
      )}
      <Population population={population} />
      <div className="no-hit-routes">
        <h3>Where to look next</h3>
        <ul>
          {routes.map((route) => {
            // Validated through the route policy, never merely escaped: a hostile scheme escapes
            // to a safe attribute value and stays a working link. A route off the publisher's own
            // host is shown and not linked, rather than dropped, because a card that quietly
            // offers fewer next steps than it was given has edited the list without saying so.
            const href = tryPublisherSourceUri(route.publisher, String(route.uri));
            return (
              <li key={`${route.publisher}:${route.uri}`}>
                {href === null ? (
                  <>
                    {route.label}{' '}
                    <span className="no-hit-inert">
                      not linked: this route is not on {route.publisher}&apos;s own host
                    </span>
                  </>
                ) : (
                  <a href={href}>{route.label}</a>
                )}
              </li>
            );
          })}
        </ul>
      </div>
    </section>
  );
}
