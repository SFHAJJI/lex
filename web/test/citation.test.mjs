// The one citation generator (`scripts/citation.mjs`; the plan's item 3.3, the specification of record's R65): its three
// forms for a Luxembourg act (held only as published, consolidated and current or not, a code) and an EU act (its original
// wording, a consolidation, current or not, in English and French), in the language of the text cited; what it leaves out
// and says it does not hold; that `verify` resolves its permalink and evidence forms by their hashes (the census's verify
// answers, from the mounts that sent its bundles); and that the export keeps the evidence form in JSON, CSV and PDF.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { Citations, PinnedLead } from "../.react-build/app.mjs";
import { readingPinOf } from "../scripts/initial-answer.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { CITATION_ORIGIN, citationForms, citationWords, dateInWords, europeArticle, evidenceVerdict, luxembourgArticle, usualVerdict } from "../scripts/citation.mjs";
import { readSearchAnswer } from "../scripts/search-answer.mjs";
import { readEuropeEvidenceBundle, readEvidenceBundle, readEvidenceBundleAnswer } from "../scripts/reading-answer.mjs";
import { AUTHENTICITY_SENTENCES } from "../scripts/as-published.mjs";
import { CSV_COLUMNS, composeEuropeExport, composeExport, exportCsv, exportJson } from "../scripts/export-build.mjs";
import { exportPdf, pdfRefusal } from "../scripts/export-pdf.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { europeBundle } from "../scripts/europe-bundle-sample.mjs";

const NB = String.fromCharCode(0xa0);
const FR = liveChrome("fr");
const EN = liveChrome("en");
const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === operation && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the ${operation} envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const LU = envelopeOf("evidence_bundle", "the work on its state's date");
const EU = envelopeOf("evidence_bundle", "an EU wording's permalink whose next wording's text is not held");
const LU_VERIFIED = envelopeOf("verify", "an article's permalink");
const ANSWERS = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const EU_VERIFIED = envelopeOf("verify", "an EU provision's permalink");
const OBSERVED = LU.context.freshness.observed_at;
const WORK = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3";
const fact = (subject, value) => ({ subject_iri: subject, object_kind: "literal", value, datatype_iri: null, language_tag: null, evidence_sha256: "e".repeat(64) });

/** The census's Luxembourg bundle with `change` applied to a copy. */
function luxembourgAnswer(change = () => {}) {
  const answer = structuredClone(LU.result.value);
  change(answer);
  return answer;
}

/** The forms of one article of the first state of a Luxembourg answer. */
function luxembourgForms(answer, publisherId, observedAt = OBSERVED) {
  const view = readEvidenceBundle(answer);
  const [state] = view.states;
  const article = state.articles.find((candidate) => candidate.publisherId === publisherId) ?? { publisherId };
  return citationForms({ view, version: state, article, observedAt });
}

test("a Luxembourg act held only as published is cited in French legal usage, dated, with its evidence form", () => {
  const view = readEvidenceBundle(LU.result.value);
  const [state] = view.states;
  const article = state.articles.find((candidate) => candidate.publisherId === "art_15");
  const forms = citationForms({ view, version: state, article, observedAt: OBSERVED });
  // The act's type and date from its ELI; dated, since an act held only as published may have been amended since (R76).
  const usual = "Art. 15 de la loi du 10 août 1991, dans sa version applicable au 1er février 2024";
  const permalink = `${CITATION_ORIGIN}${article.permalink}`;
  assert.equal(forms.language, "fra", "the text's language, whatever the interface's");
  assert.equal(forms.conventional, usual);
  assert.equal(forms.withPermalink, `${usual} (${permalink})`);
  assert.equal(permalink, `https://law.soufien.lu${state.permalink}/art_15`);
  assert.deepEqual(forms.evidenceLines, [
    usual,
    `Permalien${NB}: ${permalink}`,
    `ELI${NB}: ${WORK}/jo`,
    `Version${NB}: applicable à partir du 2024-02-01 (éditeur)`,
    // The fixture's index holds no isPartOf fact for its act: said, never guessed.
    `Publication${NB}: le numéro du Mémorial A n’est pas détenu par cet index.`,
    `Empreinte du texte (SHA-256)${NB}: ${article.textSha256}`,
    "Consulté sur Lex le 2026-09-18 (date au Luxembourg).",
    `Tel que publié${NB}; peut avoir été modifié depuis${NB}; aucun texte consolidé détenu.`,
    FR.common.asPublished.authenticity.publication_date_not_stated.replace("{act}", `${WORK}/jo`),
  ]);
  assert.equal(forms.evidence, forms.evidenceLines.join("\n"));
  assert.equal(forms.textSha256, article.textSha256);
  for (const line of forms.evidenceLines) assertTimeVocabulary(line, "a Luxembourg evidence line");
  // Other article numbers, as the act prints them.
  assert.equal(luxembourgForms(LU.result.value, "art_1er").conventional, "Art. 1er de la loi du 10 août 1991, dans sa version applicable au 1er février 2024");
  assert.equal(luxembourgForms(LU.result.value, "art_30-2").conventional, "Art. 30-2 de la loi du 10 août 1991, dans sa version applicable au 1er février 2024");
});

