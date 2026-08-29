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
 * whose counts sit inside the producer's own integer range and cohere with its rows. A count the
 * producer cannot mint is malformed, not merely large. A refusal without its typed
 * limitation is invalid, not an evidence-free refusal. Missing, unknown, misspelled or
 * malformed envelopes are `invalid`: they authorize neither rows nor absence claims, and their
 * presence makes an otherwise empty response `incomplete_response`, never `no_match`.
 *
 * Fail closed means: malformed input is IGNORED, never rendered, and can never suppress a
 * primary view. Nothing here copies or logs query text; every sentence is fixed.
 */

import { publisherIdentity } from "./publisherIdentity.ts";
// IMPORTED, not copied. The search coherence table is read from
// tests/Lex.Tests/search-population-contract.json by exactly one validator, and a second
// reading of it here would put three copies of one rule in the workspace. The direction is
// acyclic: searchPopulation.ts imports publisherIdentity.ts and nothing else, and it takes its
// classifier as a callback rather than importing this module back.
import { exclusionsOf, MAX_PRODUCER_COUNT, validateSearchPopulation } from "./searchPopulation.ts";

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

/**
 * The four filters the index itself checks. This set is signed: CapabilityManifest.RequestedFilters
 * returns exactly these, Populated throws for anything else, and
 * VerifyCapabilityManifestMatchesDocuments compares the stored manifest against a recompute, so
 * widening it would invalidate every signed index stamp. Do not add to this set.
 */
export const MANIFEST_FILTERS = new Set([
  "act_form", "binding_status", "domain", "hierarchy",
]);

/**
 * Filters a producer may refuse outside the signed manifest. `publisher_metadata_identifier` is
 * answerable only by an extended work catalog; an older index evaluates it as an ordinary
 * predicate and returns an authoritative-looking zero, so search refuses instead (Codex contract
 * amendment, 2026-08-28).
 *
 * This set is deliberately NOT part of MANIFEST_FILTERS, and the two must stay separate. Merging
 * them would either throw inside the manifest path or invalidate signed stamps. Nor can this one
 * simply be omitted: an unrecognized filter name makes the whole refusal classify as invalid, so
 * the client would present an honest capability refusal as a malformed response, which is exactly
 * what never-implied rule 10 forbids.
 */
export const GUARDED_FILTERS = new Set(["publisher_metadata_identifier"]);

/** The governed filter identifiers; anything else in the list is dropped. */
export const GOVERNED_FILTERS = new Set([...MANIFEST_FILTERS, ...GUARDED_FILTERS]);

/** The retrieval modes a successful search envelope must declare. */
const RETRIEVAL_MODES = new Set(["keyword", "hybrid"]);

/**
 * Search's own non-ran class: the publisher and its index boundary are mounted, the requested
 * retrieval mode is not authorized for it. Never a filter refusal, and never an absence claim.
 */
const RETRIEVAL_MODE_UNAVAILABLE = "retrieval_mode_unavailable";

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

/**
 * The largest name list this sentence will speak for. `SourceAdapterRegistry` registers exactly
 * two publishers, "lu-legilux" and "eu-eurlex", so a handful is already generous headroom and a
 * longer list is not a publisher list whatever it claims to be. The sentence names rather than
 * counts, so it cannot absorb a long list by summarizing it; naming nobody and leaving the
 * generic notice to stand alone is the only honest answer to one.
 */
const CONFLICTED_PUBLISHER_CAP = 4;

/**
 * O20. The sentence takes `unknown` and validates HERE rather than trusting a `string[]`
 * annotation at a call site, because this field reaches `UiEffect` as transport data from the
 * assistant as well as from the local projector, and an annotation is erased at runtime and
 * defends nothing against a payload. Validating in the function covers every present and future
 * caller instead of the one that happened to be audited.
 *
 * THE NON-EMPTY STRING IS THE TRAP, and it is why `Array.isArray` is the first line rather than
 * any test on `length`. A string HAS a `length`, so a guard written against emptiness admits one
 * and everything after it walks the CHARACTERS: the grammar in `publisherIdentity` is
 * `/^[a-z0-9_-]+$/` bounded at 1 to 64, so every single character of "eu" is a perfectly legal
 * publisher identity and the sentence renders "Nothing from e, u is shown here." Nothing throws,
 * nothing looks broken, and the product has named two publishers that do not exist.
 *
 * Returns the validated names, or null for anything that is not exactly a publisher list.
 */
const conflictedPublisherNames = (value: unknown): string[] | null => {
  if (!Array.isArray(value)) return null;
  if (value.length > CONFLICTED_PUBLISHER_CAP) return null;
  const names = Array.from(value, (item) => publisherIdentity(item));
  // One invalid member voids the WHOLE set rather than being filtered out of it. Known exclusions
  // work this way and for the same reason: a list with a member quietly dropped still presents
  // itself as the complete set of withheld publishers, so it names fewer than were actually
  // withheld while looking exactly like an honest answer. Naming none is the smaller lie.
  if (names.some((name) => name === undefined)) return null;
  // The projector emits this list sorted and distinct, so a repeat did not come from the
  // projector, and this sentence has no business rendering one publisher twice.
  if (new Set(names).size !== names.length) return null;
  return names as string[];
};

/**
 * Names the publishers whose every claim was withheld, or nothing when there are none,
 * and nothing when the list is not one the projector could have produced (O20, below).
 *
 * Named rather than counted. A conflicted publisher is not merely missing: every claim it made
 * was withheld, and a reader deciding whether this answer covers what they care about needs to
 * know which publisher that was.
 *
 * The copy states only the fact both causes share, that more than one answer arrived from that
 * publisher, and says nothing about whether those answers agreed. It used to say the publisher
 * answered in ways that contradict each other, which was true while a conflict meant its units
 * disagreed about kind. A publisher is now conflicted on the unit COUNT alone: the reader registry
 * is keyed by collection, so the producer can emit at most one unit per publisher and a second one
 * is illegitimate even when it is byte-identical to the first. In that case the two units
 * contradict each other in no way whatever, and the old sentence asserted a specific falsehood on
 * screen. As with the population wording before it, a specific cause that is wrong in half the
 * cases is worse than a general one that holds in all of them.
 *
 * "Each publisher named" rather than "that publisher": the list can carry several, and the
 * function's contract is to name them rather than count them.
 */
export function conflictedPublishersSentence(conflicted: unknown): string | undefined {
  const names = conflictedPublisherNames(conflicted);
  // Failing closed removes the NAMES and nothing else. `PartialResponseNotice` renders the
  // incompleteness disclosure from the partition's own counts and only the inner paragraph from
  // this value, so a hostile payload costs the reader the publisher names while the disclosure
  // that something was incomplete still stands.
  if (names === null || names.length === 0) return undefined;
  return `Nothing from ${names.join(", ")} is shown here. This response carried more than one `
    + "answer from each publisher named, and Lex stands behind a publisher's claims only when "
    + "exactly one answer arrives from it.";
}

/** The fixed sentence for a response that cannot support results or absence claims. */
export const INCOMPLETE_RESPONSE_SENTENCE =
  "This response was incomplete, so Lex cannot show results or state what is absent. "
  + "Try the request again.";

/**
 * JURISDICTION ONLY, and it stays separate from the publisher validator on purpose.
 *
 * Publisher and jurisdiction are different vocabularies with different grammars, and the producer
 * says so itself. `McpCore.SelectReaders` compares publisher with `reader.Collection == publisher`
 * (ordinal) and jurisdiction with `StringComparison.OrdinalIgnoreCase` in the same expression.
 * `LegalOperationCatalog` documents the jurisdiction argument as "optional jurisdiction code from
 * index metadata, e.g. LU or EU", so UPPER CASE is a jurisdiction value the producer really does
 * emit, and this validator must keep accepting it. Routing jurisdiction through the publisher
 * validator would refuse "LU" outright and drop a field the reader is entitled to see.
 */
const boundedIdentifier = (value: unknown): string | undefined =>
  typeof value === "string" && value.length > 0 && value.length <= 64
    && /^[a-z0-9_-]+$/i.test(value)
    ? value
    : undefined;

/** `timeline_semantics` is free stamp text, not an identifier, so only the bound applies. */
const MAX_TIMELINE_SEMANTICS = 64;

/**
 * The population basis. The producer's longest is in_force_on's
 * "distinct non-withdrawn works in the selected publisher and legal metadata scope", so this is
 * headroom rather than a fit, and it is a bound on a sentence rather than a grammar for one:
 * the string is the producer's own and is never rewritten here.
 */
const MAX_SCOPE_BASIS = 120;

/** A bounded, non-empty string kept verbatim. Refuses rather than trims, like every validator
    in this module: a padded value is evidence that something upstream is not the producer. */
