// The dossier, in the three shapes where a hub page misleads.
//
// A work with a flag that means now printed beside dates that mean then. A work whose corpus
// coverage has a twenty-year hole in the middle of it. And a work several of whose fields this
// corpus simply does not hold, which is the case a dossier hides best, because an absent row
// looks like a complete page.
//
// Every value is synthetic and none of it is law.

import { page } from './render.mjs';
import { renderDossier } from './dossier.mjs';
import { skinFor } from './shells.mjs';

const IDENTITY = {
  title: 'Acte synthetique de demonstration',
  title_language: 'fr',
  publisher: 'preview-synthetic',
  work_identifier: 'https://preview.invalid/synthetic-preview-work',
  document_type: 'CODE',
};

function section(heading, note, html) {
  return (
    `      <section class="dossier-case"><h2>${heading}</h2>` +
    `<p class="dossier-case-note">${note}</p>${html}</section>\n`
  );
}

/** The dossier preview, in the Workbench shell: its reader is working through one instrument. */
export function renderDossierPreview({ locale = 'en' } = {}) {
  const ordinary = renderDossier({
    identity: IDENTITY,
    status: { binding_status: 'in_force' },
    dates: [
      { role: 'publication', date: '2021-01-26', source: 'publisher record' },
      { role: 'applicable_from', date: '2021-01-26', source: 'publisher record' },
      { role: 'applicable_to', date: '2021-04-23', source: 'publisher record' },
      { role: 'observed_from', date: '2026-08-14T23:05:14Z', source: 'this corpus' },
    ],
    coverage: { states_held: 4, states_with_text: 4, holes: [] },
  });

  const gapped = renderDossier({
    identity: { ...IDENTITY, title: 'Loi synthetique de 1993', document_type: 'LOI' },
    status: { binding_status: 'in_force' },
    dates: [
      { role: 'publication', date: '1993-04-05', source: 'publisher record' },
      { role: 'applicable_from', date: '1993-04-05', source: 'publisher record' },
      { role: 'observed_from', date: '2026-08-14T23:05:14Z', source: 'this corpus' },
    ],
    coverage: {
      states_held: 12,
      states_with_text: 2,
      holes: [{ from: '2004-04-02', to: '2024-12-28' }],
    },
  });

  const partial = renderDossier({
    identity: {
      ...IDENTITY,
      title: 'Reglement synthetique de l Union',
      document_type: 'REG',
      title_language: 'fr',
    },
    status: { binding_status: 'in_force' },
    dates: [
      { role: 'publication', date: '2016-05-04', source: 'publisher record' },
      { role: 'applicable_from', date: '2016-05-04', source: 'publisher record' },
      {
        role: 'entry_into_force',
        date: null,
        source: 'publisher axiom',
        awaiting: 'the publisher entry-into-force axiom, held in its own vocabulary service',
      },
      {
        role: 'application',
        date: null,
        source: 'publisher axiom',
        awaiting: 'the publisher application axiom, held in its own vocabulary service',
      },
      { role: 'observed_from', date: '2026-08-01T00:00:00Z', source: 'this corpus' },
    ],
    coverage: { states_held: 2, states_with_text: 2, holes: [] },
    slots: [
      {
        what: 'responsible ministry',
        where: 'published by the publisher on its own open channel',
      },
      {
        what: 'historical identifiers this act was known by',
        where: 'published by the publisher on its own open channel',
      },
    ],
  });

  return page({
    state: 'dossier',
    title: 'Dossier',
    locale: 'en',
    // Every label in the dossier is a hardcoded English literal, so this page is English
    // whatever locale is asked for. Passing the requested locale here satisfied the guard by
    // asserting something untrue, which is the defect that guard exists to catch.
    copyLocale: 'en',
    shell: 'w',
    density: skinFor('w').density,
    main:
      '      <p class="eyebrow">Workbench</p>\n' +
      '      <h1>Dossier</h1>\n' +
      '      <p>This is the only screen where the publisher current-state flag belongs, and it ' +
      'belongs here only with the sentence that makes it readable. Everywhere else in this ' +
      'interface a row carrying that flag is refused, because a fact about now printed against ' +
      'a historical interval dates a claim the publisher never made about that date.</p>\n' +
      '      <p>Every value on this page is synthetic and none of it is law.</p>\n' +
      section(
        'A work whose record is complete',
        'Four states, text for all four, no gap. The flag is still captioned, because the ' +
          'caption is not a warning about this work, it is what the chip means.',
        ordinary,
      ) +
      section(
        'A work with twenty years missing from the middle',
        'The counts and the gap are stated together. Twelve states held and text for two is the ' +
          'honest pair; twelve on its own is a different and untrue number.',
        gapped,
      ) +
      section(
        'A work this corpus only partly holds',
        'The rows that cannot be filled say what they are waiting for, and the fields this ' +
          'corpus does not hold say where the publisher keeps them. An absent row looks like a ' +
          'complete page, which is how a dossier misleads best.',
        partial,
      ),
  });
}
