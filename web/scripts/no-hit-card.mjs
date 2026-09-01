// The typed no-hit card, v3-spec change item 3: what replaces "nothing matches".
//
// A zero-hit result is the moment a reader decides whether the law exists. "No results" lets
// them conclude it does not, and that conclusion is wrong far more often than it is right:
// the corpus holds the publishers' own consolidations, and about 24,579 never-consolidated
// Luxembourg acts are outside it entirely. So the card is built so that it cannot be rendered
// without saying three things, and each of them is a construction-time requirement rather
// than a template a screen can forget to fill.
//
// Which layers ran, and in which language. A reader who typed English and got nothing is
// owed the fact that the statute is French and which bridge, if any, tried to cross that.
//
// What is searchable and what is not, as counts. Not a reassurance, a population: a zero-hit
// against 1,402 works means something different from a zero-hit against a corpus that never
// held the instrument.
//
// Where to look next, on the publisher's own site, through the one route policy.
//
// One more rule, from the live behaviour rather than from the spec. When the query was
// expanded before it was run, the expansions are shown verbatim. The live service answers an
// English lay query with `["many -> mady", "many -> man"]`, which is nonsense, and a reader
// who cannot see it has no way to understand why nothing came back. An expansion that only
// the log knows about is a silent edit of the question.

import { mark } from './design-tokens.mjs';
import { tryPublisherSourceUri } from './routes.mjs';
import { isCalendarDate } from './temporal.mjs';

/** The retrieval layers this interface knows how to name. */
export const LAYERS = Object.freeze([
  'work_resolution',
  'exact_identifier',
  'keyword',
  'lay_vocabulary_bridge',
  'semantic',
]);

const LAYER_LABEL = new Map([
  ['work_resolution', 'resolved the query against work titles'],
  ['exact_identifier', 'looked the query up as an identifier'],
  ['keyword', 'searched provision wording by keyword'],
  ['lay_vocabulary_bridge', 'expanded lay terms into legal ones'],
  ['semantic', 'ranked by meaning'],
]);

/**
 * What a layer did. `not_run` is a first-class outcome and says so on the page.
 *
 * `not_applicable` exists so the plan can be closed. Completeness used to be inferred from
 * every supplied entry being `ran`, which proved only that the caller's own list was fully
 * executed. A caller naming one layer and running it produced the whole-corpus sentence while
 * four layers went unmentioned. A layer that genuinely does not apply to this query must say
 * so explicitly rather than be omitted, because omission and non-applicability read the same
 * to a reader and only one of them is a fact about the search.
 */
export const LAYER_OUTCOMES = Object.freeze([
  'ran',
  'not_run',
  'unavailable',
  'not_applicable',
]);

const OUTCOME_LABEL = new Map([
  ['ran', 'ran'],
  ['not_run', 'did not run'],
  ['unavailable', 'was unavailable'],
  ['not_applicable', 'did not apply to this query'],
]);

const LANGUAGE_TAG = /^[a-z]{2}$/;

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function requireLayers(layers) {
  if (!Array.isArray(layers) || layers.length === 0) {
    throw new Error(
      'a no-hit card must name which layers ran; "nothing matches" with no account of what ' +
        'was tried is the sentence this component exists to replace',
    );
  }
  const seen = new Set();
  for (const layer of layers) {
    if (!LAYER_LABEL.has(layer?.name)) {
      throw new Error(
        `${JSON.stringify(layer?.name)} is not a retrieval layer this interface can name; ` +
          `the layers are ${LAYERS.join(', ')}`,
      );
    }
    if (seen.has(layer.name)) {
      throw new Error(`${layer.name} is reported twice`);
    }
    seen.add(layer.name);
    if (!LAYER_OUTCOMES.includes(layer.outcome)) {
      throw new Error(
        `${layer.name} reports outcome ${JSON.stringify(layer.outcome)}; a layer either ` +
          `${LAYER_OUTCOMES.join(', ')}, and an unreported outcome reads as success`,
      );
    }
    if (typeof layer.language !== 'string' || !LANGUAGE_TAG.test(layer.language)) {
      throw new Error(
        `${layer.name} must say which language it ran in; a reader who typed English and got ` +
          'nothing is owed that, because the statute is not in English',
      );
    }
  }

  // Completeness last, so a malformed entry is still reported by its own guard. Placed first,
  // this check shadowed the per-layer name, outcome and language rules: every test that fed a
  // deliberately broken single layer got the completeness message instead, and three rules
  // became unreachable from the suite in one edit.
  const named = new Set(layers.map((layer) => layer.name));
  const missing = LAYERS.filter((name) => !named.has(name));
  if (missing.length > 0) {
    throw new Error(
      `the execution plan omits ${missing.join(', ')}; completeness cannot be inferred from a ` +
        'partial list, and a layer that does not apply must say so rather than be left out',
    );
  }
}

