// The Quality page's expected-article results (the owner's order of 2026-11-13, section D): every run's results by kind add
// up to its questions, the wrong-article figure is the latest run's own, the held-out score is shown apart from the test
// questions', the cost is said as an estimate, and the known failures are listed.

import assert from "node:assert/strict";
import test from "node:test";

import { renderLiveReferencePage } from "../.react-build/app.mjs";
import { QUALITY_KNOWN, QUALITY_OUTCOMES, QUALITY_SET_RUNS } from "../scripts/assistant-quality.mjs";
import { LIVE_CHROME } from "../scripts/live-chrome.mjs";

test("each run's results by kind add up to its questions, test and held-out apart", () => {
  for (const run of QUALITY_SET_RUNS) {
    for (const set of ["dev", "heldout"]) {
      const counts = run[set];
      assert.equal(QUALITY_OUTCOMES.reduce((sum, outcome) => sum + counts[outcome], 0), counts.cases, `${run.date} ${set}`);
    }
    assert.ok(run.costEur > 0 && run.costEur < 0.01, "a cost per question, in euros");
  }
});

test("the Quality page shows the latest wrong articles, the held-out score, the estimated cost and the known failures", () => {
  const html = renderLiveReferencePage("quality");
  const latest = QUALITY_SET_RUNS.at(-1);
  const copy = LIVE_CHROME.en.quality;
  assert.match(html, new RegExp(`data-quality-tile="wrongArticle"><strong>${latest.dev.wrongArticle}</strong>`));
  assert.match(html, new RegExp(`data-quality-tile="heldout"><strong>${latest.heldout.rightArticle} of ${latest.heldout.cases}</strong>`));
  assert.ok(html.includes(copy.setTiles.cost.label), "the cost is said as an estimate");
  assert.equal((html.match(/data-quality-outcome="/g) ?? []).length, QUALITY_OUTCOMES.length, "a row per kind of result");
  assert.ok(html.includes(`data-quality-outcomes="${QUALITY_SET_RUNS.length}"`));
  assert.ok(html.includes(`data-quality-known="${QUALITY_KNOWN.length}"`));
  for (const key of QUALITY_KNOWN) assert.ok(html.includes(copy.known[key].slice(0, 30)), key);
  assert.ok(html.indexOf('data-reference-section="outcomes"') < html.indexOf('data-reference-section="runs"'));
});
