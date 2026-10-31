// An EU act's titles and Official Journal references (Release 3's item 1), read and said: the publisher's title and short
// title and the act's Formex file's, each with its source and in its own language; the reference the file states, in EUR-Lex's
// form, in English and in French, for an issue of the Official Journal before October 2023 and for an act published act by
// act since; and the EU acts a search that names no work found by their titles. An answer over an EU index /6 carries none,
// and the pages say none.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { DossierAnswerView, ReadingAnswerView, SearchAnswerView } from "../.react-build/app.mjs";
import {
  europeReferenceOf,
  europeTitleOf,
  journalDate,
  officialJournalWords,
  readEuropeOfficialJournal,
  readEuropeTitleResolution,
  readEuropeTitles,
} from "../scripts/europe-publication.mjs";
import { europeBundle, europeEnvelope, EUROPE_SAMPLE } from "../scripts/europe-bundle-sample.mjs";
import { citationForms, citationWords } from "../scripts/citation.mjs";
import { LIVE_CHROME, entriesOf } from "../scripts/live-chrome.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { searchOutcome } from "../scripts/live-search.mjs";
import { readEuropeEvidenceBundle } from "../scripts/reading-answer.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const NB = " ";
const EXPRESSION = `${EUROPE_SAMPLE.work}.0006`;
const TITLE = "Regulation (EU) 2016/679 of the European Parliament and of the Council of 27 April 2016 on the protection of natural persons with regard to the processing of personal data and on the free movement of such data, and repealing Directive 95/46/EC (General Data Protection Regulation) (Text with EEA relevance)";

/** The titles and reference members of one expression, as an answer over an EU index /7 carries them, with `change` applied. */
function publication(expression = EXPRESSION, change = () => {}) {
  const value = {
    titles: {
      rule: "titles are the publisher's own, each with its source",
      entries: [
        { publisher_expression_id: expression, publisher_work_id: EUROPE_SAMPLE.work, language: "eng", kind: "title", source: "cellar_expression_title", text: TITLE, title_identity_sha256: "1".repeat(64) },
        { publisher_expression_id: expression, publisher_work_id: EUROPE_SAMPLE.work, language: "eng", kind: "short_title", source: "cellar_expression_title_short", text: "gdpr, personal data, personal data protection", title_identity_sha256: "2".repeat(64) },
        { publisher_expression_id: expression, publisher_work_id: EUROPE_SAMPLE.work, language: "eng", kind: "title", source: "formex_title", text: TITLE, title_identity_sha256: "3".repeat(64) },
      ],
      expressions_without_title: [],
    },
    official_journal: {
      rule: "each reference is read from the bibliographic markup of the expression's admitted Formex package and nothing else",
      references: [{
        publisher_expression_id: expression, publisher_work_id: EUROPE_SAMPLE.work, language: "eng", kind: "act_publication", number_form: "series_number",
        series: "L", oj_number: "119", act_number: null, year: "2016", first_page: "1", publication_date: "2016-05-04",
        package_sha256: "4".repeat(64), package_entry: "L_2016119EN.01000101.xml", reference_identity_sha256: "5".repeat(64),
      }],
      expressions_without_reference: [],
    },
  };
  change(value);
  return value;
}

test("an answer over an EU index /6 carries no title and no reference, and is read as none", () => {
  assert.equal(readEuropeTitles({ celex: "32016R0679" }, "an answer"), null);
  assert.equal(readEuropeOfficialJournal({ celex: "32016R0679" }, "an answer"), null);
  assert.equal(readEuropeTitleResolution({ publisher: "lu-legilux" }, "an answer"), null);
  assert.equal(europeTitleOf(null, EXPRESSION), null);
  assert.equal(europeReferenceOf(null, EXPRESSION), null);
});

