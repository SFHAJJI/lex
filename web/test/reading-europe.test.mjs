// The EU reading: the original wording an EU index holds, read from the EU `evidence_bundle` and shown on the reading
// screen with the acknowledgement and authenticity statement Decision 95 requires (the owner's proxy, 2026-10-01
// 14:30 UTC: serve EU text now).
//
// The answer here is built by hand in the shape `V3CorpusMount.EvidenceBundleEurope` sends (the API's own tests hold
// that shape to the real handler on the GDPR fixture). It stands in until the answer census
// (`schemas/v3-platform/answer-samples.json`) captures an EU bundle; that capture replaces it.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { ExportAnswerView, ReadingAnswerView } from "../.react-build/app.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { exportState } from "../scripts/live-export.mjs";
import { LIVE_READING_EUROPE_REFUSAL_SENTENCES, LIVE_READING_REFUSAL_SENTENCES, readingOutcome } from "../scripts/live-reading.mjs";
import { EUROPE_TEXT_ACKNOWLEDGEMENT, readEuropeEvidenceBundle, readEvidenceBundleAnswer } from "../scripts/reading-answer.mjs";
import { escapeProvision } from "../scripts/search-answer.mjs";

const sha256 = (text) => createHash("sha256").update(text, "utf8").digest("hex");
const WORK = "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
const WORDING_SHA = "5".repeat(64);
const BODY = "b".repeat(64);
const PERMALINK = `/eu-eurlex/32016R0679/eng/2016-04-27--${WORDING_SHA}`;
const ARTICLES = [
  ["001", "Subject-matter and objectives", "This Regulation lays down rules relating to the protection of natural persons with regard to the processing of personal data."],
  ["002", "Material scope", "This Regulation applies to the processing of personal data wholly or partly by automated means."],
];

/** An EU bundle as the platform sends it, with `change` applied to a fresh copy. */
function europeBundle(change = () => {}) {
  const bundle = {
    scope: "the evidence a reader needs to quote the original wording of an EU work the mounted EU index holds",
    requested_identifier: "32016R0679",
    requested_date: "2016-04-27",
    requested_language: "eng",
    publisher: "eu-eurlex",
    publisher_work_id: WORK,
    celex: "32016R0679",
    available_languages: ["eng"],
    served_languages: ["eng"],
    wordings: [{
      publisher_expression_id: `${WORK}.0006`,
      language: "eng",
      wording_date: "2016-04-27",
      wording_sha256: WORDING_SHA,
      stable_coordinate: "/eu-eurlex/32016R0679/eng/2016-04-27",
      permalink: PERMALINK,
      sources: [{ object_ref_sha256: "c".repeat(64), outcome: "acquired", body_sha256: BODY, body_byte_length: 1024, body_receipt_sha256: "d".repeat(64) }],
      articles: ARTICLES.map(([id, heading, text]) => ({
        article_identity_sha256: sha256(`identity ${id}`),
        publisher_id: id,
        heading,
        language: "eng",
        text,
        text_sha256: sha256(text),
        text_byte_length: Buffer.byteLength(text, "utf8"),
        body_sha256: BODY,
        package_entry: "L_2016119EN.01000101.xml",
        package_sha256: "e".repeat(64),
        source_entry_sha256: "f".repeat(64),
        official_source: "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.02/DOC_1",
        article_permalink: `${PERMALINK}#${escapeProvision(id)}`,
        provision_coordinate: `${WORK}.0006#lex-provision=${escapeProvision(id)}`,
      })),
      articles_without_text: [],
    }],
    acknowledgement: "© European Union, https://eur-lex.europa.eu",
    authenticity: "Only the Official Journal of the European Union published in electronic form is authentic and produces legal effects (Regulation (EU) No 216/2013, Article 1(2)); this text is a reproduction read from the Publications Office's Formex package, not the authentic edition.",
    rights_rule: "rights are enforced when the bundle is composed, before any text is read",
    date_rule: "the EU index holds one wording of each expression, the original act's, dated by its Formex act date",
    date_semantics: "the wording date is the date the publisher's Formex package gives the act",
    digest_rule: "the SHA-256, under the domain lex-v3-eu-wording/1, of the work's CELEX",
    consolidations_held: false,
    not_held: [
      { item: "later_wordings", reason: "no consolidated version is held, so only the original wording is served, and only for its own wording date" },
      { item: "force_dates", reason: "no entry-into-force, application or end-of-validity date is held; the wording date is none of them" },
    ],
    corpus_sha256: "1".repeat(64),
    index_sha256: "2".repeat(64),
  };
  const copy = structuredClone(bundle);
  change(copy);
  return copy;
}

const envelopeOf = (value) => ({ result: { operation_id: "evidence_bundle", object_type: "evidence_bundle", value }, context: { publisher: "eu-eurlex" } });

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
    ["a consolidation held", (b) => { b.consolidations_held = true; }, /original wording/],
    ["a wording of another date", (b) => { b.wordings[0].wording_date = "2016-05-04"; }, /answers only its own date/],
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

test("the export composer reads an EU wording and says it is not composed, offering no file", () => {
  const outcome = readingOutcome({ state: "success", envelope: envelopeOf(europeBundle()) });
  const markup = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins: new Set(), onPin: () => {} }));
  assert.ok(markup.includes(liveChrome().export.europeNotComposed.slice(0, 40)));
  assert.match(markup, /<blockquote lang="en">/);
  assert.doesNotMatch(markup, /type="checkbox"/, "nothing is offered to pin");
  assert.deepEqual(exportState(outcome, new Set()), { state: "none" });
});
