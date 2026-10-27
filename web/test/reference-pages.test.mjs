// The three reference pages of the launch set (the specification of record, §3, Screens: Developers, Get help, How to read
// Lex; R42, R46, R48, R50, R55; `scripts/reference-pages.mjs`, `scripts/developers.mjs`, `app/ReferencePages.jsx`).
//
// Held here:
// - each page is built under each interface language, with the site around it, its own words from the chrome table, no
//   script and no form, never indexed, its language links to the same page; every internal link it holds leads to a page or
//   a file the build wrote, and the live server serves each;
// - every live page's footer links the three, and the home page links Developers and Get help (§3, Shell and home);
// - the Developers page is the registry's: its operations, refusal codes, verdicts and envelope members are the contracts'
//   and the C# registry's, in their order, each with its words in both languages, so an operation (or a code, a verdict, a
//   member) added to the platform without the page fails here; its served operations are the API's routes; its schemas are
//   published byte for byte, each linked with its digest; the facts it takes from the API's code are that code's;
// - the glossary's terms are the site's own words, in each language, as the pages that print them say them;
// - R21's time-word guard holds over every word of the three pages, in both languages, as written and as rendered;
// - Get help says there is no contact and gives none (the owner's decision of 2026-10-19);
// - every count the Developers page says is the contracts'.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

import * as app from "../.react-build/app.mjs";
import { buildLive } from "../scripts/build-live.mjs";
import {
  CLIENT_LIMIT, KEY_SET_PATH, LIVE_EXAMPLE_REQUEST, MAXIMUM_REQUEST_BYTES, MCP_INSTRUCTIONS, MCP_METHODS, MCP_PROTOCOL_VERSION, REPLAY_GUARANTEES,
  REPLAY_TESTS, TRANSPORT_PROBLEMS, TRANSPORT_PROBLEM_SCHEMA, developersModel, liveAnswerText, publishedSchemaPath,
} from "../scripts/developers.mjs";
import { OUT_OF_SCOPE_HOSTS } from "../scripts/routes.mjs";
import { sitePageFor } from "../scripts/permalinks.mjs";
import { readDevelopersContracts, readPlatformSchemas } from "../scripts/developers-contracts.mjs";
import { SHOWN_NOWHERE, shownOnPage } from "../scripts/legal-pages.mjs";
import { LIVE_CHROME, entriesOf, fillCounted, liveChrome } from "../scripts/live-chrome.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { GLOSSARY, REFERENCE_PAGES, REFERENCE_PAGE_PATHS, entryAt, glossaryRows, glossaryTerm } from "../scripts/reference-pages.mjs";
import { NEVER_INDEXED_META } from "../scripts/search-engines.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";

const repository = (path) => readFile(new URL(`../../${path}`, import.meta.url), "utf8");
const census = JSON.parse(await repository("schemas/v3-platform/envelope-samples.json"));
const answers = JSON.parse(await repository("schemas/v3-platform/answer-samples.json"));
const DEVELOPERS = await readDevelopersContracts();
const PLATFORM_SCHEMAS = await readPlatformSchemas();
const DEVELOPERS_SCHEMAS = PLATFORM_SCHEMAS.map(({ file, json, sha256 }) => ({ file, json, sha256 }));
const SCHEMA_BYTES = new Map(PLATFORM_SCHEMAS.map(({ file, bytes }) => [`schemas/v3-platform/${file}`, bytes]));
/** A schema file of `schemas/v3-platform`, by its path in the repository: its text, or with `null` its bytes. */
const readFileSyncOf = (path, encoding = "utf8") => (encoding === null ? SCHEMA_BYTES.get(path) : SCHEMA_BYTES.get(path).toString("utf8"));

// React escapes an apostrophe and a quotation mark in text; the table holds the text itself.
const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");
/** What a page says, its tags taken out and its entities read. */
const said = (html) => html.slice(html.indexOf("<body")).replace(/<script[\s\S]*?<\/script>/g, " ").replace(/<[^>]+>/g, "")
  .replaceAll("&#x27;", "'").replaceAll("&quot;", '"').replaceAll("&lt;", "<").replaceAll("&gt;", ">").replaceAll("&amp;", "&");
