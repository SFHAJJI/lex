/**
 * The EnvelopeStrip's facts, taken from the ONE parse of a governed response.
 *
 * Trust rule 4 puts index freshness and stamp validity on every screen without exception, and
 * never-implied rule 8 forbids implying data is fresher than its build. The workspace received
 * both facts in every envelope and discarded them before render: `freshness` and `built_at` did
 * not appear anywhere in this client. Confirmed by the private browser lane, which reports the
 * rule false against the live workspace and true when the disclosure is injected.
 *
 * ONE AUTHORITY, NOT TWO (O1). This module used to be a second parser. `envelopeStripRows` took
 * the raw response beside `parseGovernedResponse`, checked publisher and strip field shapes for
 * itself, and never asked whether the unit carrying them was a valid unit of the requested tool.
 * A search unit with valid-looking freshness and `hits: [null]` was therefore rejected as unusable
 * by the table and still displayed a confident build date and a valid-signature badge. A trust
 * surface cannot ship a signature claim authorized by input its own parser refused. The strip now
 * consumes the typed `GovernedResponse` and nothing else:
 *
 *   - an ADMITTED unit contributes the disclosure fields validated at that one parse;
 *   - a CONFLICTED or named UNREADABLE publisher renders the identity-unavailable row and
 *     nothing else, because the response established no identity for it, while dropping it
 *     would read as "not mounted", a different and false statement;
 *   - rejected anonymous input authorizes no row at all, so it can make no claim.
 *
 * Freshness is per index, not per product, so these stay per publisher and are never collapsed
 * into one build date. Two mounted indexes built a week apart have two answers, and picking one
 * would assert a freshness the other does not have.
 *
 * The same rule holds inside one publisher, and the parse enforces it upstream now: at most one
 * claim-bearing unit per publisher survives, and a publisher that sent two is conflicted whatever
 * the two units said. What is left here is the consequence rather than a second comparison. A
 * publisher appearing twice among the units is refused and never collapsed, even when the two
 * rows look identical, exactly as `queriedDenominator` refuses a repeated publisher: arrival order
 * is not evidence, and a reader checking an answer against the wrong corpus commit is the failure
 * this disclosure exists to prevent.
 *
 * Fails closed: a value that is not the type the producer promised becomes undefined, and an
 * absent build date is stated as absent rather than omitted, because an undated screen is exactly
 * what rule 8 forbids. For the build date, "the type the producer promised" is not "a string" but
 * the producer's exact timestamp grammar, because a bounded string is enough to print
 * `index built tomorrow`, which is a freshness claim rule 8 forbids just as much as an undated one.
 */

// TYPE-ONLY, and it creates no runtime cycle. The runtime direction is limitations.ts importing
// this module for `envelopeDisclosure` and `envelopeStripRows`; the import back is erased, which
// is the same shape searchPopulation.ts already uses.
import type { GovernedResponse, PublisherUnit } from "./limitations.ts";

export interface EnvelopeStripRow {
  publisher: string;
  timelineSemantics?: string;
  builtAt?: string;
  signatureValid?: boolean;
  corpusCommit?: string;
  codeCommit?: string;
  manifestSetId?: string;
  contentDigest?: string;
}

/**
 * The identity fields one envelope discloses.
 *
 * Neither the publisher nor the timeline semantics is here, and their absence is the point: both
 * are validated once, at the parse, and travel on the unit. The publisher is the join key across
 * three disclosures and goes through `publisherIdentity`, which never repairs; the timeline
 * semantics goes through the parse's own bounded validator. A second validator for either field
 * in this module is exactly the two-authorities defect O1 named, one field at a time.
 */
export type EnvelopeDisclosure = Omit<EnvelopeStripRow, "publisher" | "timelineSemantics">;

/**
 * Commit hashes and digests are bounded; anything longer is not a value we will render.
 *
 * This helper TRIMS, which is harmless for a hash or a digest and was a defect for `publisher`.
 * The publisher is a join key across three disclosures, so " lu-legilux " trimmed here became one
 * identity that the population footer and the limitation list both refused. The publisher no
 * longer passes through this module at all; everything below still goes through `str`.
 */
const MAX_IDENTITY = 128;

