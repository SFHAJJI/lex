// The search screen as a whole page, so the composition is exercised end to end.
//
// Two screens, because the two shapes fail differently. A screen with rows has to disclose what it
// did to the query and what it is a page of; a screen with none has to avoid being read as the law
// being silent. Every value here is synthetic and none of it is law.
//
// This page is in the build and it hydrates.
//
// It was held out for a while, and the reason was a property of the harness rather than of the
// screen: `browser-evidence.mjs` counted every button on a built page as an inert control,
// unconditionally, because until now no built page had carried a control. That check now asks
// whether the page ships a script first, which is what its own failure message always said.
//
// Fixing the check was not enough, and it should not have been. With the check corrected the page
// still failed, honestly: it shipped no script, so its seven controls really did have no
// activation path. A search screen whose controls cannot be operated is not a delivered journey,
// and a rendered picture of one is worse than nothing because it looks finished. So the page
// hydrates now, like the dossier proof, and the controls work: the chips toggle, the date default
// can be removed, and a pair of rows arms the comparison.
//
// The three handlers that leave the page are still no-ops, and that is honest rather than lazy.
// Opening a row, submitting a date and running a comparison all need a service, and this preview
// has none. Every value here is synthetic and none of it is law.
//
// Building it once, before any of this, is what found three real defects in these components. Two
// are repaired in `CompareArming.jsx` and here: the row title and its interval were painted flush,
// so the date a state applied read as part of its name, and the Compare button used `disabled`,
// which removes it from the tab order, so a keyboard reader never learned comparison existed or
// why a pair was refused. The third was that every control rendered 21 CSS pixels tall against the
// WCAG 2.2 minimum of 24, because the design system had no control sizing at all; that is fixed in
// `styles.css` and this page is what measures it.

import { Document } from './Document.jsx';
import { SearchScreen } from './SearchScreen.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';

const WORK = 'preview-synthetic:synthetic-preview-work';
const ANNEX = 'preview-synthetic:synthetic-preview-annex';
const AS_OF = '2026-09-01';

const POPULATION = Object.freeze({
  searchable_works: [
    { what: 'consolidated LU works held by this corpus', count: 1111, counted_at: '2026-08-15' },
    { what: 'reviewed EU works held by this corpus', count: 2222, counted_at: '2026-08-15' },
  ],
  not_searchable: [
    {
        // Synthetic populations, not measured ones. `counted_at` satisfies Decision 27's
        // dating rule, and a dated real number is still a real number on a page that tells
        // the reader every value here is synthetic. The dating rule and the synthetic
        // banner are two different claims and this fixture has to satisfy both, so the
        // counts are obviously invented and the shape is what the preview demonstrates.
      what:
        // See the note in scripts/search-preview.mjs: the dated count is fine, an undated
        // population figure in the prose beside it is not, and the guard cannot see it there.
        'LU acts that never receive a consolidated edition and are therefore not ' +
        'searchable here',
      count: 7777,
      counted_at: '2026-08-15',
    },
  ],
});

// Two provisions of one state share its identifier, which is why a row is keyed by its position.
const HITS = Object.freeze([
  {
    lex_id: `${WORK}:2001-01-01`,
    title: 'Acte synthetique de demonstration, article 1',
    language: 'fr',
    valid_from: '2001-01-01',
    valid_to: null,
    match_reasons: ['keyword'],
  },
  {
    lex_id: `${WORK}:2001-01-01`,
    title: 'Acte synthetique de demonstration, article 2',
    language: 'fr',
    valid_from: '2001-01-01',
    valid_to: null,
    match_reasons: ['keyword', 'exact_title'],
  },
  {
    lex_id: `${ANNEX}:2003-05-05`,
    title: 'Annexe synthetique de demonstration',
    language: 'fr',
    valid_from: '2003-05-05',
    valid_to: null,
    // Badged as the editorial crosswalk's doing, which the account below declares as applied. A
    // row badged this way inside an account that says the crosswalk never ran is refused.
    match_reasons: ['interpreted'],
  },
]);

