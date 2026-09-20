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

test("a declared page that caught nothing is a wrong declaration", async () => {
  const { declarationVerdict } = await import("../scripts/evidence-mutations.mjs");
  const caught = [
    "  search-react.html @narrow/light: Home moved focus to option 2, expected 0",
    "  search-react.html @tablet/dark: Home moved focus to option 2, expected 0",
  ];
  // Declared where it was caught: nothing to say.
  assert.deepEqual(declarationVerdict(["search-react.html"], caught), { failures: [], notes: [] });
  // Declared somewhere else entirely: the declaration is wrong, and says which page caught nothing.
  const wrong = declarationVerdict(["compare.html"], caught);
  assert.deepEqual(wrong.failures, ["declares compare.html and no failure naming compare.html caught it"]);
  assert.deepEqual(wrong.notes, ["search-react.html"]);
  // Two declared, one of them empty: the empty one fails on its own.
  const half = declarationVerdict(["search-react.html", "reading.html"], caught);
  assert.deepEqual(half.failures, ["declares reading.html and no failure naming reading.html caught it"]);
  // A mutation caught nowhere at all fails for every page it declares.
  assert.equal(declarationVerdict(["search-react.html"], []).failures.length, 1);
});

test("a page that caught it and was not declared is reported, not failed", async () => {
  const { declarationVerdict } = await import("../scripts/evidence-mutations.mjs");
  const caught = [
    "  hydration.html @narrow/light: 1 request(s) after the page settled: /pages.json",
    "  search-react.html @narrow/light: 1 request(s) after the page settled: /pages.json",
  ];
  const verdict = declarationVerdict(["hydration.html"], caught);
  assert.deepEqual(verdict.failures, []);
  assert.deepEqual(verdict.notes, ["search-react.html"]);
  // Declaring both leaves nothing to report.
  assert.deepEqual(declarationVerdict(["hydration.html", "search-react.html"], caught), {
    failures: [],
    notes: [],
  });
  // "all" is judged by nothing: its defect is only visible across pages.
  assert.deepEqual(declarationVerdict("all", caught), { failures: [], notes: [] });
  // A sentence that names no page is neither a failure nor a note.
  assert.deepEqual(declarationVerdict("all", ["  @narrow: 3 shells but 1 distinct main line-height(s)"]), {
    failures: [],
    notes: [],
  });
});

test("a page is read from the start of a sentence, not from anywhere in it", async () => {
  const { pageOf, declarationVerdict } = await import("../scripts/evidence-mutations.mjs");
  assert.equal(pageOf("  search-react.html @narrow/light: ArrowDown moved focus nowhere"), "search-react.html");
  assert.equal(pageOf("  trust-surface.html: /no-such-screen/x answers 404"), "trust-surface.html");
  assert.equal(
    pageOf("  provenance-preview-synthetic~synthetic-preview-work~2001-01-01.html @tablet/dark: 0 h1 elements"),
    "provenance-preview-synthetic~synthetic-preview-work~2001-01-01.html",
  );
  assert.equal(pageOf("  @narrow: 3 shells but 1 distinct main line-height(s)"), null);
  assert.equal(pageOf("all 45 induced mutations were caught."), null);
  // The hazard: a sentence about one page quoting another page's name in a path. Asking whether
  // the name appears anywhere in the line would call this a catch for reading.html.
  const crafted = ["  trust-surface.html: /no-such-screen/reading.html answers 404; a visible action"];
  const verdict = declarationVerdict(["reading.html"], crafted);
  assert.deepEqual(verdict.failures, ["declares reading.html and no failure naming reading.html caught it"]);
  assert.deepEqual(verdict.notes, ["trust-surface.html"]);
});