const boundedText = (value: unknown, maximum: number): string | undefined =>
  typeof value === "string" && value.length > 0 && value.length <= maximum
    ? value
    : undefined;

/**
 * The producer's own numeric range, verified against the authoritative chain rather than assumed.
 * Every count this module consumes is minted as a C# `int` end to end: `IndexReader.ChangeTotals`
 * is declared `(int Works, int Versions)` and reads its two columns with `GetInt32`,
 * `Rows.InForcePage.TotalGroups` is `int` and `IndexReader.InForceOn` computes it into a local
 * `int total`, and `McpCore` publishes exactly those values as `works_changed`, `new_versions` and
 * `total_works_in_force`. Nothing in that chain is a `long`, and the assistant-side combiner reads
 * them back with `GetValue<int>()`. A count above this is therefore not merely imprecise, it is a
 * number the producer cannot mint.
 *
 * IMPORTED, not restated. `validateSearchPopulation` has to apply the same ceiling to
 * `works_in_scope`, and it lives in the module this one imports, so the constant lives there and
 * both doors read one value. A local copy here was the shape of the O15 defect: the parser
 * refused 2147483648 and the search footer published it.
 */

/**
 * A count the projection consumes: it must be PRESENT, a nonnegative integer, and inside the
 * producer's range. Treating a missing count as zero is the fail-open pattern this contract
 * exists to close, and `Number.isFinite` plus `Number.isInteger` is not that range: it admits
 * 1e20 and 2^53, which are exact doubles and "integers" and legal counts nothing can have sent.
 * Such a value passed the count/row coherence checks unchanged and then reached the screen as a
 * legal figure. Out of range fails closed exactly as a malformed shape does: null authorizes
 * neither rows nor a count.
 */
const requiredCount = (value: unknown): number | null =>
  typeof value === "number" && Number.isSafeInteger(value)
    && value >= 0 && value <= MAX_PRODUCER_COUNT
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
    // The publisher is the join key with the strip and the population footer, so it uses the one
    // shared non-normalizing validator. The jurisdiction beside it does not, and must not.
    publisher: publisherIdentity(record.publisher),
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
  | { kind: "ran"; entry: unknown; publisher?: string }
  | { kind: "refused"; limitation: PublisherLimitation }
  | { kind: "mode_unavailable"; publisher?: string }
  | { kind: "no_corpus" }
  | { kind: "invalid" };

const record = (value: unknown): Record<string, unknown> | null =>
  typeof value === "object" && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;

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

/** A non-empty string field, which is what every renderer actually consumes. */
const text = (value: unknown): boolean =>
  typeof value === "string" && value.length > 0;

// ---------------------------------------------------------------------------
// The parsed governed response: one door for untrusted input
// ---------------------------------------------------------------------------
//
// THREE independent authorities used to parse the same untrusted MCP response, not two, and the
// comment that stood here undercounted them: `normalizeSearchResponse` in searchPopulation.ts,
// `partitionGovernedResponse` below, and `envelopeStripRows`, which walks the response for the
// index strip with no classification at all. Search called all three in three consecutive
// statements. Entry points that each take `unknown` can disagree, and they did: two
// byte-identical search units for one publisher produced `complete: true` with one surviving
// population on one side and `incomplete_response` with zero rows on the other, so the footer
// reported a scope for a publisher the page was showing nothing from. The repair is not a rule
// about duplicates. It is a shape. ONE function takes `unknown`, everything downstream takes a
// parsed unit, and disagreement stops being expressible rather than merely being prevented.
//
// This is that function, and it is now the only parse on the search path. `classifyEnvelope`,
// `partitionGovernedResponse`, `normalizeSearchResponse`, `projectSearchResponse` and
// `projectGovernedEmptiness` are all views of its output. `envelopeStripRows` is NOT yet, and
// that is the one authority this cutover did not close: it is called from App.tsx, which this
// change was not permitted to edit, so it still walks the raw response. Until it is routed
// through here, an entry the table refuses can still contribute a build date and a signature
// claim to the index strip.

/**
 * The governed operations, closed at COMPILE time rather than carried as a string. A fourth tool
 * must be a type error at the call site rather than a silent invalid classification at runtime.
 */
export type GovernedTool = "search" | "changes_in_period" | "in_force_on";

/**
 * Which claim a disclosed denominator makes, carried so no renderer can swap the two.
 *
 * They are different populations from different producer queries. `works_in_scope` is narrowed by
 * the request's own metadata filters: `McpCore.SearchPopulation` reads
 * `reader.SearchPopulationTotal(filter)`, and changes_in_period reads
 * `r.PopulationTotal(kinds, filter)`. `works_covered` is not narrowed by anything the request
 * sent: in_force_on reads `r.Coverage(1).Groups`, the publisher's versioned works. Presenting one
 * as the other tells the reader a filtered list was measured against an unfiltered denominator,
 * or the reverse.
 */
export type ScopeMeasure = "works_in_scope" | "works_covered";

export interface ScopeDisclosure {
  /** Carried so no renderer can swap the two claims. */
  measure: ScopeMeasure;
  /** Non-negative, a safe integer, and inside the producer's own Int32 ceiling. */
  works: number;
  /** The producer's own string, bounded at parse time, never invented and never repaired. */
  basis: string;
  /** Search only. changes_in_period and in_force_on publish neither field. */
  scopeFiltersApplied: boolean | undefined;
  /** Search only, and the field that decides whether a scope may join a "searched N" sentence. */
  queryRan: boolean | undefined;
  /** Bounded, de-duplicated and order-insensitive, through the shared population helper. */
  knownExclusions: string[];
}

/**
 * A publisher's disclosed scope, or the positive statement that there is none to disclose.
 *
 * `none_published` is legitimate and is a claim about the CONTRACT, not a shrug: the producer
 * emits no population at all on a `changes_in_period` or `in_force_on` refusal, and a reader is
 * entitled to know that rather than to be shown a blank. An absent denominator modelled as a bare
 * `undefined` reads as "nothing to say", which is the exact shape of the defect this lane exists
 * to close.
 *
 * `unreadable` is the third answer, and it is a distinct fact rather than a shade of the second:
 * a population the producer does publish but that will not validate is not the producer's own
 * silence, and collapsing the two would be the very lie the paragraph above forbids, one step
 * removed.
 *
 * IT IS ALSO NOT MERELY A LABEL. Where the tool's table requires a scope, this verdict
 * invalidates the whole claim-bearing unit: no rows, no denominator, no limitation, no mode and
 * no absence claim survive it. See `claimed` in `classifyEntry`. The earlier shape recorded the
 * problem in this field and let the unit go on authorizing all five, which is a marker that looks
 * like a check while the guarantee it implies happens nowhere.
 */
export type Scope =
  | { kind: "disclosed"; scope: ScopeDisclosure }
  | { kind: "none_published" }
  | { kind: "unreadable"; reason: string };

interface UnitCore {
  /**
   * Where the response carried this unit. `parseGovernedResponse` sorts its units by publisher,
   * and several callers must recover transport order exactly: the limitation cap decides WHICH
   * eight survive, and the population contract asserts the ran list entry for entry. Carrying
   * the position is how that is recovered without a second pass over the raw response, and it
   * replaces an identity-keyed Map that silently merged two entries that were one object.
   */
  index: number;
  /** From publisherIdentity, never trimmed and never case-folded. */
  publisher: string;
  /** Already inside this tool's closed status set. */
  status: string;
  /**
   * From boundedIdentifier, case-preserving: the producer emits "LU". Load-bearing, not
   * decoration. App.tsx reads jurisdiction and timeline_semantics off the raw entries in both
   * governed effects to render chips, and no node test imports App.tsx, so a unit that dropped
   * them would take those chips off the screen with the suite still green.
   */
  jurisdiction: string | undefined;
  /** The same, bounded at 64: `publisher_applicability` or `official_consolidation_state`. */
  timelineSemantics: string | undefined;
  scope: Scope;
  /**
   * TRANSITIONAL, and now down to two readers. The raw entry this unit was parsed from.
   *
   * `GovernedPartition.ran` still returns the original entry objects, because App.tsx reads
   * `changes`, `works`, `envelope.jurisdiction`, `envelope.timeline_semantics` and
   * `population.basis` off them and passes them to the api.ts sum helpers, and this change was
   * not permitted to edit App.tsx. `envelopeStripRows` is the other, and it does not even come
   * through here: it walks the raw response for itself.
   *
   * The parsed unit already models everything App.tsx reads. This field goes when those call
   * sites move to `ranUnits`, which is the point of the whole exercise. Nothing in this module
   * reads it for its CONTENT any more: `partitionOf` passes it through into `ran`, and
   * `classifyEnvelope` carries it in the legacy class it still returns, and that is all.
   */
  entry: unknown;
}

