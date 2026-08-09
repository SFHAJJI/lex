import assert from "node:assert/strict";
import test from "node:test";
import { groupSearchResults } from "./searchResults.ts";

test("results partition by jurisdiction and nest passages under their work", () => {
  const works = [
    { work: "eu-eurlex:gdpr", title: "GDPR", jurisdiction: "EU" },
    { work: "lu-legilux:privacy", title: "Luxembourg privacy law", jurisdiction: "LU" },
  ];
  const passages = [
    { work: "eu-eurlex:gdpr", title: "GDPR", jurisdiction: "EU", anchor: "art_5", validFrom: "2016-05-04" },
    { work: "lu-legilux:privacy", title: "Luxembourg privacy law", jurisdiction: "LU", anchor: "art_2", validFrom: "2020-01-01" },
    { work: "eu-eurlex:gdpr", title: "GDPR", jurisdiction: "EU", anchor: "art_6", validFrom: "2016-05-04" },
  ];

  const sections = groupSearchResults(works, passages);

  assert.deepEqual(sections.map(section => section.jurisdiction), ["EU", "LU"]);
  assert.deepEqual(sections[0].works.map(work => work.work), ["eu-eurlex:gdpr"]);
  assert.deepEqual(sections[0].works[0].passages.map(passage => passage.anchor), ["art_5", "art_6"]);
  assert.deepEqual(sections[1].works[0].passages.map(passage => passage.anchor), ["art_2"]);
});

test("a passage-only work remains visible once and section order follows ranked works", () => {
  const sections = groupSearchResults(
    [{ work: "lu-legilux:first", title: "First", jurisdiction: "LU" }],
    [{ work: "eu-eurlex:passage-only", title: "Second", jurisdiction: "EU",
       anchor: "art_1", validFrom: "2024-01-01" }],
  );

  assert.deepEqual(sections.map(section => section.jurisdiction), ["LU", "EU"]);
  assert.equal(sections[1].works.length, 1);
  assert.equal(sections[1].works[0].work, "eu-eurlex:passage-only");
  assert.equal(sections[1].works[0].passages.length, 1);
});
