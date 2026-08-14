export const PUBLISHER_METADATA_KINDS = [
  "publisher_short_title",
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
] as const;

export type PublisherMetadataKind = typeof PUBLISHER_METADATA_KINDS[number];

export type PublisherMetadata = {
  kind: PublisherMetadataKind;
  identifier: string;
  label: string;
  displayLabel: string;
  language: string;
  sourceUri: string;
  matchedSegment?: string;
};

const CAPTIONS: Record<PublisherMetadataKind, string> = {
  publisher_short_title: "Official publisher short title",
  eurovoc: "Publisher EuroVoc concept",
  directory: "Publisher directory classification",
  eurovoc_alt_label: "Publisher EuroVoc alternative label",
  eurovoc_broader: "Publisher EuroVoc broader concept",
  eurovoc_subdomain: "Publisher EuroVoc subdomain",
  eurovoc_domain: "Publisher EuroVoc domain",
  legilux_subject_level1_theme: "Legilux level 1 theme",
  legilux_subject_level1_organisation: "Legilux level 1 organisation",
  legilux_subject_level1_place: "Legilux level 1 place",
  legilux_subject_level1_legal_resource: "Legilux level 1 legal-resource class",
  legilux_subject_level1_country: "Legilux level 1 country",
  legilux_subject_level2_theme: "Legilux level 2 theme",
  legilux_subject_level2_organisation: "Legilux level 2 organisation",
  legilux_subject_level2_place: "Legilux level 2 place",
  legilux_subject_level2_legal_resource: "Legilux level 2 legal-resource class",
  legilux_subject_level2_country: "Legilux level 2 country",
};

const kinds = new Set<string>(PUBLISHER_METADATA_KINDS);

function stringField(value: unknown, maximum: number): string | undefined {
  return typeof value === "string" && value.trim().length > 0 && value.length <= maximum
    ? value
    : undefined;
}

function absoluteHttpUri(value: unknown): string | undefined {
  const text = stringField(value, 2_048);
  if (!text || text.trim() !== text) return undefined;
  try {
    const parsed = new URL(text);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? text : undefined;
  } catch {
    return undefined;
  }
}

function display(value: string): string {
  return value.length <= 180 ? value : `${value.slice(0, 179)}…`;
}

/**
 * Parse only the frozen publisher-owned metadata DTO. These values explain retrieval; they are
 * never legal-text evidence and only the official short-title kind may participate in identity.
 */
export function parsePublisherMetadata(value: unknown): PublisherMetadata | undefined {
  if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
  const row = value as Record<string, unknown>;
  const kind = typeof row.kind === "string" && kinds.has(row.kind)
    ? row.kind as PublisherMetadataKind
    : undefined;
  const identifier = absoluteHttpUri(row.identifier);
  const label = stringField(row.label, 4_096);
  const language = stringField(row.language, 16);
  const sourceUri = absoluteHttpUri(row.source_uri);
  if (!kind || !identifier || !label || !language || !sourceUri) return undefined;

  const segment = stringField(row.matched_segment, 256);
  if (kind === "publisher_short_title" ? !segment : row.matched_segment !== undefined)
    return undefined;

  return {
    kind,
    identifier,
    label,
    displayLabel: display(segment ?? label),
    language,
    sourceUri,
    ...(segment ? { matchedSegment: segment } : {}),
  };
}

export function publisherMetadataCaption(kind: PublisherMetadataKind): string {
  return CAPTIONS[kind];
}

/** Only a parsed server result can produce this exact opaque discovery filter. */
export function publisherMetadataFilterArguments(
  metadata: PublisherMetadata,
): { publisher_metadata_identifier: string } | undefined {
  return metadata.kind === "publisher_short_title"
    ? undefined
    : { publisher_metadata_identifier: metadata.identifier };
}
