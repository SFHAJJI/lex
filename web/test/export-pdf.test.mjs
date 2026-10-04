// The PDF export, read back as a PDF reader would read it.
//
// The model comes from the evidence bundle the real handler sent (`schemas/v3-platform/answer-samples.json`,
// operation `evidence_bundle`), composed by `composeExport` as the JSON and CSV exports are. The file
// is parsed here on its own terms, not through the writer: its header, its cross-reference table (each
// offset must land on its object), its trailer, and each page's text runs, decoded from WinAnsi with this
// file's own table (Node's `windows-1252` decoder reads bytes 128 to 159 as Latin-1 controls, so it
// cannot be the check). The launch contract's line, "preserve citations, rights,
// watermarks and exclusions", is then checked on what a reader of the pages would see. An EU export
// is composed from the hand-built EU bundle the web tests share (`scripts/europe-bundle-sample.mjs`)
// and read back the same way, Decision 95's acknowledgement in place of a rights disposition and its
// wording dates never said as Luxembourg dates.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { EUROPE_TEXT_ACKNOWLEDGEMENT, readEuropeEvidenceBundle, readEvidenceBundle } from "../scripts/reading-answer.mjs";
import { composeEuropeExport, composeExport, EXPORT_WATERMARK } from "../scripts/export-build.mjs";
import { PDF_SUBSTITUTION_NOTE, PDF_TEXT_WIDTH, exportPdf, pdfRefusal, pdfTextWidth } from "../scripts/export-pdf.mjs";
import { EUROPE_SAMPLE, europeBundle, sha256 } from "../scripts/europe-bundle-sample.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const OBSERVED = "2026-09-18T07:00:00.0000000Z";

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === "<varies-per-run>") { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

async function answer() {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  return withDigests(parsed.sampled.find((sample) => sample.operation === "evidence_bundle").answer);
}

async function modelOf(change = () => {}, pick = (state) => state.articles.slice(0, 3)) {
  const raw = await answer();
  change(raw);
  const view = readEvidenceBundle(raw);
  const [state] = view.states;
  const pinned = pick(state).map((article) => ({ stateSha256: state.stateSha256, publisherId: article.publisherId }));
  return { view, state, model: composeExport({ view, pinned, observedAt: OBSERVED }) };
}

// WinAnsiEncoding's bytes 128 to 159 (PDF 32000-1, Annex D, table D.2); every other byte is Latin-1.
const WIN_ANSI_128 = ["€", null, "‚", "ƒ", "„", "…", "†", "‡", "ˆ", "‰", "Š", "‹", "Œ", null, "Ž", null,
  null, "‘", "’", "“", "”", "•", "–", "—", "˜", "™", "š", "›", "œ", null, "ž", "Ÿ"];
const winAnsi = (bytes) => Array.from(bytes, (byte) => {
  if (byte < 128 || byte > 159) return String.fromCharCode(byte);
  assert.ok(WIN_ANSI_128[byte - 128] !== null, `byte ${byte} is not a WinAnsi character`);
  return WIN_ANSI_128[byte - 128];
}).join("");

/** The bytes a PDF literal string holds, from its source between the parentheses. */
function literalBytes(source) {
  const bytes = [];
  for (let index = 0; index < source.length; index += 1) {
    const char = source[index];
    if (char !== "\\") { bytes.push(char.charCodeAt(0)); continue; }
    const next = source[index + 1];
    if (/[0-7]/.test(next)) {
      const digits = source.slice(index + 1, index + 4).match(/^[0-7]{1,3}/)[0];
      bytes.push(parseInt(digits, 8));
      index += digits.length;
    } else {
      bytes.push(next.charCodeAt(0));
      index += 1;
    }
  }
  return Uint8Array.from(bytes);
}

/** The file read as a reader reads it: structure checked, and each page's text runs in order. */
function readPdf(bytes) {
  const file = Array.from(bytes, (byte) => String.fromCharCode(byte)).join("");
  assert.ok(file.startsWith("%PDF-1.4\n"), "a PDF 1.4 header");
  assert.ok(file.endsWith("%%EOF\n"), "the end-of-file marker");
  const startxref = Number(file.match(/startxref\n(\d+)\n%%EOF\n$/)[1]);
  assert.ok(file.startsWith("xref\n", startxref), "startxref points at the cross-reference table");
  const [, first, count] = file.slice(startxref).match(/^xref\n(\d+) (\d+)\n/);
  assert.equal(Number(first), 0);
  const entries = file.slice(startxref).split("\n").slice(2, 2 + Number(count));
  assert.equal(entries[0], "0000000000 65535 f ");
  const objects = new Map();
  entries.slice(1).forEach((entry, index) => {
    assert.match(entry, /^\d{10} 00000 n $/, "each entry is twenty bytes");
    const offset = Number(entry.slice(0, 10));
    const header = `${index + 1} 0 obj\n`;
    assert.ok(file.startsWith(header, offset), `object ${index + 1} is where the table says`);
    objects.set(index + 1, file.slice(offset + header.length, file.indexOf("\nendobj\n", offset)));
  });
  const trailer = file.slice(file.indexOf("trailer\n", startxref));
  assert.match(trailer, new RegExp(`/Size ${count} /Root 1 0 R`));
  const pagesRef = Number(objects.get(1).match(/\/Pages (\d+) 0 R/)[1]);
  const kids = [...objects.get(pagesRef).match(/\/Kids \[([^\]]*)\]/)[1].matchAll(/(\d+) 0 R/g)].map((match) => Number(match[1]));
  assert.match(objects.get(pagesRef), new RegExp(`/Count ${kids.length}`));
  return kids.map((kid) => {
    const contents = Number(objects.get(kid).match(/\/Contents (\d+) 0 R/)[1]);
    const body = objects.get(contents);
    const [, length, stream] = body.match(/^<< \/Length (\d+) >>\nstream\n([\s\S]*)\nendstream$/);
    assert.equal(stream.length, Number(length), "each stream's length is its byte length");
    return [...stream.matchAll(/BT \/(F\d) ([\d.]+) Tf 1 0 0 1 ([\d.]+) ([\d.]+) Tm \(((?:\\[\s\S]|[^\\)])*)\) Tj ET/g)].map((match) => {
      const raw = literalBytes(match[5]);
      return { font: match[1], size: Number(match[2]), x: Number(match[3]), y: Number(match[4]), raw, text: winAnsi(raw) };
    });
  });
}

