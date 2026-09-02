// S12, get help.
//
// The screen a reader reaches when they have understood that this service will not apply the law
// to their situation. Decision 41 fixes its shape: a referral LIST, not one counter, because
// naming a single destination is advice about who should advise you.
//
// The hard part is what it does when the list is empty, which is the state this build is in.
// `HANDOFF_HOSTS` is editorial (product spec build item 14) and currently holds only the synthetic
// preview host, because no real counter has been verified into this build. So the honest page is
// not an empty list, and it is certainly not the synthetic host wearing the word "help": it is a
// page that says no counter has been verified here yet and points at the one thing that is true,
// which is that the publisher's own routes remain open.
//
// A reader on this page has already been refused once. Offering them a destination that does not
// resolve, or one that resolves to a fixture, is a second refusal disguised as an answer.

import { handoffUri, publisherSourceUri } from './routes.mjs';

/** The synthetic host, which is a fixture and must never be offered as help. */
const SYNTHETIC_HANDOFF_HOST = 'handoff.invalid';

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/** The sentence shown when no counter has been verified into this build. */
export const NO_COUNTER_NOTE =
  'No advice counter has been verified into this build, so this service names none. That is a ' +
  'statement about this build and not about who can help you: counters exist, and this page will ' +
  'list them once each has been verified with its own address.';

/**
 * Said once, above whatever is offered, because it is why this page exists.
 *
 * The wording is the product spec's fixed EN master, not a paraphrase. An earlier version here
 * said "that assessment is a legal consultation and it is reserved", which claims categorically
 * that every assessment of facts is reserved. That is wider than the law it rests on: the loi du
 * 10 aout 1991 art. 2(2) reserves consultation given *a titre habituel et contre remuneration*,
 * and art. 2(3) exempts public administrations, regulated professions within their remit,
 * in-house counsel, unions informing members and others. Overclaiming a legal boundary on a page
 * whose entire purpose is to state that boundary accurately is the worst place to be loose.
 */
export const BOUNDARY_NOTE =
  'This service shows what the published text says, at any date, and how it changed, with ' +
  'citations. It cannot apply the law to your situation; under Luxembourg law that assessment ' +
  'is a legal consultation reserved to qualified professionals.';

/**
 * A counter that may be offered, or a refusal saying why not.
 *
 * This function refuses the synthetic host, so a caller reaching for it directly is told rather
 * than quietly given nothing. The page below drops it before calling, because a build whose
 * registry holds only the fixture is the expected state today and must render rather than throw.
 * Those two behaviours are deliberate and different: one is a caller error, the other is the
 * current state of the world.
 */
export function admissibleCounter(counter, index) {
  const where = `counter ${index + 1}`;
  if (typeof counter?.label !== 'string' || counter.label.trim().length === 0) {
    throw new Error(`${where} has no label; an unnamed counter is an address a reader must trust`);
  }
  // handoffUri throws for anything off the registry, naming the host it refused. That is the
  // refusal this function wants, so it is not caught and not restated: a counter enters the
  // registry editorially with a verified address, and a link this page cannot vouch for is worse
  // than no link at all.
  const href = handoffUri(counter?.href);
  if (new URL(href).hostname === SYNTHETIC_HANDOFF_HOST) {
    throw new Error(
      `${where} is the synthetic preview counter and cannot be offered as help; it is a fixture, ` +
        'and a reader on this page has already been refused once',
    );
  }
  return { label: counter.label, href };
}

/**
 * The publisher routes this page may link to, validated.
 *
 * One implementation for both surfaces. Validated against the publisher each route names rather
 * than merely escaped: `escapeHtml` replaces `& < > "`, and `javascript:alert(1)` contains none of
 * them, so an unvalidated value survives escaping intact and renders as a working link. A reader
 * reaches this page having already been refused once, and a destination that runs script or leaves
 * the origin is a second refusal wearing the word help.
 *
 * Extracted rather than repeated because repeating it is exactly how the React surface kept the
 * defect after the string renderer was repaired. The parity test caught that, which is the only
 * reason this is a shared function and not two.
 */
export function admissibleOfficialRoutes(officialRoutes) {
  if (!Array.isArray(officialRoutes) || officialRoutes.length === 0) {
    throw new Error(
      'get help lists the publisher routes that remain open; without them a page with no verified ' +
        'counter would offer a reader nothing at all',
    );
  }

  for (const route of officialRoutes) {
    publisherSourceUri({ publisher: route?.publisher, uri: route?.uri });
    if (typeof route?.label !== 'string' || route.label.trim().length === 0) {
      throw new Error('an official route with no label is a link a reader cannot read');
    }
  }

  return officialRoutes;
}

/**
 * The counters this build may actually offer, in order.
 *
 * Extracted so the string renderer and the React component apply one implementation. Which of the
 * two the page shows, a list or the no-counter sentence, follows from the length of what comes
 * back, and neither surface decides it independently.
 *
 * @param {object} input
 * @param {Array} [input.counters]  verified counters, each `{ label, href }`
 * @param {Array} input.officialRoutes  the publisher's own routes, which are always true
 */
export function admissibleCounters({ counters = [], officialRoutes }) {
  if (!Array.isArray(officialRoutes) || officialRoutes.length === 0) {
    throw new Error(
      'get help lists the publisher routes that remain open; without them a page with no verified ' +
        'counter would offer a reader nothing at all',
    );
  }

  // The synthetic host is dropped here rather than refused, because a build whose registry holds
  // only the fixture is the expected state and must render, not throw. Every other inadmissible
  // counter still throws through admissibleCounter.
  const real = counters.filter(
    (counter) => new URL(handoffUri(counter?.href)).hostname !== SYNTHETIC_HANDOFF_HOST,
  );
  return real.map(admissibleCounter);
}

/**
 * The get-help page.
 *
 * @param {object} input
 * @param {Array} [input.counters]  verified counters, each `{ label, href }`
 * @param {Array} input.officialRoutes  the publisher's own routes, which are always true
 */
export function renderGetHelp({ counters = [], officialRoutes }) {
  const admitted = admissibleCounters({ counters, officialRoutes });

  const list =
    admitted.length === 0
      ? `<p class="get-help-none">${escapeHtml(NO_COUNTER_NOTE)}</p>`
      : '<ul class="get-help-counters">' +
        admitted
          .map(
            (counter) =>
              `<li><a href="${escapeHtml(counter.href)}" rel="external">` +
              `${escapeHtml(counter.label)}</a></li>`,
          )
          .join('') +
        '</ul>';

  return (
    '<section class="get-help">' +
    '<h2>Getting advice</h2>' +
    `<p class="get-help-boundary">${escapeHtml(BOUNDARY_NOTE)}</p>` +
    list +
    '<h3>The publisher, directly</h3>' +
    '<ul class="get-help-official">' +
    admissibleOfficialRoutes(officialRoutes)
      .map(
        (route) =>
          `<li><a href="${escapeHtml(route.uri)}" rel="external">` +
          `${escapeHtml(route.label)}</a></li>`,
      )
      .join('') +
    '</ul>' +
    '</section>'
  );
}
