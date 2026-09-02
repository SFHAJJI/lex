// Compile the React side once, so the tests and the build measure the same bytes.
//
// Runs as `pretest` and before the build. The output is gitignored: it is a compilation
// artifact, and a committed one would drift from its source silently, which is the whole
// failure mode this project keeps meeting.

import { bundle, resetWork } from './react-build.mjs';

await resetWork();
const out = await bundle('app/index.jsx', 'app.mjs');
process.stdout.write(`compiled app/index.jsx -> ${out}\n`);
