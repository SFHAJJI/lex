// Reading, in the five shapes where a page of law says something the publisher did not.
//
// A wording the publisher dates before the state that carries it, which is 39.8 percent of
// the Luxembourg provision states and therefore the ordinary case rather than the exotic one.
// A provision whose licence forbids republishing its wording, which renders as a blank pane
// unless something makes the absence legible. A provision this corpus holds no text for at
// all, which is a different absence with a different answer. An anchor the version does not
// contain, which is the one case where an empty page is indistinguishable from a provision
// that says nothing. And a state the publisher scheduled for a date that has not arrived,
// which without a mark reads as current law.
//
// Every value is synthetic and none of it is law.

import { page } from './render.mjs';
import { renderReading } from './reading.mjs';
import { skinFor } from './shells.mjs';

const PUBLISHER = 'preview-synthetic';
const WORK = 'synthetic-preview-work';
const AS_OF = '2021-03-15';

function digest(seed) {
  return seed.repeat(64).slice(0, 64);
}

const AUTHENTICITY = {
  schema: 'lex-v3-resource-authenticity/1',
  resource_id: `${PUBLISHER}:${WORK}/2021-01-26/fr`,
  authentic_languages: ['fr'],
  basis: 'the publisher declares one authentic language for this resource',
  asserted_by: 'the publisher',
  publisher: PUBLISHER,
  official_uri: 'https://preview.invalid/synthetic-preview-work/2021-01-26/fr',
  observed_at: '2026-08-14T23:05:14Z',
};

const EXPRESSION = {
  resource_id: AUTHENTICITY.resource_id,
  language: 'fr',
  authenticity: AUTHENTICITY,
};

const ENVELOPE = { timeline_semantics: 'publisher_applicability' };

function state(overrides = {}) {
  return {
    lex_id: `${PUBLISHER}:${WORK}:2021-01-26`,
    valid_from: '2021-01-26',
    valid_to: '2021-04-23',
    publication_date: '2021-01-26',
    observed_from: '2026-08-14T23:05:14Z',
    hash: digest('1'),
    record_sha256: digest('2'),
    source_uri: 'https://preview.invalid/synthetic-preview-work/2021-01-26/fr',
    consolidation_status: 'published',
    withdrawn: false,
    ...overrides,
  };
}

const CONFLICTED = {
  anchor: 'art_l_121-6',
  num: 'Art. L. 121-6.',
  wording_valid_from: '2020-11-01',
  text_status: 'held',
  text:
    'Texte synthetique de demonstration. Ce paragraphe occupe la place du texte publie et '
    + 'n a aucune valeur juridique.',
  text_sha256: digest('3'),
};

const AGREEING = {
  anchor: 'art_l_121-7',
  num: 'Art. L. 121-7.',
  wording_valid_from: '2021-01-26',
  text_status: 'held',
  text: 'Deuxieme paragraphe synthetique, dans la meme expression que le precedent.',
  text_sha256: digest('4'),
  renderings: [
    {
      language: 'en',
      text: 'Second synthetic paragraph, rendered outside the authentic language.',
    },
  ],
};

const WITHHELD = {
  anchor: 'art_l_121-8',
  num: 'Art. L. 121-8.',
  wording_valid_from: '2021-01-26',
  text_status: 'withheld',
  licence: 'a publisher licence this corpus may not republish under',
  digest_observed_at: '2026-08-14T23:05:14Z',
  text_sha256: digest('5'),
  official_uri: 'https://preview.invalid/synthetic-preview-work/2021-01-26/art_l_121-8',
};

const NOT_AVAILABLE = {
  anchor: 'art_l_121-9',
  num: 'Art. L. 121-9.',
  wording_valid_from: '2021-01-26',
  text_status: 'not_available',
  official_uri: 'https://preview.invalid/synthetic-preview-work/2021-01-26/art_l_121-9',
  gazette_chain: 'the synthetic gazette chain for this preview act',
};

function section(heading, note, html) {
  return (
    `      <section class="reading-case"><h2>${heading}</h2>`
    + `<p class="reading-case-note">${note}</p>${html}</section>\n`
  );
}

