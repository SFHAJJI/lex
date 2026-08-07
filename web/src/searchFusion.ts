/**
 * Merge independently ranked publisher results without pretending their BM25/vector scores are
 * comparable. Each mounted index is one retriever, so reciprocal-rank fusion is applied to its
 * local rank. This preserves every publisher's relevance order while preventing the first (or
 * largest) corpus from consuming every visible result slot.
 */
export type PublisherEnvelope<T extends object> = {
  envelope?: { publisher?: string; jurisdiction?: string };
  hits?: T[];
};

export type FusedHit<T extends object> = T & {
  _jurisdiction?: string;
  _publisher?: string;
};

type Candidate<T extends object> = {
  hit: FusedHit<T>;
  score: number;
  bestRank: number;
  publisher: string;
  identity: string;
};

const RRF_K = 60;

function hitIdentity(hit: Record<string, unknown>, publisher: string, rank: number): string {
  const lexId = String(hit.lex_id ?? "");
  if (lexId) return lexId;
  return [publisher, hit.work, hit.anchor, hit.valid_from, rank].map(String).join("\u001f");
}

/**
 * The MCP contract deliberately returns one provenance envelope per publisher. Consumers that
 * show a single list must call this function instead of concatenating those envelopes.
 */
export function fusePublisherHits<T extends object>(
  result: PublisherEnvelope<T> | PublisherEnvelope<T>[],
): FusedHit<T>[] {
  const envelopes = (Array.isArray(result) ? result : [result])
    .map((value, index) => ({ value, index }))
    .sort((a, b) => {
      const ak = a.value.envelope?.publisher ?? a.value.envelope?.jurisdiction ?? String(a.index);
      const bk = b.value.envelope?.publisher ?? b.value.envelope?.jurisdiction ?? String(b.index);
      return ak.localeCompare(bk) || a.index - b.index;
    });
  const candidates = new Map<string, Candidate<T>>();

  for (const { value: envelope, index: envelopeIndex } of envelopes) {
    const publisher = envelope.envelope?.publisher
      ?? envelope.envelope?.jurisdiction
      ?? `publisher-${envelopeIndex}`;
    const jurisdiction = envelope.envelope?.jurisdiction;
    for (const [rank, raw] of (envelope.hits ?? []).entries()) {
      const identity = hitIdentity(raw as Record<string, unknown>, publisher, rank);
      const contribution = 1 / (RRF_K + rank + 1);
      const existing = candidates.get(identity);
      if (existing) {
        existing.score += contribution;
        existing.bestRank = Math.min(existing.bestRank, rank);
        continue;
      }
      candidates.set(identity, {
        hit: { ...raw, _jurisdiction: jurisdiction, _publisher: publisher },
        score: contribution,
        bestRank: rank,
        publisher,
        identity,
      });
    }
  }

  return [...candidates.values()]
    .sort((a, b) => b.score - a.score
      || a.bestRank - b.bestRank
      || a.publisher.localeCompare(b.publisher)
      || a.identity.localeCompare(b.identity))
    .map(candidate => candidate.hit);
}
