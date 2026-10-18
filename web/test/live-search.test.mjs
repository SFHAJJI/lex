// The live search screen, measured against envelopes the platform really sent.
//
// The screen asks `search` through the client module when the reader submits, and shows one state.
// These tests drive it with an injected fetch that answers the census envelopes
// (`schemas/v3-platform/envelope-samples.json`: a search answer in the strict lane alone, as the page asks it, with its
// passages by act, and one with no hit; an answer with hits in both lanes, as an API caller may ask it; a
// `language_not_available` refusal and a `no_corpus_mounted` refusal; and, for an EU work named by
// its CELEX, an answer whose hits carry the pinned wording's permalink, one with no hit, a language the work is not
// held in and an identifier the EU index does not hold), so every state is the one a real server
// response produces, and the server render is the idle form a browser hydrates.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { FailureFrame, LiveSearch, RefusalCard, SearchAnswerView, SearchResultsView, renderLiveSearchPage } from "../.react-build/app.mjs";
import {
  LIVE_SEARCH_IDLE,
  builtOnOf,
  LIVE_SEARCH_REFUSAL_SENTENCES,
  SEARCH_QUERY_MAX,
  SEARCH_TERMS_MAX,
  createSearchSession,
  loadLiveSearch,
  searchOutcome,
  searchParameters,
  searchTerms,
  startLiveSearch,
} from "../scripts/live-search.mjs";
import { REFUSAL_EXAMPLES } from "../scripts/refusal-catalog.mjs";
import { OFFICIAL_SEARCH_PAGES } from "../scripts/live-failure.mjs";
import { publisherSourceUri } from "../scripts/routes.mjs";

