// The VerifyCluster of UX spec section 1: official source anchor, hash chip, provenance link.
// Identical on every screen, because a verification affordance that appears in some places
// teaches readers that the others are not verifiable.
//
// Three rules here are about what a hash means, and one about what "official" means.
//
// The whole digest is on the page, as selectable text, with its first eight characters
// emphasised. It used to be an eight-character chip beside a Copy button, and that button
// had no handler: this line ships inert HTML with no client script, so the control was a
// promise the page could not keep, and the only digest a reader could actually take away
// was the truncation. A truncated digest in a citation cannot be verified by anyone. The
// scripted chip returns when there is a script to hang it on; until then the evidence is
// present rather than promised.
//
// The digest also names which one it is. `record_sha256`, `body_sha256` and `text_sha256`
// are all present on every state and they answer different questions. Sixty-four hex
// characters with no label is a number, not evidence.
//
// The official-source anchor is validated against the publisher's own host set, not merely
// escaped. `http://evil.example/fake` under the words "Official source" was renderable
// before, and escaping made it safe to place in the attribute while leaving it a working
// link to the wrong place.
//
// And the publisher whose host set that is comes off the `lex_id`, not from a second argument
// beside it. Both of this cluster's links are about one record, and a caller that could name
// the publisher separately could have the anchor checked against one publisher while
// "Provenance" addressed another. The record already says which, so nobody is asked.

import { publisherOf } from './record-identity.mjs';
import { publisherSourceUri } from './routes.mjs';
import { TIMELINE_SEMANTICS } from './state-banner.mjs';
import { requireUtcInstant } from './temporal.mjs';

/** The digests a state carries, product spec section 4.6. */
export const HASH_KINDS = Object.freeze([
  'record_sha256',
  'body_sha256',
  'text_sha256',
]);

const KINDS = new Set(HASH_KINDS);

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/**
 * The publisher a cluster is about, which is the record's and never a caller's.
 *
 * A `lex_id` is `publisher:work:state`, so the publisher is already written on the record this
 * cluster links to. Reading it here rather than accepting it beside the identifier is what
 * makes the two links agree by construction: the "Official source" anchor is checked against
 * one publisher's host set and the provenance link addresses one publisher's record, and a
 * caller holding both halves could otherwise name a different publisher for each.
 */
export function clusterPublisher(lexId) {
  if (typeof lexId !== 'string' || lexId.trim().length === 0) {
    throw new Error('the verify cluster requires a lex_id for the provenance link');
  }
  return publisherOf(lexId, 'the verify cluster');
}

/**
 * Everything a verify cluster shows, checked. The markup is the renderers' business.
 *
 * @param {object} input
 * @param {string} input.publisher  the publisher a caller believes this is, cross-checked
 * @param {string} input.sourceUri  the publisher's own address for this state
 * @param {string} input.lexId      the identity whose provenance page this links to
 * @param {{kind: string, value: string}} input.hash
 */
export function verifyClusterModel({ publisher, sourceUri, lexId, hash }) {
  const uri = publisherSourceUri({ publisher, uri: sourceUri });

  // The record's own publisher, and the caller may only agree with it. Before this, a cluster
  // could be handed `lu-legilux` beside an `eu-eurlex:` identifier: the anchor was then checked
  // against Legilux hosts while "Provenance" led to a Union record, and both links looked
  // official. That is the same shape as authenticity evidence lifted from one resource onto a
  // sibling, and escaping does nothing about it.
  const recordPublisher = clusterPublisher(lexId);
  if (publisher !== recordPublisher) {
    throw new Error(
      `this cluster was told publisher ${JSON.stringify(publisher)} while ${lexId} names ` +
        `${recordPublisher}; the publisher is written on the record, so a second one beside it ` +
        'is a fact about the caller and it decides which host set the official link is checked ' +
        'against',
    );
  }

  if (!hash || !KINDS.has(hash.kind)) {
    throw new Error(
      `a hash chip must name which digest it shows; ${JSON.stringify(hash?.kind)} is not one of ` +
        HASH_KINDS.join(', '),
    );
  }

  if (typeof hash.value !== 'string' || !/^[0-9a-f]{64}$/.test(hash.value)) {
    throw new Error('a digest is 64 lowercase hex characters');
  }

  return Object.freeze({
    publisher: recordPublisher,
    uri,
    kind: hash.kind,
    // The digest is one value shown in two runs: the first eight carry the visual weight and
    // the rest sits beside them, in one selectable text node.
    short: hash.value.slice(0, 8),
    rest: hash.value.slice(8),
    provenance: `/provenance/${encodeURIComponent(lexId)}`,
  });
}

