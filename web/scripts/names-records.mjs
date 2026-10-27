// R92's two records beside a release's mount (the plan's item 1.11): the indexing record (each work R92's measurement found
// to name no private individual, which search engines, the sitemap and the home page's examples may name) and the names record
// (each work withheld because it names private individuals, which the API answers `text_withheld` for). The release measures
// its own mount (`Lex.V3.Tool names-records`, read-only, never writing into the mount), carries both records beside the mount
// in its image, and holds them here:
// - `recordsFailures`: the two records read as their readers read them, and no work in both, so the sitemap lists no law page
//   of a withheld work;
// - `namesProbeFailures`: the running image's answers for the works its names record withholds: each one its index holds is
//   `text_withheld` with R92's reason on its dossier, and an act the catalogue names only by a title is named by a search for
//   its ELI without that title.
// Neither record holds a name or any other personal data, and no failure here quotes an answer's text: only identifiers.

import { INDEXING_RECORD_FILE, readIndexingRecord, sitemapFiles } from "./search-engines.mjs";

export const NAMES_RECORD_FILE = "names-record.json";
export const NAMES_RECORD_SCHEMA = "lex-v3-names-record/1";
export const NAMES_MEASUREMENT_FILE = "names-measurement.json";
export const NAMES_WITHHELD_REASON = "names_private_individuals";
export { INDEXING_RECORD_FILE };

/**
 * The names record's works from its JSON text, in the shape the release reads (the API holds the whole record to the mount's
 * build and refuses a broken one): `{ measured, works: [{ publisher, work_iri, work_key, celex, class }] }`. Throws when it is
 * not one.
 */
export function readNamesRecord(text) {
  const value = JSON.parse(String(text).replace(/^﻿/, ""));
  if (value === null || typeof value !== "object" || value.schema !== NAMES_RECORD_SCHEMA || !Array.isArray(value.works)) {
    throw new Error(`the names record is not ${NAMES_RECORD_SCHEMA}`);
  }
  for (const work of value.works) {
    if (work === null || typeof work !== "object" || !["lu-legilux", "eu-eurlex"].includes(work.publisher) || typeof work.work_iri !== "string" ||
      work.reason !== NAMES_WITHHELD_REASON) {
      throw new Error("the names record holds a work that is not one");
    }
  }
  return Object.freeze({ measured: value.measured, works: Object.freeze(value.works) });
}

/** The dossier address of a withheld work the site could open (one the index holds), or null for an act it holds no version of. */
export function withheldAddress(work) {
  // A CELEX is one segment, its solidus written %2F, as the site addresses it (the Treaties' 12016E%2FTXT).
  if (work.publisher === "eu-eurlex") return work.celex ? `/eu-eurlex/${work.celex.replaceAll("/", "%2F")}` : null;
  return work.work_key ? `/lu-legilux/${work.work_key}` : null;
}

/**
 * What is wrong with a release's two records, as failures (empty when they hold): the indexing record must be one the web
 * root admits, the names record one of its schema, both measured over the same build, no withheld work listed in the indexing
 * record, and so no address of a withheld work in the sitemap the site would serve once indexed.
 */
export function recordsFailures({ indexingText, namesText }) {
  const failures = [];
  const indexing = readIndexingRecord(indexingText);
  if (indexing === null) failures.push(`the ${INDEXING_RECORD_FILE} the release measured is not one the web root admits`);
  let names = null;
  try {
    names = readNamesRecord(namesText);
  } catch (error) {
    failures.push(error.message);
  }
  if (indexing === null || names === null) return failures;
  if ((indexing.builtAt ?? null) !== (names.measured?.built_at ?? null)) {
    failures.push(`the two records name different builds: ${indexing.builtAt} and ${names.measured?.built_at}`);
  }
  const withheld = new Set(names.works.map(withheldAddress).filter((address) => address !== null));
  for (const work of indexing.works) {
    if (withheld.has(work.address)) failures.push(`${work.address} is both withheld and offered to search engines`);
  }
  const listed = Object.values(sitemapFiles(indexing)).join("");
  for (const address of withheld) {
    for (const locale of ["", "/fr", "/en"]) {
      if (new RegExp(`<loc>https://[^<]*${escape(`${locale}${address}`)}(?:/|<)`).test(listed)) {
        failures.push(`the sitemap would list ${locale}${address}, a work R92 withholds`);
      }
    }
  }
  return failures;
}

function escape(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * The running image's answers for the works its names record withholds, as failures (empty when they hold), at most `limit`
 * of each kind: a work the index holds answers its `dossier` `text_withheld` with R92's reason and no title; an act the
 * catalogue names only by a title (no version held) is named by a search for its ELI with no title. `fetchImpl` stands for
 * `fetch`. A failure names the work's identifier, never anything an answer said of it.
 */
export async function namesProbeFailures(origin, names, { limit = 5, fetchImpl = fetch } = {}) {
  const failures = [];
  const ask = async (operation, parameters) => {
    const response = await fetchImpl(`${origin}/api/v3/${operation}`, {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ operation_id: operation, parameters }),
    });
    return response.ok ? response.json() : { status: response.status };
  };
  const held = names.works.filter((work) => withheldAddress(work) !== null).slice(0, limit);
  for (const work of held) {
    const identifier = work.publisher === "eu-eurlex" ? work.celex : withheldAddress(work);
    const envelope = await ask("dossier", { identifier });
    const refusal = envelope?.refusal ?? null;
    if (refusal?.code !== "text_withheld" || refusal.helpful_payload?.reason !== NAMES_WITHHELD_REASON) {
      failures.push(`the dossier of ${identifier}, which the names record withholds, is not text_withheld for R92: ${refusal?.code ?? envelope?.status ?? "an answer"}`);
    }
  }
  const catalogued = names.works.filter((work) => work.publisher === "lu-legilux" && work.work_key === null).slice(0, limit);
  for (const work of catalogued) {
    const envelope = await ask("search", { query: work.work_iri, language: "fra" });
    if (envelope?.refusal === undefined && envelope?.result === undefined) {
      failures.push(`a search for the ELI of ${work.work_iri} was not answered: ${envelope?.status}`);
    } else if (titlesIn(envelope).length > 0) {
      failures.push(`a search for the ELI of ${work.work_iri}, an act the names record withholds, names a title of it`);
    }
  }
  return failures;
}

/** The members of an answer that carry a title (an act's, a card's, a recognised reference's, a candidate's), when not null. */
const TITLE_MEMBERS = new Set(["title", "titles", "short_titles", "matched_title", "recognised_title", "candidate_titles"]);

/** Every title an envelope carries anywhere in it, as values (empty when none): what a withheld act's answer must not hold. */
export function titlesIn(value) {
  const found = [];
  const walk = (node) => {
    if (Array.isArray(node)) node.forEach(walk);
    else if (node !== null && typeof node === "object") {
      for (const [key, member] of Object.entries(node)) {
        if (TITLE_MEMBERS.has(key)) {
          for (const title of Array.isArray(member) ? member : [member]) if (title !== null && title !== undefined) found.push(title);
        } else {
          walk(member);
        }
      }
    }
  };
  walk(value);
  return found;
}
