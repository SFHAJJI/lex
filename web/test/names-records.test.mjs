// R92's two records as a release carries them beside its mount (`names-records.mjs`; the plan's item 1.11): the records held
// together (no withheld work offered to search engines, so the sitemap lists no law page of one), and the running image held to
// the names record (a withheld work's dossier answers text_withheld for R92; an act the catalogue names only by a title is
// named by a search for its ELI without it). Every identifier, digest and title here is synthetic.

import assert from "node:assert/strict";
import test from "node:test";

import { NAMES_WITHHELD_REASON, namesProbeFailures, readNamesRecord, recordsFailures, titlesIn, withheldAddress } from "../scripts/names-records.mjs";
import { readIndexingRecord, sitemapFiles } from "../scripts/search-engines.mjs";

const BUILT_AT = "2026-10-12T18:17:05Z";
const indexing = (works, builtAt = BUILT_AT) => JSON.stringify({ schema: "lex-v3-indexing-record/1", built_at: builtAt, works });
const names = (works, builtAt = BUILT_AT) => JSON.stringify({
  schema: "lex-v3-names-record/1",
  measure: "lex-v3-names-measure/1",
  rule: "R92(1)",
  measured: { corpus_sha256: "a".repeat(64), luxembourg_index_sha256: "b".repeat(64), europe_index_sha256: "c".repeat(64), built_at: builtAt },
  works,
});
const withheldWork = (overrides = {}) => ({
  publisher: "lu-legilux", work_iri: "http://data.legilux.public.lu/eli/etat/leg/loi/2020/03/03/a1", work_key: "loi-2020-03-03-a1", celex: null,
  class: "lu_text_names_person", reason: NAMES_WITHHELD_REASON, evidence: ["birth_date", "person_named"], state_sha256s: ["d".repeat(64)], ...overrides,
});
const catalogued = withheldWork({ work_iri: "http://data.legilux.public.lu/eli/etat/leg/loi/1880/03/03/n1", work_key: null, class: "lu_naturalisation", evidence: ["class"], state_sha256s: [] });
const europe = withheldWork({ publisher: "eu-eurlex", work_iri: "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000092", work_key: null, celex: "32099R0092", class: "eu_restrictive_measures", evidence: ["class"] });

test("a release's two records hold together when no withheld work is offered to search engines", () => {
  const indexingText = indexing([{ address: "/eu-eurlex/32016R0679", languages: ["eng", "fra"] }, { address: "/lu-legilux/code-civil", languages: ["fra"] }]);
  const namesText = names([europe, catalogued, withheldWork()]);
  assert.deepEqual(recordsFailures({ indexingText, namesText }), []);
  assert.deepEqual(readNamesRecord(namesText).works.map(withheldAddress), ["/eu-eurlex/32099R0092", null, "/lu-legilux/loi-2020-03-03-a1"],
    "an act held only in the catalogue has no page to open");
  assert.ok(readIndexingRecord(indexingText) !== null);
});

test("a withheld work offered to search engines, or a record that is not one, or two builds, is a failure", () => {
  const namesText = names([withheldWork(), europe]);
  const both = recordsFailures({ indexingText: indexing([{ address: "/lu-legilux/loi-2020-03-03-a1", languages: ["fra"] }]), namesText });
  assert.ok(both.includes("/lu-legilux/loi-2020-03-03-a1 is both withheld and offered to search engines"), both.join("; "));
  assert.ok(both.some((failure) => failure.startsWith("the sitemap would list") && failure.includes("/lu-legilux/loi-2020-03-03-a1")), both.join("; "));
  // The sitemap the site would serve lists it, which is what the failure guards.
  assert.match(Object.values(sitemapFiles(readIndexingRecord(indexing([{ address: "/lu-legilux/loi-2020-03-03-a1", languages: ["fra"] }])))).join(""),
    /\/fr\/lu-legilux\/loi-2020-03-03-a1</);
  assert.ok(recordsFailures({ indexingText: indexing([{ address: "/eu-eurlex/32099R0092", languages: ["eng"] }]), namesText })
    .includes("/eu-eurlex/32099R0092 is both withheld and offered to search engines"));
  // A work whose key merely starts as a withheld one's is another work.
  assert.deepEqual(recordsFailures({ indexingText: indexing([{ address: "/lu-legilux/loi-2020-03-03-a10", languages: ["fra"] }]), namesText }), []);

  assert.deepEqual(recordsFailures({ indexingText: "not json", namesText }), ["the indexing-record.json the release measured is not one the web root admits"]);
  assert.deepEqual(recordsFailures({ indexingText: indexing([]), namesText: JSON.stringify({ schema: "lex-v3-names-record/2", works: [] }) }),
    ["the names record is not lex-v3-names-record/1"]);
  assert.deepEqual(recordsFailures({ indexingText: indexing([]), namesText: names([{ ...withheldWork(), reason: "licence" }]) }),
    ["the names record holds a work that is not one"]);
  assert.deepEqual(recordsFailures({ indexingText: indexing([], null), namesText: names([]) }),
    [`the two records name different builds: null and ${BUILT_AT}`]);
});

