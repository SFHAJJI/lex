// What the site says to search engines (the specification of record's R67; the plan's item 3.6), for the live server
// (`serve-live.mjs`); the API holds the same rules (`V3WebRoot.SearchEngines.cs`), and the tests hold the two to one table
// (`tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json`, its `searchEngines` member).
//
// One release-level switch decides it, `LEX_V3_SEARCH_ENGINES`, read by both servers when they start: `indexed` opens the
// site to search engines, and anything else, its absence included, keeps it out. Kept out is the default because the owner
// has not answered O27 (search-engine indexing), and the plan says: "If unanswered: the pages open but are kept out of
// search engines."
//
//   kept out   robots.txt allows the pages and disallows the API, MCP, the health routes, search and export, as indexed,
//              and names no sitemap (`ROBOTS_KEPT_OUT`): a crawler must fetch a page to read its noindex, which a page
//              robots.txt blocks never is, and a blocked address can still be listed bare (review of #976, F1). Every
//              response carries `X-Robots-Tag: noindex`, every page names `<meta name="robots" content="noindex">`, and no
//              sitemap is offered (`/sitemap.xml` is an address the site does not hold).
//   indexed    robots.txt allows the pages, disallows the same and names the sitemap (`ROBOTS_INDEXED`); a page whose
//              kept-out marker the server may remove (`indexablePage`) is served without it and without the header, a
//              redirect or a move without the header (it is no page: the page it leads to says what it says), and every
//              other response keeps the header.
//
// The pages carry one of two robots meta elements (`Document.jsx`): `NEVER_INDEXED_META` on a page no search engine is ever
// asked to index (search, export, the provision history, compare and radar forms, a law's page as built, a refusal, the 404
// and maintenance pages, the German and Luxembourgish pages), and `KEPT_OUT_META` on a page the switch opens (the home pages,
// Trust and Coverage, and a law's page rendered whole from an answer that shows the version the address answers today). The
// built bytes are kept out: a server that forgot the switch serves a page search engines leave alone.
//
// Names (R92: "keep the text with names out of full-text search and search engines, with an objection contact ... or
// point to the publisher"; the owner's O28): a law's page is opened only for a work the release's indexing record lists
// (`readIndexingRecord`), the outcome of R92's measurement of which classes of acts name private individuals (the plan's
// item 1.11, in the data lane), and that the mounted corpus holds: when the site is indexed, each server asks its API's
// `dossier` of every listed work once, and a work it does not answer is dropped (`searchEngineFacts`, review of #976, F5).
// No release carries a record yet, so with the switch on no law's page is opened and the sitemap lists none: the home pages
// and Trust and Coverage only. A work the record does not name is kept out, whatever the switch.
//
// This module is data and pure functions only.

import { INTERFACE_LANGUAGES, currentAddress, parseSitePath } from "./permalinks.mjs";
import { CANONICAL_HOST } from "./routes.mjs";
import { isUtcInstant } from "./temporal.mjs";

/** The release-level switch's variable, and its one value that opens the site to search engines. */
export const SEARCH_ENGINES_VARIABLE = "LEX_V3_SEARCH_ENGINES";
export const INDEXED = "indexed";
export const KEPT_OUT = "kept-out";

/** The switch's state for a value of the variable: `indexed` only when it says so exactly, else kept out. */
export function searchEnginesState(value) {
  return value === INDEXED ? INDEXED : KEPT_OUT;
}

/** The two robots meta elements a live page carries, as the pages' renderer writes them (`Document.jsx`). */
export const NEVER_INDEXED_META = '<meta name="robots" content="noindex"/>';
export const KEPT_OUT_META = '<meta name="robots" content="noindex" data-search-engines="kept-out"/>';

/** The header every response carries but a page the switch opened. */
export const X_ROBOTS_TAG = "noindex";

/** The site's origin, which the sitemap and robots.txt name whole. */
export const SITE_ORIGIN = `https://${CANONICAL_HOST}`;
export const ROBOTS_PATH = "/robots.txt";
export const SITEMAP_PATH = "/sitemap.xml";
export const ROBOTS_CONTENT_TYPE = "text/plain; charset=utf-8";
export const SITEMAP_CONTENT_TYPE = "application/xml; charset=utf-8";

/**
 * robots.txt's rules, in both states (R67: "robots.txt allows dossiers and reading pages, and disallows /api, /mcp, search
 * results and exports"): the API, the MCP endpoint, the health routes, and each language's search and export pages; the rest
 * is allowed.
 */
