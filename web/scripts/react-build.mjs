// Build the React pages.
//
// esbuild compiles the JSX and bundles the server entry into one module this script then
// imports and calls. React is bundled rather than left external so the built entry has no
// resolution story of its own; the truth-rule modules under scripts/ come along with it
// unchanged, which is the point of the port: none of them knows what a component is.
//
// Two outputs come out of one component tree. The HTML is what a reader receives, and the
// client bundle is what makes it interactive. A page is listed in `pages.json` so the
// browser harness measures exactly what was built, rather than a list kept by hand that
// drifts the moment a page is added.

import { build } from 'esbuild';
import { mkdir, rm, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const root = new URL('../', import.meta.url);
const work = new URL('.react-build/', root);

/**
 * Compile a JSX entry into a single ESM module and return its path.
 *
 * @param {string} entry   path relative to the workspace root
 * @param {string} outfile path relative to the build directory
 */
export async function bundle(entry, outfile, { platform = 'node' } = {}) {
  const out = fileURLToPath(new URL(outfile, work));
  await build({
    entryPoints: [fileURLToPath(new URL(entry, root))],
    outfile: out,
    bundle: true,
    format: 'esm',
    platform,
    jsx: 'automatic',
    // On the server the dependencies stay external. react-dom/server is CommonJS and reaches
    // for node builtins, and bundling it into ESM turns those into a dynamic require that
    // throws at import time. The browser build has no such escape and bundles everything.
    packages: platform === 'node' ? 'external' : undefined,
    minify: platform === 'browser',
    logLevel: 'silent',
  });
  return out;
}

/** Remove and recreate the intermediate build directory. */
export async function resetWork() {
  await rm(work, { force: true, recursive: true });
  await mkdir(work, { recursive: true });
}

/**
 * Build the client bundle a hydrated page loads.
 *
 * Browser platform, everything bundled: the page is served under a policy that admits scripts
 * only from this origin, so a bare specifier the browser would have to resolve elsewhere is a
 * script that never loads. Minified because the bundle is shipped, and the reviewable artefact is
 * the source in app/, not the bytes.
 */
export async function bundleClient(entry, outfile) {
  return bundle(entry, outfile, { platform: 'browser' });
}

/** Write one built page and return its file name. */
export async function writePage(destination, name, html) {
  if (typeof html !== 'string' || !html.startsWith('<!doctype html>')) {
    throw new Error(`${name} did not come back as a whole document`);
  }
  await writeFile(new URL(name, destination), html, 'utf8');
  return name;
}
