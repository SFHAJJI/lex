// Build the live pages: the pages served beside the API, on its origin.
//
// It goes to `dist-live/`, never to `dist/`: the preview build's pages are fixtures the browser
// gates measure against a closed list (`pages.json`) and a network gate that fails on any request,
// and this page's whole purpose is one request to the server it came from. The shell (stylesheet,
// fonts, favicon) is the same, copied from `src/` with the generated tokens appended, as the
// preview build does. The pages are `index.html` (Trust and Coverage, script `client-live.js`) and
// `search.html` (search, script `client-live-search.js`).

import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

import { tokenCss } from "./design-tokens.mjs";

const source = new URL("../src/", import.meta.url);
export const LIVE_DESTINATION = new URL("../dist-live/", import.meta.url);

export async function buildLive(destination = LIVE_DESTINATION) {
  await rm(destination, { force: true, recursive: true });
  await mkdir(destination, { recursive: true });
  for (const asset of ["styles.css", "favicon.svg", "fonts"]) {
    await cp(new URL(asset, source), new URL(asset, destination), { recursive: true });
  }
  const stylesheet = new URL("styles.css", destination);
  await writeFile(stylesheet, `${await readFile(stylesheet, "utf8")}
/* Semantic tokens, generated from scripts/design-tokens.mjs. */
${tokenCss()}`, "utf8");

  // Its own outputs in the intermediate directory, never `app.mjs` and never a reset of the
  // directory: the tests import `app.mjs` from there in parallel processes, and rewriting it under
  // them (as this build first did) fails whichever test file loads it mid-write.
  const { bundle, bundleClient } = await import("./react-build.mjs");
  const ssr = await import(pathToFileURL(await bundle("app/live-coverage-page.jsx", "live-coverage-page.mjs")).href);
  await writeFile(new URL("index.html", destination), ssr.renderLiveCoveragePage(), "utf8");
  await cp(await bundleClient("app/client-live-entry.jsx", "client-live.js"), new URL("client-live.js", destination));
  const searchSsr = await import(pathToFileURL(await bundle("app/live-search-page.jsx", "live-search-page.mjs")).href);
  await writeFile(new URL("search.html", destination), searchSsr.renderLiveSearchPage(), "utf8");
  await cp(await bundleClient("app/client-live-search-entry.jsx", "client-live-search.js"), new URL("client-live-search.js", destination));
  return destination;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const built = await buildLive();
  console.log(`built the live pages into ${built.pathname}`);
}
