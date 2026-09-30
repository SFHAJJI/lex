// The PDF export: the same model the JSON and CSV exports write (`export-build.mjs`), set as pages.
//
// The launch contract's line is that exports "preserve citations, rights, watermarks and exclusions",
// in PDF as in JSON and CSV. The PDF is composed from the one export model, so the three formats cannot
// disagree: every page carries the watermark at its head and its page number at its foot; the first
// page says what was asked, when the answering snapshot was observed, the rights the text was served
// under with the platform's rule, and the corpus, index and registry digests; each item carries its
// citation (the hash-pinned article permalink), its text and body digests, its official source, its
// rights, its own date where it differs from its state's, its notes, and its text; each exclusion
// carries its reason.
//
// It is written here, byte by byte, with no library: PDF 1.4, A4 pages, uncompressed content streams,
// the standard Helvetica and Courier fonts in WinAnsiEncoding, no embedded font and no time of making,
// so the same model always gives the same bytes. The standard fonts set Latin-1 and the few
// typographic characters WinAnsi adds. Typographic spaces and hyphens outside that set are set as
// plain spaces and hyphens, and the PDF says so when it did; any other character it cannot set makes
// the PDF refused with the characters named (`pdfRefusal`), never replaced silently. The text digests
// are of the text as served, which the JSON export carries whole. The PDF is not tagged, so the
// statute language of each text is not marked in it.

const PAGE_WIDTH = 595.28;
const PAGE_HEIGHT = 841.89;
const MARGIN_X = 56;
const MARGIN_TOP = 64;
const MARGIN_BOTTOM = 64;
export const PDF_TEXT_WIDTH = PAGE_WIDTH - 2 * MARGIN_X;

/** The one sentence the PDF adds when it set typographic spaces or hyphens as plain ones. */
export const PDF_SUBSTITUTION_NOTE =
  'Typographic spaces and hyphens outside the standard PDF character set are set here as plain spaces and hyphens; each text digest is of the text as served.';

// WinAnsiEncoding's bytes 128 to 159, as the Unicode characters they set (PDF 32000-1, Annex D).
const WIN_ANSI_HIGH = Object.freeze({
  0x20ac: 0x80, 0x201a: 0x82, 0x0192: 0x83, 0x201e: 0x84, 0x2026: 0x85, 0x2020: 0x86, 0x2021: 0x87,
  0x02c6: 0x88, 0x2030: 0x89, 0x0160: 0x8a, 0x2039: 0x8b, 0x0152: 0x8c, 0x017d: 0x8e, 0x2018: 0x91,
  0x2019: 0x92, 0x201c: 0x93, 0x201d: 0x94, 0x2022: 0x95, 0x2013: 0x96, 0x2014: 0x97, 0x02dc: 0x98,
  0x2122: 0x99, 0x0161: 0x9a, 0x203a: 0x9b, 0x0153: 0x9c, 0x017e: 0x9e, 0x0178: 0x9f,
});

// The typographic spaces and hyphens set as their plain forms.
const PLAIN_FORMS = Object.freeze({
  0x0009: 0x20, 0x2002: 0x20, 0x2003: 0x20, 0x2004: 0x20, 0x2005: 0x20, 0x2006: 0x20, 0x2007: 0x20,
  0x2008: 0x20, 0x2009: 0x20, 0x200a: 0x20, 0x202f: 0x20, 0x205f: 0x20, 0x2010: 0x2d, 0x2011: 0x2d,
});

// Helvetica's advance widths (Adobe's AFM, thousandths of the size) for WinAnsi bytes 32 to 255.
const HELVETICA = [
  278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
  556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
  1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
  667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
  333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
  556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 350,
  556, 350, 222, 556, 333, 1000, 556, 556, 333, 1000, 667, 333, 1000, 350, 611, 350,
  350, 222, 222, 333, 333, 350, 556, 1000, 333, 1000, 500, 333, 944, 350, 500, 667,
  278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333,
  400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,
  667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
  722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
  556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278,
  556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500,
];