test("the publisher's title, the act's Mémorial A issue and its publication date are said when the index holds them, and only then", () => {
  const titled = (title, more = {}) => luxembourgAnswer((answer) => {
    const jo = `${WORK}/jo`;
    answer.citation_facts.titles = [{
      language: "fra",
      expression_iri: answer.states[0].expression_iri,
      titles: (Array.isArray(title) ? title : [title]).map((words) => ({ title: words, evidence_sha256: "1".repeat(64) })),
      short_titles: [],
    }];
    answer.citation_facts.memorial_issues = more.memorial ?? [];
    answer.citation_facts.publication_dates = more.published ?? [];
    if (more.published !== undefined) {
      // The as-published block reads the same publication date, which chooses its authenticity sentence.
      answer.as_published.authenticity.publication_dates = more.published;
      answer.as_published.authenticity.basis = "printed_memorial";
      answer.as_published.authenticity.sentence = AUTHENTICITY_SENTENCES.printed_memorial.replace("{act}", jo);
    }
  });
  const title = "Loi du 10 août 1991 sur la profession d’avocat.";
  const full = luxembourgForms(titled(title, {
    memorial: [{ ...fact(`${WORK}/jo`, "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58"), object_kind: "iri" }],
    published: [fact(`${WORK}/jo`, "1991-08-27")],
  }), "art_15");
  assert.equal(full.conventional, "Art. 15 de la loi du 10 août 1991 sur la profession d’avocat, dans sa version applicable au 1er février 2024",
    "the title's words after its type and date follow the act's, its final full stop taken off");
  assert.ok(full.evidenceLines.includes(`Intitulé (éditeur)${NB}: ${title}`), "the title, verbatim");
  assert.ok(full.evidenceLines.includes(`Publication${NB}: Mémorial A n° 58 de 1991 (http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58)`));
  assert.ok(full.evidenceLines.includes(`Date de publication (éditeur)${NB}: 1991-08-27`));
  assert.ok(!full.evidenceLines.some((line) => line.includes("n’est pas détenu")), "a held issue is never said not held");
  assert.equal(full.evidenceLines.at(-1), FR.common.asPublished.authenticity.printed_memorial.replace("{act}", `${WORK}/jo`));

  // A title that continues after a comma keeps it; one that does not start with the act's type and date is not read into
  // the usual form, only quoted in the evidence form; two titles are no one title, and neither is said.
  assert.equal(luxembourgForms(titled("Loi du 10 août 1991, portant organisation du barreau."), "art_15").conventional,
    "Art. 15 de la loi du 10 août 1991, portant organisation du barreau, dans sa version applicable au 1er février 2024");
  const revision = luxembourgForms(titled("Révision de l’article 52 de la Constitution."), "art_15");
  assert.equal(revision.conventional, "Art. 15 de la loi du 10 août 1991, dans sa version applicable au 1er février 2024");
  assert.ok(revision.evidenceLines.includes(`Intitulé (éditeur)${NB}: Révision de l’article 52 de la Constitution.`));
  const two = luxembourgForms(titled([title, "Loi du 10 août 1991 relative à autre chose."]), "art_15");
  assert.equal(two.conventional, "Art. 15 de la loi du 10 août 1991, dans sa version applicable au 1er février 2024");
  assert.ok(!two.evidenceLines.some((line) => line.startsWith("Intitulé")), "no title is chosen between two");
  // A Mémorial issue of another shape is printed as the publisher names it; several are each printed.
  const odd = luxembourgForms(titled(title, { memorial: [fact(`${WORK}/jo`, "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58_2")] }), "art_15");
  assert.ok(odd.evidenceLines.includes(`Publication${NB}: http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58_2`));

  // The reader refuses what would name another act or another text.
  assert.throws(() => readEvidenceBundle(luxembourgAnswer((answer) => { answer.citation_facts.titles = [{ language: "fra", expression_iri: `${WORK}/consolide/20210822/fr`, titles: [], short_titles: [] }]; })), /which no listed state is/);
  assert.throws(() => readEvidenceBundle(luxembourgAnswer((answer) => { answer.citation_facts.memorial_issues = [fact(WORK, "x")]; })), /not of/);
  assert.throws(() => readEvidenceBundle(luxembourgAnswer((answer) => { answer.citation_facts.journal_officiel_iri = `${WORK}0/jo`; })), /not the act as published/);
  assert.throws(() => readEvidenceBundle(luxembourgAnswer((answer) => { delete answer.citation_facts; })), /citation_facts/);
});

