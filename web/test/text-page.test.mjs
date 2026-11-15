// The text page's rules (the owner's brief of 2026-11-15, section 4), each on the shape of a real answer of the live API
// (15/11/2026): the insurance contract law's article 44 and article 38 in its versions of 16/04/2011 and 01/03/2013.
import assert from "node:assert/strict";
import test from "node:test";
import {
  alignParagraphs, articleNumberOf, articleParts, axisGeometry, changeIndex, cleanTitle, ownTitleOf, sameVisibleWording, versionAt, versionsOf, wordDiff,
} from "../scripts/text-page.mjs";
import { textHref, textStateOf } from "../scripts/text-address.mjs";
import { CompareArticle, ReadArticle, numericDate, renderLiveTextPage, typedDate } from "../.react-build/app.mjs";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

const ART_44 = "Article 44Délai de prescription1.Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.Le délai court à partir du jour de l'événement qui donne ouverture à l'action.2.Sous réserve de dispositions légales particulières, l'action se prescrit par cinq ans.3.L'action récursoire de l'assureur contre l'assuré se prescrit par trois ans.";
const ART_38_2011 = "Article 38Durée des obligationsLa durée du contrat est fixée par les parties.Toutefois, et sauf pour les assurances sur la vie et l'assurance maladie, le preneur d'assurance a le droit de résilier le contrat chaque année à l'échéance de la prime annuelle, ou, à défaut à la date anniversaire de la prise d'effet du contrat, en envoyant une lettre recommandée à l'assureur au moins trois mois avant cette date. Ce droit appartient dans les mêmes conditions à l'assureur.";
const ART_38_2013 = "Article 38Durée des obligationsLa durée du contrat est fixée par les parties.Toutefois, et sauf pour les assurances sur la vie et l'assurance maladie, le preneur d'assurance a le droit de résilier le contrat chaque année à l'échéance de la prime annuelle, ou, à défaut à la date anniversaire de la prise d'effet du contrat, ci-après dénommée date de reconduction, dans les formes prévues à l’article 39, en notifiant cette résiliation à l’assureur trente jours avant cette date. Ce droit appartient dans les mêmes conditions à l’assureur.Sans préjudice de l’alinéa précédent, l'avis d'échéance doit accorder un délai minimum de trente jours.";

test("an article's text, run together as the API serves it, is read back into its heading, paragraphs and numbered points", () => {
  const parts = articleParts(ART_44);
  assert.equal(parts.heading, "Délai de prescription");
  assert.deepEqual(parts.paragraphs.map((p) => p.marker), ["1.", null, "2.", "3."]);
  assert.equal(parts.paragraphs[0].text, "Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.");
  assert.equal(parts.paragraphs[2].text.startsWith("Sous réserve"), true);
  // Every word is the text's own, in its order: nothing is added or dropped but the article's own number.
  const rebuilt = [parts.heading, ...parts.paragraphs.map((p) => `${p.marker ?? ""}${p.text}`)].join("");
  assert.equal(rebuilt, ART_44.replace(/^Article 44/u, ""));
  // An apostrophe before a capital is no new paragraph ("l'Union"), nor an acronym.
  assert.equal(articleParts("Article 3ObjetLa loi vise l'Union et la CSSF.").paragraphs.length, 1);
});

test("an article's number is the publisher's id as it is cited", () => {
  assert.equal(articleNumberOf("art_44"), "44");
  assert.equal(articleNumberOf("art_1er"), "1er");
  assert.equal(articleNumberOf("art_62-11"), "62-11");
});

test("the text's own title, never an amending act's (trap 2)", () => {
  const police = {
    titles: [
      { language: "fra", titles: [{ title: "Loi du 18 juillet 2018 sur la Police grand-ducale." }], short_titles: [] },
    ],
    publisher_relations: [{ title: "Loi du 29 juillet 2020 portant modification de la loi modifiée du 18 juillet 2018 sur la Police grand-ducale." }],
  };
  assert.equal(ownTitleOf(police), "Loi du 18 juillet 2018 sur la Police grand-ducale");
  assert.equal(cleanTitle("Version consolidée applicable au 25/11/2025 : Loi du 27 juillet 1997 sur le contrat d'assurance."), "Loi du 27 juillet 1997 sur le contrat d'assurance");
  assert.equal(ownTitleOf({}), null);
});

