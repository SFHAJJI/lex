// The site's 404 page and its maintenance page (the specification of record's R60; plan item 6.9), built and served.
//
// The live build writes one 404 page and one maintenance page per interface language. This test builds it once and holds:
// - each page says plainly what it is, and never anything that reads as a legal answer (no answer region, no quotation,
//   no citation, no count); the 404 page says it says nothing about the law and offers the home page's search, the same
//   screen hydrated by the same script; the maintenance page says it is not an answer about the law, names the publishers'
//   official sites, and ships no script and no form;
// - each carries the site's header and footer and the reviewed CSP, in its language, and names no address of its own;
// - the live server answers each page request nothing claims with the 404 page in the reader's language and status 404,
//   as the site's own table says (`tests/Lex.V3.Tests/Fixtures/SitePaths/not-found.json`, which the API is held to as
//   well), and a machine's path with its JSON problem; neither page is served at its own address.

import assert from "node:assert/strict";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

import { renderLiveMaintenancePage, renderLiveNotFoundPage } from "../.react-build/app.mjs";
import { buildLive } from "../scripts/build-live.mjs";
import { cspValue } from "../scripts/csp.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { OFFICIAL_SITES } from "../scripts/live-failure.mjs";
import { MACHINE_PATH_PREFIXES, isPageRequest, notFoundPageFor } from "../scripts/not-found.mjs";

const TABLE = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/SitePaths/not-found.json", import.meta.url), "utf8"));

const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");

/** What a page must never carry to read as an answer about the law. */
const ANSWER_MARKS = ["data-answer-state=\"success\"", "<blockquote", "data-article", "data-state=", "data-wording=", "refusal-card", "lex-initial-answer", "data-population", "/lu-legilux/", "/eu-eurlex/"];

test("the site's table says which request is a page's and in which language it is answered, and the module says the same", () => {
  assert.ok(TABLE.requests.length >= 20, "the table the API is held to as well");
  assert.deepEqual([...MACHINE_PATH_PREFIXES], ["/api/", "/mcp", "/health/"]);
  for (const row of TABLE.requests) {
    const chosen = notFoundPageFor(row.method, row.path, row.acceptLanguage);
    assert.equal(chosen?.page ?? null, row.page, `${row.method} ${row.path}`);
    assert.equal(isPageRequest(row.method, row.path), row.page !== null, `${row.method} ${row.path}`);
    if (row.page !== null) assert.equal(chosen.negotiated, row.negotiated, `${row.method} ${row.path}: negotiated`);
  }
});

test("the release probes hold neither page to be served at its own address, as neither server serves it", async () => {
  const { servedPaths } = await import("../scripts/image-rehearsal.mjs");
  const page = Buffer.from("<!doctype html>");
  const served = servedPaths([["fr/index.html", page], ["not-found/fr.html", page], ["maintenance/en.html", page], ["ssr/fr.js", page]]);
  assert.deepEqual([...served.keys()].sort(), ["/fr/", "/fr/index.html"]);
});

