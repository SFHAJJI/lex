// The V3 search readers, held to the answers the real handler sent.
//
// The answers come from `schemas/v3-platform/answer-samples.json` (operation `search`), captured
// by driving the real handler on the fixture mounts (Luxembourg's, and the GDPR's for an EU search
// in one work), so each reader is written against what the platform sends and a change to that
// shape fails here before any page renders it. Each rule an answer states about itself is then
// broken once, and the reader must refuse the broken answer with that rule's reason.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import {
  LANE_MATCH_REASON,
  NOT_OPENED_REASONS,
  SEARCHED_AS_WORDS_REASONS,
  OPENED_ADDRESS_KINDS,
  REFERENCE_DATE_SOURCES,
  REFERENCE_KINDS,
  REFERENCE_OUTCOMES,
  SEARCH_LANES,
  WORK_RESOLUTION_OUTCOMES,
  escapeProvision,
  readEuropeSearch,
  readReferenceAnswer,
  readRefusalRecognition,
  readSearch,
  readSearchAnswer,
} from "../scripts/search-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

/** Whether a captured search answer is an exact reference's (it opens an act, or says why not) rather than a word search's. */
function isReferenceAnswer(answer) {
  return ["opens", "not_opened"].includes(answer.reference?.outcome);
}

async function capturedRows(publisher, { references = false } = {}) {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  const rows = parsed.sampled.filter((sample) => sample.operation === "search" && sample.answer.publisher === publisher &&
    isReferenceAnswer(sample.answer) === references);
  for (const row of rows) assert.equal(row.object_type, "quote");
  return rows.map((row) => withDigests(row.answer));
}

async function capturedAnswers() {
  const rows = await capturedRows("lu-legilux");
  assert.equal(rows.length, 5, "the census holds the five Luxembourg search answers this reader is written against");
  const [both, paged, next, relaxed, none] = rows;
  return { both, paged, next, relaxed, none };
}

async function capturedEuropeAnswers() {
  const rows = await capturedRows("eu-eurlex");
  assert.equal(rows.length, 4, "the census holds the four EU search answers this reader is written against");
  const [both, paged, next, none] = rows;
  return { both, paged, next, none };
}

/** Fills the fields the census normalises, and only those, each with its own digest. */
function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") {
    return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  }
  if (node === PLACEHOLDER) {
    counter.n += 1;
    return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64);
  }
  return node;
}

function mutate(answer, change) {
  const copy = structuredClone(answer);
  change(copy);
  return copy;
}

test("the three captured answers read: both lanes, a truncated page with its cursor, and no hit", async () => {
  const { both, paged, none } = await capturedAnswers();

  const view = readSearch(both);
  assert.equal(view.query, "assemblée générale");
  assert.deepEqual(view.terms, ["assemblée", "générale"]);
  assert.deepEqual(view.hits.map((hit) => hit.lane), ["strict", "strict", "strict", "strict", "relaxed"]);
  assert.deepEqual(view.hits.map((hit) => hit.matchReason), [...Array(4).fill("exact_phrase"), "all_terms"]);
  assert.equal(view.population.strictHits + view.population.relaxedHits, view.hits.length);
  assert.equal(view.truncated, false);
  assert.equal(view.continueAfter, null);
  for (const hit of view.hits) {
    assert.equal(hit.permalink, `/lu-legilux/${hit.workKey}/${hit.applicabilityDate}--${hit.stateSha256}/${hit.language}`);
  }

  const page = readSearch(paged);
  assert.equal(page.hits.length, 1);
  assert.equal(page.truncated, true);
  assert.equal(page.continueAfter, `strict.${page.hits[0].stateSha256}.${page.hits[0].articleIdentitySha256}`);
  assert.deepEqual(page.hits[0], view.hits[0], "the first page of one is the first hit of the whole answer");

  const empty = readSearch(none);
  assert.equal(empty.hits.length, 0);
  assert.equal(empty.population.strictHits + empty.population.relaxedHits, 0);
  assert.equal(empty.searchableTextHeld, true, "no hit is an answer over held text, not an absence of text");
});