/**
 * The population, as counts.
 *
 * `not_searchable` is required and may not be zero-by-omission: the whole point is that the
 * corpus is the publishers' consolidations and a large body of law sits outside it. A
 * disclosure that lists only what is held is an advertisement.
 */
function requirePopulation(population) {
  for (const field of ['searchable_works', 'not_searchable']) {
    if (!Array.isArray(population?.[field]) || population[field].length === 0) {
      throw new Error(
        `the population disclosure needs ${field}; a zero-hit against a corpus of unknown ` +
          'size tells a reader nothing about whether the law exists',
      );
    }
  }
  for (const entry of [...population.searchable_works, ...population.not_searchable]) {
    if (typeof entry?.what !== 'string' || entry.what.trim().length === 0) {
      throw new Error(`a population entry must say what it counts: ${JSON.stringify(entry)}`);
    }
    if (!Number.isInteger(entry?.count) || entry.count < 0) {
      throw new Error(
        `a population entry must carry a whole count: ${JSON.stringify(entry)}; an approximate ` +
          'figure belongs in the wording, not in the number',
      );
    }
    // Counted at build, and the card says when. 31-v3-spec required one public wording
    // correction in Phase 0 precisely because a figure written by hand went stale and stayed
    // on the page: the live service still says "~24,579 never-consolidated acts" where the
    // measured set is 23,370 of a 24,622 population. A count with no build date cannot be
    // checked against the index it claims to describe, so it cannot be rendered.
    if (!isCalendarDate(entry?.counted_at)) {
      throw new Error(
        `a population entry must say when it was counted: ${JSON.stringify(entry)}; a figure ` +
          'written by hand and never dated is how a superseded number stays on the page',
      );
    }
  }
}

function renderPopulation(population) {
  const row = (entry, held) =>
    `<li><strong>${escapeHtml(String(entry.count))}</strong> ` +
    `${escapeHtml(entry.what)} ${held ? 'are searchable here' : 'are not'}` +
    `${entry.note ? ` (${escapeHtml(entry.note)})` : ''}` +
    `<span class="no-hit-counted">counted ${escapeHtml(entry.counted_at)}</span></li>`;
  return (
    '<div class="no-hit-population">' +
    '<h3>What this search covered</h3>' +
    '<ul>' +
    population.searchable_works.map((entry) => row(entry, true)).join('') +
    population.not_searchable.map((entry) => row(entry, false)).join('') +
    '</ul></div>'
  );
}

function renderLayers(layers) {
  const items = layers
    .map(
      (layer) =>
        `<li><span class="no-hit-layer">${escapeHtml(LAYER_LABEL.get(layer.name))}</span> ` +
        `<span class="no-hit-outcome">${escapeHtml(OUTCOME_LABEL.get(layer.outcome))}</span> ` +
        `<span class="no-hit-language">in ${escapeHtml(layer.language)}</span></li>`,
    )
    .join('');
  return `<div class="no-hit-layers"><h3>What ran</h3><ul>${items}</ul></div>`;
}

function renderExpansions(expansions) {
  if (!Array.isArray(expansions) || expansions.length === 0) return '';
  const items = expansions
    .map((one) => `<li><code>${escapeHtml(one)}</code></li>`)
    .join('');
  return (
    '<div class="no-hit-expansions"><h3>Your query was expanded before it ran</h3>' +
    `<ul>${items}</ul>` +
    '<p>These substitutions were applied by the search, not by you. If one of them is wrong, ' +
    'that is a reason the search found nothing.</p></div>'
  );
}

