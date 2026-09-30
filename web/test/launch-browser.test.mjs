// The browser launcher (`launchBrowser`), held to the failure it replaced: two browsers started at the
// same moment each get a debugging port of their own and answer, and a browser that dies at once is
// reported by its exit code instead of as a silence.

import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { findBrowser, launchBrowser } from "../scripts/browser-evidence.mjs";

test("two browsers started together each bind their own debugging port and answer", async () => {
  const browser = await findBrowser();
  const profiles = await Promise.all([1, 2].map((n) => mkdtemp(join(tmpdir(), `lex-launch-${n}-`))));
  const launched = [];
  try {
    // Settled, not raced: a browser that started is recorded even when the other launch fails, so the
    // cleanup below ends it (review of #822: `Promise.all` rejected first and left one running).
    const settled = await Promise.allSettled(profiles.map((profile) => launchBrowser(browser, profile)));
    launched.push(...settled.filter((one) => one.status === "fulfilled").map((one) => one.value));
    for (const one of settled) if (one.status === "rejected") throw one.reason;
    const ports = launched.map(({ url }) => new URL(url).port);
    assert.equal(new Set(ports).size, 2, `two ports, not one shared: ${ports.join(", ")}`);
    for (const { url } of launched) assert.match(url, /^ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\//);
  } finally {
    for (const { child } of launched) child.kill();
    await new Promise((resolve) => setTimeout(resolve, 500));
    for (const profile of profiles) await rm(profile, { recursive: true, force: true }).catch(() => {});
  }
});

test("a browser that exits before opening its port is reported with its exit code", async () => {
  const profile = await mkdtemp(join(tmpdir(), "lex-launch-dead-"));
  try {
    // Node standing in for a browser that dies at once: it refuses the browser's flags and exits.
    await assert.rejects(launchBrowser(process.execPath, profile, [], { deadlineMs: 10000 }), /exited \(\d+\) before it opened its debugging port/);
  } finally {
    await rm(profile, { recursive: true, force: true }).catch(() => {});
  }
});
