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

/**
 * The producer's own numeric range, verified against the authoritative chain rather than assumed.
 * Every count this module consumes is minted as a C# `int` end to end: `IndexReader.ChangeTotals`
 * is declared `(int Works, int Versions)` and reads its two columns with `GetInt32`,
 * `Rows.InForcePage.TotalGroups` is `int` and `IndexReader.InForceOn` computes it into a local
 * `int total`, and `McpCore` publishes exactly those values as `works_changed`, `new_versions` and
 * `total_works_in_force`. Nothing in that chain is a `long`, and the assistant-side combiner reads
 * them back with `GetValue<int>()`. A count above this is therefore not merely imprecise, it is a
 * number the producer cannot mint.
 */
const MAX_PRODUCER_COUNT = 2147483647;

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

/** A non-empty string field, which is what every renderer actually consumes. */
const text = (value: unknown): boolean =>
  typeof value === "string" && value.length > 0;

/**
 * Operation-specific row schemas (PR293 exact review, O2). Round 7 accepted any non-null
 * object, so `hits:[{}]` was `ran`: the empty row then yielded no work id, the surface found
 * zero rows and claimed "Nothing in the corpus matches that", and an empty change row threw
 * inside `work.includes`. A row that a renderer cannot read is malformed, not empty.
 */
const ROW_SCHEMAS: Record<string, (row: Record<string, unknown>) => boolean> = {
  // The workspace derives every work identity from lex_id.
  search: (row) => text(row.lex_id),
  // The ranking view reads work and calls string methods on it.
  changes_in_period: (row) => text(row.work),
  // The in-force view opens rows by work, or by lex_id when the publisher supplies one.
  in_force_on: (row) => text(row.work) || text(row.lex_id),
};

const rowsMatchSchema = (tool: string, rows: Record<string, unknown>[]): boolean =>
  rows.every((row) => ROW_SCHEMAS[tool]!(row));