const mainOf = (html) => html.slice(html.indexOf('<main id="main">'), html.indexOf("</main>"));
const footerOf = (html) => html.slice(html.indexOf('<footer class="site-footer">'), html.indexOf("</footer>") + "</footer>".length);
const referenceLinks = (html) => [...footerOf(html).matchAll(/<nav aria-label="([^"]+)" class="reference-nav" data-reference-nav=""><ul>([\s\S]*?)<\/ul><\/nav>/g)]
  .map((match) => ({ label: match[1], links: [...match[2].matchAll(/<a href="([^"]+)"( aria-current="page")?>([^<]+)<\/a>/g)].map((link) => [link[1], link[3], link[2] !== undefined]) }));
const render = (key) => app.renderLiveReferencePage(key, { developers: DEVELOPERS });

/** The chrome table's sections the three pages say. */
const SECTIONS = /^(?:reference|help|guide|developers)\./;

// The C# the registry and the API's routes are declared in, read as `refusal-registry-parity.test.mjs` reads it: every
// extraction is checked for plausibility, so a parse that found nothing never agrees with an empty page.
const REGISTRY_SOURCE = await repository("src/Lex.V3.Contracts/Platform/V3OperationRegistry.cs");
function arrayIn(source, name) {
  const block = new RegExp(`private static readonly string\\[\\] ${name}\\s*=\\s*\\[([\\s\\S]*?)\\];`).exec(source);
  assert.ok(block, `${name} is not where this test expects it in V3OperationRegistry.cs`);
  const values = [...block[1].matchAll(/"([a-z0-9_]+)"/g)].map((match) => match[1]);
  assert.ok(values.length >= 15, `parsed ${values.length} entries of ${name}, too few to be the registry`);
  return values;
}
const REGISTERED_OPERATIONS = arrayIn(REGISTRY_SOURCE, "RequiredOperationIds");
const REGISTERED_REFUSALS = arrayIn(REGISTRY_SOURCE, "RequiredRefusalCodes");
const MANDATORY_FIELDS = (() => {
  const block = /MandatoryRefusalPayloadFields\s*=[\s\S]*?\{([\s\S]*?)\n        \};/.exec(REGISTRY_SOURCE);
  assert.ok(block, "MandatoryRefusalPayloadFields is not where this test expects it");
  return new Map([...block[1].matchAll(/\["([a-z_]+)"\]\s*=\s*\[([^\]]*)\]/g)].map((entry) => [entry[1], [...entry[2].matchAll(/"([a-z0-9_]+)"/g)].map((match) => match[1])]));
})();
/** The operations the API routes (`V3RestRouteBinding.Served`), by their ids. */
const SERVED_ROUTES = await (async () => {
  const source = await repository("src/Lex.V3.Api/V3ResolveRestRoute.cs");
  const bindings = new Map([...source.matchAll(/public static readonly V3RestRouteBinding (\w+) = new\("\/api\/v3\/([a-z_]+)", "([a-z_]+)"\);/g)].map((match) => {
    assert.equal(match[2], match[3], `${match[1]}: its path names its operation`);
    return [match[1], match[3]];
  }));
  const served = /IReadOnlyList<V3RestRouteBinding> Served = \[([^\]]+)\]/.exec(source);
  assert.ok(served, "V3RestRouteBinding.Served is not where this test expects it");
  const ids = served[1].split(",").map((name) => bindings.get(name.trim()));
  assert.ok(ids.length >= 15 && ids.every((id) => typeof id === "string"), "the served routes parse to operations");
  return ids;
})();

test("the three reference pages are built under each interface language, with the site around them, no script and no form, never indexed, their language links to the same page, and every link they hold leads somewhere", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-live-reference-"));
  try {
    await buildLive(pathToFileURL(`${destination}/`), { buildTag: "reference-test" });
    const internalLinks = new Set();
    for (const language of ["en", "fr"]) {
      const copy = liveChrome(language);
      const files = await readdir(join(destination, language));
      for (const page of REFERENCE_PAGES) {
        const label = `${language}/${page.file}`;
        assert.ok(files.includes(page.file), `${label} is built`);
        const html = await readFile(join(destination, language, page.file), "utf8");
        if (language === "en") assert.equal(html, render(page.key), `${label}: as the English bundle renders it`);
        const entry = copy[page.key];
        assert.match(html, new RegExp(`^<!doctype html>\\n<html lang="${language}" data-product-line="lex-v3" data-preview-state="${page.state}"`), `${label}: labelled ${language}`);
        assert.ok(html.includes(`<title>${escaped(entry.title)} - Lex</title>`), `${label}: its title`);
        assert.ok(html.includes(`<p class="eyebrow">${escaped(entry.eyebrow)}</p>`) && html.includes(`<h1>${escaped(entry.heading)}</h1>`) && html.includes(`<p>${escaped(entry.intro)}</p>`), `${label}: its eyebrow, heading and introduction`);
        // Readable without scripts: it ships none and asks nothing; never indexed, whatever the release's switch says (R67).
        assert.ok(!html.includes("<script") && !html.includes("<form"), `${label}: no script, no form`);
        assert.deepEqual(html.match(/<meta name="robots"[^>]*\/>/g), [NEVER_INDEXED_META], `${label}: never indexed`);
        assert.ok(!html.includes('rel="canonical"'), `${label}: names no canonical address`);
        assert.deepEqual([...html.matchAll(/<link rel="alternate" hrefLang="([^"]+)" href="([^"]+)"\/>/g)].map((match) => [match[1], match[2]]), [
          ["fr", `https://law.soufien.lu/fr/${page.file}`],
          ["en", `https://law.soufien.lu/en/${page.file}`],
          ["x-default", `https://law.soufien.lu/fr/${page.file}`],
        ], `${label}: the same page in each language`);
        const nav = html.slice(html.indexOf('data-locale-nav=""'), html.indexOf("</nav>", html.indexOf('data-locale-nav=""')));
        assert.deepEqual([...nav.matchAll(/<a href="([^"]+)" lang="([a-z]+)"/g)].map((match) => [match[2], match[1]]), [
          ["en", `/en/${page.file}`], ["fr", `/fr/${page.file}`], ["de", "/locale-de.html"], ["lb", "/locale-lb.html"],
        ], `${label}: its language links`);
        // The site around it, the footer listing the three with this one current, and no answer in its freshness strip.
        assert.ok(html.includes('data-live="lex-v3-live-mount"') && html.includes(escaped(copy.site.notAdvice)), `${label}: the site's header and footer`);
        assert.deepEqual(referenceLinks(html), [{
          label: escaped(copy.reference.menu),
          links: REFERENCE_PAGES.map((other) => [`/${language}/${other.file}`, escaped(copy[other.key].title), other.key === page.key]),
        }], `${label}: the three pages in its footer, itself current`);
        assert.equal((html.match(/aria-current="page"/g) ?? []).length, 1, `${label}: no menu entry is current but itself`);
        assert.ok(html.includes(`<p>${escaped(copy.freshness.noAnswer)}</p>`), `${label}: its strip carries no answer`);
        // Each part under its heading, in the table's order.
        const parts = page.key === "developers" ? [...app.DEVELOPERS_PARTS] : Object.keys(entry.sections);
        assert.deepEqual([...mainOf(html).matchAll(/data-reference-section="([a-z]+)"/gi)].map((match) => match[1]), page.key === "guide" ? [...parts, "glossary"] : parts, `${label}: its parts`);
        // No page of the site gives a contact: no e-mail address, no mail or telephone link, no postcode, in any bytes.
        assert.deepEqual(shownOnPage(html), [], `${label}: shows none of what the owner's decision keeps off the site`);
        for (const [, href] of mainOf(html).matchAll(/href="([^"]+)"/g)) if (href.startsWith("/")) internalLinks.add(href);
      }
    }
    // The French pages say the French table's words, and none of the English table's own words for these pages.
    const frenchTexts = entriesOf(LIVE_CHROME.fr).map(([, text]) => text);
    const englishOnly = entriesOf(LIVE_CHROME.en).filter(([path, text]) => SECTIONS.test(path)
      && text.split(/\{[A-Za-z]+\}/).some((part) => /\p{L}{4}/u.test(part)) && !frenchTexts.includes(text));
    assert.ok(englishOnly.length > 100, `most of the three pages' English differs from their French (${englishOnly.length})`);
    for (const page of REFERENCE_PAGES) {
      const text = said(await readFile(join(destination, "fr", page.file), "utf8"));
      for (const [path, entry] of englishOnly) {
        const longest = entry.split(/\{[A-Za-z]+\}/).sort((a, b) => b.length - a.length)[0].trim();
        if (longest.length >= 12) assert.ok(!text.includes(longest), `fr/${page.file} says the English ${path}: ${JSON.stringify(longest)}`);
      }
    }

    // The schemas, published beside the pages byte for byte, one file each.
    const schemas = PLATFORM_SCHEMAS;
    assert.ok(schemas.length >= 50, `the platform's schemas are read (${schemas.length})`);
    for (const schema of schemas) {
      const published = await readFile(join(destination, publishedSchemaPath(schema.file).slice(1)));
      assert.ok(published.equals(schema.bytes), `${schema.file} is published as it is`);
    }
    assert.deepEqual((await readdir(join(destination, "schemas", "v3-platform"))).sort(), schemas.map((schema) => publishedSchemaPath(schema.file).split("/").at(-1)).sort(), "and nothing else");

    // Every link the pages hold to this site leads to a page or a file the build wrote, and the live server serves it.
    assert.ok(internalLinks.size > 60, `the pages' own links are read (${internalLinks.size})`);
    const server = createLiveServer({ root: pathToFileURL(`${destination}/`), apiOrigin: "http://127.0.0.1:9" });
    const origin = await new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
    try {
      for (const href of [...internalLinks, ...REFERENCE_PAGE_PATHS].sort()) {
        const path = href.split("#")[0];
        const answer = await fetch(`${origin}${path}`, { redirect: "manual" });
        assert.equal(answer.status, 200, `${href} answers`);
        if (path.startsWith("/schemas/")) {
          assert.equal(answer.headers.get("content-type"), "application/json; charset=utf-8", `${href}: a schema, as JSON`);
          const bytes = Buffer.from(await answer.arrayBuffer());
          const schema = schemas.find((one) => publishedSchemaPath(one.file) === path);
          assert.ok(schema !== undefined && bytes.equals(schema.bytes), `${href}: the schema's bytes`);
        } else {
          assert.equal(answer.headers.get("content-type"), "text/html; charset=utf-8", `${href}: a page`);
          if (REFERENCE_PAGE_PATHS.includes(path)) assert.equal(answer.headers.get("x-robots-tag"), "noindex", `${href}: kept out of search engines`);
          await answer.arrayBuffer();
        }
      }
      // A link inside a page to one of its own parts names a part the page holds.
      for (const page of REFERENCE_PAGES) {
        const html = await readFile(join(destination, "en", page.file), "utf8");
        for (const [, id] of mainOf(html).matchAll(/href="#([^"]+)"/g)) assert.ok(html.includes(` id="${id}"`), `${page.file}#${id} names a part of the page`);
      }
    } finally {
      await new Promise((resolve) => server.close(resolve));
    }
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});

