// The scope of a browser evidence run, held without a browser.
//
// A mutation sweep measured every page for every mutation: a defect on one page was looked for on
// the thirty-two it cannot touch, at about two minutes a mutation. A run can now be scoped to the
// pages a mutation declares. What must not follow is a scoped run that reads like a full one, or a
// scope that names a page the build never emitted and reports it clean.

import assert from "node:assert/strict";
import test from "node:test";

const BUILT = [
  "citation-checker.html",
  "hydration.html",
  "reading.html",
  "search-react.html",
  "trust-surface.html",
];

async function gate() {
  const { pagesInScope } = await import("../scripts/browser-evidence.mjs");
  return pagesInScope;
}

test("no scope asked for measures every page the build emitted", async () => {
  const pagesInScope = await gate();
  assert.deepEqual(pagesInScope(BUILT, null), BUILT);
  assert.deepEqual(pagesInScope(BUILT, undefined), BUILT);
  assert.deepEqual(pagesInScope(BUILT, ""), BUILT);
  assert.deepEqual(pagesInScope(BUILT, "  , ,"), BUILT);
});

test("a scope measures the pages asked for, in the build's order", async () => {
  const pagesInScope = await gate();
  assert.deepEqual(pagesInScope(BUILT, "search-react.html"), ["search-react.html"]);
  // Asked out of order and with spaces; the build's order is what the run walks.
  assert.deepEqual(pagesInScope(BUILT, " search-react.html , hydration.html "), [
    "hydration.html",
    "search-react.html",
  ]);
});

test("a scope naming a page the build did not emit is refused, not silently skipped", async () => {
  const pagesInScope = await gate();
  assert.throws(
    () => pagesInScope(BUILT, "search-react.html,compare.html"),
    (error) =>
      /scoped to page\(s\) this build did not emit: compare\.html/.test(error.message) &&
      /would report it clean/.test(error.message),
  );
  // Every page absent, not just one of them.
  assert.throws(() => pagesInScope(BUILT, "gone.html"), /did not emit: gone\.html/);
});

test("every mutation declares where its defect can be seen, and every declared page is built", async () => {
  const { MUTATIONS } = await import("../scripts/evidence-mutations.mjs");
  const { readFile } = await import("node:fs/promises");
  const declared = JSON.parse(await readFile(new URL("../dist/pages.json", import.meta.url), "utf8")).pages;
  assert.ok(MUTATIONS.length > 0);
  for (const mutation of MUTATIONS) {
    const pages = mutation.pages;
    assert.ok(
      pages === "all" || (Array.isArray(pages) && pages.length > 0),
      `${mutation.name} declares no pages`,
    );
    if (pages === "all") continue;
    for (const page of pages) {
      assert.ok(declared.includes(page), `${mutation.name} declares ${page}, which the build does not emit`);
    }
  }
});
