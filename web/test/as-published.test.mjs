// A Luxembourg act held only as first published says so everywhere it is shown (the plan's item 0.6; the specification
// of record's R76, R18 and R20): the reading, the dossier (whose state table is the act's timeline), the export page and
// the three export files, read from `as_published` as the platform sends it.
//
// The census answers are the fixture act, held as its Journal officiel expression with no fact about later versions or
// its publication date: the case almost the whole population is in, with the least said. The facts are then added to a
// copy of the answer one kind at a time, and an answer that says what its facts do not is refused as drift.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { DossierAnswerView, ExportAnswerView, ExportPreview, ReadingAnswerView } from "../.react-build/app.mjs";
import {
  AS_PUBLISHED_LABEL,
  AUTHENTICITY_SENTENCES,
  LATER_VERSIONS_NOT_HELD,
  laterVersionsSentence,
  readAsPublished,
} from "../scripts/as-published.mjs";
import { readDossier } from "../scripts/dossier-answer.mjs";
import { readEvidenceBundle } from "../scripts/reading-answer.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { CSV_COLUMNS, asPublishedParagraph, composeExport, exportCsv, exportJson } from "../scripts/export-build.mjs";
import { exportPdf, pdfRefusal } from "../scripts/export-pdf.mjs";
import { LIVE_CHROME, entriesOf, fillText } from "../scripts/live-chrome.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const envelopeOf = (operation) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === operation && candidate.envelope.result?.value?.publisher === "lu-legilux");
  assert.ok(entry, `the census holds a Luxembourg ${operation} answer`);
  return entry.envelope;
};

const WORK = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3";
const ACT = `${WORK}/jo`;
const OBSERVED = "2026-09-17T00:00:00Z";
const LABEL_SAID = "As published; may have been amended since; no consolidated text.";
const UNDATED = AUTHENTICITY_SENTENCES.publication_date_not_stated.replace("{act}", ACT);

