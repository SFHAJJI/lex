// The RefusalCard of UX spec section 1, over the closed refusal registry of product spec
// section 4.9.
//
// Three rules are enforced by construction rather than by review.
//
// First, the registry is closed. An unknown code is refused here rather than rendered as a
// generic error, because a refusal the client does not recognise is exactly the case where a
// reader most needs to be told the truth about what happened.
//
// Second, and this is the one the specs call the most consequential UX gap: a refusal is
// never sterile. v3-spec item 4 says the boundary refusal always co-delivers the governing
// provisions, on the ground that a bare refusal trains users that honesty equals
// uselessness. So a card cannot be constructed with nothing in it. It must carry a helpful
// payload, or co-delivered governing text, or a handoff.
//
// Third, "each with mandatory helpful payload" (v3-spec section 4.9) is not one rule, it is
// nine. The specs name what several codes must carry, and a generic non-empty payload
// satisfies none of them: `{work: "loi-2006-07-31-n2"}` is non-empty and tells a reader
// nothing they did not already type. REQUIRED_PAYLOAD below turns each named requirement
// into a construction-time refusal, and every entry cites the line it came from, so a
// requirement can be checked against the spec rather than against my memory of it. Codes the
// specs do not pin down keep the generic rule; they are not given invented requirements.
//
// A refusal is styled as an answer: neutral ground, shield icon, no alert role. It is not an
// error toast, and the components here give a caller no way to make it one.

import { mark } from './design-tokens.mjs';
import { parseObjectUrl } from './urls.mjs';

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

/**
 * The payload each code must carry, and where the requirement comes from.
 *
 * Only codes the architect pack actually pins down appear here. `basis` is quoted closely
 * enough that a reviewer can grep the named file for it.
 */
export const REQUIRED_PAYLOAD = Object.freeze({
  no_version_for_date: Object.freeze({
    keys: Object.freeze(['history_begins', 'nearest_earlier', 'nearest_later']),
    basis:
      '33-product-spec: "no_version_for_date carries history_begins, nearest_earlier, ' +
      'nearest_later"',
  }),
  anchor_not_in_version: Object.freeze({
    keys: Object.freeze(['nearest_anchors']),
    basis:
      '31-v3-spec: "anchor_not_in_version (with nearest_anchors and the do-not-fall-back note)"',
  }),
  ambiguous_version: Object.freeze({
    keys: Object.freeze(['candidates']),
    basis:
      '35-ideal-ux: "listing each candidate as applicable from {date}, hash {8 hex}, ' +
      'published {date} ... there is no default selection"',
  }),
  profiles_differ: Object.freeze({
    keys: Object.freeze(['profiles']),
    basis:
      '33-product-spec: "profiles_differ refusal across extraction profiles, never overridable"',
  }),
  not_transposable: Object.freeze({
    keys: Object.freeze(['execution_acts']),
    basis:
      '31-v3-spec: "Regulation view: not_transposable explainer plus execution acts ' +
      'reachable via citations, labeled as citations"',
  }),
  text_not_available: Object.freeze({
    keys: Object.freeze(['official_uri', 'gazette_chain']),
    basis: '31-v3-spec: "text_not_available (metadata + official link + gazette chain)"',
  }),
  retrieval_mode_unavailable: Object.freeze({
    keys: Object.freeze(['fallback_mode']),
    basis: '31-v3-spec: "retrieval_mode_unavailable falls back visibly to keyword"',
  }),
  identifier_unknown: Object.freeze({
    keys: Object.freeze(['population_disclosure']),
    basis:
      '35-ideal-ux: "the card offers the resolver ... and the out-of-corpus explanation ' +
      'with the population disclosure"',
  }),
  out_of_corpus_scope: Object.freeze({
    keys: Object.freeze(['population_disclosure']),
    basis: '35-ideal-ux: the same population disclosure as the unresolved identifier',
  }),
});

/**
 * Notes the component writes itself, because they are contract rules rather than data. A
 * caller who had to remember to pass them is a caller who will eventually not.
 */
const MANDATED_NOTE = Object.freeze({
  anchor_not_in_version:
    'Lex does not fall back to full-text search for a provision of a known work. A ' +
    'different provision is not a near miss.',
  ambiguous_version:
    'The publisher ranks neither state. There is no default and no remembered choice.',
  profiles_differ:
    'This refusal is not overridable. The two states were extracted by different profiles, ' +
    'so a difference between them would report parser disagreement as legislation.',
});

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;
const SHA256 = /^[0-9a-f]{64}$/;

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function isPresent(value) {
  if (value === null || value === undefined) return false;
  if (Array.isArray(value)) return value.length > 0;
  if (typeof value === 'object') return Object.keys(value).length > 0;
  return String(value).trim().length > 0;
}

/**
 * `ambiguous_version` is the interstitial that must never default, so each candidate has to
 * be readable on its own terms and its Read link has to lead to the state it names. A link
 * that says one hash and resolves to another is the silent resolution the card exists to
 * prevent, so the two are checked against each other here.
 */
