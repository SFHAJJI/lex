// What a reading at a law's address shows first (`PinnedReading.jsx`; the specification of record's R64 and R67): the
// cited article, under its own title, with the version it is quoted from and the whole act; the banner when the version
// shown is not the latest held, in each publisher's words (R21); and for a permalink refused because its digest is not the
// version held at its date, the notice with the current link. Read from the census envelopes the real handler sent, and
// from the hand-built EU bundle the web tests share.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { LiveReading, PinnedLead, citedArticle, citedTitle, readingTitle, versionShown } from "../.react-build/app.mjs";
import { readingAddressShown, readingPinOf } from "../scripts/initial-answer.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { LIVE_CHROME, entriesOf } from "../scripts/live-chrome.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";
import { EUROPE_SAMPLE, europeBundle, europeEnvelope } from "../scripts/europe-bundle-sample.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "evidence_bundle" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the evidence_bundle envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const PINNED = envelopeOf("a state's permalink at its own date");
// The EU branch fixture (review of #972): the English original wording's permalink, whose next wording (2024-01-01) is held
// in French only.
const LATER_NOT_HELD = envelopeOf("an EU wording's permalink whose next wording's text is not held in its language");
const MISMATCH = envelopeOf("a state's permalink whose digest is not the one held at its date");
const STATE = PINNED.result.value.states[0];
const ARTICLE = STATE.articles[14];
const lead = (outcome, pin) => renderToStaticMarkup(h(PinnedLead, { outcome, pin })).replaceAll("<!-- -->", "");
const success = (envelope) => readingOutcome({ state: "success", envelope });

test("the census's permalink answer is the state the permalink pins, asked by the permalink itself", () => {
  assert.equal(PINNED.result.value.requested_identifier, STATE.permalink, "the page's own request (R64)");
  assert.equal(MISMATCH.refusal.code, "pinned_digest_mismatch");
  assert.equal(MISMATCH.refusal.helpful_payload.current_hash_pinned_url, STATE.permalink, "with the current link");
});

test("what a law's address names for its reading: the permalink's request, or a readable address's at today's date", () => {
  const permalink = readingPinOf(`/en${STATE.permalink}/${ARTICLE.publisher_id}`, PINNED);
  assert.equal(permalink.kind, "state");
  assert.deepEqual({ ...permalink.request }, { identifier: STATE.permalink, date: STATE.applicability_date, language: "fra" }, "asked by the permalink, with no article");
  assert.deepEqual({ ...permalink.form }, { identifier: "/lu-legilux/loi-1991-08-10-n3", date: STATE.applicability_date, language: "fra" }, "the form names the work");
  assert.equal(permalink.anchor, ARTICLE.publisher_id);

  // A readable address is asked at today: the date the answer it carries was asked for, else the day its server answered,
  // else this browser's clock (all in Luxembourg, R13); a page the server rendered and the page hydrated name one date.
  const readable = "/fr/lu-legilux/loi-1991-08-10-n3/fra/art_15";
  assert.equal(readingPinOf(readable, PINNED).request.date, STATE.applicability_date, "the answer's own date");
  const refused = structuredClone(MISMATCH);
  refused.context.freshness.observed_at = "2026-10-15T22:30:00.0000000Z";
  assert.equal(readingPinOf(readable, refused).request.date, "2026-10-16", "the day its server answered, in Luxembourg (summer time)");
  assert.equal(readingPinOf(readable, null, () => "2026-12-31T23:30:00Z").request.date, "2027-01-01", "this browser's clock, in Luxembourg (winter time)");
  assert.deepEqual({ ...readingPinOf(readable, null, () => "2026-10-15T08:00:00Z").request }, { identifier: "/lu-legilux/loi-1991-08-10-n3", date: "2026-10-15", language: "fra" });
  assert.equal(readingPinOf(readable, null, () => "1970-01-01T00:00:00Z"), null, "a clock outside the rule's range names no date, and no reading");
  assert.equal(readingPinOf("/lu-legilux/loi-1991-08-10-n3/fra"), null, "an address under no interface language is sent to one first");
  assert.equal(readingPinOf("/en/lu-legilux/loi-1991-08-10-n3"), null, "a work's address names its dossier");
});

