// The ambiguous-version interstitial.
//
// UX spec section 5: two states cover the requested date, so the service refuses rather than
// picking one. The screen must not pick one either. That is the whole component: a modal that
// lists both candidates in full, offers a Read button for each and a side-by-side option, closes
// on Escape WITHOUT choosing, has no default selection and no "remember my choice".
//
// The rules here are not interaction polish. A default selection would answer a question the
// publisher left open, and "remember my choice" would answer it again silently on every later
// visit. The reader is here because the record is genuinely ambiguous; resolving it for them is
// the one thing this screen must never do.
//
// Focus containment is the same rule in the accessibility layer. If focus escapes to the page
// behind, a keyboard or screen-reader user can act on content that assumes a version was chosen.

import { useCallback, useEffect, useRef } from 'react';

import { STATE_PHRASE, semanticsOf } from '../scripts/publisher-vocabulary.mjs';

/** Elements that can hold focus inside the dialog, in document order. */
const FOCUSABLE = 'a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * One candidate, described in its own publisher's terms.
 *
 * The spec writes "applicable from {date}" because its worked example is Luxembourg. The word
 * belongs to the publisher, not to this screen, so it comes from the record.
 */
function Candidate({ candidate, publisher, onRead }) {
  const phrase = STATE_PHRASE[semanticsOf(publisher, 'an ambiguous-version candidate')];
  return (
    <li className="ambiguous-candidate">
      <span className="ambiguous-when">
        {phrase} {candidate.valid_from}
      </span>
      <span className="ambiguous-hash">
        hash <code>{candidate.hash.slice(0, 8)}</code>
      </span>
      <span className="ambiguous-published">published {candidate.publication_date}</span>
      <button type="button" onClick={() => onRead(candidate)}>
        Read the state {phrase} {candidate.valid_from}
      </button>
    </li>
  );
}

/**
 * The interstitial.
 *
 * @param {object} props
 * @param {string} props.publisher     whose record this is
 * @param {Array}  props.candidates    every state covering the requested date, in publisher order
 * @param {string} props.requestedDate the date that turned out to be ambiguous
 * @param {Function} props.onDismiss   called when the reader leaves without choosing
 * @param {Function} props.onRead      called with the candidate the reader chose
 * @param {Function} props.onCompare   called when the reader asks for both side by side
 */
export function AmbiguousVersion({
  publisher,
  candidates,
  requestedDate,
  onDismiss,
  onRead,
  onCompare,
}) {
  const dialog = useRef(null);
  const restoreTo = useRef(null);

  if (!Array.isArray(candidates) || candidates.length < 2) {
    throw new Error(
      'an ambiguous-version interstitial needs the states that made the date ambiguous; with ' +
        'fewer than two there is no ambiguity and this dialog asks the reader to resolve nothing',
    );
  }

  // Escape closes without choosing, and the key never reaches anything that would choose.
  const onKeyDown = useCallback(
    (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onDismiss();
        return;
      }
      if (event.key !== 'Tab') return;

      // Containment. Without it, Tab walks into the page behind, where the reader can act on
      // content that assumes a version was chosen.
      const focusable = [...dialog.current.querySelectorAll(FOCUSABLE)];
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const moving = event.shiftKey ? first : last;
      if (document.activeElement === moving) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
      }
    },
    [onDismiss],
  );

  useEffect(() => {
    // Focus moves into the dialog and returns where it came from on close, so a keyboard reader
    // is not dropped at the top of the document after dismissing.
    restoreTo.current = document.activeElement;
    dialog.current?.focus();
    return () => {
      if (restoreTo.current instanceof HTMLElement) restoreTo.current.focus();
    };
  }, []);

  return (
    <div
      className="ambiguous-version"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="ambiguous-title"
      aria-describedby="ambiguous-explanation"
      tabIndex={-1}
      ref={dialog}
      onKeyDown={onKeyDown}
    >
      <h2 id="ambiguous-title">Two states cover {requestedDate}</h2>
      <p id="ambiguous-explanation">
        The publisher holds more than one state covering that date, and has not ranked them. This
        service will not choose between them for you, and nothing here is preselected.
      </p>
      <ul className="ambiguous-candidates">
        {candidates.map((candidate) => (
          <Candidate
            key={candidate.hash}
            candidate={candidate}
            publisher={publisher}
            onRead={onRead}
          />
        ))}
      </ul>
      <button type="button" onClick={onCompare}>
        Show both side by side
      </button>
      <button type="button" onClick={onDismiss}>
        Close without choosing
      </button>
    </div>
  );
}
