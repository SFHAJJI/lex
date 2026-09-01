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
import { requireRelaxationAccount, renderRelaxationDisclosures } from './relaxation.mjs';
import {
  DATE_SCOPE,
  INTERVAL_SENTENCE,
  semanticsOf,
  sharedSemantics,
} from './publisher-vocabulary.mjs';
import { identityOf, publisherOf } from './record-identity.mjs';
import { canonicalStateUrl } from './routes.mjs';

// The date-scoped heading is vocabulary, so it lives with the rest of it, keyed by publisher.
// Re-exported because this screen's own tests already read it from here.
export { DATE_SCOPE };

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

/**
 * Whether these rows were narrowed to a date at all.
 *
 * The live service answers `all_versions` by default, and this screen printed the date-scoped
 * heading over every result set regardless. So a list drawn from a whole history was published
 * under "Provisions as applicable on 2026-09-01", and nothing let a reader tell that a row from
 * a long-superseded state was not among the provisions applicable that day.
 */
export const TIME_SCOPES = Object.freeze(['as_of', 'all_versions']);

// A Map rather than an object literal, like MATCH_LABEL above it and for the same reason: an
// object literal inherits from Object.prototype, so a table meant to hold two vocabularies also
// answers for `constructor` and `toString`. A Map holds only what it was given.
const SCOPE_HEADING = new Map([
  ['publisher_applicability', 'Every state held for these provisions, not narrowed to one date'],
  ['official_consolidation_state', 'Every wording state held, not narrowed to one date'],
]);

/**
 * Which relaxation a match reason is evidence of.
 *
 * `exact_title` and `keyword` are the reader's own words and imply nothing. The other two are
 * this service having gone beyond them: `interpreted` is the editorial crosswalk, `semantic` is
 * semantic retrieval. A row carrying one of those is standing evidence that the relaxation ran,
 * which is why the account has to agree with it.
 */
const REASON_EVIDENCES = new Map([
  ['interpreted', 'crosswalk'],
  ['semantic', 'semantic'],
]);

