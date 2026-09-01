// The verification cluster, as React: official source anchor, named digest, provenance link.
//
// Every rule stays in `scripts/verify-cluster.mjs` and is applied by `verifyClusterModel`.
// This file decides how a checked cluster looks and re-derives nothing.
//
// There is no `publisher` prop, and that absence is the point. A `lex_id` is
// `publisher:work:state`, so the publisher of the record this cluster is about is already
// written on the identifier it links to. Taken as a second parameter beside it, the two can
// disagree, and neither link shows it: the anchor is checked against one publisher's host set
// while "Provenance" addresses another publisher's record, and both resolve. Reading the
// publisher off the identifier is what makes them agree by construction rather than by
// review. `renderVerifyCluster` still accepts one from its existing callers and refuses it
// when the record contradicts it; nothing can hand one to this component at all.
//
// The whole digest is on the page, as one selectable text node, with the first eight
// characters emphasised rather than shown alone. A truncated digest in a citation cannot be
// verified by anyone. And it says which digest it is: `record_sha256`, `body_sha256` and
// `text_sha256` are all present on every state and answer different questions, so sixty-four
// hex characters with no label is a number rather than evidence.

import { clusterPublisher, verifyClusterModel } from '../scripts/verify-cluster.mjs';

/**
 * @param {object} props
 * @param {string} props.sourceUri  the publisher's own address for this state
 * @param {string} props.lexId      the record this cluster is about, and its provenance page
 * @param {{kind: string, value: string}} props.hash
 */
export function VerifyCluster({ sourceUri, lexId, hash }) {
  const cluster = verifyClusterModel({
    publisher: clusterPublisher(lexId),
    sourceUri,
    lexId,
    hash,
  });

  return (
    <div className="verify-cluster">
      <a className="verify-source" href={cluster.uri} rel="external">
        Official source
      </a>
      <span className="verify-hash">
        <span className="verify-hash-kind">{cluster.kind}</span>
        {/* One text node, selectable end to end, so what a reader takes away is the whole
            digest even though the first eight carry the visual weight. */}
        <code className="verify-hash-value">
          <span className="verify-hash-short">{cluster.short}</span>
          {cluster.rest}
        </code>
      </span>
      <a className="verify-provenance" href={cluster.provenance}>
        Provenance
      </a>
    </div>
  );
}