test("the publisher's title is shown before the package's, and the reference is read field by field", () => {
  const titles = readEuropeTitles(publication(), "an answer");
  assert.equal(europeTitleOf(titles, EXPRESSION).source, "cellar_expression_title");
  const formexOnly = readEuropeTitles(publication(EXPRESSION, (value) => { value.titles.entries.splice(0, 2); }), "an answer");
  assert.equal(europeTitleOf(formexOnly, EXPRESSION).source, "formex_title", "the package's title stands in for none of the publisher's");
  const reference = europeReferenceOf(readEuropeOfficialJournal(publication(), "an answer"), EXPRESSION);
  assert.deepEqual([reference.series, reference.ojNumber, reference.year, reference.firstPage, reference.publicationDate], ["L", "119", "2016", "1", "2016-05-04"]);
});

test("a reference is said in EUR-Lex's form, in English and in French, for both number forms, with or without its date", () => {
  const reference = europeReferenceOf(readEuropeOfficialJournal(publication(), "an answer"), EXPRESSION);
  assert.equal(journalDate("2016-05-04"), "4.5.2016");
  assert.equal(officialJournalWords(reference, citationWords("eng")), "OJ L 119, 4.5.2016, p. 1");
  assert.equal(officialJournalWords(reference, citationWords("fra")), "JO L 119 du 4.5.2016, p. 1");
  assert.equal(officialJournalWords(reference, citationWords("eng"), (date) => date), "OJ L 119, 2016-05-04, p. 1", "a live screen states the date as ISO");
  assert.equal(officialJournalWords({ ...reference, ojNumber: "011", publicationDate: "2019-01-14", firstPage: "3" }, citationWords("eng")), "OJ L 11, 14.1.2019, p. 3",
    "an issue number is printed without its leading zeros");
  const act = { ...reference, numberForm: "act_number", ojNumber: null, actNumber: "2026/1965", year: "2026", publicationDate: "2026-08-27" };
  assert.equal(officialJournalWords(act, citationWords("eng")), "OJ L, 2026/1965, 27.8.2026");
  assert.equal(officialJournalWords(act, citationWords("fra")), "JO L, 2026/1965, 27.8.2026");
  assert.equal(officialJournalWords({ ...reference, publicationDate: null }, citationWords("eng")), "OJ L 119 (2016), p. 1", "a reference whose date is not stated is said with its year");
  assert.equal(officialJournalWords({ ...act, publicationDate: null }, citationWords("eng")), "OJ L, 2026/1965");
  assert.equal(officialJournalWords({ ...reference, firstPage: null }, citationWords("fra")), "JO L 119 du 4.5.2016");
});

test("a title or reference that breaks the answer's own rules is thrown, never said", () => {
  const drift = {
    "a title of an unknown source": [(value) => { value.titles.entries[0].source = "lex"; }, /which no title is/],
    "a short title said a title": [(value) => { value.titles.entries[1].kind = "title"; }, /which gives a short_title/],
    "a title said twice": [(value) => { value.titles.entries[1].title_identity_sha256 = "1".repeat(64); }, /repeats a title/],
    "a title in German": [(value) => { value.titles.entries[0].language = "deu"; }, /not English or French/],
    "an expression said untitled that has one": [(value) => { value.titles.expressions_without_title = [EXPRESSION]; }, /holds no title, and lists one/],
  };
  for (const [name, [change, rule]] of Object.entries(drift)) {
    assert.throws(() => readEuropeTitles(publication(EXPRESSION, change), "an answer"), rule, name);
  }
  const referenceDrift = {
    "an issue number on an act's own number": [(value) => { value.official_journal.references[0].number_form = "act_number"; }, /is numbered act_number/],
    "an unknown kind": [(value) => { value.official_journal.references[0].kind = "corrigendum"; }, /which no reference is/],
    "a date that is no date": [(value) => { value.official_journal.references[0].publication_date = "4.5.2016"; }, /publication_date/],
    "an expression said without a reference that has one": [(value) => { value.official_journal.expressions_without_reference = [EXPRESSION]; }, /holds no reference, and lists one/],
  };
  for (const [name, [change, rule]] of Object.entries(referenceDrift)) {
    assert.throws(() => readEuropeOfficialJournal(publication(EXPRESSION, change), "an answer"), rule, name);
  }
});

