// Content /3's wording on the live pages and in the exports (slice S3b): an article whose wording holds quoted wording or
// wording the publisher marks deleted carries its segments, and a note whose reference the publisher wrote without a
// marker is unmarked. Built from the census bundle the real handler sent (`envelope-samples.json`) and the captured answer
// (`answer-samples.json`), with segments added to one article as the platform adds them: its text segments are exactly
// its text, so the digests the platform sent stay the text's. The rules held here:
//  - the reader reads the segments and refuses any that break the platform's rules (text segments other than the text,
//    a quotation not closed or closed by another, an unknown kind), and reads an unmarked note, never a marked one without
//    a marker;
//  - the reading and the export composer show quoted wording between « and », unless the publisher writes the marks in its
//    wording, and deleted wording struck through in a del that says so to a screen reader, in English and in French, with
//    the sentence under the article; the text quoted, cited and digested never holds the deleted wording;
//  - the JSON and CSV exports carry the segments, the PDF quotes the text with its marks and lists the deleted wording
//    apart, never inside the text.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import test from "node:test";
import { build } from "esbuild";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { ExportAnswerView, ReadingAnswerView } from "../.react-build/app.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { readEvidenceBundle } from "../scripts/reading-answer.mjs";
import { CSV_COLUMNS, composeExport, exportCsv, exportJson } from "../scripts/export-build.mjs";
import { exportPdf } from "../scripts/export-pdf.mjs";
import { LIVE_CHROME } from "../scripts/live-chrome.mjs";
import { mergeReadingPage } from "../scripts/reading-pages.mjs";
import { cutIntoPages } from "../scripts/reading-page-samples.mjs";
import { addedMark } from "../scripts/quotation-marks.mjs";

const web = new URL("../", import.meta.url);
const census = JSON.parse(await readFile(new URL("../schemas/v3-platform/envelope-samples.json", web), "utf8"));
const samples = JSON.parse(await readFile(new URL("../schemas/v3-platform/answer-samples.json", web), "utf8"));
const OBSERVED = "2026-09-18T07:00:00.0000000Z";
const DELETED = "ancien libellé supprimé synthétique";
const EN = LIVE_CHROME.en.reading;
const FR = LIVE_CHROME.fr.reading;
const sha256 = (text) => createHash("sha256").update(text, "utf8").digest("hex");

// WinAnsiEncoding's bytes 128 to 159 (PDF 32000-1, Annex D, table D.2); every other byte is Latin-1.
const WIN_ANSI_128 = ["€", null, "‚", "ƒ", "„", "…", "†", "‡", "ˆ", "‰", "Š", "‹", "Œ", null, "Ž", null,
  null, "‘", "’", "“", "”", "•", "–", "—", "˜", "™", "š", "›", "œ", null, "ž", "Ÿ"];
const winAnsi = (text) => Array.from(text, (char) => {
  const code = char.charCodeAt(0);
  return code >= 128 && code <= 159 ? WIN_ANSI_128[code - 128] : char;
}).join("");

/** One article's segments as the platform sends them: a quotation around a run of its text, and deleted wording after it. */
function segmentsFor(text, { marksInWording = false } = {}) {
  const from = text.indexOf(" ", 10);
  const to = text.indexOf(" ", from + 8);
  assert.ok(from > 0 && to > from, "the article's text has words to quote");
  return [
    { kind: "text", text: text.slice(0, from + 1) },
    { kind: "quotation_start", quotation: "embeddedText", marks_in_wording: marksInWording, marker: null },
    { kind: "text", text: text.slice(from + 1, to) },
    { kind: "quotation_end", quotation: "embeddedText", marks_in_wording: marksInWording, marker: null },
    { kind: "deleted_text", text: DELETED },
    { kind: "text", text: text.slice(to) },
  ];
}

