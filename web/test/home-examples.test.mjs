// The home page's example searches (the specification of record's §3, Shell and home: "a dated coverage line and example
// searches"; R47; the plan's item 4.1: "example searches that land on served text"), derived from the mounted corpus
// through the API's own answers (`home-examples.mjs`), here the census's.
//
// - Derived: the census's one act gives one example, its first article's usual citation, the act named by its type and date
//   and never its title (R92), kept because the exact-reference phase opens Reading at that very article; a citation the
//   phase does not open at its article is dropped; with no corpus counted there is no record.
// - Read back: a record is held to its shape, and a page shows its examples only over the index they were derived from.
// - Shown: the home page lists them under its coverage line, each a link to the search page with the search in its address
//   (R109), in each language's words; over no mount, another mount or as built, none.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { LiveHome } from "../.react-build/app.mjs";
import {
  HOME_EXAMPLES, HOME_EXAMPLES_AT_MOST, HOME_EXAMPLES_SCHEMA, deriveHomeExamples, exampleHref, examplesShown, readHomeExamplesValue,
} from "../scripts/home-examples.mjs";
import { coverageLineOf, homeFacts } from "../scripts/live-home.mjs";
import { LIVE_CHROME } from "../scripts/live-chrome.mjs";
import { readSearchFragment } from "../scripts/search-address.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (operation, scenarioStart) => structuredClone(census.envelopes.find((entry) => entry.operation === operation && entry.scenario.startsWith(scenarioStart)).envelope);
const COVERAGE = envelopeOf("coverage", "the whole mount");
const UNMOUNTED = envelopeOf("coverage", "no corpus mounted");
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const BROWSE = answers.sampled.find((row) => row.operation === "browse").answer;
const USUAL = answers.sampled.find((row) => row.operation === "search" && row.answer.reference?.outcome === "opens" && row.answer.reference?.opens?.page === "reading" && /^Art\. 15 /.test(row.answer.requested_query)).answer;
const SEARCH_CONTEXT = envelopeOf("search", "the phrase as the site asks it");
const INDEX = COVERAGE.result.value.mounted.index_sha256;
const TODAY = "2026-10-27";

/**
 * The search answer the exact-reference phase gives a usual citation of `article` (the census's answer for art_15, said of
 * the article asked), in the census's search envelope.
 */
function opensArticle(query, article, number) {
  const answer = structuredClone(USUAL);
  answer.requested_query = query;
  answer.reference.recognised.text = query;
  answer.reference.recognised.article = { ...answer.reference.recognised.article, as_typed: number, key: number, number };
  answer.reference.opens.address = answer.reference.opens.address.replace(/\/art_15$/, `/${article}`);
  answer.reference.opens.article_id = article;
  for (const key of ["corpus_sha256", "index_sha256"]) answer[key] = SEARCH_CONTEXT.result.value[key] ?? answer[key];
  const envelope = structuredClone(SEARCH_CONTEXT);
  envelope.result.value = answer;
  return envelope;
}

/** The API, as the census answers it: `search` by `searchFor(parameters)`. */
function census_ask(searchFor, { coverage = COVERAGE } = {}) {
  const asked = [];
  const ask = async (operation, parameters) => {
    asked.push([operation, parameters]);
    if (operation === "coverage") return coverage;
    if (operation === "browse") return { refusal: null, result: { value: BROWSE } };
    if (operation === "evidence_bundle") return READING;
    if (operation === "search") return searchFor(parameters);
    throw new Error(`no census answer for ${operation}`);
  };
  return { ask, asked };
}

test("the census's one act gives one example: its first article's usual citation, by the act's type and date, opening that article", async () => {
  const { ask, asked } = census_ask((parameters) => opensArticle(parameters.query, "art_1er", "1"));
  const record = await deriveHomeExamples({ ask, contract, today: TODAY });
  assert.equal(record.schema, HOME_EXAMPLES_SCHEMA);
  assert.equal(record.index_sha256, INDEX, "the examples are the counted index's");
  assert.equal(record.derived_on, TODAY);
  assert.equal(record.examples.length, 1);
  const [example] = record.examples;
  assert.match(example.query, /^Art\. 1er de la loi du 10 août 1991/, example.query);
  assert.equal(example.language, "fra");
  assert.match(example.opens, /\/fra\/art_1er$/);
  // Asked as the site asks a search its address holds: the strict lane, the text's language, no date, no work.
  const search = asked.find(([operation]) => operation === "search")[1];
  assert.deepEqual(search, { query: example.query, language: "fra", mode: "strict" });
  assert.deepEqual(asked.find(([operation]) => operation === "evidence_bundle")[1], { identifier: BROWSE.works[0].identifier, date: TODAY });
});