/** A refusal's card as the screen shows it: one about this service (R60) inside the failure panel, under its line; any other alone. */
const asShown = (outcome, card) => renderToStaticMarkup(h(FailureFrame, { outcome }, h("lex-card"))).replace("<lex-card></lex-card>", card);

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "search" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the search envelope "${scenarioStart}..."`);
  return entry.envelope;
};
// The answers as the page asks them (the strict lane alone, the plan's item 4.5), and one in both lanes, as an API caller may ask.
const ANSWER = "the phrase as the site asks it";
const BOTH_LANES = "a phrase the held text carries";
const NO_HIT = "a word no held article carries";
const REQUEST = { query: "assemblée générale", language: "fra" };
const ASKED = { ...REQUEST, mode: "strict" };
const EU_ANSWER = "the EU work as the site asks it";
const EU_NO_HIT = "a word the EU work's wording does not carry";
const EU_REQUEST = { query: "joint controllers", language: "eng", identifier: "32016R0679" };
const EU_ASKED = { query: "joint controllers", language: "eng", mode: "strict", identifier: "32016R0679" };

function answering(status, contentType, body) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    return {
      status,
      headers: { get: (name) => (name.toLowerCase() === "content-type" ? contentType : null) },
      text: async () => (typeof body === "string" ? body : JSON.stringify(body)),
    };
  };
  return { calls, fetchImpl };
}

const view = (outcome) => renderToStaticMarkup(h(SearchAnswerView, { outcome }));
const text = (markup) => markup.replace(/<[^>]+>/g, "").replace(/&#x27;|&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, "&");

test("the server renders the form in its idle state, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const markup = renderToStaticMarkup(h(LiveSearch, { contract, fetchImpl }));
  assert.match(markup, /<form role="search"/);
  assert.match(markup, /data-answer-state="idle"/);
  assert.ok(markup.includes(LIVE_SEARCH_IDLE));
  assert.equal(calls.length, 0, "a search is asked when the reader submits, never while rendering");

  const page = renderLiveSearchPage();
  assert.match(page, /<script src="\/en\/client-live-search.js" defer=""><\/script>/);
  assert.match(page, /id="live-search-root"/);
  const form = page.slice(page.indexOf('<form role="search"'), page.indexOf("</form>"));
  assert.ok(form.length > 0);
  assert.doesNotMatch(form, /\sname=/, "no control is named, so a submit the browser performs before hydration sends nothing (review of #772)");
  assert.ok(form.includes("Work identifier (optional)"), "the form offers to search within one work, which is how an EU work is searched");
});

test("the request carries the phrase as typed, the language, the strict lane and the work if one is named, and nothing else", () => {
  assert.deepEqual(searchParameters(REQUEST), ASKED);
  assert.deepEqual(Object.keys(searchParameters(EU_REQUEST)), ["query", "language", "mode", "identifier"], "in the order a journey's body is held to");
  assert.deepEqual(searchParameters(EU_REQUEST), EU_ASKED, "a named work is sent");
  assert.deepEqual(searchParameters({ ...REQUEST, identifier: "" }), ASKED, "an empty work field sends no identifier");
  assert.deepEqual(searchParameters({ ...REQUEST, identifier: "  " }), ASKED, "nor does a blank one");
  assert.deepEqual(searchParameters({ ...EU_REQUEST, identifier: " 32016R0679" }), { ...EU_ASKED, identifier: " 32016R0679" }, "an identifier is sent as typed, as the dossier screen sends it");
  assert.deepEqual(searchParameters({ ...EU_REQUEST, after: "strict.a" }), { ...EU_ASKED, after: "strict.a" });
  assert.deepEqual(searchParameters({ ...REQUEST, query: "  Loyer " }), { query: "  Loyer ", language: "fra", mode: "strict" }, "never trimmed or folded: the platform matches bytes");
  assert.deepEqual(searchParameters({ ...REQUEST, after: "strict.a.b" }), { ...ASKED, after: "strict.a.b" });
  // Only exact matching runs until the relaxed lane's own check passes (R90): no request from this page asks for it.
  assert.equal(searchParameters({ ...REQUEST, mode: "relaxed" }).mode, "strict");
  const words = (count) => Array.from({ length: count }, (_, index) => `w${index}`).join(" ");
  assert.equal(searchParameters({ ...REQUEST, query: words(SEARCH_TERMS_MAX) }).query, words(SEARCH_TERMS_MAX));
  assert.ok(searchParameters({ ...REQUEST, query: `${words(SEARCH_TERMS_MAX)} w0` }), "a repeated word is one term, as the platform counts it");
  assert.equal(searchTerms("a\u00a0b\u0085c\u2028d e\t\ta").length, 5, "split where .NET splits: no-break space, NEL, line separator, tabs");
  assert.equal(searchTerms("a\ufeffb").length, 1, "and not where it does not: U+FEFF is not whitespace to the platform");
  for (const [what, request, reason] of [
    ["more distinct words than the platform counts", { ...REQUEST, query: words(SEARCH_TERMS_MAX + 1) }, /at most 32 different words, and this one has 33/],
    ["a blank phrase", { ...REQUEST, query: "   " }, /needs a phrase/],
    ["no phrase", { language: "fra" }, /needs a phrase/],
    ["a phrase over the ceiling", { ...REQUEST, query: "x".repeat(SEARCH_QUERY_MAX + 1) }, /at most 512/],
    ["a language the form does not offer", { ...REQUEST, language: "ltz" }, /not a language this form offers/],
    ["an empty cursor", { ...REQUEST, after: "" }, /cursor the previous page handed over/],
    ["an identifier that is not text", { ...REQUEST, identifier: 32016 }, /a work identifier is text/],
  ]) {
    assert.throws(() => searchParameters(request), reason, what);
  }
  assert.equal(searchParameters({ ...REQUEST, query: "x".repeat(SEARCH_QUERY_MAX) }).query.length, SEARCH_QUERY_MAX);
});

test("a served search answer is the results by act, read by the search reader", async () => {
  const envelope = envelopeOf(ANSWER);
  const value = envelope.result.value;
  const { calls, fetchImpl } = answering(200, "application/json; charset=utf-8", envelope);
  const outcome = await loadLiveSearch({ contract, fetchImpl, request: REQUEST });
  assert.equal(outcome.state, "success");
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "/api/v3/search");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "search", parameters: ASKED }, "the body is the operation, the two parameters and the strict lane");

  const markup = view(outcome);
  assert.match(markup, /^<section data-answer-state="success">/);
  const shown = text(markup);
  for (const hit of value.hits) {
    assert.ok(markup.includes(`<a href="/en${hit.resolve.identifier}/${hit.publisher_id}"><strong>${hit.publisher_id}</strong></a>`), `${hit.publisher_id} links to its reading at the article, by its permalink (R64: the article a segment of it)`);
    // The extract, word for word, each whole match marked (R59).
    const quoted = hit.extract.parts.map((part) => (part.matched ? `<mark>${part.text}</mark>` : part.text)).join("");
    assert.ok(markup.includes(`<q lang="fr">${quoted}</q>`), `${hit.publisher_id}'s extract is quoted as the answer cuts it`);
  }
  for (const row of markup.match(/<li data-lane="[^"]*">.*?<\/li>/g)) {
    assert.doesNotMatch(text(row), /[0-9a-f]{64}/, "a hit row prints no digest (UX spec section 2): its link carries the pin");
  }
  assert.equal((markup.match(/data-lane="strict"/g) ?? []).length, value.hits.length);
  assert.equal((markup.match(/data-lane="relaxed"/g) ?? []).length, 0);
  assert.equal((markup.match(/<mark>assemblée générale<\/mark>/g) ?? []).length, value.hits.flatMap((hit) => hit.extract.parts.filter((part) => part.matched)).length);
  assert.doesNotMatch(markup, /class="badge"/, "a strict passage carries no lane badge: every passage is the exact phrase");

  // The act once, its passages under its one version, dated in the publisher's words, said as held only as published (R21).
  const [act] = value.acts;
  assert.equal((markup.match(/<li data-act="/g) ?? []).length, 1);
  assert.ok(markup.includes(`<a href="/en/lu-legilux/${act.work_key}">${act.work_key}</a>`), "an act with no title held is named by its work key, linked to its dossier");
  assert.ok(shown.includes(`${act.work_key} (this index holds no title for this act)`));
  assert.ok(shown.includes(`${value.hits.length} passages in this act`));
  assert.ok(shown.includes("As published; may have been amended since; no consolidated text."));
  assert.ok(shown.includes(`The act as published, applicable from ${value.hits[0].applicability_date} (publisher)`));
  assert.doesNotMatch(shown, /version of/, "a version is dated as the publisher dates it, never by its date alone");

  // What was found and what was searched, every count the answer's, dated by the index's build (R11).
  assert.ok(shown.includes(`“assemblée générale” in French: ${value.hits.length} passages with the exact phrase, in 1 act.`));
  const { searched } = value;
  assert.ok(shown.includes(`Searched: ${searched.works} act of Luxembourg law in French, in ${searched.states} version with ${searched.articles_with_searchable_text} articles of searchable text, as of the index built on ${builtOnOf(searched.index_built_at)}.`), shown);
  assert.ok(shown.includes("The first hits in the stated order, not the best hits."), "the page says there is no ranker, where the hits are");
  assert.ok(shown.includes("This index holds no work titles"), "the work resolution the answer gives is shown");
  assert.doesNotMatch(markup, /data-no-hit-card/, "a search with passages shows no no-hit card");
  assert.doesNotMatch(markup, /Next page/, "a whole page offers no next page");
});

test("an answer in both lanes, as an API caller may ask it, still says each lane's count, and marks a relaxed passage", () => {
  const outcome = searchOutcome({ state: "success", envelope: envelopeOf(BOTH_LANES) });
  assert.equal(outcome.state, "success");
  const markup = view(outcome);
  const shown = text(markup);
  assert.ok(shown.includes("4 with the exact phrase, 1 with every word, in 1 work."));
  assert.equal((markup.match(/data-lane="relaxed"/g) ?? []).length, 1);
  assert.match(markup, /<span class="badge">every word<\/span>/, "a relaxed passage says its lane");
});

test("a search with no hit is the plain no-hit card: what ran, that only exact matching ran, the population, and the publishers' search pages", () => {
  const value = envelopeOf(NO_HIT).result.value;
  const outcome = searchOutcome({ state: "success", envelope: envelopeOf(NO_HIT) });
  assert.equal(outcome.state, "success");
  const markup = view(outcome);
  const shown = text(markup);
  assert.match(markup, /<section class="no-hit-card" data-no-hit-card="">/);
  assert.ok(shown.includes("No article of the Luxembourg text this server holds and may search contains “zéphyr”."));
  assert.ok(shown.includes("What was searched"));
  assert.ok(shown.includes("Exact references: the phrase is no reference this site reads (an ELI, a CELEX number, a Mémorial reference, a permalink or a citation)."));
  assert.ok(shown.includes("This index holds no work titles, so the phrase was matched against article text only."), "the titles line is the work resolution's own");
  assert.ok(shown.includes("Text: no article contains the phrase exactly as typed."));
  assert.doesNotMatch(markup, /data-search-run="every-word"/, "the relaxed lane was not run, so it is not said to have found nothing");
  assert.ok(shown.includes("Only exact matching ran: capitals, accents and every other character of the phrase count, and nothing is folded, stemmed or expanded. A looser search stays off until its own check has passed."));
  assert.ok(shown.includes(`Searched: ${value.searched.works} act of Luxembourg law in French`));
  assert.equal((markup.match(/data-searched=/g) ?? []).length, 1, "the population is said once, inside the card");
  assert.ok(markup.indexOf("data-searched=") > markup.indexOf("data-no-hit-card"));
  for (const page of OFFICIAL_SEARCH_PAGES) {
    assert.ok(markup.includes(`<a href="${page.uri}" rel="external" data-publisher="${page.publisher}">`), page.key);
  }
  assert.ok(shown.includes("Legilux search, for Luxembourg law") && shown.includes("EUR-Lex search, for EU law"));
  assert.ok(shown.includes("It is not evidence that the instrument or the law does not exist."), "the absence note (journey J2)");
  assert.ok(shown.includes("0 passages with the exact phrase, in 0 acts."));
});

test("an EU work's no-hit card says the reference and the titles were not run, and counts no Luxembourg population", () => {
  const outcome = searchOutcome({ state: "success", envelope: envelopeOf(EU_NO_HIT) });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = view(outcome);
  const shown = text(markup);
  assert.ok(shown.includes("Exact references: not read, since a work was named."));
  assert.ok(shown.includes("Titles: not searched, since a work was named."));
  assert.ok(shown.includes("Text: no article contains the phrase exactly as typed."));
  assert.ok(shown.includes("Only exact matching ran:"));
  assert.doesNotMatch(markup, /data-searched=/, "an EU search counts no Luxembourg population");
  assert.doesNotMatch(shown, /Searched:/);
  assert.ok(markup.includes(OFFICIAL_SEARCH_PAGES[1].uri));
});

test("the publishers' search pages are the contracts' addresses, each on its publisher's own host, and links only", () => {
  assert.deepEqual(OFFICIAL_SEARCH_PAGES.map((page) => page.uri), ["https://legilux.public.lu/search", "https://eur-lex.europa.eu/advanced-search-form.html"]);
  for (const page of OFFICIAL_SEARCH_PAGES) assert.equal(publisherSourceUri({ publisher: page.publisher, uri: page.uri }), page.uri);
});

test("the act a title names is on top, linked; several acts' titles are counted, the first ten shown and the rest one step away", () => {
  const card = (index, reason = "exact_normalized_title") => ({
    work_identifier: `http://data.legilux.public.lu/eli/etat/leg/loi/2011/11/12/n${index}/jo/fr`,
    work_key: `loi-2011-11-12-n${index}`,
    expressions: [`http://data.legilux.public.lu/eli/etat/leg/loi/2011/11/12/n${index}/jo/fr`],
    languages: ["fra"],
    matched_title: `Loi du 12 novembre 2011 numéro ${index}.`,
    matched_title_language: "fra",
    match_reason: reason,
  });
  const resolved = (resolution) => {
    const envelope = structuredClone(envelopeOf(ANSWER));
    envelope.result.value.work_resolution = { retrieval_lane: "r1_work_discovery", candidates: null, work: null, ...resolution };
    const outcome = searchOutcome({ state: "success", envelope });
    assert.equal(outcome.state, "success", outcome.sentence);
    return view(outcome);
  };
  for (const [reason, said] of [["exact_normalized_title", "The phrase is the title of"], ["prefix", "The phrase begins the title of"], ["all_tokens_contained", "Every word of the phrase is in the title of"]]) {
    const markup = resolved({ outcome: "one_work", work: card(1, reason) });
    assert.ok(markup.includes(`${said} <a href="/en/lu-legilux/loi-2011-11-12-n1"><span lang="fr">Loi du 12 novembre 2011 numéro 1.</span></a>.`), reason);
    assert.ok(markup.indexOf("data-work-resolution") < markup.indexOf("data-population"), "the act a title names comes first, above what the text search found");
  }
  const unlinked = resolved({ outcome: "one_work", work: { ...card(1), work_key: null } });
  assert.ok(unlinked.includes('The phrase is the title of <span lang="fr">Loi du 12 novembre 2011 numéro 1.</span> (this site holds no version of it).'),
    "a card naming no held work is said without a link, and said not held");

  const twelve = resolved({ outcome: "several_candidates", candidates: Array.from({ length: 12 }, (_, index) => card(index + 1, "prefix")) });
  assert.ok(text(twelve).includes("The phrase matches the titles of 12 acts:"));
  const shownList = twelve.slice(twelve.indexOf("data-candidates"), twelve.indexOf("<details"));
  assert.equal((shownList.match(/<li>/g) ?? []).length, 10, "the first ten are shown");
  assert.match(twelve, /<details data-more-titles="2"><summary>2 more acts<\/summary><ul>(<li>.*?<\/li>){2}<\/ul><\/details>/, "the rest are one step away");
  const two = resolved({ outcome: "several_candidates", candidates: [card(1), card(2)] });
  assert.doesNotMatch(two, /<details/, "two titles are both shown");
});

test("an EU work's search is the EU results: the pinned wording once, each hit's heading, CELEX and wording date, its permalink, and what is not covered", async () => {
  const envelope = envelopeOf(EU_ANSWER);
  const value = envelope.result.value;
  const { calls, fetchImpl } = answering(200, "application/json; charset=utf-8", envelope);
  const outcome = await loadLiveSearch({ contract, fetchImpl, request: EU_REQUEST });
  assert.equal(outcome.state, "success", outcome.sentence);
  assert.equal(outcome.view.publisher, "eu-eurlex");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "search", parameters: EU_ASKED }, "the work is named in the request, and the strict lane asked");

  const markup = view(outcome);
  const shown = text(markup);
  assert.ok(markup.includes(`data-pinned-wording="${value.pinned_wording.wording_sha256}"`));
  assert.ok(markup.includes(`<code>${value.pinned_wording.permalink}</code>`), "the wording's permalink is printed once, above the hits");
  assert.ok(shown.includes(`Every hit is in the original wording of 32016R0679 this server holds in eng, dated ${value.pinned_wording.wording_date} and pinned by its digest`));
  assert.ok(shown.includes(`The wording date (wording_date in search, wording_dates in dossier) is the date the publisher's Formex package gives the act`),
    "the answer's own sentence says what the date is, as a sentence");
  assert.ok(markup.includes(`<a href="/en${value.pinned_wording.permalink}"><code>`), "the wording's permalink links to its reading");
  for (const hit of value.hits) {
    assert.ok(markup.includes(`<a href="/en${hit.permalink}">`), `${hit.publisher_id} links to its reading at the provision, by its permalink`);
    assert.ok(!markup.includes(`<code>${hit.permalink}</code>`), "and the row prints no digest");
    assert.ok(shown.includes(`${hit.heading} of ${hit.celex}, wording of ${hit.wording_date}`));
    assert.ok(markup.includes(`<strong lang="en">${hit.heading}</strong>`), "the publisher's heading is marked in its language");
  }
  assert.doesNotMatch(shown, /version of/, "an EU wording is never called a version");
  assert.equal((markup.match(/data-lane="strict"/g) ?? []).length, 2);
  assert.equal((markup.match(/data-lane="relaxed"/g) ?? []).length, 0, "the page asks the strict lane alone");
  assert.ok(shown.includes("“joint controllers” in English: 2 passages with the exact phrase, in 1 act."));
  assert.ok(shown.includes("What this search does not cover"));
  for (const row of value.not_held) assert.ok(shown.includes(`${row.item}: ${row.reason}`), row.item);
  assert.doesNotMatch(markup, /Next page/, "a whole page offers no next page");

  // The review of #853's reproduction, through the screen: a hit of a second work is not shown as a one-work result.
  const twoWorks = structuredClone(envelope);
  const second = twoWorks.result.value.hits[1];
  second.publisher_work_id = `${second.publisher_work_id.slice(0, -1)}${second.publisher_work_id.endsWith("0") ? "1" : "0"}`;
  const mixed = searchOutcome({ state: "success", envelope: twoWorks });
  assert.equal(mixed.state, "invalid_envelope");
  assert.match(mixed.sentence, /the hits are in 2 works/);

  const unpinned = structuredClone(envelope);
  unpinned.result.value.pinned_wording = null;
  const refused = searchOutcome({ state: "success", envelope: unpinned });
  assert.equal(refused.state, "invalid_envelope", "hits that pin no wording are not shown unpinned");
  assert.match(refused.sentence, /pins no wording/);
});

