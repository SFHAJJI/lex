// The pages at a law's address rendered by the server from its own answer (the specification of record's R67: the dossier
// and reading pages are complete HTML from the server, readable without JavaScript, then enhanced): what each address is
// rendered from, the answer the page carries, the same tree on both sides of hydration, and the live server rendering and
// falling back as the API does. The home page is rendered the same way from the coverage answer (R11, R13), and the
// built renderer says it, and R50's sentence, in each interface language.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createServer } from "node:http";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import { rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import vm from "node:vm";
import { renderToString } from "react-dom/server";

import {
  LIVE_CONTRACT, LIVE_DOSSIER_ROOT, LIVE_READING_ROOT, liveDossierTree, liveReadingTree, renderLiveDossierPage, renderLiveReadingPage,
} from "../.react-build/app.mjs";
import { INITIAL_ANSWER_ID, addressBeforeAnswer, jsonInScript, lawPathOf, readInitialAnswer, readingAddressShown, todayAt } from "../scripts/initial-answer.mjs";
import { parseSitePath, sitePageFor } from "../scripts/permalinks.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { answerHandedWith } from "../scripts/v3-client.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";
import { buildLive } from "../scripts/build-live.mjs";
import { LIVE_CHROME, fillCounted, fillText } from "../scripts/live-chrome.mjs";
import { luxembourgDate } from "../scripts/temporal.mjs";
import { homePageFailures } from "../scripts/deploy-probe.mjs";

const TABLE = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json", import.meta.url), "utf8"));
const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart) => census.envelopes.find((entry) => entry.operation === operation && entry.scenario.startsWith(scenarioStart)).envelope;
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const COVERAGE = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope;
const BROWSE = census.envelopes.find((entry) => entry.operation === "browse" && entry.envelope.result !== null).envelope;
const STATE = READING.result.value.states[0];
// The reading the page at the state's permalink is rendered from (R64: asked by the permalink itself), and the refusal a
// permalink whose digest is not the one held at its date gets, with the current link.
const PINNED = envelopeOf("evidence_bundle", "a state's permalink at its own date");
const MISMATCH = envelopeOf("evidence_bundle", "a state's permalink whose digest is not the one held at its date");
/** An answer as given on `date` (at the same time of day, in the morning in Luxembourg): a readable address asked today. */
const answeredOn = (envelope, date) => {
  const answered = structuredClone(envelope);
  answered.context.freshness.observed_at = answered.context.freshness.observed_at.replace(/^\d{4}-\d{2}-\d{2}/, date);
  return answered;
};

/**
 * The built server renderer of each interface language (`ssr/{language}.js`), as both servers run it, built once for this
 * file and its directory removed when the process ends.
 */
let built = null;
function builtRenderers() {
  built ??= (async () => {
    const destination = await mkdtemp(join(tmpdir(), "lex-live-ssr-renderers-"));
    process.on("exit", () => rmSync(destination, { recursive: true, force: true }));
    await buildLive(new URL(`file:///${destination.replaceAll("\\", "/")}/`), { buildTag: "live-ssr-renderers" });
    const renderers = {};
    for (const language of ["en", "fr"]) {
      const context = vm.createContext({ TextEncoder });
      vm.runInContext(await readFile(join(destination, "ssr", `${language}.js`), "utf8"), context);
      renderers[language] = context.lexSsr;
    }
    return renderers;
  })();
  return built;
}

/** The root's inner markup in a whole page: what the browser hydrates. */
function rootOf(html, id) {
  const open = `<div id="${id}">`;
  const start = html.indexOf(open) + open.length;
  const end = html.indexOf(`<script type="application/json" id="${INITIAL_ANSWER_ID}"`, start);
  return html.slice(start, html.lastIndexOf("</div>", end));
}