test("the cited article comes first, under its own title, quoted from its version, with the whole act at that version (R64)", () => {
  const outcome = success(PINNED);
  const pin = readingPinOf(`/en${STATE.permalink}/${ARTICLE.publisher_id}`, PINNED);
  const markup = lead(outcome, pin);
  assert.ok(markup.startsWith(`<div data-pinned-lead=""><section class="cited-article" data-cited-article="${ARTICLE.publisher_id}"><h2>${ARTICLE.publisher_id}</h2>`), markup.slice(0, 200));
  assert.ok(markup.includes(`<p data-cited-from="">Quoted from loi-1991-08-10-n3, fra, the state applying from ${STATE.applicability_date}.</p>`));
  assert.ok(markup.includes(`<blockquote lang="fr">${ARTICLE.text}</blockquote>`) || markup.includes('<blockquote lang="fr">'), "its text, in its language");
  assert.ok(markup.includes(ARTICLE.text_sha256), "with the digests a quotation carries");
  assert.ok(markup.includes(`<a href="/en${ARTICLE.article_permalink}"><code>${ARTICLE.article_permalink}</code></a>`), "and its own permalink");
  assert.ok(markup.includes(`<p data-whole-act=""><a href="/en${STATE.permalink}">The whole act at this version</a></p>`));
  assert.ok(!markup.includes("data-changed-since"), "the one state held: nothing newer");
  assert.equal(citedTitle(outcome, pin), `loi-1991-08-10-n3, ${ARTICLE.publisher_id}`);
  assert.equal(readingTitle({ envelope: PINNED, path: `/en${STATE.permalink}/${ARTICLE.publisher_id}` }), `loi-1991-08-10-n3, ${ARTICLE.publisher_id}`);
  assert.equal(readingTitle({ envelope: PINNED, path: `/en${STATE.permalink}` }), "Reading", "no article named: the reading page's title");

  // An article the version does not hold leads nothing; the state stands, and the page names the state.
  const none = readingPinOf(`/en${STATE.permalink}/no-such-article`, PINNED);
  assert.equal(citedArticle(versionShown(outcome.view, none), none), null);
  assert.equal(lead(outcome, none), "");
  assert.equal(lead(outcome, readingPinOf(`/en${STATE.permalink}`, PINNED)), "", "no article named, nothing changed since: nothing first");

  // An article held without text is named first, never quoted empty.
  const withoutText = structuredClone(PINNED);
  const [moved] = withoutText.result.value.states[0].articles.splice(14, 1);
  withoutText.result.value.states[0].articles_without_text.push({ article_identity_sha256: moved.article_identity_sha256, publisher_id: moved.publisher_id, reason: "no_text_tokens" });
  if (moved.validity_conflict) withoutText.result.value.states[0].validity_conflict_count -= 1;
  const held = lead(success(withoutText), pin);
  assert.ok(held.includes(`<p data-cited-from="" data-without-text="">Held without text in loi-1991-08-10-n3, fra, the state applying from ${STATE.applicability_date}.</p>`), held);
  assert.ok(!held.includes("<blockquote"), "no empty quotation");
  assert.ok(!held.includes("Quoted from"), "and never said to be quoted (the French review of 2026-10-16)");
});

