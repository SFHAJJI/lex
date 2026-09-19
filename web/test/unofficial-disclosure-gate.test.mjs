// The S5-A10 browser gate, held without a browser.
//
// `unofficialFailures` turns what the driven probe measured into failure sentences. The browser run
// proves the measurement; these tests prove the verdict: that each way an unofficial rendering can be
// the default view, carry a label nobody can see, or be unreachable, produces a failure that names
// it, and that the page this slice ships produces none.

import assert from "node:assert/strict";
import test from "node:test";

const WHERE = "reading.html @390/light";

const closed = (overrides = {}) => ({
  tag: "details",
  open: false,
  label: "◰ UNOFFICIAL Rendering in en",
  labelHidden: null,
  visible: false,
  shown: false,
  ...overrides,
});

const drive = (overrides = {}) => ({
  focused: true,
  opened: closed({ open: true, visible: true, shown: true }),
  closed: closed(),
  ...overrides,
});

const measured = (count = 1, overrides = {}) => ({
  load: Array.from({ length: count }, () => closed()),
  drives: Array.from({ length: count }, () => drive()),
  ...overrides,
});

async function gate() {
  const { unofficialFailures } = await import("../scripts/browser-evidence.mjs");
  return unofficialFailures;
}

test("closed, labelled renderings that each open and close on Enter pass", async () => {
  const unofficialFailures = await gate();
  assert.deepEqual(unofficialFailures(WHERE, measured(1), [false]), []);
  assert.deepEqual(unofficialFailures(WHERE, measured(2), [false, false]), []);
});

test("a page with no unofficial rendering passes, and a stray UNOFFICIAL control does not", async () => {
  const unofficialFailures = await gate();
  assert.deepEqual(unofficialFailures(WHERE, null, []), []);
  const stray = unofficialFailures(WHERE, null, [false]);
  assert.equal(stray.length, 1);
  assert.match(stray[0], /UNOFFICIAL disclosure control\(s\) with no rendering behind them/);
});

test("a rendering shown by default fails, however it is shown", async () => {
  const unofficialFailures = await gate();
  for (const shown of [{ open: true }, { visible: true }, { shown: true }]) {
    const failures = unofficialFailures(WHERE, measured(1, { load: [closed(shown)] }), [false]);
    assert.ok(
      failures.some((line) => /is shown by default .*S5-A10 says translation is never the default view/.test(line)),
      `${JSON.stringify(shown)}: ${JSON.stringify(failures)}`,
    );
  }
});

test("a rendering outside a disclosure fails as not a closed disclosure", async () => {
  const unofficialFailures = await gate();
  const failures = unofficialFailures(WHERE, measured(1, { load: [closed({ tag: "section" })] }), [false]);
  assert.ok(failures.some((line) => /is a <section>, not a closed disclosure/.test(line)), JSON.stringify(failures));
});

test("a control that does not say UNOFFICIAL fails", async () => {
  const unofficialFailures = await gate();
  const failures = unofficialFailures(WHERE, measured(1, { load: [closed({ label: "Rendering in en" })] }), [false]);
  assert.ok(failures.some((line) => /does not say UNOFFICIAL/.test(line)), JSON.stringify(failures));
});

test("a label in the markup that the page does not show fails, and names why", async () => {
  const unofficialFailures = await gate();
  for (const why of [
    "display, visibility or opacity hides it",
    "its box is 0x0 px",
    "its text colour is transparent",
    "something else is on top of it, or it is clipped or off the page",
  ]) {
    const failures = unofficialFailures(WHERE, measured(1, { load: [closed({ labelHidden: why })] }), [false]);
    assert.deepEqual(
      failures,
      [
        `${WHERE}: the UNOFFICIAL label of unofficial rendering 1 of 1 is in the markup but not shown: ` +
          `${why}; S5-A10 says clearly labelled unofficial`,
      ],
      why,
    );
  }
});

test("the accessibility tree must hold one collapsed UNOFFICIAL control per rendering", async () => {
  const unofficialFailures = await gate();
  const missing = unofficialFailures(WHERE, measured(1), []);
  assert.ok(missing.some((line) => /1 unofficial rendering\(s\) and 0 disclosure control\(s\)/.test(line)), JSON.stringify(missing));
  for (const state of [true, null]) {
    const failures = unofficialFailures(WHERE, measured(1), [state]);
    assert.ok(
      failures.some((line) => /not reported collapsed to a screen reader at load/.test(line)),
      `${state}: ${JSON.stringify(failures)}`,
    );
  }
});

test("a rendering the reader can never open fails, and the second of two is held too", async () => {
  const unofficialFailures = await gate();
  const unfocusable = unofficialFailures(WHERE, measured(1, { drives: [drive({ focused: false, opened: null, closed: null })] }), [false]);
  assert.ok(unfocusable.some((line) => /the UNOFFICIAL control of unofficial rendering 1 of 1 cannot take keyboard focus/.test(line)), JSON.stringify(unfocusable));
  const dead = unofficialFailures(WHERE, measured(1, { drives: [drive({ opened: closed() })] }), [false]);
  assert.ok(dead.some((line) => /Enter on the UNOFFICIAL control of unofficial rendering 1 of 1 did not show the rendering/.test(line)), JSON.stringify(dead));
  const stuck = unofficialFailures(WHERE, measured(1, { drives: [drive({ closed: closed({ open: true, shown: true }) })] }), [false]);
  assert.ok(stuck.some((line) => /did not close the rendering/.test(line)), JSON.stringify(stuck));
  const second = unofficialFailures(
    WHERE,
    measured(2, { drives: [drive(), drive({ focused: false, opened: null, closed: null })] }),
    [false, false],
  );
  assert.deepEqual(second, [`${WHERE}: the UNOFFICIAL control of unofficial rendering 2 of 2 cannot take keyboard focus`]);
});
