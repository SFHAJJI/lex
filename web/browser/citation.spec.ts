import { AxeBuilder } from "@axe-core/playwright";
import { expect, test, type Page, type Route } from "@playwright/test";

/**
 * UI-O5. The citation helpers are covered by unit tests that stay green if the App or Provision
 * wiring is deleted, because the suite runs only `src/*.test.ts` and every `.tsx` file is invisible
 * to it. The defect this whole lane repairs is a producer fact reaching the wire and then dying at a
 * rendering boundary, so a lane repairing that defect cannot rest on tests blind to exactly that
 * boundary.
 *
 * These journeys drive the mounted workspace and read what a reader would actually copy. Each
 * assertion has a wiring-removal mutation recorded beside it in the repair packet: removing the
 * named prop or field makes that assertion, and only that assertion, fail.
 *
 * The `/mcp` responses are controlled fixtures and the route fails closed, so no request from this
 * file reaches a real MCP endpoint. Each journey asserts the bounded set of methods it asked for.
 * No law payload, no query text and no golden content is read or written by this file.
 */

const WORK = "lu-legilux:fixture-citation";
const PINNED = "2020-07-17";
const RECORD_SHA = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";
const BODY_SHA = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
const LATER = "2021-01-01";
const LATER_RECORD_SHA = "9988776655443322110099887766554433221100998877665544332211009988";

/**
 * A well-formed digest derived from the text it covers.
 *
 * Every article previously shared one constant, so a citation that quoted the wrong provision's
 * digest still matched, and so did one built from a provision the page never requested. Deriving it
 * from the wording makes both impossible to state accidentally: two articles cannot share a digest,
 * and one article cannot carry a digest belonging to different wording.
 */
const shaOf = (text: string): string => {
  let hash = 0n;
  for (const character of text) {
    hash = (hash * 131n + BigInt(character.codePointAt(0) ?? 0)) % (1n << 256n);
  }
  return hash.toString(16).padStart(64, "0").slice(-64);
};

type Variant = "plain" | "amended" | "punctuation";

const articleText = (num: number, variant: Variant) => {
  if (variant === "amended") return `Fixture article ${num} as amended.`;
  // U+2019 against U+0027. Identical after normalisation, different bytes, different digest.
  const apostrophe = variant === "punctuation" ? "\u2019" : "'";
  return `Fixture article ${num}, l${apostrophe}alinea.`;
};
const articleSha = (num: number, variant: Variant) => shaOf(articleText(num, variant));

/**
 * How the later version differs from the pinned one.
 *
 * `amend` rewrites the wording. `add` leaves existing articles untouched and introduces one new
 * article. `punctuation` changes only typography, which moves the bytes and the digest without
 * changing a word.
 */
type FixtureMode = "amend" | "add" | "punctuation";

const SOURCE_PINNED = "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo";
const SOURCE_LATER = "https://legilux.public.lu/eli/etat/leg/loi/2021/01/01/a999/jo";

/** `art-3` and nothing else. An unrecognised anchor yields no article, never the first one. */
const ANCHOR = /^art-(\d+)$/;

/** A version whose document carries both document-level digests, with `count` articles. */
function lawAnswer(
  nums: number[], withItemDigest: boolean, date: string = PINNED, mode: FixtureMode = "amend",
): Record<string, unknown>[] {
  const later = date === LATER;
  // The digest follows the text in every mode, so the fixture cannot present one wording under
  // another wording's digest, and two articles cannot share one.
  const variant: Variant = !later ? "plain"
    : mode === "amend" ? "amended"
    : mode === "punctuation" ? "punctuation"
    : "plain";
  return [{
    envelope: {
      publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
      timeline_semantics: "publisher_applicability",
    },
    document: {
      title: "Fixture law", language: "fr", valid_from: date,
      extraction_profile: "akn-lu/1",
      // Distinct per side. A citation that carried one side's source for both, or substituted the
      // Lex permalink for either, would otherwise read plausibly.
      source_uri: later
        ? "https://legilux.public.lu/eli/etat/leg/loi/2021/01/01/a999/jo"
        : "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
      // Distinct per side, so a comparison citation that carried one side's digest for both, or
      // dropped one, is visible rather than plausible.
      record_sha256: later ? LATER_RECORD_SHA : RECORD_SHA,
      body_sha256: BODY_SHA,
    },
    provisions: nums.map((num) => ({
      anchor: `art-${num}`,
      num: `Art. ${num}`,
      heading: `Heading ${num}`,
      text: articleText(num, variant),
      ...(withItemDigest ? { text_sha256: articleSha(num, variant) } : {}),
    })),
  }];
}