function ranShapeValid(tool: string, entry: Record<string, unknown>, status: string): boolean {
  if (tool === "search") {
    // O3: the producer always declares its actual retrieval mode on a successful search.
    // Admitting an envelope without one lets a response of unknown provenance render.
    return rowsValid(entry.hits)
      && rowsMatchSchema("search", entry.hits)
      && typeof entry.retrieval_mode === "string"
      && RETRIEVAL_MODES.has(entry.retrieval_mode);
  }
  if (tool === "changes_in_period") {
    if (!rowsValid(entry.changes)) return false;
    if (!rowsMatchSchema("changes_in_period", entry.changes)) return false;
    const worksChanged = requiredCount(entry.works_changed);
    const newVersions = optionalCount(entry.new_versions);
    if (worksChanged === null || newVersions === null) return false;
    if (status === "no_changes_in_period") {
      return worksChanged === 0 && entry.changes.length === 0
        && optionalCount(entry.new_versions) === 0;
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
    if (!rowsMatchSchema("in_force_on", entry.works)) return false;
    const total = requiredCount(entry.total_works_in_force);
    if (total === null) return false;
    if (entry.ambiguous_works !== undefined
      && (!rowsValid(entry.ambiguous_works)
        || !rowsMatchSchema("in_force_on", entry.ambiguous_works))) return false;
    const ambiguities = Array.isArray(entry.ambiguous_works)
      ? entry.ambiguous_works.length : 0;
    // Status and counts must agree. no_result means the publisher found nothing, so a
    // positive total contradicts it; round 7 admitted that and rendered a false claim that
    // no state covers the date beside a response reporting five (O2). ambiguous_version
    // means at least one ambiguity unit exists.
    if (status === "no_result"
      && (total !== 0 || entry.works.length > 0 || ambiguities > 0)) return false;
    if (status === "ambiguous_version" && ambiguities === 0) return false;
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
    return entry.envelope === undefined ? { kind: "no_corpus" } : { kind: "invalid" };
  }
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
    return { kind: "mode_unavailable", publisher: publisherIdentity(envelope.publisher) };
  }
  if (!SUCCESS_STATUSES[tool]!.has(status)) return { kind: "invalid" };
  // The validated publisher travels with the class: the projector groups claims by it, and a
  // grouping key that disagreed with the one the refusal and the mode-unavailable classes
  // already carry would split one publisher in two. One validator, three classes.
  return ranShapeValid(tool, entry, status)
    ? { kind: "ran", entry: value, publisher: publisherIdentity(envelope.publisher) }
    : { kind: "invalid" };
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

export function partitionGovernedResponse(
  tool: string,
  envelopes: unknown[],
): GovernedPartition {
  const list = Array.isArray(envelopes) ? envelopes : [];
  const classes = list.map((entry) => classifyEnvelope(tool, entry));

  // SAME-PUBLISHER TERMINAL-KIND CONFLICT, for every governed tool rather than for search
  // alone. One publisher cannot both have run this operation and refused it. A response
  // carrying both contains two claims and nothing that says which is true, so keeping either
  // side is the product choosing one and asserting it. Keeping the refusal made the screen say
  // "No selected publisher ran this query", and LIMITATION_EXPLANATION say the publisher did
  // not run this query, about a response that also said it ran.
  //
  // The three claim-bearing classes each speak for one publisher; `no_corpus` and `invalid`
  // speak for no publisher and are not grouped. An unattributable claim (no valid publisher
  // identity) cannot be grouped either, and stays out of this: it is already handled as an
  // unattributed entry upstream.
  const claimPublisher = (item: EnvelopeClass): string | undefined =>
    item.kind === "ran" || item.kind === "mode_unavailable" ? item.publisher
      : item.kind === "refused" ? item.limitation.publisher
        : undefined;
  const unitsByPublisher = new Map<string, number>();
  for (const item of classes) {
    const publisher = claimPublisher(item);
    if (publisher === undefined) continue;
    unitsByPublisher.set(publisher, (unitsByPublisher.get(publisher) ?? 0) + 1);
  }
  /**
   * Decided across the WHOLE response before anything is projected, so arrival order cannot decide
   * the outcome, and a conflict between DISTINCT publishers is not a conflict at all.
   *
   * The test is the unit count, not the set of kinds. The reader registry is keyed by collection
   * with an ordinal comparer and the tools iterate its values, so the producer emits at most one
   * unit per publisher. A second unit is a shape the producer cannot emit whatever it says, which
   * makes two identical refusals as illegitimate as a ran beside a refusal. Counting kinds instead
   * admitted the duplicate case, and two ran units for one publisher doubled the works changed,
   * the new versions, the population and the rows.
   */
  const conflicted = new Set([...unitsByPublisher]
    .filter(([, units]) => units > 1)
    .map(([publisher]) => publisher));
  const conflictedPublishers = [...conflicted].sort();
  /**
   * A claim-bearing unit with no valid publisher identity is not authoritative.
   *
   * The envelope always carries the reader's collection, so a successful governed unit without a
   * bounded producer identity is malformed. The search surface has a second normalization layer
   * that withholds these, but the other governed tools reach the projector directly, so a missing,
   * padded, upper-case or overlong publisher could render rows, totals and exclusions with no
   * index identity beside them. Unattributable is withheld, not merely ungroupable.
   */
  const unattributed = (item: EnvelopeClass): boolean =>
    (item.kind === "ran" || item.kind === "mode_unavailable" || item.kind === "refused")
    && claimPublisher(item) === undefined;
  const inConflict = (item: EnvelopeClass): boolean => {
    const publisher = claimPublisher(item);
    return unattributed(item)
      || (publisher !== undefined && conflicted.has(publisher));
  };
  // Retained as typed incoherence rather than dropped, so an all-conflicted response becomes
  // incomplete and another publisher's rows may render only as partial.
  const withheldByConflict = classes.filter(inConflict).length;

  const ran = classes.flatMap((item) =>
    item.kind === "ran" && !inConflict(item) ? [item.entry] : []);
  const limitations = dedupeLimitations(
    classes.flatMap((item) =>
      item.kind === "refused" && !inConflict(item) ? [item.limitation] : []))
    .slice(0, LIMITATION_CAP);
  const modeUnavailablePublishers = [...new Set(classes
    .flatMap((item) => item.kind === "mode_unavailable" && item.publisher
      && !inConflict(item)
      ? [item.publisher] : []))];
  const modeUnavailableCount =
    classes.filter((item) => item.kind === "mode_unavailable" && !inConflict(item)).length;
  const refusedCount =
    classes.filter((item) => item.kind === "refused" && !inConflict(item)).length;
  const noCorpusCount = classes.filter((item) => item.kind === "no_corpus").length;
  // The terminal refusal is GLOBAL and SINGULAR. `McpCore.CallToolCore` returns it before any
  // per-publisher work runs, so it can never legitimately arrive beside a ran, refused,
  // mode-unavailable or invalid sibling, AND it can only ever be returned once. Both facts fall
  // out of one comparison: the response is the terminal answer only when it is exactly one unit
  // and that unit is the terminal object.
  //
  // Not "every unit is no-corpus". A repeated pair is not a conservative case to preserve; it
  // is a shape the producer cannot emit, and accepting it would widen the sentence's authority
  // to cover a response nothing stands behind. Not "any unit is no-corpus" either: that is the
  // original defect, and it printed "this server has no verified legal index mounted" beside a
  // retrieval_mode_unavailable envelope asserting a mounted publisher and index boundary.
  const soleTerminalUnit = classes.length === 1 && noCorpusCount === 1;
  // A no-corpus unit that is not the sole terminal answer is an incoherence, and incoherence is
  // what `invalid` means here. Counting it as invalid is what routes it into the partial and
  // incomplete authority the projector already has, instead of dropping it: a ran envelope
  // beside a no-corpus object used to render whole results and silently discard a global state
  // saying the server holds no law.
  const invalidCount = classes.filter((item) => item.kind === "invalid").length
    + (soleTerminalUnit ? 0 : noCorpusCount)
    + withheldByConflict;
  const ambiguityUnits = ran
    .map(record)
    .reduce((sum, entry) => sum + (Array.isArray(entry?.ambiguous_works)
      ? entry!.ambiguous_works.length : 0), 0);
  return {
    ran,
    limitations,
    modeUnavailablePublishers,
    modeUnavailableCount,
    noCorpus: soleTerminalUnit,
    ambiguityUnits,
    invalidCount,
    conflictedPublishers,
    anyRefused: refusedCount > 0,
    // A capability REFUSAL is what the filter-limitation copy explains. A publisher that
    // merely lacked the hybrid retrieval mode refused no filter, so it must not select copy
    // blaming a filter that was never refused.
    // No separate no-corpus term: a whole-response no-corpus carries refusedCount 0, and a
    // mixed one is already inside invalidCount above. Restating it here would be a condition
    // no mutation could kill, which is its own kind of lie about what is tested.
    allRefused: ran.length === 0 && refusedCount > 0 && invalidCount === 0,
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
  const ambiguous = partition.ran
    .map((entry) => (entry as Record<string, unknown> | null)?.ambiguous_works)
    .flatMap((units) => Array.isArray(units) ? units as Record<string, unknown>[] : []);
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
  return {
    partition,
    empty: partition.anyRefused ? "mixed_no_match" : "none_matched",
    partial,
    ambiguous,
  };
}
