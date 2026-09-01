// The React timeline and coverage screens as built pages, so the browser run measures them.
//
// Same job as `hydration-proof.jsx`: a real truth surface rendered through the real shell, kept
// out of the component files so those stay components. The cases are the ones where these two
// screens go wrong quietly rather than loudly, because the correct output is always the longer
// and denser one, and denser is where reflow and separation break.
//
// The timeline's wide table and coverage's two facet tables each scroll inside their own box.
// That is the property this page exists to have measured at 320 CSS pixels: a page that scrolls
// sideways hides a column, and a hidden column on this product is a hidden disclosure.
//
// Every value here is synthetic and none of it is law.

import { Coverage } from './Coverage.jsx';
import { Document } from './Document.jsx';
import { Timeline } from './Timeline.jsx';
import { renderDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';

const WORK = 'preview-synthetic:synthetic-preview-work';
const UNION_WORK = 'eu-eurlex:synthetic-preview-union';
const AS_OF = '2026-09-01';
const POPULATION =
  'Drawn from the states this corpus holds for this work, not from the states the publisher ' +
  'has published.';

const digest = (seed) => seed.repeat(64).slice(0, 64);

function state(work, overrides) {
  return {
    lex_id: `${work}:${overrides.valid_from}`,
    publication_date: '2000-12-01',
    observed_from: '2026-01-01T00:00:00Z',
    extraction_profile: 'akn-lu/1',
    text_available: true,
    withdrawn: false,
    ...overrides,
  };
}

/** One case, with the sentence saying what it is there to show. */
function Case({ heading, note, children }) {
  return (
    <section className="timeline-case">
      <h2>{heading}</h2>
      <p className="timeline-case-note">{note}</p>
      {children}
    </section>
  );
}

/** The React timeline, in the shapes where a chart would draw something nobody said. */
export function renderTimelineReactPage() {
  return renderDocument(
    <Document
      state="timeline-react"
      title="Timeline (React)"
      shell="w"
      density={skinFor('w').density}
    >
      <p className="eyebrow">Workbench</p>
      <h1>Timeline (React)</h1>
      <p>
        This screen is the two clocks, rendered by the React port. The cases below are the ones
        where a chart would otherwise draw something the publisher never said: a gap read as
        continuity, two states merged into one, and a scheduled date read as a current one.
      </p>
      <p>Every value on this page is synthetic and none of it is law.</p>
      <Case
        heading="A gap, and a list that stops"
        note={
          'The gap is derived from the held intervals and says so. The list names its total, ' +
          'because a list that simply ends reads as a complete one.'
        }
      >
        <Timeline
          asOf={AS_OF}
          population={POPULATION}
          totalCount={12}
          states={[
            state(WORK, { valid_from: '1993-04-05', valid_to: '2004-04-02', hash: digest('c') }),
            state(WORK, {
              valid_from: '2024-12-28',
              valid_to: null,
              hash: digest('d'),
              text_available: false,
              publication_date: '2024-12-20',
            }),
          ]}
        />
      </Case>
      <Case
        heading="A title that names another state, and two states covering one day"
        note={
          "Both disagreements are the publisher's own. The record places the row; the title " +
          'never does, and neither overlapping state is preselected.'
        }
      >
        <Timeline
          asOf={AS_OF}
          population={POPULATION}
          totalCount={3}
          states={[
            state(WORK, {
              valid_from: '2020-03-14',
              valid_to: '2020-09-25',
              hash: digest('e'),
              publication_date: '2024-11-05',
              title: 'Version consolidee applicable au 25/09/2020 : acte synthetique',
              title_language: 'fr',
            }),
            state(WORK, {
              valid_from: '2001-01-01',
              valid_to: '2020-03-14',
              hash: digest('f'),
              title: 'Version consolidee applicable au 25/09/2020 : acte synthetique',
              title_language: 'fr',
            }),
            state(WORK, {
              valid_from: '2020-01-01',
              valid_to: '2020-12-31',
              hash: digest('1'),
              publication_date: '2019-11-01',
            }),
          ]}
        />
      </Case>
      <Case
        heading="A Union work, in the Union's own words"
        note={
          'Nothing told this screen which vocabulary to use. It read the publisher out of the ' +
          'records, because the two publishers make different claims and a caller cannot pass ' +
          'one that disagrees with the rows underneath it.'
        }
      >
        <Timeline
          asOf={AS_OF}
          population={POPULATION}
          totalCount={2}
          states={[
            state(UNION_WORK, {
              valid_from: '2016-04-27',
              valid_to: '2016-05-03',
              hash: digest('2'),
              extraction_profile: 'xhtml-eu/1',
            }),
            state(UNION_WORK, {
              valid_from: '2029-03-29',
              valid_to: null,
              hash: digest('3'),
              extraction_profile: 'xhtml-eu/1',
              publication_date: '2026-02-01',
            }),
          ]}
        />
      </Case>
    </Document>,
  );
}

const COMPLETE = {
  envelope: { freshness: { built_at: '2026-08-15T09:22:08Z', stamp_signature_valid: true } },
  publisher_name: 'Synthetic preview publisher',
  works: 40,
  scope_expected_works: 40,
  build_inventory_status: 'complete',
  build_complete: true,
  build_issues: [],
  versions: 120,
  valid_from_earliest: '1849-03-14',
  valid_from_latest: '2030-09-15',
  document_types: [
    { code: 'LOI', versions: 52, versions_with_text: 51 },
    { code: 'RGD', versions: 30, versions_with_text: 30 },
    { code: 'RECUEIL', versions: 25, versions_with_text: 3 },
    { code: null, versions: 13, versions_with_text: 0 },
  ],
  document_types_total: 4,
  facets_truncated: false,
  // Deliberately summing past the headline. A work published in two languages is one work in two
  // rows, so 41 language works against 40 held is the correct shape and not an error.
  languages: [
    { code: 'fr', works: 40, versions: 120 },
    { code: 'de', works: 1, versions: 1 },
  ],
  text: { versions_with_text_served: 84, versions_without_text: 36 },
  known_gaps: [
    'never-consolidated acts are not ingested; the reviewed corpus is dated consolidations only',
    'coverage density follows the publisher own digitised consolidations: dense recently, ' +
      'sparse before, isolated snapshots earlier, forward-dated to the publisher horizon',
  ],
};

const INCOMPLETE = {
  ...COMPLETE,
  build_inventory_status: 'partial',
  build_complete: false,
  build_issues: ['one publisher endpoint did not respond', 'one manifest failed verification'],
};

/** The React coverage page, in a finished build and an unfinished one. */
export function renderCoverageReactPage() {
  return renderDocument(
    <Document
      state="coverage-react"
      title="Coverage (React)"
      shell="dev"
      density={skinFor('dev').density}
    >
      <p className="eyebrow">Gateway</p>
      <h1>Coverage (React)</h1>
      <p>
        This is the page whose job is to say what is missing, rendered by the React port, so its
        failure mode is not a wrong answer but a comfortable one: a count with no date, a total
        with no denominator, a type row saying how many states are held and not how many have
        text.
      </p>
      <p>Every value on this page is synthetic and none of it is law.</p>
      <section className="coverage-case">
        <h2>A build that finished</h2>
        <Coverage coverage={COMPLETE} />
      </section>
      <section className="coverage-case">
        <h2>A build that did not</h2>
        <p className="coverage-case-note">
          No counts at all. A build that did not finish is not a smaller corpus, it is an unknown
          one, and its figures would read as measurements of what is held.
        </p>
        <Coverage coverage={INCOMPLETE} />
      </section>
    </Document>,
  );
}
