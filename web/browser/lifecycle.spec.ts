import { expect, test, type Page, type Route } from "@playwright/test";

/**
 * The UTC day lifecycle: what happens to a workspace that is open when the calendar day changes.
 *
 * "Today" is a fact about the world that moves without anyone touching the page, and every
 * default-dated request in this application is asked against it. `useUtcDay` in `App.tsx` holds
 * it as state and refreshes it from two triggers, a timer at the next UTC boundary and a
 * visibility return, because either alone leaves a gap: background timers are throttled and may
 * fire late or not at all.
 *
 * These journeys live in their own file rather than in `product.spec.ts` because they need a
 * fixed clock installed before navigation, which is a per-test precondition rather than a
 * property of the page under test.
 *
 * What makes them tests rather than decoration, in one sentence each:
 *
 *   - the evidence is the recorded `/mcp` request arguments, not the copy, because the copy is
 *     downstream of the request and can be right for the wrong reason;
 *   - every emptiness claim is preceded by an assertion that the same locator was populated,
 *     because these fixtures fail closed in several places and every one of those failures ends
 *     in the same blank screen an emptiness assertion is looking for;
 *   - emptiness is read with `expect(await locator.count()).toBe(0)`, never
 *     `await expect(locator).toHaveCount(0)`, because the retrying form passes the moment a field
 *     empties and would therefore accept an arbitrarily long stale window, which is the opposite
 *     polarity to the property under test.
 *
 * Every journey below was watched failing before it was trusted. The production bundle was
 * rewritten in flight, one defect at a time, and the run recorded. Which journeys each defect
 * takes down, and the assertion that catches it:
 *
 *   - the day never advancing at all: ALL EIGHT, each at its own request or content assertion.
 *     No journey here passes on a page whose clock is dead.
 *   - `clearTimeout` deleted from `refresh`, which is the accumulation this lane repairs:
 *     journey 3 at the pending census, reading 6 rather than 1, and journey 2 reading 2.
 *   - the `visibilitychange` listener never bound: journey 2 at the request that the return
 *     should have produced, and journey 3 at the schedule count that proves the returns were
 *     reaching the hook at all.
 *   - the law layout effect's clears deleted: journey 5 only, at the gap count, reading 1.
 *   - the search response generation guard deleted: journey 6 only, at the row count, which
 *     reads 2 because the previous day's answer wrote after the new day had rendered.
 *   - `today` added to the search request's dependency lists, so a rollover refetches a pinned
 *     reading: journey 7 only, at the request count.
 *
 * On the clock API, established against the installed Playwright 1.62.1 rather than assumed:
 *
 *   - `install({ time })` replaces `Date`, `setTimeout`, `clearTimeout` and friends, and the
 *     installed clock then TICKS WITH REAL TIME. A short timer scheduled by the page still fires
 *     after its real delay. Only the multi-hour timer this hook schedules needs help.
 *   - `fastForward(ms)` jumps the clock and fires every due timer AT MOST ONCE. Three timers due
 *     at the same instant produce three callbacks, which is exactly the discrimination journey 3
 *     needs: a leaked handle is a callback that runs.
 *   - `setSystemTime(t)` moves what `Date` reports and fires NOTHING. A timer left pending across
 *     it stays pending, verified across a real dwell. That is what lets journey 2 move the world
 *     forward while the tab is away without letting the timer do the work the visibility
 *     listener is supposed to do.
 *
 * On tab visibility, stated plainly because it is the weakest joint in this file: real tab
 * visibility IS NOT DRIVABLE from this harness. `page.bringToFront()` on a sibling page leaves
 * both pages reporting `visible` in headless AND headed Chromium, the CDP method
 * `Emulation.setPageVisibilityOverride` does not exist, and `Page.setWebLifecycleState` with
 * `frozen` does not change `document.visibilityState`. So the init script below redefines
 * `Document.prototype.hidden` and `visibilityState` over a variable it controls and dispatches
 * a `visibilitychange` event on `document`. What that weakens: these journeys prove the
 * listener is bound to the right target, that it reads `document.hidden` before acting, and that
 * it does not accumulate handles. They do NOT prove the browser would deliver the event in the
 * situation the hook exists for, and they do not exercise real background timer throttling,
 * which is the very condition that motivated the visibility trigger.
 */

/** The day the workspace opens on, and the two that follow it. */
const DAY_ONE = "2026-03-14";
const DAY_TWO = "2026-03-15";
/** Ninety minutes before the first boundary, so the hook's first timer is comfortably pending. */
const START = "2026-03-14T22:30:00Z";
/** A date a reader pinned deliberately. Far from the boundary, so a rollover cannot reach it. */
const PINNED = "2026-02-01";
/** Nothing else in this application schedules an hour ahead, so this isolates the day timer. */
const HOUR = 3_600_000;

const WORK_ONE = "lu-legilux:loi-2020-07-17-a624";
const WORK_TWO = "lu-legilux:loi-2019-03-01-b100";

/** The workspace's own sentence for a law that holds no text on the date asked for. */
const GAP_SENTENCE = "No text is held for this law on that date.";

interface McpCall { name: string; args: Record<string, unknown> }

interface TimerCensus { scheduled: number[]; live: () => number[] }

/**
 * A fixed clock, a census of the timers scheduled under it, and a drivable visibility flag.
 *
 * Order matters and is load-bearing. `page.clock.install()` first, `addInitScript` second, so the
 * `setTimeout` captured below is the FAKE one the page will actually call. Reversed, the clock
 * would replace this wrapper and the census would record nothing while still looking healthy.
 * The assertion that the census contains an hours-long delay is what proves the layering held.
 */
