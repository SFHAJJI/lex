// The RefusalCard of UX spec section 1, over the closed refusal registry of product spec
// section 4.9.
//
// Two rules are enforced by construction rather than by review.
//
// First, the registry is closed. An unknown code is refused here rather than rendered as a
// generic error, because a refusal the client does not recognise is exactly the case where a
// reader most needs to be told the truth about what happened.
//
// Second, and this is the one the specs call the most consequential UX gap: a refusal is
// never sterile. v3-spec item 4 says the boundary refusal always co-delivers the governing
// provisions, on the ground that a bare refusal trains users that honesty equals
// uselessness. So a card cannot be constructed with nothing in it. It must carry a helpful
// payload, or co-delivered governing text, or a handoff, and `advice_boundary` must carry
// the governing text specifically, because that is the refusal where the reader still has a
// real question the corpus can answer.
//
// A refusal is styled as an answer: neutral ground, shield icon, no alert role. It is not an
// error toast, and the components here give a caller no way to make it one.

import { mark } from './design-tokens.mjs';

/**
 * The closed registry, product spec section 4.9.
 *
 * The UX spec's prose names two of these informally, `unknown_anchor` and `unknown_work`,
 * where the product spec and the live service use `anchor_not_in_version` and
 * `identifier_unknown`. The product spec is the versioned registry, so it governs here, and
 * the discrepancy is raised rather than silently resolved.
 */
export const REFUSAL_CODES = Object.freeze([
  'identifier_unknown',
  'ambiguous_identifier',
  'out_of_corpus_scope',
  'no_version_for_date',
  'ambiguous_version',
  'anchor_not_in_version',
  'language_not_available',
  'text_not_available',
  'text_withheld',
  'format_not_available',
  'profiles_differ',
  'not_transposable',
  'derivation_refused',
  'retrieval_mode_unavailable',
  'no_corpus_mounted',
  'snapshot_unknown',
  'upstream_unreachable',
  'rate_limited',
  'advice_boundary',
]);

const CODES = new Set(REFUSAL_CODES);

/** Refusals the reader can retry; the card says so rather than leaving them guessing. */
export const RETRYABLE = Object.freeze(new Set(['upstream_unreachable', 'rate_limited']));

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function renderPayload(payload) {
  const rows = Object.entries(payload ?? {}).filter(
    ([, value]) => value !== null && value !== undefined && String(value).length > 0,
  );
  if (rows.length === 0) return '';
  const items = rows
    .map(
      ([key, value]) =>
        `<div class="strip-row"><dt>${escapeHtml(key)}</dt>` +
        `<dd>${escapeHtml(Array.isArray(value) ? value.join(', ') : value)}</dd></div>`,
    )
    .join('');
  return `<dl class="refusal-payload">${items}</dl>`;
}

/**
 * @param {object} input
 * @param {string} input.code           a member of REFUSAL_CODES
 * @param {string} input.sentence       one human sentence, the reader's answer
 * @param {object} [input.payload]      the mandatory helpful payload
 * @param {string} [input.governingText] provisions co-delivered with the refusal
 * @param {{label: string, href: string}} [input.handoff]
 */
export function renderRefusalCard({ code, sentence, payload, governingText, handoff }) {
  if (!CODES.has(code)) {
    throw new Error(
      `unknown refusal code ${JSON.stringify(code)}; the registry is closed and a code ` +
        'the client cannot name must not be rendered as a generic error',
    );
  }

  if (typeof sentence !== 'string' || sentence.trim().length === 0) {
    throw new Error('a refusal card requires one human sentence');
  }

  const payloadHtml = renderPayload(payload);
  const hasGoverningText = typeof governingText === 'string' && governingText.trim().length > 0;
  const hasHandoff = Boolean(handoff?.label && handoff?.href);

  if (!payloadHtml && !hasGoverningText && !hasHandoff) {
    throw new Error(
      `refusal ${code} carries no payload, no governing text and no handoff; a sterile ` +
        'refusal teaches a reader that honesty equals uselessness',
    );
  }

  if (code === 'advice_boundary' && !hasGoverningText) {
    throw new Error(
      'advice_boundary must co-deliver the governing provisions; refusing the question ' +
        'without delivering the text the reader may still have is the gap this rule closes',
    );
  }

  const retry = RETRYABLE.has(code)
    ? '<p class="refusal-retry">This one is worth retrying.</p>'
    : '';

  const text = hasGoverningText
    ? `<div class="refusal-governing"><h3>The published text, in full</h3>` +
      `<p class="body" lang="fr">${escapeHtml(governingText)}</p></div>`
    : '';

  const foot = hasHandoff
    ? `<p class="refusal-handoff"><a href="${escapeHtml(handoff.href)}">` +
      `${escapeHtml(handoff.label)}</a></p>`
    : '';

  // No role="alert" and no live region. A refusal is an answer, and announcing it as an
  // alert is the aural equivalent of the red toast the spec rules out.
  return (
    '<section class="refusal-card">' +
    '<p class="refusal-head">' +
    mark('--refusal', sentence) +
    `<code class="refusal-code">${escapeHtml(code)}</code>` +
    '</p>' +
    retry +
    payloadHtml +
    text +
    foot +
    '</section>'
  );
}
