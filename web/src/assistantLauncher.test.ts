import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8");
const panel = readFileSync(new URL("./AskPanel.tsx", import.meta.url), "utf8");

test("the closed launcher is fixed and the desktop panel reserves content space", () => {
  const launcherRules = [...css.matchAll(/\.asklaunch\s*\{([^}]*)\}/g)].map((match) => match[1]);
  const panel = css.match(/\.askpanel\s*\{([^}]*)\}/)?.[1] ?? "";

  assert.ok(launcherRules.length > 0, "the launcher must have an explicit style rule");
  assert.match(launcherRules[0], /position\s*:\s*fixed/,
    "the assistant must remain reachable while a reader moves through a long law");
  assert.match(panel, /position\s*:\s*fixed/,
    "the assistant is a stable side surface rather than part of legal text flow");
  assert.match(css, /body\.assistant-open\s+main[^}]*margin-right\s*:/s,
    "desktop content must reflow instead of being hidden behind the assistant");
});

test("the launcher is one compact control without a promotional hint bubble", () => {
  assert.doesNotMatch(panel, /askhint|dates, articles and changes/);
  assert.match(panel, />Ask Lex<\/span>/);
  assert.doesNotMatch(css, /\.askhint\s*\{/);
});

test("narrow screens use an explicit modal backdrop and preserve reduced motion", () => {
  assert.match(css, /@media\s*\(width\s*<\s*1100px\)/);
  assert.match(css, /\.askbackdrop\s*\{/);
  assert.match(css, /@media\s*\(prefers-reduced-motion:\s*reduce\)/);
});

test("the mobile surface is a portalled modal while desktop remains complementary", () => {
  assert.match(panel, /createPortal/,
    "the modal must sit outside the main region it makes inert");
  assert.match(panel, /body > header, body > main, body > footer/,
    "all page landmarks behind the mobile modal must become inert");
  assert.match(panel, /\.inert\s*=\s*true/);
  assert.doesNotMatch(panel, /<aside[^>]*role="dialog"/s,
    "a complementary desktop surface is not also a dialog");
  assert.match(panel, /<div[^>]*role="dialog"/s,
    "only the portalled mobile surface has modal-dialog semantics");
});