test("every live page's footer links the three reference pages, none of them current but on a reference page, and the home page links Developers and Get help", () => {
  const copy = liveChrome("en");
  const pages = {
    home: app.renderLiveHomePage(), coverage: app.renderLiveCoveragePage(), search: app.renderLiveSearchPage(), dossier: app.renderLiveDossierPage(),
    reading: app.renderLiveReadingPage(), history: app.renderLiveHistoryPage(), compare: app.renderLiveComparePage(), radar: app.renderLiveRadarPage(),
    export: app.renderLiveExportPage(), notFound: app.renderLiveNotFoundPage(), maintenance: app.renderLiveMaintenancePage(),
    ...Object.fromEntries(["legalNotice", "privacy", "terms", "accessibility", "cookies"].map((key) => [key, app.renderLiveLegalPage(key)])),
  };
  for (const [name, html] of Object.entries(pages)) {
    assert.deepEqual(referenceLinks(html), [{ label: copy.reference.menu, links: REFERENCE_PAGES.map((page) => [`/en/${page.file}`, escaped(copy[page.key].title), false]) }], name);
  }
  const links = /<nav aria-label="[^"]*" class="home-links" data-home-links="">([\s\S]*?)<\/nav>/.exec(pages.home);
  assert.deepEqual([...links[1].matchAll(/<a href="([^"]+)">([^<]+)<\/a>/g)].map(([, href, text]) => [href, text]), [
    ["/en/radar.html", copy.site.radar],
    ["/en/coverage.html", copy.coverage.title],
    ["/en/developers.html", copy.developers.title],
    ["/en/help.html", copy.help.title],
  ], "the specification's list: Changes, Coverage and trust, Developers and Get help");
});