/** An answer's first article with segments, and an unmarked note, as the platform composes them. */
function segmented(answer, options) {
  const article = answer.states[0].articles[0];
  article.segments = segmentsFor(article.text, options);
  article.notes = [...article.notes, { marker: null, text: "Note synthétique sans appel.", unmarked: true }];
  answer.segments_rule = "segments rule";
  return article;
}

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === "<varies-per-run>") { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

const capturedAnswer = () => withDigests(structuredClone(samples.sampled.find((sample) => sample.operation === "evidence_bundle").answer));

function envelope(change) {
  const entry = census.envelopes.find((candidate) => candidate.operation === "evidence_bundle" && candidate.scenario.startsWith("the work on its state's date"));
  assert.ok(entry);
  const copy = structuredClone(entry.envelope);
  change(copy.result.value);
  return copy;
}

const props = (outcome) => ({ outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {} });
const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'" };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|#x27);/g, (entity) => ENTITIES[entity]);

/** The blockquote of the article `publisherId` in a render. */
function quoteOf(markup, publisherId) {
  const start = markup.indexOf(`data-article="${publisherId}"`);
  assert.ok(start >= 0, `the render holds ${publisherId}`);
  const open = markup.indexOf("<blockquote", start);
  return markup.slice(open, markup.indexOf("</blockquote>", open) + "</blockquote>".length);
}

test("the reader reads an article's segments and an unmarked note, and refuses segments that break the platform's rules", () => {
  const answer = capturedAnswer();
  const article = segmented(answer);
  const view = readEvidenceBundle(answer);
  const [first, second] = view.states[0].articles;
  assert.deepEqual(first.segments.map((segment) => segment.kind), ["text", "quotation_start", "text", "quotation_end", "deleted_text", "text"]);
  assert.equal(first.holdsDeletedWording, true);
  assert.equal(first.text, article.text, "the text is untouched");
  assert.equal(first.textSha256, article.text_sha256);
  assert.deepEqual({ ...first.notes.at(-1) }, { marker: null, unmarked: true, text: "Note synthétique sans appel." });
  assert.equal(second.segments, null, "an article with none has none");
  assert.equal(second.holdsDeletedWording, false);

  const cases = [
    ["text segments that are not the text", (a) => { a.states[0].articles[0].segments[0].text += "x"; }, /text segments are not its text/],
    ["deleted wording counted as text", (a) => { a.states[0].articles[0].segments[4].kind = "text"; }, /text segments are not its text/],
    ["a quotation left open", (a) => { a.states[0].articles[0].segments.splice(3, 1); }, /leaves a quotation open|text segments are not its text/],
    ["a quotation closed by another", (a) => { a.states[0].articles[0].segments[3].quotation = "embeddedStructure"; }, /closes a quotation it did not open/],
    ["an unknown element", (a) => { a.states[0].articles[0].segments[1].quotation = "mod"; a.states[0].articles[0].segments[3].quotation = "mod"; }, /not a quotation element/],
    ["an unknown kind", (a) => { a.states[0].articles[0].segments[4].kind = "inserted_text"; }, /not a segment of wording/],
    ["empty deleted wording", (a) => { a.states[0].articles[0].segments[4].text = ""; }, /deleted_text with no text/],
    ["no segment at all", (a) => { a.states[0].articles[0].segments = []; }, /carries segments and none of them/],
    ["a marked note with no marker", (a) => { a.states[0].articles[0].notes.at(-1).unmarked = false; }, /says unmarked as false/],
    ["an unmarked note with a marker", (a) => { a.states[0].articles[0].notes.at(-1).marker = "1"; }, /unmarked and carries a marker/],
    ["a note with no marker that does not say unmarked", (a) => { delete a.states[0].articles[0].notes.at(-1).unmarked; }, /marker is not a value this page can print/],
  ];
  for (const [what, change, reason] of cases) {
    const broken = capturedAnswer();
    segmented(broken);
    change(broken);
    assert.throws(() => readEvidenceBundle(broken), reason, what);
  }
});

test("the reading and the export composer quote the marks and strike the deleted wording, with what a screen reader hears", () => {
  const value = envelope((answer) => segmented(answer));
  const article = value.result.value.states[0].articles[0];
  const outcome = readingOutcome({ state: "success", envelope: value });
  for (const markup of [renderToStaticMarkup(h(ReadingAnswerView, { outcome })), renderToStaticMarkup(h(ExportAnswerView, props(outcome)))]) {
    const quote = quoteOf(markup, article.publisher_id);
    assert.match(quote, /^<blockquote lang="fr" data-segmented="">/);
    const segments = segmentsFor(article.text);
    assert.ok(decode(quote).includes(`${segments[0].text}</span><span data-quotation-mark="open">« </span><span>${segments[2].text}</span><span data-quotation-mark="close"> » </span>`),
      "the quoted words between « and », the publisher's own words");
    assert.ok(decode(quote).includes(`<del data-deleted-wording=""><span class="visually-hidden" lang="en"> ${EN.deletedWordingStart} </span>${DELETED}<span class="visually-hidden" lang="en"> ${EN.deletedWordingEnd} </span></del>`),
      "the deleted wording struck through, said to a screen reader at its start and end");
    // What the text says when the marks and the deleted wording are taken out is the text quoted and digested.
    const inForce = decode(quote.replace(/<del[\s\S]*?<\/del>/g, "").replace(/<span data-quotation-mark="(?:open|close)">[^<]*<\/span>/g, "").replace(/<[^>]+>/g, ""));
    assert.equal(inForce, article.text);
    assert.equal(sha256(inForce), article.text_sha256);
  }
  const reading = renderToStaticMarkup(h(ReadingAnswerView, { outcome }));
  assert.ok(decode(reading).includes(`<p data-deleted-wording-note="">${EN.deletedWordingNote}</p>`));
  const composer = renderToStaticMarkup(h(ExportAnswerView, props(outcome)));
  assert.equal((composer.match(/data-deleted-wording-note/g) ?? []).length, 1, "the export composer says it too, under the struck words");
  assert.ok(decode(reading).includes(`<li data-unmarked=""><span>[${EN.unmarkedNote}]</span> <span lang="fr">Note synthétique sans appel.</span></li>`));
  assert.equal((reading.match(/data-deleted-wording-note/g) ?? []).length, 1, "the sentence stands under the one article that holds deleted wording");

  // The publisher writes its own marks in the wording: none are added.
  const own = readingOutcome({ state: "success", envelope: envelope((answer) => segmented(answer, { marksInWording: true })) });
  assert.doesNotMatch(quoteOf(renderToStaticMarkup(h(ReadingAnswerView, { outcome: own })), article.publisher_id), /data-quotation-mark/);
});

test("the French pages say the deleted wording and the unmarked note in the reviewed French", async () => {
  const out = fileURLToPath(new URL(".react-build/app-fr-content3-test.mjs", web));
  await build({
    stdin: { contents: "export * from './app/index.jsx';", resolveDir: fileURLToPath(web), sourcefile: "content3-french-test.jsx", loader: "jsx" },
    outfile: out,
    bundle: true,
    format: "esm",
    platform: "node",
    jsx: "automatic",
    packages: "external",
    logLevel: "silent",
    define: { __LEX_CHROME_LOCALE__: JSON.stringify("fr") },
  });
  const french = await import(`${pathToFileURL(out).href}?${Date.now()}`);
  const frenchLive = await import(`${new URL("scripts/live-reading.mjs", web).href}`);
  const value = envelope((answer) => segmented(answer));
  const markup = decode(renderToStaticMarkup(h(french.ReadingAnswerView, { outcome: frenchLive.readingOutcome({ state: "success", envelope: value }) })));
  assert.ok(markup.includes(`<span class="visually-hidden" lang="fr"> ${FR.deletedWordingStart} </span>${DELETED}<span class="visually-hidden" lang="fr"> ${FR.deletedWordingEnd} </span>`));
  assert.ok(markup.includes(`<p data-deleted-wording-note="">${FR.deletedWordingNote}</p>`));
  assert.ok(markup.includes(`<span>[${FR.unmarkedNote}]</span>`));
  for (const key of ["deletedWordingStart", "deletedWordingEnd", "deletedWordingNote", "unmarkedNote"]) {
    assert.notEqual(FR[key], EN[key], `${key} is said in French`);
  }
});

test("the exports carry the segments, and the deleted wording is never the text in force", async () => {
  const answer = capturedAnswer();
  const article = segmented(answer);
  const view = readEvidenceBundle(answer);
  const [state] = view.states;
  const model = composeExport({ view, pinned: state.articles.slice(0, 2).map((entry) => ({ stateSha256: state.stateSha256, publisherId: entry.publisherId })), observedAt: OBSERVED });

  const json = JSON.parse(exportJson(model));
  const [first, second] = json.items;
  assert.equal(first.text, article.text);
  assert.ok(!first.text.includes(DELETED));
  assert.deepEqual(first.segments, article.segments.map((segment) => (segment.kind === "text" || segment.kind === "deleted_text"
    ? { kind: segment.kind, text: segment.text }
    : { kind: segment.kind, quotation: segment.quotation, marks_in_text: segment.marks_in_wording, marker: segment.marker })));
  assert.deepEqual(first.notes.at(-1), { marker: null, text: "Note synthétique sans appel.", unmarked: true });
  assert.ok(!Object.hasOwn(second, "segments"), "an article with none carries none");
  assert.ok(!exportJson(model).includes("wording"), "the Luxembourg export borrows no EU word");

  const rows = exportCsv(model).split("\r\n").filter((line) => line.length > 0);
  assert.ok(rows[0].split(",").includes("segments"));
  // Segments keeps its place, after every column before it; the date's role is appended after it (the review of #987), and the
  // rights disclosure after that (a single-channel admission's, the exports' copy of the L5 sentence).
  assert.deepEqual(CSV_COLUMNS.slice(-3), ["segments", "date_role", "rights_disclosure"], "every column before the date's role keeps its place");
  assert.deepEqual(CSV_COLUMNS.slice(-8, -3), ["observed_at", "rights_rule", "corpus_sha256", "index_sha256", "registry_sha256"]);
  const csv = exportCsv(model);
  assert.ok(csv.includes(DELETED), "the CSV carries the deleted wording, in its segments");
  assert.equal(csv.split(DELETED).length - 1, 1, "once, in the segments cell, never in the text cell");

  const pdf = Buffer.from(exportPdf(model)).toString("latin1");
  const runs = [...pdf.matchAll(/\(((?:\\[\s\S]|[^\\)])*)\) Tj/g)].map((match) => winAnsi(match[1].replace(/\\([0-7]{1,3}|[\s\S])/g, (_, escape) =>
    /^[0-7]+$/.test(escape) ? String.fromCharCode(parseInt(escape, 8)) : escape)));
  const shown = runs.join(" ");
  // The text line exactly (the review of #981, F5): the text segments with the marks between them, the deleted wording
  // left out, each run of the wrapped paragraph as it was set.
  const parts = segmentsFor(article.text);
  const expected = `${parts[0].text}« ${parts[2].text} »${parts[5].text}`;
  const from = runs.findIndex((run) => run.length > 0 && expected.startsWith(run));
  const to = runs.findIndex((run) => run.startsWith("The quotation marks around quoted wording"));
  assert.ok(from >= 0 && to > from, "the text's runs, then the marks' line");
  assert.equal(runs.slice(from, to).join(""), expected);
  assert.ok(shown.includes("Wording the publisher marks deleted, in order; none of it is part of the text above:"));
  assert.ok(shown.includes(DELETED.slice(0, 12)), "the deleted wording is listed");
  assert.ok(shown.includes("« "), "the quoted wording is set between its marks");
  assert.ok(shown.includes("[no marker] Note synthétique sans appel."));
  const textAt = shown.indexOf(segmentsFor(article.text)[0].text.trim().slice(0, 20));
  const listAt = shown.indexOf("Wording the publisher marks deleted");
  assert.ok(textAt >= 0 && listAt > textAt, "the deleted wording follows the text, under its label");
  assert.ok(!shown.slice(textAt, listAt).includes(DELETED.slice(0, 12)), "and is never inside the text");
});

