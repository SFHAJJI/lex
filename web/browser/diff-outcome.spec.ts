import { expect, test, type Page } from "@playwright/test";

/**
 * D08. `DiffView.Changed` is the only typed outcome a whole-work comparison has, and its own
 * producer comment says so: without it a reader is told a comparison happened and left to guess how
 * it came out. It reached the wire, was undeclared in the client contract, and every rendered
 * outcome tag sat inside the anchored branch, so an unanchored comparison stated nothing at all.
 *
 * The fixture carries two operations on purpose. `compoundOperationViews` in `src/api.ts` returns an
 * empty list unless more than one operation has a view, and the panel under test lives in
 * `renderOperation`, which the workspace calls only for that compound case. A single-operation reply
 * takes a different and deliberate path: `assistantWorkspaceState` maps a diff to the law route in
 * compare mode and the deterministic `Compare` component answers instead. That handoff is not what
 * this file tests and must not be changed to make it pass.
 *
 * These journeys drive the mounted workspace and read what the reader sees. A unit test cannot cover
 * this: the suite runs only `src/*.test.ts`, so `App.tsx` is invisible to it and would stay green
 * with the wiring deleted. That blindness is the reason this file exists rather than a unit test.
 *
 * The wording under test is deliberately about versions rather than about the law. `changed` is a
 * record fact, whether the two dates resolved to different publisher versions. Rendering it as
 * "nothing changed" would be a legal claim the field cannot support and Decision 44 forbids.
 *
 * The `/api/ask/stream` response is a controlled fixture. No law payload, no query text and no
 * golden content is read or written by this file.
 */

const WORK = "lu-legilux:fixture-diff";
const FROM = "2020-01-01";
const TO = "2021-01-01";

/**
 * The diff operation, with `changed` present or absent as the case under test requires.
 *
 * `changed` is deliberately `unknown` rather than `boolean | undefined`. The values that matter
 * most here are the ones the client contract says cannot occur: the stream parser casts parsed JSON
 * without validating it, so the declaration constrains the producer and not the wire. A fixture
 * typed to the declaration could not express the case that produced a false claim.
 */
function diffOperation(requestId: string, changed: unknown, anchor?: string,
                       limitations?: unknown, limitationsMalformed?: boolean) {
  const diff: Record<string, unknown> = {
    subject: { work: WORK, title: "Fixture law", ...(anchor ? { anchor } : {}) },
    from_date: FROM, to_date: TO,
    provision_level_comparable: false,
  };
  if (changed !== undefined) diff.changed = changed;
  if (limitations !== undefined) diff.comparison_limitations = limitations;
  if (limitationsMalformed === true) diff.comparison_limitations_malformed = true;
  return {
    operation_id: `${requestId}:op-1`, order: 0, tool: "diff",
    result_class: null, disposition: "answer", legal_outcome: "answer",
    transport_outcome: "completed", effects: ["diff"],
    ui: { diff },
  };
}

/**
 * A second view-carrying operation, which is what makes the reply compound. Provenance has no
 * workspace destination, so it cannot overwrite the comparison route under test.
 */
function companionOperation(requestId: string) {
  return {
    operation_id: `${requestId}:op-2`, order: 1, tool: "provenance",
    result_class: null, disposition: "answer", legal_outcome: "answer",
    transport_outcome: "completed", effects: ["verification"],
    ui: { verification: { lex_id: `${WORK}@${FROM}` } },
  };
}

async function runAssistant(page: Page, requestId: string,
                            changed: unknown, anchor?: string, limitations?: unknown,
                            limitationsMalformed?: boolean) {
  const operations = [diffOperation(requestId, changed, anchor, limitations, limitationsMalformed),
                      companionOperation(requestId)];
  await runOperations(page, requestId, operations);
  const panel = page.getByRole("region", { name: "Comparison result" });
  await expect(panel).toBeVisible();
  return panel;
}