const normalised = (text) => text.replace(/\s+/g, " ").trim();
/** The body runs of every page: without the watermark at its head and the page number at its foot. */
const bodyOf = (pages) => pages.flatMap((runs) => runs.slice(1, -1));

test("every page carries the watermark and its number, and the pages say what was asked, the rights and the digests", async () => {
  const { model } = await modelOf();
  const pages = readPdf(exportPdf(model));
  pages.forEach((runs, index) => {
    assert.equal(runs[0].text, EXPORT_WATERMARK, `page ${index + 1} opens with the watermark`);
    assert.equal(runs.at(-1).text, `Page ${index + 1} of ${pages.length}. ${model.identifier}, read on ${model.date}.`);
  });
  const all = pages.flat().map((run) => run.text);
  const prose = normalised(all.join(" "));
  assert.ok(prose.includes(`Asked: ${model.identifier}, read on ${model.date}. The answering snapshot was observed at ${OBSERVED}.`));
  const rule = `${model.rightsRule.charAt(0).toUpperCase()}${model.rightsRule.slice(1)}`;
  assert.ok(prose.includes(normalised(`Text served under ${model.rightsDisposition}. ${rule}`)), "the rights and the platform's rule, as a sentence");
  assert.ok(prose.includes("3 articles pinned: 3 exported with text, 0 excluded."));
  for (const digest of Object.values(model.verifiedBy)) assert.ok(all.some((text) => text.endsWith(digest)), `the digest ${digest}`);
});

