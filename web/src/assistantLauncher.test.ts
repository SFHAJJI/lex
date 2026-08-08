import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8");

test("the closed assistant launcher stays in flow while the opened panel may overlay", () => {
  const launcherRules = [...css.matchAll(/\.asklaunch\s*\{([^}]*)\}/g)].map((match) => match[1]);
  const slot = css.match(/\.askslot\s*\{([^}]*)\}/)?.[1] ?? "";
  const panel = css.match(/\.askpanel\s*\{([^}]*)\}/)?.[1] ?? "";
  const slotHeight = Number(slot.match(/min-height\s*:\s*(\d+)px/)?.[1] ?? 0);

  assert.ok(launcherRules.length > 0, "the launcher must have an explicit style rule");
  for (const rule of launcherRules) {
    assert.doesNotMatch(rule, /position\s*:\s*fixed/,
      "no base or responsive rule may put the closed launcher over legal controls and links");
  }
  assert.ok(slotHeight >= 55,
    "the slot must reserve the launcher's two text lines, padding and fractional line height");
  assert.match(panel, /position\s*:\s*fixed/,
    "the deliberately opened assistant remains a dialog over the workspace");
});
