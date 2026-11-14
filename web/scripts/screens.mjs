// Screenshots of the live site for the driver's design review (2026-11-13): each page at a desktop and a phone width, after
// its answers load, saved as PNG. Runs only on GitHub's runners (never on the driver's PC: no local browser). Asks the public
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
const PAGES = [
  ["fr-home", "/fr/"],
  ["en-home", "/en/"],
  ["fr-how-it-works", "/fr/how-it-works.html"],
  ["en-how-it-works", "/en/how-it-works.html"],
  ["fr-quality", "/fr/quality.html"],
  ["en-quality", "/en/quality.html"],
  ["en-search-ai-act", `/en/search.html${q("AI Act", "eng")}`],
  ["fr-search-rgpd", `/fr/search.html${q("RGPD", "fra")}`],
  ["fr-search-insurance-article", `/fr/search.html${q("article 1er de la loi du 27 juillet 1997 sur le contrat d'assurance", "fra")}`],
  ["fr-dossier-insurance-1997", "/fr/lu-legilux/loi-1997-07-27-n10"],
  ["en-dossier-ai-act", "/en/eu-eurlex/32024R1689"],
  ["fr-assistant-insurance-1997", `/fr/assistant${ask("Que dit l'article 1er de la loi du 27 juillet 1997 sur le contrat d'assurance ?")}`],
  ["fr-assistant-rgpd-5", `/fr/assistant${ask("Que dit l'article 5 du RGPD ?")}`],
  ["en-assistant-ai-act-50", `/en/assistant${ask("What does Article 50 of the AI Act say?")}`],
  ["en-assistant-insurer", `/en/assistant${ask("Can my insurer refuse to pay my claim?")}`],
  // Release 7 (the owner's order of 2026-11-13): the answer card for a concept question, the law page at that article (its
  // two buttons), and the changes page as the menu opens it.
  ["fr-assistant-prescription", `/fr/assistant${ask("Quel est le délai de prescription des actions dérivant d'un contrat d'assurance ?")}`],
  ["fr-law-page-article-44", "/fr/lu-legilux/loi-1997-07-27-n10#article-art_44"],
  ["fr-law-page-changes-44", "/fr/lu-legilux/loi-1997-07-27-n10#changes-art_44"],
  ["fr-radar-12-months", "/fr/radar.html#last-12-months"],
];
const QUESTIONS = [];
const WIDTHS = [["desktop", 1280, 800, false], ["menu-1024", 1024, 768, false], ["phone", 390, 844, true]];
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
  const shoot = async (name, width, mobile) => {
    const height = Math.min(7000, Math.max(800, Number(await evaluate("document.documentElement.scrollHeight")) || 800));
    await session.send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: 1, mobile }, sessionId);
    await settle(500);
    const { data } = await session.send("Page.captureScreenshot", { format: "png", captureBeyondViewport: true }, sessionId);
    await writeFile(join(out, `${name}.png`), Buffer.from(data, "base64"));
    report.push(`${name}.png ${width}x${height}`);
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
  for (const [label, width, height, mobile] of WIDTHS) {
    for (const [name, path] of PAGES) {
      try {
        await open(path, width, height, mobile);
        await shoot(`${name}--${label}`, width, mobile);
      } catch (error) {
        report.push(`${name}--${label} FAILED ${error.message}`);
      }
    }
  }
  for (const [name, path, question] of QUESTIONS) {
    for (const [label, width, height, mobile] of WIDTHS) {
      try {
        await open(path, width, height, mobile);
        const asked = await evaluate(`(() => {
          const field = document.querySelector('#assistant-question');
          if (!field) return 'no field';
          Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(field, ${JSON.stringify(question)});
          field.dispatchEvent(new Event('input', { bubbles: true }));
          const button = field.form?.querySelector('button[type=submit]');
          if (!button) return 'no button';
          button.click();
          return 'asked';
        })()`);
        await settle(15000);
        await shoot(`${name}--${label}`, width, mobile);
        report.push(`  ${name}--${label}: ${asked}`);
      } catch (error) {
        report.push(`${name}--${label} FAILED ${error.message}`);
      }
    }
  }
} finally {
  child.kill();
}
await writeFile(join(out, "report.txt"), report.join("\n") + "\n");
console.log(report.join("\n"));