// What a page says, its tags taken out (an inline element's text joins its sentence's).
const text = (markup) => markup.replace(/<[^>]+>/g, "").replace(/&#x27;|&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, "&");
/** The as-published section of a page: what this file holds to R21's words (the statute text quoted below it is the publisher's). */
const section = (markup) => {
  const start = markup.indexOf("<section data-as-published");
  assert.ok(start >= 0, "the page has the as-published section");
  return markup.slice(start, markup.indexOf("</section>", start));
};
const fact = (subject, value) => ({ subject_iri: subject, object_kind: "literal", value, datatype_iri: "http://www.w3.org/2001/XMLSchema#date", language_tag: null, evidence_sha256: "e".repeat(64) });

/** A copy of a census answer with the facts as_published lists replaced, its sentences and not-held row said again. */
function withFacts(answer, { consolidations = [], endOfForce = [], publicationDates = [] }) {
  const changed = structuredClone(answer);
  const facts = changed.as_published.later_version_facts;
  facts.consolidations = consolidations.map(([iri, from, to]) => ({ legal_resource_iri: iri, applicability_date: from, end_applicability_date: to }));
  facts.end_of_force = endOfForce.map((date) => fact(ACT, date));
  facts.sentence = laterVersionsSentence(
    consolidations.map(([, from, to]) => ({ applicabilityDate: from, endApplicabilityDate: to })), endOfForce);
  const row = changed.not_held.find((entry) => entry.item === "later_versions");
  if (row) row.reason = facts.sentence;
  const authenticity = changed.as_published.authenticity;
  authenticity.publication_dates = publicationDates.map((date) => fact(ACT, date));
  authenticity.basis = publicationDates.length !== 1 ? "publication_date_not_stated" : publicationDates[0] >= "2017-01-01" ? "electronic_journal_officiel" : "printed_memorial";
  authenticity.sentence = AUTHENTICITY_SENTENCES[authenticity.basis].replace("{act}", ACT);
  return changed;
}

function outcomeWith(operation, answer) {
  const envelope = structuredClone(envelopeOf(operation));
  envelope.result.value = answer;
  return (operation === "dossier" ? dossierOutcome : readingOutcome)({ state: "success", envelope });
}

test("the label and the authenticity sentences the pages say in English are the platform's own, word for word", () => {
  const copy = LIVE_CHROME.en.common.asPublished;
  assert.equal(copy.label, AS_PUBLISHED_LABEL);
  assert.equal(AS_PUBLISHED_LABEL, "as published; may have been amended since; no consolidated text");
  assert.deepEqual(Object.keys(copy.authenticity), Object.keys(AUTHENTICITY_SENTENCES));
  for (const [basis, template] of Object.entries(copy.authenticity)) {
    assert.equal(fillText(template, { act: ACT }), AUTHENTICITY_SENTENCES[basis].replace("{act}", ACT), basis);
  }
  // R21 in both languages: none of these says "in force", "en vigueur" or "applied" of a date; the end-of-force date is
  // the publisher's typed date, said as its own value.
  for (const language of ["en", "fr"]) {
    for (const [path, said] of entriesOf(LIVE_CHROME[language].common.asPublished)) assertTimeVocabulary(said, `${language} common.asPublished.${path}`);
  }
});

test("the later-versions sentence is the platform's rule, and says only what its facts say", () => {
  // The same four cases V3CorpusAsPublishedMountTests holds the platform to, in the same words.
  assert.equal(laterVersionsSentence([], []), LATER_VERSIONS_NOT_HELD);
  assert.equal(laterVersionsSentence([{ applicabilityDate: "2022-04-09", endApplicabilityDate: null }], []),
    "later versions of this act are not held: the publisher's record names 1 consolidated version of it, applicable from 2022-04-09 (publisher); this index holds none of its text");
  assert.equal(laterVersionsSentence([{ applicabilityDate: null, endApplicabilityDate: null }, { applicabilityDate: null, endApplicabilityDate: "2024-01-01" }], []),
    "later versions of this act are not held: the publisher's record names 2 consolidated versions of it, with no single applicability date stated; this index holds none of their text");
  assert.equal(laterVersionsSentence([], ["2020-01-01", "2021-01-01"]),
    "later versions of this act are not held; the publisher's record gives 2020-01-01 and 2021-01-01 as the act's end-of-force date (dateNoLongerInForce)");
  assert.ok(!/exists|no later version/.test(LATER_VERSIONS_NOT_HELD), "it never says that none exists");
});

test("the census act is held only as published, and the dossier and the reading read it so", () => {
  for (const [operation, read] of [["dossier", readDossier], ["evidence_bundle", readEvidenceBundle]]) {
    const view = read(envelopeOf(operation).result.value);
    assert.ok(view.asPublished !== null, operation);
    assert.equal(view.asPublished.label, AS_PUBLISHED_LABEL);
    assert.equal(view.asPublished.journalOfficielIri, ACT);
    assert.equal(view.asPublished.laterVersions.sentence, LATER_VERSIONS_NOT_HELD);
    assert.deepEqual(view.asPublished.laterVersions.consolidations, []);
    assert.equal(view.asPublished.authenticity.basis, "publication_date_not_stated");
    assert.equal(view.asPublished.authenticity.sentence, UNDATED);
    assert.deepEqual(view.asPublished.authenticity.servedManifestations.map((file) => file.manifestationIri), [`${ACT}/fr/xml`]);
    assert.deepEqual(view.notHeld.at(-1), { item: "later_versions", reason: LATER_VERSIONS_NOT_HELD });
  }
});

test("an answer that says what its facts do not is drift, and is refused", () => {
  const answer = envelopeOf("dossier").result.value;
  const mutate = (change) => { const copy = structuredClone(answer); change(copy); return copy; };
  const refusals = [
    [(a) => { a.as_published.label = "as published"; }, /not the label/],
    [(a) => { a.as_published.journal_officiel_iri = `${WORK}/consolide/20200101`; }, /not a Journal officiel legal resource/],
    [(a) => { a.as_published.later_version_facts.sentence = "no later version exists"; }, /not what its facts say/],
    [(a) => { a.as_published.later_version_facts.consolidations = [{ legal_resource_iri: `${WORK}0/consolide/20200101`, applicability_date: "2020-01-01", end_applicability_date: null }]; }, /not a consolidated version of/],
    [(a) => { a.as_published.authenticity.basis = "electronic_journal_officiel"; a.as_published.authenticity.sentence = AUTHENTICITY_SENTENCES.electronic_journal_officiel.replace("{act}", ACT); }, /make it publication_date_not_stated/],
    [(a) => { a.as_published.authenticity.sentence = UNDATED.replace("is not authentic", "is authentic"); }, /not the publication_date_not_stated sentence/],
    [(a) => { a.not_held = a.not_held.filter((row) => row.item !== "later_versions"); }, /later-versions row exactly once/],
    [(a) => { a.as_published = null; }, /lists later versions as not held/],
    [(a) => { a.states[0].publisher_legal_resource_iri = `${WORK}/consolide/20200101`; }, /not an expression of the act as published/],
  ];
  for (const [change, reason] of refusals) assert.throws(() => readDossier(mutate(change)), reason);
  // A work whose consolidated text is held says nothing of the kind, and lists no later-versions row.
  const consolidated = mutate((a) => { a.as_published = null; a.not_held = a.not_held.filter((row) => row.item !== "later_versions"); });
  assert.equal(readDossier(consolidated).asPublished, null);
  assert.equal(readAsPublished({ as_published: null }, { states: [] }), null);
});

test("the dossier and the reading say the label, that later versions are not held, and the authenticity sentence", () => {
  for (const [operation, View] of [["dossier", DossierAnswerView], ["evidence_bundle", ReadingAnswerView]]) {
    const outcome = outcomeWith(operation, envelopeOf(operation).result.value);
    assert.equal(outcome.state, "success", outcome.sentence);
    const markup = renderToStaticMarkup(h(View, { outcome }));
    const said = text(markup);
    assert.match(markup, /<section data-as-published="publication_date_not_stated">/, operation);
    assert.ok(said.includes(LABEL_SAID), `${operation} says the label`);
    assert.ok(said.includes("Later versions: not held."), `${operation} says later versions are not held`);
    assert.ok(said.includes(UNDATED), `${operation} says the authenticity sentence`);
    assert.ok(markup.includes(`<code>${ACT}</code>`), "the act as published is printed, not linked");
    assert.doesNotMatch(markup, /data-consolidations|data-end-of-force/, "no fact is held, so none is said");
    assertTimeVocabulary(text(section(markup)), `${operation}'s as-published section`);
  }

  // With the facts: the consolidated versions the publisher names, its end-of-force date and a publication date.
  const answer = withFacts(envelopeOf("dossier").result.value, {
    consolidations: [[`${WORK}/consolide/20220101`, "2022-01-01", "2024-06-01"], [`${WORK}/consolide/20240601`, "2024-06-01", null], [`${WORK}/consolide/x`, null, null]],
    endOfForce: ["2026-01-04"],
    publicationDates: ["2017-03-23"],
  });
  const markup = renderToStaticMarkup(h(DossierAnswerView, { outcome: outcomeWith("dossier", answer) }));
  const said = text(markup);
  assert.match(markup, /data-later-versions="3"/);
  assert.ok(said.includes("Later versions: not held. The publisher's record names 3 consolidated versions of this act, whose text this site does not hold:"));
  assert.ok(said.includes(`${WORK}/consolide/20220101, applicable from 2022-01-01 to 2024-06-01 (publisher)`));
  assert.ok(said.includes(`${WORK}/consolide/20240601, applicable from 2024-06-01 (publisher)`));
  assert.ok(said.includes(`${WORK}/consolide/x, with no single applicability date stated by the publisher`));
  assert.ok(said.includes("The publisher's record gives 2026-01-04 as this act's end-of-force date."));
  assert.ok(said.includes(AUTHENTICITY_SENTENCES.electronic_journal_officiel.replace("{act}", ACT)));
  assert.match(markup, /data-as-published="electronic_journal_officiel"/);
  assertTimeVocabulary(text(section(markup)), "the dossier's as-published section with every fact");

  const printed = withFacts(envelopeOf("evidence_bundle").result.value, { publicationDates: ["2016-12-31"] });
  assert.ok(text(renderToStaticMarkup(h(ReadingAnswerView, { outcome: outcomeWith("evidence_bundle", printed) })))
    .includes(AUTHENTICITY_SENTENCES.printed_memorial.replace("{act}", ACT)));
});

test("the export page and the three export files say it too", () => {
  const outcome = outcomeWith("evidence_bundle", envelopeOf("evidence_bundle").result.value);
  const pins = new Set();
  assert.ok(text(renderToStaticMarkup(h(ExportAnswerView, { outcome, pins, onPin: () => {} }))).includes(LABEL_SAID), "the page an export is pinned from");

  const view = readEvidenceBundle(envelopeOf("evidence_bundle").result.value);
  const [state] = view.states;
  const model = composeExport({ view, pinned: [{ stateSha256: state.stateSha256, publisherId: state.articles[0].publisherId }], observedAt: OBSERVED });
  const paragraph = `${LABEL_SAID} Later versions of this act are not held. ${UNDATED}`;
  assert.equal(asPublishedParagraph(model.asPublished), paragraph);
  assert.ok(text(renderToStaticMarkup(h(ExportPreview, { model, onSave: () => {} }))).includes(paragraph), "the export's preview");

  assert.deepEqual(JSON.parse(exportJson(model)).as_published, {
    label: AS_PUBLISHED_LABEL,
    later_versions: LATER_VERSIONS_NOT_HELD,
    authenticity: UNDATED,
    journal_officiel_iri: ACT,
  });
  const [header, row] = exportCsv(model).split("\r\n");
  assert.deepEqual(header.split(",").slice(CSV_COLUMNS.indexOf("as_published"), CSV_COLUMNS.indexOf("authenticity") + 1), ["as_published", "later_versions", "authenticity"]);
  // The label and the later-versions sentence hold no comma, so they stand bare; the authenticity sentence is quoted.
  for (const said of [`,${AS_PUBLISHED_LABEL},`, `,${LATER_VERSIONS_NOT_HELD},`, `,"${UNDATED}",`]) assert.ok(row.includes(said), `each CSV row carries ${said}`);
  assert.equal(pdfRefusal(model), null, "the PDF can set it: its characters are in the standard fonts");
  const pdf = new TextDecoder("latin1").decode(exportPdf(model));
  assert.ok(pdf.includes("(As published; may have been amended since; no consolidated text."), "the PDF sets the paragraph");

  // An act whose consolidated text is held says none of it in its files.
  const consolidated = structuredClone(envelopeOf("evidence_bundle").result.value);
  consolidated.as_published = null;
  consolidated.not_held = consolidated.not_held.filter((entry) => entry.item !== "later_versions");
  const plain = composeExport({ view: readEvidenceBundle(consolidated), pinned: [{ stateSha256: state.stateSha256, publisherId: state.articles[0].publisherId }], observedAt: OBSERVED });
  assert.equal(JSON.parse(exportJson(plain)).as_published, null);
  assert.ok(!exportCsv(plain).includes(AS_PUBLISHED_LABEL));
});

test("the as-published block and the export's paragraph wrap their long addresses at a phone's width (#964's CI)", async () => {
  const css = await readFile(new URL("../src/styles.css", import.meta.url), "utf8");
  const wrapping = [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)]
    .filter(([, , body]) => /overflow-wrap:\s*anywhere/.test(body))
    .flatMap(([, selectors]) => selectors.split(",").map((selector) => selector.replace(/\/\*[\s\S]*?\*\//g, "").trim()));
  for (const selector of ["[data-as-published] p", "[data-as-published] li", "[data-export-as-published]"]) {
    assert.ok(wrapping.includes(selector), `${selector} wraps a long token`);
  }
});
