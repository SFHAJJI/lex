import assert from "node:assert/strict";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { GapExplanation } from "./unknownWorkNotice.ts";
import {
  UNKNOWN_WORK_BODY, UNKNOWN_WORK_CANDIDATES_HEADING, UNKNOWN_WORK_HEADING,
} from "./workCandidates.ts";

// These render the REAL component the workspace Gap delegates to, so a regression on the
// evidence-absent user path fails here, not only in a browser.
const render = (props: { status: string; explanation: string; candidates?: unknown }) =>
  renderToStaticMarkup(h(GapExplanation, props));

const EXPLANATION = "Lex does not hold this work at all.";

test("unknown_work with no candidate field renders the exact primary notice", () => {
  const html = render({ status: "unknown_work", explanation: EXPLANATION });
  assert.ok(html.includes(UNKNOWN_WORK_HEADING), "frozen heading must render");
  // renderToStaticMarkup escapes quotes, so compare against the escaped body bytes.
  const escapedBody = UNKNOWN_WORK_BODY.replace(/"/g, "&quot;");
  assert.ok(html.includes(escapedBody) || html.includes(UNKNOWN_WORK_BODY),
    "complete frozen body must render");
  assert.ok(!html.includes(UNKNOWN_WORK_CANDIDATES_HEADING),
    "no candidate subheading without candidates");
  assert.ok(!html.includes(EXPLANATION),
    "the bald non-holding sentence must not compete with the honest boundary");
  assert.ok(html.includes('data-testid="unknown-work-notice"'));
});

test("unknown_work with valid candidates renders one primary heading and one subheading", () => {
  const html = render({
    status: "unknown_work",
    explanation: EXPLANATION,
    candidates: [
      { work: "loi-2020-12-19-a1039", title: "Loi du 19 decembre 2020", publisher: "lu-legilux" },
    ],
  });
  // The heading appears exactly twice: once as the aria-label, once visible in <b>.
  assert.equal(html.split(`<b>${UNKNOWN_WORK_HEADING}</b>`).length - 1, 1,
    "exactly one visible primary heading");
  assert.equal(html.split(`<b>${UNKNOWN_WORK_CANDIDATES_HEADING}</b>`).length - 1, 1,
    "exactly one visible candidate subheading");
  assert.ok(html.includes('href="/lu-legilux/loi-2020-12-19-a1039"'),
    "link rebuilt from validated coordinates");
});

test("a wholly malformed candidate array keeps the primary notice and drops the list", () => {
  const html = render({
    status: "unknown_work",
    explanation: EXPLANATION,
    candidates: [{ work: "../..", publisher: "evil host" }, "junk", null],
  });
  assert.ok(html.includes(UNKNOWN_WORK_HEADING), "primary notice survives malformed evidence");
  assert.ok(!html.includes("<ul"), "no candidate list from invalid entries");
  assert.ok(!html.includes(UNKNOWN_WORK_CANDIDATES_HEADING));
});

test("any other gap status keeps the mapper explanation and no notice", () => {
  const html = render({ status: "no_version_for_date", explanation: "No version covers that date." });
  assert.ok(html.includes("No version covers that date."));
  assert.ok(!html.includes(UNKNOWN_WORK_HEADING));
  assert.ok(!html.includes("unknown-work-notice"));
});