const ROBOTS_RULES = Object.freeze([
  "User-agent: *",
  "Disallow: /api/",
  "Disallow: /mcp",
  "Disallow: /health/",
  ...INTERFACE_LANGUAGES.flatMap((language) => [`Disallow: /${language}/search.html`, `Disallow: /${language}/export.html`]),
]);

/**
 * robots.txt with the site kept out: the same rules, and no sitemap. The pages stay crawlable so that a crawler reads their
 * noindex (header and element), which is what keeps them out; `Disallow: /` would hide it, and a blocked address can still be
 * listed bare when linked (review of #976, F1).
 */
export const ROBOTS_KEPT_OUT = `${ROBOTS_RULES.join("\n")}\n`;

/** robots.txt with the site indexed: the same rules, and the sitemap named. */
export const ROBOTS_INDEXED = `${ROBOTS_RULES.join("\n")}\n\nSitemap: ${SITE_ORIGIN}${SITEMAP_PATH}\n`;

/** robots.txt in a state of the switch. */
export function robotsTxt(state) {
  return state === INDEXED ? ROBOTS_INDEXED : ROBOTS_KEPT_OUT;
}

/** The schema of the release's indexing record, R92's outcome for search engines. */
export const INDEXING_RECORD_SCHEMA = "lex-v3-indexing-record/1";

/** The file the API reads it from, beside its corpus mount (`v3-corpus/`). */
export const INDEXING_RECORD_FILE = "indexing-record.json";

/**
 * A sitemap file holds at most this many addresses and bytes (the sitemaps protocol: 50,000 and 50 MiB, uncompressed); more
 * are split into numbered parts a sitemap index names (`sitemapFiles`).
 */
export const SITEMAP_MAXIMUM_URLS = 50_000;
export const SITEMAP_MAXIMUM_BYTES = 50 * 1024 * 1024;

/** The sitemap's numbered parts, `/sitemap-1.xml` on, each of which the index at `/sitemap.xml` names. */
export const SITEMAP_PART = /^\/sitemap-([1-9][0-9]*)\.xml$/;
export const sitemapPartPath = (number) => `/sitemap-${number}.xml`;

/** The pages the switch opens that stand at no law's address: each language's home page and Trust and Coverage. */
export const OPENED_PAGES = Object.freeze(["", "coverage.html"]);

/** The text language of a readable address: the publisher's three lower-case letters. */
const TEXT_LANGUAGE = /^[a-z]{3}$/;

/** A consolidated version's CELEX, whose readable address has moved to its act's: no work of the record. */
const CONSOLIDATED_CELEX = /^0[0-9]{4}[A-Z]{1,2}[0-9]{4,}(?:\([0-9]{2}\))?-[0-9]{8}$/;

/**
 * The release's indexing record, read from its JSON text, or null when it is none, so nothing is opened: `{ builtAt, works:
 * [{ address, languages }] }`. Its rules, each broken one refusing it whole: the schema `lex-v3-indexing-record/1`;
 * `built_at`, the build of the index R92's measurement read, as an instant in UTC (`yyyy-mm-ddThh:mm:ssZ`), or null; and
 * `works`, each work R92's measurement found to name no private individual: its dossier's address with no interface
 * language (`/lu-legilux/{work}`, `/eu-eurlex/{celex}`, never a consolidated version's CELEX) and the text languages its
 * readable address answers a reading in, at least one, in order and each once; the works in their addresses' order, each
 * once. Nothing else is read from it. How many works it lists is no rule of it: a sitemap splits into parts (review of #976,
 * F3), and R92's gate does not hang on a sitemap's size.
 */
export function readIndexingRecord(text) {
  let value;
  try {
    // A byte-order mark is no part of the record, as .NET's reader drops it (`V3WebRoot.ReadIndexingRecordFile`).
    value = JSON.parse(String(text).replace(/^\uFEFF/, ""));
  } catch {
    return null;
  }
  if (value === null || typeof value !== "object" || Array.isArray(value)) return null;
  if (value.schema !== INDEXING_RECORD_SCHEMA) return null;
  const builtAt = value.built_at ?? null;
  if (builtAt !== null && !isUtcInstant(builtAt)) return null;
  if (!Array.isArray(value.works)) return null;
  const works = [];
  for (const work of value.works) {
    if (work === null || typeof work !== "object" || typeof work.address !== "string") return null;
    const named = parseSitePath(work.address);
    if (named === null || named.kind !== "work" || named.locale !== null) return null;
    if (named.publisher === "eu-eurlex" && CONSOLIDATED_CELEX.test(named.identifier)) return null;
    if (works.length > 0 && !(works[works.length - 1].address < work.address)) return null;
    if (!Array.isArray(work.languages) || work.languages.length === 0) return null;
    for (const [index, language] of work.languages.entries()) {
      if (typeof language !== "string" || !TEXT_LANGUAGE.test(language)) return null;
      if (index > 0 && !(work.languages[index - 1] < language)) return null;
    }
    works.push(Object.freeze({ address: work.address, languages: Object.freeze([...work.languages]) }));
  }
  return Object.freeze({ builtAt, works: Object.freeze(works) });
}