test("a truncated page offers the next page with its own cursor, and a whole-result page does not", async () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  const value = envelope.result.value;
  value.hits = value.hits.slice(0, 2);
  value.limit = 2;
  value.truncated = true;
  const last = value.hits[1];
  value.continue_after = `${last.lane}.${last.state_sha256}.${last.article_identity_sha256}`;
  const outcome = searchOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success");
  let asked = null;
  const onNextPage = (after) => { asked = after; };
  const markup = renderToStaticMarkup(h(SearchAnswerView, { outcome, onNextPage }));
  assert.match(markup, /<button type="button">Next page<\/button>/);
  assert.equal(asked, null, "rendering asks nothing");

  // The button itself, pressed: it hands over the cursor the page carries, and nothing else (review of #772).
  const find = (node) => {
    if (node === null || typeof node !== "object") return null;
    if (Array.isArray(node)) return node.map(find).find(Boolean) ?? null;
    if (node.type === "button") return node;
    return find(node.props?.children);
  };
  const button = find(SearchResultsView({ view: outcome.view, onNextPage }));
  assert.ok(button, "the truncated page renders a button");
  button.props.onClick();
  assert.equal(asked, value.continue_after, "the next page is asked with this page's own cursor");
});

test("an answer the search reader refuses is an invalid answer, said, never rendered", () => {
  const envelope = structuredClone(envelopeOf(BOTH_LANES));
  envelope.result.value.hits.reverse();
  const outcome = searchOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "invalid_envelope");
  assert.match(outcome.sentence, /relaxed never outranks strict/);
  assert.match(view(outcome), /^<section data-answer-state="invalid_envelope"><div class="failure-panel" data-failure="invalid_envelope"><p class="failure-line"[^]*<p role="status">/,
    "an answer the page cannot read is a failure, said under the failure line (R60)");
});

