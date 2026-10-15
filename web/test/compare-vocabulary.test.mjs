// The vocabulary check (the specification of record's R21) holds the Compare card: never "in force", "en vigueur" or
// "applied" of a date or an interval, in any language. The card stopped saying "the same version applied on both dates"
// (the plan's item 0.6): one state on both dates is now "Both dates fall within the same version, applicable from {date}
// (publisher)" for a Luxembourg work whose consolidated state is held, and for the EU (when Compare serves it, the plan's
// item 2.5) "Both dates fall within the same consolidated wording state (from {date})".
//
// An act held only as published (the review of #964) is not one version covering both dates: its one state is the act as
// first published, and its publisher's record can name a consolidated version, or an end-of-force date, between the two
// dates. Both dates selecting it is "Both dates select the one text held, the act as published, applicable from {date}
// (publisher); later versions are not held", and what the publisher dates between the dates is named, from the facts
// alone; the card shows the as-published block, and the reader refuses a note that is not its facts'.
//
// The check is `assertTimeVocabulary` (`publisher-vocabulary.mjs`). It is held to every word the Compare page says of
// its own, in English and in the reviewed French, and to the card as rendered for every census comparison; and it is
// shown failing on purpose: the old note, fed to it as a string and as a rendered card, is refused, and so is every
// variant the review of #964 found it letting through (a French form without "s’", a no-break space, a hyphen, a
// decomposed accent, a modifier-letter apostrophe).

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { CompareAnswerView, CompareView } from "../.react-build/app.mjs";
import { compareOutcome } from "../scripts/live-compare.mjs";
import { readDiff } from "../scripts/compare-answer.mjs";
import { sameTextHeldNote } from "../scripts/as-published.mjs";
import { LIVE_CHROME, entriesOf, fillText } from "../scripts/live-chrome.mjs";
import { FORBIDDEN_TIME_WORDS, SAME_STATE_SENTENCE, assertTimeVocabulary, vocabularyText } from "../scripts/publisher-vocabulary.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));

const OLD_NOTE = "the same version applied on both dates";
const LABEL_SAID = "As published; may have been amended since; no consolidated text.";
const WORK = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3";