test("a stale permalink's page names the state it shows as its own, never the digest it was opened at", () => {
  const stale = STATE.permalink.replace(/--[0-9a-f]{64}\//, `--${"0".repeat(64)}/`);
  const html = renderLiveReadingPage({ initial: { envelope: READING, path: `/en${stale}` } });
  assert.ok(html.includes(`<link rel="canonical" href="https://law.soufien.lu/en${STATE.permalink}"/>`), "the canonical is the state the page shows, in the page's language");
  assert.ok(html.includes(`hrefLang="fr" href="https://law.soufien.lu/fr${STATE.permalink}"`), "and so is each language's alternate");
  assert.ok(!html.slice(0, html.indexOf("</head>")).includes("0".repeat(64)), "the digest it was opened at is named nowhere in the head");
  // A permalink that holds, of the version its act's readable address shows today, names that readable address (R67).
  const exact = renderLiveReadingPage({ initial: { envelope: READING, path: `/en${STATE.permalink}` } });
  assert.ok(exact.includes(`<link rel="canonical" href="https://law.soufien.lu/en/lu-legilux/loi-1991-08-10-n3/fra"/>`), "today's version: the act's readable address");
  const older = structuredClone(READING);
  older.result.value.states[0].next_applicability_date = "2025-03-07";
  const superseded = renderLiveReadingPage({ initial: { envelope: older, path: `/en${STATE.permalink}` } });
  assert.ok(superseded.includes(`<link rel="canonical" href="https://law.soufien.lu/en${STATE.permalink}"/>`), "a version replaced since names itself");
});

test("a law page under /en/ names as canonical the address it shows in its own language, each language and x-default (R61, R67)", () => {
  // The test bundle is the English one; the French renderer names its canonical under /fr/ (the build's test, below).
  const older = structuredClone(READING);
  older.result.value.states[0].next_applicability_date = "2025-03-07";
  const article = `${STATE.permalink}/${STATE.articles[14].publisher_id}`;
  const readable = `/lu-legilux/loi-1991-08-10-n3/fra/${STATE.articles[14].publisher_id}`;
  for (const [envelope, law] of [[READING, readable], [older, article]]) {
    const html = renderLiveReadingPage({ initial: { envelope, path: `/en${article}` } });
    const head = html.slice(0, html.indexOf("</head>"));
    assert.equal((head.match(/rel="canonical"/g) ?? []).length, 1, `${law}: one canonical link`);
    assert.ok(head.includes(`<link rel="canonical" href="https://law.soufien.lu/en${law}"/>`), `${law}: in the page's own language, a page that answers itself`);
    for (const [hrefLang, href] of [["fr", `/fr${law}`], ["en", `/en${law}`], ["x-default", law]]) {
      assert.ok(head.includes(`<link rel="alternate" hrefLang="${hrefLang}" href="https://law.soufien.lu${href}"/>`), `${law}: ${hrefLang}`);
    }
    assert.ok(head.includes('http-equiv="Content-Security-Policy"'), "the page's own policy is untouched");
  }
});

test("a reading's page names as canonical an address the platform mints, never the request's own, as the shared table says (review of #967)", async () => {
  const origin = "https://law.soufien.lu";
  const rows = TABLE.paths.filter((row) => row.canonical !== undefined);
  assert.ok(rows.length >= 5, "the article minted, escaped otherwise, not held, none, and a stale permalink's");
  assert.ok(rows.some((row) => row.path.startsWith("/fr/")) && rows.some((row) => row.path.startsWith("/en/")), "in both interface languages");
  const refused = envelopeOf("evidence_bundle", "a date before the work's history");
  const view = readingOutcome(answerHandedWith("evidence_bundle", READING, { contract: LIVE_CONTRACT, objectType: "evidence_bundle" })).view;
  const today = todayAt(READING.context.freshness.observed_at);
  const renderers = await builtRenderers();
  for (const { path, canonical } of rows) {
    // Each server renders a page with the renderer of the page's interface language (`sitePageFor`).
    const locale = path.slice(1, 3);
    const html = renderers[locale].render("reading", JSON.stringify(READING), path);
    const head = html.slice(0, html.indexOf("</head>"));
    assert.deepEqual([...head.matchAll(/<link rel="canonical" href="([^"]*)"\/>/g)].map((match) => match[1]), [canonical], `${path}: one canonical link`);
    assert.ok(canonical.startsWith(`${origin}/${locale}/`), `${path}: under the page's own interface language`);
    const law = canonical.slice(`${origin}/${locale}`.length);
    assert.deepEqual(
      [...head.matchAll(/<link rel="alternate" hrefLang="([^"]*)" href="([^"]*)"\/>/g)].map(([, hrefLang, href]) => [hrefLang, href]),
      [["fr", `${origin}/fr${law}`], ["en", `${origin}/en${law}`], ["x-default", `${origin}${law}`]],
      `${path}: each language once, and x-default`,
    );
    // The page as a browser loads it names the same once its answer is read (`LiveReading.jsx`), and before that, as built,
    // the state's permalink (or the act's readable address) with no article (`client.jsx`), an address the platform mints.
    const named = parseSitePath(path);
    const before = named.kind === "state" ? named.permalink : named.act;
    assert.equal(readingAddressShown(view, lawPathOf(path), null, today), law, `${path}: once loaded`);
    assert.equal(addressBeforeAnswer(path), before, `${path}: as built`);
    assert.equal(parseSitePath(addressBeforeAnswer(path)).anchor, null, `${path}: as built, no article`);
    // A refusal shows no state: its page names the state's permalink the address pins (or the act's readable address),
    // with no article.
    const refusal = renderers[locale].render("reading", JSON.stringify(refused), path);
    assert.ok(refusal.includes(`<link rel="canonical" href="${origin}/${locale}${before}"/>`), `${path}: a refusal's page`);
  }
  assert.ok(rows.some((row) => parseSitePath(row.path).kind === "current"), "the table names a readable address's canonical too");

  // A permalink refused because its digest is not the version held at its date names the current version's permalink the
  // refusal gives (R64: "with the current link"), never the digest it was opened at.
  const current = MISMATCH.refusal.helpful_payload.current_hash_pinned_url;
  const stalePath = `/en${current.replace(/--[0-9a-f]{64}\//, `--${"0".repeat(64)}/`)}/art_15`;
  const staleHtml = renderLiveReadingPage({ initial: { envelope: MISMATCH, path: stalePath } });
  assert.ok(staleHtml.includes(`<link rel="canonical" href="${origin}/en${current}"/>`), "a mismatch's page names the current version");
  assert.ok(!staleHtml.slice(0, staleHtml.indexOf("</head>")).includes("0".repeat(64)), "and never the digest it was opened at");
  assert.equal(readingAddressShown(null, lawPathOf(stalePath), { code: "pinned_digest_mismatch", payload: MISMATCH.refusal.helpful_payload }), current);
  assert.equal(readingAddressShown(null, lawPathOf(stalePath), { code: "text_withheld", payload: {} }), parseSitePath(stalePath).permalink, "another refusal: the pinned state's");
  assert.equal(
    readingAddressShown(null, lawPathOf(stalePath), { code: "pinned_digest_mismatch", payload: { current_hash_pinned_url: "/en/lu-legilux/x/2024-01-01--" + "a".repeat(64) + "/fra" } }),
    parseSitePath(stalePath).permalink,
    "a current link under an interface language is no address the platform mints: the pinned state's",
  );

  // An article the state holds without text is minted as a segment too, and so is an EU wording's provision; an article
  // neither holds is the state's (or wording's) permalink.
  const state = { permalink: `/lu-legilux/w/2024-01-01--${"a".repeat(64)}/fra`, articles: [], articlesWithoutText: [{ publisherId: "Art. 2" }] };
  const luxembourg = { publisher: "lu-legilux", states: [state] };
  assert.equal(readingAddressShown(luxembourg, `${state.permalink}/Art.%202`), `${state.permalink}/Art.%202`);
  assert.equal(readingAddressShown(luxembourg, `${state.permalink}/Art.%20%32`), `${state.permalink}/Art.%202`, "as the platform escapes it");
  assert.equal(readingAddressShown(luxembourg, `${state.permalink}/Art.%203`), state.permalink);
  const pin = `/eu-eurlex/32016R0679/2016-05-04--${"b".repeat(64)}/eng`;
  const europe = { publisher: "eu-eurlex", wordings: [{ permalink: pin, articles: [{ publisherId: "art_5(1)", permalink: `${pin}/art_5%281%29` }], articlesWithoutText: [] }] };
  assert.equal(readingAddressShown(europe, `${pin}/art_5(1)`), `${pin}/art_5%281%29`);
  assert.equal(readingAddressShown(europe, `${pin}/art_6`), pin);
  assert.equal(readingAddressShown(null, `${pin}/art_6`), pin, "no view: the pinned wording, with no article");
  assert.equal(addressBeforeAnswer("/en/eu-eurlex/32016R0679"), "/eu-eurlex/32016R0679", "a work's address is its own");
  assert.equal(addressBeforeAnswer("/en/search.html"), null);
});

test("each law's address under a language is rendered from what the page itself would ask, as the shared table says", () => {
  const served = TABLE.paths.filter(({ render }) => render !== null);
  assert.ok(served.length >= 12, "the table the API is held to as well");
  for (const { path, search, render } of TABLE.paths) {
    const page = sitePageFor(path, { today: TABLE.today, search: search ?? '' });
    assert.deepEqual(page === null ? null : { ...page, parameters: { ...page.parameters } }, render, `${path}${search ?? ''}`);
  }
});

test("the answer a page carries is JSON that cannot end its block, read back as the page was rendered from", () => {
  const value = { text: "</script><!-- &     >" };
  const written = jsonInScript(value);
  for (const forbidden of ["<", ">", "&", " ", " "]) assert.ok(!written.includes(forbidden), `${JSON.stringify(forbidden)} is escaped`);
  assert.deepEqual(JSON.parse(written), value, "and reads back whole");
  const page = { getElementById: (id) => (id === INITIAL_ANSWER_ID ? { textContent: written } : null) };
  assert.deepEqual(readInitialAnswer(page, { pathname: "/en/x" }), { envelope: value, path: "/en/x" });
  assert.equal(readInitialAnswer({ getElementById: () => null }, { pathname: "/en/x" }), null, "a page the server did not render carries none");
  assert.equal(readInitialAnswer({ getElementById: () => ({ textContent: "not json" }) }, { pathname: "/en/x" }), null);
});

test("a permalink's reading is rendered complete, and the browser renders the same tree from the answer the page carries", () => {
  const cited = STATE.articles[14];
  const initial = { envelope: PINNED, path: `/en${STATE.permalink}/${cited.publisher_id}` };
  const html = renderLiveReadingPage({ initial });
  assert.match(html, /<section data-answer-state="success">/, "the reading, without any script");
  assert.equal((html.match(/<blockquote/g) ?? []).length, STATE.articles.length + 1, "every article quoted in the page itself, the cited one first");
  // R64: the cited article first, under its own title, with a link to the whole act at that version; the page's own title.
  const lead = html.indexOf("data-pinned-lead");
  assert.ok(lead > 0 && lead < html.indexOf('<section data-answer-state="success">'), "the cited article comes before the reading");
  assert.ok(html.includes(`<section class="cited-article" data-cited-article="${cited.publisher_id}"><h2>${cited.publisher_id}</h2>`), "under its own title");
  assert.ok(html.indexOf(cited.text.slice(0, 40)) < html.indexOf('<section data-answer-state="success">'), "its text first");
  assert.ok(html.includes(`<p data-whole-act=""><a href="/en${STATE.permalink}">The whole act at this version</a></p>`), "and the whole act at that version");
  assert.ok(html.includes(`<title>loi-1991-08-10-n3, ${cited.publisher_id} - Lex</title>`), "the page's own title is the article's");
  assert.ok(!html.includes("data-changed-since"), "the one state held has no newer version");
  assert.ok(html.includes(`value="${STATE.applicability_date}"`), "the form names the date the address names");
  assert.ok(html.includes(`value="/lu-legilux/loi-1991-08-10-n3"`), "and the work, which a reader asks another date of");
  assert.ok(!html.includes("data-pin-replaced"), "the state served is the one the permalink pins");
  assert.equal(rootOf(html, LIVE_READING_ROOT), renderToString(liveReadingTree(initial)), "hydration changes nothing");
  const block = html.slice(html.indexOf(`id="${INITIAL_ANSWER_ID}">`) + `id="${INITIAL_ANSWER_ID}">`.length, html.indexOf("</script>", html.indexOf(INITIAL_ANSWER_ID)));
  assert.deepEqual(JSON.parse(block), PINNED, "the page carries the answer it was rendered from");

  // A version that is not the latest held says so (R64), in the publisher's words (R21), with the act at today's date.
  const older = structuredClone(PINNED);
  older.result.value.states[0].next_applicability_date = "2025-03-07";
  const olderHtml = renderLiveReadingPage({ initial: { envelope: older, path: initial.path } });
  const banner = olderHtml.slice(olderHtml.indexOf("<p data-changed-since"), olderHtml.indexOf("</p>", olderHtml.indexOf("<p data-changed-since")));
  assert.ok(olderHtml.indexOf("<p data-changed-since") < olderHtml.indexOf("data-cited-article"), "the banner, first");
  assert.ok(banner.includes("A newer version is applicable from 2025-03-07 (publisher)."), banner);
  assert.ok(banner.includes(`<a href="/en/lu-legilux/loi-1991-08-10-n3/fra/${cited.publisher_id}">Read this act at today&#x27;s date</a>`), "the act at today's date, at its readable address");
  assert.equal(rootOf(olderHtml, LIVE_READING_ROOT), renderToString(liveReadingTree({ envelope: older, path: initial.path })));

  // A permalink whose digest is not the version held at its date: the refusal, with the notice and the current link first.
  const refusedPath = `/en${STATE.permalink.replace(/--[0-9a-f]{64}\//, `--${"0".repeat(64)}/`)}/${cited.publisher_id}`;
  const refusedHtml = renderLiveReadingPage({ initial: { envelope: MISMATCH, path: refusedPath } });
  assert.match(refusedHtml, /<section data-answer-state="refusal">/);
  assert.ok(refusedHtml.includes('data-pin-not-held=""'), "the notice");
  assert.ok(refusedHtml.includes(`<a href="/en${MISMATCH.refusal.helpful_payload.current_hash_pinned_url}"><code>${MISMATCH.refusal.helpful_payload.current_hash_pinned_url}</code></a>`), "the current link");
  assert.ok(refusedHtml.includes("pinned_digest_mismatch"), "the refusal card, with its payload");
  assert.equal(rootOf(refusedHtml, LIVE_READING_ROOT), renderToString(liveReadingTree({ envelope: MISMATCH, path: refusedPath })));

  // An act's readable address (R67): the reading at today's date (the answer's own), the cited article first, and its
  // readable address as canonical; today said beside the form's date (R13), the answer's day: asked on the day it was answered.
  const readable = { envelope: answeredOn(READING, READING.result.value.requested_date), path: `/en/lu-legilux/loi-1991-08-10-n3/fra/${cited.publisher_id}` };
  const readableHtml = renderLiveReadingPage({ initial: readable });
  assert.ok(readableHtml.includes(`data-cited-article="${cited.publisher_id}"`), "the cited article first at a readable address too");
  assert.ok(readableHtml.includes(`<p data-whole-act=""><a href="/en/lu-legilux/loi-1991-08-10-n3/fra">`), "the whole act at its readable address");
  assert.ok(readableHtml.includes(`<link rel="canonical" href="https://law.soufien.lu/en/lu-legilux/loi-1991-08-10-n3/fra/${cited.publisher_id}"/>`));
  assert.ok(readableHtml.includes(`value="${STATE.applicability_date}"`), "the form names the date the answer was asked for");
  assert.ok(readableHtml.includes(`<p id="reading-today" class="field-hint" data-today="${STATE.applicability_date}">Today in Luxembourg: ${STATE.applicability_date}.</p>`), "today beside the date field, the answer's day");
  assert.ok(!readableHtml.includes("data-not-today"), "read today: nothing to say of another day");
  assert.equal(rootOf(readableHtml, LIVE_READING_ROOT), renderToString(liveReadingTree(readable)), "hydration changes nothing");

  // The same address read on another day than the one the answer was given on (an address that keeps a date, `?date=`, the
  // plan's item 3.4): the date read and today, first, with the act at today's date; the whole act at the version it shows,
  // by its permalink; the canonical still the readable address, the version shown being today's too.
  const another = { envelope: READING, path: readable.path };
  const anotherHtml = renderLiveReadingPage({ initial: another });
  assert.ok(anotherHtml.replaceAll("<!-- -->", "").includes(`<p data-not-today="${STATE.applicability_date}">This page reads the act on ${STATE.applicability_date}, as this site holds it for that date; today in Luxembourg is 2026-09-18. <a href="/en/lu-legilux/loi-1991-08-10-n3/fra/${cited.publisher_id}" data-read-today="">Read this act at today&#x27;s date</a></p>`), anotherHtml.slice(anotherHtml.indexOf("data-pinned-lead"), anotherHtml.indexOf("data-pinned-lead") + 400));
  assert.ok(anotherHtml.indexOf("data-not-today") < anotherHtml.indexOf("data-cited-article"), "said first");
  assert.ok(anotherHtml.includes(`<p data-whole-act=""><a href="/en${STATE.permalink}">`), "the whole act at the version it shows");
  assert.ok(anotherHtml.includes(`<link rel="canonical" href="https://law.soufien.lu/en/lu-legilux/loi-1991-08-10-n3/fra/${cited.publisher_id}"/>`), "the version shown is today's");
  assert.equal(rootOf(anotherHtml, LIVE_READING_ROOT), renderToString(liveReadingTree(another)), "hydration changes nothing");
  // A version that is not today's: its permalink is the page's canonical address, which the release's switch never opens.
  const superseded = structuredClone(READING);
  superseded.result.value.states[0].next_applicability_date = "2025-03-07";
  const supersededHtml = renderLiveReadingPage({ initial: { envelope: superseded, path: readable.path } });
  assert.ok(supersededHtml.includes(`<link rel="canonical" href="https://law.soufien.lu/en${STATE.permalink}/${cited.publisher_id}"/>`), "the version's own permalink, its article's");
  assert.ok(supersededHtml.replaceAll("<!-- -->", "").includes(`<p data-changed-since="">A newer version is applicable from 2025-03-07 (publisher). <a href="/en/lu-legilux/loi-1991-08-10-n3/fra/${cited.publisher_id}">Read this act at today&#x27;s date</a></p>`));

  const stale = { envelope: READING, path: `/en${STATE.permalink.replace(/--[0-9a-f]{64}\//, `--${"0".repeat(64)}/`)}` };
  const staleHtml = renderLiveReadingPage({ initial: stale });
  assert.ok(staleHtml.includes("data-pin-replaced"), "a stale permalink's notice is in the page itself");
  assert.equal(rootOf(staleHtml, LIVE_READING_ROOT), renderToString(liveReadingTree(stale)));

  assert.ok(!renderLiveReadingPage().includes(INITIAL_ANSWER_ID), "the page as built carries no answer");

  // The page names as canonical the address it shows in its own interface language: at a permalink of the version the act's
  // readable address shows today, that readable address, its article included (R67); the same address in each language, and
  // the address with no interface language (which sends a reader to their language) for any other (R61, R64, R67).
  const origin = "https://law.soufien.lu";
  const article = `/lu-legilux/loi-1991-08-10-n3/fra/${STATE.articles[14].publisher_id}`;
  assert.ok(html.includes(`<link rel="canonical" href="${origin}/en${article}"/>`), "canonical: this article's readable address");
  for (const [hrefLang, href] of [["fr", `${origin}/fr${article}`], ["en", `${origin}/en${article}`], ["x-default", `${origin}${article}`]]) {
    assert.ok(html.includes(`<link rel="alternate" hrefLang="${hrefLang}" href="${href}"/>`), `${hrefLang}: ${href}`);
  }
  assert.ok(!renderLiveReadingPage().includes('rel="canonical"') && !renderLiveReadingPage().includes('rel="alternate"'),
    "the page as built names no address: it may stand at any law's");
});

test("a work's dossier is rendered complete at its address, and a refusal is rendered as the page would show it", () => {
  const dossier = envelopeOf("dossier", "");
  const initial = { envelope: dossier, path: "/en/lu-legilux/loi-1991-08-10-n3" };
  const html = renderLiveDossierPage({ initial });
  assert.match(html, /<section data-answer-state="(success|refusal)">/);
  assert.ok(html.includes('value="/lu-legilux/loi-1991-08-10-n3"'), "the form names the work the address names");
  assert.equal(rootOf(html, LIVE_DOSSIER_ROOT), renderToString(liveDossierTree(initial)), "hydration changes nothing");
});

test("the build writes a server renderer per language, which renders the page in a context with nothing else in it", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-live-ssr-"));
  try {
    await buildLive(new URL(`file:///${destination.replaceAll("\\", "/")}/`), { buildTag: "live-ssr-test" });
    for (const language of ["en", "fr"]) {
      const context = vm.createContext({ TextEncoder });
      vm.runInContext(await readFile(join(destination, "ssr", `${language}.js`), "utf8"), context);
      const html = context.lexSsr.render("reading", JSON.stringify(READING), `/${language}${STATE.permalink}`);
      assert.match(html, new RegExp(`^<!doctype html>\\n<html lang="${language}"`), `${language}: a whole page in its language`);
      assert.ok(html.includes(`<link rel="canonical" href="https://law.soufien.lu/${language}/lu-legilux/loi-1991-08-10-n3/fra"/>`), `${language}: the canonical in the page's own language`);
      assert.equal((html.match(/<blockquote/g) ?? []).length, STATE.articles.length, `${language}: the reading, quoted`);
      assert.throws(() => context.lexSsr.render("history", "{}", "/en/x"), /no server-rendered history page/);

      // The Developers page, from the answer to browse for the first act held: its live examples, the release's own data
      // (the plan's item 8.4), with the contracts the build handed the renderer; the page as built carries none.
      const developers = context.lexSsr.render("developers", JSON.stringify(BROWSE), `/${language}/developers.html`);
      assert.match(developers, new RegExp(`^<!doctype html>\n<html lang="${language}" data-product-line="lex-v3" data-preview-state="live-developers"`), `${language}: the Developers page`);
      assert.ok(developers.includes('data-live-examples="live"') && developers.includes(`<code>${BROWSE.result.value.works[0].identifier}</code>`), `${language}: its live examples, the first act held`);
      assert.ok(developers.includes(`data-registry-sha256="${census.contract.registry_sha256}"`), `${language}: from the contracts`);
      const built = await readFile(join(destination, language, "developers.html"), "utf8");
      assert.ok(built.includes('data-live-examples="built"') && !built.includes('data-live-examples="live"'), `${language}: as built, none`);

      // The home page, from the coverage answer, in this language: the one box with its placeholder, today in Luxembourg
      // when the API answered, the coverage line from the answer's counts, and R50's sentence under the box and in the footer.
      const copy = LIVE_CHROME[language];
      const home = context.lexSsr.render("home", JSON.stringify(COVERAGE), `/${language}/`);
      assert.match(home, new RegExp(`^<!doctype html>\\n<html lang="${language}" data-product-line="lex-v3" data-preview-state="live-home"`), `${language}: the home page`);
      assert.ok(home.includes(`placeholder="${copy.search.placeholder}"`), `${language}: R52's placeholder`);
      const today = luxembourgDate(COVERAGE.context.freshness.observed_at);
      assert.match(home, new RegExp(`data-search-date="" aria-describedby="search-today" value="${today}"`), `${language}: today in the date field`);
      const { totals, history } = COVERAGE.result.value;
      const line = fillText(copy.home.coverage, {
        acts: fillCounted(copy.home.acts, totals.works, {}, language),
        versions: fillCounted(copy.home.versions, totals.states, {}, language),
        articles: fillCounted(copy.home.articles, totals.articles, {}, language),
        date: luxembourgDate(history.snapshots_with_text[0].built_at),
      });
      assert.ok(home.includes(`data-coverage-line="counted" data-built-on="${luxembourgDate(history.snapshots_with_text[0].built_at)}">${line}</p>`), `${language}: ${line}`);
      assert.equal(home.split(`data-not-advice="">${copy.site.notAdvice}</p>`).length - 1, 2, `${language}: R50's sentence under the box and in the footer`);
      // The reading says it in this language too, above the text.
      assert.ok(html.includes(`<p class="not-advice" data-not-advice="">${copy.site.notAdvice}</p>`), `${language}: Reading says R50's sentence`);
      // The deploy probe holds a revision's rendered home page to the release's own: the page this renderer renders from
      // a coverage answer passes, and the four pages the review of #971 found it passed do not.
      if (language === "fr") {
        const built = await readFile(join(destination, "fr", "index.html"));
        const renderer = await readFile(join(destination, "ssr", "fr.js"));
        assert.deepEqual(homePageFailures(Buffer.from(home), built, { renderer, coverage: COVERAGE }), [], "the page as the server renders it");
        assert.deepEqual(homePageFailures(built, built, { renderer, coverage: COVERAGE }), [], "the page as built");
        for (const [what, page] of [
          ["the footer's R50 line swapped for the old advice line", home.replace(/<p data-not-advice="">[^<]*<\/p><p>/, "<p>Documentation, et non consultation juridique.</p><p>")],
          ["the heading and the whole root replaced, with empty facts", home.replace(/<h1>[\s\S]*<\/main>/, '<h1>Autre</h1><div id="live-home-root"><p>anything</p></div><script type="application/json" id="lex-home-facts">{}</script><script src="/fr/client-live-home.js" defer=""></script></main>')],
          ["a count typed into the coverage line", home.replace(/(data-coverage-line="counted" data-built-on="[^"]*">)[^<]*/, "$1Ce site détient 1402 actes")],
          ["another day in the date field", home.replace(`value="${today}"`, 'value="2001-01-01"')],
        ]) {
          assert.notEqual(page, home, `${what}: the page was changed`);
          assert.ok(homePageFailures(Buffer.from(page), built, { renderer, coverage: COVERAGE }).length > 0, what);
        }
      }
      // And the search page as built, and the home page as built, which asks for its facts once loaded.
      for (const page of ["search.html", "index.html"]) {
        const built = await readFile(join(destination, language, page), "utf8");
        assert.ok(built.includes(`<p class="not-advice" data-not-advice="">${copy.site.notAdvice}</p>`), `${language}/${page}: R50's sentence`);
        assert.ok(built.includes(`placeholder="${copy.search.placeholder}"`), `${language}/${page}: R52's placeholder`);
      }
    }
    assert.equal(LIVE_CHROME.fr.site.notAdvice, "Lex montre le texte de la loi. Il ne dit pas ce qui s’applique à votre situation, et ce n’est pas un conseil juridique.");
    assert.equal(LIVE_CHROME.fr.search.placeholder, "Rechercher par intitulé, numéro, ELI ou CELEX, ou par des mots du texte");
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});

test("the live server renders a law's address from the API's answer, refuses to serve its renderer, and falls back to the page as built", async () => {
  const root = await mkdtemp(join(tmpdir(), "lex-live-ssr-serve-"));
  const answers = new Map();
  const asked = [];
  const api = createServer((request, response) => {
    const chunks = [];
    request.on("data", (chunk) => chunks.push(chunk));
    request.on("end", () => {
      asked.push({ url: request.url, body: Buffer.concat(chunks).toString("utf8") });
      const answer = answers.get(request.url);
      if (answer === undefined) {
        response.writeHead(404);
        response.end();
        return;
      }
      response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
      response.end(JSON.stringify(answer));
    });
  });
  await new Promise((resolve) => api.listen(0, "127.0.0.1", resolve));
  try {
    for (const language of ["en", "fr"]) await mkdir(join(root, language), { recursive: true });
    await mkdir(join(root, "ssr"), { recursive: true });
    await writeFile(join(root, "en", "reading.html"), "<!doctype html><p>the reading page as built</p>");
    await writeFile(join(root, "en", "dossier.html"), "<!doctype html><p>the dossier page as built</p>");
    await writeFile(join(root, "ssr", "en.js"), `globalThis.lexSsr = { render(kind, envelope, path) {
      const value = JSON.parse(envelope);
      return '<!doctype html><p>' + kind + ' ' + (value.refusal ? value.refusal.code : 'answer') + ' ' + path + '</p>';
    } };`);
    let clock = "2026-10-15T23:30:00Z";
    const live = createLiveServer({ root, apiOrigin: `http://127.0.0.1:${api.address().port}`, now: () => clock });
    const origin = await new Promise((resolve) => live.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${live.address().port}`)));
    try {
      const permalink = `/en/lu-legilux/loi-1991-08-10-n3/2024-02-01--${"a".repeat(64)}/fra/art_15`;
      answers.set("/api/v3/evidence_bundle", { result: { value: {} }, refusal: null });
      const page = await fetch(`${origin}${permalink}`);
      assert.equal(page.status, 200);
      assert.equal(await page.text(), `<!doctype html><p>reading answer ${permalink}</p>`, "rendered from the API's answer");
      assert.deepEqual(
        JSON.parse(asked.at(-1).body),
        { operation_id: "evidence_bundle", parameters: { identifier: `/lu-legilux/loi-1991-08-10-n3/2024-02-01--${"a".repeat(64)}/fra`, date: "2024-02-01", language: "fra", article: "art_15" } },
        "asked as the page itself would ask: by the permalink, its date and the text's language it names (R64), for the page that holds its article (the plan's item 3.7)",
      );
      // A page its query names (the plan's item 3.7), asked as a whole number; the rest of the query is not read.
      assert.equal((await fetch(`${origin}${permalink.replace(/\/art_15$/, "")}?from=card&page=3`)).status, 200);
      assert.deepEqual(JSON.parse(asked.at(-1).body).parameters, { identifier: `/lu-legilux/loi-1991-08-10-n3/2024-02-01--${"a".repeat(64)}/fra`, date: "2024-02-01", language: "fra", page: 3 });

      // An act's readable address (R67): asked at today's date in Luxembourg by the server's clock (R13): 23:30 UTC on
      // 2026-10-15 is 2026-10-16 in Luxembourg, in summer time.
      const readable = "/en/lu-legilux/loi-1991-08-10-n3/fra/art_15";
      assert.equal((await fetch(`${origin}${readable}`)).status, 200);
      assert.deepEqual(JSON.parse(asked.at(-1).body), { operation_id: "evidence_bundle", parameters: { identifier: "/lu-legilux/loi-1991-08-10-n3", date: "2026-10-16", language: "fra", article: "art_15" } });
      // On both sides of each clock change (the plan's item 3.4): the day turns at midnight in Luxembourg, 22:00 UTC in
      // summer time and 23:00 UTC in winter time; summer time ends on 25 October 2026 and starts on 28 March 2027. An address
      // that keeps a date is asked at it, whatever the clock says.
      for (const [instant, date] of [
        ["2026-10-24T21:59:59Z", "2026-10-24"], ["2026-10-24T22:00:00Z", "2026-10-25"], ["2026-10-25T22:59:59Z", "2026-10-25"],
        ["2026-10-25T23:00:00Z", "2026-10-26"], ["2027-03-27T22:59:59Z", "2027-03-27"], ["2027-03-27T23:00:00Z", "2027-03-28"],
        ["2027-03-28T21:59:59Z", "2027-03-28"], ["2027-03-28T22:00:00Z", "2027-03-29"],
      ]) {
        clock = instant;
        assert.equal((await fetch(`${origin}${readable}`)).status, 200);
        assert.equal(JSON.parse(asked.at(-1).body).parameters.date, date, `${instant}: today in Luxembourg`);
        assert.equal((await fetch(`${origin}${readable}?date=2024-02-01`)).status, 200);
        assert.equal(JSON.parse(asked.at(-1).body).parameters.date, "2024-02-01", `${instant}: the date the address keeps`);
      }
      clock = "1970-01-01T00:00:00Z";
      const asks = asked.length;
      assert.equal(await (await fetch(`${origin}${readable}`)).text(), "<!doctype html><p>the reading page as built</p>", "a clock outside the rule's range: the page as built, which asks for itself");
      assert.equal(asked.length, asks, "and the server asks nothing");
      assert.equal((await fetch(`${origin}${readable}?date=2024-02-01`)).status, 200);
      assert.equal(JSON.parse(asked.at(-1).body).parameters.date, "2024-02-01", "a date kept needs no clock");
      clock = "2026-10-15T23:30:00Z";

      // R64: the EU shape before it has moved for good, its interface language and query kept; the Luxembourg shape before it
      // names no language, so it is answered as any address the server does not hold.
      const before = await fetch(`${origin}/en/eu-eurlex/32016R0679/eng/2016-05-04--${"a".repeat(64)}?from=card`, { redirect: "manual" });
      assert.equal(before.status, 301);
      assert.equal(before.headers.get("location"), `/en/eu-eurlex/32016R0679/2016-05-04--${"a".repeat(64)}/eng?from=card`);
      assert.equal(before.headers.get("vary"), null, "the same answer for every reader");
      assert.equal((await fetch(`${origin}/en/lu-legilux/loi-1991-08-10-n3/2024-02-01--${"a".repeat(64)}`, { redirect: "manual" })).status, 404);

      answers.set("/api/v3/dossier", { result: null, refusal: { code: "identifier_unknown" } });
      const unknown = await fetch(`${origin}/en/lu-legilux/no-such-work`);
      assert.equal(unknown.status, 404, "a work the API does not know is a page that is not there");
      assert.match(await unknown.text(), /dossier identifier_unknown/);

      answers.delete("/api/v3/evidence_bundle");
      const fallback = await fetch(`${origin}${permalink}`);
      assert.equal(fallback.status, 200);
      assert.equal(await fallback.text(), "<!doctype html><p>the reading page as built</p>", "no answer: the page as built, which asks for itself");

      const french = await fetch(`${origin}/fr/lu-legilux/loi-1991-08-10-n3`);
      assert.equal(french.status, 404, "no French renderer and no French page as built: nothing");
      assert.equal((await fetch(`${origin}/ssr/en.js`)).status, 404, "the renderer is run, never served");
      for (const variant of ["/SSR/en.js", "/Ssr/en.js", "/en/../ssr/en.js", "/ssr%2fen.js"]) {
        assert.equal((await fetch(`${origin}${variant}`)).status, 404, `${variant}: not under another spelling either`);
      }
    } finally {
      await new Promise((resolve) => live.close(resolve));
    }
  } finally {
    await new Promise((resolve) => api.close(resolve));
    await rm(root, { recursive: true, force: true });
  }
});