/** A Luxembourg reading view of one consolidated state, built as the reader builds it, for the cases the census holds none of. */
function consolidated({ work = WORK, date = "2021-08-22", next = null, titles = [] } = {}) {
  const legal = `${work}/consolide/${date.replaceAll("-", "")}`;
  const state = {
    language: "fra",
    applicabilityDate: date,
    nextApplicabilityDate: next,
    permalink: `/lu-legilux/loi-1991-08-10-n3/${date}--${"d".repeat(64)}/fra`,
    publisherWorkIri: work,
    publisherLegalResourceIri: legal,
    expressionIri: `${legal}/fr`,
  };
  const view = {
    publisher: "lu-legilux",
    asPublished: null,
    citationFacts: { titles: titles.map((words) => ({ language: "fra", expressionIri: state.expressionIri, titles: [words], shortTitles: [] })), journalOfficielIri: `${work}/jo`, memorialIssues: [], publicationDates: [] },
  };
  return { view, state };
}

test("a consolidated Luxembourg version is cited with its date only when a later one applies today, with the site's sentence on consolidated text", () => {
  const article = (state, publisherId = "art_15") => ({ publisherId, textSha256: "a".repeat(64), permalink: `${state.permalink}/${publisherId}` });
  const current = consolidated();
  const forms = citationForms({ view: current.view, version: current.state, article: article(current.state), observedAt: "2026-10-17T10:00:00Z" });
  assert.equal(forms.conventional, "Art. 15 de la loi du 10 août 1991", "the version that answers today needs no date");
  assert.equal(forms.evidenceLines.at(-1), FR.site.authenticity, "a consolidation is never said to be authentic (R18, R20)");
  assert.ok(forms.evidenceLines.includes(`ELI${NB}: ${WORK}/consolide/20210822`), "the ELI of the resource quoted, the consolidation's");
  assert.ok(!forms.evidenceLines.some((line) => line.startsWith("Tel que publié")), "no as-published label on a consolidated text");
  // A later state applicable on or before today: this one is not current, and is dated; one applicable after today is not yet.
  const superseded = consolidated({ next: "2023-01-01" });
  assert.equal(citationForms({ view: superseded.view, version: superseded.state, article: article(superseded.state), observedAt: "2026-10-17T10:00:00Z" }).conventional,
    "Art. 15 de la loi du 10 août 1991, dans sa version applicable au 22 août 2021");
  const pending = consolidated({ next: "2027-01-01" });
  assert.equal(citationForms({ view: pending.view, version: pending.state, article: article(pending.state), observedAt: "2026-10-17T10:00:00Z" }).conventional,
    "Art. 15 de la loi du 10 août 1991", "a later state that applies only after today does not make this one past");
  // Today is Luxembourg's: 23:30 UTC on 31 December is already the next year there (winter time, an hour ahead).
  const future = consolidated({ date: "2027-01-01" });
  assert.equal(citationForms({ view: future.view, version: future.state, article: article(future.state), observedAt: "2026-12-31T23:30:00Z" }).conventional,
    "Art. 15 de la loi du 10 août 1991");
  assert.equal(citationForms({ view: future.view, version: future.state, article: article(future.state), observedAt: "2026-12-31T22:30:00Z" }).conventional,
    "Art. 15 de la loi du 10 août 1991, dans sa version applicable au 1er janvier 2027", "a version not yet applicable is dated");

  // A code is named by its title (R65's example), its article as the code prints it; with no title held, no usual form.
  const code = consolidated({ work: "http://data.legilux.public.lu/eli/etat/leg/code/travail", titles: ["Code du travail"] });
  const labour = citationForms({ view: code.view, version: code.state, article: article(code.state, "art_l_121-6"), observedAt: "2026-10-17T10:00:00Z" });
  assert.equal(labour.conventional, "Art. L. 121-6 du Code du travail");
  const untitled = consolidated({ work: "http://data.legilux.public.lu/eli/etat/leg/code/travail" });
  const bare = citationForms({ view: untitled.view, version: untitled.state, article: article(untitled.state, "art_l_121-6"), observedAt: "2026-10-17T10:00:00Z" });
  assert.equal(bare.conventional, null);
  assert.equal(bare.withPermalink, bare.permalink, "with no usual form, the permalink alone");
  assert.equal(bare.evidenceLines[0], bare.permalink);
  // A type of act the generator does not name.
  const decree = consolidated({ work: "http://data.legilux.public.lu/eli/etat/leg/dec/2001/01/01/n1" });
  assert.equal(citationForms({ view: decree.view, version: decree.state, article: article(decree.state), observedAt: "2026-10-17T10:00:00Z" }).conventional, null);
});