test("the Developers page is the registry's: every operation, refusal code, verdict and envelope member, in order, each with its words in both languages", () => {
  // The census's contract block is the C# registry's, as declared.
  const contract = census.contract;
  assert.deepEqual(contract.operations.map((operation) => operation.operation_id), REGISTERED_OPERATIONS, "the census names the registry's operations");
  assert.deepEqual(contract.refusals.map((refusal) => refusal.code), REGISTERED_REFUSALS, "and its refusal codes");
  for (const refusal of contract.refusals) assert.deepEqual([...refusal.mandatory_payload_fields].sort(), [...MANDATORY_FIELDS.get(refusal.code)].sort(), `${refusal.code}: the fields its payload must carry`);
  for (const entry of census.envelopes) assert.equal(entry.envelope.registry_sha256, contract.registry_sha256, `${entry.operation}: every envelope names the registry's digest`);

  // Each operation, code, verdict and member has its words, in each language, and none is said that the contracts lack.
  const envelopeSchema = JSON.parse(JSON.stringify(DEVELOPERS.envelope));
  for (const language of ["en", "fr"]) {
    const copy = LIVE_CHROME[language].developers;
    assert.deepEqual(Object.keys(copy.operations.descriptions).sort(), [...REGISTERED_OPERATIONS].sort(), `${language}: one description per registered operation`);
    assert.deepEqual(Object.keys(copy.refusals.codes).sort(), [...REGISTERED_REFUSALS].sort(), `${language}: one sentence per refusal code`);
    assert.deepEqual(Object.keys(copy.envelope.verdicts), contract.verdicts, `${language}: one sentence per verdict, in the registry's order`);
    assert.deepEqual(Object.keys(copy.envelope.members), envelopeSchema.members, `${language}: one sentence per envelope member, in the schema's order`);
    assert.deepEqual(Object.keys(copy.envelope.context).sort(), envelopeSchema.contextMembers, `${language}: one sentence per context member`);
  }

  // The page as rendered lists them all, in the contracts' order, and says each one's facts from them.
  const html = render("developers");
  const main = mainOf(html);
  assert.deepEqual([...main.matchAll(/data-operation="([a-z_]+)" data-served="(true|false)"/g)].map((match) => [match[1], match[2] === "true"]),
    REGISTERED_OPERATIONS.map((id) => [id, SERVED_ROUTES.includes(id)]), "every registered operation, served exactly when the API routes it");
  const coverage = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope.result.value.operations;
  assert.deepEqual([...coverage.served_operations].sort(), [...SERVED_ROUTES].sort(), "the coverage answer serves the API's routes");
  for (const operation of DEVELOPERS.operations) {
    const section = main.slice(main.indexOf(`data-operation="${operation.id}"`), main.indexOf("</section>", main.indexOf(`data-operation="${operation.id}"`)));
    assert.ok(section.includes(`<code>POST /api/v3/${operation.id}</code>`), `${operation.id}: its REST path`);
    assert.ok(section.includes(escaped(liveChrome("en").developers.operations.descriptions[operation.id])), `${operation.id}: its description`);
    assert.ok(section.includes(`<dd><code>${operation.id}</code></dd>`), `${operation.id}: an MCP tool, served or not (every registered operation is routed)`);
    const request = JSON.parse(readFileSyncOf(`schemas/v3-platform/${operation.id.replaceAll("_", "-")}-request.schema.json`)).properties.parameters;
    assert.deepEqual([...section.matchAll(/data-parameter="([a-z_]+)" data-required="(true|false)"/g)].map((match) => [match[1], match[2] === "true"]),
      Object.keys(request.properties ?? {}).map((name) => [name, (request.required ?? []).includes(name)]), `${operation.id}: its parameters, as its request schema states them`);
    for (const schema of [operation.requestSchema, operation.resultSchema]) {
      assert.equal(schema.sha256, createHash("sha256").update(readFileSyncOf(`schemas/v3-platform/${schema.file}`, null)).digest("hex"), `${schema.id}: its digest`);
      assert.ok(section.includes(`<a href="${schema.path}" data-schema="${schema.id}">`) && section.includes(`<code>${schema.sha256}</code>`), `${operation.id}: links ${schema.id} with its digest`);
    }
    if (!operation.served) {
      const row = coverage.not_served_data.find((one) => one.operation === operation.id);
      assert.ok(section.includes(escaped(row.data_needed)), `${operation.id}: the data that would serve it`);
      // The typed refusal every request for it answers inside the envelope (the plan's item 8.6), and the mode it names.
      assert.equal(row.reason, "retrieval_mode_unavailable", `${operation.id}: its typed reason`);
      assert.ok(section.includes(`data-refusal-code="${row.reason}"`) && section.includes(`<code>${row.reason}</code>`) && section.includes(`<code>${row.requested_mode}</code>`), `${operation.id}: its refusal and its mode`);
    }
  }
  assert.deepEqual([...main.matchAll(/data-refusal="([a-z_]+)"/g)].map((match) => match[1]), REGISTERED_REFUSALS, "every refusal code, in the registry's order");
  for (const refusal of contract.refusals) {
    const row = main.slice(main.indexOf(`data-refusal="${refusal.code}"`), main.indexOf("</div>", main.indexOf(`data-refusal="${refusal.code}"`)));
    for (const field of refusal.mandatory_payload_fields) assert.ok(row.includes(`<code>${field}</code>`), `${refusal.code}: names ${field}`);
    // Its payload's schema (the plan's item 6.10), published, linked with its digest, which the registry names.
    const file = `${refusal.code.replaceAll("_", "-")}-payload.schema.json`;
    assert.equal(refusal.payload_schema, `lex-v3-${refusal.code}-payload/1`, `${refusal.code}: its payload schema's identity`);
    assert.equal(refusal.payload_schema_sha256, createHash("sha256").update(readFileSyncOf(`schemas/v3-platform/${file}`, null)).digest("hex"), `${refusal.code}: its payload schema's digest`);
    assert.deepEqual(JSON.parse(readFileSyncOf(`schemas/v3-platform/${file}`)).required, refusal.mandatory_payload_fields, `${refusal.code}: the schema requires the registry's fields`);
    assert.ok(row.includes(`<a href="${publishedSchemaPath(file)}" data-schema="${refusal.payload_schema}">`) && row.includes(`<code>${refusal.payload_schema_sha256}</code>`), `${refusal.code}: links its payload schema with its digest`);
  }
  assert.deepEqual(MANDATORY_FIELDS.get("out_of_corpus_scope"), ["requested_identifier", "official_source", "reason", "who_to_ask"], "out_of_corpus_scope carries its four elements (36-ideal-evaluation.md §4)");

  // What Lex never holds: the census's out-of-scope list, each class with its official place and who to ask, an institution.
  const list = contract.out_of_scope;
  assert.equal(list.schema, "lex-v3-out-of-scope/1");
  assert.ok(list.classes.length >= 4, `the list's classes are read (${list.classes.length})`);
  assert.deepEqual([...main.matchAll(/data-out-of-scope-class="([a-z_]+)"/g)].map((match) => match[1]), list.classes.map((entry) => entry.class), "every class of the list, in its order");
  assert.ok(main.includes(`<code>${list.sha256}</code>`), "the list's digest");
  for (const entry of list.classes) {
    const item = main.slice(main.indexOf(`data-out-of-scope-class="${entry.class}"`), main.indexOf("</li>", main.indexOf(`data-out-of-scope-class="${entry.class}"`)));
    assert.ok(item.includes(escaped(entry.instrument)) && item.includes(escaped(entry.reason)), `${entry.class}: the instrument and the reason`);
    assert.ok(item.includes(`<a href="${entry.official_source}" rel="external">`) && item.includes(`<a href="${entry.who_to_ask.link}" rel="external">${escaped(entry.who_to_ask.name)}</a>`), `${entry.class}: its official place and who to ask, linked`);
    for (const example of entry.examples) assert.ok(item.includes(`<code>${escaped(example)}</code>`), `${entry.class}: ${example}`);
    assert.equal(entry.who_to_ask.role, "information", `${entry.class}: who to ask is the institution that publishes it`);
    assert.match(entry.who_to_ask.name, /\([A-Z]{3,4}\)$/, `${entry.class}: an institution, by its official name`);
  }
  // The card's link policy for an out-of-scope refusal names exactly the list's hosts.
  assert.deepEqual([...new Set(list.classes.flatMap((entry) => [new URL(entry.official_source).host, new URL(entry.who_to_ask.link).host]))].sort(), [...OUT_OF_SCOPE_HOSTS].sort(), "OUT_OF_SCOPE_HOSTS is the list's hosts");
  assert.deepEqual([...main.matchAll(/data-verdict="([a-z_]+)"/g)].map((match) => match[1]), contract.verdicts, "every verdict");
  assert.deepEqual([...main.matchAll(/data-member="([a-z0-9_]+)"/g)].map((match) => match[1]), DEVELOPERS.envelope.members, "every envelope member");
  assert.ok(main.includes(`data-registry-sha256="${contract.registry_sha256}"`) && main.includes(`<code>${contract.registry_sha256}</code>`), "the registry's digest");
  // The events' names are the events request schema's, and the delivery is the platform's own statement, marked as sent.
  const events = JSON.parse(readFileSyncOf("schemas/v3-platform/events-request.schema.json")).properties.parameters.properties;
  for (const name of events.event.enum) assert.ok(main.includes(`<code>${name}</code>`), `the event ${name}`);
  assert.ok(main.includes(escaped(answers.sampled.find((entry) => entry.operation === "events").answer.delivery)), "the events answer's own delivery statement");

  // An operation the registry gains without the page's words fails the page, and so does a refusal code: the page never
  // lists an operation it cannot describe, and never leaves one out.
  const schemas = DEVELOPERS_SCHEMAS;
  const grown = structuredClone(census);
  grown.contract.operations.push({ operation_id: "made_up", result_schema: "lex-v3-coverage-result/1", result_object_types: ["coverage_report"] });
  assert.throws(() => developersModel({ census: grown, answers, schemas }), /made_up/, "an operation with no schema of its own and no route the coverage answer names");
  const model = developersModel({ census, answers, schemas });
  model.operations.push({ ...model.operations.find((one) => one.id === "coverage"), id: "made_up" });
  assert.throws(() => app.renderLiveReferencePage("developers", { developers: model }), /made_up has no description/, "an operation the page has no words for");
  const coded = developersModel({ census, answers, schemas });
  coded.refusals.push({ code: "made_up_refusal", fields: [] });
  assert.throws(() => app.renderLiveReferencePage("developers", { developers: coded }), /made_up_refusal has no description/, "a refusal code the page has no words for");
  assert.throws(() => app.renderLiveReferencePage("developers"), /generated from the contracts/, "and no page at all without the contracts");
});

