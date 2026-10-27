// The home page's example searches (the specification of record's §3, Shell and home: "a dated coverage line and example
// searches"; R47; the plan's item 4.1: "example searches that land on served text"): derived at build from the mounted
// corpus, through the API's own answers, and never written into copy, since a word in copy may name nothing a mount holds
// (R11's rule for counts, held for words too).
//
// The rule, fixed, so that builds over one mount derive the same examples:
// 1. The mount is the one `coverage` counts. Its Luxembourg index's digest is the examples' own: a page shows them only
//    over that index (`examplesShown`), so a build's examples never stand over another mount, or over none.
// 2. Its acts in the order `browse` lists them (by work key), at most HOME_EXAMPLE_CANDIDATES of them, each read as of
//    today in Luxembourg (`evidence_bundle`, R13), Luxembourg acts only (no answer lists the EU acts an index holds).
// 3. In each, the first article quoted with its text whose usual citation the site's generator composes (`citationForms`,
//    R65), the act named by its type and date alone, never by its title, since a title can name a person (R92).
// 4. That citation asked as the search page asks a search its address holds (`search`, the strict lane, in the text's
//    language, with no date): kept only when the exact-reference phase reads it and opens Reading at that very article
//    (`usualVerdict` says "matches", the plan's item 4.2). So an example lands on served text by construction, and the
//    deploy probe and the journeys hold the page to the examples the build carries.
// 5. One example per act, at most HOME_EXAMPLES_AT_MOST.
//
// The examples a build carries are a record (`lex-v3-home-examples/1`): the index's digest, the date they were derived on,
// and each example's query, its text's language and the address the search opens. `build-live.mjs` compiles the record
// into the home page's bundles (`__LEX_HOME_EXAMPLES__`), so the page the servers render and the page the browser hydrates
// say the same examples; a build handed none (the web suite's, CI's web job) carries none, and the page shows none.
//
// The derivation is a function of an `ask(operation, parameters)` that answers the envelope the API's REST route answers
// (or null), so it is tested with the census's answers and run against a real API by the journeys and the release.

import { citationForms, usualVerdict } from "./citation.mjs";
import { readingOutcome } from "./live-reading.mjs";
import { searchParameters } from "./live-search.mjs";
import { searchFragment } from "./search-address.mjs";
import { readSearchAnswer } from "./search-answer.mjs";
import { isCalendarDate } from "./temporal.mjs";
import { answerHandedWith } from "./v3-client.mjs";

/** The record's schema. */
export const HOME_EXAMPLES_SCHEMA = "lex-v3-home-examples/1";

/** How many acts the derivation reads, in `browse`'s order, before it stops: each costs a reading and a search. */
export const HOME_EXAMPLE_CANDIDATES = 24;

/** How many examples the home page shows at most. */
export const HOME_EXAMPLES_AT_MOST = 3;

/** The text languages an example is searched in: the languages the citation generator composes in and the form offers. */
const EXAMPLE_LANGUAGES = Object.freeze(["fra", "eng"]);

const DIGEST = /^[0-9a-f]{64}$/;

/** A reading view with no publisher's title held, so its usual citation names the act by its type and date alone (R92). */
function withoutTitles(view) {
  return { ...view, citationFacts: { ...view.citationFacts, titles: [] } };
}

/** The first article quoted with text, in the order the reading lists its versions and their articles, that has a usual citation. */
function firstCitation(view, observedAt) {
  for (const version of view.states ?? []) {
    if (!EXAMPLE_LANGUAGES.includes(version.language)) continue;
    for (const article of version.articles ?? []) {
      const forms = citationForms({ view, version, article, observedAt });
      if (forms !== null && typeof forms.conventional === "string" && forms.conventional.length > 0) return forms;
    }
  }
  return null;
}

/**
 * The home page's examples over the API `ask` answers for (`contract`, the census's, reads its envelopes), derived on
 * `today` (the calendar date in Luxembourg, `yyyy-mm-dd`): the record, or null when no corpus is counted (the page then
 * shows none). `log` hears one line per act read.
 */