test("the refusals a search from this page can meet are refusal cards, an EU work's too", async () => {
  for (const [scenario, code, request] of [
    ["no corpus mounted", "no_corpus_mounted", REQUEST],
    ["a language the mount holds no text in", "language_not_available", { ...REQUEST, language: "deu" }],
    ["one EU work in a language it is not held in", "language_not_available", { ...EU_REQUEST, language: "fra" }],
    ["an EU identifier the EU index does not hold", "identifier_unknown", { ...EU_REQUEST, identifier: "32099R9999" }],
  ]) {
    const envelope = envelopeOf(scenario);
    const { fetchImpl } = answering(200, "application/json", envelope);
    const outcome = await loadLiveSearch({ contract, fetchImpl, request });
    assert.equal(outcome.state, "refusal", code);
    assert.equal(outcome.code, code);
    assert.equal(outcome.card, true, `${code}: the card's rules accept the payload the platform sent`);
    assert.equal(outcome.sentence, code === "no_corpus_mounted" ? "This build has no Luxembourg index mounted." : LIVE_SEARCH_REFUSAL_SENTENCES[code]);
    const card = renderToStaticMarkup(h(RefusalCard, { code, sentence: outcome.sentence, payload: outcome.payload }));
    assert.equal(view(outcome), `<section data-answer-state="refusal">${asShown(outcome, card)}</section>`);
  }
  assert.equal(LIVE_SEARCH_REFUSAL_SENTENCES.no_corpus_mounted, undefined, "the missing index is named from the payload, never a sentence claiming none is mounted (review of #775)");
  assert.ok(REFUSAL_EXAMPLES.no_corpus_mounted.payload.required_corpus, "the catalog's example carries the corpus the sentence names");
});

