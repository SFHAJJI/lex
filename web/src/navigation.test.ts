import test from "node:test";
import assert from "node:assert/strict";
import { shouldPush, type State } from "./state.ts";

// Back should undo "I opened another law", not "I nudged the date", and a destination the reader
// is already at is not a journey at all. The second rule is the one with a story: an assistant
// turn applies twice through the callback captured when the reader asked, so both applies compare
// the same pre-navigation state, both conclude the space changed, and the turn pushes two
// identical entries. The reader then presses Back, the URL does not change, no effect re-runs
// because no dependency changed, and the view is cleared into a loading state that never resolves.

const at = (s: Partial<State> = {}): State => ({ mode: "read", ...s } as State);
const ranking = { space: "time", from: "2024-01-01", until: "2024-12-31", order: "by_churn" };

test("a change of law, question or space is a journey", () => {
  assert.equal(shouldPush({ work: "lu-legilux:x" }, at(), "?work=x", "?other"), true);
  assert.equal(shouldPush({ q: "conge" }, at(), "?q=conge", "?other"), true);
  assert.equal(shouldPush({ space: "time" }, at(), "?space=time", "?other"), true);
});

test("nudging a control is not a journey", () => {
  assert.equal(shouldPush({ asOf: "2024-01-01" }, at({ q: "x" }), "?q=x&asOf=2024", "?q=x"), false);
});

test("a destination already in the address bar is never a journey", () => {
  // The regression. Every field says this is a new space, and it is not: the reader is there.
  assert.equal(
    shouldPush(ranking as Partial<State>, at(), "?space=time&from=2024-01-01", "?space=time&from=2024-01-01"),
    false);
});

test("the address-bar rule outranks an explicit request to push", () => {
  // The second apply passes the same explicit intent as the first. Honouring it is what produced
  // the duplicate entry, so this rule has to win over the caller rather than defer to it.
  assert.equal(shouldPush(ranking as Partial<State>, at(), "?same", "?same", true), false);
});

test("an explicit request still decides when the destination is genuinely new", () => {
  assert.equal(shouldPush({ asOf: "2024-01-01" }, at(), "?a", "?b", true), true);
  assert.equal(shouldPush({ work: "lu-legilux:x" }, at(), "?a", "?b", false), false);
});
