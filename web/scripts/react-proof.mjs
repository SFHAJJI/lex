// Run the React shell proof and print the document it produces.

import { pathToFileURL } from 'node:url';

import { bundle, resetWork } from './react-build.mjs';

await resetWork();
const out = await bundle('app/proof.jsx', 'proof.mjs');
const { proof } = await import(pathToFileURL(out).href);
process.stdout.write(proof());
