// Search results, and the four things a result list implies without saying.
//
// A list of hits is read as an answer to "what does the law say", and it is an answer to
// something much narrower: what this corpus holds, under the retrieval that actually ran,
// among the rows that were returned. Each of those three qualifications is invisible unless
// the screen puts it there, and each of them is where a reader draws a conclusion the records
// do not support.
//
// So the population travels with the list rather than sitting on a page somebody could visit.
// A list that was cut names its total, because a list that simply ends reads as a complete
// one. Any relaxation that ran is disclosed beside the hits it produced, with a way back to
// the exact words, because a reader who asked about a deposit and received results for a
// different word has not been answered. And zero hits is never an empty list: it is the one
// result most likely to be read as "the law does not say", so it goes to a card that names
// every layer that ran and everything the query was turned into.
//
// The resolver comes first, once. When the query names an instrument, that instrument is the
// answer and the ranked provisions are context; keyword ranking alone puts an unrelated
// regulation above the governing code, which is measured behaviour on the live service rather
// than a worry. Two governing cards would be two answers to one question, so there is at most
// one and it is refused rather than truncated.

import { isCalendarDate } from './temporal.mjs';
import { escapeHtml } from './render.mjs';
import { renderNoHitCard } from './no-hit-card.mjs';
import { renderRelaxationDisclosures } from './relaxation.mjs';

/**
 * Why a row is here, from the live enum rather than the prose in the specs.
 *
 * Observed on the service as `match_reasons`. A row that will not say why it matched cannot be
 * told apart from one the reader's own words found, which is the difference between a result
 * and an interpretation.
 */
export const MATCH_REASONS = Object.freeze([
  'exact_title',
  'keyword',
  'interpreted',
  'semantic',
]);

const MATCH_LABEL = new Map([
  ['exact_title', 'matched on title, not wording'],
  ['keyword', 'matched your words'],
  ['interpreted', 'interpreted (editorial layer, versioned, non-official)'],
  ['semantic', 'semantic match'],
]);

/** The header sentence, in the publisher's own vocabulary. There is no third and no default. */
export const DATE_SCOPE = Object.freeze({
  publisher_applicability: (date) => `Provisions as applicable on ${date}`,
  official_consolidation_state: (date) => `Wording states covering ${date}`,
});

/**
 * The population, in the shape the zero-hit card already fixed.
 *
 * Two shapes for one disclosure would let a hit list say less than an empty one, which is the
 * wrong way round: a reader who got results is exactly the reader who stops checking.
 */
