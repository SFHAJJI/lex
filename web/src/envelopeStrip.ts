/**
 * The EnvelopeStrip's facts, extracted from the envelopes a response already carried.
 *
 * Trust rule 4 puts index freshness and stamp validity on every screen without exception, and
 * never-implied rule 8 forbids implying data is fresher than its build. The workspace received
 * both facts in every envelope and discarded them before render: `freshness` and `built_at` did
 * not appear anywhere in this client. Confirmed by the private browser lane, which reports the
 * rule false against the live workspace and true when the disclosure is injected.
 *
 * Freshness is per index, not per product, so these stay per publisher and are never collapsed
 * into one build date. Two mounted indexes built a week apart have two answers, and picking one
 * would assert a freshness the other does not have.
 *
 * Fails closed: a value that is not the type the producer promised becomes undefined, and an
 * absent build date is stated as absent rather than omitted, because an undated screen is exactly
 * what rule 8 forbids. For the build date, "the type the producer promised" is not "a string" but
 * the producer's exact timestamp grammar, because a bounded string is enough to print
 * `index built tomorrow`, which is a freshness claim rule 8 forbids just as much as an undated one.
 */

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

/** Commit hashes and digests are bounded; anything longer is not a value we will render. */
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

export function envelopeStripRows(raw: unknown): EnvelopeStripRow[] {
  if (!Array.isArray(raw)) return [];
  const byPublisher = new Map<string, EnvelopeStripRow>();
  for (const entry of raw) {
    const envelope = (entry as { envelope?: unknown } | null)?.envelope;
    if (envelope === null || typeof envelope !== "object" || Array.isArray(envelope)) continue;
    const e = envelope as Record<string, unknown>;
    const publisher = str(e.publisher);
    if (publisher === undefined || byPublisher.has(publisher)) continue;
    const freshness = (e.freshness ?? {}) as Record<string, unknown>;
    const artifact = (e.artifact ?? {}) as Record<string, unknown>;
    byPublisher.set(publisher, {
      publisher,
      timelineSemantics: str(e.timeline_semantics),
      builtAt: buildTimestamp(freshness.built_at),
      signatureValid: typeof freshness.stamp_signature_valid === "boolean"
        ? freshness.stamp_signature_valid
        : undefined,
      corpusCommit: str(freshness.corpus_commit),
      codeCommit: str(artifact.code_commit),
      manifestSetId: str(artifact.manifest_set_id),
      contentDigest: str(artifact.content_digest),
    });
  }
  // Ordinal sort so the strip renders identically between processes and runs.
  return [...byPublisher.values()].sort((a, b) => (a.publisher < b.publisher ? -1 : 1));
}

/**
 * Never render an undated current. A missing build date is said out loud, because a screen that
 * simply omits it reads as "current" and that is the claim rule 8 exists to stop.
 */
export function indexFreshnessLabel(builtAt: string | undefined): string {
  return builtAt === undefined ? "index build date unavailable" : `index built ${builtAt}`;
}
