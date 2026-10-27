// R77 and R57 on the live pages (the audit of 2026-10-27, gaps 1 and 2), measured against what the platform really sent.
//
// R77: an act the publisher's catalogue holds and this site holds no version of is a refusal carrying its record
// (`text_not_available`, `record_kind` catalogue_only; `schemas/v3-platform/envelope-samples.json`, the act the fixture's
// catalogue names on the fixture's day); every screen says it in one sentence, and the card says why no text is held and each
// fact of the record by its role, in English and in French. R57: the comparison of two states carries each changed article's
// served text (`schemas/v3-platform/answer-samples.json`), which the compare page shows inline, word by word (`word-diff.mjs`),
// rendered by the server at the comparison's own address, readable without JavaScript.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup, renderToString } from "react-dom/server";

import { CompareAnswerView, DossierAnswerView, LIVE_COMPARE_ROOT, RefusalCard, liveCompareTree, renderLiveComparePage } from "../.react-build/app.mjs";
import { compareOutcome } from "../scripts/live-compare.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { searchOutcome } from "../scripts/live-search.mjs";
import { readDiff } from "../scripts/compare-answer.mjs";
import { CATALOGUE_RECORD_SENTENCE } from "../scripts/live-refusals.mjs";
import { CATALOGUE_RECORD_KEYS, REQUIRED_PAYLOAD, catalogueRecord, reasonView, validateRefusal } from "../scripts/refusal-card.mjs";
import { LIVE_CHROME, liveChrome } from "../scripts/live-chrome.mjs";
import { MAX_EDITS, sidesOf, wordDiff, wordTokens } from "../scripts/word-diff.mjs";
import { compareAddress, compareOfQuery, sitePageFor } from "../scripts/permalinks.mjs";
import { INITIAL_ANSWER_ID } from "../scripts/initial-answer.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const payloads = JSON.parse(await readFile(new URL("../../schemas/v3-platform/refusal-payload-samples.json", import.meta.url), "utf8"));
const envelopeOf = (operation, scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === operation && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the ${operation} envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const RECORD_DOSSIER = envelopeOf("dossier", "an act the publisher's catalogue holds and the index holds no version of (R77)");
const RECORD_READING = envelopeOf("evidence_bundle", "the same act read at its address in French");
const RECORD_SEARCH = envelopeOf("search", "a reference naming the same act by the address this site gives it");

/** The two-state comparison of the answer census, its per-run digests given a digest's shape, as the compare test does. */
function twoStates() {
  const row = answers.sampled.filter((sample) => sample.operation === "diff")[1];
  let n = 0;
  const digests = (node) => {
    if (Array.isArray(node)) return node.map(digests);
    if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, digests(value)]));
    if (node === "<varies-per-run>") { n += 1; return (String(n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
    return node;
  };
  const envelope = structuredClone(envelopeOf("diff", "the state on one date against the state on the same date"));
  envelope.result.value = digests(row.answer);
  return envelope;
}

test("a text's word tokens give it back, and the parts of a comparison give back both texts exactly", () => {
  for (const text of ["", "Le ministre peut accorder une dérogation.", "L’article 5, alinéa 2 — modifié.\n\nArt. 6. Abrogé.", "  deux  espaces "]) {
    assert.equal(wordTokens(text).join(""), text);
  }
  const cases = [
    ["Le ministre peut accorder une dérogation.", "Le ministre accorde une dérogation motivée."],
    ["", "Texte ajouté."],
    ["Texte supprimé.", ""],
    ["identique", "identique"],
    ["L'article 5 est abrogé.", "L’article 5 est modifié."],
  ];
  for (const [before, after] of cases) {
    const { parts, complete } = wordDiff(before, after);
    assert.ok(complete);
    assert.deepEqual(sidesOf(parts), { before, after }, `${before} → ${after}`);
    for (let index = 1; index < parts.length; index += 1) {
      assert.ok(!(parts[index - 1].kind === "ins" && parts[index].kind === "del"), "a deletion before the insertion beside it");
      assert.notEqual(parts[index - 1].kind, parts[index].kind, "each part one kind, merged");
    }
  }
  // A change of an apostrophe, a capital or an accent is the change it is: nothing is normalised.
  assert.deepEqual(wordDiff("Loi", "loi").parts, [{ kind: "del", text: "Loi" }, { kind: "ins", text: "loi" }]);
  // White space alone between two changes is part of both, so a replacement reads as one.
  assert.deepEqual(wordDiff("Le ministre peut accorder une", "Le ministre accorde une").parts,
    [{ kind: "same", text: "Le ministre " }, { kind: "del", text: "peut accorder" }, { kind: "ins", text: "accorde" }, { kind: "same", text: " une" }]);
  // Past its bound a comparison is shown whole, and says so; still exact.
  const whole = wordDiff("un deux trois quatre", "cinq six sept huit", { maxEdits: 2 });
  assert.equal(whole.complete, false);
  assert.deepEqual(sidesOf(whole.parts), { before: "un deux trois quatre", after: "cinq six sept huit" });
  // The beginning and end the two texts share stay as they are even then (the French review of this PR, form 13).
  const shared = wordDiff("Le ministre peut accorder une aide aux entreprises.", "Le Gouvernement accorde une subvention aux communes.", { maxEdits: 1 });
  assert.equal(shared.complete, false);
  assert.equal(shared.parts[0].text, "Le ");
  assert.equal(shared.parts.at(-1).text, ".");
  assert.deepEqual(shared.parts.map((part) => part.kind), ["same", "del", "ins", "same"], "each side in one piece");
  assert.ok(MAX_EDITS >= 1000);
});

test("a changed article's wording is read from both sides' served texts, an added or removed one whole, an unchanged one not at all", () => {
  const view = readDiff(twoStates().result.value);
  const [comparison] = view.comparisons;
  const byId = new Map(comparison.articles.map((row) => [row.publisherId, row]));
  const changed = byId.get("art_15");
  assert.equal(changed.status, "changed");
  assert.deepEqual(sidesOf(changed.wording.parts), { before: changed.from[0].text, after: changed.to[0].text });
  assert.ok(changed.wording.parts.some((part) => part.kind !== "same"), "the change is shown");
  assert.deepEqual(byId.get("art_16").wording.parts.map((part) => part.kind), ["del"], "a removed article, its text deleted");
  assert.deepEqual(byId.get("art_16-new").wording.parts.map((part) => part.kind), ["ins"], "an added article, its text inserted");
  assert.ok([...byId.values()].filter((row) => row.status === "unchanged").every((row) => row.wording === null));
  assert.match(view.textRule, /^R57: /);

  // A side whose rights do not admit its text: the digests stand, and the row says which side it is not shown for.
  const withheld = twoStates();
  const row = withheld.result.value.comparisons[0].articles.find((entry) => entry.publisher_id === "art_15");
  row.to = row.to.map((entry) => ({ ...entry, text: null, text_sha256: null }));
  assert.deepEqual(readDiff(withheld.result.value).comparisons[0].articles.find((entry) => entry.publisherId === "art_15").wording.notServed, ["to"]);
  // A text with no digest, or some entries with a text and others with none, is no answer.
  const broken = twoStates();
  broken.result.value.comparisons[0].articles.find((entry) => entry.publisher_id === "art_15").to[0].text_sha256 = null;
  assert.throws(() => readDiff(broken.result.value), /text_sha256/);
  const partial = twoStates();
  delete partial.result.value.comparisons[0].articles.find((entry) => entry.publisher_id === "art_15").to[0].text;
  assert.throws(() => readDiff(partial.result.value), /some of its entries/);
});

test("the compare page shows each change inline, struck through and underlined, said in words, with a jump list and the next and previous change", () => {
  const outcome = compareOutcome({ state: "success", envelope: twoStates() });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = renderToStaticMarkup(h(CompareAnswerView, { outcome }));
  const copy = liveChrome().compare;
  assert.ok(markup.includes(`<del><span class="visually-hidden" lang="en">${copy.deletedStart} </span>`), "a deletion said in words");
  assert.ok(markup.includes(`<ins><span class="visually-hidden" lang="en">${copy.insertedStart} </span>`), "an insertion said in words");
  assert.match(markup, /<p class="wording-diff" lang="fr" data-wording="words">/, "the quotation in the text's language");
  assert.match(markup, /<nav aria-labelledby="jump-fra" data-jump-list="3"><h3 id="jump-fra">The changes<\/h3>/);
  assert.ok(markup.includes('<a href="#change-fra-1">art_15: changed</a>'));
  assert.ok(markup.includes('<li id="change-fra-2" data-status="removed">'));
  assert.ok(markup.includes('<a href="#change-fra-1">Previous change</a>') && markup.includes('<a href="#change-fra-3">Next change</a>'));
  assert.ok(!/color|colour/i.test(markup), "nothing is said by colour");
  // The digests and both sides' permalinks stay.
  assert.ok(markup.includes("wording <code>"), "the digests");
  assert.equal((markup.match(/data-side=/g) ?? []).length, 2);
});

test("a comparison's address is rendered by the server from diff, complete, and the browser hydrates the same tree", () => {
  const search = "?work=%2Flu-legilux%2Floi-1991-08-10-n3&from=2024-02-01&to=2025-04-01&language=fra";
  assert.deepEqual({ ...compareOfQuery(search) }, { identifier: "/lu-legilux/loi-1991-08-10-n3", date_from: "2024-02-01", date_to: "2025-04-01", language: "fra" });
  assert.equal(sitePageFor("/en/compare.html", { search }).operation, "diff");
  assert.equal(compareAddress("fr", { identifier: "/lu-legilux/loi-1991-08-10-n3", dateFrom: "2024-02-01", dateTo: "2025-04-01", language: "fra" }),
    "/fr/compare.html?work=%2Flu-legilux%2Floi-1991-08-10-n3&from=2024-02-01&to=2025-04-01&language=fra", "the address the form writes, which reads back");
  assert.deepEqual({ ...compareOfQuery(compareAddress("en", { identifier: "Loi du 10 août 1991", dateFrom: "2024-02-01", dateTo: "2025-04-01" }).slice("/en/compare.html".length)) },
    { identifier: "Loi du 10 août 1991", date_from: "2024-02-01", date_to: "2025-04-01" });

  const initial = { envelope: twoStates(), path: `/en/compare.html${search}` };
  const html = renderLiveComparePage({ initial });
  assert.match(html, /<section data-answer-state="success">/, "the comparison, without any script");
  assert.ok(html.includes('value="/lu-legilux/loi-1991-08-10-n3"') && html.includes('value="2024-02-01"') && html.includes('value="2025-04-01"'), "the form names what its address compares");
  assert.ok(html.includes("<del>") && html.includes("<ins>"), "and the wording that changed");
  assert.ok(html.includes(`<script type="application/json" id="${INITIAL_ANSWER_ID}">`), "the answer the browser hydrates from");
  const open = `<div id="${LIVE_COMPARE_ROOT}">`;
  const start = html.indexOf(open) + open.length;
  const root = html.slice(start, html.lastIndexOf("</div>", html.indexOf(`<script type="application/json" id="${INITIAL_ANSWER_ID}"`)));
  assert.equal(root, renderToString(liveCompareTree(initial)), "hydration changes nothing");
});

test("an act held only in the publisher's catalogue is said in one sentence on every screen, with its record in the card", () => {
  for (const [label, outcome] of [
    ["dossier", dossierOutcome({ state: "refusal", envelope: RECORD_DOSSIER })],
    ["reading", readingOutcome({ state: "refusal", envelope: RECORD_READING })],
    ["search", searchOutcome({ state: "refusal", envelope: RECORD_SEARCH })],
  ]) {
    assert.equal(outcome.state, "refusal", label);
    assert.equal(outcome.code, "text_not_available", label);
    assert.equal(outcome.card, true, `${label}: the card's rules accept the record (${outcome.sentence})`);
    assert.equal(outcome.sentence, CATALOGUE_RECORD_SENTENCE, label);
    assert.ok(catalogueRecord(outcome.code, outcome.payload), label);
  }
  // Every key the record carries is one the card declares; the payload census holds the record too.
  for (const key of Object.keys(RECORD_DOSSIER.refusal.helpful_payload)) {
    assert.ok(REQUIRED_PAYLOAD.text_not_available.keys.includes(key) || REQUIRED_PAYLOAD.text_not_available.optional.includes(key) || ["what_would_answer", "asserts_absence_of_law"].includes(key), key);
  }
  assert.ok(CATALOGUE_RECORD_KEYS.includes("record_kind"));
  assert.ok(payloads.produced.some((entry) => entry.code === "text_not_available" && entry.payload?.record_kind === "catalogue_only"), "the payload census samples the record");

  const outcome = dossierOutcome({ state: "refusal", envelope: RECORD_DOSSIER });
  const markup = renderToStaticMarkup(h(DossierAnswerView, { outcome }));
  assert.ok(markup.includes('<h2 data-catalogue-record="">loi-1991-08-10-n5</h2>'), "the act, by the address this site gives it");
  assert.ok(markup.includes('<p lang="fr">Loi du 10 août 1991 portant approbation de la Convention de La Haye</p>'), "its title, in its language");
  for (const said of [
    "This act is in the publisher's catalogue, as this site read it no later than 2026-10-01T08:00:00Z, and this site holds none of its text: the publisher licenses every XML and PDF file of its French text under licenceSCL, which this site does not reproduce.",
    "Its type in the publisher's record: LOI (typeDocument).",
    "The publisher's record gives 1991-08-10 as the act's date (dateDocument).",
    "The publisher's record gives 1991-08-23 as its publication date (publicationDate).",
    "The publisher's record places it in Mémorial A no. 58 of 1991 (isPartOf).",
    "Its entry into force in the publisher's record: 1991-08-27 (dateEntryInForce).",
    "The publisher's record gives 2010-01-01 as the act's end-of-force date (dateNoLongerInForce).",
    "Its status in the publisher's record: no-longer-in-force (inForceStatus).",
    "Its historical identifier in the publisher's record: 1991A10580 (historicalLegalId).",
  ]) {
    assert.ok(markup.replaceAll("&#x27;", "'").includes(said), said);
  }
  assert.ok(markup.includes('<a href="/en/lu-legilux/loi-1991-08-10-n5"><code>/lu-legilux/loi-1991-08-10-n5</code></a>'), "its record's page on this site");
  assert.ok(markup.includes('<dd lang="fr">Loi du 10 août 1991'), "the title's row marked in its language");

  // The same card in French, from the reviewed table: one line per fact, each in the page's language.
  const french = reasonView("text_not_available", RECORD_DOSSIER.refusal.helpful_payload, LIVE_CHROME.fr.refusalCard);
  assert.deepEqual(french.map((line) => line.key), ["catalogue.held", "catalogue.type", "catalogue.documentDate", "catalogue.publicationDate", "catalogue.memorial",
    "catalogue.entryIntoForce", "catalogue.endOfForce", "catalogue.status", "catalogue.historicalId"]);
  assert.ok(french[0].text.startsWith("Cet acte figure dans le catalogue de l’éditeur"), french[0].text);
  assert.ok(french.every((line) => !/\{[a-zA-Z]+\}/.test(line.text)), "every placeholder filled");
  // The review's refused forms: no file but the XML and PDF ones is said of, the act's French text is never "son texte", a
  // compared side is named by its date's label (the page does not order the dates), and "n°" takes an ordinary space.
  const tables = JSON.stringify([LIVE_CHROME.fr.compare, LIVE_CHROME.fr.refusalCard.catalogue]);
  for (const refused of ["son texte français", "de son texte", "tous les fichiers de", "les mots supprimés", "version antérieure", "version postérieure", "en entier", "n° "]) {
    assert.ok(!tables.includes(refused), refused);
  }
  const english = JSON.stringify([LIVE_CHROME.en.compare, LIVE_CHROME.en.refusalCard.catalogue]);
  for (const refused of ["every file of its French text", "as its entry-into-force date", "of the earlier version", "of the later version", "each whole"]) {
    assert.ok(!english.includes(refused), refused);
  }

  // A reason the card does not word is refused, so a new one is worded before it is shown.
  assert.throws(() => validateRefusal({ code: "text_not_available", sentence: CATALOGUE_RECORD_SENTENCE, payload: RECORD_DOSSIER.refusal.helpful_payload }) &&
    renderToStaticMarkup(h(RefusalCard, { code: "text_not_available", sentence: CATALOGUE_RECORD_SENTENCE, payload: { ...RECORD_DOSSIER.refusal.helpful_payload, reason: "catalogue_unworded" } })), /words catalogue_licence_scl/);
});
