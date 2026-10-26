// The EU act's dates as the publisher records them (the plan's item 2.1; R79's E1, R21), read and said: each date a value of
// its own with its role, the publisher's qualifier word for word in its own codes, the roles not held, and both dates where
// the publisher records two entries into force. An answer over an EU index /5 carries none, and the page says none.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { DossierAnswerView, ReadingAnswerView } from "../.react-build/app.mjs";
import { EUROPE_FORCE_DATE_NAMED_ROLES, EUROPE_FORCE_DATE_ROLES, noteCodes, readEuropeForceDates } from "../scripts/europe-force-dates.mjs";
import { europeBundle, europeEnvelope } from "../scripts/europe-bundle-sample.mjs";
import { LIVE_CHROME, entriesOf } from "../scripts/live-chrome.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const FD = "http://publications.europa.eu/resource/authority/fd_335/";
const carrier = (code) => `{${code}|${FD}${encodeURIComponent(code)}}`;

/** The GDPR's three dates as run 16 holds them, in an answer's shape. */
function forceDates(change = () => {}) {
  const row = (role, date, code, note, axiom) => ({
    role,
    date,
    datatype: "http://www.w3.org/2001/XMLSchema#date",
    open_ended: false,
    publisher_code: code,
    publisher_note: note,
    publisher_note_codes: noteCodes(note),
    date_property: "http://publications.europa.eu/ontology/cdm#resource_legal_date_entry-into-force",
    statement: `http://publications.europa.eu/.well-known/genid/lex-force-dates/${axiom}`,
    date_identity_sha256: axiom.charCodeAt(0).toString(16).padStart(2, "0").repeat(32),
  });
  const value = {
    scope: "the act's dates as the publisher records them",
    publisher_work_id: "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
    seed_celex: "32016R0679",
    dates: [
      row("entry_into_force", "2016-05-24", "EV", `${carrier("DATPUB")} +20 ${carrier("V")} ${carrier("ART")} 99`, "a"),
      row("application", "2018-05-25", "MA", `${carrier("V")} ${carrier("ART")} 99`, "b"),
      row("deadline", "2020-05-25", null, `${carrier("AU+TARD")} ${carrier("V")} ${carrier("ART")} 97`, "c"),
    ],
    roles_not_held: ["end_of_validity"],
    conflicts: [],
    role_rule: "entry_into_force is a date on the publisher's entry-into-force property with its code EV",
    note_rule: "publisher_note_codes is the same note with each carrier written as its code",
    not_held_rule: "roles_not_held names the roles for which no date is held",
    conflict_rule: "conflicts names the entry into force when the publisher's statements give it more than one date",
  };
  change(value);
  return value;
}

test("an answer over an EU index /5 carries no force date, and is read as none", () => {
  assert.equal(readEuropeForceDates({ celex: "32016R0679" }, "an answer"), null);
});

test("the act's dates are read by role, with the publisher's code and its qualifier in its own codes", () => {
  const view = readEuropeForceDates({ celex: "32016R0679", force_dates: forceDates() }, "an answer");
  assert.deepEqual(view.dates.map((row) => [row.role, row.date, row.code, row.codes]), [
    ["entry_into_force", "2016-05-24", "EV", "DATPUB +20 V ART 99"],
    ["application", "2018-05-25", "MA", "V ART 99"],
    ["deadline", "2020-05-25", null, "AU+TARD V ART 97"],
  ]);
  assert.deepEqual(view.rolesNotHeld, ["end_of_validity"]);
  assert.equal(noteCodes(`{MA/PART|${FD}MA%2FPART} {V|${FD}V} {ART|${FD}ART} 55 L 4`), "MA/PART V ART 55 L 4");
  assert.equal(noteCodes("Strasbourg"), "Strasbourg");
});

