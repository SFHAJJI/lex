// The citation checker, shown on the four verdicts at once.
//
// Every other preview in this build shows a screen working. This one shows it refusing three times
// out of four, because that is the honest distribution for a checker and because the three
// refusals are the part that has to be right. A reader arrives here holding a citation somebody
// else wrote, often to find out whether to trust it.
//
// Every value is synthetic and none of it is law.

import { page } from './render.mjs';
import { checkCitation, checkQuote, renderVerdict } from './citation-checker.mjs';
import { skinFor } from './shells.mjs';

const ELI = 'http://data.legilux.public.lu/eli/etat/leg/loi/1993/04/05/n1';

const RESOLVED = checkCitation({
  raw: `${ELI}/consolide/20250101`,
  candidates: [
    {
      lex_id: 'lu-legilux:loi-1993-04-05-n1:2025-01-01',
      identifier: ELI,
      valid_from: '2025-01-01',
      valid_to: null,
    },
  ],
});

const AMBIGUOUS = checkCitation({
  raw: ELI,
  candidates: [
    {
      lex_id: 'lu-legilux:loi-1993-04-05-n1:2025-01-01',
      identifier: ELI,
      valid_from: '2025-01-01',
      valid_to: '2025-12-31',
    },
    {
      lex_id: 'lu-legilux:loi-1993-04-05-n1:2026-01-01',
      identifier: ELI,
      valid_from: '2026-01-01',
      valid_to: null,
    },
  ],
});

const OUT_OF_CORPUS = checkCitation({ raw: 'CSSF 20/747' });
const UNRECOGNISED = checkCitation({ raw: 'see the blue book, page 12' });

// A quote that differs by one character, which is the case the screen exists for. The offset is
// reported and the difference is never characterised, because saying a change is immaterial would
// be applying law to facts.
const QUOTE = checkQuote({
  quoted: 'Les societes commerciales sont regies par la presente loi.',
  held: 'Les societes commerciales sont regies par la presente Loi.',
});

/** The checker preview, in the workbench shell. */
export function renderCitationCheckerPreview({ locale = 'en' } = {}) {
  return page({
    state: 'citation-checker',
    title: 'Citation checker',
    locale,
    shell: 'w',
    density: skinFor('w').density,
    main:
      '      <p class="eyebrow">Workbench</p>\n' +
      '      <h1>Citation checker</h1>\n' +
      '      <p>Paste a citation and find out whether it resolves. Three of the four verdicts ' +
      'below are refusals, which is the honest distribution for a checker and the part that has ' +
      'to be right.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      '      <section class="check-case"><h2>Resolved</h2>' +
      '<p class="check-case-note">A dated ELI names one state, and one held record answers it.</p>' +
      renderVerdict(RESOLVED) +
      '</section>\n' +
      '      <section class="check-case"><h2>More than one answer</h2>' +
      '<p class="check-case-note">An undated citation of a work with several held states. Every ' +
      'candidate is listed and none is chosen, the same rule the timeline applies to overlapping ' +
      'states.</p>' +
      renderVerdict(AMBIGUOUS) +
      '</section>\n' +
      '      <section class="check-case"><h2>Recognised, and not ours</h2>' +
      '<p class="check-case-note">A CSSF circular is a real citation to a body this corpus does ' +
      'not hold. It is classified and linked out rather than reported as unreadable.</p>' +
      renderVerdict(OUT_OF_CORPUS) +
      '</section>\n' +
      '      <section class="check-case"><h2>Not a citation form this build reads</h2>' +
      '<p class="check-case-note">Distinct from the case above, and the distinction matters: ' +
      'there is nowhere to send this reader, so the page does not pretend there is.</p>' +
      renderVerdict(UNRECOGNISED) +
      '</section>\n' +
      '      <section class="check-case"><h2>Quote check</h2>' +
      '<p class="check-case-note">Character comparison against the held text, and nothing else. ' +
      `The passages differ from character ${QUOTE.at}. The screen reports where and never says ` +
      'whether the difference matters, because that would be applying law to facts.</p>' +
      `<p class="check-quote-verdict">Not identical. First difference at character ${QUOTE.at}.</p>` +
      '</section>\n',
  });
}