test("each item carries its citation, digests, official source, rights and its whole text", async () => {
  const { model } = await modelOf();
  const runs = bodyOf(readPdf(exportPdf(model)));
  const mono = runs.filter((run) => run.font === "F2").map((run) => run.text).join("");
  const prose = normalised(runs.filter((run) => run.font === "F1").map((run) => run.text).join(" "));
  for (const item of model.items) {
    for (const value of [item.citation, item.statePermalink, item.textSha256, item.bodySha256, item.officialSource]) {
      assert.ok(mono.includes(value), `${item.publisherId}: ${value} is set whole (a monospaced value may wrap, never lose a character)`);
    }
    assert.ok(prose.includes(`${item.publisherId} (${item.language}, applying from ${item.appliesFrom})`));
    assert.ok(prose.includes(`Rights: ${item.rightsDisposition}.`));
    assert.ok(prose.includes(normalised(item.text)), `${item.publisherId}: the text, every word of it`);
  }
});

test("an exclusion is set with its reason, and every line stays inside the text width", async () => {
  const { model } = await modelOf((raw) => {
    const state = raw.states[0];
    const [moved] = state.articles.splice(0, 1);
    if (moved.validity_conflict) state.validity_conflict_count -= 1;
    state.articles_without_text = [{ article_identity_sha256: moved.article_identity_sha256, publisher_id: moved.publisher_id, reason: "no_text_tokens" }];
  }, (state) => [state.articlesWithoutText[0], state.articles[0]]);
  const runs = readPdf(exportPdf(model)).flat();
  const prose = normalised(runs.map((run) => run.text).join(" "));
  assert.ok(prose.includes(`${model.excluded[0].publisherId} (${model.excluded[0].language}, applying from ${model.excluded[0].appliesFrom}): excluded, no_text_tokens.`));
  assert.ok(prose.includes("1 exported with text, 1 excluded"));
  const mono = runs.filter((run) => run.font === "F2").map((run) => run.text).join("");
  assert.ok(mono.includes(model.excluded[0].citation), "an excluded article keeps its citation in the PDF too");
  for (const run of runs) {
    // What a line shows: its trailing spaces are kept (the text is kept whole) and take no room.
    const shown = [...run.raw];
    while (shown.at(-1) === 0x20) shown.pop();
    const width = pdfTextWidth(shown, run.font === "F2" ? "mono" : "sans", run.size);
    assert.ok(run.x + width <= 56 + PDF_TEXT_WIDTH + 0.01, `"${run.text.slice(0, 40)}" fits (${width.toFixed(1)} pt)`);
  }
});

test("an item's text is set whole: its lines joined with nothing between them are the text, every space kept", async () => {
  const { createHash } = await import("node:crypto");
  // Double spaces, a leading and a trailing space, a run of spaces at a likely break, and a word longer
  // than a line: each must survive into the PDF (review of #790: "A  B" was set as "A B").
  const text = ` Art. 1.  Le  texte   garde ${"chaque ".repeat(18)}espace,  y compris   ${"x".repeat(140)}  et à la fin. `;
  const { model } = await modelOf((raw) => {
    const article = raw.states[0].articles[0];
    article.text = text;
    article.text_byte_length = Buffer.byteLength(text, "utf8");
    article.text_sha256 = createHash("sha256").update(text, "utf8").digest("hex");
  }, (state) => [state.articles[0]]);
  const body = bodyOf(readPdf(exportPdf(model)));
  const after = body.findIndex((run) => run.text.startsWith("Rights: "));
  const lines = body.slice(after + 1).filter((run) => run.font === "F1" && run.size === 10);
  assert.ok(lines.length > 2, `the text wraps over ${lines.length} lines`);
  assert.equal(lines.map((run) => run.text).join(""), text, "not normalised: the very text the digest vouches for");
});