export interface RanUnit extends UnitCore {
  kind: "ran";
  /** Each row satisfies this tool's row schema. */
  rows: Record<string, unknown>[];
  /** Empty except on in_force_on, the only tool with an ambiguity field. */
  ambiguities: Record<string, unknown>[];
  /** Every count this tool requires, present and inside the producer's Int32 ceiling. */
  counts: Readonly<Record<string, number>>;
  /**
   * The producer's paging receipts, typed and retained. Absent members mean the response said
   * nothing, which the projector must be able to tell apart from a receipt that said no.
   */
  paging: PagingEvidence;
  /** Search only. */
  retrievalMode: "keyword" | "hybrid" | undefined;
  /** Search only, bounded at 128 characters each and de-duplicated. */
  expansions: string[];
}

export interface RefusedUnit extends UnitCore {
  kind: "refused";
  /** `limitation.publisher` equals `this.publisher`: one validator mints both. */
  limitation: PublisherLimitation;
}

export interface ModeUnavailableUnit extends UnitCore { kind: "mode_unavailable"; }

export type PublisherUnit = RanUnit | RefusedUnit | ModeUnavailableUnit;

export interface GovernedResponse {
  tool: GovernedTool;
  /** At most one per publisher, sorted ordinally by publisher. */
  units: PublisherUnit[];
  /** Publishers that sent more than one claim-bearing unit, sorted. */
  conflicted: string[];
  /** Claim-bearing units with no mintable publisher identity. */
  unattributed: number;
  /**
   * Publishers whose claim was invalidated by an unreadable required scope, sorted and distinct.
   * They contribute nothing: no rows, no denominator, no limitation, no mode. They are named
   * because "something was withheld" with nobody to ask about is a worse disclosure than none.
   */
  unreadable: string[];
  /** Today's invalidCount: malformed units, incoherent no-corpus units, conflicts, unattributed. */
  unusable: number;
  /** Exactly one bare `no_corpus_mounted` unit and nothing else. */
  noCorpus: boolean;
  /**
   * Whether the units below are the whole response or only the part of it that survived. A
   * denominator drawn from `usable_units_only` describes fewer publishers than answered.
   */
  scopeAuthority: "every_unit_usable" | "usable_units_only";
}

/** The facts a status invariant is allowed to see: no raw entry, so it cannot widen itself. */
interface StatusFacts {
  status: string;
  rowCount: number;
  ambiguityCount: number;
  counts: Readonly<Record<string, number>>;
}

// ---------------------------------------------------------------------------
// Paging receipts (O16)
// ---------------------------------------------------------------------------
//
// WHY THESE ARE NOT DECORATION, and why O12 cannot be closed without them: they are what decides
// whether zero rows means "there is nothing" or "you are looking at an empty slice of a page". A
// projector that infers absence from a row count it cannot situate is sometimes right and never
// checkable, which is the opposite of the claim this product makes.
//
// The shapes are PER TOOL, which is why they live in TOOL_SCHEMAS beside the counts they cohere
// with rather than in one generic validator. Read from McpCore.cs:
//
//   MarkPublisherSet, 701-712, stamped on every item of all three tools:
//     publisher_result_set = { total, returned = Math.Min(total, maximum),
//                              maximum = MaximumPublisherRows, truncated = total > maximum }
//   MarkResponseRows, 713-725, stamped on every item of search and in_force_on:
//     response_row_set = { maximum, returned, truncated }
//   in_force_on, 1155-1157:
//     total_works_in_force = total, offset = localOffset,
//     truncated = total > localOffset + pageUnits, pageUnits = rows.Count + ambiguities.Count
//   search, 1575-1577: MarkPublisherSet and MarkResponseRows only, nothing entry-level
//   changes_in_period, 1813-1814 and 1865-1882:
//     shown = rows.Length, offset = offset,
//     response_row_set = { maximum = limit, returned = shown, truncated = works_changed > shown },
//     global_response_row_set = { offset, maximum = limit, returned = candidates.Count,
//                                 total = totalAcrossPublishers,
//                                 truncated = total > offset + returned }
//
// WHAT IS CHECKED AND WHAT IS NOT. Every receipt is shape-checked and range-checked. Only the
// equalities that THIS entry's own numbers determine are checked as equalities: search's
// response_row_set counts rows across every publisher (limit minus remainingResults), so nothing
// in it follows from one entry and asserting one would refuse a legitimate response.
//
// ABSENCE IS TOLERATED, PRESENCE IS BINDING, and that boundary is deliberate rather than
// accidental. A receipt the response does not carry is a receipt this client cannot use, and O12
// is closed from the primary counts, which ARE required. A receipt it does carry must be the
// producer's own arithmetic or the unit is not the producer's. Requiring presence would be a
// stronger rule and a larger change: it would invalidate every response shape not verified
// against a live server, and it is a separate decision from refusing a contradiction.
//
// The entry-level fields are ALL OR NOTHING, because the producer mints them in one object
// literal. Half of a paged receipt is not a partial disclosure; it is a shape nothing emits.

/** `publisher_result_set`, whose two equalities follow from its own three numbers. */
export interface PublisherSetReceipt {
  total: number;
  returned: number;
  maximum: number;
  truncated: boolean;
}

/** `response_row_set`. Its `returned` is cross-publisher on search and in_force_on. */
export interface RowSetReceipt {
  maximum: number;
  returned: number;
  truncated: boolean;
}

/** `global_response_row_set`, changes_in_period only. */
export interface GlobalRowSetReceipt {
  offset: number;
  maximum: number;
  returned: number;
  total: number;
  truncated: boolean;
}

/**
 * The producer's paging evidence for one unit, TYPED AND RETAINED rather than collapsed to a
 * yes or no here. The projector has to be able to tell "the producer said this page is a slice"
 * from "the producer said nothing about it", and a boolean minted at parse time cannot say which.
 */
export interface PagingEvidence {
  /** The entry's own `offset`. changes_in_period and in_force_on. */
  offset: number | undefined;
  /** The entry's own `shown`. changes_in_period only. */
  shown: number | undefined;
  /** The entry's own `truncated`. in_force_on only. */
  truncated: boolean | undefined;
  publisherSet: PublisherSetReceipt | undefined;
  rowSet: RowSetReceipt | undefined;
  globalRowSet: GlobalRowSetReceipt | undefined;
}

/** Which receipts one tool stamps, and the arithmetic its own entry determines. */
interface PagingSchema {
  /** `offset` is stamped on this tool's ran entry. */
  hasOffset: boolean;
  /** `shown` is stamped. */
  hasShown: boolean;
  /** An entry-level `truncated` is stamped. */
  hasTruncated: boolean;
  /** `global_response_row_set` is stamped. */
  hasGlobalRowSet: boolean;
  /** The equalities this entry's own numbers determine. Everything else is shape-checked only. */
  coherent: (facts: PagingFacts) => boolean;
}

interface PagingFacts {
  rowCount: number;
  ambiguityCount: number;
  counts: Readonly<Record<string, number>>;
  paging: PagingEvidence;
}

/** A boolean the producer actually minted, never a truthy stand-in. */
const flag = (value: unknown): boolean | undefined =>
  typeof value === "boolean" ? value : undefined;

/**
 * One receipt object, or null for a shape the producer could not have emitted. `undefined` means
 * the field was absent, which is the one thing this returns that is not a verdict.
 */
function publisherSetOf(value: unknown): PublisherSetReceipt | null | undefined {
  if (value === undefined) return undefined;
  const o = record(value);
  if (!o) return null;
  const total = requiredCount(o.total);
  const returned = requiredCount(o.returned);
  const maximum = requiredCount(o.maximum);
  const truncated = flag(o.truncated);
  if (total === null || returned === null || maximum === null || truncated === undefined) {
    return null;
  }
  // MarkPublisherSet mints all three from `total` and one constant, so the receipt has to agree
  // with itself. The constant itself is NOT pinned here: `MaximumPublisherRows` is 8 today and
  // the two equalities hold whatever it becomes, so pinning the 8 would buy nothing and would
  // break a legitimate response the day it moves.
  if (returned !== Math.min(total, maximum)) return null;
  if (truncated !== (total > maximum)) return null;
  return { total, returned, maximum, truncated };
}

function rowSetOf(value: unknown): RowSetReceipt | null | undefined {
  if (value === undefined) return undefined;
  const o = record(value);
  if (!o) return null;
  const maximum = requiredCount(o.maximum);
  const returned = requiredCount(o.returned);
  const truncated = flag(o.truncated);
  if (maximum === null || returned === null || truncated === undefined) return null;
  return { maximum, returned, truncated };
}

