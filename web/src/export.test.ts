import assert from "node:assert/strict";
import test from "node:test";
import {
  citationText, comparisonCitationText, comparisonEvidenceMarkdown, evidenceFilename,
  lawEvidenceMarkdown, type ComparisonRow, type ComparisonScope,
} from "./export.ts";

/**
 * A well-formed digest for a fixture.
 *
 * The citation boundary now refuses to call anything a SHA-256 unless it is 64 lowercase
 * hexadecimal characters, so a readable placeholder would silently put every fixture on the
 * refusal path. `mint` keeps them readable and well formed at once: the seed stays visible in
 * the value, so a failing assertion still names the fixture it was looking at.
 */
const mint = (seed: string): string => {
  const hexed = [...seed]
    .map((character) => (character.codePointAt(0) ?? 0).toString(16).padStart(2, "0"))
    .join("");
  return `${hexed}${"f".repeat(64)}`.slice(0, 64);
};
const ABC123 = mint("abc123");
const OLD_SHA = mint("old-sha");
const NEW_SHA = mint("new-sha");
const RECORD_A = mint("record-digest-aaa");
const BODY_B = mint("body-digest-bbb");
const EXACT_ONE = mint("exact-one");
const ONE = mint("one");
const TWO = mint("two");
const FROM_RECORD = mint("from-record");
const TO_RECORD = mint("to-record");
const ROW_A = mint("a");
const ROW_B = mint("b");
const ROW_C = mint("c");
const ROW_D = mint("d");
const SHA_AA = mint("aa");
const SHA_BB = mint("bb");
const SAME = mint("same");


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
    provisions: [{ anchor: "art_1", num: "Article 1", text, text_sha256: ABC123 }],
    exportedAt: "2026-08-06T16:00:00.000Z",
  };

  const markdown = lawEvidenceMarkdown(input);
  assert.match(markdown, /Reading aid exported from Lex/);
  assert.match(markdown, /Official source: https:\/\/eur-lex\.example\/source/);
  assert.match(markdown, new RegExp(`Text SHA-256: ${ABC123}`));
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
      fromSha: OLD_SHA,
      toSha: NEW_SHA,
    }],
    unchanged: ["Art. 2"],
    punctuationOnly: ["Art. 3"],
    exportedAt: "2026-08-06T16:00:00.000Z",
  });

  assert.match(markdown, /The rate is five percent\./);
  assert.match(markdown, /The rate is six percent\./);
  assert.match(markdown, new RegExp(`2020-01-01 text SHA-256: ${OLD_SHA}`));
  assert.match(markdown, new RegExp(`2021-01-01 text SHA-256: ${NEW_SHA}`));
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
      label: "Art. 1", anchor: "art_1", kind: "changed", pieces: [], fromSha: OLD_SHA, toSha: NEW_SHA,
    }], unchanged: [], punctuationOnly: [], exportedAt: "2026-08-06T16:00:00.000Z",
  }), new RegExp(`Art\. 1 \| comparison 2020-01-01 to 2021-01-01.*${OLD_SHA}.*${NEW_SHA}`));
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
    recordSha256: RECORD_A,
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One.", text_sha256: ONE },
      { anchor: "art_2", num: "Article 2", text: "Two.", text_sha256: TWO },
    ],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, new RegExp(`record SHA-256 ${RECORD_A}`));
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
    recordSha256: RECORD_A,
    provisions: [{ anchor: "art_1", num: "Article 1", text: "One.", text_sha256: EXACT_ONE }],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, new RegExp(`text SHA-256 ${EXACT_ONE}`));
});

test("a multi-row comparison citation carries digests for both sides", () => {
  const citation = comparisonCitationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    from: "2020-01-01",
    to: "2021-01-01",
    permalink: "https://law.soufien.lu/compare",
    fromRecordSha256: FROM_RECORD,
    toRecordSha256: TO_RECORD,
    rows: [
      { label: "Article 1", anchor: "art_1", kind: "changed", pieces: [], fromSha: ROW_A, toSha: ROW_B },
      { label: "Article 2", anchor: "art_2", kind: "changed", pieces: [], fromSha: ROW_C, toSha: ROW_D },
    ],
    unchanged: [],
    punctuationOnly: [],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, new RegExp(`2020-01-01 record SHA-256 ${FROM_RECORD}`));
  assert.match(citation, new RegExp(`2021-01-01 record SHA-256 ${TO_RECORD}`));
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
    recordSha256: RECORD_A,
    provisions: [{ anchor: "art_1", num: "Article 1", text: "One.", text_sha256: ONE }],
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
    fromRecordSha256: FROM_RECORD,
    toRecordSha256: TO_RECORD,
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
    recordSha256: RECORD_A,
    bodySha256: BODY_B,
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One.", text_sha256: ONE },
      { anchor: "art_2", num: "Article 2", text: "Two.", text_sha256: TWO },
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
    recordSha256: RECORD_A,
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One." },
      { anchor: "art_2", num: "Article 2", text: "Two." },
    ],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, new RegExp(`record SHA-256 ${RECORD_A} \\(version metadata\\)`));
  assert.doesNotMatch(citation, /^(?!.*no aggregate text digest recorded).*$/s);
});