test("the page a cursor leads to and a search in one lane read, though the population is the whole result's", async () => {
  const { both, paged, next, relaxed } = await capturedAnswers();

  const rest = readSearch(next);
  assert.equal(rest.after, paged.continue_after, "the census followed the first page's own cursor");
  assert.equal(rest.truncated, false);
  assert.equal(rest.hits.length, 4);
  assert.equal(rest.population.strictHits + rest.population.relaxedHits, 5, "the population still counts the cursor's hit");
  assert.deepEqual([readSearch(paged).hits[0], ...rest.hits], [...readSearch(both).hits], "two pages are the whole result, in order");

  const one = readSearch(relaxed);
  assert.equal(one.mode, "relaxed");
  assert.equal(one.population.strictHits, null, "the strict lane was not scanned, so it is not counted");
  assert.equal(one.population.relaxedHits, 5);
  assert.ok(one.hits.every((hit) => hit.lane === "relaxed" && hit.matchReason === "all_terms"));

  const view = readSearch(both);
  assert.deepEqual(view.ambiguousWorks, []);
  assert.equal(view.workResolution.outcome, "no_titles_held", "the fixture holds no titles, and the view says so");
  assert.equal(view.workResolution.work, null);
});

test("a dated search names the works it could not place, and a resolved work is carried whole", async () => {
  const { both } = await capturedAnswers();
  const work = both.hits[0].work_key;
  const state = (date, digit) => `/lu-legilux/other-work/${date}--${digit.repeat(64)}/fra`;
  const dated = mutate(both, (a) => {
    a.requested_date = "2024-02-01";
    a.ambiguous_works = [{ work_key: "other-work", reason: "ambiguous_version", candidates: [state("2023-01-01", "a"), state("2023-01-01", "b")] }];
  });
  assert.deepEqual(readSearch(dated).ambiguousWorks, [{ workKey: "other-work", candidates: [state("2023-01-01", "a"), state("2023-01-01", "b")] }]);

  const card = { work_identifier: `/lu-legilux/${work}`, expressions: ["http://x/fr"], languages: ["fra"], matched_title: "Loi", matched_title_language: "fra", match_reason: "title_exact" };
  const resolved = mutate(both, (a) => { a.work_resolution = { retrieval_lane: "r1_work_discovery", outcome: "one_work", work: card, candidates: null }; });
  assert.equal(readSearch(resolved).workResolution.work.matchedTitle, "Loi");

  const refused = [
    ["an ambiguous work on an undated search", mutate(dated, (a) => { a.requested_date = null; }), /only on a date/],
    ["an ambiguous work with hits", mutate(dated, (a) => { a.ambiguous_works[0].work_key = work; a.ambiguous_works[0].candidates = [`/lu-legilux/${work}/2023-01-01--${"a".repeat(64)}/fra`, `/lu-legilux/${work}/2023-01-01--${"b".repeat(64)}/fra`]; }), /contributes hits/],
    ["a candidate of another work", mutate(dated, (a) => { a.ambiguous_works[0].candidates[1] = `/lu-legilux/elsewhere/2023-01-01--${"b".repeat(64)}/fra`; }), /not a state of other-work/],
    ["a candidate after the date", mutate(dated, (a) => { a.ambiguous_works[0].candidates[1] = state("2025-01-01", "b"); }), /applying by 2024-02-01/],
    ["one candidate", mutate(dated, (a) => { a.ambiguous_works[0].candidates.pop(); }), /at least 2/],
    ["another reason", mutate(dated, (a) => { a.ambiguous_works[0].reason = "no_version_for_date"; }), /is ambiguous_version/],
    ["ambiguous works dropped", mutate(both, (a) => { delete a.ambiguous_works; }), /does not carry ambiguous_works/],
    ["work resolution dropped", mutate(both, (a) => { delete a.work_resolution; }), /does not carry work_resolution/],
    ["an outcome outside the vocabulary", mutate(both, (a) => { a.work_resolution.outcome = "guessed"; }), /is not one of/],
    ["one work without its card", mutate(resolved, (a) => { a.work_resolution.work = null; }), /exactly when the outcome is one_work/],
    ["a card for no match", mutate(resolved, (a) => { a.work_resolution.outcome = "no_title_match"; }), /exactly when the outcome is one_work/],
    ["resolution run although an identifier was given", mutate(both, (a) => { a.requested_identifier = `/lu-legilux/${work}`; }), /exactly when no identifier is given/],
  ];
  for (const [what, answer, reason] of refused) {
    assert.throws(() => readSearch(answer), reason, what);
  }
});