// What a card says, its tags taken out.
const text = (markup) => markup.replace(/<[^>]+>/g, " ").replace(/&#x27;|&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, "&").replace(/\s+/g, " ").replace(/ ([.,;:])/g, "$1");

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === "<varies-per-run>") { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

const diffSamples = answers.sampled.filter((entry) => entry.operation === "diff");
/** The answer census's comparison of an act held only as published on two dates its publisher dates versions between. */
const straddled = () => withDigests(structuredClone(diffSamples.find((entry) => entry.scenario.startsWith("an act held only as published")).answer));

test("the check refuses each word R21 forbids, in English and in French, in every spelling, and lets the publisher's own words through", () => {
  for (const said of [
    "The same version applied on both dates.", "in force from 2024-02-01", "En vigueur depuis le 2024-02-01",
    "les versions qui s’appliquaient à ces dates", "la version appliquée", "la loi s'appliquait",
    // The review of #964's probes, each let through before: French forms with "s’y" or without "s’", a no-break space,
    // a decomposed accent, a modifier-letter apostrophe (a letter, so no word boundary), a hyphen.
    "s’y appliquait", "qui s’y appliquaient", "appliquait", "appliquaient", "in force", "en vigueur",
    "appliquée", "sʼappliquait", "in-force",
    // And their neighbours: a narrow no-break space, a non-breaking hyphen, a soft hyphen inside the word, capitals, the
    // simple past.
    "en vigueur", "In‑Force", "app­lied", "APPLIQUÉE", "la loi s’appliqua", "elles s’appliquèrent",
  ]) {
    assert.throws(() => assertTimeVocabulary(said, "a test"), /R21 never says/, said);
  }
  for (const said of [
    "Both dates fall within the same version, applicable from 2024-02-01 (publisher).",
    "Les deux dates relèvent de la même version, applicable à partir du 2024-02-01 (éditeur).",
    "la version applicable à partir du 2024-02-01", "S’applique à partir du", "the state applying from 2024-02-01",
    "an end-of-force date", "une date de fin de vigueur", "applicability",
    // R21's own words, and the present, which says what applies on a date asked, not a past interval.
    "Both dates select the one text held, the act as published, applicable from 2024-02-01 (publisher); later versions are not held.",
    "la version suivante détenue s’applique à partir du 2025-03-07", "plusieurs versions s’appliquent à cette date",
    "appliquant", "applicabilité", "dateNoLongerInForce", "in_force_on", "Je ne peux pas appliquer le droit à votre situation",
  ]) {
    assert.equal(assertTimeVocabulary(said, "a test"), said);
  }
  // What the check reads: one composed, folded spelling.
  assert.equal(vocabularyText("in ‑ force sʼy appliquée"), "in force s’y appliquée");
});

test("the old note, and the slice's same-version note for an act held only as published, are refused: by the check, by the reader, and as a rendered card", () => {
  // Fed to the check as it was said.
  assert.throws(() => assertTimeVocabulary(OLD_NOTE, "the old Compare note"), /says "applied"/);

  // Sent by a platform that still said it, the reader refuses the answer as drift. The census act is held only as
  // published, so the note it must say is the one text held's.
  const sample = diffSamples.find((entry) => entry.answer.comparisons[0].same_state);
  const old = withDigests(structuredClone(sample.answer));
  old.comparisons[0].note = OLD_NOTE;
  assert.throws(() => readDiff(old), /says "the same version applied on both dates" rather than "Both dates select the one text held, the act as published/);
  // The note the slice gave it (the review of #964): one version covering both dates is not what an act held only as
  // published is, and its publisher's record may date a version between them.
  const slice = withDigests(structuredClone(sample.answer));
  slice.comparisons[0].note = `Both dates fall within the same version, applicable from ${slice.comparisons[0].from.applicability_date} (publisher)`;
  assert.throws(() => readDiff(slice), /rather than "Both dates select the one text held/);

  // And a card that printed it, fed to the check, fails it.
  const view = readDiff(withDigests(structuredClone(diffSamples[1].answer)));
  const printed = { ...view, comparisons: view.comparisons.map((comparison) => ({ ...comparison, note: OLD_NOTE })) };
  const markup = renderToStaticMarkup(h(CompareView, { view: printed }));
  assert.ok(text(markup).includes("The same version applied on both dates."), "the card prints the note it is given");
  assert.throws(() => assertTimeVocabulary(text(markup), "the Compare card"), /says "applied"/);
});

test("every word the Compare page says of its own passes the check, in English and in French", () => {
  const between = { after: "2024-02-10", onOrBefore: "2024-10-31" };
  for (const language of ["en", "fr"]) {
    const copy = LIVE_CHROME[language].compare;
    for (const [path, said] of entriesOf(copy)) assertTimeVocabulary(said, `${language} compare.${path}`);
    for (const key of ["sameVersion", "sameTextHeld", "sameTextHeldOfSeveral"]) {
      assertTimeVocabulary(fillText(copy[key], { from: "2024-02-01" }), `${language} compare.${key}`);
    }
    assertTimeVocabulary(fillText(copy.betweenEndOfForce, { date: "2024-09-30", ...between }), `${language} compare.betweenEndOfForce`);
    for (const form of ["one", "other"]) assertTimeVocabulary(fillText(copy.betweenNamed[form], { count: 2, ...between }), `${language} compare.betweenNamed.${form}`);
  }
  assert.equal(fillText(LIVE_CHROME.en.compare.sameVersion, { from: "2024-02-01" }), `${SAME_STATE_SENTENCE.publisher_applicability("2024-02-01")}.`,
    "the page's English is the platform's note, as a sentence");
  // With nothing dated between the dates, the page's English for an act held only as published is the platform's note too.
  const nothing = { after: "2024-02-01", onOrBefore: "2024-02-01", consolidations: [], endOfForce: [] };
  assert.equal(fillText(LIVE_CHROME.en.compare.sameTextHeld, { from: "2024-02-01" }), `${sameTextHeldNote("2024-02-01", true, nothing)}.`);
  assert.equal(fillText(LIVE_CHROME.en.compare.sameTextHeldOfSeveral, { from: "2024-02-01" }), `${sameTextHeldNote("2024-02-01", false, nothing)}.`);
  // The EU's form, for when Compare serves EU versions: its consolidated wording state, never a Luxembourg word.
  const europe = SAME_STATE_SENTENCE.official_consolidation_state("2016-04-27");
  assert.equal(europe, "Both dates fall within the same consolidated wording state (from 2016-04-27)");
  assertTimeVocabulary(europe, "the EU same-state sentence");
  assert.doesNotMatch(europe, /\bversion\b|applicab|\(publisher\)/);
});

test("the Compare card passes the check for every census comparison, and one state says what the work is held as", () => {
  const entries = census.envelopes.filter((entry) => entry.operation === "diff" && entry.envelope.result);
  assert.ok(entries.length > 0, "the census holds a comparison");
  for (const entry of entries) {
    const outcome = compareOutcome({ state: "success", envelope: entry.envelope });
    assert.equal(outcome.state, "success", outcome.sentence);
    const said = text(renderToStaticMarkup(h(CompareAnswerView, { outcome })));
    assertTimeVocabulary(said, `the Compare card for ${entry.scenario}`);
    // The census act is held only as published: the card shows the label, and its one state is the one text held.
    assert.ok(outcome.view.asPublished !== null, entry.scenario);
    assert.ok(said.includes(LABEL_SAID), `${entry.scenario}: the card shows the as-published label`);
    for (const comparison of outcome.view.comparisons.filter((candidate) => candidate.sameState)) {
      assert.equal(comparison.onlyTextHeld, true, entry.scenario);
      assert.ok(said.includes(`Both dates select the one text held, the act as published, applicable from ${comparison.from.applicabilityDate} (publisher); later versions are not held.`), entry.scenario);
      assert.ok(!said.includes("same version"), `${entry.scenario}: never one version covering both dates`);
    }
  }
  // Every comparison of the answer census too: the two-state one keeps the platform's own note.
  for (const entry of diffSamples) {
    assertTimeVocabulary(text(renderToStaticMarkup(h(CompareView, { view: readDiff(withDigests(structuredClone(entry.answer))) }))), `the Compare card for ${entry.scenario}`);
  }
  assert.ok(FORBIDDEN_TIME_WORDS.test(OLD_NOTE));
});

test("an act held only as published compared across versions its publisher dates: the one text held, and each version and the end-of-force date named (the review of #964)", () => {
  const answer = straddled();
  const [comparison] = answer.comparisons;
  // What the real handler said, on a fixture whose publisher's record dates three consolidated versions and an end of force.
  assert.equal(comparison.note,
    "Both dates select the one text held, the act as published, applicable from 2024-02-01 (publisher); later versions are not held; the " +
    "publisher's record names 2 consolidated versions of it whose applicability dates fall after 2024-02-10 and on or before 2024-10-31, " +
    "applicable from 2024-03-11 to 2024-06-24 and from 2024-06-25 to 2024-09-29 (publisher); the publisher's record gives 2024-09-30 as the " +
    "act's end-of-force date (dateNoLongerInForce), which falls after 2024-02-10 and on or before 2024-10-31");
  const view = readDiff(answer);
  assert.equal(view.comparisons[0].onlyTextHeld, true);
  assert.deepEqual(view.laterVersionsBetween.consolidations.map((entry) => entry.legalResourceIri), [`${WORK}/consolide/20240311`, `${WORK}/consolide/20240625`]);
  assert.deepEqual(view.laterVersionsBetween.endOfForce, ["2024-09-30"]);
  assert.equal(view.asPublished.laterVersions.consolidations.length, 3, "the version before the earlier date is the as-published block's, not named between the dates");

  const said = text(renderToStaticMarkup(h(CompareView, { view })));
  for (const expected of [
    LABEL_SAID,
    "Both dates select the one text held, the act as published, applicable from 2024-02-01 (publisher); later versions are not held.",
    "The publisher's record names, for this act, 2 consolidated versions whose applicability dates fall after 2024-02-10 and on or before 2024-10-31:",
    `${WORK}/consolide/20240311, applicable from 2024-03-11 to 2024-06-24 (publisher)`,
    `${WORK}/consolide/20240625, applicable from 2024-06-25 to 2024-09-29 (publisher)`,
    "The publisher's record gives 2024-09-30 as this act's end-of-force date, which falls after 2024-02-10 and on or before 2024-10-31.",
  ]) {
    assert.ok(said.includes(expected), `the card says ${expected}\n${said}`);
  }
  assert.ok(!said.includes("same version"), "never one version covering both dates");
  // What the card says of the two dates (the label above it says the act "may have been amended since", which is R76's).
  const ofTheDates = said.slice(said.indexOf("Both dates select"), said.indexOf("Wording_sha256"));
  assert.ok(ofTheDates.length > 200, ofTheDates);
  assert.doesNotMatch(ofTheDates, /\b(?:changed|unchanged|amended|modified)\b/, "facts only: nothing is said of whether the text changed");
  assertTimeVocabulary(said, "the Compare card of an act held only as published");
  // The French page's sentences for the same facts, in the reviewed French, pass the check too.
  const fr = LIVE_CHROME.fr.compare;
  for (const said of [
    fillText(fr.sameTextHeld, { from: "2024-02-01" }),
    fillText(fr.betweenNamed.other, { count: 2, after: "2024-02-10", onOrBefore: "2024-10-31" }),
    fillText(fr.betweenEndOfForce, { date: "2024-09-30", after: "2024-02-10", onOrBefore: "2024-10-31" }),
  ]) {
    assertTimeVocabulary(said, "the French Compare card");
  }

  // The reader refuses an answer whose note, or whose facts between the dates, are not its facts'.
  const mutate = (change) => { const copy = straddled(); change(copy); return copy; };
  const base = "Both dates select the one text held, the act as published, applicable from 2024-02-01 (publisher); later versions are not held";
  for (const [change, reason] of [
    [(a) => { a.comparisons[0].note = base; }, /rather than "Both dates select the one text held/],
    [(a) => { a.comparisons[0].note = comparison.note.replace("the one text held", "the same version"); }, /rather than/],
    [(a) => { a.later_version_facts_between_dates.consolidations.pop(); }, /are not the consolidated versions as_published dates/],
    [(a) => { a.later_version_facts_between_dates.consolidations.reverse(); }, /are not the consolidated versions as_published dates/],
    [(a) => { a.later_version_facts_between_dates.end_of_force = []; }, /is not the end-of-force dates as_published gives/],
    [(a) => { a.later_version_facts_between_dates.after = "2024-02-01"; }, /the dates asked make it 2024-02-10 and 2024-10-31/],
    [(a) => { a.later_version_facts_between_dates = null; }, /does not carry after/],
    [(a) => { a.as_published = null; }, /is given for a work that is not held only as published/],
    // "The one text held" where the language holds another state, from a date the answer shows.
    [(a) => { a.comparisons[0].from.next_applicability_date = "2025-01-01"; a.comparisons[0].to.next_applicability_date = "2025-01-01"; }, /says the one text held, and the language holds another state, from 2025-01-01/],
  ]) {
    assert.throws(() => readDiff(mutate(change)), reason);
  }
  // Said as the same text held, that answer is read, and the card says so.
  const several = mutate((a) => {
    a.comparisons[0].from.next_applicability_date = "2025-01-01";
    a.comparisons[0].to.next_applicability_date = "2025-01-01";
    a.comparisons[0].note = comparison.note.replace("the one text held", "the same text held").replace("later versions are not held", "no consolidated version is held");
  });
  const severalView = readDiff(several);
  assert.equal(severalView.comparisons[0].onlyTextHeld, false);
  assert.ok(text(renderToStaticMarkup(h(CompareView, { view: severalView }))).includes("Both dates select the same text held, the act as published, applicable from 2024-02-01 (publisher); no consolidated version is held."));

  // A work whose consolidated state is held keeps one version's clock, and shows neither the label nor the facts between.
  const consolidated = mutate((a) => {
    a.as_published = null;
    a.later_version_facts_between_dates = null;
    a.comparisons[0].note = SAME_STATE_SENTENCE.publisher_applicability("2024-02-01");
  });
  const plain = text(renderToStaticMarkup(h(CompareView, { view: readDiff(consolidated) })));
  assert.ok(plain.includes("Both dates fall within the same version, applicable from 2024-02-01 (publisher)."));
  assert.ok(!plain.includes(LABEL_SAID) && !plain.includes("The publisher's record names"));
});
