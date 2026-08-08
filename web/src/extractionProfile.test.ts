import assert from "node:assert/strict";
import test from "node:test";
import { extractionDisclosure } from "./extractionProfile.ts";

test("every Memorial extraction profile receives the stronger gazette disclosure", () => {
  assert.equal(extractionDisclosure("pdf-memorial-lu/1"), "gazette");
  assert.equal(extractionDisclosure("pdf-memorial-lu/2"), "gazette");
});

test("a publisher PDF and publisher-marked text retain their distinct disclosures", () => {
  assert.equal(extractionDisclosure("pdf-lu/1"), "publisher-pdf");
  assert.equal(extractionDisclosure("akn-lu/1"), "publisher-markup");
  assert.equal(extractionDisclosure(undefined), "publisher-markup");
});