const FONTS = Object.freeze({
  sans: Object.freeze({ resource: 'F1', width: (byte) => HELVETICA[byte - 32] ?? 556 }),
  mono: Object.freeze({ resource: 'F2', width: () => 600 }),
});

/**
 * One text as WinAnsi bytes: `{bytes, substituted}`, or `{unsettable}` naming the characters the
 * standard fonts cannot set (as U+XXXX).
 */
function encode(text) {
  const bytes = [];
  const unsettable = new Set();
  let substituted = false;
  for (const char of text) {
    const code = char.codePointAt(0);
    if ((code >= 0x20 && code <= 0x7e) || (code >= 0xa0 && code <= 0xff)) bytes.push(code);
    else if (WIN_ANSI_HIGH[code] !== undefined) bytes.push(WIN_ANSI_HIGH[code]);
    else if (PLAIN_FORMS[code] !== undefined) {
      bytes.push(PLAIN_FORMS[code]);
      substituted = true;
    } else unsettable.add(`U+${code.toString(16).toUpperCase().padStart(4, '0')}`);
  }
  return unsettable.size > 0 ? { unsettable: [...unsettable] } : { bytes, substituted };
}

/** The width, in points, of WinAnsi bytes set in one font and size. */
export function pdfTextWidth(bytes, font, size) {
  return bytes.reduce((sum, byte) => sum + FONTS[font].width(byte), 0) * size / 1000;
}

/** Every text the PDF sets for a model, as it will set them. */
function textsOf(model) {
  const texts = [model.watermark, model.identifier, model.rightsDisposition, model.rightsRule, model.observedAt];
  for (const item of model.items) {
    texts.push(item.publisherId, item.language, item.citation, item.officialSource, item.text, ...item.notes.flatMap((note) => [note.marker, note.text]));
  }
  for (const entry of model.excluded) texts.push(entry.publisherId, entry.language, entry.citation, entry.statePermalink, entry.reason);
  return texts.map((text) => String(text).replace(/\r\n?/g, '\n').replaceAll('\n', ' '));
}

/**
 * Why this model cannot be set as a PDF (the characters the standard fonts cannot set), or null when
 * it can.
 */
export function pdfRefusal(model) {
  const unsettable = new Set(textsOf(model).flatMap((text) => encode(text).unsettable ?? []));
  return unsettable.size === 0 ? null : `its text holds characters the standard PDF fonts cannot set (${[...unsettable].join(', ')})`;
}

/** The width a line takes on the page: its trailing spaces take none. */
function visibleWidth(bytes, font, size) {
  let end = bytes.length;
  while (end > 0 && bytes[end - 1] === 0x20) end -= 1;
  return pdfTextWidth(bytes.slice(0, end), font, size);
}

/**
 * Breaks WinAnsi bytes into lines no wider than `width`, as consecutive slices of the text: a line ends
 * after a run of spaces, which stays on it where it takes no visible room, and inside a word only when
 * the word is wider than a line. Every byte is kept, so the lines joined with nothing between them are the
 * text exactly (review of #790: collapsing a double space changed the text its digest vouches for).
 */
function wrap(bytes, font, size, width) {
  const runs = [];
  for (const byte of bytes) {
    const space = byte === 0x20;
    const last = runs.at(-1);
    if (last !== undefined && last.space === space) last.bytes.push(byte);
    else runs.push({ space, bytes: [byte] });
  }
  const lines = [];
  let line = [];
  const fits = (candidate) => visibleWidth(candidate, font, size) <= width;
  for (const run of runs) {
    if (run.space || fits([...line, ...run.bytes])) {
      line.push(...run.bytes);
      continue;
    }
    // A line holding only spaces keeps them: they lead the word, and a break there would drop nothing
    // but start a line with nothing visible on the one before.
    if (visibleWidth(line, font, size) > 0) {
      lines.push(line);
      line = [];
    }
    for (const byte of run.bytes) {
      if (!fits([...line, byte]) && visibleWidth(line, font, size) > 0) {
        lines.push(line);
        line = [];
      }
      line.push(byte);
    }
  }
  if (line.length > 0 || lines.length === 0) lines.push(line);
  return lines;
}