function globalRowSetOf(value: unknown): GlobalRowSetReceipt | null | undefined {
  if (value === undefined) return undefined;
  const o = record(value);
  if (!o) return null;
  const offset = requiredCount(o.offset);
  const maximum = requiredCount(o.maximum);
  const returned = requiredCount(o.returned);
  const total = requiredCount(o.total);
  const truncated = flag(o.truncated);
  if (offset === null || maximum === null || returned === null || total === null
    || truncated === undefined) {
    return null;
  }
  // `truncated = totalAcrossPublishers > offset + candidates.Count`, all three of which the
  // receipt carries, so this equality is the receipt's own arithmetic and not an inference.
  if (truncated !== (total > offset + returned)) return null;
  return { offset, maximum, returned, total, truncated };
}

/**
 * The paging evidence a ran entry carries, or null for metadata the producer cannot have minted.
 *
 * RAN ONLY, for the same reason counts and scope are: the numbers a receipt has to cohere with
 * exist only on the executed path, and applying the rule to a refusal would invalidate every
 * in-force refusal for lacking a receipt it was never asked to carry.
 */
function pagingOf(schema: PagingSchema, entry: Record<string, unknown>): PagingEvidence | null {
  const rawOffset = schema.hasOffset ? entry.offset : undefined;
  const rawShown = schema.hasShown ? entry.shown : undefined;
  const rawTruncated = schema.hasTruncated ? entry.truncated : undefined;
  const present = [rawOffset, rawShown, rawTruncated].filter((v) => v !== undefined).length;
  const expected = [schema.hasOffset, schema.hasShown, schema.hasTruncated]
    .filter((declared) => declared).length;
  // All or nothing: one object literal mints them together, so half of a set is a shape nothing
  // emits rather than a partial disclosure.
  if (present !== 0 && present !== expected) return null;
  const offset = rawOffset === undefined ? undefined : requiredCount(rawOffset);
  const shown = rawShown === undefined ? undefined : requiredCount(rawShown);
  const truncated = rawTruncated === undefined ? undefined : flag(rawTruncated);
  if (offset === null || shown === null) return null;
  if (rawTruncated !== undefined && truncated === undefined) return null;
  const publisherSet = publisherSetOf(entry.publisher_result_set);
  const rowSet = rowSetOf(entry.response_row_set);
  const globalRowSet = schema.hasGlobalRowSet
    ? globalRowSetOf(entry.global_response_row_set)
    : undefined;
  if (publisherSet === null || rowSet === null || globalRowSet === null) return null;
  return { offset, shown, truncated, publisherSet, rowSet, globalRowSet };
}

/**
 * How one (tool, status) discloses its population.
 *
 * `search_contract` defers to `validateSearchPopulation`, which reads the jointly accepted
 * coherence table in tests/Lex.Tests/search-population-contract.json. It is IMPORTED rather than
 * copied: a copy would put three readings of that table in one commit.
 *
 * `bounded_disclosure` is shape and bounds ONLY. There is no accepted coherence contract for the
 * other two populations, and they carry neither `scope_filters_applied` nor `query_ran`, so there
 * is nothing to be coherent with. Inventing a table for them would be the same defect this lane
 * exists to stop, written by us this time.
 */
type ScopeRule =
  | { rule: "search_contract" }
  | { rule: "bounded_disclosure"; measure: ScopeMeasure; companionCounts: readonly string[] }
  | { rule: "none_published" };

interface ToolSchema {
  successStatuses: ReadonlySet<string>;
  rowsField: "hits" | "changes" | "works";
  rowValid: (row: Record<string, unknown>) => boolean;
  ambiguityField: string | undefined;
  requiredCounts: readonly string[];
  /**
   * The one count the shipped validator has always tolerated as absent, reading a missing value
   * as zero. Declared rather than assumed, because "required" and "tolerated absent" are the two
   * readings that decide whether a real `no_changes_in_period` envelope is evidence or garbage,
   * and the classifier's current answer for `new_versions` is the second one.
   */
  absentCountsAsZero: ReadonlySet<string>;
  statusInvariant: (facts: StatusFacts) => boolean;
  /** Keyed by status, and every status this tool can be classified under has an entry. */
  scopeRule: Readonly<Record<string, ScopeRule>>;
  /** Which paging receipts this tool stamps, and the arithmetic its own entry determines. */
  paging: PagingSchema;
  /**
   * The count whose positivity makes an absence claim false when nothing is visible (O12).
   * `undefined` for search, which publishes no per-publisher total and decides absence from the
   * fused hit count instead.
   */
  absenceTotal: string | undefined;
  requiresRetrievalMode: boolean;
}

/**
 * The per-tool schema, keyed by GovernedTool so a fourth tool is a compile error until its schema
 * exists. Every row is pinned against src/Lex.Mcp/McpCore.cs:
 *
 *   search              statuses `ok`; rows `hits`; no counts; no ambiguity field; a population on
 *                       the ran, mode-unavailable AND refusal paths; `retrieval_mode` required.
 *   changes_in_period   statuses `ok` and `no_changes_in_period`; rows `changes`; counts
 *                       `works_changed` and `new_versions`; no ambiguity field; a population on
 *                       the ran path only.
 *   in_force_on         statuses `ok`, `no_result` and `ambiguous_version`; rows `works`; count
 *                       `total_works_in_force`; ambiguity field `ambiguous_works`; a population
 *                       on the ran path only.
 *
 * COUNTS AND SCOPE ARE RAN-ONLY, and the table has to say so. The shipped shape check gets away
 * with leaving it implicit because it is never reached for a refusal; a merged parser that
 * applied `requiredCounts` to every unit would make every in-force refusal invalid for lacking a
 * count it was never sent.
 */
