// The export composer, asked of the live API: pin articles of one reading and take them away.
//
// The eighth live screen. It asks what the reading screen asks (`evidence_bundle`, through
// `live-reading.mjs`: the same request, the same outcome mapping and the same refusal sentences,
// because what is refused is the reading an export would be composed from), and composes what the
// reader pins with `export-build.mjs`: a Luxembourg state's articles with `composeExport`, an EU
// wording's with `composeEuropeExport`. Composing and saving send nothing: the one request is the
// reading. This file holds the pin keys, the file names, the saving and the sentences the page needs;
// what an export carries is decided in `export-build.mjs`.

import { composeEuropeExport, composeExport, exportCsv, exportJson } from './export-build.mjs';
import { exportPdf, pdfRefusal } from './export-pdf.mjs';
import { englishRun, fillParts, liveChrome, statement } from './live-chrome.mjs';

const COPY = liveChrome().export;

export const LIVE_EXPORT_IDLE = COPY.idle;

export const NOTHING_PINNED = COPY.nothingPinned;

/**
 * The formats a reader can save, each written from the one model (`export-build.mjs`,
 * `export-pdf.mjs`). `refusal` says why a model cannot be written in that format, or null.
 */
export const EXPORT_FORMATS = Object.freeze([
  Object.freeze({ id: 'json', label: COPY.save.json, extension: 'json', mediaType: 'application/json', write: exportJson, refusal: () => null }),
  Object.freeze({ id: 'csv', label: COPY.save.csv, extension: 'csv', mediaType: 'text/csv;charset=utf-8', write: exportCsv, refusal: () => null }),
  Object.freeze({ id: 'pdf', label: COPY.save.pdf, extension: 'pdf', mediaType: 'application/pdf', write: exportPdf, refusal: pdfRefusal }),
]);

/** Why a format is not offered, in the table's words around the writer's own reason, which is English (`statement`). */
export function formatRefused(format, reason) {
  return statement(fillParts(COPY.formatRefused, { format: format.id.toUpperCase(), reason: englishRun(reason) }));
}

/**
 * One pin: the digest of what the article is read in (a Luxembourg state, or an EU wording) and the
 * article's publisher id, which together name one article.
 */
export function pinKey(digest, publisherId) {
  return `${digest}#${publisherId}`;
}

/** A pin taken apart: its digest under `digestName` (`stateSha256` or `wordingSha256`), and its publisher id. */
function unpin(key, digestName) {
  // A digest is hex, so the first '#' ends it; a publisher id may hold anything after.
  const split = key.indexOf('#');
  return { [digestName]: key.slice(0, split), publisherId: key.slice(split + 1) };
}

/**
 * Why an export cannot be composed, in the table's words around the composer's own reason, which is English: a
 * page in another language marks it so (`statement`).
 */
export function composeFailed(reason) {
  return statement(fillParts(COPY.composeFailed, { reason: englishRun(reason) }));
}

/**
 * What the export panel shows for a reading outcome and the pinned keys: nothing before a reading,
 * the nothing-pinned sentence, the composed export, or the reason it cannot be composed (an envelope
 * that says no observation time, say). Never throws, since it runs while rendering.
 */
export function exportState(outcome, pins) {
  if (outcome.state !== 'success') return { state: 'none' };
  if (pins.size === 0) return { state: 'empty', sentence: NOTHING_PINNED };
  const observedAt = outcome.context?.freshness?.observed_at;
  try {
    // An EU reading is composed into its own model, never the Luxembourg one: its wordings are dated as wordings,
    // and its text carries Decision 95's acknowledgement rather than a rights disposition.
    const model = outcome.view.publisher === 'eu-eurlex'
      ? composeEuropeExport({
        view: outcome.view,
        pinned: [...pins].map((key) => unpin(key, 'wordingSha256')),
        observedAt,
        registrySha256: outcome.registrySha256,
      })
      : composeExport({
        view: outcome.view,
        pinned: [...pins].map((key) => unpin(key, 'stateSha256')),
        observedAt,
      });
    return { state: 'composed', model };
  } catch (error) {
    return { state: 'failed', ...composeFailed(error.message) };
  }
}

/**
 * The name a saved export is offered under: the identifier asked, made safe for a file name, and the
 * date read, e.g. `lex-v3-export-lu-legilux-loi-1991-08-10-n3-2024-02-01.json`.
 */
export function exportFileName(model, extension) {
  const slug = model.identifier.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  return `lex-v3-export-${slug || 'work'}-${model.date}.${extension}`;
}

/**
 * Hands the reader one export as a file, from the page itself: a Blob, an object URL and an anchor
 * with `download`, clicked and removed. Nothing is sent anywhere. `doc` and `urls` are the page's
 * `document` and `URL`, injectable for tests.
 */
export function saveExport(model, format, { doc = globalThis.document, urls = globalThis.URL, later = setTimeout } = {}) {
  const url = urls.createObjectURL(new Blob([format.write(model)], { type: format.mediaType }));
  const anchor = doc.createElement('a');
  anchor.href = url;
  anchor.download = exportFileName(model, format.extension);
  doc.body.append(anchor);
  anchor.click();
  anchor.remove();
  // Revoked once the browser has taken the file, not before: a revoke in the same task can cancel
  // the download.
  later(() => urls.revokeObjectURL(url), 1000);
  return anchor.download;
}