function str(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const trimmed = value.trim();
  return trimmed.length > 0 && trimmed.length <= MAX_IDENTITY ? trimmed : undefined;
}

/**
 * The producer's build-timestamp grammar, frozen. Not derived from intuition about ISO 8601.
 *
 * Evidence. `Lex.Ingest/IndexFromCorpus.cs` is the single site that mints the value, and writes
 * `now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")` into the stamp. `Lex.Mcp/McpCore.cs` copies
 * that stamp entry into `envelope.freshness.built_at` verbatim, so nothing reformats it in
 * between. `Lex.Ingest/CorpusIntegrity.cs` validates sibling UTC stamps against that same exact
 * format string. Observed live on 2026-08-28 from the `coverage` tool: `2026-08-15T09:01:06Z`
 * (eu-eurlex) and `2026-08-15T09:22:08Z` (lu-legilux). Producer and live output agree.
 *
 * So: fixed width, literal `T` and `Z`, no offsets, no fractional seconds, no surrounding
 * whitespace. The producer emits none of those, and accepting one would widen the grammar past
 * what was observed. The anchors are also the length bound, since a match is exactly 20
 * characters; anything longer cannot match, overlong junk and a padded real stamp alike.
 *
 * Four digits is also the producer's own upper bound and needs no separate guard: the value is
 * minted from a .NET `DateTime`, and `DateTime.MaxValue` is `9999-12-31`, the largest year four
 * digits can express. The lower bound is not free that way, and `buildTimestamp` carries it.
 */
const BUILT_AT = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})Z$/;

/**
 * A build date the producer could actually have emitted, or undefined. Total and fail-closed.
 *
 * The grammar alone is not enough. `2026-02-31` and `2026-13-01` match it and are not instants
 * that exist, and JavaScript will not say so: `new Date("2026-02-31")` rolls silently over to
 * 3 March rather than failing, so a parse that "succeeds" proves nothing. The check is therefore a
 * round trip. Rebuild the instant from the parsed components and require every component back
 * unchanged; any rollover moves one of them and the value is refused.
 */
function buildTimestamp(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const parts = BUILT_AT.exec(value);
  if (parts === null) return undefined;
  const [year, month, day, hour, minute, second] = parts.slice(1).map(Number);
  // A producer-derived bound, not an arbitrary narrowing. The value is minted from a .NET
  // `DateTime`, whose `DateTime.MinValue` is `0001-01-01`, so year 0000 is not merely unobserved,
  // it is unmintable at the single mint site. Four digits is the only reason it is expressible
  // here, and it is also the only sub-0001 year this grammar can express, so refusing it closes
  // the whole gap below the producer's range. Years 0001-0099 stay in range and stay accepted.
  if (year === 0) return undefined;
  const stamp = new Date(Date.UTC(year, month - 1, day, hour, minute, second));
  // Date.UTC maps years 0-99 onto 1900-1999, which would defeat the year comparison below, and
  // is what would otherwise let 0000 round-trip cleanly. Load-bearing for 0001-0099 either way.
  stamp.setUTCFullYear(year);
  return stamp.getUTCFullYear() === year
    && stamp.getUTCMonth() === month - 1
    && stamp.getUTCDate() === day
    && stamp.getUTCHours() === hour
    && stamp.getUTCMinutes() === minute
    && stamp.getUTCSeconds() === second
    ? value
    : undefined;
}

/** Nothing disclosed: every identity field absent, which every label states out loud. */
export const NO_DISCLOSURE: EnvelopeDisclosure = {
  builtAt: undefined,
  signatureValid: undefined,
  corpusCommit: undefined,
  codeCommit: undefined,
  manifestSetId: undefined,
  contentDigest: undefined,
};

/**
 * The identity fields one envelope discloses, validated field by field.
 *
 * Called from the parse, once per classified envelope, so the strip never reads a response for
 * itself. Total: an envelope that is not an object, or that carries no freshness or artifact
 * object, discloses nothing rather than failing, because the unit around it may still be a
 * perfectly valid claim and rule 4 wants the publisher listed either way.
 */
