// The EU reading: the original wording an EU index holds, read from the EU `evidence_bundle` and shown on the reading
// screen with the acknowledgement and authenticity statement Decision 95 requires (the owner's proxy, 2026-10-01
// 14:30 UTC: serve EU text now).
//
// The answer here is the hand-built EU bundle the web tests share (`scripts/europe-bundle-sample.mjs`), in the shape
// `V3CorpusMount.EvidenceBundleEurope` sends; it stands in until the answer census captures an EU bundle.

import assert from "node:assert/strict";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { ExportAnswerView, ReadingAnswerView } from "../.react-build/app.mjs";
import { EUROPE_SAMPLE, europeAnnexRow as annexRow, europeBundle, europeEnvelope as envelopeOf, sha256 } from "../scripts/europe-bundle-sample.mjs";
import { EUROPE_EXPORT_SCHEMA } from "../scripts/export-build.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { NOTHING_PINNED, exportState, pinKey } from "../scripts/live-export.mjs";
import { LIVE_READING_EUROPE_REFUSAL_SENTENCES, LIVE_READING_REFUSAL_SENTENCES, readingOutcome } from "../scripts/live-reading.mjs";
import { EUROPE_TEXT_ACKNOWLEDGEMENT, readEuropeEvidenceBundle, readEvidenceBundleAnswer } from "../scripts/reading-answer.mjs";
import { escapeProvision } from "../scripts/search-answer.mjs";

const { work: WORK, bodySha256: BODY, permalink: PERMALINK, articles: ARTICLES } = EUROPE_SAMPLE;

test("the EU bundle reads: the wording on its date, pinned, its quoted articles with their evidence, and what it does not hold", () => {
  const view = readEuropeEvidenceBundle(europeBundle());
  assert.equal(view.publisher, "eu-eurlex");
  assert.equal(view.acknowledgement, EUROPE_TEXT_ACKNOWLEDGEMENT);
  assert.match(view.authenticity, /Regulation \(EU\) No 216\/2013/);
  assert.equal(view.wordings.length, 1);
  const [wording] = view.wordings;
  assert.equal(wording.permalink, PERMALINK);
  assert.deepEqual(wording.articles.map((article) => article.publisherId), ["001", "002"]);
  for (const article of wording.articles) {
    assert.equal(article.textSha256, sha256(article.text), "the digest is the text's");
    assert.equal(article.permalink, `${PERMALINK}#${article.publisherId}`);
    assert.equal(article.bodySha256, BODY);
  }
  assert.deepEqual(view.notHeld.map((row) => row.item), ["later_wordings", "force_dates"]);
  assert.equal(readEvidenceBundleAnswer(europeBundle()).publisher, "eu-eurlex", "the dispatcher sends an EU bundle to the EU reader");
});

