// Prove the keyboard checker before any page depends on it.
//
// The three envelope-free pages have no interactive elements, so `focusableCount` is
// zero and the keyboard criteria are vacuously satisfied. A checker that has only ever
// run against pages with nothing to focus has never been exercised, and the first page
// it actually judges would also be the first page it has ever judged.
//
// So it is judged here, against two fixtures written for the purpose: one page that
// behaves and one that does not. Neither is product content and neither is shipped.

import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawn } from "node:child_process";
import { pathToFileURL } from "node:url";

import { Session, allocateDebuggerPort, findBrowser, keyboardWalk, waitForDebugger } from "../scripts/browser-evidence.mjs";

const GOOD = `<!doctype html><html lang="en"><head><meta charset="utf-8"><style>
  a:focus-visible { outline: 3px solid #05f; outline-offset: 2px; }
</style></head><body><main>
  <a href="#one">one</a> <a href="#two">two</a> <a href="#three">three</a>
</main></body></html>`;

// Identical, except the focus indicator is suppressed. This is the single most common
// real defect in this area and the one a count-based check cannot see.
const NO_RING = GOOD.replace("outline: 3px solid #05f; outline-offset: 2px;", "outline: none;");

async function main() {
  const browser = await findBrowser();
  const port = allocateDebuggerPort(9800, 300);
  const profile = await mkdtemp(join(tmpdir(), "lex-cdp-selftest-"));
  const child = spawn(
    browser,
    [
      "--headless=new",
      `--remote-debugging-port=${port}`,
      `--user-data-dir=${profile}`,
      "--no-first-run",
      "--no-default-browser-check",
      "about:blank",
    ],
    { stdio: "ignore" },
  );

  try {
    await writeFile(join(profile, "good.html"), GOOD, "utf8");
    await writeFile(join(profile, "no-ring.html"), NO_RING, "utf8");

    const session = await Session.open(await waitForDebugger(port));
    const { targetId } = await session.send("Target.createTarget", { url: "about:blank" });
    const { sessionId } = await session.send("Target.attachToTarget", { targetId, flatten: true });
    await session.send("Runtime.enable", {}, sessionId);
    await session.send("Page.enable", {}, sessionId);

    const walk = async (file) => {
      await session.send("Page.navigate", { url: pathToFileURL(join(profile, file)).href }, sessionId);
      await new Promise((resolve) => setTimeout(resolve, 250));
      return keyboardWalk(session, sessionId, 3);
    };

    const good = await walk("good.html");
    assert.equal(good.length, 3, "every link must be reachable by Tab");
    assert.deepEqual(
      good.map((stop) => stop.text),
      ["one", "two", "three"],
      "Tab order must follow document order",
    );
    assert.ok(
      good.every((stop) => stop.focusVisible),
      "every focus stop must show a visible indicator",
    );
    console.log("  good fixture   : 3 stops, in order, all with a visible focus ring");

    const bad = await walk("no-ring.html");
    assert.equal(bad.length, 3, "the links are still reachable");
    assert.equal(
      bad.filter((stop) => stop.focusVisible).length,
      0,
      "the checker must notice that no focus indicator is drawn",
    );
    console.log("  no-ring fixture: 3 stops, none with a visible focus ring, correctly detected");

    session.close();
    console.log("\nkeyboard checker proven: it passes a good page and fails a bad one");
  } finally {
    child.kill();
    await rm(profile, { force: true, recursive: true }).catch(() => {});
  }
}

await main();
