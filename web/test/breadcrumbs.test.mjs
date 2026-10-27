// The breadcrumbs of a law's pages and the banners' info controls (the specification of record's R48: "Header, compact
// search, breadcrumbs (publisher, act, article, version) and footer. A 'How to read Lex' guide and a French and English
// glossary, linked from each banner's info control").
//
// - The crumbs, from the census's answers: a reading at a permalink names the publisher, the act (a link to its dossier), the
//   article its address names and the version it pins, which is the page; the version by its date's role (R21) and an EU
//   wording by its own date; a reading of several versions names none; a dossier names the publisher and the act, which is
//   the page; a page with no answer has none; in French, the French table's words.
// - The pages: the reading page the server renders at a permalink, and the dossier page at a work's address, carry them as a
//   navigation landmark, in the record's order, the page's crumb current and no link, the act's link to its dossier.
// - Each banner's info control: the staleness banner, the freshness strip, the as-published label and the EU statement
//   each end with a link to the section of How to read Lex that explains it, named by that section's heading, and the guide
//   page holds every section a banner points to, in both languages.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import {
  FreshnessStrip, GUIDE_LINK_SECTIONS, GuideLink, ReadingAnswerView, StalenessBanner, guideSectionHref, renderLiveDossierPage,
  renderLiveGuidePage, renderLiveReadingPage,
} from "../.react-build/app.mjs";
import { CRUMB_PARTS, dossierCrumbs, readingCrumbs, versionWords } from "../scripts/breadcrumbs.mjs";
import { LIVE_CHROME, fillText } from "../scripts/live-chrome.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { answerHandedWith } from "../scripts/v3-client.mjs";
import { LIVE_CONTRACT } from "../.react-build/app.mjs";
import { europeEnvelope } from "../scripts/europe-bundle-sample.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart) => census.envelopes.find((entry) => entry.operation === operation && entry.scenario.startsWith(scenarioStart)).envelope;
const READING = envelopeOf("evidence_bundle", "the work on its state's date");
const PINNED = envelopeOf("evidence_bundle", "a state's permalink at its own date");
const DOSSIER = envelopeOf("dossier", "the work, in the language it is held in");
const EU_DOSSIER = envelopeOf("dossier", "one EU work by its CELEX");
const UNMOUNTED = envelopeOf("evidence_bundle", "no corpus mounted");

// The hand-built EU bundle is no census answer, so it is handed as an answer, as the EU reading's own tests hand it.
const handed = (envelope) => (envelope.context?.publisher === "eu-eurlex"
  ? { state: "success", envelope }
  : answerHandedWith("evidence_bundle", envelope, { contract: LIVE_CONTRACT, objectType: "evidence_bundle" }));
const readingView = (envelope) => {
  const outcome = readingOutcome(handed(envelope));
  assert.equal(outcome.state, "success", "the census answer reads as a reading");
  return outcome.view;
};
const dossierView = (envelope) => {
  const outcome = dossierOutcome(answerHandedWith("dossier", envelope, { contract: LIVE_CONTRACT, objectType: "work_record" }));
  assert.equal(outcome.state, "success", "the census answer reads as a dossier");
  return outcome.view;
};
const EN = LIVE_CHROME.en.breadcrumbs;
const FR = LIVE_CHROME.fr.breadcrumbs;

test("a reading at a permalink names the publisher, the act, the article its address names and the version it pins, in the record's order", () => {
  const view = readingView(PINNED);
  const state = view.states[0];
  const article = state.articles[0].publisherId;
  const crumbs = readingCrumbs(view, { permalink: state.permalink, article, locale: "en" });
  assert.deepEqual(crumbs.map((crumb) => crumb.part), ["publisher", "act", "article", "version"]);
  assert.deepEqual(crumbs.map((crumb) => crumb.part), CRUMB_PARTS.filter((part) => crumbs.some((crumb) => crumb.part === part)), "the record's order");
  assert.equal(crumbs[0].text, EN.publishers["lu-legilux"]);
  assert.equal(crumbs[0].href, null, "the publisher has no page here");
  assert.equal(crumbs[1].text, view.workKey);
  assert.equal(crumbs[1].href, `/en/lu-legilux/${view.workKey}`, "the act links to its dossier at its address");
  assert.equal(crumbs[2].text, article);
  assert.equal(crumbs[3].current, true, "the version is the page");
  assert.equal(crumbs[3].href, null, "the page's own crumb is no link");
  assert.equal(crumbs.filter((crumb) => crumb.current).length, 1);
  assert.equal(crumbs[3].text, versionWords(view, state, EN));
});

