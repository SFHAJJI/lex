import { AxeBuilder } from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { readFileSync } from "node:fs";

const budgets = JSON.parse(readFileSync(new URL("./budgets.json", import.meta.url), "utf8"));
const PUBLIC_ROUTES = [
  "/", "/browse", "/coverage", "/in-force-on?date=2024-01-01",
  "/search?q=travail", "/changed?from=2024-01-01&to=2024-12-31", "/find",
  "/how-it-works", "/built", "/about", "/stories", "/architecture",
  "/architecture/next", "/decisions", "/benchmarks", "/verify", "/developers",
];

async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() =>
    Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth));
  expect(overflow).toBeLessThanOrEqual(budgets.rendering.horizontal_overflow_px_max);
}

async function expectNoSeriousAxeViolation(page: Page) {
  const result = await new AxeBuilder({ page }).analyze();
  const severe = result.violations.filter((violation) =>
    violation.impact === "serious" || violation.impact === "critical");
  expect(severe, severe.map((violation) =>
    `${violation.id}: ${violation.nodes.map((node) => node.target.join(" ")).join(", ")}`).join("\n"))
    .toEqual([]);
}

test("the browser-facing route set stays navigable, bounded and accessible", async ({ page }) => {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const networkErrors: string[] = [];
  page.on("console", (message) => { if (message.type() === "error") consoleErrors.push(message.text()); });
  page.on("pageerror", (error) => pageErrors.push(error.message));
  page.on("requestfailed", (request) => networkErrors.push(`${request.method()} ${request.url()}`));
  page.on("response", (response) => {
    if (response.status() >= 400) networkErrors.push(`${response.status()} ${response.url()}`);
  });

  for (const route of PUBLIC_ROUTES) {
    const response = await page.goto(route, { waitUntil: "networkidle" });
    expect(response?.status(), route).toBe(200);
    await expectNoHorizontalOverflow(page);
  }

  for (const route of ["/", "/browse", "/coverage", "/built", "/developers"]) {
    await page.goto(route, { waitUntil: "networkidle" });
    await expectNoSeriousAxeViolation(page);
  }
  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(networkErrors).toEqual([]);
});

test("every public page reflows at the frozen desktop, modal, phone and landscape widths", async ({ page }) => {
  test.setTimeout(120_000);
  for (const [width, height] of [[1100, 800], [1099, 800], [320, 568], [568, 320]]) {
    await page.setViewportSize({ width, height });
    for (const route of PUBLIC_ROUTES) {
      const response = await page.goto(route, { waitUntil: "networkidle" });
      expect(response?.status(), `${route} at ${width}x${height}`).toBe(200);
      await expectNoHorizontalOverflow(page);
    }
  }
});

test("Check the work supports click, keyboard and hover semantics", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/");
  const disclosure = page.locator("details.proofnav");
  const summary = disclosure.locator("summary");

  await summary.click();
  await expect(disclosure).toHaveAttribute("open", "");
  await page.keyboard.press("Escape");
  await expect(disclosure).not.toHaveAttribute("open", "");
  await expect(summary).toBeFocused();

  // Exercise hover independently of the preceding keyboard path. A disclosure with focus still
  // inside is expected to stay open when the pointer leaves.
  await page.getByRole("link", { name: "Lex", exact: true }).focus();
  await page.mouse.move(1000, 500);
  await summary.hover();
  await expect(disclosure).toHaveAttribute("open", "");
  await page.mouse.move(1000, 500);
  await expect(disclosure).not.toHaveAttribute("open", "", { timeout: 1_000 });

});

test("Check the work uses native touch disclosure semantics", async ({ browser }, testInfo) => {
  const context = await browser.newContext({
    baseURL: String(testInfo.project.use.baseURL),
    viewport: { width: 320, height: 568 },
    hasTouch: true,
    isMobile: true,
  });
  try {
    const page = await context.newPage();
    await page.goto("/");
    const disclosure = page.locator("details.proofnav");
    await disclosure.locator("summary").tap();
    await expect(disclosure).toHaveAttribute("open", "");
    await expect(disclosure.getByRole("link", { name: "Verify the artifacts" })).toBeVisible();
  } finally {
    await context.close();
  }
});

test("the assistant crosses from complementary dock to a true modal at the frozen boundary", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/");
  await page.getByRole("button", { name: "Open Ask Lex legal research assistant" }).click();
  const dock = page.locator("aside.askpanel");
  await expect(dock).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  const overlap = await page.evaluate(() => {
    const main = document.querySelector("main")!.getBoundingClientRect();
    const panel = document.querySelector(".askpanel")!.getBoundingClientRect();
    return main.right > panel.left;
  });
  expect(overlap).toBe(false);

  await page.setViewportSize({ width: 1099, height: 800 });
  const dialog = page.getByRole("dialog", { name: "Lex legal research assistant" });
  await expect(dialog).toBeVisible();
  expect(await page.locator("body > header, body > main, body > footer").evaluateAll((nodes) =>
    nodes.every((node) => (node as HTMLElement).inert))).toBe(true);
  expect(await page.evaluate(() => getComputedStyle(document.body).overflow)).toBe("hidden");

  await dialog.locator(".ap-form button").focus();
  await page.keyboard.press("Tab");
  await expect(dialog.getByRole("button", { name: "Minimise assistant" })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Open Ask Lex legal research assistant" })).toBeFocused();
});