test("the facts the Developers page takes from the API's code are that code's", async () => {
  const mcp = await repository("src/Lex.V3.Api/V3McpJsonRpc.cs");
  assert.equal(/public const string ProtocolVersion = "([^"]+)";/.exec(mcp)?.[1], MCP_PROTOCOL_VERSION, "the MCP protocol version");
  const dispatch = /return method switch\s*\{([\s\S]*?)_ => Error/.exec(mcp);
  assert.ok(dispatch, "the MCP dispatch is where this test expects it");
  assert.deepEqual([...dispatch[1].matchAll(/"([a-z/]+)" =>/g)].map((match) => match[1]), MCP_METHODS, "the MCP methods answered");
  assert.ok(MCP_METHODS.includes("ping"), "ping (the plan's item 8.5)");
  // The server's instructions, word for word (the plan's item 8.5), as the page quotes them.
  const instructions = /internal const string Instructions =([\s\S]*?);\n/.exec(mcp);
  assert.ok(instructions, "the MCP instructions are where this test expects them");
  assert.equal([...instructions[1].matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((match) => match[1]).join(""), MCP_INSTRUCTIONS, "the MCP instructions");
  assert.ok(render("developers").includes(escaped(MCP_INSTRUCTIONS)), "and the page quotes them");
  // The replay guarantees are held by the tests the page names, each a test method of the class it names.
  const replay = await repository(REPLAY_TESTS);
  assert.deepEqual(REPLAY_GUARANTEES.map((guarantee) => guarantee.id), ["G1", "G2", "G3", "G4", "G5"]);
  for (const guarantee of REPLAY_GUARANTEES) {
    assert.match(replay, new RegExp(`\\[TestMethod\\]\\s*public async Task ${guarantee.test}\\(`), `${guarantee.id}: ${guarantee.test} is a test of ${REPLAY_TESTS}`);
    assert.ok(render("developers").includes(`data-guarantee="${guarantee.id}"`) && render("developers").includes(`<code>${guarantee.test}</code>`), `${guarantee.id}: on the page, with its test`);
  }
  const host = await repository("src/Lex.V3.Api/V3PlatformHost.cs");
  const ceiling = /internal const int MaximumRequestBytes = ([0-9 *]+);/.exec(host)?.[1];
  assert.ok(ceiling !== undefined, "the request ceiling is where this test expects it");
  assert.equal(ceiling.split("*").reduce((product, factor) => product * Number(factor.trim()), 1), MAXIMUM_REQUEST_BYTES, "the request body's ceiling");
  const transport = await repository("src/Lex.V3.Api/V3TransportResponse.cs");
  assert.equal(/public const string Schema = "([^"]+)";/.exec(transport)?.[1], TRANSPORT_PROBLEM_SCHEMA, "the problem document's schema");
  const problems = [...transport.matchAll(/V3TransportFailureKind\.\w+ =>\s*\("([a-z_]+)", "([^"]+)", StatusCodes\.Status(\d{3})\w*\)/g)]
    .map((match) => ({ code: match[1], title: match[2], status: Number(match[3]) }));
  assert.ok(problems.length >= 10, `the transport problems parse (${problems.length})`);
  assert.deepEqual(TRANSPORT_PROBLEMS.map((problem) => ({ ...problem })), problems, "every failure below the envelope, its title and its status");
  const html = render("developers");
  for (const problem of TRANSPORT_PROBLEMS) assert.ok(html.includes(`data-transport-problem="${problem.code}"`), problem.code);
  assert.ok(html.includes(`data-request-bytes="${MAXIMUM_REQUEST_BYTES}"`), "the ceiling, as the page says it");
  // The per-client limit (R109, R46), as the server enforces it, and the key set's address (R116).
  const limiter = await repository("src/Lex.V3.Api/V3ClientLimiter.cs");
  for (const [name, value] of [["ApiPerMinute", CLIENT_LIMIT.apiPerMinute], ["ApiInFlight", CLIENT_LIMIT.apiInFlight], ["RendersPerMinute", CLIENT_LIMIT.rendersPerMinute]]) {
    assert.equal(Number(new RegExp(`internal const int ${name} = (\\d+);`).exec(limiter)?.[1]), value, `the limit's ${name}`);
  }
  const route = await repository("src/Lex.V3.Api/V3ResolveRestRoute.cs");
  assert.equal(Number(/StatusCodes\.Status(\d{3})TooManyRequests/.exec(route)?.[1]), CLIENT_LIMIT.status, "the status REST answers rate_limited with");
  const keySet = await repository("src/Lex.V3.Api/V3KeySet.cs");
  assert.equal(/internal const string RawTarget = "([^"]+)";/.exec(keySet)?.[1], KEY_SET_PATH, "the key set's address");
  assert.ok(html.includes(`data-client-limit="${CLIENT_LIMIT.apiPerMinute}/${CLIENT_LIMIT.apiInFlight}/${CLIENT_LIMIT.status}"`), "the API's limit, as the page says it");
  assert.ok(html.includes(`data-render-limit="${CLIENT_LIMIT.rendersPerMinute}"`), "the pages' budget, as the page says it");
  assert.ok(html.includes(`data-key-set="${KEY_SET_PATH}"`), "the key set, named");
  for (const language of ["en", "fr"]) {
    assert.doesNotMatch(LIVE_CHROME[language].developers.limits.rate, /no per-client|aucune limite de débit/, `${language}: the page no longer says there is no limit`);
  }
});

test("the Developers page's live examples are the served release's own answer: rendered from browse for the first act it holds, and as built, none", () => {
  // Both servers render the page from their answer to browse for the first act held (the plan's item 8.4: "live examples from
  // the release's own data"), as the site's table says (`sitePageFor`, `V3WebRoot.SitePageFor`).
  for (const language of ["en", "fr"]) {
    assert.deepEqual(JSON.parse(JSON.stringify(sitePageFor(`/${language}/developers.html`))), { language, kind: "developers", operation: LIVE_EXAMPLE_REQUEST.operation_id, parameters: LIVE_EXAMPLE_REQUEST.parameters });
  }
  const browse = census.envelopes.find((entry) => entry.operation === "browse" && entry.envelope.result !== null).envelope;
  const work = browse.result.value.works[0];
  const live = mainOf(app.renderLiveReferencePage("developers", { developers: DEVELOPERS, live: browse }));
  assert.ok(live.includes('data-live-examples="live"'), "rendered from an answer");
  assert.ok(live.includes(escaped(liveAnswerText(browse))), "the answer, whole");
  assert.ok(live.includes(escaped(JSON.stringify(LIVE_EXAMPLE_REQUEST))), "the request it answers");
  assert.ok(live.includes(`<code>${work.identifier}</code>`) && live.includes(`<code>${work.latest_applicability_date}</code>`), "the first act held and its latest version's date");
  assert.ok(live.includes(escaped(`"identifier":"${work.identifier}"`)) && live.includes(escaped(`"name": "as_of"`)), "the next requests ask about it");
  const built = mainOf(render("developers"));
  assert.ok(built.includes('data-live-examples="built"') && !built.includes('data-live-examples="live"'), "as built, none");
  // A refusal is shown as the answer it is.
  const refused = structuredClone(census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.refusal !== null).envelope);
  refused.operation_id = "browse";
  const refusedPage = mainOf(app.renderLiveReferencePage("developers", { developers: DEVELOPERS, live: refused }));
  assert.ok(refusedPage.includes(escaped(liveAnswerText(refused))) && refusedPage.includes(escaped(LIVE_CHROME.en.developers.examples.refused)), "a refusal, as an answer");
  assert.throws(() => app.renderLiveReferencePage("developers", { developers: DEVELOPERS, live: census.envelopes[0].envelope }), /drawn from a browse answer/, "and from browse only");
  assert.throws(() => app.renderLiveReferencePage("help", { live: browse }), /only the Developers page/, "no other reference page is rendered from an answer");
});

test("the glossary's terms are the site's own words, in each language, as the pages that print them say them", () => {
  for (const language of ["en", "fr"]) {
    const table = LIVE_CHROME[language];
    assert.deepEqual(Object.keys(table.guide.glossary.terms).sort(), GLOSSARY.map((row) => row.key).sort(), `${language}: one definition per term, and no definition without its term`);
    for (const row of GLOSSARY) {
      assert.equal(typeof entryAt(table, row.term), "string", `${language} ${row.term}: an entry of the table`);
      const term = glossaryTerm(table, row.term);
      assert.ok(entryAt(table, row.term).startsWith(term), `${language} ${row.key}: the entry's own words`);
    }
    const rows = glossaryRows(table, language);
    const collator = new Intl.Collator(language, { sensitivity: "base" });
    assert.deepEqual(rows.map((row) => row.term), [...rows.map((row) => row.term)].sort(collator.compare), `${language}: in the order of the terms`);
  }
  // A template's term is its words before its first value.
  assert.equal(glossaryTerm(LIVE_CHROME.en, "citation.evidence.textSha256"), "Text digest (SHA-256)");
  assert.equal(glossaryTerm(LIVE_CHROME.fr, "form.today"), "Aujourd’hui au Luxembourg");
  assert.throws(() => glossaryTerm(LIVE_CHROME.en, "guide.nothing"), /no entry/);
  assert.throws(() => glossaryTerm({ x: "{date} applies" }, "x"), /no words/);
  // As the page prints them: each term in its own words, in the page's language.
  const html = app.renderLiveGuidePage();
  for (const row of glossaryRows(LIVE_CHROME.en, "en")) assert.ok(html.includes(`<div id="term-${row.key}" data-term="${row.key}"><dt>${escaped(row.term)}</dt><dd>${escaped(row.definition)}</dd></div>`), row.key);
});

