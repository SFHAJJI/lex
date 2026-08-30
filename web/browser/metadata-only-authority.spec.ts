import { expect, test, type Page, type Route } from "@playwright/test";

/**
 * O12. The metadata_only suppression is a POSITIVE claim: it tells the reader that everything the
 * corpus returned matched records rather than wording, and it hides the rows while saying so.
 *
 * The browser lane decided it by walking the raw response a second time, after the governed parse
 * had already run. That helper filters on status alone, so a response the governed parser refuses
 * still reached it as evidence. Each case below is a response the parser refuses, or one whose
 * rows it never admitted, and none of them may authorise suppression.
 *
 * The last case is the other direction, and it is the one that keeps this file honest: a clean,
 * fully accepted, genuinely metadata-only response must still suppress. Without it every assertion
 * here would pass against a page that simply never suppresses anything.
 *
 * The `/mcp` responses are controlled fixtures and the route fails closed. No law payload and no
 * golden content is read or written by this file.
 */

const HEADING = "No held text match";

const envelope = (extra: Record<string, unknown> = {}) => ({
  publisher: "lu-legilux",
  jurisdiction: "LU",
  timeline_semantics: "publisher_applicability",
  status: "ok",
  ...extra,
});

const metadataHit = (extra: Record<string, unknown> = {}) => ({
  // A GENUINE canonical key: yyyy-MM-dd--sha256(publisher version id), per VersionIdentity.
  lex_id: "lu-legilux:fixture-authority:2024-08-04--74422504dcb92c9661b9977cc9c7b4dadb9ae730b43343f10f8ae5e7b91ebc11",
  title: "Fixture instrument",
  language: "fr",
  valid_from: "2024-08-04",
  valid_to: null,
  match_reasons: ["work_metadata"],
  ...extra,
});

const unit = (over: Record<string, unknown> = {}) => ({
  envelope: envelope(),
  retrieval_mode: "keyword",
  population: {
    basis: "selected_metadata_scope",
    works_in_scope: 1,
    scope_filters_applied: true,
    query_ran: true,
    known_exclusions: [],
  },
  hits: [metadataHit()],
  ...over,
});

async function search(page: Page, units: unknown[]): Promise<string> {
  await page.route("**/mcp", async (route: Route) => {
    const request = route.request().postDataJSON() as {
      id: number; params?: { name?: string };
    };
    if (request.params?.name !== "search") {
      await route.fulfill({
        status: 200, contentType: "application/json",
        body: JSON.stringify({
          jsonrpc: "2.0", id: request.id,
          error: { code: -32602, message: "authority fixture serves search only" },
        }),
      });
      return;
    }
    await route.fulfill({
      status: 200, contentType: "application/json",
      body: JSON.stringify({
        jsonrpc: "2.0", id: request.id,
        result: { content: [{ type: "text", text: JSON.stringify(units) }] },
      }),
    });
  });

  await page.goto("/?space=search&q=fixture", { waitUntil: "networkidle" });
  return (await page.locator("body").innerText()).toString();
}

test("a clean accepted metadata-only response still suppresses", async ({ page }) => {
  const body = await search(page, [unit()]);

  expect(body).toContain(HEADING);
});

test("a truncated page cannot authorise the response-wide metadata-only claim", async ({ page }) => {
  const body = await search(page, [unit({
    response_row_set: { maximum: 20, returned: 1, truncated: true },
  })]);

  expect(body).not.toContain(HEADING);
});

test("a false receipt cannot authorise suppression", async ({ page }) => {
  // status ok, and the producer's own receipt says the query never ran.
  const body = await search(page, [unit({
    population: {
      basis: "selected_metadata_scope",
      works_in_scope: 1,
      scope_filters_applied: true,
      query_ran: false,
      known_exclusions: [],
    },
  })]);

  expect(body).not.toContain(HEADING);
});

test("an unknown status carrying hits cannot authorise suppression", async ({ page }) => {
  const body = await search(page, [unit({
    envelope: envelope({ status: "a_state_this_client_has_never_seen" }),
  })]);

  expect(body).not.toContain(HEADING);
});

