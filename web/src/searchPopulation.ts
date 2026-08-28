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
 * ONE SHARED NORMALIZED RESPONSE SET. `normalizeSearchResponse` classifies every entry once and
 * returns the rows, the populations and the completeness verdict from that single pass, so a
 * publisher can never have its hits rendered beside a denominator that was refused, and no
 * projection can disagree with another about which publishers were admitted.
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

export const POPULATION_BOUNDS = {
  maxExclusions: 20,
  maxExclusionLength: 300,
  maxPublisherLength: 64,
};

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

/**
 * The publisher identity a population may be attributed to.
 *
 * The bound is the shipped classifier's own identifier bound, mirrored rather than widened or
 * trimmed, so both sides name the same publisher. A value the classifier would not accept is not
 * an identity: " lu-legilux " is not lu-legilux, and admitting it here would let one response
 * carry two spellings of one publisher, each passing the duplicate check the other should have
 * failed.
 */
export function boundedPublisher(raw: unknown): string | undefined {
  return typeof raw === "string"
    && raw.length > 0
    && raw.length <= POPULATION_BOUNDS.maxPublisherLength
    && /^[a-z0-9_-]+$/i.test(raw)
    ? raw
    : undefined;
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
  /** Always present: an unattributable denominator is never returned. */
  publisher: string;
  /** From the shipped classifier, never re-derived here. */
  kind: "ran" | "mode_unavailable" | "refused";
  population: SearchPopulation;
}

/** Envelope classifications that may carry a population the reader can be shown. */
const DISCLOSABLE = new Set(["ran", "mode_unavailable", "refused"]);

/**
 * Set equality, not array equality.
 *
 * `known_exclusions` is a set the producer happens to serialize as a list. Two entries that
 * disclose the same exclusions in a different order state one fact, and comparing positionally
 * would call that a conflict and drop a publisher that never disagreed. Two entries that disclose
 * DIFFERENT exclusions disagree about what their denominator leaves out, which is a disagreement
 * about the denominator itself, not a detail beside it.
 */
function sameExclusions(a: readonly string[], b: readonly string[]): boolean {
  // Both sides are already de-duplicated by exclusionsOf, so equal size plus containment is set
  // equality rather than multiset equality.
  if (a.length !== b.length) return false;
  const present = new Set(a);
  return b.every((item) => present.has(item));
}

/** Logical identity of one publisher's disclosure: every field a reader could check. */
function sameDisclosure(a: PublisherPopulation, b: PublisherPopulation): boolean {
  return a.kind === b.kind
    && a.population.works_in_scope === b.population.works_in_scope
    && a.population.basis === b.population.basis
    && a.population.query_ran === b.population.query_ran
    && a.population.scope_filters_applied === b.population.scope_filters_applied
    && sameExclusions(a.population.known_exclusions, b.population.known_exclusions);
}

/**
 * The result of one normalization pass over one search response.
 *
 * CONTRACT FOR THE CALLER
 *
 * 1. Project rows from the entry list this returns, never from the raw response. The list is the
 *    raw entries minus every entry whose row authority was withheld, so hits from a publisher
 *    whose denominator was refused cannot reach the screen.
 * 2. `populations` is the footer's only source. It holds at most one entry per publisher and
 *    never holds a withheld one, so footer and rows describe the same set by construction.
 * 3. The entry list is named `entries` when nothing was withheld and `entriesAfterWithholding`
 *    when something was. That is deliberate: TypeScript refuses to hand out rows until the caller
 *    has read `complete`, so an incomplete response cannot be presented as a whole one.
 * 4. When `complete` is false the caller must disclose the withholding and must NOT make an
 *    absence claim from these rows. Withheld entries are gone from the list, so the projector can
 *    no longer see that anything was hidden and would otherwise report a confident "nothing
 *    matched" for a response that was cut down.
 * 5. `complete: true` says only that this pass withheld nothing. It is not a health claim about
 *    the response: an envelope the classifier rejects is passed through untouched and surfaces in
 *    the projector's own absence state as `incomplete_response`.
 */
export type NormalizedSearchResponse =
  | {
      /** Nothing was withheld: every entry is attributable and every denominator validated. */
      complete: true;
      /** The entries to project rows from. Entry for entry, the response as received. */
      entries: unknown[];
      /** At most one validated population per publisher. */
      populations: PublisherPopulation[];
    }
  | {
      complete: false;
      /** The entries to project rows from, with every withheld entry removed. */
      entriesAfterWithholding: unknown[];
      /** At most one validated population per publisher, holding none of the withheld ones. */
      populations: PublisherPopulation[];
      /**
       * Named publishers whose population authority is void, sorted. Their rows are gone from the
       * entry list and they contribute no population. Empty when only unattributable entries were
       * withheld.
       */
      withheldPublishers: string[];
      /**
       * How many disclosable entries carried rows or a population under no bounded publisher
       * identity. They are withheld, and named to nobody, which is why they are only a count.
       */
      unattributedEntries: number;
    };

