// The five legal pages every footer links (the plan's item 7.1; the specification of record's R49, R50, R62 and R70, as
// the owner's decision of 2026-10-19 departs from R49 and O5; `scripts/legal-pages.mjs`, `app/LegalPage.jsx`): the legal
// notice, the privacy notice, the terms of use, the accessibility statement and the cookies page, in French and English.
//
// Held here: each page is built under each interface language, with the site around it, its own words from the chrome
// table, no script and never indexed, its language links to the same page; every live page's footer links all five; by the
// owner's decision of 2026-10-19 no page, in either language, names a person or gives a postal address, an e-mail address
// or a contact of any kind, and no page says a placeholder; the privacy notice keeps the reader's right to complain to the
// CNPD, one sentence, its site linked and no postal address; the opening's check passes over the built pages as both
// servers serve them; and what the pages say of the site is what its code does.

import assert from "node:assert/strict";
import { access, mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

import * as app from "../.react-build/app.mjs";
import { buildLive } from "../scripts/build-live.mjs";
import { CSP_DIRECTIVES } from "../scripts/csp.mjs";
import {
  ACCESSIBILITY_LAW, CNPD_SITE, EURLEX_ACKNOWLEDGEMENT, LEGAL_PAGES, LEGAL_PAGE_PATHS, LEGAL_TEXTS_UPDATED, SHOWN_NOWHERE,
  builtLegalPageFailures, legalPageTextFailures, openingFailures, shownOnPage,
} from "../scripts/legal-pages.mjs";
import { LIVE_CHROME, entriesOf, liveChrome } from "../scripts/live-chrome.mjs";
import { searchFragment } from "../scripts/search-address.mjs";
import { NEVER_INDEXED_META } from "../scripts/search-engines.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";

const TABLE = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json", import.meta.url), "utf8"));
const repository = (path) => readFile(new URL(`../../${path}`, import.meta.url), "utf8");

// React escapes an apostrophe and a quotation mark in text; the table holds the text itself.
const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");
/** What a page says, its tags taken out and its entities read. */
const said = (html) => html.slice(html.indexOf("<body")).replace(/<script[\s\S]*?<\/script>/g, " ").replace(/<[^>]+>/g, "")
  .replaceAll("&#x27;", "'").replaceAll("&quot;", '"').replaceAll("&lt;", "<").replaceAll("&gt;", ">").replaceAll("&amp;", "&");
const footerOf = (html) => html.slice(html.indexOf('<footer class="site-footer">'), html.indexOf("</footer>") + "</footer>".length);
const footerLinks = (html) => [...footerOf(html).matchAll(/<nav aria-label="([^"]+)" class="legal-nav" data-legal-nav=""><ul>([\s\S]*?)<\/ul><\/nav>/g)]
  .map((match) => ({ label: match[1], links: [...match[2].matchAll(/<a href="([^"]+)"( aria-current="page")?>([^<]+)<\/a>/g)].map((link) => [link[1], link[3], link[2] !== undefined]) }));

/** The chrome table's sections the legal pages say. */
const LEGAL_SECTIONS = /^(?:legal|legalNotice|privacy|terms|accessibility|cookies)\./;

/**
 * The words for a person's details or a contact, in English and in French, that no legal page says (the owner's decision
 * of 2026-10-19: no person, no postal address, no e-mail address, no contact of any kind): a contact, an e-mail or a
 * mailbox, a postal address, a telephone, writing to someone, reporting to someone, a request or an objection to someone,
 * and the persons R49 named (a director of publication, a controller, the publisher of Lex, the private person publishing
 * it). The privacy notice's "names no private person" is about the acts, and stays.
 */
const NO_CONTACT_WORDS = Object.freeze([
  /\bcontact/i, /\be-?mail/i, /courriel/i, /courrier électronique/i, /adresse électronique/i, /mailbox/i, /messagerie/i,
  /postal address/i, /adresse postale/i, /\bstreet\b/i, /\b(?:tele)?phone\b/i, /téléphone/i,
  /\bwrite to\b/i, /\bwrite us\b/i, /écri(?:vez|re|vant)\b/i, /\breport(?:ing)? (?:an error|a barrier|a problem)\b/i, /signaler|signalement/i,
  /\byou may (?:ask|request)\b/i, /\brequest (?:access|erasure|rectification)\b/i, /vous pouvez demander/i,
  /\bobject(?:ion)?s?\b/i, /\bopposition\b/i, /vous opposer/i,
  /director of publication/i, /direction de la publication/i, /\bcontroller\b/i, /responsable du traitement/i,
  /publisher of Lex/i, /éditeur de Lex/i, /published by a private person/i, /édité par une personne privée/i, /personne physique/i,
]);

/** The words of `NO_CONTACT_WORDS` a text says, as the patterns that match. */
const contactWordsIn = (text) => NO_CONTACT_WORDS.filter((pattern) => pattern.test(text)).map(String);

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
        // The owner's decision of 2026-10-19, on the page as built: no e-mail address, no mail or telephone link, no
        // placeholder, no postcode anywhere in its bytes, and no word for a person's details or a contact in what it says.
        assert.deepEqual(shownOnPage(html), [], `${label}: shows none of what the decision keeps off the site`);
        assert.deepEqual(contactWordsIn(said(html)), [], `${label}: says no word for a person's details or a contact`);
        assert.ok(!html.includes("mailto:") && !html.includes("data-legal-placeholder"), `${label}: no mail link, no unfilled detail`);
      }
    }

    // The EU texts' acknowledgement, quoted on the legal notice as the texts carry it, in English: marked English on the
    // French page (WCAG 3.1.2).
    const frenchNotice = await readFile(join(destination, "fr", "legal-notice.html"), "utf8");
    assert.ok(frenchNotice.includes(`« <span lang="en">${EURLEX_ACKNOWLEDGEMENT}</span> »`), "fr/legal-notice.html: the acknowledgement marked English");
    assert.ok(!(await readFile(join(destination, "en", "legal-notice.html"), "utf8")).includes('<span lang="en">'), "en/legal-notice.html: nothing to mark");
    // What Lex is, for the texts and dates it holds, and the footer strip of a page that never has an answer, in the
    // reviewed French (the review of #990; the addendum of 2026-10-26).
    assert.ok(said(frenchNotice).includes(LIVE_CHROME.fr.legalNotice.sections.what.text), "fr/legal-notice.html: what Lex is, qualified");
    assert.ok(frenchNotice.includes(`<p>${LIVE_CHROME.fr.freshness.noAnswer}</p>`), "fr/legal-notice.html: the strip carries no answer");

    // The French pages say the French table's words and none of the English table's own legal words.
    const english = new Map(entriesOf(LIVE_CHROME.en));
    const frenchTexts = entriesOf(LIVE_CHROME.fr).map(([, text]) => text);
    const englishOnly = [...english].filter(([path, text]) => LEGAL_SECTIONS.test(path)
      && text.split(/\{[A-Za-z]+\}/).some((part) => /\p{L}{4}/u.test(part)) && !frenchTexts.includes(text));
    assert.ok(englishOnly.length > 50, `most of the legal pages' English differs from their French (${englishOnly.length})`);
    for (const page of LEGAL_PAGES) {
      const text = said(await readFile(join(destination, "fr", page.file), "utf8"));
      for (const [path, entry] of englishOnly) {
        const longest = entry.split(/\{[A-Za-z]+\}/).sort((a, b) => b.length - a.length)[0].trim();
        if (longest.length >= 12) assert.ok(!text.includes(longest), `fr/${page.file} says the English ${path}: ${JSON.stringify(longest)}`);
      }
    }

    // The release-time check over these very pages, and the opening's check as both servers serve them: both pass.
    const files = [];
    for (const language of ["en", "fr"]) for (const page of LEGAL_PAGES) files.push([`${language}/${page.file}`, await readFile(join(destination, language, page.file))]);
    assert.deepEqual(builtLegalPageFailures(files), [], "the built pages are ready to publish");

    const server = createLiveServer({ root: pathToFileURL(`${destination}/`), apiOrigin: "http://127.0.0.1:9" });
    const origin = await new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
    try {
      assert.deepEqual(await openingFailures(origin), [], "the opening's check passes over the pages the live server serves");
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
    // No page of the site gives a contact either: no e-mail address and no mail link in any page's bytes.
    assert.deepEqual(shownOnPage(html), [], `${name}: shows none of what the owner's decision keeps off the site`);
  }
});

