// S10, the export composer.
//
// The bundle renderer in `evidence-bundle.mjs` decides what a bundle *contains*. This module
// decides what a reader is told *before* they commit to one, which is a different job and the
// reason this screen exists at all.
//
// The rule that shapes everything here: rights are applied at compose time, not at export time.
// Someone who pins twelve items, exports, and only then discovers that four of them travelled as
// a bare digest has been misled by their own tool. So the composer states each item's disposition
// while the cart is still editable, and states why, in the item's own terms.
//
// Every disposition is derived from `text_public` and the licence. None is accepted from the
// caller. A caller who could declare "this one embeds" could declare it wrongly, and the bundle
// and the preview would then disagree about the same item, with only one of them tested.

import {
  LICENCES,
  ITEM_KINDS,
  REGISTER_COLUMNS,
  requireIntervalAndDigest,
} from './evidence-bundle.mjs';
import { identityOf } from './record-identity.mjs';
import { publisherSourceUri } from './routes.mjs';

/** Kinds a bundle refuses structurally, whatever their licence says. */
const EXCLUDED_KINDS = Object.freeze(['derived', 'unofficial']);

/**
 * What a cart item will contribute to the bundle. Closed, and ordered from most to least.
 *
 * `withheld_by_licence` and `withheld_by_rights` are deliberately separate. They look identical
 * on the page until you read the sentence, and they are different facts about the world: one is
 * a term of a grant that exists, the other is a grant that was never established (D38, C2).
 * Collapsing them would let a rights gap hide behind a licence label.
 */
export const DISPOSITIONS = Object.freeze([
  'travels_with_text',
  'withheld_by_licence',
  'withheld_by_rights',
]);

const DISPOSITION_SENTENCE = Object.freeze(
  Object.assign(Object.create(null), {
    travels_with_text:
      'The full text travels in the bundle, with its attribution line.',
    withheld_by_licence:
      'The licence does not let the text travel. This item exports as its digest and its ' +
      'official link, which are enough to fetch and check it.',
    withheld_by_rights:
      'No public-text right has been established for this item, so no text may travel. It ' +
      'exports as its digest and its official link.',
  }),
);

/** Said once, above the cart, because it governs every row rather than any one of them. */
export const COMPOSE_TIME_NOTE =
  'Rights are applied here, not at export. What each item says below is what the bundle will ' +
  'contain.';

/** The empty cart. A shipped state, and the first one most readers will see. */
export const EMPTY_CART_NOTE =
  'Nothing is pinned yet. Pin a state from a reading or timeline view and it appears here, with ' +
  'what it will contribute to the bundle.';

/** The watermark the composed bundle will carry, shown before composing so it is never a surprise. */
export const WATERMARK_PREVIEW =
  'Documentation. Consolidations have no legal effect. Authentic sources cited per item.';

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

/**
 * The disposition of one item, derived.
 *
 * Order matters and is not cosmetic. `text_public` is asked first because it is whether the
 * grant exists at all; the licence's terms are only reachable once it does. Asking the licence
 * first would report `withheld_by_licence` for an item whose real problem is that nobody
 * established a right, which is the more serious of the two and the one worth naming.
 */
export function dispositionOf(item, where) {
  if (typeof item?.text_public !== 'boolean') {
    throw new Error(
      `${where} does not carry text_public; the composer states what a bundle will contain and ` +
        'cannot state that without knowing whether a public-text right was established (D38)',
    );
  }
  if (!Object.hasOwn(LICENCES, item?.licence ?? '')) {
    throw new Error(
      `${where} declares licence ${JSON.stringify(item?.licence)}; rights are applied at compose ` +
        `time and that is only possible for a known licence: ${Object.keys(LICENCES).join(', ')}`,
    );
  }
  if (!item.text_public) {
    return 'withheld_by_rights';
  }
  return LICENCES[item.licence].embedsText ? 'travels_with_text' : 'withheld_by_licence';
}

