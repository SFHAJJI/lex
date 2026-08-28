/**
 * Publisher capability limitations and governed response authority, browser side.
 *
 * When one mounted publisher refuses a governed filter with
 * `filter_not_supported_by_index` while another answers, the supported rows must render AND the
 * refusal must render beside them as a typed limitation. Silently dropping either side misreads
 * a coverage statement as evidence about the law. This module is the single fail-closed
 * validator, classifier and projector for all three direct paths (search, changes_in_period,
 * in_force_on) and for the assistant's additive `publisher_limitations` field.
 *
 * Round-4 contract (Codex O1/O2): classification is CLOSED. An envelope enters `ran` only when
 * it carries an allowed terminal success status for its operation AND a valid response shape
 * whose counts are finite, nonnegative and coherent with its rows. A refusal without its typed
 * limitation is invalid, not an evidence-free refusal. Missing, unknown, misspelled or
 * malformed envelopes are `invalid`: they authorize neither rows nor absence claims, and their
 * presence makes an otherwise empty response `incomplete_response`, never `no_match`.
 *
 * Fail closed means: malformed input is IGNORED, never rendered, and can never suppress a
 * primary view. Nothing here copies or logs query text; every sentence is fixed.
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

/**
 * The closed terminal success statuses per governed operation, pinned against the producer
 * (McpCore): search executes as `ok` only; changes_in_period as `ok` or
 * `no_changes_in_period`; in_force_on as `ok`, `no_result` or `ambiguous_version`. Search's
 * `retrieval_mode_unavailable` is its own non-ran class. Everything else is invalid.
 */
/** The retrieval modes a successful search envelope must declare. */
const RETRIEVAL_MODES = new Set(["keyword", "hybrid"]);

const SUCCESS_STATUSES: Record<string, ReadonlySet<string>> = {
  search: new Set(["ok"]),
  changes_in_period: new Set(["ok", "no_changes_in_period"]),
  in_force_on: new Set(["ok", "no_result", "ambiguous_version"]),
};

/** Client-only presentation states; never rendered as a wire status badge (Codex O5). */
export const CLIENT_GAP_STATES: ReadonlySet<string> = new Set([
  "mixed_no_match", "incomplete_response", "error", "partial_response", "ambiguous_only",
]);

/**
 * The gap badge decision (round 4, O5), extracted so the production seam is testable: a
 * client-only presentation state is not a publisher status and must never wear the
 * monospaced wire-status badge. Returns the status to display, or null for no badge.
 */
export const gapBadgeStatus = (status: string): string | null =>
  CLIENT_GAP_STATES.has(status) ? null : status;

/** The browser's fixed copy for this notice (facts-only ruling): the wire carries facts,
    each renderer owns its fixed template. Never interpolates prose or the reader's query. */
export const LIMITATION_EXPLANATION =
  "This publisher's index does not describe the requested filter for the requested scope, so "
  + "it did not run this query. That is a statement about Lex's coverage, not evidence that a "
  + "law or record is absent.";

/** The documented no-corpus refusal: a top-level status with no envelope, every tool. */
export const NO_CORPUS_STATUS = "no_corpus_mounted";

/** Fixed copy for the no-corpus terminal state. Retrying cannot help, so it never says to. */
export const NO_CORPUS_SENTENCE =
  "This server has no verified legal index mounted, so it holds no law and cannot answer "
  + "legal questions. This is a deployment state, not a statement about the law.";

/**
 * Fixed copy for a page made entirely of ambiguity units. The publisher holds states covering
 * the date but exposes several identified versions, so Lex can neither list them as one result
 * nor claim nothing covers the date.
 */
export const AMBIGUOUS_ONLY_SENTENCE =
  "The publisher exposes several identified versions covering this date, so Lex cannot list a "
  + "single set of states for it. Choose an exact publisher version to continue.";

/** Fixed copy disclosed beside verified rows when a sibling response was unusable. */
export const PARTIAL_RESPONSE_SENTENCE =
  "Some publishers returned a response Lex could not read, so these results are not everything "
  + "it holds for this request.";

/** The fixed sentence for a response that cannot support results or absence claims. */
export const INCOMPLETE_RESPONSE_SENTENCE =
  "This response was incomplete, so Lex cannot show results or state what is absent. "
  + "Try the request again.";