test("the citation's evidence form says the wording's reference and the publisher's title where the index holds them", () => {
  const bundle = europeBundle();
  Object.assign(bundle, publication(bundle.wordings[0].publisher_expression_id));
  const view = readEuropeEvidenceBundle(bundle);
  const [wording] = view.wordings;
  const forms = citationForms({ view, version: wording, article: wording.articles[0], observedAt: EUROPE_SAMPLE.observedAt });
  assert.ok(forms.evidenceLines.includes(`Title (publisher): ${TITLE}`), forms.evidence);
  assert.ok(forms.evidenceLines.includes("Published in: OJ L 119, 4.5.2016, p. 1"), forms.evidence);
  assert.ok(!forms.evidenceLines.includes(LIVE_CHROME.en.citation.evidence.officialJournalNotHeld));
  for (const line of forms.evidenceLines) assertTimeVocabulary(line, "an EU evidence line");

  // A consolidated wording names the act it consolidates as published; the bundle of an index /6 says none is held.
  bundle.official_journal.references[0].kind = "consolidated_act_publication";
  const consolidated = readEuropeEvidenceBundle(bundle);
  const again = citationForms({ view: consolidated, version: consolidated.wordings[0], article: consolidated.wordings[0].articles[0], observedAt: EUROPE_SAMPLE.observedAt });
  assert.ok(again.evidenceLines.includes("The act consolidated was published in: OJ L 119, 4.5.2016, p. 1"), again.evidence);
  const plain = readEuropeEvidenceBundle(europeBundle());
  const none = citationForms({ view: plain, version: plain.wordings[0], article: plain.wordings[0].articles[0], observedAt: EUROPE_SAMPLE.observedAt });
  assert.ok(none.evidenceLines.includes(LIVE_CHROME.en.citation.evidence.officialJournalNotHeld), "over an index /6 the line says the reference is not held");
});

test("the EU reading and dossier say each wording's title and reference, and over an index /6 say nothing of them", () => {
  const bundle = europeBundle();
  Object.assign(bundle, publication(bundle.wordings[0].publisher_expression_id));
  const outcome = readingOutcome({ state: "success", envelope: europeEnvelope(bundle) });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = renderToStaticMarkup(h(ReadingAnswerView, { outcome }));
  assert.ok(markup.includes(`Title in English, as the publisher records it: <span lang="en">${TITLE}</span>`), "the title, marked in its own language");
  assert.ok(markup.includes("Short title in English, as the publisher records it:"));
  assert.ok(markup.includes("Official Journal reference of the English wording: OJ L 119, 2016-05-04, p. 1"), "the page states the date as ISO");
  const plain = renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: europeEnvelope(europeBundle()) }) }));
  assert.ok(!plain.includes("data-europe-publication"), "an answer over an index /6 says no title and no reference");

  // A wording whose package states no reference: said, never left out.
  const unreferenced = europeBundle();
  Object.assign(unreferenced, publication(unreferenced.wordings[0].publisher_expression_id, (value) => {
    value.official_journal.references = [];
    value.official_journal.expressions_without_reference = [unreferenced.wordings[0].publisher_expression_id];
  }));
  const said = renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: europeEnvelope(unreferenced) }) }));
  assert.ok(said.includes("This site holds no Official Journal reference of the English wording."));

  const dossier = census.envelopes.find((entry) => entry.operation === "dossier" && entry.envelope.result?.value.publisher === "eu-eurlex");
  assert.ok(dossier, "the census holds an EU dossier");
  const value = structuredClone(dossier.envelope.result.value);
  if (!Object.hasOwn(value, "titles")) Object.assign(value, publication(value.expressions[0].publisher_expression_id));
  const dossierOutcomeValue = dossierOutcome({ state: "success", envelope: { ...dossier.envelope, result: { ...dossier.envelope.result, value } } });
  assert.equal(dossierOutcomeValue.state, "success", dossierOutcomeValue.sentence);
  const dossierMarkup = renderToStaticMarkup(h(DossierAnswerView, { outcome: dossierOutcomeValue }));
  assert.match(dossierMarkup, /Title in English, as the publisher records it:/);
  assert.match(dossierMarkup, /Official Journal reference of the English wording: OJ L 119, 2016-05-04, p\. 1/, "the page states the date as ISO");
});