test("an act published since 2017, whose ELI ends a{n}, is named too, and a long or multi-line title stays in the evidence form only", () => {
  // Every held act since 2017 (1,701 of the population's 1,730 works) ends its ELI a{n}, the others n{n} (review of the French).
  const omnibus = consolidated({ work: "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/03/a318", date: "2017-03-07" });
  const article = (state) => ({ publisherId: "art_1er", textSha256: "a".repeat(64), permalink: `${state.permalink}/art_1er` });
  assert.equal(citationForms({ view: omnibus.view, version: omnibus.state, article: article(omnibus.state), observedAt: "2026-10-17T10:00:00Z" }).conventional,
    "Art. 1er de la loi du 3 mars 2017");
  const rgd = consolidated({ work: "http://data.legilux.public.lu/eli/etat/leg/rgd/2018/06/06/a464", date: "2018-06-12" });
  assert.equal(citationForms({ view: rgd.view, version: rgd.state, article: { publisherId: "art_unique", textSha256: "a".repeat(64), permalink: `${rgd.state.permalink}/art_unique` }, observedAt: "2026-10-17T10:00:00Z" }).conventional,
    "Art. unique du règlement grand-ducal du 6 juin 2018");
  // A title whose words run on several lines (the publisher writes a line break as backslash and n), or past 200
  // characters, is no usual citation's: the type and date stand alone, and the evidence form quotes the title whole.
  const listed = "Loi du 3 mars 2017 dite « Omnibus » portant modification :\\n- de la loi modifiée du 19 juillet 2004 ;\\n- de la loi du 21 mai 1999.";
  const titled = consolidated({ work: "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/03/a318", date: "2017-03-07", titles: [listed] });
  const forms = citationForms({ view: titled.view, version: titled.state, article: article(titled.state), observedAt: "2026-10-17T10:00:00Z" });
  assert.equal(forms.conventional, "Art. 1er de la loi du 3 mars 2017");
  assert.ok(forms.evidenceLines.includes(`Intitulé (éditeur)${NB}: ${listed}`), "the title, verbatim, in the evidence form");
  const long = `Loi du 3 mars 2017 portant ${"modification de diverses dispositions ".repeat(6).trim()}.`;
  const longer = consolidated({ work: "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/03/a318", date: "2017-03-07", titles: [long] });
  assert.ok(long.length - "Loi du 3 mars 2017 ".length > 200);
  assert.equal(citationForms({ view: longer.view, version: longer.state, article: article(longer.state), observedAt: "2026-10-17T10:00:00Z" }).conventional, "Art. 1er de la loi du 3 mars 2017");
  const short = consolidated({ work: "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/03/a318", date: "2017-03-07", titles: ["Loi du 3 mars 2017 portant modification de la loi électorale."] });
  assert.equal(citationForms({ view: short.view, version: short.state, article: article(short.state), observedAt: "2026-10-17T10:00:00Z" }).conventional,
    "Art. 1er de la loi du 3 mars 2017 portant modification de la loi électorale");
});

test("an article's number is read from the publisher's id by a fixed rule, and an id of no shape is given no usual form", () => {
  const french = citationWords("fra");
  const english = citationWords("eng");
  for (const [id, said] of [
    ["art_15", "Art. 15"], ["art_1er", "Art. 1er"], ["art_ier", "Art. Ier"], ["art_iii", "Art. III"], ["art_xxxviii", "Art. XXXVIII"],
    ["art_unique", "Art. unique"], ["article_unique", "Art. unique"], ["art_40bis", "Art. 40bis"], ["art12", "Art. 12"], ["art1er", "Art. 1er"],
    ["article_17", "Art. 17"], ["art_30-2", "Art. 30-2"], ["art_l_121-6", "Art. L. 121-6"], ["art_2_champ_d'application", "Art. 2"],
    ["art_4)_art_13", null], ["art__art_1er", null], ["section_1", null],
  ]) {
    assert.equal(luxembourgArticle(id, french), said, id);
  }
  assert.equal(luxembourgArticle("art_1er", english), "Article 1");
  assert.equal(luxembourgArticle("art_unique", english), "Sole article");
  // A quoted article of an amending act is not an article of the act: cited by its permalink alone, and the page says so.
  const quoted = consolidated();
  const forms = citationForms({ view: quoted.view, version: quoted.state, article: { publisherId: "art_4)_art_13", textSha256: "a".repeat(64), permalink: `${quoted.state.permalink}/art_4%29_art_13` }, observedAt: "2026-10-17T10:00:00Z" });
  assert.equal(forms.conventional, null);
  const markup = renderToStaticMarkup(h(Citations, { view: quoted.view, version: quoted.state, article: { publisherId: "art_4)_art_13", textSha256: "a".repeat(64), permalink: `${quoted.state.permalink}/art_4%29_art_13` }, observedAt: "2026-10-17T10:00:00Z" }));
  assert.ok(markup.includes(EN.reading.citeNotComposed), "the usual form says why it is not composed");
  // An article held without text is cited as any article of its version, with no text digest.
  const without = citationForms({ view: quoted.view, version: quoted.state, article: { publisherId: "art_36" }, observedAt: "2026-10-17T10:00:00Z" });
  assert.equal(without.permalink, `${CITATION_ORIGIN}${quoted.state.permalink}/art_36`);
  assert.ok(without.evidenceLines.includes(`Empreinte du texte (SHA-256)${NB}: aucune, l’article est détenu sans texte.`));
  assert.equal(without.textSha256, null);
});

test("dates are said in words in the citation's language, the first of a month as French writes it", () => {
  assert.equal(dateInWords("1991-08-10", citationWords("fra")), "10 août 1991");
  assert.equal(dateInWords("2024-02-01", citationWords("fra")), "1er février 2024");
  assert.equal(dateInWords("2024-02-01", citationWords("eng")), "1 February 2024");
  assert.equal(dateInWords("2016-04-27", citationWords("eng")), "27 April 2016");
  assert.throws(() => dateInWords("2024-02-30", citationWords("fra")), /calendar date/);
});

