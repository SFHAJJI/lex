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
 * One line of this comment used to say that the live service uses `identifier_unknown` where the
 * UX spec's prose says `unknown_work`. That was wrong about the service. Measured, not inferred:
 * `provenance` for `eu-eurlex:32016R0679:2018-05-25` against production returns
 * `{"status": "unknown_work"}`.
 *
 * The registry stays closed at these nineteen, because the product spec is the versioned registry
 * and a wire status is not a product code. `unknown_work` is translated where it arrives, by a
 * closed table in `provenance.mjs`, and that translation deliberately does not let the sentence
 * claim more than the status supports: the service returns `unknown_work` both for an identifier
 * no work matches and for a work held at other dates but not the one asked for, so the reader is
 * told that rather than told the work does not exist.
 *
 * `unknown_anchor` remains the UX spec's informal name for `anchor_not_in_version`, which the
 * product spec governs and which the service has not contradicted.
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

/**
 * What could turn this absence into an answer.
 *
 * Not invented here: this is the closed vocabulary of the shipped contract, in
 * `schemas/v3-synthetic-preview/synthetic-resolve-envelope.schema.json`, where the array is
 * required, non-empty, unique and in declared enum order. The order matters because a client
 * comparing two refusals byte for byte cannot do so if the same set arrives in two orders.
 */
export const WHAT_WOULD_ANSWER = Object.freeze([
  'corrected_identifier',
  'new_official_observation',
  'expanded_official_scope',
]);

const WHAT_WOULD_ANSWER_LABEL = new Map([
  ['corrected_identifier', 'a corrected identifier, if you meant a different instrument'],
  ['new_official_observation', 'a new observation, if the publisher publishes this'],
  ['expanded_official_scope', 'an expansion of the reviewed corpus to this class of act'],
]);

/**
 * The codes where a reader can reasonably conclude that the law does not exist.
 *
 * The contract requires `what_would_answer` and `asserts_absence_of_law` on every refusal.
 * These are the ones where getting it wrong changes what somebody believes about the law
 * rather than about this service, so the card enforces both here and says so on the page.
 */
export const ABSENCE_CODES = Object.freeze([
  'identifier_unknown',
  'out_of_corpus_scope',
  'no_version_for_date',
  'anchor_not_in_version',
  'text_not_available',
]);

const ABSENCES = new Set(ABSENCE_CODES);

function requireAbsenceEvidence(code, payload) {
  if (!ABSENCES.has(code)) return;

  // Own properties, the same accessor the payload contract uses. These two were the half of
  // the repair I missed: a payload owning only its declared key while inheriting both absence
  // fields validated here and then rendered, because the renderer reads own properties and
  // this did not. One rule, two readers, disagreeing about what the payload contains.
  const routes = own(payload, 'what_would_answer');
  if (!Array.isArray(routes) || routes.length === 0) {
    throw new Error(
      `${code} is an absence, so it must say what would answer it; the closed vocabulary is ` +
        `${WHAT_WOULD_ANSWER.join(', ')} and an absence with no route out is a dead end`,
    );
  }
  for (const route of routes) {
    if (!WHAT_WOULD_ANSWER_LABEL.has(route)) {
      throw new Error(
        `${JSON.stringify(route)} is not in the what_would_answer vocabulary; the contract ` +
          `closes it at ${WHAT_WOULD_ANSWER.join(', ')}`,
      );
    }
  }
  if (new Set(routes).size !== routes.length) {
    throw new Error(`${code} repeats a what_would_answer value`);
  }
  // Declared enum order, as the schema requires. Two refusals with the same set must be the
  // same bytes, or a client cannot compare them.
  const ordered = WHAT_WOULD_ANSWER.filter((one) => routes.includes(one));
  if (ordered.join() !== routes.join()) {
    throw new Error(
      `what_would_answer must be in declared enum order, ${ordered.join(', ')}, not ` +
        `${routes.join(', ')}; the same set in two orders is two different responses`,
    );
  }

  // The contract pins this to false. The invariant it protects is the product's oldest:
  // absence of a record is never absence of law.
  if (own(payload, 'asserts_absence_of_law') !== false) {
    throw new Error(
      `${code} must carry asserts_absence_of_law: false; the contract pins it to that constant ` +
        'because absence of a held record is never evidence that the law does not exist',
    );
  }
}

/**
 * The sentence an absence refusal cannot appear without.
 *
 * Named, because it is the product's oldest invariant said out loud, and a sentence written
 * as a literal in two renderers is a sentence that gets corrected in one of them.
 */
