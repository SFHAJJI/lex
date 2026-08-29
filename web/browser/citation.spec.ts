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
const TEXT_SHA = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
const LATER = "2021-01-01";
const LATER_RECORD_SHA = "9988776655443322110099887766554433221100998877665544332211009988";

/** A version whose document carries both document-level digests, with `count` articles. */
function lawAnswer(
  count: number, withItemDigest: boolean, date: string = PINNED, addedAtLater: boolean = false,
): Record<string, unknown>[] {
  const later = date === LATER;
  // One extra article exists only at the later date, so the comparison yields exactly one added
  // row: the O1 shape, reached through the mounted application rather than the pure function.
  const total = addedAtLater && later ? count + 1 : count;
  return [{
    envelope: {
      publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
      timeline_semantics: "publisher_applicability",
    },
    document: {
      title: "Fixture law", language: "fr", valid_from: date,
      extraction_profile: "akn-lu/1",
      source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
      // Distinct per side, so a comparison citation that carried one side's digest for both, or
      // dropped one, is visible rather than plausible.
      record_sha256: later ? LATER_RECORD_SHA : RECORD_SHA,
      body_sha256: BODY_SHA,
    },
    provisions: Array.from({ length: total }, (_unused, index) => ({
      anchor: `art-${index + 1}`,
      num: `Art. ${index + 1}`,
      heading: `Heading ${index + 1}`,
      text: `Fixture article ${index + 1}${later ? " as amended" : ""}.`,
      ...(withItemDigest ? { text_sha256: TEXT_SHA } : {}),
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
  states: [{ valid_from: PINNED, text_sha256: TEXT_SHA }],
}];

const mcpBody = (id: number, payload: unknown) => JSON.stringify({
  jsonrpc: "2.0", id,
  result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
});

const FIXTURE_METHODS = new Set(["timeline", "as_of", "article_history"]);

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
  page: Page, count: number, withItemDigest: boolean, addedAtLater: boolean = false,
): Promise<string[]> {
  const called: string[] = [];
  await page.route("**/mcp", async (route: Route) => {
    const request = route.request().postDataJSON() as {
      id: number; params?: { name?: string; arguments?: Record<string, unknown> };
    };
    const name = request.params?.name ?? "";
    called.push(name);
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
      // The producer honours an anchor selection, so the fixture must too. Returning every article
      // regardless made the narrowing journey pass for the wrong reason: the permalink changed
      // while the citation still described a whole document.
      const anchors = typeof args.anchors === "string" && args.anchors.length > 0
        ? args.anchors.split(",").length
        : count;
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: mcpBody(
          request.id, lawAnswer(Math.min(anchors, count), withItemDigest, date, addedAtLater)),
      });
      return;
    }
    // A refusal the application can parse, rather than a network error or a real answer. The
    // journey then fails on the recorded method set, which names the offending method.
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
 * Every journey ends with this.
 *
 * The first assertion is the point: no method escaped to a real MCP endpoint. The second exists
 * because the first alone would pass forever on a journey that stopped reaching the wire at all,
 * which is the empty-baseline failure this repository has been bitten by before.
 */
function expectFixtureServedEverything(called: string[]): void {
  expect(called.filter((name) => !FIXTURE_METHODS.has(name))).toEqual([]);
  expect(called.length).toBeGreaterThan(0);
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
    expectFixtureServedEverything(called);
  });

test("a copied single-article citation carries the exact wording digest and no absence notice",
  async ({ page }) => {
    const called = await openLaw(page, 1, true);
    const citation = await copiedCitation(page);

    expect(citation).toContain(`text SHA-256 ${TEXT_SHA}`);
    // The narrowest claim is available, so the absence sentence must not appear beside it.
    expect(citation).not.toContain("no aggregate text digest recorded");
    expectFixtureServedEverything(called);
  });

test("the copied citation is rebuilt when the reader moves to another article",
  async ({ page }) => {
    const called = await openLaw(page, 3, true);
    const first = await copiedCitation(page);
    expect(first).toContain("no aggregate text digest recorded");

    // Narrowing to one article must change what the citation can claim. If state were stale the
    // reader would copy a whole-document citation while looking at one article.
    await page.goto(`/?space=law&work=${WORK}&date=${PINNED}&anchor=art-1&mode=read`,
      { waitUntil: "domcontentloaded" });
    await expect(page.locator("article.art").first()).toContainText("Fixture article 1");
    const narrowed = await copiedCitation(page);
    // Not merely different. The narrowed view has one article, so it must gain the exact wording
    // digest and lose the absence statement. Asserting only inequality would pass on any change.
    expect(narrowed).toContain(`text SHA-256 ${TEXT_SHA}`);
    expect(narrowed).not.toContain("no aggregate text digest recorded");
    expectFixtureServedEverything(called);
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
    expectFixtureServedEverything(called);
  });

test("a copied citation for an added article says it was not present, not that a digest is missing",
  async ({ page }) => {
    // One article exists only at the later date, so the comparison has a single added row and the
    // earlier side holds no provision at all.
    const called = await routeMcp(page, 1, true, true);
    await page.goto(`/?space=law&work=${WORK}&mode=compare&date=${PINNED}&to=${LATER}`,
      { waitUntil: "domcontentloaded" });
    const citation = await copiedCitation(page);

    // The whole of O1: an absent provision is a different condition from an unrecorded digest, and
    // the citation must state the one that is true. Before the repair this side read
    // `2020-01-01 no aggregate text digest recorded`, which describes text that exists.
    expect(citation).toContain(`${PINNED} not present in this version`);
    expect(citation).not.toContain(`${PINNED} no aggregate text digest recorded`);
    expect(citation).not.toContain(`${PINNED} record SHA-256`);
    expectFixtureServedEverything(called);
  });

test("the citation controls survive a 320 pixel viewport without horizontal overflow",
  async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 720 });
    const called = await openLaw(page, 3, false);
    await expect(page.getByRole("button", { name: "copy citation" })).toBeVisible();
    const overflow = await page.evaluate(() =>
      Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth));
    expect(overflow).toBeLessThanOrEqual(0);
    expectFixtureServedEverything(called);
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
    expectFixtureServedEverything(called);
  });