test("no legal page, in either language, names a person or gives an address, an e-mail or a contact (the owner's decision of 2026-10-19)", async () => {
  for (const language of ["en", "fr"]) {
    const table = LIVE_CHROME[language];
    const entries = entriesOf(table).filter(([path]) => LEGAL_SECTIONS.test(path));
    assert.ok(entries.length > 60, `${language}: the legal sections are read (${entries.length})`);
    for (const [path, text] of entries) {
      assert.deepEqual(contactWordsIn(text), [], `${language} ${path}: ${text}`);
      assert.deepEqual(shownOnPage(text), [], `${language} ${path}: ${text}`);
      // A sentence names only the values the pages fill, none of them a person's detail.
      for (const [, name] of text.matchAll(/\{([A-Za-z]+)\}/g)) {
        assert.ok(["date", "notAdvice", "authenticity", "legalNoticePage", "cookiesPage", "cnpdSite", "eurlexAcknowledgement", "accessibilityLaw"].includes(name), `${language} ${path} names {${name}}`);
      }
    }
    // No part of any page is a way to reach anyone: no report, no rights request, no objection, no controller, no publisher.
    for (const [page, parts] of [["legalNotice", ["publisher", "independence"]], ["privacy", ["controller", "email", "processors"]], ["terms", ["errors"]], ["accessibility", ["report"]]]) {
      for (const part of parts) assert.ok(!Object.hasOwn(table[page].sections, part), `${language} ${page}.${part} is gone`);
    }
    // The privacy notice's rights are the one complaint line, to the regulator: no request to Lex, no objection.
    assert.deepEqual(Object.keys(table.privacy.sections.rights), ["heading", "complaint"], `${language}: the rights are the complaint line alone`);
    assert.ok(!Object.hasOwn(table.legal, "months"), `${language}: no retention to say, since nothing is kept`);
  }
  // Rendered from each language's table, as each page's bundle renders it: nothing of the decision's on any page.
  for (const page of LEGAL_PAGES) {
    const html = app.renderLiveLegalPage(page.key);
    assert.deepEqual(legalPageTextFailures(`/en/${page.file}`, html), [], page.key);
  }
  // There is no data file of a publisher's details any more, and no page or script reads one.
  await assert.rejects(access(new URL("../scripts/legal-publisher.mjs", import.meta.url)), /ENOENT/);
  for (const directory of ["../app/", "../scripts/"]) {
    for (const name of await readdir(new URL(directory, import.meta.url))) {
      if (!/\.(?:jsx|mjs)$/.test(name)) continue;
      assert.doesNotMatch(await readFile(new URL(`${directory}${name}`, import.meta.url), "utf8"), /legal-publisher|publisherName|contactEmail|reportRetentionMonths/, `${directory}${name}`);
    }
  }
});

