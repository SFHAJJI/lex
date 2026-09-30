// The live page and the one-origin server it is served from.
//
// The page is the loading state under the live banner, with its own script and the census contract;
// the server serves the built page and forwards exactly one kind of request, carrying nothing about
// the caller. Both are measured here without a browser; the browser journey is the next slice.

import assert from "node:assert/strict";
import test from "node:test";
import { createServer } from "node:http";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { renderToString } from "react-dom/server";
import { Document, LIVE_CONTRACT, LIVE_MARKER, SYNTHETIC_MARKER, liveCompareTree, liveCoverageTree, liveDossierTree, liveHistoryTree, liveRadarTree, liveReadingTree, liveSearchTree, liveExportTree, renderLiveComparePage, renderLiveExportPage, renderLiveCoveragePage, renderLiveDossierPage, renderLiveHistoryPage, renderLiveRadarPage, renderLiveReadingPage, renderLiveSearchPage } from "../.react-build/app.mjs";
import { MAXIMUM_REQUEST_BYTES, createLiveServer } from "../scripts/serve-live.mjs";
import { buildLive } from "../scripts/build-live.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));

test("the live page is the loading state under the live banner, with its own script and the platform's contract", () => {
  const html = renderLiveCoveragePage();
  assert.ok(html.startsWith("<!doctype html>"));
  assert.match(html, /data-preview-state="live-coverage"/);
  assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`));
  assert.ok(!html.includes(`data-synthetic="${SYNTHETIC_MARKER}"`), "the synthetic banner would misdescribe a live answer");
  assert.match(html, /id="live-coverage-root"><section data-answer-state="loading">/);
  assert.match(html, /<script src="\/client-live.js" defer=""><\/script>/);
  assert.ok(!html.includes('src="/client.js"'), "the preview pages' bundle is not this page's");
  assert.deepEqual(LIVE_CONTRACT, census.contract, "the contract is the census's, as the platform rendered it");
  assert.match(html, /<title>Trust and Coverage - Lex V3 live<\/title>/, "the title does not call a live page a preview");
});

test("the tree the browser hydrates is the tree the server rendered (review of #765)", () => {
  const html = renderLiveCoveragePage();
  const root = html.slice(html.indexOf('<div id="live-coverage-root">') + '<div id="live-coverage-root">'.length);
  const inner = root.slice(0, root.indexOf("</div>"));
  assert.equal(inner, renderToString(liveCoverageTree()), "a hydration that changed the markup would re-render silently");
});

test("the search page's server render is the tree the browser hydrates", () => {
  const html = renderLiveSearchPage();
  const open = '<div id="live-search-root">';
  const root = html.slice(html.indexOf(open) + open.length);
  assert.ok(root.startsWith(renderToString(liveSearchTree())), "a hydration that changed the markup would re-render silently");
  assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`), "the search page is a live page, under the live banner");
});

test("the export composer page's server render is the tree the browser hydrates", () => {
  const html = renderLiveExportPage();
  const open = '<div id="live-export-root">';
  const root = html.slice(html.indexOf(open) + open.length);
  assert.ok(root.startsWith(renderToString(liveExportTree())), "a hydration that changed the markup would re-render silently");
  assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`), "the export composer page is a live page, under the live banner");
});

test("the radar page's server render is the tree the browser hydrates", () => {
  const html = renderLiveRadarPage();
  const open = '<div id="live-radar-root">';
  const root = html.slice(html.indexOf(open) + open.length);
  assert.ok(root.startsWith(renderToString(liveRadarTree())), "a hydration that changed the markup would re-render silently");
  assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`), "the radar page is a live page, under the live banner");
});

test("the compare page's server render is the tree the browser hydrates", () => {
  const html = renderLiveComparePage();
  const open = '<div id="live-compare-root">';
  const root = html.slice(html.indexOf(open) + open.length);
  assert.ok(root.startsWith(renderToString(liveCompareTree())), "a hydration that changed the markup would re-render silently");
  assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`), "the compare page is a live page, under the live banner");
});

test("the provision history page's server render is the tree the browser hydrates", () => {
  const html = renderLiveHistoryPage();
  const open = '<div id="live-history-root">';
  const root = html.slice(html.indexOf(open) + open.length);
  assert.ok(root.startsWith(renderToString(liveHistoryTree())), "a hydration that changed the markup would re-render silently");
  assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`), "the provision history page is a live page, under the live banner");
});

