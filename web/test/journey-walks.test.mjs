// Journeys J6 and J7 through the site's own pages (the specification of record's R53: "J6 uses search, the not-legal-advice
// line and Get help; J7 uses the Developers page"; `journey.mjs`, `WALKS`), held without a browser: the walks' definitions
// against the pages they walk, a walk's verdict on what a browser would observe, and the Developers page held to the API it
// documents. The walks themselves run in a real browser in CI only (no browser runs on the driver's machine).

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { HomeLinks, renderLiveDevelopersPage, renderLiveHelpPage } from "../.react-build/app.mjs";
import { MCP_PROTOCOL_VERSION, NOT_ADVICE_SAID, WALKS, developersPageFailures, pageAsksNothing, walkFailures } from "../scripts/journey.mjs";
import { readDevelopersContracts } from "../scripts/developers-contracts.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const COVERAGE = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope;
const DEVELOPERS = renderLiveDevelopersPage({ developers: await readDevelopersContracts() });
const HELP = renderLiveHelpPage();
const [J6, J7] = WALKS;

/** What a browser observes of a walk that went as it should. */
const walked = (walk, change = {}) => ({
  answerState: walk.typed === undefined ? "walk" : "success",
  notAdvice: [NOT_ADVICE_SAID],
  followedBy: "pointer",
  location: walk.to,
  text: walk.texts.join("\n"),
  requests: [
    { url: "http://127.0.0.1:1/en/", method: "GET" },
    ...(walk.apiRequests === 1 ? [{ url: "http://127.0.0.1:1/api/v3/search", method: "POST" }] : []),
    { url: `http://127.0.0.1:1${walk.to}`, method: "GET" },
  ],
  requestsAfterFollowing: [{ url: `http://127.0.0.1:1${walk.to}`, method: "GET" }],
  console: [],
  cookie: "",
  ...change,
});

test("J6 walks from the home page's box to Get help, and J7 from the home page to the Developers page, by links the home page holds", () => {
  assert.deepEqual(WALKS.map((walk) => walk.journey), ["J6", "J7"]);
  const links = renderToStaticMarkup(h(HomeLinks));
  for (const walk of WALKS) {
    const href = /a\[href="([^"]+)"\]/.exec(walk.link)[1];
    assert.ok(walk.link.startsWith("[data-home-links] "), `${walk.journey} follows one of the home page's links`);
    assert.ok(links.includes(`<a href="${href}">`), `${walk.journey}: the home page links ${href}`);
    assert.equal(href, walk.to);
  }
  // Each page says the words its walk looks for, asks nothing and is kept out of search engines.
  for (const [walk, page] of [[J6, HELP], [J7, DEVELOPERS]]) {
    const text = page.replace(/<[^>]+>/g, "").replaceAll("&#x27;", "'").replaceAll("&quot;", '"').replaceAll("&amp;", "&");
    for (const said of walk.texts) assert.ok(text.includes(said), `${walk.to} says "${said}"`);
    assert.deepEqual(pageAsksNothing(page), { scripts: 0, forms: 0 }, walk.to);
    assert.deepEqual(walkFailures(walked(walk), walk, { status: 200, bytes: page }), [], walk.name);
  }
  assert.equal(J6.apiRequests, 1, "J6 asks one search");
  assert.equal(J7.apiRequests, 0);
});

