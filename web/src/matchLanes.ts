/**
 * The B2 lane classifier, TypeScript twin of src/Lex.Web/MatchLanes.cs. Both are bound to the
 * one normative case table (tests/Lex.Tests/match-lane-cases.json); the parity test loads that
 * exact file. A hit is text, identity, metadata, or unclassified_render; an unknown reason
 * renders through the existing visible path and is never suppressed, and never asserted as
 * identity (Codex Q1 ruling, 2026-08-28).
 */

export type MatchLane = "text" | "identity" | "metadata" | "unclassified_render";

const TEXT_REASONS = new Set([
  "keyword", "semantic", "semantic_work", "semantic_concept", "article_intent", "fuzzy",
]);
const IDENTITY_SUFFIXES = ["_identifier", "_publisher_short_title", "_title", "_alias"];
const AMBIGUOUS_PREFIX = "ambiguous_";
const METADATA_REASON = "work_metadata";

export function classifyMatchLane(reasons: readonly (string | undefined | null)[]): MatchLane {
  if (reasons.length === 0) return "unclassified_render";
  let sawIdentity = false;
  let sawMetadata = false;
  for (const raw of reasons) {
    const reason = raw ?? "";
    if (TEXT_REASONS.has(reason)) return "text";
    if (reason.startsWith(AMBIGUOUS_PREFIX)) { sawIdentity = true; continue; }
    if (IDENTITY_SUFFIXES.some((suffix) => reason.endsWith(suffix))) {
      sawIdentity = true;
      continue;
    }
    if (reason === METADATA_REASON) { sawMetadata = true; continue; }
    return "unclassified_render";
  }
  if (sawIdentity) return "identity";
  return sawMetadata ? "metadata" : "unclassified_render";
}

/** metadata_only holds only when every hit is POSITIVELY metadata; unclassified never triggers it. */
export function metadataOnlyState(
  hitReasons: readonly (readonly (string | undefined | null)[])[],
): boolean {
  return hitReasons.length > 0
    && hitReasons.every((reasons) => classifyMatchLane(reasons) === "metadata");
}

/** Decision 41 frozen copy, browser authority for the workspace surface. */
export const METADATA_ONLY_HEADING = "No held text match";
export const METADATA_ONLY_BODY =
  "Lex found records that match only in metadata. They are not shown as text answers. This "
  + "is not evidence that the named instrument or law does not exist. Check the name or "
  + "identifier, review coverage and known gaps, or search the official publisher.";
export const METADATA_ONLY_DISCLOSURE = "Matched only in metadata";
