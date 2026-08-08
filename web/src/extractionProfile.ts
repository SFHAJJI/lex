export type ExtractionDisclosure = "publisher-markup" | "publisher-pdf" | "gazette";

/**
 * Classify how legal text was obtained for the reader's provenance disclosure.
 *
 * Profile identifiers are immutable and versioned. Disclosure belongs to the profile family:
 * a successor Memorial parser still located an act inside a whole gazette issue and must never
 * silently fall back to the publisher-markup presentation merely because its version is new.
 */
export function extractionDisclosure(profile?: string): ExtractionDisclosure {
  if (profile?.startsWith("pdf-memorial-lu/")) return "gazette";
  if (profile?.startsWith("pdf-lu/")) return "publisher-pdf";
  return "publisher-markup";
}