async function installFixedClock(page: Page): Promise<void> {
  await page.clock.install({ time: new Date(START) });
  await page.addInitScript(() => {
    const scope = window as unknown as Record<string, unknown>;

    const live = new Map<number, number>();
    const scheduled: number[] = [];
    // Loosely typed on purpose: `@types/node` is in scope for this file, so the DOM and Node
    // declarations of these two names disagree about their return type.
    const rawSet = window.setTimeout as unknown as
      (handler: unknown, ms?: number, ...rest: unknown[]) => number;
    const rawClear = window.clearTimeout as unknown as (id?: number) => void;
    const patched = (handler: TimerHandler, ms?: number, ...rest: unknown[]): number => {
      const delay = typeof ms === "number" ? ms : 0;
      let id = 0;
      const call = handler as (...args: unknown[]) => unknown;
      const wrapped: TimerHandler = typeof handler === "function"
        ? (...args: unknown[]) => { live.delete(id); return call(...args); }
        : handler;
      id = rawSet.call(window, wrapped, delay, ...rest);
      live.set(id, delay);
      scheduled.push(delay);
      return id;
    };
    const census: TimerCensus = { scheduled, live: () => Array.from(live.values()) };
    scope.__lexTimers = census;
    window.setTimeout = patched as unknown as typeof window.setTimeout;
    window.clearTimeout = ((id?: number) => {
      if (id !== undefined) live.delete(id);
      rawClear.call(window, id);
    }) as unknown as typeof window.clearTimeout;

    // How many workspace responses have actually settled inside the page. The route handler
    // knows only that it wrote a body; this knows the browser handed one back to the fetch
    // whose continuation is the guarded write. That difference is what turns "we released the
    // loser" from an inference into an observation.
    const rawFetch = window.fetch;
    let settled = 0;
    window.fetch = ((input: unknown, init?: unknown) => rawFetch(
      input as RequestInfo, init as RequestInit).then((response) => {
        const url = typeof input === "string"
          ? input : String((input as { url?: string })?.url ?? input);
        if (url.includes("/mcp")) settled += 1;
        return response;
      })) as typeof window.fetch;
    scope.__lexSettledMcp = () => settled;

    // Real visibility is not drivable here. See the file header for what that weakens.
    let hidden = false;
    Object.defineProperty(Document.prototype, "hidden", {
      configurable: true, get: () => hidden,
    });
    Object.defineProperty(Document.prototype, "visibilityState", {
      configurable: true, get: () => (hidden ? "hidden" : "visible"),
    });
    scope.__lexVisibility = (next: boolean) => {
      hidden = next;
      // On `document`, which is where the platform dispatches it and where the hook listens.
      document.dispatchEvent(new Event("visibilitychange", { bubbles: true }));
    };
  });
}

/** Every delay this page has ever asked `setTimeout` for, oldest first. */
const scheduledDelays = (page: Page): Promise<number[]> => page.evaluate(
  () => (window as unknown as { __lexTimers: TimerCensus }).__lexTimers.scheduled);

/** The delays of the timers that are still pending: neither fired nor cleared. */
const pendingDelays = (page: Page): Promise<number[]> => page.evaluate(
  () => (window as unknown as { __lexTimers: TimerCensus }).__lexTimers.live());

/** How many of those belong to the day hook, which is the only thing scheduling hours ahead. */
const dayTimers = (delays: number[]) => delays.filter((delay) => delay >= HOUR).length;

const scheduledDayTimers = async (page: Page) => dayTimers(await scheduledDelays(page));
const pendingDayTimers = async (page: Page) => dayTimers(await pendingDelays(page));

/** Drive the visibility flag and dispatch the event, in that order, as the platform would. */
const setTabHidden = (page: Page, hidden: boolean): Promise<void> => page.evaluate(
  (next) => (window as unknown as { __lexVisibility: (v: boolean) => void })
    .__lexVisibility(next), hidden);

/** Workspace responses the page's own fetch layer has handed back so far. */
const settledMcp = (page: Page): Promise<number> => page.evaluate(
  () => (window as unknown as { __lexSettledMcp: () => number }).__lexSettledMcp());

const utcDayInPage = (page: Page): Promise<string> => page.evaluate(
  () => new Date().toISOString().slice(0, 10));

const msToNextBoundary = (page: Page): Promise<number> => page.evaluate(() => {
  const now = new Date();
  return Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1) - now.getTime();
});

/**
 * Cross the next UTC midnight the way a page left open crosses it: the timer actually fires.
 * A minute of overshoot absorbs the real time this test has spent since the clock was read,
 * because the installed clock keeps ticking.
 */
async function fastForwardPastMidnight(page: Page): Promise<void> {
  await page.clock.fastForward(await msToNextBoundary(page) + 60_000);
}

/**
 * Cross the boundary WITHOUT firing anything, which is the situation the visibility trigger
 * exists for. `setSystemTime` moves what `Date` reports and leaves pending timers pending.
 */
async function moveClockPastMidnight(page: Page): Promise<void> {
  await page.clock.setSystemTime(new Date(Date.parse(`${DAY_TWO}T00:05:00Z`)));
}

const mcpBody = (id: number, payload: unknown) => JSON.stringify({
  jsonrpc: "2.0", id, result: { content: [{ type: "text", text: JSON.stringify(payload) }] },
});

/** Everything the page holds, including the collapsed identity list inside the strip. */
const pageText = (page: Page) => page.evaluate(() => document.body.textContent ?? "");

/**
 * A complete, coherent search answer.
 *
 * Every field here is load-bearing and fails closed: `retrieval_mode` must be keyword or hybrid,
 * every hit needs a non-empty `lex_id`, `jurisdiction` must be a real region code or the group
 * label throws and takes the render down, the population must agree with the status, and
 * `built_at` must match the exact `yyyy-MM-ddTHH:mm:ssZ` shape or the strip renders unavailable.
 * The second publisher refuses one filter, with a population coherent with THAT status, so the
 * baseline carries a limitation as well as rows.
 *
 * `mark` appears in the row title and in all four index identity strings, so a stale answer that
 * wins is visible by name rather than merely by count.
 */
