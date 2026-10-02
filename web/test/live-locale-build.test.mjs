// A reviewed interface language builds its own live pages (Decision 41; the launch contract's chrome
// line). French is reviewed (by Claude, an AI reviewer, under the owner's delegation of 2026-10-02), so
// the product build writes the eight pages and their scripts in English at the root and in French under
// `/fr/`. This test builds it and holds:
// - every French page and the script that hydrates it under `/fr/`, labelled `fr`, loading its own script;
// - the French table's chrome on every French page, and none of the English table's where the two differ;
// - the English pages at the root exactly as the English bundle renders them;
// - every page and script served by the live server, the French home included;
// - no `locale-fr.html`: French leads to its pages, German and Luxembourgish to the page that says they
//   are not reviewed.

import assert from "node:assert/strict";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

import {
  CENSUS_EVALUATION_CARD,
  localeHome,
  renderLiveComparePage,
  renderLiveCoveragePage,
  renderLiveDossierPage,
  renderLiveExportPage,
  renderLiveHistoryPage,
  renderLiveRadarPage,
  renderLiveReadingPage,
  renderLiveSearchPage,
} from "../.react-build/app.mjs";
import { buildLive } from "../scripts/build-live.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";
import { entriesOf, fillText, liveChrome } from "../scripts/live-chrome.mjs";

const PAGES = {
  "index.html": ["coverage", "client-live.js", renderLiveCoveragePage, "loading"],
  "search.html": ["search", "client-live-search.js", renderLiveSearchPage, "idle"],
  "dossier.html": ["dossier", "client-live-dossier.js", renderLiveDossierPage, "idle"],
  "reading.html": ["reading", "client-live-reading.js", renderLiveReadingPage, "idle"],
  "history.html": ["history", "client-live-history.js", renderLiveHistoryPage, "idle"],
  "compare.html": ["compare", "client-live-compare.js", renderLiveComparePage, "idle"],
  "radar.html": ["radar", "client-live-radar.js", renderLiveRadarPage, "idle"],
  "export.html": ["export", "client-live-export.js", renderLiveExportPage, "idle"],
};

const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");

const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'", "&#39;": "'", "&nbsp;": " " };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|nbsp|#x27|#39);/g, (entity) => ENTITIES[entity]);

/**
 * What a page says, as one text: its text nodes and human-read attributes, with the locale navigation (each language
 * named in itself) and every element marked English (the platform's own English, as the evaluation card's values on
 * Trust and Coverage) set aside.
 */
function saidOutsideEnglish(html) {
  let body = html.slice(html.indexOf("<body")).replace(/<script[\s\S]*?<\/script>/g, "").replace(/<!--[\s\S]*?-->/g, "");
  body = body.replace(/<nav [^>]*data-locale-nav=""[\s\S]*?<\/nav>/, "");
  for (let at = body.search(/<([a-z]+)[^>]* lang="en"/); at >= 0; at = body.search(/<([a-z]+)[^>]* lang="en"/)) {
    const tag = body.slice(at).match(/^<([a-z]+)/)[1];
    let depth = 0;
    let end = -1;
    for (const match of body.slice(at).matchAll(new RegExp(`<(/?)${tag}[\\s>]`, "g"))) {
      depth += match[1] === "/" ? -1 : 1;
      if (depth === 0) {
        end = at + match.index + `</${tag}>`.length;
        break;
      }
    }
    assert.ok(end > at, `an element marked English is never closed: ${body.slice(at, at + 80)}`);
    body = `${body.slice(0, at)} ${body.slice(end)}`;
  }
  const texts = [...body.matchAll(/>([^<]+)</g)].map((match) => decode(match[1]));
  const attributes = [...body.matchAll(/\s(?:aria-label|title|alt|placeholder)="([^"]*)"/g)].map((match) => decode(match[1]));
  return [...texts, ...attributes].join(" | ");
}

/**
 * The evaluation card's own values on Trust and Coverage (its set, arm, gate and control names, its figures), as
 * the chrome scan sets them aside: the card's, not the interface's. Its verdicts are left in, since the page says
 * them in the table's words. Longest first, so a longer value is taken out whole.
 */
function cardValues(node, found = new Set()) {
  if (typeof node === "string") found.add(node);
  else if (Array.isArray(node)) node.forEach((item) => cardValues(item, found));
  else if (node !== null && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) if (key !== "verdict") cardValues(value, found);
  }
  return [...found].filter((value) => value.length > 0).sort((a, b) => b.length - a.length);
}

/** An entry's whole text as a pattern, bounded where it begins or ends in a letter, so "Compare" never matches "Comparer". */
function wholly(text) {
  const pattern = text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return new RegExp(`${/^\p{L}/u.test(text) ? "(?<!\\p{L})" : ""}${pattern}${/\p{L}$/u.test(text) ? "(?!\\p{L})" : ""}`, "u");
}