test("a transport failure and an unreadable envelope are each a state with a sentence", async () => {
  const problem = answering(400, "application/problem+json", { type: "about:blank", title: "bad", status: 400, code: "request_schema_invalid" });
  const failed = await loadLiveSearch({ contract, fetchImpl: problem.fetchImpl, request: REQUEST });
  assert.equal(failed.state, "transport_failure");
  assert.match(failed.sentence, /refused the search as it was asked \(request_schema_invalid\); ask it again from the first page/,
    "a server that was reached and refused is not one that could not be reached (review of #772)");
  const offline = await loadLiveSearch({ contract, fetchImpl: async () => { throw new TypeError("failed to fetch"); }, request: REQUEST });
  assert.match(offline.sentence, /could not be reached \(network_error\)/);

  const garbled = answering(200, "application/json", "{");
  const unreadable = await loadLiveSearch({ contract, fetchImpl: garbled.fetchImpl, request: REQUEST });
  assert.equal(unreadable.state, "invalid_envelope");
});

test("a search asks once and hands the state over, and a cancel aborts and silences it", async () => {
  const settled = answering(200, "application/json", envelopeOf(ANSWER));
  const outcomes = [];
  let done;
  const arrived = new Promise((resolve) => { done = resolve; });
  startLiveSearch({ contract, fetchImpl: settled.fetchImpl, request: REQUEST, onOutcome: (outcome) => { outcomes.push(outcome); done(); } });
  await arrived;
  assert.equal(settled.calls.length, 1);
  assert.deepEqual(outcomes.map((outcome) => outcome.state), ["success"]);

  let aborted = false;
  const hanging = async (url, init) => new Promise((resolve, reject) => {
    init.signal.addEventListener("abort", () => { aborted = true; reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
  });
  const silenced = [];
  const cancel = startLiveSearch({ contract, fetchImpl: hanging, request: REQUEST, onOutcome: (outcome) => silenced.push(outcome) });
  cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.equal(aborted, true, "the request is aborted");
  assert.deepEqual(silenced, [], "and nothing is handed over after cancel");
});

test("a session asks when told, says a request it will not send, cancels the search in flight, and asks the next page with the cursor", async () => {
  const outcomes = [];
  const settled = answering(200, "application/json", envelopeOf(ANSWER));
  let done;
  let arrived = new Promise((resolve) => { done = resolve; });
  const session = createSearchSession({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome); if (outcome.state !== "loading") done(); } });

  assert.equal(session.next("strict.a.b"), false, "no next page before a first search");
  assert.equal(session.ask({ ...REQUEST, query: " " }), false);
  assert.deepEqual(outcomes.map((outcome) => outcome.state), ["invalid_request"]);
  assert.equal(settled.calls.length, 0, "nothing is asked for a request the form will not send");

  arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.ask(REQUEST), true);
  await arrived;
  assert.deepEqual(outcomes.map((outcome) => outcome.state), ["invalid_request", "loading", "success"]);

  arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.next("strict.cursor.last"), true);
  await arrived;
  assert.deepEqual(JSON.parse(settled.calls[1].init.body).parameters, { ...ASKED, after: "strict.cursor.last" }, "the next page repeats the search with the cursor");

  arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.ask(EU_REQUEST), true);
  await arrived;
  arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.next("strict.article"), true);
  await arrived;
  assert.deepEqual(JSON.parse(settled.calls[3].init.body).parameters, { ...EU_ASKED, after: "strict.article" }, "the next page stays within the work named");

  const aborted = [];
  const hanging = async (url, init) => new Promise((resolve, reject) => {
    init.signal.addEventListener("abort", () => { aborted.push(JSON.parse(init.body).parameters.query); reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
  });
  const later = [];
  const second = createSearchSession({ contract, fetchImpl: hanging, onOutcome: (outcome) => later.push(outcome.state) });
  second.ask({ ...REQUEST, query: "first" });
  second.ask({ ...REQUEST, query: "second" });
  second.cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(aborted, ["first", "second"], "a new search cancels the one in flight, and cancel the last");
  assert.deepEqual(later, ["loading", "loading"], "nothing settles after a cancel");
});