const TOOL_SCHEMAS: Readonly<Record<GovernedTool, ToolSchema>> = {
  search: {
    // McpCore search emits `Envelope(r, McpStatus.Ok)` on the executed path and nothing else.
    successStatuses: new Set(["ok"]),
    rowsField: "hits",
    // The workspace derives every work identity from lex_id.
    rowValid: (row) => text(row.lex_id),
    ambiguityField: undefined,
    requiredCounts: [],
    absentCountsAsZero: new Set(),
    statusInvariant: () => true,
    // The only tool that discloses a population on all three of its paths:
    // `refusal["population"] = SearchPopulation(reader, filter, false, false)`,
    // `["population"] = SearchPopulation(reader, filter, true, false)` beside
    // `McpStatus.RetrievalModeUnavailable`, and `SearchPopulation(r, filter, true, true)` beside
    // the hits. All three go through the accepted coherence contract.
    scopeRule: {
      ok: { rule: "search_contract" },
      [RETRIEVAL_MODE_UNAVAILABLE]: { rule: "search_contract" },
      [LIMITATION_STATUS]: { rule: "search_contract" },
    },
    // MarkPublisherSet and MarkResponseRows only (McpCore 1575-1577). Search stamps no
    // entry-level paging field at all, and its `response_row_set.returned` is
    // `limit - remainingResults`, counted across every publisher, so no equality in it follows
    // from one entry. Shape and range are checked; nothing else can honestly be.
    paging: {
      hasOffset: false, hasShown: false, hasTruncated: false, hasGlobalRowSet: false,
      coherent: () => true,
    },
    // Search publishes hits and a population, never a per-publisher result total, so there is no
    // count here whose positivity could contradict an empty page. Inventing one would be the
    // same defect the scope table refuses for the other two populations.
    absenceTotal: undefined,
    // O3: the producer always declares its actual retrieval mode on a successful search
    // (`["retrieval_mode"] = execution.RetrievalMode`). Admitting an envelope without one lets a
    // response of unknown provenance render. This flag is also what makes
    // `retrieval_mode_unavailable` a class at all: a tool with no retrieval mode cannot have an
    // unauthorized one.
    requiresRetrievalMode: true,
  },
  changes_in_period: {
    // `works == 0 ? McpStatus.NoChangesInPeriod : McpStatus.Ok`.
    successStatuses: new Set(["ok", "no_changes_in_period"]),
    rowsField: "changes",
    // The ranking view reads work and calls string methods on it.
    rowValid: (row) => text(row.work),
    ambiguityField: undefined,
    // `["works_changed"] = works, ["new_versions"] = versions`, both from
    // `IndexReader.ChangeTotals`, declared `(int Works, int Versions)`.
    requiredCounts: ["works_changed", "new_versions"],
    absentCountsAsZero: new Set(["new_versions"]),
    statusInvariant: (facts) => {
      const worksChanged = facts.counts.works_changed;
      if (facts.status === "no_changes_in_period") {
        return worksChanged === 0 && facts.rowCount === 0 && facts.counts.new_versions === 0;
      }
      // Rows may legitimately be empty beside a positive count: the producer emits this
      // publisher's FULL period total beside its slice of one globally merged page, so a
      // publisher outranked out of the current page returns count>0 with zero rows. Only the
      // impossible directions are contradictions.
      if (facts.rowCount > worksChanged) return false;
      if (facts.rowCount > 0 && worksChanged === 0) return false;
      return true;
    },
    // The refusal branch adds `window`, `works_changed`, `new_versions`, `shown` and `offset` to
    // `UnsupportedFilterResult` and no population.
    scopeRule: {
      ok: {
        rule: "bounded_disclosure", measure: "works_in_scope",
        companionCounts: ["expected_works"],
      },
      no_changes_in_period: {
        rule: "bounded_disclosure", measure: "works_in_scope",
        companionCounts: ["expected_works"],
      },
      [LIMITATION_STATUS]: { rule: "none_published" },
    },
    // `["shown"] = rows.Length` and `["offset"] = offset` (McpCore 1813-1814), then a second
    // pass stamps both receipts from those same numbers (1865-1882).
    paging: {
      hasOffset: true, hasShown: true, hasTruncated: false, hasGlobalRowSet: true,
      coherent: (facts) => {
        const { shown, offset, rowSet, globalRowSet } = facts.paging;
        // `["shown"] = rows.Length`. A response claiming to show more or fewer rows than it
        // carries is the cardinality lie O16 names, and it is what lets an empty slice read as
        // a page that showed everything there was.
        if (shown !== undefined && shown !== facts.rowCount) return false;
        if (rowSet !== undefined) {
          // `["returned"] = shown`, `["truncated"] = works_changed > shown`.
          const seen = shown ?? facts.rowCount;
          if (rowSet.returned !== seen) return false;
          if (rowSet.truncated !== (facts.counts.works_changed > seen)) return false;
        }
        // Both are the request's own `offset`, read from one variable in one method.
        if (globalRowSet !== undefined && offset !== undefined
          && globalRowSet.offset !== offset) return false;
        return true;
      },
    },
    // `["works_changed"] = works` is this publisher's FULL period total, beside its slice of one
    // globally merged page. A positive one with nothing on screen means the page is a slice.
    absenceTotal: "works_changed",
    requiresRetrievalMode: false,
  },
  in_force_on: {
    // `total == 0 ? McpStatus.NoResult : ambiguities.Count > 0 ? McpStatus.AmbiguousVersion
    // : McpStatus.Ok`.
    successStatuses: new Set(["ok", "no_result", "ambiguous_version"]),
    rowsField: "works",
    // The in-force view opens rows by work, or by lex_id when the publisher supplies one.
    rowValid: (row) => text(row.work) || text(row.lex_id),
    ambiguityField: "ambiguous_works",
    // `["total_works_in_force"] = total`, from `Rows.InForcePage.TotalGroups`, an `int`.
    requiredCounts: ["total_works_in_force"],
    absentCountsAsZero: new Set(),
    statusInvariant: (facts) => {
      const total = facts.counts.total_works_in_force;
      // Status and counts must agree. no_result means the publisher found nothing, so a
      // positive total contradicts it; round 7 admitted that and rendered a false claim that
      // no state covers the date beside a response reporting five (O2). ambiguous_version
      // means at least one ambiguity unit exists.
      if (facts.status === "no_result"
        && (total !== 0 || facts.rowCount > 0 || facts.ambiguityCount > 0)) return false;
      if (facts.status === "ambiguous_version" && facts.ambiguityCount === 0) return false;
      // As above: one remainingLimit is shared across publishers, so an exhausted page returns
      // zero rows beside this publisher's full total. Ambiguity units also consume the page, so
      // rows plus ambiguities is what must not exceed the total.
      if (facts.rowCount + facts.ambiguityCount > total) return false;
      if (facts.rowCount > 0 && total === 0) return false;
      return true;
    },
    // `UnsupportedFilterResult(r, unsupported, filter, CapabilityManifest.AsOf, date, "works")`
    // is returned unchanged, so an in-force refusal carries no population at all.
    scopeRule: {
      ok: { rule: "bounded_disclosure", measure: "works_covered", companionCounts: [] },
      no_result: { rule: "bounded_disclosure", measure: "works_covered", companionCounts: [] },
      ambiguous_version: {
        rule: "bounded_disclosure", measure: "works_covered", companionCounts: [],
      },
      [LIMITATION_STATUS]: { rule: "none_published" },
    },
    // `["offset"] = localOffset` and `["truncated"] = total > localOffset + pageUnits`, where
    // `pageUnits = rows.Count + ambiguities.Count` (McpCore 1155-1157). Every term is on the
    // entry, so this one is an exact equality rather than a bound.
    paging: {
      hasOffset: true, hasShown: false, hasTruncated: true, hasGlobalRowSet: false,
      coherent: (facts) => {
        const { offset, truncated } = facts.paging;
        if (truncated === undefined || offset === undefined) return true;
        const pageUnits = facts.rowCount + facts.ambiguityCount;
        return truncated === (facts.counts.total_works_in_force > offset + pageUnits);
      },
    },
    // `["total_works_in_force"] = total` is the publisher's whole answer for the date, and one
    // shared remainingLimit can page every row of it out of this response.
    absenceTotal: "total_works_in_force",
    requiresRetrievalMode: false,
  },
};

/**
 * The governed operations at RUNTIME, derived from the table's own keys so the two cannot drift.
 * A limitation naming any other tool is malformed, and this is also the gate that keeps every
 * table lookup below off an attacker's string: `TOOL_SCHEMAS[tool]` is only ever reached through
 * this membership test, exactly as the shipped classifier reached `SUCCESS_STATUSES[tool]`.
 */
const GOVERNED_TOOLS: ReadonlySet<string> = new Set(Object.keys(TOOL_SCHEMAS));

/**
 * One publisher's disclosed scope for one already-classified status.
 *
 * Called ONLY from a branch whose status is already inside a closed set, so `scopeRule[status]`
 * is never indexed with a value the response chose. A plain object answers "toString" with a
 * function from its prototype, and the shipped code was immune to that only because it gated
 * every lookup behind a membership test first.
 */
function parseScope(
  schema: ToolSchema,
  status: string,
  entry: Record<string, unknown>,
): Scope {
  const rule: ScopeRule | undefined = schema.scopeRule[status];
  if (rule === undefined) {
    return { kind: "unreadable", reason: "no scope rule for this status" };
  }
  if (rule.rule === "none_published") {
    // Fail closed, on the precedent already in this file: an object carrying BOTH the terminal
    // no-corpus status and an envelope is a forgery rather than a terminal answer. A population
    // arriving where the producer publishes none is the same kind of object, so it may not be
    // reported as the producer's own silence.
    return entry.population === undefined
      ? { kind: "none_published" }
      : { kind: "unreadable", reason: "a population arrived where the producer publishes none" };
  }
  if (rule.rule === "search_contract") {
    const verdict = validateSearchPopulation(status, entry.population);
    if (!verdict.valid) return { kind: "unreadable", reason: verdict.reason };
    // The contract checks a safe non-negative integer; the producer mints this from a C# `int`
    // like every other count here, so the Int32 ceiling applies to it too.
    const works = requiredCount(verdict.population.works_in_scope);
    if (works === null) {
      return { kind: "unreadable", reason: "works_in_scope is outside the producer's range" };
    }
    return {
      kind: "disclosed",
      scope: {
        measure: "works_in_scope",
        works,
        // Closed vocabulary, both members far inside the basis bound.
        basis: verdict.population.basis,
        scopeFiltersApplied: verdict.population.scope_filters_applied,
        queryRan: verdict.population.query_ran,
        knownExclusions: verdict.population.known_exclusions,
      },
    };
  }
  const population = record(entry.population);
  if (!population) {
    return { kind: "unreadable", reason: "population absent or not an object" };
  }
  const works = requiredCount(population[rule.measure]);
  if (works === null) {
    return { kind: "unreadable", reason: `${rule.measure} is not a count the producer can mint` };
  }
  const basis = boundedText(population.basis, MAX_SCOPE_BASIS);
  if (basis === undefined) {
    return { kind: "unreadable", reason: "basis absent or not a bounded string" };
  }
  // The producer serializes this as ONE string rather than a list; the shared helper takes both
  // and returns a bounded, trimmed, de-duplicated set either way.
  const knownExclusions = exclusionsOf(population.known_exclusions ?? []);
  if (knownExclusions === null) {
    return {
      kind: "unreadable",
      reason: "known_exclusions is not a bounded list of bounded strings",
    };
  }
  // `expected_works` is `Coverage(1).ExpectedWorks`, an `int?`, so absent and null are both the
  // producer's own answer. Nothing carries it, but a value that is neither absent nor a count
  // the producer can mint means this is not the producer's population object.
  for (const name of rule.companionCounts) {
    if (optionalCount(population[name]) === null) {
      return { kind: "unreadable", reason: `${name} is not a count the producer can mint` };
    }
  }
  return {
    kind: "disclosed",
    scope: {
      measure: rule.measure,
      works,
      basis,
      scopeFiltersApplied: undefined,
      queryRan: undefined,
      knownExclusions,
    },
  };
}

