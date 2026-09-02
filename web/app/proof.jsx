// A one-page proof that the shell renders through React at all.
//
// Deliberately trivial. It exists to answer one question before any screen is ported:
// does a React component tree produce a complete, valid document with the synthetic
// banner, the preview state and the root-absolute asset links still on it. If this is
// wrong, every ported screen is wrong the same way.

import { Document } from './Document.jsx';
import { renderDocument } from './render-document.mjs';

export function proof() {
  return renderDocument(
    <Document state="proof" title="Proof" shell="w" density="reading">
      <h1>Proof</h1>
      <p>
        This page exists to prove the React shell emits a whole document. It carries no legal
        content and asserts nothing about any record.
      </p>
    </Document>,
  );
}