function searchAnswer(mark: string, works: number, hits = 1): Record<string, unknown>[] {
  return [
    {
      envelope: {
        publisher: "lu-legilux",
        jurisdiction: "LU",
        status: "ok",
        timeline_semantics: "official_consolidation_state",
        freshness: {
          built_at: "2026-08-15T09:22:08Z",
          stamp_signature_valid: true,
          corpus_commit: `corpus-${mark}`,
        },
        artifact: {
          code_commit: `code-${mark}`,
          manifest_set_id: `manifest-${mark}`,
          content_digest: `digest-${mark}`,
        },
      },
      retrieval_mode: "keyword",
      population: {
        basis: "selected_metadata_scope", works_in_scope: works,
        scope_filters_applied: true, query_ran: true, known_exclusions: [],
      },
      hits: Array.from({ length: hits }, (_unused, index) => ({
        lex_id: `lu-legilux:loi-${mark}-${index}:2020-07-17`,
        title: `Loi marked ${mark} number ${index}`,
        language: "fr", valid_from: "2020-07-17", valid_to: null, match_reasons: ["text"],
      })),
    },
    {
      envelope: { publisher: "eu-eurlex", jurisdiction: "EU",
                  status: "filter_not_supported_by_index" },
      unsupported_filters: ["domain"],
      population: {
        basis: "mounted_scope_before_unsupported_filters", works_in_scope: 0,
        scope_filters_applied: false, query_ran: false, known_exclusions: [],
      },
    },
  ];
}

/**
 * One law, as `as_of` answers it. With `provisions` at zero the read path finds no text and the
 * workspace renders its gap, which is the state journey 5 needs a populated baseline of.
 * `valid_from` is withheld in that case on purpose: a validity interval is only claimed when a
 * version actually resolved.
 */
function lawAnswer(
  mark: string, provisions: number, cite?: string, validFrom = "2020-07-17",
): Record<string, unknown>[] {
  const document = provisions === 0
    ? { title: `Loi ${mark}`, language: "fr" }
    : {
        title: `Loi ${mark}`, language: "fr", valid_from: validFrom,
        extraction_profile: "akn-lu/1",
        source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
      };
  return [{
    envelope: {
      publisher: "lu-legilux", jurisdiction: "LU",
      status: provisions === 0 ? "no_version_for_date" : "ok",
      timeline_semantics: "official_consolidation_state",
    },
    document,
    provisions: Array.from({ length: provisions }, (_unused, index) => ({
      anchor: `art-${index + 1}`,
      num: `Art. ${index + 1}`,
      heading: `Heading ${mark}`,
      text: `Article text marked ${mark}.`,
      ...(index === 0 && cite
        ? { citations: [{ work: cite, href: "https://legilux.public.lu/eli/x",
                          text: `follow ${mark}` }] }
        : {}),
    })),
  }];
}

/** One version, so the rail and the held-text count are coherent rather than absent. */
const timelineAnswer = (): Record<string, unknown>[] => [{
  envelope: { publisher: "lu-legilux", jurisdiction: "LU", status: "ok",
              timeline_semantics: "official_consolidation_state" },
  versions: [{
    valid_from: "2020-07-17", language: "fr", text_available: true, document_type: "LOI",
    source_uri: "https://legilux.public.lu/eli/etat/leg/loi/2020/07/17/a624/jo",
  }],
}];

/**
 * Record every workspace request and answer it from `answer`.
 *
 * The call is recorded BEFORE `answer` is awaited, so a journey can poll for a request that is
 * being deliberately held open. `timeline` is answered here too, and everything else is passed
 * to the real server, because a gate on an unrelated request would hang the page rather than
 * test it.
 */
async function routeMcp(
  page: Page, calls: McpCall[],
  answer: (call: McpCall, index: number) => Promise<unknown> | unknown,
): Promise<void> {
  await page.route("**/mcp", async (route: Route) => {
    const request = route.request().postDataJSON() as {
      id: number; params?: { name?: string; arguments?: Record<string, unknown> };
    };
    const name = request.params?.name ?? "";
    const args = request.params?.arguments ?? {};
    if (name === "timeline") {
      await route.fulfill({
        status: 200, contentType: "application/json", body: mcpBody(request.id, timelineAnswer()),
      });
      return;
    }
    if (name !== "search" && name !== "as_of") { await route.continue(); return; }
    const call: McpCall = { name, args };
    const index = calls.push(call);
    const payload = await answer(call, index);
    await route.fulfill({
      status: 200, contentType: "application/json", body: mcpBody(request.id, payload),
    });
  });
}

/** The workspace search, told apart from the law picker's typeahead by its fixed limit. */
const searchDates = (calls: McpCall[]) => calls
  .filter((call) => call.name === "search" && call.args.limit === 40)
  .map((call) => call.args.as_of);

const lawCalls = (calls: McpCall[]) => calls.filter((call) => call.name === "as_of");

/** The law calls for one date in one mode, which is what tells the outline path from the read. */
const lawCallsFor = (calls: McpCall[], date: string, mode: string) => lawCalls(calls)
  .filter((call) => call.args.date === date && call.args.mode === mode);

function watchErrors(page: Page) {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") consoleErrors.push(message.text());
  });
  page.on("pageerror", (error) => pageErrors.push(error.message));
  return { consoleErrors, pageErrors };
}

