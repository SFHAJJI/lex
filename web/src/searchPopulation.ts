/**
 * Validation for the search population object.
 *
 * WIRED. The producer that emits this object has merged and the search surface consumes this
 * module. It was written ahead of that producer and deliberately inert until the candidate
 * landed, because the accepted contract requires client and producer to ship together: a client
 * demanding a population before one existed would have authorized no rows at all.
 *
 * The rule is not written here. It is read from tests/Lex.Tests/search-population-contract.json
 * by the test that binds this module, exactly as the governed status contract works, because the
 * defect this whole lane exists to stop is a validation predicate written from a partial reading
 * of the producer.
 *
 * Fails closed throughout. A population that cannot be validated authorizes neither rows nor an
 * absence claim, since an unverified denominator is worse than a missing one: it invites the
 * reader to check an answer against a number nothing stands behind.
 *
 * ONE PARSE, NOT ONE PASS. `normalizeSearchResponse` no longer reads a response at all. It takes
 * the `GovernedResponse` that `limitations.ts` produced from the raw bytes and reshapes it, so
 * rows, populations and absence authority are three views of one typed parse rather than three
 * readings of the same untrusted object. A publisher can never have its hits rendered beside a
 * denominator that was refused, and no projection can disagree with another about which
 * publishers were admitted, because there is nothing left to disagree with.
 *
 * The type import below is deliberately type-only and creates no runtime cycle: limitations.ts
 * imports this module's validator, and this module imports only its TYPES back. The runtime
 * direction stays exactly as acyclic as it was.
 */

import { MAX_PUBLISHER_IDENTITY } from "./publisherIdentity.ts";
import type { GovernedResponse, PublisherUnit } from "./limitations.ts";

export type PopulationBasis =
  | "selected_metadata_scope"
  | "mounted_scope_before_unsupported_filters";

export interface SearchPopulation {
  basis: PopulationBasis;
  works_in_scope: number;
  /** Whether every selected legal-metadata and time filter narrowed this denominator. */
  scope_filters_applied: boolean;
  /** Whether this publisher actually executed the text query. */
  query_ran: boolean;
  known_exclusions: string[];
}

export type PopulationVerdict =
  | { valid: true; population: SearchPopulation }
  | { valid: false; reason: string };

const BASES: readonly PopulationBasis[] = [
  "selected_metadata_scope",
  "mounted_scope_before_unsupported_filters",
];

/** Coherence triples, keyed by envelope status. Mirrored by the contract file the test reads. */
const COHERENCE: Record<string, Omit<SearchPopulation, "works_in_scope" | "known_exclusions">> = {
  ok: { basis: "selected_metadata_scope", scope_filters_applied: true, query_ran: true },
  retrieval_mode_unavailable:
    { basis: "selected_metadata_scope", scope_filters_applied: true, query_ran: false },
  filter_not_supported_by_index:
    { basis: "mounted_scope_before_unsupported_filters", scope_filters_applied: false, query_ran: false },
};

export const POPULATION_BOUNDS = {
  maxExclusions: 20,
  maxExclusionLength: 300,
  /** The shared identity bound, referenced rather than restated so the two cannot drift. */
  maxPublisherLength: MAX_PUBLISHER_IDENTITY,
};

/**
 * The producer's own numeric range for every count this workspace consumes: C# `int`, end to end.
 *
 * Evidence, read from the mint sites rather than assumed. This population's own denominator is
 * `McpCore.SearchPopulation`, which sets `["works_in_scope"]` from either
 * `reader.SearchPopulationTotal(filter)` or `reader.PopulationTotal(null)`; both are declared
 * `public int` in `Lex.Index/IndexReader.cs`. The other two governed populations are the same
 * shape: `in_force_on` publishes `works_covered` from `r.Coverage(1).Groups`, and `CoverageInfo`
 * declares `int Groups`. The counts beside the rows are `int` as well: `IndexReader.ChangeTotals`
 * is `(int Works, int Versions)` and `Rows.InForcePage.TotalGroups` is `int`.
 *
 * IT LIVES HERE, not in limitations.ts, because two doors have to agree on it and only one
 * direction between these modules is acyclic. limitations.ts imports this module; this module
 * imports publisherIdentity.ts and nothing else at runtime. Putting the constant in the importer
 * would leave this validator with a copy, and a copy is the defect the whole lane exists to stop:
 * a value above this reached the search footer as a legal denominator while the same number was
 * refused one module away.
 *
 * A count above this is not merely large. It is a number nothing in that chain can produce, so
 * accepting it is accepting a forgery.
 */
export const MAX_PRODUCER_COUNT = 2147483647;