test("an EU act is cited as EUR-Lex numbers it, at its wording's date when another wording answers today", () => {
  const view = readEuropeEvidenceBundle(EU.result.value);
  const [wording] = view.wordings;
  const article = wording.articles[0];
  const forms = citationForms({ view, version: wording, article, observedAt: EU.context.freshness.observed_at });
  // The English original wording; the 2024 consolidation (held in French only) answers today, so the original is dated.
  const usual = "Article 1 of Regulation (EU) 2016/679, as at 27 April 2016";
  assert.equal(forms.language, "eng");
  assert.equal(forms.conventional, usual);
  assert.equal(forms.withPermalink, `${usual} (${CITATION_ORIGIN}${article.permalink})`);
  assert.deepEqual(forms.evidenceLines, [
    usual,
    `Permalink: ${CITATION_ORIGIN}${article.permalink}`,
    "CELEX: 32016R0679",
    "Wording: the original wording of 2016-04-27",
    "Published in: the Official Journal reference is not held by this index.",
    `Text digest (SHA-256): ${article.textSha256}`,
    "Consulted on Lex on 2026-09-18 (date in Luxembourg).",
    EU.result.value.authenticity,
  ]);
  for (const line of forms.evidenceLines) assertTimeVocabulary(line, "an EU evidence line");
  assert.doesNotMatch(forms.evidence, /\b(?:applicab|version|state)/i, "an EU date is never an applicability date (R21)");
  // The bundle of an index that holds no consolidation: its original wording cannot be said to answer today, so it is dated.
  const original = readEuropeEvidenceBundle(europeBundle());
  const sample = citationForms({ view: original, version: original.wordings[0], article: original.wordings[0].articles[1], observedAt: "2026-10-17T10:00:00Z" });
  assert.equal(sample.conventional, "Article 2 of Regulation (EU) 2016/679, as at 27 April 2016", "its heading is the article's title, so its id's number names it");
  assert.equal(EU.result.value.authenticity, EN.citation.europe.authenticity, "the table holds the platform's statement word for word");
});

/** An EU reading view of one consolidated wording, as the reader builds it, in `language`. */
function europeConsolidation({ language = "fra", next = null, celex = "32016R0679", authenticity = EN.citation.europe.authenticity, heading = "Article 33" } = {}) {
  const permalink = `/eu-eurlex/${celex}/2024-01-01--${"c".repeat(64)}/${language}`;
  return {
    view: { publisher: "eu-eurlex", celex, authenticity },
    wording: { language, kind: "consolidated_version", wordingDate: "2024-01-01", nextDate: next, wordingCelex: `0${celex.slice(1)}-20240101`, permalink },
    article: { publisherId: "033", heading, textSha256: "b".repeat(64), permalink: `${permalink}/033` },
  };
}

test("an EU consolidation in French is cited in French, with its own CELEX and the EU's statement in French", () => {
  const { view, wording, article } = europeConsolidation();
  const forms = citationForms({ view, version: wording, article, observedAt: "2026-10-17T10:00:00Z" });
  assert.equal(forms.conventional, "Article 33 du règlement (UE) 2016/679", "the latest wording held answers today: no date");
  assert.deepEqual(forms.evidenceLines.slice(1), [
    `Permalien${NB}: ${CITATION_ORIGIN}${article.permalink}`,
    `CELEX${NB}: 02016R0679-20240101`,
    `Libellé${NB}: le libellé consolidé du 2024-01-01`,
    `Publication${NB}: la référence au Journal officiel n’est pas détenue par cet index.`,
    `Empreinte du texte (SHA-256)${NB}: ${"b".repeat(64)}`,
    "Consulté sur Lex le 2026-10-17 (date au Luxembourg).",
    FR.citation.europe.authenticity,
  ]);
  const later = europeConsolidation({ next: "2025-06-01" });
  assert.equal(citationForms({ view: later.view, version: later.wording, article: later.article, observedAt: "2026-10-17T10:00:00Z" }).conventional,
    "Article 33 du règlement (UE) 2016/679, au 1er janvier 2024");
  // The provision's own heading, as the text prints it, its no-break spaces plain in a citation that is copied.
  const bis = europeConsolidation({ heading: `Article${NB}32${NB}bis` });
  assert.equal(citationForms({ view: bis.view, version: bis.wording, article: bis.article, observedAt: "2026-10-17T10:00:00Z" }).conventional, "Article 32 bis du règlement (UE) 2016/679");
  // The narrow no-break space too (review of #975).
  const NNB = String.fromCharCode(0x202f);
  const narrow = europeConsolidation({ heading: `Article${NNB}32${NNB}bis` });
  assert.equal(citationForms({ view: narrow.view, version: narrow.wording, article: narrow.article, observedAt: "2026-10-17T10:00:00Z" }).conventional, "Article 32 bis du règlement (UE) 2016/679");
  // A statement the table does not hold word for word is said as the platform served it.
  const other = europeConsolidation({ authenticity: "Another statement." });
  assert.equal(citationForms({ view: other.view, version: other.wording, article: other.article, observedAt: "2026-10-17T10:00:00Z" }).evidenceLines.at(-1), "Another statement.");
  // A text in a language no citation is composed in is cited by its permalink alone: no form.
  const german = europeConsolidation({ language: "deu" });
  assert.equal(citationForms({ view: german.view, version: german.wording, article: german.article, observedAt: "2026-10-17T10:00:00Z" }), null);
  assert.equal(renderToStaticMarkup(h(Citations, { view: german.view, version: german.wording, article: german.article, observedAt: "2026-10-17T10:00:00Z" })), "");
});

