// Build the live pages: the pages served beside the API, on its origin.
//
// It goes to `dist-live/`, never to `dist/`: the preview build's pages are fixtures the browser
// gates measure against a closed list (`pages.json`) and a network gate that fails on any request,
// and this page's whole purpose is one request to the server it came from. The shell (stylesheet,
// fonts, favicon) is the same, copied from `src/` with the generated tokens appended, as the
// preview build does, at the root with the evaluation card. The pages stand under each interface language,
// English under `en/` and French under `fr/` (R61): `index.html` (the home page: the search screen under
// the site's introduction, script `client-live-search.js`), `coverage.html` (Trust and Coverage, script
// `client-live.js`), `search.html` (search, script `client-live-search.js`), `dossier.html` (dossier,
// script `client-live-dossier.js`), `reading.html` (reading, script `client-live-reading.js`),
// `history.html` (provision history, script `client-live-history.js`), `compare.html` (compare, script
// `client-live-compare.js`), `radar.html` (change radar, script `client-live-radar.js`) and
// `export.html` (export composer, script `client-live-export.js`). The root holds no page: both servers
// send it to a language. German and Luxembourgish are `locale-de.html` and `locale-lb.html`, at the root,
// which say they are not reviewed. Each interface language's 404 page is `not-found/{language}.html` (the search
// screen under the page's own words, script `client-live-search.js`) and its maintenance page
// `maintenance/{language}.html` (no script); neither server serves either at that address (`not-found.mjs`).

import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

import { tokenCss } from "./design-tokens.mjs";
import { CHROME_LOCALES } from "./localization.mjs";
import { REVIEWED_CHROME_LOCALES } from "./locale-unavailable.mjs";
import { MAINTENANCE_DIRECTORY, NOT_FOUND_DIRECTORY } from "./not-found.mjs";

const source = new URL("../src/", import.meta.url);
export const LIVE_DESTINATION = new URL("../dist-live/", import.meta.url);

/** The live pages: each page's source, its renderer, the file it is written to, and its script. */
const PAGES = Object.freeze([
  ["live-search-page", "renderLiveHomePage", "index.html", "client-live-search-entry.jsx", "client-live-search.js"],
  ["live-coverage-page", "renderLiveCoveragePage", "coverage.html", "client-live-entry.jsx", "client-live.js"],
  ["live-search-page", "renderLiveSearchPage", "search.html", "client-live-search-entry.jsx", "client-live-search.js"],
  ["live-dossier-page", "renderLiveDossierPage", "dossier.html", "client-live-dossier-entry.jsx", "client-live-dossier.js"],
  ["live-reading-page", "renderLiveReadingPage", "reading.html", "client-live-reading-entry.jsx", "client-live-reading.js"],
  ["live-history-page", "renderLiveHistoryPage", "history.html", "client-live-history-entry.jsx", "client-live-history.js"],
  ["live-compare-page", "renderLiveComparePage", "compare.html", "client-live-compare-entry.jsx", "client-live-compare.js"],
  ["live-radar-page", "renderLiveRadarPage", "radar.html", "client-live-radar-entry.jsx", "client-live-radar.js"],
  ["live-export-page", "renderLiveExportPage", "export.html", "client-live-export-entry.jsx", "client-live-export.js"],
]);

/**
 * The nine pages and their scripts for one interface language, under that language's own path (`/en/`,
 * `/fr/`: the specification of record's R61; the root sends a reader to one of them), each page and the
 * script that hydrates it compiled for that language.
 */
