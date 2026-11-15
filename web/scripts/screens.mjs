// Screenshots of the live site for the driver's design review (2026-11-13; the owner's first screens of 2026-11-14), after
// their answers load, saved as PNG. Runs only on GitHub's runners (never on the driver's PC: no local browser). Asks the public
// site what any reader may ask; the assistant is asked the questions listed here, as a reader would ask them.
// Release 14 (the owner's brief of 2026-11-15): the V4 pages and the brief's ten acceptance checks, French and English.
// Usage: node web/scripts/screens.mjs <origin> <out directory>
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Session, findBrowser, launchBrowser } from "./browser-evidence.mjs";

const [origin = "https://law.soufien.lu", out = "screens"] = process.argv.slice(2);
console.log(`origin ${origin}`);
const LAW = encodeURIComponent("/lu-legilux/loi-1997-07-27-n10");
const text = (query) => `/text.html?text=${LAW}${query}`;
const find = (words) => `/search.html#q=${encodeURIComponent(words)}&lang=fra`;
// The dock's question box, typed into as a reader types (React reads the native input event), then submitted.
const ASK = (question) => `(() => { const input = document.getElementById('lx-dock-q'); if (!input) return 'no dock';
  const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set; set.call(input, ${JSON.stringify(question)});
  input.dispatchEvent(new Event('input', { bubbles: true })); input.form.requestSubmit(); return 'asked'; })()`;
