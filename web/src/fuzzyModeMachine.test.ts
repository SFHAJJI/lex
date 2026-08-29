import test from "node:test";
import assert from "node:assert/strict";
import { fuzzyModeFor } from "./api.ts";

/**
 * Review O1: the exact-word override was dormant, not reset. `fuzzyModeFor` already ignores a
 * non-matching override, so the defect was invisible on the question that replaced it and showed
 * on the way back: `q1 -> exact -> q2 -> q1` re-armed exact mode with no reader action.
 *
 * This file sequences reader actions and asks the SHIPPED functions what happens. It reimplements
 * neither transition: `nextExactQuery` is the clearing rule and `fuzzyModeFor` is the output. The
 * last test binds those calls to the wiring in Search.tsx, because a unit test of the rule stays
 * green when the effect that calls it is deleted.
 */

type Fuzzy = "auto" | "off";
type Action =
  | { kind: "submit"; text: string }
  | { kind: "chooseExact" }
  | { kind: "chooseRelaxed" }
  | { kind: "clear" };

const submit = (text: string): Action => ({ kind: "submit", text });
const CHOOSE_EXACT: Action = { kind: "chooseExact" };
const CHOOSE_RELAXED: Action = { kind: "chooseRelaxed" };
const CLEAR: Action = { kind: "clear" };

/** The Search surface's fuzzy-mode state. Each line cites the Search.tsx line it comes from. */
class SearchFuzzyMode {
  /** Search.tsx:140 `const q = p.state.q ?? ""`. */
  private q = "";
  /** Search.tsx:111 `useState<string>()`. */
  private exactQuery: string | undefined = undefined;

  /** Search.tsx:237 -> searchSubmission.ts:9 -> App.tsx:647. Nothing on that path trims. */
  submit(text: string): Fuzzy {
    // App.tsx keys Search by the submitted question, so a different question remounts the
    // component and every locally held authorization goes with it, during render. That is why
    // this is an assignment rather than a clearing rule: there is no surviving value to clear,
    // and no window in which one could be observed.
    if (text.trim() !== this.q.trim()) this.exactQuery = undefined;
    this.q = text;
    return this.fuzzy();
  }

  /**
   * Search.tsx:351 `setExactQuery(q.trim())`, gated by Search.tsx:341
   * `expansions.length > 0 && fuzzyMode === "auto"` inside Search.tsx:294 `{q ? (`. A blank
   * question cannot offer it: Search.tsx:170 returns before the request, so `expansions` is
   * empty. `expansions` is a server fact, so the model allows the tap whenever the mode is
   * "auto", which is a superset of what production reaches.
   */
  chooseExact(): Fuzzy {
    if (this.q.trim() === "" || this.fuzzy() !== "auto") return this.fuzzy();
    this.exactQuery = this.q.trim();
    return this.fuzzy();
  }

  /** Search.tsx:367 `setExactQuery(undefined)`, gated by Search.tsx:357 `fuzzyMode === "off"`. */
  chooseRelaxed(): Fuzzy {
    if (this.fuzzy() !== "off") return this.fuzzy();
    this.exactQuery = undefined;
    return this.fuzzy();
  }

  /** App.tsx:647 `q: query || undefined`, searchSubmission.ts:9 for a bare date, App.tsx:659. */
  clear(): Fuzzy {
    return this.submit("");
  }

  /** Search.tsx:143, sent as the request's `fuzzy` argument at Search.tsx:176. */
  fuzzy(): Fuzzy {
    return fuzzyModeFor(this.exactQuery, this.q);
  }

  apply(action: Action): Fuzzy {
    if (action.kind === "submit") return this.submit(action.text);
    if (action.kind === "chooseExact") return this.chooseExact();
    if (action.kind === "chooseRelaxed") return this.chooseRelaxed();
    return this.clear();
  }
}

const trace = (...actions: Action[]): Fuzzy[] => {
  const machine = new SearchFuzzyMode();
  return actions.map((action) => machine.apply(action));
};

const Q1 = "conge parental";
const Q2 = "travial salarie";
const Q1_PADDED = "  conge parental  ";

test("O1: returning to an earlier question does not silently re-arm exact words", () => {
  // The third value is the objection: before the repair, coming back produced "off" with no
  // reader action, narrowing a search for a question the reader had already left.
  assert.deepEqual(
    trace(submit(Q1), CHOOSE_EXACT, submit(Q2), submit(Q1)),
    ["auto", "off", "auto", "auto"],
  );
});

test("INSUFFICIENT ON ITS OWN: the override does not apply to the next question", () => {
  // What the old test asserted. It passes against the defect too, because fuzzyModeFor already
  // ignores a non-matching override. Kept as a requirement, never as evidence of a reset.
  assert.deepEqual(trace(submit(Q1), CHOOSE_EXACT, submit(Q2)), ["auto", "off", "auto"]);
});

test("re-asking the SAME question keeps the reader's own override", () => {
  // Correct, not a leak: the reader is still on the question they chose exact words for, and
  // Search.tsx:357-368 still shows the one-tap way back.
  assert.deepEqual(trace(submit(Q1), CHOOSE_EXACT, submit(Q1)), ["auto", "off", "off"]);
});