export const ABSENCE_NOTE =
  'This is what this service holds, and does not hold. It is not evidence that the instrument '
  + 'or the law does not exist.';

/** The heading over the routes that would answer an absence. */
export const ABSENCE_HEADING = 'What would answer this';

/**
 * What an absence refusal discloses, as claims rather than markup, or null when the code is
 * not an absence. Both renderers call this, so neither can drop the note that makes the
 * absence readable.
 */
export function absenceEvidenceParts(code, payload) {
  if (!ABSENCES.has(code)) return null;
  return Object.freeze({
    note: ABSENCE_NOTE,
    heading: ABSENCE_HEADING,
    routes: Object.freeze(
      payload.what_would_answer.map((route) => WHAT_WOULD_ANSWER_LABEL.get(route)),
    ),
  });
}

function renderAbsenceEvidence(code, payload) {
  const absence = absenceEvidenceParts(code, payload);
  if (absence === null) return '';
  const items = absence.routes.map((label) => `<li>${escapeHtml(label)}</li>`).join('');
  return (
    '<div class="refusal-absence">' +
    `<p class="refusal-absence-note">${escapeHtml(absence.note)}</p>` +
    `<h3>${escapeHtml(absence.heading)}</h3><ul>${items}</ul></div>`
  );
}

/** Refusals the reader can retry; the card says so rather than leaving them guessing. */
export const RETRYABLE = Object.freeze(new Set(['upstream_unreachable', 'rate_limited']));

/**
 * What a retryable refusal says.
 *
 * Exported so the two renderers cannot drift on it. A sentence duplicated as a literal in two
 * files is a sentence that gets corrected in one of them.
 */
export const RETRY_SENTENCE = 'This one is worth retrying.';

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

/**
 * Read a declared payload member, own properties only.
 *
 * `requirePayload` and `requireCandidates` read `payload[key]`, which walks the prototype
 * chain; the allowlist and the renderer use `Object.keys`, which does not. So an inherited
 * `candidates` array validated and then vanished, producing an ambiguity card offering no
 * choice at all: the one refusal whose entire purpose is to make the reader choose.
 *
 * Validation and rendering have to be looking at the same object, so both look here.
 */
function own(payload, key) {
  return Object.hasOwn(payload ?? {}, key) ? payload[key] : undefined;
}

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
const CANDIDATE_KEYS = new Set(['valid_from', 'hash', 'publication_date', 'href', 'withdrawn']);

/**
 * One coordinate check for every state a reader can be offered.
 *
 * This existed twice, in the interstitial and in the superseded-sibling renderer, and the
 * second copy was written without the anchor comparison, so the same defect came back in the
 * component added to fix the first one. A rule that has to be repaired in two places is a
 * rule that will be repaired in one.
 */
function requireStateCoordinate(candidate, publisher, work, what) {
  requireCalendarDate(candidate?.valid_from, `${what} valid_from`);
  requireCalendarDate(candidate?.publication_date, `${what} publication_date`);
  if (!SHA256.test(candidate?.hash ?? '')) {
    throw new Error(
      `${what} is identified by its 64 hex character hash; eight characters on screen are a ` +
        'display truncation, not the identity',
    );
  }
  const target = parseObjectUrl(candidate?.href ?? '');
  if (target?.kind !== 'reading') {
    throw new Error(`${what} needs a reading URL to read it at: ${JSON.stringify(candidate?.href)}`);
  }
  // The whole coordinate, anchor included. Two states that agree on publisher, work, date
  // and hash but differ in anchor lead to different provisions.
  if (
    target.publisher !== publisher
    || target.work !== work
    || target.validFrom !== candidate.valid_from
    || target.hash !== candidate.hash
    || target.anchor !== null
  ) {
    throw new Error(
      `${what} link resolves to a different object than the state names; a link that ` +
        'disagrees with its own label resolves the ambiguity silently',
    );
  }
}

