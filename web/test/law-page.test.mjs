// The law page (the owner's order of 2026-11-13, section B), held to its acceptance on the 1997 insurance-contract law
// (loi-1997-07-27-n10): its ten versions from 2006-12-25 to 2025-11-25, the version a date of 3 March 2019 falls in (the one
// of 2018-11-01), the latest compared with the one before it when the page opens, and a heading that is the act's own title,
// never the publisher's "Version consolidée applicable au …" prefix. The versions below are the live dossier's of
// 2026-11-13 (dates and article counts); the comparison is the answer census's.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { CompareAnswerView, LawHeader, LawTimeline } from "../.react-build/app.mjs";
import { actTitle, actTitleOf, afterClick, articleHeadingOf, articleNumberOf, defaultPair, lawStatus, readClaimDate, versionOn, versionsOf, yearOf } from "../scripts/law-page.mjs";
import { displayDate } from "../scripts/live-chrome.mjs";
import { compareOutcome } from "../scripts/live-compare.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));

const WORK = "loi-1997-07-27-n10";
const DATES = ["2006-12-25", "2007-12-21", "2011-04-16", "2013-03-01", "2015-04-20", "2016-01-01", "2018-11-01", "2020-01-01", "2021-01-01", "2025-11-25"];
const ARTICLES = [142, 143, 143, 143, 143, 146, 146, 146, 146, 147];
const digest = (index) => (String(index) + "0123456789abcdef".repeat(4)).slice(0, 64);
const expression = (date) => `http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/${date.replaceAll("-", "")}/fr`;

/** The dossier view of the 1997 law as `readDossier` gives it, its versions in the order the answer lists them. */
function insuranceLaw() {
  const states = DATES.map((date, index) => Object.freeze({
    language: "fra",
    applicabilityDate: date,
    dateRole: "applicability",
    entryIntoForce: null,
    nextApplicabilityDate: DATES[index + 1] ?? null,
    stateSha256: digest(index),
    expressionIri: expression(date),
    publisherLegalResourceIri: expression(date).replace(/\/fr$/, ""),
    articleCount: ARTICLES[index],
    articlesNotAdmitted: 1,
    stableCoordinate: `/lu-legilux/${WORK}/${date}`,
    permalink: `/lu-legilux/${WORK}/${date}--${digest(index)}/fra`,
  }));
  const titles = DATES.map((date) => Object.freeze({
    language: "fra",
    expressionIri: expression(date),
    titles: [{ title: `Version consolidée applicable au ${date.split("-").reverse().join("/")} : Loi du 27 juillet 1997 sur le contrat d'assurance.`, evidenceSha256: digest(1) }],
    shortTitles: [],
  }));
  return { publisher: "lu-legilux", workKey: WORK, states, titles };
}

test("the act's own title heads the page, never the publisher's version prefix", () => {
  assert.equal(actTitleOf("Version consolidée applicable au 25/12/2006 : Loi du 27 juillet 1997 sur le contrat d'assurance."),
    "Loi du 27 juillet 1997 sur le contrat d'assurance.");
  assert.equal(actTitleOf("Texte coordonné au 1er janvier 2020 : Code de la sécurité sociale"), "Code de la sécurité sociale");
  assert.equal(actTitleOf("Loi du 12 mai 2020 portant sur le Trésor"), "Loi du 12 mai 2020 portant sur le Trésor", "a title with no prefix is itself");
  assert.deepEqual(actTitle(insuranceLaw()), { title: "Loi du 27 juillet 1997 sur le contrat d'assurance.", language: "fra" });
  const markup = renderToStaticMarkup(h(LawHeader, { view: insuranceLaw(), today: "2026-11-14" }));
  assert.ok(markup.includes("Loi du 27 juillet 1997 sur le contrat d&#x27;assurance."), markup);
  assert.ok(!markup.includes("Version consolidée"), "the version prefix is never the act's heading");
  assert.ok(markup.includes('data-law-status="in_force"') && markup.includes("In force"));
  assert.ok(markup.includes("147 articles") && markup.includes("10 versions"), markup);
});