/**
 * A bounded list, or a single bounded string, and nothing else.
 *
 * EXPORTED so limitations.ts can read the other two tools' `known_exclusions` through this one
 * helper instead of carrying its own copy. The producer serializes the field as ONE string
 * (`McpCore.KnownExclusions` returns a `string`) on all three governed tools, which is why the
 * single-string case is here rather than at a call site. Returns null for anything that is not
 * a bounded list of bounded strings; the trimmed, de-duplicated set otherwise.
 */
export function exclusionsOf(raw: unknown): string[] | null {
  const list = typeof raw === "string" ? [raw] : raw;
  if (!Array.isArray(list)) return null;
  if (list.length > POPULATION_BOUNDS.maxExclusions) return null;
  const out: string[] = [];
  for (const item of list) {
    if (typeof item !== "string") return null;
    if (item.length > POPULATION_BOUNDS.maxExclusionLength) return null;
    const trimmed = item.trim();
    if (trimmed.length > 0 && !out.includes(trimmed)) out.push(trimmed);
  }
  return out;
}

/**
 * The publisher identity a population may be attributed to comes from `publisherIdentity`, the
 * one non-normalizing validator the strip and the limitation list also use. It is no longer
 * called here: the identity now arrives already validated on the parsed unit, which is the point
 * of the cutover. This module used to carry its own copy, whose regex had the `i` flag:
 * "LU-Legilux" was an identity here and not to the producer's ordinal registry lookup, so one raw
 * publisher could pass the duplicate check that the other spelling should have failed. See
 * publisherIdentity.ts for the producer evidence.
 */

export function validateSearchPopulation(status: unknown, raw: unknown): PopulationVerdict {
  if (typeof status !== "string" || !(status in COHERENCE)) {
    return { valid: false, reason: `no population rule for status ${String(status)}` };
  }
  if (raw === null || typeof raw !== "object" || Array.isArray(raw)) {
    return { valid: false, reason: "population absent or not an object" };
  }
  const p = raw as Record<string, unknown>;

  const works = p.works_in_scope;
  // Two different tests, and both are needed. Number.isSafeInteger rejects NaN, Infinity,
  // fractions and anything past 2^53, which is a test on whether the VALUE is an exact integer.
  // MAX_PRODUCER_COUNT is a test on whether it is inside the range the PRODUCER can mint, and
  // 2^31 to 2^53 is exactly the gap between them: 2147483648 is a perfectly exact integer and a
  // number `SearchPopulationTotal` cannot return. It used to pass here and be refused by the
  // parser one module away, so the same response disclosed a denominator on the footer that
  // authorized no rows above it.
  if (!Number.isSafeInteger(works) || (works as number) < 0
    || (works as number) > MAX_PRODUCER_COUNT) {
    return { valid: false, reason: "works_in_scope is not a count the producer can mint" };
  }
  if (typeof p.basis !== "string" || !BASES.includes(p.basis as PopulationBasis)) {
    return { valid: false, reason: `unknown basis ${String(p.basis)}` };
  }
  if (typeof p.scope_filters_applied !== "boolean") {
    return { valid: false, reason: "scope_filters_applied is not a boolean" };
  }
  if (typeof p.query_ran !== "boolean") {
    return { valid: false, reason: "query_ran is not a boolean" };
  }
  const exclusions = exclusionsOf(p.known_exclusions ?? []);
  if (exclusions === null) {
    return { valid: false, reason: "known_exclusions is not a bounded list of bounded strings" };
  }

  const expected = COHERENCE[status];
  if (p.basis !== expected.basis
    || p.scope_filters_applied !== expected.scope_filters_applied
    || p.query_ran !== expected.query_ran) {
    return { valid: false, reason: `population contradicts status ${status}` };
  }

  return {
    valid: true,
    population: {
      basis: p.basis as PopulationBasis,
      works_in_scope: works as number,
      scope_filters_applied: p.scope_filters_applied,
      query_ran: p.query_ran,
      known_exclusions: exclusions,
    },
  };
}

/**
 * A publisher may contribute its population to a claim about what the query covered only when it
 * actually ran the query. A refused or unavailable publisher still discloses its mounted scope,
 * but that number never joins a "searched N works" sentence.
 */
export function contributesToQueryPopulation(verdict: PopulationVerdict): boolean {
  return verdict.valid && verdict.population.query_ran;
}

/** A publisher's validated population, tagged with whether it actually answered the query. */
export interface PublisherPopulation {
  /** Always present: an unattributable denominator is never returned. */
  publisher: string;
  /** From the shipped classifier, never re-derived here. */
  kind: "ran" | "mode_unavailable" | "refused";
  population: SearchPopulation;
}