test("a UTC midnight with the tab visible re-asks the search for the new day, and the previous "
  + "day's answer is gone before the new one arrives",
  async ({ page }) => {
    // Journey 1 and the search half of journey 5 in one mount, because they read the same
    // window from the same transition and splitting them would mean crossing the boundary twice
    // to assert the same thing.
    //
    // Why this transition is evidence about the code under test: a change of question remounts
    // `Search`, and `App` clears the envelope strip in `onSubmit`, `onAsOf`, `onRefine` and its
    // own popstate listener, so those drivers are masked. A rollover is none of them. Nothing
    // outside `Search` runs here; the only reason its layout effect runs is that `requestAsOf`
    // moved, and the only reason `requestAsOf` moved is that `useUtcDay` refreshed.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    let release!: () => void;
    const opened = new Promise<void>((resolve) => { release = resolve; });

    try {
      await installFixedClock(page);
      await routeMcp(page, calls, async (call, index) => {
        // The second search is held open with no timeout of its own: a route handler that has
        // not fulfilled leaves the browser fetch unsettled, so the component sits in its
        // in-flight state for as long as this window needs to be read.
        if (index === 2) await opened;
        return call.args.as_of === DAY_ONE
          ? searchAnswer("d14", 100)
          : searchAnswer("d15", 200);
      });

      const rows = page.locator("article.res-work");
      const strip = page.getByTestId("envelope-strip");
      const searched = page.getByTestId("population-searched");

      // The populated baseline, before anything below claims a locator is empty.
      await page.goto("/?space=search&q=travail", { waitUntil: "domcontentloaded" });
      await expect.poll(() => calls.length).toBe(1);
      expect(searchDates(calls)).toEqual([DAY_ONE]);
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText("Loi marked d14");
      await expect(strip).toHaveCount(1);
      await expect(searched).toContainText("Searched 100 works");
      expect(await pageText(page)).toContain("corpus-d14");

      await fastForwardPastMidnight(page);

      // The request is the evidence. Polling it also proves the clear ahead of it has already
      // run, because the request cannot leave the browser before the effect that issues it.
      await expect.poll(() => calls.length).toBe(2);
      expect(searchDates(calls)).toEqual([DAY_ONE, DAY_TWO]);
      // A rendered signal that React committed the in-flight state, so the reads below take one
      // instant of the real window rather than a frame that predates it.
      await expect(page.locator(".res-head .sub")).toContainText("Searching");

      // Non-retrying, on purpose. Each line reads one instant.
      expect(await rows.count()).toBe(0);
      expect(await strip.count()).toBe(0);
      const during = await pageText(page);
      expect(during).not.toContain("marked d14");
      expect(during).not.toContain("corpus-d14");
      expect(during).not.toContain("Searched 100 works");
      // Those negatives are all satisfied by a page that rendered nothing at all, so the text
      // they read is asserted to be the in-flight page rather than a blank one.
      expect(during).toContain("Searching");

      // Dwell and read a second instant while the answer is still held. A clear that arrived
      // late, or a field that repopulated itself, shows up in the gap between the two reads.
      await page.waitForTimeout(400);
      expect(await rows.count()).toBe(0);
      expect(await strip.count()).toBe(0);
      expect(await pageText(page)).not.toContain("corpus-d14");
      expect(calls).toHaveLength(2);

      // Release, and the new day's answer renders whole. Without this the reads above would
      // pass just as well on a page that can never render anything again.
      release();
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText("Loi marked d15");
      await expect(strip).toHaveCount(1);
      await expect(searched).toContainText("Searched 200 works");
      expect(await pageText(page)).toContain("corpus-d15");
      expect(calls).toHaveLength(2);
      expect(errors.pageErrors).toEqual([]);
      expect(errors.consoleErrors).toEqual([]);
    } finally {
      // Never leave the gate closed. A held handler that is never released hangs teardown.
      release();
    }
  });

test("a boundary crossed while the tab is away is caught on the return, and not before",
  async ({ page }) => {
    // Journey 2. The clock is moved with `setSystemTime`, which fires nothing, so the timer
    // trigger is out of the picture entirely and the only thing that can produce a request here
    // is the visibility listener.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    await installFixedClock(page);
    await routeMcp(page, calls, (call) => (call.args.as_of === DAY_ONE
      ? searchAnswer("d14", 100)
      : searchAnswer("d15", 200)));

    const rows = page.locator("article.res-work");

    await page.goto("/?space=search&q=travail", { waitUntil: "domcontentloaded" });
    await expect.poll(() => calls.length).toBe(1);
    expect(searchDates(calls)).toEqual([DAY_ONE]);
    await expect(rows).toContainText("Loi marked d14");

    // The reader switches away. Then the world moves on without them.
    await setTabHidden(page, true);
    await moveClockPastMidnight(page);
    expect(await utcDayInPage(page)).toBe(DAY_TWO);

    // A second event while still hidden. This is what makes the `!document.hidden` guard
    // load-bearing rather than incidental: the day HAS changed by now, so a listener that
    // refreshed unconditionally would issue the new day's request here, with the tab away.
    await setTabHidden(page, true);
    await page.waitForTimeout(500);
    expect(calls).toHaveLength(1);
    // Still showing the day it was asked for, which is the honest state for a tab nobody is
    // looking at: the answer on screen and the request that produced it still agree.
    await expect(rows).toContainText("Loi marked d14");

    const before = await scheduledDayTimers(page);

    // The reader comes back.
    await setTabHidden(page, false);
    await expect.poll(() => calls.length).toBe(2);
    expect(searchDates(calls)).toEqual([DAY_ONE, DAY_TWO]);
    await expect(rows).toContainText("Loi marked d15");
    // The return rescheduled the boundary timer exactly once, so the refresh ran once.
    expect(await scheduledDayTimers(page)).toBe(before + 1);
    // And no timer fired to produce any of this: the pending one is still pending.
    expect(await pendingDayTimers(page)).toBe(1);
    expect(errors.pageErrors).toEqual([]);
    expect(errors.consoleErrors).toEqual([]);
  });

test("repeated visibility returns leave exactly one boundary timer, so one midnight advances "
  + "the day once",
  async ({ page }) => {
    // Journey 3, the regression that motivated the repair. The old code scheduled a new timer on
    // every visibility return without clearing the previous one, so handles accumulated and each
    // leaked callback bred another at the next midnight.
    //
    // Request counts CANNOT see this. Every leaked callback computes the same new day, and
    // React's updater bails out when the value is unchanged, so ten leaked timers still produce
    // one re-render and one request. The census of timers is what discriminates, which is why
    // this file installs one.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    await installFixedClock(page);
    await routeMcp(page, calls, (call) => (call.args.as_of === DAY_ONE
      ? searchAnswer("d14", 100)
      : searchAnswer("d15", 200)));

    const rows = page.locator("article.res-work");

    await page.goto("/?space=search&q=travail", { waitUntil: "domcontentloaded" });
    await expect.poll(() => calls.length).toBe(1);
    await expect(rows).toContainText("Loi marked d14");

    // The census only means anything if it saw the hook. The hook is the only thing in this
    // application that schedules hours ahead, so an hours-long delay in the log is its
    // signature, and its presence proves the wrapper layered over the fake clock correctly.
    expect(await scheduledDayTimers(page)).toBe(1);
    expect(await pendingDayTimers(page)).toBe(1);

    const cycles = 5;
    for (let cycle = 0; cycle < cycles; cycle += 1) {
      await setTabHidden(page, true);
      await setTabHidden(page, false);
    }

    // Two independent facts, and the test needs both. That each return REACHED the hook, which
    // rules out the vacuous pass where the listener is not bound at all and nothing accumulates
    // because nothing happens. And that despite five reschedules only one handle is pending,
    // which is the repair. Delete the `clearTimeout` and this second line reads 6.
    expect(await scheduledDayTimers(page)).toBe(1 + cycles);
    expect(await pendingDayTimers(page)).toBe(1);
    // The day did not change, so nothing was re-asked. Ten refreshes of an unchanged value are
    // still one value.
    expect(calls).toHaveLength(1);
    await expect(rows).toContainText("Loi marked d14");

    const beforeBoundary = await scheduledDayTimers(page);
    await fastForwardPastMidnight(page);

    // `fastForward` fires every due timer at most once, so one pending handle means one callback
    // and one reschedule. Six pending handles would mean six of each. This is the assertion the
    // journey is named for.
    expect(await scheduledDayTimers(page)).toBe(beforeBoundary + 1);
    expect(await pendingDayTimers(page)).toBe(1);

    await expect.poll(() => calls.length).toBe(2);
    expect(searchDates(calls)).toEqual([DAY_ONE, DAY_TWO]);
    await expect(rows).toContainText("Loi marked d15");
    expect(calls).toHaveLength(2);
    expect(errors.pageErrors).toEqual([]);
    expect(errors.consoleErrors).toEqual([]);
  });

