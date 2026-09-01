// S10, the export composer, in React.
//
// The rules live in `scripts/export-composer.mjs`. This file renders them and decides nothing:
// `exportComposerModel` validates the cart, derives each item's disposition from `text_public`
// and the licence, and returns the counts. If this component derived one of those itself, the
// preview and the exported bundle could disagree about the same item, which is precisely the
// failure this screen exists to prevent.
//
// The sentences that carry the meaning are imported constants rather than JSX prose, so the two
// surfaces cannot drift on the wording of a rights claim.

import {
  COMPOSE_TIME_NOTE,
  EMPTY_CART_NOTE,
  WATERMARK_PREVIEW,
  exportComposerModel,
  withheldSummary,
} from '../scripts/export-composer.mjs';

const DISPOSITION_SENTENCE = {
  travels_with_text: 'The full text travels in the bundle, with its attribution line.',
  withheld_by_licence:
    'The licence does not let the text travel. This item exports as its digest and its ' +
    'official link, which are enough to fetch and check it.',
  withheld_by_rights:
    'No public-text right has been established for this item, so no text may travel. It ' +
    'exports as its digest and its official link.',
};

/** One pinned item: what it is, and what it will contribute. */
function CartItem({ row }) {
  return (
    <li className={`compose-item compose-${row.disposition}`}>
      <code className="compose-id">{row.lex_id}</code>
      <span className="compose-interval">
        {`${row.valid_from} to ${row.valid_to === null ? 'open' : row.valid_to}`}
      </span>
      <code className="compose-digest">{row.record_sha256.slice(0, 8)}</code>
      <span className="compose-licence">{row.licence}</span>
      {/* A sentence, not a badge. A badge reading "withheld" beside a licence name would leave
          the reason to adjacency, and adjacency is not a statement. */}
      <span className="compose-disposition">{DISPOSITION_SENTENCE[row.disposition]}</span>
      <a className="compose-official" href={row.official_uri} rel="external">
        Official source
      </a>
    </li>
  );
}

/**
 * The export composer.
 *
 * @param {object} props the same shape `exportComposerModel` takes
 */
export function ExportComposer(props) {
  const model = exportComposerModel(props);

  if (model.rows.length === 0) {
    // Not an empty list. An empty cart and a cart nobody has filled look identical and are not
    // the same thing, and only one of them has an action attached.
    return (
      <section className="export-composer export-composer-empty">
        <h2>Export composer</h2>
        <p className="compose-empty">{EMPTY_CART_NOTE}</p>
      </section>
    );
  }

  const summary = withheldSummary(model);

  return (
    <section className="export-composer">
      <h2>Export composer</h2>
      {/* Said once, above the cart, because it governs every row rather than any one of them. */}
      <p className="compose-note">{COMPOSE_TIME_NOTE}</p>
      <dl className="compose-matter">
        <dt>Matter</dt>
        <dd>{model.matter.reference}</dd>
        <dt>Prepared by</dt>
        <dd>{model.matter.author}</dd>
      </dl>
      {summary === null ? null : <p className="compose-withheld">{summary}</p>}
      <ol className="compose-items">
        {model.rows.map((row) => (
          <CartItem key={`${row.lex_id}-${row.record_sha256}`} row={row} />
        ))}
      </ol>
      <section className="compose-register">
        <h3>Register columns</h3>
        <ul className="compose-columns">
          {model.columns.map((column) => (
            <li key={column}>
              <code>{column}</code>
            </li>
          ))}
        </ul>
      </section>
      {/* Shown before composing rather than discovered on the exported file. */}
      <p className="compose-watermark">{WATERMARK_PREVIEW}</p>
    </section>
  );
}