const FILTERS = Object.freeze([
  {
    key: 'main-work',
    label: 'The main work only',
    keeps: (hit) => hit.lex_id.startsWith(`${WORK}:`),
  },
  {
    key: 'own-words',
    label: 'Rows that matched my own words',
    keeps: (hit) => hit.match_reasons.includes('keyword'),
  },
]);

const RELAXED = Object.freeze({
  fuzzy: { applied: true, expansions: ['many -> mady', 'many -> man'] },
  crosswalk: {
    applied: true,
    understood_as: 'garantie locative',
    version: 'crosswalk/1',
    reviewed_on: '2026-08-15',
  },
  semantic: { applied: false },
});

const noop = () => {};

/** The element, built once so the two renderers cannot diverge by construction. */
export function searchScreenTree() {
  return (
    <>
      <section className="results-case">
        <h2>A page of a larger result set, produced by a rewritten query</h2>
        <p className="results-case-note">
          Three rows of forty-seven matching passages. The query was expanded and read as another
          term before it ran, and both disclosures carry their own way back to the exact words.
        </p>
        <SearchScreen
          query="how many months deposit"
          today={AS_OF}
          asOf={AS_OF}
          hits={HITS}
          matchingTotal={47}
          population={POPULATION}
          relaxations={RELAXED}
          searchPath="/ask/search"
          filters={FILTERS}
          governing={{
            lex_id: WORK,
            why:
              'Your question names this instrument by title, so it is the answer and the passages ' +
              'below are context within it.',
          }}
          resolved={{
            lex_id: `${WORK}:2001-01-01`,
            valid_from: '2001-01-01',
            valid_to: null,
            publication_date: '2000-12-01',
          }}
          onOpen={noop}
          onSubmitDate={noop}
          onCompare={noop}
        />
      </section>

      <section className="results-case">
        <h2>Nothing found, which is not the law being silent</h2>
        <p className="results-case-note">
          The live shape: an English lay query answered by expanding "many" into nonsense and
          returning nothing, while the rule exists. The card names every layer, its outcome and the
          language it ran in, what the corpus holds and what it does not, and where to look next.
        </p>
        <SearchScreen
          query="security deposit how many months landlord"
          today={AS_OF}
          asOf={AS_OF}
          hits={[]}
          matchingTotal={0}
          population={POPULATION}
          relaxations={{
            fuzzy: { applied: true, expansions: ['many -> mady', 'many -> man'] },
            crosswalk: { applied: false },
            semantic: { applied: false },
          }}
          searchPath="/ask/search"
          filters={[]}
          layers={[
            { name: 'work_resolution', outcome: 'not_run', language: 'en' },
            { name: 'exact_identifier', outcome: 'ran', language: 'en' },
            { name: 'keyword', outcome: 'ran', language: 'en' },
            { name: 'lay_vocabulary_bridge', outcome: 'not_applicable', language: 'en' },
            { name: 'semantic', outcome: 'unavailable', language: 'en' },
          ]}
          routes={[
            {
              label: 'Search the publisher directly',
              publisher: 'lu-legilux',
              uri: 'https://legilux.public.lu/',
            },
            {
              label: 'Search the Union publisher',
              publisher: 'eu-eurlex',
              uri: 'https://eur-lex.europa.eu/',
            },
          ]}
          onOpen={noop}
          onSubmitDate={noop}
          onCompare={noop}
        />
      </section>
    </>
  );
}

/** The whole page, server side, and then hydrated by `/client.js`. */
export function renderSearchScreenPage() {
  return renderHydratableDocument(
    <Document state="search-react" title="Search" shell="ask" density={skinFor('ask').density}>
      <p className="eyebrow">Ask</p>
      <h1>Search</h1>
      <p>
        The search screen, composed from the controls it is made of. Every value here is synthetic
        and none of it is law. The page is server-rendered and carries no script, so the controls
        are shown rather than operated.
      </p>

      <div id="search-root">{searchScreenTree()}</div>
      <script src="/client.js" defer />
    </Document>,
  );
}
