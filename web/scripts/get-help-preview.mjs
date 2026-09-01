// Getting advice, in the state this build is actually in.
//
// The handoff registry is editorial, product spec build item 14, and holds only the synthetic
// preview host. So the page cannot list a counter, and the preview does not pretend otherwise: it
// renders exactly what a reader would see today.
//
// That makes this preview unusual and worth keeping that way. Most previews show a screen at its
// best; this one shows a screen at its emptiest, because the empty state is the shipped state and
// it is the one that has to be honest.

import { page } from './render.mjs';
import { renderGetHelp } from './get-help.mjs';
import { skinFor } from './shells.mjs';

const OFFICIAL_ROUTES = [
  { label: 'Legilux, the Luxembourg publisher', uri: 'https://legilux.public.lu/' },
  { label: 'EUR-Lex, the Union publisher', uri: 'https://eur-lex.europa.eu/' },
];

// What the registry holds today. It is dropped rather than offered, and the page says why.
const REGISTRY_TODAY = [
  { label: 'Synthetic preview counter', href: 'https://handoff.invalid/one' },
];

/** The get-help preview, in the Ask shell: its reader is a citizen who has been refused. */
export function renderGetHelpPreview({ locale = 'en' } = {}) {
  return page({
    state: 'get-help',
    title: 'Getting advice',
    locale,
    shell: 'ask',
    density: skinFor('ask').density,
    main:
      '      <p class="eyebrow">Ask</p>\n' +
      '      <h1>Getting advice</h1>\n' +
      '      <p>A reader reaches this page having already been told that this service will not ' +
      'apply the law to their situation. So the page owes them a destination, and a destination ' +
      'that does not resolve is a second refusal wearing the word help.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      '      <section class="get-help-case"><h2>This build, as it stands</h2>' +
      '<p class="get-help-case-note">The handoff registry is editorial and holds only the ' +
      'synthetic preview host, so no counter is named. The page says that rather than showing an ' +
      'empty list, because an empty list is indistinguishable from a build that never had one. ' +
      'The publisher routes below are true regardless.</p>' +
      renderGetHelp({ counters: REGISTRY_TODAY, officialRoutes: OFFICIAL_ROUTES }) +
      '</section>\n',
  });
}
