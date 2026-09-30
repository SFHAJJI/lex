// A reviewed interface language builds its own live pages (Decision 41; the launch contract's chrome
// line), so French ships as soon as its table is reviewed, and not before.
//
// No language but English is reviewed, so the product build writes English only, at the root, exactly
// as before. This test builds a second language from a stand-in table (the French draft, which the
// product never imports and never serves) and holds what a reviewed language will get:
// - every page and the script that hydrates it, under its own path (`/fr/`);
// - labelled in that language, and saying that table's words;
// - with its scripts at its own path.
// It also holds that building it leaves the English pages byte for byte as they were.

import assert from "node:assert/strict";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

import { buildLive } from "../scripts/build-live.mjs";
import { LIVE_CHROME_FR_DRAFT } from "../scripts/live-chrome-fr-draft.mjs";

const PAGES = {
  "index.html": ["coverage", "client-live.js"],
  "search.html": ["search", "client-live-search.js"],
  "dossier.html": ["dossier", "client-live-dossier.js"],
  "reading.html": ["reading", "client-live-reading.js"],
  "history.html": ["history", "client-live-history.js"],
  "compare.html": ["compare", "client-live-compare.js"],
  "radar.html": ["radar", "client-live-radar.js"],
  "export.html": ["export", "client-live-export.js"],
};

const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");

test("a reviewed language builds every page and script under its own path, in its own words, and leaves English as it was", async () => {
  const plain = await mkdtemp(join(tmpdir(), "lex-live-en-"));
  const withFrench = await mkdtemp(join(tmpdir(), "lex-live-fr-"));
  try {
    // Tagged, so these builds' intermediate bundles never collide with another test file's build.
    await buildLive(pathToFileURL(`${plain}/`), { buildTag: "locale-test-en" });
    await buildLive(pathToFileURL(`${withFrench}/`), { tables: { fr: LIVE_CHROME_FR_DRAFT }, buildTag: "locale-test-fr" });

    assert.ok(!(await readdir(plain)).includes("fr"), "with no other reviewed language, the build writes English only");
    for (const name of await readdir(plain)) {
      if (!name.endsWith(".html") && !name.endsWith(".js")) continue;
      assert.equal(await readFile(join(withFrench, name), "utf8"), await readFile(join(plain, name), "utf8"), `${name}: English is untouched by building another language`);
    }

    const french = await readdir(join(withFrench, "fr"));
    for (const [page, [key, script]] of Object.entries(PAGES)) {
      assert.ok(french.includes(page) && french.includes(script), `fr/${page} and fr/${script} are built`);
      const html = await readFile(join(withFrench, "fr", page), "utf8");
      assert.match(html, /<html lang="fr"/, `fr/${page} is labelled French`);
      assert.ok(html.includes(`<script src="/fr/${script}" defer="">`), `fr/${page} loads its own script, compiled for French`);
      assert.ok(!html.includes(`src="/${script}"`), `fr/${page} does not load the English script`);
      assert.ok(html.includes(`<h1>${escaped(LIVE_CHROME_FR_DRAFT[key].heading)}</h1>`), `fr/${page} says the French table's heading`);
      assert.ok(html.includes(`<title>${escaped(LIVE_CHROME_FR_DRAFT.shell.title.replace("{title}", LIVE_CHROME_FR_DRAFT[key].title))}</title>`), `fr/${page} has the French title`);
    }
    // The script says the same language as its page: the French search script carries the French words.
    assert.ok((await readFile(join(withFrench, "fr", "client-live-search.js"), "utf8")).includes(LIVE_CHROME_FR_DRAFT.search.nextPage));
  } finally {
    await rm(plain, { recursive: true, force: true });
    await rm(withFrench, { recursive: true, force: true });
  }
});
