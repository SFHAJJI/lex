/**
 * The B2 lane classifier, TypeScript twin of src/Lex.Web/MatchLanes.cs. Both are bound to the
 * one normative case table (tests/Lex.Tests/match-lane-cases.json); the parity test loads that
 * exact file. A hit is text, identity, metadata, or unclassified_render; an unknown reason
 * renders through the existing visible path and is never suppressed, and never asserted as
 * identity (Codex Q1 ruling, 2026-08-28).
 */

export type MatchLane = "text" | "identity" | "metadata" | "unclassified_render";

// semantic_work and semantic_concept are deliberately NOT text: the producer's work vector
// is subjects plus names, never provision text, and the concept arm is unreachable and
// kind-unbound (Codex B2 review O3). Both fall through to unclassified_render.
const TEXT_REASONS = new Set(["keyword", "semantic", "article_intent", "fuzzy"]);
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

/**
 * A served reason list straight off the wire: a non-array or any non-string member makes the
 * hit unclassified_render, never metadata, so a hostile or wrong-typed shape can neither
 * crash rendering nor suppress results (Codex B2 review, O4).
 */
export function laneOfServedReasons(raw: unknown): MatchLane {
  if (!Array.isArray(raw) || raw.some((member) => typeof member !== "string")) {
    return "unclassified_render";
  }
  return classifyMatchLane(raw as string[]);
}

/**
 * The response-level decision, taken over the COMPLETE fused population before any display
 * cap, dedup projection or passage filter (Codex B2 review, O2). Hits that collapse to one
 * logical work contribute the UNION of their reasons, so a metadata row can never mask a
 * text or identity row for the same work. Any wrong-typed reason shape forbids the
 * metadata_only claim outright.
 */
export function metadataOnlyResponse(
  population: readonly { work: unknown; reasons: unknown }[],
): boolean {
  if (population.length === 0) return false;
  const unionByWork = new Map<string, string[]>();
  for (const [index, hit] of population.entries()) {
    if (!Array.isArray(hit.reasons)
      || hit.reasons.some((member) => typeof member !== "string")) {
      return false;
    }
    const key = typeof hit.work === "string" && hit.work.length > 0
      ? hit.work
      : `#anonymous-${index}`;
    unionByWork.set(key, [...(unionByWork.get(key) ?? []), ...(hit.reasons as string[])]);
  }
  return [...unionByWork.values()]
    .every((reasons) => classifyMatchLane(reasons) === "metadata");
}

/** One logical work in the authoritative response population. */
export interface PopulationEntry {
  work: string;
  title: string;
  reasons: unknown;
}





/**
 * The official publisher search entry per collection, exact reviewed hosts only; an unknown
 * collection falls back to the internal search page rather than guessing a URL. Mirrors the
 * server-side authority in WorkCandidates.OfficialSearchHref.
 */
export function officialSearchHref(collection: string): string {
  switch (collection) {
    case "lu-legilux": return "https://legilux.public.lu";
    case "eu-eurlex": return "https://eur-lex.europa.eu";
    default: return "/search";
  }
}

/** Decision 41 frozen copy, browser authority for the workspace surface. */
export const METADATA_ONLY_HEADING = "No held text match";
export const METADATA_ONLY_BODY =
  "Lex found records that match only in metadata. They are not shown as text answers. This "
  + "is not evidence that the named instrument or law does not exist. Check the name or "
  + "identifier, review coverage and known gaps, or search the official publisher.";
export const METADATA_ONLY_DISCLOSURE = "Matched only in metadata";