const timelineAnswer = (): Record<string, unknown>[] => [{
  envelope: { publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
              timeline_semantics: "publisher_applicability" },
  versions: [{
    valid_from: PINNED, language: "fr", text_available: true, document_type: "LOI",
    source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
  }],
}];

/**
 * Per-article history, requested by the workspace whenever an anchor is selected.
 *
 * Nothing in a citation depends on it. It is here because failing closed proved that the narrowing
 * journey had been issuing this call against the mounted server all along, which is the exact
 * environmental dependency the fail-closed route removes.
 */
const historyAnswer = (): Record<string, unknown>[] => [{
  envelope: { publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
              timeline_semantics: "publisher_applicability" },
  states: [{ valid_from: PINNED, text_sha256: articleSha(1, "plain") }],
}];

const mcpBody = (id: number, payload: unknown) => JSON.stringify({
  jsonrpc: "2.0", id,
  result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
});

const FIXTURE_METHODS = new Set(["timeline", "as_of", "article_history"]);

/**
 * The canonical identity of a request: the method and every argument it carried.
 *
 * The previous revision listed the arguments it thought identified a request, which meant the ones
 * it did not think of were invisible. `work` and `language` were among them, so a request for the
 * wrong law or the wrong language of the right law passed the contract, and `timeline` recorded as
 * a bare name with no identity at all. Codex demonstrated it by rewriting both arguments before the
 * fixture recorded and served them, and all ten journeys stayed green.
 *
 * Nothing is selected now. Every argument present is rendered, keys sorted so the trace is stable,
 * values JSON-encoded so a string, a number and a missing value cannot collide. An argument this
 * file has never heard of shows up in the trace as itself rather than being dropped.
 */
const operation = (name: string, args: Record<string, unknown>): string => {
  const rendered = Object.keys(args).sort()
    .map((key) => `${key}=${JSON.stringify(args[key])}`).join(" ");
  return rendered ? `${name}(${rendered})` : name;
};

/**
 * What this fixture is willing to answer, per method.
 *
 * Recording an argument is not the same as refusing a bad one. A trace makes a wrong request
 * visible after the fact; this refuses to serve it at all, which is the behaviour the fail-closed
 * route was introduced for. The fixture holds exactly one law in one language, so a request for
 * another is not its to answer, and answering anyway is how a wrong-work regression survives.
 */
const ALLOWED_ARGUMENTS: Record<string, ReadonlySet<string>> = {
  timeline: new Set(["work", "limit", "language"]),
  as_of: new Set(["work", "date", "mode", "anchors", "language"]),
  article_history: new Set(["work", "anchor", "language"]),
};

function argumentsAreServable(name: string, args: Record<string, unknown>): boolean {
  const allowed = ALLOWED_ARGUMENTS[name];
  if (allowed === undefined) return false;
  // An argument this fixture does not model is a request it cannot honestly answer.
  if (Object.keys(args).some((key) => !allowed.has(key))) return false;
  if (args.work !== WORK) return false;
  if (args.language !== undefined && args.language !== "fr") return false;
  return true;
}

