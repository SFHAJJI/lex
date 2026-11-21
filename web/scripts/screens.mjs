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
// The pages builder's preview of 21/11/2026: this branch's pages, the answers in the engine's contract served from the
// fixtures they are checked against (`preview-server.mjs`): four answers the funnel really gave, and one fixture a state.
// pages2: the questions asked tonight of the shipped pipeline, answered from their fixtures (test/next-fixtures/funnel-real/b1-*).
const B1 = {
  listHeadings: "Quels sont tous les congés spéciaux prévus par le Code du travail ?",
  compound: "Quel est le délai de préavis du bailleur pour résilier un bail d'habitation et quel est le montant maximal de la garantie locative ?",
  clarify: "Préavis ?",
  notHeld: "En 2014, quelle était la durée du congé de paternité ?",
  solvency: "Comment est calculé le capital de solvabilité requis selon la directive Solvabilité II ?",
  css: "Qui est assuré obligatoirement à l'assurance maladie selon le Code de la sécurité sociale ?",
  partial: "Quel est le délai pour déclarer un sinistre à mon assureur habitation ?",
};
const P = {
  exception: "Mon assureur peut-il suspendre ma garantie si je ne paie pas ma prime ?",
  list: "Quelles mentions la police d'assurance doit-elle contenir ?",
  solvency: "Quel est le capital de solvabilité requis d'un assureur selon Solvabilité II ?",
  none: "Combien de boîtes aux lettres une commune doit-elle installer ?",
  clarify: "Quel est le délai pour résilier ?",
  partial: "En 2012, quel délai pour résilier mon contrat d’assurance et sous quelle forme ?",
  notHeld: "Quel est le capital de solvabilité requis d’un assureur selon Solvabilité II ?",
  candidates: "Quelles sont mes obligations d’information envers mon assureur ?",
  out: "Quel cadeau de départ offrir à un collègue ?",
};
// Ask, then (after the answer) open every trace, or press a widening choice.
const ASK_THEN = (question, then) => `(() => { const asked = ${ASK(question)}; setTimeout(() => { ${then} }, 5000); return asked; })()`;
const OPEN_TRACE = "document.querySelectorAll('details[data-dock-trace]').forEach((d) => { d.open = true; });";
const WIDEN = (level) => `document.querySelector('[data-widen="${level}"]')?.click();`;
SHOTS.length = 0;
SHOTS.push(
  ["p-fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 0],
  ["p-fr-02-ask-exception-opens-art21--1366x768", "/fr/", 1366, 768, false, false, ASK(P.exception), 16000],
  ["p-fr-03-ask-exception--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(P.exception), 9000],
  ["p-fr-04-trace-open--tall", "/fr/radar.html", 1366, 6000, false, false, ASK_THEN(P.exception, OPEN_TRACE), 9000],
  ["p-fr-05-ask-list--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(P.list), 9000],
  ["p-fr-06-widen-section--tall", "/fr/radar.html", 1366, 3600, false, false, ASK_THEN(P.list, WIDEN("section")), 11000],
  ["p-fr-07-ask-solvency--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(P.solvency), 9000],
  ["p-fr-08-ask-solvency--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(P.solvency), 9000],
  ["p-fr-09-ask-not-found--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(P.none), 9000],
  ["p-fr-10-ask-clarify--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(P.clarify), 9000],
  ["p-fr-11-ask-partial--tall", "/fr/radar.html", 1366, 2200, false, false, ASK(P.partial), 9000],
  ["p-fr-12-ask-not-held--tall", "/fr/radar.html", 1366, 2200, false, false, ASK(P.notHeld), 9000],
  ["p-fr-13-ask-candidates--tall", "/fr/radar.html", 1366, 2400, false, false, ASK(P.candidates), 9000],
  ["p-fr-14-ask-out-of-scope--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(P.out), 9000],
  ["p-fr-15-text-art21-outline--1366x768", `/fr${text("&article=art_21")}`, 1366, 768, false, false, null, 6000],
  ["p-fr-16-how--1366x768", "/fr/evidence.html", 1366, 768, false, false, null, 5000],
  ["p-fr-17-how--1366-whole", "/fr/evidence.html", 1366, 768, false, true, null, 5000],
  ["p-fr-18-decisions--1366x768", "/fr/decisions.html", 1366, 768, false, false, null, 5000],
  ["p-fr-19-decisions--1366-whole", "/fr/decisions.html", 1366, 768, false, true, null, 5000],
  ["p-fr-20-coverage--1366x768", "/fr/coverage.html", 1366, 768, false, false, null, 6000],
  ["p-fr-21-coverage--1366-whole", "/fr/coverage.html", 1366, 768, false, true, null, 6000],
  ["p-fr-22-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 5000],
  ["p-fr-23-results--1366-whole", "/fr/results.html", 1366, 768, false, true, null, 5000],
  ["p-fr-24-text-answer--phone", `/fr${text("&article=art_21")}`, 390, 844, true, false, ASK(P.exception), 9000],
  ["p-fr-25-radar-answer--phone", "/fr/radar.html", 390, 844, true, false, ASK(P.list), 9000],
  ["p-fr-26-home--phone", "/fr/", 390, 844, true, false, null, 0],
  ["p-en-01-home--1366x768", "/en/", 1366, 768, false, false, null, 0],
  ["p-en-03-ask-exception--tall", "/en/radar.html", 1366, 3600, false, false, ASK(P.exception), 9000],
  ["p-en-16-how--1366x768", "/en/evidence.html", 1366, 768, false, false, null, 5000],
  ["p-en-22-results--1366-whole", "/en/results.html", 1366, 768, false, true, null, 5000],
  // pages2 (21/11/2026): the answers the shipped pipeline (combo + refs) gave tonight, and the pages changed.
  ["q-fr-01-list-headings--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(B1.listHeadings), 9000],
  ["q-fr-02-list-headings--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(B1.listHeadings), 9000],
  ["q-fr-03-compound--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(B1.compound), 9000],
  ["q-fr-04-compound-trace--tall", "/fr/radar.html", 1366, 7000, false, false, ASK_THEN(B1.compound, OPEN_TRACE), 9000],
  ["q-fr-05-partial--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(B1.partial), 9000],
  ["q-fr-06-not-held-2014--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(B1.notHeld), 9000],
  ["q-fr-07-css--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(B1.css), 9000],
  ["q-fr-08-solvency-held--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(B1.solvency), 9000],
  ["q-fr-09-text-css-from-engine--1366x768", `/fr/text.html?text=${encodeURIComponent("/lu-legilux/code-securite-sociale")}&article=art_1er`, 1366, 768, false, false, null, 8000],
  ["q-fr-10-text-solvency-from-engine--1366x768", `/fr/text.html?text=32009L0138&article=001`, 1366, 768, false, false, null, 8000],
  ["q-fr-11-clarify-real--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(B1.clarify), 6000],
  ["q-fr-12-list-headings--phone", "/fr/radar.html", 390, 844, true, false, ASK(B1.listHeadings), 9000],
  ["q-fr-13-coverage--1366x768", "/fr/coverage.html", 1366, 768, false, false, null, 6000],
  ["q-fr-14-coverage--1366-whole", "/fr/coverage.html", 1366, 768, false, true, null, 6000],
  ["q-fr-15-results-build--1366x768", "/fr/results.html", 1366, 768, false, false, null, 5000],
  ["q-fr-16-how--1366-whole", "/fr/evidence.html", 1366, 768, false, true, null, 5000],
  ["q-fr-17-decisions-tab--1366x768", "/fr/decisions.html", 1366, 768, false, false, null, 5000],
  ["q-fr-18-modifications--1366x768", "/fr/radar.html", 1366, 768, false, false, null, 8000],
  ["q-en-01-compound--tall", "/en/radar.html", 1366, 3600, false, false, ASK(B1.compound), 9000],
  ["q-en-02-coverage--1366-whole", "/en/coverage.html", 1366, 768, false, true, null, 6000],
);
// pages2, second run: only the views changed since run 44939497094.
SHOTS.length = 0;
SHOTS.push(
  ["r-fr-01-list-headings--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(B1.listHeadings), 9000],
  ["r-fr-02-compound--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(B1.compound), 9000],
  ["r-fr-03-compound-trace--tall", "/fr/radar.html", 1366, 7000, false, false, ASK_THEN(B1.compound, OPEN_TRACE), 9000],
  ["r-fr-04-not-held-2014--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(B1.notHeld), 9000],
  ["r-fr-05-css--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(B1.css), 9000],
  ["r-fr-06-text-solvency-from-engine--1366x768", `/fr/text.html?text=32009L0138&article=001`, 1366, 768, false, false, null, 8000],
  ["r-fr-07-coverage-key-texts--tall", "/fr/coverage.html", 1366, 2400, false, false, null, 6000],
  ["r-fr-08-how--tall", "/fr/evidence.html", 1366, 3000, false, false, null, 5000],
  ["r-fr-09-decisions-tab--1366x768", "/fr/decisions.html", 1366, 768, false, false, null, 5000],
  ["r-fr-10-list-headings--phone", "/fr/radar.html", 390, 844, true, false, ASK(B1.listHeadings), 9000],
  ["r-fr-11-partial--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(B1.partial), 9000],
  ["r-en-01-list-headings--1366x768", "/en/radar.html", 1366, 768, false, false, ASK(B1.listHeadings), 9000],
);
// rc (21/11/2026): the release candidate rc1 (code t9, data 20261122b) at its own private address, real answers of the
// deployed engine (no fixture): about 15 s an answer, so each waits 36 s; the trace is opened 30 s after the question.
const ASK_LATE = (question, then, ms) => `(() => { const asked = ${ASK(question)}; setTimeout(() => { ${then} }, ${ms}); return asked; })()`;
const CSS = `/text.html?text=${encodeURIComponent("/lu-legilux/code-securite-sociale")}&article=art_1er`;
const ROUTE = `/text.html?text=${encodeURIComponent("/lu-legilux/code-route")}`;
SHOTS.length = 0;
SHOTS.push(
  ["s-fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 0],
  ["s-fr-02-home-ask-premium--1366x768", "/fr/", 1366, 768, false, false, ASK(P.exception), 36000],
  ["s-fr-03-compound-trace-open--tall", "/fr/radar.html", 1366, 7000, false, false, ASK_LATE(B1.compound, OPEN_TRACE, 30000), 36000],
  ["s-fr-04-list-headings--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(B1.listHeadings), 36000],
  ["s-fr-05-solvency-held--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(B1.solvency), 36000],
  ["s-fr-06-css--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(B1.css), 36000],
  ["s-fr-07-not-held-2014--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(B1.notHeld), 36000],
  ["s-fr-08-clarify--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(B1.clarify), 30000],
  ["s-fr-09-not-found--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(P.none), 36000],
  ["s-fr-10-out-of-scope--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(P.out), 30000],
  ["s-fr-11-named-article--phone", `/fr${text("&article=art_21")}`, 390, 844, true, false, ASK(Q.named), 36000],
  ["s-en-01-ask-premium--tall", "/en/radar.html", 1366, 3000, false, false, ASK(Q.english), 36000],
  ["s-fr-12-text-art21--1366x768", `/fr${text("&article=art_21")}`, 1366, 768, false, false, null, 0],
  ["s-fr-13-compare-2011-2013-art38--1366x768", `/fr${text("&from=2011-04-16&to=2013-03-01&article=art_38")}`, 1366, 768, false, false, null, 0],
  ["s-fr-14-text-css--1366x768", `/fr${CSS}`, 1366, 768, false, false, null, 8000],
  ["s-fr-15-text-solvency--1366x768", "/fr/text.html?text=32009L0138&article=001", 1366, 768, false, false, null, 8000],
  ["s-fr-16-text-code-route--1366x768", `/fr${ROUTE}`, 1366, 768, false, false, null, 8000],
  ["s-fr-17-how--1366x768", "/fr/evidence.html", 1366, 768, false, false, null, 5000],
  ["s-fr-18-how--1366-whole", "/fr/evidence.html", 1366, 768, false, true, null, 5000],
  ["s-fr-19-decisions--1366x768", "/fr/decisions.html", 1366, 768, false, false, null, 5000],
  ["s-fr-20-coverage--1366x768", "/fr/coverage.html", 1366, 768, false, false, null, 6000],
  ["s-fr-21-coverage--1366-whole", "/fr/coverage.html", 1366, 768, false, true, null, 6000],
  ["s-fr-22-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 5000],
  ["s-fr-23-results--1366-whole", "/fr/results.html", 1366, 768, false, true, null, 5000],
  ["s-fr-24-modifications--1366x768", "/fr/radar.html", 1366, 768, false, false, null, 8000],
  ["s-fr-25-privacy--1366-whole", "/fr/privacy.html", 1366, 768, false, true, null, 3000],
  ["s-fr-26-home--phone", "/fr/", 390, 844, true, false, null, 0],
  ["s-fr-27-text--phone", `/fr${text("&article=art_44")}`, 390, 844, true, false, null, 0],
  ["s-en-02-home--1366x768", "/en/", 1366, 768, false, false, null, 0],
  ["s-en-03-how--1366x768", "/en/evidence.html", 1366, 768, false, false, null, 5000],
  ["s-en-04-coverage--1366-whole", "/en/coverage.html", 1366, 768, false, true, null, 6000],
  ["s-en-05-results--1366-whole", "/en/results.html", 1366, 768, false, true, null, 5000],
);
// repair-pages (22/11/2026): this branch's pages (preview), the engine's answers from the fixtures: repair-engine's fields
// (typed answer, summary, conflict, German text, articles level), the not-found evidence table at 1366 px and on a phone, an
// English and a German question on the English page, How with the dictionary and the departures, coverage, results in
// lex-results/2, the Solvency II text page, the footers.
const R = {
  fields: "Un assuré peut-il résilier son contrat après un sinistre, et avec quel préavis ? (champs de repair-engine)",
  notFound: "En 2010, quel était le montant minimal de garantie de l'assurance obligatoire de la responsabilité civile automobile ?",
  english: "What is the minimum capital requirement of an insurance undertaking under Solvency II?",
  german: "Wie lange ist die Kündigungsfrist des Vermieters bei einem Mietvertrag über eine Wohnung?",
};
const OPEN_DICT = "(() => { document.querySelectorAll('details[data-dictionary-rows]').forEach((d) => { d.open = true; }); return 'opened'; })()";
SHOTS.length = 0;
SHOTS.push(
  ["t-fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["t-fr-02-home--1366-whole", "/fr/", 1366, 768, false, true, null, 6000],
  ["t-fr-03-typed-answer--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(R.fields), 9000],
  ["t-fr-04-typed-answer-trace--tall", "/fr/radar.html", 1366, 7000, false, false, ASK_THEN(R.fields, OPEN_TRACE), 9000],
  ["t-fr-05-not-found--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(R.notFound), 9000],
  ["t-fr-06-not-found--phone-whole", "/fr/radar.html", 390, 844, true, true, ASK(R.notFound), 9000],
  ["t-fr-07-typed-answer--phone", "/fr/radar.html", 390, 844, true, false, ASK(R.fields), 9000],
  ["t-en-01-english--tall", "/en/radar.html", 1366, 3600, false, false, ASK(R.english), 9000],
  ["t-en-02-german--tall", "/en/radar.html", 1366, 3000, false, false, ASK(R.german), 9000],
  ["t-en-03-not-found--tall", "/en/radar.html", 1366, 3000, false, false, ASK(R.notFound), 9000],
  ["t-fr-08-how--1366-whole", "/fr/evidence.html", 1366, 768, false, true, null, 6000],
  ["t-fr-09-how-dictionary-open--1366-whole", "/fr/evidence.html", 1366, 768, false, true, OPEN_DICT, 2000],
  ["t-fr-10-coverage--1366-whole", "/fr/coverage.html", 1366, 768, false, true, null, 6000],
  ["t-fr-11-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 6000],
  ["t-fr-12-results--1366-whole", "/fr/results.html", 1366, 768, false, true, null, 6000],
  ["t-fr-13-text-solvency--1366x768", "/fr/text.html?text=32009L0138&article=001", 1366, 768, false, false, null, 8000],
  ["t-fr-14-modifications--1366-whole", "/fr/radar.html", 1366, 768, false, true, null, 8000],
  ["t-en-04-how--1366-whole", "/en/evidence.html", 1366, 768, false, true, null, 6000],
  ["t-en-05-coverage--1366-whole", "/en/coverage.html", 1366, 768, false, true, null, 6000],
  ["t-en-06-results--1366-whole", "/en/results.html", 1366, 768, false, true, null, 6000],
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