const boundedIdentifier = (value: unknown): string | undefined =>
  typeof value === "string" && value.length > 0 && value.length <= 64
    && /^[a-z0-9_-]+$/i.test(value)
    ? value
    : undefined;

/** A count the projection consumes: it must be PRESENT and a nonnegative integer. Treating
    a missing count as zero is the fail-open pattern this contract exists to close. */
const requiredCount = (value: unknown): number | null =>
  typeof value === "number" && Number.isFinite(value) && Number.isInteger(value) && value >= 0
    ? value
    : null;

/** A secondary aggregate: absent is tolerated, present must be valid. */
const optionalCount = (value: unknown): number | null =>
  value === undefined || value === null ? 0 : requiredCount(value);

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
 * Tool authority (round 4, O6): a single-operation surface accepts only limitations carrying
 * its own tool. A multi-tool surface must label the authority visibly instead of filtering.
 */
export function limitationsForTool(
  items: PublisherLimitation[],
  tool: string,
): PublisherLimitation[] {
  return items.filter((item) => item.tool === tool);
}

/**
 * The scoping decision the renderer actually applies (round-5 O5). Production called a
 * duplicate of this logic inline, so the tested helper proved nothing about what shipped:
 * a mutation of limitationsForTool left the rendered output identical. The component now
 * calls this, and this calls the helper, so one seam governs both.
 *
 * An undefined tool means the surface is genuinely multi-operation (the assistant can issue
 * any tool), which the review sanctions provided each row labels its own authority visibly.
 */
export function scopedLimitations(
  items: PublisherLimitation[],
  tool: string | undefined,
): PublisherLimitation[] {
  return tool === undefined ? items : limitationsForTool(items, tool);
}

/** One classified envelope of a governed call (round 4, O1). */
export type EnvelopeClass =
  | { kind: "ran"; entry: unknown }
  | { kind: "refused"; limitation: PublisherLimitation }
  | { kind: "mode_unavailable"; publisher?: string }
  | { kind: "no_corpus" }
  | { kind: "invalid" };

const record = (value: unknown): Record<string, unknown> | null =>
  typeof value === "object" && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;

/**
 * Operation-specific response-shape and count/row coherence checks (round 4, O2). Both
 * governed direct callers request offset 0, so a positive count with zero rows is a
 * contradiction, as are rows exceeding their count or rows beside a zero count. A
 * contradictory envelope is invalid: it never becomes results or corpus absence.
 */
/**
 * Every row in a successful response must be a non-null object (PR293 review, O4). Round 6
 * validated only the outer array, so a single null row was admitted and then threw inside
 * fusePublisherHits while reading lex_id, taking the workspace down on a malformed response
 * instead of degrading.
 */
function rowsValid(value: unknown): value is Record<string, unknown>[] {
  return Array.isArray(value)
    && value.every((row) => typeof row === "object" && row !== null && !Array.isArray(row));
}

function ranShapeValid(tool: string, entry: Record<string, unknown>, status: string): boolean {
  if (tool === "search") {
    // O3: the producer always declares its actual retrieval mode on a successful search.
    // Admitting an envelope without one lets a response of unknown provenance render.
    return rowsValid(entry.hits)
      && typeof entry.retrieval_mode === "string"
      && RETRIEVAL_MODES.has(entry.retrieval_mode);
  }
  if (tool === "changes_in_period") {
    if (!rowsValid(entry.changes)) return false;
    const worksChanged = requiredCount(entry.works_changed);
    const newVersions = optionalCount(entry.new_versions);
    if (worksChanged === null || newVersions === null) return false;
    if (status === "no_changes_in_period") {
      return worksChanged === 0 && entry.changes.length === 0;
    }
    // Rows may legitimately be empty beside a positive count: the producer emits this
    // publisher's FULL period total beside its slice of one globally merged page, so a
    // publisher outranked out of the current page returns count>0 with zero rows. Only the
    // impossible directions are contradictions.
    if (entry.changes.length > worksChanged) return false;
    if (entry.changes.length > 0 && worksChanged === 0) return false;
    return true;
  }
  if (tool === "in_force_on") {
    if (!rowsValid(entry.works)) return false;
    const total = requiredCount(entry.total_works_in_force);
    if (total === null) return false;
    if (entry.ambiguous_works !== undefined && !rowsValid(entry.ambiguous_works)) return false;
    const ambiguities = Array.isArray(entry.ambiguous_works)
      ? entry.ambiguous_works.length : 0;
    // As above: one remainingLimit is shared across publishers, so an exhausted page returns
    // zero rows beside this publisher's full total. Ambiguity units also consume the page, so
    // rows plus ambiguities is what must not exceed the total.
    if (entry.works.length + ambiguities > total) return false;
    if (entry.works.length > 0 && total === 0) return false;
    return true;
  }
  return false;
}