test("many items run over pages, each page within its margins, and the same model gives the same bytes", async () => {
  const { model } = await modelOf(() => {}, (state) => state.articles);
  const bytes = exportPdf(model);
  assert.deepEqual(exportPdf(model), bytes, "no time of making, no randomness");
  const pages = readPdf(bytes);
  assert.ok(pages.length > 1, `${model.items.length} articles take ${pages.length} pages`);
  for (const runs of pages) {
    const body = runs.slice(1, -1);
    for (const run of body) assert.ok(run.y >= 64 && run.y <= 841.89 - 64, `a body line at ${run.y} is inside the margins`);
    const last = body.at(-1);
    assert.ok(!(last.size === 12 && last.font === "F1"), `no page ends on a heading ("${last.text}")`);
  }
  const prose = normalised(bodyOf(pages).map((run) => run.text).join(" "));
  for (const item of model.items) assert.ok(prose.includes(normalised(item.text)), `${item.publisherId} is not lost across a page`);
});

test("typographic spaces are set plainly and said; a character the fonts cannot set refuses the PDF, naming it", async () => {
  const setText = (text) => (raw) => {
    const article = raw.states[0].articles[0];
    article.text = text;
    article.text_byte_length = Buffer.byteLength(text, "utf8");
    article.text_sha256 = (awaitHash)(text);
  };
  const { createHash } = await import("node:crypto");
  const awaitHash = (text) => createHash("sha256").update(text, "utf8").digest("hex");

  const { model: spaced } = await modelOf(setText("Article 1 : « ceci » – l’été…"), (state) => [state.articles[0]]);
  assert.equal(pdfRefusal(spaced), null);
  const prose = normalised(bodyOf(readPdf(exportPdf(spaced))).map((run) => run.text).join(" "));
  assert.ok(prose.includes("Article 1 : « ceci » – l’été…"), "Latin-1 and WinAnsi's typographic characters are set as themselves");
  assert.ok(prose.includes(normalised(PDF_SUBSTITUTION_NOTE)));

  const { model: plain } = await modelOf(() => {}, (state) => [state.articles[0]]);
  assert.ok(!normalised(readPdf(exportPdf(plain)).flat().map((run) => run.text).join(" ")).includes("Typographic spaces"), "said only when it happened");

  const { model: foreign } = await modelOf(setText("Art. 1 漢 Δ"), (state) => [state.articles[0]]);
  assert.equal(pdfRefusal(foreign), "its text holds characters the standard PDF fonts cannot set (U+6F22, U+0394)");
  assert.throws(() => exportPdf(foreign), /cannot be set as a PDF: its text holds characters the standard PDF fonts cannot set \(U\+6F22, U\+0394\)/);
});

/**
 * The EU sample's export, with `change` applied to its bundle, of the articles `pick` pins in each wording (every one,
 * quoted or held without text, by default), and the bundle it was read from.
 */
function europeModelOf(change = () => {}, pick = (wording) => [...wording.articles, ...wording.articlesWithoutText]) {
  const sent = europeBundle(change);
  const view = readEuropeEvidenceBundle(sent);
  const pinned = view.wordings.flatMap((wording) => pick(wording).map((article) => ({ wordingSha256: wording.wordingSha256, publisherId: article.publisherId })));
  return { sent, model: composeEuropeExport({ view, pinned, observedAt: OBSERVED, registrySha256: EUROPE_SAMPLE.registrySha256 }) };
}

/** The sample's wording with `count` more quoted articles, each long enough that together they run over pages. */
const moreArticles = (count) => (bundle) => {
  const [wording] = bundle.wordings;
  for (let index = 3; index < 3 + count; index += 1) {
    const id = String(index).padStart(3, "0");
    const text = `Article ${id}. ${"The controller shall implement appropriate technical and organisational measures. ".repeat(6).trim()}`;
    wording.articles.push({
      ...structuredClone(wording.articles[0]), article_identity_sha256: sha256(`identity ${id}`), publisher_id: id, heading: `Heading ${id}`,
      text, text_sha256: sha256(text), text_byte_length: Buffer.byteLength(text, "utf8"), article_permalink: `${wording.permalink}#${id}`,
    });
  }
};