test("the reading page's server render is the tree the browser hydrates", () => {
  const html = renderLiveReadingPage();
  const open = '<div id="live-reading-root">';
  const root = html.slice(html.indexOf(open) + open.length);
  assert.ok(root.startsWith(renderToString(liveReadingTree())), "a hydration that changed the markup would re-render silently");
  assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`), "the reading page is a live page, under the live banner");
});

test("the dossier page's server render is the tree the browser hydrates", () => {
  const html = renderLiveDossierPage();
  const open = '<div id="live-dossier-root">';
  const root = html.slice(html.indexOf(open) + open.length);
  assert.ok(root.startsWith(renderToString(liveDossierTree())), "a hydration that changed the markup would re-render silently");
  assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`), "the dossier page is a live page, under the live banner");
});

test("the live build writes its own directory, embeds the contract and nothing else of the census (review of #765)", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-live-build-"));
  const url = new URL(`file:///${destination.replaceAll("\\", "/")}/`);
  try {
    await buildLive(url);
    const index = await readFile(join(destination, "index.html"), "utf8");
    const bundle = await readFile(join(destination, "client-live.js"), "utf8");
    assert.match(index, new RegExp(`data-live="${LIVE_MARKER}"`));
    for (const asset of ["styles.css", "favicon.svg"]) await readFile(join(destination, asset));
    assert.ok(bundle.includes(census.contract.registry_sha256), "the contract is embedded");
    for (const entry of census.envelopes) {
      assert.ok(!bundle.includes(entry.scenario), `the census envelope "${entry.scenario}" is not shipped to the browser`);
    }

    const search = await readFile(join(destination, "search.html"), "utf8");
    const searchBundle = await readFile(join(destination, "client-live-search.js"), "utf8");
    assert.match(search, /<script src="\/client-live-search.js" defer=""><\/script>/);
    assert.match(search, new RegExp(`data-live="${LIVE_MARKER}"`));
    assert.ok(searchBundle.includes(census.contract.registry_sha256), "the search bundle embeds the contract");
    for (const entry of census.envelopes) {
      assert.ok(!searchBundle.includes(entry.scenario), `the census envelope "${entry.scenario}" is not shipped with the search page`);
    }

    const dossier = await readFile(join(destination, "dossier.html"), "utf8");
    const dossierBundle = await readFile(join(destination, "client-live-dossier.js"), "utf8");
    assert.ok(dossier.includes('<script src="/client-live-dossier.js" defer=""></script>'));
    assert.match(dossier, new RegExp(`data-live="${LIVE_MARKER}"`));
    assert.ok(dossierBundle.includes(census.contract.registry_sha256), "the dossier bundle embeds the contract");
    for (const entry of census.envelopes) {
      assert.ok(!dossierBundle.includes(entry.scenario), `the census envelope "${entry.scenario}" is not shipped with the dossier page`);
    }

    const reading = await readFile(join(destination, "reading.html"), "utf8");
    const readingBundle = await readFile(join(destination, "client-live-reading.js"), "utf8");
    assert.ok(reading.includes('<script src="/client-live-reading.js" defer=""></script>'));
    assert.match(reading, new RegExp(`data-live="${LIVE_MARKER}"`));
    assert.ok(readingBundle.includes(census.contract.registry_sha256), "the reading bundle embeds the contract");
    for (const entry of census.envelopes) {
      assert.ok(!readingBundle.includes(entry.scenario), `the census envelope "${entry.scenario}" is not shipped with the reading page`);
    }

    const history = await readFile(join(destination, "history.html"), "utf8");
    const historyBundle = await readFile(join(destination, "client-live-history.js"), "utf8");
    assert.ok(history.includes('<script src="/client-live-history.js" defer=""></script>'));
    assert.match(history, new RegExp(`data-live="${LIVE_MARKER}"`));
    assert.ok(historyBundle.includes(census.contract.registry_sha256), "the history bundle embeds the contract");
    for (const entry of census.envelopes) {
      assert.ok(!historyBundle.includes(entry.scenario), `the census envelope "${entry.scenario}" is not shipped with the history page`);
    }

    const compare = await readFile(join(destination, "compare.html"), "utf8");
    const compareBundle = await readFile(join(destination, "client-live-compare.js"), "utf8");
    assert.ok(compare.includes('<script src="/client-live-compare.js" defer=""></script>'));
    assert.match(compare, new RegExp(`data-live="${LIVE_MARKER}"`));
    assert.ok(compareBundle.includes(census.contract.registry_sha256), "the compare bundle embeds the contract");
    for (const entry of census.envelopes) {
      assert.ok(!compareBundle.includes(entry.scenario), `the census envelope "${entry.scenario}" is not shipped with the compare page`);
    }

    const radar = await readFile(join(destination, "radar.html"), "utf8");
    const radarBundle = await readFile(join(destination, "client-live-radar.js"), "utf8");
    assert.ok(radar.includes('<script src="/client-live-radar.js" defer=""></script>'));
    assert.match(radar, new RegExp(`data-live="${LIVE_MARKER}"`));
    assert.ok(radarBundle.includes(census.contract.registry_sha256), "the radar bundle embeds the contract");
    for (const entry of census.envelopes) {
      assert.ok(!radarBundle.includes(entry.scenario), `the census envelope "${entry.scenario}" is not shipped with the radar page`);
    }

    const exported = await readFile(join(destination, "export.html"), "utf8");
    const exportBundle = await readFile(join(destination, "client-live-export.js"), "utf8");
    assert.ok(exported.includes('<script src="/client-live-export.js" defer=""></script>'));
    assert.match(exported, new RegExp(`data-live="${LIVE_MARKER}"`));
    assert.ok(exportBundle.includes(census.contract.registry_sha256), "the export bundle embeds the contract");
    for (const entry of census.envelopes) {
      assert.ok(!exportBundle.includes(entry.scenario), `the census envelope "${entry.scenario}" is not shipped with the export page`);
    }
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});