test("the 1997 law has ten versions from 2006-12-25 to 2025-11-25, and a claim of 3 March 2019 falls in the one of 2018-11-01", () => {
  const view = insuranceLaw();
  const versions = versionsOf({ ...view, states: [...view.states].reverse() });
  assert.deepEqual(versions.map((version) => version.applicabilityDate), DATES, "oldest first, whatever the answer's order");
  assert.equal(versions[versionOn(versions, "2019-03-03")].applicabilityDate, "2018-11-01");
  assert.equal(versions[versionOn(versions, "2020-01-01")].applicabilityDate, "2020-01-01", "a version applies from its own date");
  assert.equal(versions[versionOn(versions, "2019-12-31")].applicabilityDate, "2018-11-01", "and the one before until the day before");
  assert.equal(versions[versionOn(versions, "2030-01-01")].applicabilityDate, "2025-11-25", "the latest has no end");
  assert.equal(versionOn(versions, "2000-01-01"), -1, "no version held before the first");
  assert.equal(versionOn(versions, "03/03/2019"), -1, "a date is a calendar date");
  assert.equal(lawStatus(view, "2026-11-14"), "in_force");
  // The review of #1046: on 2025-01-01 the version of 2021-01-01 applies, though the latest applies only from 2025-11-25.
  assert.equal(lawStatus(view, "2025-01-01"), "in_force", "an earlier version applies that day");
  assert.equal(lawStatus(view, "2006-01-01"), "not_yet", "before its first version");
  const ended = { ...view, states: view.states.map((state, index) => (index === 9 ? { ...state, nextApplicabilityDate: "2026-01-01" } : state)) };
  assert.equal(lawStatus(ended, "2026-11-14"), null, "no version applies today and one applied before: no status claimed");
  assert.equal(lawStatus(view, null), null, "no today, no status");
});

test("one click reads a version, a second compares the two, and the page opens on the latest against the one before", () => {
  assert.deepEqual(defaultPair(versionsOf(insuranceLaw())), [8, 9]);
  assert.equal(defaultPair(versionsOf({ ...insuranceLaw(), states: insuranceLaw().states.slice(0, 1) })), null, "one version, nothing to compare");
  assert.deepEqual(afterClick({ read: 8, other: 9 }, 3), { read: 3, other: null }, "with two compared, a click reads that one");
  assert.deepEqual(afterClick({ read: 3, other: null }, 7), { read: 3, other: 7 }, "a second click compares the two");
  assert.deepEqual(afterClick({ read: 7, other: null }, 3), { read: 3, other: 7 }, "the earlier first");
  assert.deepEqual(afterClick({ read: 3, other: null }, 3), { read: 3, other: null }, "the same one again reads it");

  const markup = renderToStaticMarkup(h(LawTimeline, { view: insuranceLaw(), today: "2026-11-14" }));
  assert.equal((markup.match(/class="timeline-dot"/g) ?? []).length, 10, "a point for each version");
  assert.equal((markup.match(/aria-pressed="true"/g) ?? []).length, 2, "two chosen");
  assert.ok(markup.includes('data-chosen-versions="2021-01-01/2025-11-25"'), markup);
  assert.ok(markup.includes("Compared: the version of 2021-01-01 and the version of 2025-11-25."));
  // The date of a claim is typed as a reader writes it (the owner's order of 2026-11-14): its one text field.
  const fields = markup.match(/<input[^>]*type="text"[^>]*>/g) ?? [];
  assert.equal(fields.length, 1, "no identifier to type on the law page, only the date of a claim");
  assert.ok(markup.includes("Date of the claim") && fields[0].includes('placeholder="yyyy-mm-dd"'), fields[0]);
  // The page's one disclosure at its foot, closed: the technical details, which hold the act's key and its record.
  assert.match(markup, /<details class="law-technical" data-law-technical=""><summary>Technical details · <code>loi-1997-07-27-n10<\/code><\/summary>/);
  assert.ok(!markup.slice(0, markup.indexOf("data-law-technical")).replace(/<[^>]+>/g, "").includes("loi-1997-07-27-n10"), "no identifier said above it");
  assert.ok(!markup.includes("data-answer-state"), "the comparison is asked in the browser, not rendered by the server");
});

