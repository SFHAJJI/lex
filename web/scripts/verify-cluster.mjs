// The VerifyCluster of UX spec section 1: official source anchor, hash chip, provenance link.
// Identical on every screen, because a verification affordance that appears in some places
// teaches readers that the others are not verifiable.
//
// Two rules here are about what a hash means.
//
// The chip shows eight hex characters because a full digest is unreadable, but the copy
// control copies the whole thing. A truncated digest pasted into a citation cannot be
// verified by anyone, so a component that copies what it displays would quietly produce
// unverifiable evidence, which is the opposite of this component's purpose.
//
// The chip also names which digest it is. `record_sha256`, `body_sha256` and `text_sha256`
// are all present on every state and they answer different questions. Eight hex characters
// with no label is a number, not evidence.

import { mark } from './design-tokens.mjs';

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

function requireHttpsPublisherUri(sourceUri) {
  if (typeof sourceUri !== 'string' || sourceUri.length === 0) {
    throw new Error('the verify cluster requires the publisher source_uri');
  }
  let parsed;
  try {
    parsed = new URL(sourceUri);
  } catch {
    throw new Error(`source_uri is not a URL: ${sourceUri}`);
  }
  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    throw new Error(`source_uri is not an http(s) URL: ${sourceUri}`);
  }
  return sourceUri;
}

/**
 * @param {object} input
 * @param {string} input.sourceUri  the publisher's own address for this state
 * @param {string} input.lexId      the identity whose provenance page this links to
 * @param {{kind: string, value: string}} input.hash
 */
export function renderVerifyCluster({ sourceUri, lexId, hash }) {
  const uri = requireHttpsPublisherUri(sourceUri);

  if (typeof lexId !== 'string' || lexId.trim().length === 0) {
    throw new Error('the verify cluster requires a lex_id for the provenance link');
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

  const short = hash.value.slice(0, 8);

  return (
    '<div class="verify-cluster">' +
    `<a class="verify-source" href="${escapeHtml(uri)}" rel="external">Official source</a>` +
    '<span class="verify-hash">' +
    `<span class="verify-hash-kind">${escapeHtml(hash.kind)}</span>` +
    `<code class="verify-hash-short" title="${escapeHtml(hash.kind)}">${escapeHtml(short)}</code>` +
    // The full value travels with the control that copies it, never only the eight shown.
    `<button type="button" class="verify-copy" data-copy="${escapeHtml(hash.value)}">` +
    `Copy the full ${escapeHtml(hash.kind)}</button>` +
    '</span>' +
    `<a class="verify-provenance" href="/provenance/${encodeURIComponent(lexId)}">Provenance</a>` +
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

  const builtAt = freshness.built_at ?? null;
  const valid = freshness.stamp_signature_valid;

  if (typeof valid !== 'boolean') {
    throw new Error(
      'stamp_signature_valid must be a boolean; an absent signature verdict is not the same ' +
        'as a valid one and must never render as one',
    );
  }

  const stamp = valid
    ? '<span class="strip-stamp-valid">stamp valid</span>'
    : mark('--conflict', 'stamp NOT valid');

  const built = builtAt
    ? `index built ${escapeHtml(builtAt)}`
    : 'index build time not recorded';

  const rows = Object.entries({
    corpus_commit: artifact.corpus_commit ?? freshness.corpus_commit,
    code_commit: artifact.code_commit,
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