test("a typed assistant operation is presented within the local browser budget", async ({ page }, testInfo) => {
  const requestId = "0123456789abcdef0123456789abcdef";
  const operation = {
    operation_id: `${requestId}:op-1`, order: 0, tool: "legal_boundary",
    result_class: null, disposition: "legal_boundary", legal_outcome: "legal_boundary",
    transport_outcome: "completed", effects: ["gap"],
    ui: { gap: { status: "legal_boundary", explanation: "Verified text only.", available: [] } },
  };
  await page.addInitScript(({ requestId, operation }) => {
    const originalFetch = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const url = typeof input === "string" ? input : input instanceof URL
        ? input.href : input.url;
      if (!url.endsWith("/api/ask/stream")) return originalFetch(input, init);
      const encoder = new TextEncoder();
      const envelope = (sequence: number, payload: unknown) => JSON.stringify({
        version: "1", request_id: requestId, sequence, server_elapsed_ms: 10, payload,
      });
      const stream = new ReadableStream<Uint8Array>({
        start(controller) {
          controller.enqueue(encoder.encode(
            `event: operation_result\ndata: ${envelope(1, operation)}\n\n`));
          setTimeout(() => {
            controller.enqueue(encoder.encode(`event: done\ndata: ${envelope(2, {
              reply: "Verified text only.", operations: [operation], ui: operation.ui,
            })}\n\n`));
            controller.close();
          }, 650);
        },
      });
      return Promise.resolve(new Response(stream, {
        status: 200,
        headers: {
          "Content-Type": "text/event-stream",
          "X-Lex-Request-Id": requestId,
        },
      }));
    };
  }, { requestId, operation });
  await page.goto("/");
  await page.getByRole("button", { name: "Open Ask Lex legal research assistant" }).click();
  await page.getByRole("textbox", { name: "Ask Lex" }).fill("Can Lex advise me?");
  await page.getByRole("button", { name: "Ask", exact: true }).click();

  await page.waitForFunction(() =>
    performance.getEntriesByName("lex-operation-result-received-to-presented").length === 1);
  await expect(page.getByText("Verified text only.", { exact: true })).toBeVisible();
  const duration = await page.evaluate(() =>
    performance.getEntriesByName("lex-operation-result-received-to-presented")[0].duration);
  await testInfo.attach("assistant-operation-presented.json", {
    body: JSON.stringify({ duration_ms: duration, maximum_ms: 500 }),
    contentType: "application/json",
  });
  expect(duration).toBeLessThanOrEqual(500);
  expect(await page.evaluate(() =>
    performance.getEntriesByName("lex-operation-result-received-to-presented").length)).toBe(1);
  await expect(page.locator("[data-lex-operation-result-id]")).toHaveCount(1);
});

test("an operation without a typed view is never counted as presented", async ({ page }) => {
  const requestId = "1123456789abcdef0123456789abcdef";
  const operation = {
    operation_id: `${requestId}:op-1`, order: 0, tool: "legal_boundary",
    legal_outcome: "legal_boundary", transport_outcome: "completed", effects: ["gap"],
  };
  await page.addInitScript(({ requestId, operation }) => {
    const originalFetch = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const url = typeof input === "string" ? input : input instanceof URL
        ? input.href : input.url;
      if (!url.endsWith("/api/ask/stream")) return originalFetch(input, init);
      const envelope = (sequence: number, payload: unknown) => JSON.stringify({
        version: "1", request_id: requestId, sequence, server_elapsed_ms: 10, payload,
      });
      const body = `event: operation_result\ndata: ${envelope(1, operation)}\n\n`
        + `event: done\ndata: ${envelope(2, {
          reply: "No typed result was available.", operations: [operation],
        })}\n\n`;
      return Promise.resolve(new Response(body, {
        status: 200,
        headers: {
          "Content-Type": "text/event-stream",
          "X-Lex-Request-Id": requestId,
        },
      }));
    };
  }, { requestId, operation });

  await page.goto("/");
  await page.getByRole("button", { name: "Open Ask Lex legal research assistant" }).click();
  await page.getByRole("textbox", { name: "Ask Lex" }).fill("Can Lex advise me?");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await expect(page.locator(".said")).toContainText("No typed result was available.");

  expect(await page.evaluate(() =>
    performance.getEntriesByName("lex-operation-result-received-to-presented").length)).toBe(0);
  await expect(page.locator("[data-lex-operation-result-id]")).toHaveCount(0);
});