test("the check the opening runs finds each mark of a person's details or a contact, and nothing in the pages as they are", () => {
  const cases = [
    ["an e-mail address", "<p>Contact: someone@example.invalid.</p>"],
    ["a mail or telephone link", '<p><a href="mailto:someone">write</a></p>'],
    ["a mail or telephone link", '<p><a href="tel:+352000000">call</a></p>'],
    ["a placeholder a template left («LIKE_THIS»)", "<p>Lex is published by «PUBLISHER_NAME».</p>"],
    ["a Luxembourg postcode, as a postal address writes it", "<p>1, rue de l’Exemple, L-0000 Exempleville</p>"],
  ];
  for (const [what, html] of cases) {
    assert.deepEqual(shownOnPage(html), [what], html);
    assert.match(legalPageTextFailures("/fr/privacy.html", html)[0], new RegExp(`^/fr/privacy\\.html shows ${what.replace(/[()«»]/g, ".")}, which the owner's decision of 2026-10-19 keeps off the site`));
  }
  assert.equal(SHOWN_NOWHERE.length, 4);
  // Every pattern reads Unicode, and a link's scheme or a postcode counts only at the edge of a word in any script (the
  // review of #990): `\b` took the "ô" of "Hôtel" for a word's edge, even under `u`, and found "tel:" in it.
  assert.ok(SHOWN_NOWHERE.every(({ pattern }) => pattern.unicode), "every pattern reads Unicode");
  for (const text of ["Hôtel: rue", "Hôtel: rue", "HÔTEL:", "L’hôtel:", "Motel:", "ÉL-1234", "L-1234é"]) {
    assert.deepEqual(shownOnPage(text), [], `${JSON.stringify(text)}: inside a word, no link and no postcode`);
  }
  for (const [text, what] of [
    ["Hôtel (tel:+352000000)", "a mail or telephone link"], ["Hôtel tel:+352000000", "a mail or telephone link"], ["Écrire mailto:x", "a mail or telephone link"],
    ["rue de l’Hôtel, L-1234 Exempleville", "a Luxembourg postcode, as a postal address writes it"], ["«L-1234»", "a Luxembourg postcode, as a postal address writes it"],
  ]) {
    assert.deepEqual(shownOnPage(text), [what], `${JSON.stringify(text)}: at a word's edge, found`);
  }
  // What the pages say that only looks like one: a quotation in French marks, an article's number, a licence's version, a
  // regulation's number, the "#" of a search kept in a link, the link to the regulator a reader may complain to.
  for (const html of ["<p>« © European Union, https://eur-lex.europa.eu »</p>", "<p>WCAG 2.2, CC BY 4.0, 2011/833/EU, « # »</p>", `<a href="${CNPD_SITE.href}" rel="external">${CNPD_SITE.text}</a>`]) {
    assert.deepEqual(shownOnPage(html), [], html);
  }
});