/**
 * Answer `as_of`, `timeline` and `article_history` from fixtures, and fail closed on everything
 * else.
 *
 * An earlier revision called `route.fallback()` here, handing any unexpected MCP method to the
 * mounted server. That contradicts the controlled-fixture claim above, and the local empty-index
 * server is not the only possibility: with `LEX_BROWSER_BASE_URL` set the config starts no server
 * at all and points every request at that deployment instead. A wiring regression could therefore
 * issue a real request, receive an unrelated answer, and pass for environmental reasons. Nothing
 * this fixture does not recognise now leaves the browser.
 *
 * The returned array records every method the page asked for, so a journey asserts what it actually
 * requested rather than assuming. That assertion earned itself immediately: it showed the narrowing
 * journey had been calling `article_history` against the mounted server, so part of what that
 * journey proved came from the environment rather than from the fixture.
 */
async function routeMcp(
  page: Page, count: number, withItemDigest: boolean, mode: FixtureMode = "amend",
): Promise<string[]> {
  const called: string[] = [];
  await page.route("**/mcp", async (route: Route) => {
    const request = route.request().postDataJSON() as {
      id: number; params?: { name?: string; arguments?: Record<string, unknown> };
    };
    const name = request.params?.name ?? "";
    const args = (request.params?.arguments ?? {}) as Record<string, unknown>;
    called.push(operation(name, args));
    // Both halves of the same rule: the trace records what was asked, and the route refuses to
    // answer what this fixture does not hold. A wrong work fails the journey twice over.
    if (!argumentsAreServable(name, args)) {
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: JSON.stringify({
          jsonrpc: "2.0", id: request.id,
          error: { code: -32602, message: `citation fixture does not serve ${name} with these arguments` },
        }),
      });
      return;
    }
    if (name === "timeline") {
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: mcpBody(request.id, timelineAnswer()),
      });
      return;
    }
    if (name === "article_history") {
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: mcpBody(request.id, historyAnswer()),
      });
      return;
    }
    if (name === "as_of") {
      const args = (request.params as { arguments?: Record<string, unknown> } | undefined)
        ?.arguments ?? {};
      const date = String(args.date ?? PINNED);
      const available = mode === "add" && date === LATER
        ? Array.from({ length: count + 1 }, (_unused, index) => index + 1)
        : Array.from({ length: count }, (_unused, index) => index + 1);
      // The producer honours an anchor selection, so the fixture must too, and it must honour the
      // identity rather than the count. Counting the requested anchors and regenerating `art-1`
      // upward meant any single wrong anchor still returned `art-1`, so a request for the wrong
      // provision satisfied the narrowing assertion. An anchor this fixture does not recognise now
      // returns no article at all.
      const requested = typeof args.anchors === "string" && args.anchors.length > 0
        ? args.anchors.split(",")
        : undefined;
      const nums = requested === undefined
        ? available
        : requested
            .map((value) => Number(ANCHOR.exec(value.trim())?.[1] ?? Number.NaN))
            .filter((num) => available.includes(num));
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: mcpBody(request.id, lawAnswer(nums, withItemDigest, date, mode)),
      });
      return;
    }
    // A refusal the application can parse, rather than a network error or a real answer. The
    // journey then fails on its recorded trace, which names the offending operation.
    await route.fulfill({
      status: 200, contentType: "application/json",
      body: JSON.stringify({
        jsonrpc: "2.0", id: request.id,
        error: { code: -32601, message: `citation fixture does not serve ${name}` },
      }),
    });
  });
  return called;
}

/**
 * The operation traces these journeys actually produce, measured rather than assumed.
 *
 * `LAW_READ` carries `as_of(... outline)` twice, and that is a real duplicate rather than a quirk of
 * the fixture: `App.tsx` fetches the outline once for the contents rail at line 358, and again
 * inside the read effect at line 402 purely to count provisions against the whole-act threshold,
 * with byte-identical arguments. `COMPARE_WHOLE` carries the same duplicate for the pinned side.
 * Writing this contract is what made it visible, which is the point of the contract.
 *
 * These constants therefore record observed behaviour, not desired behaviour. The duplicate is a
 * defect in my lane and it is not this lane's defect, so it is carried as its own row rather than
 * repaired under a citation change. Whoever fixes it must delete an entry here, deliberately.
 */
const quoted = (value: string) => JSON.stringify(value);