/** Classify one envelope of one governed operation, closed in every direction. */
export function classifyEnvelope(tool: string, value: unknown): EnvelopeClass {
  if (!GOVERNED_TOOLS.has(tool)) return { kind: "invalid" };
  const entry = record(value);
  if (!entry) return { kind: "invalid" };
  // The no-corpus refusal is documented as a top-level status with no envelope, for every
  // tool. It is terminal, not malformed: telling the reader to retry would be a lie.
  if (entry.status === NO_CORPUS_STATUS) return { kind: "no_corpus" };
  const envelope = record(entry.envelope);
  if (!envelope) return { kind: "invalid" };
  const status = envelope.status;
  if (typeof status !== "string") return { kind: "invalid" };
  if (status === LIMITATION_STATUS) {
    const limitation = validateLimitation({
      status: LIMITATION_STATUS,
      tool,
      publisher: envelope.publisher,
      jurisdiction: envelope.jurisdiction,
      unsupported_filters: entry.unsupported_filters,
    });
    // A refusal without its required typed limitation is invalid, never evidence-free.
    return limitation ? { kind: "refused", limitation } : { kind: "invalid" };
  }
  if (tool === "search" && status === "retrieval_mode_unavailable") {
    return { kind: "mode_unavailable", publisher: boundedIdentifier(envelope.publisher) };
  }
  if (!SUCCESS_STATUSES[tool]!.has(status)) return { kind: "invalid" };
  return ranShapeValid(tool, entry, status) ? { kind: "ran", entry: value } : { kind: "invalid" };
}

/**
 * The row-authority boundary. Rows come only from `ran`; refusals contribute limitations and
 * nothing else; invalid envelopes contribute nothing and forbid absence claims.
 */
export interface GovernedPartition {
  /** Envelopes that actually executed with a coherent shape. Rows come only from these. */
  ran: unknown[];
  /** Typed limitations derived from the refusing envelopes. */
  limitations: PublisherLimitation[];
  /** Bounded publisher ids whose retrieval mode is unauthorized (search only). */
  modeUnavailablePublishers: string[];
  /** How many envelopes lacked the retrieval mode, id-bearing or not. */
  modeUnavailableCount: number;
  /** Every selected publisher returned the terminal no-corpus refusal. */
  noCorpus: boolean;
  /** Ambiguity units across ran in_force_on envelopes; they are content, not emptiness. */
  ambiguityUnits: number;
  /** Envelopes that authorize neither rows nor absence claims. */
  invalidCount: number;
  /** At least one envelope refused. */
  anyRefused: boolean;
  /** Nothing ran and at least one envelope refused or lacked the retrieval mode. */
  allRefused: boolean;
}

export function partitionGovernedResponse(
  tool: string,
  envelopes: unknown[],
): GovernedPartition {
  const list = Array.isArray(envelopes) ? envelopes : [];
  const classes = list.map((entry) => classifyEnvelope(tool, entry));
  const ran = classes.flatMap((item) => item.kind === "ran" ? [item.entry] : []);
  const limitations = dedupeLimitations(
    classes.flatMap((item) => item.kind === "refused" ? [item.limitation] : []))
    .slice(0, LIMITATION_CAP);
  const modeUnavailablePublishers = [...new Set(classes
    .flatMap((item) => item.kind === "mode_unavailable" && item.publisher
      ? [item.publisher] : []))];
  const modeUnavailableCount =
    classes.filter((item) => item.kind === "mode_unavailable").length;
  const invalidCount = classes.filter((item) => item.kind === "invalid").length;
  const refusedCount = classes.filter((item) => item.kind === "refused").length;
  const noCorpusCount = classes.filter((item) => item.kind === "no_corpus").length;
  const ambiguityUnits = ran
    .map(record)
    .reduce((sum, entry) => sum + (Array.isArray(entry?.ambiguous_works)
      ? entry!.ambiguous_works.length : 0), 0);
  return {
    ran,
    limitations,
    modeUnavailablePublishers,
    modeUnavailableCount,
    noCorpus: noCorpusCount > 0 && ran.length === 0 && refusedCount === 0
      && invalidCount === 0,
    ambiguityUnits,
    invalidCount,
    anyRefused: refusedCount > 0,
    // A capability REFUSAL is what the filter-limitation copy explains. A publisher that
    // merely lacked the hybrid retrieval mode refused no filter, so it must not select copy
    // blaming a filter that was never refused.
    allRefused: ran.length === 0 && refusedCount > 0 && invalidCount === 0
      && noCorpusCount === 0,
  };
}