function requireCartItem(item, index) {
  const where = `cart item ${index + 1}`;

  if (!ITEM_KINDS.includes(item?.kind)) {
    throw new Error(
      `${where} has kind ${JSON.stringify(item?.kind)}; a cart holds only ` +
        `${ITEM_KINDS.join(', ')}`,
    );
  }
  // Refused rather than filtered. A derived join silently dropped from a cart the reader
  // assembled leaves them with a bundle they did not compose and no statement that it changed.
  if (EXCLUDED_KINDS.includes(item.kind)) {
    throw new Error(
      `${where} is ${item.kind}; a bundle excludes derived joins and unofficial translations ` +
        'structurally, so it cannot be pinned and then quietly dropped at export',
    );
  }
  if (typeof item?.lex_id !== 'string' || item.lex_id.length === 0) {
    throw new Error(`${where} carries no lex_id`);
  }
  // Parsed for its side effect: an unparseable identity is refused here, where the reader can
  // still unpin it, rather than at export.
  identityOf(item.lex_id, where);

  for (const field of ['valid_from', 'official_uri', 'record_sha256']) {
    if (typeof item?.[field] !== 'string' || item[field].trim().length === 0) {
      throw new Error(
        `${where} carries no ${field}; the cover sheet names every item by identity, interval, ` +
          'hash and official source, and an item missing one of those cannot be listed honestly',
      );
    }
  }

  // The same interval and digest rules the bundle applies. Checking only that these fields are
  // non-empty let a not-a-date, a not-a-digest, an inverted interval and the year-9999 sentinel
  // through a screen whose entire purpose is telling a reader what the export will contain.
  requireIntervalAndDigest(item, where);

  // The official link is validated against the publisher the record names, not merely escaped.
  // `escapeHtml` replaces `& < > "`, and `javascript:alert(1)` contains none of them, so an
  // unvalidated value survives escaping intact and renders as a working link on the one control a
  // reader uses to check us against the source. This is the same open-redirect shape as the revert
  // control repaired earlier on this branch; I fixed that one and did not sweep for siblings.
  publisherSourceUri({
    publisher: identityOf(item.lex_id, where).publisher,
    uri: item.official_uri,
  });

  const disposition = dispositionOf(item, where);
  // Attribution travels with any body. Checked here and not only in the bundle, because the
  // composer promises what the bundle will contain, and promising a body with no source named
  // is the promise that would break.
  if (
    disposition === 'travels_with_text' &&
    (typeof item.attribution !== 'string' || item.attribution.trim().length === 0)
  ) {
    throw new Error(
      `${where} will export its publisher's text and names no attribution; text that travels ` +
        'without naming its source cannot be checked against anything',
    );
  }
  return disposition;
}

/**
 * The composer's model: the cart, each item's derived disposition, and the counts.
 *
 * @param {object} input
 * @param {Array}  input.items    pinned items
 * @param {object} input.matter   `{ reference, author }`, the two facts the record cannot supply
 * @param {Array}  [input.columns] register columns; defaults to the closed set
 */
export function exportComposerModel({ items, matter, columns = REGISTER_COLUMNS }) {
  if (!Array.isArray(items)) {
    throw new Error('the composer takes a cart, which is an array, even when it is empty');
  }

  // The two genuine caller facts on this screen. A matter reference and an author are not in any
  // record and never could be, so they are required rather than derived, and required rather
  // than defaulted: a bundle filed under an empty reference is a bundle nobody can find again.
  for (const field of ['reference', 'author']) {
    if (typeof matter?.[field] !== 'string' || matter[field].trim().length === 0) {
      throw new Error(
        `the bundle needs a matter ${field}; it is one of the two facts on this screen that no ` +
          'record can supply, so it is asked for rather than invented',
      );
    }
  }

  const rows = items.map((item, index) => ({
    lex_id: item.lex_id,
    valid_from: item.valid_from,
    valid_to: item.valid_to ?? null,
    official_uri: item.official_uri,
    record_sha256: item.record_sha256,
    licence: item.licence,
    attribution: item.attribution ?? null,
    disposition: requireCartItem(item, index),
    publisher: identityOf(item.lex_id, `cart item ${index + 1}`).publisher,
  }));

  const counts = Object.create(null);
  for (const name of DISPOSITIONS) {
    counts[name] = rows.filter((row) => row.disposition === name).length;
  }

  return {
    rows,
    counts: Object.freeze(counts),
    columns,
    matter: { reference: matter.reference, author: matter.author },
    withheld: counts.withheld_by_licence + counts.withheld_by_rights,
  };
}

