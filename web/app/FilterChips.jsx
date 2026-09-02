// Filter chips, as toggle buttons.
//
// UX spec section 9: "chips are toggle buttons with aria-pressed". The attribute is the whole
// point rather than a detail. A chip communicates its state three ways to a sighted reader
// (fill, border, position) and none of them reach a screen reader, so without aria-pressed a
// reader is told there is a filter and not whether it is on.
//
// That matters more here than on an ordinary site. A filter narrows what the result set is a
// statement about. A reader who cannot tell that a filter is active reads a partial answer as a
// whole one, which is the same failure as a hit list implying a population it did not measure.
//
// So the count of what is filtered out is shown beside the chips, not only the count of what
// remains. "Showing 12" is a fact about the page; "12 of 47, 35 hidden by filters" is a fact
// about the corpus and the filters together, and only the second lets a reader judge whether to
// turn one off.

import { useCallback } from 'react';

/**
 * A group of filters, each on or off, each saying which it is.
 *
 * `hides`, the count of rows a chip was removing, used to be part of this contract. It was never
 * read, so nothing would have noticed it being wrong, and it was derivable from the rows the
 * caller was already filtering. A renderer that accepts a fact it can derive is a renderer a
 * caller can contradict, and an unread one is worse: it is a contradiction nobody can see.
 *
 * @param {object} props
 * @param {Array} props.filters   `{ key, label, active }`
 * @param {number} props.total    rows before any filter
 * @param {number} props.shown    rows after all filters
 * @param {Function} props.onToggle called with the filter key
 */
export function FilterChips({ filters, total, shown, onToggle }) {
  if (!Array.isArray(filters) || filters.length === 0) {
    throw new Error(
      'a filter group with no filters is chrome that does nothing; render nothing instead, so a ' +
        'reader is not shown a control that cannot change what they are reading',
    );
  }
  // Negative counts refused as well as impossible ones. Only `shown > total` was checked, so a
  // negative `shown` passed and the group reported hiding more rows than it ever held, which is
  // the same page-describing-a-corpus-it-does-not-have failure with the sign flipped.
  if (
    !Number.isInteger(total) ||
    !Number.isInteger(shown) ||
    shown > total ||
    shown < 0 ||
    total < 0
  ) {
    throw new Error(
      'a filter group states how many rows it is hiding, so it needs the count before and after; ' +
        'showing only what remains lets a filtered page read as a whole result set',
    );
  }

  const toggle = useCallback((key) => () => onToggle(key), [onToggle]);
  const hidden = total - shown;
  const anyActive = filters.some((filter) => filter.active);

  return (
    <div className="filter-chips">
      <ul className="filter-chip-list" aria-label="Filters">
        {filters.map((filter) => (
          <li key={filter.key}>
            <button
              type="button"
              className="filter-chip"
              // The state, not a style. Without this a screen reader announces a button and
              // gives no way to know the filter is on.
              aria-pressed={filter.active}
              onClick={toggle(filter.key)}
            >
              {filter.label}
            </button>
          </li>
        ))}
      </ul>
      {/* Both numbers, always. A page that reports only what it shows cannot be told apart from
          one with nothing filtered, and a reader cannot judge whether to turn a filter off. */}
      <p className="filter-count" aria-live="polite">
        {anyActive
          ? `Showing ${shown} of ${total}. ${hidden} hidden by filters you can turn off.`
          : `Showing all ${total}. No filter is active.`}
      </p>
    </div>
  );
}