/** A stand-in for the image's API: the answers it gives, by operation and identifier or query. */
function answering(answers) {
  const asked = [];
  const fetchImpl = async (url, init) => {
    const body = JSON.parse(init.body);
    const key = `${body.operation_id} ${body.parameters.identifier ?? body.parameters.query}`;
    asked.push(key);
    const envelope = answers[key];
    return envelope === undefined
      ? { ok: false, status: 404, json: async () => ({}) }
      : { ok: true, status: 200, json: async () => envelope };
  };
  return { fetchImpl, asked };
}

const withheldDossier = { refusal: { code: "text_withheld", helpful_payload: { reason: NAMES_WITHHELD_REASON, official_link: "x", state_sha256s: [] } } };

test("the image answers every work the names record withholds as R92 says, and a failure quotes no answer", async () => {
  const record = { works: [withheldWork(), europe, catalogued] };
  const good = answering({
    "dossier /lu-legilux/loi-2020-03-03-a1": withheldDossier,
    "dossier 32099R0092": withheldDossier,
    [`search ${catalogued.work_iri}`]: { refusal: { code: "identifier_unknown", helpful_payload: { recognised_title: null, recognised_act: catalogued.work_iri } } },
  });
  assert.deepEqual(await namesProbeFailures("http://image.invalid", record, { fetchImpl: good.fetchImpl }), []);
  assert.deepEqual(good.asked, ["dossier /lu-legilux/loi-2020-03-03-a1", "dossier 32099R0092", `search ${catalogued.work_iri}`]);

  const bad = answering({
    "dossier /lu-legilux/loi-2020-03-03-a1": { result: { value: { work_key: "loi-2020-03-03-a1", titles: [{ title: "Loi synthétique" }] } } },
    "dossier 32099R0092": { refusal: { code: "text_withheld", helpful_payload: { rights_disposition: "non_admitting_licence_scl" } } },
    [`search ${catalogued.work_iri}`]: { refusal: { code: "identifier_unknown", helpful_payload: { recognised_title: "Loi synthétique qui accorde la naturalisation au sieur Fictif" } } },
  });
  const failures = await namesProbeFailures("http://image.invalid", record, { fetchImpl: bad.fetchImpl });
  assert.equal(failures.length, 3, failures.join("\n"));
  assert.ok(failures[0].startsWith("the dossier of /lu-legilux/loi-2020-03-03-a1, which the names record withholds, is not text_withheld for R92"));
  assert.ok(failures[1].startsWith("the dossier of 32099R0092"), "a licence's text_withheld is not R92's");
  assert.ok(failures[2].includes("names a title of it"));
  assert.ok(failures.every((failure) => !failure.includes("Fictif") && !failure.includes("synthétique")), "no failure quotes what an answer said");

  const limited = answering({});
  await namesProbeFailures("http://image.invalid", { works: Array.from({ length: 9 }, (_, index) => withheldWork({ work_key: `loi-2020-03-03-a${index}` })) }, { fetchImpl: limited.fetchImpl, limit: 2 });
  assert.equal(limited.asked.length, 2, "at most `limit` of each kind is asked");
});

test("every title an answer carries is found wherever it sits", () => {
  assert.deepEqual(titlesIn({ refusal: { helpful_payload: { recognised_title: null, candidate_titles: [null, "un"], requested_identifier: "x" } } }), ["un"]);
  assert.deepEqual(titlesIn({ result: { value: { acts: [{ title: "deux", work_key: "w" }], work_resolution: { work: { matched_title: "trois" } } } } }), ["deux", "trois"]);
  assert.deepEqual(titlesIn({ result: { value: { reference: { recognised: { act: { title: null } } } } } }), []);
});