test("EU acts are named by their CELEX's type, year and number in EUR-Lex's numbering, the domain only where the year says it", () => {
  const named = (celex, language = "eng") => {
    const { view, wording, article } = europeConsolidation({ celex, language });
    return citationForms({ view, version: wording, article, observedAt: "2026-10-17T10:00:00Z" }).conventional;
  };
  for (const [celex, english, french] of [
    ["32016R0679", "Article 33 of Regulation (EU) 2016/679", "Article 33 du règlement (UE) 2016/679"],
    ["32013R0575", "Article 33 of Regulation (EU) No 575/2013", "Article 33 du règlement (UE) n° 575/2013"],
    ["32007R0864", "Article 33 of Regulation (EC) No 864/2007", "Article 33 du règlement (CE) n° 864/2007"],
    ["31971R1408", "Article 33 of Regulation (EEC) No 1408/71", "Article 33 du règlement (CEE) n° 1408/71"],
    ["32006L0112", "Article 33 of Directive 2006/112/EC", "Article 33 de la directive 2006/112/CE"],
    ["31985L0374", "Article 33 of Directive 85/374/EEC", "Article 33 de la directive 85/374/CEE"],
    ["32016L0680", "Article 33 of Directive (EU) 2016/680", "Article 33 de la directive (UE) 2016/680"],
    ["32011D0833", "Article 33 of Decision 2011/833/EU", "Article 33 de la décision 2011/833/UE"],
    // 1993 and 2009 acts carry either of two domains, and the index holds which none: none is written.
    ["31993L0013", "Article 33 of Directive 93/13", "Article 33 de la directive 93/13"],
    ["32009L0138", "Article 33 of Directive 2009/138", "Article 33 de la directive 2009/138"],
    ["32009R1223", "Article 33 of Regulation No 1223/2009", "Article 33 du règlement n° 1223/2009"],
  ]) {
    assert.equal(named(celex), english, celex);
    assert.equal(named(celex, "fra"), french, celex);
  }
  // A recommendation, an agreement or a consolidated CELEX is no act this numbering names.
  for (const celex of ["32013H0001", "22016A0101(01)", "02016R0679-20240101"]) assert.equal(named(celex), null, celex);
  assert.equal(europeArticle({ publisherId: "038L", heading: "" }, citationWords("eng")), "Article 38l");
});

test("verify resolves the permalink and evidence forms by their hashes, on the mounts that sent the census's bundles", () => {
  const lu = readEvidenceBundle(LU.result.value);
  const luForms = citationForms({ view: lu, version: lu.states[0], article: lu.states[0].articles.find((article) => article.publisherId === "art_15"), observedAt: OBSERVED });
  const eu = readEvidenceBundleAnswer(EU.result.value);
  const euForms = citationForms({ view: eu, version: eu.wordings[0], article: eu.wordings[0].articles.find((article) => article.publisherId === "001"), observedAt: OBSERVED });
  for (const [forms, verified] of [[luForms, LU_VERIFIED], [euForms, EU_VERIFIED]]) {
    const value = verified.result.value;
    // The permalink the forms print, under the site's origin, is the one verify was asked (it reads both forms of it).
    assert.equal(`${CITATION_ORIGIN}${value.requested_identifier}`, forms.permalink);
    assert.equal(evidenceVerdict(forms, value), "matches", "both hashes: the version's, in the permalink, and the text's");
    assert.equal(evidenceVerdict({ ...forms, textSha256: "0".repeat(64) }, value), "text_differs");
    assert.equal(evidenceVerdict({ ...forms, permalink: `${forms.permalink.slice(0, forms.permalink.lastIndexOf("/"))}/art_99` }, value), "another_article");
    assert.equal(evidenceVerdict(forms, { ...value, verdict: "current_digests" }), "refused");
    assert.equal(evidenceVerdict(forms, null), "refused");
    assert.ok(forms.evidence.includes(value.article_text_sha256), "the evidence form carries the digest verify names");
  }
  assert.equal(LU_VERIFIED.result.value.state_sha256, lu.states[0].stateSha256);
  assert.equal(EU_VERIFIED.result.value.wording_sha256, eu.wordings[0].wordingSha256);
});

