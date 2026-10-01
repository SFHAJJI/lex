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
  SEARCH_LANES,
  WORK_RESOLUTION_OUTCOMES,
  escapeProvision,
  readEuropeSearch,
  readSearch,
  readSearchAnswer,
} from "../scripts/search-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

async function capturedRows(publisher) {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  const rows = parsed.sampled.filter((sample) => sample.operation === "search" && sample.answer.publisher === publisher);
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
    assert.equal(hit.permalink, `/lu-legilux/${hit.workKey}/${hit.applicabilityDate}--${hit.stateSha256}`);
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
  const state = (date, digit) => `/lu-legilux/other-work/${date}--${digit.repeat(64)}`;
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
    ["an ambiguous work with hits", mutate(dated, (a) => { a.ambiguous_works[0].work_key = work; a.ambiguous_works[0].candidates = [`/lu-legilux/${work}/2023-01-01--${"a".repeat(64)}`, `/lu-legilux/${work}/2023-01-01--${"b".repeat(64)}`]; }), /contributes hits/],
    ["a candidate of another work", mutate(dated, (a) => { a.ambiguous_works[0].candidates[1] = `/lu-legilux/elsewhere/2023-01-01--${"b".repeat(64)}`; }), /not a state of other-work/],
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
    ["a permalink to another state", both, (a) => { a.hits[1].resolve.identifier = a.hits[1].resolve.identifier.slice(0, -1) + "0"; }, /not to the state it names/],
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
  assert.match(view.wording.permalink, /^\/eu-eurlex\/32016R0679\/eng\/\d{4}-\d{2}-\d{2}--[0-9a-f]{64}$/);
  assert.equal(view.wording.permalink, `/eu-eurlex/32016R0679/eng/${view.wording.wordingDate}--${view.wording.wordingSha256}`);
  assert.deepEqual(view.hits.map((hit) => hit.lane), ["strict", "strict", "relaxed"]);
  assert.deepEqual(view.hits.map((hit) => hit.heading), ["Article 26", "Article 36", "Article 47"]);
  for (const hit of view.hits) {
    assert.equal(hit.permalink, `${view.wording.permalink}#${hit.publisherId}`, "each hit's permalink is the wording's with its provision");
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
    hit.permalink = `${a.pinned_wording.permalink}#26%28a%29`;
  });
  assert.equal(readEuropeSearch(escaped).hits[0].permalink.split("#")[1], "26%28a%29");
  assert.throws(() => readEuropeSearch(mutate(escaped, (a) => { a.hits[0].permalink = `${a.pinned_wording.permalink}#26(a)`; })), /not the pinned wording's with its provision/);
  assert.throws(() => readEuropeSearch(mutate(escaped, (a) => { a.hits[0].resolve.identifier = `${a.hits[0].publisher_expression_id}#lex-provision=26(a)`; })), /not to the provision it names/);
});

test("each rule an EU answer states about itself is refused when broken, with that rule's reason", async () => {
  const { both, paged, next, none } = await capturedEuropeAnswers();
  assert.equal(readEuropeSearch(mutate(none, (a) => { a.pinned_wording = null; })).wording, null, "a no-hit answer that pins no wording still reads: it shows no hit");
  const flipLast = (text) => text.replace(/[0-9a-f]$/, (digit) => (digit === "0" ? "1" : "0"));
  const cases = [
    ["a date", both, (a) => { a.requested_date = "2020-01-01"; }, /names no date/],
    ["no work named", both, (a) => { a.requested_identifier = null; }, /requested_identifier is not a value/],
    ["hits and no pinned wording", both, (a) => { a.pinned_wording = null; }, /hit 1 pins no wording/],
    ["a pinned permalink to another digest", both, (a) => { a.pinned_wording.permalink = flipLast(a.pinned_wording.permalink); }, /not the wording it names/],
    ["a pinned permalink to another date", both, (a) => { a.pinned_wording.wording_date = "2016-05-04"; }, /not the wording it names/],
    ["a pinned permalink in another language", both, (a) => { a.pinned_wording.permalink = a.pinned_wording.permalink.replace("/eng/", "/fra/"); }, /in fra, and the search was asked in eng/],
    ["a pinned permalink of another grammar", both, (a) => { a.pinned_wording.permalink = `/lu-legilux/32016R0679/2016-04-27--${a.pinned_wording.wording_sha256}`; }, /not an EU wording permalink/],
    ["a hit permalink to another provision", both, (a) => { a.hits[0].permalink = a.hits[0].permalink.replace(/#.*$/, "#999"); }, /not the pinned wording's with its provision/],
    ["a hit of another act", both, (a) => { a.hits[1].celex = "32016L0680"; }, /not in the wording the answer pins/],
    ["a hit of another wording date", both, (a) => { a.hits[1].wording_date = "2016-05-04"; }, /not in the wording the answer pins/],
    ["a hit in another language", both, (a) => { a.hits[2].language = "fra"; }, /one language asked \(eng\)/],
    ["a hit resolving to another provision", both, (a) => { a.hits[0].resolve.identifier = a.hits[0].resolve.identifier.replace(/=.*$/, "=999"); }, /not to the provision it names/],
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