test("a publisher body digest is carried and labelled separately", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    bodySha256: BODY_B,
    provisions: [
      { anchor: "art_1", num: "Article 1", text: "One." },
      { anchor: "art_2", num: "Article 2", text: "Two." },
    ],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, new RegExp(`body SHA-256 ${BODY_B} \\(publisher body\\)`));
});

test("a single-provision citation still carries the exact wording digest and no aggregate notice", () => {
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    recordSha256: RECORD_A,
    provisions: [{ anchor: "art_1", num: "Article 1", text: "One.", text_sha256: EXACT_ONE }],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, new RegExp(`text SHA-256 ${EXACT_ONE}`));
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
    fromRecordSha256: FROM_RECORD,
    toRecordSha256: TO_RECORD,
    rows: [
      { label: "Article 1", anchor: "art_1", kind: "changed", pieces: [], fromSha: ROW_A, toSha: ROW_B },
      { label: "Article 2", anchor: "art_2", kind: "changed", pieces: [], fromSha: ROW_C, toSha: ROW_D },
    ],
    unchanged: [],
    punctuationOnly: [],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, /2020-01-01 no aggregate text digest recorded/);
  assert.match(citation, /2021-01-01 no aggregate text digest recorded/);
  assert.match(citation, new RegExp(`2020-01-01 record SHA-256 ${FROM_RECORD} \\(version metadata\\)`));
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
  fromRecordSha256: FROM_RECORD,
  toRecordSha256: TO_RECORD,
  rows: [row],
  unchanged: [],
  punctuationOnly: [],
  exportedAt: "2026-08-29T00:00:00.000Z",
});

test("an added article is reported as absent on the earlier side, not as a missing digest", () => {
  const citation = oneRowComparison(
    { label: "Article 9", anchor: "art_9", kind: "added", pieces: [], toSha: SHA_BB });
  assert.match(citation, /2020-01-01 not present in this version/);
  assert.doesNotMatch(citation, /2020-01-01 no aggregate text digest recorded/);
  // The absent side carries no digest at all. A record digest there invites the reader to think
  // something about the article can be checked against it, and nothing can.
  assert.doesNotMatch(citation, /2020-01-01 record SHA-256/);
  // The side that does exist keeps its exact wording digest.
  assert.match(citation, new RegExp(`2021-01-01 text SHA-256 ${SHA_BB}`));
});

test("a removed article is reported as absent on the later side, not as a missing digest", () => {
  const citation = oneRowComparison(
    { label: "Article 9", anchor: "art_9", kind: "removed", pieces: [], fromSha: SHA_AA });
  assert.match(citation, /2021-01-01 not present in this version/);
  assert.doesNotMatch(citation, /2021-01-01 no aggregate text digest recorded/);
  assert.doesNotMatch(citation, /2021-01-01 record SHA-256/);
  assert.match(citation, new RegExp(`2020-01-01 text SHA-256 ${SHA_AA}`));
});

test("a changed one-row comparison keeps both sides present", () => {
  const citation = oneRowComparison(
    { label: "Article 9", anchor: "art_9", kind: "changed", pieces: [], fromSha: SHA_AA, toSha: SHA_BB });
  assert.doesNotMatch(citation, /not present in this version/);
  assert.match(citation, new RegExp(`2020-01-01 text SHA-256 ${SHA_AA}`));
  assert.match(citation, new RegExp(`2021-01-01 text SHA-256 ${SHA_BB}`));
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
    fromRecordSha256: FROM_RECORD,
    toRecordSha256: TO_RECORD,
    rows: [
      { label: "Article 1", anchor: "art_1", kind: "added", pieces: [], toSha: SHA_BB },
      { label: "Article 2", anchor: "art_2", kind: "removed", pieces: [], fromSha: SHA_AA },
    ],
    unchanged: [],
    punctuationOnly: [],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.doesNotMatch(citation, /not present in this version/);
  assert.match(citation, /2020-01-01 no aggregate text digest recorded/);
  assert.match(citation, /2021-01-01 no aggregate text digest recorded/);
});