/** Built exactly as `operation` renders them, so an expectation cannot drift from the recorder. */
const TIMELINE = `timeline(limit=400 work=${quoted(WORK)})`;
const asOf = (date: string, mode: string, anchors?: string) => `as_of(${
  anchors === undefined ? "" : `anchors=${quoted(anchors)} `
}date=${quoted(date)} mode=${quoted(mode)} work=${quoted(WORK)})`;
const history = (anchor: string) =>
  `article_history(anchor=${quoted(anchor)} work=${quoted(WORK)})`;

const LAW_READ = [
  TIMELINE,
  asOf(PINNED, "outline"),
  asOf(PINNED, "outline"),
  asOf(PINNED, "full"),
];

/** A whole-document comparison: both sides outlined, then the shared rail and title. */
const COMPARE_WHOLE = [
  asOf(PINNED, "outline"),
  asOf(LATER, "outline"),
  TIMELINE,
  asOf(PINNED, "outline"),
];

/** A comparison scoped to one article. Note that no text is fetched at all. */
const COMPARE_ART1 = [
  asOf(PINNED, "outline", "art-1"),
  asOf(LATER, "outline", "art-1"),
  TIMELINE,
  asOf(PINNED, "outline"),
  history("art-1"),
];

/**
 * Every journey ends with its exact operation trace.
 *
 * The previous revision asserted only that no operation fell outside a global allowed set and that
 * at least one occurred. That is a membership check, and I described it as a contract, which it was
 * not: it cannot see extra traffic that happens to be allowed. Codex demonstrated the gap by
 * injecting one additional `timeline` operation into every trace, and all ten journeys stayed
 * green. An extra, missing, duplicated or wrong-argument operation must fail the journey that
 * caused it, and now does.
 *
 * Compared as a sorted multiset rather than an ordered sequence because the comparison view fetches
 * its two sides concurrently, so their relative order is genuinely unspecified. Cardinality and
 * arguments are still exact, which is what the objection was about; where the product requires an
 * order, the journey asserts it separately rather than by accident of scheduling.
 */
