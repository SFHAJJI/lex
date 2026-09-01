// Compare, and the three ways a diff lies.
//
// A diff is the most persuasive object this product renders. Two columns with red and green
// in them read as fact, and a reader who would question a sentence will not question a
// highlight. So the rules here are about the ways a diff can be confidently wrong, and each
// one is a refusal rather than a caveat.
//
// The first lie is parser noise. Two states extracted under different profiles mint different
// anchors, so aligning them reports the extractor's disagreement as legislation. The pack's
// word is "never overridable", and that is implemented as the panes not existing rather than
// as a warning above them.
//
// The second lie is translation. Comparing an English state against a French one shows a
// change on every line, and none of them are changes in the law. This is the same failure as
// the first with a different cause, so it gets the same treatment.
//
// The third lie is the empty diff. When both dates resolve to the same state, rendering two
// empty panes tells a reader "nothing changed between these dates" in a way that looks like a
// measurement. The publisher's own note says the same thing and means it, so the note is
// rendered verbatim and the panes are not built.
//
// Underneath all three: the resolution header comes first, always. A reader has to see WHICH
// two states are being compared before seeing HOW they differ, because a diff of the wrong
// pair is worse than no diff.

import { isCalendarDate, isUtcInstant } from './temporal.mjs';
import { renderRefusalCard } from './refusal-card.mjs';

/** The two axes a comparison can run along. They are never mixed. */
export const COMPARE_MODES = Object.freeze(['temporal', 'language']);

/** The fixed label on a renumber row. Mechanical, and it says so. */
export const RENUMBER_LABEL =
  'renumbering detected mechanically by identical text hash, not publisher-asserted';

const SHA256 = /^[0-9a-f]{64}$/;

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/**
 * One side of the comparison, fully resolved.
 *
 * Every field here appears on the card, and the card is what makes the comparison checkable.
 * A side that cannot produce all of them has not been resolved, and an unresolved side cannot
 * be compared to anything.
 */
function requireSide(side, which) {
  const where = `the ${which} side`;

  if (typeof side?.lex_id !== 'string' || side.lex_id.trim().length === 0) {
    throw new Error(
      `${where} has no lex_id; the resolution header names the exact states being compared, ` +
        'and a diff of an unnamed pair cannot be checked by anybody',
    );
  }
  if (!isCalendarDate(side.valid_from)) {
    throw new Error(`${where} valid_from is not a calendar date: ${JSON.stringify(side.valid_from)}`);
  }
  if (side.valid_to !== null && !isCalendarDate(side.valid_to)) {
    throw new Error(`${where} valid_to is neither null nor a calendar date`);
  }
  if (!isCalendarDate(side.publication_date)) {
    throw new Error(`${where} publication_date is not a calendar date`);
  }
  if (!isUtcInstant(side.observed_from)) {
    throw new Error(`${where} observed_from is not a UTC instant`);
  }
  if (!SHA256.test(side.body_sha256 ?? '')) {
    throw new Error(
      `${where} has no body digest; both hashes are what let a reader recompute this ` +
        'comparison, and the export records them',
    );
  }
  if (typeof side.language !== 'string' || side.language.trim().length === 0) {
    throw new Error(`${where} does not say which language it is`);
  }
  return side;
}

/**
 * A resolved state card, carrying both clocks and its digest.
 *
 * Legal time first in the publisher's own vocabulary, record time second and smaller. The
 * vocabulary is the caller's because it comes from the envelope's timeline_semantics; a card
 * that invented its own wording would be putting the product's words in the publisher's mouth.
 */
function renderSideCard(side, which) {
  requireSide(side, which);
  if (typeof side.legal_time_sentence !== 'string' || side.legal_time_sentence.trim().length === 0) {
    throw new Error(
      `the ${which} side needs its legal-time sentence in the publisher's own vocabulary; ` +
        'this product does not choose between "applicable" and "consolidated wording state"',
    );
  }

  return (
    `<article class="compare-side" data-side="${escapeHtml(which)}">` +
    `<h3 class="compare-side-id"><code>${escapeHtml(side.lex_id)}</code></h3>` +
    `<p class="compare-legal-time">${escapeHtml(side.legal_time_sentence)}</p>` +
    `<p class="compare-record-time">Published ${escapeHtml(side.publication_date)} / ` +
    `First observed ${escapeHtml(side.observed_from)}</p>` +
    `<p class="compare-language">Language: ${escapeHtml(side.language)}</p>` +
    `<p class="compare-digest">body_sha256 <code>${escapeHtml(side.body_sha256)}</code></p>` +
    '</article>'
  );
}