const FR_FOUND = "Quel est le délai de prescription d'un contrat d'assurance ?";
const EN_FOUND = "What does Article 5 of the GDPR say?";
const NONE = "Que se passe-t-il en cas d'erreur sur l'âge de l'assuré dans une assurance sur la vie ?";
const CLARIFY = "Quel est le délai de préavis pour résilier un contrat d'assurance ?";
const CITATION = "Loi du 27 juillet 1997 sur le contrat d'assurance, article 38, 1er juin 2012";
// [name, path, width, height, mobile, whole page, what to do once loaded, how long to wait after it]
const SHOTS = [];
for (const lang of ["fr", "en"]) {
  SHOTS.push(
    [`${lang}-01-home--1366x768`, `/${lang}/`, 1366, 768, false, false, null, 0],
    [`${lang}-02-home-ask-found--1366x768`, `/${lang}/`, 1366, 768, false, false, ASK(lang === "fr" ? FR_FOUND : EN_FOUND), 22000],
    [`${lang}-03-text-art44--1366x768`, `/${lang}${text("&article=art_44")}`, 1366, 768, false, false, null, 0],
    [`${lang}-04-compare-2011-2013-art38--1366x768`, `/${lang}${text("&from=2011-04-16&to=2013-03-01&article=art_38")}`, 1366, 768, false, false, null, 0],
    [`${lang}-05-compare-in-one-text--1366x768`, `/${lang}${text("&from=2011-04-16&to=2013-03-01&article=art_38&view=inline")}`, 1366, 768, false, false, null, 0],
    [`${lang}-06-find-contrat-assurance--1366x768`, `/${lang}${find("contrat d'assurance")}`, 1366, 768, false, false, null, 0],
    [`${lang}-07-find-citation-opens-art38--1366x768`, `/${lang}${find(CITATION)}`, 1366, 768, false, false, null, 8000],
    [`${lang}-08-ask-citation-no-model--1366x768`, `/${lang}${text("&article=art_44")}`, 1366, 768, false, false, ASK(CITATION), 20000],
    [`${lang}-09-ask-clarify--1366x768`, `/${lang}/radar.html`, 1366, 768, false, false, ASK(CLARIFY), 20000],
    [`${lang}-10-ask-not-found--1366x768`, `/${lang}/radar.html`, 1366, 768, false, false, ASK(NONE), 20000],
    [`${lang}-11-gdpr--1366x768`, `/${lang}/text.html?text=32016R0679&article=005`, 1366, 768, false, false, null, 0],
    [`${lang}-12-changes--1366x768`, `/${lang}/radar.html`, 1366, 768, false, false, null, 0],
    [`${lang}-13-how-it-works--1366x768`, `/${lang}/evidence.html`, 1366, 768, false, false, null, 0],
    [`${lang}-14-decisions--1366x768`, `/${lang}/decisions.html`, 1366, 768, false, false, null, 0],
    [`${lang}-15-coverage--1366x768`, `/${lang}/coverage.html`, 1366, 768, false, false, null, 0],
    [`${lang}-16-home--phone`, `/${lang}/`, 390, 844, true, false, null, 0],
    [`${lang}-17-text--phone`, `/${lang}${text("&article=art_44")}`, 390, 844, true, false, null, 0],
    [`${lang}-18-compare--phone`, `/${lang}${text("&from=2011-04-16&to=2013-03-01&article=art_38")}`, 390, 844, true, false, null, 0],
    [`${lang}-home--1366-whole`, `/${lang}/`, 1366, 768, false, true, null, 0],
    [`${lang}-how-it-works--1366-whole`, `/${lang}/evidence.html`, 1366, 768, false, true, null, 0],
    [`${lang}-decisions--1366-whole`, `/${lang}/decisions.html`, 1366, 768, false, true, null, 0],
    [`${lang}-changes--1366-whole`, `/${lang}/radar.html`, 1366, 768, false, true, null, 0],
  );
}
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
  // A first screen is the viewport as it opens; a whole page, its full height. Each is reported with the page's height, the
  // address it ended at, and any English in sight on a French page (and French on an English one) outside quoted law.
  const shoot = async (name, width, height, mobile, whole) => {
    const tall = Number(await evaluate("document.documentElement.scrollHeight")) || height;
    const where = await evaluate("location.pathname + location.search");
    const english = await evaluate(`(() => {
      const other = document.documentElement.lang === 'en' ? 'fr' : 'en';
      const seen = [...document.querySelectorAll('main [lang="' + other + '"], aside [lang="' + other + '"]')].filter((node) => node.getClientRects().length > 0 && !node.closest('blockquote, .lx-body, .lx-reading, .lx-dock-quote, details:not([open])'));
      return seen.length + (seen.length === 0 ? '' : ': ' + seen.slice(0, 4).map((node) => node.textContent.trim().slice(0, 50)).join(' | '));
    })()`);
    if (whole) {
      await session.send("Emulation.setDeviceMetricsOverride", { width, height: Math.min(9000, Math.max(height, tall)), deviceScaleFactor: 1, mobile }, sessionId);
      await settle(800);
    }
    const { data } = await session.send("Page.captureScreenshot", { format: "png", captureBeyondViewport: whole }, sessionId);
    await writeFile(join(out, `${name}.png`), Buffer.from(data, "base64"));
    report.push(`${name}.png ${width}x${whole ? tall : height}; page height ${tall} px; at ${where}; other language in sight ${english}`);
  };
  const open = async (path, width, height, mobile) => {
    await session.send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: 1, mobile }, sessionId);
    const blank = session.waitFor("Page.loadEventFired", sessionId, 60000);
    await session.send("Page.navigate", { url: "about:blank" }, sessionId);
    await blank;
    const loaded = session.waitFor("Page.loadEventFired", sessionId, 60000);
    await session.send("Page.navigate", { url: origin + path }, sessionId);
    await loaded;
    // A text page asks about a dozen requests once loaded (the per-address allowance is 60 a minute): give it time.
    await settle(path.includes("text.html") ? 14000 : 7000);
  };
  for (const [name, path, width, height, mobile, whole, action, wait] of SHOTS) {
    try {
      await open(path, width, height, mobile);
      if (action !== null) {
        const said = await evaluate(action);
        if (said === "no dock") report.push(`${name}: no dock on the page`);
        await settle(wait);
      } else if (wait > 0) {
        await settle(wait);
      }
      await shoot(name, width, height, mobile, whole);
    } catch (error) {
      report.push(`${name} FAILED ${error.message}`);
    }
    // Keep under the API's allowance per address.
    await settle(4000);
  }
} finally {
  child.kill();
}
await writeFile(join(out, "report.txt"), report.join("\n") + "\n");
console.log(report.join("\n"));