/**
 * @param {object} input
 * @param {string} input.publisher  the publisher whose host set the source must be on
 * @param {string} input.sourceUri  the publisher's own address for this state
 * @param {string} input.lexId      the identity whose provenance page this links to
 * @param {{kind: string, value: string}} input.hash
 */
export function renderVerifyCluster({ publisher, sourceUri, lexId, hash }) {
  const cluster = verifyClusterModel({ publisher, sourceUri, lexId, hash });

  return (
    '<div class="verify-cluster">' +
    `<a class="verify-source" href="${escapeHtml(cluster.uri)}" rel="external">Official source</a>` +
    '<span class="verify-hash">' +
    `<span class="verify-hash-kind">${escapeHtml(cluster.kind)}</span>` +
    // One text node, selectable end to end, so what a reader takes away is the whole digest
    // even though the first eight carry the visual weight.
    `<code class="verify-hash-value">` +
    `<span class="verify-hash-short">${escapeHtml(cluster.short)}</span>${escapeHtml(cluster.rest)}</code>` +
    '</span>' +
    `<a class="verify-provenance" href="${escapeHtml(cluster.provenance)}">Provenance</a>` +
    '</div>'
  );
}

/**
 * The freshness and identity strip of UX spec section 1, pinned to the bottom of every data
 * view. An invalid stamp is not rendered like a valid one: it takes the conflict token and
 * says so in words, because a strip that reports `false` in the same neutral voice as `true`
 * is a strip nobody reads.
 */
export function renderEnvelopeStrip({ envelope }) {
  const publisher = envelope?.publisher_name ?? envelope?.publisher;
  const semantics = envelope?.timeline_semantics;
  const freshness = envelope?.freshness ?? {};
  const artifact = envelope?.artifact ?? {};

  if (typeof publisher !== 'string' || publisher.length === 0) {
    throw new Error('the envelope strip requires a publisher');
  }

  // The same closed vocabulary the state banner uses. The strip used to print whatever it
  // was given, or "timeline semantics not stated", so an unknown value reached a reader as
  // if it were a publisher's word for its own dates.
  if (!TIMELINE_SEMANTICS.includes(semantics)) {
    throw new Error(
      `unknown timeline_semantics ${JSON.stringify(semantics)}; the strip may not invent a ` +
        `publisher's vocabulary, and the admitted values are ${TIMELINE_SEMANTICS.join(', ')}`,
    );
  }

  const builtAt = freshness.built_at ?? null;
  if (builtAt !== null) requireUtcInstant(builtAt, 'freshness.built_at');
  const valid = freshness.stamp_signature_valid;

  if (typeof valid !== 'boolean') {
    throw new Error(
      'stamp_signature_valid must be a boolean; an absent signature verdict is not the same ' +
        'as a valid one and must never render as one',
    );
  }

  // An invalid signature is not a date conflict, and `--conflict` says in words that two
  // publisher dates disagree. Borrowing it to mean "this stamp did not verify" would put a
  // false sentence on the page in the one place that exists to say whether to trust it. The
  // invalid case is plain, emphatic text with no token.
  const stamp = valid
    ? '<span class="strip-stamp-valid">stamp signature valid</span>'
    : '<strong class="strip-stamp-invalid">stamp signature did NOT verify</strong>';

  const built = builtAt
    ? `index built ${escapeHtml(builtAt)}`
    : 'index build time not recorded';

  // Two commits, named. `code_commit` answered a different question than it appeared to: it
  // is the commit that built the index, while the commit that computed and served the answer
  // was nowhere in the envelope. Decision 63 settles the two names and gives `code_commit`
  // no standing, so there is no alias from it here: a legacy-only value rendered under a
  // stronger V3 fact is the V2 envelope surviving inside the V3 line.
  const rows = Object.entries({
    corpus_commit: artifact.corpus_commit ?? freshness.corpus_commit,
    index_builder_source_commit: artifact.index_builder_source_commit,
    serving_runtime_source_commit: artifact.serving_runtime_source_commit,
    manifest_set_id: artifact.manifest_set_id,
    content_digest: artifact.content_digest,
  })
    .map(
      ([key, value]) =>
        `<div class="strip-row"><dt>${escapeHtml(key)}</dt><dd>${
          value ? escapeHtml(value) : 'not recorded'
        }</dd></div>`,
    )
    .join('');

  return (
    '<details class="envelope-strip">' +
    '<summary>' +
    `<span class="strip-publisher">${escapeHtml(publisher)}</span>` +
    `<span class="strip-semantics">${escapeHtml(semantics ?? 'timeline semantics not stated')}</span>` +
    `<span class="strip-built">${built}</span>` +
    stamp +
    '</summary>' +
    `<dl class="strip-detail">${rows}</dl>` +
    '</details>'
  );
}
