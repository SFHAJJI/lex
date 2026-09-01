// S4, one provision's history.
//
// The screen answers "what did this article say over its life, and when did it change". That is a
// different question from the work timeline, and the difference is the reason this file exists
// rather than a mode flag on the timeline: a work timeline lists the states the work has had, and
// most of them do not touch any given article.
//
// Three things here are load-bearing, and each is a way this screen could lie.
//
// **The intervals are text intervals, not version intervals.** The service returns every DISTINCT
// text a provision has had, so consecutive versions whose wording is byte-identical collapse into
// one row with a merged interval. A reader counting rows is counting changes to this article, not
// consolidations of the work, and a screen that does not say so invites them to read one as the
// other. The live payload makes this visible: an article with 25 version rows in the index returns
// 2 distinct texts over the filtered range.
//
// **A validity conflict is two publisher dates that disagree, and both are shown.** The provision
// carries its own `article_valid_from`, and the version it sits in carries `valid_from`. When they
// differ the publisher has said two things, and this screen shows both rather than choosing. The
// live record that prompted this: article in force from 2023-04-01, inside a version applicable
// from 2023-07-01.
//
// **An empty history is not "this article never changed".** It is this corpus holding no text
// states for that anchor, which is a fact about the corpus.

import { INTERVAL_SENTENCE, semanticsOf } from './publisher-vocabulary.mjs';
import { identityOf } from './record-identity.mjs';
import { isCalendarDate } from './temporal.mjs';
import { canonicalStateUrl } from './routes.mjs';

const SHA256 = /^[0-9a-f]{64}$/;

/**
 * The lifecycle events an anchor can carry, closed.
 *
 * Measured on the LU index: `renumbered`, `inserted`, `removed`, and nothing else across 60,435
 * rows. Closed rather than open, so a fourth kind arriving from the service is refused instead of
 * rendered under a label written for three.
 */
export const ANCHOR_EVENTS = Object.freeze(['inserted', 'removed', 'renumbered']);

/**
 * What a renumber row is allowed to say.
 *
 * The same rule as the comparison screen: this names whose the renumbering is and stops. It does
 * not name a detection method, because the screen receives a from anchor and a to anchor and never
 * observes how the pairing was found.
 */
export const RENUMBER_BASIS = 'renumbering derived by this service, not publisher-asserted';

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/**
 * Validate one text state and return what the renderers need.
 *
 * Exported so the string renderer and the React component apply one implementation. What comes
 * back is claims; which element carries them is the renderer's business.
 */
export function provisionStateModel(state, index, semantics) {
  const where = `provision state ${index + 1}`;
  if (!isCalendarDate(state?.valid_from)) {
    throw new Error(`${where} has no valid_from calendar date`);
  }
  const validTo = state.valid_to ?? null;
  if (validTo !== null && !isCalendarDate(validTo)) {
    throw new Error(`${where} has a valid_to that is not a calendar date`);
  }
  if (!SHA256.test(state?.text_sha256 ?? '')) {
    throw new Error(
      `${where} has no text digest; the digest is what makes two rows the same text or ` +
        'different ones, and without it this screen cannot say a change happened',
    );
  }

  // Both publisher dates, when they disagree. The provision states when it took effect and the
  // version states when it applied, and the publisher has said both.
  const articleFrom = state.article_valid_from ?? null;
  if (articleFrom !== null && !isCalendarDate(articleFrom)) {
    throw new Error(`${where} has an article_valid_from that is not a calendar date`);
  }
  // Derived, never believed. The payload carries `validity_conflict` and it is absent rather than
  // false on the agreeing rows, so trusting the flag would make a missing field mean agreement.
  const conflict = articleFrom !== null && articleFrom !== state.valid_from;
  if (state.validity_conflict === true && !conflict) {
    throw new Error(
      `${where} declares a validity conflict while its two dates agree; one of those two ` +
        'statements is wrong and this screen must not choose which',
    );
  }

  const permalink = canonicalStateUrl(state?.permalink);
  if (permalink === null) {
    throw new Error(
      `${where} needs a canonical same-origin permalink; a provision history is a list of ` +
        'citations and a citation that does not resolve here is not one',
    );
  }

  return Object.freeze({
    interval: INTERVAL_SENTENCE[semantics](state.valid_from, validTo),
    textDigest: state.text_sha256,
    permalink: permalink.path,
    conflict,
    articleFrom,
    versionFrom: state.valid_from,
  });
}