function requirePopulationShape(population) {
  for (const field of ['searchable_works', 'not_searchable']) {
    if (!Array.isArray(population?.[field]) || population[field].length === 0) {
      throw new Error(
        `a result list needs ${field} in its population disclosure; a hit list against a ` +
          'corpus of unknown size reads as an answer about the law',
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
    if (!isCalendarDate(entry?.counted_at)) {
      throw new Error(
        `a population entry must say when it was counted: ${JSON.stringify(entry)}; a figure ` +
          'with no date outlives the measurement it came from, which has happened here',
      );
    }
  }
}

function renderPopulation(population) {
  const line = (entry) =>
    `<li>${entry.count} ${escapeHtml(entry.what)} (counted ${escapeHtml(entry.counted_at)})</li>`;
  return (
    '<section class="results-population"><h3>What was searched</h3>' +
    `<ul class="results-searchable">${population.searchable_works.map(line).join('')}</ul>` +
    '<h3>What was not</h3>' +
    `<ul class="results-not-searchable">${population.not_searchable.map(line).join('')}</ul>` +
    '</section>'
  );
}

function requireHit(hit, index) {
  const where = `hit ${index + 1}`;

  if (typeof hit?.lex_id !== 'string' || hit.lex_id.trim().length === 0) {
    throw new Error(`${where} has no lex_id`);
  }
  if (!isCalendarDate(hit.valid_from)) {
    throw new Error(`${where} valid_from is not a calendar date`);
  }
  if (hit.valid_to !== null && !isCalendarDate(hit.valid_to)) {
    throw new Error(`${where} valid_to is neither null nor a calendar date`);
  }
  if (!isCalendarDate(hit.publication_date)) {
    throw new Error(`${where} publication_date is not a calendar date`);
  }
  if (typeof hit.text_available !== 'boolean') {
    throw new Error(
      `${where} does not say whether its text is held; a row silent about it reads as a row ` +
        'that has it, and this corpus holds 1,493 versions that do not',
    );
  }
  if (typeof hit.permalink !== 'string' || !hit.permalink.includes('--')) {
    throw new Error(
      `${where} needs its hash-carrying permalink; a link without the digest silently follows ` +
        'the publisher when the file behind it is replaced',
    );
  }

  const reasons = hit.match_reasons;
  if (!Array.isArray(reasons) || reasons.length === 0) {
    throw new Error(
      `${where} does not say why it matched; a row that will not say cannot be told apart ` +
        "from one the reader's own words found",
    );
  }
  for (const reason of reasons) {
    if (!MATCH_LABEL.has(reason)) {
      throw new Error(
        `${JSON.stringify(reason)} is not a match reason; the set is ${MATCH_REASONS.join(', ')}`,
      );
    }
  }

  // A title carries the language it is written in. The same defect was live on the timeline:
  // a default labelled every title of one publisher as the language of the other, and a
  // screen reader then read Union law in a French voice.
  if (Object.hasOwn(hit, 'title')) {
    if (typeof hit.title !== 'string' || hit.title.trim().length === 0) {
      throw new Error(`${where} carries a title that is not a string`);
    }
    if (typeof hit.title_language !== 'string' || !/^[a-z]{2}$/.test(hit.title_language)) {
      throw new Error(
        `${where} carries a title and does not say what language it is in`,
      );
    }
  }

  // The publisher's current-state flag is not a statement about this interval, and a hit row
  // is the densest place a reader would read it as one.
  if (Object.hasOwn(hit, 'binding_status')) {
    throw new Error(
      `${where} carries binding_status, which is a current-state flag and not a historical ` +
        'statement; it belongs in the dossier status strip under its own caption',
    );
  }
  return hit;
}

function renderHit(hit, index, semantics) {
  requireHit(hit, index);
  const legal =
    semantics === 'publisher_applicability'
      ? `Applicable from ${hit.valid_from} to ${hit.valid_to ?? 'no end recorded'} (publisher)`
      : `Consolidated wording state from ${hit.valid_from} to ${hit.valid_to ?? 'no end recorded'}`;

  const badges = hit.match_reasons
    .map((reason) => `<li class="hit-badge">${escapeHtml(MATCH_LABEL.get(reason))}</li>`)
    .join('');

  const title =
    typeof hit.title === 'string' && hit.title.length > 0
      ? `<p class="hit-title" lang="${escapeHtml(hit.title_language)}">${escapeHtml(hit.title)}</p>`
      : '';

  return (
    '<li class="hit">' +
    `<p class="hit-where">${escapeHtml(hit.provision_num ?? 'the work')}` +
    (hit.chapter_path ? ` &middot; ${escapeHtml(hit.chapter_path)}` : '') +
    '</p>' +
    title +
    `<p class="hit-legal-time">${escapeHtml(legal)}</p>` +
    `<p class="hit-record-time">Published ${escapeHtml(hit.publication_date)}</p>` +
    `<p class="hit-text">${hit.text_available ? 'text held' : 'no text held'}</p>` +
    `<ul class="hit-badges">${badges}</ul>` +
    `<p class="hit-link"><a href="${escapeHtml(hit.permalink)}">Read this state</a></p>` +
    '</li>'
  );
}

/**
 * The results screen.
 *
 * @param {object}  input
 * @param {string}  input.query          what was asked, verbatim
 * @param {string}  input.semantics      the envelope's timeline_semantics, no default
 * @param {string}  input.asOf           the date the results are scoped to, always explicit
 * @param {Array}   input.hits
 * @param {object}  input.rowSet         `{ returned, total }` for the served page
 * @param {object}  input.population      the structured disclosure, as the no-hit card takes
 * @param {object}  [input.governing]     the one resolved instrument, when the query named one
 * @param {Array}   [input.relaxations]   every relaxation, each declaring whether it applied
 * @param {string}  [input.searchPath]    the path a revert link goes back to
 * @param {Array}   [input.layers]        for the zero-hit case
 * @param {Array}   [input.expansions]    what the query was turned into
 * @param {Array}   [input.routes]        official routes out, for the zero-hit case
 */
export function renderSearchResults({
  query,
  semantics,
  asOf,
  hits,
  rowSet,
  population,
  governing = null,
  relaxations = [],
  searchPath,
  layers,
  expansions = [],
  routes,
}) {
  if (!Object.hasOwn(DATE_SCOPE, semantics ?? '')) {
    throw new Error(
      `results are scoped in the publisher's own vocabulary and ${JSON.stringify(semantics)} ` +
        `is not one of ${Object.keys(DATE_SCOPE).join(', ')}`,
    );
  }
  // Never a silent default. The pack's rule is that the operative date is explicit even when
  // it is today, because "today" is the one date a reader will not think to check.
  if (!isCalendarDate(asOf)) {
    throw new Error(
      'results carry the date they are scoped to, explicitly, even when it is today; an ' +
        'implicit date is the one a reader never checks',
    );
  }
  // The same structured disclosure the zero-hit card takes, rather than a sentence. A list
  // with hits and a list without must disclose the same thing in the same shape, or the
  // disclosure is a property of how the search happened to go.
  requirePopulationShape(population);
  if (typeof query !== 'string' || query.trim().length === 0) {
    throw new Error('results echo the query they answer');
  }

  // The row set is checked before anything branches on the hits, because the branch that
  // reads as "the law does not say so" used to be reachable without it. An empty page of a
  // nine-row result set rendered the corpus-miss card, and so did a malformed `hits` that was
  // not an array at all: a response this screen could not parse was published to the reader as
  // an absence of law. The row set is the only thing that distinguishes an empty page from an
  // empty corpus, so it is validated first and unconditionally.
  if (!Number.isInteger(rowSet?.total) || !Number.isInteger(rowSet?.returned)) {
    throw new Error(
      'a result list says how many rows it returned and how many there were; a list that ' +
        'simply ends reads as a complete one',
    );
  }
  if (rowSet.returned < 0 || rowSet.total < 0) {
    throw new Error('a row set counts rows, and a negative count is not a number of rows');
  }
  if (!Array.isArray(hits)) {
    throw new Error(
      'the hits are not a list; a response this screen cannot read is a transport fact, and ' +
        'rendering it as an absence of law states something the service never said',
    );
  }

  // Zero hits is the result most likely to be read as "the law does not say so", so it is
  // never an empty list. It is a card that names every layer that ran and every word the
  // query was turned into. It is also only honest when the result set itself is empty:
  // returning no rows out of a nonzero total is a page boundary, not a corpus miss.
  if (hits.length === 0) {
    if (rowSet.total !== 0 || rowSet.returned !== 0) {
      throw new Error(
        `no rows were given while the row set reports ${rowSet.returned} of ${rowSet.total}; an ` +
          'empty page of a nonempty result set is not evidence that the corpus holds nothing',
      );
    }
    return (
      '<section class="results results-none">' +
      renderNoHitCard({ query, layers, population, expansions, routes }) +
      '</section>'
    );
  }

  if (rowSet.returned !== hits.length) {
    throw new Error(
      `the row set says ${rowSet.returned} rows and ${hits.length} were given; one of those ` +
        'two numbers is wrong and this screen must not choose which',
    );
  }
  if (rowSet.total < rowSet.returned) {
    throw new Error('the row set returned more rows than it holds');
  }

  // A relaxation that ran without its disclosure is the screen answering a question the reader
  // did not ask. The expansions are the evidence that one ran, so they cannot be silent.
  const applied = Object.values(relaxations).filter((one) => one?.applied === true);
  if (expansions.length > 0 && applied.length === 0) {
    throw new Error(
      `this query was expanded into ${expansions.join(', ')} and no relaxation is declared ` +
        'as applied; a reader who asked one thing and was answered another has to be told',
    );
  }
  const disclosures =
    Object.keys(relaxations).length > 0
      ? renderRelaxationDisclosures({ searchPath, relaxations })
      : '';

  // One resolved instrument, or none. Two would be two answers to one question.
  let governingHtml = '';
  if (governing !== null) {
    if (Array.isArray(governing)) {
      throw new Error(
        'there is at most one governing instrument on a result screen; two cards are two ' +
          'answers to one question and the reader is left to pick',
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
    governingHtml =
      '<section class="governing"><h2>The instrument your question names</h2>' +
      `<p class="governing-id"><code>${escapeHtml(governing.lex_id)}</code></p>` +
      `<p class="governing-why">${escapeHtml(governing.why)}</p>` +
      '<p class="governing-note">The rows below are ranked passages within this corpus, not a ' +
      'second answer.</p>' +
      '</section>';
  }

  const pager =
    rowSet.total > rowSet.returned
      ? `<p class="results-pager">Showing ${rowSet.returned} of ${rowSet.total} matching ` +
        'passages.</p>'
      : '';

  return (
    '<section class="results">' +
    `<h2 class="results-scope">${escapeHtml(DATE_SCOPE[semantics](asOf))}</h2>` +
    `<p class="results-query">You asked: ${escapeHtml(query)}</p>` +
    disclosures +
    governingHtml +
    `<ol class="hits">${hits.map((hit, i) => renderHit(hit, i, semantics)).join('')}</ol>` +
    pager +
    renderPopulation(population) +
    '</section>'
  );
}