/** The validated ran payload, or null for a shape the producer could not have emitted. */
interface RanShape {
  rows: Record<string, unknown>[];
  ambiguities: Record<string, unknown>[];
  counts: Readonly<Record<string, number>>;
  paging: PagingEvidence;
  retrievalMode: "keyword" | "hybrid" | undefined;
  expansions: string[];
}

/**
 * Operation-specific response-shape and count/row coherence, read from the table (round 4, O2).
 * Both governed direct callers request offset 0, so a positive count with zero rows is ordinary
 * paging while rows exceeding their count is a contradiction. A contradictory envelope is
 * invalid: it never becomes results or corpus absence.
 */
function ranShape(
  schema: ToolSchema,
  entry: Record<string, unknown>,
  status: string,
): RanShape | null {
  const rows = entry[schema.rowsField];
  if (!rowsValid(rows) || !rows.every(schema.rowValid)) return null;
  let ambiguities: Record<string, unknown>[] = [];
  if (schema.ambiguityField !== undefined) {
    const raw = entry[schema.ambiguityField];
    if (raw !== undefined) {
      if (!rowsValid(raw) || !raw.every(schema.rowValid)) return null;
      ambiguities = raw;
    }
  }
  const counts: Record<string, number> = {};
  for (const name of schema.requiredCounts) {
    const value = schema.absentCountsAsZero.has(name)
      ? optionalCount(entry[name])
      : requiredCount(entry[name]);
    if (value === null) return null;
    counts[name] = value;
  }
  let retrievalMode: "keyword" | "hybrid" | undefined;
  if (schema.requiresRetrievalMode) {
    const mode = entry.retrieval_mode;
    if (typeof mode !== "string" || !RETRIEVAL_MODES.has(mode)) return null;
    retrievalMode = mode === "hybrid" ? "hybrid" : "keyword";
  }
  if (!schema.statusInvariant({
    status, rowCount: rows.length, ambiguityCount: ambiguities.length, counts,
  })) return null;
  // The producer's own paging arithmetic, checked against this entry's own rows and counts
  // (O16). A receipt that contradicts them is not a detail beside the answer: it is what turns
  // an empty slice of a page into a confident statement that there was nothing to show.
  const paging = pagingOf(schema.paging, entry);
  if (paging === null) return null;
  if (!schema.paging.coherent({
    rowCount: rows.length, ambiguityCount: ambiguities.length, counts, paging,
  })) return null;
  // Query expansions exist only where a retrieval mode does, which is the same one tool.
  const expansions = schema.requiresRetrievalMode && Array.isArray(entry.query_expansions)
    ? [...new Set((entry.query_expansions as unknown[]).filter(boundedExpansion))]
    : [];
  return { rows, ambiguities, counts, paging, retrievalMode, expansions };
}

/** Everything a claim-bearing unit carries before it is known to be attributable. */
interface ClaimCore {
  entry: unknown;
  publisher: string | undefined;
  status: string;
  jurisdiction: string | undefined;
  timelineSemantics: string | undefined;
  scope: Scope;
}

interface RanClaim extends ClaimCore, RanShape { kind: "ran"; }
interface RefusedClaim extends ClaimCore { kind: "refused"; limitation: PublisherLimitation; }
interface ModeClaim extends ClaimCore { kind: "mode_unavailable"; }
type ClaimEntry = RanClaim | RefusedClaim | ModeClaim;
/**
 * `invalid` carries a publisher when the entry was coherent enough to name one and was
 * invalidated anyway, which is the unreadable-scope case below. It authorizes nothing: it exists
 * so the surface can NAME the publisher it is showing the reader nothing from. Dropping the name
 * would leave "something was withheld" with no way to ask whose.
 */
type ClassifiedEntry =
  | ClaimEntry
  | { kind: "no_corpus" }
  | { kind: "invalid"; publisher?: string };

const isClaim = (item: ClassifiedEntry): item is ClaimEntry =>
  item.kind === "ran" || item.kind === "refused" || item.kind === "mode_unavailable";

/** Classify one envelope of one governed operation over the table, closed in every direction. */
function classifyEntry(tool: string, value: unknown): ClassifiedEntry {
  if (!GOVERNED_TOOLS.has(tool)) return { kind: "invalid" };
  const schema = TOOL_SCHEMAS[tool as GovernedTool];
  const entry = record(value);
  if (!entry) return { kind: "invalid" };
  // The no-corpus refusal is documented as a top-level status with no envelope, for every
  // tool. It is terminal, not malformed: telling the reader to retry would be a lie.
  //
  // NO ENVELOPE IS PART OF THAT SHAPE, not a detail beside it. `McpCore.CallToolCore` returns
  // the object before any per-publisher work runs:
  //     if (!_corpusMounted) return new JsonObject { ["status"] = McpStatus.NoCorpusMounted,
  //         ["detail"] = ..., ["hosted_endpoint"] = ..., ["tool_called"] = name, ... };
  // Every field beside the status is a documented diagnostic, and there is never an envelope
  // among them. An object carrying BOTH the terminal status and an envelope asserts a mounted
  // publisher and index boundary in the same breath as claiming nothing is mounted. That is a
  // forgery or a corruption, so it is invalid, not terminal, and it may not authorize the
  // no-corpus sentence. Extra diagnostic fields stay allowed; an envelope does not.
  if (entry.status === NO_CORPUS_STATUS) {
    // O17. THE TERMINAL OBJECT NAMES THE OPERATION IT ANSWERED, and this classifier never read
    // it. `McpCore.CallToolCore` line 901 stamps `["tool_called"] = name`, the tool actually
    // requested, so a search terminal object was authorizing the widest absence sentence this
    // product makes on an in_force_on page: the reader was told the corpus holds nothing for
    // what they asked about, on the strength of an answer to a different question. An absence
    // asserted about the wrong subject is the worst failure this surface has.
    //
    // Four distinct ways to fail, because they are four different lies. Bounded and
    // membership-tested exactly as every other identity in this table is: the value is
    // response-chosen text, so it validates through `boundedIdentifier` and is then tested
    // against the closed SET of governed tools. Nothing indexes a plain object with it.
    const called = boundedIdentifier(entry.tool_called);
    // Missing, or not a bounded string at all.
    if (called === undefined) return { kind: "invalid" };
    // A bounded identifier naming no governed operation.
    if (!GOVERNED_TOOLS.has(called)) return { kind: "invalid" };
    // A governed operation, but not this one.
    if (called !== tool) return { kind: "invalid" };
    return entry.envelope === undefined ? { kind: "no_corpus" } : { kind: "invalid" };
  }
  const envelope = record(entry.envelope);
  if (!envelope) return { kind: "invalid" };
  const status = envelope.status;
  if (typeof status !== "string") return { kind: "invalid" };
  // Built only inside a branch whose status is already closed, so nothing below indexes the
  // scope table with a string the response chose. The validated publisher travels with every
  // class: the projector groups claims by it, and a grouping key that disagreed with the one the
  // refusal already carries would split one publisher in two. One validator, three classes.
  const core = (): ClaimCore => ({
    entry: value,
    publisher: publisherIdentity(envelope.publisher),
    status,
    jurisdiction: boundedIdentifier(envelope.jurisdiction),
    timelineSemantics: boundedText(envelope.timeline_semantics, MAX_TIMELINE_SEMANTICS),
    scope: parseScope(schema, status, entry),
  });
  /**
   * A CLAIM SURVIVES ONLY WITH A READABLE SCOPE, and saying so here is what makes `unreadable`
   * a check rather than a label.
   *
   * `parseScope` already detects an absent, malformed, out-of-range or self-contradicting
   * population. What it returns is a field on the unit, and a field is not a guarantee: the unit
   * was still emitted, still counted as usable, still authorized rows, a limitation, a mode and
   * an absence claim, and still reported `scopeAuthority` as `every_unit_usable`. The parser
   * noticed the problem, wrote it down, and behaved as though it had not, which is exactly the
   * defect class this whole table exists to remove.
   *
   * So the detection and the consequence are one statement. The publisher travels with the
   * refusal because it was validated before the scope was read and is the one fact still worth
   * disclosing.
   */
  const claimed = (build: (claim: ClaimCore) => ClaimEntry): ClassifiedEntry => {
    const claim = core();
    return claim.scope.kind === "unreadable"
      ? { kind: "invalid", publisher: claim.publisher }
      : build(claim);
  };
  if (status === LIMITATION_STATUS) {
    const limitation = validateLimitation({
      status: LIMITATION_STATUS,
      tool,
      publisher: envelope.publisher,
      jurisdiction: envelope.jurisdiction,
      unsupported_filters: entry.unsupported_filters,
    });
    // A refusal without its required typed limitation is invalid, never evidence-free.
    return limitation === null
      ? { kind: "invalid" }
      : claimed((claim) => ({ ...claim, kind: "refused", limitation }));
  }
  if (schema.requiresRetrievalMode && status === RETRIEVAL_MODE_UNAVAILABLE) {
    return claimed((claim) => ({ ...claim, kind: "mode_unavailable" }));
  }
  if (!schema.successStatuses.has(status)) return { kind: "invalid" };
  const shape = ranShape(schema, entry, status);
  return shape === null
    ? { kind: "invalid" }
    : claimed((claim) => ({ ...claim, kind: "ran", ...shape }));
}

