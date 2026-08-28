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
 * The population plus whether it is COMPLETE (B1+B2 round 2 review, O2). Round 1 discarded
 * response invalidity: a successful envelope whose hits field was malformed was read as empty,
 * so a sibling metadata-only response could still authorize suppression, and a wrong-typed
 * match_reasons could vanish when a duplicate work later contributed work_metadata. Any
 * malformed successful envelope, hit or reason member now makes the positive metadata_only
 * claim unreachable rather than silently shrinking the evidence.
 */
export interface ResponsePopulation {
  entries: PopulationEntry[];
  complete: boolean;
}

/**
 * The only status under which a search envelope actually executed. Verified against the
 * producer: the search case emits ok, retrieval_mode_unavailable, unknown_work, unknown_anchor
 * and no_provision_history, and never no_result. Round 1 admitted no_result, so a
 * cross-operation envelope carrying metadata hits could authorize suppression.
 */
const SEARCH_SUCCESS_STATUSES = new Set(["ok"]);

/**
 * The authoritative response population, read from the RAW envelopes (B1+B2 review, O2 and
 * O5). Three things must all be true here and cannot be recovered later:
 *
 * 1. Only an envelope with a successful status contributes. A refused or malformed envelope's
 *    rows are not evidence, and admitting them can suppress real answers behind the notice.
 * 2. Reasons are unioned per logical work BEFORE any fusion. The workspace's fusion step
 *    deduplicates by identity and discards the losing hit's match_reasons, so a work matched
 *    by metadata in one publisher and by keyword in another would otherwise read as
 *    metadata-only and be suppressed.
 * 3. The full population is retained, not the display slice, so the disclosure and its
 *    overflow count describe the whole response.
 */
export function responsePopulation(raw: unknown): ResponsePopulation {
  const envelopes = Array.isArray(raw) ? raw : [raw];
  const byWork = new Map<string, PopulationEntry>();
  let complete = true;
  for (const envelope of envelopes) {
    if (typeof envelope !== "object" || envelope === null) {
      complete = false;
      continue;
    }
    const record = envelope as Record<string, unknown>;
    const meta = record.envelope as Record<string, unknown> | undefined;
    const status = typeof meta?.status === "string" ? meta.status : undefined;
    if (status === undefined) {
      complete = false;
      continue;
    }
    if (!SEARCH_SUCCESS_STATUSES.has(status)) continue;
    // A successful envelope MUST carry a well-formed hits array. Reading a malformed one as
    // empty is what let a sibling authorize suppression.
    if (!Array.isArray(record.hits)) {
      complete = false;
      continue;
    }
    for (const hit of record.hits) {
      if (typeof hit !== "object" || hit === null || Array.isArray(hit)) {
        complete = false;
        continue;
      }
      const row = hit as Record<string, unknown>;
      const work = String(row.lex_id ?? "").split(":").slice(0, 2).join(":");
      if (!work) {
        complete = false;
        continue;
      }
      // A reasons field that is not an array of strings is not evidence, and must not be
      // silently replaced with an empty union.
      const rawReasons = row.match_reasons;
      if (rawReasons !== undefined
        && (!Array.isArray(rawReasons)
          || rawReasons.some((member) => typeof member !== "string"))) {
        complete = false;
        continue;
      }
      const reasons = Array.isArray(rawReasons) ? rawReasons : [];
      const existing = byWork.get(work);
      if (existing) {
        // Union, never replace: a later hit's reasons are as authoritative as the first's.
        existing.reasons = [
          ...(existing.reasons as unknown[]),
          ...reasons,
        ];
        if (!existing.title && typeof row.title === "string") existing.title = row.title;
        continue;
      }
      byWork.set(work, {
        work,
        title: typeof row.title === "string" ? row.title : work,
        reasons: [...reasons],
      });
    }
  }
  return { entries: [...byWork.values()], complete };
}

/** The one production decision: suppression requires a complete, positively metadata population. */
export function metadataOnlyFromResponse(raw: unknown): {
  metadataOnly: boolean;
  population: PopulationEntry[];
} {
  const { entries, complete } = responsePopulation(raw);
  return {
    metadataOnly: complete && metadataOnlyResponse(entries),
    population: entries,
  };
}

/** True when any envelope reports a truncated row set, so no exact overflow total exists. */
export function anyRowSetTruncated(raw: unknown): boolean {
  const envelopes = Array.isArray(raw) ? raw : [raw];
  return envelopes.some((envelope) => {
    if (typeof envelope !== "object" || envelope === null) return false;
    const rowSet = (envelope as Record<string, unknown>).response_row_set;
    return typeof rowSet === "object" && rowSet !== null
      && (rowSet as Record<string, unknown>).truncated === true;
  });
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