/** Half-open, the same reading the resolver uses: a state covers [valid_from, valid_to). */
function covers(hit, date) {
  return hit.valid_from <= date && (hit.valid_to === null || date < hit.valid_to);
}

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
  // Through the shared same-origin route policy, not a substring test. Containing "--" was the
  // entire guard, so `javascript:alert(1)--x` satisfied it and was rendered as a working href a
  // few lines below, as did any URL on any host that happened to carry the digest separator.
  // `canonicalStateUrl` requires this product's own host, read from the raw authority so an
  // explicit port or userinfo cannot dress another host as this one, and then requires the path
  // to be a state URL the object-URL builders could have minted.
  const permalink = canonicalStateUrl(hit.permalink);
  if (permalink === null) {
    throw new Error(
      `${where} needs its hash-carrying permalink as a canonical same-origin state URL; ` +
        `${JSON.stringify(hit.permalink)} is not one, and a link without the digest silently ` +
        'follows the publisher when the file behind it is replaced',
    );
  }
  // Bound to the row on every coordinate, not merely on the date. Comparing valid_from alone
  // accepted a link to work-b on a row describing work-a whenever the two shared a start date,
  // which is the common case for consolidations published together rather than an exotic one.
  // A reader would arrive at a different instrument with every field above still true of the
  // row, which is the worst shape this failure can take: nothing on the screen is wrong.
  const identity = identityOf(hit.lex_id, where);
  if (
    permalink.publisher !== identity.publisher ||
    permalink.work !== identity.work ||
    permalink.validFrom !== hit.valid_from
  ) {
    throw new Error(
      `${where} links to ${permalink.publisher}:${permalink.work} applicable from ` +
        `${permalink.validFrom} while the row is ${identity.workKey} from ${hit.valid_from}; ` +
        'the link and the row must name one state',
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

  // A title carries the language it is written in, and the record already says what that is.
  //
  // This required a separate `title_language` and therefore refused every live hit that has a
  // title, which is all of them. The defect it was written for was a hardcoded `'fr'` default
  // on the timeline, and that is a different thing: falling back to a constant guesses, while
  // falling back to this record's own `language` reads the expression's own claim about
  // itself. A title is published as part of the expression it belongs to.
  //
  // An explicit `title_language` still wins when the wire grows one, for the case where a
  // publisher serves an expression in one language under a title it only publishes in
  // another. Neither present is still refused, because then nothing has said anything.
  if (Object.hasOwn(hit, 'title')) {
    if (typeof hit.title !== 'string' || hit.title.trim().length === 0) {
      throw new Error(`${where} carries a title that is not a string`);
    }
    const declared = hit.title_language ?? hit.language;
    if (typeof declared !== 'string' || !/^[a-z]{2}$/.test(declared)) {
      throw new Error(
        `${where} carries a title and neither it nor the record says what language it is in`,
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

function renderHit(hit, index) {
  // The row's own publisher decides which clock its dates are on. This used to take the
  // envelope's single vocabulary, so every row in a multi-publisher list was described in one
  // publisher's words: an EUR-Lex row rendered "Applicable from ... (publisher)" and attributed
  // to the Union an applicability claim it does not make. There is no parameter to get wrong
  // now, because the record carries the answer.
  //
  // The row is not re-validated here. `renderSearchResults` requires every row once, before
  // anything is derived from them, and a second guard in this function was a guard a test could
  // delete without failing.
  const where = `hit ${index + 1}`;
  const semantics = semanticsOf(publisherOf(hit.lex_id, where), where);
  const legal = INTERVAL_SENTENCE[semantics](hit.valid_from, hit.valid_to);

  const badges = hit.match_reasons
    .map((reason) => `<li class="hit-badge">${escapeHtml(MATCH_LABEL.get(reason))}</li>`)
    .join('');

  const title =
    typeof hit.title === 'string' && hit.title.length > 0
      ? `<p class="hit-title" lang="${escapeHtml(hit.title_language ?? hit.language)}">${escapeHtml(hit.title)}</p>`
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
 * @param {string}  input.asOf           the operative date, always explicit even when today
 * @param {string}  input.timeScope      as_of or all_versions; the service's own answer
 * @param {Array}   input.hits
 * @param {object}  input.rowSet         `{ returned, total }` for the served page
 * @param {object}  input.population      the structured disclosure, as the no-hit card takes
 * @param {object}  [input.governing]     the one resolved instrument, when the query named one
 * @param {object}  input.relaxations    every relaxation, each declaring whether it applied
 * @param {string}  [input.searchPath]    the path a revert link goes back to
 * @param {Array}   [input.layers]        for the zero-hit case
 * @param {Array}   [input.expansions]    what the query was turned into
 * @param {Array}   [input.routes]        official routes out, for the zero-hit case
 */
export function renderSearchResults({
  query,
  semantics: declaredSemantics,
  asOf,
  timeScope,
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
  // There is no semantics parameter. The vocabulary is a property of each record's publisher
  // and is derived below, so a caller cannot pass one that disagrees with the data. Passing one
  // is refused rather than ignored: a caller who believes they are choosing the vocabulary has
  // misunderstood the contract, and silently overriding them leaves them believing it worked.
  if (declaredSemantics !== undefined) {
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
  // The account is required, and closed: one entry per relaxation, none missing and none
  // invented. The contract is the disclosure module's, checked here rather than restated, so
  // this screen and the disclosures it renders cannot come to disagree about what an account is.
  requireRelaxationAccount(relaxations);
  if (!TIME_SCOPES.includes(timeScope)) {
    throw new Error(
      `results say whether they were narrowed to a date; ${JSON.stringify(timeScope)} is not ` +
        `one of ${TIME_SCOPES.join(', ')}, and the service answers all_versions by default`,
    );
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

  // Every row validated once, here, before anything is derived from any of them. It used to
  // happen inside the as_of branch and again inside renderHit, so an all_versions list reached
  // the badge cross-check below with rows nothing had checked, and a row that never said why it
  // matched failed there as a type error instead of saying so.
  hits.forEach(requireHit);

  // A row under a date-scoped heading must satisfy that scope. Nothing compared the two, so a
  // long-superseded state could be listed among the provisions applicable on a date it does not
  // cover, under a heading asserting exactly that.
  if (timeScope === 'as_of') {
    hits.forEach((hit, index) => {
      if (!covers(hit, asOf)) {
        throw new Error(
          `hit ${index + 1} is applicable from ${hit.valid_from} to ` +
            `${hit.valid_to ?? 'no end recorded'} and does not cover ${asOf}, while the heading ` +
            'says these rows are the ones applicable on that date',
        );
      }
    });
  }

  // A relaxation that ran without its disclosure is the screen answering a question the reader
  // did not ask. The expansions are the evidence that one ran, so they cannot be silent.
  // The expansions are evidence a rewrite happened, and they belong to the relaxation that
  // produced them. Testing whether ANY relaxation applied let fuzzy substitutions disappear
  // behind an unrelated crosswalk, so the reader saw a disclosure about the wrong thing.
  if (expansions.length > 0 && relaxations.fuzzy?.applied !== true) {
    throw new Error(
      `this query was expanded into ${expansions.join(', ')} and fuzzy expansion is not ` +
        'declared as applied; substitutions have to be attributed to the relaxation that made ' +
        'them, or the reader is answered on words nobody says were used',
    );
  }
  // Every badge that is evidence of a relaxation must be matched by that relaxation declaring
  // itself applied. The expansions check above catches a rewritten query with no relaxation
  // declared; this catches the same failure from the other end, a row badged "semantic match"
  // inside a result set whose account says semantic retrieval never ran. One of those two is
  // false, and the reader is looking at the badge.
  hits.forEach((hit, index) => {
    for (const reason of hit.match_reasons) {
      const evidenced = REASON_EVIDENCES.get(reason);
      if (evidenced !== undefined && relaxations[evidenced].applied !== true) {
        throw new Error(
          `hit ${index + 1} is badged ${JSON.stringify(reason)}, which is evidence that the ` +
            `${evidenced} relaxation ran, while this result set declares ${evidenced} as not ` +
            'applied; the badge and the account cannot both be true',
        );
      }
    }
  });

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

  // Null when the rows disagree, which a multi-publisher result set routinely does. The heading
  // may use one publisher's words only when every row shares that publisher's clock; over a
  // mixed list, picking one states a claim about the rows it does not describe. The neutral
  // wording is not a hedge, it is the only true sentence available over rows that make
  // different kinds of assertion.
  const scope = sharedSemantics(
    hits.map((hit, index) => publisherOf(hit.lex_id, `hit ${index + 1}`)),
    'the result heading',
  );
  const heading =
    timeScope === 'as_of'
      ? scope === null
        ? `States covering ${asOf}, each in its own publisher's terms`
        : DATE_SCOPE[scope](asOf)
      : scope === null
        ? "Every state held for these provisions, each in its own publisher's terms, not " +
          'narrowed to one date'
        : SCOPE_HEADING.get(scope);

  return (
    '<section class="results">' +
    `<h2 class="results-scope">${escapeHtml(heading)}</h2>` +
    `<p class="results-operative-date">Operative date: ${escapeHtml(asOf)}.` +
    (timeScope === 'as_of'
      ? ''
      : ' These rows were not narrowed to it; each carries its own interval below.') +
    '</p>' +
    `<p class="results-query">You asked: ${escapeHtml(query)}</p>` +
    disclosures +
    governingHtml +
    `<ol class="hits">${hits.map((hit, i) => renderHit(hit, i)).join('')}</ol>` +
    pager +
    renderPopulation(population) +
    '</section>'
  );
}