function requireCandidates(payload) {
  const publisher = own(payload, 'publisher');
  const work = own(payload, 'work');
  const candidates = own(payload, 'candidates');
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
  // 30-FINAL-VERDICT splits this population per attack 4.4: a live ambiguity, where the
  // publisher ranks two states that both stand, gets the interstitial; a withdrawn-superseded
  // pair does not, because there the publisher has ranked them and the right answer is the
  // live state with the withdrawn sibling disclosed. The census that produced the original
  // requirement conflated two populations with opposite correct behaviours, so this card
  // refuses the second rather than forcing a choice the publisher already made.
  // Declared first, so an undeclared withdrawal is named as one rather than reported as a
  // withdrawal. Behind the live-count rules it was unreachable, and an unreachable guard is
  // a guard that will be deleted by somebody who notices it never fires.
  const undeclared = candidates.filter(
    (candidate) => typeof candidate?.withdrawn !== 'boolean',
  );
  if (undeclared.length > 0) {
    throw new Error(
      `${undeclared.length} candidate must declare whether the publisher withdrew it; an ` +
        'undeclared withdrawal is how a superseded state gets offered as a live choice',
    );
  }

  const live = candidates.filter((candidate) => candidate?.withdrawn === false);
  if (live.length < 2) {
    throw new Error(
      `${live.length} of these ${candidates.length} states is live, so this is not the live ` +
        'ambiguity the interstitial is for; a withdrawn-superseded pair is rendered by ' +
        'renderSupersededState, which shows the live state and discloses the withdrawn sibling',
    );
  }
  // Every rendered candidate, not merely two of them. Counting live candidates and then
  // rendering all of them let a withdrawn state be offered as a selectable choice inside a
  // set that happened to contain two live ones, which is the conflation the split removes.
  // An ambiguity is a choice between different states. The same state offered twice satisfies a
  // count of two and gives a reader nothing to choose between, which is the one thing this
  // interstitial exists to make them do.
  const identities = live.map((one) => `${one.valid_from}--${one.hash}`);
  if (new Set(identities).size !== identities.length) {
    throw new Error(
      'this ambiguity offers the same state more than once; two entries with one identity are ' +
        'not a choice, and the interstitial exists to make a reader choose',
    );
  }

  if (live.length !== candidates.length) {
    throw new Error(
      `${candidates.length - live.length} of these candidates is withdrawn; the interstitial ` +
        'offers a choice, so every state in it must be one the publisher still holds, and a ' +
        'withdrawn sibling is disclosed by renderSupersededState rather than offered here',
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
    requireStateCoordinate(candidate, publisher, work, 'a candidate');
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

/**
 * Keys whose declared value may legitimately be `null`.
 *
 * The pack's own worked example broke this card. Asking for loi-1915 at 2010-01-01 refuses
 * with `no_version_for_date`, and the publisher's history begins in 2017: there is no nearest
 * earlier state, and there cannot be one. Requiring all three keys to be present left a
 * caller two ways out, and both were worse than the refusal. Omitting the key hit the same
 * error, and inventing a date put a state in the publisher's history that the publisher does
 * not have.
 *
 * So the rule is declaration, not presence. The key must be there; `null` is a legitimate
 * declared value meaning there is none, and the card says so in words. What stays refused is
 * the key being absent, because then a reader cannot tell "there is no earlier state" from
 * "nobody looked".
 */
const NULLABLE_KEYS = new Map([['no_version_for_date', new Set(['nearest_earlier', 'nearest_later'])]]);

/** Keys whose value is a date, so the card can tell a state from a sentence about one. */
const DATED_KEYS = new Map([
  ['no_version_for_date', new Set(['history_begins', 'nearest_earlier', 'nearest_later'])],
]);

const NULL_SENTENCE = new Map([
  ['nearest_earlier', 'No earlier state is held: the requested date precedes this history.'],
  ['nearest_later', 'No later state is held: the requested date follows every state held.'],
]);

function requirePayload(code, payload) {
  const requirement = REQUIRED_PAYLOAD[code];
  if (!requirement) return;

  const nullable = NULLABLE_KEYS.get(code) ?? new Set();

  const undeclared = requirement.keys.filter(
    (key) => nullable.has(key) && !Object.hasOwn(payload ?? {}, key),
  );
  if (undeclared.length > 0) {
    throw new Error(
      `refusal ${code} must declare ${undeclared.join(', ')} even when there is none; an ` +
        'absent key cannot be told apart from a state nobody looked for',
    );
  }

  // Declared but blank is neither a state nor a declaration that there is none. Leaving it
  // through would drop the row from the card and put the reader back where an absent key did.
  const blank = requirement.keys.filter(
    (key) => nullable.has(key) && own(payload, key) !== null && !isPresent(own(payload, key)),
  );
  if (blank.length > 0) {
    throw new Error(
      `refusal ${code} declares ${blank.join(', ')} blank; the value is a held state or null ` +
        'for none, and a blank is a third thing that renders as nothing at all',
    );
  }

  // Adding `null` gave the caller a machine-readable way to say none. It did not take away
  // the free-text one, and "none held" in a date field was the whole defect: the caller writes
  // the words, so one fact has as many renderings as it has callers. A declared nearest state
  // is a calendar date or it is null, and nothing else is either.
  const dated = DATED_KEYS.get(code) ?? new Set();
  const prose = [...dated].filter((key) => {
    const value = own(payload, key);
    if (value === null || value === undefined) return false;
    return !isCalendarDate(value);
  });
  if (prose.length > 0) {
    throw new Error(
      `refusal ${code} carries ${prose.join(', ')} as prose rather than a calendar date; a ` +
        'sentinel a caller writes has as many spellings as it has callers, and only one of ' +
        'them survives a byte comparison',
    );
  }

  const missing = requirement.keys.filter(
    (key) => !nullable.has(key) && !isPresent(own(payload, key)),
  );
  if (missing.length > 0) {
    throw new Error(`refusal ${code} must carry ${missing.join(', ')}; ${requirement.basis}`);
  }

  // Declared null on both sides contradicts the history this same payload asserts: if a
  // history begins, some state lies on one side of any date in it.
  // A nearest earlier state dated after the nearest later one describes a history that cannot
  // exist. The shapes were checked and the order never was, so the card could render a
  // chronology the publisher does not have.
  if (code === 'no_version_for_date') {
    const begins = own(payload, 'history_begins');
    const earlier = own(payload, 'nearest_earlier');
    const later = own(payload, 'nearest_later');
    if (earlier !== null && later !== null && earlier !== undefined && later !== undefined &&
        !(earlier < later)) {
      throw new Error(
        `nearest_earlier ${earlier} does not precede nearest_later ${later}; a nearest earlier ` +
          'state dated after the nearest later one is a history that cannot exist',
      );
    }
    if (typeof begins === 'string' && earlier !== null && earlier !== undefined &&
        earlier < begins) {
      throw new Error(
        `nearest_earlier ${earlier} precedes history_begins ${begins}; a state cannot be held ` +
          'before the history it belongs to starts',
      );
    }
  }

  const declaredNull = [...nullable].filter((key) => own(payload, key) === null);
  if (nullable.size > 0 && declaredNull.length === nullable.size) {
    throw new Error(
      `refusal ${code} declares no nearest state in either direction while also carrying ` +
        'history_begins; if a history begins, some held state lies on one side of the date',
    );
  }

  // A declared key set is an allowlist, not a minimum. It was a minimum, and
  // `ambiguous_version` therefore accepted and rendered `selected: true`, which is precisely
  // the default this refusal exists to refuse. The nine variants Decision 63 defers stay
  // open, because closing a set nobody has specified would be inventing the contract.
  if (requirement.keys.length > 0) {
    const allowed = new Set([
      ...requirement.keys,
      ...(ABSENCES.has(code) ? ['what_would_answer', 'asserts_absence_of_law'] : []),
    ]);
    const undeclared = Object.keys(payload ?? {}).filter((key) => !allowed.has(key));
    if (undeclared.length > 0) {
      throw new Error(
        `refusal ${code} carries undeclared payload ${undeclared.join(', ')}; its contract is ` +
          `${[...allowed].join(', ')}, and a field nobody declared is a field nobody checked`,
      );
    }
  }

  if (code === 'ambiguous_version') requireCandidates(payload);
  if (code === 'profiles_differ') requireProfiles(own(payload, 'profiles'));
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

/**
 * The payload as the things it shows rather than as markup: the candidate list, the anchor
 * chips, and the labelled rows.
 *
 * Which keys appear and what a declared null says are rules, so they live here and both
 * renderers call them. A row dropped in one renderer and kept in the other is a refusal that
 * hands two readers different amounts of help.
 */
export function payloadParts(code, payload) {
  const nullable = NULLABLE_KEYS.get(code) ?? new Set();
  // A declared null renders as its sentence rather than being filtered away. Dropping the row
  // would put the reader back where the missing key left them.
  const declaredNull = [...nullable].filter(
    (key) => Object.hasOwn(payload ?? {}, key) && payload[key] === null,
  );
  const entries = Object.entries(payload ?? {}).filter(
    ([key, value]) => isPresent(value) || declaredNull.includes(key),
  );

  const structured = [];
  const rows = [];

  for (const [key, value] of entries) {
    if (code === 'ambiguous_version' && key === 'candidates') {
      structured.push({ kind: 'candidates', key, values: value });
    } else if (key === 'nearest_anchors' && Array.isArray(value)) {
      structured.push({ kind: 'chips', key, className: 'refusal-anchors', values: value });
    } else if (value === null) {
      rows.push({ key, value: NULL_SENTENCE.get(key) });
    } else {
      // Text, not the raw value. `asserts_absence_of_law: false` is the one payload field
      // whose whole job is to be read, and a component tree renders a boolean `false` as
      // nothing at all: the row survived with an empty cell, which reads as a field the
      // service declined to answer. The string surface stringified it on the way out and so
      // never showed the defect, which is exactly why the conversion belongs here.
      rows.push({ key, value: Array.isArray(value) ? value.join(', ') : String(value) });
    }
  }

  return Object.freeze({ structured: Object.freeze(structured), rows: Object.freeze(rows) });
}

function renderPayload(code, payload) {
  const parts = payloadParts(code, payload);
  if (parts.structured.length === 0 && parts.rows.length === 0) return '';

  const structured = parts.structured
    .map((item) => (item.kind === 'candidates'
      ? renderCandidates(item.values)
      : renderChips(item.className, item.values)))
    .join('');

  const list = parts.rows.length > 0
    ? '<dl class="refusal-payload">' +
      parts.rows
        .map(
          ({ key, value }) =>
            `<div class="strip-row"><dt>${escapeHtml(key)}</dt>` +
            `<dd>${escapeHtml(value)}</dd></div>`,
        )
        .join('') +
      '</dl>'
    : '';

  return structured + list;
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
 * @param {object} [input.governingText] provisions co-delivered with the refusal, carrying
 *        the resource identity, its authenticity evidence and the expression's own language
 * @param {{label: string, href: string}} [input.handoff]
 */
/**
 * Every rule a refusal card must satisfy, decided once and shared.
 *
 * Split out so the React runtime cannot become a second place where a legal rule lives. The
 * component calls this and renders what it returns; it re-derives nothing. If a rule is wrong it
 * is wrong in one file, and a fix cannot land in the string renderer while the React one keeps
 * the defect, which is the failure mode a parallel implementation invites.
 *
 * Returns the normalised inputs a renderer needs. Throws on anything a card must not display.
 */
export function validateRefusal({ code, sentence, payload, governingText, handoff }) {
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
  requireAbsenceEvidence(code, payload);

  const payloadHtml = renderPayload(code, payload);
  const hasGoverningText = Boolean(governingText);
  const handoffs = (Array.isArray(handoff) ? handoff : handoff ? [handoff] : []).filter(
    (one) => one?.label && one?.href,
  );

  if (!payloadHtml && !hasGoverningText && handoffs.length === 0) {
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

  // Decision 41 settles this boundary and settles its ending: a referral list, not one
  // counter. A citizen handed a single name has been handed whichever one happened to be
  // nearest to whoever wrote the caller.
  if (code === 'advice_boundary' && handoffs.length < 2) {
    throw new Error(
      'advice_boundary must name the referral list, not one counter; Decision 41 settles it ' +
        'as several named services and a lawyer, and one arbitrary counter is not that list',
    );
  }

  return {
    code,
    sentence,
    payload,
    payloadHtml,
    // The same payload and absence disclosure, as claims a renderer can lay out itself. The
    // HTML form above is what the string surface emits and what the sterility check counts;
    // these two are what a component tree renders, from the same decisions.
    payloadParts: payloadParts(code, payload),
    absence: absenceEvidenceParts(code, payload),
    governingText: hasGoverningText ? governingText : null,
    handoffs,
    retryable: RETRYABLE.has(code),
    note: MANDATED_NOTE[code] ?? null,
  };
}

export function renderRefusalCard({ code, sentence, payload, governingText, handoff }) {
  // Every rule lives in validateRefusal and is applied once. This function decides only how
  // the validated result looks, which is what lets the React runtime share the rules rather
  // than reimplement them beside a copy that can drift.
  const card = validateRefusal({ code, sentence, payload, governingText, handoff });
  const payloadHtml = card.payloadHtml;
  const hasGoverningText = card.governingText !== null;
  const handoffs = card.handoffs;
  const hasHandoff = handoffs.length > 0;

  const retry = RETRYABLE.has(code)
    ? `<p class="refusal-retry">${escapeHtml(RETRY_SENTENCE)}</p>`
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
    renderAbsenceEvidence(code, payload) +
    payloadHtml +
    text +
    foot +
    '</section>'
  );
}

/**
 * The other half of the split: a state the publisher superseded, and the live state that
 * replaced it.
 *
 * This is not a refusal and does not use the refusal card. The publisher has ranked these,
 * so forcing a reader to choose would invent an ambiguity that the record does not contain.
 * The live state is the answer; the withdrawn sibling is disclosed, because a reader who
 * followed an old link needs to know their state exists and was superseded rather than
 * finding it silently gone.
 */

/**
 * The sentence a superseded-state disclosure cannot appear without.
 *
 * Fixed, and named, because it is what makes the two links readable: the publisher ranked
 * these states, so nothing is being asked of the reader, and the withdrawn one stays
 * addressable because a link somebody already holds should not lead nowhere.
 */
export const SUPERSEDED_NOTE =
  'The publisher withdrew the state below and replaced it. This is the publisher ranking '
  + 'them, not this interface choosing, so no choice is asked of you. The withdrawn state is '
  + 'still addressable, because a link to it should not lead nowhere.';

/**
 * The two coordinates a superseded-state disclosure shows, both checked against the work.
 *
 * The rules live here so the string renderer and the React component apply one implementation
 * rather than two that can drift apart.
 */
export function validateSupersededState({ publisher, work, live, withdrawn }) {
  // A state cannot have superseded itself. The live one was checked for not being withdrawn and
  // the siblings for being withdrawn, and nothing checked they were different states, so one
  // record passed as both would render as its own replacement.
  if (typeof publisher !== 'string' || typeof work !== 'string') {
    throw new Error('a superseded-state disclosure must name the work it is about');
  }
  if (live?.withdrawn !== false) {
    throw new Error('the live state of a superseded pair must be the one that is not withdrawn');
  }
  const liveIdentity = `${live.valid_from}--${live.hash}`;
  if (Array.isArray(withdrawn) && withdrawn.some((one) => `${one?.valid_from}--${one?.hash}` === liveIdentity)) {
    throw new Error(
      'a state cannot have superseded itself; the same record appears as both the live state ' +
        'and one it replaced',
    );
  }
  if (!Array.isArray(withdrawn) || withdrawn.length === 0) {
    throw new Error(
      'a superseded-state disclosure exists to disclose a withdrawn sibling; with none, the ' +
        'live state is simply the state and needs no disclosure',
    );
  }
  if (!withdrawn.every((one) => one?.withdrawn === true)) {
    throw new Error('a disclosed sibling must be one the publisher withdrew');
  }
  for (const candidate of [live, ...withdrawn]) {
    requireStateCoordinate(candidate, publisher, work, 'a state');
  }

  // Eight characters are what a reader can hold in their eye, so the display truncation is
  // computed once here and carried beside the whole hash rather than sliced at each mention.
  // The identity is the whole hash, which is why it was checked above and is not this.
  const shown = (one) => Object.freeze({ ...one, short_hash: one.hash.slice(0, 8) });
  return Object.freeze({ live: shown(live), withdrawn: Object.freeze(withdrawn.map(shown)) });
}

/**
 * The disclosure a withdrawn state cannot be read without.
 *
 * @see validateSupersededState, which holds every rule this renders.
 */
export function renderSupersededState({ publisher, work, live, withdrawn }) {
  const pair = validateSupersededState({ publisher, work, live, withdrawn });

  const siblings = pair.withdrawn
    .map(
      (one) =>
        `<li><a href="${escapeHtml(one.href)}">applicable from ${escapeHtml(one.valid_from)}, ` +
        `hash <code>${escapeHtml(one.short_hash)}</code>, published ` +
        `${escapeHtml(one.publication_date)}</a></li>`,
    )
    .join('');

  return (
    '<section class="superseded-state">' +
    `<p class="superseded-live"><a href="${escapeHtml(pair.live.href)}">The state the publisher ` +
    `holds, applicable from ${escapeHtml(pair.live.valid_from)}, hash ` +
    `<code>${escapeHtml(pair.live.short_hash)}</code></a></p>` +
    `<p class="superseded-note">${escapeHtml(SUPERSEDED_NOTE)}</p>` +
    `<ul class="superseded-siblings">${siblings}</ul>` +
    '</section>'
  );
}