/**
 * Normalize one search response into the single set that rows, population and absence authority
 * all consume. See NormalizedSearchResponse for the caller's contract.
 *
 * Classification comes from `classifyEnvelope`, the same authority the rows come from, so a
 * publisher can never be counted as having answered here while being withheld there, and it is
 * consulted exactly once per entry.
 *
 * A publisher's population authority is void when any of its disclosable entries fails validation
 * or when two of them disagree. Void is decided across the whole response before anything is
 * projected, so it cannot depend on arrival order, and it takes that publisher's ROWS with it: a
 * row list the reader cannot check against a denominator is the exact claim this lane exists to
 * stop.
 */
export function normalizeSearchResponse(
  raw: unknown,
  classify: (tool: string, entry: unknown) => { kind: string },
): NormalizedSearchResponse {
  const list = Array.isArray(raw) ? raw : [raw];
  const scanned: { entry: unknown; carriesRows: boolean; publisher?: string }[] = [];
  const byPublisher = new Map<string, PublisherPopulation>();
  const voided = new Set<string>();
  let unattributed = 0;

  for (const entry of list) {
    const kind = entry !== null && typeof entry === "object"
      ? classify("search", entry).kind
      : "invalid";
    // Entries the classifier will not disclose stay in the list untouched. They carry no rows and
    // no population, and removing them would shrink the invalid count the projector builds its
    // absence state from, turning a response that hid something into a confident "nothing found".
    if (!DISCLOSABLE.has(kind)) {
      scanned.push({ entry, carriesRows: false });
      continue;
    }
    const record = entry as Record<string, unknown>;
    const envelope = (record.envelope ?? {}) as Record<string, unknown>;
    const publisher = boundedPublisher(envelope.publisher);
    // Only a ran envelope carries hits. A refusal carries a limitation the reader must still be
    // told about, so withholding is scoped to row-bearing entries and never silences a refusal.
    const carriesRows = kind === "ran";
    scanned.push({ entry, carriesRows, publisher });
    if (publisher === undefined) {
      // A denominator nobody is named for cannot be attributed, checked or corrected. Rule 6 asks
      // whose population this is, not only how large it is. A refusal that discloses neither rows
      // nor a population has nothing to attribute and passes through without opening a hole.
      if (carriesRows || record.population !== undefined) unattributed++;
      continue;
    }
    const verdict = validateSearchPopulation(envelope.status, record.population);
    if (!verdict.valid) {
      voided.add(publisher);
      continue;
    }
    const candidate: PublisherPopulation = {
      publisher,
      kind: kind as PublisherPopulation["kind"],
      population: verdict.population,
    };
    const existing = byPublisher.get(publisher);
    if (existing === undefined) {
      byPublisher.set(publisher, candidate);
      continue;
    }
    // Two entries for one publisher that disagree are an incoherent response. Keeping the first
    // lets arrival order decide what the reader is told, so the publisher drops out of both
    // projections rather than contributing a number nothing stands behind. Logically identical
    // duplicates simply collapse.
    if (!sameDisclosure(existing, candidate)) voided.add(publisher);
  }

  for (const publisher of voided) byPublisher.delete(publisher);
  const populations = [...byPublisher.values()];
  const kept = scanned
    .filter((item) => !(item.carriesRows
      && (item.publisher === undefined || voided.has(item.publisher))))
    .map((item) => item.entry);
  const withheldPublishers = [...voided].sort();
  if (withheldPublishers.length === 0 && unattributed === 0) {
    return { complete: true, entries: kept, populations };
  }
  return {
    complete: false,
    entriesAfterWithholding: kept,
    populations,
    withheldPublishers,
    unattributedEntries: unattributed,
  };
}

/**
 * Per-publisher populations from one search response.
 *
 * The narrow read, for a caller that renders only the footer. Anything that also renders rows
 * must call `normalizeSearchResponse`: populations alone cannot tell it which publishers were
 * withheld, and rendering their hits beside this footer is the defect O2 named.
 */
export function searchPopulations(
  raw: unknown,
  classify: (tool: string, entry: unknown) => { kind: string },
): PublisherPopulation[] {
  return normalizeSearchResponse(raw, classify).populations;
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
 * normalizeSearchResponse may collapse an identical duplicate because it has the response in
 * front of it: it classified both entries under one authority and validated each against its own
 * envelope status, so "these are one disclosure" is an observation there. Here that context is
 * gone. Two identical-looking rows are equally likely to be one disclosure counted twice by a
 * caller that concatenated two lists, and picking a reading is the guess the order-dependence
 * objection forbade. Refusing costs a legitimate caller nothing, because the normalized set
 * already satisfies the invariant.
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