test("each rule the answer states about itself is refused when broken, with that rule's reason", async () => {
  const { both, paged, next, relaxed } = await capturedAnswers();
  const cases = [
    ["a lane the index does not hold", both, (a) => { a.hits[0].lane = "bm25"; }, /the index holds strict and relaxed/],
    ["a reason that is not its lane's", both, (a) => { a.hits[0].match_reasons = ["all_terms"]; }, /matched by exact_phrase and nothing else/],
    ["two reasons", both, (a) => { a.hits[4].match_reasons = ["all_terms", "exact_phrase"]; }, /matched by all_terms and nothing else/],
    ["relaxed before strict", both, (a) => { a.hits.reverse(); }, /relaxed never outranks strict/],
    ["a permalink to another state", both, (a) => { a.hits[1].resolve.identifier = a.hits[1].resolve.identifier.replace(/[0-9a-f](\/[a-z]{3})$/, "0$1"); }, /not to the state it names/],
    ["a permalink in another language", both, (a) => { a.hits[1].resolve.identifier = a.hits[1].resolve.identifier.replace(/\/fra$/, "/deu"); }, /not to the state it names/],
    ["one article twice", both, (a) => { a.hits[1] = structuredClone(a.hits[0]); }, /twice/],
    ["more hits than the limit", both, (a) => { a.limit = 2; }, /against a limit of 2/],
    ["a truncated page without its cursor", paged, (a) => { a.continue_after = null; }, /exactly when it is truncated/],
    ["a cursor on a whole page", both, (a) => { a.continue_after = "strict.x.y"; }, /exactly when it is truncated/],
    ["a cursor that is not the last hit", paged, (a) => { a.continue_after = a.continue_after.replace("strict.", "relaxed."); }, /not the last hit/],
    ["a population that does not add up", both, (a) => { a.population.strict_hits = 3; }, /counts 4 hits and the untruncated first page holds 5/],
    ["a truncated page from a population no larger", paged, (a) => { a.population.strict_hits = 1; a.population.relaxed_hits = 0; }, /at least 2, not 1/],
    ["a page after a cursor from a population no larger", next, (a) => { a.population.strict_hits = 3; }, /after a cursor comes from a population of at least 5, not 4/],
    ["a cursor that is not one", next, (a) => { a.requested_after = "page-2"; }, /is not a cursor/],
    ["a lane not asked for, counted", relaxed, (a) => { a.population.strict_hits = 0; }, /null rather than zero/],
    ["the lane asked for, not counted", relaxed, (a) => { a.population.relaxed_hits = null; }, /population.relaxed_hits is null rather than a count/],
    ["a strict hit in a relaxed-only search", relaxed, (a) => { a.hits[0].lane = "strict"; a.hits[0].match_reasons = ["exact_phrase"]; }, /serves only relaxed hits/],
    ["a mode that is not a lane", relaxed, (a) => { a.requested_mode = "bm25"; }, /not a lane the index holds/],
    ["another publisher's answer", both, (a) => { a.publisher = "eu-eurlex"; }, /Luxembourg \(lu-legilux\) search answer only/],
    ["a missing member", both, (a) => { delete a.hits[2].publisher_id; }, /hit 3 does not carry publisher_id/],
    ["a digest the census left unfilled", both, (a) => { a.corpus_sha256 = PLACEHOLDER; }, /corpus_sha256 is not a SHA-256 digest/],
    ["a date that is not one", both, (a) => { a.hits[0].applicability_date = "2024-02-30"; }, /not a calendar date/],
    ["no terms", both, (a) => { a.terms = []; }, /at least one term/],
  ];
  for (const [what, base, change, reason] of cases) {
    assert.throws(() => readSearch(mutate(base, change)), reason, what);
  }
});

test("the pre-V3 search shape is not read as a V3 answer", () => {
  const retired = {
    query: "loyer",
    hits: [{ lex_id: "lu-legilux:x", provision_num: "Art. 1", match_reasons: ["keyword"] }],
    row_set: { returned: 1 },
  };
  assert.throws(() => readSearch(retired), /does not carry publisher/);
});

test("the lanes, their reasons and the work-resolution outcomes are the platform's closed vocabularies", () => {
  assert.deepEqual(SEARCH_LANES, ["strict", "relaxed"]);
  assert.deepEqual(LANE_MATCH_REASON, { strict: "exact_phrase", relaxed: "all_terms" });
  assert.deepEqual(WORK_RESOLUTION_OUTCOMES, ["not_run_identifier_given", "no_titles_held", "no_title_match", "one_work", "several_candidates"]);
});