test("each rule the EU bundle states about itself is refused when broken, with that rule's reason", () => {
  const cases = [
    ["another acknowledgement", (b) => { b.acknowledgement = "© EU"; }, /acknowledgement/],
    // A held consolidation is no longer a broken rule: the time view (#909) quotes consolidated versions.
    ["a wording of a later date", (b) => { b.wordings[0].wording_date = "2016-05-04"; }, /never answers a date before its own/],
    ["a permalink of another wording", (b) => { b.wordings[0].permalink = PERMALINK.replace("--5", "--6"); }, /not the wording it pins/],
    ["a coordinate that is not the permalink's", (b) => { b.wordings[0].stable_coordinate = "/eu-eurlex/32016R0679/eng"; }, /coordinate/],
    ["a source not acquired", (b) => { b.wordings[0].sources[0].outcome = "rights_withheld"; }, /only from acquired sources/],
    ["a text of another length", (b) => { b.wordings[0].articles[0].text_byte_length += 1; }, /bytes of text/],
    ["an article in another language", (b) => { b.wordings[0].articles[1].language = "fra"; }, /its wording is in eng/],
    ["a body that is not a source's", (b) => { b.wordings[0].articles[0].body_sha256 = "9".repeat(64); }, /does not name as a source/],
    ["an article permalink of another provision", (b) => { b.wordings[0].articles[0].article_permalink = `${PERMALINK}#002`; }, /not its wording's with 001/],
    ["a wording in a language not asked", (b) => { b.requested_language = "fra"; b.available_languages = ["eng", "fra"]; }, /asked in fra/],
    ["the same provision quoted twice", (b) => { b.wordings[0].articles[1] = b.wordings[0].articles[0]; }, /quotes 001 twice/],
    ["no wording", (b) => { b.wordings = []; }, /at least one wording/],
    ["a Luxembourg bundle", (b) => { b.publisher = "lu-legilux"; }, /reads the EU/],
    ["the annex list dropped", (b) => { delete b.wordings[0].annexes_not_served; }, /has no annexes_not_served/],
    ["an annex served as text", (b) => { b.wordings[0].annexes_not_served = [annexRow({ served_as: "text" })]; }, /an annex's text is never served/],
    ["an annex disposition outside the corpus's", (b) => { b.wordings[0].annexes_not_served = [annexRow({ disposition: "annex_quoted" })]; }, /not an annex disposition/],
    ["one annex disposition listed twice", (b) => { b.wordings[0].annexes_not_served = [annexRow(), annexRow()]; }, /repeats the disposition/],
    ["an annex count its digests are not", (b) => { b.wordings[0].annexes_not_served = [annexRow({ annexes: 2 })]; }, /does not name exactly that many annex digests/],
    ["an annex with no official source", (b) => { b.wordings[0].annexes_not_served = [annexRow({ official_source: "" })]; }, /official_source is not text/],
  ];
  for (const [what, change, reason] of cases) {
    assert.throws(() => readEuropeEvidenceBundle(europeBundle(change)), reason, what);
  }
});

test("an EU provision is escaped as the platform escapes it in an article's permalink", () => {
  const id = "Article 1(2)";
  const bundle = europeBundle((b) => {
    const article = b.wordings[0].articles[0];
    article.publisher_id = id;
    article.article_permalink = `${PERMALINK}#${escapeProvision(id)}`;
  });
  assert.equal(readEuropeEvidenceBundle(bundle).wordings[0].articles[0].permalink, `${PERMALINK}#Article%201%282%29`);
});

test("the reading screen shows the EU wording: the acknowledgement and authenticity above the quoted text, each quote with its evidence", () => {
  const outcome = readingOutcome({ state: "success", envelope: envelopeOf(europeBundle()) });
  assert.equal(outcome.state, "success");
  const markup = renderToStaticMarkup(h(ReadingAnswerView, { outcome }));
  assert.ok(markup.indexOf("© European Union, https://eur-lex.europa.eu") < markup.indexOf("<blockquote"), "the acknowledgement stands above the text");
  assert.match(markup, /data-authenticity=""[^>]*>Only the Official Journal of the European Union published in electronic form is authentic/);
  assert.match(markup, /<blockquote lang="en">This Regulation lays down rules/);
  assert.match(markup, /<span lang="en">Subject-matter and objectives<\/span>/, "the heading is the publisher's, in its language");
  assert.ok(markup.includes(`${PERMALINK}#001`) && markup.includes(sha256(ARTICLES[0][2])), "each quote carries its permalink and text digest");
  assert.match(markup, /the original wording of 2016-04-27/);
  // The launch contract: Luxembourg applicability dates and EU wording-state dates are never merged. Once the
  // answer's own data is taken out, what the page says names no Luxembourg date (the rule date-speech.test.mjs holds
  // for every census answer).
  let said = markup.replace(/<[^>]+>/g, " ").replace(/&#x27;/g, "'").replace(/&amp;/g, "&").replace(/&quot;/g, '"');
  const data = [];
  const walk = (node) => {
    if (typeof node === "string") data.push(node);
    else if (node !== null && typeof node === "object") Object.values(node).forEach(walk);
  };
  walk(europeBundle());
  for (const value of data.filter((item) => item.length > 0).sort((a, b) => b.length - a.length)) said = said.split(value).join(" ");
  assert.doesNotMatch(said, /\b(?:appl(?:y|ies|ied|ying|icability)|versions?|states?)\b|s[’']appliqu|applicab/i, "an EU wording date is never said as an applicability date");
});

test("an annex the wording does not serve as text is said beside it, with the platform's reason and the official source", () => {
  const row = annexRow({ annexes: 2, annex_identities_sha256: ["a".repeat(64), "b".repeat(64)] });
  const bundle = europeBundle((b) => { b.wordings[0].annexes_not_served = [row]; });
  const [wording] = readEuropeEvidenceBundle(bundle).wordings;
  assert.deepEqual(wording.annexesNotServed, [{
    disposition: "annex_text_not_available",
    count: 2,
    identities: ["a".repeat(64), "b".repeat(64)],
    officialIdentity: row.official_identity,
    officialSource: row.official_source,
    reason: row.reason,
  }]);
  assert.deepEqual(readEuropeEvidenceBundle(europeBundle((b) => { b.wordings[0].annexes_not_served = []; })).wordings[0].annexesNotServed, [], "a wording with no annex lists none");

  const markup = renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: envelopeOf(bundle) }) }));
  assert.match(markup, /<p data-annexes-not-served="2" data-annex-disposition="annex_text_not_available">/);
  const said = markup.replace(/<[^>]+>/g, "").replace(/&#x27;/g, "'");
  assert.ok(said.includes(`2 annexes of the English wording are not served as text, and are never searched, quoted or exported: ${row.reason}. Official source ${row.official_source}.`), said);
  assert.ok(markup.includes(`<code>${row.official_source}</code>`), "the official source is printed, not linked");
  assert.ok(markup.indexOf("data-annexes-not-served") > markup.lastIndexOf("<blockquote"), "the annexes are said after the quoted text, never among it");
});

test("an EU refusal on the reading screen is said in EU words: no sentence or hint on its card speaks of applicability (review of #903)", () => {
  const refused = (code, payload) => readingOutcome({ state: "refusal", envelope: { refusal: { code, helpful_payload: payload }, context: { publisher: "eu-eurlex" } } });
  const later = refused("no_version_for_date", {
    requested_date: "2021-04-27", history_begins: "2016-04-27", nearest_earlier: "2016-04-27", nearest_later: null,
    what_would_answer: ["new_official_observation"], asserts_absence_of_law: false,
  });
  assert.equal(later.sentence, LIVE_READING_EUROPE_REFUSAL_SENTENCES.no_version_for_date);
  const before = refused("no_version_for_date", {
    requested_date: "2016-04-26", history_begins: "2016-04-27", nearest_earlier: null, nearest_later: "2016-04-27",
    what_would_answer: ["new_official_observation"], asserts_absence_of_law: false,
  });
  for (const outcome of [later, before]) {
    const shown = renderToStaticMarkup(h(ReadingAnswerView, { outcome, chrome: liveChrome() }));
    assert.doesNotMatch(shown, /appl(y|ies|icable|icability)/i, "no EU refusal speaks of applicability");
    assert.doesNotMatch(shown, /(?:states?|versions?)/i, "nor of Luxembourg's states or versions");
  }
  for (const code of Object.keys(LIVE_READING_EUROPE_REFUSAL_SENTENCES)) {
    assert.doesNotMatch(LIVE_READING_EUROPE_REFUSAL_SENTENCES[code], /appl(y|ies|icable|icability)/i, code);
  }
  // A Luxembourg refusal keeps its own sentence.
  const luxembourg = readingOutcome({ state: "refusal", envelope: { refusal: { code: "no_version_for_date", helpful_payload: later.payload }, context: { publisher: "lu-legilux" } } });
  assert.equal(luxembourg.sentence, LIVE_READING_REFUSAL_SENTENCES.no_version_for_date);
});

test("a time-view bundle reads: a consolidated version answers the dates from its own, and the page says how long it holds (#909)", () => {
  const consolidatedSha = "7".repeat(64);
  const consolidated = `/eu-eurlex/32016R0679/eng/2024-01-01--${consolidatedSha}`;
  const timeView = (change = () => {}) => europeBundle((copy) => {
    copy.requested_date = "2025-03-01";
    copy.consolidations_held = true;
    const wording = copy.wordings[0];
    Object.assign(wording, { kind: "consolidated_version", wording_date: "2024-01-01", next_date: null, wording_sha256: consolidatedSha,
      permalink: consolidated, stable_coordinate: "/eu-eurlex/32016R0679/eng/2024-01-01", basis: "single_work" });
    for (const article of wording.articles) article.article_permalink = `${consolidated}#${escapeProvision(article.publisher_id)}`;
    change(copy);
  });
  const view = readEuropeEvidenceBundle(timeView());
  assert.equal(view.consolidationsHeld, true);
  assert.equal(view.wordings[0].kind, "consolidated_version");
  assert.equal(view.wordings[0].nextDate, null);
  const markup = renderToStaticMarkup(h(ReadingAnswerView, { outcome: { state: "success", view } }));
  assert.ok(markup.includes("the consolidated wording of 2024-01-01"), "the heading names the kind and its date");
  assert.ok(markup.includes(liveChrome().reading.europeLatest), "the latest wording says it answers every later date");
  assert.doesNotMatch(markup, /no later wording is held/, "the original-only heading is not used");

  const bounded = readEuropeEvidenceBundle(timeView((copy) => { copy.wordings[0].next_date = "2026-01-01"; }));
  const boundedMarkup = renderToStaticMarkup(h(ReadingAnswerView, { outcome: { state: "success", view: bounded } }));
  assert.ok(boundedMarkup.includes("to the day before 2026-01-01"), "a wording with a next date says where it ends");

  assert.throws(() => readEuropeEvidenceBundle(timeView((copy) => { copy.requested_date = "2023-12-31"; })), /never answers a date before its own/);
  assert.throws(() => readEuropeEvidenceBundle(timeView((copy) => { copy.wordings[0].next_date = "2025-03-01"; })), /the next wording answers that date/);
  assert.throws(() => readEuropeEvidenceBundle(timeView((copy) => { copy.wordings[0].kind = "applicable_version"; })), /original wording or a consolidated version/);
});

test("the time view's disclosure is shown beside the wording: the other works of its date and the undated versions (review of #909)", () => {
  const view = readEuropeEvidenceBundle(europeBundle((copy) => {
    copy.requested_date = "2025-03-01";
    Object.assign(copy.wordings[0], { kind: "original_wording", next_date: null,
      same_date_works: [{ publisher_work_id: `${WORK}-b`, text_held: true }, { publisher_work_id: `${WORK}-c`, text_held: false }],
      unplaced_versions: [{ publisher_work_id: `${WORK}-d`, text_held: false }] });
  }));
  assert.deepEqual(view.wordings[0].sameDateWorks.map((row) => row.textHeld), [true, false]);
  const markup = renderToStaticMarkup(h(ReadingAnswerView, { outcome: { state: "success", view } }));
  assert.ok(markup.includes("2 other publisher works carry this date: 1 with this same text held here, 1 with no text held here."), markup);
  assert.ok(markup.includes("1 wording of this act has no usable publisher date and is not placed in time"));
  // A bundle with no disclosure (an index with no states) shows none.
  const plain = renderToStaticMarkup(h(ReadingAnswerView, { outcome: { state: "success", view: readEuropeEvidenceBundle(europeBundle()) } }));
  assert.ok(!plain.includes("data-disclosure"));
});

test("an EU ambiguity says its EU sentence and shows no Luxembourg card; an EU text_not_available shows its card (review of #909)", () => {
  const eu = { publisher: "eu-eurlex" };
  const ambiguous = readingOutcome({ state: "refusal", envelope: { refusal: { code: "ambiguous_version", helpful_payload: {
    requested_date: "2025-03-01", candidates: [`/eu-eurlex/32016R0679/eng/2024-01-01--${"1".repeat(64)}`, `/eu-eurlex/32016R0679/eng/2024-01-01--${"2".repeat(64)}`] } }, context: eu } });
  assert.equal(ambiguous.card, false);
  assert.equal(ambiguous.sentence, LIVE_READING_EUROPE_REFUSAL_SENTENCES.ambiguous_version, "the EU sentence, not a card that cannot be shown");
  const unavailable = readingOutcome({ state: "refusal", envelope: { refusal: { code: "text_not_available", helpful_payload: {
    official_identity: WORK, official_source: WORK, retained_transport_evidence: "none", language: "eng",
    what_would_answer: ["new_official_observation"], asserts_absence_of_law: false } }, context: eu } });
  assert.equal(unavailable.card, true, "the payload the API sends is one the card admits");
  assert.equal(unavailable.sentence, LIVE_READING_EUROPE_REFUSAL_SENTENCES.text_not_available);
});

test("the export composer reads an EU wording to pin from: a pin per article, quoted or held without text, below the acknowledgement", () => {
  const outcome = readingOutcome({ state: "success", envelope: envelopeOf(europeBundle()) });
  const [wording] = outcome.view.wordings;
  const markup = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins: new Set(), onPin: () => {} }));
  assert.equal([...markup.matchAll(/data-pin=""/g)].length, wording.articles.length + wording.articlesWithoutText.length, "every article can be pinned");
  assert.doesNotMatch(markup, /\sname=/, "a pin carries no name");
  assert.ok(markup.indexOf(EUROPE_TEXT_ACKNOWLEDGEMENT) < markup.indexOf("<blockquote"), "the acknowledgement stands above the text");
  assert.match(markup, /data-authenticity=""[^>]*>Only the Official Journal of the European Union published in electronic form is authentic/);
  assert.match(markup, /the original wording of 2016-04-27/, "the wording is headed as the reading screen heads it");
  assert.match(markup, /<span lang="en">Subject-matter and objectives<\/span><blockquote lang="en">This Regulation lays down rules/);
  assert.ok(markup.includes(`<code>${PERMALINK}#001</code>`) && markup.includes(sha256(ARTICLES[0][2])), "each quote carries its evidence");
  const [annexLine] = markup.match(/<p data-annexes-not-served="2" data-annex-disposition="annex_text_not_available">[\s\S]*?<\/p>/) ?? [];
  assert.ok(annexLine?.includes("are not served as text"), "the annexes are said beside the wording");
  assert.ok(!annexLine.includes("<input"), "and no pin reaches an annex");

  assert.deepEqual(exportState(outcome, new Set()), { state: "empty", sentence: NOTHING_PINNED });
  const panel = exportState(outcome, new Set([pinKey(wording.wordingSha256, "002")]));
  assert.equal(panel.state, "composed", panel.sentence);
  assert.equal(panel.model.schema, EUROPE_EXPORT_SCHEMA, "an EU wording is composed into the EU export");
  assert.deepEqual(panel.model.items.map((item) => item.citation), [`${PERMALINK}#002`]);
  assert.equal(liveChrome().export.europeNotComposed, undefined, "the composer no longer says EU text is not composed");
});
