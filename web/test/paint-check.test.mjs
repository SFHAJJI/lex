// Prove the journey's colour check (`PAINT_ONLY`) in a real browser before any page depends on it.
//
// The live pages pass it, which says nothing unless it can fail: so it is judged here against two
// fixtures written for the purpose, one whose painted marks all say what they are and one whose marks
// say nothing, painted each way a mark can be painted (a background, a border, an outline, a shadow).
// The border case is the review of #811's: an empty span with a red border passed the first version,
// which looked at backgrounds only. Neither fixture is product content and neither is shipped.

import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

import { Session, allocateDebuggerPort, findBrowser, waitForDebugger } from "../scripts/browser-evidence.mjs";
import { PAINT_ONLY } from "../scripts/journey.mjs";

const page = (body) => `<!doctype html><html lang="en"><head><meta charset="utf-8"><style>
  .mark { display: inline-block; width: 20px; height: 20px; }
  .fill { background: #c00; }
  .edge { border: 2px solid #c00; }
  .ring { outline: 2px solid #c00; }
  .glow { box-shadow: 0 0 0 3px #c00; }
</style></head><body><main>${body}</main></body></html>`;

// Every mark says what it is: in words, in an accessible name, or it is declared decorative.
const NAMED = page(`
  <span class="mark fill">changed</span>
  <span class="mark edge" aria-label="removed"></span>
  <span class="mark ring">added</span>
  <span class="mark glow" aria-label="unchanged"></span>
  <span class="mark fill" aria-hidden="true"></span>
  <p>A sentence on the page's own background, painted by nothing.</p>`);

// The same marks, saying nothing: each is a meaning only a reader who sees its colour receives.
const WORDLESS = page(`
  <span class="mark fill" data-way="background"></span>
  <span class="mark edge" data-way="border"></span>
  <span class="mark ring" data-way="outline"></span>
  <span class="mark glow" data-way="shadow"></span>
  <p>A sentence on the page's own background, painted by nothing.</p>`);

async function main() {
  const browser = await findBrowser();
  const port = allocateDebuggerPort(9800, 300);
  const profile = await mkdtemp(join(tmpdir(), "lex-paint-selftest-"));
  const child = spawn(browser, [
    "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
    "--no-first-run", "--no-default-browser-check",
    "--disable-component-update", "--disable-background-networking",
    "about:blank",
  ], { stdio: "ignore" });
  try {
    await writeFile(join(profile, "named.html"), NAMED, "utf8");
    await writeFile(join(profile, "wordless.html"), WORDLESS, "utf8");
    const session = await Session.open(await waitForDebugger(port));
    const { targetId } = await session.send("Target.createTarget", { url: "about:blank" });
    const { sessionId } = await session.send("Target.attachToTarget", { targetId, flatten: true });
    await session.send("Runtime.enable", {}, sessionId);
    await session.send("Page.enable", {}, sessionId);
    const check = async (file) => {
      await session.send("Page.navigate", { url: pathToFileURL(join(profile, file)).href }, sessionId);
      await new Promise((resolve) => setTimeout(resolve, 250));
      return (await session.send("Runtime.evaluate", { expression: PAINT_ONLY, returnByValue: true }, sessionId)).result.value;
    };

    const named = await check("named.html");
    assert.equal(named.painted, 4, "the four marks a reader can see are painted, the decorative one exempt");
    assert.deepEqual(named.unnamed, [], "every painted mark that says what it is passes");
    console.log("  named fixture   : 4 painted marks, each saying what it is");

    const wordless = await check("wordless.html");
    assert.equal(wordless.painted, 4, "a background, a border, an outline and a shadow are each paint");
    assert.deepEqual(wordless.unnamed, ["span.mark", "span.mark", "span.mark", "span.mark"], "each wordless mark is caught, the border one included");
    console.log("  wordless fixture: 4 painted marks, all caught (background, border, outline, shadow)");
    session.close();
  } finally {
    child.kill();
    await rm(profile, { force: true, recursive: true }).catch(() => {});
  }
}

await main();
