// The five legal pages every footer links (the plan's item 7.1; the specification of record's R49, R50, R62, R70, R74 and
// R92; `scripts/legal-pages.mjs`, `app/LegalPage.jsx`): the legal notice, the privacy notice, the terms of use, the
// accessibility statement and the cookies page, in French and English.
//
// Held here: each page is built under each interface language, with the site around it, its own words from the chrome
// table, no script and never indexed, its language links to the same page; every live page's footer links all five; the
// publisher's details are the one data file's, each printed as its placeholder until the owner fills it; while one is
// unfilled the opening's check refuses the built pages as both servers serve them, and a filled set passes it; a detail
// filled in a form the pages cannot print stops the build; and what the pages say of the site is what its code does.

import assert from "node:assert/strict";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

import * as app from "../.react-build/app.mjs";
import { buildLive } from "../scripts/build-live.mjs";
import { CSP_DIRECTIVES } from "../scripts/csp.mjs";
import {
  ACCESSIBILITY_LAW, EURLEX_SITE, LEGAL_PAGES, LEGAL_PAGE_PATHS, LEGAL_TEXTS_UPDATED, PLACEHOLDER, PLACEHOLDER_ATTRIBUTE, PUBLISHER_PLACEHOLDERS,
  builtLegalPageFailures, isUnfilled, malformedPublisherFailures, openingFailures, placeholdersIn, publisherFailures,
} from "../scripts/legal-pages.mjs";
import { LEGAL_PUBLISHER } from "../scripts/legal-publisher.mjs";
import { LIVE_CHROME, entriesOf, liveChrome } from "../scripts/live-chrome.mjs";
import { NEVER_INDEXED_META } from "../scripts/search-engines.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";

const TABLE = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json", import.meta.url), "utf8"));
const repository = (path) => readFile(new URL(`../../${path}`, import.meta.url), "utf8");

/** A publisher's details as the owner might fill them: none of them real. */
const FILLED = Object.freeze({
  publisherName: "Alex Example",
  publisherAddress: "1, rue de l’Exemple, L-0000 Exempleville",
  contactEmail: "contact@example.invalid",
  reportRetentionMonths: "12",
});

// React escapes an apostrophe and a quotation mark in text; the table holds the text itself.
const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");
/** What a page says, its tags taken out and its entities read. */
const said = (html) => html.slice(html.indexOf("<body")).replace(/<script[\s\S]*?<\/script>/g, " ").replace(/<[^>]+>/g, "")
  .replaceAll("&#x27;", "'").replaceAll("&quot;", '"').replaceAll("&lt;", "<").replaceAll("&gt;", ">").replaceAll("&amp;", "&");
