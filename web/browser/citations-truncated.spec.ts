import { expect, test, type Page, type Route } from "@playwright/test";

/**
 * D09 stratum A. The publisher budget can cut an article's references. `McpCore.cs:483` sets
 * `citations_truncated` on that provision, and `McpCore.cs:476` writes the `citations` array only
 * when at least one row survived. So an article whose references were ALL cut arrives with no
 * citations array and the truncation flag set, which is byte-identical in shape to an article that
 * genuinely refers to nothing.
 *
 * The client declared no such field, and `views.tsx` rendered the section on `citations.length > 0`
 * alone, so both cases reached the same branch and rendered nothing. That is absent evidence
 * presented as a negative fact, which is the one claim this product may not make.
 *
 * The pair that matters is art-3 against art-4. Everything else here is context for it.
 *
 * The `/mcp` route is a controlled fixture and fails closed, so nothing this file does not model
 * leaves the browser. No law payload, no query text and no golden content is read or written here.
 */

const WORK = "lu-legilux:fixture-cites";
const PINNED = "2020-07-17";
const CITED = "Loi du 1er janvier 2000";

const mcpBody = (id: number, payload: unknown) => JSON.stringify({
  jsonrpc: "2.0", id,
  result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
});

/**
 * Four articles covering every combination the producer can emit.
 *
 * art-1 complete, art-2 cut with some references surviving, art-3 cut with none surviving,
 * art-4 genuinely holds no references. No heading here may contain the wording under test: the
 * first draft titled art-4 "Refers to nothing", which satisfied the assertion from the heading
 * alone and would have passed with the repair deleted. art-3 and art-4 are the two the producer cannot tell apart
 * without the flag, and the two a reader must not be shown identically.
 */
const lawAnswer = (): Record<string, unknown>[] => [{
  envelope: {
    publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
    timeline_semantics: "publisher_applicability",
  },
  document: {
    title: "Fixture law", language: "fr", valid_from: PINNED,
    extraction_profile: "akn-lu/1",
    source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
  },
  provisions: [
    { anchor: "art-1", num: "Art. 1", heading: "Complete", text: "Texte un.",
      citations: [{ work: "lu-legilux:cited-work", href: "#", text: CITED }] },
    { anchor: "art-2", num: "Art. 2", heading: "Partly cut", text: "Texte deux.",
      citations: [{ work: "lu-legilux:cited-work", href: "#", text: CITED }],
      citations_truncated: true },
    { anchor: "art-3", num: "Art. 3", heading: "Entirely cut", text: "Texte trois.",
      citations_truncated: true },
    { anchor: "art-4", num: "Art. 4", heading: "No references", text: "Texte quatre." },
  ],
}];

const timelineAnswer = (): Record<string, unknown>[] => [{
  envelope: { publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
              timeline_semantics: "publisher_applicability" },
  versions: [{ valid_from: PINNED, language: "fr", text_available: true, document_type: "LOI",
               source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo" }],
}];

/** Serve only what this fixture models, and refuse everything else rather than falling through. */
async function routeMcp(page: Page): Promise<void> {
  await page.route("**/mcp", async (route: Route) => {
    const request = route.request().postDataJSON() as {
      id: number; params?: { name?: string; arguments?: Record<string, unknown> };
    };
    const name = request.params?.name ?? "";
    const args = (request.params?.arguments ?? {}) as Record<string, unknown>;
    const servable = (name === "as_of" || name === "timeline") && args.work === WORK;
    if (!servable) {
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: JSON.stringify({
          jsonrpc: "2.0", id: request.id,
          error: { code: -32602, message: `citations fixture does not serve ${name}` },
        }),
      });
      return;
    }
    await route.fulfill({
      status: 200, contentType: "application/json",
      body: mcpBody(request.id, name === "timeline" ? timelineAnswer() : lawAnswer()),
    });
  });
}

async function openLaw(page: Page) {
  await routeMcp(page);
  await page.goto(`/?space=law&work=${WORK}&date=${PINNED}&mode=read`,
                  { waitUntil: "domcontentloaded" });
  await expect(page.locator("article.art#art-1")).toBeVisible();
}

test("an article whose references were all cut does not read as one that refers to nothing",
  async ({ page }) => {
    await openLaw(page);
    const cut = page.locator("article.art#art-3");
    const none = page.locator("article.art#art-4");

    // The whole row in one assertion: the two cases must not render the same thing.
    await expect(cut).toContainText("Refers to");
    await expect(cut).toContainText("not returned in this response");
    await expect(none).not.toContainText("Refers to");
    await expect(none).not.toContainText("not returned in this response");

    // The cut article states an omission, not a partial list, so it must not claim some arrived.
    await expect(cut).not.toContainText("more not returned");
  });

test("a partly cut list says so beside the references that did arrive", async ({ page }) => {
  await openLaw(page);
  const partial = page.locator("article.art#art-2");

  await expect(partial).toContainText("Refers to");
  await expect(partial).toContainText(CITED);
  await expect(partial).toContainText("more not returned in this response");
});

test("a complete list is presented without qualification", async ({ page }) => {
  await openLaw(page);
  const complete = page.locator("article.art#art-1");

  await expect(complete).toContainText("Refers to");
  await expect(complete).toContainText(CITED);
  // No disclosure may appear where nothing was cut, or the disclosure means nothing anywhere.
  await expect(complete).not.toContainText("not returned in this response");
});