test("the first renderable operation is measured after bounded layout retries", async ({ page }) => {
  const requestId = "2123456789abcdef0123456789abcdef";
  const hidden = {
    operation_id: `${requestId}:op-1`, order: 0, tool: "search",
    legal_outcome: "succeeded", transport_outcome: "completed", effects: [],
  };
  const visible = {
    operation_id: `${requestId}:op-2`, order: 1, tool: "legal_boundary",
    legal_outcome: "legal_boundary", transport_outcome: "completed", effects: ["gap"],
    ui: { gap: { status: "legal_boundary", explanation: "Visible typed result.", available: [] } },
  };
  await page.addInitScript(({ requestId, hidden, visible }) => {
    const host = window as typeof window & {
      __lexPresentedOperation?: string;
      __lexPresentationRectReads?: number;
    };
    window.addEventListener("lex:operation-result-presented", (event) => {
      host.__lexPresentedOperation = (event as CustomEvent<{ operation_id: string }>).detail.operation_id;
    });
    const originalRect = HTMLElement.prototype.getBoundingClientRect;
    HTMLElement.prototype.getBoundingClientRect = function () {
      if (this.dataset.lexOperationResultId === `${requestId}:op-2`) {
        host.__lexPresentationRectReads = (host.__lexPresentationRectReads ?? 0) + 1;
        if (host.__lexPresentationRectReads <= 2) {
          const actual = originalRect.call(this);
          return DOMRect.fromRect({ x: actual.x, y: actual.y, width: actual.width, height: 0 });
        }
      }
      return originalRect.call(this);
    };
    const originalFetch = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const url = typeof input === "string" ? input : input instanceof URL
        ? input.href : input.url;
      if (!url.endsWith("/api/ask/stream")) return originalFetch(input, init);
      const envelope = (sequence: number, payload: unknown) => JSON.stringify({
        version: "1", request_id: requestId, sequence, server_elapsed_ms: 10, payload,
      });
      const body = `event: operation_result\ndata: ${envelope(1, hidden)}\n\n`
        + `event: operation_result\ndata: ${envelope(2, visible)}\n\n`
        + `event: done\ndata: ${envelope(3, {
          reply: "Visible typed result.", operations: [hidden, visible], ui: visible.ui,
        })}\n\n`;
      return Promise.resolve(new Response(body, {
        status: 200,
        headers: {
          "Content-Type": "text/event-stream",
          "X-Lex-Request-Id": requestId,
        },
      }));
    };
  }, { requestId, hidden, visible });

  await page.goto("/");
  await page.getByRole("button", { name: "Open Ask Lex legal research assistant" }).click();
  await page.getByRole("textbox", { name: "Ask Lex" }).fill("Can Lex advise me?");
  await page.getByRole("button", { name: "Ask", exact: true }).click();

  await page.waitForFunction((expected) => {
    const host = window as typeof window & { __lexPresentedOperation?: string };
    return host.__lexPresentedOperation === expected;
  }, visible.operation_id);
  expect(await page.evaluate(() => performance
    .getEntriesByName("lex-operation-result-received-to-presented").length)).toBe(1);
  expect(await page.evaluate(() => (window as typeof window & {
    __lexPresentationRectReads?: number;
  }).__lexPresentationRectReads)).toBeGreaterThanOrEqual(3);
  await expect(page.locator("[data-lex-operation-result-id]")).toHaveCount(1);
});

test("the launcher clears interactive content at the smallest supported viewport", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 568 });
  const collisions: string[] = [];

  const intersections = async () => page.evaluate(() => {
    const launcher = document.querySelector<HTMLElement>(".asklaunch")?.getBoundingClientRect();
    if (!launcher) return ["launcher missing"];
    const intersects = (a: DOMRect, b: DOMRect) =>
      a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;
    return [...document.querySelectorAll<HTMLElement>("a,button,input,select,textarea,[tabindex]")]
      .filter((element) => !element.closest(".askslot") && element.getClientRects().length > 0)
      .filter((element) => intersects(launcher, element.getBoundingClientRect()))
      .map((element) => {
        const box = element.getBoundingClientRect();
        const name = element.getAttribute("aria-label") || element.textContent?.trim() || element.tagName;
        return `${name} [${box.x.toFixed(0)},${box.y.toFixed(0)},${box.width.toFixed(0)},${box.height.toFixed(0)}] under launcher [${launcher.x.toFixed(0)},${launcher.y.toFixed(0)},${launcher.width.toFixed(0)},${launcher.height.toFixed(0)}]`;
      });
  });

  const assistantRoutes = [
    "/", "/browse", "/in-force-on?date=2024-01-01", "/search?q=travail",
    "/changed?from=2024-01-01&to=2024-12-31", "/find", "/stories",
  ];
  for (const route of assistantRoutes) {
    await page.goto(route, { waitUntil: "networkidle" });
    await expect(page.locator("body")).toHaveClass(/assistant-enabled/);
    await expect(page.getByRole("button", { name: "Open Ask Lex legal research assistant" })).toBeVisible();
    await page.evaluate(() => scrollTo(0, 0));
    const top = await intersections();
    if (top.length > 0) collisions.push(`${route} at top: ${top.join(", ")}`);
    await page.evaluate(() => scrollTo(0, document.documentElement.scrollHeight));
    const bottom = await intersections();
    if (bottom.length > 0) collisions.push(`${route} at bottom: ${bottom.join(", ")}`);
  }
  expect(collisions).toEqual([]);
});

