// The results listbox, with roving tabindex and the interpretation announcement above it.
//
// UX spec section 2: "results are a listbox with roving tabindex", "every match badge has a text
// label", and "the interpretation banner is aria-live='polite' so screen reader users hear that
// their query was rewritten before they hear results".
//
// That last clause is the one that matters, and it is an ordering rule rather than a styling one.
// A reader whose words were silently replaced is reading answers to a question they did not ask.
// A sighted reader sees the banner above the list; a screen reader user hears whatever comes
// first in the document. So the banner is before the list in DOM order, not merely above it in
// the layout, and there is a test for that rather than a comment.
//
// Roving tabindex exists because a listbox with fifty tabbable rows makes Tab useless: reaching
// the content after the results means fifty presses. One row is tabbable, arrows move between
// them, and the tab stop follows the reader rather than resetting.

import { useCallback, useEffect, useRef, useState } from 'react';

import { INTERVAL_SENTENCE, semanticsOf } from '../scripts/publisher-vocabulary.mjs';

/** The publisher half of a state identifier. */
function publisherOf(lexId) {
  return String(lexId).split(':')[0];
}

/**
 * What the search did to the query before it ran, announced before the results.
 *
 * Rendered even when nothing was relaxed, because a screen that only speaks when it rewrote
 * something teaches a reader that silence means their words were used, and silence is also what
 * a broken disclosure looks like.
 */
function Interpretation({ expansions, understoodAs }) {
  const rewritten = expansions.length > 0 || understoodAs !== null;
  return (
    <p className="results-interpretation" aria-live="polite">
      {rewritten ? (
        <>
          Your query was changed before it ran.
          {understoodAs === null ? null : ` Understood as: ${understoodAs}.`}
          {expansions.length === 0 ? null : ` Expansions applied: ${expansions.join(', ')}.`}
        </>
      ) : (
        'Your exact words were searched.'
      )}
    </p>
  );
}

/**
 * The results listbox.
 *
 * @param {object} props
 * @param {Array} props.hits          rows, each carrying its own lex_id so it keeps its publisher
 * @param {Array} [props.expansions]  substitutions applied before the query ran, verbatim
 * @param {string|null} [props.understoodAs] the editorial crosswalk's reading, if it fired
 * @param {Function} props.onOpen     called with the hit the reader opened
 */
export function ResultList({ hits, expansions = [], understoodAs = null, onOpen }) {
  if (!Array.isArray(hits) || hits.length === 0) {
    throw new Error(
      'a results listbox needs rows; an empty list is the no-hit card, which says what ran and ' +
        'what this corpus holds, and is not this component',
    );
  }

  // The tab stop, not the selection. Nothing here is selected: moving focus through candidates
  // is not choosing one, and a listbox that marked the focused row as selected would be
  // answering on the reader's behalf.
  const [focused, setFocused] = useState(0);
  const rows = useRef([]);
  const moved = useRef(false);

  useEffect(() => {
    // Only after a keypress. Focusing on first render would steal focus from the query field
    // the reader is still typing in.
    if (moved.current) rows.current[focused]?.focus();
  }, [focused]);

  const onKeyDown = useCallback(
    (event) => {
      const last = hits.length - 1;
      const next = {
        ArrowDown: Math.min(focused + 1, last),
        ArrowUp: Math.max(focused - 1, 0),
        Home: 0,
        End: last,
      }[event.key];
      if (next === undefined) return;
      event.preventDefault();
      moved.current = true;
      setFocused(next);
    },
    [focused, hits.length],
  );

  return (
    <>
      <Interpretation expansions={expansions} understoodAs={understoodAs} />
      <ul className="results-list" role="listbox" aria-label="Search results">
        {hits.map((hit, index) => {
          const semantics = semanticsOf(publisherOf(hit.lex_id), `hit ${index + 1}`);
          return (
            <li
              key={hit.lex_id}
              role="option"
              aria-selected="false"
              className="results-row"
              // Exactly one row is reachable by Tab. The rest are reachable by arrow, which is
              // what makes the content after a long list reachable at all.
              tabIndex={index === focused ? 0 : -1}
              ref={(el) => {
                rows.current[index] = el;
              }}
              onKeyDown={onKeyDown}
              onFocus={() => setFocused(index)}
            >
              <span className="results-title">{hit.title}</span>
              <span className="results-when">
                {INTERVAL_SENTENCE[semantics](hit.valid_from, hit.valid_to)}
              </span>
              {/* Every match badge carries a text label; colour and position are not the message. */}
              <span className="results-badge">{hit.match_label}</span>
              <button type="button" onClick={() => onOpen(hit)}>
                Read {hit.title}
              </button>
            </li>
          );
        })}
      </ul>
    </>
  );
}
