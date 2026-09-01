// Disclosure of relaxation: the load-bearing feature of the Ask screen.
//
// A relaxation is anything the search did to the query other than run it. Expanding "many"
// to "mady", reading "caution" as "garantie locative", ranking by meaning rather than by
// words. Each is defensible and each changes what the reader is looking at, so UX spec
// section 2 requires three independent, visually distinct disclosures, each with its own
// one-tap revert, and states the rule as "no relaxation ever runs without its banner".
// 31-v3-spec puts it more sharply: the banner is part of the retrieval contract, not the
// UI's discretion.
//
// Two live cases say why. The service answers an English lay query by expanding "many" to
// "mady" and "man", which is nonsense, and returns nothing; a reader who cannot see the
// expansion has no way to understand the zero hits. And the expander turned the Portuguese
// "caucao", a rental deposit, into "cacao". Both are silent edits of somebody's question.
//
// So the shape here refuses silence rather than trusting a caller to break it. All three
// relaxations must be declared on every render: an absent relaxation is not "off", it is a
// caller who did not say, and a screen that renders results without knowing whether the
// crosswalk fired cannot honestly disclose that it did not.

import { isCalendarDate } from './temporal.mjs';
import { canonicalSearchPath } from './routes.mjs';

/** The three relaxations, each disclosed on its own terms. */
export const RELAXATIONS = Object.freeze(['fuzzy', 'crosswalk', 'semantic']);

/** The query parameter each revert turns off, and the value that turns it off. */
const REVERT = new Map([
  ['fuzzy', ['fuzzy', 'off']],
  ['crosswalk', ['crosswalk', 'off']],
  ['semantic', ['retrieval_mode', 'keyword']],
]);

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function requireNonempty(value, what) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${what} is required: ${JSON.stringify(value)}`);
  }
  return value;
}

/**
 * The revert path: the same search with one relaxation turned off and the others untouched.
 *
 * Independent, because the disclosures are independent. A reader who wants their exact words
 * back is not also asking to turn off semantic ranking, and a single "turn everything off"
 * control would make the cheapest way to undo one relaxation be to undo all of them.
 */
export function revertPath(searchPath, relaxation) {
  if (!REVERT.has(relaxation)) {
    throw new Error(
      `${JSON.stringify(relaxation)} is not a relaxation; the three are ${RELAXATIONS.join(', ')}`,
    );
  }
  // Through the shared route policy. A leading slash was the whole check, and
  // `//evil.example/x` has one: protocol-relative is off-site and begins with a slash, so the
  // revert control offered a one-tap trip to another origin under a label promising the reader
  // their own words back.
  const search = canonicalSearchPath(searchPath);
  if (search === null) {
    throw new Error(
      `a revert needs the current same-origin search path; ${JSON.stringify(searchPath)} is ` +
        'not one, and a revert that leaves this origin is not a revert',
    );
  }

  const { path, query: rawQuery } = search;
  const params = new URLSearchParams(rawQuery);
  const [name, value] = REVERT.get(relaxation);
  params.set(name, value);
  return `${path}?${params.toString()}`;
}

function disclose({ relaxation, searchPath, heading, body, revertLabel }) {
  const href = revertPath(searchPath, relaxation);
  return (
    `<div class="relaxation relaxation-${escapeHtml(relaxation)}" ` +
    `data-relaxation="${escapeHtml(relaxation)}">` +
    `<p class="relaxation-heading">${escapeHtml(heading)}</p>` +
    body +
    `<p class="relaxation-revert"><a href="${escapeHtml(href)}">${escapeHtml(revertLabel)}</a></p>` +
    '</div>'
  );
}

function fuzzyDisclosure(state, searchPath) {
  if (!Array.isArray(state.expansions) || state.expansions.length === 0) {
    throw new Error(
      'a fuzzy relaxation must list the expansions it applied, verbatim; the live service ' +
        'expanded "many" to "mady" and returned nothing, and a reader who cannot see that ' +
        'cannot understand the result',
    );
  }
  const items = state.expansions
    .map((one) => `<li><code>${escapeHtml(one)}</code></li>`)
    .join('');
  return disclose({
    relaxation: 'fuzzy',
    searchPath,
    heading: 'Fuzzy expansions applied',
    body: `<ul class="relaxation-expansions">${items}</ul>`,
    revertLabel: 'Turn fuzzy expansion off',
  });
}

function crosswalkDisclosure(state, searchPath) {
  const understood = requireNonempty(state.understood_as, 'the crosswalk understood_as');
  const version = requireNonempty(state.version, 'the crosswalk version');
  if (!isCalendarDate(state.reviewed_on)) {
    throw new Error(
      `the crosswalk must carry its review date: ${JSON.stringify(state.reviewed_on)}; it is ` +
        'editorial and not official, so when somebody last looked at it is part of the claim',
    );
  }
  return disclose({
    relaxation: 'crosswalk',
    searchPath,
    heading: `Understood as: ${understood}`,
    // The label is the component's, not the caller's. "Editorial crosswalk, not official" is
    // the whole reason this disclosure exists, and a caller who could phrase it would
    // eventually phrase it away.
    body:
      '<p class="relaxation-note">Editorial crosswalk, not official. Version ' +
      `${escapeHtml(version)}, reviewed ${escapeHtml(state.reviewed_on)}.</p>`,
    revertLabel: 'Search my exact words instead',
  });
}

function semanticDisclosure(state, searchPath) {
  const encoder = requireNonempty(state.encoder, 'the semantic encoder');
  const benchmark = requireNonempty(state.benchmark, 'the semantic benchmark version');
  return disclose({
    relaxation: 'semantic',
    searchPath,
    heading: 'Ranked by meaning, not only by words',
    body:
      `<p class="relaxation-note">Encoder ${escapeHtml(encoder)}, passing benchmark ` +
      `${escapeHtml(benchmark)}. Semantic ranking serves only behind that gate.</p>`,
    revertLabel: 'Rank by keywords instead',
  });
}

const DISCLOSURE = new Map([
  ['fuzzy', fuzzyDisclosure],
  ['crosswalk', crosswalkDisclosure],
  ['semantic', semanticDisclosure],
]);

/**
 * Every relaxation, declared, and a disclosure for each one that ran.
 *
 * @param {object} input
 * @param {string} input.searchPath   the current search path, which the reverts are built from
 * @param {object} input.relaxations  one entry per member of RELAXATIONS, each with `applied`
 */
export function renderRelaxationDisclosures({ searchPath, relaxations }) {
  for (const relaxation of RELAXATIONS) {
    const state = relaxations?.[relaxation];
    if (typeof state?.applied !== 'boolean') {
      throw new Error(
        `${relaxation} must declare whether it was applied; an absent relaxation is not "off", ` +
          'it is a caller who did not say, and a screen that does not know cannot disclose',
      );
    }
  }

  const extra = Object.keys(relaxations).filter((name) => !RELAXATIONS.includes(name));
  if (extra.length > 0) {
    throw new Error(
      `${extra.join(', ')} is not a relaxation this interface can disclose; adding one to the ` +
        'retrieval path without adding it here is how a silent relaxation ships',
    );
  }

  const applied = RELAXATIONS.filter((relaxation) => relaxations[relaxation].applied);
  if (applied.length === 0) return '';

  // Three independent blocks, in the fixed order, each visually distinct and separately
  // revertible. Not one merged banner: merging them makes the cheapest undo undo everything.
  return (
    '<div class="relaxations">' +
    applied
      .map((relaxation) => DISCLOSURE.get(relaxation)(relaxations[relaxation], searchPath))
      .join('') +
    '</div>'
  );
}
