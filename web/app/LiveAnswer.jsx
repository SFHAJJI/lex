// The region a live screen's answer is written into, announced to a screen reader when it changes.
//
// A live region must exist before its content changes, or assistive technology has nothing to watch:
// so the server renders it around the idle or loading state, and the answer, the refusal card or the
// sentence a state carries replaces what is inside it. `polite` waits for the reader to finish what
// they are hearing; nothing on these screens is urgent enough to interrupt them. The launch contract
// asks for keyboard and screen-reader paths through the eight screens, and this is the screen-reader
// half of the answer.
//
// `Say` lays out one sentence of the interface copy table (`live-chrome.mjs`): the template's text with
// its values in their places, where a value may be an element (a quotation in its own language, an
// identifier set as code).

import { Fragment } from 'react';

import { fillParts } from '../scripts/live-chrome.mjs';

export function Say({ template, values }) {
  return fillParts(template, values).map((part, index) => (typeof part === 'string' ? part : <Fragment key={index}>{part}</Fragment>));
}

export function LiveAnswer({ children }) {
  return (
    <div className="live-answer" aria-live="polite" data-live-answer="">
      {children}
    </div>
  );
}