test("the act is named by its type and date, never by its title, even when the publisher's title starts with them (R92)", async () => {
  const titled = structuredClone(READING);
  const value = titled.result.value;
  const state = value.states[0];
  value.citation_facts.titles = [{ expression_iri: state.expression_iri, language: state.language, titles: [{ title: "Loi du 10 août 1991 portant naturalisation de Madame Une Personne.", title_kind: "title", evidence_sha256: "a".repeat(64) }] }];
  const record = await deriveHomeExamples({
    ask: async (operation, parameters) => (operation === "evidence_bundle" ? titled : census_ask((asked) => opensArticle(asked.query, "art_1er", "1")).ask(operation, parameters)),
    contract,
    today: TODAY,
  });
  for (const example of record?.examples ?? []) assert.doesNotMatch(example.query, /naturalisation|Personne/, example.query);
});

test("a citation the phase does not open at its article is dropped, and with no corpus counted there is no record", async () => {
  const another = await deriveHomeExamples({ ask: census_ask((parameters) => opensArticle(parameters.query, "art_2", "2")).ask, contract, today: TODAY });
  assert.deepEqual(another.examples, [], "it opens another article: no example");
  const noHit = await deriveHomeExamples({ ask: census_ask(() => envelopeOf("search", "a word no held article carries")).ask, contract, today: TODAY });
  assert.deepEqual(noHit.examples, [], "a search with no reference read: no example");
  assert.equal(await deriveHomeExamples({ ask: census_ask(() => null, { coverage: UNMOUNTED }).ask, contract, today: TODAY }), null);
  await assert.rejects(deriveHomeExamples({ ask: census_ask(() => null).ask, contract, today: "27/10/2026" }), /calendar date/);
});

test("a record is held to its shape, and a page shows its examples only over the very index they were derived from", async () => {
  const record = await deriveHomeExamples({ ask: census_ask((parameters) => opensArticle(parameters.query, "art_1er", "1")).ask, contract, today: TODAY });
  assert.deepEqual(readHomeExamplesValue(JSON.parse(JSON.stringify(record))), record);
  for (const broken of [
    null, [], { ...record, schema: "lex-v3-home-examples/2" }, { ...record, index_sha256: "b".repeat(63) }, { ...record, derived_on: "today" },
    { ...record, examples: [{ ...record.examples[0], language: "lux" }] }, { ...record, examples: [{ ...record.examples[0], query: " " }] },
    { ...record, examples: [{ ...record.examples[0], opens: "https://elsewhere.example/" }] },
    { ...record, examples: Array.from({ length: HOME_EXAMPLES_AT_MOST + 1 }, () => record.examples[0]) },
  ]) assert.equal(readHomeExamplesValue(broken), null, JSON.stringify(broken)?.slice(0, 80));

  const counted = homeFacts(COVERAGE, contract).coverage;
  assert.equal(counted.indexSha256, INDEX, "the coverage line carries the index it counts");
  assert.deepEqual(examplesShown(counted, record), record.examples);
  assert.deepEqual(examplesShown({ ...counted, indexSha256: "c".repeat(64) }, record), [], "another mount");
  assert.deepEqual(examplesShown(homeFacts(UNMOUNTED, contract).coverage, record), [], "no corpus mounted");
  assert.deepEqual(examplesShown(null, record), [], "the page as built, before its coverage answer");
  assert.deepEqual(examplesShown(counted, null), [], "a build handed no examples");
  assert.equal(HOME_EXAMPLES, null, "the web suite's bundle carries none");
  assert.equal(coverageLineOf({ state: "refusal", code: "no_corpus_mounted" }).indexSha256, undefined);
});

