// Writes `home-texts-record.mjs`: the six starting texts of the V4 Home (the owner's brief of 2026-11-15, section 5; the
// mockup V1Home), each its versions and its article count read from the live API's `timeline` answer, and a Luxembourg
// text's own title from its `dossier` answer (`recentTitleOf`). An EU act is named by its usual name in the copy tables
// (`home.startTexts`), never by the publisher's long title.
//
//   node scripts/write-home-texts.mjs [--base https://law.soufien.lu] [--today yyyy-mm-dd]
//
// Nine requests, one at a time (the API allows 60 a minute per address). Run it again when the index the site stands over
// changes; until then the Home page asks the six timelines again itself when its coverage answer counts another index.

import { writeFile } from "node:fs/promises";

import { invokedDirectly } from "./invoked-directly.mjs";
import { homeTextOfTimeline, recentTitleOf } from "./live-home.mjs";
import { isCalendarDate, todayInLuxembourg } from "./temporal.mjs";

/** The six starting texts, in the order Home lists them (the owner's brief and mockup). */
export const HOME_TEXT_IDENTIFIERS = Object.freeze([
  "/lu-legilux/loi-1997-07-27-n10",
  "/lu-legilux/code-civil",
  "/lu-legilux/code-travail",
  "32016R0679",
  "32022R2554",
  "32024R1689",
]);

const argument = (name, fallback) => {
  const index = process.argv.indexOf(name);
  return index > 0 ? process.argv[index + 1] : fallback;
};

async function ask(base, operation, parameters) {
  const response = await fetch(`${base}/api/v3/${operation}`, {
    method: "POST",
    headers: { "content-type": "application/json", "user-agent": "lex-web-build" },
    body: JSON.stringify({ operation_id: operation, parameters }),
  });
  const envelope = await response.json();
  if (envelope?.refusal !== null || envelope?.result?.value === undefined) {
    throw new Error(`${operation} ${JSON.stringify(parameters)} was refused: ${JSON.stringify(envelope?.refusal ?? envelope).slice(0, 300)}`);
  }
  return envelope.result.value;
}

/** The EU act's number as the Official Journal writes it, from its CELEX (32016R0679 is 2016/679). */
export function europeActNumber(celex) {
  const match = /^3(\d{4})[RLD](\d{4})$/u.exec(celex);
  return match === null ? null : `${match[1]}/${Number(match[2])}`;
}

async function main() {
  const base = argument("--base", "https://law.soufien.lu");
  const today = argument("--today", todayInLuxembourg());
  if (!isCalendarDate(today)) throw new Error(`${today} is not a calendar date`);
  const texts = [];
  const indexes = {};
  for (const identifier of HOME_TEXT_IDENTIFIERS) {
    const timeline = await ask(base, "timeline", { identifier });
    const row = homeTextOfTimeline(timeline, today);
    if (row === null) throw new Error(`${identifier}: the timeline names no held version`);
    indexes[timeline.publisher] = row.index_sha256;
    const text = { identifier, publisher: timeline.publisher, versions: row.versions, articles: row.articles };
    if (timeline.publisher === "lu-legilux") {
      const title = recentTitleOf(await ask(base, "dossier", { identifier }), identifier.split("/").at(-1));
      if (title === null) throw new Error(`${identifier}: the dossier names no title of the text's own`);
      text.title = title.title;
      text.title_language = title.language;
    } else {
      text.number = europeActNumber(identifier);
    }
    texts.push(text);
    console.log(`${identifier}: ${JSON.stringify(row.versions).slice(0, 160)}`);
  }
  const record = { schema: "lex-v3-home-texts/1", read_on: today, source: base, indexes, texts };
  const source = [
    "// Written by scripts/write-home-texts.mjs from the live API's timeline and dossier answers (the six starting texts of the",
    "// V4 Home). Do not edit: run the script again.",
    "",
    `export const HOME_TEXTS_RECORD = Object.freeze(${JSON.stringify(record, null, 2)});`,
    "",
  ].join("\n");
  await writeFile(new URL("./home-texts-record.mjs", import.meta.url), source, "utf8");
}

if (invokedDirectly(import.meta.url, process.argv[1])) await main();