test("on the law page an article is named as a reader names it, unchanged ones since the earlier year, each with its actions", () => {
  assert.equal(articleNumberOf("art_62-11"), "62-11");
  assert.equal(articleNumberOf("annexe_1"), "annexe_1");
  assert.equal(yearOf("2021-01-01"), "2021");
  const row = answers.sampled.filter((sample) => sample.operation === "diff")[1];
  const envelope = structuredClone(census.envelopes.find((candidate) => candidate.operation === "diff" && candidate.scenario.startsWith("the state on one date against the state on the same date")).envelope);
  envelope.result.value = withDigests(row.answer);
  const outcome = compareOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const law = { actions: (moved) => h("p", { "data-test-actions": moved.publisherId }) };
  const markup = renderToStaticMarkup(h(CompareAnswerView, { outcome, law }));
  const comparison = outcome.view.comparisons[0];
  const year = comparison.from.applicabilityDate.slice(0, 4);
  assert.ok(markup.includes(`unchanged since ${year}`), "the unchanged articles say since when");
  assert.ok(!markup.includes("(wording "), "no digests on the law page");
  for (const article of comparison.articles) {
    assert.ok(markup.includes(`data-test-actions="${article.publisherId}"`), `${article.publisherId} has its actions`);
  }
  assert.ok(markup.indexOf('data-status="unchanged"') > markup.indexOf('data-moved'), "the moved articles first");
  const plain = renderToStaticMarkup(h(CompareAnswerView, { outcome }));
  assert.ok(plain.includes("(wording "), "the compare page keeps its digests");
  assert.ok(!plain.includes("unchanged since"), "and its own disclosure");
});

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === "<varies-per-run>") { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

test("an article's heading is read from its text as the platform's search reads it, and running text is no heading", () => {
  assert.equal(articleHeadingOf("Article 44Délai de prescription1.Le délai de prescription de toute action"), "Délai de prescription");
  assert.equal(articleHeadingOf("Article 11Obligation de déclarationLe preneur d’assurance a"), "Obligation de déclaration");
  assert.equal(articleHeadingOf("Art. 74.Garanties accessoiresMême lorsque"), "Garanties accessoires");
  assert.equal(articleHeadingOf("Article 62-11Actions en cessation ou en interdictionLes mesures"), "Actions en cessation ou en interdiction");
  assert.equal(articleHeadingOf("Article 50 Transparency obligations for providers and deployers of certain AI systems 1. Providers shall"),
    "Transparency obligations for providers and deployers of certain AI systems");
  assert.equal(articleHeadingOf("Art. 15.(1)L’Assemblée générale annuelle se tient"), "", "a paragraph's number first");
  assert.equal(articleHeadingOf("Art. 3.Le présent règlement s’applique aux avocats. Il entre"), "", "a sentence first");
  assert.equal(articleHeadingOf("Le contrat est formé."), "", "no article number");
});

test("the date of a claim is read as a reader writes it, day first, and a French page writes a date day first", () => {
  assert.equal(readClaimDate("03/03/2019"), "2019-03-03");
  assert.equal(readClaimDate(" 3/3/2019 "), "2019-03-03", "one digit and spaces around");
  assert.equal(readClaimDate("25.11.2025"), "2025-11-25");
  assert.equal(readClaimDate("25-11-2025"), "2025-11-25");
  assert.equal(readClaimDate("2019-03-03"), "2019-03-03", "a calendar date as it is");
  assert.equal(readClaimDate("31/02/2019"), null, "no such day");
  assert.equal(readClaimDate("03/2019"), null);
  assert.equal(readClaimDate("hier"), null);
  assert.equal(readClaimDate(""), null);
  const versions = versionsOf(insuranceLaw());
  assert.equal(versions[versionOn(versions, readClaimDate("03/03/2019"))].applicabilityDate, "2018-11-01", "the owner's case, typed");
  assert.equal(displayDate("2025-11-25", "fr"), "25/11/2025");
  assert.equal(displayDate("2025-11-25", "en"), "2025-11-25");
  assert.equal(displayDate("loi-2025-11-25", "fr"), "loi-2025-11-25", "only a whole date");
  assert.equal(displayDate(null, "fr"), null);
});