async function runOperations(page: Page, requestId: string, operations: unknown[]) {
  await page.addInitScript(({ requestId, operations }) => {
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
          for (const [index, operation] of operations.entries())
            controller.enqueue(encoder.encode(
              `event: operation_result
data: ${envelope(index + 1, operation)}

`));
          controller.enqueue(encoder.encode(`event: done
data: ${envelope(operations.length + 1, {
            reply: "Comparison complete.",
            operations,
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
  }, { requestId, operations });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  const launcher = page.getByRole("button", { name: "Open Ask Lex legal research assistant" });
  if (await launcher.count() > 0) await launcher.click();
  await expect(page.locator(".askpanel")).toBeVisible();
  await page.getByRole("textbox", { name: "Ask Lex" }).fill("compare these dates");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
}

async function runRefusal(page: Page, requestId: string, status: string,
                          limitations: unknown, limitationsMalformed = false) {
  const gap: Record<string, unknown> = {
    status,
    work: WORK,
    date: FROM,
    explanation: status === "profiles_differ"
      ? "The two versions use different extraction profiles."
      : "Certified wording is not available for every requested coordinate.",
    available: [],
    comparison_from_date: FROM,
    comparison_to_date: TO,
    comparison_limitations: limitations,
  };
  if (limitationsMalformed) gap.comparison_limitations_malformed = true;
  const diff = {
    subject: { work: WORK, title: "Fixture law" },
    from_date: FROM,
    to_date: TO,
    status,
    comparison_limitations: limitations,
    comparison_limitations_malformed: limitationsMalformed,
  };
  const keepsDiff = status === "profiles_differ";
  const operation = {
    operation_id: `${requestId}:op-1`, order: 0, tool: "diff",
    result_class: null, disposition: "refuse",
    legal_outcome: keepsDiff ? "not_comparable" : "not_available",
    transport_outcome: "completed", effects: keepsDiff ? ["diff", "gap"] : ["gap"],
    ui: keepsDiff ? { diff, gap } : { gap },
  };
  await runOperations(page, requestId, [operation, companionOperation(requestId)]);
  const gapPanel = page.locator(".operation-result .gap").first();
  await expect(gapPanel).toBeVisible();
  return gapPanel;
}

test("a whole-work comparison that moved states which versions applied", async ({ page }) => {
  const panel = await runAssistant(page, "1023456789abcdef0123456789abcdef", true);

  // The outcome the reader could previously not see at all.
  await expect(panel).toContainText("different versions on these dates");
  // It is a record claim. The panel must not tell a reader the law changed.
  await expect(panel).not.toContainText("the law changed");
});

test("a whole-work comparison that did not move says the same version applied", async ({ page }) => {
  const panel = await runAssistant(page, "2023456789abcdef0123456789abcdef", false);

  await expect(panel).toContainText("the same version applied on both dates");
  // The inverse claim is the one that would be a legal statement, so it must not appear.
  await expect(panel).not.toContainText("nothing changed");
});

test("a comparison with no reported outcome states none rather than guessing", async ({ page }) => {
  const panel = await runAssistant(page, "3023456789abcdef0123456789abcdef", undefined);

  // Absent evidence and a negative outcome are different facts. When the producer reports no
  // outcome the panel says nothing, rather than defaulting to the reassuring branch.
  await expect(panel).not.toContainText("different versions on these dates");
  await expect(panel).not.toContainText("the same version applied on both dates");
  await expect(panel.getByRole("button", { name: "Open comparison" })).toBeVisible();
});

/**
 * Values the declaration forbids and the transport can still deliver. Each one is a value this
 * panel cannot interpret, and the rule is that an uninterpretable outcome is reported as no
 * outcome. Truthiness would send `null` and `0` to the reassuring branch and `"no"` to the other,
 * so each of these once produced a claim the producer never made.
 */
const HOSTILE: readonly (readonly [string, string, unknown])[] = [
  ["null", "5023456789abcdef0123456789abcdef", null],
  ["a string", "6023456789abcdef0123456789abcdef", "no"],
  ["a number", "7023456789abcdef0123456789abcdef", 0],
];

for (const [label, requestId, value] of HOSTILE) {
  test(`a comparison whose outcome is ${label} makes no claim`, async ({ page }) => {
    const panel = await runAssistant(page, requestId, value);

    await expect(panel).not.toContainText("the same version applied on both dates");
    await expect(panel).not.toContainText("different versions on these dates");
    // The panel itself must still be there. Refusing to interpret one field is not a reason to
    // withhold the comparison the reader asked for.
    await expect(panel.getByRole("button", { name: "Open comparison" })).toBeVisible();
  });
}

test("an anchored comparison keeps its provision-level tags and gains no whole-work outcome",
  async ({ page }) => {
    const panel = await runAssistant(page, "4023456789abcdef0123456789abcdef", true, "art_1");

    // The anchored branch is unchanged by this repair. Its outcome is provision-level and the
    // whole-work sentence must not appear beside it.
    await expect(panel).toContainText("art_1");
    await expect(panel).not.toContainText("different versions on these dates");
  });

/**
 * D18. The producer classifies why a comparison is limited, in `comparison_limitations`, and writes
 * the same facts into the prose note. Only the note reached a reader. Prose cannot be branched on,
 * so no surface could refuse a comparison it had been told was uncertifiable; it could only print a
 * paragraph and hope the paragraph was finished.
 */
test("typed comparison limitations are stated, not left to the prose note", async ({ page }) => {
  const panel = await runAssistant(page, "8023456789abcdef0123456789abcdef", true, undefined,
    ["profiles_differ", "typed_text_gap"]);

  await expect(panel).toContainText("different extraction profiles");
  await expect(panel).toContainText("wording comparison not certified");
});

test("a limitation this panel cannot interpret is still shown", async ({ page }) => {
  const panel = await runAssistant(page, "9023456789abcdef0123456789abcdef", true, undefined,
    ["some_future_reason"]);

  // Refusing to interpret a limitation is not a reason to hide that one exists.
  await expect(panel).toContainText("some_future_reason");
});

test("a comparison with no limitations states none", async ({ page }) => {
  const panel = await runAssistant(page, "a123456789abcdef0123456789abcdef", true);

  await expect(panel).not.toContainText("different extraction profiles");
  await expect(panel).not.toContainText("not certified");
  await expect(panel).not.toContainText("limitation data was malformed");
});

test("valid limitations survive malformed siblings and the malformed field is explicit",
  async ({ page }) => {
    const panel = await runAssistant(page, "b123456789abcdef0123456789abcdef", true, undefined,
      ["profiles_differ"], true);

    await expect(panel).toContainText("different extraction profiles");
    await expect(panel).toContainText("limitation data was malformed");
  });

test("a present non-array limitation field is reported as malformed", async ({ page }) => {
  const panel = await runAssistant(page, "c123456789abcdef0123456789abcdef", true, undefined,
    undefined, true);

  await expect(panel).toContainText("limitation data was malformed");
});

test("a profiles-differ refusal renders its typed comparison limitation", async ({ page }) => {
  const gap = await runRefusal(page, "d123456789abcdef0123456789abcdef",
    "profiles_differ", ["profiles_differ"]);

  await expect(gap).toContainText("different extraction profiles");
  await expect(gap).toContainText("provisions cannot be paired");
  await expect.poll(() => new URL(page.url()).searchParams.get("mode")).toBe("compare");
  await expect.poll(() => new URL(page.url()).searchParams.get("work")).toBe(WORK);
  await expect.poll(() => new URL(page.url()).searchParams.get("to")).toBe(TO);
});

test("a text-unavailable refusal renders its typed text-gap limitation", async ({ page }) => {
  const gap = await runRefusal(page, "e123456789abcdef0123456789abcdef",
    "text_not_available", ["typed_text_gap"]);

  await expect(gap).toContainText("typed text gap");
  await expect(gap).toContainText("wording comparison not certified");
  await expect.poll(() => new URL(page.url()).searchParams.get("mode")).toBe("compare");
  await expect.poll(() => new URL(page.url()).searchParams.get("work")).toBe(WORK);
  await expect.poll(() => new URL(page.url()).searchParams.get("to")).toBe(TO);
});

test("a refusal reports malformed limitation data without hiding valid facts",
  async ({ page }) => {
    const gap = await runRefusal(page, "f123456789abcdef0123456789abcdef",
      "text_not_available", ["typed_text_gap"], true);

    await expect(gap).toContainText("typed text gap");
    await expect(gap).toContainText("limitation data was malformed");
  });