export function envelopeDisclosure(envelope: unknown): EnvelopeDisclosure {
  if (envelope === null || typeof envelope !== "object" || Array.isArray(envelope)) {
    return NO_DISCLOSURE;
  }
  const e = envelope as Record<string, unknown>;
  const freshness = (e.freshness ?? {}) as Record<string, unknown>;
  const artifact = (e.artifact ?? {}) as Record<string, unknown>;
  return {
    builtAt: buildTimestamp(freshness.built_at),
    signatureValid: typeof freshness.stamp_signature_valid === "boolean"
      ? freshness.stamp_signature_valid
      : undefined,
    corpusCommit: str(freshness.corpus_commit),
    codeCommit: str(artifact.code_commit),
    manifestSetId: str(artifact.manifest_set_id),
    contentDigest: str(artifact.content_digest),
  };
}

/**
 * The publisher is mounted and its index identity could not be established. Every field is absent,
 * so `indexFreshnessLabel` and `signatureStatusLabel` state the unavailable case and the identity
 * list renders empty. There is no path from here to a confident build date.
 *
 * The whole row blanks, not only the fields that disagreed. Keeping the agreeing halves of two
 * withheld disclosures would mint a row no envelope carried, and pairing one envelope's build
 * date with another's corpus commit is a stronger claim than either envelope made.
 */
function identityUnestablished(publisher: string): EnvelopeStripRow {
  return { publisher, timelineSemantics: undefined, ...NO_DISCLOSURE };
}

/**
 * The strip rows one parsed governed response authorizes.
 *
 * Takes the typed parse, never bytes. See the header: an admitted unit discloses what it
 * validated, a withheld publisher discloses only that its identity is unavailable, and anything
 * the parse refused contributes nothing at all.
 */
export function envelopeStripRows(parsed: GovernedResponse): EnvelopeStripRow[] {
  // DEFENSIVE, and not decoration. `tool<any>` makes `any` the static type of every raw MCP
  // response in this workspace, so a restored raw call would compile and hand this function a
  // JSON array. Raw transport carries no `units`, so it authorizes no row, which is O1's third
  // rule enforced at runtime rather than asserted in a comment.
  const response = parsed === null || typeof parsed !== "object"
    ? undefined
    : parsed as Partial<GovernedResponse>;
  const carriedUnits: unknown = response?.units;
  const units: PublisherUnit[] = Array.isArray(carriedUnits)
    ? carriedUnits as PublisherUnit[]
    : [];
  const withheld: unknown[] = [
    ...(Array.isArray(response?.conflicted) ? response.conflicted : []),
    ...(Array.isArray(response?.unreadable) ? response.unreadable : []),
  ];

  const rows = new Map<string, EnvelopeStripRow>();
  const unestablished = new Set<string>();
  for (const name of withheld) {
    if (typeof name === "string" && name.length > 0) unestablished.add(name);
  }
  for (const unit of units) {
    const publisher = typeof unit?.publisher === "string" ? unit.publisher : "";
    if (publisher.length === 0) continue;
    // Refused, never collapsed. The parse admits at most one unit per publisher, so a repeat
    // reaching here did not come from it, and two disclosures for one index establish no identity
    // however alike they look.
    if (rows.has(publisher)) {
      unestablished.add(publisher);
      continue;
    }
    rows.set(publisher, {
      publisher,
      timelineSemantics: unit.timelineSemantics,
      ...(unit.disclosure ?? NO_DISCLOSURE),
    });
  }
  // Last, so a withheld publisher can never keep a row an admitted sibling entry minted for it.
  for (const publisher of unestablished) rows.set(publisher, identityUnestablished(publisher));
  // Ordinal sort so the strip renders identically between processes and runs.
  return [...rows.values()].sort((a, b) => (a.publisher < b.publisher ? -1 : 1));
}

/**
 * Never render an undated current. A missing build date is said out loud, because a screen that
 * simply omits it reads as "current" and that is the claim rule 8 exists to stop.
 *
 * A publisher whose identity was withheld arrives here as undefined and gets the same sentence,
 * which is the honest one either way: the strip cannot state this index's build date.
 */
export function indexFreshnessLabel(builtAt: string | undefined): string {
  return builtAt === undefined ? "index build date unavailable" : `index built ${builtAt}`;
}
