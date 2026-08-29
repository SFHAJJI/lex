/**
 * Merge independently ranked publisher results without pretending their BM25/vector scores are
 * comparable. Each mounted index is one retriever, so reciprocal-rank fusion is applied to its
 * local rank. This preserves every publisher's relevance order while preventing the first (or
 * largest) corpus from consuming every visible result slot.
 */
export type PublisherEnvelope<T extends object> = {
  envelope?: { publisher?: string; jurisdiction?: string; timeline_semantics?: string };
  hits?: T[];
};

export type FusedHit<T extends object> = T & {
  _jurisdiction?: string;
  _publisher?: string;
  _timelineSemantics?: string;
};

type Candidate<T extends object> = {
  hit: FusedHit<T>;
  score: number;
  bestRank: number;
  publisher: string;
  identity: string;
};

const RRF_K = 60;

/**
 * What counts as "the same result", which is the only question fusion asks. The producer's
 * retrieval unit is the provision, not the document: McpCore.cs deduplicates hits on
 * (work, anchor) and only then caps per work, so one law deliberately returns several
 * articles that all carry the same document-level lex_id. Identity on lex_id alone therefore
 * deleted every article after the first, and the passage count was then stated over the
 * survivors. Identity is the (document, provision) pair: exactly the producer's own unit, so
 * it merges genuine agreement between two retrievers and nothing else.
 *
 * An absent provision is a real shape rather than a missing field. A work-level hit carries
 * anchor null; an identifier/title fallback hit carries no anchor key at all. Both mean
 * "this document, no particular article", so both normalize to the same empty provision and
 * still fuse on document identity across publishers. A real anchor is never empty, so nothing
 * can collide with that.
 *
 * With no lex_id nothing can be identified, so the hit keeps its own publisher and rank and
 * merges with nothing. Showing a duplicate is the safe failure; deleting a distinct result
 * is not.
 */
function hitIdentity(hit: Record<string, unknown>, publisher: string, rank: number): string {
  const lexId = String(hit.lex_id ?? "");
  if (lexId) return `${lexId}\u001f${String(hit.anchor ?? "")}`;
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
    const timelineSemantics = envelope.envelope?.timeline_semantics;
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
        hit: { ...raw, _jurisdiction: jurisdiction, _publisher: publisher,
               _timelineSemantics: timelineSemantics },
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
