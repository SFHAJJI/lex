// The S5-A10 browser gate, held without a browser.
//
// `unofficialFailures` turns what the driven probe measured into failure sentences. The browser run
// proves the measurement; these tests prove the verdict: that each way an unofficial rendering can be
// the default view, or be unreachable, produces a failure that names it, and that the page this slice
// ships produces none.

import assert from "node:assert/strict";
import test from "node:test";

const WHERE = "reading.html @390/light";

const closed = (overrides = {}) => ({
  tag: "details",
  open: false,
  label: "◰ UNOFFICIAL Rendering in en",
  visible: false,
  shown: false,
  ...overrides,
});

const measured = (overrides = {}) => ({
  load: [closed()],
  focused: true,
  opened: closed({ open: true, visible: true, shown: true }),
  closed: closed(),
  ...overrides,
});

async function gate() {
  const { unofficialFailures } = await import("../scripts/browser-evidence.mjs");
  return unofficialFailures;
}

test("a closed, labelled rendering that opens and closes on Enter passes", async () => {
  const unofficialFailures = await gate();
  assert.deepEqual(unofficialFailures(WHERE, measured(), [false]), []);
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
    const failures = unofficialFailures(WHERE, measured({ load: [closed(shown)] }), [false]);
    assert.ok(
      failures.some((line) => /is shown by default .*S5-A10 says translation is never the default view/.test(line)),
      `${JSON.stringify(shown)}: ${JSON.stringify(failures)}`,
    );
  }
});

test("a rendering outside a disclosure fails as not a closed disclosure", async () => {
  const unofficialFailures = await gate();
  const failures = unofficialFailures(WHERE, measured({ load: [closed({ tag: "section" })] }), [false]);
  assert.ok(failures.some((line) => /is a <section>, not a closed disclosure/.test(line)), JSON.stringify(failures));
});

test("a control that does not say UNOFFICIAL fails", async () => {
  const unofficialFailures = await gate();
  const failures = unofficialFailures(WHERE, measured({ load: [closed({ label: "Rendering in en" })] }), [false]);
  assert.ok(failures.some((line) => /does not say UNOFFICIAL/.test(line)), JSON.stringify(failures));
});

test("the accessibility tree must hold one collapsed UNOFFICIAL control per rendering", async () => {
  const unofficialFailures = await gate();
  const missing = unofficialFailures(WHERE, measured(), []);
  assert.ok(missing.some((line) => /1 unofficial rendering\(s\) and 0 disclosure control\(s\)/.test(line)), JSON.stringify(missing));
  for (const state of [true, null]) {
    const failures = unofficialFailures(WHERE, measured(), [state]);
    assert.ok(
      failures.some((line) => /not reported collapsed to a screen reader at load/.test(line)),
      `${state}: ${JSON.stringify(failures)}`,
    );
  }
});

test("a rendering the reader can never open fails", async () => {
  const unofficialFailures = await gate();
  const unfocusable = unofficialFailures(WHERE, measured({ focused: false, opened: null, closed: null }), [false]);
  assert.ok(unfocusable.some((line) => /cannot take keyboard focus/.test(line)), JSON.stringify(unfocusable));
  const dead = unofficialFailures(WHERE, measured({ opened: closed() }), [false]);
  assert.ok(dead.some((line) => /Enter on the UNOFFICIAL control did not show the rendering/.test(line)), JSON.stringify(dead));
  const stuck = unofficialFailures(WHERE, measured({ closed: closed({ open: true, shown: true }) }), [false]);
  assert.ok(stuck.some((line) => /did not close the rendering/.test(line)), JSON.stringify(stuck));
});