function renderRoutes(routes) {
  const items = routes
    .map((route) => {
      const href = tryPublisherSourceUri(route.publisher, String(route.uri));
      return href === null
        ? `<li>${escapeHtml(route.label)} <span class="no-hit-inert">not linked: this route ` +
            `is not on ${escapeHtml(route.publisher)}'s own host</span></li>`
        : `<li><a href="${escapeHtml(href)}">${escapeHtml(route.label)}</a></li>`;
    })
    .join('');
  return `<div class="no-hit-routes"><h3>Where to look next</h3><ul>${items}</ul></div>`;
}

/**
 * @param {object} input
 * @param {string} input.query        the query as the reader typed it
 * @param {Array}  input.layers       every layer, with its outcome and language
 * @param {object} input.population    what is searchable and what is not, as counts
 * @param {Array}  [input.expansions]  substitutions applied before the query ran
 * @param {Array}  input.routes        publisher search routes, checked by the route policy
 */
export function renderNoHitCard({ query, layers, population, expansions, routes }) {
  if (typeof query !== 'string' || query.trim().length === 0) {
    throw new Error('a no-hit card must echo the query it found nothing for');
  }
  requireLayers(layers);
  requirePopulation(population);
  if (!Array.isArray(routes) || routes.length === 0) {
    throw new Error(
      'a no-hit card must offer the publisher’s own search; a dead end that names no ' +
        'next step teaches a reader that the record is closed',
    );
  }

  // What this card may claim is decided by what actually ran, not by the fact that no rows came
  // back. Every layer could report not_run or unavailable and the card still said "Nothing in
  // the held records matches", which is an absence claim resting on a search that never
  // happened. A reader cannot tell that sentence apart from a real corpus miss, and on this
  // product that is the most expensive confusion available.
  // Every exported layer must have run, not every layer the caller considered applicable.
  // not_applicable is the caller's own judgement, so letting it unlock the whole-corpus
  // sentence hands the caller a switch for the strongest claim on the screen: mark the
  // inconvenient layers non-applicable and the card asserts the corpus holds nothing. That
  // is the same hole as inferring completeness from the supplied list, one level up. The
  // claim stays reachable, it just has to be earned by actually running all five.
  const ran = layers.filter((layer) => layer.outcome === 'ran');
  const account =
    ran.length === 0
      ? {
          head: `No search of the held records completed for ${query}.`,
          scope:
            'Nothing was searched, so nothing is known about whether this corpus holds a match. ' +
            'This is a fact about this request, not about the records.',
        }
      : ran.length < LAYERS.length
        ? {
            head:
              `Nothing matched ${query} in the searches that ran ` +
              `(${ran.map((layer) => LAYER_LABEL.get(layer.name)).join(', ')}).`,
            scope:
              'The layers that did not run are listed below, and this says nothing about what ' +
              'they would have found.',
          }
        : {
            head: `Nothing in the held records matches ${query}.`,
            scope: 'It is what this corpus holds, and what it does not.',
          };

  // The disclaimer is invariant across all three accounts. Scoping the sentence to what ran is
  // the point of this change, but an earlier draft moved this clause into the branches and the
  // partial case silently lost it, which would have made a narrower search read as a broader
  // claim. What varies is how much was searched; that this is never a statement about the law
  // does not vary.
  const caution =
    'This is not evidence that the instrument or the law does not exist. ' + account.scope;

  // Not styled as an error, and not announced as one. Nothing was found, which is a fact
  // about this corpus on this query, and it is frequently not a fact about the law.
  return (
    '<section class="no-hit-card">' +
    '<p class="no-hit-head">' +
    mark('--hole', account.head) +
    '</p>' +
    `<p class="no-hit-caution">${escapeHtml(caution)}</p>` +
    renderLayers(layers) +
    renderExpansions(expansions) +
    renderPopulation(population) +
    renderRoutes(routes) +
    '</section>'
  );
}
