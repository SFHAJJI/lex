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

import { handoffUri } from './routes.mjs';

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

/** The boundary sentence, which is the reason this page exists at all. */
export const BOUNDARY_NOTE =
  'This service shows what the published text says, at any date, with citations. It does not ' +
  'apply the law to your situation. That assessment is a legal consultation and it is reserved.';

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
 * The get-help page.
 *
 * @param {object} input
 * @param {Array} [input.counters]  verified counters, each `{ label, href }`
 * @param {Array} input.officialRoutes  the publisher's own routes, which are always true
 */
export function renderGetHelp({ counters = [], officialRoutes }) {
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
  const admitted = real.map(admissibleCounter);

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
    officialRoutes
      .map(
        (route) =>
          `<li><a href="${escapeHtml(route.uri)}" rel="external">${escapeHtml(route.label)}</a></li>`,
      )
      .join('') +
    '</ul>' +
    '</section>'
  );
}