export async function deriveHomeExamples({ ask, contract, today, candidates = HOME_EXAMPLE_CANDIDATES, atMost = HOME_EXAMPLES_AT_MOST, log = () => {} }) {
  if (typeof ask !== "function") throw new Error("the examples are derived through the API's answers: ask(operation, parameters)");
  if (!isCalendarDate(today)) throw new Error(`the examples are derived on a calendar date, not ${JSON.stringify(today)}`);
  const coverage = await ask("coverage", {});
  const indexSha256 = coverage?.refusal === null ? coverage?.result?.value?.mounted?.index_sha256 : undefined;
  if (typeof indexSha256 !== "string" || !DIGEST.test(indexSha256)) {
    log("no corpus counted: no example searches");
    return null;
  }
  const browsed = await ask("browse", { limit: candidates });
  const works = (browsed?.refusal === null ? browsed?.result?.value?.works : null) ?? [];
  const examples = [];
  for (const work of works.slice(0, candidates)) {
    if (examples.length >= atMost) break;
    if (typeof work?.identifier !== "string") continue;
    const reading = await ask("evidence_bundle", { identifier: work.identifier, date: today });
    const outcome = readingOutcome(answerHandedWith("evidence_bundle", reading, { contract, objectType: "evidence_bundle" }));
    if (outcome.state !== "success" || outcome.view.publisher === "eu-eurlex") {
      log(`${work.identifier}: no reading on ${today} (${outcome.state})`);
      continue;
    }
    const forms = firstCitation(withoutTitles(outcome.view), reading.context.freshness.observed_at);
    if (forms === null) {
      log(`${work.identifier}: no article with text and a usual citation`);
      continue;
    }
    const parameters = searchParameters({ query: forms.conventional, language: forms.language });
    const searched = await ask("search", parameters);
    const answered = answerHandedWith("search", searched, { contract, objectType: "quote" });
    const view = answered.state === "success" ? readSearchAnswer(searched.result.value) : null;
    const verdict = usualVerdict(forms, view);
    if (verdict !== "matches") {
      log(`${work.identifier}: "${forms.conventional}" does not open its article (${verdict})`);
      continue;
    }
    log(`${work.identifier}: "${forms.conventional}" opens ${view.opens.address}`);
    examples.push(Object.freeze({ query: forms.conventional, language: forms.language, opens: view.opens.address }));
  }
  return Object.freeze({ schema: HOME_EXAMPLES_SCHEMA, index_sha256: indexSha256, derived_on: today, examples: Object.freeze(examples) });
}

/**
 * A record read back, held to its shape (the schema, the index's digest, the date, and each example a query the search page
 * would send in a language it offers, with the address it opens), or null when it is none: a page then shows no example.
 */
export function readHomeExamplesValue(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return null;
  if (value.schema !== HOME_EXAMPLES_SCHEMA || typeof value.index_sha256 !== "string" || !DIGEST.test(value.index_sha256)) return null;
  if (!isCalendarDate(value.derived_on) || !Array.isArray(value.examples) || value.examples.length > HOME_EXAMPLES_AT_MOST) return null;
  const examples = [];
  for (const example of value.examples) {
    if (example === null || typeof example !== "object") return null;
    const { query, language, opens } = example;
    if (!EXAMPLE_LANGUAGES.includes(language) || typeof opens !== "string" || !opens.startsWith("/")) return null;
    try {
      searchParameters({ query, language });
    } catch {
      return null;
    }
    examples.push(Object.freeze({ query, language, opens }));
  }
  return Object.freeze({ schema: value.schema, index_sha256: value.index_sha256, derived_on: value.derived_on, examples: Object.freeze(examples) });
}

/** The record this bundle was compiled with (`build-live.mjs`), or null for a build handed none. */
export const HOME_EXAMPLES = readHomeExamplesValue(
  // eslint-disable-next-line no-undef
  typeof __LEX_HOME_EXAMPLES__ === "string" ? JSON.parse(__LEX_HOME_EXAMPLES__) : null,
);

/**
 * The examples a home page shows for its coverage line (`coverageLineOf`): the record's, when the line counts the very
 * index they were derived from; none otherwise (no corpus mounted, an answer that could not be read, or another mount).
 */
export function examplesShown(line, record = HOME_EXAMPLES) {
  if (record === null || line === null || line?.state !== "counted" || line.indexSha256 !== record.index_sha256) return Object.freeze([]);
  return record.examples;
}

/** An example's link: the search page of `locale` with the search in its address (R109), as a shared search is. */
export function exampleHref(example, locale) {
  return `/${locale}/search.html${searchFragment({ query: example.query, language: example.language }, { work: false })}`;
}