test("a walk fails when the page asks the server, ends elsewhere, loses its words, logs, or the home page's search or sentence is missing", () => {
  const help = { status: 200, bytes: HELP };
  const fails = (change, destination = help, walk = J6) => walkFailures(walked(walk, change), walk, destination);
  assert.match(fails({ requests: [...walked(J6).requests, { url: "http://127.0.0.1:1/api/v3/coverage", method: "POST" }] }).join(), /asked the API 2 times, not 1/);
  assert.match(fails({ requestsAfterFollowing: [{ url: "http://127.0.0.1:1/api/v3/coverage" }] }).join(), /following the link asked the API/);
  assert.match(fails({ location: "/en/" }).join(), /ended at \/en\/, not \/en\/help\.html/);
  assert.match(fails({ followedBy: "absent" }).join(), /was not reached/);
  assert.match(fails({ text: "" }).join(), /does not say/);
  assert.match(fails({ console: ["error: boom"] }).join(), /the console said: error: boom/);
  assert.match(fails({ cookie: "a=b" }).join(), /a cookie was set/);
  assert.match(fails({ answerState: "failure" }).join(), /search ended in failure/);
  assert.match(fails({ notAdvice: [] }).join(), /does not say under its box what Lex is not/);
  assert.match(fails({}, { status: 404, bytes: HELP }).join(), /answered 404/);
  assert.match(fails({}, { status: 200, bytes: HELP.replace("</main>", '<script src="/x.js"></script><form></form></main>') }).join(), /1 script\(s\) and 1 form\(s\)/);
  assert.match(fails({}, { status: 200, bytes: HELP.replace('<meta name="robots" content="noindex"/>', "") }).join(), /not kept out of search engines/);
});

test("J7 holds the Developers page to the API: its operations, its schemas and its two examples, sent as printed, one envelope", async () => {
  const posted = [];
  const envelope = structuredClone(COVERAGE);
  const post = async (url, body) => {
    posted.push([new URL(url).pathname, body]);
    if (url.endsWith("/mcp")) {
      return { status: 200, protocolVersion: MCP_PROTOCOL_VERSION, json: { jsonrpc: "2.0", id: body.id, result: { isError: false, content: [{ type: "text", text: JSON.stringify(envelope) }], structuredContent: envelope } } };
    }
    return { status: 200, protocolVersion: null, json: envelope };
  };
  const fetched = [];
  const fetchImpl = async (url) => {
    fetched.push(new URL(url).pathname);
    return { status: 200, text: async () => "{}" };
  };
  assert.deepEqual(await developersPageFailures("http://api", DEVELOPERS, { post, fetchImpl }), []);
  assert.deepEqual(posted.map(([path]) => path), ["/api/v3/coverage", "/api/v3/coverage", "/mcp"], "the coverage answer, then the page's REST example and its MCP message");
  assert.deepEqual(posted[1][1], { operation_id: "coverage", parameters: {} }, "the curl's body, as printed");
  assert.equal(posted[2][1].method, "tools/call");
  assert.ok(fetched.length > 0 && fetched.every((path) => /^\/schemas\/v3-platform\/[a-z0-9-]+\.json$/.test(path)), "every schema the page links");

  // A page that says another operation is served, a schema not served, an MCP answer that differs: each a failure.
  const served = DEVELOPERS.replace(/data-operation="([a-z_]+)" data-served="false"/, 'data-operation="$1" data-served="true"');
  assert.match((await developersPageFailures("http://api", served, { post, fetchImpl })).join(), /are served, and the server serves/);
  assert.match((await developersPageFailures("http://api", DEVELOPERS, { post, fetchImpl: async () => ({ status: 404, text: async () => "no" }) })).join(), /answered 404, not JSON/);
  const differs = async (url, body) => (url.endsWith("/mcp")
    ? { status: 200, protocolVersion: MCP_PROTOCOL_VERSION, json: { result: { isError: false, content: [{ text: JSON.stringify({ ...envelope, verdict: "refuse" }) }], structuredContent: { ...envelope, verdict: "refuse" } } } }
    : post(url, body));
  assert.match((await developersPageFailures("http://api", DEVELOPERS, { post: differs, fetchImpl })).join(), /sent as printed: MCP and REST answered different envelopes/);
  assert.match((await developersPageFailures("http://api", DEVELOPERS.replace(/<pre class="reference-code"><code>curl[\s\S]*?<\/pre>/, ""), { post, fetchImpl })).join(), /prints no REST example/);
});
