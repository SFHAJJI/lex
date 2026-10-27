// The three reference pages the specification of record's launch set names beside the screens (§3, Screens: "Developers",
// "Get help", "How to read Lex"; R55, R42, R46, R48, R50): the Developers page, generated from the operation registry and
// the published schemas (`developers.mjs`), Get help, and How to read Lex with its glossary, in each interface language,
// under its path (`/en/developers.html`, `/fr/…`).
//
// Their words are the chrome table's (`live-chrome.mjs`, the sections `developers`, `help`, `guide` and `reference`), so the
// French is the reviewed table's (Decision 41). Like the legal pages, they ask nothing, ship no script and are never indexed:
// each is a page of the site as built, complete without JavaScript (R67), its footer's freshness strip saying it carries no
// answer. Every footer links the three (`Document.jsx`), and the home page links Developers and Get help (§3, Shell and home).
//
// The glossary's terms are the site's own words: each is an entry of the chrome table, named by its path, and said as that
// entry says it in the page's language (`glossaryTerm`), so a term cannot drift from the page that prints it. A template's
// term is its words before its first placeholder ("Text digest (SHA-256): {digest}" is "Text digest (SHA-256)").
//
// This module is data and pure functions only.

import { INTERFACE_LANGUAGES } from "./permalinks.mjs";

/** The three pages: each one's chrome section, its file under each interface language, and its state, in the footer's order. */
export const REFERENCE_PAGES = Object.freeze([
  Object.freeze({ key: "help", file: "help.html", state: "live-help" }),
  Object.freeze({ key: "guide", file: "how-to-read.html", state: "live-guide" }),
  Object.freeze({ key: "developers", file: "developers.html", state: "live-developers" }),
]);

/** The page of a chrome section's key, or undefined. */
export const referencePage = (key) => REFERENCE_PAGES.find((page) => page.key === key);

/** Every reference page's address, in every interface language (French first, as `INTERFACE_LANGUAGES` orders them). */
export const REFERENCE_PAGE_PATHS = Object.freeze(INTERFACE_LANGUAGES.flatMap((language) => REFERENCE_PAGES.map((page) => `/${language}/${page.file}`)));

/**
 * The glossary of How to read Lex: each definition's key in `guide.glossary.terms`, and the chrome entry whose words are
 * its term, by its path in the table. The page lists them in the order of their terms in its own language.
 */
export const GLOSSARY = Object.freeze([
  Object.freeze({ key: "appliesFrom", term: "common.appliesFrom" }),
  Object.freeze({ key: "versionApplicable", term: "search.version" }),
  Object.freeze({ key: "asPublished", term: "common.asPublished.label" }),
  Object.freeze({ key: "entryIntoForce", term: "common.europeForceDates.roleNames.entry_into_force" }),
  Object.freeze({ key: "application", term: "common.europeForceDates.roleNames.application" }),
  Object.freeze({ key: "endOfValidity", term: "common.europeForceDates.roleNames.end_of_validity" }),
  Object.freeze({ key: "wordingDate", term: "dossier.wordingDate" }),
  Object.freeze({ key: "wordingChanged", term: "history.wording.changed" }),
  Object.freeze({ key: "wordingUnchanged", term: "history.wording.unchanged" }),
  Object.freeze({ key: "catalogue", term: "dossier.chainHeading" }),
  Object.freeze({ key: "withoutText", term: "export.withoutTextHeading" }),
  Object.freeze({ key: "notAdmitted", term: "dossier.articlesNotAdmitted" }),
  Object.freeze({ key: "absence", term: "refusalCard.absenceHeading" }),
  Object.freeze({ key: "permalink", term: "common.permalink" }),
  Object.freeze({ key: "usualCitation", term: "reading.citeConventional" }),
  Object.freeze({ key: "withPermalink", term: "reading.citeWithPermalink" }),
  Object.freeze({ key: "evidenceForm", term: "reading.citeEvidence" }),
  Object.freeze({ key: "textDigest", term: "citation.evidence.textSha256" }),
  Object.freeze({ key: "today", term: "form.today" }),
  Object.freeze({ key: "dossier", term: "site.dossier" }),
  Object.freeze({ key: "changes", term: "site.radar" }),
  Object.freeze({ key: "coverage", term: "coverage.title" }),
  Object.freeze({ key: "freshness", term: "freshness.heading" }),
  Object.freeze({ key: "stale", term: "freshness.staleHeading" }),
  Object.freeze({ key: "indexCommit", term: "freshness.indexCommit" }),
  Object.freeze({ key: "serviceCommit", term: "freshness.serviceCommit" }),
]);

/** The entry of `table` at a dotted path, or undefined. */
export function entryAt(table, path) {
  let node = table;
  for (const key of path.split(".")) node = node !== null && typeof node === "object" && Object.hasOwn(node, key) ? node[key] : undefined;
  return node;
}

/**
 * A glossary term as `table` says it: the entry at `path`, or for a template its words before its first placeholder, with
 * the colon and the spaces that lead to the value taken off. Throws when the path names no entry, or leaves no words.
 */
export function glossaryTerm(table, path) {
  const entry = entryAt(table, path);
  if (typeof entry !== "string") throw new Error(`the glossary's term ${path} is no entry of the chrome table`);
  const term = entry.split("{")[0].replace(/[\s  ]*:?[\s  ]*$/u, "");
  if (!/\p{L}{2}/u.test(term)) throw new Error(`the glossary's term ${path} leaves no words before its first value: ${JSON.stringify(entry)}`);
  return term;
}

/** The glossary in `locale`'s table: each row's key, term and definition, in the order of the terms in that language. */
export function glossaryRows(table, locale) {
  const collator = new Intl.Collator(locale, { sensitivity: "base" });
  return GLOSSARY.map(({ key, term }) => {
    const definition = table.guide.glossary.terms[key];
    if (typeof definition !== "string") throw new Error(`the glossary's ${key} has no definition`);
    return { key, term: glossaryTerm(table, term), definition };
  }).sort((a, b) => collator.compare(a.term, b.term));
}
