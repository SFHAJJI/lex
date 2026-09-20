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

test("every mutation declares where its defect can be seen", async () => {
  const { MUTATIONS } = await import("../scripts/evidence-mutations.mjs");
  assert.ok(MUTATIONS.length > 0);
  for (const mutation of MUTATIONS) {
    const pages = mutation.pages;
    assert.ok(
      pages === "all" || (Array.isArray(pages) && pages.length > 0 && pages.every((page) => page.endsWith(".html"))),
      `${mutation.name} declares no pages`,
    );
  }
});

test("every page a mutation declares is one the build emits", async (t) => {
  // Reads the built manifest, which this suite does not build: `npm test` runs without a `dist`
  // in CI. A missing build is named inconclusive rather than passed over, because silence here
  // would read as having checked the declarations. The property is enforced at runtime in any
  // case: `pagesInScope` refuses a scope naming a page the build did not emit, which the test
  // above this one holds.
  const { MUTATIONS } = await import("../scripts/evidence-mutations.mjs");
  const { readFile } = await import("node:fs/promises");
  let declared;
  try {
    declared = JSON.parse(await readFile(new URL("../dist/pages.json", import.meta.url), "utf8")).pages;
  } catch {
    t.diagnostic("INCONCLUSIVE: no build in this checkout, so the declarations were not checked against one");
    return;
  }
  for (const mutation of MUTATIONS) {
    if (mutation.pages === "all") continue;
    for (const page of mutation.pages) {
      assert.ok(declared.includes(page), `${mutation.name} declares ${page}, which the build does not emit`);
    }
  }
});
