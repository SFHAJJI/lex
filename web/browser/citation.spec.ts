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
 * The `/mcp` responses are controlled fixtures. No law payload, no query text and no golden content
 * is read or written by this file.
 */

const WORK = "lu-legilux:fixture-citation";
const PINNED = "2020-07-17";
const RECORD_SHA = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";
const BODY_SHA = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
const TEXT_SHA = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

/** A version whose document carries both document-level digests, with `count` articles. */
function lawAnswer(count: number, withItemDigest: boolean): Record<string, unknown>[] {
  return [{
    envelope: {
      publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
      timeline_semantics: "publisher_applicability",
    },
    document: {
      title: "Fixture law", language: "fr", valid_from: PINNED,
      extraction_profile: "akn-lu/1",
      source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
      record_sha256: RECORD_SHA,
      body_sha256: BODY_SHA,
    },
    provisions: Array.from({ length: count }, (_unused, index) => ({
      anchor: `art-${index + 1}`,
      num: `Art. ${index + 1}`,
      heading: `Heading ${index + 1}`,
      text: `Fixture article ${index + 1}.`,
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

const mcpBody = (id: number, payload: unknown) => JSON.stringify({
  jsonrpc: "2.0", id,
  result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
});

/** Answer `as_of` and `timeline` from fixtures; pass everything else to the real server. */
async function routeMcp(page: Page, count: number, withItemDigest: boolean): Promise<void> {
  await page.route("**/mcp", async (route: Route) => {
    const request = route.request().postDataJSON() as {
      id: number; params?: { name?: string };
    };
    const name = request.params?.name ?? "";
    if (name === "timeline") {
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: mcpBody(request.id, timelineAnswer()),
      });
      return;
    }
    if (name === "as_of") {
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: mcpBody(request.id, lawAnswer(count, withItemDigest)),
      });
      return;
    }
    await route.fallback();
  });
}

async function openLaw(page: Page, count: number, withItemDigest: boolean): Promise<void> {
  await routeMcp(page, count, withItemDigest);
  await page.goto(`/?space=law&work=${WORK}&date=${PINNED}&mode=read`,
    { waitUntil: "domcontentloaded" });
  await expect(page.locator("article.art").first()).toContainText("Fixture article 1");
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
    await openLaw(page, 3, false);
    const citation = await copiedCitation(page);

    // The claim under test. A metadata digest must not stand in for a wording digest.
    expect(citation).toContain("no aggregate text digest recorded");
    // Mutation: remove `recordSha256={loaded.recordSha256}` from the Provision call site in
    // App.tsx and this assertion fails while every src/*.test.ts stays green.
    expect(citation).toContain(`record SHA-256 ${RECORD_SHA} (version metadata)`);
    // Mutation: remove `bodySha256={loaded.bodySha256}` and this one fails alone.
    expect(citation).toContain(`body SHA-256 ${BODY_SHA} (publisher body)`);
    expect(citation).toContain("Lex reading aid, not an official publication");
  });

test("a copied single-article citation carries the exact wording digest and no absence notice",
  async ({ page }) => {
    await openLaw(page, 1, true);
    const citation = await copiedCitation(page);

    expect(citation).toContain(`text SHA-256 ${TEXT_SHA}`);
    // The narrowest claim is available, so the absence sentence must not appear beside it.
    expect(citation).not.toContain("no aggregate text digest recorded");
  });

test("the copied citation is rebuilt when the reader moves to another article",
  async ({ page }) => {
    await openLaw(page, 3, true);
    const first = await copiedCitation(page);
    expect(first).toContain("no aggregate text digest recorded");

    // Narrowing to one article must change what the citation can claim. If state were stale the
    // reader would copy a whole-document citation while looking at one article.
    await page.goto(`/?space=law&work=${WORK}&date=${PINNED}&anchor=art-1&mode=read`,
      { waitUntil: "domcontentloaded" });
    await expect(page.locator("article.art").first()).toContainText("Fixture article 1");
    const narrowed = await copiedCitation(page);
    expect(narrowed).not.toEqual(first);
  });

test("the citation controls survive a 320 pixel viewport without horizontal overflow",
  async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 720 });
    await openLaw(page, 3, false);
    await expect(page.getByRole("button", { name: "copy citation" })).toBeVisible();
    const overflow = await page.evaluate(() =>
      Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth));
    expect(overflow).toBeLessThanOrEqual(0);
  });

test("the law view with citation controls has no serious accessibility violation",
  async ({ page }) => {
    await openLaw(page, 3, false);
    const result = await new AxeBuilder({ page }).analyze();
    const severe = result.violations.filter((violation) =>
      violation.impact === "serious" || violation.impact === "critical");
    expect(severe, severe.map((violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(" ")).join(", ")}`)
      .join("\n")).toEqual([]);
  });