test("a rollover on a default-dated law route re-asks the outline and the read for the new day",
  async ({ page }) => {
    // Journey 4 on the law surface. `readDate` feeds the request AND both dependency lists, so
    // the thing sent and the thing watched cannot drift apart; this asserts on the thing sent.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    await installFixedClock(page);
    await routeMcp(page, calls, (call) => lawAnswer(
      call.args.date === DAY_ONE ? "d14" : "d15", 3));

    const articles = page.locator("article.art");

    await page.goto(`/?space=law&work=${WORK_ONE}&mode=read`, { waitUntil: "domcontentloaded" });
    await expect(articles).toHaveCount(3);
    await expect(articles.first()).toContainText("Article text marked d14");

    const dayOne = lawCalls(calls);
    // The outline path and the read path are separate effects with separate dependency lists.
    // Both must have asked, and both must have asked for today.
    expect(dayOne.length).toBeGreaterThanOrEqual(2);
    expect(dayOne.every((call) => call.args.date === DAY_ONE)).toBe(true);
    expect(dayOne.every((call) => call.args.work === WORK_ONE)).toBe(true);
    expect(new Set(dayOne.map((call) => call.args.mode))).toEqual(new Set(["outline", "full"]));

    const mark = calls.length;
    await fastForwardPastMidnight(page);

    await expect(articles.first()).toContainText("Article text marked d15");
    const dayTwo = lawCalls(calls.slice(mark));
    expect(dayTwo.length).toBeGreaterThanOrEqual(2);
    expect(dayTwo.every((call) => call.args.date === DAY_TWO)).toBe(true);
    expect(new Set(dayTwo.map((call) => call.args.mode))).toEqual(new Set(["outline", "full"]));
    // Nothing kept asking for yesterday behind the new answer.
    expect(lawCalls(calls).map((call) => call.args.date))
      .toEqual(lawCalls(calls).map((call, index) => (index < mark ? DAY_ONE : DAY_TWO)));
    expect(errors.pageErrors).toEqual([]);
    expect(errors.consoleErrors).toEqual([]);
  });

test("the previous day's gap is cleared before paint, not when the new day's answer arrives",
  async ({ page }) => {
    // Journey 5 on the law surface, where the pre-paint clear is observable.
    //
    // Which of the four cleared fields can actually be read in the in-flight window, established
    // by reading what renders them rather than by hope:
    //
    //   - `ui.gap` renders from `ui` alone, ahead of every other branch, and the read effect
    //     does not touch `ui` before its request. So the previous day's gap survives the whole
    //     in-flight window unless something clears it before paint. THIS is the discriminating
    //     assertion, and it is the one below.
    //   - `loaded` is also cleared by the read effect's own body before it fetches, so by the
    //     time a request has been recorded it is undefined either way. Asserted anyway, but it
    //     is evidence about the passive clear, not about the layout effect.
    //   - `toc` renders only inside `Provision`, which renders only when `loaded` is set, so its
    //     clear has no observable frame.
    //   - `strip` is never populated on a law route: nothing on this path sets it from a
    //     response. Its clear is unobservable here and is covered on the search route instead.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    let release!: () => void;
    const opened = new Promise<void>((resolve) => { release = resolve; });

    try {
      await installFixedClock(page);
      await routeMcp(page, calls, async (call) => {
        if (call.args.date === DAY_ONE) return lawAnswer("d14", 0);
        await opened;
        return lawAnswer("d15", 2);
      });

      const gap = page.locator(".gap");
      const skeleton = page.locator(".sk-law");
      const articles = page.locator("article.art");

      // The populated baseline is the gap itself: this law holds no text on day one.
      await page.goto(`/?space=law&work=${WORK_ONE}&mode=read`, { waitUntil: "domcontentloaded" });
      await expect(gap).toHaveCount(1);
      await expect(gap).toContainText(GAP_SENTENCE);
      expect(lawCalls(calls).every((call) => call.args.date === DAY_ONE)).toBe(true);

      const mark = calls.length;
      await fastForwardPastMidnight(page);

      // The new day's request has left the browser and is being held.
      await expect.poll(() => lawCalls(calls.slice(mark)).length).toBeGreaterThanOrEqual(1);
      expect(lawCalls(calls.slice(mark)).every((call) => call.args.date === DAY_TWO)).toBe(true);
      // The page is live rather than blank, so the emptiness below is read from a real frame.
      // Deliberately the law header and not the skeleton: the header renders in every state,
      // including the broken one, so this guard cannot consume the failure that belongs to the
      // assertion below it.
      await expect(page.locator(".lawhead h2")).toBeVisible();

      // Non-retrying. Yesterday's refusal must not be sitting under today's date for even one
      // committed frame, which is exactly what the retrying form would tolerate.
      expect(await gap.count()).toBe(0);
      expect(await articles.count()).toBe(0);
      // The same claim read as text rather than as a node count, so a gap that survived inside
      // some other container would still be caught.
      expect(await pageText(page)).not.toContain(GAP_SENTENCE);
      // What is deliberately NOT asserted: the law's title. It belongs to the law, not to the
      // date, the work did not change here, and the layout effect leaves it alone on purpose.

      // And what stands in the gap's place is the shape of what is coming.
      expect(await skeleton.count()).toBe(1);

      await page.waitForTimeout(400);
      expect(await gap.count()).toBe(0);
      expect(await skeleton.count()).toBe(1);

      release();
      // The held answer renders whole, so the reads above were taken on a page that can still
      // render, and the gap locator above is one this fixture is able to produce.
      await expect(articles).toHaveCount(2);
      await expect(articles.first()).toContainText("Article text marked d15");
      expect(await gap.count()).toBe(0);
      expect(errors.pageErrors).toEqual([]);
      expect(errors.consoleErrors).toEqual([]);
    } finally {
      release();
    }
  });