function requireCandidates(candidates) {
  if (!Array.isArray(candidates) || candidates.length < 2) {
    throw new Error(
      'ambiguous_version means two or more publisher states cover the date; a candidate ' +
        'list shorter than two does not describe the ambiguity it claims',
    );
  }
  for (const candidate of candidates) {
    if (!ISO_DATE.test(candidate?.valid_from ?? '')) {
      throw new Error(`a candidate state needs an ISO valid_from: ${JSON.stringify(candidate)}`);
    }
    if (!ISO_DATE.test(candidate?.publication_date ?? '')) {
      throw new Error(`a candidate state needs its publication_date: ${JSON.stringify(candidate)}`);
    }
    if (!SHA256.test(candidate?.hash ?? '')) {
      throw new Error(
        'a candidate state is identified by its 64 hex character hash; eight characters on ' +
          'screen are a display truncation, not the identity',
      );
    }
    if (candidate.selected === true || candidate.default === true) {
      throw new Error(
        'the ambiguous_version interstitial has no default selection; the publisher ranks ' +
          'neither state and neither may the interface',
      );
    }
    const target = parseObjectUrl(candidate?.href ?? '');
    if (target?.kind !== 'reading') {
      throw new Error(
        `each candidate needs a reading URL to read it at: ${JSON.stringify(candidate?.href)}`,
      );
    }
    if (target.hash !== candidate.hash || target.validFrom !== candidate.valid_from) {
      throw new Error(
        'a candidate Read link resolves to a different state than the candidate names; a ' +
          'link that disagrees with its own label resolves the ambiguity silently',
      );
    }
  }
}

function requireProfiles(profiles) {
  if (!Array.isArray(profiles) || profiles.length !== 2) {
    throw new Error(
      'profiles_differ names both profiles; a refusal that does not say which two profiles ' +
        'disagreed cannot be checked by the reader',
    );
  }
  if (profiles[0] === profiles[1]) {
    throw new Error('profiles_differ was raised with one profile named twice');
  }
}

function requirePayload(code, payload) {
  const requirement = REQUIRED_PAYLOAD[code];
  if (!requirement) return;

  const missing = requirement.keys.filter((key) => !isPresent(payload?.[key]));
  if (missing.length > 0) {
    throw new Error(`refusal ${code} must carry ${missing.join(', ')}; ${requirement.basis}`);
  }

  if (code === 'ambiguous_version') requireCandidates(payload.candidates);
  if (code === 'profiles_differ') requireProfiles(payload.profiles);
}

function renderCandidates(candidates) {
  const items = candidates
    .map(
      (candidate) =>
        '<li class="refusal-candidate">' +
        `<a href="${escapeHtml(candidate.href)}">applicable from ` +
        `${escapeHtml(candidate.valid_from)}, hash ` +
        `<code>${escapeHtml(candidate.hash.slice(0, 8))}</code>, published ` +
        `${escapeHtml(candidate.publication_date)}</a></li>`,
    )
    .join('');
  return `<ul class="refusal-candidates">${items}</ul>`;
}

function renderChips(className, values) {
  const items = values.map((value) => `<li><code>${escapeHtml(value)}</code></li>`).join('');
  return `<ul class="${className}">${items}</ul>`;
}

function renderPayload(code, payload) {
  const entries = Object.entries(payload ?? {}).filter(([, value]) => isPresent(value));
  if (entries.length === 0) return '';

  const structured = [];
  const rows = [];

  for (const [key, value] of entries) {
    if (code === 'ambiguous_version' && key === 'candidates') {
      structured.push(renderCandidates(value));
    } else if (key === 'nearest_anchors' && Array.isArray(value)) {
      structured.push(renderChips('refusal-anchors', value));
    } else {
      rows.push(
        `<div class="strip-row"><dt>${escapeHtml(key)}</dt>` +
          `<dd>${escapeHtml(Array.isArray(value) ? value.join(', ') : value)}</dd></div>`,
      );
    }
  }

  const list = rows.length > 0 ? `<dl class="refusal-payload">${rows.join('')}</dl>` : '';
  return structured.join('') + list;
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

  requirePayload(code, payload);

  const payloadHtml = renderPayload(code, payload);
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

  // The fixed advice template ends "and here is who can advise you". A list of acronyms in
  // the payload is not that: a citizen who cannot reach the counter has not been handed off.
  if (code === 'advice_boundary' && !hasHandoff) {
    throw new Error(
      'advice_boundary must name a reachable human counter with a label and a link; the ' +
        'fixed template promises to say who can advise you, and an acronym is not a counter',
    );
  }

  const retry = RETRYABLE.has(code)
    ? '<p class="refusal-retry">This one is worth retrying.</p>'
    : '';

  const note = MANDATED_NOTE[code]
    ? `<p class="refusal-note">${escapeHtml(MANDATED_NOTE[code])}</p>`
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
    note +
    payloadHtml +
    text +
    foot +
    '</section>'
  );
}
