// One provision's history, in the three shapes that fail differently.
//
// The values keep the proportion that makes this screen worth having, taken from a real
// `article_history` payload: an article whose work has many consolidations and whose own wording
// changed twice. A preview where every version changed the article would hide the entire reason
// the rows say what they count.
//
// The middle case is the one to read. Its first row carries two publisher dates that disagree:
// the provision takes effect on one date and the version it sits in applies from another. Both are
// shown, because both are the publisher's.

import { page } from './render.mjs';
import { renderProvisionHistory } from './provision-history.mjs';
import { skinFor } from './shells.mjs';

const WORK = 'preview-synthetic:synthetic-preview-work';
const link = (date, seed) =>
  `https://law.soufien.lu/preview-synthetic/synthetic-preview-work/${date}--${seed.repeat(64)}`;

const CHANGED_TWICE = {
  work: WORK,
  anchor: 'art_18',
  truncated: false,
  distinctTexts: 2,
  states: [
    {
      valid_from: '2023-07-01',
      valid_to: '2024-06-30',
      text_sha256: 'a'.repeat(64),
      // The publisher's two dates, disagreeing. The provision took effect in April; the version
      // carrying it applies from July.
      article_valid_from: '2023-04-01',
      validity_conflict: true,
      permalink: link('2023-07-01', 'e'),
    },
    {
      valid_from: '2024-07-01',
      valid_to: null,
      text_sha256: 'b'.repeat(64),
      article_valid_from: '2024-07-01',
      permalink: link('2024-07-01', '3'),
    },
  ],
};

const RENUMBERED = {
  work: WORK,
  anchor: 'art_4',
  truncated: false,
  distinctTexts: 1,
  states: [
    {
      valid_from: '2021-01-26',
      valid_to: null,
      text_sha256: 'c'.repeat(64),
      article_valid_from: '2021-01-26',
      permalink: link('2021-01-26', '7'),
    },
  ],
  anchorEvents: [{ kind: 'renumbered', from: 'art_4', to: 'art_4bis' }],
};

const NOTHING_HELD = {
  work: WORK,
  anchor: 'art_99',
  truncated: false,
  distinctTexts: 0,
  states: [],
};

/** The provision history preview, in the Workbench shell: its reader is reading a history. */
export function renderProvisionHistoryPreview({ locale = 'en' } = {}) {
  return page({
    state: 'provision-history',
    title: 'Provision history',
    locale,
    shell: 'w',
    density: skinFor('w').density,
    main:
      '      <p class="eyebrow">Workbench</p>\n' +
      '      <h1>Provision history</h1>\n' +
      '      <p>What one article said over its life, and when its wording changed. That is a ' +
      'different question from the work timeline, which lists the states the work has had; most ' +
      'of those do not touch any given article.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      '      <section class="provision-case"><h2>Two wordings, and two publisher dates that ' +
      'disagree</h2>' +
      '<p class="provision-case-note">The first row is a validity conflict. The publisher says ' +
      'the provision took effect on one date and that the version carrying it applies from ' +
      'another. Both are shown rather than one being chosen.</p>' +
      renderProvisionHistory(CHANGED_TWICE) +
      '</section>\n' +
      '      <section class="provision-case"><h2>One wording, and a renumbering</h2>' +
      '<p class="provision-case-note">The lifecycle row says whose the renumbering is and stops ' +
      'there. It does not say how the pairing was found, because this screen never observes ' +
      'that.</p>' +
      renderProvisionHistory(RENUMBERED) +
      '</section>\n' +
      '      <section class="provision-case"><h2>Nothing held</h2>' +
      '<p class="provision-case-note">Not a claim that the provision never changed. A statement ' +
      'about what this corpus holds.</p>' +
      renderProvisionHistory(NOTHING_HELD) +
      '</section>\n',
  });
}
