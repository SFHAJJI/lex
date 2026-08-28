/**
 * Validation for the search population object.
 *
 * NOT YET WIRED INTO THE RENDER PATH. The producer candidate that emits this object has not
 * merged, and the jointly accepted contract requires the client and producer to ship together:
 * a client that demanded population today would authorize no rows at all, because search
 * currently emits none. This module is written ahead of the producer so the interface is pinned
 * from both sides, and it is connected when that candidate lands.
 *
 * The rule is not written here. It is read from tests/Lex.Tests/search-population-contract.json
 * by the test that binds this module, exactly as the governed status contract works, because the
 * defect this whole lane exists to stop is a validation predicate written from a partial reading
 * of the producer.
 *
 * Fails closed throughout. A population that cannot be validated authorizes neither rows nor an
 * absence claim, since an unverified denominator is worse than a missing one: it invites the
 * reader to check an answer against a number nothing stands behind.
 */

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

export const POPULATION_BOUNDS = { maxExclusions: 20, maxExclusionLength: 300 };

/** A bounded list, or a single bounded string, and nothing else. */
function exclusionsOf(raw: unknown): string[] | null {
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

export function validateSearchPopulation(status: unknown, raw: unknown): PopulationVerdict {
  if (typeof status !== "string" || !(status in COHERENCE)) {
    return { valid: false, reason: `no population rule for status ${String(status)}` };
  }
  if (raw === null || typeof raw !== "object" || Array.isArray(raw)) {
    return { valid: false, reason: "population absent or not an object" };
  }
  const p = raw as Record<string, unknown>;

  const works = p.works_in_scope;
  // Number.isSafeInteger rejects NaN, Infinity, fractions, and anything past 2^53 in one test.
  if (!Number.isSafeInteger(works) || (works as number) < 0) {
    return { valid: false, reason: "works_in_scope is not a non-negative safe integer" };
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
  publisher?: string;
  /** From the shipped classifier, never re-derived here. */
  kind: "ran" | "mode_unavailable" | "refused";
  population: SearchPopulation;
}

/** Envelope classifications that may carry a population the reader can be shown. */
const DISCLOSABLE = new Set(["ran", "mode_unavailable", "refused"]);

/**
 * Per-publisher populations from one search response.
 *
 * Classification comes from `classifyEnvelope`, the same authority the rows come from, so a
 * publisher can never be counted as having answered here while being withheld there. An invalid
 * sibling contributes nothing: it authorizes neither rows nor an absence claim, and a denominator
 * is an absence claim about everything outside it.
 */
export function searchPopulations(
  raw: unknown,
  classify: (tool: string, entry: unknown) => { kind: string },
): PublisherPopulation[] {
  const list = Array.isArray(raw) ? raw : [raw];
  const seen = new Set<string>();
  const out: PublisherPopulation[] = [];
  for (const entry of list) {
    if (entry === null || typeof entry !== "object") continue;
    const kind = classify("search", entry).kind;
    if (!DISCLOSABLE.has(kind)) continue;
    const record = entry as Record<string, unknown>;
    const envelope = (record.envelope ?? {}) as Record<string, unknown>;
    const verdict = validateSearchPopulation(envelope.status, record.population);
    if (!verdict.valid) continue;
    const publisher = typeof envelope.publisher === "string" ? envelope.publisher : undefined;
    if (publisher !== undefined) {
      if (seen.has(publisher)) continue;
      seen.add(publisher);
    }
    out.push({ publisher, kind: kind as PublisherPopulation["kind"], population: verdict.population });
  }
  return out;
}

/**
 * The denominator a "searched N works" sentence may claim: only publishers that ran the query.
 * Returns undefined when none did, because zero would assert an empty corpus was searched.
 */
export function queriedPopulationTotal(rows: PublisherPopulation[]): number | undefined {
  const queried = rows.filter((r) => r.population.query_ran);
  return queried.length === 0
    ? undefined
    : queried.reduce((n, r) => n + r.population.works_in_scope, 0);
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