/** The sample's wording as a consolidated one, dated 2024-01-01 and read on 2025-03-01 (the EU time view). */
function consolidated(bundle) {
  const sha = "7".repeat(64);
  const permalink = `/eu-eurlex/${EUROPE_SAMPLE.celex}/eng/2024-01-01--${sha}`;
  Object.assign(bundle, { requested_date: "2025-03-01", consolidations_held: true });
  Object.assign(bundle.wordings[0], {
    kind: "consolidated_version", wording_date: "2024-01-01", next_date: null, wording_sha256: sha, permalink,
    stable_coordinate: `/eu-eurlex/${EUROPE_SAMPLE.celex}/eng/2024-01-01`,
  });
  for (const article of bundle.wordings[0].articles) article.article_permalink = `${permalink}#${article.publisher_id}`;
}

/** The body runs of every page of an EU export: without the watermark at its head, nor the acknowledgement and number at its foot. */
const europeBodyOf = (pages) => pages.flatMap((runs) => runs.slice(1, -2));

const capitalised = (text) => `${text.charAt(0).toUpperCase()}${text.slice(1)}`;

test("every page of an EU export carries the watermark, Decision 95's acknowledgement and its number, and the first says what the text is served with", () => {
  const { model } = europeModelOf(moreArticles(30));
  const bytes = exportPdf(model);
  assert.deepEqual(exportPdf(model), bytes, "the same model gives the same bytes");
  const pages = readPdf(bytes);
  assert.ok(pages.length > 1, `${model.items.length} articles take ${pages.length} pages`);
  pages.forEach((runs, index) => {
    assert.equal(runs[0].text, EXPORT_WATERMARK, `page ${index + 1} opens with the watermark`);
    assert.equal(runs.at(-2).text, EUROPE_TEXT_ACKNOWLEDGEMENT, `page ${index + 1} carries the acknowledgement`);
    assert.equal(runs.at(-1).text, `Page ${index + 1} of ${pages.length}. ${model.identifier}, read on ${model.date}.`);
    for (const run of runs.slice(1, -2)) assert.ok(run.y >= 64 && run.y <= 841.89 - 64, `a body line at ${run.y} is inside the margins`);
    assert.ok(runs.at(-2).y > runs.at(-1).y && runs.at(-2).y < 64, "the acknowledgement stands below the body, above the number");
  });
  const first = normalised(pages[0].map((run) => run.text).join(" "));
  assert.ok(first.includes(`Asked: ${model.identifier}, read on ${model.date}. The answering snapshot was observed at ${OBSERVED}.`));
  assert.ok(first.includes(normalised(model.authenticity)), "the authenticity statement");
  assert.ok(first.includes(normalised(`${capitalised(model.rightsRule)}.`)), "the platform's rights rule, as a sentence");
  assert.ok(first.includes(normalised(`${capitalised(model.wordingDateSemantics)}.`)), "and what a wording date is");
  assert.ok(first.includes(`${model.items.length + 1} articles pinned: ${model.items.length} exported with text, 1 excluded.`));
  for (const digest of Object.values(model.verifiedBy)) assert.ok(pages[0].some((run) => run.text.endsWith(digest)), `the digest ${digest}`);
  const prose = normalised(europeBodyOf(pages).map((run) => run.text).join(" "));
  for (const item of model.items) assert.ok(prose.includes(normalised(item.text)), `${item.publisherId} is not lost across a page`);
});

