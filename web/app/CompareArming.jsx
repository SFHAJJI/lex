// Arming a comparison from a result or timeline list.
//
// UX spec section 6: "row click = Read; two-row multi-select arms Compare". Arming is not
// comparing. The control becomes available; nothing is diffed until the reader asks.
//
// The rule worth building carefully is which pairs may arm at all. compare.mjs refuses a
// comparison of two different works, because two unrelated instruments are not states of each
// other and their differences are not legislation. That refusal is correct and it arrives too
// late: by then the reader has chosen two rows, pressed Compare, and been told no. A control
// that offers an action it will refuse teaches a reader that refusals are noise.
//
// So the same rule is applied at arming time, and the reason is said in the same words. The
// reader learns why the pair cannot be compared while both rows are still in front of them,
// which is the moment the information is useful.

import { useCallback, useMemo, useState } from 'react';

/** The publisher and work halves of a state identifier. */
function workOf(lexId) {
  const parts = String(lexId).split(':');
  return parts.length < 3 ? null : `${parts[0]}:${parts[1]}`;
}

/**
 * Why this selection cannot arm a comparison, or null when it can.
 *
 * Exported because the reason belongs to the rule rather than to the component, and the screen
 * that renders it and the test that proves it should read the same sentence.
 */
export function armingRefusal(selected) {
  if (selected.length < 2) return null;
  if (selected.length > 2) {
    return 'A comparison is between two states. Deselect one before comparing.';
  }
  const [left, right] = selected.map((state) => workOf(state.lex_id));
  if (left === null || right === null) {
    return 'One of these rows does not name a publisher, a work and a state, so it cannot be placed.';
  }
  if (left !== right) {
    return (
      'These are two different works. Two unrelated instruments are not states of each other, ' +
      'and their differences are not legislation.'
    );
  }
  return null;
}

/**
 * The arming control.
 *
 * @param {object} props
 * @param {Array} props.selected  the rows the reader has selected, in selection order
 * @param {Function} props.onCompare called when the reader asks for the comparison
 */
export function CompareArming({ selected, onCompare }) {
  const refusal = useMemo(() => armingRefusal(selected), [selected]);
  const armed = selected.length === 2 && refusal === null;
  const compare = useCallback(() => onCompare(selected), [onCompare, selected]);

  return (
    <div className="compare-arming">
      <p className="compare-arming-state" aria-live="polite">
        {selected.length === 0
          ? 'Select two states to compare them.'
          : selected.length === 1
            ? 'One state selected. Select a second to compare.'
            : refusal === null
              ? 'Two states of one work selected.'
              : refusal}
      </p>
      <button type="button" disabled={!armed} onClick={compare}>
        Compare the two selected states
      </button>
    </div>
  );
}

/**
 * Selection state for a list that can arm a comparison.
 *
 * Kept here so a list does not have to reimplement it, and so the two-row limit is one rule
 * rather than one per list.
 */
export function useCompareSelection() {
  const [selected, setSelected] = useState([]);
  const toggle = useCallback((state) => {
    setSelected((current) => {
      const without = current.filter((one) => one.lex_id !== state.lex_id);
      return without.length === current.length ? [...current, state] : without;
    });
  }, []);
  return { selected, toggle };
}
