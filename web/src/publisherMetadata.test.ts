import test from "node:test";
import assert from "node:assert/strict";
import { safeHttpsUrl } from "./api.ts";
import {
  parsePublisherMetadata,
  publisherMetadataCaption,
  publisherMetadataFilterArguments,
  type PublisherMetadataKind,
} from "./publisherMetadata.ts";

const WEAK_KINDS: PublisherMetadataKind[] = [
  "eurovoc",
  "directory",
  "eurovoc_alt_label",
  "eurovoc_broader",
  "eurovoc_subdomain",
  "eurovoc_domain",
  "legilux_subject_level1_theme",
  "legilux_subject_level1_organisation",
  "legilux_subject_level1_place",
  "legilux_subject_level1_legal_resource",
  "legilux_subject_level1_country",
  "legilux_subject_level2_theme",
  "legilux_subject_level2_organisation",
  "legilux_subject_level2_place",
  "legilux_subject_level2_legal_resource",
  "legilux_subject_level2_country",
];

function row(kind: PublisherMetadataKind, overrides: Record<string, unknown> = {}) {
  return {
    kind,
    identifier: "http://publications.europa.eu/resource/authority/eurovoc/1000",
    label: "Financial regulation",
    language: "en",
    source_uri: "http://publications.europa.eu/resource/authority/eurovoc/1000",
    ...overrides,
  };
}

test("every frozen discovery kind has a distinct accurate caption and exact filter", () => {
  const captions = new Set<string>();
  for (const kind of WEAK_KINDS) {
    const metadata = parsePublisherMetadata(row(kind));
    assert.ok(metadata, kind);
    const filter = publisherMetadataFilterArguments(metadata!);
    assert.deepEqual(filter, {
      publisher_metadata_identifier:
        "http://publications.europa.eu/resource/authority/eurovoc/1000",
    });
    captions.add(publisherMetadataCaption(kind));
  }
  assert.equal(captions.size, WEAK_KINDS.length);
});

test("official short-title metadata is contextual authority provenance, not a filter chip", () => {
  const metadata = parsePublisherMetadata(row("publisher_short_title", {
    label: "DORA, Digital Operational Resilience Act",
    matched_segment: "DORA",
  }));

  assert.ok(metadata);
  assert.equal(metadata.displayLabel, "DORA");
  assert.equal(publisherMetadataFilterArguments(metadata), undefined);
});

test("HTTP publisher identifiers remain opaque filter values and never become links", () => {
  const metadata = parsePublisherMetadata(row("eurovoc_domain"));

  assert.ok(metadata);
  assert.deepEqual(publisherMetadataFilterArguments(metadata!), {
    publisher_metadata_identifier:
      "http://publications.europa.eu/resource/authority/eurovoc/1000",
  });
  assert.equal(safeHttpsUrl(metadata!.sourceUri), undefined);
  assert.equal(safeHttpsUrl("https://publications.europa.eu/source"),
    "https://publications.europa.eu/source");
});

test("malformed, unknown, oversized, and semantically inconsistent metadata fails closed", () => {
  assert.equal(parsePublisherMetadata(row("eurovoc", { kind: "domain" })), undefined);
  assert.equal(parsePublisherMetadata(row("eurovoc", { identifier: "urn:eurovoc:1000" })),
    undefined);
  assert.equal(parsePublisherMetadata(row("eurovoc", { source_uri: "javascript:alert(1)" })),
    undefined);
  assert.equal(parsePublisherMetadata(row("eurovoc", { label: "x".repeat(4_097) })),
    undefined);
  assert.equal(parsePublisherMetadata(row("eurovoc", { matched_segment: "DORA" })),
    undefined);
  assert.equal(parsePublisherMetadata(row("publisher_short_title")), undefined);
});
