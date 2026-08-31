// Browser discovery must be deterministic on every supported CI host.
//
// The candidate list was Windows-only, so `findBrowser()` threw on the Ubuntu runner and the
// required `web` check failed after 63 tests had passed. It was looking for
// `C:/Program Files/...` on Linux. These tests fail if Linux discovery regresses to that.

import assert from "node:assert/strict";
import test from "node:test";


test("every supported CI platform declares real browser candidates", async () => {
  const { browserCandidates } = await import("../scripts/browser-evidence.mjs");

  for (const [platform, expectedPrefix] of [
    ["win32", "C:/"],
    ["linux", "/"],
    ["darwin", "/Applications/"],
  ]) {
    const candidates = browserCandidates(platform);
    assert.ok(candidates.length > 0, `${platform} declares no browser candidates`);
    assert.ok(
      candidates.every((c) => c.startsWith(expectedPrefix)),
      `${platform} candidates are not ${expectedPrefix} paths: ${JSON.stringify(candidates)}`,
    );
  }
});

test("linux discovery names a real Chromium executable, not a Windows path", async () => {
  const { browserCandidates } = await import("../scripts/browser-evidence.mjs");
  const linux = browserCandidates("linux");

  assert.ok(
    linux.some((c) => /chrome|chromium/i.test(c)),
    `linux candidates name no Chromium: ${JSON.stringify(linux)}`,
  );
  assert.ok(
    !linux.some((c) => c.includes("Program Files")),
    "linux discovery still points at a Windows install path",
  );
});

test("an unsupported platform fails with a reason rather than an empty search", async () => {
  const { findBrowser } = await import("../scripts/browser-evidence.mjs");

  await assert.rejects(
    () => findBrowser("plan9"),
    /no browser candidates are declared for platform plan9/,
  );
});

// ---- debugger port allocation ---------------------------------------------------------
//
// `9800 + random*300` could yield 10080, which WHATWG Fetch refuses to connect to. Chrome
// launched, fetch was forbidden from querying it, and the run waited twenty seconds and
// reported that the debugger never answered. Identical trees went green or red on a dice
// roll. These tests make that port unreachable by construction.

test("no allocator can return a port Fetch blocks, over the whole range", async () => {
  const { allocateDebuggerPort, FETCH_BLOCKED_PORTS } =
    await import("../scripts/browser-evidence.mjs");

  for (const [start, count] of [[9222, 500], [9800, 300]]) {
    // Drive the draw across every index rather than sampling, so this is exhaustive.
    for (let index = 0; index < count; index += 1) {
      const port = allocateDebuggerPort(start, count, () => index / count);
      assert.ok(
        !FETCH_BLOCKED_PORTS.has(port),
        `allocator returned blocked port ${port} from ${start}..${start + count - 1}`,
      );
      assert.ok(port >= start && port < start + count, `port ${port} left its range`);
    }
  }
});

test("10080 is the port that caused the incident and is excluded", async () => {
  const { allocateDebuggerPort, FETCH_BLOCKED_PORTS } =
    await import("../scripts/browser-evidence.mjs");

  assert.ok(FETCH_BLOCKED_PORTS.has(10080), "10080 must be known to be blocked");

  // 10080 sits inside 9800..10099. Every draw must skip it.
  const drawn = new Set();
  for (let index = 0; index < 300; index += 1) {
    drawn.add(allocateDebuggerPort(9800, 300, () => index / 300));
  }
  assert.ok(!drawn.has(10080), "10080 was still reachable");
  assert.ok(drawn.has(10079) && drawn.has(10081), "the neighbours are still reachable");
});

test("a range consisting only of blocked ports fails loudly", async () => {
  const { allocateDebuggerPort } = await import("../scripts/browser-evidence.mjs");

  assert.throws(
    () => allocateDebuggerPort(10080, 1),
    /every port in 10080\.\.10080 is blocked/,
  );
});