test("R21's time-word guard holds over every word of the three pages, in both languages, as written and as rendered", async () => {
  let checked = 0;
  for (const language of ["en", "fr"]) {
    for (const [path, text] of entriesOf(LIVE_CHROME[language]).filter(([one]) => SECTIONS.test(one))) {
      assertTimeVocabulary(text, `${language} ${path}`);
      checked += 1;
    }
  }
  assert.ok(checked > 300, `the three pages' words are read (${checked})`);
  for (const page of REFERENCE_PAGES) assertTimeVocabulary(said(render(page.key)), `${page.file} as rendered`);
  // And the guard is not idle on these pages: the words it refuses are refused here too.
  assert.throws(() => assertTimeVocabulary(`${LIVE_CHROME.en.guide.sections.clocks.never} The version in force from that date.`, "a test"), /R21 never says/);
});

test("Get help says there is no contact and gives none, in both languages (the owner's decision of 2026-10-19)", () => {
  assert.equal(SHOWN_NOWHERE.length, 4);
  for (const language of ["en", "fr"]) {
    const contact = LIVE_CHROME[language].help.sections.contact;
    assert.ok(Object.keys(contact).every((key) => ["heading", "text", "complaint"].includes(key)), `${language}: the part says, and offers nothing`);
    for (const [path, text] of entriesOf(LIVE_CHROME[language]).filter(([one]) => SECTIONS.test(one))) {
      assert.deepEqual(shownOnPage(text), [], `${language} ${path}`);
      for (const [, name] of text.matchAll(/\{([A-Za-z]+)\}/g)) assert.doesNotMatch(name, /mail|phone|address|contact|form/i, `${language} ${path} names {${name}}`);
    }
  }
  const html = app.renderLiveHelpPage();
  assert.ok(said(html).includes(LIVE_CHROME.en.help.sections.contact.text), "says there is no contact");
  assert.ok(!html.includes("mailto:") && !html.includes("tel:") && !/<form/.test(html), "and gives none");
});

test("every count the Developers page says is the contracts'", () => {
  const html = render("developers");
  const summary = /<p data-operations-count="(\d+)" data-served-count="(\d+)">([^<]+)<\/p>/.exec(html.replaceAll("<!-- -->", ""));
  assert.ok(summary !== null, "the operations' summary");
  assert.deepEqual([Number(summary[1]), Number(summary[2])], [REGISTERED_OPERATIONS.length, SERVED_ROUTES.length], "the registry's and the routes' counts");
  assert.deepEqual(summary[3].match(/\d+/g), [String(REGISTERED_OPERATIONS.length), String(SERVED_ROUTES.length)], "and no other number");
  assert.match(html, new RegExp(`data-refusals-count="${REGISTERED_REFUSALS.length}"`), "the refusal codes' count");
  // Served with other counts, the page says those: nothing is written into its words.
  const fewer = structuredClone(DEVELOPERS);
  fewer.operations = fewer.operations.slice(0, 3);
  fewer.served = 2;
  assert.match(app.renderLiveReferencePage("developers", { developers: fewer }).replaceAll("<!-- -->", ""), /data-operations-count="3" data-served-count="2">The registry lists 3 operations; this server serves 2 of them\./);
});

test("the forms the French review pinned say what the platform does, and the French keeps the review's words (the addendum of 2026-10-27)", async () => {
  const [EN, FR] = [LIVE_CHROME.en, LIVE_CHROME.fr];
  // A Luxembourg version covers [from, to): the end the publisher states is the first day it no longer covers.
  const intervals = await repository("src/Lex.V3.Api/V3CorpusMount.Intervals.cs");
  assert.ok(intervals.includes("<c>[from, to)</c>") && intervals.includes("SameWording"), "the interval rule and the same-text choice, where the review read them");
  assert.ok(EN.guide.sections.clocks.luxembourg.includes("until the day before the end date Legilux gives it"), "the English says the end day");
  assert.ok(FR.guide.sections.clocks.luxembourg.includes("jusqu’à la veille de la date de fin que Legilux lui donne"), "and the French");
  // Several versions held with one text: the platform serves one of them (`SameWording`), so Lex chooses only then.
  assert.ok(EN.help.sections.not.list.choose.includes("and their texts differ") && FR.help.sections.not.list.choose.includes("et que leurs textes diffèrent"));
  assert.ok(EN.guide.sections.ambiguity.text.includes("When their texts differ") && FR.guide.sections.ambiguity.text.includes("Lorsque leurs textes diffèrent"));
  // Another method at a REST route answers a failure below the envelope (`RequireClaimedRequest`), never silence.
  const route = await repository("src/Lex.V3.Api/V3ResolveRestRoute.cs");
  assert.ok(route.includes("V3TransportFailureKind.MethodNotAllowed") && route.includes("V3TransportFailureKind.UnknownRoute"));
  assert.ok(EN.developers.access.rest.includes("any other method gets only a failure below the envelope") && FR.developers.access.rest.includes("toute autre méthode ne reçoit qu’un échec sous l’enveloppe"));
  // The counted refusal sentence reads right for 0 and 1 in French and for 1 in English.
  for (const [language, count] of [["fr", 0], ["fr", 1], ["en", 1], ["en", 20]]) {
    const said = fillCounted(LIVE_CHROME[language].developers.refusals.text, count, { refuse: "refuse", schema: "lex-v3-refusal/1" }, language);
    assert.doesNotMatch(said, /l’un des \d+ code\b|one of \d+ code\b/, `${language} ${count}`);
    assert.ok(said.includes(String(count)), `${language} ${count}`);
  }
  // The glossary's French definition of a plural term agrees with it.
  assert.equal(glossaryTerm(FR, "export.withoutTextHeading"), "Détenus sans texte");
  assert.match(FR.guide.glossary.terms.withoutText, /^Se dit d’articles .* Ils sont listés à part/);
  // Words the review refused in these pages' French: Lex's texts are never "sur Lex", the MCP transport is never "HTTP en
  // flux", this release is never "version du site", a text "sert de réponse pour" a date, the site "connaît une
  // défaillance", and a catalogue gap is an "intervalle sans version" ("vide" is kept for "vide juridique").
  const refused = [/\bsur Lex\b/, /HTTP en flux/, /version du site/, /répond à la date/, /est en défaut/, /(?:\bun|\ble|\bce|’un) vide(?! juridique)/, /^Vides$/, /conseil de votre choix/, /aucun conseil\b/];
  for (const [path, text] of entriesOf(FR).filter(([one]) => SECTIONS.test(one))) {
    for (const pattern of refused) assert.doesNotMatch(text, pattern, `fr ${path}`);
  }
});

