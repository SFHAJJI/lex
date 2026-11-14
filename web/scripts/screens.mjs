// Screenshots of the live site for the driver's design review (2026-11-13; the owner's first screens of 2026-11-14), after
// their answers load, saved as PNG. Runs only on GitHub's runners (never on the driver's PC: no local browser). Asks the public
// site what any reader may ask; the assistant is asked the questions listed here, as a reader would ask them.
// Usage: node web/scripts/screens.mjs <origin> <out directory>
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Session, findBrowser, launchBrowser } from "./browser-evidence.mjs";

const [origin = "https://law.soufien.lu", out = "screens"] = process.argv.slice(2);
console.log(`origin ${origin}`);
const q = (text, lang) => `#q=${encodeURIComponent(text)}&lang=${lang}`;
const ask = (text) => `#q=${encodeURIComponent(text)}`;
// The owner's order of 2026-11-14: the first screen of three pages at 1366 by 768, judged by screenshot (home, an answer,
// the law page); each also whole at 1366 (the law page under three screens), and the three on a phone.
// [name, path, width, height, mobile, whole page]
const PRESCRIPTION = ask("Quel est le délai de prescription des actions dérivant d'un contrat d'assurance ?");
const LAW = "/lu-legilux/loi-1997-07-27-n10";
const SHOTS = [
  ["1-fr-home--1366x768", "/fr/", 1366, 768, false, false],
  ["2-fr-answer-prescription--1366x768", `/fr/assistant${PRESCRIPTION}`, 1366, 768, false, false],
  ["3-fr-law-page--1366x768", `/fr${LAW}`, 1366, 768, false, false],
  ["fr-home--1366-whole", "/fr/", 1366, 768, false, true],
  ["fr-answer-prescription--1366-whole", `/fr/assistant${PRESCRIPTION}`, 1366, 768, false, true],
  ["fr-law-page--1366-whole", `/fr${LAW}`, 1366, 768, false, true],
  ["en-law-page--1366x768", `/en${LAW}`, 1366, 768, false, false],
  ["fr-law-page-changes-44--1366x768", `/fr${LAW}#changes-art_44`, 1366, 768, false, false],
  ["fr-home--phone", "/fr/", 390, 844, true, false],
  ["fr-answer-prescription--phone", `/fr/assistant${PRESCRIPTION}`, 390, 844, true, false],
  ["fr-law-page--phone", `/fr${LAW}`, 390, 844, true, false],
];
const settle = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

await mkdir(out, { recursive: true });
const browser = await findBrowser();
const profile = await mkdtemp(join(tmpdir(), "screens-"));
const { child, url } = await launchBrowser(browser, profile);
const session = await Session.open(url);
const report = [];
try {
  const { targetId } = await session.send("Target.createTarget", { url: "about:blank" });
  const { sessionId } = await session.send("Target.attachToTarget", { targetId, flatten: true });
  await session.send("Page.enable", {}, sessionId);
  await session.send("Runtime.enable", {}, sessionId);
  const evaluate = async (expression) =>
    (await session.send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true }, sessionId)).result?.value;
  // A first screen is the viewport as it opens; a whole page, its full height. Each is reported with the page's height and
  // any English in sight (an element marked English that is not inside a closed disclosure).
  const shoot = async (name, width, height, mobile, whole) => {
    const tall = Number(await evaluate("document.documentElement.scrollHeight")) || height;
    const english = await evaluate(`(() => {
      const seen = [...document.querySelectorAll('main [lang="en"]')].filter((node) => node.getClientRects().length > 0);
      return seen.length + (seen.length === 0 ? '' : ': ' + seen.slice(0, 4).map((node) => node.textContent.trim().slice(0, 50)).join(' | '));
    })()`);
    if (whole) {
      await session.send("Emulation.setDeviceMetricsOverride", { width, height: Math.min(9000, Math.max(height, tall)), deviceScaleFactor: 1, mobile }, sessionId);
      await settle(500);
    } else {
      await evaluate("window.scrollTo(0, 0)");
    }
    const { data } = await session.send("Page.captureScreenshot", { format: "png", captureBeyondViewport: whole }, sessionId);
    await writeFile(join(out, `${name}.png`), Buffer.from(data, "base64"));
    report.push(`${name}.png ${width}x${whole ? tall : height}; page height ${tall} px (${(tall / height).toFixed(2)} screens); English in sight ${english}`);
  };
  const open = async (path, width, height, mobile) => {
    await session.send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: 1, mobile }, sessionId);
    const blank = session.waitFor("Page.loadEventFired", sessionId, 60000);
    await session.send("Page.navigate", { url: "about:blank" }, sessionId);
    await blank;
    const loaded = session.waitFor("Page.loadEventFired", sessionId, 60000);
    await session.send("Page.navigate", { url: origin + path }, sessionId);
    await loaded;
    // An assistant page asks its question once loaded: give the answer time to arrive.
    await settle(path.includes("/assistant#") ? 15000 : 6000);
  };
  for (const [name, path, width, height, mobile, whole] of SHOTS) {
    try {
      await open(path, width, height, mobile);
      await shoot(name, width, height, mobile, whole);
    } catch (error) {
      report.push(`${name} FAILED ${error.message}`);
    }
  }
} finally {
  child.kill();
}
await writeFile(join(out, "report.txt"), report.join("\n") + "\n");
console.log(report.join("\n"));