test("a prior-day search answer released after the rollover cannot write over the new day",
  async ({ page }) => {
    // Journey 6. The losing request is issued before the boundary and released after the winning
    // one has already rendered. The layout effect advances the request generation when
    // `requestAsOf` moves, each request captures the value it was issued under, and its
    // continuation compares the two before writing. A boolean flipped by passive cleanup could
    // not do this job: cleanup runs after the next paint, so an answer arriving in that interval
    // would still be allowed to write.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    let release!: () => void;
    const opened = new Promise<void>((resolve) => { release = resolve; });

    try {
      await installFixedClock(page);
      await routeMcp(page, calls, async (call) => {
        if (call.args.as_of === DAY_ONE) {
          await opened;
          // Two rows and a different denominator, so a stale write would change the count and
          // the footer as well as the words, and could not hide inside a coincidence.
          return searchAnswer("d14", 100, 2);
        }
        return searchAnswer("d15", 300, 1);
      });

      const rows = page.locator("article.res-work");
      const searched = page.getByTestId("population-searched");

      await page.goto("/?space=search&q=travail", { waitUntil: "domcontentloaded" });
      await expect.poll(() => calls.length).toBe(1);
      expect(searchDates(calls)).toEqual([DAY_ONE]);
      await expect(page.locator(".res-head .sub")).toContainText("Searching");

      await fastForwardPastMidnight(page);
      await expect.poll(() => calls.length).toBe(2);
      expect(searchDates(calls)).toEqual([DAY_ONE, DAY_TWO]);

      // The populated baseline: the new day answered and rendered.
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText("Loi marked d15");
      await expect(searched).toContainText("Searched 300 works");

      // Now the previous day's answer arrives, late. Polling the page's own settled count is
      // what makes this a delivery rather than a release: a route handler writing a body proves
      // nothing about the fetch whose continuation carries the guard. Without it, a loser that
      // silently never arrived would pass every assertion below.
      const settledBeforeRelease = await settledMcp(page);
      release();
      await expect.poll(() => settledMcp(page)).toBe(settledBeforeRelease + 1);
      await page.waitForTimeout(500);

      expect(await rows.count()).toBe(1);
      await expect(rows).toContainText("Loi marked d15");
      await expect(searched).toContainText("Searched 300 works");
      expect(await page.locator("article.res-work", { hasText: "marked d14" }).count()).toBe(0);
      const after = await pageText(page);
      expect(after).not.toContain("marked d14");
      expect(after).not.toContain("corpus-d14");
      expect(after).not.toContain("Searched 100 works");
      expect(after).toContain("corpus-d15");
      expect(calls).toHaveLength(2);
      expect(errors.pageErrors).toEqual([]);
      expect(errors.consoleErrors).toEqual([]);
    } finally {
      release();
    }
  });

test("a pinned date is not re-asked at a rollover, and the page it produced is left alone",
  async ({ page }) => {
    // Journey 7. The reader chose this date; the clock moving did not change their question.
    // The risk this guards is over-correction: a hook that refreshed the request rather than
    // the default would clear and refetch a pinned reading at midnight.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    await installFixedClock(page);
    await routeMcp(page, calls, (call) => (call.args.as_of === PINNED
      ? searchAnswer("pinned", 100)
      : searchAnswer("today", 200)));

    const rows = page.locator("article.res-work");
    const strip = page.getByTestId("envelope-strip");

    await page.goto(`/?space=search&q=travail&asOf=${PINNED}`, { waitUntil: "domcontentloaded" });
    await expect.poll(() => calls.length).toBe(1);
    expect(searchDates(calls)).toEqual([PINNED]);
    await expect(rows).toContainText("Loi marked pinned");
    await expect(strip).toHaveCount(1);

    const before = await scheduledDayTimers(page);
    await fastForwardPastMidnight(page);

    // The rollover really happened and the hook really ran: one more boundary timer scheduled,
    // which only the refresh does. Without this the rest of the test would pass vacuously on a
    // page whose clock never moved.
    await expect.poll(() => scheduledDayTimers(page)).toBe(before + 1);
    expect(await utcDayInPage(page)).toBe(DAY_TWO);

    await page.waitForTimeout(500);
    expect(calls).toHaveLength(1);
    expect(searchDates(calls)).toEqual([PINNED]);
    await expect(rows).toContainText("Loi marked pinned");
    await expect(strip).toHaveCount(1);

    // And the application does know the day moved. Dropping the pin asks for the NEW day, which
    // is the end to end proof that `today` advanced underneath a request that correctly ignored
    // it. If `today` had been captured once at render, this would ask for the old day.
    await page.getByRole("button", { name: "use today instead" }).click();
    await expect.poll(() => calls.length).toBe(2);
    expect(searchDates(calls)).toEqual([PINNED, DAY_TWO]);
    await expect(rows).toContainText("Loi marked today");
    expect(errors.pageErrors).toEqual([]);
    expect(errors.consoleErrors).toEqual([]);
  });