test("the four captured EU answers read: one work's pinned wording, both lanes, a page and the page its cursor leads to, and no hit", async () => {
  const { both, paged, next, none } = await capturedEuropeAnswers();

  const view = readEuropeSearch(both);
  assert.equal(view.publisher, "eu-eurlex");
  assert.equal(view.identifier, "32016R0679");
  assert.equal(view.language, "eng");
  assert.equal(view.wording.celex, "32016R0679");
  assert.match(view.wording.permalink, /^\/eu-eurlex\/32016R0679\/\d{4}-\d{2}-\d{2}--[0-9a-f]{64}\/eng$/);
  assert.equal(view.wording.permalink, `/eu-eurlex/32016R0679/${view.wording.wordingDate}--${view.wording.wordingSha256}/eng`);
  assert.deepEqual(view.hits.map((hit) => hit.lane), ["strict", "strict", "relaxed"]);
  assert.deepEqual(view.hits.map((hit) => hit.heading), ["Article 26", "Article 36", "Article 47"]);
  for (const hit of view.hits) {
    assert.equal(hit.permalink, `${view.wording.permalink}/${hit.publisherId}`, "each hit's permalink is the wording's with its provision as a last segment");
    assert.equal(hit.wordingDate, view.wording.wordingDate);
    assert.equal(hit.coordinate, `${hit.publisherExpressionIri}#lex-provision=${hit.publisherId}`);
  }
  assert.equal(view.workResolution.outcome, "not_run_identifier_given", "an EU search names its work");
  assert.equal(view.population.worksWithHits, 1);
  assert.equal(view.consolidationsHeld, false);
  assert.ok(view.notHeld.some((row) => row.item === "corrigenda_applied"), "the answer says corrigenda are not applied, and the view keeps it");
  assert.match(view.dateSemantics, /not a publication, entry-into-force, application or consolidation date/);

  const page = readEuropeSearch(paged);
  assert.equal(page.truncated, true);
  assert.equal(page.continueAfter, `strict.${page.hits[0].articleIdentitySha256}`, "an EU cursor names the lane and the article");
  const rest = readEuropeSearch(next);
  assert.equal(rest.after, paged.continue_after, "the census followed the first page's own cursor");
  assert.deepEqual([page.hits[0], ...rest.hits], [...view.hits], "two pages are the whole result, in order");

  const empty = readEuropeSearch(none);
  assert.equal(empty.hits.length, 0);
  assert.equal(empty.searchableTextHeld, true, "no hit is an answer over held text");
  assert.equal(empty.wording.permalink, view.wording.permalink, "a no-hit answer still names the wording it searched");

  assert.deepEqual(readSearchAnswer(both), view, "readSearchAnswer sends an EU answer to the EU reader");
  const luxembourg = (await capturedAnswers()).both;
  assert.deepEqual(readSearchAnswer(luxembourg), readSearch(luxembourg), "and a Luxembourg answer to the Luxembourg reader");
});

