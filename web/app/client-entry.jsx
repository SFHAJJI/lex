// The browser entry. Bundled to /client.js and loaded by every hydrated page.
//
// One bundle serves several pages, so each root is attached only where it exists. Attaching
// unconditionally threw on the first page that shipped the script without the dossier proof on
// it, which is a script that fails before it can hydrate anything else on the page.

import { attach } from './client.jsx';
import { hydrationTree } from './hydration-proof.jsx';
import { searchScreenTree } from './search-screen-preview.jsx';

const ROOTS = [
  ['hydration-root', hydrationTree],
  ['search-root', searchScreenTree],
];

for (const [id, tree] of ROOTS) {
  const root = document.getElementById(id);
  if (root !== null) attach(root, tree());
}