test("each EU item is headed by its wording's kind and date, and carries its citation, wording permalink, digests, official source, acknowledgement and text", () => {
  const { model } = europeModelOf();
  const runs = europeBodyOf(readPdf(exportPdf(model)));
  const mono = runs.filter((run) => run.font === "F2").map((run) => run.text).join("");
  const prose = normalised(runs.filter((run) => run.font === "F1").map((run) => run.text).join(" "));
  for (const item of model.items) {
    for (const value of [item.citation, item.wordingPermalink, item.textSha256, item.bodySha256, item.officialSource]) {
      assert.ok(mono.includes(value), `${item.publisherId}: ${value} is set whole`);
    }
    assert.ok(prose.includes(`${item.publisherId} ${item.heading} (eng, original wording of 2016-04-27)`), `${item.publisherId}'s heading`);
    assert.ok(prose.includes(normalised(item.text)), `${item.publisherId}: the text, every word of it`);
  }
  assert.equal(runs.filter((run) => run.text === `Acknowledgement: ${EUROPE_TEXT_ACKNOWLEDGEMENT}`).length, model.items.length, "each item carries the acknowledgement");
  assert.equal(prose.split(normalised(`Authenticity: ${model.authenticity}`)).length - 1, model.items.length,
    "and the authenticity statement, on any page it falls (review of #920)");
  assert.ok(!prose.includes("agreed_same_run_cc_by") && !prose.includes("Rights:"), "and no rights disposition");

  const { model: later } = europeModelOf(consolidated, (wording) => wording.articles.slice(0, 1));
  const said = normalised(readPdf(exportPdf(later)).flat().map((run) => run.text).join(" "));
  assert.ok(said.includes("001 Subject-matter and objectives (eng, consolidated wording of 2024-01-01)"), "a consolidated wording is headed as one");
});

test("an EU export's exclusions are set: the article held without text, cited, and each annex row with its count, reason and official source", () => {
  const { model } = europeModelOf();
  const pages = readPdf(exportPdf(model));
  // The body's runs, so a sentence that runs onto the next page reads whole, without that page's foot and head between
  // its lines; every run, head and foot included, must still fit the page.
  const body = europeBodyOf(pages);
  const runs = pages.flat();
  const prose = normalised(body.filter((run) => run.font === "F1").map((run) => run.text).join(" "));
  const mono = body.filter((run) => run.font === "F2").map((run) => run.text).join("");
  const [excluded] = model.excluded;
  const [annex] = model.annexesNotServed;
  assert.ok(prose.includes("3 articles pinned: 2 exported with text, 1 excluded."));
  assert.ok(prose.includes("099 (eng, original wording of 2016-04-27): excluded, articles_without_text."));
  assert.ok(mono.includes(excluded.citation), "an excluded article keeps its citation");
  assert.ok(prose.includes(normalised(`2 annexes (eng, original wording of 2016-04-27): excluded, annex_text_not_available, served as text_not_available. ${capitalised(annex.reason)}.`)));
  assert.ok(mono.includes(annex.officialSource), "an annex is pointed to by its official source");
  for (const run of runs) {
    const shown = [...run.raw];
    while (shown.at(-1) === 0x20) shown.pop();
    const width = pdfTextWidth(shown, run.font === "F2" ? "mono" : "sans", run.size);
    assert.ok(run.x + width <= 56 + PDF_TEXT_WIDTH + 0.01, `"${run.text.slice(0, 40)}" fits (${width.toFixed(1)} pt)`);
  }
});