/**
 * What an empty governed surface may claim (round 4, O1/O2). `no_match` speaks for the corpus
 * and requires every envelope to have actually run; any invalid envelope, or an entirely
 * empty response, is `incomplete_response` and claims nothing. An all-refused call is a
 * coverage statement. Any ran hit renders results regardless of invalid siblings, whose rows
 * never render.
 */
export function searchAbsenceState(
  partition: GovernedPartition,
  ranHitCount: number,
): "has_results" | "partial_results" | "all_refused" | "no_corpus" | "mixed_no_match"
  | "no_match" | "incomplete_response" {
  // Rows exist, but a sibling response was unusable: the answer is incomplete and says so
  // rather than presenting itself as the whole of what Lex holds.
  if (ranHitCount > 0) {
    return partition.invalidCount > 0 ? "partial_results" : "has_results";
  }
  if (partition.noCorpus) return "no_corpus";
  if (partition.invalidCount > 0) return "incomplete_response";
  if (partition.allRefused) return "all_refused";
  if (partition.ran.length === 0) return "incomplete_response";
  return partition.anyRefused || partition.modeUnavailableCount > 0
    ? "mixed_no_match"
    : "no_match";
}

/**
 * The empty-state sentence is a truth claim, so its scope is typed. `no_match` may speak for
 * the corpus only when every selected publisher ran; a mixed state speaks only for the
 * publishers that could apply the filters; all-refused speaks only about coverage; an
 * incomplete response claims nothing at all. The component renders exactly this decision.
 */
export function searchEmptyPresentation(
  state: "all_refused" | "no_corpus" | "mixed_no_match" | "no_match"
    | "incomplete_response",
): { kind: string; sentence: string } {
  switch (state) {
    case "no_corpus":
      return { kind: "no_corpus", sentence: NO_CORPUS_SENTENCE };
    case "all_refused":
      return { kind: "all_refused", sentence: "No selected publisher ran this query." };
    case "mixed_no_match":
      return {
        kind: "mixed_no_match",
        sentence: "No match was returned by the publishers that could apply these filters.",
      };
    case "incomplete_response":
      return { kind: "incomplete_response", sentence: INCOMPLETE_RESPONSE_SENTENCE };
    default:
      return { kind: "no_match", sentence: "Nothing in the corpus matches that." };
  }
}

/**
 * The scoped sentence for a mixed zero-result outcome, per governed operation: a whole-scope
 * absence claim is unprovable while any publisher refused, so the copy names only the
 * publishers that ran.
 */
export const MIXED_ZERO_SENTENCES: Record<string, string> = {
  search: "No match was returned by the publishers that could apply these filters.",
  changes_in_period:
    "No change was returned by the publishers that could apply these filters.",
  in_force_on:
    "No in-force state was returned by the publishers that could apply these filters.",
};

/**
 * The one cleared-result tuple: every result-bearing state a search request can set, in one
 * place, so the empty-query, request-start and error transitions cannot drift apart and
 * strand a stale notice or a stale mode badge (round 4, O3) on an empty workspace.
 */
export interface SearchResultsState<W, A> {
  works: W[];
  articles: A[];
  error: string | undefined;
  modeUsed: "hybrid" | "keyword" | undefined;
  modeUnavailable: string | undefined;
  expansions: string[];
  limitations: PublisherLimitation[];
  absence: "has_results" | "partial_results" | "all_refused" | "no_corpus"
    | "mixed_no_match" | "no_match" | "incomplete_response";
}

/** The one cleared tuple: every result-bearing key, one place, three transitions share it. */
export const clearedSearchResults = <W, A>(): SearchResultsState<W, A> => ({
  works: [], articles: [], error: undefined, modeUsed: undefined, modeUnavailable: undefined,
  expansions: [], limitations: [], absence: "no_match",
});