test("the version is said by its date's role: a state applying from its date, an act dated by its entry into force never so (R21)", () => {
  const view = readingView(READING);
  const state = view.states[0];
  assert.equal(versionWords(view, { ...state, dateRole: "applicability" }, EN), fillText(EN.state, { date: state.applicabilityDate }));
  const entry = versionWords(view, { ...state, dateRole: "entry_into_force" }, EN);
  assert.equal(entry, fillText(EN.stateEntryIntoForce, { date: state.applicabilityDate }));
  assert.doesNotMatch(entry, /appl(?:y|ies|ying|icable)/i, "an entry into force is never said as an application");
  assert.doesNotMatch(versionWords(view, { ...state, dateRole: "entry_into_force" }, FR), /applicable|s[’']appliqu/i);
});

test("an EU reading names its wording by its own date, never with a Luxembourg date word, and a reading of several versions names none", () => {
  const view = readingView(europeEnvelope());
  const crumbs = readingCrumbs(view, { locale: "en" });
  if (view.wordings.length === 1) {
    assert.deepEqual(crumbs.map((crumb) => crumb.part), ["publisher", "act", "version"]);
    assert.equal(crumbs[0].text, EN.publishers["eu-eurlex"]);
    assert.equal(crumbs[1].href, `/en/eu-eurlex/${view.celex}`);
    assert.match(crumbs[2].text, /wording of \d{4}-\d{2}-\d{2}$/);
  }
  for (const wording of view.wordings) {
    for (const words of [versionWords(view, wording, EN), versionWords(view, wording, FR)]) {
      assert.doesNotMatch(words, /\b(?:appl(?:y|ies|ying|icability)|versions?|states?)\b|s[’']appliqu|applicab/i, words);
    }
  }
  const several = { ...view, wordings: [view.wordings[0], { ...view.wordings[0], permalink: `${view.wordings[0].permalink}-other` }] };
  assert.deepEqual(readingCrumbs(several, { locale: "en" }).map((crumb) => crumb.part), ["publisher", "act"], "no version is the page");
  assert.equal(readingCrumbs(several, { locale: "en" }).some((crumb) => crumb.current), false);
});

test("an article the version shown does not hold is not named, and a page with no answer has no breadcrumbs", () => {
  const view = readingView(PINNED);
  const crumbs = readingCrumbs(view, { permalink: view.states[0].permalink, article: "art_no_such_article", locale: "en" });
  assert.deepEqual(crumbs.map((crumb) => crumb.part), ["publisher", "act", "version"]);
  assert.deepEqual(readingCrumbs(null), []);
  assert.deepEqual(dossierCrumbs(null), []);
});

test("a dossier names the publisher and the act, which is the page, for Luxembourg and for the EU; in French the French table's words", () => {
  const view = dossierView(DOSSIER);
  const crumbs = dossierCrumbs(view, { locale: "en" });
  assert.deepEqual(crumbs.map((crumb) => [crumb.part, crumb.current, crumb.href]), [["publisher", false, null], ["act", true, null]]);
  assert.equal(crumbs[1].text, view.workKey);
  const europe = dossierCrumbs(dossierView(EU_DOSSIER), { locale: "fr" });
  assert.equal(europe[0].text, FR.publishers["eu-eurlex"]);
  assert.equal(europe[1].text, dossierView(EU_DOSSIER).celex);
  const french = readingCrumbs(readingView(PINNED), { permalink: readingView(PINNED).states[0].permalink, locale: "fr" });
  assert.equal(french[0].text, FR.publishers["lu-legilux"]);
  assert.equal(french[1].href, `/fr/lu-legilux/${readingView(PINNED).workKey}`);
});

test("the reading page at a permalink, and the dossier page at a work's address, carry the breadcrumbs as a landmark the server renders", () => {
  const view = readingView(PINNED);
  const state = view.states[0];
  const article = state.articles[0].publisherId;
  const html = renderLiveReadingPage({ initial: { envelope: PINNED, path: `/en${state.permalink}/${article}` } });
  const nav = /<nav class="breadcrumbs" aria-label="([^"]+)" data-breadcrumbs="([^"]+)">(.*?)<\/nav>/s.exec(html);
  assert.ok(nav, "the reading page names its breadcrumbs");
  assert.equal(nav[1], EN.label);
  assert.equal(nav[2], "publisher act article version");
  assert.match(nav[3], new RegExp(`<a href="/en/lu-legilux/${view.workKey}"><code>${view.workKey}</code></a>`));
  assert.match(nav[3], /<li data-crumb="version" aria-current="page">[^<]+<\/li>/);
  assert.ok(html.indexOf('class="breadcrumbs"') < html.indexOf('role="search"'), "the breadcrumbs come before the form");

  const dossier = renderLiveDossierPage({ initial: { envelope: DOSSIER, path: `/en/lu-legilux/${dossierView(DOSSIER).workKey}` } });
  assert.match(dossier, /<nav class="breadcrumbs" aria-label="Breadcrumb" data-breadcrumbs="publisher act">/);
  assert.match(dossier, /<li data-crumb="act" aria-current="page"><code>[^<]+<\/code><\/li>/);

  const unmounted = renderLiveReadingPage({ initial: { envelope: UNMOUNTED, path: `/en${state.permalink}` } });
  assert.doesNotMatch(unmounted, /class="breadcrumbs"/, "no answer, no breadcrumbs");
  assert.doesNotMatch(renderLiveReadingPage(), /class="breadcrumbs"/, "the page as built has none");
});

test("each banner ends with its info control: a link to the section of How to read Lex that explains it, named by its heading", () => {
  const guide = LIVE_CHROME.en.guide.sections;
  const facts = {
    builtOn: "2026-10-01", today: "2026-12-01", stale: true, maximumAgeDays: 30, indexCommit: null, serviceCommit: null,
    channels: [{ publisher: "lu-legilux", completedOn: "2026-10-01", ageDays: 61, stale: true }],
  };
  const link = (section) => `<a class="info-control" href="/en/how-to-read.html#guide-${section}" aria-label="${fillText(LIVE_CHROME.en.reference.infoNamed, { section: guide[section].heading })}" data-guide-link="${section}">${LIVE_CHROME.en.reference.info}</a>`;
  assert.equal(renderToStaticMarkup(h(GuideLink, { section: "freshness" })), link("freshness"));
  for (const [name, html] of [
    ["the staleness banner", renderToStaticMarkup(h(StalenessBanner, { facts }))],
    ["the strip with an answer", renderToStaticMarkup(h(FreshnessStrip, { facts }))],
    ["the strip as built", renderToStaticMarkup(h(FreshnessStrip, {}))],
    ["the strip with no answer", renderToStaticMarkup(h(FreshnessStrip, { answers: false }))],
  ]) assert.ok(html.includes(link("freshness")), name);
  const asPublished = renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome(answerHandedWith("evidence_bundle", READING, { contract: LIVE_CONTRACT, objectType: "evidence_bundle" })), onRetry: () => {} }));
  if (/data-as-published=/.test(asPublished)) assert.ok(asPublished.includes(link("versions")), "the as-published label");
  const europe = renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome(handed(europeEnvelope())), onRetry: () => {} }));
  assert.ok(europe.indexOf("data-authenticity") < europe.indexOf(link("versions")), "the EU statement, then its info control");
  assert.throws(() => guideSectionHref("nowhere"), /no section a banner points to/);
});

test("How to read Lex holds every section a banner points to, in both languages", () => {
  for (const language of ["en", "fr"]) {
    for (const section of GUIDE_LINK_SECTIONS) {
      assert.ok(LIVE_CHROME[language].guide.sections[section].heading.length > 0, `${language} ${section}`);
    }
  }
  const html = renderLiveGuidePage();
  for (const section of GUIDE_LINK_SECTIONS) assert.match(html, new RegExp(`<h2 id="guide-${section}">`), section);
});