/**
 * The resolution header. Mandatory, and it renders before anything else in the document order
 * as well as before anything else in this function, so a reader meets the pair first.
 */
export function renderResolutionHeader({ left, right }) {
  return (
    '<header class="compare-resolution"><h2>The two states being compared</h2>' +
    renderSideCard(left, 'left') +
    renderSideCard(right, 'right') +
    '</header>'
  );
}

/**
 * A side that refused. The healthy side stays and the failing side becomes its refusal, so a
 * reader sees which half of their question could not be answered rather than losing both.
 */
function renderSideRefusal(refusal, which) {
  return (
    `<article class="compare-side compare-side-refused" data-side="${escapeHtml(which)}">` +
    renderRefusalCard(refusal) +
    '</article>'
  );
}

function renderChangeBlock(block, index) {
  const where = `change block ${index + 1}`;
  if (typeof block?.anchor_label !== 'string' || block.anchor_label.trim().length === 0) {
    throw new Error(`${where} does not say which provision it is in`);
  }
  const removed = block.removed ?? '';
  const added = block.added ?? '';
  if (removed === '' && added === '') {
    throw new Error(
      `${where} has neither a removal nor an addition; an empty block in a changed diff ` +
        'renders as a change that is not there',
    );
  }

  // The linear reading is the markup, not an alternative to it. Prefixes are visually hidden
  // rather than absent, so the screen reader sequence is "In Art. X: removed ...; added ..."
  // and the visual pairs colour with strikethrough and underline rather than relying on it.
  const parts = [`<p class="compare-block-anchor">In ${escapeHtml(block.anchor_label)}:</p>`];
  if (removed !== '') {
    parts.push(
      '<p class="compare-removed"><span class="visually-hidden">removed: </span>' +
        `<del>${escapeHtml(removed)}</del></p>`,
    );
  }
  if (added !== '') {
    parts.push(
      '<p class="compare-added"><span class="visually-hidden">added: </span>' +
        `<ins>${escapeHtml(added)}</ins></p>`,
    );
  }
  return `<li class="compare-block">${parts.join('')}</li>`;
}

function renderRenumbering(rows) {
  if (rows.length === 0) return '';
  const items = rows
    .map((row, index) => {
      if (typeof row?.from !== 'string' || typeof row?.to !== 'string') {
        throw new Error(`renumber row ${index + 1} needs both anchors`);
      }
      return (
        `<li class="compare-renumber"><code>${escapeHtml(row.from)}</code> to ` +
        `<code>${escapeHtml(row.to)}</code> ` +
        `<span class="compare-renumber-basis">${escapeHtml(RENUMBER_LABEL)}</span></li>`
      );
    })
    .join('');
  return `<section class="compare-renumbering"><h3>Renumbering</h3><ul>${items}</ul></section>`;
}

/**
 * Compare two resolved states.
 *
 * @param {object} input
 * @param {'temporal'|'language'} input.mode
 * @param {object} input.left   resolved state, or `{ refusal }` for a side that could not resolve
 * @param {object} input.right  same
 * @param {object} input.result `{ changed, note }` or `{ changed, blocks, renumbering }`
 */
