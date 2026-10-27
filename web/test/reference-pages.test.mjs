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
import { developersModel, MAXIMUM_REQUEST_BYTES, MCP_METHODS, MCP_PROTOCOL_VERSION, TRANSPORT_PROBLEMS, TRANSPORT_PROBLEM_SCHEMA, publishedSchemaPath } from "../scripts/developers.mjs";
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
        const parts = page.key === "developers" ? ["access", "operations", "envelope", "refusals", "citing", "limits", "events", "schemas"] : Object.keys(entry.sections);
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
    assert.equal(section.includes(`<dd><code>${operation.id}</code></dd>`), operation.served, `${operation.id}: an MCP tool exactly when served`);
    const request = JSON.parse(readFileSyncOf(`schemas/v3-platform/${operation.id.replaceAll("_", "-")}-request.schema.json`)).properties.parameters;
    assert.deepEqual([...section.matchAll(/data-parameter="([a-z_]+)" data-required="(true|false)"/g)].map((match) => [match[1], match[2] === "true"]),
      Object.keys(request.properties ?? {}).map((name) => [name, (request.required ?? []).includes(name)]), `${operation.id}: its parameters, as its request schema states them`);
    for (const schema of [operation.requestSchema, operation.resultSchema]) {
      assert.equal(schema.sha256, createHash("sha256").update(readFileSyncOf(`schemas/v3-platform/${schema.file}`, null)).digest("hex"), `${schema.id}: its digest`);
      assert.ok(section.includes(`<a href="${schema.path}" data-schema="${schema.id}">`) && section.includes(`<code>${schema.sha256}</code>`), `${operation.id}: links ${schema.id} with its digest`);
    }
    if (!operation.served) assert.ok(section.includes(escaped(coverage.not_served_data.find((row) => row.operation === operation.id).data_needed)), `${operation.id}: the data that would serve it`);
  }
  assert.deepEqual([...main.matchAll(/data-refusal="([a-z_]+)"/g)].map((match) => match[1]), REGISTERED_REFUSALS, "every refusal code, in the registry's order");
  for (const refusal of contract.refusals) {
    const row = main.slice(main.indexOf(`data-refusal="${refusal.code}"`), main.indexOf("</div>", main.indexOf(`data-refusal="${refusal.code}"`)));
    for (const field of refusal.mandatory_payload_fields) assert.ok(row.includes(`<code>${field}</code>`), `${refusal.code}: names ${field}`);
  }
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
    assert.doesNotMatch(said, /l’un des \d+ code|one of \d+ code/, `${language} ${count}`);
    assert.ok(said.includes(String(count)), `${language} ${count}`);
  }
  // The glossary's French definition of a plural term agrees with it.
  assert.equal(glossaryTerm(FR, "export.withoutTextHeading"), "Détenus sans texte");
  assert.match(FR.guide.glossary.terms.withoutText, /^Se dit d’articles .* Ils sont listés à part/);
  // Words the review refused in these pages' French: Lex's texts are never "sur Lex", the MCP transport is never "HTTP en
  // flux", this release is never "version du site", a text "sert de réponse pour" a date, the site "connaît une
  // défaillance", and a catalogue gap is an "intervalle sans version" ("vide" is kept for "vide juridique").
  const refused = [/sur Lex/, /HTTP en flux/, /version du site/, /répond à la date/, /est en défaut/, /(?:un|le|ce|’un) vide(?! juridique)/, /^Vides$/, /conseil de votre choix/, /aucun conseil/];
  for (const [path, text] of entriesOf(FR).filter(([one]) => SECTIONS.test(one))) {
    for (const pattern of refused) assert.doesNotMatch(text, pattern, `fr ${path}`);
  }
});
