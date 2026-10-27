// Search with capitals and accents ignored (the plan's item 4.4): the fold this site reads an answer's matches by
// (`text-folding.mjs`), and the words the pages say about it, in English and in French, as the independent French review
// of 2026-10-27 fixed them (`C:\lex-v3\lanes\fr-review\review.md`, its addendum on this search).

import assert from "node:assert/strict";
import test from "node:test";

import { foldText } from "../scripts/text-folding.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { REFUSALS_FR } from "../scripts/live-chrome-fr.mjs";
import { LIVE_SEARCH_REFUSAL_SENTENCES } from "../scripts/live-search.mjs";

test("the fold ignores capitals and accents, writes œ and æ as two letters, and keeps every other character", () => {
  for (const form of ["Congé parental", "congé parental", "conge parental", "CONGÉ PARENTAL", "conge\u0301 parental"]) {
    assert.equal(foldText(form), "conge parental", form);
  }
  assert.equal(foldText("Œuvre æther"), "oeuvre aether");
  assert.equal(foldText("l\u2019article l'article 6\u00A0septembre compte-rendu"), "l\u2019article l'article 6\u00A0septembre compte-rendu",
    "apostrophes, no-break spaces and hyphens are kept");
  assert.throws(() => foldText(null), /not text to fold/);
});

test("the search pages say what the matching is, and no form the French review refused", () => {
  const en = liveChrome("en").search;
  const fr = liveChrome("fr").search;
  assert.match(en.exactOnly, /with capitals and accents ignored and œ and æ read as oe and ae/);
  assert.doesNotMatch(en.exactOnly, /nothing is folded|Only exact matching/, "the text is folded now");
  assert.doesNotMatch(en.runText, /exactly as typed/);
  assert.doesNotMatch(en.intro, /exactly as typed/);
  assert.doesNotMatch(en.euUndated, /left unchanged/, "an article's text, not the article, is shown once");
  assert.doesNotMatch(fr.euUndated, /qui le contient|n’a pas modifié le texte/);
  assert.doesNotMatch(fr.euDated, /que cette date désigne|daté de ce jour/);
  assert.doesNotMatch(fr.exactOnly, /correspondance exacte|ramené à une forme commune/);
  const refusal = REFUSALS_FR.sentences[LIVE_SEARCH_REFUSAL_SENTENCES.retrieval_mode_unavailable];
  assert.equal(typeof refusal, "string", "the EU date refusal is said in French");
  assert.doesNotMatch(refusal, /, et l’interroge/);
});