test("the 404 page and the maintenance page are built in each language, carry the site around them, and never read as a legal answer", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-live-404-"));
  try {
    // Tagged, so this build's intermediate bundles never collide with another test file's build.
    await buildLive(pathToFileURL(`${destination}/`), { buildTag: "not-found-test" });
    assert.deepEqual((await readdir(join(destination, "not-found"))).sort(), ["en.html", "fr.html"]);
    assert.deepEqual((await readdir(join(destination, "maintenance"))).sort(), ["en.html", "fr.html"]);
    for (const language of ["en", "fr"]) {
      assert.ok(!(await readdir(join(destination, language))).some((name) => /not-found|maintenance/.test(name)), `${language}/ holds neither page`);
    }

    // English, exactly as the English bundle renders each page.
    assert.equal(await readFile(join(destination, "not-found", "en.html"), "utf8"), renderLiveNotFoundPage());
    assert.equal(await readFile(join(destination, "maintenance", "en.html"), "utf8"), renderLiveMaintenancePage());

    for (const language of ["en", "fr"]) {
      const copy = liveChrome(language);
      const notFound = await readFile(join(destination, "not-found", `${language}.html`), "utf8");
      const maintenance = await readFile(join(destination, "maintenance", `${language}.html`), "utf8");
      for (const [name, html, entry] of [["not-found", notFound, copy.notFound], ["maintenance", maintenance, copy.maintenance]]) {
        const label = `${name}/${language}.html`;
        assert.match(html, new RegExp(`^<!doctype html>\\n<html lang="${language}" data-product-line="lex-v3" data-preview-state="live-${name}"`), `${label}: labelled ${language}`);
        assert.ok(html.includes(`<meta http-equiv="Content-Security-Policy" content="${escaped(cspValue())}"/>`), `${label}: the reviewed CSP`);
        assert.ok(html.includes(`<title>${escaped(entry.title)} - Lex</title>`), `${label}: its title`);
        for (const text of [entry.eyebrow, entry.heading, entry.intro, copy.site.brand, copy.site.menu, copy.site.advice, copy.site.sources]) {
          assert.ok(html.includes(escaped(text)), `${label} says ${JSON.stringify(text.slice(0, 50))}`);
        }
        assert.ok(html.includes('data-live="lex-v3-live-mount"') && html.includes('<footer class="site-footer">'), `${label}: the site's header and footer`);
        assert.ok(!/rel="(?:canonical|alternate)"/.test(html), `${label}: names no address of its own`);
        assert.ok(!/aria-current="page"/.test(html), `${label}: no menu entry is the current page`);
        for (const mark of ANSWER_MARKS) assert.ok(!html.includes(mark), `${label}: carries nothing of an answer (${mark})`);
      }

      // The 404 page: plainly a missing page, saying it says nothing about the law, with the home page's search.
      assert.match(copy.notFound.intro, language === "en" ? /says nothing about the law/ : /ne dit rien du droit/);
      assert.ok(notFound.includes(`<a href="/${language}/">${escaped(copy.notFound.home)}</a>`), `not-found/${language}: the home page`);
      assert.ok(notFound.includes('<div id="live-search-root">'), `not-found/${language}: the search screen`);
      assert.ok(notFound.includes(`<form role="search">`) && notFound.replaceAll("<!-- -->", "").includes(`<button type="submit">${escaped(copy.form.submit.search)}</button>`), `not-found/${language}: the search form`);
      assert.ok(notFound.includes(`<p role="status">${escaped(copy.search.idle)}</p>`), `not-found/${language}: the search's idle state`);
      assert.ok(notFound.includes(`<script src="/${language}/client-live-search.js" defer="">`), `not-found/${language}: hydrated by the home page's own script, named from the root`);
      assert.ok(!/ name="/.test(notFound.replace(/<meta [^>]*>/g, "")), `not-found/${language}: no field a browser would put in an address`);

      // The maintenance page: not an answer about the law, the official sites, no script and no form.
      assert.match(copy.maintenance.intro, language === "en" ? /not an answer about the law/ : /pas une réponse sur le droit/);
      assert.ok(!maintenance.includes("<script") && !maintenance.includes("<form"), `maintenance/${language}: asks nothing`);
      assert.ok(maintenance.includes(escaped(copy.failure.official)), `maintenance/${language}: the official sites' heading`);
      for (const site of OFFICIAL_SITES) {
        assert.ok(maintenance.includes(`<a href="${site.uri}" rel="external" data-publisher="${site.publisher}">${escaped(copy.failure.sites[site.key])}</a>`), `maintenance/${language}: ${site.uri}`);
      }
    }

    // Served: every page request nothing claims gets the 404 page in the reader's language, a machine's path its problem.
    const server = createLiveServer({ root: pathToFileURL(`${destination}/`), apiOrigin: "http://127.0.0.1:9" });
    const origin = await new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
    try {
      const pages = {
        "not-found/en.html": await readFile(join(destination, "not-found", "en.html"), "utf8"),
        "not-found/fr.html": await readFile(join(destination, "not-found", "fr.html"), "utf8"),
      };
      for (const row of TABLE.requests) {
        const label = `${row.method} ${row.path}`;
        const answer = await fetch(`${origin}${row.path}`, {
          method: row.method,
          redirect: "manual",
          headers: row.acceptLanguage === null ? {} : { "accept-language": row.acceptLanguage },
        });
        const body = await answer.text();
        if (row.page === null) {
          assert.notEqual(answer.headers.get("content-type"), "text/html; charset=utf-8", `${label}: not a page`);
          assert.ok(answer.status === 404 || answer.status === 405, `${label}: the server's own answer (${answer.status})`);
          if (row.method !== "HEAD") assert.match(body, /^\{"schema":"lex-v3-live-server-problem\/1"/, `${label}: the live server's problem`);
          continue;
        }
        assert.equal(answer.status, 404, label);
        assert.equal(answer.headers.get("content-type"), "text/html; charset=utf-8", label);
        assert.equal(answer.headers.get("x-content-type-options"), "nosniff", label);
        assert.equal(answer.headers.get("cache-control"), "no-store", label);
        assert.equal(answer.headers.get("vary"), row.negotiated ? "Accept-Language" : null, `${label}: Vary exactly when the language came from Accept-Language`);
        assert.equal(Number(answer.headers.get("content-length")), Buffer.byteLength(pages[row.page]), label);
        assert.equal(body, row.method === "HEAD" ? "" : pages[row.page], label);
      }
      // The pages stand at no address of their own, in any case, and the server renderers stay unserved.
      for (const path of ["/not-found/fr.html", "/NOT-FOUND/en.html", "/maintenance/en.html", "/Maintenance/fr.html", "/SSR/en.js", "/fr/no-such-page?query=x"]) {
        const answer = await fetch(`${origin}${path}`, { headers: { "accept-language": "fr" } });
        assert.equal(answer.status, 404, path);
        assert.equal(await answer.text(), pages["not-found/fr.html"], `${path}: the 404 page, never the page asked`);
      }
      // A page the build holds is still served, and the root still sends a reader to a language.
      assert.equal((await fetch(`${origin}/fr/search.html`)).status, 200);
      assert.equal((await fetch(`${origin}/`, { redirect: "manual" })).status, 302);
    } finally {
      await new Promise((resolve) => server.close(resolve));
    }
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});
