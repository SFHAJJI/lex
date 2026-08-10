import { chromium, expect, test } from "@playwright/test";
import lighthouse from "lighthouse";
import { readFileSync } from "node:fs";

const budgets = JSON.parse(readFileSync(new URL("./budgets.json", import.meta.url), "utf8"));

test("representative mobile routes remain inside the Lighthouse performance budget", async ({ baseURL }, testInfo) => {
  test.setTimeout(180_000);
  const port = 9333;
  const browser = await chromium.launch({ args: [`--remote-debugging-port=${port}`] });
  const reports: Record<string, unknown> = {};
  try {
    for (let attempt = 0; attempt < 50; attempt++) {
      try { if ((await fetch(`http://127.0.0.1:${port}/json/version`)).ok) break; }
      catch { /* Chrome is still opening the debugging socket. */ }
      await new Promise((resolve) => setTimeout(resolve, 100));
    }

    for (const route of budgets.lighthouse_mobile.routes as string[]) {
      const result = await lighthouse(`${baseURL}${route}`, {
        port,
        logLevel: "error",
        output: "json",
        onlyCategories: ["performance"],
        formFactor: "mobile",
        screenEmulation: { mobile: true, width: 360, height: 640, deviceScaleFactor: 1, disabled: false },
        throttlingMethod: "simulate",
      });
      if (!result) throw new Error(`Lighthouse returned no report for ${route}`);
      const lhr = result.lhr;
      const audit = (name: string) => lhr.audits[name]?.numericValue ?? 0;
      const lcpElement = lhr.audits["largest-contentful-paint-element"]?.details as
        | { items?: { items?: { node?: { snippet?: string } }[] }[] }
        | undefined;
      const metrics = {
        score: lhr.categories.performance.score,
        fcp_ms: audit("first-contentful-paint"),
        lcp_ms: audit("largest-contentful-paint"),
        cls: lhr.audits["cumulative-layout-shift"].numericValue,
        tbt_ms: lhr.audits["total-blocking-time"].numericValue,
        server_ms: audit("server-response-time"),
        main_thread_ms: audit("mainthread-work-breakdown"),
        bootup_ms: audit("bootup-time"),
        transferred_bytes: audit("total-byte-weight"),
        lcp_element: lcpElement?.items?.[0]?.items?.[0]?.node?.snippet,
        lcp_audits: Object.keys(lhr.audits).filter((name) =>
          name.includes("lcp") || name.includes("render-blocking")),
        lcp_phases: lhr.audits["lcp-phases-insight"]?.details?.items,
        render_blocking: lhr.audits["render-blocking-insight"]?.displayValue,
      };
      reports[route] = metrics;
      await testInfo.attach(`lighthouse-${route === "/" ? "home" : route.slice(1)}.json`, {
        body: result.report as string,
        contentType: "application/json",
      });
    }
  } finally {
    await browser.close();
  }
  await testInfo.attach("lighthouse-metrics.json", {
    body: JSON.stringify(reports, null, 2),
    contentType: "application/json",
  });
  const failures: string[] = [];
  for (const [route, value] of Object.entries(reports)) {
    const metrics = value as { lcp_ms: number; cls: number; tbt_ms: number };
    if (metrics.lcp_ms > budgets.lighthouse_mobile.lcp_ms_max)
      failures.push(`${route} LCP ${metrics.lcp_ms.toFixed(1)}ms`);
    if (metrics.cls > budgets.lighthouse_mobile.cls_max)
      failures.push(`${route} CLS ${metrics.cls.toFixed(4)}`);
    if (metrics.tbt_ms > budgets.lighthouse_mobile.tbt_ms_max)
      failures.push(`${route} TBT ${metrics.tbt_ms.toFixed(1)}ms`);
  }
  expect(failures, JSON.stringify(reports, null, 2)).toEqual([]);
});
