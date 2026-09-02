// The export composer, shown with a cart that exercises every rights outcome at once.
//
// Most previews show a screen at its best. This one deliberately shows a mixed cart, because the
// composer's whole reason for existing is the case where the pinned items do not all behave the
// same: one travels whole, one is limited by its licence, one has no established right at all. A
// preview of three identical CC-BY items would demonstrate nothing this screen is for.
//
// Every value here is synthetic and none of it is law. The identifiers are shaped like real ones
// so the layout is measured at realistic widths, which is where the interval, digest and rights
// sentence have previously collided at high zoom.

import { page } from './render.mjs';
import { renderExportComposer } from './export-composer.mjs';
import { skinFor } from './shells.mjs';

const MATTER = { reference: 'M-2026-014, banking licence review', author: 'Preview' };

const CART = [
  {
    kind: 'publisher_text',
    lex_id: 'lu-legilux:loi-1993-04-05-n1:2025-01-01',
    valid_from: '2025-01-01',
    valid_to: null,
    official_uri:
      'https://legilux.public.lu/eli/etat/leg/loi/1993/04/05/n1/consolide/20250101',
    record_sha256: '4f'.repeat(32),
    licence: 'cc-by-4.0',
    attribution: 'Journal officiel du Grand-Duche de Luxembourg',
    text_public: true,
  },
  {
    kind: 'publisher_text',
    lex_id: 'lu-legilux:rgd-2010-03-31-n2:2026-06-30',
    valid_from: '2026-06-30',
    valid_to: null,
    official_uri:
      'https://legilux.public.lu/eli/etat/leg/rgd/2010/03/31/n2/consolide/20260630',
    record_sha256: 'b7'.repeat(32),
    licence: 'licence-scl',
    attribution: 'Journal officiel du Grand-Duche de Luxembourg',
    text_public: true,
  },
  {
    kind: 'publisher_text',
    lex_id: 'eu-eurlex:32016R0679:2016-05-04',
    valid_from: '2016-05-04',
    valid_to: '2018-05-24',
    official_uri: 'https://eur-lex.europa.eu/eli/reg/2016/679/oj',
    record_sha256: 'c3'.repeat(32),
    licence: 'cc-by-4.0',
    attribution: 'Publications Office of the European Union',
    text_public: false,
  },
];

/** The composer preview, in the workbench shell: its reader is assembling a file. */
export function renderExportComposerPreview({ locale = 'en' } = {}) {
  return page({
    state: 'export-composer',
    title: 'Export composer',
    locale,
    shell: 'w',
    density: skinFor('w').density,
    main:
      '      <p class="eyebrow">Workbench</p>\n' +
      '      <h1>Export composer</h1>\n' +
      '      <p>Rights are applied while the cart is still editable, not when the file is ' +
      'written. Someone who exports twelve items and only then discovers that four of them ' +
      'travelled as a bare digest has been misled by their own tool.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      '      <section class="compose-case"><h2>A cart with three different outcomes</h2>' +
      '<p class="compose-case-note">One item travels whole. One is limited by its licence and ' +
      'exports as a digest and a link. One has no established public-text right, which is a ' +
      'different fact from a licence limit and is worded as one: the licence would have embedded ' +
      'the text, and the missing right is the reason it cannot.</p>' +
      renderExportComposer({ items: CART, matter: MATTER }) +
      '</section>\n' +
      '      <section class="compose-case"><h2>Before anything is pinned</h2>' +
      '<p class="compose-case-note">The empty cart is the first state most readers see, so it ' +
      'says what to do rather than showing an empty list.</p>' +
      renderExportComposer({ items: [], matter: MATTER }) +
      '</section>\n',
  });
}