/** The sentence stating how much of the cart will not carry text, or null when all of it will. */
export function withheldSummary(model) {
  if (model.withheld === 0) {
    return null;
  }
  const total = model.rows.length;
  // Both numbers, always. "4 items are withheld" invites the reader to supply the denominator
  // themselves, and a reader who assumes it is the whole cart stops composing.
  return (
    `${model.withheld} of ${total} pinned ${total === 1 ? 'item' : 'items'} will export as a ` +
    'digest and an official link rather than as text. Each row says which, and why.'
  );
}

function renderRow(row) {
  const interval = `${escapeHtml(row.valid_from)} to ${
    row.valid_to === null ? 'open' : escapeHtml(row.valid_to)
  }`;
  return (
    `<li class="compose-item compose-${escapeHtml(row.disposition)}">` +
    `<code class="compose-id">${escapeHtml(row.lex_id)}</code>` +
    `<span class="compose-interval">${interval}</span>` +
    `<code class="compose-digest">${escapeHtml(row.record_sha256.slice(0, 8))}</code>` +
    `<span class="compose-licence">${escapeHtml(row.licence)}</span>` +
    // The disposition is a sentence, not a badge. A badge reading "withheld" beside a licence
    // name is meaning carried by adjacency, which is the thing this product refuses.
    `<span class="compose-disposition">${escapeHtml(
      DISPOSITION_SENTENCE[row.disposition],
    )}</span>` +
    `<a class="compose-official" href="${escapeHtml(row.official_uri)}" rel="external">` +
    'Official source</a>' +
    '</li>'
  );
}

/**
 * The composer.
 *
 * @param {object} input the same shape `exportComposerModel` takes
 */
export function renderExportComposer(input) {
  const model = exportComposerModel(input);

  if (model.rows.length === 0) {
    // Not an empty list. An empty list and a cart nobody has filled look the same and are not
    // the same, and only one of them has an action attached.
    return (
      '<section class="export-composer export-composer-empty">' +
      '<h2>Export composer</h2>' +
      `<p class="compose-empty">${escapeHtml(EMPTY_CART_NOTE)}</p>` +
      '</section>'
    );
  }

  const summary = withheldSummary(model);

  return (
    '<section class="export-composer">' +
    '<h2>Export composer</h2>' +
    `<p class="compose-note">${escapeHtml(COMPOSE_TIME_NOTE)}</p>` +
    '<dl class="compose-matter">' +
    `<dt>Matter</dt><dd>${escapeHtml(model.matter.reference)}</dd>` +
    `<dt>Prepared by</dt><dd>${escapeHtml(model.matter.author)}</dd>` +
    '</dl>' +
    (summary === null
      ? ''
      : `<p class="compose-withheld">${escapeHtml(summary)}</p>`) +
    `<ol class="compose-items">${model.rows.map(renderRow).join('')}</ol>` +
    '<section class="compose-register"><h3>Register columns</h3>' +
    `<ul class="compose-columns">${model.columns
      .map((column) => `<li><code>${escapeHtml(column)}</code></li>`)
      .join('')}</ul></section>` +
    // Shown before composing rather than discovered on the exported file.
    `<p class="compose-watermark">${escapeHtml(WATERMARK_PREVIEW)}</p>` +
    '</section>'
  );
}
