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