async function buildPages(destination, { card, locale, buildTag = "" }) {
  const { bundle, bundleClient } = await import("./react-build.mjs");
  const english = locale === "en";
  const options = english ? {} : { define: { __LEX_CHROME_LOCALE__: JSON.stringify(locale) } };
  // A caller's tag keeps its intermediate bundles apart from another build running at the same time.
  const suffix = `${buildTag ? `.${buildTag}` : ""}${english ? "" : `.${locale}`}`;
  const out = new URL(`${locale}/`, destination);
  await mkdir(out, { recursive: true });
  for (const [source, render, page, clientEntry, client] of PAGES) {
    const ssr = await import(pathToFileURL(await bundle(`app/${source}.jsx`, `${source}${suffix}.mjs`, options)).href);
    // Trust and Coverage carries the evaluation card it is handed (ruling 2); no other page carries a card.
    await writeFile(new URL(page, out), page === "coverage.html" ? ssr[render]({ card }) : ssr[render](), "utf8");
    await cp(await bundleClient(`app/${clientEntry}`, client.replace(/\.js$/, `${suffix}.js`), options), new URL(client, out));
  }
  // The server renderer for this language's pages at a law's address (R67), under ssr/, which neither server serves:
  // the API and the live server run it, a reader never fetches it.
  const { bundleServerRenderer } = await import("./react-build.mjs");
  await mkdir(new URL("ssr/", destination), { recursive: true });
  await cp(await bundleServerRenderer("app/ssr-entry.jsx", `ssr-entry${suffix}.js`, options), new URL(`ssr/${locale}.js`, destination));
  // This language's 404 page, which both servers answer a page request nothing claims with (R60; `not-found.mjs`), and its
  // maintenance page, for the deploy kit: each under a directory of its own at the root, which neither server serves.
  const notFound = await import(pathToFileURL(await bundle("app/live-not-found-page.jsx", `live-not-found-page${suffix}.mjs`, options)).href);
  const maintenance = await import(pathToFileURL(await bundle("app/live-maintenance-page.jsx", `live-maintenance-page${suffix}.mjs`, options)).href);
  for (const [directory, html] of [[NOT_FOUND_DIRECTORY, notFound.renderLiveNotFoundPage()], [MAINTENANCE_DIRECTORY, maintenance.renderLiveMaintenancePage()]]) {
    await mkdir(new URL(`${directory}/`, destination), { recursive: true });
    await writeFile(new URL(`${directory}/${locale}.html`, destination), html, "utf8");
  }
}

/**
 * Builds the live pages into `destination`. `card` is the evaluation card the Trust and Coverage page carries: the
 * platform's rendered card when none is given, the release card when a release build hands one (ruling 2).
 */
export async function buildLive(destination = LIVE_DESTINATION, { card, buildTag = "" } = {}) {
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
  // The card as JSON at its stable route, for machines (36 s6): the same card the page renders.
  const { CARD_ROUTE } = await import("./evaluation-card.mjs");
  const platformCard = JSON.parse(await readFile(new URL("../../schemas/v3-platform/evaluation-card.json", import.meta.url), "utf8"));
  await writeFile(new URL(CARD_ROUTE.slice(1), destination), `${JSON.stringify(card ?? platformCard, null, 2)}
`, "utf8");
  // Every other reviewed interface language, under its own path, its bundles compiled for it
  // (Decision 41): French, reviewed by Claude (AI reviewer) under the owner's delegation of
  // 2026-10-02, at `/fr/`.
  for (const locale of REVIEWED_CHROME_LOCALES.filter((code) => code !== "en")) await buildPages(destination, { card, locale, buildTag });
  // One static page per chrome locale without reviewed copy: localization_unavailable, in English and
  // labelled English, with no script (Decision 41; the launch contract's DE and LB line).
  // Tagged as every other intermediate bundle is: an untagged name was one file every concurrent build rewrote, and a test
  // file importing it mid-write found no renderLiveLocaleUnavailablePage (the web suite's race, 2026-10-15).
  const { bundle } = await import("./react-build.mjs");
  const localeSsr = await import(pathToFileURL(await bundle("app/live-locale-page.jsx", `live-locale-page${buildTag ? `.${buildTag}` : ""}.mjs`)).href);
  for (const locale of CHROME_LOCALES.filter((code) => !REVIEWED_CHROME_LOCALES.includes(code))) {
    await writeFile(new URL(`locale-${locale}.html`, destination), localeSsr.renderLiveLocaleUnavailablePage(locale), "utf8");
  }
  return destination;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const built = await buildLive();
  console.log(`built the live pages into ${built.pathname}`);
}