test("the product build writes every page in French under /fr/, in the French table's words and none of the English's, and English as it was", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-live-fr-"));
  try {
    // Tagged, so this build's intermediate bundles never collide with another test file's build.
    await buildLive(pathToFileURL(`${destination}/`), { buildTag: "locale-test" });
    const root = await readdir(destination);
    assert.ok(root.includes("fr"), "French is reviewed, so the product build writes its pages");
    assert.ok(!root.includes("locale-fr.html"), "French leads to its pages, not to a page saying it is unavailable");
    assert.ok(root.includes("locale-de.html") && root.includes("locale-lb.html"), "German and Luxembourgish still answer localization_unavailable");

    const english = liveChrome("en");
    const french = liveChrome("fr");
    const frenchByPath = new Map(entriesOf(french));
    // Each English entry a page could say whole, where the French says something else, and which no French entry
    // says too ("disposition" is a French word as well as an English one).
    const frenchTexts = entriesOf(french).map(([, text]) => text);
    const englishOnly = entriesOf(english).filter(([path, text]) => !text.includes("{") && frenchByPath.get(path) !== text && /\p{L}{2}/u.test(text)
      && !frenchTexts.some((one) => wholly(text).test(one)));
    assert.ok(englishOnly.length > 150, `most of the English table differs from the French (${englishOnly.length})`);
    // The check below finds English where English is said: on each English page, its own entries.
    for (const [page] of Object.entries(PAGES)) {
      const said = saidOutsideEnglish(await readFile(join(destination, page), "utf8"));
      assert.ok(englishOnly.filter(([, text]) => wholly(text).test(said)).length >= 5, `${page}: the check sees the English page's English`);
    }

    const frenchFiles = await readdir(join(destination, "fr"));
    for (const [page, [key, script, render, state]] of Object.entries(PAGES)) {
      // English at the root, exactly as the English bundle renders each page.
      assert.equal(await readFile(join(destination, page), "utf8"), render(), `${page}: English as the English bundle renders it`);

      assert.ok(frenchFiles.includes(page) && frenchFiles.includes(script), `fr/${page} and fr/${script} are built`);
      const html = await readFile(join(destination, "fr", page), "utf8");
      assert.match(html, /<html lang="fr"/, `fr/${page} is labelled French`);
      assert.ok(html.includes(`<script src="/fr/${script}" defer="">`), `fr/${page} loads its own script, compiled for French`);
      assert.ok(!html.includes(`src="/${script}"`), `fr/${page} does not load the English script`);
      assert.notEqual(await readFile(join(destination, "fr", script), "utf8"), await readFile(join(destination, script), "utf8"), `fr/${script} is compiled for French`);

      // The French table's chrome: the page's own entries, its first state, the shell's.
      assert.ok(html.includes(`<title>${escaped(fillText(french.shell.title, { title: french[key].title }))}</title>`), `fr/${page} has the French title`);
      for (const text of [french[key].eyebrow, french[key].heading, french[key].intro, french.shell.bannerLead, french.shell.banner, french.shell.localeNav]) {
        assert.ok(html.includes(escaped(text)), `fr/${page} says ${JSON.stringify(text.slice(0, 50))}`);
      }
      const first = state === "loading" ? french.coverage.loading : french[key].idle;
      assert.ok(html.includes(`<p role="status">${escaped(first)}</p>`), `fr/${page} says its ${state} sentence in French`);
      if (key !== "coverage") {
        assert.ok(html.includes(`<button type="submit">${escaped(french.form.submit[key])}</button>`), `fr/${page}: the French submit button`);
      }
      assert.match(html, /lang="fr" hrefLang="fr" aria-current="true">Français/, `fr/${page}: French is the current language`);

      // And none of the English table's chrome, once the language names, the English marked English and the
      // evaluation card's own values are set aside.
      let said = saidOutsideEnglish(html);
      if (key === "coverage") for (const value of cardValues(CENSUS_EVALUATION_CARD)) said = said.split(value).join(" ");
      for (const [path, text] of englishOnly) {
        assert.doesNotMatch(said, wholly(text), `fr/${page} says the English ${path}: ${JSON.stringify(text)}`);
      }
    }

    // Through the live server, as a reader reaches them: the language's home, where the locale navigation links it,
    // and every page and script under it (review of #813: the first link, `/fr/`, was a 404 on both servers).
    const server = createLiveServer({ root: pathToFileURL(`${destination}/`), apiOrigin: "http://127.0.0.1:9" });
    const origin = await new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
    try {
      assert.equal(localeHome("fr"), "/fr/index.html");
      for (const path of [localeHome("fr"), ...Object.entries(PAGES).flatMap(([page, [, script]]) => [`/fr/${page}`, `/fr/${script}`])]) {
        const answer = await fetch(`${origin}${path}`);
        assert.equal(answer.status, 200, `${path} is served`);
        await answer.arrayBuffer();
      }
      assert.equal((await fetch(`${origin}/fr/`)).status, 404, "a directory is not a page, which is why the link names the file");
    } finally {
      await new Promise((resolve) => server.close(resolve));
    }
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});
