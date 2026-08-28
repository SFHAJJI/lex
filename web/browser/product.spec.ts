import { AxeBuilder } from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { readFileSync } from "node:fs";

const budgets = JSON.parse(readFileSync(new URL("./budgets.json", import.meta.url), "utf8"));
const PUBLIC_ROUTES = [
  "/", "/browse", "/coverage", "/in-force-on?date=2024-01-01",
  "/search?q=travail", "/changed?from=2024-01-01&to=2024-12-31", "/find",
  "/how-it-works", "/built", "/built/model", "/built/data", "/built/retrieval",
  "/built/assistant", "/built/release", "/built/decisions", "/built/incidents",
  "/built/limits", "/architecture/dossier", "/about", "/stories", "/decisions",
  "/benchmarks", "/verify", "/developers",
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

// The assistant opens on a first arrival, so the launcher is present only after a reader closes it.
// Opening is therefore "make sure it is open", not "click the launcher", and the launcher-specific
// tests close it first rather than assuming a closed starting state.
async function openAssistant(page: Page) {
  const launcher = page.getByRole("button", { name: "Open Ask Lex legal research assistant" });
  if (await launcher.count() > 0) await launcher.click();
  await expect(page.locator(".askpanel")).toBeVisible();
}

async function closeAssistant(page: Page) {
  const close = page.getByRole("button", { name: "Close assistant" });
  if (await close.count() > 0) await close.click();
  await expect(page.locator(".asklaunch")).toBeVisible();
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
    const response = await page.goto(route);
    expect(response?.status(), route).toBe(200);
    await expectNoHorizontalOverflow(page);
  }

  for (const route of ["/", "/browse", "/coverage", "/built", "/developers"]) {
    await page.goto(route);
    await expectNoSeriousAxeViolation(page);
  }
  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(networkErrors).toEqual([]);
});

test("global navigation exposes one architecture destination and no broken local links", async ({ page, request }) => {
  await page.goto("/");
  const hrefs = await page.locator("header a[href], footer a[href]").evaluateAll((links) =>
    links.map((link) => (link as HTMLAnchorElement).href));
  const local = hrefs
    .map((href) => new URL(href))
    .filter((url) => url.origin === new URL(page.url()).origin);
  const paths = local.map((url) => url.pathname);

  expect(paths).not.toContain("/architecture");
  expect(paths).not.toContain("/architecture/next");
  expect(paths.filter((path) => path === "/built")).toHaveLength(2); // header + footer

  for (const url of new Set(local.map((item) => item.href))) {
    const response = await request.get(url);
    expect(response.status(), url).toBeLessThan(400);
  }
});

test("release evaluation evidence fails closed without inventing a passing result", async ({ page, request }) => {
  const response = await page.goto("/built/release");
  expect(response?.status()).toBe(200);
  await expect(page.getByRole("heading", { name: "Latest signed assistant evaluation" }))
    .toBeVisible();
  await expect(page.getByText("No verified evaluation published.", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "evaluation.json", exact: true }))
    .toHaveAttribute("href", "/built/release/evaluation.json");
  await expect(page.getByRole("link", { name: "attestation.json", exact: true }))
    .toHaveAttribute("href", "/attestation.json");

  const status = await request.get("/built/release/evaluation.json");
  expect(status.status()).toBe(200);
  expect(await status.json()).toEqual({
    schema: "lex-public-assistant-evaluation-status/1",
    status: "unavailable",
  });
  await expectNoHorizontalOverflow(page);
  await expectNoSeriousAxeViolation(page);
});

test("every public page reflows at the frozen desktop, modal, phone and landscape widths", async ({ page }) => {
  test.setTimeout(120_000);
  for (const [width, height] of [
    [1440, 900], [1100, 800], [1099, 800], [768, 900], [320, 568], [568, 320],
  ]) {
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
    await expect(disclosure.getByRole("link", { name: "Verify artifacts" })).toBeVisible();
  } finally {
    await context.close();
  }
});