test("every other page keeps the synthetic banner, and a banner nobody named is refused", () => {
  const synthetic = renderToStaticMarkup(h(Document, { state: "x", title: "X" }, h("p", null, "x")));
  assert.ok(synthetic.includes(`data-synthetic="${SYNTHETIC_MARKER}"`));
  assert.ok(!synthetic.includes("data-live"));
  assert.throws(() => renderToStaticMarkup(h(Document, { state: "x", title: "X", banner: "real" }, null)), /synthetic or the live banner/);
});

async function listen(server) {
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  return `http://127.0.0.1:${server.address().port}`;
}

async function withServers(run) {
  const root = await mkdtemp(join(tmpdir(), "lex-live-"));
  await writeFile(join(root, "index.html"), "<!doctype html><title>live</title>");
  await writeFile(join(root, "client-live.js"), "void 0;");
  const received = [];
  const api = createServer((request, response) => {
    const chunks = [];
    request.on("data", (chunk) => chunks.push(chunk));
    request.on("end", () => {
      received.push({ method: request.method, url: request.url, headers: request.headers, body: Buffer.concat(chunks).toString("utf8") });
      response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
      response.end('{"answered":true}');
    });
  });
  const apiOrigin = await listen(api);
  const live = createLiveServer({ root, apiOrigin });
  const origin = await listen(live);
  try {
    await run({ origin, received, root });
  } finally {
    await new Promise((resolve) => live.close(resolve));
    await new Promise((resolve) => api.close(resolve));
    await rm(root, { recursive: true, force: true });
  }
}

