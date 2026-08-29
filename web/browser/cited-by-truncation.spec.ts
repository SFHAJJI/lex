import { expect, test, type Page } from "@playwright/test";

/**
 * D09 stratum B. `cited_by` stamps a response-wide `response_row_set` receipt (McpCore.cs:1903) and
 * sets `citing_articles` to the hits that fitted (McpCore.cs:1889), so the count can never reveal a
 * cut: it equals the row count by construction. When an earlier publisher unit has already spent the
 * shared budget, a later unit with real candidates yields zero hits and an envelope of `no_result`
 * (McpCore.cs:1878-1884) while the same response reports `truncated`.
 *
 * The receipt reached the tool response and stopped at `UiMapper.Cited`, so the client saw an empty
 * list and rendered `No held provision version in this corpus refers to this law.` A mounted probe
 * confirmed that reader outcome before this repair was written.
 *
 * The rule under test is that an absence claim requires a receipt saying nothing was cut. A missing
 * receipt, or one that is not a boolean, is not that receipt. Truthiness would read `null` as false
 * and restore the absence claim, which is the D08 O1 failure in a second place.
 *
 * The `/api/ask/stream` response is a controlled fixture. No law payload, no query text and no
 * golden content is read or written by this file.
 */

const WORK = "lu-legilux:fixture-cited";
const ABSENCE = "No held provision version in this corpus refers to this law.";
const CUT = "returned fewer rows than it found";
const UNKNOWN = "does not record whether it was complete";

const row = () => ({
  work: "lu-legilux:citing-work", title: "Citing law", valid_from: "2020-01-01",
  anchor: "art-1", num: "Art. 1",
});

async function ask(page: Page, requestId: string, rows: unknown[], truncated: unknown,
                   extra: Record<string, unknown> = {}) {
  const citedBy: Record<string, unknown> = {
    cited_work: WORK, citing_articles: rows.length, rows, status: rows.length ? "ok" : "no_result",
  };
  if (truncated !== undefined) citedBy.rows_truncated = truncated;
  Object.assign(citedBy, extra);
  const operation = {
    operation_id: `${requestId}:op-1`, order: 0, tool: "cited_by",
    result_class: null, disposition: "answer", legal_outcome: "answer",
    transport_outcome: "completed", effects: ["cited_by"],
    ui: { cited_by: citedBy },
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
            `event: operation_result
data: ${envelope(1, operation)}

`));
          controller.enqueue(encoder.encode(`event: done
data: ${envelope(2, {
            reply: "Here is what refers to it.", operations: [operation], ui: operation.ui,
          })}

`));
          controller.close();
        },
      });
      return Promise.resolve(new Response(stream, {
        status: 200,
        headers: { "Content-Type": "text/event-stream", "X-Lex-Request-Id": requestId },
      }));
    };
  }, { requestId, operation });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  const launcher = page.getByRole("button", { name: "Open Ask Lex legal research assistant" });
  if (await launcher.count() > 0) await launcher.click();
  await expect(page.locator(".askpanel")).toBeVisible();
  await page.getByRole("textbox", { name: "Ask Lex" }).fill("what refers to it");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await expect(page.locator(".ws")).toBeVisible();
  return page.locator(".ws");
}

test("a cut response with no surviving rows does not claim nothing refers to the law",
  async ({ page }) => {
    const ws = await ask(page, "a023456789abcdef0123456789abcdef", [], true);

    await expect(ws).not.toContainText(ABSENCE);
    await expect(ws).toContainText(CUT);
    // The count is what fitted, so it must not be presented as a fact about the law.
    await expect(ws).not.toContainText("0 articles refer to it");
  });

test("an empty response that reports nothing was cut may still state the absence",
  async ({ page }) => {
    const ws = await ask(page, "b023456789abcdef0123456789abcdef", [], false);

    // The one branch holding positive evidence of completeness keeps the definitive sentence.
    await expect(ws).toContainText(ABSENCE);
    await expect(ws).not.toContainText(CUT);
  });

test("an empty response carrying no receipt claims neither absence nor a cut", async ({ page }) => {
  const ws = await ask(page, "c023456789abcdef0123456789abcdef", [], undefined);

  await expect(ws).not.toContainText(ABSENCE);
  await expect(ws).toContainText(UNKNOWN);
});

/**
 * Values the declaration forbids and the transport can still deliver. Under truthiness each of
 * these would be read as "not truncated" and would restore the absence claim.
 */