test("a version that is not the latest held says so, in its publisher's words, with the act at today's date (R64, R21)", () => {
  const older = structuredClone(PINNED);
  older.result.value.states[0].next_applicability_date = "2025-03-07";
  const pin = readingPinOf(`/en${STATE.permalink}/${ARTICLE.publisher_id}`, older);
  const markup = lead(success(older), pin);
  assert.ok(markup.startsWith(`<div data-pinned-lead=""><p data-changed-since="">A newer version is applicable from 2025-03-07 (publisher). <a href="/en/lu-legilux/loi-1991-08-10-n3/fra/${ARTICLE.publisher_id}">Read this act at today&#x27;s date</a></p>`), markup.slice(0, 300));
  // With no article named, the banner alone comes first, and the act at today's date is the act's readable address.
  const act = lead(success(older), readingPinOf(`/en${STATE.permalink}`, older));
  assert.ok(act.includes('<a href="/en/lu-legilux/loi-1991-08-10-n3/fra">Read this act at today&#x27;s date</a>'), act);
  // At a readable address, the page is the act today already: the banner names the newer version and links nothing.
  const current = lead(success(older), readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra/art_15", older));
  assert.ok(current.includes("A newer version is applicable from 2025-03-07 (publisher).") && !current.includes("today"), current);
  assert.ok(current.includes('<p data-whole-act=""><a href="/en/lu-legilux/loi-1991-08-10-n3/fra">'), "the whole act at its readable address");

  // An EU wording: its next wording's date, never an applicability date; nothing for the latest wording, or for a wording
  // an index with no states table answers only on its own date.
  const europe = europeEnvelope(europeBundle((value) => {
    value.wordings[0].kind = "original_wording";
    value.wordings[0].next_date = "2024-01-01";
    value.wordings[0].next_text_held = true;
  }));
  const europePin = readingPinOf(`/en${EUROPE_SAMPLE.permalink}/001`, europe);
  const europeMarkup = lead(success(europe), europePin);
  assert.ok(europeMarkup.includes('<p data-changed-since="">A later wording of this act is held: the wording of 2024-01-01. <a href="/en/eu-eurlex/32016R0679/eng/001">Read this act at today&#x27;s date</a></p>'), europeMarkup);
  assert.ok(europeMarkup.includes('<h2>001 <span lang="en">Subject-matter and objectives</span></h2>'), "the provision's own title, its heading in the text's language");
  assert.ok(europeMarkup.includes('<p data-whole-act=""><a href="/en/eu-eurlex/32016R0679/2016-04-27--'), "the whole act in that wording");
  assert.ok(europeMarkup.includes("The whole act in this wording"));
  assert.ok(!/appl(?:y|ies|icable)/.test(europeMarkup.replace(/<blockquote[\s\S]*?<\/blockquote>/g, "")), "no Luxembourg date word on an EU wording");
  // The branch fixture's answer, as the platform sent it: the next wording's text is not held in English, so the page says
  // the publisher's record names it, and links no text this site does not hold (review of #972).
  const branch = success(LATER_NOT_HELD);
  assert.equal(branch.state, "success");
  const branchWording = branch.view.wordings[0];
  assert.deepEqual([branchWording.nextDate, branchWording.nextTextHeld], ["2024-01-01", false]);
  const branchPin = readingPinOf(`/en${branchWording.permalink}/${branchWording.articles[4].publisherId}`, LATER_NOT_HELD);
  const branchMarkup = lead(branch, branchPin);
  assert.ok(branchMarkup.includes("<p data-changed-since=\"\">The publisher&#x27;s record names a later wording of this act, of 2024-01-01, whose text this site does not hold in this language.</p>"), branchMarkup.slice(0, 400));
  assert.ok(!branchMarkup.includes("is held: the wording of"), "never said held");
  assert.ok(!branchMarkup.includes("today&#x27;s date"), "no link to the act today, whose text this site does not hold");
  const latest = europeEnvelope(europeBundle((value) => {
    value.wordings[0].kind = "original_wording";
    value.wordings[0].next_date = null;
  }));
  assert.ok(!lead(success(latest), europePin).includes("data-changed-since"));
  assert.ok(!lead(success(europeEnvelope()), europePin).includes("data-changed-since"), "an original wording with no time view says nothing of later ones");
});

test("a permalink refused because its digest is not the version held at its date says so, with the current link (R64)", () => {
  const stale = STATE.permalink.replace(/--[0-9a-f]{64}\//, `--${"0".repeat(64)}/`);
  const outcome = readingOutcome({ state: "refusal", envelope: MISMATCH });
  assert.equal(outcome.state, "refusal");
  assert.equal(outcome.card, true, "the card shows the payload: the digest asked, the digest held, the coordinate and the current link");
  assert.equal(outcome.sentence, "The version this permalink pins is not the one this index holds for its date.");
  const pin = readingPinOf(`/en${stale}/art_15`, MISMATCH);
  assert.equal(
    lead(outcome, pin),
    `<p data-pinned-lead="" data-pin-not-held="">This link pins <code>${stale}</code>, which is not the version this site holds for that date; the version it holds is <a href="/en${STATE.permalink}"><code>${STATE.permalink}</code></a>.</p>`,
  );
  assert.equal(readingAddressShown(null, `${stale}/art_15`, { code: outcome.code, payload: outcome.payload }), STATE.permalink, "the canonical is the current link");
  // An EU wording's refusal says it of a wording; a refusal of any other code says nothing first.
  const europe = { ...outcome, context: { ...outcome.context, publisher: "eu-eurlex" } };
  assert.ok(lead(europe, pin).includes("which is not a wording this site holds for that date; the wording that answers that date on this site is"));
  assert.equal(lead({ ...outcome, code: "text_withheld" }, pin), "");
  assert.equal(lead(outcome, readingPinOf("/en/lu-legilux/loi-1991-08-10-n3/fra/art_15", MISMATCH)), "", "a readable address pins no digest");
});

test("the page at a permalink, as the server renders it, leads with the cited article and keeps the form on the work", () => {
  const markup = renderToStaticMarkup(h(LiveReading, { contract, initial: { envelope: PINNED, path: `/en${STATE.permalink}/${ARTICLE.publisher_id}` } }));
  const leadAt = markup.indexOf("data-pinned-lead");
  assert.ok(leadAt > markup.indexOf("data-live-answer") && leadAt < markup.indexOf('data-answer-state="success"'), "inside the live answer, before the reading");
  assert.ok(markup.includes('value="/lu-legilux/loi-1991-08-10-n3"'), "a reader asks another date of the work, not of the permalink");
  const refused = renderToStaticMarkup(h(LiveReading, { contract, initial: { envelope: MISMATCH, path: `/en${STATE.permalink.replace(/--[0-9a-f]{64}\//, `--${"0".repeat(64)}/`)}` } }));
  assert.ok(refused.indexOf("data-pin-not-held") < refused.indexOf('data-answer-state="refusal"'), "the notice before the card");
  assert.ok(!refused.includes("data-pin-replaced"), "never another version in the pinned one's place");
});

test("what the page says first holds to R21's words in both languages, and the French is reviewed beside the English", () => {
  const keys = ["pinNotHeld", "europePinNotHeld", "citedTitle", "citedFrom", "citedWithoutText", "wholeAct", "europeWholeAct", "newerVersion", "europeNewerWording", "openToday"];
  for (const [language, table] of [["en", LIVE_CHROME.en], ["fr", LIVE_CHROME.fr]]) {
    const entries = new Map(entriesOf(table));
    for (const key of keys) {
      const text = entries.get(`reading.${key}`);
      assert.equal(typeof text, "string", `${language} reading.${key}`);
      assertTimeVocabulary(text, `${language} reading.${key}`);
      const placeholders = (value) => [...value.matchAll(/\{([a-z]+)\}/gi)].map((match) => match[1]).sort();
      assert.deepEqual(placeholders(text), placeholders(new Map(entriesOf(LIVE_CHROME.en)).get(`reading.${key}`)), `${language} reading.${key}: the English's placeholders`);
    }
  }
});
