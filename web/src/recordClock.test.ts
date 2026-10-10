import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import {
  NO_OBSERVATION, NO_PUBLICATION_DATE, NO_RECORD_CLOCK, SEVERAL_STATES_ON_DATE,
  railClockLabel, recordClockLabel, recordClockOf, recordClocksByDate,
} from "./recordClock.ts";
import { assistantProvisionLoad } from "./assistantShell.ts";
import { intervalLabel } from "./temporal.ts";

// One live Luxembourg state, verbatim from the shape `DocJson` emits: a wording the publisher
// dates from 2021-01-26, published the same day, first held by Lex in 2026. The pair below is
// the one that matters: 2,748 of 4,656 LU states were published AFTER they applied, so a row
// showing only the interval is a row that cannot be checked.
const lateState = {
  lex_id: "lu-legilux:loi-2006-07-31-n2:2020-01-01",
  valid_from: "2020-01-01",
  valid_to: "2021-01-25",
  publication_date: "2023-07-19",
  observed_from: "2026-08-14T23:05:14Z",
};

test("both clocks reach the reader, and the record clock is the response's own values", () => {
  const clock = recordClockOf(lateState);
  assert.deepEqual(clock,
    { publicationDate: "2023-07-19", observedFrom: "2026-08-14T23:05:14Z" });
  // Verbatim, including the UTC instant. Section 1 of the specification requires the timestamp
  // as it arrived, not a rendered day, because a reader checking Lex against the publisher is
  // comparing strings.
  assert.equal(recordClockLabel(clock),
    "Published 2023-07-19. First observed 2026-08-14T23:05:14Z.");
  // And the legal clock stays exactly what it was: two clocks, not one replaced by the other.
  // The arrow is built rather than typed so this file stays ASCII.
  const arrow = String.fromCharCode(0x2192);
  assert.equal(
    intervalLabel("lu-legilux:loi-2006-07-31-n2", lateState.valid_from, lateState.valid_to),
    `in force 2020-01-01 ${arrow} 2021-01-25`);
});

test("a missing publication date is disclosed, never inferred from anything beside it", () => {
  // `DocRow.PublicationDate` is nullable and `DocRow.ObservedFrom` is not, so this is a real
  // state of a real record rather than a broken response.
  const row = { valid_from: "2019-05-01", publication_date: null,
                observed_from: "2026-08-14T23:05:14Z" };
  const clock = recordClockOf(row);
  assert.equal(clock.publicationDate, undefined);
  const label = recordClockLabel(clock);
  assert.ok(label.includes(NO_PUBLICATION_DATE),
    "an absent publication date is not disclosed as absent");
  // The two substitutions that would each manufacture the fact the reader came to check.
  assert.ok(!/Published/.test(label), "a publication date was asserted from an absent field");
  assert.ok(!label.includes("2019-05-01"),
    "the legal date was reused as the publication date");
  assert.equal(label,
    `${NO_PUBLICATION_DATE} First observed 2026-08-14T23:05:14Z.`);
  // The observation instant is still stated, and stated as an observation: the two halves are
  // independent, so losing one must not take the other with it.
  assert.ok(label.includes("First observed 2026-08-14T23:05:14Z."));
});

test("a missing observation is disclosed on its own terms, and a bare row states neither", () => {
  assert.equal(recordClockLabel(recordClockOf({ publication_date: "2021-01-26" })),
    `Published 2021-01-26. ${NO_OBSERVATION}`);
  // Neither field: one sentence, scoped to the response rather than to the corpus. Saying
  // "the publisher recorded none" would be a claim about the record that one envelope cannot
  // support, and saying nothing at all is the defect this whole change removes.
  assert.equal(recordClockLabel(recordClockOf({ valid_from: "2021-01-26" })), NO_RECORD_CLOCK);
  assert.equal(recordClockLabel(undefined), NO_RECORD_CLOCK);
  assert.equal(recordClockLabel({}), NO_RECORD_CLOCK);
});