test("Back and Forward across a rollover ask for the pinned date and for the current day",
  async ({ page }) => {
    // Journey 8. Two history entries that differ in exactly the thing under test: one route
    // carries an explicit date, the other takes the default. The citation link is the driver
    // because it is the transition that pushes a history entry AND clears the date, so Back and
    // Forward move between a pinned reading and a default one.
    //
    // The boundary is crossed while standing on the default-dated entry, so Back has to produce
    // the pinned date the clock cannot touch, and Forward has to produce the day the clock moved
    // to. Reading `s.date` alone would send undefined; capturing `today` at render would send
    // the day before.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    await installFixedClock(page);
    await routeMcp(page, calls, (call) => lawAnswer(
      `${call.args.work === WORK_ONE ? "one" : "two"}-${String(call.args.date)}`,
      3,
      call.args.work === WORK_ONE ? WORK_TWO : undefined));

    const articles = page.locator("article.art");
    const citation = page.locator("button.citelink");

    await page.goto(`/?space=law&work=${WORK_ONE}&date=${PINNED}&mode=read`,
      { waitUntil: "domcontentloaded" });
    await expect(articles.first()).toContainText(`marked one-${PINNED}`);
    expect(lawCalls(calls).every((call) => call.args.date === PINNED)).toBe(true);

    // Push the second entry: another law, with the date deliberately dropped.
    let mark = calls.length;
    await citation.first().click();
    await expect(articles.first()).toContainText(`marked two-${DAY_ONE}`);
    const opened = lawCalls(calls.slice(mark));
    expect(opened.length).toBeGreaterThanOrEqual(2);
    expect(opened.every((call) => call.args.work === WORK_TWO)).toBe(true);
    expect(opened.every((call) => call.args.date === DAY_ONE)).toBe(true);

    mark = calls.length;
    await fastForwardPastMidnight(page);
    await expect(articles.first()).toContainText(`marked two-${DAY_TWO}`);
    expect(lawCalls(calls.slice(mark)).every((call) => call.args.date === DAY_TWO)).toBe(true);

    // Back to the pinned reading. The clock has moved twice since it was opened and must not
    // have reached it.
    mark = calls.length;
    await page.goBack();
    await expect(articles.first()).toContainText(`marked one-${PINNED}`);
    const back = lawCalls(calls.slice(mark));
    expect(back.length).toBeGreaterThanOrEqual(2);
    expect(back.every((call) => call.args.work === WORK_ONE)).toBe(true);
    expect(back.every((call) => call.args.date === PINNED)).toBe(true);

    // Forward to the default reading, which must follow the clock to the day it is now.
    mark = calls.length;
    await page.goForward();
    await expect(articles.first()).toContainText(`marked two-${DAY_TWO}`);
    const forward = lawCalls(calls.slice(mark));
    expect(forward.length).toBeGreaterThanOrEqual(2);
    expect(forward.every((call) => call.args.work === WORK_TWO)).toBe(true);
    expect(forward.every((call) => call.args.date === DAY_TWO)).toBe(true);
    expect(errors.pageErrors).toEqual([]);
    expect(errors.consoleErrors).toEqual([]);
  });

test("a prior-day law text released after the rollover cannot write over the new day",
  async ({ page }) => {
    // Journey 9. Journey 6's shape on the law read path, which is the case none of the first
    // eight reached: journey 6 drives the search surface, and journey 5 drives this surface but
    // holds the NEW day's answer, so the previous day's had already landed and there was no late
    // writer at all. The repair this kills is the read effect comparing the generation it was
    // issued under, where a boolean flipped by passive cleanup used to be.
    //
    // WHICH REQUEST IS HELD, and why it is the `full` one. In the non-anchor case the read
    // effect issues two sequential calls, `outline` then `full`, and only the second resolves
    // the promise whose continuation carries the guard. Holding the outline would stall
    // `fetchRead` before it had decided anything, and releasing it would then issue a fresh
    // day one `full` request AFTER the boundary, which is a different situation with a
    // different shape. Holding the `full` gives the exact case: one complete answer to a
    // question the reader has already left, arriving whole and late.
    //
    // WHY THIS ISOLATES THE READ EFFECT'S GUARD from the outline effect's. Every `outline` call
    // on both days is answered promptly, so no outline response is ever in flight across the
    // boundary and the outline effect's own guard is never consulted about a stale response.
    // Confirmed by mutation: removing the outline guard leaves this journey green.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    let release!: () => void;
    const opened = new Promise<void>((resolve) => { release = resolve; });

    try {
      await installFixedClock(page);
      await routeMcp(page, calls, async (call) => {
        const dayOne = call.args.date === DAY_ONE;
        // The loser, held from page load. It differs from the winner in four independent ways:
        // article count, article text, title, and the validity interval in the header. A stale
        // write cannot land as a coincidence that happens to look like the right answer.
        if (dayOne && call.args.mode === "full") {
          await opened;
          return lawAnswer("d14", 5, undefined, "2018-01-09");
        }
        return dayOne
          ? lawAnswer("d14", 5, undefined, "2018-01-09")
          : lawAnswer("d15", 2, undefined, "2021-05-05");
      });

      const articles = page.locator("article.art");
      const header = page.locator(".lawhead");
      const gap = page.locator(".gap");

      await page.goto(`/?space=law&work=${WORK_ONE}&mode=read`, { waitUntil: "domcontentloaded" });

      // The loser has left the browser and is being held. The outline effect answered for day
      // one, so the page is genuinely on day one rather than merely blank: its title is up, and
      // the text it is still waiting for is the request now hanging.
      await expect.poll(() => lawCallsFor(calls, DAY_ONE, "full").length).toBe(1);
      await expect(header).toContainText("Loi d14");
      await expect(page.locator(".sk-law")).toHaveCount(1);
      expect(await articles.count()).toBe(0);

      await fastForwardPastMidnight(page);

      // The populated baseline: the new day answered and rendered whole, body and header.
      await expect(articles).toHaveCount(2);
      await expect(articles.first()).toContainText("Article text marked d15");
      await expect(header).toContainText("Loi d15");
      await expect(header).toContainText("2021-05-05");
      expect(await gap.count()).toBe(0);
      expect(lawCallsFor(calls, DAY_TWO, "full")).toHaveLength(1);
      const answered = calls.length;

      // Now the previous day's text arrives, late and whole. The settled count is read from
      // inside the page, so this asserts the browser handed the response to the fetch whose
      // continuation carries the guard, rather than asserting that Playwright wrote a body.
      const settledBeforeRelease = await settledMcp(page);
      release();
      await expect.poll(() => settledMcp(page)).toBe(settledBeforeRelease + 1);
      await page.waitForTimeout(500);

      // And it wrote nothing. Non-retrying: a write that landed and was corrected a moment later
      // is still a frame in which the reader was shown the wrong law text under today's date.
      expect(await articles.count()).toBe(2);
      await expect(articles.first()).toContainText("Article text marked d15");
      expect(await page.locator("article.art", { hasText: "marked d14" }).count()).toBe(0);
      expect(await gap.count()).toBe(0);
      const head = await header.textContent() ?? "";
      expect(head).toContain("Loi d15");
      expect(head).toContain("2021-05-05");
      expect(head).not.toContain("Loi d14");
      // The validity interval is the assertion that catches a stale write which happened to
      // agree about the article count, because `setLoaded` carries both and neither is derived
      // from the other.
      expect(head).not.toContain("2018-01-09");
      // The late answer produced no follow-up request of its own.
      expect(calls).toHaveLength(answered);
      expect(errors.pageErrors).toEqual([]);
      expect(errors.consoleErrors).toEqual([]);
    } finally {
      release();
    }
  });