const HOSTILE: readonly (readonly [string, string, unknown])[] = [
  ["null", "d023456789abcdef0123456789abcdef", null],
  ["a string", "e023456789abcdef0123456789abcdef", "no"],
  ["a number", "f023456789abcdef0123456789abcdef", 0],
];

for (const [label, requestId, value] of HOSTILE) {
  test(`an empty response whose receipt is ${label} claims no absence`, async ({ page }) => {
    const ws = await ask(page, requestId, [], value);

    await expect(ws).not.toContainText(ABSENCE);
    await expect(ws).toContainText(UNKNOWN);
  });
}

test("a cut response that did return rows says so beside them", async ({ page }) => {
  const ws = await ask(page, "1123456789abcdef0123456789abcdef", [row()], true);

  await expect(ws).toContainText("Citing law");
  await expect(ws).toContainText(CUT);
  await expect(ws).toContainText("1 returned in this response");
});

test("a complete response with rows is presented without qualification", async ({ page }) => {
  const ws = await ask(page, "2123456789abcdef0123456789abcdef", [row()], false);

  await expect(ws).toContainText("Citing law");
  await expect(ws).toContainText("1 article refer");
  await expect(ws).not.toContainText(CUT);
});

/**
 * A returned row proves that at least one article refers to the law. It does not prove that the
 * number beside it is the total, and cited_by sets citing_articles to the hits that fitted. So an
 * exact total may be stated only against a receipt saying nothing was cut.
 */
const UNQUALIFIED = "1 article refer to it";

test("rows with no receipt are framed as returned, not as a total", async ({ page }) => {
  const ws = await ask(page, "3123456789abcdef0123456789abcdef", [row()], undefined);

  await expect(ws).toContainText("Citing law");
  await expect(ws).not.toContainText(UNQUALIFIED);
  await expect(ws).toContainText("1 returned in this response");
  await expect(ws).toContainText(UNKNOWN);
});

for (const [label, requestId, value] of [
  ["null", "4123456789abcdef0123456789abcdef", null],
  ["a string", "5123456789abcdef0123456789abcdef", "false"],
  ["a number", "6123456789abcdef0123456789abcdef", 1],
] as readonly (readonly [string, string, unknown])[]) {
  test(`rows whose receipt is ${label} are framed as returned, not as a total`, async ({ page }) => {
    const ws = await ask(page, requestId, [row()], value);

    await expect(ws).not.toContainText(UNQUALIFIED);
    await expect(ws).toContainText("1 returned in this response");
  });
}

/**
 * D24. Every cited_by response carries three scope disclaimers: what the list is evidence of, and
 * that the producer assessed neither whether the references are currently in effect nor what kind
 * of relationship each one is. All three stopped at the mapper. A list of referring articles with
 * none of them beside it invites exactly the two conclusions the producer declines to support.
 */
const SCOPE = "captured_cross_references_in_held_non_withdrawn_versions";

test("the scope of the evidence and the two unassessed claims are stated", async ({ page }) => {
  const ws = await ask(page, "7123456789abcdef0123456789abcdef", [row()], false, {
    evidence_scope: SCOPE,
    current_legal_effect_assessed: false,
    relationship_type_assessed: false,
  });

  await expect(ws).toContainText("Cross references Lex captured");
  await expect(ws).toContainText("whether each reference is currently in effect");
  await expect(ws).toContainText("what kind of relationship it is");
});

test("a response carrying no disclaimers states none of them", async ({ page }) => {
  const ws = await ask(page, "8123456789abcdef0123456789abcdef", [row()], false);

  await expect(ws).not.toContainText("Not assessed");
  await expect(ws).not.toContainText("Cross references Lex captured");
});

test("an unassessed flag is reported only when it is exactly false", async ({ page }) => {
  // Truthiness would read the string and the number as assessed, and !value would read an absent
  // field as not assessed. Both invent a fact about work the producer never reported on.
  const ws = await ask(page, "9123456789abcdef0123456789abcdef", [row()], false, {
    current_legal_effect_assessed: "false",
    relationship_type_assessed: 0,
  });

  await expect(ws).not.toContainText("Not assessed");
});

test("a scope this build does not recognise is shown verbatim", async ({ page }) => {
  const ws = await ask(page, "a223456789abcdef0123456789abcdef", [row()], false, {
    evidence_scope: "some_future_scope",
  });

  await expect(ws).toContainText("some_future_scope");
});