test("the privacy notice keeps the reader's right to complain to the CNPD, in one sentence, its site linked and no postal address (a driver decision of 2026-10-19)", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-live-legal-cnpd-"));
  try {
    await buildLive(pathToFileURL(`${destination}/`), { buildTag: "legal-cnpd" });
    for (const [language, sentence] of [
      ["en", "You may lodge a complaint with the Commission nationale pour la protection des données (CNPD), cnpd.public.lu."],
      ["fr", "Vous pouvez introduire une réclamation auprès de la Commission nationale pour la protection des données (CNPD), cnpd.public.lu."],
    ]) {
      const html = await readFile(join(destination, language, "privacy.html"), "utf8");
      assert.ok(said(html).includes(sentence), `${language}: the complaint line, one sentence`);
      assert.equal(html.match(/<a href="https:\/\/cnpd\.public\.lu\/" rel="external">cnpd\.public\.lu<\/a>/g)?.length, 1, `${language}: the regulator's site, linked once`);
      assert.equal(said(html).match(/CNPD/g)?.length, 1, `${language}: the regulator named once`);
      // Its postal address, which the 2026-10-18 page gave, is said nowhere, and the opening's check finds nothing.
      for (const absent of [/Belvaux/, /Boulevard du Jazz/, /\bL-\d{4}\b/, /\b4370\b/]) assert.doesNotMatch(html, absent, `${language}: ${absent}`);
      assert.deepEqual(legalPageTextFailures(`/${language}/privacy.html`, html), [], `${language}: the opening's check passes`);
      assert.deepEqual(contactWordsIn(said(html)), [], `${language}: no word for a contact`);
    }
    // No other legal page names the regulator.
    for (const language of ["en", "fr"]) {
      for (const page of LEGAL_PAGES.filter((one) => one.key !== "privacy")) {
        assert.doesNotMatch(await readFile(join(destination, language, page.file), "utf8"), /cnpd/i, `${language}/${page.file}`);
      }
    }
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});

