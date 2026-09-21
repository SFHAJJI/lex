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
import { PREVIEW_ANSWERS as COVERAGE_PREVIEWS } from '../scripts/coverage-preview.mjs';
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

/**
 * The React coverage page, in the three shapes the string preview shows.
 *
 * The answers come from `scripts/coverage-preview.mjs` rather than being written again here. Two
 * sets of coverage fixtures is how this page came to carry a payload the platform stopped sending:
 * the string preview and this one each held their own, and both were edited by hand whenever the
 * page changed. One set means the browser run measures the same answers the string page does, and
 * the shape bridge in `test/coverage.test.mjs` holds that one set against the captured answer.
 */
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
        failure mode is not a wrong answer but a comfortable one: a count presented as current, a
        breakdown that reads as complete because nothing said it was not, two numbers in one row
        that cannot both be true.
      </p>
      <p>
        Nothing on it says when the counting happened. This mount holds no build time and records
        that it does not, so what names the artifacts these counts came from is a pair of digests
        rather than an instant. The calendar dates in the tables are the publisher&rsquo;s facts
        about the law and are a different kind of thing.
      </p>
      <p>Every value on this page is synthetic and none of it is law.</p>
      {COVERAGE_PREVIEWS.map((preview) => (
        <section className="coverage-case" key={preview.heading}>
          <h2>{preview.heading}</h2>
          <p className="coverage-case-note">{preview.note}</p>
          <Coverage answer={preview.answer} />
        </section>
      ))}
    </Document>,
  );
}
