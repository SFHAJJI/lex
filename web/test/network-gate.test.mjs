// The bounded-network browser gate, held without a browser.
//
// `networkFailures` turns what the browser reported for one navigation into failure sentences. The
// browser run proves the capture; these tests prove the verdict: a request after the page settled,
// a response of the wrong media type and an error status each fail and name the path, and the shape
// every built page has today passes.

import assert from "node:assert/strict";
import test from "node:test";

const WHERE = "search-react.html @390/light";
const ORIGIN = "http://127.0.0.1:5555";

const request = (path, type) => ({ kind: "request", type, url: `${ORIGIN}${path}` });
const response = (path, type, mime, status = 200) => ({ kind: "response", type, url: `${ORIGIN}${path}`, status, mime });

// What search-react.html loads today, once `client.js` is served as JavaScript.
const SETTLED = [
  request("/search-react.html", "Document"),
  response("/search-react.html", "Document", "text/html"),
  request("/styles.css", "Stylesheet"),
  response("/styles.css", "Stylesheet", "text/css"),
  request("/client.js", "Script"),
  response("/client.js", "Script", "text/javascript"),
  request("/fonts/inter-400-latin.woff2", "Font"),
  response("/fonts/inter-400-latin.woff2", "Font", "font/woff2"),
];

async function gate() {
  const { networkFailures } = await import("../scripts/browser-evidence.mjs");
  return networkFailures;
}

test("the settled shape of a built page passes", async () => {
  const networkFailures = await gate();
  assert.deepEqual(networkFailures(WHERE, SETTLED, SETTLED.length), []);
});

test("a request after the page settled fails and names the path", async () => {
  const networkFailures = await gate();
  const events = [...SETTLED, request("/pages.json", "Fetch"), response("/pages.json", "Fetch", "application/json")];
  const failures = networkFailures(WHERE, events, SETTLED.length);
  assert.equal(failures.length, 1, JSON.stringify(failures));
  assert.match(failures[0], /1 request\(s\) after the page settled.*: \/pages\.json$/);
});

test("the browser's own favicon request is not the page reaching out", async () => {
  const networkFailures = await gate();
  const events = [...SETTLED, request("/favicon.svg", "Other"), response("/favicon.svg", "Other", "image/svg+xml")];
  assert.deepEqual(networkFailures(WHERE, events, SETTLED.length), []);
});

test("an asset answered with the wrong media type fails, whatever its status", async () => {
  const networkFailures = await gate();
  for (const [path, type, mime] of [
    ["/client.js", "Script", "application/octet-stream"],
    ["/images/missing.png", "Image", "text/html"],
    ["/fonts/missing.woff2", "Font", "text/html"],
    ["/styles.css", "Stylesheet", "text/plain"],
  ]) {
    const failures = networkFailures(WHERE, [request(path, type), response(path, type, mime)], 2);
    assert.equal(failures.length, 1, `${path}: ${JSON.stringify(failures)}`);
    assert.ok(
      failures[0].includes(`${/^[AEIOU]/.test(type) ? "an" : "a"} ${type} request for ${path} was answered with ${mime}`),
      failures[0],
    );
  }
});

test("an error status fails and names the path", async () => {
  const networkFailures = await gate();
  const failures = networkFailures(WHERE, [response("/fonts/gone.woff2", "Font", "text/plain", 404)], 1);
  assert.deepEqual(failures, [`${WHERE}: a Font request for /fonts/gone.woff2 was answered 404`]);
});

test("a data: URL and an untyped request are not held to a media type", async () => {
  const networkFailures = await gate();
  const events = [
    { kind: "response", type: "Image", url: "data:image/png;base64,AAAA", status: 200, mime: "image/png" },
    response("/whatever", "Other", "application/octet-stream"),
  ];
  assert.deepEqual(networkFailures(WHERE, events, events.length), []);
});