// O5. The citation's scope is what the reader asked to compare, which is not what the diff found.
// Compare files an identical anchor as unchanged before fetching any text, and moves a
// punctuation-only anchor out of the rows. Both leave `rows` empty while both exact digests exist,
// so classifying from `rows.length` dropped the strongest available claim in exactly the case where
// it was strongest.
const scopedComparison = (
  scope: ComparisonScope, rows: ComparisonRow[] = [],
  extra: Record<string, unknown> = {},
) => comparisonCitationText({
  title: "Sample law",
  work: "lu-legilux:sample",
  from: "2020-01-01",
  to: "2021-01-01",
  permalink: "https://law.soufien.lu/compare",
  fromRecordSha256: FROM_RECORD,
  toRecordSha256: TO_RECORD,
  scope,
  rows,
  unchanged: [],
  punctuationOnly: [],
  exportedAt: "2026-08-29T00:00:00.000Z",
  ...extra,
});

const SCOPE: ComparisonScope = {
  anchor: "art_9", label: "Article 9",
  fromSha: SHA_AA, toSha: SHA_BB, fromPresent: true, toPresent: true,
};

test("an anchor-scoped comparison whose text is identical keeps both exact digests", () => {
  // Identical wording means one digest on both sides and no changed row at all.
  const citation = scopedComparison(
    { ...SCOPE, fromSha: SAME, toSha: SAME }, []);
  assert.match(citation, new RegExp(`2020-01-01 text SHA-256 ${SAME}`));
  assert.match(citation, new RegExp(`2021-01-01 text SHA-256 ${SAME}`));
  assert.doesNotMatch(citation, /no aggregate text digest recorded/);
  assert.match(citation, /Article 9/);
});

test("an anchor-scoped punctuation-only comparison keeps both exact digests", () => {
  // The bytes moved, so the digests differ, but the row was filed as punctuation and never
  // reached `rows`. The citation must still state what each side actually is.
  const citation = scopedComparison(SCOPE, []);
  assert.match(citation, new RegExp(`2020-01-01 text SHA-256 ${SHA_AA}`));
  assert.match(citation, new RegExp(`2021-01-01 text SHA-256 ${SHA_BB}`));
  assert.doesNotMatch(citation, /no aggregate text digest recorded/);
});

test("an anchor-scoped comparison reports an absent side from the scope, not from the rows", () => {
  const citation = scopedComparison(
    { ...SCOPE, fromSha: undefined, fromPresent: false }, []);
  assert.match(citation, /2020-01-01 not present in this version/);
  assert.doesNotMatch(citation, /2020-01-01 record SHA-256/);
  assert.match(citation, new RegExp(`2021-01-01 text SHA-256 ${SHA_BB}`));
});

test("a recorded scope outranks the changed-row count", () => {
  // A scoped comparison that did produce a changed row must still speak from the scope. If the two
  // ever disagree the scope is the question the reader asked.
  const citation = scopedComparison(SCOPE, [
    { label: "Article 9", anchor: "art_9", kind: "changed", pieces: [], fromSha: SHA_AA, toSha: SHA_BB },
  ]);
  assert.match(citation, new RegExp(`2020-01-01 text SHA-256 ${SHA_AA}`));
  assert.match(citation, new RegExp(`2021-01-01 text SHA-256 ${SHA_BB}`));
});

// O4. Both official sources are already carried and already rendered in the Markdown export. The
// citation is the artifact people paste into documents, and it names Lex a reading aid; naming the
// aid without naming the record it stands for leaves the reader nowhere to check.
test("a comparison citation names the official source for each side", () => {
  const citation = scopedComparison(SCOPE, [], {
    fromSource: "https://legilux.public.lu/eli/etat/leg/loi/2020/a1/jo",
    toSource: "https://legilux.public.lu/eli/etat/leg/loi/2021/a2/jo",
  });
  assert.match(citation, /2020-01-01 source https:\/\/legilux\.public\.lu\/eli\/etat\/leg\/loi\/2020\/a1\/jo/);
  assert.match(citation, /2021-01-01 source https:\/\/legilux\.public\.lu\/eli\/etat\/leg\/loi\/2021\/a2\/jo/);
  // The Lex permalink is a different thing and must not stand in for either.
  assert.match(citation, /https:\/\/law\.soufien\.lu\/compare/);
});

test("a comparison citation omits a source it does not have rather than inventing one", () => {
  const citation = scopedComparison(SCOPE, [], {
    fromSource: "https://legilux.public.lu/eli/etat/leg/loi/2020/a1/jo",
  });
  assert.match(citation, /2020-01-01 source https/);
  assert.doesNotMatch(citation, /2021-01-01 source/);
});