/** The `dossier` identifier of a work the record names: a Luxembourg work's address, or an EU act's CELEX. */
export function dossierIdentifierOf(address) {
  return parseSitePath(address)?.identifier ?? null;
}

/**
 * The build time of the mounted index, from the API's `coverage` envelope: the `built_at` of the snapshot its history names
 * `mounted`, an instant in UTC, else null (no corpus, an index that records no build, or anything else).
 */
export function mountedBuiltAt(envelope) {
  const snapshots = envelope?.refusal === null ? envelope?.result?.value?.history?.snapshots_with_text : null;
  if (!Array.isArray(snapshots)) return null;
  const mounted = snapshots.filter((snapshot) => Array.isArray(snapshot?.retained_as) && snapshot.retained_as.includes("mounted"));
  return mounted.length === 1 && isUtcInstant(mounted[0].built_at) ? mounted[0].built_at : null;
}

/** Whether a `dossier` envelope answers the work: a result, and no refusal. */
export function dossierHolds(envelope) {
  return envelope !== null && typeof envelope === "object" && envelope.refusal === null && envelope.result !== null && envelope.result !== undefined;
}

/**
 * What a server says to search engines with the site indexed, from its API's own answers, asked once (review of #976, F5):
 * `lastmod`, the mounted index's build time (`mountedBuiltAt` of `coverage`), and `record`, the indexing record with each
 * work the API's `dossier` does not answer dropped (a work the mount does not hold is never opened nor listed), or null with
 * no record. `ask(operation, parameters)` answers an envelope, or null when there is none (which drops the work, or names no
 * build time).
 */
export async function searchEngineFacts(record, ask) {
  const lastmod = mountedBuiltAt(await ask("coverage", {}));
  if (record === null) return Object.freeze({ record: null, lastmod });
  const works = [];
  for (const work of record.works) {
    if (dossierHolds(await ask("dossier", { identifier: dossierIdentifierOf(work.address) }))) works.push(work);
  }
  return Object.freeze({ record: Object.freeze({ builtAt: record.builtAt, works: Object.freeze(works) }), lastmod });
}

/** A law's address's work, as the indexing record names it (`/lu-legilux/{work}`, `/eu-eurlex/{celex}`), or null. */
export function workAddressOf(pathname) {
  const named = parseSitePath(pathname);
  if (named === null) return null;
  return named.publisher === "lu-legilux" ? named.identifier : `/eu-eurlex/${named.identifier}`;
}

/**
 * Whether a page at `pathname` may be served without its kept-out marker: with the switch on, a page at no law's address,
 * and a law's page whose work the indexing record lists (R92). The page itself says, by its marker, whether the switch
 * opens it at all.
 */
export function indexablePage(state, pathname, record) {
  if (state !== INDEXED) return false;
  if (parseSitePath(pathname) === null) return true;
  const work = workAddressOf(pathname);
  return record !== null && record.works.some((listed) => listed.address === work);
}

/**
 * A page's bytes as served, and whether it is opened: with the switch on and the page indexable (`indexablePage`), the
 * kept-out marker removed (once, as the renderer writes one), else the bytes as they are. `opened` is true only when the
 * marker was removed: a page without it keeps the header, whatever it is.
 */
export function servedPage(html, { state, pathname, record }) {
  if (!indexablePage(state, pathname, record)) return { html, opened: false };
  const at = html.indexOf(KEPT_OUT_META);
  if (at < 0) return { html, opened: false };
  return { html: `${html.slice(0, at)}${html.slice(at + KEPT_OUT_META.length)}`, opened: true };
}