/**
 * The whole history, validated.
 *
 * @param {object} input
 * @param {string} input.work    a work-level lex_id, which names the publisher
 * @param {string} input.anchor  the provision anchor
 * @param {Array}  input.states  distinct text states, publisher order
 * @param {Array}  [input.anchorEvents]
 * @param {boolean} input.truncated
 * @param {number} input.distinctTexts
 */
export function provisionHistoryModel({
  work,
  anchor,
  states,
  anchorEvents = [],
  truncated,
  distinctTexts,
}) {
  const identity = identityOf(`${work}:history`, 'a provision history');
  const semantics = semanticsOf(identity.publisher, 'a provision history');

  if (typeof anchor !== 'string' || anchor.trim().length === 0) {
    throw new Error('a provision history is about one anchor and none was given');
  }
  if (typeof truncated !== 'boolean') {
    throw new Error(
      'a provision history says whether it was cut; a list that simply stops reads as the whole ' +
        'life of the provision',
    );
  }
  if (!Array.isArray(states)) {
    throw new Error('a provision history needs its states');
  }
  if (!Number.isInteger(distinctTexts) || distinctTexts < 0) {
    throw new Error('a provision history says how many distinct texts it found');
  }
  if (!truncated && distinctTexts !== states.length) {
    throw new Error(
      `this history reports ${distinctTexts} distinct texts and carries ${states.length} states ` +
        'while declaring it was not cut; one of those two numbers is wrong',
    );
  }

  for (const event of anchorEvents) {
    if (!ANCHOR_EVENTS.includes(event?.kind)) {
      throw new Error(
        `${JSON.stringify(event?.kind)} is not an anchor event; the set is ` +
          `${ANCHOR_EVENTS.join(', ')} and one this screen cannot label would render unexplained`,
      );
    }
  }

  return Object.freeze({
    semantics,
    anchor,
    truncated,
    distinctTexts,
    states: states.map((state, index) => provisionStateModel(state, index, semantics)),
    anchorEvents,
  });
}

/** The sentence that stops a text-change count being read as a consolidation count. */
export const TEXT_INTERVAL_NOTE =
  'Each row is one distinct wording. Consecutive publisher versions whose text is identical share ' +
  'a row, so these intervals count changes to this provision and not consolidations of the work.';

/** The sentence an empty history carries. */
export const EMPTY_NOTE =
  'This corpus holds no text states for this provision. That is a statement about this corpus, ' +
  'not about whether the provision ever changed.';

/**
 * The provision history, as HTML.
 *
 * @param {object} input the same shape `provisionHistoryModel` takes
 */
export function renderProvisionHistory(input) {
  const model = provisionHistoryModel(input);

  if (model.states.length === 0) {
    return (
      '<section class="provision-history provision-history-empty">' +
      `<h2>${escapeHtml(model.anchor)}</h2>` +
      `<p class="provision-history-empty-note">${escapeHtml(EMPTY_NOTE)}</p>` +
      '</section>'
    );
  }

  const rows = model.states
    .map(
      (state) =>
        '<li class="provision-state">' +
        `<span class="provision-when">${escapeHtml(state.interval)}</span>` +
        `<code class="provision-digest">${escapeHtml(state.textDigest.slice(0, 8))}</code>` +
        (state.conflict
          ? '<span class="provision-conflict">The publisher gives two dates for this text: the ' +
            `provision takes effect ${escapeHtml(state.articleFrom)} and the version it sits in ` +
            `applies from ${escapeHtml(state.versionFrom)}. Both are shown because both are the ` +
            'publisher\'s.</span>'
          : '') +
        `<a class="provision-link" href="${escapeHtml(state.permalink)}">Read this wording</a>` +
        '</li>',
    )
    .join('');

  const events =
    model.anchorEvents.length === 0
      ? ''
      : '<section class="provision-events"><h3>Lifecycle</h3><ul>' +
        model.anchorEvents
          .map(
            (event) =>
              `<li class="provision-event"><span>${escapeHtml(event.kind)}</span>` +
              (event.kind === 'renumbered'
                ? ` <span class="provision-event-basis">${escapeHtml(RENUMBER_BASIS)}</span>`
                : '') +
              '</li>',
          )
          .join('') +
        '</ul></section>';

  return (
    '<section class="provision-history">' +
    `<h2>${escapeHtml(model.anchor)}</h2>` +
    `<p class="provision-history-note">${escapeHtml(TEXT_INTERVAL_NOTE)}</p>` +
    `<ol class="provision-states">${rows}</ol>` +
    (model.truncated
      ? `<p class="provision-truncated">Showing ${model.states.length} of ` +
        `${model.distinctTexts} distinct wordings.</p>`
      : '') +
    events +
    '</section>'
  );
}