test("the Developers page says the per-client limit and the key set in the review's words (R46, R109, R116; the addendum of 2026-10-27)", () => {
  // The French of the review's addendum of 2026-10-27 on the Developers page's limits and the key set, byte for byte, with the
  // English it recommended (a no-break space before ":" and ";").
  const french = {
    "limits.anonymous": "Il n’y a ni clé ni compte, et le serveur ne garde aucune trace écrite d’une requête ; pour la limite par client ci-dessous, il compte les requêtes dans sa seule mémoire vive : voir la {privacyPage}.",
    "limits.rate": "Chaque client, reconnu à son adresse IP, peut envoyer {perMinute} requêtes par minute à l’API, REST et MCP confondus, dont au plus {atOnce} traitées en même temps. Au-delà de l’une ou l’autre limite, une requête adressée à une opération (en MCP, un appel d’outil) est refusée avec {code}, qui porte {retryAfter}, le nombre de secondes à attendre avant de demander de nouveau, comme le fait l’en-tête {retryAfterHeader} ; en REST, son statut est {status}. Le service est de petite taille : envoyez une requête à la fois et interrogez avec modération.",
    "limits.pages": "Les pages que le serveur produit lui-même ont un budget distinct, de {renders} par minute pour chaque client ; au-delà, une page est envoyée telle qu’elle a été construite, et le navigateur demande alors sa réponse à l’API. Aucune page n’est refusée.",
    "citing.keys": "Chaque mise en production de Lex est signée. Les clés qui les signent, chacune avec son identité, son rôle, sa validité et son statut, sont publiées à l’adresse {keySet} : vérifiez les signatures d’une mise en production au regard de cet ensemble, jamais au seul regard de la clé qu’elle contient.",
    "access.same": "Les deux donnent la même enveloppe pour la même requête : un appel d’outil est converti en document de requête REST et traité par le même code, et son résultat porte l’enveloppe comme contenu structuré. Un refus est une réponse, non une erreur : REST l’envoie comme une réponse HTTP réussie, et MCP comme un résultat d’outil avec {isError} ; seul le refus d’une requête au-delà de la limite de son client a un autre statut en REST, indiqué ci-dessous.",
  };
  const english = {
    "limits.anonymous": "There is no key and no account, and the server keeps no record of a request; for the per-client limit below, it counts requests in its working memory alone: see the {privacyPage}.",
    "limits.rate": "Each client, known by its IP address, may send {perMinute} API requests a minute, REST and MCP together, with at most {atOnce} being answered at once. Past either limit, a request to an operation (over MCP, a tool call) is refused with {code}, which carries {retryAfter}, the seconds to wait before asking again, as the {retryAfterHeader} header does; over REST its status is {status}. The service is small: send one request at a time and poll gently.",
    "limits.pages": "The pages the server renders itself have a budget of their own, {renders} a minute for each client; past it, a page is sent as built, and the browser then asks the API for its answer. No page is refused.",
    "citing.keys": "Each release of Lex is signed. The keys that sign releases, each with its identity, role, validity and status, are published at {keySet}: check a release's signatures against that set, never only against the key the release carries.",
    "access.same": "Both give the same envelope for the same request: a tool call is turned into the REST request document and answered by the same code, and its result carries the envelope as its structured content. A refusal is an answer, not an error: REST sends it as a successful HTTP answer, and MCP as a tool result with {isError}; only the refusal of a request past its client's limit has another status over REST, given below.",
  };
  const at = (table, path) => path.split(".").reduce((node, key) => node[key], table.developers);
  for (const [path, text] of Object.entries(french)) {
    assert.equal(at(LIVE_CHROME.fr, path), text, `fr developers.${path}`);
    assert.equal(at(LIVE_CHROME.en, path), english[path], `en developers.${path}`);
    // A release of Lex is a "mise en production" (on this page "publication" is a law's); a client is known "à" its IP
    // address, and a refusal is "avec" its code (the addendum's G22).
    assert.doesNotMatch(text, /publication|reconnu par|refusée par/, `fr developers.${path}`);
  }
  assert.deepEqual(Object.keys(LIVE_CHROME.en.developers.limits), ["heading", "anonymous", "size", "paging", "rate", "pages", "availability"]);
  assert.deepEqual(Object.keys(LIVE_CHROME.fr.developers.limits), Object.keys(LIVE_CHROME.en.developers.limits));
});

