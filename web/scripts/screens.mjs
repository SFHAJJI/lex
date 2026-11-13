// Screenshots of the live site for the driver's design review (2026-11-13): each page at a desktop and a phone width, after
// its answers load, saved as PNG. Runs only on GitHub's runners (never on the driver's PC: no local browser). Asks the public
// site what any reader may ask; the assistant is asked the questions listed here, as a reader would ask them.
// Usage: node web/scripts/screens.mjs <origin> <out directory>
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Session, findBrowser, launchBrowser } from "./browser-evidence.mjs";

const [origin = "https://law.soufien.lu", out = "screens"] = process.argv.slice(2);
const q = (text, lang) => `#q=${encodeURIComponent(text)}&lang=${lang}`;
const PAGES = [
  ["en-home", "/en/"],
  ["fr-home", "/fr/"],
  ["en-search-ai-act", `/en/search.html${q("AI Act", "eng")}`],
  ["en-search-artificial-intelligence", `/en/search.html${q("artificial intelligence", "eng")}`],
  ["fr-search-contrat-assurance", `/fr/search.html${q("contrat d'assurance", "fra")}`],
  ["fr-search-1997-insurance-article", `/fr/search.html${q("article 1er de la loi du 27 juillet 1997 sur le contrat d'assurance", "fra")}`],
  ["en-dossier-ai-act", "/en/eu-eurlex/32024R1689"],
  ["en-dossier-gdpr", "/en/eu-eurlex/32016R0679"],
  ["fr-dossier-insurance-1997", "/fr/lu-legilux/loi-1997-07-27-n10"],
  ["en-dossier-civil-code", "/en/lu-legilux/code-civil"],
  ["en-assistant", "/en/assistant"],
  ["en-coverage", "/en/coverage.html"],
  ["en-help", "/en/help.html"],
  ["en-how-to-read", "/en/how-to-read.html"],
  ["en-developers", "/en/developers.html"],
  ["en-privacy", "/en/privacy.html"],
  ["en-history", "/en/history.html"],
  ["en-compare", "/en/compare.html"],
  ["en-radar", "/en/radar.html"],
  ["en-dossier-page", "/en/dossier.html"],
  ["en-export", "/en/export.html"],
];
const QUESTIONS = [
  ["en-assistant-ai-act-50", "/en/assistant", "What does Article 50 of the AI Act say?"],
  ["fr-assistant-rgpd-5", "/fr/assistant", "Que dit l'article 5 du RGPD ?"],
  ["fr-assistant-insurance-1997", "/fr/assistant", "Que dit l'article 1er de la loi du 27 juillet 1997 sur le contrat d'assurance ?"],
  ["en-assistant-advice", "/en/assistant", "Can I be fired for refusing overtime?"],
];
const WIDTHS = [["desktop", 1280, 800, false], ["phone", 390, 844, true]];
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
    const loaded = session.waitFor("Page.loadEventFired", sessionId, 60000);
    await session.send("Page.navigate", { url: origin + path }, sessionId);
    await loaded;
    await settle(6000);
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
