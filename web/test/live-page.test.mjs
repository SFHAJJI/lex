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

import { Document, LIVE_CONTRACT, LIVE_MARKER, SYNTHETIC_MARKER, renderLiveCoveragePage } from "../.react-build/app.mjs";
import { MAXIMUM_REQUEST_BYTES, createLiveServer } from "../scripts/serve-live.mjs";

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
    for (const path of ["/../secret.html", "/..%2fsecret.html", "/%2e%2e%2fsecret.html", "/..\secret.html"]) {
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