test("a search that names no work lists the EU acts its phrase names by title, linked to their dossiers", () => {
  const sample = answers.sampled.find((entry) => entry.operation === "search" && Object.hasOwn(entry.answer, "europe_titles"));
  // The census writes the digests that vary per run as a placeholder; the reader wants a SHA-256, so one is filled in.
  let n = 0;
  const withDigests = (node) => node === "<varies-per-run>" ? (String(n += 1) + "0123456789abcdef".repeat(4)).slice(0, 64)
    : Array.isArray(node) ? node.map(withDigests)
      : node !== null && typeof node === "object" ? Object.fromEntries(Object.entries(node).map(([key, item]) => [key, withDigests(item)])) : node;
  const value = sample ? withDigests(structuredClone(sample.answer)) : null;
  const resolution = readEuropeTitleResolution({
    europe_titles: {
      retrieval_lane: "eu_title_discovery",
      outcome: "one_act",
      acts: [{ seed_celex: "32016R0679", original_work_id: EUROPE_SAMPLE.work, matched_title: TITLE, matched_title_language: "eng", title_kind: "title",
        title_source: "cellar_expression_title", match_reason: "phrase_in_title", dossier: { identifier: "32016R0679" } }],
      matching: "a title matches when the query, folded, is found in the title's words",
      rule: "with no work named, the query is also matched against the titles of the EU acts the mounted EU index holds",
    },
  }, "an answer");
  assert.equal(resolution.acts[0].seedCelex, "32016R0679");
  assert.throws(() => readEuropeTitleResolution({ europe_titles: { retrieval_lane: "eu_title_discovery", outcome: "several_acts", acts: [], matching: "m", rule: "r" } }, "an answer"),
    /lists 0 acts/);
  assert.ok(value, "the answer census holds a search that lists an EU act by title");
  const outcome = searchOutcome({ state: "success", envelope: { result: { value }, context: { freshness: { observed_at: EUROPE_SAMPLE.observedAt } } } });
  assert.equal(outcome.state, "success", outcome.sentence);
  assert.equal(outcome.view.europeTitles.acts[0].seedCelex, "32016R0679");
  const markup = renderToStaticMarkup(h(SearchAnswerView, { outcome }));
  assert.match(markup, /href="\/en\/eu-eurlex\/32016R0679"/);
  assert.match(markup, /data-europe-titles-searched=""/);
});

test("each title and reference entry says what it is without a date word, in English and in French", () => {
  for (const [language, table] of [["en", LIVE_CHROME.en], ["fr", LIVE_CHROME.fr]]) {
    const entries = entriesOf(table).filter(([path]) => path.startsWith("common.europePublication.") || path.startsWith("citation.europe.journal.")
      || path.startsWith("search.euTitle") || path.startsWith("search.runEuTitles") || path === "search.euTitlesSearched"
      || path === "citation.evidence.officialJournal" || path === "citation.evidence.officialJournalConsolidated");
    assert.equal(entries.length, 7 + 6 + 6 + 3 + 2, `${language}: the entries`);
    for (const [path, text] of entries) assertTimeVocabulary(text, `${language} ${path}`);
  }
  assert.equal(LIVE_CHROME.fr.citation.evidence.officialJournal, `Publication${NB}: {reference}`);
});
