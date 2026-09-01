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

import { canonicalStateUrl } from './routes.mjs';
import {
  DATE_SCOPE as SCOPE_HEADING,
  INTERVAL_SENTENCE,
  semanticsOf,
  sharedSemantics,
} from './publisher-vocabulary.mjs';
import { isCalendarDate } from './temporal.mjs';
import { escapeHtml } from './render.mjs';
import { renderNoHitCard } from './no-hit-card.mjs';
import { requireRelaxationAccount, renderRelaxationDisclosures } from './relaxation.mjs';

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

/**
 * Which relaxation a match reason is evidence of.
 *
 * `exact_title` and `keyword` are the reader's own words and imply nothing. The other two are
 * this service having gone beyond them: `interpreted` is the editorial crosswalk, `semantic` is
 * semantic retrieval. A row carrying one of those is standing evidence that the relaxation ran,
 * which is why the account below has to agree with it.
 *
 * Null-prototype, so a reason nobody classified cannot be answered by an inherited member.
 */
const REASON_EVIDENCES = Object.freeze(
  Object.assign(Object.create(null), { interpreted: 'crosswalk', semantic: 'semantic' }),
);

const MATCH_LABEL = new Map([
  ['exact_title', 'matched on title, not wording'],
  ['keyword', 'matched your words'],
  ['interpreted', 'interpreted (editorial layer, versioned, non-official)'],
  ['semantic', 'semantic match'],
]);

