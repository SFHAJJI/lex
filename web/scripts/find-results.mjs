// The V4 search results (the owner's brief of 2026-11-15, section 5): what the search page does with the `search` answer
// for the words the header's field brought after "#".
//
// - The words read as an exact reference that opens (`reference.phase` exact_reference, `reference.outcome` opens): the
//   page goes straight to the text page at that article and that version's date (`openedHref`), with no model call.
// - Otherwise the matching texts by title, one row each (`findRows`): the Luxembourg acts whose title the words match (the
//   title ladder, `work_resolution`), then the Luxembourg acts whose articles hold the words (`acts`, in the answer's order),
//   then the EU acts whose title matches (`europe_titles`); each text once, each by its own title (never its identifier: a
//   text with no title held is not listed), each a link to the text page.
//
// Data and pure functions only: the page and the tests share it.

import { textHref } from "./text-address.mjs";
import { titleShown } from "./live-home.mjs";

/** The text page's link for an exact reference that opens (the search view's `opens`), or null for anything else. */
export function openedHref(view) {
  if (view?.kind !== "reference" || view.outcome !== "opens" || view.opens === null) return null;
  const { publisher, work, stateDate, articleId } = view.opens;
  const identifier = publisher === "eu-eurlex" ? work : `/lu-legilux/${work}`;
  return textHref({ identifier, date: stateDate, article: articleId });
}

/** The kind of an EU act by its CELEX's letter (R regulation, L directive, D decision), else `eu`. */
export function europeKind(celex) {
  const match = /^3\d{4}([RLD])\d{4}$/u.exec(String(celex));
  return match === null ? "eu" : match[1];
}

/**
 * The texts a search view lists, each `{ key, identifier, title, titleLanguage, kind, href }`, in the order the rule above
 * says, each text once: `kind` is `lu` for a Luxembourg act and the CELEX letter for an EU act.
 */
export function findRows(view, query = "") {
  if (view === null || typeof view !== "object" || view.kind === "reference") return [];
  const rows = [];
  const seen = new Set();
  const add = (identifier, title, titleLanguage, kind) => {
    const shown = titleShown(title);
    if (shown === null || seen.has(identifier)) return;
    const href = textHref({ identifier });
    if (href === null) return;
    seen.add(identifier);
    rows.push(Object.freeze({ key: identifier, identifier, title: shown, titleLanguage: titleLanguage ?? null, kind, href }));
  };
  const resolution = view.workResolution ?? null;
  const cards = resolution === null ? [] : resolution.outcome === "one_work" ? [resolution.work] : resolution.candidates ?? [];
  for (const card of cards) {
    if (typeof card?.workKey === "string") add(`/lu-legilux/${card.workKey}`, card.matchedTitle, card.matchedTitleLanguage, "lu");
  }
  for (const act of view.acts ?? []) {
    if (typeof act?.workKey === "string") add(`/lu-legilux/${act.workKey}`, act.title, act.titleLanguage, "lu");
  }
  for (const act of view.europeTitles?.acts ?? []) add(act.seedCelex, act.title, act.titleLanguage, europeKind(act.seedCelex));
  return byTitleMatch(rows, query);
}

/** Text compared for a title match: lower case, no accents, one apostrophe, one space. */
const folded = (text) => String(text ?? "").normalize("NFD").replace(/\p{M}/gu, "").replace(/[’‘ʼ]/gu, "'").replace(/\s+/gu, " ").trim().toLowerCase();

/**
 * The rows with the texts whose own title holds the words searched first, the shortest such title first (the text that is
 * about them before the acts that amend it: "contrat d'assurance" lists the insurance contract law before the laws that
 * modify it), every other row after them in the search's own order. The search's ranking is the engine's and is unchanged.
 */
export function byTitleMatch(rows, query) {
  const words = folded(query);
  if (words.length === 0) return rows;
  const matching = rows.filter((row) => folded(row.title).includes(words));
  const others = rows.filter((row) => !folded(row.title).includes(words));
  return [...[...matching].sort((a, b) => a.title.length - b.title.length), ...others];
}

/** How many rows the page shows before it says how many more hold the words. */
export const FIND_ROWS_SHOWN = 20;