/** Classify one envelope of one governed operation, closed in every direction. */
export function classifyEnvelope(tool: string, value: unknown): EnvelopeClass {
  const classified = classifyEntry(tool, value);
  switch (classified.kind) {
    case "ran":
      return { kind: "ran", entry: classified.entry, publisher: classified.publisher };
    case "refused":
      return { kind: "refused", limitation: classified.limitation };
    case "mode_unavailable":
      return { kind: "mode_unavailable", publisher: classified.publisher };
    case "no_corpus":
      return { kind: "no_corpus" };
    case "invalid":
      return { kind: "invalid" };
  }
}

const toUnit = (claim: ClaimEntry, publisher: string, index: number): PublisherUnit => {
  const core = {
    index,
    publisher,
    status: claim.status,
    jurisdiction: claim.jurisdiction,
    timelineSemantics: claim.timelineSemantics,
    scope: claim.scope,
    entry: claim.entry,
  };
  return claim.kind === "ran"
    ? {
      ...core, kind: "ran", rows: claim.rows, ambiguities: claim.ambiguities,
      counts: claim.counts, paging: claim.paging, retrievalMode: claim.retrievalMode,
      expansions: claim.expansions,
    }
    : claim.kind === "refused"
      ? { ...core, kind: "refused", limitation: claim.limitation }
      : { ...core, kind: "mode_unavailable" };
};

/**
 * The one parse of one untrusted governed response.
 *
 * SAME-PUBLISHER CONFLICT, for every governed tool rather than for search alone. One publisher
 * cannot both have run this operation and refused it. A response carrying both contains two
 * claims and nothing that says which is true, so keeping either side is the product choosing one
 * and asserting it.
 *
 * The test is the unit COUNT, not the set of kinds. The reader registry is keyed by collection
 * with an ordinal comparer and the tools iterate its values, so the producer emits at most one
 * unit per publisher. A second unit is a shape the producer cannot emit whatever it says, which
 * makes two identical refusals as illegitimate as a ran beside a refusal. Counting kinds instead
 * admitted the duplicate case, and two ran units for one publisher doubled the works changed,
 * the new versions, the population and the rows.
 *
 * Decided across the WHOLE response before anything is projected, so arrival order cannot decide
 * the outcome, and a conflict between DISTINCT publishers is not a conflict at all.
 *
 * A claim-bearing unit with no valid publisher identity is not authoritative either. The envelope
 * always carries the reader's collection, so a successful governed unit without a bounded
 * producer identity is malformed: unattributable is withheld, not merely ungroupable.
 */
export function parseGovernedResponse(tool: GovernedTool, raw: unknown): GovernedResponse {
  const list = Array.isArray(raw) ? raw : [raw];
  // Position is captured HERE, at the only place that sees the response as a sequence, so no
  // later consumer has to hold the raw list to recover transport order.
  const classified = list.map((entry, index) => ({ index, item: classifyEntry(tool, entry) }));
  const claims = classified.flatMap(({ index, item }) => isClaim(item) ? [{ index, item }] : []);
  const unitsByPublisher = new Map<string, number>();
  for (const { item: claim } of claims) {
    if (claim.publisher === undefined) continue;
    unitsByPublisher.set(claim.publisher, (unitsByPublisher.get(claim.publisher) ?? 0) + 1);
  }
  const conflictedSet = new Set([...unitsByPublisher]
    .filter(([, units]) => units > 1)
    .map(([publisher]) => publisher));
  const conflicted = [...conflictedSet].sort();
  const unattributed = claims.filter(({ item }) => item.publisher === undefined).length;
  // Named, not merely counted. These are publishers whose one coherent claim was invalidated by
  // a scope the producer publishes and this response did not state readably.
  const unreadable = [...new Set(classified.flatMap(({ item }) =>
    item.kind === "invalid" && item.publisher !== undefined ? [item.publisher] : []))].sort();
  // Retained as typed incoherence rather than dropped, so an all-conflicted response becomes
  // incomplete and another publisher's rows may render only as partial.
  const withheld = claims.filter(({ item }) => item.publisher === undefined
    || conflictedSet.has(item.publisher)).length;
  const noCorpusCount = classified.filter(({ item }) => item.kind === "no_corpus").length;
  // The terminal refusal is GLOBAL and SINGULAR. `McpCore.CallToolCore` returns it before any
  // per-publisher work runs, so it can never legitimately arrive beside a ran, refused,
  // mode-unavailable or invalid sibling, AND it can only ever be returned once. Both facts fall
  // out of one comparison: the response is the terminal answer only when it is exactly one unit
  // and that unit is the terminal object.
  const soleTerminalUnit = classified.length === 1 && noCorpusCount === 1;
  // A no-corpus unit that is not the sole terminal answer is an incoherence, and incoherence is
  // what unusable means here. Counting it is what routes it into the partial and incomplete
  // authority the projector already has, instead of dropping it: a ran envelope beside a
  // no-corpus object used to render whole results and silently discard a global state saying the
  // server holds no law.
  const unusable = classified.filter(({ item }) => item.kind === "invalid").length
    + (soleTerminalUnit ? 0 : noCorpusCount)
    + withheld;
  const units = claims.flatMap(({ index, item: claim }) => {
    const publisher = claim.publisher;
    return publisher === undefined || conflictedSet.has(publisher)
      ? []
      : [toUnit(claim, publisher, index)];
  }).sort((a, b) => a.publisher < b.publisher ? -1 : a.publisher > b.publisher ? 1 : 0);
  return {
    tool,
    units,
    conflicted,
    unattributed,
    unreadable,
    unusable,
    noCorpus: soleTerminalUnit,
    scopeAuthority: conflicted.length === 0 && unattributed === 0 && unusable === 0
      ? "every_unit_usable"
      : "usable_units_only",
  };
}

/**
 * The row-authority boundary. Rows come only from `ran`; refusals contribute limitations and
 * nothing else; invalid envelopes contribute nothing and forbid absence claims.
 */
export interface GovernedPartition {
  /** Envelopes that actually executed with a coherent shape. Rows come only from these. */
  ran: unknown[];
  /**
   * The same units, parsed, in the order the response carried them. Rows, counts, scope,
   * ambiguity, retrieval mode and expansions all come from HERE. `ran` above is the raw entry
   * list the remaining raw-entry readers still consume, and it goes when they do.
   */
  ranUnits: RanUnit[];
  /**
   * The response's own numbers say there is more than this page showed (O12).
   *
   * A row count on its own cannot be situated: zero rows is "there is nothing" or "this is an
   * empty slice of a page", and those are different answers to the reader's question. This is
   * the fact that tells them apart, and it is read from the producer's own totals and truncation
   * receipts rather than inferred. It never suppresses rows; it only forbids an absence claim.
   */
  moreBeyondPage: boolean;
  /** Typed limitations derived from the refusing envelopes. */
  limitations: PublisherLimitation[];
  /** Bounded publisher ids whose retrieval mode is unauthorized (search only). */
  modeUnavailablePublishers: string[];
  /** How many envelopes lacked the retrieval mode, id-bearing or not. */
  modeUnavailableCount: number;
  /**
   * The response was EXACTLY the producer's terminal object and nothing else: one bare unit,
   * `no_corpus_mounted`, no envelope. Not "at least one unit was", and not "every unit was":
   * `McpCore.CallToolCore` returns the object globally, before publisher iteration, so it can
   * only ever return ONE. A response carrying two is a shape the producer cannot emit, and a
   * no-corpus unit beside a sibling that answered is an incoherent response rather than a
   * corpus statement.
   */
  noCorpus: boolean;
  /** Ambiguity units across ran in_force_on envelopes; they are content, not emptiness. */
  ambiguityUnits: number;
  /** Envelopes that authorize neither rows nor absence claims. */
  invalidCount: number;
  /**
   * Publishers who sent more than one claim-bearing unit for this operation. Sorted. Every claim
   * they made is withheld; the incoherence is the only thing left, and the surface must disclose
   * it.
   *
   * The test is the count, not whether the units disagree. The reader registry is keyed by
   * collection, so the producer emits at most one unit per publisher: a second unit is a shape it
   * cannot emit, and byte identity does not make it legitimate. That covers the obvious
   * contradiction, a publisher that both ran and refused, and the quieter one, two ran units
   * whose counts were being added together.
   */
  conflictedPublishers: string[];
  /** At least one envelope refused. */
  anyRefused: boolean;
  /** Nothing ran and at least one envelope refused or lacked the retrieval mode. */
  allRefused: boolean;
}

