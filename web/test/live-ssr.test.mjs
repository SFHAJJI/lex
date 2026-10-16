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
import { tmpdir } from "node:os";
import { join } from "node:path";
import vm from "node:vm";
import { renderToString } from "react-dom/server";

import {
  LIVE_CONTRACT, LIVE_DOSSIER_ROOT, LIVE_READING_ROOT, liveDossierTree, liveReadingTree, renderLiveDossierPage, renderLiveReadingPage,
} from "../.react-build/app.mjs";
import { INITIAL_ANSWER_ID, addressBeforeAnswer, jsonInScript, lawPathOf, readInitialAnswer, readingAddressShown } from "../scripts/initial-answer.mjs";
import { parseSitePath, sitePageFor } from "../scripts/permalinks.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { answerHandedWith } from "../scripts/v3-client.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";
import { buildLive } from "../scripts/build-live.mjs";
import { LIVE_CHROME, fillCounted, fillText } from "../scripts/live-chrome.mjs";
import { luxembourgDate } from "../scripts/temporal.mjs";

const TABLE = JSON.parse(await readFile(new URL("../../tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json", import.meta.url), "utf8"));
const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart) => census.envelopes.find((entry) => entry.operation === operation && entry.scenario.startsWith(scenarioStart)).envelope;
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const COVERAGE = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope;
const STATE = READING.result.value.states[0];

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
  assert.ok(html.includes(`<link rel="canonical" href="https://law.soufien.lu${STATE.permalink}"/>`), "the canonical is the state the page shows, with no interface language");
  assert.ok(html.includes(`hrefLang="fr" href="https://law.soufien.lu/fr${STATE.permalink}"`), "and so is each language's alternate");
  assert.ok(!html.slice(0, html.indexOf("</head>")).includes("0".repeat(64)), "the digest it was opened at is named nowhere in the head");
  const exact = renderLiveReadingPage({ initial: { envelope: READING, path: `/en${STATE.permalink}` } });
  assert.ok(exact.includes(`<link rel="canonical" href="https://law.soufien.lu${STATE.permalink}"/>`), "a permalink that holds names itself");
});

test("a law page under /fr/ or /en/ names the permalink with no interface language as canonical, each language and x-default (R64)", () => {
  const article = `${STATE.permalink}/${STATE.articles[14].publisher_id}`;
  for (const language of ["en", "fr"]) {
    const html = renderLiveReadingPage({ initial: { envelope: READING, path: `/${language}${article}` } });
    const head = html.slice(0, html.indexOf("</head>"));
    assert.equal((head.match(/rel="canonical"/g) ?? []).length, 1, `/${language}/: one canonical link`);
    assert.ok(head.includes(`<link rel="canonical" href="https://law.soufien.lu${article}"/>`), `/${language}/: the article's own permalink, with no interface language`);
    for (const [hrefLang, href] of [["fr", `/fr${article}`], ["en", `/en${article}`], ["x-default", article]]) {
      assert.ok(head.includes(`<link rel="alternate" hrefLang="${hrefLang}" href="https://law.soufien.lu${href}"/>`), `/${language}/: ${hrefLang}`);
    }
    assert.ok(head.includes('http-equiv="Content-Security-Policy"'), "the page's own policy is untouched");
  }
});

