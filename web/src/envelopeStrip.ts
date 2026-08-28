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
 * what rule 8 forbids.
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
      builtAt: str(freshness.built_at),
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
