import assert from "node:assert/strict";
import test from "node:test";
import {
  citationText, comparisonCitationText, comparisonEvidenceMarkdown, evidenceFilename,
  lawEvidenceMarkdown,
} from "./export.ts";

test("law evidence preserves legal text and records provenance", () => {
  const text = "Exact publisher wording.\nSecond paragraph.";
  const input = {
    title: "Sample law",
    work: "eu-eurlex:32016R0679",
    validFrom: "2018-05-25",
    language: "en",
    source: "https://eur-lex.example/source",
    permalink: "https://law.soufien.lu/eu-eurlex/32016R0679/2018-05-25#art_1",
    extractionProfile: "akn-eu/1",
    provisions: [{ anchor: "art_1", num: "Article 1", text, text_sha256: "abc123" }],
    exportedAt: "2026-08-06T16:00:00.000Z",
  };

  const markdown = lawEvidenceMarkdown(input);
  assert.match(markdown, /Reading aid exported from Lex/);
  assert.match(markdown, /Official source: https:\/\/eur-lex\.example\/source/);
  assert.match(markdown, /Text SHA-256: abc123/);
  assert.ok(markdown.includes(text));
  assert.match(citationText(input), /Article 1 \| version in force from 2018-05-25/);
});

test("comparison evidence reconstructs both exact sides from the displayed diff", () => {
  const markdown = comparisonEvidenceMarkdown({
    title: "Sample law",
    work: "lu-legilux:sample",
    from: "2020-01-01",
    to: "2021-01-01",
    permalink: "https://law.soufien.lu/compare",
    rows: [{
      label: "Art. 1",
      anchor: "art_1",
      kind: "changed",
      pieces: [
        { k: " ", t: "The rate is " },
        { k: "-", t: "five" },
        { k: "+", t: "six" },
        { k: " ", t: " percent." },
      ],
      fromSha: "old-sha",
      toSha: "new-sha",
    }],
    unchanged: ["Art. 2"],
    punctuationOnly: ["Art. 3"],
    exportedAt: "2026-08-06T16:00:00.000Z",
  });

  assert.match(markdown, /The rate is five percent\./);
  assert.match(markdown, /The rate is six percent\./);
  assert.match(markdown, /2020-01-01 text SHA-256: old-sha/);
  assert.match(markdown, /2021-01-01 text SHA-256: new-sha/);
  assert.match(markdown, /Punctuation-only differences/);
  assert.match(markdown, /Identical provisions/);
  assert.match(comparisonCitationText({
    title: "Sample law", work: "lu-legilux:sample", from: "2020-01-01", to: "2021-01-01",
    permalink: "https://law.soufien.lu/compare", rows: [{
      label: "Art. 1", anchor: "art_1", kind: "changed", pieces: [], fromSha: "old-sha", toSha: "new-sha",
    }], unchanged: [], punctuationOnly: [], exportedAt: "2026-08-06T16:00:00.000Z",
  }), /Art\. 1 \| comparison 2020-01-01 to 2021-01-01.*old-sha.*new-sha/);
});

test("evidence filename is portable", () => {
  assert.equal(evidenceFilename("EU EUR-Lex:32016R0679", "Article 1 / 2026"), "eu-eur-lex-32016r0679-article-1-2026.md");
});