test("what the legal pages say of the site is what its code and its deployment do", async () => {
  const copy = liveChrome("en");
  // R50's sentence and the authenticity line, word for word, on the legal notice and the terms of use.
  for (const key of ["legalNotice", "terms"]) {
    const html = app.renderLiveLegalPage(key);
    assert.ok(html.includes(`<p>${escaped(copy.site.notAdvice)}</p>`), `${key}: R50's sentence, as a paragraph of its own`);
    assert.ok(said(html).includes(copy.site.authenticity), `${key}: the authenticity line`);
  }
  assert.ok(said(app.renderLiveLegalPage("legalNotice")).includes(`${copy.site.authenticity} Where a text on Lex differs from its official publication, the official publication prevails.`));
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
  // "A search's words travel in the body of the request, never in its address": every answer is asked by POST to the
  // operation's path with the parameters in the body; and "a link that keeps a search holds it after the “#”".
  const client = await readFile(new URL("../scripts/v3-client.mjs", import.meta.url), "utf8");
  assert.match(client, /fetchImpl\(`\/api\/v3\/\$\{operation\}`, \{\s*method: "POST",[\s\S]*?body: JSON\.stringify\(\{ operation_id: operation, parameters \}\)/);
  assert.match(searchFragment({ query: "bail à loyer", language: "fra" }, { work: false }), /^#q=bail%20%C3%A0%20loyer&lang=fra$/);
  // "No cookie, no browser storage": no live page's code sets a cookie or writes the browser's storage.
  for (const name of await readdir(new URL("../app/", import.meta.url))) {
    if (!/\.(?:jsx|mjs)$/.test(name)) continue;
    const source = await readFile(new URL(`../app/${name}`, import.meta.url), "utf8");
    assert.doesNotMatch(source, /document\.cookie\s*=|localStorage|sessionStorage|indexedDB/, `app/${name}`);
  }
  // The EU acknowledgement the legal notice quotes is the one the platform serves with EU text (Decision 95).
  assert.ok((await repository("src/Lex.V3.Api/V3CorpusMount.cs")).includes(`EuropeTextAcknowledgement = "${EURLEX_ACKNOWLEDGEMENT}";`));
  assert.ok(said(app.renderLiveLegalPage("legalNotice")).includes(`carry its acknowledgement, “${EURLEX_ACKNOWLEDGEMENT}”.`), "quoted as the texts carry it");
  // The terms point to the legal notice for the sources and their acknowledgements.
  assert.ok(app.renderLiveLegalPage("terms").includes(`<a href="/en/legal-notice.html">${copy.legal.inline.legalNotice}</a>`));
  // The accessibility law, by its French title, marked French on an English page (WCAG 3.1.2).
  assert.ok(app.renderLiveLegalPage("accessibility").includes(`<span lang="fr">${escaped(ACCESSIBILITY_LAW)}</span>`));
  // "Files exported as PDF are not tagged": the PDF writer says so of itself.
  assert.match(await readFile(new URL("../scripts/export-pdf.mjs", import.meta.url), "utf8"), /The PDF is not tagged/);
});

test("the accessibility statement declares Lex non-conformant until an audit, the owner's decision of 2026-10-19, in the review's words, and asks for no report", () => {
  const english = said(app.renderLiveLegalPage("accessibility"));
  assert.ok(english.includes("Lex is built to meet the Web Content Accessibility Guidelines (WCAG) 2.2 at level AA. It has not been audited yet, so it does not claim to meet them: until an audit of the site in service is done, it is non-conformant, as a public sector body's site with no audit is declared in Luxembourg."), english);
  assert.ok(!english.includes("partially conformant"), "the status the owner replaced is said nowhere");
  // The French, entry 13 of the review's addendum of 2026-10-18, word for word (a no-break space before the colon).
  const french = LIVE_CHROME.fr.accessibility.sections.status.text;
  assert.equal(french, "Lex est conçu pour respecter les Règles pour l’accessibilité des contenus Web (WCAG) 2.2 au niveau AA. Il n’a pas encore été audité et ne prétend donc pas les respecter : tant qu’un audit du site en service n’a pas été réalisé, il est non conforme, comme est déclaré au Luxembourg le site d’un organisme du secteur public qui n’a fait l’objet d’aucun audit.");
  assert.ok(!JSON.stringify(LIVE_CHROME.fr).includes("partiellement conforme"));
  assert.deepEqual(Object.keys(LIVE_CHROME.en.accessibility.sections), ["status", "done", "limits", "law", "prepared"]);
});

test("the legal notice promises a text at a date only for the texts and dates Lex holds, as the terms do, and its date says when it changed (the review of #990)", () => {
  // The English, and the French of the review's addendum of 2026-10-26, byte for byte (a no-break space before the colon).
  const english = "Lex is an independent, non-commercial tool for reading Luxembourg and EU law over time: for the texts and dates it holds, it shows a text as it stood on the date you choose, and how it changed. It is free, carries no advertising and asks for no account.";
  const french = "Lex est un outil indépendant et non commercial pour lire le droit luxembourgeois et le droit de l’UE au fil du temps : pour les textes et les dates qu’il détient, il montre un texte tel qu’il se présentait à la date de votre choix, et comment il a changé. Il est gratuit, ne comporte aucune publicité et ne demande la création d’aucun compte.";
  assert.equal(LIVE_CHROME.en.legalNotice.sections.what.text, english);
  assert.equal(LIVE_CHROME.fr.legalNotice.sections.what.text, french);
  // The terms' service sentence makes the same promise with the same limit, in each language.
  assert.match(LIVE_CHROME.en.terms.sections.what.service, /^It is an information service, free of charge: it shows the texts it holds, at the dates it holds them,/);
  assert.match(LIVE_CHROME.fr.terms.sections.what.service, /il montre les textes qu’il détient, aux dates auxquelles il les détient,/);
  // No legal text in either language promises a text at the chosen date without the limit.
  for (const language of ["en", "fr"]) {
    const unqualified = language === "en" ? /: it shows a text as it stood/ : /: il montre un texte tel qu’il/;
    for (const [path, text] of entriesOf(LIVE_CHROME[language]).filter(([one]) => LEGAL_SECTIONS.test(one))) assert.doesNotMatch(text, unqualified, `${language} ${path}`);
  }
  assert.ok(said(app.renderLiveLegalPage("legalNotice")).includes(english), "the English page says it");
  // The texts changed on 2026-10-26, and every page says so ("Last updated"), in words and in the markup.
  assert.equal(LEGAL_TEXTS_UPDATED, "2026-10-26");
  for (const page of LEGAL_PAGES) assert.ok(app.renderLiveLegalPage(page.key).includes(`<time dateTime="2026-10-26">26 October 2026</time>`), page.key);
  // The footer strip of a page that ships no script, as reviewed in the same addendum, byte for byte (shown on every legal
  // page and the maintenance page: `freshness.test.mjs`; the French page as built says both: the first test above).
  assert.equal(LIVE_CHROME.fr.freshness.noAnswer, "Cette page ne contient aucune réponse du serveur ; il n’y a donc rien à afficher ici. Sur une page qui en contient une, cette partie indique, d’après cette réponse, quand l’index a été construit, quand les textes de chaque éditeur ont été collectés pour la dernière fois, ainsi que le code qui a construit l’index et celui qui le sert.");
  assert.equal(LIVE_CHROME.en.freshness.noAnswer, "This page carries no answer from the server, so there is nothing to show here. On a page that has one, this part says, from that answer, when the index was built, when each publisher's texts were last collected and which code built and serves it.");
});
