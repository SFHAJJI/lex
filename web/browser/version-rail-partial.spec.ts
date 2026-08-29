import { expect, test, type Page, type Route } from "@playwright/test";

/**
 * D19. The version rail's own count is a claim about the law. "12 versions" says the law has twelve.
 *
 * The rail is filled from two places. The deterministic route fetches `timeline` at limit 400
 * (`App.tsx`), and an assistant timeline answer reseeds it through `assistantTimelineSeed`, which has
 * always returned `truncated` alongside the rows. Both paths dropped it, so a rail built from a page
 * of versions counted them as if they were all of them, and the rail is the last place a reader would
 * think to doubt.
 *
 * The reseed case is not hypothetical. The deterministic timeline effect depends on `[s.work]` alone,
 * so when the assistant answers about the work already open the effect does not re-run and the seed
 * stands unopposed. That is the journey below: open a complete rail, then let a truncated assistant
 * answer replace it.
 *
 * `truncated` is read by identity throughout. Neither response is validated on the way in, and a
 * value that is not exactly `true` must not withdraw the completeness claim by accident, nor assert
 * one. This is the D08 O1 and D09 O1 rule applied where it belongs.
 *
 * The `/mcp` and `/api/ask/stream` responses are controlled fixtures and the route fails closed. No
 * law payload, no query text and no golden content is read or written by this file.
 */

const WORK = "lu-legilux:fixture-rail";
const PINNED = "2020-07-17";
const DATES = ["2018-01-01", "2019-01-01", PINNED];

const mcpBody = (id: number, payload: unknown) => JSON.stringify({
  jsonrpc: "2.0", id,
  result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
});

const timelineAnswer = (truncated: unknown) => {
  const unit: Record<string, unknown> = {
    envelope: { publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
                timeline_semantics: "publisher_applicability" },
    versions: DATES.map((valid_from) => ({
      valid_from, language: "fr", text_available: true, document_type: "LOI",
      source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
    })),
    total_count: DATES.length,
  };
  if (truncated !== undefined) unit.truncated = truncated;
  return [unit];
};

const lawAnswer = () => [{
  envelope: { publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
              timeline_semantics: "publisher_applicability" },
  document: {
    title: "Fixture law", language: "fr", valid_from: PINNED,
    extraction_profile: "akn-lu/1",
    source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
  },
  provisions: [{ anchor: "art-1", num: "Art. 1", heading: "Un", text: "Texte un." }],
}];

/** Serve only what this fixture models; refuse anything else rather than falling through. */
async function routeMcp(page: Page, truncated: unknown): Promise<void> {
  await page.route("**/mcp", async (route: Route) => {
    const request = route.request().postDataJSON() as {
      id: number; params?: { name?: string; arguments?: Record<string, unknown> };
    };
    const name = request.params?.name ?? "";
    const args = (request.params?.arguments ?? {}) as Record<string, unknown>;
    if ((name !== "timeline" && name !== "as_of") || args.work !== WORK) {
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: JSON.stringify({
          jsonrpc: "2.0", id: request.id,
          error: { code: -32602, message: `rail fixture does not serve ${name}` },
        }),
      });
      return;
    }
    await route.fulfill({
      status: 200, contentType: "application/json",
      body: mcpBody(request.id, name === "timeline" ? timelineAnswer(truncated) : lawAnswer()),
    });
  });
}

async function openLaw(page: Page, truncated: unknown) {
  await routeMcp(page, truncated);
  await page.goto(`/?space=law&work=${WORK}&date=${PINNED}&mode=read`,
                  { waitUntil: "domcontentloaded" });
  const head = page.locator(".railhead");
  await expect(head).toBeVisible();
  return head;
}

test("a truncated timeline response stops the rail counting versions as the whole list",
  async ({ page }) => {
    const head = await openLaw(page, true);

    await expect(head).toContainText("returned in this response");
    // The count itself is still shown; what changes is that it stops being a claim about the law.
    await expect(head).toContainText(String(DATES.length));
  });

test("a complete timeline response leaves the count unqualified", async ({ page }) => {
  const head = await openLaw(page, false);

  await expect(head).toContainText(`${DATES.length} versions`);
  await expect(head).not.toContainText("returned in this response");
});

test("a response with no truncation field claims nothing either way", async ({ page }) => {
  const head = await openLaw(page, undefined);

  await expect(head).not.toContainText("returned in this response");
});

/**
 * Values the declaration forbids and the transport can deliver. Under truthiness the string and the
 * number would each qualify a list that was never reported as cut.
 */
for (const [label, value] of [
  ["a string", "true"], ["a number", 1], ["null", null],
] as readonly (readonly [string, unknown])[]) {
  test(`a truncation field that is ${label} qualifies nothing`, async ({ page }) => {
    const head = await openLaw(page, value);

    await expect(head).not.toContainText("returned in this response");
  });
}

test("an assistant answer that reseeds the rail from a shorter list says so", async ({ page }) => {
  // A complete rail first, so the reseed is replacing something the reader already trusts.
  const head = await openLaw(page, false);
  await expect(head).toContainText(`${DATES.length} versions`);

  const requestId = "7023456789abcdef0123456789abcdef";
  const seeded = DATES.slice(0, 2);
  const operation = {
    operation_id: `${requestId}:op-1`, order: 0, tool: "timeline",
    result_class: null, disposition: "answer", legal_outcome: "answer",
    transport_outcome: "completed", effects: ["timeline"],
    ui: {
      timeline: {
        subject: { work: WORK, title: "Fixture law", language: "fr" },
        rows: seeded.map((valid_from) => ({ valid_from, language: "fr" })),
        total_count: DATES.length,
        truncated: true,
      },
    },
  };
  await page.addInitScript(({ requestId, operation }) => {
    const originalFetch = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const url = typeof input === "string" ? input
        : input instanceof URL ? input.href : input.url;
      if (!url.endsWith("/api/ask/stream")) return originalFetch(input, init);
      const encoder = new TextEncoder();
      const envelope = (sequence: number, payload: unknown) => JSON.stringify({
        version: "1", request_id: requestId, sequence, server_elapsed_ms: 10, payload,
      });
      const stream = new ReadableStream<Uint8Array>({
        start(controller) {
          controller.enqueue(encoder.encode(
            `event: operation_result\ndata: ${envelope(1, operation)}\n\n`));
          controller.enqueue(encoder.encode(`event: done\ndata: ${envelope(2, {
            reply: "Here are the versions.", operations: [operation], ui: operation.ui,
          })}\n\n`));
          controller.close();
        },
      });
      return Promise.resolve(new Response(stream, {
        status: 200,
        headers: { "Content-Type": "text/event-stream", "X-Lex-Request-Id": requestId },
      }));
    };
  }, { requestId, operation });

  await page.reload({ waitUntil: "domcontentloaded" });
  const launcher = page.getByRole("button", { name: "Open Ask Lex legal research assistant" });
  if (await launcher.count() > 0) await launcher.click();
  await expect(page.locator(".askpanel")).toBeVisible();
  await page.getByRole("textbox", { name: "Ask Lex" }).fill("what versions are there");
  await page.getByRole("button", { name: "Ask", exact: true }).click();

  // The rail now holds fewer versions than it did a moment ago, and must say so.
  await expect(page.locator(".railhead")).toContainText("returned in this response");
});