/**
 * The partition, as a view of ONE parse and nothing else. It takes the parsed response rather
 * than bytes, so a caller that already parsed cannot cause a second parse by reaching for it.
 *
 * RESPONSE ORDER IS RECOVERED, not assumed. `parseGovernedResponse` sorts its units by publisher,
 * and every list here has always been in the order the response carried. It is load-bearing in
 * two places: `ran` is asserted entry for entry by the population contract tests, and the
 * limitation cap decides WHICH eight survive, so sorted order would silently change both. The
 * order comes from the position each unit recorded at parse time; the previous form keyed a Map
 * by the raw entry OBJECT, which merged two entries that were the same reference and could not
 * work at all for a caller that no longer holds the raw list.
 */
export function partitionOf(parsed: GovernedResponse): GovernedPartition {
  const schema = TOOL_SCHEMAS[parsed.tool];
  // Transport order, recovered from the position each unit recorded when it was parsed. The
  // previous form keyed a Map by the raw entry OBJECT, which merged two entries that happened to
  // be the same reference and could not survive a caller that no longer holds the raw list.
  const ordered = [...parsed.units].sort((a, b) => a.index - b.index);
  const ranUnits = ordered.flatMap((unit) => unit.kind === "ran" ? [unit] : []);
  const ran = ranUnits.map((unit) => unit.entry);
  const refusals = ordered.flatMap((unit) => unit.kind === "refused" ? [unit.limitation] : []);
  const modeUnavailable = ordered.filter((unit) => unit.kind === "mode_unavailable");
  return {
    ran,
    ranUnits,
    // O12. Three independent ways the response can say there is more than it showed, and any of
    // them is enough. The declared total is the strongest, because it is required on every ran
    // unit; the truncation receipts are corroboration the producer stamps and this client used
    // to throw away. Read through the table: `absenceTotal` names the count, so no probe here
    // guesses at a field name.
    moreBeyondPage: ranUnits.some((unit) => {
      const total = schema.absenceTotal === undefined
        ? 0
        : unit.counts[schema.absenceTotal] ?? 0;
      return total > unit.rows.length + unit.ambiguities.length
        || unit.paging.truncated === true
        || unit.paging.rowSet?.truncated === true
        || unit.paging.globalRowSet?.truncated === true;
    }),
    limitations: dedupeLimitations(refusals).slice(0, LIMITATION_CAP),
    modeUnavailablePublishers: [...new Set(modeUnavailable.map((unit) => unit.publisher))],
    modeUnavailableCount: modeUnavailable.length,
    noCorpus: parsed.noCorpus,
    // Read from the PARSED unit, through `ambiguityField`. It used to read `ambiguous_works` off
    // any ran entry of any tool, so a stray field on a changes_in_period entry, which the table
    // gives no ambiguity field and the producer never sends one for, drove that whole surface to
    // `ambiguous_only`. The rows inside it were never validated either, because nothing on that
    // tool's path looks at them.
    ambiguityUnits: ranUnits.reduce((sum, unit) => sum + unit.ambiguities.length, 0),
    invalidCount: parsed.unusable,
    conflictedPublishers: parsed.conflicted,
    anyRefused: refusals.length > 0,
    // A capability REFUSAL is what the filter-limitation copy explains. A publisher that
    // merely lacked the hybrid retrieval mode refused no filter, so it must not select copy
    // blaming a filter that was never refused.
    // No separate no-corpus term: a whole-response no-corpus carries no refusal, and a mixed one
    // is already inside the unusable count above. Restating it here would be a condition no
    // mutation could kill, which is its own kind of lie about what is tested.
    allRefused: ran.length === 0 && refusals.length > 0 && parsed.unusable === 0,
  };
}

export function partitionGovernedResponse(
  tool: string,
  envelopes: unknown[],
): GovernedPartition {
  const list = Array.isArray(envelopes) ? envelopes : [];
  if (!GOVERNED_TOOLS.has(tool)) {
    // No schema, so nothing in the response can be classified. Every entry is unusable, which is
    // exactly what the classifier has always said for an ungoverned tool.
    return {
      ran: [], ranUnits: [], moreBeyondPage: false, limitations: [],
      modeUnavailablePublishers: [], modeUnavailableCount: 0,
      noCorpus: false, ambiguityUnits: 0, invalidCount: list.length, conflictedPublishers: [],
      anyRefused: false, allRefused: false,
    };
  }
  return partitionOf(parseGovernedResponse(tool as GovernedTool, list));
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
/** A state nobody handled must not compile, let alone reach a reader as a corpus-wide claim. */
function assertNever(value: never): never {
  throw new Error(`unhandled absence state: ${String(value)}`);
}

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
    case "no_match":
      return { kind: "no_match", sentence: "Nothing in the corpus matches that." };
  }
  // No default branch, deliberately. The sentence below is the strongest legal absence
  // claim this product makes: nothing in the whole corpus matches. A default handed that
  // claim to every state nobody had thought about yet, and noFallthroughCasesInSwitch does
  // not catch a default, so adding an absence state would silently have made the widest
  // possible assertion about the corpus. This turns that into a compile error instead.
  return assertNever(state);
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
  parsed: GovernedResponse,
  build: (ran: RanUnit[]) => { works: W[]; articles: A[]; ranHitCount: number },
): SearchResultsState<W, A> {
  const partition = partitionOf(parsed);
  // From the PARSED unit, not from a second read of the raw entry. `retrieval_mode` was
  // re-probed here with a bare string comparison and `query_expansions` re-filtered, so the two
  // readings could disagree with the table that had already validated both. They cannot now.
  const usedHybrid = partition.ranUnits.some((unit) => unit.retrievalMode === "hybrid");
  const usedKeyword = partition.ranUnits.some((unit) => unit.retrievalMode === "keyword");
  const expansions = [...new Set(partition.ranUnits.flatMap((unit) => unit.expansions))];
  const modeUnavailable = partition.modeUnavailableCount > 0
    ? `Words + meaning is unavailable${partition.modeUnavailablePublishers.length > 0
      ? ` for ${partition.modeUnavailablePublishers.join(", ")}` : ""}: its signed retrieval `
      + "benchmark has not authorized it. Choose Exact words."
    : undefined;
  const built = build(partition.ranUnits);
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
  /**
   * The ambiguity units the caller must render beside normal rows (PR293 exact review, O1).
   * Round 7 chose visible rows first, so a mixed page dropped these objects while keeping
   * their contribution to the total, producing pagination like 1 to 1 of 2 with an
   * unexplained second unit.
   */
  ambiguous: Record<string, unknown>[];
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
  // Through the table's `ambiguityField`, so these are objects the tool actually declares an
  // ambiguity field for AND whose rows passed that tool's row schema. The raw read this replaces
  // rendered whatever an entry happened to put under `ambiguous_works`, on any tool.
  const ambiguous = partition.ranUnits.flatMap((unit) => unit.ambiguities);
  if (visibleRowCount > 0) return { partition, empty: null, partial, ambiguous };
  // Ambiguity units are held content, so this is not absence. But it is not a result either:
  // round 6 returned null here, which let the caller render a positive total beside an empty
  // list (PR293 review, O2). It is its own state, and the surface must ask for clarification
  // rather than report a count it cannot itemise.
  if (partition.ambiguityUnits > 0) {
    return { partition, empty: "ambiguous_only", partial, ambiguous };
  }
  if (partition.noCorpus) return { partition, empty: "no_corpus", partial, ambiguous };
  if (partial) return { partition, empty: "incomplete_response", partial, ambiguous };
  if (partition.allRefused) return { partition, empty: "all_refused", partial, ambiguous };
  if (partition.ran.length === 0) {
    return { partition, empty: "incomplete_response", partial, ambiguous };
  }
  // O12. Both remaining states are absence claims: `none_matched` says nothing in scope matched,
  // and `mixed_no_match` says the publishers that could apply the filters returned nothing. A
  // page that showed nothing because its SLICE was empty has established neither. The response
  // reported a positive total, or stamped a truncation receipt, and this surface used to answer
  // "no publisher state covers that date" beside it.
  //
  // `incomplete_response` rather than a new state, deliberately. It is the one state already on
  // every governed surface that claims nothing about the corpus, and inventing a fourth would
  // fall through the empty-state chains in the pages to the confident-absence branch, which is
  // the very sentence this is here to stop. The copy asking the reader to try again is a poorer
  // fit than a paging sentence would be, and it is honest: it claims nothing.
  if (partition.moreBeyondPage) {
    return { partition, empty: "incomplete_response", partial, ambiguous };
  }
  return {
    partition,
    empty: partition.anyRefused ? "mixed_no_match" : "none_matched",
    partial,
    ambiguous,
  };
}
