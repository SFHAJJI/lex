import { expect, test } from "@playwright/test";
import { diffWords } from "../src/diff";

const WORK = "lu-legilux:comparison-output-budget-fixture";
const FROM = "2020-01-01";
const TO = "2021-01-01";
const BYTES_PER_ANCHOR = 5_000;

type MeasureCase = {
  id: string;
  anchors: number;
};

const CASES: MeasureCase[] = [
  { id: "hostile-one-row", anchors: 1 },
  { id: "hostile-forty-rows", anchors: 40 },
  { id: "hostile-forty-one-rows", anchors: 41 },
];

const hostileText = (later: boolean) => {
  const parts: string[] = [];
  for (let i = 0; parts.join("").length + 12 <= BYTES_PER_ANCHOR; i++) {
    const suffix = String(i).padStart(4, "0");
    parts.push(`k${suffix} ${later ? "b" : "a"}${suffix} `);
  }
  return parts.join("").padEnd(BYTES_PER_ANCHOR, "z");
};

const rpc = (id: unknown, payload: unknown) => JSON.stringify({
  jsonrpc: "2.0",
  id,
  result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
});

for (const measure of CASES) {
  test(`comparison hostile output budget ${measure.id}`, async ({ page, context }) => {
    test.setTimeout(120_000);
    const anchors = Array.from({ length: measure.anchors }, (_, i) => `art_${i + 1}`);
    const beforeText = hostileText(false);
    const afterText = hostileText(true);
    const piecesPerAnchor = diffWords(beforeText, afterText).length;
    let mcpCalls = 0;
    let selectCalls = 0;

    await page.route("**/mcp", async (route) => {
      mcpCalls++;
      const body = JSON.parse(route.request().postData() ?? "{}");
      const name = body?.params?.name;
      const args = body?.params?.arguments ?? {};
      if (name !== "as_of") {
        await route.fulfill({ status: 200, contentType: "application/json", body: rpc(body.id, []) });
        return;
      }

      const later = String(args.date ?? "") === TO;
      const requested = args.mode === "select"
        ? String(args.anchors ?? "").split(",").filter(Boolean)
        : anchors;
      if (args.mode === "select") selectCalls++;
      const provisions = requested.map((anchor: string) => ({
        anchor,
        num: anchor,
        text_sha256: later ? "f".repeat(64) : "0".repeat(64),
        ...(args.mode === "select" ? { text: later ? afterText : beforeText } : {}),
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
            lex_id: `${WORK}:${args.date}`,
            valid_from: String(args.date ?? ""),
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
    const renderedPieceElements = await page.locator(".diffrow .lawtxt > *").count();
    const renderedDiffSubtreeNodes = await page.locator(".diffrow").evaluateAll((rows) =>
      rows.reduce((total, row) => {
        const walker = document.createTreeWalker(row, NodeFilter.SHOW_ALL);
        let count = 1;
        while (walker.nextNode()) count++;
        return total + count;
      }, 0));
    await cdp.send("HeapProfiler.collectGarbage");
    const after = await cdp.send("Runtime.getHeapUsage") as { usedSize: number };

    console.log(JSON.stringify({
      schema: "lex-comparison-output-budget-measurement/1",
      case: measure.id,
      anchors: measure.anchors,
      source_bytes_per_side: measure.anchors * BYTES_PER_ANCHOR,
      pieces_per_anchor: piecesPerAnchor,
      output_pieces: piecesPerAnchor * measure.anchors,
      rendered_piece_elements: renderedPieceElements,
      rendered_diff_subtree_nodes: renderedDiffSubtreeNodes,
      elapsed_ms: elapsedMs,
      retained_heap_delta_bytes: after.usedSize - before.usedSize,
      mcp_calls: mcpCalls,
      select_calls: selectCalls,
    }));
  });
}