/** The layout: a list of lines (`{font, size, leading, bytes, indent}`) and gaps (`{gap}`). */
function layout(model) {
  const blocks = [];
  let substituted = false;
  // A text's line breaks are its paragraphs; each is encoded and wrapped on its own.
  // `keep` is the height that must follow a heading on its page, so no heading ends a page alone.
  const add = (text, { font = 'sans', size = 10, leading = size * 1.3, indent = 0, label = '', keep = 0 } = {}) => {
    for (const paragraph of `${label}${text}`.replace(/\r\n?/g, '\n').split('\n')) {
      const encoded = encode(paragraph);
      if (encoded.unsettable) throw new Error(`the PDF cannot set ${encoded.unsettable.join(', ')}`);
      substituted ||= encoded.substituted;
      for (const bytes of wrap(encoded.bytes, font, size, PDF_TEXT_WIDTH - indent)) blocks.push({ font, size, leading, bytes, indent, keep });
    }
  };
  const sentence = (text) => {
    const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
    return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
  };
  const gap = (height) => blocks.push({ gap: height });

  const total = model.items.length + model.excluded.length;
  add('Lex V3 export', { size: 16, leading: 22 });
  add(`Asked: ${model.identifier}, read on ${model.date}. The answering snapshot was observed at ${model.observedAt}.`);
  add(`Text served under ${model.rightsDisposition}. ${sentence(model.rightsRule)}`);
  add(`${total} ${total === 1 ? 'article' : 'articles'} pinned: ${model.items.length} exported with text, ${model.excluded.length} excluded.`);
  gap(4);
  add('Verified by', { size: 9 });
  add(model.verifiedBy.corpusSha256, { font: 'mono', size: 8, label: 'corpus   ' });
  add(model.verifiedBy.indexSha256, { font: 'mono', size: 8, label: 'index    ' });
  add(model.verifiedBy.registrySha256, { font: 'mono', size: 8, label: 'registry ' });

  for (const item of model.items) {
    gap(12);
    add(`${item.publisherId} (${item.language}, applying from ${item.appliesFrom})`, { size: 12, leading: 16, keep: 120 });
    add(item.citation, { font: 'mono', size: 8, label: 'citation        ' });
    add(item.statePermalink, { font: 'mono', size: 8, label: 'state           ' });
    add(item.textSha256, { font: 'mono', size: 8, label: 'text sha-256    ' });
    add(item.bodySha256, { font: 'mono', size: 8, label: 'body sha-256    ' });
    add(item.officialSource, { font: 'mono', size: 8, label: 'official source ' });
    add(`Rights: ${item.rightsDisposition}.`, { size: 9 });
    if (item.validityConflict) {
      add(`This article's own date is ${item.articleValidFrom}; its state applies from ${item.appliesFrom}.`, { size: 9 });
    }
    gap(3);
    add(item.text);
    for (const note of item.notes) add(`[${note.marker}] ${note.text}`, { size: 9, indent: 12 });
  }

  if (model.excluded.length > 0) {
    gap(12);
    add('Excluded', { size: 12, leading: 16, keep: 40 });
    for (const entry of model.excluded) {
      add(`${entry.publisherId} (${entry.language}, applying from ${entry.appliesFrom}): excluded, ${entry.reason}.`);
      add(entry.citation, { font: 'mono', size: 8, label: 'citation ' });
      add(entry.statePermalink, { font: 'mono', size: 8, label: 'state    ' });
    }
  }
  if (substituted) {
    gap(12);
    add(PDF_SUBSTITUTION_NOTE, { size: 9 });
  }
  return blocks;
}