/**
 * The result of one normalization pass over one search response.
 *
 * AN ADAPTER, NOT A PARSER. It takes the `GovernedResponse` the one door already produced and
 * reshapes it for the footer. It never sees raw `unknown`, never takes a classifier callback, and
 * makes no decision about publisher identity, duplicates, row authority or population validity:
 * every one of those is already decided, once, by `parseGovernedResponse`.
 *
 * That is the whole repair. This module used to own a second state machine over the same bytes:
 * it re-read `envelope.publisher`, re-validated `record.population`, kept its own `voided` and
 * `statusConflicted` sets and withheld its own rows. Two entry points that both took `unknown`
 * disagreed, and the disagreement reached the reader: two logically identical ran units for one
 * publisher collapsed into one surviving population HERE while `parseGovernedResponse` withheld
 * every row that publisher sent, so the footer reported a denominator for a source the page was
 * showing nothing from. Disagreement is now not merely prevented but inexpressible, because
 * there is only one parse to disagree with.
 *
 * CONTRACT FOR THE CALLER
 *
 * 1. `populations` is the footer's only source. At most one entry per publisher, holding none of
 *    the withheld ones, so footer and rows describe the same set by construction.
 * 2. Rows come from the same `GovernedResponse` this was built from, never from the raw response.
 * 3. When `complete` is false the caller must disclose the withholding and must NOT make an
 *    absence claim. `projectSearchResponse` reads the same parse and types that state as
 *    `partial_results` or `incomplete_response`, so the disclosure is structural either way.
 * 4. `complete: true` says only that nothing was withheld from ATTRIBUTION. It is not a health
 *    claim about the response: an entry the table rejects is counted in the parse's own
 *    `unusable` and surfaces in the projector's absence state.
 */
export type NormalizedSearchResponse =
  | {
      /** Nothing was withheld: every claim is attributable and every denominator validated. */
      complete: true;
      /** At most one validated population per publisher, ordered by publisher. */
      populations: PublisherPopulation[];
    }
  | {
      complete: false;
      /** At most one validated population per publisher, holding none of the withheld ones. */
      populations: PublisherPopulation[];
      /**
       * Named publishers whose claims were withheld, sorted and distinct. Two causes, both
       * decided by the one parse: a SAME-PUBLISHER CONFLICT, where more than one claim-bearing
       * unit arrived and nothing says which is true, and an UNREADABLE REQUIRED SCOPE, where the
       * producer publishes a population on every search path and this one will not validate.
       * Empty when only unattributable entries were withheld.
       */
      withheldPublishers: string[];
      /**
       * How many claim-bearing entries carried no bounded publisher identity. They are withheld,
       * and named to nobody, which is why they are only a count.
       */
      unattributedEntries: number;
    };

/**
 * The population a search unit discloses, or nothing when it is not one.
 *
 * A NARROWING of an already-validated value, not a second validator. `parseScope` ran the whole
 * coherence contract for this status before the unit existed; every test here can only refuse
 * something that pass accepted, and none of them can accept something it refused. They are here
 * because `ScopeDisclosure` is the shape shared by all three governed tools, and only search's
 * carries a closed basis vocabulary and the two booleans this footer reads.
 */
function searchPopulationOf(unit: PublisherUnit): PublisherPopulation | undefined {
  if (unit.scope.kind !== "disclosed") return undefined;
  const scope = unit.scope.scope;
  if (scope.measure !== "works_in_scope") return undefined;
  if (!BASES.includes(scope.basis as PopulationBasis)) return undefined;
  if (scope.scopeFiltersApplied === undefined || scope.queryRan === undefined) return undefined;
  return {
    publisher: unit.publisher,
    kind: unit.kind,
    population: {
      basis: scope.basis as PopulationBasis,
      works_in_scope: scope.works,
      scope_filters_applied: scope.scopeFiltersApplied,
      query_ran: scope.queryRan,
      known_exclusions: scope.knownExclusions,
    },
  };
}

/**
 * Reshape one already-parsed search response for the footer. See NormalizedSearchResponse.
 */
export function normalizeSearchResponse(
  parsed: GovernedResponse,
): NormalizedSearchResponse {
  const populations = parsed.units
    .map(searchPopulationOf)
    .filter((row): row is PublisherPopulation => row !== undefined);
  // Both causes are named, and they are named TOGETHER because the reader's question is the same
  // for both: which publisher is this page showing me nothing from. Sorted and de-duplicated
  // because the two lists are independent and a publisher can in principle appear in neither
  // order nor only once.
  const withheldPublishers = [...new Set([...parsed.conflicted, ...parsed.unreadable])].sort();
  if (withheldPublishers.length === 0 && parsed.unattributed === 0) {
    return { complete: true, populations };
  }
  return {
    complete: false,
    populations,
    withheldPublishers,
    unattributedEntries: parsed.unattributed,
  };
}

/**
 * Per-publisher populations from one already-parsed search response.
 *
 * The narrow read, for a caller that renders only the footer. Anything that also renders rows
 * must read `normalizeSearchResponse`: populations alone cannot tell it which publishers were
 * withheld, and rendering their hits beside this footer is the defect O2 named.
 */