test("the export keeps the evidence form: each item and exclusion carries the three forms in JSON, CSV and PDF", () => {
  const answer = luxembourgAnswer((value) => {
    // One article held without text, so an exclusion is cited too.
    const [state] = value.states;
    const moved = state.articles.pop();
    state.articles_without_text.push({ article_identity_sha256: moved.article_identity_sha256, publisher_id: moved.publisher_id, reason: "no_text_tokens" });
    state.validity_conflict_count = state.articles.filter((article) => article.validity_conflict).length;
  });
  const view = readEvidenceBundle(answer);
  const [state] = view.states;
  const pinned = [state.articles[0], state.articlesWithoutText[0]].map((article) => ({ stateSha256: state.stateSha256, publisherId: article.publisherId }));
  const model = composeExport({ view, pinned, observedAt: OBSERVED });
  const expected = citationForms({ view, version: state, article: state.articles[0], observedAt: OBSERVED });
  assert.equal(model.items[0].citations.evidence, expected.evidence, "one generator for the page and the export");
  assert.equal(model.items[0].citation, state.articles[0].permalink, "the permalink citation verify takes is kept");
  assert.ok(model.excluded[0].citations.evidence.includes("aucune, l’article est détenu sans texte"));

  const json = JSON.parse(exportJson(model));
  assert.deepEqual(json.items[0].citation_forms, { language: "fra", conventional: expected.conventional, with_permalink: expected.withPermalink, evidence: expected.evidence });
  assert.equal(json.excluded[0].citation_forms.language, "fra");

  const csv = exportCsv(model);
  for (const column of ["citation_conventional", "citation_with_permalink", "citation_evidence"]) assert.ok(CSV_COLUMNS.includes(column), column);
  assert.ok(csv.includes(`"${expected.evidence.replaceAll('"', '""')}"`), "the evidence form is one quoted CSV field, its lines kept");

  const pdf = Buffer.from(exportPdf(model)).toString("latin1");
  assert.ok(pdf.includes("Citation \\(evidence form\\)"), "the PDF labels each item's evidence form");
  assert.ok(pdf.includes("Consult\\351 sur Lex le 2026-09-18 \\(date au Luxembourg\\)."), "and sets its lines, in the text's language");
  assert.equal(pdfRefusal(model), null, "the French forms' characters are the standard fonts' (the no-break space, ’, é, °)");

  // An EU export keeps its forms too, and its PDF is offered.
  const eu = readEuropeEvidenceBundle(europeBundle());
  const [wording] = eu.wordings;
  const euModel = composeEuropeExport({
    view: eu, observedAt: OBSERVED, registrySha256: "3".repeat(64),
    pinned: [...wording.articles, ...wording.articlesWithoutText].map((article) => ({ wordingSha256: wording.wordingSha256, publisherId: article.publisherId })),
  });
  assert.equal(euModel.items[0].citations.conventional, "Article 1 of Regulation (EU) 2016/679, as at 27 April 2016");
  assert.ok(euModel.excluded[0].citations.evidence.includes("Text digest (SHA-256): none, the article is held without text."));
  assert.equal(pdfRefusal(euModel), null);
  assert.ok(Buffer.from(exportPdf(euModel)).toString("latin1").includes("Published in: the Official Journal reference is not held by this index."));
});

test("a title written with combining accents is cited composed, quoted as served, and set by the PDF as the letters it composes (review of #975)", () => {
  // Four held règlements grand-ducaux of 2018 hold such titles; the PDF refused them, which no export did before the forms.
  const decomposed = "Loi du 10 août 1991 relative à la procédure d’élection.".normalize("NFD");
  assert.notEqual(decomposed, decomposed.normalize("NFC"), "the title is decomposed");
  const answer = luxembourgAnswer((value) => {
    value.citation_facts.titles = [{ language: "fra", expression_iri: value.states[0].expression_iri, titles: [{ title: decomposed, evidence_sha256: "1".repeat(64) }], short_titles: [] }];
  });
  const forms = luxembourgForms(answer, "art_15");
  assert.equal(forms.conventional, "Art. 15 de la loi du 10 août 1991 relative à la procédure d’élection, dans sa version applicable au 1er février 2024",
    "the usual form, copied, in its composed form, its title's words read after the type and date");
  assert.equal(forms.withPermalink, forms.withPermalink.normalize("NFC"));
  assert.ok(forms.evidenceLines.includes(`Intitulé (éditeur)${NB}: ${decomposed}`), "the evidence form quotes the title as served");

  const view = readEvidenceBundle(answer);
  const [state] = view.states;
  const model = composeExport({ view, pinned: [{ stateSha256: state.stateSha256, publisherId: "art_15" }], observedAt: OBSERVED });
  assert.ok(JSON.parse(exportJson(model)).items[0].citation_forms.evidence.includes(decomposed), "the JSON keeps the bytes as served");
  assert.equal(pdfRefusal(model), null, "the PDF is offered");
  const pdf = Buffer.from(exportPdf(model)).toString("latin1");
  assert.ok(pdf.includes("proc\\351dure d\\222\\351lection."), "set as the composed letters WinAnsi holds");
  assert.ok(pdf.includes("Accents written as combining marks are set here as the accented letters they compose"), "and the PDF says so");
  // A text already composed adds no such note.
  const plain = composeExport({ view: readEvidenceBundle(LU.result.value), pinned: [{ stateSha256: state.stateSha256, publisherId: "art_15" }], observedAt: OBSERVED });
  assert.ok(!Buffer.from(exportPdf(plain)).toString("latin1").includes("Accents written as combining marks"));
});

