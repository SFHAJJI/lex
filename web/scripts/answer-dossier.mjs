// Claims as lines with evidence chips, and the operations trace under them.
//
// Product spec section "Code decides; the model phrases" states the rule this module exists
// to make structural: every declarative sentence must bind to at least one tool-result fact
// in a `claims[]` array, and unbindable sentences are not emitted. The model phrases; it does
// not get to assert.
//
// So an unbound claim is not rendered without its evidence, it is refused. There is no
// parameter for a sentence with nothing behind it, which means the failure mode this
// prevents, a fluent paragraph with one unsupported clause in the middle, cannot be
// expressed rather than being caught in review. A reader cannot tell a bound sentence from
// an unbound one by reading it, which is exactly why the check cannot live with the reader.
//
// The operations trace is the other half. A claim binds to a result, and the result has to
// be findable: the trace names the call, its parameters and the identity of what came back,
// so a reader can re-run it. A claim binding to an operation the trace does not contain is
// refused, because a citation to a call nobody recorded is a citation to nothing.
//
// Derived facts are labelled derived and say they are excluded from evidence exports. The
// pack's rule is that everything derived is permanently labelled and excluded; a derived
// claim that reads like a publisher assertion is the single most expensive confusion this
// product can produce.

import { mark } from './design-tokens.mjs';
import { isUtcInstant } from './temporal.mjs';

/** Where a claim's support comes from. */
export const CLAIM_KINDS = Object.freeze(['publisher_asserted', 'derived']);

const KIND_LABEL = new Map([
  ['publisher_asserted', 'asserted by the publisher'],
  ['derived', 'derived, not publisher-asserted'],
]);

const SHA256 = /^[0-9a-f]{64}$/;

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function requireOperations(operations) {
  if (!Array.isArray(operations) || operations.length === 0) {
    throw new Error(
      'an answer carries its operations trace; a claim that cites a call nobody recorded ' +
        'cites nothing, and the trace is what makes an answer re-runnable rather than ' +
        'merely confident',
    );
  }
  const seen = new Set();
  for (const operation of operations) {
    if (typeof operation?.operation_id !== 'string' || operation.operation_id.length === 0) {
      throw new Error(`an operation needs its id: ${JSON.stringify(operation)}`);
    }
    if (typeof operation?.call_id !== 'string' || operation.call_id.length === 0) {
      throw new Error(`an operation needs a call id to be bound to: ${operation.operation_id}`);
    }
    if (seen.has(operation.call_id)) {
      throw new Error(`call id ${operation.call_id} appears twice in one trace`);
    }
    seen.add(operation.call_id);
    if (operation.parameters === null || typeof operation.parameters !== 'object') {
      throw new Error(
        `${operation.operation_id} must record the parameters it was called with; a call ` +
          'whose arguments are not recorded cannot be re-run',
      );
    }
    if (!SHA256.test(operation?.result_identity ?? '')) {
      throw new Error(
        `${operation.operation_id} must record the identity of what came back, as a 64 hex ` +
          'character digest; without it the trace names a call and not a result',
      );
    }
    if (!isUtcInstant(operation?.called_at)) {
      throw new Error(
        `${operation.operation_id} must record when it was called: ` +
          JSON.stringify(operation?.called_at),
      );
    }
  }
  return seen;
}

