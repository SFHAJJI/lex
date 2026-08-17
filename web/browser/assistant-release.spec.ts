import { expect, test } from "@playwright/test";
import { renameSync, writeFileSync } from "node:fs";

const output = process.env.LEX_BROWSER_EVIDENCE_OUT;
const required = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required for release browser evidence.`);
  return value;
};

test("the exact candidate presents accepted operations within the browser budget", async ({
  page, browser,
}) => {
  test.skip(!output, "Release browser evidence runs only in the authenticated publication gate.");
  const baseUrl = required("LEX_BROWSER_BASE_URL").replace(/\/$/, "");
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(baseUrl);
  // The assistant opens on a first arrival, so the launcher exists only after a reader closes it.
  // Opening is therefore "make sure it is open" rather than "click the launcher", which is what this
  // measurement needs either way: it times presentation, not the act of opening.
  const launcher = page.getByRole("button", { name: "Open Ask Lex legal research assistant" });
  if (await launcher.count() > 0) await launcher.click();
  await expect(page.locator(".askpanel")).toBeVisible();
  const question = page.getByRole("textbox", { name: "Ask Lex" });
  const ask = page.getByRole("button", { name: "Ask", exact: true });
  const samples: number[] = [];
  await page.evaluate(() => {
    const host = window as typeof window & {
      __lexPresentationSamples?: { duration_ms: number; operation_id: string }[];
    };
    host.__lexPresentationSamples = [];
    window.addEventListener("lex:operation-result-presented", (event) => {
      host.__lexPresentationSamples!.push(
        (event as CustomEvent<{ duration_ms: number; operation_id: string }>).detail);
    });
  });

  for (let repetition = 0; repetition < 5; repetition++) {
    await question.fill("Show coverage.");
    await ask.click();
    await page.waitForFunction((expected) => {
      const host = window as typeof window & {
        __lexPresentationSamples?: { duration_ms: number; operation_id: string }[];
      };
      return host.__lexPresentationSamples?.length === expected;
    }, repetition + 1, { timeout: 25_000 });
    samples.push(await page.evaluate(() => {
      const host = window as typeof window & {
        __lexPresentationSamples?: { duration_ms: number; operation_id: string }[];
      };
      return host.__lexPresentationSamples!.at(-1)!.duration_ms;
    }));
    await expect(ask).toBeEnabled({ timeout: 90_000 });
    await expect(page.locator("[data-lex-operation-result-id]")).toHaveCount(1);
  }

  const ordered = [...samples].sort((left, right) => left - right);
  const percentile = (quantile: number) => ordered[Math.max(0,
    Math.min(ordered.length - 1, Math.ceil(quantile * ordered.length) - 1))];
  const latency = {
    p50_milliseconds: percentile(0.50),
    p95_milliseconds: percentile(0.95),
    p99_milliseconds: percentile(0.99),
  };
  const viewport = page.viewportSize();
  const evidence = {
    schema: "lex-assistant-browser-evidence/1",
    run_at: new Date().toISOString(),
    base_url: baseUrl,
    revision_name: required("LEX_CANDIDATE_REVISION"),
    code_commit: required("LEX_CODE_COMMIT"),
    artifact_manifest_set: required("LEX_ARTIFACT_MANIFEST_SET"),
    candidate_evidence_sha256: required("LEX_CANDIDATE_EVIDENCE_SHA256"),
    browser_name: "chromium",
    browser_version: browser.version(),
    viewport_width: viewport?.width,
    viewport_height: viewport?.height,
    metric: "operation_result_received_to_presented_ms",
    samples_milliseconds: samples,
    latency,
    maximum_p95_milliseconds: 500,
    passed: latency.p95_milliseconds <= 500,
  };
  expect(evidence.passed).toBe(true);
  const temporary = `${output}.${process.pid}.tmp`;
  writeFileSync(temporary, `${JSON.stringify(evidence, null, 2)}\n`, { encoding: "utf8" });
  renameSync(temporary, output!);
});
