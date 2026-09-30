// Build the live pages: the pages served beside the API, on its origin.
//
// It goes to `dist-live/`, never to `dist/`: the preview build's pages are fixtures the browser
// gates measure against a closed list (`pages.json`) and a network gate that fails on any request,
// and this page's whole purpose is one request to the server it came from. The shell (stylesheet,
// fonts, favicon) is the same, copied from `src/` with the generated tokens appended, as the
// preview build does. The pages are `index.html` (Trust and Coverage, script `client-live.js`),
// `search.html` (search, script `client-live-search.js`), `dossier.html` (dossier, script
// `client-live-dossier.js`), `reading.html` (reading, script `client-live-reading.js`) and `history.html`
// (provision history, script `client-live-history.js`), `compare.html` (compare, script
// `client-live-compare.js`), `radar.html` (change radar, script `client-live-radar.js`) and
// `export.html` (export composer, script `client-live-export.js`).

import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { dirname } from "node:path";
import { pathToFileURL } from "node:url";

import { tokenCss } from "./design-tokens.mjs";
import { CHROME_LOCALES } from "./localization.mjs";
import { REVIEWED_CHROME_LOCALES } from "./locale-unavailable.mjs";

const source = new URL("../src/", import.meta.url);
export const LIVE_DESTINATION = new URL("../dist-live/", import.meta.url);

/** The live pages: each page's source, its renderer, the file it is written to, and its script. */
const PAGES = Object.freeze([
  ["live-coverage-page", "renderLiveCoveragePage", "index.html", "client-live-entry.jsx", "client-live.js"],
  ["live-search-page", "renderLiveSearchPage", "search.html", "client-live-search-entry.jsx", "client-live-search.js"],
  ["live-dossier-page", "renderLiveDossierPage", "dossier.html", "client-live-dossier-entry.jsx", "client-live-dossier.js"],
  ["live-reading-page", "renderLiveReadingPage", "reading.html", "client-live-reading-entry.jsx", "client-live-reading.js"],
  ["live-history-page", "renderLiveHistoryPage", "history.html", "client-live-history-entry.jsx", "client-live-history.js"],
  ["live-compare-page", "renderLiveComparePage", "compare.html", "client-live-compare-entry.jsx", "client-live-compare.js"],
  ["live-radar-page", "renderLiveRadarPage", "radar.html", "client-live-radar-entry.jsx", "client-live-radar.js"],
  ["live-export-page", "renderLiveExportPage", "export.html", "client-live-export-entry.jsx", "client-live-export.js"],
]);

/** A build plugin that puts a stand-in table for `locale` beside English in the chrome table's module. */
function standInTable(locale, table) {
  const TABLE = "export const LIVE_CHROME = Object.freeze({ en: EN });";
  return {
    name: "stand-in-chrome-table",
    setup(builder) {
      builder.onLoad({ filter: /live-chrome\.mjs$/ }, async (args) => {
        const source = await readFile(args.path, "utf8");
        if (source.split(TABLE).length !== 2) throw new Error("the chrome table is not exported as the stand-in build expects");
        return {
          contents: source.replace(TABLE, `export const LIVE_CHROME = Object.freeze({ en: EN, ${JSON.stringify(locale)}: ${JSON.stringify(table)} });`),
          loader: "js",
          resolveDir: dirname(args.path),
        };
      });
    },
  };
}

/**
 * The eight pages and their scripts for one interface language: English at the root, any other under
 * its own path (`/fr/`), each page and the script that hydrates it compiled for that language.
 */
async function buildPages(destination, { card, locale, table = null, buildTag = "" }) {
  const { bundle, bundleClient } = await import("./react-build.mjs");
  const english = locale === "en";
  const options = english ? {} : {
    define: { __LEX_CHROME_LOCALE__: JSON.stringify(locale) },
    plugins: table === null ? undefined : [standInTable(locale, table)],
  };
  // A caller's tag keeps its intermediate bundles apart from another build running at the same time.
  const suffix = `${buildTag ? `.${buildTag}` : ""}${english ? "" : `.${locale}`}`;
  const out = english ? destination : new URL(`${locale}/`, destination);
  await mkdir(out, { recursive: true });
  for (const [source, render, page, clientEntry, client] of PAGES) {
    const ssr = await import(pathToFileURL(await bundle(`app/${source}.jsx`, `${source}${suffix}.mjs`, options)).href);
    await writeFile(new URL(page, out), page === "index.html" ? ssr[render]({ card }) : ssr[render](), "utf8");
    await cp(await bundleClient(`app/${clientEntry}`, client.replace(/\.js$/, `${suffix}.js`), options), new URL(client, out));
  }
}

/**
 * Builds the live pages into `destination`. `card` is the evaluation card the Trust and Coverage page carries: the
 * platform's rendered card when none is given, the release card when a release build hands one (ruling 2).
 */
export async function buildLive(destination = LIVE_DESTINATION, { card, tables = {}, buildTag = "" } = {}) {
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
  await buildPages(destination, { card, locale: "en", buildTag });
  // Every other reviewed interface language, under its own path, its bundles compiled for it
  // (Decision 41: none but English is reviewed today, so this builds nothing yet). `tables` lets a
  // test build a stand-in language without it being reviewed; the product build passes none.
  const others = [...new Set([...REVIEWED_CHROME_LOCALES.filter((code) => code !== "en"), ...Object.keys(tables)])];
  for (const locale of others) await buildPages(destination, { card, locale, table: tables[locale] ?? null, buildTag });
  // One static page per chrome locale without reviewed copy: localization_unavailable, in English and
  // labelled English, with no script (Decision 41; the launch contract's DE and LB line).
  const { bundle } = await import("./react-build.mjs");
  const localeSsr = await import(pathToFileURL(await bundle("app/live-locale-page.jsx", "live-locale-page.mjs")).href);
  for (const locale of CHROME_LOCALES.filter((code) => !REVIEWED_CHROME_LOCALES.includes(code))) {
    await writeFile(new URL(`locale-${locale}.html`, destination), localeSsr.renderLiveLocaleUnavailablePage(locale), "utf8");
  }
  return destination;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const built = await buildLive();
  console.log(`built the live pages into ${built.pathname}`);
}