test("the home page lists the examples under its coverage line, each a link to the search page with the search in its address", async () => {
  const record = await deriveHomeExamples({ ask: census_ask((parameters) => opensArticle(parameters.query, "art_1er", "1")).ask, contract, today: TODAY });
  const facts = homeFacts(COVERAGE, contract);
  const html = renderToStaticMarkup(h(LiveHome, { contract, facts, examples: record }));
  const copy = LIVE_CHROME.en.home;
  const [example] = record.examples;
  const href = exampleHref(example, "en");
  assert.match(href, /^\/en\/search\.html#q=/);
  assert.deepEqual(readSearchFragment(href.slice(href.indexOf("#")), { work: false }).request, { query: example.query, language: "fra", identifier: "", date: "" });
  const section = /<section class="home-examples" aria-labelledby="home-examples-heading" data-home-examples="1">(.*?)<\/section>/s.exec(html);
  assert.ok(section, html);
  assert.ok(section[1].includes(`<h2 id="home-examples-heading">${copy.examplesHeading}</h2>`));
  assert.ok(section[1].includes(`<a href="${href.replaceAll("&", "&amp;")}" data-home-example="${example.opens}" lang="fr">${example.query}</a>`), section[1]);
  assert.ok(html.indexOf('data-coverage-line="counted"') < html.indexOf("data-home-examples"), "under the coverage line");
  assert.ok(html.indexOf("data-home-examples") < html.indexOf("data-home-links"), "before the links");

  for (const [name, shown] of [
    ["no corpus mounted", renderToStaticMarkup(h(LiveHome, { contract, facts: homeFacts(UNMOUNTED, contract), examples: record }))],
    ["another mount", renderToStaticMarkup(h(LiveHome, { contract, facts: { ...facts, coverage: { ...facts.coverage, indexSha256: "d".repeat(64) } }, examples: record }))],
    ["as built", renderToStaticMarkup(h(LiveHome, { contract, examples: record }))],
    ["no examples", renderToStaticMarkup(h(LiveHome, { contract, facts }))],
  ]) assert.doesNotMatch(shown, /data-home-examples/, name);
  for (const language of ["en", "fr"]) {
    for (const key of ["examplesHeading", "examplesIntro"]) assert.ok(LIVE_CHROME[language].home[key].length > 0, `${language} home.${key}`);
  }
});

test("a build handed the record compiles it into the home page's bundles: the servers' renderer shows it over that index, the page as built none", async () => {
  const { mkdtemp, rm } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const vm = await import("node:vm");
  const { buildLive } = await import("../scripts/build-live.mjs");
  const record = await deriveHomeExamples({ ask: census_ask((parameters) => opensArticle(parameters.query, "art_1er", "1")).ask, contract, today: TODAY });
  await assert.rejects(buildLive(new URL("file:///nowhere/"), { examples: { ...record, schema: "another" } }), /not a record of lex-v3-home-examples\/1/);
  const destination = await mkdtemp(join(tmpdir(), "lex-home-examples-"));
  try {
    await buildLive(new URL(`file:///${destination.replaceAll("\\", "/")}/`), { buildTag: "home-examples", examples: record });
    for (const language of ["en", "fr"]) {
      const context = vm.createContext({ TextEncoder });
      vm.runInContext(await readFile(join(destination, "ssr", `${language}.js`), "utf8"), context);
      const html = context.lexSsr.render("home", JSON.stringify(COVERAGE), `/${language}/`);
      const href = exampleHref(record.examples[0], language).replaceAll("&", "&amp;");
      assert.ok(html.includes(`<h2 id="home-examples-heading">${LIVE_CHROME[language].home.examplesHeading}</h2>`), `${language}: the heading`);
      assert.ok(html.includes(`href="${href}"`), `${language}: the example's link`);
      assert.doesNotMatch(context.lexSsr.render("home", JSON.stringify(UNMOUNTED), `/${language}/`), /data-home-examples/, `${language}: no mount, no example`);
      assert.doesNotMatch(await readFile(join(destination, language, "index.html"), "utf8"), /data-home-examples/, `${language}: the page as built shows none until its coverage answer`);
    }
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});