test("with the data taken out, an EU PDF's own words name no Luxembourg date, and a Luxembourg PDF's own words no EU wording date", async () => {
  // The two date vocabularies of `date-speech.test.mjs`.
  const LUXEMBOURG_DATE_WORDS = /\b(?:appl(?:y|ies|ied|ying|icability)|versions?|states?)\b|s[’']appliqu|applicab/i;
  const EUROPE_DATE_WORDS = /\bwording (?:of|date)\b|\bdated (?:\{|\d{4}-)|libellé du (?:\{|\d{4}-)|date du libellé|daté du (?:\{|\d{4}-)/i;
  /** What a PDF says in its own words: its prose and its monospaced lines, each value it was made from taken out. */
  const ownWords = (pages, ...sources) => {
    const values = new Set();
    const walk = (node) => {
      if (typeof node === "string" || typeof node === "number") values.add(normalised(String(node)));
      else if (Array.isArray(node)) node.forEach(walk);
      else if (node !== null && typeof node === "object") Object.values(node).forEach(walk);
    };
    sources.forEach(walk);
    const data = [...values].filter((value) => value.length > 0).sort((a, b) => b.length - a.length);
    const takeOut = (text) => data.reduce((rest, value) => rest.split(value).join(" ").split(capitalised(value)).join(" "), text);
    const runs = pages.flat();
    return [
      takeOut(normalised(runs.filter((run) => run.font === "F1").map((run) => run.text).join(" "))),
      takeOut(runs.filter((run) => run.font === "F2").map((run) => run.text).join("")),
    ].join(" | ");
  };

  const { sent, model } = europeModelOf(moreArticles(3));
  const europe = ownWords(readPdf(exportPdf(model)), sent, model);
  assert.doesNotMatch(europe, LUXEMBOURG_DATE_WORDS, `the EU PDF says ${europe.match(LUXEMBOURG_DATE_WORDS)?.[0]}`);
  assert.match(europe, EUROPE_DATE_WORDS, "it dates its articles in its own words");

  const { model: luxembourg } = await modelOf();
  const own = ownWords(readPdf(exportPdf(luxembourg)), await answer(), luxembourg);
  assert.doesNotMatch(own, EUROPE_DATE_WORDS, `the Luxembourg PDF says ${own.match(EUROPE_DATE_WORDS)?.[0]}`);
  assert.match(own, LUXEMBOURG_DATE_WORDS, "it dates its articles in its own words");
});

test("an EU PDF the standard fonts cannot set is refused, naming the characters of the field that holds them; a typographic space is set plainly", () => {
  const { model } = europeModelOf();
  const [item] = model.items;
  const [excluded] = model.excluded;
  const [annex] = model.annexesNotServed;
  const fields = [
    ["the identifier asked", { identifier: `${model.identifier} Δ` }, "U+0394"],
    ["the acknowledgement", { acknowledgement: `${model.acknowledgement} Ω` }, "U+03A9"],
    ["the authenticity statement", { authenticity: `${model.authenticity} −` }, "U+2212"],
    ["the rights rule", { rightsRule: `${model.rightsRule} ≤` }, "U+2264"],
    ["what a wording date is", { wordingDateSemantics: `${model.wordingDateSemantics} μ` }, "U+03BC"],
    ["an article's heading", { items: [{ ...item, heading: `${item.heading} 漢` }] }, "U+6F22"],
    ["an article's text", { items: [{ ...item, text: `${item.text} ∞` }] }, "U+221E"],
    ["an article's official source", { items: [{ ...item, officialSource: `${item.officialSource}→` }] }, "U+2192"],
    ["an excluded article's id", { excluded: [{ ...excluded, publisherId: "Art. ★" }] }, "U+2605"],
    ["an annex's reason", { annexesNotServed: [{ ...annex, reason: `${annex.reason} ✓` }] }, "U+2713"],
    ["an annex's official source", { annexesNotServed: [{ ...annex, officialSource: `${annex.officialSource}/✗` }] }, "U+2717"],
  ];
  assert.equal(pdfRefusal(model), null);
  for (const [field, change, code] of fields) {
    const refused = { ...model, ...change };
    assert.equal(pdfRefusal(refused), `its text holds characters the standard PDF fonts cannot set (${code})`, field);
    assert.throws(() => exportPdf(refused), new RegExp(`cannot be set as a PDF: .*\\(U\\+${code.slice(2)}\\)$`), field);
  }

  const { model: spaced } = europeModelOf((bundle) => {
    const article = bundle.wordings[0].articles[0];
    article.text = "Article 1 : “personal data” – the data’s subject…";
    article.text_byte_length = Buffer.byteLength(article.text, "utf8");
    article.text_sha256 = sha256(article.text);
  }, (wording) => wording.articles.slice(0, 1));
  assert.equal(pdfRefusal(spaced), null);
  const prose = normalised(europeBodyOf(readPdf(exportPdf(spaced))).map((run) => run.text).join(" "));
  assert.ok(prose.includes("Article 1 : “personal data” – the data’s subject…"), "WinAnsi's typographic characters are set as themselves");
  assert.ok(prose.includes(normalised(PDF_SUBSTITUTION_NOTE)), "and a thin space plainly, said");
});
