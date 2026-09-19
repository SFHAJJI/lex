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
  assert.deepEqual(failures, [
    `${WHERE}: 1 request(s) after the page settled, during the tab walk, the driven actions or the ` +
      "minute of page time run after them: /pages.json",
  ]);
});

test("the browser's own favicon request is not the page reaching out", async () => {
  const networkFailures = await gate();
  const events = [...SETTLED, request("/favicon.svg", "Other"), response("/favicon.svg", "Other", "image/svg+xml")];
  assert.deepEqual(networkFailures(WHERE, events, SETTLED.length), []);
});

test("a page script fetching the favicon's path is still the page reaching out", async () => {
  const networkFailures = await gate();
  const events = [...SETTLED, request("/favicon.svg", "Fetch"), response("/favicon.svg", "Fetch", "application/json")];
  const failures = networkFailures(WHERE, events, SETTLED.length);
  assert.equal(failures.length, 1, JSON.stringify(failures));
  assert.match(failures[0], /: 1 request\(s\) after the page settled.*: \/favicon\.svg$/);
});

test("an icon link swapped to carry a query string is the page reaching out", async () => {
  const networkFailures = await gate();
  const events = [...SETTLED, request("/favicon.svg?leak=1", "Other")];
  const failures = networkFailures(WHERE, events, SETTLED.length);
  assert.equal(failures.length, 1, JSON.stringify(failures));
  assert.match(failures[0], /: 1 request\(s\) after the page settled.*: \/favicon\.svg$/);
});

test("a clock whose wait runs out while the policy is unanswered is a named failure, not a crash", async () => {
  const { runPageClock } = await import("../scripts/browser-evidence.mjs");
  const unhandled = [];
  const onUnhandled = (reason) => unhandled.push(reason);
  process.on("unhandledRejection", onUnhandled);
  try {
    // The wait gives up after 10 ms; the policy command answers after 50 ms.
    const session = {
      waitFor: () => new Promise((_, reject) => setTimeout(() => reject(new Error("no budget event")), 10)),
      send: () => new Promise((resolve) => setTimeout(resolve, 50)),
    };
    assert.equal(await runPageClock(session, "s", 1000), "no budget event");
    await new Promise((resolve) => setTimeout(resolve, 20));
    assert.deepEqual(unhandled, []);
    // A policy the browser refuses is named the same way, and a clock that runs returns null.
    const refused = { waitFor: () => new Promise(() => {}), send: async () => { throw new Error("refused"); } };
    assert.equal(await runPageClock(refused, "s", 1000), "refused");
    const runs = { waitFor: async () => ({}), send: async () => ({}) };
    assert.equal(await runPageClock(runs, "s", 1000), null);
  } finally {
    process.off("unhandledRejection", onUnhandled);
  }
});

test("media, text tracks, manifests and event streams are held to their own types", async () => {
  const networkFailures = await gate();
  for (const [path, type, good, bad] of [
    ["/clip.mp4", "Media", "video/mp4", "text/html"],
    ["/captions.vtt", "TextTrack", "text/vtt", "text/html"],
    ["/app.webmanifest", "Manifest", "application/manifest+json", "text/html"],
    ["/events", "EventSource", "text/event-stream", "text/html"],
  ]) {
    assert.deepEqual(networkFailures(WHERE, [request(path, type), response(path, type, good)], 2), [], path);
    const failures = networkFailures(WHERE, [request(path, type), response(path, type, bad)], 2);
    assert.equal(failures.length, 1, `${path}: ${JSON.stringify(failures)}`);
    assert.ok(failures[0].includes(`request for ${path} was answered with ${bad}`), failures[0]);
  }
});

test("a response of a kind the gate has no media type for fails rather than passing unjudged", async () => {
  const networkFailures = await gate();
  for (const type of ["WebSocket", "Ping", "Prefetch"]) {
    const failures = networkFailures(WHERE, [response("/x", type, "application/octet-stream")], 1);
    assert.deepEqual(failures, [
      `${WHERE}: a ${type} request for /x is of a kind no page here makes, and the gate has no media ` +
        "type to judge its answer by",
    ]);
  }
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

test("a data: URL and the browser's own untyped requests are not held to a media type", async () => {
  const networkFailures = await gate();
  const events = [
    { kind: "response", type: "Image", url: "data:image/png;base64,AAAA", status: 200, mime: "image/png" },
    response("/whatever", "Other", "application/octet-stream"),
  ];
  assert.deepEqual(networkFailures(WHERE, events, events.length), []);
});
