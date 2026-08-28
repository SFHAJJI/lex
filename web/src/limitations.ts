/**
 * Publisher capability limitations, browser side.
 *
 * When one mounted publisher refuses a governed filter with
 * `filter_not_supported_by_index` while another answers, the supported rows must render AND the
 * refusal must render beside them as a typed limitation. Silently dropping either side misreads
 * a coverage statement as evidence about the law. This module is the single fail-closed
 * validator and derivation helper for all three direct paths (search, changes_in_period,
 * in_force_on) and for the assistant's additive `publisher_limitations` field.
 *
 * Fail closed means: malformed input is IGNORED, never rendered, and can never suppress a
 * primary view. Nothing here copies or logs query text; the explanation is one fixed sentence.
 */

export interface PublisherLimitation {
  status: "filter_not_supported_by_index";
  tool: string;
  publisher?: string;
  jurisdiction?: string;
  unsupported_filters: string[];
}

/** The closed status this module speaks for; anything else is not a capability limitation. */
export const LIMITATION_STATUS = "filter_not_supported_by_index";

/** At most eight limitation objects render; the rest are dropped, never summarized. */
export const LIMITATION_CAP = 8;

/** The governed operations; a limitation naming any other tool is malformed. */
const GOVERNED_TOOLS = new Set(["search", "changes_in_period", "in_force_on"]);

/** The governed filter identifiers; anything else in the list is dropped. */
const GOVERNED_FILTERS = new Set(["act_form", "binding_status", "domain", "hierarchy"]);

/** Fixed server explanation. Never interpolates publisher prose or the reader's query. */
export const LIMITATION_EXPLANATION =
  "This publisher's index does not describe the requested filter for the requested scope, so "
  + "it did not run this query. That is a statement about Lex's coverage, not evidence that a "
  + "law or record is absent.";

const boundedIdentifier = (value: unknown): string | undefined =>
  typeof value === "string" && value.length > 0 && value.length <= 64
    && /^[a-z0-9_-]+$/i.test(value)
    ? value
    : undefined;

/**
 * Validate one limitation object from the assistant's additive field. Returns null for any
 * shape that is not exactly the contract; the caller ignores nulls.
 */
export function validateLimitation(value: unknown): PublisherLimitation | null {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return null;
  const record = value as Record<string, unknown>;
  if (record.status !== LIMITATION_STATUS) return null;
  const tool = typeof record.tool === "string" && GOVERNED_TOOLS.has(record.tool)
    ? record.tool : null;
  if (!tool) return null;
  if (!Array.isArray(record.unsupported_filters)) return null;
  const filters = [...new Set(record.unsupported_filters
    .filter((item): item is string => typeof item === "string" && GOVERNED_FILTERS.has(item)))]
    .sort();
  if (filters.length === 0) return null;
  return {
    status: LIMITATION_STATUS,
    tool,
    publisher: boundedIdentifier(record.publisher),
    jurisdiction: boundedIdentifier(record.jurisdiction),
    unsupported_filters: filters,
  };
}

/**
 * Logical identity (O3 ruling): status, tool, validated publisher, validated jurisdiction and
 * the sorted distinct governed-filter values. Two separately allocated but logically identical
 * objects are one limitation; dedup runs before the cap in every layer that can merge.
 */
const logicalIdentity = (item: PublisherLimitation): string =>
  [item.status, item.tool, item.publisher ?? "", item.jurisdiction ?? "",
    item.unsupported_filters.join(",")].join("|");

function dedupeLimitations(items: PublisherLimitation[]): PublisherLimitation[] {
  const seen = new Set<string>();
  const result: PublisherLimitation[] = [];
  for (const item of items) {
    const identity = logicalIdentity(item);
    if (seen.has(identity)) continue;
    seen.add(identity);
    result.push(item);
  }
  return result;
}

/** The assistant's additive `publisher_limitations` field: validate, dedup, cap, ignore rest. */
export function limitationsFromEffect(value: unknown): PublisherLimitation[] {
  if (!Array.isArray(value)) return [];
  return dedupeLimitations(value.map(validateLimitation)
    .filter((item): item is PublisherLimitation => item !== null))
    .slice(0, LIMITATION_CAP);
}

/**
 * Direct-path derivation: the raw per-publisher envelope array of one governed tool call.
 * A refusing envelope contributes one limitation; every other envelope contributes nothing.
 * The caller keeps its supported rows exactly as before; this function never sees the query.
 */
export function limitationsFromEnvelopes(
  tool: string,
  envelopes: unknown[],
): PublisherLimitation[] {
  if (!GOVERNED_TOOLS.has(tool) || !Array.isArray(envelopes)) return [];
  const result: PublisherLimitation[] = [];
  for (const entry of envelopes) {
    if (typeof entry !== "object" || entry === null) continue;
    const envelope = (entry as Record<string, unknown>).envelope;
    if (typeof envelope !== "object" || envelope === null) continue;
    const env = envelope as Record<string, unknown>;
    if (env.status !== LIMITATION_STATUS) continue;
    const validated = validateLimitation({
      status: LIMITATION_STATUS,
      tool,
      publisher: env.publisher,
      jurisdiction: env.jurisdiction,
      unsupported_filters: (entry as Record<string, unknown>).unsupported_filters,
    });
    if (validated) result.push(validated);
  }
  return dedupeLimitations(result).slice(0, LIMITATION_CAP);
}

/** True when every envelope in the call refused; the caller then keeps its full typed gap. */
export function everyPublisherRefused(envelopes: unknown[]): boolean {
  if (!Array.isArray(envelopes) || envelopes.length === 0) return false;
  return envelopes.every((entry) => {
    if (typeof entry !== "object" || entry === null) return false;
    const envelope = (entry as Record<string, unknown>).envelope;
    return typeof envelope === "object" && envelope !== null
      && (envelope as Record<string, unknown>).status === LIMITATION_STATUS;
  });
}

/**
 * What an empty search surface may claim (O1). "no_match" is a statement about the corpus and
 * is only true when at least one publisher actually ran the query; an all-refused call is a
 * coverage statement and must render the typed limitation gap instead. Any hit renders results.
 */
export function searchAbsenceState(
  envelopes: unknown[],
  hitCount: number,
): "has_results" | "all_refused" | "no_match" {
  if (hitCount > 0) return "has_results";
  if (everyPublisherRefused(envelopes)) return "all_refused";
  return "no_match";
}

/**
 * The one cleared-result tuple (O2): every result-bearing state a search request can set, in
 * one place, so the empty-query transition and the request-start transition cannot drift apart
 * and strand a stale notice on an empty workspace.
 */
export interface ClearedSearchResults {
  works: never[];
  articles: never[];
  error: undefined;
  modeUnavailable: undefined;
  limitations: never[];
  expansions: never[];
}
export const clearedSearchResults = (): ClearedSearchResults => ({
  works: [], articles: [], error: undefined, modeUnavailable: undefined,
  limitations: [], expansions: [],
});