/** The layout cut into pages: the body between the margins, gaps dropped at the head of a page. */
function paginate(blocks) {
  const pages = [[]];
  let y = PAGE_HEIGHT - MARGIN_TOP;
  for (const block of blocks) {
    const page = pages[pages.length - 1];
    if (block.gap !== undefined) {
      if (page.length > 0) y -= block.gap;
      continue;
    }
    if (y - block.leading - block.keep < MARGIN_BOTTOM && page.length > 0) {
      pages.push([]);
      y = PAGE_HEIGHT - MARGIN_TOP;
    }
    y -= block.leading;
    pages[pages.length - 1].push({ ...block, x: MARGIN_X + block.indent, y });
  }
  return pages;
}

function number(value) {
  return Number(value.toFixed(2)).toString();
}

/** A PDF literal string of WinAnsi bytes, in ASCII: delimiters escaped, other bytes in octal. */
function literal(bytes) {
  let out = '(';
  for (const byte of bytes) {
    if (byte === 0x28 || byte === 0x29 || byte === 0x5c) out += `\\${String.fromCharCode(byte)}`;
    else if (byte < 0x20 || byte > 0x7e) out += `\\${byte.toString(8).padStart(3, '0')}`;
    else out += String.fromCharCode(byte);
  }
  return `${out})`;
}

function show(line) {
  return `BT /${FONTS[line.font].resource} ${number(line.size)} Tf 1 0 0 1 ${number(line.x)} ${number(line.y)} Tm ${literal(line.bytes)} Tj ET`;
}

function ascii(text) {
  const encoded = encode(text);
  if (encoded.unsettable) throw new Error(`the PDF cannot set ${encoded.unsettable.join(', ')}`);
  return encoded.bytes;
}

/**
 * The PDF export of a model, as bytes. Throws when its text holds a character the standard fonts
 * cannot set (see `pdfRefusal`).
 */
export function exportPdf(model) {
  const refusal = pdfRefusal(model);
  if (refusal !== null) throw new Error(`this export cannot be set as a PDF: ${refusal}`);
  const pages = paginate(layout(model));
  const watermark = wrap(ascii(model.watermark), 'sans', 8, PDF_TEXT_WIDTH);

  const objects = [];
  const reserve = () => objects.push(null);
  const set = (index, body) => { objects[index - 1] = body; };
  reserve(); // 1: catalog
  reserve(); // 2: pages
  const helvetica = objects.push('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>');
  const courier = objects.push('<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>');
  const info = objects.push(`<< /Title ${literal(ascii(`Lex V3 export: ${model.identifier}, ${model.date}`))} /Subject ${literal(ascii(model.watermark))} /Producer (Lex V3 export composer) >>`);
  const pageRefs = [];
  pages.forEach((lines, index) => {
    const head = watermark.map((bytes, row) => show({ font: 'sans', size: 8, x: MARGIN_X, y: PAGE_HEIGHT - 36 - row * 10, bytes }));
    const foot = show({ font: 'sans', size: 8, x: MARGIN_X, y: 32, bytes: ascii(`Page ${index + 1} of ${pages.length}. ${model.identifier}, read on ${model.date}.`) });
    const content = [...head, ...lines.map(show), foot].join('\n');
    const stream = objects.push(`<< /Length ${content.length} >>\nstream\n${content}\nendstream`);
    pageRefs.push(objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${PAGE_WIDTH} ${PAGE_HEIGHT}] /Resources << /Font << /F1 ${helvetica} 0 R /F2 ${courier} 0 R >> >> /Contents ${stream} 0 R >>`));
  });
  set(1, '<< /Type /Catalog /Pages 2 0 R >>');
  set(2, `<< /Type /Pages /Kids [${pageRefs.map((ref) => `${ref} 0 R`).join(' ')}] /Count ${pageRefs.length} >>`);

  // Everything above is ASCII, so a character is a byte; the header's comment marks the file binary.
  let file = '%PDF-1.4\n%âãÏÓ\n';
  const offsets = [];
  objects.forEach((body, index) => {
    offsets.push(file.length);
    file += `${index + 1} 0 obj\n${body}\nendobj\n`;
  });
  const xref = file.length;
  file += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const offset of offsets) file += `${String(offset).padStart(10, '0')} 00000 n \n`;
  file += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R /Info ${info} 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Uint8Array.from(file, (char) => char.charCodeAt(0));
}
