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
import { INTERVAL_TERM, semanticsOf } from './publisher-vocabulary.mjs';
import { identityOf } from './record-identity.mjs';

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
  // The publisher is not a second, independent fact the caller supplies. It is the first
  // segment of the item's own identifier, and an item whose declared publisher disagrees with
  // its identifier has one of those two wrong. The official-source host check runs against the
  // declared value, so a disagreement here decides whose official hosts this item may link to.
  const identity = identityOf(item.identifier, where);
  if (item.publisher !== identity.publisher) {
    throw new Error(
      `${where} declares publisher ${JSON.stringify(item.publisher)} while its identifier names ` +
        `${JSON.stringify(identity.publisher)}; one of those is wrong, and the composer must not ` +
        'choose which, because that choice decides whose official hosts this item may link to',
    );
  }

  // D38, honoured here because an evidence bundle is a public surface and the decision says
  // every public surface honours the gate. It was not honoured at all: the composer asked the
  // caller-supplied licence label whether the text could travel, so a caller who wrote `cc0`
  // over a Legilux item exported the publisher's text on the strength of their own label. The
  // licence is a term of the grant. `text_public` is whether the grant was established, with
  // recorded evidence (C2). Only the second can unlock a body.
  //
  // Absence is refused rather than read as false. C2 says the flag starts false, so false is the
  // safe reading, but "the publisher's gate is closed" and "this payload never said" are
  // different facts, and D38 exists to keep withholding reasons distinct. Treating a missing
  // field as a closed gate hides a payload defect behind a correct-looking refusal.
  if (typeof item.text_public !== 'boolean') {
    throw new Error(
      `${where} does not carry text_public; an evidence bundle is a public surface, D38 says ` +
        'every public surface honours that gate, and an item that never states it cannot be ' +
        'composed either way',
    );
  }

  return publisherSourceUri({ publisher: item.publisher, uri: item.official_uri });
}

function isCalendarDateOrThrow(value, what) {
  if (!isCalendarDate(value)) {
    throw new Error(`${what} is not a calendar date: ${JSON.stringify(value)}`);
  }
}

function renderItem(item, index) {
  const official = requireItem(item, index);
  const licence = LICENCES[item.licence];
  // Per item, not per bundle. A bundle may carry Luxembourg and Union items together, and one
  // vocabulary spread over both prints an applicability claim onto a consolidation that the
  // Union never said applied to anything.
  const semantics = semanticsOf(item.publisher, `bundle item ${index + 1}`);

  // Two gates, refusing for different reasons, so they say so separately. `text_public` is
  // whether the publisher's rights position was established with recorded evidence (C2, D38);
  // the licence's own terms are whether that grant lets the body itself travel. A reader told
  // only "withheld" cannot tell which, and one of the two may change tomorrow while the other
  // will not.
  const body = !item.text_public
    ? '<p class="bundle-withheld">Text withheld: this publisher\'s text gate has not cleared, ' +
      'so no public surface of this service carries the body. The digest and the official link ' +
      'are enough to fetch and verify it at the publisher.</p>'
    : licence.embedsText
      ? `<blockquote class="bundle-text">${escapeHtml(item.text ?? '')}</blockquote>`
      : '<p class="bundle-withheld">Text withheld by licence. This item travels as its digest ' +
        'and its official link, which are enough to fetch and verify it at the publisher.</p>';

  // Attribution travels with any body, whatever the licence says. A licence that waives
  // attribution waives an obligation; it does not make it acceptable to leave the source
  // unnamed. This artefact exists to be checked against the publisher, and a body with no
  // publisher named cannot be checked against anything. The licence table decides obligations,
  // not provenance.
  const carriesBody = item.text_public && licence.embedsText;
  if (
    carriesBody &&
    (typeof item.attribution !== 'string' || item.attribution.trim().length === 0)
  ) {
    throw new Error(
      `bundle item ${index + 1} exports the publisher's text and names no attribution; the ` +
        'licence may waive that obligation, but a bundle carrying a body without saying whose ' +
        'it is cannot be checked against the source it came from',
    );
  }
  const attribution =
    licence.attribution || carriesBody
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
export function renderEvidenceBundle({
  items,
  columns,
  verification,
  semantics: declaredSemantics,
}) {
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

  // Last, after every item and the annex, so this check shadows none of them.
  //
  // There is no bundle-wide vocabulary any more, and one could never have been right: a bundle
  // may carry Luxembourg and Union items side by side, so a single value describes at most half
  // of them. Each item is now labelled from its own publisher. Passing one is refused rather
  // than ignored, because a caller who believes they are choosing it has misunderstood the
  // contract and silently overriding them leaves them believing it worked.
  if (declaredSemantics !== undefined) {
    throw new Error(
      'an evidence bundle does not take a date vocabulary; it is a property of the ' +
        'publisher of each item, and a bundle may span publishers, so no single value can ' +
        'describe every item',
    );
  }

  return (
    '<section class="evidence-bundle">' +
    `<p class="bundle-watermark">${escapeHtml(WATERMARK)}</p>` +
    `<ul class="bundle-items">${items
      .map((item, index) => renderItem(item, index))
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