function expectOperations(called: string[], expected: string[]): void {
  expect([...called].sort()).toEqual([...expected].sort());
  // Kept from the previous revision: nothing outside the fixture contract, ever.
  const names = called.map((entry) => entry.replace(/\(.*$/, ""));
  expect(names.filter((name) => !FIXTURE_METHODS.has(name))).toEqual([]);
}

async function openLaw(page: Page, count: number, withItemDigest: boolean): Promise<string[]> {
  const called = await routeMcp(page, count, withItemDigest);
  await page.goto(`/?space=law&work=${WORK}&date=${PINNED}&mode=read`,
    { waitUntil: "domcontentloaded" });
  await expect(page.locator("article.art").first()).toContainText("Fixture article 1");
  return called;
}

/** What the reader actually gets, read back from the clipboard rather than from the DOM. */
async function copiedCitation(page: Page): Promise<string> {
  const copy = page.getByRole("button", { name: "copy citation" });
  await expect(copy).toBeVisible();
  await copy.click();
  await expect(page.getByRole("button", { name: "citation copied" })).toBeVisible();
  return page.evaluate(() => navigator.clipboard.readText());
}

test.use({ permissions: ["clipboard-read", "clipboard-write"] });

test("a copied multi-article citation states that no aggregate text digest is recorded",
  async ({ page }) => {
    const called = await openLaw(page, 3, false);
    const citation = await copiedCitation(page);

    // The claim under test. A metadata digest must not stand in for a wording digest.
    expect(citation).toContain("no aggregate text digest recorded");
    // Mutation: remove `recordSha256={loaded.recordSha256}` from the Provision call site in
    // App.tsx and this assertion fails while every src/*.test.ts stays green.
    expect(citation).toContain(`record SHA-256 ${RECORD_SHA} (version metadata)`);
    // Mutation: remove `bodySha256={loaded.bodySha256}` and this one fails alone.
    expect(citation).toContain(`body SHA-256 ${BODY_SHA} (publisher body)`);
    expect(citation).toContain("Lex reading aid, not an official publication");
    expectOperations(called, LAW_READ);
  });

test("a copied single-article citation carries the exact wording digest and no absence notice",
  async ({ page }) => {
    const called = await openLaw(page, 1, true);
    const citation = await copiedCitation(page);

    expect(citation).toContain(`text SHA-256 ${articleSha(1, "plain")}`);
    // The narrowest claim is available, so the absence sentence must not appear beside it.
    expect(citation).not.toContain("no aggregate text digest recorded");
    expectOperations(called, LAW_READ);
  });

test("the copied citation is rebuilt when the reader moves to another article",
  async ({ page }) => {
    const called = await openLaw(page, 3, true);
    const first = await copiedCitation(page);
    expect(first).toContain("no aggregate text digest recorded");

    // Narrowing to one article must change what the citation can claim. If state were stale the
    // reader would copy a whole-document citation while looking at one article.
    //
    // The third article, not the first. Narrowing to `art-1` could be satisfied by a fixture that
    // ignored the anchor and returned the first article anyway, which is how this journey used to
    // pass. `art-3` is only reachable by honouring the exact requested identity.
    await page.goto(`/?space=law&work=${WORK}&date=${PINNED}&anchor=art-3&mode=read`,
      { waitUntil: "domcontentloaded" });
    await expect(page.locator("article.art").first()).toContainText("Fixture article 3");
    const narrowed = await copiedCitation(page);
    // Not merely different. The narrowed view holds one article, so the citation must name that
    // article and carry its own wording digest, and must drop the absence statement. Asserting
    // inequality alone would pass on any change, including the wrong one.
    expect(narrowed).toContain("Art. 3");
    expect(narrowed).toContain(`text SHA-256 ${articleSha(3, "plain")}`);
    expect(narrowed).not.toContain(`text SHA-256 ${articleSha(1, "plain")}`);
    expect(narrowed).not.toContain("no aggregate text digest recorded");
    expectOperations(called, [
      ...LAW_READ,
      TIMELINE,
      asOf(PINNED, "outline"),
      asOf(PINNED, "select", "art-3"),
      history("art-3"),
    ]);
  });

test("a copied comparison citation carries a labelled digest for each side",
  async ({ page }) => {
    const called = await routeMcp(page, 3, false);
    await page.goto(`/?space=law&work=${WORK}&mode=compare&date=${PINNED}&to=${LATER}`,
      { waitUntil: "domcontentloaded" });
    const citation = await copiedCitation(page);

    // UI-O4 applies to both sides. Each side states the absence separately, and each carries its
    // own record digest labelled for what it covers. A citation reusing one side's digest for both
    // would pass an inequality check and fail here.
    expect(citation).toContain(`${PINNED} no aggregate text digest recorded`);
    expect(citation).toContain(`${LATER} no aggregate text digest recorded`);
    expect(citation).toContain(`${PINNED} record SHA-256 ${RECORD_SHA} (version metadata)`);
    expect(citation).toContain(`${LATER} record SHA-256 ${LATER_RECORD_SHA} (version metadata)`);
    expect(citation).toContain("Lex reading aid, not an official publication");
    expectOperations(called, COMPARE_WHOLE);
  });

test("a copied comparison citation names the official source for each side", async ({ page }) => {
  const called = await routeMcp(page, 3, false);
  await page.goto(`/?space=law&work=${WORK}&mode=compare&date=${PINNED}&to=${LATER}`,
    { waitUntil: "domcontentloaded" });
  const citation = await copiedCitation(page);

  // O4. Both sources are already carried and already rendered in the Markdown export; only the
  // citation dropped them, which is the one artifact people paste into documents and the one that
  // calls Lex a reading aid. Naming the aid without naming the record leaves nowhere to check.
  expect(citation).toContain(`${PINNED} source ${SOURCE_PINNED}`);
  expect(citation).toContain(`${LATER} source ${SOURCE_LATER}`);
  expectOperations(called, COMPARE_WHOLE);
});

test("a copied citation for an article whose wording did not change keeps both exact digests",
  async ({ page }) => {
    // O5. In added-article mode `art-1` is untouched between the two dates, so scoping the
    // comparison to it produces no changed row at all. Classifying from the row count therefore
    // reported no aggregate digest precisely where both exact digests existed, which is the
    // strongest claim the citation can make and the one it was dropping.
    const called = await routeMcp(page, 1, true, "add");
    await page.goto(`/?space=law&work=${WORK}&mode=compare&date=${PINNED}&to=${LATER}&anchor=art-1`,
      { waitUntil: "domcontentloaded" });
    const citation = await copiedCitation(page);

    const sha = articleSha(1, "plain");
    expect(citation).toContain(`${PINNED} text SHA-256 ${sha}`);
    expect(citation).toContain(`${LATER} text SHA-256 ${sha}`);
    expect(citation).not.toContain("no aggregate text digest recorded");
    expect(citation).toContain("Art. 1");
    expectOperations(called, COMPARE_ART1);
  });

test("a copied citation for a typographic-only change keeps both exact digests",
  async ({ page }) => {
    // O5, the second of its two branches, and the one the previous revision claimed without
    // reaching. An identical article never enters `moved` and its text is never fetched. A
    // typographic change does enter `moved`, is fetched and diffed, yields no changed pieces, and
    // is then filed under punctuation and removed from the rows. Both end with an empty `rows`,
    // through different code, and only the scope survives either.
    const called = await routeMcp(page, 1, true, "punctuation");
    await page.goto(`/?space=law&work=${WORK}&mode=compare&date=${PINNED}&to=${LATER}&anchor=art-1`,
      { waitUntil: "domcontentloaded" });
    const citation = await copiedCitation(page);

    // The digests differ here, unlike the identical case, because the bytes really did move. Both
    // are exact and both must survive.
    expect(citation).toContain(`${PINNED} text SHA-256 ${articleSha(1, "plain")}`);
    expect(citation).toContain(`${LATER} text SHA-256 ${articleSha(1, "punctuation")}`);
    expect(articleSha(1, "plain")).not.toEqual(articleSha(1, "punctuation"));
    expect(citation).not.toContain("no aggregate text digest recorded");
    expectOperations(called, [
      ...COMPARE_ART1,
      asOf(PINNED, "select", "art-1"),
      asOf(LATER, "select", "art-1"),
    ]);
  });

test("a copied citation for an added article says it was not present, not that a digest is missing",
  async ({ page }) => {
    // One article exists only at the later date, so the comparison has a single added row and the
    // earlier side holds no provision at all.
    const called = await routeMcp(page, 1, true, "add");
    await page.goto(`/?space=law&work=${WORK}&mode=compare&date=${PINNED}&to=${LATER}`,
      { waitUntil: "domcontentloaded" });
    const citation = await copiedCitation(page);

    // The whole of O1: an absent provision is a different condition from an unrecorded digest, and
    // the citation must state the one that is true. Before the repair this side read
    // `2020-01-01 no aggregate text digest recorded`, which describes text that exists.
    expect(citation).toContain(`${PINNED} not present in this version`);
    expect(citation).not.toContain(`${PINNED} no aggregate text digest recorded`);
    expect(citation).not.toContain(`${PINNED} record SHA-256`);
    expectOperations(called, [...COMPARE_WHOLE, asOf(LATER, "select", "art-2")]);
  });

test("the citation controls survive a 320 pixel viewport without horizontal overflow",
  async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 720 });
    const called = await openLaw(page, 3, false);
    await expect(page.getByRole("button", { name: "copy citation" })).toBeVisible();
    const overflow = await page.evaluate(() =>
      Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth));
    expect(overflow).toBeLessThanOrEqual(0);
    expectOperations(called, LAW_READ);
  });

test("the law view with citation controls has no serious accessibility violation",
  async ({ page }) => {
    const called = await openLaw(page, 3, false);
    const result = await new AxeBuilder({ page }).analyze();
    const severe = result.violations.filter((violation) =>
      violation.impact === "serious" || violation.impact === "critical");
    expect(severe, severe.map((violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(" ")).join(", ")}`)
      .join("\n")).toEqual([]);
    expectOperations(called, LAW_READ);
  });
