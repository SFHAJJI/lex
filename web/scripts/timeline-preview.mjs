// The timeline in the shapes that make it lie, on one page.
//
// The interesting cases are all absences and disagreements: a gap, an overlap, a state
// scheduled for a date that has not arrived, a title that names a different state's date. Each
// is rendered here so the browser run measures them, because every one of them is a place where
// the correct output is longer and denser than the wrong one, and denser is where reflow and
// separation break.
//
// Every value is synthetic and none of it is law.

import { page } from './render.mjs';
import { renderTimeline } from './timeline.mjs';
import { skinFor } from './shells.mjs';

const WORK = 'preview-synthetic:synthetic-preview-work';
const AS_OF = '2026-09-01';
const POPULATION =
  'Drawn from the states this corpus holds for this work, not from the states the publisher ' +
  'has published.';

function digest(seed) {
  return seed.repeat(64).slice(0, 64);
}

function state(overrides) {
  return {
    lex_id: `${WORK}:${overrides.valid_from}`,
    publication_date: '2000-12-01',
    observed_from: '2026-01-01T00:00:00Z',
    extraction_profile: 'akn-lu/1',
    text_available: true,
    withdrawn: false,
    ...overrides,
  };
}

function section(heading, note, html) {
  return (
    `      <section class="timeline-case"><h2>${heading}</h2>` +
    `<p class="timeline-case-note">${note}</p>${html}</section>\n`
  );
}

/** The timeline preview, in the Workbench shell: its reader is reading a history. */
export function renderTimelinePreview({ locale = 'en' } = {}) {
  const ordinary = renderTimeline({
    semantics: 'publisher_applicability',
    asOf: AS_OF,
    population: POPULATION,
    truncated: false,
    states: [
      state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('a') }),
      state({
        valid_from: '2004-01-01',
        valid_to: null,
        hash: digest('b'),
        publication_date: '2005-06-01',
      }),
    ],
  });

  const gapped = renderTimeline({
    semantics: 'publisher_applicability',
    asOf: AS_OF,
    population: POPULATION,
    truncated: true,
    totalCount: 12,
    states: [
      state({ valid_from: '1993-04-05', valid_to: '2004-04-02', hash: digest('c') }),
      state({
        valid_from: '2024-12-28',
        valid_to: null,
        hash: digest('d'),
        text_available: false,
        publication_date: '2024-12-20',
      }),
    ],
  });

  const conflicted = renderTimeline({
    semantics: 'publisher_applicability',
    asOf: AS_OF,
    population: POPULATION,
    truncated: false,
    states: [
      state({
        valid_from: '2020-03-14',
        valid_to: '2020-09-25',
        hash: digest('e'),
        publication_date: '2024-11-05',
        title: 'Version consolidee applicable au 25/09/2020 : acte synthetique de demonstration',
        title_language: 'fr',
      }),
      state({
        valid_from: '2001-01-01',
        valid_to: '2020-03-14',
        hash: digest('f'),
        title: 'Version consolidee applicable au 25/09/2020 : acte synthetique de demonstration',
        title_language: 'fr',
      }),
      state({
        valid_from: '2020-01-01',
        valid_to: '2020-12-31',
        hash: digest('1'),
        publication_date: '2019-11-01',
      }),
    ],
  });

  const scheduled = renderTimeline({
    semantics: 'official_consolidation_state',
    asOf: AS_OF,
    population: POPULATION,
    truncated: false,
    states: [
      state({ valid_from: '2016-04-27', valid_to: '2016-05-03', hash: digest('2'), extraction_profile: 'xhtml-eu/1' }),
      state({
        valid_from: '2029-03-29',
        valid_to: null,
        hash: digest('3'),
        extraction_profile: 'xhtml-eu/1',
        publication_date: '2026-02-01',
      }),
    ],
  });

  const withdrawn = renderTimeline({
    semantics: 'publisher_applicability',
    asOf: AS_OF,
    population: POPULATION,
    truncated: false,
    states: [
      state({ valid_from: '2001-01-01', valid_to: '2004-01-01', hash: digest('4') }),
      state({
        valid_from: '2004-01-01',
        valid_to: null,
        hash: digest('5'),
        withdrawn: true,
        withdrawn_from_source: '2026-02-01',
      }),
    ],
  });

  return page({
    state: 'timeline',
    title: 'Timeline',
    locale,
    shell: 'w',
    density: skinFor('w').density,
    main:
      '      <p class="eyebrow">Workbench</p>\n' +
      '      <h1>Timeline</h1>\n' +
      '      <p>This screen is the two clocks. The cases below are the ones where a chart ' +
      'would otherwise draw something the publisher never said: a gap read as continuity, two ' +
      'states merged into one, a scheduled date read as a current one, and a title read as a ' +
      'record.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      section(
        'Two states, no surprises',
        'The second was published after it began to apply, which is the ordinary case for ' +
          'this publisher rather than the exception.',
        ordinary,
      ) +
      section(
        'A gap, and a list that stops',
        'The gap is computed from the intervals and says so. The list names its total, ' +
          'because a list that simply ends reads as a complete one.',
        gapped,
      ) +
      section(
        'A title that names another state, and two states covering one day',
        'Both disagreements are the publisher\'s own. The record places the row; the title ' +
          'never does, and neither overlapping state is preselected.',
        conflicted,
      ) +
      section(
        'A state scheduled for a date that has not arrived',
        'In the Union\'s vocabulary rather than Luxembourg\'s, because the two publishers ' +
          'make different claims and this screen does not choose between them.',
        scheduled,
      ) +
      section(
        'A state the publisher withdrew',
        'Struck, with the date the publisher withdrew it, because a strike with no date is a ' +
          'rumour.',
        withdrawn,
      ),
  });
}