/** A transport failure: cleared results carrying only the fixed error sentence. */
export const searchResultsFromError = <W, A>(error: string): SearchResultsState<W, A> => ({
  ...clearedSearchResults<W, A>(), error,
});

const boundedExpansion = (value: unknown): value is string =>
  typeof value === "string" && value.length > 0 && value.length <= 128;

/**
 * The search response projector (round 4, O3/O4): the ONE production path from a raw governed
 * response to the rendered state, called identically by the component and its tests. Used and
 * tried retrieval facts and query expansions derive exclusively from the validated ran
 * partition, never from refused, unavailable or invalid envelopes; the actual mode lives in
 * the returned state so every transition clears it structurally. The `build` callback maps
 * ran hits to the component's presentation rows and reports the pre-cap hit count.
 */
export function projectSearchResponse<W, A>(
  raw: unknown,
  build: (ranEnvelopes: unknown[]) => { works: W[]; articles: A[]; ranHitCount: number },
): SearchResultsState<W, A> {
  const envelopes = Array.isArray(raw) ? raw : [raw];
  const partition = partitionGovernedResponse("search", envelopes);
  const ranRecords = partition.ran
    .map(record)
    .filter((item): item is Record<string, unknown> => item !== null);
  const usedHybrid = ranRecords.some((entry) => entry.retrieval_mode === "hybrid");
  const usedKeyword = ranRecords.some((entry) => entry.retrieval_mode === "keyword");
  const expansions = [...new Set(ranRecords
    .flatMap((entry) => Array.isArray(entry.query_expansions) ? entry.query_expansions : [])
    .filter(boundedExpansion))] as string[];
  const modeUnavailable = partition.modeUnavailableCount > 0
    ? `Words + meaning is unavailable${partition.modeUnavailablePublishers.length > 0
      ? ` for ${partition.modeUnavailablePublishers.join(", ")}` : ""}: its signed retrieval `
      + "benchmark has not authorized it. Choose Exact words."
    : undefined;
  const built = build(partition.ran);
  return {
    works: built.works,
    articles: built.articles,
    error: undefined,
    modeUsed: usedHybrid ? "hybrid" : usedKeyword ? "keyword" : undefined,
    modeUnavailable,
    expansions,
    limitations: partition.limitations,
    absence: searchAbsenceState(partition, built.ranHitCount),
  };
}

/** One governed non-search projection: validated rows, counts and the typed empty state. */
export interface GovernedProjection {
  partition: GovernedPartition;
  /** The typed empty state; null while rows or ambiguity units exist. */
  empty: "all_refused" | "no_corpus" | "mixed_no_match" | "none_matched"
    | "incomplete_response" | "ambiguous_only" | null;
  /** Rows rendered, but a sibling response was unusable and must be disclosed. */
  partial: boolean;
}

/**
 * The changes and in-force projector core (round 4, O4): the same closed partition and empty
 * decision the components render, importable by tests. Row mapping stays with the caller;
 * the DECISION does not.
 */
export function projectGovernedEmptiness(
  tool: string,
  raw: unknown,
  visibleRowCount: number,
): GovernedProjection {
  const envelopes = Array.isArray(raw) ? raw : [raw];
  const partition = partitionGovernedResponse(tool, envelopes);
  const partial = partition.invalidCount > 0;
  if (visibleRowCount > 0) return { partition, empty: null, partial };
  // Ambiguity units are held content, so this is not absence. But it is not a result either:
  // round 6 returned null here, which let the caller render a positive total beside an empty
  // list (PR293 review, O2). It is its own state, and the surface must ask for clarification
  // rather than report a count it cannot itemise.
  if (partition.ambiguityUnits > 0) {
    return { partition, empty: "ambiguous_only", partial };
  }
  if (partition.noCorpus) return { partition, empty: "no_corpus", partial };
  if (partial) return { partition, empty: "incomplete_response", partial };
  if (partition.allRefused) return { partition, empty: "all_refused", partial };
  if (partition.ran.length === 0) {
    return { partition, empty: "incomplete_response", partial };
  }
  return {
    partition,
    empty: partition.anyRefused ? "mixed_no_match" : "none_matched",
    partial,
  };
}
