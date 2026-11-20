// Screenshots of the live site for the driver's design review (2026-11-13; the owner's first screens of 2026-11-14), after
// their answers load, saved as PNG. Runs only on GitHub's runners (never on the driver's PC: no local browser). Asks the public
// site what any reader may ask; the assistant is asked the questions listed here, as a reader would ask them.
// Release 14 (the owner's brief of 2026-11-15): the V4 pages and the brief's ten acceptance checks, French and English.
// Release 15 (the bricks, 2026-11-19): the two questions of 2026-11-18 as their reader typed them, an absence, a question on
// the reader's own case (refused, the law beside it), and the same in English.
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
const NONE = "C'est quoi la franchise ?";
const FIRST = { fr: "c'est quoi une personne lésé", en: "What is an injured party?" };
const SECOND = { fr: "l'assureur peut résilier le contrat de façon unilatérale ?", en: "Can the insurer terminate the contract?" };
const OWN = { fr: "Mon assureur peut-il résilier mon contrat ?", en: "Can my insurer terminate my contract?" };
const AGE = "Que se passe-t-il en cas d'erreur sur l'âge de l'assuré dans une assurance sur la vie ?";
const CLARIFY = "Quel est le délai de préavis pour résilier un contrat d'assurance ?";
const OTHER = { fr: "Combien de mois de caution un propriétaire peut-il demander ?", en: "How many days of paid holiday is an employee entitled to per year?" };
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
    [`${lang}-10-ask-absence--1366x768`, `/${lang}/radar.html`, 1366, 768, false, false, ASK(NONE), 20000],
    [`${lang}-19-ask-first-question--1366x768`, `/${lang}/`, 1366, 768, false, false, ASK(FIRST[lang]), 20000],
    [`${lang}-20-ask-second-question--1366x768`, `/${lang}/`, 1366, 768, false, false, ASK(SECOND[lang]), 24000],
    [`${lang}-23-ask-other-law--1366x768`, `/${lang}/`, 1366, 768, false, false, ASK(OTHER[lang]), 26000],
    [`${lang}-other-law--1366-whole`, `/${lang}/`, 1366, 768, false, true, ASK(OTHER[lang]), 26000],
    [`${lang}-21-ask-own-case-refused--1366x768`, `/${lang}/radar.html`, 1366, 768, false, false, ASK(OWN[lang]), 22000],
    [`${lang}-22-ask-heading-written-out--1366x768`, `/${lang}/radar.html`, 1366, 768, false, false, ASK(AGE), 22000],
    [`${lang}-first-question--1366-whole`, `/${lang}/`, 1366, 768, false, true, ASK(FIRST[lang]), 20000],
    [`${lang}-second-question--1366-whole`, `/${lang}/`, 1366, 768, false, true, ASK(SECOND[lang]), 24000],
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
// The side build of 2026-11-20 (the new engine behind the current layout), photographed at its own address before any switch.
const Q = {
  premium: "Mon assureur peut-il suspendre ma garantie si je ne paie pas ma prime ?",
  life: "Je ne paie plus la prime de mon assurance sur la vie. L'assureur peut-il suspendre la garantie ?",
  notice: "Quel est le délai de préavis pour un salarié licencié après 6 ans d'ancienneté ?",
  dated: "En 2012, mon assureur pouvait-il résilier le contrat après un sinistre ?",
  notHeld: "En 2014, combien de temps durait au maximum la période d'essai d'un salarié ?",
  named: "Que dit l'article 21 de la loi du 27 juillet 1997 sur le contrat d'assurance ?",
  german: "Wie lange ist die Probezeit bei einem Arbeitsvertrag?",
  european: "Que dit le RGPD sur le délai pour notifier une violation de données ?",
  hello: "Bonjour",
  english: "Can my insurer suspend my cover if I do not pay my premium?",
};
SHOTS.length = 0;
SHOTS.push(
  ["fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 0],
  ["fr-02-ask-premium--1366x768", "/fr/", 1366, 768, false, false, ASK(Q.premium), 40000],
  ["fr-02-ask-premium--1366-whole", "/fr/", 1366, 768, false, true, ASK(Q.premium), 40000],
  ["fr-03-ask-life-insurance--1366x768", "/fr/", 1366, 768, false, false, ASK(Q.life), 40000],
  ["fr-04-ask-notice-period--1366x768", "/fr/", 1366, 768, false, false, ASK(Q.notice), 40000],
  ["fr-05-ask-in-2012--1366x768", "/fr/", 1366, 768, false, false, ASK(Q.dated), 40000],
  ["fr-06-ask-2014-not-held--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(Q.notHeld), 26000],
  ["fr-06-ask-2014-not-held--1366-whole", "/fr/radar.html", 1366, 768, false, true, ASK(Q.notHeld), 26000],
  ["fr-07-ask-named-article--1366x768", "/fr/", 1366, 768, false, false, ASK(Q.named), 40000],
  ["fr-08-ask-german--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(Q.german), 26000],
  ["fr-09-ask-european--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(Q.european), 26000],
  ["fr-10-ask-hello--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(Q.hello), 14000],
  ["fr-11-text-art21-references--1366x768", `/fr${text("&article=art_21")}`, 1366, 768, false, false, null, 6000],
  ["fr-11-text-art21-references--1366-whole", `/fr${text("&article=art_21")}`, 1366, 768, false, true, null, 6000],
  ["fr-12-compare-2011-2013-art38--1366x768", `/fr${text("&from=2011-04-16&to=2013-03-01&article=art_38")}`, 1366, 768, false, false, null, 0],
  ["fr-13-how-it-works--1366x768", "/fr/evidence.html", 1366, 768, false, false, null, 4000],
  ["fr-13-how-it-works--1366-whole", "/fr/evidence.html", 1366, 768, false, true, null, 4000],
  ["fr-14-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 4000],
  ["fr-14-results--1366-whole", "/fr/results.html", 1366, 768, false, true, null, 4000],
  ["fr-15-coverage--1366x768", "/fr/coverage.html", 1366, 768, false, false, null, 4000],
  ["fr-15-coverage--1366-whole", "/fr/coverage.html", 1366, 768, false, true, null, 4000],
  ["fr-16-home--phone", "/fr/", 390, 844, true, false, null, 0],
  ["fr-17-ask-premium--phone", "/fr/", 390, 844, true, true, ASK(Q.premium), 40000],
  ["fr-20-ask-premium--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(Q.premium), 30000],
  ["fr-21-ask-caution--tall", "/fr/radar.html", 1366, 3000, false, false, ASK("Combien de mois de caution un propriétaire peut-il demander ?"), 30000],
  ["fr-22-ask-no-answer-evidence--tall", "/fr/radar.html", 1366, 2600, false, false, ASK("Quelle est la couleur réglementaire des boîtes aux lettres au Luxembourg ?"), 30000],
  ["fr-23-ask-european--tall", "/fr/radar.html", 1366, 2200, false, false, ASK(Q.european), 30000],
  ["fr-24-ask-definition--tall", "/fr/radar.html", 1366, 2600, false, false, ASK("L'assureur doit-il indemniser la personne lésée directement ?"), 30000],
  ["fr-25-ask-annex--tall", "/fr/radar.html", 1366, 2600, false, false, ASK("Quelles sont les branches d'assurance non vie ?"), 30000],
  ["fr-26-text-art38-wording--tall", `/fr${text("&article=art_38")}`, 1366, 1600, false, false, null, 6000],
  ["fr-27-text-links--tall", `/fr${text("")}`, 1366, 1600, false, false, null, 6000],
  ["fr-28-how-it-works--tall", "/fr/evidence.html", 1366, 4200, false, false, null, 5000],
  ["fr-29-results--tall", "/fr/results.html", 1366, 4200, false, false, null, 5000],
  ["fr-30-coverage--tall", "/fr/coverage.html", 1366, 3200, false, false, null, 5000],
  ["fr-31-ask-gdpr-fine--tall", "/fr/radar.html", 1366, 3000, false, false, ASK("Quelle amende maximale pour une entreprise qui ne respecte pas le RGPD ?"), 30000],
  ["fr-32-ask-list--tall", "/fr/radar.html", 1366, 3000, false, false, ASK("Dans quels cas un salarié peut-il être licencié sans préavis ?"), 30000],
  ["fr-33-ask-in-2014--tall", "/fr/radar.html", 1366, 2400, false, false, ASK(Q.notHeld), 30000],
  ["en-34-ask-ai-act--tall", "/en/radar.html", 1366, 3000, false, false, ASK("What is a high-risk AI system under the AI Act?"), 30000],
  ["en-01-home--1366x768", "/en/", 1366, 768, false, false, null, 0],
  ["en-02-ask-premium--1366x768", "/en/", 1366, 768, false, false, ASK(Q.english), 40000],
  ["en-02-ask-premium--1366-whole", "/en/", 1366, 768, false, true, ASK(Q.english), 40000],
  ["en-13-how-it-works--1366-whole", "/en/evidence.html", 1366, 768, false, true, null, 4000],
  ["en-14-results--1366-whole", "/en/results.html", 1366, 768, false, true, null, 4000],
);
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