test("a prior-day law outline released after the rollover cannot repopulate the contents",
  async ({ page }) => {
    // Journey 10. The outline effect's generation guard, which journey 9 deliberately does not
    // reach and which nothing else in this file was killing.
    //
    // THE DISCRIMINATOR IS `s.anchor`, AND IT IS STRUCTURAL. The obvious way to build this is to
    // hold "the first outline request", because on a default law route the outline effect and
    // the read effect each issue a byte-identical `mode: "outline"` call and only React's
    // effect-declaration order separates them. That would make this test a hostage to a
    // declaration order nobody would think to preserve. It is avoidable: the read effect's
    // `fetchRead` takes its `mode: "select"` branch whenever an anchor is set and then never
    // asks for an outline at all, so on an anchored route the ONE `mode: "outline"` request
    // belongs to the outline effect by construction. The journey selects on the request's own
    // arguments, never on arrival order.
    //
    // NOT the dependency sets. `s.language` is in BOTH lists, so changing it re-runs both
    // effects; the only deps unique to the read effect are `s.mode` and `s.anchor`, and those
    // suppress the read effect rather than identifying its request. Request identity is what
    // this needed, and `s.anchor` supplies it.
    //
    // `toc` IS THE OBSERVABLE because the read effect never writes it. A stale outline write
    // moves the contents column and nothing else can, so what this asserts cannot be satisfied
    // or broken by the read path.
    const errors = watchErrors(page);
    const calls: McpCall[] = [];
    let release!: () => void;
    const opened = new Promise<void>((resolve) => { release = resolve; });

    try {
      await installFixedClock(page);
      await routeMcp(page, calls, async (call) => {
        const dayOne = call.args.date === DAY_ONE;
        if (call.args.mode === "outline") {
          // The loser, held from page load, and the only outline request on this route.
          // Nine entries against the winner's seven, and headings that name their day, so a
          // stale write changes both the count and the words.
          if (dayOne) { await opened; return lawAnswer("outline-d14", 9); }
          return lawAnswer("outline-d15", 7);
        }
        return dayOne ? lawAnswer("text-d14", 1) : lawAnswer("text-d15", 1);
      });

      const articles = page.locator("article.art");
      const contents = page.locator(".toccol");
      const tocRows = page.locator(".toccol ul.rows > li");

      await page.goto(`/?space=law&work=${WORK_ONE}&anchor=art-1&mode=read`,
        { waitUntil: "domcontentloaded" });

      // Day one's READ path completed, so the page is genuinely populated on day one rather
      // than merely blank: its article text is on screen. Only its outline is still hanging,
      // which is why there is no contents column beside it yet.
      await expect(articles).toHaveCount(1);
      await expect(articles.first()).toContainText("Article text marked text-d14");
      expect(await contents.count()).toBe(0);
      expect(lawCallsFor(calls, DAY_ONE, "outline")).toHaveLength(1);
      expect(lawCallsFor(calls, DAY_ONE, "select")).toHaveLength(1);
      // The read effect asked for a selection, not an outline, so the held request above is the
      // outline effect's by construction rather than by arrival order.
      expect(lawCallsFor(calls, DAY_ONE, "outline")).toHaveLength(1);

      await fastForwardPastMidnight(page);

      // The populated baseline: the new day's contents rendered whole, beside the new day's text.
      await expect(contents).toHaveCount(1);
      await expect(tocRows).toHaveCount(7);
      await expect(contents).toContainText("Heading outline-d15");
      await expect(articles.first()).toContainText("Article text marked text-d15");
      expect(lawCallsFor(calls, DAY_TWO, "outline")).toHaveLength(1);
      const answered = calls.length;

      // The previous day's outline arrives, late and whole, and the page is asked to confirm it
      // received it rather than the harness confirming it sent it.
      const settledBeforeRelease = await settledMcp(page);
      release();
      await expect.poll(() => settledMcp(page)).toBe(settledBeforeRelease + 1);
      await page.waitForTimeout(500);

      // And it wrote nothing. Non-retrying: a contents column that took yesterday's articles and
      // was corrected a moment later still offered the reader nine links into a document that
      // has seven.
      expect(await tocRows.count()).toBe(7);
      expect(await page.locator(".toccol ul.rows > li",
        { hasText: "Heading outline-d14" }).count()).toBe(0);
      await expect(contents).toContainText("Heading outline-d15");
      expect(await page.locator(".toccol .tochead .mono").textContent()).toBe("7");
      // `outline-d14` is a string ONLY the held outline response carries, in its headings and in
      // its document title, so this is a claim about that response and not about the read path.
      expect(await pageText(page)).not.toContain("outline-d14");
      await expect(articles.first()).toContainText("Article text marked text-d15");
      expect(calls).toHaveLength(answered);
      expect(errors.pageErrors).toEqual([]);
      expect(errors.consoleErrors).toEqual([]);
    } finally {
      release();
    }
  });