test("the versions and their bars come from one changes_in_period answer: the insurance contract law's 4, 0, 5, 1, 18, 1, 1, 1, 1", () => {
  const dates = ["2006-12-25", "2007-12-21", "2011-04-16", "2013-03-01", "2015-04-20", "2016-01-01", "2018-11-01", "2020-01-01", "2021-01-01", "2025-11-25"];
  const counts = [null, [1, 3], [0, 0], [0, 5], [0, 1], [3, 15], [0, 1], [0, 1], [0, 1], [1, 0]];
  const changes = {
    changes: dates.map((date, i) => ({
      reason: i === 0 ? "first_held_state" : null,
      baseline: i === 0 ? null : { applicability_date: dates[i - 1] },
      state: { applicability_date: date, applicable_to: dates[i + 1] ?? null, article_count: 147 },
      counts: counts[i] === null ? null : { added: counts[i][0], changed: counts[i][1], removed: 0, unchanged: 140 },
    })).reverse(),
  };
  const versions = versionsOf(changes);
  assert.deepEqual(versions.map((v) => v.bar), [null, 4, 0, 5, 1, 18, 1, 1, 1, 1]);
  assert.equal(versions[0].first, true);
  assert.equal(versions[3].previous, "2011-04-16");
  assert.equal(versionAt(versions, "2012-06-01").date, "2011-04-16");
  assert.equal(versionAt(versions, "2006-01-01"), null);
  const axis = axisGeometry(versions, "2026-11-15", 1000);
  assert.ok(axis.at("2006-12-25") === axis.x0 && Math.abs(axis.at("2026-11-15") - axis.x1) < 0.001, "the first held to today, end to end");
  assert.ok(axis.at("2013-03-01") > axis.at("2011-04-16"), "in proportion to the calendar");
});

test("which versions changed each article, from the pairwise comparisons", () => {
  const index = changeIndex([
    { requested_date_to: "2013-03-01", comparisons: [{ to: { applicability_date: "2013-03-01" }, articles: [{ publisher_id: "art_38", status: "changed" }, { publisher_id: "art_44", status: "unchanged" }] }] },
    { requested_date_to: "2016-01-01", comparisons: [{ to: { applicability_date: "2016-01-01" }, articles: [{ publisher_id: "art_38", status: "unchanged" }, { publisher_id: "art_62-11", status: "added" }] }] },
  ]);
  assert.deepEqual(index.get("art_38"), ["2013-03-01"]);
  assert.deepEqual(index.get("art_62-11"), ["2016-01-01"]);
  assert.equal(index.has("art_44"), false);
});

test("article 38 compared, 16/04/2011 with 01/03/2013: the three months removed, the thirty days added, a new paragraph facing 'not in this version'", () => {
  const rows = alignParagraphs(articleParts(ART_38_2011).paragraphs, articleParts(ART_38_2013).paragraphs);
  assert.deepEqual(rows.map((r) => r.kind), ["same", "changed", "added"]);
  const removed = rows[1].diff.left.filter((r) => r.kind === "removed").map((r) => r.text).join("");
  const added = rows[1].diff.right.filter((r) => r.kind === "added").map((r) => r.text).join("");
  assert.ok(removed.includes("en envoyant une lettre recommandée à l'assureur au moins trois mois"), removed);
  assert.ok(added.includes("trente jours"), added);
  // ' and ’ read as one: "l’assureur" is not a change of "l'assureur".
  assert.deepEqual(wordDiff("à l'assureur.", "à l’assureur.").right.map((r) => r.kind), ["same"]);
  // Trap 1: a wording whose only difference is typography reads the same.
  assert.equal(sameVisibleWording("l'assureur  doit", "l’assureur doit"), true);
});

test("the text page's address carries the text, the date, the article and the compare pair, and never a word typed", () => {
  const href = textHref({ identifier: "/lu-legilux/loi-1997-07-27-n10", from: "2011-04-16", to: "2013-03-01", article: "art_38" }, "fr");
  assert.equal(href, "/fr/text.html?text=%2Flu-legilux%2Floi-1997-07-27-n10&from=2011-04-16&to=2013-03-01&article=art_38");
  const state = textStateOf(href.slice(href.indexOf("?")));
  assert.deepEqual({ ...state }, { identifier: "/lu-legilux/loi-1997-07-27-n10", date: null, article: "art_38", from: "2011-04-16", to: "2013-03-01", view: "side" });
  assert.equal(textHref({ identifier: "délai de prescription" }, "fr"), null, "words are not a text");
  assert.equal(textStateOf("?text=x%20y&date=2013-02-30").identifier, null);
});