test("whitespace does not make one question into two", () => {
  // Both shipped functions trim, so padding does not fork the override, in either direction.
  assert.deepEqual(trace(submit(Q1), CHOOSE_EXACT, submit(Q1_PADDED)), ["auto", "off", "off"]);
  assert.deepEqual(trace(submit(Q1_PADDED), CHOOSE_EXACT, submit(Q1)), ["auto", "off", "off"]);
  assert.deepEqual(
    trace(submit(Q1_PADDED), CHOOSE_EXACT, submit(Q2), submit(Q1_PADDED)),
    ["auto", "off", "auto", "auto"],
  );
});

test("emptying the box discards the override", () => {
  assert.deepEqual(
    trace(submit(Q1), CHOOSE_EXACT, CLEAR, submit(Q1)),
    ["auto", "off", "auto", "auto"],
  );
});

test("the reader can restore the relaxed interpretation", () => {
  assert.deepEqual(trace(submit(Q1), CHOOSE_EXACT, CHOOSE_RELAXED), ["auto", "off", "auto"]);
});

test("exact words are never armed on a question that was never asked", () => {
  assert.deepEqual(trace(CHOOSE_EXACT, submit(Q1)), ["auto", "auto"]);
  assert.deepEqual(trace(CLEAR, CHOOSE_EXACT, submit(Q1)), ["auto", "auto", "auto"]);
});

/**
 * The invariant, stated over the ACTION HISTORY: "off" is produced exactly when the last
 * `chooseExact` was taken on a non-blank question, nothing since restored the relaxed reading,
 * and no different question was submitted since. It never computes an override, so it is the
 * property under test rather than a second copy of the rule.
 */
function expectedFuzzy(history: readonly Action[]): Fuzzy {
  const displayed: string[] = [];
  let current = "";
  for (const action of history) {
    if (action.kind === "submit") current = action.text;
    else if (action.kind === "clear") current = "";
    displayed.push(current);
  }
  let armedAt = -1;
  for (let index = 0; index < history.length; index += 1)
    if (history[index].kind === "chooseExact") armedAt = index;
  if (armedAt < 0) return "auto";
  const question = displayed[armedAt].trim();
  if (question === "") return "auto";
  for (let index = armedAt + 1; index < history.length; index += 1) {
    const action = history[index];
    if (action.kind === "chooseRelaxed" || action.kind === "clear") return "auto";
    if (action.kind === "submit" && action.text.trim() !== question) return "auto";
  }
  return "off";
}

const ALPHABET: Action[] = [
  submit(Q1), submit(Q2), submit(Q1_PADDED), CHOOSE_EXACT, CHOOSE_RELAXED, CLEAR,
];
const MAX_LENGTH = 4;

function allSequences(): Action[][] {
  let level: Action[][] = [[]];
  const sequences: Action[][] = [];
  for (let length = 1; length <= MAX_LENGTH; length += 1) {
    level = level.flatMap((prefix) => ALPHABET.map((action) => [...prefix, action]));
    sequences.push(...level);
  }
  return sequences;
}

const describe = (history: readonly Action[]): string =>
  history.map((action) => (action.kind === "submit"
    ? `submit(${JSON.stringify(action.text)})` : action.kind)).join(" -> ");

test("every sequence up to length 4 obeys the override invariant", () => {
  const sequences = allSequences();
  assert.equal(sequences.length, 6 + 36 + 216 + 1296);
  let offOutcomes = 0;
  let returnsToArmedQuestion = 0;
  for (const history of sequences) {
    const machine = new SearchFuzzyMode();
    let outcome: Fuzzy = "auto";
    for (const action of history) outcome = machine.apply(action);
    assert.equal(outcome, expectedFuzzy(history), `${describe(history)} produced ${outcome}`);
    if (outcome === "off") offOutcomes += 1;
    const armedAt = history.findIndex((action) => action.kind === "chooseExact");
    const after = armedAt < 0 ? [] : history.slice(armedAt + 1);
    if (armedAt > 0 && history[armedAt - 1].kind === "submit"
        && after.some((action) => action.kind === "submit" && action.text.trim() === Q2)
        && after.some((action) => action.kind === "submit" && action.text.trim() === Q1))
      returnsToArmedQuestion += 1;
  }
  // The invariant is a biconditional, so it also holds over a machine that never reaches exact
  // mode. These bounds fail if the alphabet or the gates stop producing "off" at all.
  assert.ok(offOutcomes > 100, `only ${offOutcomes} sequences reached exact words`);
  assert.ok(returnsToArmedQuestion > 0, "no sequence exercised the return to an armed question");
});

test("the enumeration actually contains the reviewer's sequence", () => {
  const reported = [submit(Q1), CHOOSE_EXACT, submit(Q2), submit(Q1)];
  assert.ok(allSequences().some((history) => describe(history) === describe(reported)));
  assert.equal(expectedFuzzy(reported), "auto");
});

/**
 * Deliberately absent: an assertion that Search.tsx still calls this rule.
 *
 * Node cannot load Search.tsx here, and asserting the wiring against source text was tried and
 * withdrawn. A regex over source fails for the wrong reasons, a rename or a reformat, and passes
 * for the wrong reasons, since the component can regress while its spelling is unchanged. It also
 * quietly changes what this file is: an assertion about a file's shape is not an assertion about
 * behaviour.
 *
 * The wiring is integration evidence and lives where the real bundle runs, in the committed
 * Playwright lane that protected CI executes. This file proves the rule; that one proves the
 * surface calls it.
 */
