import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("the browser shell identifies the V3 product line", async () => {
  const html = await readFile(new URL("../src/index.html", import.meta.url), "utf8");

  assert.match(html, /<title>Lex V3<\/title>/);
  assert.match(html, /data-product-line="lex-v3"/);
});
