import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8");

test("the closed assistant launcher stays in flow while the opened panel may overlay", () => {
  const launcher = css.match(/\.asklaunch\s*\{([^}]*)\}/)?.[1] ?? "";
  const slot = css.match(/\.askslot\s*\{([^}]*)\}/)?.[1] ?? "";
  const panel = css.match(/\.askpanel\s*\{([^}]*)\}/)?.[1] ?? "";

  assert.doesNotMatch(launcher, /position\s*:\s*fixed/,
    "a closed fixed launcher can cover legal controls and links");
  assert.match(slot, /min-height\s*:/,
    "opening the fixed dialog must not collapse its in-flow launcher slot");
  assert.match(panel, /position\s*:\s*fixed/,
    "the deliberately opened assistant remains a dialog over the workspace");
});
