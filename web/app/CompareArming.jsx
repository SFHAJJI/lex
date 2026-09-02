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

import { useCallback, useId, useMemo, useState } from 'react';

import { identityOf } from '../scripts/record-identity.mjs';

/**
 * The work half of a state identifier, or null when the row does not name one.
 *
 * Through the shared strict reading rather than a local split. Counting the colons was the whole
 * check, so `:::` named the work `:` and two rows of nothing armed a comparison of nothing.
 * `compare.mjs` learned that the hard way and fixed it locally, which is why `record-identity.mjs`
 * exists: a screen cannot be more permissive about what a work is than the URL space it links
 * into.
 *
 * A refusal to parse becomes null rather than an exception, because the reader picked this row
 * and is owed a sentence about it while it is still in front of them.
 */
function workOf(lexId) {
  try {
    return identityOf(lexId, 'a selected row').workKey;
  } catch {
    return null;
  }
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
/** Whether this selection may arm the control at all. */
export function armedBy(selected) {
  return selected.length === 2 && armingRefusal(selected) === null;
}

/**
 * Compare, or refuse, and say which happened.
 *
 * The rule refuses, not the attribute. `aria-disabled` announces unavailability and does not
 * stop a click the way `disabled` does, which is the price of keeping the control in the tab
 * order; so the guard that actually holds is this one. Exported because it is the guard, and a
 * guard that can only be reached through a click nothing in this package can dispatch is a
 * guard nothing proves.
 *
 * @returns {boolean} whether the comparison was asked for
 */
export function compareIfArmed(selected, onCompare) {
  if (!armedBy(selected)) return false;
  onCompare(selected);
  return true;
}

export function CompareArming({ selected, onCompare }) {
  const refusal = useMemo(() => armingRefusal(selected), [selected]);
  const armed = armedBy(selected);
  const stateId = useId();
  const compare = useCallback(
    () => compareIfArmed(selected, onCompare),
    [onCompare, selected],
  );

  return (
    <div className="compare-arming">
      <p className="compare-arming-state" id={stateId} aria-live="polite">
        {selected.length === 0
          ? 'Select two states to compare them.'
          : selected.length === 1
            ? 'One state selected. Select a second to compare.'
            : refusal === null
              ? 'Two states of one work selected.'
              : refusal}
      </p>
      {/* aria-disabled, never the `disabled` attribute. A disabled button is removed from the
          tab order entirely, so a reader moving by keyboard never arrives at it, is never told
          that comparison exists, and never hears why this pair cannot be compared. The browser
          run measured exactly that on the composed screen: fifteen focusable elements and
          fourteen reachable by Tab, the missing one being this button.
          Saying why the control is unavailable is the whole point of arming early, and a
          control the reader cannot reach cannot say anything. So it stays reachable, states its
          own unavailability, points at the sentence that explains it, and `compareIfArmed`
          refuses the action. */}
      <button
        type="button"
        aria-disabled={!armed}
        aria-describedby={stateId}
        onClick={compare}
      >
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