/** The reading preview, in the Workbench shell: its reader is working through one article. */
export function renderReadingPreview({ locale = 'en' } = {}) {
  const ordinary = renderReading({
    envelope: ENVELOPE,
    work: { publisher: PUBLISHER, work: WORK },
    state: state(),
    expression: EXPRESSION,
    provisions: [CONFLICTED, AGREEING],
    holes: [],
    asOf: AS_OF,
  });

  const absent = renderReading({
    envelope: ENVELOPE,
    work: { publisher: PUBLISHER, work: WORK },
    state: state(),
    expression: EXPRESSION,
    provisions: [WITHHELD, NOT_AVAILABLE],
    holes: [{ kind: 'no_state_held', from: '2004-04-02', to: '2024-12-28' }],
    asOf: AS_OF,
  });

  const refused = renderReading({
    envelope: ENVELOPE,
    work: { publisher: PUBLISHER, work: WORK },
    state: state(),
    expression: EXPRESSION,
    provisions: [CONFLICTED, AGREEING, WITHHELD, NOT_AVAILABLE],
    holes: [],
    asOf: AS_OF,
    anchor: 'art_l121-6',
  });

  const scheduled = renderReading({
    envelope: ENVELOPE,
    work: { publisher: PUBLISHER, work: WORK },
    state: state({
      lex_id: `${PUBLISHER}:${WORK}:2030-09-15`,
      valid_from: '2030-09-15',
      valid_to: null,
      publication_date: '2026-06-30',
      hash: digest('6'),
      record_sha256: digest('7'),
    }),
    expression: EXPRESSION,
    provisions: [{ ...AGREEING, wording_valid_from: '2030-09-15', renderings: [] }],
    holes: [],
    asOf: AS_OF,
  });

  const withdrawnHash = digest('8');
  const liveHash = digest('9');
  const superseded = renderReading({
    envelope: ENVELOPE,
    work: { publisher: PUBLISHER, work: WORK },
    state: state({ hash: withdrawnHash, withdrawn: true }),
    expression: EXPRESSION,
    provisions: [AGREEING],
    holes: [],
    asOf: AS_OF,
    superseded: {
      live: {
        valid_from: '2021-01-26',
        hash: liveHash,
        publication_date: '2021-02-11',
        href: `/${PUBLISHER}/${WORK}/2021-01-26--${liveHash}`,
        withdrawn: false,
      },
      withdrawn: [
        {
          valid_from: '2021-01-26',
          hash: withdrawnHash,
          publication_date: '2021-01-26',
          href: `/${PUBLISHER}/${WORK}/2021-01-26--${withdrawnHash}`,
          withdrawn: true,
        },
      ],
    },
  });

  return page({
    state: 'reading',
    title: 'Reading',
    locale,
    copyLocale: locale,
    shell: 'w',
    density: skinFor('w').density,
    main:
      '      <p class="eyebrow">Workbench</p>\n'
      + '      <h1>Reading</h1>\n'
      + '      <p>One work, one date, and the publisher text as it stood. The chrome on this '
      + 'page is English and the quoted text is not, so each carries its own language: the '
      + 'chrome language switcher never changes the language of the law.</p>\n'
      + '      <p>Every value on this page is synthetic and none of it is law.</p>\n'
      + section(
        'A wording the publisher dates before the state carrying it',
        'Both dates are the publisher own and neither is derived, so both are shown and '
          + 'neither is chosen. This is 39.8 percent of the Luxembourg provision states, which '
          + 'makes it the ordinary case rather than the exception.',
        ordinary,
      )
      + section(
        'Two absences that are not the same absence',
        'A licence that forbids republishing the wording is not the same as holding no text. '
          + 'The first renders as its digest and the publisher own file; the second says what '
          + 'is missing and where the publisher keeps it. Rendered as a blank pane, both read '
          + 'as a provision that says nothing.',
        absent,
      )
      + section(
        'An anchor this version does not contain',
        'The refusal keeps the two clocks and hands back the anchors this version does have. '
          + 'It does not fall back to searching the text for something similar, because a '
          + 'different provision is not a near miss.',
        refused,
      )
      + section(
        'A state the publisher scheduled for a date that has not arrived',
        'The publisher has recorded this state and it has not begun. Without the mark it '
          + 'reads as current law, and the mark is a label and a sentence rather than a colour.',
        scheduled,
      )
      + section(
        'A state the publisher withdrew and replaced',
        'The publisher ranked these two, so no choice is asked of the reader. The withdrawn '
          + 'state stays addressable, because a link somebody already has should not lead '
          + 'nowhere.',
        superseded,
      ),
  });
}