/**
 * Attribution to a MOUNTED index is a server concept: this lane has no reader registry, so a
 * well-formed publisher it has never heard of is indistinguishable from one it has. That case is
 * covered where it can be decided, in TrustNoticeTests.An_unattributable_publisher_disables_the_
 * metadata_claim. What this lane can decide is whether the identity is one the producer could have
 * minted at all, and a response carrying one it could not is not evidence of anything.
 */
test("a publisher identity the producer could not have minted authorises nothing",
  async ({ page }) => {
    for (const publisher of ["", "   ", "x".repeat(300)]) {
      const body = await search(page, [unit({ envelope: envelope({ publisher }) })]);
      expect(body).not.toContain(HEADING);
    }
  });

/**
 * The mixed case, and the one the completeness gate exists for. One publisher is accepted and is
 * genuinely metadata-only; a second is a shape the parse refuses. Every other case here leaves the
 * population empty, so the gate looks redundant until this response arrives: a claim made ACROSS
 * publishers may not be made while one of them was never read.
 */
test("an accepted metadata publisher beside an unreadable one authorises nothing",
  async ({ page }) => {
    const body = await search(page, [
      unit(),
      // No population at all, which is a shape the producer never sends and the parse refuses.
      { envelope: envelope({ publisher: "eu-eurlex", jurisdiction: "EU" }),
        retrieval_mode: "keyword", hits: [] },
    ]);

    expect(body).not.toContain(HEADING);
  });

/**
 * O14. Attribution completeness is not whole-response authority. A clean metadata unit beside an
 * unknown-status sibling leaves the normalized answer "complete" while the parse has already
 * counted that sibling unusable, so the positive claim was still reachable over a response one
 * publisher of which was never read.
 */
test("a clean metadata unit beside an unusable sibling authorises nothing", async ({ page }) => {
  const body = await search(page, [
    unit(),
    unit({ envelope: envelope({ publisher: "eu-eurlex", jurisdiction: "EU",
                                status: "a_state_this_client_has_never_seen" }) }),
  ]);

  expect(body).not.toContain(HEADING);
});

/**
 * O15. The search row schema validates lex_id as nonempty text and nothing else, so a plausible
 * string coordinate with an invalid date stayed in the accepted rows and could suppress. Each of
 * these is one invalid row in an otherwise clean metadata response.
 */
test("a malformed string coordinate cannot authorise suppression", async ({ page }) => {
  const body = await search(page, [unit({
    hits: [metadataHit({ lex_id: "no-colon-here" })],
  })]);

  expect(body).not.toContain(HEADING);
});

test("a coordinate belonging to another publisher cannot authorise suppression",
  async ({ page }) => {
    const body = await search(page, [unit({
      hits: [metadataHit({ lex_id: "eu-eurlex:someone-elses-work:2024-08-04" })],
    })]);

    expect(body).not.toContain(HEADING);
  });

test("a non-canonical date cannot authorise suppression", async ({ page }) => {
  const body = await search(page, [unit({
    hits: [metadataHit({ valid_from: "2024-8-4" })],
  })]);

  expect(body).not.toContain(HEADING);
});

/**
 * O16. The notice needs publisher:group. It was handed the full version lex_id, whose group
 * segment then carried a colon, so it rejected every ordinary row and silently dropped the
 * disclosure and the official-publisher link that are the whole point of the notice.
 */
/**
 * O17. A shape check plus Date.parse is not a date check: JavaScript normalises, so 2024-02-30
 * parses happily and becomes 2024-03-01. A day that never existed would have authorised a
 * suppression.
 */
test("a date that never existed cannot authorise suppression", async ({ page }) => {
  for (const invalid of ["2024-02-30", "2023-02-29", "2024-13-01", "2024-00-10", "0000-01-01"]) {
    const body = await search(page, [unit({ hits: [metadataHit({ valid_from: invalid })] })]);
    expect(body).not.toContain(HEADING);
  }
});

