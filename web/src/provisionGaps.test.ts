import assert from "node:assert/strict";
import test from "node:test";
import {
  asOfResult,
  hasTypedProvisionGaps,
  isTypedProvisionGap,
  provisionItemsOf,
} from "./api.ts";
import { assistantProvisionLoad, assistantWorkspaceState } from "./assistantShell.ts";
import { lawEvidenceMarkdown } from "./export.ts";

const gap = {
  document_order: 1,
  anchor: "art_2",
  num: "Art. 2",
  text_available: false,
  text_unavailable_reason: "marker_only",
  eli: "https://publisher.example/work#art_2",
};

test("canon/2 rows preserve mixed publisher order and keep gaps textless", () => {
  const result = {
    text_completeness: "partial",
    provisions: [{
      document_order: 0,
      anchor: "art_1",
      num: "Art. 1",
      text: "Synthetic publisher wording.",
      text_sha256: "a".repeat(64),
    }],
    provision_gaps: [gap],
  };

  const items = provisionItemsOf(result);
  assert.deepEqual(items.map((item) => item.anchor), ["art_1", "art_2"]);
  assert.equal(isTypedProvisionGap(items[1]), true);
  assert.equal(items[1].text, undefined);
  assert.equal(items[1].text_sha256, undefined);
  assert.equal(items[1].permalink, gap.eli);
  assert.equal(hasTypedProvisionGaps(result), true);
  assert.equal(hasTypedProvisionGaps(result, "art_1"), false);
  assert.equal(hasTypedProvisionGaps(result, "art_2"), true);
});

test("a gap-only publisher result remains selectable and loads as a typed workspace row", () => {
  const result = {
    envelope: { status: "text_not_available" },
    document: { valid_from: "2025-01-01" },
    text_completeness: "unavailable",
    provisions: [],
    provision_gaps: [gap],
  };
  assert.equal(asOfResult([{}, result]), result);
  assert.equal(asOfResult([{ provisions: [] }, result]), result);

  const loaded = assistantProvisionLoad({
    provision: {
      subject: { work: "t-pub:work" },
      valid_from: "2025-01-01",
      provisions: [],
      provision_gaps: [gap],
      text_completeness: "unavailable",
    },
  });
  assert.equal(loaded?.items.length, 1);
  assert.equal(loaded?.items[0].anchor, "art_2");
  assert.equal(loaded?.textCompleteness, "unavailable");

  const state = assistantWorkspaceState({
    gap: {
      status: "text_not_available",
      work: "t-pub:work",
      date: "2025-01-01",
      explanation: "Synthetic gap.",
      available: [],
      provision_gaps: [gap],
    },
  });
  assert.equal(state?.space, "law");
  assert.equal(state?.work, "t-pub:work");
  assert.equal(state?.date, "2025-01-01");
  assert.equal(state?.anchor, "art_2");

  const wholeDocumentState = assistantWorkspaceState({
    gap: {
      status: "text_not_available",
      work: "t-pub:work",
      date: "2025-01-01",
      explanation: "Synthetic gaps.",
      available: [],
      provision_gaps: [gap, { ...gap, document_order: 2, anchor: "art_3" }],
    },
  });
  assert.equal(wholeDocumentState?.anchor, undefined);
});

test("gap evidence export records the reason and official source without a text hash", () => {
  const markdown = lawEvidenceMarkdown({
    title: "Synthetic work",
    work: "t-pub:work",
    validFrom: "2025-01-01",
    permalink: "https://lex.example/t-pub/work/2025-01-01",
    provisions: provisionItemsOf({ provisions: [], provision_gaps: [gap] }),
    exportedAt: "2026-08-28T00:00:00Z",
  });
  assert.match(markdown, /Text: unavailable \(marker_only\)/);
  assert.match(markdown, /https:\/\/publisher\.example\/work#art_2/);
  assert.doesNotMatch(markdown, /Text SHA-256/);
});

test("assistant gap official_source survives mapping and export", () => {
  const [item] = provisionItemsOf({ provisions: [], provision_gaps: [{
    ...gap,
    eli: undefined,
    official_source: "https://publisher.example/exact#art_2",
  }] });
  assert.equal(item.source_uri, "https://publisher.example/exact#art_2");
  assert.equal(item.permalink, "https://publisher.example/exact#art_2");

  const markdown = lawEvidenceMarkdown({
    title: "Synthetic work",
    work: "t-pub:work",
    validFrom: "2025-01-01",
    permalink: "https://lex.example/t-pub/work/2025-01-01",
    provisions: [item],
    exportedAt: "2026-08-28T00:00:00Z",
  });
  assert.match(markdown, /https:\/\/publisher\.example\/exact#art_2/);
});

test("gap export refuses a non-HTTPS source instead of emitting an active scheme", () => {
  const markdown = lawEvidenceMarkdown({
    title: "Synthetic work",
    work: "t-pub:work",
    validFrom: "2025-01-01",
    permalink: "https://lex.example/t-pub/work/2025-01-01",
    provisions: provisionItemsOf({ provisions: [], provision_gaps: [{
      ...gap,
      eli: "javascript:alert(1)",
    }] }),
    exportedAt: "2026-08-28T00:00:00Z",
  });

  assert.match(markdown, /Official source: not recorded/);
  assert.doesNotMatch(markdown, /javascript:/);
});