test("the forms the French review of the API's gaps pinned say what the platform does (the addendum of 2026-10-27, the API's gaps)", async () => {
  const [EN, FR] = [LIVE_CHROME.en, LIVE_CHROME.fr];
  // G1 and G3: an earlier version verifies only while a kept build holds its text (`VerifyScope`); a referenced build is one
  // its caller lists (`V3GenerationRetention.Referenced`), never one a permalink names by itself.
  const mount = await repository("src/Lex.V3.Api/V3CorpusMount.cs");
  assert.ok(mount.includes("unless a retained generation holds that state"), "VerifyScope, where the review read it");
  for (const key of ["G1", "G3"]) {
    assert.match(EN.developers.replay.guarantees[key].text, /while a kept build holds its text/, `en ${key}`);
    assert.match(FR.developers.replay.guarantees[key].text, /tant qu’une construction conservée en détient le texte/, `fr ${key}`);
  }
  const retention = await repository("src/Lex.V3.Ingest/V3GenerationRetention.cs");
  assert.ok(retention.includes('public const string Referenced = "referenced";') && retention.includes("Nothing records which permalinks or bundles were published"));
  assert.ok(EN.developers.replay.guarantees.G3.text.includes("every build listed as referenced by a published permalink"));
  assert.equal(EN.developers.replay.guarantees.G3.name, "Nothing is deleted from the log");
  // G1: a replaced file is recorded whether or not its text changed (the replay test's two file_replaced events).
  assert.ok(EN.developers.replay.guarantees.G1.text.includes("the version stays the same when the text is unchanged"));
  // G5: the bundle carries the row facts since S7a, so the version's own digest is recomputed from the answer.
  assert.ok(mount.includes("caller recomputes the digest from this answer"), "ProvenanceDerivationNine, where the review read it");
  assert.ok(EN.developers.replay.guarantees.G5.text.includes("and the version's own digest") && !/does not carry yet/.test(EN.developers.replay.guarantees.G5.text));
  // Entry 3: a request to an operation served from no data is refused out of scope first, and with no mount no_corpus_mounted,
  // so the sentence says "a request", never "every request" (`V3CorpusScopeMountTests`).
  assert.ok(EN.developers.operations.notServed.startsWith("Not served from data: a request to it"));
  assert.ok(FR.developers.operations.notServed.startsWith("Opération non servie faute de données : une requête qui la vise"));
  // The words the review refused in these forms.
  const refused = [/servies? sans données/, /refusée?s? par \{/, /qui en prend/, /sans eux/, /sa fiche en REST/, /ignore les membres/, /lié à côté/, /population de l’UE/, /comme celle de \{example\}/];
  for (const [path, text] of [...entriesOf(FR.developers).map(([one, value]) => [`developers.${one}`, value]), ["coverageAnswer.facts.notRouted", FR.coverageAnswer.facts.notRouted], ["coverageAnswer.captions.notServedData", FR.coverageAnswer.captions.notServedData]]) {
    for (const pattern of refused) assert.doesNotMatch(text, pattern, `fr ${path}`);
  }
});

test("G2 names what moves with the moment of the answer, as its replay test holds it, and the change policy points to the out-of-scope list's own digest (the review of #998, F1 and F2)", async () => {
  const [EN, FR] = [LIVE_CHROME.en, LIVE_CHROME.fr];
  // F1: the members G2 names are the ones `V3ReplayGuaranteesTests` finds moving a month later, and no other.
  const replay = await repository(REPLAY_TESTS);
  for (const member of [
    "context.freshness.channels[0].age_days", "context.freshness.channels[0].stale", "context.freshness.observed_at", "context.freshness.stale",
    "request_ref", "result.value.reference.recognised.date.date", "result.value.reference.opens.state_date",
    "result.value.reference.opens.address", "result.value.reference.opens.address_kind",
  ]) {
    assert.ok(replay.includes(`"${member}"`), `${REPLAY_TESTS} holds ${member}`);
  }
  for (const key of ["servingCommit", "observedAt", "requestRef", "ageDays", "channelStale", "stale", "recognisedDate", "rateLimited"]) {
    assert.ok(EN.developers.replay.guarantees.G2.text.includes(`{${key}}`), `en G2 names {${key}}`);
    assert.ok(FR.developers.replay.guarantees.G2.text.includes(`{${key}}`), `fr G2 names {${key}}`);
  }
  assert.doesNotMatch(EN.developers.replay.guarantees.G2.text, /the two members/);
  // An undated reference is read at the search's date when the search names one, and at today only when it names none
  // either (`ExactReferenceRule`; the French review of #998's repair, F12); the French says "révision du code", never
  // "commit", and never "date donnée" beside "données" (G26).
  assert.match(EN.developers.replay.guarantees.G2.text, /today in Luxembourg \(\{recognisedDate\}\) if the search names none either/);
  assert.match(FR.developers.replay.guarantees.G2.text, /si la recherche n\u2019en indique pas non plus/);
  for (const text of [FR.developers.replay.guarantees.G2.text, FR.developers.changes.digest]) {
    assert.doesNotMatch(text, /\bcommit\b|date donnée|conserve l\u2019empreinte/);
  }
  // F2: the registry's digest moves with an operation's definition, not with a fix to how it behaves; the out-of-scope list
  // is not in the registry, and every out_of_corpus_scope refusal names its own identity and digest.
  assert.ok(EN.developers.changes.digest.startsWith("Any change to an operation's definition,"));
  assert.match(EN.developers.changes.digest, /that leaves its definition as it is keeps the digest/);
  for (const [language, table] of [["en", EN], ["fr", FR]]) {
    for (const key of ["code", "scopeList", "scopeListDigest"]) assert.ok(table.developers.changes.digest.includes(`{${key}}`), `${language} digest names {${key}}`);
  }
  const scope = await repository("src/Lex.V3.Api/V3CorpusMount.Scope.cs");
  assert.ok(scope.includes("scope_list = V3OutOfScope.Schema") && scope.includes("scope_list_sha256 = V3OutOfScope.Sha256"), "the refusal names the list and its digest");
  const page = mainOf(render("developers"));
  for (const member of ["context.freshness.channels[].age_days", "result.value.reference.recognised.date", "context.commits.serving_runtime_source_commit", "scope_list_sha256"]) {
    assert.ok(page.includes(`<code>${member}</code>`), `the page shows ${member}`);
  }
});

test("the Developers page claims no more than the server keeps or refuses: G3 re-verifiable from the mounted build, and an EU act outside the population out of scope (the audit of 2026-10-27; R89, R80)", async () => {
  const [EN, FR] = [LIVE_CHROME.en, LIVE_CHROME.fr];
  // G3 (R89, O12): what the release keeps is the build it mounts; the retention rule is the code's, not funded, and a version
  // is "re-verifiable", never replayed, while a kept build holds its text.
  const g3 = EN.developers.replay.guarantees.G3.text;
  assert.ok(g3.includes("Lex does not keep them yet: the release this site serves holds only the build its server mounts."), g3);
  assert.ok(g3.includes("re-verifiable only while a kept build holds its text, which today means the mounted build"), g3);
  assert.doesNotMatch(`${g3} ${FR.developers.replay.guarantees.G3.text}`, /\breplay|\brejou/i, "G3 says re-verifiable, never replay");
  assert.ok(FR.developers.replay.guarantees.G3.text.includes("ne peut donc être vérifiée de nouveau que tant qu’une construction conservée en détient le texte"));
  // The review's refused forms (the addendum of 2026-10-27, truth and coverage): no funding claim, no role named, and the
  // French words it replaced.
  const forms = [EN, FR].flatMap((table) => [table.developers.replay.guarantees.G3.text, ...Object.values(table.coverageAnswer.classDispositions), table.coverageAnswer.europeLanguages]);
  for (const said of forms) assert.doesNotMatch(said, /funded|financé|owner|propriétaire|revérifiable|publishes them in the others|les publie dans les autres/, said);
  const french = JSON.stringify(FR);
  for (const refused of ["reçoit lui aussi le refus", "que décide la liste", "texte servi ci-dessus", "matière rectificative", "ne l’a pas copié", "produise un texte", "qu’autorise le propriétaire", "que consigne son journal"]) {
    assert.ok(!french.includes(refused), refused);
  }
  // No release script chains a build on the one before it, so no release keeps an earlier build beside its own.
  const release = await repository("src/Lex.V3.Tool/Program.cs");
  assert.ok(release.includes("Only a chained build keeps generations."));
  // R80: the population's refusal is the mount's, said apart from the list, which names no EU act still answering unknown.
  const main = mainOf(app.renderLiveReferencePage("developers", { developers: DEVELOPERS }));
  assert.ok(main.includes("data-out-of-scope-population"));
  for (const copy of [EN, FR]) assert.doesNotMatch(copy.developers.refusals.outOfScope.pending, /EU|UE/, "no EU act is pending: the mount refuses it");
  assert.ok(EN.developers.refusals.outOfScope.population.includes("it names neither the out-of-scope list nor its digest"));
  assert.ok(EN.developers.changes.digest.includes("every {code} refusal the list decides names the list's identity"));
  const scope = await repository("src/Lex.V3.Api/V3CorpusMount.Scope.cs");
  assert.ok(scope.includes("out_of_scope_class = EuropePopulationClass") && !/EuropePopulationClass[\s\S]{0,2000}scope_list = /.test(scope.slice(scope.indexOf("RefuseOutsideEuropePopulation("))), "the population's refusal names no list");
  // Every out-of-scope sentence the page says is true of both kinds: "does not hold", never "never holds".
  assert.doesNotMatch(EN.developers.refusals.codes.out_of_corpus_scope, /never holds/);
});

test("the home page and How to read Lex claim no more than each publisher's pages do (the audit of 2026-10-27, overclaims 5 and 6)", () => {
  const EN = LIVE_CHROME.en;
  // The home page: history and comparison are said of a Luxembourg act of which more than one version is held, never of every act
  // (an EU act's history and comparison refuse, and most Luxembourg acts are held as first published only).
  assert.doesNotMatch(EN.home.intro, /see how each article changed|compare two dates/);
  assert.ok(EN.home.intro.includes("For a Luxembourg act of which the site holds more than one version, see whether each article's wording changed from one version to the next, and which articles differ between the versions of two dates."));
  // How to read Lex: no promise that a textless version is named, or why, since index 8 (the live release) names none and R77's
  // records may or may not exist (the review of #1004, F2); only what holds on every index: no other version's text in its place.
  const textless = EN.guide.sections.textless.text;
  assert.ok(textless.endsWith("or Lex may not have collected it. Lex never shows another version's text in its place."), textless);
  assert.doesNotMatch(textless, /names the version|says why/);
  assert.doesNotMatch(LIVE_CHROME.fr.guide.sections.textless.text, /nomme la version|dit pourquoi/);
  for (const language of ["en", "fr"]) {
    assert.ok(LIVE_CHROME[language].home.intro.length > 0 && LIVE_CHROME[language].guide.sections.textless.text.length > 0, language);
  }
});