function requireClaims(claims, callIds) {
  if (!Array.isArray(claims) || claims.length === 0) {
    throw new Error('an answer is made of claims; there is no unclaimed prose to render');
  }
  for (const claim of claims) {
    if (typeof claim?.sentence !== 'string' || claim.sentence.trim().length === 0) {
      throw new Error(`a claim needs its sentence: ${JSON.stringify(claim)}`);
    }
    if (!CLAIM_KINDS.includes(claim?.kind)) {
      throw new Error(
        `${JSON.stringify(claim?.kind)} is not a claim kind; a sentence is either the ` +
          'publisher’s assertion or this product’s derivation, and which one is not ' +
          'a detail a reader can infer',
      );
    }
    if (!Array.isArray(claim?.bindings) || claim.bindings.length === 0) {
      throw new Error(
        `"${claim.sentence.slice(0, 60)}" binds to nothing; an unbindable sentence is not ` +
          'emitted, because a reader cannot tell a bound sentence from an unbound one by ' +
          'reading it',
      );
    }
    for (const binding of claim.bindings) {
      if (!callIds.has(binding?.call_id)) {
        throw new Error(
          `a claim binds to call ${JSON.stringify(binding?.call_id)}, which is not in the ` +
            'operations trace; a citation to a call nobody recorded is a citation to nothing',
        );
      }
      // O8. This validated that a caller supplied a non-empty string and then rendered it
      // verbatim as a citation chip, so any prose became "the fact this claim relies on,"
      // sourced to a recorded call. Nothing in this module carries the result the chip
      // claims to quote, so nothing here can tell a quotation from an invention.
      //
      // The contract that would settle it is answer_dossier/1, which binds a claim to an
      // exact operation, snapshot and observation. It does not exist yet: it is Stage 4
      // work on #348. Until it lands, this refuses the binding rather than approving it,
      // because a validator that cannot check a claim must not be the thing that blesses
      // it. The claim and its call id still render; the unverifiable quotation does not.
      if (binding !== null && Object.hasOwn(binding, 'fact')) {
        throw new Error(
          'a binding may not carry free prose as the fact it relies on; nothing here can ' +
            'distinguish a quotation from an invention, and answer_dossier/1 is the ' +
            'contract that will, on #348',
        );
      }
    }
  }
}

function renderClaim(claim) {
  const chips = claim.bindings
    .map(
      (binding) =>
        `<li class="claim-chip"><a href="#trace-${escapeHtml(binding.call_id)}">` +
        `call ${escapeHtml(binding.call_id)}</a></li>`,
    )
    .join('');

  const sentence =
    claim.kind === 'derived'
      ? mark('--derived', claim.sentence)
      : `<span class="claim-sentence">${escapeHtml(claim.sentence)}</span>`;

  const exclusion =
    claim.kind === 'derived'
      ? '<p class="claim-exclusion">Derived here, not asserted by the publisher, and ' +
        'excluded from evidence exports.</p>'
      : '';

  return (
    `<li class="claim claim-${escapeHtml(claim.kind)}" data-claim-kind="${escapeHtml(claim.kind)}">` +
    `<p class="claim-line">${sentence}</p>` +
    exclusion +
    `<ul class="claim-chips">${chips}</ul>` +
    '</li>'
  );
}

function renderOperation(operation) {
  const parameters = Object.entries(operation.parameters)
    .map(
      ([key, value]) =>
        `<div class="strip-row"><dt>${escapeHtml(key)}</dt>` +
        `<dd>${escapeHtml(Array.isArray(value) ? value.join(', ') : String(value))}</dd></div>`,
    )
    .join('');
  return (
    `<li class="trace-operation" id="trace-${escapeHtml(operation.call_id)}">` +
    `<p class="trace-call"><code>${escapeHtml(operation.operation_id)}</code> ` +
    `called ${escapeHtml(operation.called_at)}</p>` +
    `<dl class="trace-parameters">${parameters}</dl>` +
    '<p class="trace-result">result <code>' +
    `${escapeHtml(operation.result_identity)}</code></p>` +
    '</li>'
  );
}

/**
 * The answer: claims above the fold, the trace below it.
 *
 * @param {object} input
 * @param {Array} input.claims      every sentence, with its kind and its bindings
 * @param {Array} input.operations  every call, with parameters and result identity
 */
export function renderAnswerDossier({ claims, operations }) {
  const callIds = requireOperations(operations);
  requireClaims(claims, callIds);

  // The trace is expandable and below the claims, per the spec's audit fold: a reader who
  // wants the answer gets the answer, and a reader who wants to check it gets everything
  // needed to re-run it, without either being made to read the other first.
  return (
    '<section class="answer-dossier">' +
    `<ul class="claims">${claims.map(renderClaim).join('')}</ul>` +
    '<details class="operations-trace">' +
    `<summary>How this answer was produced, ${operations.length} operation` +
    `${operations.length === 1 ? '' : 's'}</summary>` +
    `<ul class="trace">${operations.map(renderOperation).join('')}</ul>` +
    '</details>' +
    '</section>'
  );
}