test("a reading's page names as canonical an address the platform mints, never the request's own, as the shared table says (review of #967)", () => {
  const origin = "https://law.soufien.lu";
  const rows = TABLE.paths.filter((row) => row.canonical !== undefined);
  assert.ok(rows.length >= 5, "the article minted, escaped otherwise, not held, none, and a stale permalink's");
  const refused = envelopeOf("evidence_bundle", "a date before the work's history");
  const view = readingOutcome(answerHandedWith("evidence_bundle", READING, { contract: LIVE_CONTRACT, objectType: "evidence_bundle" })).view;
  for (const { path, canonical } of rows) {
    const html = renderLiveReadingPage({ initial: { envelope: READING, path } });
    const head = html.slice(0, html.indexOf("</head>"));
    assert.deepEqual([...head.matchAll(/<link rel="canonical" href="([^"]*)"\/>/g)].map((match) => match[1]), [canonical], `${path}: one canonical link`);
    const law = canonical.slice(origin.length);
    assert.deepEqual(
      [...head.matchAll(/<link rel="alternate" hrefLang="([^"]*)" href="([^"]*)"\/>/g)].map(([, hrefLang, href]) => [hrefLang, href]),
      [["fr", `${origin}/fr${law}`], ["en", `${origin}/en${law}`], ["x-default", canonical]],
      `${path}: each language once, and x-default`,
    );
    // The page as a browser loads it names the same once its answer is read (`LiveReading.jsx`), and before that, as built,
    // the state's permalink with no article (`client.jsx`), an address the platform mints as well.
    assert.equal(readingAddressShown(view, lawPathOf(path)), law, `${path}: once loaded`);
    assert.equal(addressBeforeAnswer(path), parseSitePath(path).permalink, `${path}: as built`);
    assert.equal(parseSitePath(addressBeforeAnswer(path)).anchor, null, `${path}: as built, no article`);
    // A refusal shows no state: its page names the state's permalink the address pins, with no article.
    const refusal = renderLiveReadingPage({ initial: { envelope: refused, path } });
    assert.ok(refusal.includes(`<link rel="canonical" href="${origin}${parseSitePath(path).permalink}"/>`), `${path}: a refusal's page`);
  }

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
  for (const { path, render } of TABLE.paths) {
    const page = sitePageFor(path);
    assert.deepEqual(page === null ? null : { ...page, parameters: { ...page.parameters } }, render, path);
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
  const initial = { envelope: READING, path: `/en${STATE.permalink}/${STATE.articles[14].publisher_id}` };
  const html = renderLiveReadingPage({ initial });
  assert.match(html, /<section data-answer-state="success">/, "the reading, without any script");
  assert.equal((html.match(/<blockquote/g) ?? []).length, STATE.articles.length, "every article quoted in the page itself");
  assert.ok(html.includes(`value="${STATE.applicability_date}"`), "the form names the date the address names");
  assert.ok(!html.includes("data-pin-replaced"), "the state served is the one the permalink pins");
  assert.equal(rootOf(html, LIVE_READING_ROOT), renderToString(liveReadingTree(initial)), "hydration changes nothing");
  const block = html.slice(html.indexOf(`id="${INITIAL_ANSWER_ID}">`) + `id="${INITIAL_ANSWER_ID}">`.length, html.indexOf("</script>", html.indexOf(INITIAL_ANSWER_ID)));
  assert.deepEqual(JSON.parse(block), READING, "the page carries the answer it was rendered from");

  const stale = { envelope: READING, path: `/en${STATE.permalink.replace(/--[0-9a-f]{64}\//, `--${"0".repeat(64)}/`)}` };
  const staleHtml = renderLiveReadingPage({ initial: stale });
  assert.ok(staleHtml.includes("data-pin-replaced"), "a stale permalink's notice is in the page itself");
  assert.equal(rootOf(staleHtml, LIVE_READING_ROOT), renderToString(liveReadingTree(stale)));

  assert.ok(!renderLiveReadingPage().includes(INITIAL_ANSWER_ID), "the page as built carries no answer");

  // The page names as canonical its own address with no interface language, its article included, and the same address
  // in each language, the language-neutral permalink (which sends a reader to their language) for any other (R61, R64, R67).
  const origin = "https://law.soufien.lu";
  const article = `${STATE.permalink}/${STATE.articles[14].publisher_id}`;
  assert.ok(html.includes(`<link rel="canonical" href="${origin}${article}"/>`), "canonical: this article's permalink");
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
      assert.equal((html.match(/<blockquote/g) ?? []).length, STATE.articles.length, `${language}: the reading, quoted`);
      assert.throws(() => context.lexSsr.render("history", "{}", "/en/x"), /no server-rendered history page/);

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
    const live = createLiveServer({ root, apiOrigin: `http://127.0.0.1:${api.address().port}` });
    const origin = await new Promise((resolve) => live.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${live.address().port}`)));
    try {
      const permalink = `/en/lu-legilux/loi-1991-08-10-n3/2024-02-01--${"a".repeat(64)}/fra/art_15`;
      answers.set("/api/v3/evidence_bundle", { result: { value: {} }, refusal: null });
      const page = await fetch(`${origin}${permalink}`);
      assert.equal(page.status, 200);
      assert.equal(await page.text(), `<!doctype html><p>reading answer ${permalink}</p>`, "rendered from the API's answer");
      assert.deepEqual(JSON.parse(asked.at(-1).body), { operation_id: "evidence_bundle", parameters: { identifier: "/lu-legilux/loi-1991-08-10-n3", date: "2024-02-01", language: "fra" } }, "asked as the page itself would ask, in the text's language the permalink names");

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