test("an EU provision is escaped as the platform escapes it, in the coordinate and in the permalink", async () => {
  assert.equal(escapeProvision("001"), "001");
  assert.equal(escapeProvision("art. 1(a)*!'~"), "art.%201%28a%29%2A%21%27~", "RFC 3986: only the unreserved characters stay");
  const { both } = await capturedEuropeAnswers();
  const escaped = mutate(both, (a) => {
    const hit = a.hits[0];
    hit.publisher_id = "26(a)";
    hit.resolve.identifier = `${hit.publisher_expression_id}#lex-provision=26%28a%29`;
    hit.permalink = `${a.pinned_wording.permalink}/26%28a%29`;
  });
  assert.equal(readEuropeSearch(escaped).hits[0].permalink.split("/").at(-1), "26%28a%29");
  assert.throws(() => readEuropeSearch(mutate(escaped, (a) => { a.hits[0].permalink = `${a.pinned_wording.permalink}/26(a)`; })), /not the pinned wording's with its provision/);
  assert.throws(() => readEuropeSearch(mutate(escaped, (a) => { a.hits[0].permalink = `${a.pinned_wording.permalink}#26%28a%29`; })), /not the pinned wording's with its provision/, "a provision after # is the shape before R64");
  assert.throws(() => readEuropeSearch(mutate(escaped, (a) => { a.hits[0].resolve.identifier = `${a.hits[0].publisher_expression_id}#lex-provision=26(a)`; })), /not to the provision it names/);
});

test("each rule an EU answer states about itself is refused when broken, with that rule's reason", async () => {
  const { both, paged, next, none } = await capturedEuropeAnswers();
  assert.equal(readEuropeSearch(mutate(none, (a) => { a.pinned_wording = null; })).wording, null, "a no-hit answer that pins no wording still reads: it shows no hit");
  const flipLast = (text) => text.replace(/[0-9a-f]$/, (digit) => (digit === "0" ? "1" : "0"));
  // A permalink's digest is followed by its language (R64), so the digit flipped is the digest's last.
  const flipDigest = (permalink) => permalink.replace(/([0-9a-f])(\/[a-z]{3})$/, (_, digit, language) => `${digit === "0" ? "1" : "0"}${language}`);
  const cases = [
    ["a date", both, (a) => { a.requested_date = "2020-01-01"; }, /names no date/],
    ["no work named", both, (a) => { a.requested_identifier = null; }, /requested_identifier is not a value/],
    ["hits and no pinned wording", both, (a) => { a.pinned_wording = null; }, /hit 1 pins no wording/],
    ["a pinned permalink to another digest", both, (a) => { a.pinned_wording.permalink = flipDigest(a.pinned_wording.permalink); }, /not the wording it names/],
    ["a pinned permalink to another date", both, (a) => { a.pinned_wording.wording_date = "2016-05-04"; }, /not the wording it names/],
    ["a pinned permalink in another language", both, (a) => { a.pinned_wording.permalink = a.pinned_wording.permalink.replace(/\/eng$/, "/fra"); }, /in fra, and the search was asked in eng/],
    ["a pinned permalink of another grammar", both, (a) => { a.pinned_wording.permalink = `/lu-legilux/32016R0679/2016-04-27--${a.pinned_wording.wording_sha256}/eng`; }, /not an EU wording permalink/],
    ["a pinned permalink of the shape before R64", both, (a) => { a.pinned_wording.permalink = `/eu-eurlex/32016R0679/eng/2016-04-27--${a.pinned_wording.wording_sha256}`; }, /not an EU wording permalink/],
    ["a hit permalink to another provision", both, (a) => { a.hits[0].permalink = a.hits[0].permalink.replace(/\/[^/]*$/, "/999"); }, /not the pinned wording's with its provision/],
    ["a hit of another act", both, (a) => { a.hits[1].celex = "32016L0680"; }, /not in the wording the answer pins/],
    ["a hit of another wording date", both, (a) => { a.hits[1].wording_date = "2016-05-04"; }, /not in the wording the answer pins/],
    ["a hit in another language", both, (a) => { a.hits[2].language = "fra"; }, /one language asked \(eng\)/],
    ["a hit resolving to another provision", both, (a) => { a.hits[0].resolve.identifier = a.hits[0].resolve.identifier.replace(/=.*$/, "=999"); }, /not to the provision it names/],
    // The review of #853's reproduction: the second hit's work IRI changed, everything else (the population's
    // one work included) left as it was.
    ["hits of two works", both, (a) => { a.hits[1].publisher_work_id = flipLast(a.hits[1].publisher_work_id); }, /the hits are in 2 works, and an EU search is in the one work named/],
    ["hits in two expressions", both, (a) => { a.hits[1].publisher_expression_id += "-other"; a.hits[1].resolve.identifier = `${a.hits[1].publisher_expression_id}#lex-provision=${a.hits[1].publisher_id}`; }, /in 2 expressions/],
    ["one article twice", both, (a) => { a.hits[1] = structuredClone(a.hits[0]); }, /of the wording twice/],
    ["relaxed before strict", both, (a) => { a.hits.reverse(); }, /relaxed never outranks strict/],
    ["a population that does not add up", both, (a) => { a.population.strict_hits = 1; }, /counts 2 hits and the untruncated first page holds 3/],
    ["a cursor that is not the last hit", paged, (a) => { a.continue_after = a.continue_after.replace("strict.", "relaxed."); }, /not the last hit/],
    ["a Luxembourg-shaped cursor", next, (a) => { a.requested_after = `strict.${"a".repeat(64)}.${"b".repeat(64)}`; }, /is not a cursor \(lane\.article\)/],
    ["hits in two works", both, (a) => { a.population.works_with_hits = 2; }, /2 works with hits, and an EU search is in one work/],
    ["a no-hit answer with a work", none, (a) => { a.population.works_with_hits = 1; }, /1 works with hits/],
    ["a scope of another work", both, (a) => { a.population.scope.identifier = "32016L0680"; }, /population.scope.identifier/],
    ["a scope with a date", both, (a) => { a.population.scope.date = "2020-01-01"; }, /population.scope.date/],
    ["an ambiguous work", both, (a) => { a.ambiguous_works = [{ work_key: "x", reason: "ambiguous_version", candidates: [] }]; }, /an EU search names none/],
    ["a work resolved from the query", both, (a) => { a.work_resolution.outcome = "no_title_match"; }, /exactly when no identifier is given/],
    ["the date's meaning dropped", both, (a) => { delete a.date_semantics; }, /does not carry date_semantics/],
    ["a reason something is not held, dropped", both, (a) => { delete a.not_held[0].reason; }, /not_held\[0\] does not carry reason/],
    ["another publisher's answer", both, (a) => { a.publisher = "lu-legilux"; }, /EU \(eu-eurlex\) search answer only/],
  ];
  for (const [what, base, change, reason] of cases) {
    assert.throws(() => readEuropeSearch(mutate(base, change)), reason, what);
  }
});

// Exact references first (the plan's item 4.2): the answers the census captured for a query the platform read as an
// exact reference, in the order the census drives them: Luxembourg's dossier, a permalink at a date, the readable
// address with no date, a date that names no day; the EU's dossier and its readable address.
test("the captured reference answers read: the dossier, a permalink at the date, the readable address, and nothing opened", async () => {
  const [dossier, dated, undated, notADay] = await capturedRows("lu-legilux", { references: true });
  const [europeDossier, europeReadable] = await capturedRows("eu-eurlex", { references: true });

  const opened = readSearchAnswer(dossier);
  assert.equal(opened.kind, "reference");
  assert.equal(opened.outcome, "opens");
  assert.equal(opened.referenceKind, "citation");
  assert.equal(opened.opens.page, "dossier");
  assert.equal(opened.opens.addressKind, "dossier");
  assert.match(opened.opens.address, /^\/lu-legilux\/[a-z0-9_-]+$/);
  assert.equal(opened.recognised.act.form, "type_and_date");
  assert.equal(opened.recognised.article, null);
  assert.equal(opened.recognised.date, null, "a dossier opens at no date");

  const atDate = readSearchAnswer(dated);
  assert.equal(atDate.opens.addressKind, "permalink");
  assert.match(atDate.opens.address, /^\/lu-legilux\/[a-z0-9_-]+\/\d{4}-\d{2}-\d{2}--[0-9a-f]{64}\/fra\/art_15$/);
  assert.equal(atDate.recognised.article.number, "15");
  assert.deepEqual({ ...atDate.recognised.date }, { asTyped: "15 mars 2024", date: "2024-03-15", source: "citation" });

  const today = readSearchAnswer(undated);
  assert.equal(today.opens.addressKind, "readable");
  assert.match(today.opens.address, /^\/lu-legilux\/[a-z0-9_-]+\/fra\/art_15$/);
  assert.equal(today.recognised.date.source, "today_in_luxembourg");
  assert.equal(today.recognised.date.asTyped, null);

  const nothing = readSearchAnswer(notADay);
  assert.equal(nothing.outcome, "not_opened");
  assert.equal(nothing.opens, null);
  assert.equal(nothing.reason, "date_not_a_calendar_date");
  assert.deepEqual({ ...nothing.recognised.date }, { asTyped: "31 février 2024", date: null, source: "citation" });

  const europe = readSearchAnswer(europeDossier);
  assert.equal(europe.publisher, "eu-eurlex");
  assert.equal(europe.opens.address, "/eu-eurlex/32016R0679");
  assert.equal(europe.recognised.act.identifier, "Regulation (EU) 2016/679 (CELEX 32016R0679)");
  const provision = readSearchAnswer(europeReadable);
  assert.equal(provision.opens.addressKind, "readable");
  assert.equal(provision.opens.address, "/eu-eurlex/32016R0679/eng/099");
  assert.equal(provision.recognised.article.number, "99");
});

test("each rule a reference answer states about itself is refused when broken", async () => {
  const [dossier, dated] = await capturedRows("lu-legilux", { references: true });
  const broken = [
    [mutate(dated, (a) => { a.lanes_run = true; }), /ran no lane/],
    [mutate(dated, (a) => { a.hits = [{}]; }), /lists no hit/],
    [mutate(dated, (a) => { a.requested_identifier = "/lu-legilux/x"; }), /no work is named/],
    [mutate(dated, (a) => { a.reference.reason = "date_not_a_calendar_date"; }), /exactly when it says no reason/],
    [mutate(dated, (a) => { a.reference.opens.address_kind = "readable"; }), /not a readable address/],
    [mutate(dated, (a) => { a.reference.opens.address = "/fr" + a.reference.opens.address; }), /not a permalink address/],
    [mutate(dated, (a) => { a.reference.opens.address = "https://example.invalid/x"; }), /not a permalink address/],
    [mutate(dated, (a) => { a.reference.opens.publisher = "eu-eurlex"; }), /is lu-legilux's/],
    [mutate(dossier, (a) => { a.reference.opens.page = "reading"; }), /opens a reading at a dossier address/],
    [mutate(dated, (a) => { a.reference.kind = "guess"; }), /reference.kind/],
    [mutate(dated, (a) => { a.reference.recognised.date.source = "nearby"; }), /date.source/],
    [mutate(dated, (a) => { a.reference.recognised.date.date = "2024-02-30"; }), /not a calendar date/],
  ];
  for (const [answer, reason] of broken) assert.throws(() => readReferenceAnswer(answer), reason);
});

test("an answer that searched words says the phase read no reference, or did not run because a work was named", async () => {
  const { both } = await capturedAnswers();
  assert.equal(readSearch(both).reference, "no_reference");
  const [named] = await capturedRows("eu-eurlex");
  assert.equal(readEuropeSearch(named).reference, "not_run_identifier_given");
  assert.throws(() => readSearch(mutate(both, (a) => { a.reference.outcome = "not_run_identifier_given"; })), /not run exactly when a work is named/);
  assert.throws(() => readSearch(mutate(both, (a) => { a.reference.outcome = "opens"; })), /searched words/);
  // A capture from before the phase carries no report of it, and reads as before.
  assert.equal(readSearch(mutate(both, (a) => { delete a.reference; })).reference, null);
});

test("a refusal of the phase says what was recognised; one of a word search says nothing", () => {
  const recognised = readRefusalRecognition({
    requested_identifier: "Code du travail, article L. 121-6, 15 mars 2021",
    recognised_kind: "citation",
    recognised_act: "Code du travail",
    recognised_title: null,
    recognised_article: "L. 121-6",
    recognised_date: "2021-03-15",
    recognised_date_source: "citation",
  });
  assert.deepEqual({ ...recognised }, { kind: "citation", act: "Code du travail", title: null, article: "L. 121-6", date: "2021-03-15", dateSource: "citation", heldAct: null, heldVersion: null, catalogueWorks: null });
  // The acts the publisher's catalogue names, which this site holds no version of (the review of #974, F2).
  const unknown = {
    recognised_kind: "citation", recognised_act: "loi du 10 août 1991", recognised_title: null, recognised_article: null,
    recognised_date: null, recognised_date_source: null, recognised_catalogue_works: ["http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n5"],
  };
  assert.deepEqual(readRefusalRecognition(unknown).catalogueWorks, ["http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n5"]);
  assert.throws(() => readRefusalRecognition({ ...unknown, recognised_catalogue_works: "n5" }), /recognised_catalogue_works/);
  assert.equal(readRefusalRecognition({ requested_language: "deu", available_languages: ["fra"] }), null);
  assert.throws(() => readRefusalRecognition({ recognised_kind: "guess", recognised_act: "x", recognised_title: null, recognised_article: null, recognised_date: null, recognised_date_source: null }), /recognised_kind/);
});

test("the phase's vocabularies are the platform's", () => {
  assert.deepEqual(REFERENCE_OUTCOMES, ["no_reference", "not_run_identifier_given", "opens", "not_opened"]);
  assert.deepEqual(REFERENCE_KINDS, ["eli", "celex", "memorial", "historical_legal_id", "permalink", "citation"]);
  assert.deepEqual(REFERENCE_DATE_SOURCES, ["citation", "identifier", "search_date", "today_in_luxembourg"]);
  assert.deepEqual(NOT_OPENED_REASONS, ["date_not_a_calendar_date", "two_dates_disagree"]);
  assert.deepEqual(SEARCHED_AS_WORDS_REASONS, ["title_not_held"]);
  assert.deepEqual(OPENED_ADDRESS_KINDS, { dossier: "work", permalink: "state", readable: "current" });
});
