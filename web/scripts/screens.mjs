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
// The owner's order of 2026-11-14, evening: at 1366 by 768, French and English, the home page, an answer found with the work
// closed and open, an answer not found, the law page, Decisions and Evidence; the home page and an answer on a phone; and
// the two new pages whole. Each example question is a test-set question, asked word for word.
// [name, path, width, height, mobile, whole page, what to do before the shot]
const LAW = "/lu-legilux/loi-1997-07-27-n10";
const FR_FOUND = ask("Quel est le délai de prescription des actions dérivant d'un contrat d'assurance ?");
const FR_NONE = ask("Que se passe-t-il en cas d'erreur sur l'âge de l'assuré dans une assurance sur la vie ?");
const EN_FOUND = ask("What does article 5 of the GDPR say?");
const DATED = ask("Quel était le délai de prescription des actions dérivant d'un contrat d'assurance au 1er janvier 2020 ?");
const EN_NONE = ask("What does the GDPR say about the right to data portability?");
const OPEN_WORK = "document.querySelector('[data-answer-work]') && (document.querySelector('[data-answer-work]').open = true, document.querySelector('[data-answer-work]').scrollIntoView({ block: 'start' }))";
const SHOTS = [];
for (const [lang, found, none] of [["fr", FR_FOUND, FR_NONE], ["en", EN_FOUND, EN_NONE]]) {
  SHOTS.push(
    [`${lang}-1-home--1366x768`, `/${lang}/`, 1366, 768, false, false, null],
    [`${lang}-2-answer--1366x768`, `/${lang}/assistant${found}`, 1366, 768, false, false, null],
    [`${lang}-3-answer-work-open--1366x768`, `/${lang}/assistant${found}`, 1366, 768, false, false, OPEN_WORK],
    [`${lang}-4-not-found--1366x768`, `/${lang}/assistant${none}`, 1366, 768, false, false, null],
    [`${lang}-5-law-page--1366x768`, `/${lang}${LAW}`, 1366, 768, false, false, null],
    [`${lang}-5b-law-page-at-2020-01-01--1366x768`, `/${lang}${LAW}#on-2020-01-01`, 1366, 768, false, false, null],
    [`${lang}-2b-answer-dated--1366x768`, `/${lang}/assistant${DATED}`, 1366, 768, false, false, null],
    [`${lang}-6-decisions--1366x768`, `/${lang}/decisions.html`, 1366, 768, false, false, null],
    [`${lang}-7-evidence--1366x768`, `/${lang}/evidence.html`, 1366, 768, false, false, null],
    [`${lang}-8-home--phone`, `/${lang}/`, 390, 844, true, false, null],
    [`${lang}-9-answer--phone`, `/${lang}/assistant${found}`, 390, 844, true, false, null],
    [`${lang}-decisions--1366-whole`, `/${lang}/decisions.html`, 1366, 768, false, true, null],
    [`${lang}-evidence--1366-whole`, `/${lang}/evidence.html`, 1366, 768, false, true, null],
    [`${lang}-law-page--1366-whole`, `/${lang}${LAW}`, 1366, 768, false, true, null],
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
  // A first screen is the viewport as it opens; a whole page, its full height. Each is reported with the page's height and
  // any English in sight (an element marked English that is not inside a closed disclosure).
  const shoot = async (name, width, height, mobile, whole) => {
    const tall = Number(await evaluate("document.documentElement.scrollHeight")) || height;
    const english = await evaluate(`(() => {
      const other = document.documentElement.lang === 'en' ? 'fr' : 'en';
      const seen = [...document.querySelectorAll('main [lang="' + other + '"]')].filter((node) => node.getClientRects().length > 0 && !node.closest('blockquote, .answer-sentence, .wording-diff, [data-answer-article]'));
      return seen.length + (seen.length === 0 ? '' : ': ' + seen.slice(0, 4).map((node) => node.textContent.trim().slice(0, 50)).join(' | '));
    })()`);
    if (whole) {
      await session.send("Emulation.setDeviceMetricsOverride", { width, height: Math.min(9000, Math.max(height, tall)), deviceScaleFactor: 1, mobile }, sessionId);
      await settle(500);
    } else if (!name.includes("work-open")) {
      await evaluate("window.scrollTo(0, 0)");
    }
    const { data } = await session.send("Page.captureScreenshot", { format: "png", captureBeyondViewport: whole }, sessionId);
    await writeFile(join(out, `${name}.png`), Buffer.from(data, "base64"));
    report.push(`${name}.png ${width}x${whole ? tall : height}; page height ${tall} px (${(tall / height).toFixed(2)} screens); other language in sight ${english}`);
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
  for (const [name, path, width, height, mobile, whole, action] of SHOTS) {
    try {
      await open(path, width, height, mobile);
      if (action !== null) {
        await evaluate(action);
        await settle(1500);
      }
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