export function renderCompare({ mode, left, right, result }) {
  if (!COMPARE_MODES.includes(mode)) {
    throw new Error(
      `a comparison must declare its axis, one of ${COMPARE_MODES.join(', ')}; an undeclared ` +
        'axis is how a translation gets read as an amendment',
    );
  }

  // A side that refused. The other side still renders, and no diff does: half a resolution
  // cannot produce a whole comparison.
  const leftRefused = Object.hasOwn(left ?? {}, 'refusal');
  const rightRefused = Object.hasOwn(right ?? {}, 'refusal');
  if (leftRefused || rightRefused) {
    return (
      '<section class="compare compare-partial">' +
      '<header class="compare-resolution"><h2>The two states being compared</h2>' +
      (leftRefused ? renderSideRefusal(left.refusal, 'left') : renderSideCard(left, 'left')) +
      (rightRefused ? renderSideRefusal(right.refusal, 'right') : renderSideCard(right, 'right')) +
      '</header>' +
      '<p class="compare-no-panes">One side of this comparison did not resolve, so no ' +
      'comparison is shown. The side that did resolve is above and can be read on its own.</p>' +
      '</section>'
    );
  }

  requireSide(left, 'left');
  requireSide(right, 'right');

  // Parser noise. Both profiles known and different means the anchors were minted by different
  // extractors, so alignment would report their disagreement as legislation. The panes are not
  // built, rather than built and hidden, because "not overridable" has to be a property of the
  // code and not of a flag somebody can pass.
  const bothProfilesKnown =
    typeof left.profile === 'string' &&
    left.profile.length > 0 &&
    typeof right.profile === 'string' &&
    right.profile.length > 0;
  if (bothProfilesKnown && left.profile !== right.profile) {
    return (
      '<section class="compare compare-refused">' +
      renderResolutionHeader({ left, right }) +
      renderRefusalCard({
        code: 'profiles_differ',
        sentence:
          `These states were extracted under different profiles (${left.profile}, ` +
          `${right.profile}). Comparing them would report parser differences as legislation. ` +
          'Not overridable. Read them side by side instead.',
        payload: { profiles: [left.profile, right.profile] },
      }) +
      '</section>'
    );
  }

  // Translation read as amendment. A temporal comparison across two languages shows a
  // difference on every line and not one of them is a change in the law.
  if (mode === 'temporal' && left.language !== right.language) {
    throw new Error(
      `a temporal comparison cannot cross languages (${left.language}, ${right.language}); ` +
        'every line would differ and none of the differences would be changes in the law. ' +
        'Compare one language over time, or compare two languages of one state',
    );
  }
  // And its mirror: a language comparison of two different states is a temporal difference
  // wearing a language label, which is the same lie pointing the other way.
  if (mode === 'language') {
    if (left.language === right.language) {
      throw new Error('a language comparison needs two different languages');
    }
    if (left.valid_from !== right.valid_from || left.valid_to !== right.valid_to) {
      throw new Error(
        'a language comparison must be of one state; these two cover different periods, so ' +
          'the difference would be change over time wearing a language label',
      );
    }
  }

  const banner =
    mode === 'language'
      ? '<p class="compare-axis">Language comparison, same state. Both texts are authentic. ' +
        'Nothing here is a change over time.</p>'
      : '<p class="compare-axis">Comparison over time, one language. The diff is legal time ' +
        'only.</p>';

  // Comparability could not be established. Not a refusal, because the service does not refuse
  // here and this component does not invent codes the contract has not got; but not silence
  // either, because a reader would otherwise take the comparison as profile-verified.
  const unverified = bothProfilesKnown
    ? ''
    : '<p class="compare-profile-unknown">At least one of these states does not record its ' +
      'extraction profile, so this comparison could not be checked for the parser disagreement ' +
      'that profiles_differ exists to catch.</p>';

  if (result?.changed === false) {
    // The publisher's note, verbatim. Composing our own sentence here would turn the
    // publisher's statement into ours, on the one screen where the difference matters most.
    if (typeof result.note !== 'string' || result.note.trim().length === 0) {
      throw new Error(
        'an unchanged result renders the payload note verbatim and there is no note; this ' +
          'screen must not compose its own sentence about what the publisher found',
      );
    }
    return (
      '<section class="compare compare-identical">' +
      renderResolutionHeader({ left, right }) +
      banner +
      unverified +
      `<p class="compare-note">changed: false. ${escapeHtml(result.note)}</p>` +
      '<p class="compare-note-basis">This is the answer, not an empty result. The two dates ' +
      'resolve to one state, so there is nothing between them to show.</p>' +
      '</section>'
    );
  }

  if (result?.changed !== true) {
    throw new Error(
      'a comparison result must say whether it changed; an absent verdict rendered as empty ' +
        'panes reads as a measurement that nothing changed',
    );
  }

  const blocks = Array.isArray(result.blocks) ? result.blocks : [];
  if (blocks.length === 0) {
    throw new Error(
      'a changed result with no change blocks would render two empty panes under a heading ' +
        'that says the law changed',
    );
  }
  const renumbering = Array.isArray(result.renumbering) ? result.renumbering : [];

  return (
    '<section class="compare compare-changed">' +
    renderResolutionHeader({ left, right }) +
    banner +
    unverified +
    `<ol class="compare-blocks">${blocks.map(renderChangeBlock).join('')}</ol>` +
    renderRenumbering(renumbering) +
    '</section>'
  );
}
