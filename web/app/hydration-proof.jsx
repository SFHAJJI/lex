// One page that is genuinely hydrated, and the entry that hydrates it.
//
// Deliberately the dossier rather than something new. It is a real truth surface with a status
// chip, a date table on two clocks, a coverage strip and unfilled slots, so if hydration changes
// anything a reader can see, it changes something that matters rather than a placeholder.
//
// The page ships the server-rendered dossier and one script. After that script runs the markup
// must be byte-identical to what the server sent: hydration attaches behaviour, it does not get
// to redraw the law.

import { Document } from './Document.jsx';
import { Dossier } from './Dossier.jsx';
import { renderHydratableDocument } from './render-document.mjs';

/** The one dossier both the server and the client render. Identical input, identical tree. */
export const HYDRATION_FIXTURE = Object.freeze({
  identity: Object.freeze({
    title: 'Acte synthetique de demonstration',
    title_language: 'fr',
    publisher: 'preview-synthetic',
    work_identifier: 'https://preview.invalid/synthetic-preview-work',
    document_type: 'CODE',
  }),
  status: Object.freeze({ binding_status: 'in_force' }),
  dates: Object.freeze([
    Object.freeze({ role: 'publication', date: '2021-01-26', source: 'publisher record' }),
    Object.freeze({ role: 'applicable_from', date: '2021-01-26', source: 'publisher record' }),
    Object.freeze({ role: 'observed_from', date: '2026-08-14T23:05:14Z', source: 'this corpus' }),
  ]),
  coverage: Object.freeze({ states_held: 4, states_with_text: 4, holes: Object.freeze([]) }),
});

/** The element, built once so the two renderers cannot diverge by construction. */
export function hydrationTree() {
  return <Dossier {...HYDRATION_FIXTURE} />;
}

/** The whole page, server side. */
export function renderHydrationProof() {
  return renderHydratableDocument(
    <Document state="hydration" title="Hydration" shell="w" density="reading">
      <p className="eyebrow">Workbench</p>
      <h1>Hydration</h1>
      <p>
        This page is server-rendered and then hydrated. Everything below arrived in the HTML
        response; the script attaches behaviour to it and must not change a word of it. Every
        value here is synthetic and none of it is law.
      </p>
      <div id="hydration-root">{hydrationTree()}</div>
      <script src="/client.js" defer />
    </Document>,
  );
}