test("reflow, color and motion preferences keep the page usable", async ({ page }) => {
  for (const width of [640, 320]) {
    await page.setViewportSize({ width, height: 800 });
    for (const colorScheme of ["light", "dark"] as const) {
      await page.emulateMedia({ colorScheme, reducedMotion: "reduce" });
      await page.goto("/browse");
      await expectNoHorizontalOverflow(page);
    }
  }
  await page.emulateMedia({ forcedColors: "active", reducedMotion: "reduce" });
  await page.goto("/");
  await expectNoHorizontalOverflow(page);
  await expect(page.getByRole("button", { name: "Search", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Open Ask Lex legal research assistant" })).toBeVisible();
});

test("workspace state follows browser back and forward navigation", async ({ page }) => {
  await page.goto("/", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: "What changed recently" }).click();
  await expect.poll(() => new URL(page.url()).searchParams.get("space")).toBe("time");
  expect(new URL(page.url()).searchParams.get("order")).toBe("by_churn");
  await expect(page.getByRole("heading", { name: "What changed", exact: true })).toBeVisible();

  await page.goBack();
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole("button", { name: "Search", exact: true })).toBeVisible();

  await page.goForward();
  await expect.poll(() => new URL(page.url()).searchParams.get("space")).toBe("time");
  await expect(page.getByRole("heading", { name: "What changed", exact: true })).toBeVisible();
});

test("official metadata chips apply only the exact server URI and HTTP provenance stays inert",
  async ({ page }) => {
    const identifier = "http://publications.europa.eu/resource/authority/eurovoc/1000";
    const calls: Record<string, unknown>[] = [];
    await page.route("**/mcp", async (route) => {
      const request = route.request().postDataJSON() as {
        id: number;
        params?: { arguments?: Record<string, unknown> };
      };
      calls.push(request.params?.arguments ?? {});
      const payload = [{
        envelope: {
          publisher: "eu-eurlex",
          jurisdiction: "EU",
          timeline_semantics: "official_consolidation_state",
        },
        retrieval_mode: "keyword",
        hits: [{
          lex_id: "eu-eurlex:32022r2554:2024-01-01",
          title: "Digital Operational Resilience Act",
          language: "en",
          valid_from: "2024-01-01",
          valid_to: null,
          match_reasons: ["work_metadata"],
          matched_publisher_metadata: {
            kind: "eurovoc_domain",
            identifier,
            label: "Financial regulation",
            language: "en",
            source_uri: identifier,
          },
        }],
      }];
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          jsonrpc: "2.0",
          id: request.id,
          result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
        }),
      });
    });

    await page.goto("/?space=search&q=capital", { waitUntil: "networkidle" });
    const chip = page.getByRole("button", {
      name: /Publisher EuroVoc domain.*Financial regulation/,
    });
    await expect(chip).toBeVisible();
    await expect(page.locator(".publisher-metadata a")).toHaveCount(0);
    await expect(page.locator(".publisher-metadata-source"))
      .toHaveAttribute("title", identifier);

    await chip.click();
    await expect.poll(() => calls.some((call) =>
      call.publisher_metadata_identifier === identifier)).toBe(true);
    await expect(page.getByRole("status")).toContainText("Financial regulation");
    expect(new URL(page.url()).searchParams.has("publisher_metadata_identifier")).toBe(false);
    expect(await page.evaluate((opaque) =>
      [...Object.values(localStorage), ...Object.values(sessionStorage)]
        .some((value) => value.includes(opaque)), identifier)).toBe(false);

    await page.getByRole("textbox", { name: "Search for a law, an identifier, or words in the text" })
      .fill("new question");
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await expect.poll(() => calls.some((call) => call.query === "new question"
      && call.publisher_metadata_identifier === undefined)).toBe(true);
    await expect(page.getByRole("status")).toHaveCount(0);
    await expectNoHorizontalOverflow(page);
    await expectNoSeriousAxeViolation(page);
  });