test("a later page's segments travel through the pages put together and into the export", () => {
  const answer = capturedAnswer();
  const article = answer.states[0].articles[30];
  article.segments = segmentsFor(article.text);
  answer.segments_rule = "segments rule";
  const [one, two] = cutIntoPages(answer, 25);
  const merged = mergeReadingPage(readEvidenceBundle(one), readEvidenceBundle(two));
  const [state] = merged.states;
  const read = state.articles.find((entry) => entry.publisherId === article.publisher_id);
  assert.equal(read.holdsDeletedWording, true, "the article on the second page keeps its segments");
  const model = composeExport({ view: merged, pinned: [{ stateSha256: state.stateSha256, publisherId: article.publisher_id }], observedAt: OBSERVED });
  const [item] = JSON.parse(exportJson(model)).items;
  assert.equal(item.text, article.text);
  assert.deepEqual(item.segments.map((segment) => segment.kind), ["text", "quotation_start", "text", "quotation_end", "deleted_text", "text"]);
});

test("an added mark stands apart from the words around it only where the wording has no space", () => {
  const at = (before, after, kind = "quotation_start") => {
    const quote = { kind, quotation: "embeddedStructure", marksInWording: false, marker: null };
    return addedMark([{ kind: "text", text: before }, quote, { kind: "text", text: after }], 1);
  };
  assert.equal(at("comme suit :", "La permission"), " « ", "a space before « after a colon");
  assert.equal(at("comme suit : ", "La permission"), "« ", "none where the wording has one");
  assert.equal(at("(", "texte"), "« ", "none after an opening bracket");
  assert.equal(at("texte", "suivant", "quotation_end"), " » ", "a space after » before a word");
  assert.equal(at("texte", ".", "quotation_end"), " »", "none before a full stop");
  const nested = [
    { kind: "text", text: "suit :" },
    { kind: "quotation_start", quotation: "embeddedStructure", marksInWording: false, marker: null },
    { kind: "quotation_start", quotation: "embeddedText", marksInWording: false, marker: null },
    { kind: "text", text: "x" },
  ];
  assert.equal(addedMark(nested, 2), "« ", "an inner mark follows the outer one's own space");
  assert.equal(addedMark([{ kind: "text", text: "a" }, { kind: "quotation_start", quotation: "embeddedText", marksInWording: false, marker: "„" }], 1), " „",
    "the publisher's own mark when it gives one");
});