test("the server serves the built page, and nothing outside it", async () => {
  await withServers(async ({ origin }) => {
    const index = await fetch(`${origin}/`);
    assert.equal(index.status, 200);
    assert.equal(index.headers.get("content-type"), "text/html; charset=utf-8");
    assert.equal(index.headers.get("x-content-type-options"), "nosniff");
    assert.equal(index.headers.get("cache-control"), "no-store");
    const script = await fetch(`${origin}/client-live.js`);
    assert.equal(script.headers.get("content-type"), "text/javascript; charset=utf-8");
    for (const path of ["/../package.json", "/%2e%2e/package.json", "/missing.html", "/%E0%A4%A"]) {
      assert.equal((await fetch(`${origin}${path}`)).status, 404, path);
    }
    assert.equal((await fetch(`${origin}/`, { method: "DELETE" })).status, 405);
  });
});

test("a path that climbs out of the built directory is refused, sent raw so no client normalises it first", async () => {
  // fetch resolves dot segments before sending, so the guard is only exercised by a raw request.
  const { request } = await import("node:http");
  const outer = await mkdtemp(join(tmpdir(), "lex-live-outer-"));
  const root = join(outer, "dist-live");
  await (await import("node:fs/promises")).mkdir(root);
  await writeFile(join(root, "index.html"), "<!doctype html>");
  await writeFile(join(outer, "secret.html"), "not for the page");
  const live = createLiveServer({ root, apiOrigin: "http://127.0.0.1:9" });
  const origin = await listen(live);
  const port = new URL(origin).port;
  const raw = (path) => new Promise((resolve, reject) => {
    const outgoing = request({ host: "127.0.0.1", port, path, method: "GET" }, (answer) => {
      answer.resume();
      answer.on("end", () => resolve(answer.statusCode));
    });
    outgoing.on("error", reject);
    outgoing.end();
  });
  try {
    for (const path of ["/../secret.html", "/..%2fsecret.html", "/%2e%2e%2fsecret.html", "/..\\secret.html", "/..%5csecret.html", "//", "//..%2fsecret.html"]) {
      assert.equal(await raw(path), 404, path);
    }
    assert.equal(await raw("/index.html"), 200, "the control: a file inside is served");
  } finally {
    await new Promise((resolve) => live.close(resolve));
    await rm(outer, { recursive: true, force: true });
  }
});

test("an operation request is forwarded with its body and its media type, and nothing about the caller", async () => {
  await withServers(async ({ origin, received }) => {
    const body = JSON.stringify({ operation_id: "coverage", parameters: {} });
    const answer = await fetch(`${origin}/api/v3/coverage`, {
      method: "POST",
      headers: { "content-type": "application/json", cookie: "session=1", referer: "https://elsewhere.invalid/", "user-agent": "tracker/1", "x-forwarded-for": "203.0.113.9" },
      body,
    });
    assert.equal(answer.status, 200);
    assert.equal(answer.headers.get("content-type"), "application/json; charset=utf-8");
    assert.equal(answer.headers.get("x-content-type-options"), "nosniff");
    assert.equal(await answer.text(), '{"answered":true}');
    assert.equal(received.length, 1);
    const [forwarded] = received;
    assert.equal(forwarded.method, "POST");
    assert.equal(forwarded.url, "/api/v3/coverage");
    assert.equal(forwarded.body, body);
    for (const header of ["cookie", "referer", "user-agent", "x-forwarded-for", "origin", "accept"]) {
      assert.equal(forwarded.headers[header], undefined, `${header} reached the API`);
    }
    assert.equal(forwarded.headers["content-type"], "application/json");
  });
});

test("nothing but POST /api/v3/{operation} is forwarded", async () => {
  await withServers(async ({ origin, received }) => {
    const cases = [
      ["POST", "/api/v3/coverage?x=1"],
      ["POST", "/api/v3/Coverage"],
      ["POST", "/api/v3/coverage/more"],
      ["POST", "/api/v3/"],
      ["POST", "/api/v3/cov%65rage"],
      ["POST", "/mcp"],
      ["GET", "/api/v3/coverage"],
      ["PUT", "/api/v3/coverage"],
    ];
    for (const [method, path] of cases) {
      const answer = await fetch(`${origin}${path}`, { method, body: method === "GET" ? undefined : "{}" });
      assert.equal(answer.status, 404, `${method} ${path}`);
      assert.equal(answer.headers.get("content-type"), "application/problem+json");
    }
    assert.equal(received.length, 0, "none of them reached the API");
  });
});