test("the assistant crosses from complementary dock to a true modal at the frozen boundary", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/");
  await openAssistant(page);
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
  await openAssistant(page);
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

test("each assistant answer discloses its safe typed plan and execution evidence", async ({ page }) => {
  const requestId = "4123456789abcdef0123456789abcdef";
  const operation = {
    operation_id: `${requestId}:op-1`, order: 0, tool: "as_of",
    result_class: "provision", disposition: "display", legal_outcome: "succeeded",
    transport_outcome: "completed", effects: ["provision"],
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
      const reply = {
        reply: "Verified Article 6.",
        thread_token: "A".repeat(43),
        trace: [{
          phase: "operation_plan", request_id: requestId, locale: "en", duration_ms: 12,
          operations: [{
            operation_id: operation.operation_id, order: 0, tool: "as_of",
            result_class: "provision", disposition: "display",
            arguments: { work: "eu-eurlex:32016r0679", date: "2021-01-01", anchors: "art_6" },
            repairs: ["as_of.page dropped"],
          }],
        }, {
          phase: "synthesis", status: "completed", draft_status: "answer",
          claims: [{ kind: "legal_text", evidence_ids: ["as_of:1:1"] }],
          permalinks: ["https://law.soufien.lu/a"],
          judge: { disposition: "pass", issue_count: 0 },
        }],
        operations: [operation],
        model_usage: { input_tokens: 120, output_tokens: 30, total_tokens: 150 },
        model_identity: { resource_host: "example.openai.azure.com", deployment: "planner" },
        timing: { planner_ms: 12, mcp_ms: 7, synthesis_ms: null },
      };
      return Promise.resolve(new Response(
        `event: done\ndata: ${envelope(1, reply)}\n\n`, {
          status: 200,
          headers: { "Content-Type": "text/event-stream", "X-Lex-Request-Id": requestId },
        }));
    };
  }, { requestId, operation });

  await page.goto("/");
  await openAssistant(page);
  await page.getByRole("textbox", { name: "Ask Lex" }).fill("Show GDPR Article 6 in 2021.");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await expect(page.locator(".said")).toContainText("Verified Article 6.");

  const disclosure = page.locator("details.ap-execution");
  const cards = disclosure.locator("details.ap-audit-card");
  await expect(disclosure).toHaveCount(1);
  await expect(cards.first()).toBeHidden();
  await disclosure.getByText("How this answer was produced", { exact: true }).click();
  await expect(cards).toHaveCount(4);
  await expect(cards.first()).toBeVisible();
  await expect(disclosure).toContainText("1 operation, frozen before the first ran.");
  await expect(disclosure).toContainText("as_of");
  await expect(disclosure).toContainText("art_6");
  await expect(disclosure).toContainText("repaired: as_of.page dropped");
  await expect(disclosure).toContainText("succeeded");
  await expect(disclosure).toContainText("1 claim over 1 evidence id");
  await expect(disclosure).toContainText("pass, 0 issues");
  await expect(disclosure).not.toContainText("thread_token");
  await expect(disclosure).not.toContainText("provisions");
  await expectNoHorizontalOverflow(page);
  await expectNoSeriousAxeViolation(page);
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
  await openAssistant(page);
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
  await openAssistant(page);
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
    // This test is about the launcher, which only exists once the assistant is closed.
    await closeAssistant(page);
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
  await closeAssistant(page);
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

test("quarantined meaning search stays selected and reports unavailability without keyword fallback",
  async ({ page }) => {
    await page.route("**/mcp", async (route) => {
      const request = route.request().postDataJSON() as { id: number };
      const payload = [{
        envelope: {
          publisher: "eu-eurlex",
          jurisdiction: "EU",
          status: "retrieval_mode_unavailable",
        },
        requested_retrieval_mode: "hybrid",
        retrieval_unavailable_reason: "benchmark_gate_failed",
        hits: [],
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

    await page.goto("/?space=search&q=personal+data&retrieval=hybrid", {
      waitUntil: "networkidle",
    });

    await expect(page.getByRole("button", { name: /Words \+ meaning/ }))
      .toHaveAttribute("aria-pressed", "true");
    await expect(page.getByText(/signed retrieval benchmark has not authorized it/i)).toBeVisible();
    await expect(page.locator(".res-head .badge")).toHaveText("meaning unavailable");
    expect(new URL(page.url()).searchParams.get("retrieval")).toBe("hybrid");
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
          // The real producer's Envelope() always writes a status; this double omitted it,
          // so it asserted against a shape the server never emits. The closed status
          // classification (round-4 O1) correctly rejected it and CI caught the stale double.
          status: "ok",
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
        // Same class of staleness as the missing `status` above, caught the same way and by
        // the same gate. Every search path in the producer writes `population` unconditionally:
        // the unsupported-filter refusal, the retrieval-mode-unavailable envelope and the
        // executed query all emit it. A search envelope without one is a shape the server never
        // sends, and the client now withholds that publisher's rows rather than rendering a list
        // the reader cannot check against a denominator. This is the coherent triple for `ok`.
        population: {
          basis: "selected_metadata_scope",
          works_in_scope: 1,
          scope_filters_applied: true,
          query_ran: true,
          known_exclusions: [],
        },
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

test("the exact words override is cleared by the next question and never re-armed by returning",
  async ({ page }) => {
    // What this proves that a unit test cannot. `nextExactQuery` and `fuzzyModeFor` are pure and
    // covered on their own, but every one of those assertions stays green if the effect that
    // calls the transition is deleted, because nothing in them observes React. The evidence
    // here is the `fuzzy` argument of each request the running page actually issued, so the
    // assertion fails the moment the component stops applying the rule. Reading the component
    // source instead would fail on a rename and pass on a semantic regression; the requests
    // cannot do either.
    const consoleErrors: string[] = [];
    const pageErrors: string[] = [];
    page.on("console", (message) => {
      if (message.type() === "error") consoleErrors.push(message.text());
    });
    page.on("pageerror", (error) => pageErrors.push(error.message));

    const misspelled = "travial";
    const expansion = "travail";
    const otherQuestion = "capital";
    const fuzzyArguments: unknown[] = [];

    // Where the relaxed response comes from. This suite starts Lex.Web over
    // `browser/empty-indexes`, so no query typed here can come back relaxed: a server with
    // nothing mounted answers the terminal no-corpus refusal, never expansions. The relaxed
    // response is therefore a publisher double, as it already is for the quarantined-mode and
    // publisher-metadata tests above. The double is keyed on the argument it was actually sent
    // rather than fixed, because that is what the producer does: spelling fallback is only ever
    // applied to a request that allowed it. Nothing here chooses that argument. The page does,
    // and the page is what is on trial.
    const response = (fuzzy: unknown) => [{
      envelope: {
        publisher: "lu-legilux",
        jurisdiction: "LU",
        status: "ok",
        timeline_semantics: "official_consolidation_state",
      },
      retrieval_mode: "keyword",
      ...(fuzzy === "auto" ? { query_expansions: [expansion] } : {}),
      population: {
        basis: "selected_metadata_scope",
        works_in_scope: 1,
        scope_filters_applied: true,
        query_ran: true,
        known_exclusions: [],
      },
      hits: [{
        lex_id: "lu-legilux:loi-2020-07-17-a624:2020-07-17",
        title: "Loi du 17 juillet 2020",
        language: "fr",
        valid_from: "2020-07-17",
        valid_to: null,
        match_reasons: ["text"],
      }],
    }];

    await page.route("**/mcp", async (route) => {
      const request = route.request().postDataJSON() as {
        id: number;
        params?: { name?: string; arguments?: Record<string, unknown> };
      };
      // Only the workspace search is on trial. Anything else reaches the real server and
      // answers for itself rather than being impersonated by this double.
      if (request.params?.name !== "search") { await route.continue(); return; }
      const fuzzy = request.params?.arguments?.fuzzy;
      fuzzyArguments.push(fuzzy);
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          jsonrpc: "2.0",
          id: request.id,
          result: { content: [{ type: "text", text: JSON.stringify(response(fuzzy)) }] },
        }),
      });
    });

    const relaxed = page.getByTestId("interpretation-notice");
    const revert = page.getByTestId("relaxation-revert");
    const exactWords = page.getByTestId("exact-words-notice");

    // 1. The relaxation is disclosed, and there is exactly one way back out of it.
    await page.goto(`/?space=search&q=${misspelled}`, { waitUntil: "networkidle" });
    await expect.poll(() => fuzzyArguments.length).toBe(1);
    expect(fuzzyArguments).toEqual(["auto"]);
    await expect(relaxed).toBeVisible();
    await expect(relaxed).toContainText(expansion);
    await expect(revert).toHaveCount(1);

    // 2. The revert issues a NEW search asking for the exact words. A control that only rewrote
    //    the sentence on screen would read as a revert and leave this argument at "auto", which
    //    is why the assertion is on the recorded request and not on the copy.
    await revert.click();
    await expect.poll(() => fuzzyArguments.length).toBe(2);
    expect(fuzzyArguments[1]).toBe("off");

    // 3. The screen now says what the request said, and the relaxed disclosure is gone.
    await expect(exactWords).toBeVisible();
    await expect(relaxed).toHaveCount(0);

    // 4. The regression this test exists for. A different question, then back to the first one.
    //    The override was authorised once, for those words, on that visit. Returning to them
    //    later is not a second authorisation, so the searches issued on the way back must ask
    //    for no narrowing at all.
    await page.getByRole("textbox", { name: "Search for a law, an identifier, or words in the text" })
      .fill(otherQuestion);
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await expect.poll(() => new URL(page.url()).searchParams.get("q")).toBe(otherQuestion);
    await expect.poll(() => fuzzyArguments.length).toBe(3);

    await page.goBack();
    await expect.poll(() => new URL(page.url()).searchParams.get("q")).toBe(misspelled);
    await expect.poll(() => fuzzyArguments.length).toBe(4);

    // The recorded arguments are the whole evidence, so their substance is asserted before
    // their content. `not.toContain` is satisfied by an empty list, and a test that can be
    // satisfied by observing nothing is the defect this suite exists to prevent.
    const afterReturning = fuzzyArguments.slice(2);
    expect(fuzzyArguments).toHaveLength(4);
    expect(afterReturning).toHaveLength(2);
    expect(afterReturning).not.toContain("off");
    expect(fuzzyArguments).toEqual(["auto", "off", "auto", "auto"]);

    // A dormant override would have suppressed the disclosure a second time as well, so the
    // reader would never have been told the query they came back to had been narrowed.
    await expect(relaxed).toBeVisible();
    await expect(relaxed).toContainText(expansion);
    await expect(exactWords).toHaveCount(0);

    expect(consoleErrors).toEqual([]);
    expect(pageErrors).toEqual([]);
  });