test("the sweep asks for the verdict and counts it: a dead or silent call site fails here", async () => {
  const { sweepWith, judgeMutation } = await import("../scripts/evidence-mutations.mjs");
  const { mkdtemp, rm } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const prepare = () => mkdtemp(join(tmpdir(), "lex-sweep-test-"));
  const mutation = (name, pages) => ({ name, pages, expect: /the defect/, apply: async () => {} });
  const caughtOn = (page) => ({ code: 1, output: `  ${page} @narrow/light: the defect is here\n` });

  // One caught, one caught on a page it did not declare, one green, one for another reason.
  // What the browser said, per mutation, so each branch is driven on purpose and not by order.
  const said = {
    "caught here": caughtOn("search-react.html"),
    "declared elsewhere": caughtOn("search-react.html"),
    "never caught": { code: 0, output: "all 495 page/viewport combinations clean\n" },
    "another reason": { code: 1, output: "  search-react.html @narrow/light: something else entirely\n" },
  };
  const printed = [];
  const failures = await sweepWith({
    mutations: [
      mutation("caught here", ["search-react.html"]),
      mutation("declared elsewhere", ["compare.html"]),
      mutation("never caught", ["search-react.html"]),
      mutation("another reason", ["search-react.html"]),
    ],
    prepare,
    run: async (root, pages, name) => said[name],
    full: true,
    log: (line) => printed.push(line),
  });
  // Three of the four count: the wrong declaration, the one nobody caught, and the wrong reason.
  assert.equal(failures, 3, printed.join("\n"));
  assert.ok(printed.some((l) => l.startsWith("caught       caught here")), printed.join("\n"));
  assert.ok(printed.some((l) => l.startsWith("WRONG PAGE   declared elsewhere")), printed.join("\n"));
  assert.ok(printed.some((l) => l.startsWith("STILL GREEN  never caught")), printed.join("\n"));
  assert.ok(printed.some((l) => l.startsWith("WRONG REASON another reason")), printed.join("\n"));

  // Scoped, the same wrong declaration is not judged: a scoped run measures only declared pages.
  const quiet = [];
  const scoped = await sweepWith({
    mutations: [mutation("declared elsewhere", ["compare.html"])],
    prepare,
    run: async () => caughtOn("search-react.html"),
    full: false,
    log: (line) => quiet.push(line),
  });
  assert.equal(scoped, 0, quiet.join("\n"));
  assert.ok(quiet.some((l) => l.startsWith("caught       ")), quiet.join("\n"));

  // A mutation that declares nothing is refused before any browser is asked to run.
  await assert.rejects(
    () => sweepWith({
      mutations: [{ name: "undeclared", expect: /x/, apply: async () => {} }],
      prepare,
      run: async () => ({ code: 1, output: "x" }),
      full: true,
      log: () => {},
    }),
    /declares no pages/,
  );

  // And the verdict itself, for the two counting branches the sweep reports.
  assert.equal(judgeMutation(mutation("m", ["a.html"]), { code: 0, output: "" }, true).failed, true);
  assert.equal(
    judgeMutation(mutation("m", ["a.html"]), { code: 1, output: "  a.html @narrow: the defect\n" }, true).failed,
    false,
  );
});

test("the pages noted beside a catch are sorted, and a name quoted mid-sentence is not one", async () => {
  const { declarationVerdict, pageOf } = await import("../scripts/evidence-mutations.mjs");
  // Two undeclared pages, met in reverse order: the note reads the same either way.
  const caught = [
    "  trust-surface.html @narrow/light: the defect",
    "  reading.html @narrow/light: the defect",
  ];
  assert.deepEqual(declarationVerdict(["search-react.html"], caught).notes, [
    "reading.html",
    "trust-surface.html",
  ]);
  // The anchor itself: a sentence about no page that quotes one is about no page.
  assert.equal(pageOf("  @narrow: three shells, and the link to reading.html is fine"), null);
  assert.deepEqual(declarationVerdict(["reading.html"], ["  @narrow: ... reading.html ..."]).failures, [
    "declares reading.html and no failure naming reading.html caught it",
  ]);
});

test("every page the build emits is one the matcher can read", async (t) => {
  const { pageOf } = await import("../scripts/evidence-mutations.mjs");
  const { readFile } = await import("node:fs/promises");
  let declared;
  try {
    declared = JSON.parse(await readFile(new URL("../dist/pages.json", import.meta.url), "utf8")).pages;
  } catch {
    t.diagnostic("INCONCLUSIVE: no build in this checkout, so the page names were not read from one");
    return;
  }
  for (const page of declared) {
    assert.equal(pageOf(`  ${page} @narrow/light: the defect`), page, `as a measured combination: ${page}`);
    assert.equal(pageOf(`  ${page}: a visible action answers 404`), page, `as a destination: ${page}`);
  }
});

test("a run can sweep one named mutation, and a name that selects nothing is refused", async () => {
  const { mutationsToSweep, MUTATIONS } = await import("../scripts/evidence-mutations.mjs");
  assert.equal(mutationsToSweep(MUTATIONS, "").length, MUTATIONS.length);
  assert.equal(mutationsToSweep(MUTATIONS, undefined).length, MUTATIONS.length);
  const home = mutationsToSweep(MUTATIONS, "the Home key");
  assert.equal(home.length, 1);
  assert.match(home[0].name, /^the Home key/);
  assert.throws(() => mutationsToSweep(MUTATIONS, "no such mutation"), /would sweep nothing/);
});

test("the one mutation whose defect is only visible across pages declares all", async () => {
  const { MUTATIONS } = await import("../scripts/evidence-mutations.mjs");
  const densities = MUTATIONS.find((mutation) => mutation.name.includes("shell densities"));
  assert.ok(densities, "the shell-density mutation is in the sweep");
  assert.equal(
    densities.pages,
    "all",
    "its failure compares three shells, so a scope that measured fewer would skip the comparison",
  );
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