test("no mark is added where the publisher's wording already holds one (the review of #981, F1)", async () => {
  const boundary = (kind, quotation = "embeddedText") => ({ kind, quotation, marksInWording: false, marker: null });
  const text = (value) => ({ kind: "text", text: value });
  // rgd/2020/09/02/a743/jo art_36's shape: the quoted wording opens and closes with its own marks.
  const own = [text("sont remplacés par "), boundary("quotation_start"), text("« moyen 1 » et « moyen 2 »"), boundary("quotation_end"), text(".")];
  assert.equal(addedMark(own, 1), "");
  assert.equal(addedMark(own, 3), "");
  // rgd/2017/08/21/a803/jo art_5's shape: the marks stand just outside the quoted wording.
  const outside = [text("les mots « "), boundary("quotation_start"), text("du premier trimestre"), boundary("quotation_end"), text(" »."), ];
  assert.equal(addedMark(outside, 1), "");
  assert.equal(addedMark(outside, 3), "");
  // Only one side marked: the other side still gets its mark.
  const left = [text("les mots « "), boundary("quotation_start"), text("du premier trimestre"), boundary("quotation_end"), text(" sont")];
  assert.equal(addedMark(left, 1), "");
  assert.equal(addedMark(left, 3), " »");
  // A quotation that only begins with a mark is not one that holds its marks.
  const begins = [text("par "), boundary("quotation_start"), text("« moyen 1 » et moyen 2"), boundary("quotation_end"), text(".")];
  assert.equal(addedMark(begins, 1), "« ");
  assert.equal(addedMark(begins, 3), " »");
  // Nested: the outer structure's marks stay when only the inner quotation holds its own.
  const nested = [text("suit :"), boundary("quotation_start", "embeddedStructure"), text("Le "), boundary("quotation_start"), text("« plan »"),
    boundary("quotation_end"), text(" est"), boundary("quotation_end", "embeddedStructure")];
  assert.equal(addedMark(nested, 1), " « ");
  assert.equal(addedMark(nested, 3), "");
  assert.equal(addedMark(nested, 5), "");
  assert.equal(addedMark(nested, 7), " »");

  // Through the page: an article in a743's shape shows no added mark, and its text is untouched.
  const value = envelope((answer) => {
    const article = answer.states[0].articles[0];
    const at = article.text.indexOf(" ", 10);
    article.text = `${article.text.slice(0, at + 1)}« moyen 1 » et « moyen 2 »${article.text.slice(at)}`;
    const bytes = new TextEncoder().encode(article.text);
    article.text_byte_length = bytes.length;
    article.text_sha256 = sha256(article.text);
    article.segments = [
      { kind: "text", text: article.text.slice(0, at + 1) },
      { kind: "quotation_start", quotation: "embeddedText", marks_in_wording: false, marker: null },
      { kind: "text", text: "« moyen 1 » et « moyen 2 »" },
      { kind: "quotation_end", quotation: "embeddedText", marks_in_wording: false, marker: null },
      { kind: "text", text: article.text.slice(at + 1 + "« moyen 1 » et « moyen 2 »".length) },
    ];
    answer.segments_rule = "segments rule";
  });
  const article = value.result.value.states[0].articles[0];
  const quote = quoteOf(renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: value }) })), article.publisher_id);
  assert.doesNotMatch(quote, /data-quotation-mark/, "no mark doubles the publisher's own");
  assert.equal(decode(quote.replace(/<[^>]+>/g, "")), article.text);
});
