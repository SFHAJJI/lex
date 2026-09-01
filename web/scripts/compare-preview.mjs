// The compare screen, in its four states, on one page.
//
// The states are here together for the same reason the trust surface is: the ways a diff can
// be confidently wrong are visual as much as logical, and a refusal that replaces the panes
// has to be measured for contrast, focus order and reflow beside the panes it replaces. A
// diff that reflows into unreadable columns at 320 pixels is a diff nobody checks.
//
// Every value is synthetic and none of it is law.

import { page } from './render.mjs';
import { renderCompare } from './compare.mjs';
import { skinFor } from './shells.mjs';

const PUBLISHER = 'preview-synthetic';
const DIGEST_A = 'dedbcbe0f53f5e2b41fd98551d5913b0ed56525ec35f7b26a6c0fa9eaad4ba3c';
const DIGEST_B = 'cfc9fe90f4f020e99f8da43c8d9e5f74c570eced2ad5d303c6dee7b485eb0212';
const DIGEST_C = '9d3f1a7c2b8e40516d7a9c0f3e2b514886fa7d0c9b1e3a5f7c2d4b6a8e0f1c33';

const LEFT = {
  lex_id: `${PUBLISHER}:synthetic-preview-work:2001-01-01`,
  valid_from: '2001-01-01',
  valid_to: '2004-01-01',
  publication_date: '2000-12-01',
  observed_from: '2026-01-01T00:00:00Z',
  body_sha256: DIGEST_A,
  language: 'fr',
  profile: 'akn-lu/1',
  legal_time_sentence: 'Applicable from 2001-01-01 to 2004-01-01 (publisher)',
};

const RIGHT = {
  ...LEFT,
  lex_id: `${PUBLISHER}:synthetic-preview-work:2004-01-01`,
  valid_from: '2004-01-01',
  valid_to: null,
  publication_date: '2003-12-01',
  body_sha256: DIGEST_B,
  legal_time_sentence: 'Applicable from 2004-01-01 (publisher)',
};

function section(heading, note, html) {
  return (
    `      <section class="compare-case"><h2>${heading}</h2>` +
    `<p class="compare-case-note">${note}</p>${html}</section>\n`
  );
}

/** The compare screen preview, in the Workbench shell: its reader is comparing states. */
export function renderComparePreview({ locale = 'en' } = {}) {
  const changed = renderCompare({
    mode: 'temporal',
    left: LEFT,
    right: RIGHT,
    result: {
      changed: true,
      blocks: [
        {
          anchor_label: 'Art. 1',
          removed: 'LEX V3 SYNTHETIC PREVIEW. The first synthetic provision, as it stood.',
          added: 'LEX V3 SYNTHETIC PREVIEW. The first synthetic provision, as amended.',
        },
        {
          anchor_label: 'Art. 3',
          added: 'LEX V3 SYNTHETIC PREVIEW. A synthetic provision with no earlier counterpart.',
        },
      ],
      renumbering: [{ from: 'art_2', to: 'art_2bis' }],
    },
  });

  const identical = renderCompare({
    mode: 'temporal',
    left: LEFT,
    right: { ...LEFT },
    result: { changed: false, note: 'the same version applied on both dates' },
  });

  const crossProfile = renderCompare({
    mode: 'temporal',
    left: LEFT,
    right: { ...RIGHT, profile: 'pdf-lu/1' },
    result: { changed: true, blocks: [{ anchor_label: 'Art. 1', added: 'never rendered' }] },
  });

  const halfResolved = renderCompare({
    mode: 'temporal',
    left: {
      refusal: {
        code: 'no_version_for_date',
        sentence: 'No publisher state covers 1990-01-01.',
        payload: {
          history_begins: '2001-01-01',
          nearest_earlier: null,
          nearest_later: '2001-01-01',
          what_would_answer: ['new_official_observation', 'expanded_official_scope'],
          asserts_absence_of_law: false,
        },
      },
    },
    right: RIGHT,
    result: { changed: true, blocks: [{ anchor_label: 'Art. 1', added: 'never rendered' }] },
  });

  const language = renderCompare({
    mode: 'language',
    left: { ...LEFT, language: 'en', profile: 'xhtml-eu/1', body_sha256: DIGEST_A },
    right: { ...LEFT, language: 'fr', profile: 'xhtml-eu/1', body_sha256: DIGEST_C },
    result: {
      changed: true,
      blocks: [
        {
          anchor_label: 'Art. 1',
          removed: 'LEX V3 SYNTHETIC PREVIEW. The English expression of one state.',
          added: 'LEX V3 SYNTHETIC PREVIEW. The French expression of the same state.',
        },
      ],
      renumbering: [],
    },
  });

  return page({
    state: 'compare',
    title: 'Compare',
    locale,
    shell: 'w',
    density: skinFor('w').density,
    main:
      '      <p class="eyebrow">Workbench</p>\n' +
      '      <h1>Compare</h1>\n' +
      '      <p>A diff is the most persuasive object here: two columns of red and green read ' +
      'as fact. These five cases are the ones where that reading would be wrong, rendered ' +
      'together so the refusals can be measured beside the panes they replace.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      section(
        'Changed',
        'Provision-aligned, with removals struck through and additions underlined rather ' +
          'than distinguished by colour alone. The renumber row says it was found mechanically.',
        changed,
      ) +
      section(
        'Unchanged',
        'Both dates resolve to one state. The publisher note is rendered verbatim and no ' +
          'panes are built: two empty columns would read as a measurement.',
        identical,
      ) +
      section(
        'Different extraction profiles',
        'Not overridable. The panes do not exist rather than being hidden.',
        crossProfile,
      ) +
      section(
        'One side did not resolve',
        'The side that resolved stays and can be read on its own. Half a resolution does ' +
          'not make a comparison.',
        halfResolved,
      ) +
      section(
        'Language comparison',
        'Two authentic expressions of one state, with separate digests, labelled so it is ' +
          'never read as change over time.',
        language,
      ),
  });
}
