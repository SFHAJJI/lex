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
  // The name asked for is the name the build emitted, whole and as spelled. A substring of one, a
  // page whose name ends with one, and the same name in another case are all pages this build did
  // not emit: admitting any of them measures a page nobody asked for and reports it as the one
  // they did. The last is the one that bites on Windows, where the file system would open it.
  assert.throws(() => pagesInScope(BUILT, "react.html"), /did not emit: react\.html/);
  assert.throws(() => pagesInScope(BUILT, "Search-React.html"), /did not emit: Search-React\.html/);
  assert.throws(() => pagesInScope(BUILT, "search-react.htm"), /did not emit: search-react\.htm/);
  assert.throws(() => pagesInScope(BUILT, "my-reading.html"), /did not emit: my-reading\.html/);
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
  const counts = await sweepWith({
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
  // Three of the four count, and the two kinds are counted apart: a gate that stopped working and
  // a map to a working gate that points at the wrong page send a reader to different places.
  assert.deepEqual(counts, { uncaught: 2, misdeclared: 1, unjudged: 0 }, printed.join("\n"));
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
  assert.deepEqual(scoped, { uncaught: 0, misdeclared: 0, unjudged: 0 }, quiet.join("\n"));
  assert.ok(quiet.some((l) => l.startsWith("caught       ")), quiet.join("\n"));

  // A mutation that declares nothing is refused before anything is copied, served or run, in both
  // modes. It used to be refused only on a full run, and only when the judgement was reached: the
  // scoped path met `scopeFor` first and died there with a TypeError about `join`, so the sentence
  // that says what is wrong was unreachable on the default path.
  for (const full of [true, false]) {
    let prepared = 0;
    let ran = 0;
    await assert.rejects(
      () => sweepWith({
        mutations: [
          { name: "declared", pages: ["a.html"], expect: /x/, apply: async () => {} },
          { name: "undeclared", expect: /x/, apply: async () => {} },
        ],
        prepare: async () => (prepared += 1, prepare()),
        run: async () => (ran += 1, { code: 1, output: "  a.html: x" }),
        full,
        log: () => {},
      }),
      /undeclared declares no pages/,
      `full: ${full}`,
    );
    assert.equal(prepared, 0, "a list with an undeclared mutation in it should cost nothing");
    assert.equal(ran, 0);
  }

  // An empty declaration is not a declaration: it passes every check and judges nothing, so the
  // sweep would look for that defect on no page at all.
  await assert.rejects(
    () => sweepWith({
      mutations: [{ name: "empty", pages: [], expect: /x/, apply: async () => {} }],
      prepare,
      run: async () => ({ code: 1, output: "x" }),
      full: true,
      log: () => {},
    }),
    /empty declares no pages/,
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

test("a finished sweep says how many it swept, over what, and how many it selected", async () => {
  const { sweepSummary } = await import("../scripts/evidence-mutations.mjs");
  assert.equal(sweepSummary(45, 45, false), "all 45 induced mutations were caught over the pages each declares.");
  assert.equal(sweepSummary(45, 45, true), "all 45 induced mutations were caught over every page.");
  // A selection says so, with both counts, so a one-mutation run cannot read as the whole sweep.
  assert.equal(sweepSummary(1, 45, true), "all 1 induced mutations were caught over every page (1 of 45 selected).");
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

test("the one mutation whose defect is only visible across pages declares all, and it is the only one", async () => {
  const { MUTATIONS } = await import("../scripts/evidence-mutations.mjs");
  const densities = MUTATIONS.find((mutation) => mutation.name.includes("shell densities"));
  assert.ok(densities, "the shell-density mutation is in the sweep");
  assert.equal(
    densities.pages,
    "all",
    "its failure compares three shells, so a scope that measured fewer would skip the comparison",
  );
  // And nothing else may have it. `"all"` costs a full pass and leaves the declaration unjudged,
  // so it is the one escape hatch in the sweep: held to the one mutation that has earned it, by
  // name, rather than to a count that a second `"all"` would still satisfy.
  assert.deepEqual(
    MUTATIONS.filter((mutation) => mutation.pages === "all").map((mutation) => mutation.name),
    [densities.name],
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

test("a caller's own page scope never reaches the run, and the sweep's does", async () => {
  const { childEnv, scopeFor } = await import("../scripts/evidence-mutations.mjs");
  // A caller who exported it: on a full sweep the child would measure their pages while the sweep
  // said it measured every one, and every declaration would be judged against output from a run
  // that could not have seen the pages it was judging. Wrong the way that reads as right.
  const callers = { PATH: "/usr/bin", LEX_EVIDENCE_PAGES: "compare.html", LEX_EVIDENCE_SCOPE: "full" };

  const full = childEnv(callers, "/tmp/root", scopeFor(["search-react.html"], true));
  assert.equal("LEX_EVIDENCE_PAGES" in full, false);
  assert.equal(full.LEX_EVIDENCE_ROOT, "/tmp/root");
  assert.equal(full.PATH, "/usr/bin");

  // A mutation only a comparison across pages can show: every page, whatever the caller asked for.
  assert.equal(scopeFor("all", false), null);
  assert.equal("LEX_EVIDENCE_PAGES" in childEnv(callers, "/tmp/root", scopeFor("all", false)), false);

  // Scoped, the sweep's own scope is the one that is given, and it replaces theirs.
  const scoped = childEnv(callers, "/tmp/root", scopeFor(["reading.html", "trust-surface.html"], false));
  assert.equal(scoped.LEX_EVIDENCE_PAGES, "reading.html,trust-surface.html");

  // And the caller's environment is not edited under them.
  assert.equal(callers.LEX_EVIDENCE_PAGES, "compare.html");
});

test("the caller's page scope is removed in every case of the name, and so is their root", async () => {
  // Windows environment names are case-insensitive, so an exact-case delete closed this defect for
  // one spelling and left `$env:lex_evidence_pages = "compare.html"` reaching the child untouched:
  // the same STILL GREEN in the same 6.7 seconds, on the head that had just fixed it.
  const { childEnv, scopeFor } = await import("../scripts/evidence-mutations.mjs");
  for (const spelling of ["lex_evidence_pages", "Lex_Evidence_Pages", "LEX_EVIDENCE_PAGES"]) {
    const callers = { PATH: "/usr/bin", [spelling]: "compare.html" };
    const full = childEnv(callers, "/tmp/root", scopeFor(["search-react.html"], true));
    assert.deepEqual(
      Object.keys(full).filter((key) => key.toUpperCase() === "LEX_EVIDENCE_PAGES"),
      [],
      `${spelling} survived into the child`,
    );
    assert.equal(full.PATH, "/usr/bin");

    // Scoped, exactly one page scope reaches the child, and it is the sweep's.
    const scoped = childEnv(callers, "/tmp/root", scopeFor(["reading.html"], false));
    const pageKeys = Object.keys(scoped).filter((key) => key.toUpperCase() === "LEX_EVIDENCE_PAGES");
    assert.deepEqual(pageKeys, ["LEX_EVIDENCE_PAGES"], `${spelling} left a second key`);
    assert.equal(scoped.LEX_EVIDENCE_PAGES, "reading.html");
  }

  // The root goes the same way. Two keys differing only in case would leave which one the child
  // reads to the order they happen to be in, and the caller's could win.
  for (const spelling of ["lex_evidence_root", "LEX_EVIDENCE_ROOT"]) {
    const given = childEnv({ [spelling]: "C:/somewhere/else" }, "/tmp/root", null);
    const rootKeys = Object.keys(given).filter((key) => key.toUpperCase() === "LEX_EVIDENCE_ROOT");
    assert.deepEqual(rootKeys, ["LEX_EVIDENCE_ROOT"], `${spelling} left a second key`);
    assert.equal(given.LEX_EVIDENCE_ROOT, "/tmp/root");
  }
});

test("a scope the sweep cannot read is refused, not taken for the cheap sweep", async () => {
  const { fullFrom } = await import("../scripts/evidence-mutations.mjs");
  assert.equal(fullFrom({}), false);
  assert.equal(fullFrom({ LEX_EVIDENCE_SCOPE: "" }), false);
  assert.equal(fullFrom({ LEX_EVIDENCE_SCOPE: "full" }), true);
  // Someone who meant every page and typed it another way must not be given the scoped sweep with
  // a closing line that truthfully says "over the pages each declares" while they judge
  // declarations on it.
  for (const asked of ["FULL", "Full", "true", "1", "all", " full"]) {
    assert.throws(() => fullFrom({ LEX_EVIDENCE_SCOPE: asked }), /it is "full" or it is unset/, asked);
  }
});

test("a sweep that failed says which of the three failures it found", async () => {
  const { sweepFailureSummary } = await import("../scripts/evidence-mutations.mjs");
  const none = { uncaught: 0, misdeclared: 0, unjudged: 0 };
  assert.equal(sweepFailureSummary({ ...none, uncaught: 2 }), "2 induced mutation(s) were not caught.");
  // The one that used to be counted as a mutation nobody caught. It was caught; the map is wrong.
  assert.equal(
    sweepFailureSummary({ ...none, misdeclared: 1 }),
    "1 caught mutation(s) declare a page that caught nothing.",
  );
  // And the one that used to be counted as a mutation nobody caught while nothing had looked at it.
  assert.equal(
    sweepFailureSummary({ ...none, unjudged: 1 }),
    "1 run(s) ended without judging anything.",
  );
  assert.equal(
    sweepFailureSummary({ uncaught: 2, misdeclared: 1, unjudged: 3 }),
    "2 induced mutation(s) were not caught; 1 caught mutation(s) declare a page that caught nothing; " +
      "3 run(s) ended without judging anything.",
  );
});

test("a run that judged nothing is not a mutation nobody caught", async () => {
  // It happened: a sibling process killed this sweep's browser mid-mutation, and the run came back
  // non-zero having judged nothing. Counting it among the mutations nobody caught sends a reader to
  // look for a gate that stopped working, when what stopped was the run.
  const { judgeMutation } = await import("../scripts/evidence-mutations.mjs");
  const mutation = { name: "a mutation", pages: ["a.html"], expect: /the defect/, apply: async () => {} };

  const crashed = judgeMutation(mutation, { code: 4294967295, output: "\nnode: a fatal error\n" }, false);
  assert.equal(crashed.failed, true);
  assert.equal(crashed.kind, "unjudged");
  assert.ok(crashed.report[0].startsWith("NOT JUDGED   a mutation"), crashed.report.join("\n"));
  assert.ok(crashed.report.some((l) => l.includes("it judged nothing and ended 4294967295")), crashed.report.join("\n"));
  // Its last words, since it has no failure lines to show.
  assert.ok(crashed.report.some((l) => l.includes("node: a fatal error")), crashed.report.join("\n"));

  // A run that did judge, and failed for another reason, is still a mutation nobody caught.
  const wrongReason = judgeMutation(
    mutation,
    { code: 1, output: "  a.html @narrow: something else entirely: 3 of them\n" },
    false,
  );
  assert.equal(wrongReason.kind, "uncaught");
  assert.ok(wrongReason.report[0].startsWith("WRONG REASON a mutation"), wrongReason.report.join("\n"));
});

test("reached through a junction, a script still runs: the guard compares real paths", async (t) => {
  // The hazard this holds: node resolves `import.meta.url` through a junction or symlink and keeps
  // the caller's spelling in `process.argv[1]`, so a guard that compares the two spellings makes a
  // checkout reached through a junction — an ordinary second working copy on Windows — a script
  // that sweeps nothing, prints nothing and exits 0. To a caller reading an exit code, that is a
  // passing run. The evidence here must never be an exit code alone.
  const { mkdtemp, mkdir, writeFile, symlink, rm } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const { fileURLToPath, pathToFileURL } = await import("node:url");
  const { execFile } = await import("node:child_process");
  const { promisify } = await import("node:util");
  const node = promisify(execFile);

  const dir = await mkdtemp(join(tmpdir(), "lex-guard-"));
  try {
    const real = join(dir, "real");
    await mkdir(real);
    const helper = pathToFileURL(fileURLToPath(new URL("../scripts/invoked-directly.mjs", import.meta.url))).href;
    // Two one-line scripts: the guard as it is, and the guard as it was.
    await writeFile(
      join(real, "now.mjs"),
      `import { invokedDirectly } from ${JSON.stringify(helper)};\n` +
        'console.log(invokedDirectly(import.meta.url, process.argv[1]) ? "RAN" : "NO-OP");\n',
      "utf8",
    );
    await writeFile(
      join(real, "before.mjs"),
      'import { pathToFileURL } from "node:url";\n' +
        'const ran = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;\n' +
        'console.log(ran ? "RAN" : "NO-OP");\n',
      "utf8",
    );

    // By its real path, both rules run it: the repair does not narrow the ordinary case.
    assert.match((await node(process.execPath, [join(real, "now.mjs")])).stdout, /RAN/);
    assert.match((await node(process.execPath, [join(real, "before.mjs")])).stdout, /RAN/);

    const link = join(dir, "link");
    try {
      await symlink(real, link, "junction");
    } catch (error) {
      t.diagnostic(`INCONCLUSIVE: this host cannot create a junction (${error.code}), so the guard was not walked through one`);
      return;
    }

    // The old rule, through the junction. If this host resolves the link so that the old rule runs
    // too, the case is not live here and the test says so rather than claiming to have proven it.
    const before = (await node(process.execPath, [join(link, "before.mjs")])).stdout;
    if (/RAN/.test(before)) {
      t.diagnostic("INCONCLUSIVE: this host's junction is transparent to the old rule, so it cannot show the difference");
    } else {
      assert.match(before, /NO-OP/);
    }

    // The rule that ships, through the same junction. This is the assertion.
    assert.match(
      (await node(process.execPath, [join(link, "now.mjs")])).stdout,
      /RAN/,
      "a script reached through a junction must run rather than exit 0 in silence",
    );
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test("a path that is not this module, and no path at all, are not a direct invocation", async () => {
  const { invokedDirectly } = await import("../scripts/invoked-directly.mjs");
  const here = new URL("./evidence-scope.test.mjs", import.meta.url).href;
  const { fileURLToPath } = await import("node:url");
  assert.equal(invokedDirectly(here, fileURLToPath(here)), true);
  assert.equal(invokedDirectly(here, undefined), false);
  assert.equal(invokedDirectly(here, ""), false);
  // A path nothing can resolve is not this module, and a guard is the wrong place to raise it.
  assert.equal(invokedDirectly(here, fileURLToPath(new URL("./no-such-file.mjs", import.meta.url))), false);
  assert.equal(invokedDirectly(here, fileURLToPath(new URL("./network-gate.test.mjs", import.meta.url))), false);
});

test("the spellings a caller can type still name this module", async (t) => {
  // The doc comment claims `pathToFileURL` is what normalises the drive letter's case and the
  // separators, so a caller who types a lower-case drive or forward slashes still matches. Nothing
  // pinned that claim, and a guard that silently stops matching is a script that does nothing and
  // exits 0.
  const { invokedDirectly } = await import("../scripts/invoked-directly.mjs");
  const { fileURLToPath } = await import("node:url");
  const here = new URL("./evidence-scope.test.mjs", import.meta.url).href;
  const path = fileURLToPath(here);

  // `.` and `..` segments, and a doubled separator, which a shell or an npm script can produce.
  const { dirname, basename, join } = await import("node:path");
  assert.equal(invokedDirectly(here, join(dirname(path), ".", basename(path))), true);
  assert.equal(invokedDirectly(here, join(dirname(path), "..", "test", basename(path))), true);

  if (!/^[A-Za-z]:/.test(path)) {
    t.diagnostic("INCONCLUSIVE: no drive letter on this host, so the drive-letter spellings were not walked");
    return;
  }

  // The drive letter in the other case, and forward slashes, both of which Windows accepts.
  assert.equal(invokedDirectly(here, path[0].toLowerCase() + path.slice(1)), true);
  assert.equal(invokedDirectly(here, path[0].toUpperCase() + path.slice(1)), true);
  assert.equal(invokedDirectly(here, path.replaceAll("\\", "/")), true);
});

test("a failure of a kind the sweep does not count is refused, not counted as nothing", async () => {
  // Counting into an object by a key it does not hold leaves the count NaN, and `NaN > 0` is
  // false: the sweep would print failures and end 0, which is a passing run to anything reading
  // the exit code. A kind nobody counts is a mistake in the judgement, so it stops the sweep.
  const { sweepWith } = await import("../scripts/evidence-mutations.mjs");
  const { mkdtemp } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  await assert.rejects(
    () => sweepWith({
      mutations: [{ name: "a new kind of failure", pages: ["a.html"], expect: /x/, apply: async () => {} }],
      prepare: () => mkdtemp(join(tmpdir(), "lex-sweep-kind-")),
      run: async () => ({ code: 1, output: "  a.html: x\n" }),
      full: true,
      log: () => {},
      // Standing for a future branch of the judgement that fails the head for a reason the sweep
      // was never taught to count.
      judge: () => ({ failed: true, kind: "something new", report: ["NEW KIND     a new kind of failure"] }),
    }),
    /failed as "something new", which the sweep does not count/,
  );
});

test("a whole sweep, from an environment to an exit code", async () => {
  // The five things that survived the writer seat's mutants on the previous head, all of them in
  // the shell around the judgement rather than in it: the selection read but not passed on, the
  // summary counting the whole list, the scope taken from a module constant no test could set, and
  // a failing sweep still ending 0. They are now inside one function a test drives.
  const { sweepAll } = await import("../scripts/evidence-mutations.mjs");
  const { mkdtemp } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const prepared = [];
  const prepare = async () => {
    const root = await mkdtemp(join(tmpdir(), "lex-sweepall-"));
    prepared.push(root);
    return root;
  };
  // A list of three that touch nothing, so the sweep's own wiring is what is under test.
  const mutations = [
    { name: "the Home key made to stay put", pages: ["search-react.html"], expect: /the defect/, apply: async () => {} },
    { name: "a second listbox made tabbable", pages: ["search-react.html"], expect: /the defect/, apply: async () => {} },
    { name: "the shell densities flattened", pages: "all", expect: /the defect/, apply: async () => {} },
  ];
  const caught = { code: 1, output: "  search-react.html @narrow/light: the defect is here\n" };

  // One of the three selected, scoped.
  const said = [];
  const seen = [];
  const one = await sweepAll(
    { LEX_EVIDENCE_ONLY: "Home key" },
    { mutations, prepare, run: async (root, env) => (seen.push(env), caught), log: (l) => said.push(l), err: (l) => said.push(l) },
  );
  assert.equal(one.swept, 1);
  assert.equal(one.exitCode, 0);
  assert.equal(one.summary, "all 1 induced mutations were caught over the pages each declares (1 of 3 selected).");
  // Said out loud, not merely returned: the count the sweep reports is the count it swept.
  assert.ok(said.some((l) => l.includes("(1 of 3 selected)")), said.join("\n"));
  // One selected is one run: a sweep that ran the whole list while saying it ran the selection
  // would have the same summary and three times the cost.
  assert.equal(seen.length, 1);
  // Scoped, the child is given the pages the mutation declares, and a root to serve.
  assert.equal(seen[0].LEX_EVIDENCE_PAGES, "search-react.html");
  // The root the child is served is the root that was prepared for it. Asserting only that some
  // root is set would pass on a sweep that served the caller's, or the previous mutation's.
  assert.equal(seen[0].LEX_EVIDENCE_ROOT, prepared.at(-1));
  assert.equal(prepared.length, 1);

  // Full, with a caller's own page scope exported: the child must not see it, or every declaration
  // would be judged against a run that could not have seen the pages it is judging.
  const envs = [];
  const full = await sweepAll(
    { LEX_EVIDENCE_SCOPE: "full", LEX_EVIDENCE_PAGES: "compare.html" },
    { mutations, prepare, run: async (root, env) => (envs.push(env), caught), log: () => {}, err: () => {} },
  );
  assert.equal(envs.length, 3);
  for (const env of envs) assert.equal("LEX_EVIDENCE_PAGES" in env, false);
  assert.equal(full.summary, "all 3 induced mutations were caught over every page.");

  // The mutation declaring "all" is given every page even when the sweep is scoped.
  const scopedEnvs = [];
  await sweepAll(
    { LEX_EVIDENCE_ONLY: "densities" },
    { mutations, prepare, run: async (root, env) => (scopedEnvs.push(env), caught), log: () => {}, err: () => {} },
  );
  assert.equal("LEX_EVIDENCE_PAGES" in scopedEnvs[0], false);

  // A sweep that failed ends 1, says which failure, and says it where failures are read.
  const out = [];
  const errs = [];
  const failing = await sweepAll(
    { LEX_EVIDENCE_ONLY: "Home key" },
    {
      mutations,
      prepare,
      run: async () => ({ code: 0, output: "all 495 page/viewport combinations clean\n" }),
      log: (l) => out.push(l),
      err: (l) => errs.push(l),
    },
  );
  assert.equal(failing.exitCode, 1);
  assert.equal(failing.summary, "1 induced mutation(s) were not caught.");
  assert.ok(errs.some((l) => l.includes("were not caught")), errs.join("\n"));
  assert.equal(out.some((l) => l.includes("were not caught")), false);
  // Every report line goes to the sweep's log, and the summary alone goes to the failure stream.
  assert.ok(out.some((l) => l.startsWith("STILL GREEN  the Home key")), out.join("\n"));
  assert.equal(errs.length, 1);

  // A sweep whose ONLY failure is a wrong declaration fails. It is the defect this slice exists to
  // judge, and until this assertion nothing but a browser run said it ends 1: the sum could be
  // made `counts.uncaught` and every test stayed green while a wrong declaration printed WRONG PAGE
  // above `all 1 induced mutations were caught` and exit 0.
  const wrongOut = [];
  const wrongErr = [];
  const misdeclared = await sweepAll(
    { LEX_EVIDENCE_SCOPE: "full", LEX_EVIDENCE_ONLY: "Home key" },
    {
      mutations: [{ ...mutations[0], pages: ["compare.html"] }],
      prepare,
      run: async () => caught,
      log: (l) => wrongOut.push(l),
      err: (l) => wrongErr.push(l),
    },
  );
  assert.equal(misdeclared.exitCode, 1);
  assert.deepEqual(misdeclared.counts, { uncaught: 0, misdeclared: 1, unjudged: 0 });
  assert.equal(misdeclared.summary, "1 caught mutation(s) declare a page that caught nothing.");
  assert.equal(wrongOut.some((l) => l.includes("induced mutations were caught")), false, wrongOut.join("\n"));
  assert.ok(wrongOut.some((l) => l.startsWith("WRONG PAGE")), wrongOut.join("\n"));

  // And a run that judged nothing fails as its own kind, not as a mutation nobody caught.
  const crashed = await sweepAll(
    { LEX_EVIDENCE_ONLY: "Home key" },
    {
      mutations,
      prepare,
      run: async () => ({ code: 4294967295, output: "\nnode: a fatal error\n" }),
      log: () => {},
      err: () => {},
    },
  );
  assert.equal(crashed.exitCode, 1);
  assert.deepEqual(crashed.counts, { uncaught: 0, misdeclared: 0, unjudged: 1 });
  assert.equal(crashed.summary, "1 run(s) ended without judging anything.");

  // And with no list given, the sweep is the real one: a selection naming nothing is refused
  // against the 45, before a single copy of the build is made.
  await assert.rejects(
    () => sweepAll({ LEX_EVIDENCE_ONLY: "no such mutation" }, { prepare, run: async () => caught, log: () => {}, err: () => {} }),
    /would sweep nothing/,
  );
});

test("a sweep is never left to guess whether it measured every page", async () => {
  // `full` had a default, so every test passed it and production never did: the judgement could
  // have been off in production with the whole suite green.
  const { sweepWith } = await import("../scripts/evidence-mutations.mjs");
  await assert.rejects(
    () => sweepWith({ mutations: [], prepare: async () => "", run: async () => ({ code: 1, output: "" }) }),
    /`full` is not optional/,
  );
});

test("a mutation is selected by any part of its name, and a page name is read whole", async () => {
  const { mutationsToSweep, pageOf, MUTATIONS } = await import("../scripts/evidence-mutations.mjs");
  // Not a prefix of the name: selecting by prefix would sweep nothing and say so.
  const mid = mutationsToSweep(MUTATIONS, "made to stay put");
  assert.equal(mid.length, 1);
  assert.match(mid[0].name, /^the Home key/);
  // The name ends where the sentence's punctuation begins; a viewport glued to it is not a page.
  assert.equal(pageOf("  search-react.html@narrow: Home moved focus nowhere"), null);
  assert.equal(pageOf("  search-react.htmlx @narrow: x"), null);
  assert.equal(pageOf("  search-react.html @narrow: x"), "search-react.html");
});

test("a scoped run says so whether it passed or failed", async () => {
  // The sentence was on the clean branch only, so a failing scoped run read as a failing full one
  // and its silence about the pages it never opened read as a verdict on them.
  const { failureHeadline, cleanHeadline, scopeNote } = await import("../scripts/browser-evidence.mjs");
  assert.equal(scopeNote(33, 33), "");
  assert.equal(scopeNote(2, 33), " (2 of 33 pages measured)");
  assert.equal(failureHeadline(4, 2, 33), "4 failure(s) (2 of 33 pages measured):");
  assert.equal(failureHeadline(4, 33, 33), "4 failure(s):");
  assert.equal(cleanHeadline(495, 33, 33), "all 495 page/viewport combinations clean");
  assert.equal(cleanHeadline(30, 2, 33), "all 30 page/viewport combinations clean (2 of 33 pages measured)");
});

test("no LEX_EVIDENCE_ variable of the caller's reaches the run", async () => {
  // Three defects here were one defect: a caller's LEX_EVIDENCE_PAGES scoping a sweep that said it
  // measured every page; the same one spelling away, because Windows names are case-insensitive;
  // and LEX_EVIDENCE_FAST, which is not a page scope and is a scope all the same -- it collapses
  // five widths to one and three colour schemes to one, so a sweep judged all 45 mutations on a
  // fifteenth of the matrix, in 4 seconds against 20, and printed the same sentence. The rule is
  // the family, not the names: the sweep's own are the only ones the child is given.
  const { childEnv, scopeFor } = await import("../scripts/evidence-mutations.mjs");
  const callers = {
    PATH: "/usr/bin",
    HOME: "/home/someone",
    LEX_EVIDENCE_FAST: "1",
    lex_evidence_fast: "1",
    LEX_EVIDENCE_PAGES: "compare.html",
    lex_evidence_pages: "compare.html",
    LEX_EVIDENCE_ROOT: "C:/somewhere/else",
    LEX_EVIDENCE_SCOPE: "full",
    LEX_EVIDENCE_ONLY: "the Home key",
    LEX_EVIDENCE_SOMETHING_ADDED_LATER: "1",
  };

  for (const scope of [null, "reading.html"]) {
    const given = childEnv(callers, "/tmp/root", scope);
    const ours = Object.keys(given).filter((key) => key.toUpperCase().startsWith("LEX_EVIDENCE_"));
    assert.deepEqual(
      ours.sort(),
      scope === null ? ["LEX_EVIDENCE_ROOT"] : ["LEX_EVIDENCE_PAGES", "LEX_EVIDENCE_ROOT"],
      `scope ${scope}`,
    );
    assert.equal(given.LEX_EVIDENCE_ROOT, "/tmp/root");
    // Everything that is not ours is passed through untouched.
    assert.equal(given.PATH, "/usr/bin");
    assert.equal(given.HOME, "/home/someone");
  }

  assert.equal(childEnv(callers, "/tmp/root", scopeFor(["a.html"], false)).LEX_EVIDENCE_PAGES, "a.html");
  // And the caller's environment is not edited under them.
  assert.equal(callers.LEX_EVIDENCE_FAST, "1");
});

test("a sweep of no mutations is refused, not reported clean", async () => {
  // `all 0 induced mutations were caught over the pages each declares.` and exit 0 is what a sweep
  // that judged nothing would have said, and it reads exactly like a sweep that found nothing.
  const { mutationsToSweep, sweepAll, MUTATIONS } = await import("../scripts/evidence-mutations.mjs");
  assert.throws(() => mutationsToSweep([], ""), /given no mutations/);
  assert.throws(() => mutationsToSweep([], "the Home key"), /given no mutations/);
  await assert.rejects(
    () => sweepAll({}, { mutations: [], prepare: async () => "", run: async () => ({ code: 1, output: "" }), log: () => {}, err: () => {} }),
    /given no mutations/,
  );
  // The real list is not empty, which is what makes the guard a guard and not a tautology.
  assert.ok(MUTATIONS.length > 0);
});