// O7. Nothing between the SQLite column and this module validates these fields at runtime: the MCP
// response carries no output schema and the browser parses it through one unchecked cast. The
// index builder recomputes provision digests at build time, but that is a guarantee held somewhere
// else, and `record_sha256` and `body_sha256` are copied onto the index without a recompute. So a
// malformed value reaching here must never leave as a provenance claim.
//
// The uppercase case is the one worth stating. Upstream comparison is case-insensitive, so an
// uppercase digest passes every existing check. It is a real digest and still not a string a
// reader can paste into a checker and match, so it is not offered as one.
const HOSTILE = [
  ["too short", "abc123"],
  ["uppercase", "F".repeat(64)],
  ["mixed case", `${"a".repeat(63)}F`],
  ["non-hex", "z".repeat(64)],
  ["65 characters", "a".repeat(65)],
  ["63 characters", "a".repeat(63)],
  ["whitespace padded", ` ${"a".repeat(64)} `],
  ["empty", ""],
] as const;

for (const [name, hostile] of HOSTILE) {
  test(`a ${name} provision digest is never labelled SHA-256 in a citation`, () => {
    const citation = citationText({
      title: "Sample law",
      work: "lu-legilux:sample",
      validFrom: "2020-01-01",
      permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
      provisions: [{ anchor: "art_1", num: "Article 1", text: "One.", text_sha256: hostile }],
      exportedAt: "2026-08-29T00:00:00.000Z",
    });
    assert.doesNotMatch(citation, /text SHA-256/);
    // Absent and corrupt are different conditions and the citation distinguishes them. An empty
    // string is absence; anything else present but unusable is said out loud, because a reader who
    // cannot tell the two apart has no reason to report the second.
    if (hostile === "") assert.doesNotMatch(citation, /digest withheld/);
    else assert.match(citation, /digest withheld, not in a recognised form/);
  });

  test(`a ${name} record digest is never labelled SHA-256 in a citation`, () => {
    const citation = citationText({
      title: "Sample law",
      work: "lu-legilux:sample",
      validFrom: "2020-01-01",
      permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
      recordSha256: hostile,
      bodySha256: hostile,
      provisions: [
        { anchor: "art_1", num: "Article 1", text: "One." },
        { anchor: "art_2", num: "Article 2", text: "Two." },
      ],
      exportedAt: "2026-08-29T00:00:00.000Z",
    });
    assert.doesNotMatch(citation, /record SHA-256/);
    assert.doesNotMatch(citation, /body SHA-256/);
    assert.match(citation, /no aggregate text digest recorded/);
  });

  test(`a ${name} comparison digest is never labelled SHA-256`, () => {
    const citation = comparisonCitationText({
      title: "Sample law",
      work: "lu-legilux:sample",
      from: "2020-01-01",
      to: "2021-01-01",
      permalink: "https://law.soufien.lu/compare",
      scope: {
        anchor: "art_9", label: "Article 9",
        fromSha: hostile, toSha: hostile, fromPresent: true, toPresent: true,
      },
      rows: [],
      unchanged: [],
      punctuationOnly: [],
      exportedAt: "2026-08-29T00:00:00.000Z",
    });
    assert.doesNotMatch(citation, /text SHA-256/);
  });

  test(`a ${name} provision digest is never labelled SHA-256 in exported Markdown`, () => {
    const markdown = lawEvidenceMarkdown({
      title: "Sample law",
      work: "lu-legilux:sample",
      validFrom: "2020-01-01",
      permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
      provisions: [{ anchor: "art_1", num: "Article 1", text: "One.", text_sha256: hostile }],
      exportedAt: "2026-08-29T00:00:00.000Z",
    });
    // The Markdown export states the same provenance in a longer form and is the artifact a reader
    // keeps, so it is held to the same rule.
    assert.doesNotMatch(markdown, new RegExp(`Text SHA-256: ${hostile.trim() || "x"}`));
  });
}

test("a well-formed digest is still stated, so the guard is not simply refusing everything", () => {
  const good = "a".repeat(64);
  const citation = citationText({
    title: "Sample law",
    work: "lu-legilux:sample",
    validFrom: "2020-01-01",
    permalink: "https://law.soufien.lu/lu-legilux/sample/2020-01-01",
    provisions: [{ anchor: "art_1", num: "Article 1", text: "One.", text_sha256: good }],
    exportedAt: "2026-08-29T00:00:00.000Z",
  });
  assert.match(citation, new RegExp(`text SHA-256 ${good}`));
  assert.doesNotMatch(citation, /digest withheld/);
});