const escapeXml = (text) => text.replace(/[&<>"']/g, (character) => `&#${character.charCodeAt(0)};`);

/** One sitemap entry: the page in one interface language, with its alternates in each and for any other (`x-default`). */
function entry(loc, alternates, lastmod) {
  const links = alternates.map(([hrefLang, href]) => `<xhtml:link rel="alternate" hreflang="${hrefLang}" href="${escapeXml(href)}"/>`).join("");
  return `<url><loc>${escapeXml(loc)}</loc>${lastmod === null ? "" : `<lastmod>${lastmod}</lastmod>`}${links}</url>`;
}

/** Each language's address of one page, and its alternates: each language's, and `xDefault` for any other. */
function entries(addressIn, xDefault, lastmod) {
  const alternates = [...INTERFACE_LANGUAGES.map((language) => [language, `${SITE_ORIGIN}${addressIn(language)}`]), ["x-default", `${SITE_ORIGIN}${xDefault}`]];
  return INTERFACE_LANGUAGES.map((language) => entry(`${SITE_ORIGIN}${addressIn(language)}`, alternates, lastmod));
}

/** The sitemap's entries (R67), in order, each an ASCII line: its addresses are the site's, which escape anything else. */
function sitemapEntries(record, lastmod) {
  const lines = [
    ...entries((language) => `/${language}/`, "/", lastmod),
    ...entries((language) => `/${language}/coverage.html`, `/${INTERFACE_LANGUAGES[0]}/coverage.html`, lastmod),
  ];
  for (const work of record?.works ?? []) {
    lines.push(...entries((language) => `/${language}${work.address}`, work.address, lastmod));
    const named = parseSitePath(work.address);
    const key = named.publisher === "lu-legilux" ? named.identifier.slice("/lu-legilux/".length) : named.identifier;
    for (const language of work.languages) {
      const act = currentAddress({ publisher: named.publisher, work: key, language });
      lines.push(...entries((locale) => `/${locale}${act}`, act, lastmod));
    }
  }
  return lines;
}

const URLSET_HEAD = '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n';
const URLSET_TAIL = "</urlset>\n";

/** The first two lines of the sitemap's index (`/sitemap.xml`), as `sitemapFiles` writes it. */
export const SITEMAP_INDEX_HEAD = '<?xml version="1.0" encoding="UTF-8"?>\n<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n';

/**
 * The sitemap with the site indexed (R67: "A sitemap lists dossiers and current reading pages, with lastmod from the
 * build or acquisition time, never valid_from"), by its paths: `/sitemap.xml`, the index naming each part, and
 * `/sitemap-1.xml` on, each part at most `partSize` addresses and `maximumBytes` bytes, the protocol's by default
 * (review of #976, F3: the population already lists about 29,000). The entries: each interface language's home page
 * (its `x-default` the root, which sends a reader to theirs) and Trust and Coverage (its `x-default` the French page,
 * as the pages' own alternates say); then, for each work the indexing record lists, its dossier and its readable
 * address in each text language it lists (the act's, with no article), each in both interface languages, its
 * `x-default` the address with no interface language. Never a permalink: a permalink pins one version, and the readable
 * address is the current one. `lastmod` is the mounted index's build time (`mountedBuiltAt`, review of #976, F5), on
 * every entry and part, and is left out when it is null.
 */
export function sitemapFiles(record, { lastmod = null, partSize = SITEMAP_MAXIMUM_URLS, maximumBytes = SITEMAP_MAXIMUM_BYTES } = {}) {
  const parts = [];
  let part = [];
  let bytes = URLSET_HEAD.length + URLSET_TAIL.length;
  for (const line of sitemapEntries(record, lastmod)) {
    if (part.length > 0 && (part.length === partSize || bytes + line.length + 1 > maximumBytes)) {
      parts.push(part);
      part = [];
      bytes = URLSET_HEAD.length + URLSET_TAIL.length;
    }
    part.push(line);
    bytes += line.length + 1;
  }
  parts.push(part);
  const files = {
    [SITEMAP_PATH]: `${SITEMAP_INDEX_HEAD}${parts.map((_, index) => `<sitemap><loc>${SITE_ORIGIN}${sitemapPartPath(index + 1)}</loc>${lastmod === null ? "" : `<lastmod>${lastmod}</lastmod>`}</sitemap>\n`).join("")}</sitemapindex>\n`,
  };
  for (const [index, lines] of parts.entries()) files[sitemapPartPath(index + 1)] = `${URLSET_HEAD}${lines.map((line) => `${line}\n`).join("")}${URLSET_TAIL}`;
  return Object.freeze(files);
}
