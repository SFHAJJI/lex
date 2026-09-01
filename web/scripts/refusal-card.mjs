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
import { quotedLaw } from './localization.mjs';
import { handoffUri } from './routes.mjs';
import { parseObjectUrl } from './urls.mjs';
import { isCalendarDate, requireCalendarDate } from './temporal.mjs';

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
function unspecified() {
  return Object.freeze({
    keys: Object.freeze([]),
    unspecified: true,
    basis:
      'no payload named by 30-FINAL-VERDICT, 31-v3-spec, 33-product-spec or 35-ideal-ux; ' +
      'pending the #348 API contract',
  });
}

/**
 * What this module is, so nothing downstream mistakes it for more.
 *
 * Decision 63 permits this slice only as an explicitly synthetic preview contract or as a
 * consumer of a complete shared validator. It is the first, and it says so here rather than
 * in a comment somebody can skip: nine codes carry payload keys the architect pack names,
 * nine carry none the pack states, and a partial table presented as the final V3 refusal
 * contract would be a client-visible promise nobody made. When #348 freezes the payloads,
 * this becomes a consumer of that validator and this constant goes.
 */
export const CONTRACT_STATUS = Object.freeze({
  kind: 'synthetic-preview',
  final: false,
  reason: 'payloads for nine codes are unfrozen; see issue 348',
});

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
    keys: Object.freeze(['publisher', 'work', 'candidates']),
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

  // advice_boundary's obligation is not payload keys. The fixed template owes the reader
  // the governing text and a named counter, and both are enforced in the renderer below.
  advice_boundary: Object.freeze({
    keys: Object.freeze([]),
    basis:
      '33-product-spec fixed refusal template: the governing text in full plus who can ' +
      'advise you, enforced as governingText and handoff rather than as payload keys',
  }),

  // The remaining nine are declared, not forgotten. The architect pack names no payload for
  // them, so they carry no key requirement yet, and saying so explicitly is the difference
  // between a contract with holes in it and a contract nobody finished writing: a new code
  // cannot be added without deciding which of these two it is, and the day the #348 API
  // contract fixes these payloads, each entry gets its keys and its citation.
  ambiguous_identifier: unspecified(),
  language_not_available: unspecified(),
  text_withheld: unspecified(),
  format_not_available: unspecified(),
  derivation_refused: unspecified(),
  no_corpus_mounted: unspecified(),
  snapshot_unknown: unspecified(),
  upstream_unreachable: unspecified(),
  rate_limited: unspecified(),
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
 * The value shapes a payload may carry.
 *
 * `renderPayload` used to `String()` whatever it was handed, so a nested object arrived on
 * the page as `[object Object]` and an unexpected shape rendered as a plausible-looking
 * nothing. The ten codes whose payload keys the pack does not name still accept their own
 * keys, but not their own shapes: a value is a scalar, or a list of scalars, or one of the
 * structured shapes this module renders itself. Anything else is refused here rather than
 * stringified on screen.
 */
const STRUCTURED_KEYS = new Map([['ambiguous_version', new Set(['candidates'])]]);

function requirePayloadShapes(code, payload) {
  const structured = STRUCTURED_KEYS.get(code) ?? new Set();
  for (const [key, value] of Object.entries(payload ?? {})) {
    // The exemption is bound to the code as well as the key. It used to be keyed only by
    // spelling, so `ambiguous_identifier` with a `candidates` object reached the page as
    // [object Object] by borrowing a name that means something on a different code.
    if (structured.has(key)) continue;
    const values = Array.isArray(value) ? value : [value];
    for (const one of values) {
      const type = typeof one;
      if (one !== null && type !== 'string' && type !== 'number' && type !== 'boolean') {
        throw new Error(
          `payload value ${JSON.stringify(key)} is a ${Array.isArray(value) ? 'list of ' : ''}` +
            `${type}; a refusal payload carries scalars or lists of scalars, because a shape ` +
            'nobody typed reaches the reader as [object Object]',
        );
      }
    }
  }
}

/**
 * `ambiguous_version` is the interstitial that must never default, so each candidate has to
 * be readable on its own terms and its Read link has to lead to the state it names. A link
 * that says one hash and resolves to another is the silent resolution the card exists to
 * prevent, so the two are checked against each other here.
 */
const CANDIDATE_KEYS = new Set(['valid_from', 'hash', 'publication_date', 'href']);