test("the cited article at its permalink carries its three forms, in the text's language, under the page's labels", () => {
  const pinned = envelopeOf("evidence_bundle", "a state's permalink at its own date");
  const [state] = pinned.result.value.states;
  const pin = readingPinOf(`/en${state.permalink}/art_15`, pinned);
  const markup = renderToStaticMarkup(h(PinnedLead, { outcome: readingOutcome({ state: "success", envelope: pinned }), pin })).replaceAll("<!-- -->", "");
  const view = readEvidenceBundle(pinned.result.value);
  const forms = citationForms({ view, version: view.states[0], article: view.states[0].articles.find((article) => article.publisherId === "art_15"), observedAt: pinned.context.freshness.observed_at });
  const block = markup.slice(markup.indexOf('<section data-citation="fra">'));
  assert.ok(block.length > 0, "the cited article carries its citations");
  assert.ok(block.startsWith(`<section data-citation="fra"><h3>${EN.reading.cite}</h3><dl><dt>${EN.reading.citeConventional}</dt>`), "under the English page's labels");
  assert.ok(block.includes(`<dd data-citation-form="conventional" lang="fr">${forms.conventional}</dd>`), "the usual form, marked French as its text is");
  assert.ok(block.includes(`<dd data-citation-form="permalink" lang="fr">${forms.withPermalink}</dd>`));
  assert.ok(block.includes(`<dd data-citation-form="evidence" lang="fr" data-permalink="${forms.permalink}" data-text-sha256="${forms.textSha256}"><span>${forms.evidenceLines[0]}</span><span><br/>`),
    "the evidence form, line by line, with the permalink and text digest a check of it asks verify to confirm");
  assert.ok(!block.includes("blockquote") && !block.includes("data-article="), "no quotation: the journeys' quotes are the text's alone");
});

test("the forms are said in R21's words and the reviewed French's typography, and are never in force or applied", () => {
  for (const table of [FR, EN]) {
    for (const [path, text] of Object.entries(table.citation.evidence)) assertTimeVocabulary(text, `citation.evidence.${path}`);
    assertTimeVocabulary(table.citation.luxembourg.version, "citation.luxembourg.version");
    assertTimeVocabulary(table.citation.europe.version, "citation.europe.version");
  }
  // The French table's months and its first of a month, as French writes a date in a citation.
  assert.deepEqual([...FR.citation.months], ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"]);
  assert.equal(FR.citation.firstOfMonth, "{day}er {month} {year}");
});

test("the usual form is verified by the exact-reference phase: read as the search's query, it opens the article it cites", () => {
  // R65's "verify resolves every form", for the form with no hash in it (the plan's item 4.2): the answer census asked the
  // phase this article's usual form, as the generator composes it from the bundle the envelope census holds.
  const forms = luxembourgForms(LU.result.value, "art_15");
  const sample = ANSWERS.sampled.find((row) => row.operation === "search" && row.answer.requested_query === forms.conventional);
  assert.ok(sample, `the answer census asks the phase the generator's usual form "${forms.conventional}"`);
  const view = readSearchAnswer(sample.answer);
  assert.equal(usualVerdict(forms, view), "matches");
  assert.equal(view.opens.address, forms.permalink.slice(CITATION_ORIGIN.length), "the version the form names, at its article");

  // Each other answer says why not.
  const at = (address, addressKind = "permalink") => ({ ...view, opens: { ...view.opens, address, addressKind } });
  const [, publisher, work, pin, language] = view.opens.address.split("/");
  assert.equal(usualVerdict(forms, at(`/${publisher}/${work}/${pin}/${language}/art_16`)), "another_article");
  assert.equal(usualVerdict(forms, at(`/${publisher}/${work}/2020-01-01--${"0".repeat(64)}/${language}/art_15`)), "another_version");
  assert.equal(usualVerdict(forms, at(`/${publisher}/loi-2000-01-01-n1/${pin}/${language}/art_15`)), "another_act");
  assert.equal(usualVerdict(forms, at(`/${publisher}/${work}/${language}/art_15`, "readable")), "matches", "a form with no date cites today's version");
  assert.equal(usualVerdict(forms, at(`/${publisher}/${work}/${language}/art_16`, "readable")), "another_article");
  assert.equal(usualVerdict(forms, { ...view, outcome: "not_opened", opens: null }), "refused");
  assert.equal(usualVerdict(forms, null), "refused");
  assert.equal(usualVerdict({ ...forms, conventional: null }, view), "refused", "no usual form, nothing to verify");
});