test("only a real string field is a record clock", () => {
  // Empty and blank strings are not dates, and a non-string is not repaired into one: a client
  // that coerced 0 or false into a clock would print a date nobody published.
  for (const value of ["", "   ", null, undefined, 0, false, 20210126, {}, ["2021-01-26"]]) {
    assert.equal(recordClockOf({ publication_date: value, observed_from: value }).publicationDate,
      undefined, `${JSON.stringify(value)} was admitted as a publication date`);
    assert.equal(recordClockOf({ publication_date: value, observed_from: value }).observedFrom,
      undefined, `${JSON.stringify(value)} was admitted as an observation`);
  }
  // And a row that is not an object at all yields nothing rather than throwing on the surface.
  for (const row of [null, undefined, "row", 7, ["publication_date"]])
    assert.deepEqual(recordClockOf(row), { publicationDate: undefined, observedFrom: undefined });
});

test("the two fields are never crossed, in either direction", () => {
  const onlyObserved = recordClockOf({ observed_from: "2026-08-14T23:05:14Z" });
  assert.equal(onlyObserved.publicationDate, undefined,
    "the observation instant was copied into the publication date");
  const onlyPublished = recordClockOf({ publication_date: "2021-01-26" });
  assert.equal(onlyPublished.observedFrom, undefined,
    "the publication date was copied into the observation");
  // Nor from the legal clock, which is a different kind of claim entirely.
  const legalOnly = recordClockOf({ valid_from: "2021-01-26", valid_to: "2021-04-23" });
  assert.deepEqual(legalOnly, { publicationDate: undefined, observedFrom: undefined });
});

test("the rail keeps one clock per date and refuses to choose between two", () => {
  const byDate = recordClocksByDate([
    { valid_from: "2020-01-01", publication_date: "2023-07-19", observed_from: "2026-08-14T23:05:14Z" },
    // Same date, same clock: one state seen through two language expressions is not a conflict.
    { valid_from: "2021-01-26", publication_date: "2021-01-26", observed_from: "2026-08-14T23:05:14Z" },
    { valid_from: "2021-01-26", publication_date: "2021-01-26", observed_from: "2026-08-14T23:05:14Z" },
    // Same date, different clocks: the publisher put two states here and the rail collapses
    // dates, so naming either one would assert a resolution nobody made.
    { valid_from: "2024-03-01", publication_date: "2024-02-01", observed_from: "2026-08-14T23:05:14Z" },
    { valid_from: "2024-03-01", publication_date: "2024-04-01", observed_from: "2026-08-14T23:05:14Z" },
    // No legal date: nothing to key a tick by.
    { publication_date: "2025-01-01", observed_from: "2026-08-14T23:05:14Z" },
  ]);
  assert.deepEqual([...byDate.keys()], ["2020-01-01", "2021-01-26", "2024-03-01"]);
  assert.equal(railClockLabel(byDate.get("2020-01-01")),
    "Published 2023-07-19. First observed 2026-08-14T23:05:14Z.");
  assert.equal(railClockLabel(byDate.get("2021-01-26")),
    "Published 2021-01-26. First observed 2026-08-14T23:05:14Z.");
  const contested = byDate.get("2024-03-01");
  assert.equal(contested?.several, true);
  assert.deepEqual(contested?.clock, {}, "a contested date kept one of the two clocks");
  assert.equal(railClockLabel(contested), SEVERAL_STATES_ON_DATE);
  assert.ok(!railClockLabel(contested).includes("2024-02-01")
    && !railClockLabel(contested).includes("2024-04-01"),
    "the rail named one of two publisher states as the record clock for the date");
  // A date the response never carried gets the absence sentence, not a neighbouring date's clock.
  assert.equal(railClockLabel(byDate.get("2022-06-01")), NO_RECORD_CLOCK);
  assert.equal(railClockLabel(undefined), NO_RECORD_CLOCK);
});

test("a third disagreeing state does not reopen a date the rail already contested", () => {
  const byDate = recordClocksByDate([
    { valid_from: "2024-03-01", publication_date: "2024-02-01" },
    { valid_from: "2024-03-01", publication_date: "2024-04-01" },
    { valid_from: "2024-03-01", publication_date: "2024-05-01" },
  ]);
  assert.equal(byDate.get("2024-03-01")?.several, true);
  assert.equal(railClockLabel(byDate.get("2024-03-01")), SEVERAL_STATES_ON_DATE);
});

