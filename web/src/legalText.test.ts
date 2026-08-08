import assert from "node:assert/strict";
import test from "node:test";
import { legalDisplayText } from "./legalText.ts";

test("comparison text removes Markdown presentation without removing legal wording", () => {
  const markdown = "**1.** A **regulated** entity\n\n- keeps records\n- reports changes";

  assert.equal(
    legalDisplayText(markdown),
    "1. A regulated entity\n\nkeeps records\n\nreports changes",
  );
});

test("comparison text keeps literal legal punctuation and table cells", () => {
  const markdown = "The symbols * and ** remain when they are not Markdown.\n\n| Rate | Value |\n| --- | --- |\n| A | 5 % |";

  assert.equal(
    legalDisplayText(markdown),
    "The symbols * and ** remain when they are not Markdown.\n\nRate | Value\nA | 5 %",
  );
});

test("comparison text removes profile emphasis that is invalid only because publisher spans touch", () => {
  assert.equal(
    legalDisplayText("amount follows:*TREA = max{U-TREA; x · S-TREA}*where:TREA is defined"),
    "amount follows:TREA = max{U-TREA; x · S-TREA}where:TREA is defined",
  );
});
