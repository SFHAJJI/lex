// The evidence bundle, and the four things it will not do.
//
// This is the artefact somebody takes into a meeting, or attaches to advice they are
// accountable for. Everything else in the interface can be re-read in context; a bundle
// leaves and is read by people who were not here. So the rules that matter are the ones
// about what cannot be in it, and each is structural rather than a setting.
//
// A derived or unofficial object cannot enter. Not "is excluded by default": an item
// declares what it is, and the composer refuses the two kinds outright. The pack's words are
// "excluded structurally", and the reason is that a labelled convenience which quietly
// enters a bundle stops being labelled at the exact moment it matters.
//
// Rights are applied at compose time, not at read time. An item whose licence does not
// permit serving the text is included as hash and link only, and the composer strips the
// text rather than trusting a caller to have withheld it.
//
// The register has a closed column set, and three names are refused by name: impact, owner
// action, and compliant. Those are the columns that turn a record of what the publisher
// published into an opinion about somebody's situation, which is the one thing this product
// does not do. A column header is a cheap way to smuggle that back in.
//
// The verification annex is mandatory, because a bundle nobody can check is a bundle that
// has to be trusted, and the whole product is built so that it does not have to be.

import { isCalendarDate, isUtcInstant } from './temporal.mjs';
import { publisherSourceUri } from './routes.mjs';
import { INTERVAL_TERM, requireSemantics } from './timeline.mjs';

/** What an item is. Only the first may enter a bundle. */
export const ITEM_KINDS = Object.freeze(['publisher_text', 'derived', 'unofficial']);

const EXCLUDED_KINDS = new Set(['derived', 'unofficial']);

/** Licences, and whether they let the text itself travel. */
export const LICENCES = Object.freeze({
  'cc-by-4.0': Object.freeze({ embedsText: true, attribution: true }),
  cc0: Object.freeze({ embedsText: true, attribution: false }),
  'licence-scl': Object.freeze({ embedsText: false, attribution: true }),
});

/** The register's columns, closed. */
export const REGISTER_COLUMNS = Object.freeze([
  'identifier',
  'valid_from',
  'valid_to',
  'publication_date',
  'observed_from',
  'official_uri',
  'record_sha256',
]);

/**
 * Column names that turn a record into an opinion. Named individually because they are the
 * ones a well-meaning person adds, and because refusing them generically would not say why.
 */
const FORBIDDEN_COLUMNS = new Map([
  ['impact', 'an impact is an assessment of somebody’s situation, which this product refuses'],
  ['owner_action', 'an owner action tells a reader what to do, which is advice'],
  ['compliant', 'a compliance verdict applies the law to a person, which is the reserved act'],
]);

const SHA256 = /^[0-9a-f]{64}$/;

// "Authentic sources cited per item" was printed on every bundle although no item carries
// an authenticity binding, so the artefact a reader keeps and cites certified its own
// contents. The rights and authenticity objects that would support it are not in this
// candidate, and inventing a local substitute is the defect with more machinery behind it.
// The two clauses that remain are true of every bundle without further evidence.
const WATERMARK = 'Documentation. Consolidations have no legal effect.';

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function requireItem(item, index) {
  const where = `item ${index + 1}`;

  if (!ITEM_KINDS.includes(item?.kind)) {
    throw new Error(
      `${where} must declare its kind, one of ${ITEM_KINDS.join(', ')}; an item that does not ` +
        'say what it is cannot be excluded for being it',
    );
  }
  if (EXCLUDED_KINDS.has(item.kind)) {
    throw new Error(
      `${where} is ${item.kind} and cannot enter an evidence bundle; the exclusion is ` +
        'structural, because a labelled convenience that quietly enters a bundle stops being ' +
        'labelled at the moment it matters',
    );
  }

  if (typeof item.citation !== 'string' || item.citation.trim().length === 0) {
    throw new Error(`${where} needs its citation string`);
  }
  // Three dates, the pack's phrase: applicable, published, observed. A bundle carrying one
  // of them lets a reader mistake the observation for the law's own date.
  isCalendarDateOrThrow(item.valid_from, `${where} valid_from`);
  if (item.valid_to !== null) isCalendarDateOrThrow(item.valid_to, `${where} valid_to`);
  isCalendarDateOrThrow(item.publication_date, `${where} publication_date`);
  if (!isUtcInstant(item.observed_from)) {
    throw new Error(`${where} needs the instant it was observed: ${JSON.stringify(item.observed_from)}`);
  }
  if (!SHA256.test(item.record_sha256 ?? '')) {
    throw new Error(`${where} needs its record digest, 64 lowercase hex characters`);
  }
  if (!Object.hasOwn(LICENCES, item.licence ?? '')) {
    throw new Error(
      `${where} declares licence ${JSON.stringify(item.licence)}; the composer applies rights ` +
        `at compose time and can only do that for a licence it knows: ${Object.keys(LICENCES).join(', ')}`,
    );
  }

  // O7. The licence table declares two obligations per licence and only one was enforced.
  // A licence requiring attribution was satisfied by an item that carried none, so the
  // bundle travelled with the publisher's text and without the credit the publisher's own
  // licence requires. Declaring an obligation and not checking it is worse than not
  // declaring it, because the table reads as the thing that enforces it.
  if (LICENCES[item.licence].attribution) {
    if (typeof item.attribution !== 'string' || item.attribution.trim().length === 0) {
      throw new Error(
        `${where} is licensed ${item.licence}, which requires attribution, and carries ` +
          'none; the composer cannot satisfy at compose time an obligation the item does ' +
          'not name',
      );
    }
  }
  return publisherSourceUri({ publisher: item.publisher, uri: item.official_uri });
}