test("the assistant reading path carries whatever record clock its effect held", () => {
  // The assistant's own provision mapper copies valid_from and valid_to out of the tool result
  // and leaves the two record-clock fields behind, so this seam is where the gap is visible.
  const withoutClock = assistantProvisionLoad({
    provision: {
      subject: { work: "lu-legilux:loi-2006-07-31-n2" },
      valid_from: "2021-01-26", valid_to: "2021-04-23",
      provisions: [{ anchor: "art_l_121-6", text: "..." }],
    },
  });
  assert.equal(withoutClock?.from, "2021-01-26");
  assert.equal(recordClockLabel(withoutClock?.record), NO_RECORD_CLOCK);
  // And it is a pass-through, not a hard-coded blank: the day the mapper copies the fields the
  // reader sees them, with no further change here.
  const withClock = assistantProvisionLoad({
    provision: {
      subject: { work: "lu-legilux:loi-2006-07-31-n2" },
      valid_from: "2021-01-26", valid_to: "2021-04-23",
      publication_date: "2021-01-26", observed_from: "2026-08-14T23:05:14Z",
      provisions: [{ anchor: "art_l_121-6", text: "..." }],
    } as never,
  });
  assert.equal(recordClockLabel(withClock?.record),
    "Published 2021-01-26. First observed 2026-08-14T23:05:14Z.");
});

