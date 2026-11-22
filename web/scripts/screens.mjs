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
// repair-pages, second run: the V4 pages scroll inside their frame, so whole pages are shot in tall viewports.
const OPEN_ALL = "(() => { document.querySelectorAll('main details').forEach((d) => { d.open = true; }); return 'opened'; })()";
const SECOND_RUN = [
  ["u-fr-01-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["u-fr-02-how-dictionary-open--tall", "/fr/evidence.html", 1366, 9000, false, false, OPEN_DICT, 2500],
  ["u-en-01-how--tall", "/en/evidence.html", 1366, 9000, false, false, null, 7000],
  ["u-fr-03-coverage--tall", "/fr/coverage.html", 1366, 9000, false, false, null, 7000],
  ["u-fr-04-coverage-open--tall", "/fr/coverage.html", 1366, 9000, false, false, OPEN_ALL, 2500],
  ["u-en-02-coverage--tall", "/en/coverage.html", 1366, 9000, false, false, null, 7000],
  ["u-fr-05-results--tall", "/fr/results.html", 1366, 6000, false, false, null, 7000],
  ["u-en-03-results--tall", "/en/results.html", 1366, 6000, false, false, null, 7000],
  ["u-fr-06-decisions--tall", "/fr/decisions.html", 1366, 4000, false, false, null, 6000],
  ["u-fr-07-modifications--tall", "/fr/radar.html", 1366, 1800, false, false, null, 8000],
  ["u-fr-08-text-solvency--1366x768", "/fr/text.html?text=32009L0138&article=001", 1366, 768, false, false, null, 8000],
  ["u-fr-09-home--tall", "/fr/", 1366, 1600, false, false, null, 8000],
  ["u-fr-10-typed-answer--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(R.fields), 9000],
  ["u-en-04-english--tall", "/en/radar.html", 1366, 3600, false, false, ASK(R.english), 9000],
  ["u-fr-11-how--phone", "/fr/evidence.html", 390, 844, true, false, null, 7000],
  ["u-fr-12-coverage--phone", "/fr/coverage.html", 390, 844, true, false, null, 7000],
];
SHOTS.length = 0;
SHOTS.push(...SECOND_RUN);
const FIRST_RUN = [
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
];
void FIRST_RUN;
// rc2 (22/11/2026): the release candidate rc2 (code t10, data 20261122c) at its private label address, real answers of the
// deployed engine (no fixture, no preview): about 10-20 s an answer, so each waits 40 s (45 s for the long ones); tall viewports
// for the pages that scroll inside their frame. The questions check repair-engine's work (typed answer, the premium-tax rate from
// the 1990 law beside the German original, not held for a date naming the right text, the dictionary on an English question,
// the reference trigger on a life policy) and rc1's defects (paternity leave 2014, motor cover 2010, the evidence table in the dock).
const RC2 = {
  typed: "Un assuré peut-il résilier son contrat après un sinistre, et avec quel préavis ?",
  premiumTax: "Quel est le taux de l'impôt sur les primes d'assurance au Luxembourg ?",
  suicide: "Mon mari s'est suicidé huit mois après avoir souscrit une assurance sur la vie : l'assureur doit-il payer quelque chose ?",
  bundled: "The shop says I must take their insurance if I buy the phone. Can they force me to buy both together?",
  maternity: "En 2018, combien de semaines durait le congé de maternité ?",
};
SHOTS.length = 0;
SHOTS.push(
  ["v-fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["v-fr-02-home--tall", "/fr/", 1366, 1600, false, false, null, 8000],
  ["v-fr-03-premium-tax--tall", "/fr/radar.html", 1366, 4200, false, false, ASK(RC2.premiumTax), 40000],
  ["v-fr-04-typed-answer--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(RC2.typed), 40000],
  ["v-fr-05-typed-answer-trace-open--tall", "/fr/radar.html", 1366, 8000, false, false, ASK_LATE(RC2.typed, OPEN_TRACE, 34000), 40000],
  ["v-fr-06-compound-trace-open--tall", "/fr/radar.html", 1366, 8000, false, false, ASK_LATE(B1.compound, OPEN_TRACE, 38000), 45000],
  ["v-fr-07-suicide-life--tall", "/fr/radar.html", 1366, 4200, false, false, ASK(RC2.suicide), 40000],
  ["v-fr-08-list-headings--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(B1.listHeadings), 40000],
  ["v-fr-09-not-held-2014--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(B1.notHeld), 40000],
  ["v-fr-10-not-held-maternity-2018--tall", "/fr/radar.html", 1366, 2600, false, false, ASK(RC2.maternity), 40000],
  ["v-fr-11-motor-2010--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(R.notFound), 40000],
  ["v-fr-12-not-found--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(P.none), 40000],
  ["v-fr-13-not-found--phone-whole", "/fr/radar.html", 390, 844, true, true, ASK(P.none), 40000],
  ["v-fr-14-clarify--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(B1.clarify), 30000],
  ["v-fr-15-out-of-scope--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK(P.out), 30000],
  ["v-fr-16-solvency--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(B1.solvency), 45000],
  ["v-fr-17-css--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(B1.css), 40000],
  ["v-fr-18-named-article--phone", `/fr${text("&article=art_21")}`, 390, 844, true, false, ASK(Q.named), 40000],
  ["v-en-01-bundled--tall", "/en/radar.html", 1366, 3600, false, false, ASK(RC2.bundled), 40000],
  ["v-en-02-english-mcr--tall", "/en/radar.html", 1366, 3600, false, false, ASK(R.english), 45000],
  ["v-en-03-german--tall", "/en/radar.html", 1366, 3000, false, false, ASK(R.german), 40000],
  ["v-fr-19-text-art21--1366x768", `/fr${text("&article=art_21")}`, 1366, 768, false, false, null, 0],
  ["v-fr-20-compare-2011-2013-art38--1366x768", `/fr${text("&from=2011-04-16&to=2013-03-01&article=art_38")}`, 1366, 768, false, false, null, 0],
  ["v-fr-21-text-css--1366x768", `/fr${CSS}`, 1366, 768, false, false, null, 8000],
  ["v-fr-22-text-solvency--1366x768", "/fr/text.html?text=32009L0138&article=001", 1366, 768, false, false, null, 8000],
  ["v-fr-23-text-code-route--1366x768", `/fr${ROUTE}`, 1366, 768, false, false, null, 8000],
  ["v-fr-24-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["v-fr-25-how-dictionary-open--tall", "/fr/evidence.html", 1366, 9000, false, false, OPEN_DICT, 2500],
  ["v-en-04-how--tall", "/en/evidence.html", 1366, 9000, false, false, null, 7000],
  ["v-fr-26-coverage--tall", "/fr/coverage.html", 1366, 9000, false, false, null, 7000],
  ["v-fr-27-coverage-open--tall", "/fr/coverage.html", 1366, 9000, false, false, OPEN_ALL, 2500],
  ["v-en-05-coverage--tall", "/en/coverage.html", 1366, 9000, false, false, null, 7000],
  ["v-fr-28-results--tall", "/fr/results.html", 1366, 6000, false, false, null, 7000],
  ["v-en-06-results--tall", "/en/results.html", 1366, 6000, false, false, null, 7000],
  ["v-fr-29-decisions--tall", "/fr/decisions.html", 1366, 4000, false, false, null, 6000],
  ["v-fr-30-modifications--tall", "/fr/radar.html", 1366, 1800, false, false, null, 8000],
  ["v-fr-31-privacy--1366-whole", "/fr/privacy.html", 1366, 768, false, true, null, 3000],
  ["v-fr-32-home--phone", "/fr/", 390, 844, true, false, null, 6000],
  ["v-fr-33-how--phone", "/fr/evidence.html", 390, 844, true, false, null, 7000],
  ["v-en-07-home--1366x768", "/en/", 1366, 768, false, false, null, 6000],
);
// rc3 (22/11/2026): the release candidate rc3 (code t11 = t10's engine files, data 20261122c) at its private label address.
// The Results page with the sealed run (lex-results/2) in both languages, first screen, tall, folds open and on a phone; the
// footers; a few real answers of the deployed engine (40-45 s each) to see that t11 answers as t10 did. Second run: the
// revision rc3b (the repair round: every key of the sealed file named in the page's language), same list.
SHOTS.length = 0;
SHOTS.push(
  ["w-fr-01-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["w-fr-02-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["w-fr-03-results-open--tall", "/fr/results.html", 1366, 16000, false, false, OPEN_ALL, 2500],
  ["w-fr-04-results--phone", "/fr/results.html", 390, 844, true, false, null, 7000],
  ["w-fr-05-results--phone-whole", "/fr/results.html", 390, 844, true, true, null, 7000],
  ["w-en-01-results--1366x768", "/en/results.html", 1366, 768, false, false, null, 7000],
  ["w-en-02-results--tall", "/en/results.html", 1366, 9000, false, false, null, 7000],
  ["w-en-03-results-open--tall", "/en/results.html", 1366, 16000, false, false, OPEN_ALL, 2500],
  ["w-fr-06-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["w-fr-07-premium-tax--tall", "/fr/radar.html", 1366, 4200, false, false, ASK(RC2.premiumTax), 40000],
  ["w-fr-08-typed-answer-trace-open--tall", "/fr/radar.html", 1366, 8000, false, false, ASK_LATE(RC2.typed, OPEN_TRACE, 34000), 40000],
  ["w-fr-09-not-found--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(P.none), 40000],
  ["w-en-04-bundled--tall", "/en/radar.html", 1366, 3600, false, false, ASK(RC2.bundled), 40000],
  ["w-fr-10-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["w-fr-11-coverage--tall", "/fr/coverage.html", 1366, 9000, false, false, null, 7000],
  ["w-fr-12-text-art21--1366x768", `/fr${text("&article=art_21")}`, 1366, 768, false, false, null, 0],
);
// fix-pages3 (22/11/2026): a preview of this branch's pages (built on the runner, API https://law.soufien.lu, the new /api/next/*
// addresses from fixtures saved from this tree's server functions). The dock's searches as a list with every article named (a
// real answer of rc3b, the live engine, kept as a fixture: no model is called), the home's example cards at 1366x768, the Results
// page with the walk's value and the table of runs, How with "who has checked what", Decisions; desktop and phone.
const PRESCRIPTION = "Quel est le délai de prescription des actions qui dérivent du contrat d'assurance ?";
const OPEN_FINDS = "document.querySelectorAll('details[data-dock-trace]').forEach((d) => { d.open = true; }); setTimeout(() => { document.querySelectorAll('details[data-finds-more]').forEach((d) => { d.open = true; }); }, 2500);";
SHOTS.length = 0;
SHOTS.push(
  ["x-fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["x-fr-02-home--phone", "/fr/", 390, 844, true, false, null, 6000],
  ["x-fr-03-trace-open--tall", "/fr/radar.html", 1366, 9000, false, false, ASK_THEN(PRESCRIPTION, OPEN_FINDS), 9000],
  ["x-fr-04-trace-open--phone-whole", "/fr/radar.html", 390, 844, true, true, ASK_THEN(PRESCRIPTION, OPEN_FINDS), 9000],
  ["x-en-01-trace-open--tall", "/en/radar.html", 1366, 9000, false, false, ASK_THEN(PRESCRIPTION, OPEN_FINDS), 9000],
  ["x-fr-05-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["x-fr-06-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["x-fr-07-results-open--tall", "/fr/results.html", 1366, 16000, false, false, OPEN_ALL, 2500],
  ["x-fr-08-results--phone-whole", "/fr/results.html", 390, 844, true, true, null, 7000],
  ["x-en-02-results--1366x768", "/en/results.html", 1366, 768, false, false, null, 7000],
  ["x-en-03-results--tall", "/en/results.html", 1366, 9000, false, false, null, 7000],
  ["x-fr-09-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["x-en-04-how--tall", "/en/evidence.html", 1366, 9000, false, false, null, 7000],
  ["x-fr-10-decisions--tall", "/fr/decisions.html", 1366, 4200, false, false, null, 6000],
  ["x-fr-11-how--phone-whole", "/fr/evidence.html", 390, 844, true, true, null, 7000],
);
// rc4 (22/11/2026): the release candidate rc4 (code t12: fix-engine3's engine, fix-pages3's pages; data 20261122d) at its private
// label address, real answers of the deployed engine (no preview, no fixture). Above all the dock's searches side by side at
// 1366x768: the answer asked, the trace opened as a reader opens it, the dock scrolled to the searches list. Then the typed
// answers that must keep the law's words, the two tax texts of 20261122d, a communal rule, a dated not held, Results, How.
const EN_PRESCRIPTION = "What is the limitation period for actions arising from an insurance contract?";
const RC4 = {
  heir: "Mon père assurait sa maison contre l'incendie et il vient de mourir. Moi, son héritier, combien de temps ai-je pour mettre fin à ce contrat ?",
  vat: "Quel est aujourd'hui le taux normal de la TVA au Luxembourg ?",
  housing: "J'ai 35 ans et je cotise à un contrat d'épargne-logement : quel montant puis-je déduire au maximum par an de mes impôts ?",
  communal: "Le règlement de police de la commune de Differdange interdit-il de tondre sa pelouse le dimanche ?",
  hours: "What is the maximum number of hours an employee may work in a single day in Luxembourg?",
  leave2016: "En 2016, combien de jours de congé annuel payé un salarié avait-il au minimum ?",
};
// Asks, waits for the answer's trace (at most 45 s), opens it, optionally opens "the other articles", then scrolls the searches
// list to the top of the dock.
const ASK_FINDS = (question, more) => `(() => { const asked = ${ASK(question)}; if (asked !== 'asked') return asked;
  return new Promise((resolve) => { const t0 = Date.now(); const tick = () => { const d = document.querySelector('details[data-dock-trace]');
    if (d) { d.open = true; setTimeout(() => { ${more ? "document.querySelectorAll('details[data-finds-more]').forEach((x) => { x.open = true; });" : ""}
      setTimeout(() => { const t = document.querySelector('[data-trace-table="detectors"]'); if (t) t.scrollIntoView({ block: 'start' }); resolve(t ? 'asked' : 'no searches list'); }, 1500); }, 3000); }
    else if (Date.now() - t0 > 45000) resolve('no trace'); else setTimeout(tick, 500); }; tick(); }); })()`;
SHOTS.length = 0;
SHOTS.push(
  ["y-fr-01-searches--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK_FINDS(PRESCRIPTION, false), 3000],
  ["y-fr-02-searches-more--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK_FINDS(PRESCRIPTION, true), 3000],
  ["y-en-01-searches--1366x768", "/en/radar.html", 1366, 768, false, false, ASK_FINDS(EN_PRESCRIPTION, false), 3000],
  ["y-fr-03-trace-open--tall", "/fr/radar.html", 1366, 9000, false, false, ASK_FINDS(PRESCRIPTION, true), 3000],
  ["y-fr-04-searches--phone", "/fr/radar.html", 390, 844, true, false, ASK_FINDS(PRESCRIPTION, false), 3000],
  ["y-fr-05-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["y-fr-06-heir--tall", "/fr/radar.html", 1366, 4200, false, false, ASK(RC4.heir), 30000],
  ["y-fr-07-vat--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(RC4.vat), 30000],
  ["y-fr-08-housing-savings--tall", "/fr/radar.html", 1366, 3600, false, false, ASK(RC4.housing), 30000],
  ["y-fr-09-communal--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(RC4.communal), 30000],
  ["y-en-02-hours--tall", "/en/radar.html", 1366, 3600, false, false, ASK(RC4.hours), 30000],
  ["y-fr-10-leave-2016--tall", "/fr/radar.html", 1366, 3000, false, false, ASK(RC4.leave2016), 30000],
  ["y-fr-11-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["y-fr-12-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["y-en-03-results--1366x768", "/en/results.html", 1366, 768, false, false, null, 7000],
  ["y-fr-13-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["y-fr-14-coverage--tall", "/fr/coverage.html", 1366, 9000, false, false, null, 7000],
  ["y-fr-15-decisions--tall", "/fr/decisions.html", 1366, 4200, false, false, null, 6000],
);
// rc5 (22/11/2026): the release candidate rc5 (code t13 = rc4's engine + the owner's review applied + the architecture dossier
// merged + the administration-source label; data 20261122e) at its private label address `cand`, real answers of the deployed
// engine (no preview, no fixture). Home; the dock's searches list after a real answer at 1366x768; real answers with the trace
// open (the heir's three months and forty days, the 1969 VAT law asked as in force); the architecture dossier first screen, its
// "who has checked what" part and tall; Results; How (who has checked what); Decisions (the approval line). Three questions only.
const PART = (id) => `(() => { const el = document.getElementById(${JSON.stringify(id)}); if (!el) return "no part"; el.scrollIntoView(); return "scrolled"; })()`;
const RC5 = { vat1969: "La loi du 5 août 1969 concernant la taxe sur la valeur ajoutée est-elle toujours en vigueur ?" };
SHOTS.length = 0;
SHOTS.push(
  ["z-fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["z-fr-02-searches--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK_FINDS(PRESCRIPTION, false), 3000],
  ["z-fr-03-heir-trace-open--tall", "/fr/radar.html", 1366, 9000, false, false, ASK_FINDS(RC4.heir, true), 3000],
  ["z-fr-04-vat-1969-trace-open--tall", "/fr/radar.html", 1366, 9000, false, false, ASK_FINDS(RC5.vat1969, false), 3000],
  ["z-fr-05-architecture--1366x768", "/fr/architecture.html", 1366, 768, false, false, null, 9000],
  ["z-fr-06-architecture-checked--1366x768", "/fr/architecture.html", 1366, 768, false, false, PART("arch-operations"), 2500],
  ["z-fr-07-architecture--tall", "/fr/architecture.html", 1366, 16000, false, false, null, 9000],
  ["z-en-01-architecture--1366x768", "/en/architecture.html", 1366, 768, false, false, null, 9000],
  ["z-fr-08-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["z-fr-09-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["z-fr-10-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["z-en-02-how--tall", "/en/evidence.html", 1366, 9000, false, false, null, 7000],
  ["z-fr-11-decisions--tall", "/fr/decisions.html", 1366, 4200, false, false, null, 6000],
);
// rc5r (22/11/2026): rc5's engine with test 05's checked results on the Results page and in the dossier, and rc5.md's pages-only
// defects fixed, at the private label address `cand` (code t13r on 20261122e). Results FR first screen and tall, EN first screen;
// the dossier FR first screen, part six (the test), FR tall, EN first screen; How FR / EN tall (step 3, the licence, the side
// panel's models); Decisions FR tall (the approval line); home FR (the find field); one real answer with the trace open (the
// heir's question: the dock's lead, the articles named with their text, the trace's foot line). One question only.
SHOTS.length = 0;
SHOTS.push(
  ["r5r-fr-01-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["r5r-fr-02-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["r5r-en-01-results--1366x768", "/en/results.html", 1366, 768, false, false, null, 7000],
  ["r5r-fr-03-architecture--1366x768", "/fr/architecture.html", 1366, 768, false, false, null, 9000],
  ["r5r-fr-04-architecture-evaluation--1366x768", "/fr/architecture.html", 1366, 768, false, false, PART("arch-evaluation"), 2500],
  ["r5r-fr-05-architecture--tall", "/fr/architecture.html", 1366, 16000, false, false, null, 9000],
  ["r5r-en-02-architecture--1366x768", "/en/architecture.html", 1366, 768, false, false, null, 9000],
  ["r5r-fr-06-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["r5r-en-03-how--tall", "/en/evidence.html", 1366, 9000, false, false, null, 7000],
  ["r5r-fr-07-decisions--tall", "/fr/decisions.html", 1366, 4200, false, false, null, 6000],
  ["r5r-fr-08-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["r5r-fr-09-heir-trace-open--tall", "/fr/radar.html", 1366, 9000, false, false, ASK_FINDS(RC4.heir, true), 3000],
);
// rc5s (22/11/2026): pages-only release on rc5's engine (code t13s on 20261122e) at the revision's own address (0 %, no label).
// The test 05 sentence said truly and the conditions table aligned left (Results, dossier); home FR / EN; a policyholder's notice
// to end a contract (first screen, then the trace opened whole, then on a phone); one English answer; a not found with its
// evidence; a communal regulation not held; How, coverage, Decisions; the 1997 law art. 21 and its compare view. Six questions.
const NOTICE = "Avec quel préavis le preneur d'assurance peut-il résilier son contrat d'assurance à l'échéance annuelle ?";
// Asks, waits for the answer (its trace's fold, at most 55 s), optionally opens the trace and "the other articles", no scroll.
const ASK_DONE = (question, open) => `(() => { const asked = ${ASK(question)}; if (asked !== 'asked') return asked;
  return new Promise((resolve) => { const t0 = Date.now(); const tick = () => { const d = document.querySelector('details[data-dock-trace]');
    if (d) { ${open ? "d.open = true; setTimeout(() => { document.querySelectorAll('details[data-finds-more]').forEach((x) => { x.open = true; }); setTimeout(() => resolve('asked'), 1500); }, 3000);" : "resolve('asked');"} }
    else if (Date.now() - t0 > 55000) resolve('no trace'); else setTimeout(tick, 500); }; tick(); }); })()`;
SHOTS.length = 0;
SHOTS.push(
  ["r5s-fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["r5s-fr-02-home--tall", "/fr/", 1366, 1800, false, false, null, 6000],
  ["r5s-en-01-home--1366x768", "/en/", 1366, 768, false, false, null, 6000],
  ["r5s-fr-03-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["r5s-fr-04-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["r5s-en-02-results--1366x768", "/en/results.html", 1366, 768, false, false, null, 7000],
  ["r5s-fr-05-architecture--1366x768", "/fr/architecture.html", 1366, 768, false, false, null, 9000],
  ["r5s-fr-06-architecture-evaluation--1366x768", "/fr/architecture.html", 1366, 768, false, false, PART("arch-evaluation"), 2500],
  ["r5s-fr-07-architecture--tall", "/fr/architecture.html", 1366, 16000, false, false, null, 9000],
  ["r5s-en-03-architecture--1366x768", "/en/architecture.html", 1366, 768, false, false, null, 9000],
  ["r5s-fr-08-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["r5s-fr-09-coverage--1366x768", "/fr/coverage.html", 1366, 768, false, false, null, 7000],
  ["r5s-fr-10-coverage--tall", "/fr/coverage.html", 1366, 9000, false, false, null, 7000],
  ["r5s-fr-11-decisions--tall", "/fr/decisions.html", 1366, 4200, false, false, null, 6000],
  ["r5s-fr-12-text-1997-art21--1366x768", `/fr${text("&article=art_21")}`, 1366, 768, false, false, null, 0],
  ["r5s-fr-13-compare-1997-art21--1366x768", `/fr${text("&from=2007-12-21&to=2025-11-25&article=art_21")}`, 1366, 768, false, false, null, 0],
  ["r5s-fr-14-home--phone", "/fr/", 390, 844, true, false, null, 6000],
  ["r5s-fr-15-notice-answer--1366x768", "/fr/", 1366, 768, false, false, ASK_DONE(NOTICE, false), 3000],
  ["r5s-fr-16-notice-trace-open--tall", "/fr/radar.html", 1366, 9000, false, false, ASK_DONE(NOTICE, true), 2000],
  ["r5s-en-04-answer--tall", "/en/radar.html", 1366, 3600, false, false, ASK_DONE(EN_PRESCRIPTION, false), 3000],
  ["r5s-fr-17-not-found-evidence--tall", "/fr/radar.html", 1366, 3600, false, false, ASK_DONE(P.none, false), 3000],
  ["r5s-fr-18-not-held-communal--tall", "/fr/radar.html", 1366, 3600, false, false, ASK_DONE(RC4.communal, false), 3000],
  ["r5s-fr-19-notice-answer--phone", "/fr/radar.html", 390, 844, true, false, ASK_DONE(NOTICE, false), 3000],
);
// rc5s, second run: the shot that failed (asked from the home page, which opens the article's page when the answer comes, so
// the waiting promise was cut: asked here with a fixed wait), the same answer on the Changes page, and the dossier's parts 8-9
// (below the 16000 px of the tall shot). Two questions.
SHOTS.length = 0;
SHOTS.push(
  ["r5s2-fr-01-notice-answer-home--1366x768", "/fr/", 1366, 768, false, false, ASK(NOTICE), 45000],
  ["r5s2-fr-02-notice-answer--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK_DONE(NOTICE, false), 3000],
  ["r5s2-fr-03-architecture-changes--tall", "/fr/architecture.html", 1366, 4200, false, false, PART("arch-changes"), 2500],
);
// rc5t (22/11/2026): the second pages-only release on rc5's engine (code t13t on 20261122e) at the revision's own address (0 %, no
// label): the visitor review's defects checked (B1 the dossier's worked example, M1 Results in plain words, M4 / M9 coverage,
// M5 / M6 the text page and its compare view, M7 the home's scope line, M8 Decisions, M11 the not-found evidence). Home FR / EN;
// the policyholder's notice (first screen, then the trace opened whole); one English answer; a not found; a communal rule not
// held; Results; the dossier (first screen, tall, parts 8-9, EN); How; coverage; Decisions; the 1997 law art. 21 and its compare
// view; phone: home and the heir's answer. Six questions.
SHOTS.length = 0;
SHOTS.push(
  ["r5t-fr-01-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["r5t-en-01-home--1366x768", "/en/", 1366, 768, false, false, null, 6000],
  ["r5t-fr-02-notice-answer--1366x768", "/fr/radar.html", 1366, 768, false, false, ASK_DONE(NOTICE, false), 3000],
  ["r5t-fr-03-notice-trace-open--tall", "/fr/radar.html", 1366, 9000, false, false, ASK_DONE(NOTICE, true), 2000],
  ["r5t-en-02-answer--tall", "/en/radar.html", 1366, 3600, false, false, ASK_DONE(EN_PRESCRIPTION, false), 3000],
  ["r5t-fr-04-not-found-evidence--tall", "/fr/radar.html", 1366, 3600, false, false, ASK_DONE(P.none, false), 3000],
  ["r5t-fr-05-not-held-communal--tall", "/fr/radar.html", 1366, 3600, false, false, ASK_DONE(RC4.communal, false), 3000],
  ["r5t-fr-06-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["r5t-fr-07-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["r5t-en-03-results--1366x768", "/en/results.html", 1366, 768, false, false, null, 7000],
  ["r5t-fr-08-architecture--1366x768", "/fr/architecture.html", 1366, 768, false, false, null, 9000],
  ["r5t-fr-09-architecture--tall", "/fr/architecture.html", 1366, 16000, false, false, null, 9000],
  ["r5t-fr-10-architecture-changes--tall", "/fr/architecture.html", 1366, 4200, false, false, PART("arch-changes"), 2500],
  ["r5t-en-04-architecture--1366x768", "/en/architecture.html", 1366, 768, false, false, null, 9000],
  ["r5t-fr-11-how--tall", "/fr/evidence.html", 1366, 9000, false, false, null, 7000],
  ["r5t-fr-12-coverage--1366x768", "/fr/coverage.html", 1366, 768, false, false, null, 7000],
  ["r5t-fr-13-coverage--tall", "/fr/coverage.html", 1366, 9000, false, false, null, 7000],
  ["r5t-fr-14-decisions--tall", "/fr/decisions.html", 1366, 4200, false, false, null, 6000],
  ["r5t-fr-15-text-1997-art21--1366x768", `/fr${text("&article=art_21")}`, 1366, 768, false, false, null, 0],
  ["r5t-fr-16-compare-1997-art21--1366x768", `/fr${text("&from=2007-12-21&to=2025-11-25&article=art_21")}`, 1366, 768, false, false, null, 0],
  ["r5t-fr-17-home--phone", "/fr/", 390, 844, true, false, null, 6000],
  ["r5t-fr-18-heir-answer--phone", "/fr/radar.html", 390, 844, true, false, ASK_DONE(RC4.heir, false), 3000],
);
// rc5u (22/11/2026): a pages-only release on rc5's engine (code t13u on 20261122e) at the revision's own address (0 %, no label):
// test 07 (a candidate held, the live version measured beside it, the walk measured in the run) on the Results page and in the
// dossier's part six. Results FR first screen, tall, tall from test 07, phone; Results EN first screen and from test 07; the
// dossier FR first screen and its evaluation part. No question is asked.
SHOTS.length = 0;
SHOTS.push(
  ["r5u-fr-01-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["r5u-fr-02-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["r5u-fr-03-results-test07--tall", "/fr/results.html", 1366, 9000, false, false, PART("run-test-07"), 7000],
  ["r5u-en-01-results--1366x768", "/en/results.html", 1366, 768, false, false, null, 7000],
  ["r5u-en-02-results-test07--tall", "/en/results.html", 1366, 9000, false, false, PART("run-test-07"), 7000],
  ["r5u-fr-04-architecture--1366x768", "/fr/architecture.html", 1366, 768, false, false, null, 9000],
  ["r5u-fr-05-architecture-evaluation--tall", "/fr/architecture.html", 1366, 7000, false, false, PART("arch-evaluation"), 9000],
  ["r5u-fr-06-results--phone", "/fr/results.html", 390, 844, true, false, null, 7000],
);
// rc5v (22/11/2026): the last pages build before the demo (code t13v on 20261122e, engine unchanged) at the revision's own
// address (0 %, no label): who opened each sealed set, test 07's figures said by version side by side, p and intervals written
// one way, the phone gutter. Results FR first screen, tall, from test 07, from the sealed set 4 run, phone; Results EN first
// screen and from test 07; the dossier FR part six; home FR. No question is asked.
SHOTS.length = 0;
SHOTS.push(
  ["r5v-fr-01-results--1366x768", "/fr/results.html", 1366, 768, false, false, null, 7000],
  ["r5v-fr-02-results--tall", "/fr/results.html", 1366, 9000, false, false, null, 7000],
  ["r5v-fr-03-results-test07--tall", "/fr/results.html", 1366, 9000, false, false, PART("run-test-07"), 7000],
  ["r5v-fr-04-results-sealed04--tall", "/fr/results.html", 1366, 3000, false, false, PART("run-sealed-04"), 7000],
  ["r5v-en-01-results--1366x768", "/en/results.html", 1366, 768, false, false, null, 7000],
  ["r5v-en-02-results-test07--tall", "/en/results.html", 1366, 9000, false, false, PART("run-test-07"), 7000],
  ["r5v-fr-05-architecture-evaluation--tall", "/fr/architecture.html", 1366, 7000, false, false, PART("arch-evaluation"), 9000],
  ["r5v-fr-06-home--1366x768", "/fr/", 1366, 768, false, false, null, 6000],
  ["r5v-fr-07-results--phone", "/fr/results.html", 390, 844, true, false, null, 7000],
);
const settle =(ms) => new Promise((resolve) => setTimeout(resolve, ms));

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
        else if (typeof said === "string" && said.startsWith("no ")) report.push(`${name}: ${said}`);
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