test("a real leap day is a real date and still suppresses", async ({ page }) => {
  // The version key carries the same date, because they are one fact. A fixture where they
  // disagree is not a leap-day case, it is the date-mismatch case one test below.
  const body = await search(page, [unit({
    hits: [metadataHit({
      valid_from: "2024-02-29",
      lex_id: "lu-legilux:fixture-authority:2024-02-29--74422504dcb92c9661b9977cc9c7b4dadb9ae730b43343f10f8ae5e7b91ebc11",
    })],
  })]);

  expect(body).toContain(HEADING);
});

/**
 * O18. Nonempty colon segments are not the producer's grammar. A group carrying a slash stayed
 * claimable, and the notice then rejected it and silently dropped the disclosure and the official
 * link, which is O16 reached by a different road.
 */
test("a coordinate outside the producer grammar cannot authorise suppression",
  async ({ page }) => {
    for (const bad of [
      "lu-legilux:bad/group:2024-08-04",
      "lu-legilux:bad group:2024-08-04",
      `lu-legilux:${"g".repeat(250)}:2024-08-04`,
      "lu legilux:fixture-authority:2024-08-04",
      "lu-legilux:fixture-authority:bad/version",
    ]) {
      const body = await search(page, [unit({ hits: [metadataHit({ lex_id: bad })] })]);
      expect(body).not.toContain(HEADING);
    }
  });

/**
 * O20. The two-segment work form is NOT a search coordinate. A hit's lex_id is DocJson's d.Key,
 * which VersionIdentity mints as publisher:group:yyyy-MM-dd--sha256. My earlier fixture blessed a
 * shape the producer cannot emit, so the test agreed with the code about something untrue.
 */
test("a coordinate the producer cannot mint authorises nothing", async ({ page }) => {
  for (const impossible of [
    "lu-legilux:fixture-authority",
    "lu-legilux:fixture-authority:2024-08-04",
    "lu-legilux:fixture-authority:2024-08-04--abc123",
    `lu-legilux:fixture-authority:2024-08-04--${"A".repeat(64)}`,
    `lu-legilux:fixture-authority:2024-08-04--${"f".repeat(63)}`,
  ]) {
    const body = await search(page, [unit({ hits: [metadataHit({ lex_id: impossible })] })]);
    expect(body).not.toContain(HEADING);
  }
});

test("a version key whose date disagrees with valid_from authorises nothing", async ({ page }) => {
  const body = await search(page, [unit({
    hits: [metadataHit({ valid_from: "2024-08-05" })],
  })]);

  expect(body).not.toContain(HEADING);
});

test("an ordinary version id still renders the disclosure and the official link",
  async ({ page }) => {
    const body = await search(page, [unit()]);
    expect(body).toContain(HEADING);

    // The disclosure list sits inside a collapsed details, so it is read from the DOM rather
    // than from visible text. Its presence is the point: the association evidence stays
    // inspectable without ever being presented as an answer.
    const notice = page.locator('[data-testid="metadata-only-notice"]');
    await expect(notice).toHaveCount(1);
    const disclosed = notice.locator("details li");
    await expect(disclosed).toHaveCount(1);
    expect(await disclosed.first().textContent()).toContain("Fixture instrument");
    // The coordinate reached the notice as publisher:group, so the group is the work and NOT
    // the version id. That is the whole of O16: handed the version id, the notice rejected the
    // row and dropped this disclosure entirely.
    const row = (await disclosed.first().textContent()) ?? "";
    expect(row).toContain("fixture-authority");
    expect(row).toContain("lu-legilux");
    expect(row).not.toContain("2024-08-04");

    // And both agreed actions are present.
    await expect(notice.getByRole("link", { name: "View coverage and known gaps" }))
      .toHaveCount(1);
    await expect(notice.getByRole("link", { name: "Search the official publisher" }))
      .toHaveCount(1);
  });

test("a malformed coordinate cannot authorise suppression", async ({ page }) => {
  const body = await search(page, [unit({
    hits: [metadataHit({ lex_id: 42, valid_from: "not-a-date" })],
  })]);

  expect(body).not.toContain(HEADING);
});