test("a body over the API's ceiling is refused before it is forwarded, and an unreachable API is a transport problem", async () => {
  await withServers(async ({ origin, received }) => {
    const large = await fetch(`${origin}/api/v3/coverage`, { method: "POST", headers: { "content-type": "application/json" }, body: "x".repeat(MAXIMUM_REQUEST_BYTES + 1) });
    assert.equal(large.status, 413);
    assert.equal(received.length, 0);

    // A chunked body declares no length, so only the running count can stop it (review of #765).
    const { request } = await import("node:http");
    const port = new URL(origin).port;
    const status = await new Promise((resolve, reject) => {
      const outgoing = request({ host: "127.0.0.1", port, path: "/api/v3/coverage", method: "POST", headers: { "content-type": "application/json", "transfer-encoding": "chunked" } }, (answer) => {
        answer.resume();
        answer.on("end", () => resolve(answer.statusCode));
      });
      outgoing.on("error", (error) => (error.code === "EPIPE" || error.code === "ECONNRESET" ? resolve(413) : reject(error)));
      const chunk = Buffer.alloc(64 * 1024, 120);
      for (let sent = 0; sent <= MAXIMUM_REQUEST_BYTES; sent += chunk.length) outgoing.write(chunk);
      outgoing.end();
    });
    assert.equal(status, 413, "a chunked body over the ceiling");
    assert.equal(received.length, 0, "and it never reached the API");
  });

  const root = await mkdtemp(join(tmpdir(), "lex-live-"));
  const closed = createServer();
  const deadOrigin = await listen(closed);
  await new Promise((resolve) => closed.close(resolve));
  const live = createLiveServer({ root, apiOrigin: deadOrigin });
  const origin = await listen(live);
  try {
    const answer = await fetch(`${origin}/api/v3/coverage`, { method: "POST", headers: { "content-type": "application/json" }, body: "{}" });
    assert.equal(answer.status, 502);
    assert.equal(answer.headers.get("content-type"), "application/problem+json");
    assert.equal((await answer.json()).code, "api_unreachable");
  } finally {
    await new Promise((resolve) => live.close(resolve));
    await rm(root, { recursive: true, force: true });
  }
});

test("an API that accepts and never answers is a timeout the page is told about (review of #765)", async () => {
  const root = await mkdtemp(join(tmpdir(), "lex-live-"));
  const silent = createServer(() => { /* accepts the request and never answers */ });
  const apiOrigin = await listen(silent);
  const live = createLiveServer({ root, apiOrigin, apiDeadlineMs: 200 });
  const origin = await listen(live);
  try {
    const answer = await fetch(`${origin}/api/v3/coverage`, { method: "POST", headers: { "content-type": "application/json" }, body: "{}" });
    assert.equal(answer.status, 504);
    assert.equal((await answer.json()).code, "api_timeout");
  } finally {
    await new Promise((resolve) => live.close(resolve));
    silent.closeAllConnections();
    await new Promise((resolve) => silent.close(resolve));
    await rm(root, { recursive: true, force: true });
  }
});

test("no live page says synthetic in its text, since a live page may stand over a real mount (Decision 95, ruling 3)", () => {
  for (const [name, render] of [
    ["coverage", renderLiveCoveragePage], ["search", renderLiveSearchPage], ["dossier", renderLiveDossierPage],
    ["reading", renderLiveReadingPage], ["history", renderLiveHistoryPage], ["compare", renderLiveComparePage], ["radar", renderLiveRadarPage],
    ["export", renderLiveExportPage],
  ]) {
    const html = render();
    const text = html.slice(html.indexOf("<body")).replace(/<[^>]+>/g, " ");
    assert.doesNotMatch(text, /synthetic/i, `${name}: the visible text never calls the page synthetic`);
    assert.match(html, new RegExp(`data-live="${LIVE_MARKER}"`), `${name}: under the live banner`);
  }
});
