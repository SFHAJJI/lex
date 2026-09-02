// The three qualifications a dated object carries, as React.
//
// Every rule stays in `scripts/state-qualifiers.mjs`. `validityConflictSentence`,
// `provisionalSentence` and `holeSentence` each decide what is being claimed and refuse the
// inputs that would make the claim false; this file decides only which token carries the
// sentence. Both renderers call the same three functions, so a badge cannot mean one thing in
// the string surface and another here.
//
// None of the three takes a flag saying whether to appear, and none takes a "hide" parameter.
// A conflict is two publisher dates that differ, a provisional state is a date against the
// reader's date, and a hole is a period with a kind. Each of those was once a boolean
// somebody passed, and a boolean somebody passed is a fact about the caller.
//
// The token is an icon, a label and text rather than a colour, because a meaning carried by
// colour alone is a meaning half the readers of this product do not receive.

import {
  holeSentence,
  provisionalSentence,
  validityConflictSentence,
} from '../scripts/state-qualifiers.mjs';
import { Mark } from './RefusalCard.jsx';

/**
 * Two publisher dates on one wording, shown as both and resolved as neither.
 *
 * @param {object} props
 * @param {string} props.stateValidFrom    the enclosing state's applicability date
 * @param {string} props.wordingValidFrom  the publisher's date on this wording
 */
export function ValidityConflict({ stateValidFrom, wordingValidFrom }) {
  return (
    <p className="validity-conflict">
      <Mark name="--conflict">
        {validityConflictSentence({ stateValidFrom, wordingValidFrom })}
      </Mark>
    </p>
  );
}

/**
 * A state the publisher has scheduled and which has not begun.
 *
 * The comparison date is a prop rather than the machine clock: whether a state is provisional
 * is a fact about the reader's chosen date and the publisher's record, and a component that
 * consulted its own clock would answer a question nobody asked and answer it differently
 * tomorrow.
 */
export function Provisional({ validFrom, asOf }) {
  return (
    <p className="provisional-watermark" data-provisional="true">
      <Mark name="--provisional">{provisionalSentence({ validFrom, asOf })}</Mark>
    </p>
  );
}

/**
 * A period no held state covers, captioned with which of the two claims it is.
 *
 * `holeSentence` refuses any kind outside the closed pair, so by the time `kind` reaches the
 * class name and the data attribute it is one of two known tokens rather than caller text.
 */
export function Hole({ kind, from, to }) {
  const sentence = holeSentence({ kind, from, to });
  return (
    <p className={`hole hole-${kind}`} data-hole-kind={kind}>
      <Mark name="--hole">{sentence}</Mark>
    </p>
  );
}