test("a force date that breaks the answer's own rules is thrown, never said", () => {
  // Each case breaks one rule and is refused for that rule, by its message: a case that broke two would pass on the other and
  // mask the one it names (the review of #987: "the code EV given no role" also removed the application, so the reader
  // refused it for the role order and the roles not held, and still did with the EV check taken out).
  const notPinned = (code) => (value) => {
    value.dates.push({ ...value.dates[2], role: "not_pinned", date: "2021-01-01", publisher_code: code, publisher_note: null,
      publisher_note_codes: null, date_identity_sha256: "e".repeat(64) });
  };
  const drift = {
    "an unknown role": [(value) => { value.dates[0].role = "in_force"; }, /which no force date takes/],
    "an entry into force without EV": [(value) => { value.dates[0].publisher_code = "MA"; }, /the code EV gives that role/],
    "an application without MA": [(value) => { value.dates[1].publisher_code = "EV"; }, /the code MA gives that role/],
    "the code EV given no role": [notPinned("EV"), /gives no role to the code EV, which has one/],
    "the code MA given no role": [notPinned("MA"), /gives no role to the code MA, which has one/],
    "codes that are not the note's": [(value) => { value.dates[2].publisher_note_codes = "AU+TARD V ART 98"; }, /not its note's/],
    "an open end that is a date": [(value) => { value.dates[2].open_ended = true; }, /only the publisher's 9999-12-31/],
    "a date that is no publisher date": [(value) => { value.dates[2].date = "2020-05-25T00:00:00"; }, /which is no publisher date/],
    "a role said not held that has a date": [(value) => { value.roles_not_held = ["entry_into_force", "end_of_validity"]; }, /are not held, and its dates leave/],
    "a role not said not held that has none": [(value) => { value.roles_not_held = []; }, /are not held, and its dates leave/],
    "dates out of role order": [(value) => { value.dates.reverse(); }, /the dates are in role order/],
    "a conflict the dates do not hold": [(value) => { value.conflicts = [{ role: "entry_into_force", dates: ["2016-05-24", "2016-05-25"] }]; }, /names 1 conflicts/],
    "another act's dates": [(value) => { value.seed_celex = "32014L0065"; }, /are 32014L0065's dates/],
    "a date said twice": [(value) => { value.dates[1].date_identity_sha256 = value.dates[0].date_identity_sha256; }, /repeats a date/],
  };
  for (const [name, [change, rule]] of Object.entries(drift)) {
    assert.throws(() => readEuropeForceDates({ celex: "32016R0679", force_dates: forceDates(change) }, "an answer"), rule, name);
  }
  // The isolated not-pinned row is read when its code has no role of its own.
  assert.equal(readEuropeForceDates({ celex: "32016R0679", force_dates: forceDates(notPinned("MA/PART")) }, "an answer").dates.at(-1).code, "MA/PART");
  // Two entries into force: both are held, the conflict names both, and the reader reads it.
  const two = forceDates((value) => {
    value.dates.splice(1, 0, { ...value.dates[0], date: "2016-05-25", date_identity_sha256: "d".repeat(64) });
    value.conflicts = [{ role: "entry_into_force", dates: ["2016-05-24", "2016-05-25"] }];
  });
  assert.deepEqual(readEuropeForceDates({ celex: "32016R0679", force_dates: two }, "an answer").conflicts[0].dates, ["2016-05-24", "2016-05-25"]);
});

test("a date in the forms the contract admits is read, the open end in any of them, and an unpinned code keeps its property's role (the review of #987)", () => {
  // xsd:date with a timezone (2016-05-24Z, +02:00, -14:00), as the contract's PublisherDate admits it; the open end with one.
  for (const date of ["2016-05-24Z", "2016-05-24+02:00", "2016-05-24-14:00"]) {
    const view = readEuropeForceDates({ celex: "32016R0679", force_dates: forceDates((value) => { value.dates[0].date = date; }) }, "an answer");
    assert.equal(view.dates[0].date, date, "said exactly as served");
  }
  for (const date of ["2016-05-24+15:00", "2016-05-24Z+01:00", "2016-5-24"]) {
    assert.throws(() => readEuropeForceDates({ celex: "32016R0679", force_dates: forceDates((value) => { value.dates[0].date = date; }) }, "an answer"),
      /which is no publisher date/, date);
  }
  const open = (date) => forceDates((value) => {
    value.dates.splice(2, 0, { ...value.dates[2], role: "end_of_validity", date, open_ended: true, publisher_code: null, publisher_note: null,
      publisher_note_codes: null, date_identity_sha256: "f".repeat(64) });
    value.roles_not_held = [];
  });
  for (const date of ["9999-12-31", "9999-12-31Z"]) {
    assert.equal(readEuropeForceDates({ celex: "32016R0679", force_dates: open(date) }, "an answer").dates[2].openEnded, true, date);
  }
  // A code the table does not pin on the end-of-validity or signature property: the property's role, the code kept.
  for (const role of ["end_of_validity", "signature"]) {
    const view = readEuropeForceDates({ celex: "32016R0679", force_dates: forceDates((value) => {
      value.dates.splice(2, 0, { ...value.dates[2], role, date: "2020-01-01", publisher_code: "PE", date_identity_sha256: "f".repeat(64) });
      value.dates.sort((a, b) => EUROPE_FORCE_DATE_ROLES.indexOf(a.role) - EUROPE_FORCE_DATE_ROLES.indexOf(b.role));
      value.roles_not_held = EUROPE_FORCE_DATE_NAMED_ROLES.filter((one) => !value.dates.some((row) => row.role === one));
    }) }, "an answer");
    assert.equal(view.dates.find((row) => row.role === role).code, "PE", role);
  }
});

test("each force-date entry says its role in its own words, in English and in French, and never 'in force' on an interval (R21)", () => {
  for (const [language, table] of [["en", LIVE_CHROME.en], ["fr", LIVE_CHROME.fr]]) {
    const entries = entriesOf(table).filter(([path]) => path.startsWith("common.europeForceDates."));
    assert.equal(entries.length, 17, `${language}: the section's entries`);
    for (const [path, text] of entries) assertTimeVocabulary(text, `${language} ${path}`);
    assert.deepEqual(Object.keys(table.common.europeForceDates.roles), [...EUROPE_FORCE_DATE_ROLES]);
    assert.deepEqual(Object.keys(table.common.europeForceDates.roleNames), [...EUROPE_FORCE_DATE_NAMED_ROLES]);
  }
  assert.equal(LIVE_CHROME.fr.common.europeForceDates.roles.entry_into_force, "Entrée en vigueur\u00a0: {date}", "the noun of the typed date, which the guard lets through");
});

test("the census's EU dossier says the act's dates by role, and the reading leads with them before its wordings", () => {
  const dossier = census.envelopes.find((entry) => entry.operation === "dossier" && entry.envelope.result?.value.publisher === "eu-eurlex");
  assert.ok(dossier, "the census holds an EU dossier");
  assert.ok(Object.hasOwn(dossier.envelope.result.value, "force_dates"), "over an EU index /6, the census's dossier carries the act's dates");
  const outcome = dossierOutcome({ state: "success", envelope: dossier.envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = renderToStaticMarkup(h(DossierAnswerView, { outcome }));
  assert.match(markup, /Entry into force: 2016-05-24/);
  assert.match(markup, /Application: 2018-05-25/);
  assert.match(markup, /Deadline: 2020-05-25 \(the publisher&#x27;s qualifier, word for word: <code lang="zxx">AU\+TARD V ART 97<\/code>\)/);
  assert.match(markup, /Not held for this act: end of validity\./);
  assert.doesNotMatch(markup, /data-force-dates-not-wording/, "the dossier lists no wording below its dates");

  const bundle = europeBundle((value) => { value.force_dates = forceDates(); });
  const reading = readingOutcome({ state: "success", envelope: europeEnvelope(bundle) });
  assert.equal(reading.state, "success", reading.sentence);
  const page = renderToStaticMarkup(h(ReadingAnswerView, { outcome: reading }));
  assert.ok(page.indexOf("data-force-dates=") < page.indexOf("data-wording="), "the act's dates lead the reading, before its wordings");
  assert.match(page, /None of the dates above is used to choose a wording: each wording below carries its own date\./);
  const none = renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: europeEnvelope() }) }));
  assert.doesNotMatch(none, /data-force-dates/, "a reading over an EU index /5 says no date it does not hold");
});