/** The header sentence, in the publisher's own vocabulary. There is no third and no default. */
// The heading vocabulary lives with the rest of it, keyed by publisher.
export { DATE_SCOPE } from './publisher-vocabulary.mjs';

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
  // Through the shared same-origin route policy, not a substring test. Containing "--" was
  // the entire guard, so `javascript:alert(1)--x` satisfied it and was rendered as a working
  // href a few lines below. `parseObjectUrl` refuses anything that does not start with "/",
  // refuses unsafe segments and anchors, and requires the version key to carry a calendar date
  // and a digest, which is the property the old check was reaching for.
  const permalink = canonicalStateUrl(hit.permalink);
  if (permalink === null) {
    throw new Error(
      `${where} needs its hash-carrying permalink as a canonical same-origin state URL; ` +
        `${JSON.stringify(hit.permalink)} is not one, and a link without the digest silently ` +
        'follows the publisher when the file behind it is replaced',
    );
  }
  // Bound to the row on every coordinate, not merely on the date. Comparing valid_from alone
  // accepted a link to work-b on a row describing work-a whenever the two shared a start
  // date, which is the common case for consolidations published together. A reader would
  // arrive at a different instrument with every field above still true of the row.
  const coordinate = String(hit.lex_id).split(':');
  if (coordinate.length < 3) {
    throw new Error(
      `${where} has lex_id ${JSON.stringify(hit.lex_id)}, which does not name a publisher, ` +
        'a work and a state, so its link cannot be bound to it',
    );
  }
  const [publisher, work] = coordinate;
  if (
    permalink.publisher !== publisher ||
    permalink.work !== work ||
    permalink.validFrom !== hit.valid_from
  ) {
    throw new Error(
      `${where} links to ${permalink.publisher}:${permalink.work} applicable from ` +
        `${permalink.validFrom} while the row is ${publisher}:${work} from ` +
        `${hit.valid_from}; the link and the row must name one state`,
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

/** The publisher half of a state identifier. */
function publisherOf(lexId) {
  return String(lexId).split(':')[0];
}

function renderHit(hit, index) {
  // The row's own publisher decides which clock its dates are on. This used to take the
  // envelope's single vocabulary, so every row in a multi-publisher list was described in
  // one publisher's words: an EUR-Lex row rendered "Applicable from ...(publisher)" and
  // attributed to the Union an applicability claim it does not make. There is no parameter
  // to get wrong now, because the record carries the answer.
  const semantics = semanticsOf(publisherOf(hit.lex_id), `hit ${index + 1}`);
  const legal = INTERVAL_SENTENCE[semantics](hit.valid_from, hit.valid_to);

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
  relaxations,
  searchPath,
  layers,
  expansions = [],
  routes,
}) {
  // There is no semantics parameter. The vocabulary is a property of each record's
  // publisher and is derived below, so a caller cannot pass one that disagrees with the
  // data. Passing one is refused rather than ignored: a caller who believes they are
  // choosing the vocabulary has misunderstood the contract, and silently overriding them
  // would leave them believing it worked.
  if (semantics !== undefined) {
    throw new Error(
      'results do not take a date vocabulary; each row is described in its own ' +
        "publisher's terms, derived from the record, so there is nothing to choose",
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

  // The account is required, and closed. It defaulted to `[]`, which is not an account of
  // three relaxations but the absence of one, and every check below was written over
  // `Object.keys(relaxations)`, so omitting it did not fail the disclosure contract, it skipped
  // the contract entirely. A screen that discloses only when handed something to disclose
  // cannot be told apart from a screen that never discloses.
  requireRelaxationAccount(relaxations);

  // A relaxation that ran without its disclosure is the screen answering a question the reader
  // did not ask. The expansions are the evidence that one ran, so they cannot be silent.
  const applied = Object.values(relaxations).filter((one) => one?.applied === true);
  if (expansions.length > 0 && applied.length === 0) {
    throw new Error(
      `this query was expanded into ${expansions.join(', ')} and no relaxation is declared ` +
        'as applied; a reader who asked one thing and was answered another has to be told',
    );
  }
  // Unconditional. Guarded on the account being nonempty, an omitted account rendered no
  // disclosures at all rather than failing, which is the same defect one layer down.
  const disclosures = renderRelaxationDisclosures({ searchPath, relaxations });

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

  // Rows validated before anything is derived from them. Deriving first let the publisher
  // classification throw ahead of requireHit, so a row with an empty lex_id reported an
  // unclassified publisher instead of a missing identifier, and four row guards became
  // unreachable from the suite in one edit.
  hits.forEach(requireHit);

  // Every badge that is evidence of a relaxation must be matched by that relaxation declaring
  // itself applied. The expansions check catches a rewritten query with no relaxation declared;
  // this catches the same failure from the other end, a row badged "semantic match" inside a
  // result set whose account says semantic retrieval never ran. One of those two is false, and
  // the reader is looking at the badge.
  //
  // After requireHit, and for the reason written directly above it: placed earlier, this
  // iterated match_reasons before anything had checked that match_reasons was a list, so a row
  // that never said why it matched failed here as a type error instead of saying so.
  for (const [index, hit] of hits.entries()) {
    for (const reason of hit.match_reasons) {
      const evidenced = REASON_EVIDENCES[reason];
      if (evidenced !== undefined && relaxations[evidenced].applied !== true) {
        throw new Error(
          `hit ${index + 1} is badged ${JSON.stringify(reason)}, which is evidence that the ` +
            `${evidenced} relaxation ran, while this result set declares ${evidenced} as not ` +
            'applied; the badge and the account cannot both be true',
        );
      }
    }
  }

  // Null when the rows disagree, which a multi-publisher result set routinely does.
  const scope = sharedSemantics(
    hits.map((hit) => publisherOf(hit.lex_id)),
    'the result heading',
  );

  return (
    '<section class="results">' +
    // The heading may use one publisher's words only when every row shares that publisher's
    // clock. A mixed list has no single vocabulary, and picking one states a claim about
    // the rows it does not describe. Neutral wording is not a hedge here: it is the only
    // true sentence available over rows that make different kinds of assertion.
    `<h2 class="results-scope">${escapeHtml(
      scope === null
        ? `States covering ${asOf}, each in its own publisher's terms`
        : SCOPE_HEADING[scope](asOf),
    )}</h2>` +
    `<p class="results-query">You asked: ${escapeHtml(query)}</p>` +
    disclosures +
    governingHtml +
    `<ol class="hits">${hits.map((hit, i) => renderHit(hit, i)).join('')}</ol>` +
    pager +
    renderPopulation(population) +
    '</section>'
  );
}