const footerOf = (html) => html.slice(html.indexOf('<footer class="site-footer">'), html.indexOf("</footer>") + "</footer>".length);
const footerLinks = (html) => [...footerOf(html).matchAll(/<nav aria-label="([^"]+)" class="legal-nav" data-legal-nav=""><ul>([\s\S]*?)<\/ul><\/nav>/g)]
  .map((match) => ({ label: match[1], links: [...match[2].matchAll(/<a href="([^"]+)"( aria-current="page")?>([^<]+)<\/a>/g)].map((link) => [link[1], link[3], link[2] !== undefined]) }));

/** The fields still unfilled in a set of details, by placeholder. */
const unfilledPlaceholders = (publisher) => Object.entries(PUBLISHER_PLACEHOLDERS).filter(([field]) => isUnfilled(publisher[field])).map(([, placeholder]) => placeholder);

test("the five legal pages are built under each interface language, with the site around them, no script, never indexed, their language links to the same page", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-live-legal-"));
  try {
    // Tagged, so this build's intermediate bundles never collide with another test file's build.
    await buildLive(pathToFileURL(`${destination}/`), { buildTag: "legal-test" });
    for (const language of ["en", "fr"]) {
      const copy = liveChrome(language);
      const files = await readdir(join(destination, language));
      for (const page of LEGAL_PAGES) {
        const label = `${language}/${page.file}`;
        assert.ok(files.includes(page.file), `${label} is built`);
        const html = await readFile(join(destination, language, page.file), "utf8");
        if (language === "en") assert.equal(html, app.renderLiveLegalPage(page.key), `${label}: as the English bundle renders it`);
        const entry = copy[page.key];
        assert.match(html, new RegExp(`^<!doctype html>\\n<html lang="${language}" data-product-line="lex-v3" data-preview-state="${page.state}"`), `${label}: labelled ${language}`);
        assert.ok(html.includes(`<title>${escaped(entry.title)} - Lex</title>`), `${label}: its title`);
        assert.ok(html.includes(`<p class="eyebrow">${escaped(entry.eyebrow)}</p>`) && html.includes(`<h1>${escaped(entry.heading)}</h1>`) && html.includes(`<p>${escaped(entry.intro)}</p>`), `${label}: its eyebrow, heading and introduction`);
        // Each part under its heading, in the table's order.
        const headings = [...html.matchAll(/<h2 id="legal-([A-Za-z]+)">([^<]+)<\/h2>/g)].map((match) => [match[1], match[2]]);
        assert.deepEqual(headings, Object.entries(entry.sections).map(([name, section]) => [name, escaped(section.heading)]), `${label}: its parts`);
        assert.ok(html.includes(`<time dateTime="${LEGAL_TEXTS_UPDATED}">`), `${label}: the date its text was last changed, in words and in the markup`);
        // Asks nothing, and is never indexed, whatever the release's switch says (R67).
        assert.ok(!html.includes("<script") && !html.includes("<form"), `${label}: no script, no form`);
        assert.deepEqual(html.match(/<meta name="robots"[^>]*\/>/g), [NEVER_INDEXED_META], `${label}: never indexed`);
        assert.ok(!html.includes('rel="canonical"'), `${label}: names no canonical address`);
        assert.deepEqual([...html.matchAll(/<link rel="alternate" hrefLang="([^"]+)" href="([^"]+)"\/>/g)].map((match) => [match[1], match[2]]), [
          ["fr", `https://law.soufien.lu/fr/${page.file}`],
          ["en", `https://law.soufien.lu/en/${page.file}`],
          ["x-default", `https://law.soufien.lu/fr/${page.file}`],
        ], `${label}: the same page in each language`);
        // With no script to point them at the same page once loaded, the language links name it as built.
        const nav = html.slice(html.indexOf('data-locale-nav=""'), html.indexOf("</nav>", html.indexOf('data-locale-nav=""')));
        assert.deepEqual([...nav.matchAll(/<a href="([^"]+)" lang="([a-z]+)"/g)].map((match) => [match[2], match[1]]), [
          ["en", `/en/${page.file}`], ["fr", `/fr/${page.file}`], ["de", "/locale-de.html"], ["lb", "/locale-lb.html"],
        ], `${label}: its language links`);
        // The site around it, the footer listing the five with this one current.
        assert.ok(html.includes('data-live="lex-v3-live-mount"') && html.includes(escaped(copy.site.notAdvice)), `${label}: the site's header and footer`);
        assert.deepEqual(footerLinks(html), [{
          label: escaped(copy.legal.menu),
          links: LEGAL_PAGES.map((other) => [`/${language}/${other.file}`, escaped(copy[other.key].title), other.key === page.key]),
        }], `${label}: the five pages in its footer, itself current`);
        assert.equal((html.match(/aria-current="page"/g) ?? []).length, 1, `${label}: no menu entry is current but itself`);
        // Each detail the data file has not filled is printed as its placeholder, and marked.
        for (const [field, placeholder] of Object.entries(PUBLISHER_PLACEHOLDERS)) {
          if (!isUnfilled(LEGAL_PUBLISHER[field])) continue;
          if (html.includes(placeholder)) assert.ok(html.includes(`<span ${PLACEHOLDER_ATTRIBUTE}="${placeholder.slice(1, -1)}">${placeholder}</span>`), `${label}: ${placeholder} marked`);
        }
      }
    }
    // Every page that names the publisher, its address and its contact names them where the data file has them.
    for (const [key, fields] of [["legalNotice", ["publisherName", "publisherAddress", "contactEmail"]], ["privacy", ["publisherName", "publisherAddress", "contactEmail", "reportRetentionMonths"]], ["terms", ["contactEmail"]], ["accessibility", ["contactEmail"]]]) {
      const html = await readFile(join(destination, "fr", LEGAL_PAGES.find((page) => page.key === key).file), "utf8");
      for (const field of fields) {
        const value = LEGAL_PUBLISHER[field];
        assert.ok(isUnfilled(value) ? html.includes(PUBLISHER_PLACEHOLDERS[field]) : html.includes(escaped(field === "reportRetentionMonths" ? `${value} mois` : value)), `fr ${key}: ${field}`);
      }
    }

    // The French pages say the French table's words and none of the English table's own legal words.
    const english = new Map(entriesOf(LIVE_CHROME.en));
    const frenchTexts = entriesOf(LIVE_CHROME.fr).map(([, text]) => text);
    const englishOnly = [...english].filter(([path, text]) => /^(?:legal|legalNotice|privacy|terms|accessibility|cookies)\./.test(path)
      && text.split(/\{[A-Za-z]+\}/).some((part) => /\p{L}{4}/u.test(part)) && !frenchTexts.includes(text));
    assert.ok(englishOnly.length > 80, `most of the legal pages' English differs from their French (${englishOnly.length})`);
    for (const page of LEGAL_PAGES) {
      const text = said(await readFile(join(destination, "fr", page.file), "utf8"));
      for (const [path, entry] of englishOnly) {
        const longest = entry.split(/\{[A-Za-z]+\}/).sort((a, b) => b.length - a.length)[0].trim();
        if (longest.length >= 12) assert.ok(!text.includes(longest), `fr/${page.file} says the English ${path}: ${JSON.stringify(longest)}`);
      }
    }

    // The release-time check over these very pages, as both servers serve them: while a detail is unfilled the opening is
    // refused, naming each placeholder; once all are filled it passes (the owner's data file, whichever it holds now).
    const unfilled = unfilledPlaceholders(LEGAL_PUBLISHER);
    const files = [];
    for (const language of ["en", "fr"]) for (const page of LEGAL_PAGES) files.push([`${language}/${page.file}`, await readFile(join(destination, language, page.file))]);
    const built = builtLegalPageFailures(files);
    assert.equal(built.length === 0, unfilled.length === 0, `the built pages are ready exactly when every detail is filled: ${JSON.stringify(built)}`);
    for (const placeholder of unfilled) assert.ok(built.some((failure) => failure.includes(placeholder)), `the check names ${placeholder}`);
    assert.equal(publisherFailures(LEGAL_PUBLISHER).length, unfilled.length, "and the data file's own check agrees");

    const server = createLiveServer({ root: pathToFileURL(`${destination}/`), apiOrigin: "http://127.0.0.1:9" });
    const origin = await new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
    try {
      const opening = await openingFailures(origin);
      assert.equal(opening.length === 0, unfilled.length === 0, `the opening is refused exactly while a detail is unfilled: ${JSON.stringify(opening)}`);
      for (const placeholder of unfilled) assert.ok(opening.some((failure) => failure.includes(placeholder)), `the opening names ${placeholder}`);
      // The site's table names the ten as files, and the live server serves each as the file it is (the API: V3WebRootTests).
      const rows = TABLE.paths.filter((row) => row.file !== undefined);
      assert.deepEqual(rows.map((row) => row.path).sort(), [...LEGAL_PAGE_PATHS].sort(), "the table's file rows are the ten legal pages");
      for (const row of rows) {
        assert.equal(row.path, `/${row.file}`, row.path);
        assert.deepEqual([row.page, row.redirect, row.moved, row.render], [null, null, null, null], `${row.path}: a file, not a law's address`);
        const answer = await fetch(`${origin}${row.path}`, { redirect: "manual" });
        assert.equal(answer.status, 200, row.path);
        assert.equal(answer.headers.get("content-type"), "text/html; charset=utf-8", row.path);
        assert.equal(answer.headers.get("x-robots-tag"), "noindex", `${row.path}: kept out of search engines`);
        assert.equal(answer.headers.get("set-cookie"), null, `${row.path}: no cookie`);
        assert.equal(await answer.text(), await readFile(join(destination, row.file), "utf8"), `${row.path}: the built page`);
      }
    } finally {
      await new Promise((resolve) => server.close(resolve));
    }
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});

test("every live page's footer links the five legal pages, none of them current but on a legal page", () => {
  const copy = liveChrome("en");
  const pages = {
    home: app.renderLiveHomePage(), coverage: app.renderLiveCoveragePage(), search: app.renderLiveSearchPage(), dossier: app.renderLiveDossierPage(),
    reading: app.renderLiveReadingPage(), history: app.renderLiveHistoryPage(), compare: app.renderLiveComparePage(), radar: app.renderLiveRadarPage(),
    export: app.renderLiveExportPage(), notFound: app.renderLiveNotFoundPage(), maintenance: app.renderLiveMaintenancePage(),
  };
  for (const [name, html] of Object.entries(pages)) {
    assert.deepEqual(footerLinks(html), [{ label: copy.legal.menu, links: LEGAL_PAGES.map((page) => [`/en/${page.file}`, escaped(copy[page.key].title), false]) }], name);
  }
});

test("each detail is its placeholder until filled; filled, every page says it and marks nothing; filled wrong, the build stops", async () => {
  // Unfilled: each detail its placeholder, marked; the pages are not ready to publish.
  const unfilled = Object.fromEntries(LEGAL_PAGES.map((page) => [page.key, app.renderLiveLegalPage(page.key, { publisher: PUBLISHER_PLACEHOLDERS })]));
  assert.deepEqual(placeholdersIn(unfilled.legalNotice).tokens, ["«PUBLISHER_NAME»", "«PUBLISHER_ADDRESS»", "«CONTACT_EMAIL»"]);
  assert.deepEqual(placeholdersIn(unfilled.privacy).tokens, ["«PUBLISHER_NAME»", "«PUBLISHER_ADDRESS»", "«CONTACT_EMAIL»", "«REPORT_RETENTION_MONTHS»"]);
  assert.deepEqual(placeholdersIn(unfilled.terms).tokens, ["«CONTACT_EMAIL»"]);
  assert.deepEqual(placeholdersIn(unfilled.accessibility).tokens, ["«CONTACT_EMAIL»"]);
  assert.deepEqual(placeholdersIn(unfilled.cookies).tokens, [], "the cookies page names no detail");
  assert.deepEqual(publisherFailures(PUBLISHER_PLACEHOLDERS).length, 4);
  assert.deepEqual(malformedPublisherFailures(PUBLISHER_PLACEHOLDERS), [], "an unfilled detail is built as its placeholder: only the opening refuses it");
  // An emptied detail is printed as its placeholder too, never as nothing.
  assert.ok(app.renderLiveLegalPage("legalNotice", { publisher: { ...FILLED, publisherAddress: "" } }).includes(`<span ${PLACEHOLDER_ATTRIBUTE}="PUBLISHER_ADDRESS">«PUBLISHER_ADDRESS»</span>`));

  // Filled: said where each page says it, the contact a link to write to, the retention in months; nothing marked.
  const filled = Object.fromEntries(LEGAL_PAGES.map((page) => [page.key, app.renderLiveLegalPage(page.key, { publisher: FILLED })]));
  assert.deepEqual(publisherFailures(FILLED), []);
  assert.deepEqual(builtLegalPageFailures(LEGAL_PAGES.flatMap((page) => ["en", "fr"].map((language) => [`${language}/${page.file}`, Buffer.from(filled[page.key])]))), []);
  for (const [key, html] of Object.entries(filled)) {
    assert.deepEqual(placeholdersIn(html), { tokens: [], marked: false }, key);
    assert.doesNotMatch(html, PLACEHOLDER, key);
  }
  const notice = said(filled.legalNotice);
  assert.ok(notice.includes("Lex is published by Alex Example, 1, rue de l’Exemple, L-0000 Exempleville, who is also its director of publication."), notice);
  assert.ok(filled.legalNotice.includes('Contact: <a href="mailto:contact@example.invalid">contact@example.invalid</a>.'));
  assert.ok(said(filled.privacy).includes("Your message is kept for 12 months after the matter it raises is closed, and then deleted."));
  assert.ok(said(app.renderLiveLegalPage("privacy", { publisher: { ...FILLED, reportRetentionMonths: "1" } })).includes("kept for 1 month after"), "one month, in the singular");

  // Filled in a form the pages cannot print: refused, each with its reason; and the build stops before it writes anything.
  const wrong = [
    [{ ...FILLED, contactEmail: "contact at example" }, /contactEmail "contact at example" is not an e-mail address/],
    [{ ...FILLED, reportRetentionMonths: "twelve" }, /reportRetentionMonths "twelve" is not a whole number of months/],
    [{ ...FILLED, reportRetentionMonths: "0" }, /reportRetentionMonths "0" is not a whole number of months, from 1 to 99/],
    [{ ...FILLED, publisherName: " Alex Example" }, /publisherName " Alex Example" has a space at an end/],
    [{ ...FILLED, publisherAddress: "1, rue de l’Exemple\nL-0000 Exempleville" }, /publisherAddress .* line break/],
    [{ ...FILLED, director: "Someone" }, /director is no detail the legal pages print/],
  ];
  for (const [publisher, reason] of wrong) {
    const failures = malformedPublisherFailures(publisher);
    assert.ok(failures.length === 1 && reason.test(failures[0]), `${JSON.stringify(publisher)}: ${JSON.stringify(failures)}`);
    assert.ok(publisherFailures(publisher).some((failure) => reason.test(failure)));
  }
  const destination = await mkdtemp(join(tmpdir(), "lex-live-legal-wrong-"));
  try {
    await assert.rejects(buildLive(pathToFileURL(`${destination}/`), { buildTag: "legal-wrong", publisher: wrong[0][0] }), /cannot be printed: contactEmail "contact at example" is not an e-mail address/);
    assert.deepEqual(await readdir(destination), [], "nothing was built");
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});

test("what the legal pages say of the site is what its code and its deployment do", async () => {
  const copy = liveChrome("en");
  // R50's sentence and the authenticity line, word for word, on the terms of use.
  const terms = app.renderLiveLegalPage("terms");
  assert.ok(terms.includes(`<p>${escaped(copy.site.notAdvice)}</p>`), "the terms say R50's sentence, as a paragraph of its own");
  assert.ok(said(terms).includes(copy.site.authenticity));
  // "It loads nothing from another site": the page's policy names no other origin, whatever the directive.
  for (const [directive, sources] of Object.entries(CSP_DIRECTIVES)) {
    assert.ok(sources.every((source) => source === "'self'" || source === "'none'"), `${directive}: ${sources.join(" ")}`);
  }
  // "Lex asks your browser not to tell it which page you came from": the API's pages carry Referrer-Policy: no-referrer.
  assert.match(await repository("src/Lex.V3.Api/V3WebRoot.cs"), /headers\["Referrer-Policy"\] = "no-referrer";/);
  // "It writes no access log": the API clears its logging providers; "no log store and no monitoring service": the
  // template configures none, and no affinity cookie (sticky sessions) either.
  assert.match(await repository("src/Lex.V3.Api/Program.cs"), /builder\.Logging\.ClearProviders\(\);/);
  const bicep = await repository("deploy/main.bicep");
  for (const absent of [/stickySessions/i, /affinity/i, /appInsights|applicationInsights/i, /logAnalytics|appLogsConfiguration/i, /diagnosticSettings/i]) {
    assert.doesNotMatch(bicep, absent, `the template configures ${absent}`);
  }
  // "No cookie, no browser storage": no live page's code sets a cookie or writes the browser's storage.
  const { readdir: list } = await import("node:fs/promises");
  for (const name of await list(new URL("../app/", import.meta.url))) {
    if (!/\.(?:jsx|mjs)$/.test(name)) continue;
    const source = await readFile(new URL(`../app/${name}`, import.meta.url), "utf8");
    assert.doesNotMatch(source, /document\.cookie\s*=|localStorage|sessionStorage|indexedDB/, `app/${name}`);
  }
  // The EU acknowledgement the terms quote is the one the platform serves with EU text (Decision 95).
  assert.ok((await repository("src/Lex.V3.Api/V3CorpusMount.cs")).includes(`EuropeTextAcknowledgement = "© European Union, ${EURLEX_SITE}";`));
  // The accessibility law, by its French title, marked French on an English page (WCAG 3.1.2).
  assert.ok(app.renderLiveLegalPage("accessibility").includes(`<span lang="fr">${escaped(ACCESSIBILITY_LAW)}</span>`));
  // "Files exported as PDF are not tagged": the PDF writer says so of itself.
  assert.match(await readFile(new URL("../scripts/export-pdf.mjs", import.meta.url), "utf8"), /The PDF is not tagged/);
});