// ---------------------------------------------------------------------------
// The surfaces themselves
// ---------------------------------------------------------------------------
//
// STRUCTURAL, on the precedent of the Search.tsx guard in limitations.test.ts and the App.tsx
// guard in envelopeStrip.test.ts, because no node test can import a .tsx component and the
// defect this closes lived entirely in .tsx: every dated row on the most-read surfaces stated
// legal time alone, while `publication_date` and `observed_from` sat unread on the wire.
// Comments are stripped first, so a sentence naming the defect is not a reintroduction of it.
const source = (name: string) =>
  readFileSync(new URL(`./${name}`, import.meta.url), "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/^[ \t]*\/\/.*$/gm, "");

test("every governed dated surface renders both clocks", () => {
  const views = source("views.tsx");
  assert.ok(views.includes("recordClockLabel("),
    "the stripper removed the code with the comments, so this proves nothing");

  // The law reading header: the state banner's second line.
  assert.ok(views.includes("data-testid=\"reading-record-clock\""),
    "the law reading header stopped carrying the record clock");
  assert.ok(views.includes("{recordClockLabel(record)}"),
    "the reading header renders something other than the record it was handed");

  // The version rail: the tick in focus in words, and every tick in its accessible name.
  assert.ok(views.includes("data-testid=\"rail-record-clock\""),
    "the version rail stopped carrying the record clock");
  assert.ok(views.includes("railClockLabel(clocks?.get(current))"),
    "the rail's visible clock is no longer the clock of the version on screen");
  assert.ok(views.includes("const clock = railClockLabel(clocks?.get(d));"),
    "the rail's ticks stopped carrying their own record clock");
  assert.ok(/aria-label=\{`\$\{d\}\$\{k === i \? " \(showing\)" : ""\}\. \$\{clock\}`\}/.test(views),
    "a rail tick announces a date with no record clock beside it");

  // In-force rows.
  assert.ok(views.includes("data-testid=\"in-force-record-clock\""),
    "the in-force list stopped carrying the record clock");
  assert.ok(views.includes("{recordClockLabel(recordClockOf(r))}"),
    "an in-force row renders a record clock it did not read off its own row");

  // The assistant's timeline panel.
  assert.ok(views.includes("data-testid=\"timeline-record-clock\""),
    "the timeline panel stopped carrying the record clock");
  assert.ok(views.includes("{recordClockLabel(recordClockOf(row))}"),
    "a timeline row renders a record clock it did not read off its own row");

  // Search hits.
  const search = source("Search.tsx");
  assert.ok(search.includes("data-testid=\"hit-record-clock\""),
    "search hits stopped carrying the record clock");
  assert.ok(search.includes("recordClockLabel(hit.record)"),
    "a search hit renders a record clock that is not its own");
  assert.ok(search.includes("record: recordClockOf(h),"),
    "the hit's record clock is no longer read from the parsed hit");
});

test("no surface manufactures a record clock the response did not carry", () => {
  // ONE READER of the two field names, so a second surface cannot quietly invent a third
  // spelling or a fallback. Everything else goes through recordClockOf.
  for (const name of ["views.tsx", "Search.tsx", "App.tsx", "assistantShell.ts"]) {
    const text = source(name);
    for (const [pattern, message] of [
      [/publication_date\s*(\?\?|\|\|)/, "a publication date falls back to another value"],
      [/(\?\?|\|\|)\s*[\w.?]*publication_date/, "something falls back to the publication date"],
      [/observed_from\s*(\?\?|\|\|)/, "an observation instant falls back to another value"],
      [/(\?\?|\|\|)\s*[\w.?]*observed_from/, "something falls back to the observation instant"],
      [/publicationDate\s*(\?\?|\|\|)/, "a parsed publication date falls back to another value"],
      [/observedFrom\s*(\?\?|\|\|)/, "a parsed observation falls back to another value"],
    ] as [RegExp, string][])
      assert.ok(!pattern.test(text), `${name}: ${message}`);
  }

  // The App reads the wire fields exactly where the parse already handed it the row, and the
  // in-force ambiguity units get none: their record clocks live on the candidate versions
  // inside `choices`, and lifting one onto the ambiguity row would answer the very question
  // the ambiguity exists to leave open.
  const app = source("App.tsx");
  assert.equal(app.split("publication_date: w.publication_date").length - 1, 1,
    "the in-force rows read their publication date somewhere other than the admitted row");
  assert.ok(app.includes("observed_from: w.observed_from"),
    "the in-force rows stopped carrying the observation instant");
  const ambiguity = app.slice(app.indexOf("ambiguityDecision.ambiguous.map"),
    app.indexOf("const pageUnits"));
  assert.ok(ambiguity.length > 0, "the ambiguity mapping could not be read");
  assert.ok(!ambiguity.includes("publication_date") && !ambiguity.includes("observed_from")
    && !ambiguity.includes("recordClockOf"),
    "an ambiguity row was given one candidate's record clock as if it were the date's");
});

test("the reading header is handed the record clock of the state it is showing", () => {
  const app = source("App.tsx");
  // Read off the resolved document, in the same statement that reads its interval, so the two
  // clocks on the banner can never come from different answers.
  assert.ok(app.includes("record: recordClockOf(doc) }"),
    "the reading header no longer reads the record clock off the resolved document");
  assert.ok(app.includes("record={loaded.record}"),
    "the reading header is no longer handed the record clock that was read for it");
});

test("the rail's record clocks cannot outlive the work they were read for", () => {
  const app = source("App.tsx");
  // Keyed by work, and read only while the work still matches. A bare date-keyed map would put
  // one law's publication date on another law's tick whenever the two share a legal date.
  assert.ok(app.includes("setRailClocks({ work: s.work!, byDate: recordClocksByDate(vs) })"),
    "the rail's record clocks are no longer read from the timeline rows that carry the dates");
  assert.ok(app.includes("railClocks?.work === s.work"),
    "the rail can show a record clock read for a different law");
  // Cleared on the same statements that clear the dates, so a stale map cannot survive a work
  // switch or a failed timeline.
  assert.equal(app.split("setRailClocks(undefined)").length - 1, 2,
    "the rail's record clocks are not cleared everywhere its dates are");
  // Narrowing to one article's texts drops them: `article_history` publishes no record clock,
  // and reusing the law's would attach a version's publication date to an article text state
  // the response never dated.
  assert.ok(app.includes("clocks={!narrowed && railClocks?.work === s.work ? railClocks.byDate : undefined}"),
    "an article-history rail borrows the law's record clocks");
});
