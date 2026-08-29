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
  assert.match(citationText(input), /Article 1 \| publisher version dated 2018-05-25/);
  assert.match(markdown, /Publisher version date: 2018-05-25 to latest held/);
});

test("comparison evidence reconstructs both exact sides from the displayed diff", () => {
  const markdown = comparisonEvidenceMarkdown({
    title: "Sample law",
    work: "lu-legilux:sample",
    from: "2020-01-01",
    to: "2021-01-01",
    permalink: "https://law.soufien.lu/compare",
    fromLexId: "eu-eurlex:32016R0679:2018-05-25",
    toLexId: "eu-eurlex:32016R0679:2021-06-28",
    fromVersionValidFrom: "2018-05-25",
    fromVersionValidTo: "2021-06-27",
    toVersionValidFrom: "2021-06-28",
    fromExtractionProfile: "xhtml-eu/1",
    toExtractionProfile: "xhtml-eu/1",
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
  assert.match(markdown, /2020-01-01 applicable Lex ID: eu-eurlex:32016R0679:2018-05-25/);
  assert.match(markdown, /2020-01-01 applicable interval: 2018-05-25 to 2021-06-27/);
  assert.match(markdown, /2021-01-01 applicable interval: 2021-06-28 to open/);
  assert.match(markdown, /2020-01-01 extraction profile: xhtml-eu\/1/);
  assert.match(markdown, /2021-01-01 extraction profile: xhtml-eu\/1/);
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

// A citation that carries no content digest cannot be checked, and this product's whole claim is
// that an answer can be checked rather than trusted. The digest was previously emitted only when
// exactly one provision was on screen, so every whole-document and multi-article citation left
// with a permalink and nothing to verify against.
test("a multi-provision citation still carries a checkable digest", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    recordSha256: "record-digest-aaa",
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One.", text_sha256: "one" },
      { anchor: "art_2", num: "Article 2", text: "Two.", text_sha256: "two" },
    ],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /record SHA-256 record-digest-aaa/);
});

test("a citation with no digest available says so instead of omitting it", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One." },
      { anchor: "art_2", num: "Article 2", text: "Two." },
    ],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /no content digest recorded/);
});

test("a single-provision citation still prefers the exact text digest", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    recordSha256: "record-digest-aaa",
    provisions: [{ anchor: "art_1", num: "Article 1", text: "One.", text_sha256: "exact-one" }],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /text SHA-256 exact-one/);
});

test("a multi-row comparison citation carries digests for both sides", () => {
  const citation = comparisonCitationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    from: "2020-01-01",
    to: "2021-01-01",
    permalink: "https://law.soufien.lu/compare",
    fromRecordSha256: "from-record",
    toRecordSha256: "to-record",
    rows: [
      { label: "Article 1", anchor: "art_1", kind: "changed", pieces: [], fromSha: "a", toSha: "b" },
      { label: "Article 2", anchor: "art_2", kind: "changed", pieces: [], fromSha: "c", toSha: "d" },
    ],
    unchanged: [],
    punctuationOnly: [],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /2020-01-01 record SHA-256 from-record/);
  assert.match(citation, /2021-01-01 record SHA-256 to-record/);
});
