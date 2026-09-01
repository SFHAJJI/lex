// Discovery, in the four results that are read as something they are not.
//
// A complete list, a list that was cut, a list produced by words the reader did not type, and
// no list at all. The last is the one that matters most: zero hits is the result a reader
// takes as "the law does not say so", and it is the result this service is least entitled to
// let them take that way.
//
// The zero-hit case here is the live one. Asking the service in English for the rule on rental
// deposits returns nothing while expanding the query into "mady" and "man", and the rule
// exists. That is what this screen has to survive.
//
// Every value is synthetic and none of it is law.

import { page } from './render.mjs';
import { renderSearchResults } from './search-results.mjs';
import { skinFor } from './shells.mjs';

const WORK = 'preview-synthetic:synthetic-preview-work';
const AS_OF = '2026-09-01';

const POPULATION = {
  searchable_works: [
    { what: 'consolidated LU works held by this corpus', count: 1402, counted_at: '2026-08-15' },
    { what: 'reviewed EU works held by this corpus', count: 1250, counted_at: '2026-08-15' },
  ],
  not_searchable: [
    {
      what:
        'LU acts, of a 24,622 LOI and RGD population, that never receive a consolidated ' +
        'edition and are therefore not searchable here',
      count: 23370,
      counted_at: '2026-08-15',
    },
  ],
};

const OFF = {
  fuzzy: { applied: false },
  crosswalk: { applied: false },
  semantic: { applied: false },
};

function hit(overrides = {}) {
  return {
    lex_id: `${WORK}:2001-01-01`,
    valid_from: '2001-01-01',
    valid_to: null,
    publication_date: '2000-12-01',
    text_available: true,
    permalink: `https://law.soufien.lu/preview-synthetic/synthetic-preview-work/2001-01-01--${'a'.repeat(64)}`,
    match_reasons: ['keyword'],
    provision_num: 'Art. 1',
    chapter_path: 'Title I, Chapter 2',
    ...overrides,
  };
}

function section(heading, note, html) {
  return (
    `      <section class="results-case"><h2>${heading}</h2>` +
    `<p class="results-case-note">${note}</p>${html}</section>\n`
  );
}

/** The discovery preview, in the Ask shell, because its reader is a citizen with a question. */
export function renderSearchPreview({ locale = 'en' } = {}) {
  const resolved = renderSearchResults({
    query: 'synthetic preview work article 1',
    semantics: 'publisher_applicability',
    asOf: AS_OF,
    timeScope: 'as_of',
    hits: [hit(), hit({ provision_num: 'Art. 2', match_reasons: ['keyword', 'semantic'] })],
    rowSet: { returned: 2, total: 2 },
    population: POPULATION,
    relaxations: OFF,
    searchPath: '/ask/search',
    governing: {
      lex_id: WORK,
      why: 'Your question names this instrument by title, so it is the answer and the passages below are context within it.',
    },
  });

  const cut = renderSearchResults({
    query: 'synthetic preview provision',
    semantics: 'official_consolidation_state',
    asOf: AS_OF,
    timeScope: 'all_versions',
    hits: [
      hit({ provision_num: 'Art. 3', match_reasons: ['exact_title'] }),
      hit({ provision_num: 'Art. 3', valid_from: '2001-01-01', valid_to: '2004-01-01' }),
    ],
    rowSet: { returned: 2, total: 47 },
    population: POPULATION,
    relaxations: OFF,
    searchPath: '/ask/search',
  });

  const relaxed = renderSearchResults({
    query: 'how many months deposit',
    semantics: 'publisher_applicability',
    asOf: AS_OF,
    timeScope: 'as_of',
    hits: [hit({ provision_num: 'Art. 5', match_reasons: ['interpreted'] })],
    rowSet: { returned: 1, total: 1 },
    population: POPULATION,
    expansions: ['many -> mady', 'many -> man'],
    relaxations: {
      ...OFF,
      fuzzy: { applied: true, expansions: ['many -> mady', 'many -> man'] },
    },
    searchPath: '/ask/search',
  });

  const nothing = renderSearchResults({
    query: 'security deposit how many months landlord',
    semantics: 'publisher_applicability',
    asOf: AS_OF,
    timeScope: 'as_of',
    hits: [],
    // Zero rows and a zero total, stated rather than omitted. This preview passed no row set
    // at all on the empty path, which is exactly the hole O9 names: without it nothing
    // distinguishes an empty page of a larger result set from a corpus holding no match, and
    // this is the one screen where that distinction is the whole product.
    rowSet: { returned: 0, total: 0 },
    population: POPULATION,
    layers: [
      { name: 'work_resolution', outcome: 'not_run', language: 'en' },
      { name: 'exact_identifier', outcome: 'ran', language: 'en' },
      { name: 'keyword', outcome: 'ran', language: 'en' },
      { name: 'lay_vocabulary_bridge', outcome: 'not_applicable', language: 'en' },
      { name: 'semantic', outcome: 'unavailable', language: 'en' },
    ],
    expansions: ['many -> mady', 'many -> man'],
    routes: [
      { label: 'Search the publisher directly', publisher: 'lu-legilux', uri: 'https://legilux.public.lu/' },
      { label: 'Search the Union publisher', publisher: 'eu-eurlex', uri: 'https://eur-lex.europa.eu/' },
    ],
  });

  return page({
    state: 'search',
    title: 'Discovery',
    locale,
    shell: 'ask',
    density: skinFor('ask').density,
    main:
      '      <p class="eyebrow">Ask</p>\n' +
      '      <h1>Discovery</h1>\n' +
      '      <p>A list of results is read as an answer about the law, and it is an answer ' +
      'about something narrower: what this corpus holds, under the retrieval that actually ' +
      'ran, among the rows that came back. Each of those is invisible unless the screen says ' +
      'it.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      section(
        'The query names an instrument',
        'The instrument is the answer and the passages are context within it. Ranking alone ' +
          'puts an unrelated regulation above the governing text, which is measured behaviour ' +
          'rather than a worry.',
        resolved,
      ) +
      section(
        'Every state held, and a list that was cut',
        'The service answers all versions by default, so the heading does not claim these rows ' +
          'were narrowed to a date. The operative date is still stated, and the second row is a ' +
          'state that ended in 2004: legitimate here, and a false claim under the other heading. ' +
          'The list also names its total, because a list that simply ends reads as complete.',
        cut,
      ) +
      section(
        'Words the reader did not type',
        'The expansion is disclosed beside the hits it produced, with a way back to the exact ' +
          'words. A reader answered about a different word has not been answered.',
        relaxed,
      ) +
      section(
        'Nothing found',
        'The result most likely to be read as "the law does not say so", and the one this ' +
          'service is least entitled to let anybody read that way. Every layer that ran is ' +
          'named, every word the query became is shown, and the publisher own search is offered.',
        nothing,
      ),
  });
}
