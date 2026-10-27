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
  // The review of #1005: the typographic apostrophes as the straight one, the no-break spaces as a space, a soft hyphen dropped.
  assert.equal(foldText("l\u2019article l\u2018article l\u02BCarticle 6\u00A0septembre 6\u202Fkm obli\u00ADgation"),
    "l'article l'article l'article 6 septembre 6 km obligation");
  assert.equal(foldText("compte-rendu a\u2013b \u00AB x \u00BB"), "compte-rendu a\u2013b \u00AB x \u00BB", "hyphens, dashes and quotation marks are kept");
  assert.throws(() => foldText(null), /not text to fold/);
});

test("the search pages say what the matching is, and no form the French review refused", () => {
  const en = liveChrome("en").search;
  const fr = liveChrome("fr").search;
  assert.match(en.exactOnly, /with capitals and accents ignored, œ and æ read as oe and ae, the typographic apostrophes as the straight one, a no-break space as a space and a soft hyphen as nothing:/);
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
