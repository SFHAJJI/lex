import assert from "node:assert/strict";
import test from "node:test";
import {
  citationText, comparisonCitationText, comparisonEvidenceMarkdown, evidenceFilename,
  lawEvidenceMarkdown, type ComparisonRow,
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

// A multi-article citation has no digest of the wording on screen, and says so. It still names the
// version it came from: the record digest identifies the version metadata, a weaker claim than a
// wording digest and labelled as the weaker one. Before this lane the citation carried neither.
test("a multi-provision citation carries the version-metadata digest", () => {
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
  // Wording narrowed at UI-O4: the absence being stated is specifically an aggregate WORDING
  // digest, since a metadata digest may still be present and is labelled separately.
  assert.match(citation, /no aggregate text digest recorded/);
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

// The Markdown export says what it is. The citation string, which is the artifact people actually
// paste into documents, said nothing at all, so a Lex citation could reach a legal filing reading
// as though it were the publisher's own record.
test("a copied citation says what it is and is not", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    recordSha256: "record-digest-aaa",
    provisions: [{ anchor: "art_1", num: "Article 1", text: "One.", text_sha256: "one" }],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /not an official publication/);
});

test("a copied comparison citation says what it is and is not", () => {
  const citation = comparisonCitationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    from: "2020-01-01",
    to: "2021-01-01",
    permalink: "https://law.soufien.lu/compare",
    fromRecordSha256: "from-record",
    toRecordSha256: "to-record",
    rows: [],
    unchanged: [],
    punctuationOnly: [],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /not an official publication/);
});

// UI-O4. record_sha256 hashes serialized VersionMeta, not the ordered rendered provision text. It
// therefore cannot stand in for a wording digest on a multi-article view. Each digest must stay
// inside the claim it can actually support, and the absence of a wording digest must be stated
// rather than papered over with a metadata one.
test("a multi-provision citation says no aggregate text digest is recorded", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    recordSha256: "record-digest-aaa",
    bodySha256: "body-digest-bbb",
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One.", text_sha256: "one" },
      { anchor: "art_2", num: "Article 2", text: "Two.", text_sha256: "two" },
    ],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /no aggregate text digest recorded/);
});

test("a multi-provision citation labels the record digest as version metadata, not wording", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    recordSha256: "record-digest-aaa",
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One." },
      { anchor: "art_2", num: "Article 2", text: "Two." },
    ],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /record SHA-256 record-digest-aaa \(version metadata\)/);
  assert.doesNotMatch(citation, /^(?!.*no aggregate text digest recorded).*$/s);
});

test("a publisher body digest is carried and labelled separately", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    bodySha256: "body-digest-bbb",
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One." },
      { anchor: "art_2", num: "Article 2", text: "Two." },
    ],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /body SHA-256 body-digest-bbb \(publisher body\)/);
});

test("a single-provision citation still carries the exact wording digest and no aggregate notice", () => {
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
  assert.doesNotMatch(citation, /no aggregate text digest recorded/);
});

// UI-O4 applies to both sides of a comparison for the same reason: a version-metadata digest is not
// a digest of the compared wording, and a multi-row comparison has no single wording digest at all.
test("a multi-row comparison states the absence per side and labels each record digest", () => {
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
  assert.match(citation, /2020-01-01 no aggregate text digest recorded/);
  assert.match(citation, /2021-01-01 no aggregate text digest recorded/);
  assert.match(citation, /2020-01-01 record SHA-256 from-record \(version metadata\)/);
});

// O1. A one-row comparison of an added or removed article has a side where the provision does not
// exist. Saying no digest was recorded there states the wrong condition: it reads as text whose
// digest went unrecorded rather than text that never existed. The rendered comparison already says
// `not in this version`; the copied citation must not contradict it.
const oneRowComparison = (row: ComparisonRow) => comparisonCitationText({
  title: "Sample law",
  work: "lu-legilux:sample",
  from: "2020-01-01",
  to: "2021-01-01",
  permalink: "https://law.soufien.lu/compare",
  fromRecordSha256: "from-record",
  toRecordSha256: "to-record",
  rows: [row],
  unchanged: [],
  punctuationOnly: [],
  exportedAt: "2026-08-29T00:00:00.000Z",
});

test("an added article is reported as absent on the earlier side, not as a missing digest", () => {
  const citation = oneRowComparison(
    { label: "Article 9", anchor: "art_9", kind: "added", pieces: [], toSha: "bb" });
  assert.match(citation, /2020-01-01 not present in this version/);
  assert.doesNotMatch(citation, /2020-01-01 no aggregate text digest recorded/);
  // The absent side carries no digest at all. A record digest there invites the reader to think
  // something about the article can be checked against it, and nothing can.
  assert.doesNotMatch(citation, /2020-01-01 record SHA-256/);
  // The side that does exist keeps its exact wording digest.
  assert.match(citation, /2021-01-01 text SHA-256 bb/);
});

test("a removed article is reported as absent on the later side, not as a missing digest", () => {
  const citation = oneRowComparison(
    { label: "Article 9", anchor: "art_9", kind: "removed", pieces: [], fromSha: "aa" });
  assert.match(citation, /2021-01-01 not present in this version/);
  assert.doesNotMatch(citation, /2021-01-01 no aggregate text digest recorded/);
  assert.doesNotMatch(citation, /2021-01-01 record SHA-256/);
  assert.match(citation, /2020-01-01 text SHA-256 aa/);
});

test("a changed one-row comparison keeps both sides present", () => {
  const citation = oneRowComparison(
    { label: "Article 9", anchor: "art_9", kind: "changed", pieces: [], fromSha: "aa", toSha: "bb" });
  assert.doesNotMatch(citation, /not present in this version/);
  assert.match(citation, /2020-01-01 text SHA-256 aa/);
  assert.match(citation, /2021-01-01 text SHA-256 bb/);
});

test("a multi-row comparison has both sides present regardless of row kinds", () => {
  // Several rows of mixed kinds means each side holds some provisions, so neither side is absent
  // and the aggregate rule applies to both.
  const citation = comparisonCitationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    from: "2020-01-01",
    to: "2021-01-01",
    permalink: "https://law.soufien.lu/compare",
    fromRecordSha256: "from-record",
    toRecordSha256: "to-record",
    rows: [
      { label: "Article 1", anchor: "art_1", kind: "added", pieces: [], toSha: "bb" },
      { label: "Article 2", anchor: "art_2", kind: "removed", pieces: [], fromSha: "aa" },
    ],
    unchanged: [],
    punctuationOnly: [],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.doesNotMatch(citation, /not present in this version/);
  assert.match(citation, /2020-01-01 no aggregate text digest recorded/);
  assert.match(citation, /2021-01-01 no aggregate text digest recorded/);
});