function isCalendarDateOrThrow(value, what) {
  if (!isCalendarDate(value)) {
    throw new Error(`${what} is not a calendar date: ${JSON.stringify(value)}`);
  }
}

function renderItem(item, index, semantics) {
  const official = requireItem(item, index);
  const licence = LICENCES[item.licence];

  // Rights at compose time. A licence that does not embed text loses the text here, whatever
  // the caller passed, so a bundle cannot carry text the publisher did not license.
  const body = licence.embedsText
    ? `<blockquote class="bundle-text">${escapeHtml(item.text ?? '')}</blockquote>`
    : '<p class="bundle-withheld">Text withheld by licence. This item travels as its digest ' +
      'and its official link, which are enough to fetch and verify it at the publisher.</p>';

  const attribution = licence.attribution
    ? `<p class="bundle-attribution">${escapeHtml(item.attribution ?? '')}</p>`
    : '';

  return (
    `<li class="bundle-item" data-kind="${escapeHtml(item.kind)}">` +
    `<p class="bundle-citation">${escapeHtml(item.citation)}</p>` +
    '<dl class="bundle-dates">' +
    `<div class="strip-row"><dt>${escapeHtml(INTERVAL_TERM[semantics])}</dt><dd>${escapeHtml(item.valid_from)} to ` +
    `${escapeHtml(item.valid_to ?? 'no end recorded')}</dd></div>` +
    `<div class="strip-row"><dt>published</dt><dd>${escapeHtml(item.publication_date)}</dd></div>` +
    `<div class="strip-row"><dt>observed</dt><dd>${escapeHtml(item.observed_from)}</dd></div>` +
    `<div class="strip-row"><dt>record_sha256</dt><dd><code>${escapeHtml(item.record_sha256)}</code></dd></div>` +
    `<div class="strip-row"><dt>licence</dt><dd>${escapeHtml(item.licence)}</dd></div>` +
    '</dl>' +
    body +
    attribution +
    `<p class="bundle-official"><a href="${escapeHtml(official)}" rel="external">Official source</a></p>` +
    '</li>'
  );
}

/**
 * The register, as rows over a closed column set.
 *
 * @param {Array} items    the same items as the bundle
 * @param {Array} columns  a subset of REGISTER_COLUMNS, in any order
 */
export function renderRegister({ items, columns }) {
  if (!Array.isArray(columns) || columns.length === 0) {
    throw new Error('a register needs columns');
  }
  for (const column of columns) {
    const reason = FORBIDDEN_COLUMNS.get(column);
    if (reason !== undefined) {
      throw new Error(`the register has no ${column} column: ${reason}`);
    }
    if (!REGISTER_COLUMNS.includes(column)) {
      throw new Error(
        `${JSON.stringify(column)} is not a register column; the set is closed at ` +
          `${REGISTER_COLUMNS.join(', ')} so a header cannot introduce a claim the data does not make`,
      );
    }
  }
  if (new Set(columns).size !== columns.length) {
    throw new Error('a register column appears twice');
  }

  const head = columns.map((column) => `<th scope="col">${escapeHtml(column)}</th>`).join('');
  const rows = items
    .map((item, index) => {
      requireItem(item, index);
      const cells = columns
        .map((column) => `<td>${escapeHtml(item[column] ?? 'not recorded')}</td>`)
        .join('');
      return `<tr>${cells}</tr>`;
    })
    .join('');
  // A register is wide, so it scrolls in its own box rather than making the page scroll. A
  // scrollable box is keyboard-focusable in a browser whether or not it is asked to be, so
  // it is given a role and a name here rather than being left an anonymous tab stop.
  return (
    '<div class="bundle-register-scroll" role="region" tabindex="0" ' +
    'aria-label="Evidence register, scrollable">' +
    `<table class="bundle-register"><thead><tr>${head}</tr></thead>` +
    `<tbody>${rows}</tbody></table>` +
    '</div>'
  );
}

/**
 * The bundle: cover, items, register, verification annex, watermark.
 *
 * @param {object} input
 * @param {Array}  input.items
 * @param {Array}  input.columns
 * @param {object} input.verification  the recipe, the signing key and the fetch note
 */
export function renderEvidenceBundle({ items, columns, verification, semantics }) {
  if (!Array.isArray(items) || items.length === 0) {
    throw new Error('an evidence bundle with no items is a cover sheet');
  }

  for (const field of ['recompute_recipe', 'signing_key', 'fetch_note']) {
    if (typeof verification?.[field] !== 'string' || verification[field].trim().length === 0) {
      throw new Error(
        `the verification annex needs ${field}; a bundle nobody can check is a bundle that has ` +
          'to be trusted, and this product is built so that it does not have to be',
      );
    }
  }

  // Last, after every item and the annex, so this check shadows none of them. The
  // publisher's own vocabulary: every item's dates were labelled "applicable" regardless
  // of publisher, so an EU consolidation state was exported as an applicability claim the
  // publisher never made, inside the artefact a reader keeps and cites.
  requireSemantics(semantics, 'an evidence bundle');

  return (
    '<section class="evidence-bundle">' +
    `<p class="bundle-watermark">${escapeHtml(WATERMARK)}</p>` +
    `<ul class="bundle-items">${items
      .map((item, index) => renderItem(item, index, semantics))
      .join('')}</ul>` +
    renderRegister({ items, columns }) +
    '<section class="bundle-verification"><h2>How to verify this bundle</h2>' +
    `<p class="bundle-recipe">${escapeHtml(verification.recompute_recipe)}</p>` +
    `<p class="bundle-key">Signing key: <code>${escapeHtml(verification.signing_key)}</code></p>` +
    `<p class="bundle-fetch">${escapeHtml(verification.fetch_note)}</p>` +
    '</section>' +
    '</section>'
  );
}