function requireCandidates(payload) {
  const { publisher, work, candidates } = payload;
  if (typeof publisher !== 'string' || typeof work !== 'string') {
    throw new Error(
      'ambiguous_version must name the work being disambiguated; without the publisher and ' +
        'work a candidate can only be checked on its date and hash, and two different ' +
        'instruments can share both',
    );
  }
  if (!Array.isArray(candidates) || candidates.length < 2) {
    throw new Error(
      'ambiguous_version means two or more publisher states cover the date; a candidate ' +
        'list shorter than two does not describe the ambiguity it claims',
    );
  }
  for (const candidate of candidates) {
    for (const key of Object.keys(candidate ?? {})) {
      if (!CANDIDATE_KEYS.has(key)) {
        throw new Error(
          `a candidate carries an undeclared member ${JSON.stringify(key)}; an interstitial ` +
            'that renders fields nobody typed is how a default selection arrives',
        );
      }
    }
    requireCalendarDate(candidate?.valid_from, 'a candidate valid_from');
    requireCalendarDate(candidate?.publication_date, 'a candidate publication_date');
    if (!SHA256.test(candidate?.hash ?? '')) {
      throw new Error(
        'a candidate state is identified by its 64 hex character hash; eight characters on ' +
          'screen are a display truncation, not the identity',
      );
    }
    const target = parseObjectUrl(candidate?.href ?? '');
    if (target?.kind !== 'reading') {
      throw new Error(
        `each candidate needs a reading URL to read it at: ${JSON.stringify(candidate?.href)}`,
      );
    }
    // The complete coordinate, not a date and a hash. A candidate for one work could link
    // to an unrelated publisher and work that happened to share both.
    if (
      target.publisher !== publisher
      || target.work !== work
      || target.validFrom !== candidate.valid_from
      || target.hash !== candidate.hash
    ) {
      throw new Error(
        'a candidate Read link resolves to a different object than the candidate names; a ' +
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
  for (const profile of profiles) {
    if (typeof profile !== 'string' || profile.trim().length === 0) {
      throw new Error(
        `a profile identifier must be a nonempty value: ${JSON.stringify(profile)}`,
      );
    }
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

  if (code === 'ambiguous_version') requireCandidates(payload);
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

const COVERAGE = new Map([
  ['complete_provision', (asOf) => `The governing text in full, as it stood on ${asOf}`],
  ['excerpt', () => 'An excerpt of the governing text'],
]);

function renderGoverningText(governing) {
  const heading = COVERAGE.get(governing?.coverage);
  if (heading === undefined) {
    throw new Error(
      `co-delivered text must declare its coverage as one of ${[...COVERAGE.keys()].join(', ')}; ` +
        'labelling an excerpt as the published text in full is the claim this refusal cannot make',
    );
  }
  if (governing.coverage === 'complete_provision') {
    requireCalendarDate(governing.as_of, 'the co-delivered text as_of');
  }
  return (
    '<div class="refusal-governing">' +
    `<h3>${escapeHtml(heading(governing.as_of))}</h3>` +
    quotedLaw(governing) +
    '</div>'
  );
}

/**
 * @param {object} input
 * @param {string} input.code           a member of REFUSAL_CODES
 * @param {string} input.sentence       one human sentence, the reader's answer
 * @param {object} [input.payload]      the mandatory helpful payload
 * @param {{publisher: string, language: string, text: string}} [input.governingText]
 *        provisions co-delivered with the refusal, carrying the expression's own language
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

  requirePayloadShapes(code, payload);
  requirePayload(code, payload);

  const payloadHtml = renderPayload(code, payload);
  const hasGoverningText = Boolean(governingText);
  const handoffs = (Array.isArray(handoff) ? handoff : handoff ? [handoff] : []).filter(
    (one) => one?.label && one?.href,
  );
  const hasHandoff = handoffs.length > 0;

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

  // Decision 41 settles this boundary and settles its ending: "Who can advise you on your
  // case: the Chambre des salariés, the ITM, the Service d'accueil et d'information
  // juridique, or a lawyer." That is a referral list, not one counter, and a citizen handed
  // a single name has been handed the one that happens to be nearest to whoever wrote the
  // caller. Two or more, each reachable.
  if (code === 'advice_boundary' && handoffs.length < 2) {
    throw new Error(
      'advice_boundary must name the referral list, not one counter; Decision 41 settles it ' +
        'as several named services and a lawyer, and one arbitrary counter is not that list',
    );
  }

  const retry = RETRYABLE.has(code)
    ? '<p class="refusal-retry">This one is worth retrying.</p>'
    : '';

  const note = MANDATED_NOTE[code]
    ? `<p class="refusal-note">${escapeHtml(MANDATED_NOTE[code])}</p>`
    : '';

  // The quotation carries the expression's own language. Hardcoding `lang="fr"` mislabels
  // every EU expression and every one of the handful of non-French LU renderings, and a
  // screen reader then reads English law in a French voice.
  // The heading used to say "The published text, in full" over whatever text a caller
  // supplied. Completeness is a claim about the publisher's record, so the caller has to
  // make it explicitly and date it, and an excerpt says so.
  const text = hasGoverningText ? renderGoverningText(governingText) : '';

  // Validated, not merely escaped. `javascript:alert(1)` escapes to a perfectly safe
  // attribute value and remains a working link.
  const foot = hasHandoff
    ? '<ul class="refusal-handoff">' +
      handoffs
        .map(
          (one) =>
            `<li><a href="${escapeHtml(handoffUri(one.href))}">${escapeHtml(one.label)}</a></li>`,
        )
        .join('') +
      '</ul>'
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
