import { expect, test } from "@playwright/test";

const WORK = "lu-legilux:comparison-budget-fixture";
const FROM = "2020-01-01";
const TO = "2021-01-01";

type MeasureCase = {
  id: string;
  anchors: number;
  bytesPerAnchor: number;
  tokenized?: boolean;
};

const CASES: MeasureCase[] = [
  { id: "one-batch-250k", anchors: 50, bytesPerAnchor: 5_000 },
  { id: "two-batches-500k", anchors: 100, bytesPerAnchor: 5_000 },
  { id: "four-batches-1m", anchors: 200, bytesPerAnchor: 5_000 },
  { id: "four-batches-1m-tokenized", anchors: 200, bytesPerAnchor: 5_000, tokenized: true },
  { id: "eight-batches-2m", anchors: 400, bytesPerAnchor: 5_000 },
];

const rpc = (id: unknown, payload: unknown) => JSON.stringify({
  jsonrpc: "2.0",
  id,
  result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
});

for (const measure of CASES) {
  test(`comparison delivery browser budget ${measure.id}`, async ({ page, context }) => {
    test.setTimeout(120_000);
    const anchors = Array.from({ length: measure.anchors }, (_, i) => `art_${i + 1}`);
    const baseText = measure.tokenized
      ? "alpha ".repeat(Math.ceil(measure.bytesPerAnchor / 6)).slice(0, measure.bytesPerAnchor)
      : "a".repeat(measure.bytesPerAnchor);
    const laterText = measure.tokenized
      ? `${baseText.slice(0, -5)}bravo`
      : "b".repeat(measure.bytesPerAnchor);
    let mcpCalls = 0;
    let selectCalls = 0;

    await page.route("**/mcp", async (route) => {
      mcpCalls++;
      const request = route.request();
      const body = JSON.parse(request.postData() ?? "{}");
      const name = body?.params?.name;
      const args = body?.params?.arguments ?? {};
      if (name !== "as_of") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: rpc(body.id, []),
        });
        return;
      }

      const date = String(args.date ?? "");
      const later = date === TO;
      const requested = args.mode === "select"
        ? String(args.anchors ?? "").split(",").filter(Boolean)
        : anchors;
      if (args.mode === "select") selectCalls++;
      const provisions = requested.map((anchor: string) => ({
        anchor,
        num: anchor,
        text_sha256: later ? "f".repeat(64) : "0".repeat(64),
        ...(args.mode === "select"
          ? { text: later ? laterText : baseText }
          : {}),
      }));
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: rpc(body.id, [{
          status: "ok",
          envelope: { status: "ok" },
          document: {
            extraction_profile: "akn-lu/2",
            source_uri: "https://legilux.public.lu/eli/etat/leg/fixture",
            lex_id: `${WORK}:${date}`,
            valid_from: date,
            record_sha256: "1".repeat(64),
          },
          provisions,
        }]),
      });
    });

    const cdp = await context.newCDPSession(page);
    await cdp.send("Runtime.enable");
    await cdp.send("HeapProfiler.enable");
    await cdp.send("HeapProfiler.collectGarbage");
    const before = await cdp.send("Runtime.getHeapUsage") as { usedSize: number };
    const started = Date.now();
    await page.goto(`/?space=law&work=${encodeURIComponent(WORK)}&date=${FROM}&to=${TO}&mode=compare`, {
      waitUntil: "domcontentloaded",
    });
    await expect(page.locator(".diffrow")).toHaveCount(measure.anchors, { timeout: 90_000 });
    const elapsedMs = Date.now() - started;
    await cdp.send("HeapProfiler.collectGarbage");
    const after = await cdp.send("Runtime.getHeapUsage") as { usedSize: number };

    console.log(JSON.stringify({
      schema: "lex-comparison-browser-budget-measurement/1",
      case: measure.id,
      anchors: measure.anchors,
      source_bytes_per_side: measure.anchors * measure.bytesPerAnchor,
      elapsed_ms: elapsedMs,
      retained_heap_delta_bytes: after.usedSize - before.usedSize,
      mcp_calls: mcpCalls,
      select_calls: selectCalls,
      rendered_rows: await page.locator(".diffrow").count(),
    }));
  });
}
