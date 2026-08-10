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