test("dates are dd/mm/yyyy in both languages, and a typed dd/mm/yyyy is read back only when it is a calendar date", () => {
  assert.equal(numericDate("2013-03-01"), "01/03/2013");
  assert.equal(typedDate("1/3/2013"), "2013-03-01");
  assert.equal(typedDate("31/02/2013"), null);
});

test("the text page as built is the loading state inside the V4 frame, with the dock, and shows no identifier or digest", () => {
  const html = renderLiveTextPage();
  assert.ok(html.includes('data-layout="v4"') && html.includes('data-preview-state="live-text"'));
  assert.ok(html.includes('id="live-text-root"') && html.includes('aria-busy="true"'));
  assert.ok(html.includes('class="lx-dock"') && html.includes("client-live-dock.js") && html.includes("client-live-text.js"));
  assert.ok(!/lu-legilux|art_[0-9]|[0-9a-f]{64}/u.test(html.replace(/<script[\s\S]*?<\/script>/gu, "")), "no address, article id or digest on the page");
});

test("compared wording is marked in the text's own language, the interface's labels in the page's (the review of #1062)", () => {
  const row = { publisher_id: "art_38", status: "changed", from: [{ text: ART_38_2011 }], to: [{ text: ART_38_2013 }] };
  const versions = [{ date: "2011-04-16" }, { date: "2013-03-01" }];
  for (const textLang of ["fr", "en"]) {
    const markup = renderToStaticMarkup(h(CompareArticle, { row, versions, changedDates: ["2013-03-01"], compare: { from: "2011-04-16", to: "2013-03-01" }, view: "side", index: 0, total: 1, onStep: () => {}, labelOf: () => "38", textLang }));
    const paragraphs = [...markup.matchAll(/<p class="lx-cmp-p[^"]*" lang="([a-z]+)">/g)].map((m) => m[1]);
    assert.ok(paragraphs.length >= 4 && paragraphs.every((lang) => lang === textLang), `${textLang}: ${paragraphs}`);
    assert.ok(!/class="lx-compare-grid" lang=/.test(markup), "the grid's column labels are the interface's");
  }
});

test("an article reads before the versions that changed it are known, then says when it changed (the candidate's screenshot of 2026-11-15: the page threw)", () => {
  const versions = [{ date: "2011-04-16" }, { date: "2013-03-01" }];
  const article = { publisher_id: "art_38", text: ART_38_2013, language: "fra", official_source: "https://legilux.public.lu/x" };
  const props = { entry: { id: "art_38", number: "38", textHeld: true }, article, parts: articleParts(ART_38_2013), versions, identifier: "/lu-legilux/loi-1997-07-27-n10", highlighted: false, version: versions[1], onCompareOn: () => {} };
  const before = renderToStaticMarkup(h(ReadArticle, { ...props, changedDates: null }));
  assert.ok(before.includes("Durée des obligations") && !before.includes("Wording changed"));
  const after = renderToStaticMarkup(h(ReadArticle, { ...props, changedDates: ["2013-03-01"] }));
  assert.ok(after.includes("Wording changed on 01/03/2013"));
});

test("the search page lists first the texts whose own title holds the words, the shortest first (the engine's ranking unchanged)", async () => {
  const { byTitleMatch } = await import("../scripts/find-results.mjs");
  const rows = [
    { title: "Loi du 20 novembre 2025 portant modification" },
    { title: "Loi du 6 décembre 2019 relative aux détecteurs de fumée et modifiant la loi modifiée du 27 juillet 1997 sur le contrat d’assurance" },
    { title: "Loi du 27 juillet 1997 sur le contrat d'assurance" },
  ];
  assert.deepEqual(byTitleMatch(rows, "contrat d'assurance").map((row) => row.title.slice(0, 22)), ["Loi du 27 juillet 1997", "Loi du 6 décembre 2019", "Loi du 20 novembre 202"]);
  assert.equal(byTitleMatch(rows, ""), rows);
});