export function searchPopulations(parsed: GovernedResponse): PublisherPopulation[] {
  return normalizeSearchResponse(parsed).populations;
}

/**
 * What a "searched N works" sentence is allowed to claim.
 *
 * `not_summable` is a distinct answer from `none_ran` because the sentences beside them are
 * different truths: nobody ran the query, versus publishers ran it and their disclosed scopes
 * cannot be added. Collapsing the two would print "no publisher ran this query" about a response
 * where publishers did.
 *
 * `publishers` names the publishers the answer is about: every publisher that ran, for a total or
 * for a sum that cannot be added, and the repeated ones for a set that names a publisher twice.
 */
export type QueriedDenominator =
  | { kind: "total"; works: number; publishers: string[] }
  | { kind: "none_ran" }
  | { kind: "not_summable"; publishers: string[] };

/**
 * The queried denominator, or a refusal to state one.
 *
 * `rows` is a SET: one row per publisher, which is what normalizeSearchResponse returns. This
 * function ENFORCES that rather than assuming it, because it is exported and a row can reach it
 * from somewhere else, and one publisher counted twice mints a perfectly safe integer that is
 * false. A hardening function that reintroduces the defect it hardens against is worse than none,
 * since its name tells the next caller it is safe. Identity is checked before any arithmetic:
 * the defect is in the set, not in the numbers.
 *
 * A repeated publisher is REFUSED, never collapsed, even when the two rows look identical.
 *
 * The comment here used to say normalizeSearchResponse MAY collapse an identical duplicate,
 * because it has the response in front of it. It no longer may, and it never should have: a
 * second claim-bearing unit for one publisher is a shape the producer cannot emit, so the parse
 * treats it as a conflict and withholds every claim that publisher made. This function reaches
 * the same verdict for a narrower reason. Two identical-looking rows are equally likely to be one
 * disclosure counted twice by a caller that concatenated two lists, and picking a reading is the
 * guess the order-dependence objection forbade. Refusing costs a legitimate caller nothing,
 * because the normalized set already satisfies the invariant.
 *
 * Every addend is re-checked for the same reason. The ceiling is checked BEFORE each addition:
 * afterwards there is nothing left to check, since a sum past Number.MAX_SAFE_INTEGER has already
 * silently stopped being the sum of its parts.
 */
export function queriedDenominator(rows: PublisherPopulation[]): QueriedDenominator {
  const seen = new Set<string>();
  const repeated = new Set<string>();
  for (const row of rows) {
    if (seen.has(row.publisher)) repeated.add(row.publisher);
    seen.add(row.publisher);
  }
  // Across every row, not only the queried ones: a set that names a publisher twice has lost the
  // invariant, whatever the repeat says about having run the query.
  if (repeated.size > 0) return { kind: "not_summable", publishers: [...repeated].sort() };
  const queried = rows.filter((r) => r.population.query_ran);
  if (queried.length === 0) return { kind: "none_ran" };
  const publishers = queried.map((r) => r.publisher);
  let works = 0;
  for (const row of queried) {
    const addend = row.population.works_in_scope;
    if (!Number.isSafeInteger(addend) || addend < 0) return { kind: "not_summable", publishers };
    if (addend > Number.MAX_SAFE_INTEGER - works) return { kind: "not_summable", publishers };
    works += addend;
  }
  return { kind: "total", works, publishers };
}

/**
 * The denominator a "searched N works" sentence may claim: only publishers that ran the query.
 * Returns undefined when none did, because zero would assert an empty corpus was searched.
 *
 * Undefined also covers the unsummable case, on purpose. Every caller already handles undefined
 * by rendering no total at all, so a third variant in this return type could only be ignored, and
 * an ignored variant renders a number nothing stands behind. A caller that prints a sentence
 * beside the missing total must use `queriedDenominator` to pick the true one, since "no
 * publisher ran this query" is false when the total was merely unaddable. That pointer is safe to
 * follow: queriedDenominator enforces the one-row-per-publisher invariant itself.
 */
export function queriedPopulationTotal(rows: PublisherPopulation[]): number | undefined {
  const denominator = queriedDenominator(rows);
  return denominator.kind === "total" ? denominator.works : undefined;
}

/** Publishers that disclosed a scope but did not run the query. Shown, never added in. */
export function unqueriedPopulations(rows: PublisherPopulation[]): PublisherPopulation[] {
  return rows.filter((r) => !r.population.query_ran);
}

/** Bounded, de-duplicated, never interpreted. */
export function populationExclusions(rows: PublisherPopulation[]): string[] {
  return [...new Set(rows.flatMap((r) => r.population.known_exclusions))]
    .slice(0, POPULATION_BOUNDS.maxExclusions);
}
