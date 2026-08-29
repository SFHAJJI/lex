import type { ProvisionItem } from "./api";
import type { Piece } from "./diff";
import { evidenceIntervalField, evidenceIntervalLabel } from "./temporal.ts";

export interface LawEvidence {
  title: string;
  work: string;
  validFrom: string;
  validTo?: string;
  language?: string;
  source?: string;
  permalink: string;
  extractionProfile?: string;
  timelineSemantics?: string;
  /** Document-level digests. A permalink alone cannot be checked; these are what a reader verifies
      against when the view holds more than one article, or none with its own text digest. */
  recordSha256?: string;
  bodySha256?: string;
  provisions: ProvisionItem[];
  exportedAt: string;
}

export interface ComparisonRow {
  label: string;
  anchor: string;
  kind: "changed" | "added" | "removed";
  pieces: Piece[];
  fromSha?: string;
  toSha?: string;
  /** Exact stored Markdown; visual diff pieces are a presentation-only plain-text projection. */
  fromText?: string;
  toText?: string;
}

export interface ComparisonEvidence {
  title: string;
  work: string;
  from: string;
  to: string;
  permalink: string;
  fromSource?: string;
  toSource?: string;
  fromPermalink?: string;
  toPermalink?: string;
  fromLexId?: string;
  toLexId?: string;
  fromVersionValidFrom?: string;
  fromVersionValidTo?: string;
  toVersionValidFrom?: string;
  toVersionValidTo?: string;
  fromExtractionProfile?: string;
  toExtractionProfile?: string;
  fromRecordSha256?: string;
  toRecordSha256?: string;
  rows: ComparisonRow[];
  unchanged: string[];
  punctuationOnly: string[];
  exportedAt: string;
}

const oneLine = (value: string) => value.replace(/\s+/g, " ").trim();
const itemLabel = (item: ProvisionItem) => oneLine(item.num ?? item.anchor);
const itemSha = (item: ProvisionItem) => item.text_sha256;

/**
 * Build a citation for the exact version, and article when one article is on screen.
 *
 * A citation always states what it can be checked against, and says so plainly when it cannot.
 * The exact text digest is preferred because it is the narrowest claim: it covers the wording the
 * reader actually saw. Where the view holds several articles there is no single such digest, so the
 * document-level record digest stands in. Where neither reached the browser the citation says
 * `no content digest recorded`, because a citation that quietly carries only a permalink looks
 * checkable and is not, and the reader has no way to tell the difference.
 */
function contentDigestField(input: LawEvidence): string {
  const item = input.provisions.length === 1 ? input.provisions[0] : undefined;
  if (item && itemSha(item)) return `text SHA-256 ${itemSha(item)}`;
  if (input.recordSha256) return `record SHA-256 ${input.recordSha256}`;
  if (input.bodySha256) return `body SHA-256 ${input.bodySha256}`;
  return "no content digest recorded";
}

/**
 * What a Lex citation is, stated on the citation itself.
 *
 * The Markdown export has always carried this. The citation string never did, and the citation
 * string is the artifact people actually paste into documents, so a Lex reference could reach a
 * legal filing reading as though it were the publisher's own record. The official source is named
 * one field earlier; this says which of the two the reader is holding.
 */
const NOT_OFFICIAL = "Lex reading aid, not an official publication";

export function citationText(input: LawEvidence): string {
  const item = input.provisions.length === 1 ? input.provisions[0] : undefined;
  return [
    oneLine(input.title),
    item ? itemLabel(item) : undefined,
    evidenceIntervalLabel(input.work, input.validFrom, input.validTo, input.timelineSemantics),
    input.work,
    input.permalink,
    input.source,
    contentDigestField(input),
    NOT_OFFICIAL,
  ].filter(Boolean).join(" | ");
}

/**
 * Both sides of a comparison need their own digest, for the same reason and with the same fallback
 * order. A comparison spanning several articles has no single text digest per side, so the
 * document-level record digest stands in; where a side has neither, that side says so rather than
 * going out bare.
 */
function comparisonSideDigest(label: string, textSha?: string, recordSha?: string): string {
  if (textSha) return `${label} text SHA-256 ${textSha}`;
  if (recordSha) return `${label} record SHA-256 ${recordSha}`;
  return `${label} no content digest recorded`;
}

export function comparisonCitationText(input: ComparisonEvidence): string {
  const row = input.rows.length === 1 ? input.rows[0] : undefined;
  return [
    oneLine(input.title),
    row ? oneLine(row.label) : undefined,
    `comparison ${input.from} to ${input.to}`,
    input.work,
    input.permalink,
    comparisonSideDigest(input.from, row?.fromSha, input.fromRecordSha256),
    comparisonSideDigest(input.to, row?.toSha, input.toRecordSha256),
    NOT_OFFICIAL,
  ].filter(Boolean).join(" | ");
}

/**
 * Export the exact strings already returned by MCP. Text is not normalised, reflowed, or
 * consolidated here. Metadata is deliberately separate from the wording so the file cannot be
 * mistaken for a publisher artifact.
 */
export function lawEvidenceMarkdown(input: LawEvidence): string {
  const lines = [
    `# ${oneLine(input.title)}`,
    "",
    "> Reading aid exported from Lex. This is not an official publication and is not legal advice.",
    "",
    `- Work: ${input.work}`,
    `- ${evidenceIntervalField(input.work, input.validFrom, input.validTo, input.timelineSemantics)}`,
    `- Timeline semantics: ${input.timelineSemantics ?? (input.work.startsWith("eu-eurlex:") ? "official_consolidation_state" : "publisher_applicability")}`,
    `- Language: ${input.language ?? "not recorded"}`,
    `- Lex permalink: ${input.permalink}`,
    `- Official source: ${input.source ?? "not recorded"}`,
    `- Extraction profile: ${input.extractionProfile ?? "publisher structured text"}`,
    `- Exported at: ${input.exportedAt}`,
    "",
  ];

  for (const item of input.provisions) {
    const heading = item.heading ? `, ${oneLine(item.heading)}` : "";
    lines.push(
      `## ${itemLabel(item)}${heading}`,
      "",
      `- Anchor: ${item.anchor}`,
      `- Text SHA-256: ${itemSha(item) ?? "not recorded"}`,
      "",
      item.text ?? "",
      "",
    );
  }

  return `${lines.join("\n").trimEnd()}\n`;
}

const versionText = (row: ComparisonRow, side: "before" | "after") => row.pieces
  .filter((piece) => side === "before" ? piece.k !== "+" : piece.k !== "-")
  .map((piece) => piece.t)
  .join("");

const exactVersionText = (row: ComparisonRow, side: "before" | "after") =>
  (side === "before" ? row.fromText : row.toText) ?? versionText(row, side);

/** Export only the comparison already computed for the screen. No second diff is performed. */
export function comparisonEvidenceMarkdown(input: ComparisonEvidence): string {
  const lines = [
    `# ${oneLine(input.title)}: comparison`,
    "",
    "> Reading aid exported from Lex. This is a mechanical comparison, not an official publication or legal advice.",
    "",
    `- Work: ${input.work}`,
    `- Compared: ${input.from} to ${input.to}`,
    `- Lex comparison: ${input.permalink}`,
    `- ${input.from} Lex version: ${input.fromPermalink ?? "not recorded"}`,
    `- ${input.from} applicable Lex ID: ${input.fromLexId ?? "not recorded"}`,
    `- ${input.from} applicable interval: ${input.fromVersionValidFrom ?? "not recorded"} to ${input.fromVersionValidTo ?? "open"}`,
    `- ${input.from} official source: ${input.fromSource ?? "not recorded"}`,
    `- ${input.from} extraction profile: ${input.fromExtractionProfile ?? "not recorded"}`,
    `- ${input.to} Lex version: ${input.toPermalink ?? "not recorded"}`,
    `- ${input.to} applicable Lex ID: ${input.toLexId ?? "not recorded"}`,
    `- ${input.to} applicable interval: ${input.toVersionValidFrom ?? "not recorded"} to ${input.toVersionValidTo ?? "open"}`,
    `- ${input.to} official source: ${input.toSource ?? "not recorded"}`,
    `- ${input.to} extraction profile: ${input.toExtractionProfile ?? "not recorded"}`,
    `- Exported at: ${input.exportedAt}`,
    `- Summary: ${input.rows.length} wording changes, ${input.unchanged.length} identical, ${input.punctuationOnly.length} punctuation-only`,
    "",
  ];

  for (const row of input.rows) {
    lines.push(
      `## ${oneLine(row.label)} (${row.kind})`,
      "",
      `- Anchor: ${row.anchor}`,
      `- ${input.from} text SHA-256: ${row.fromSha ?? "not present"}`,
      `- ${input.to} text SHA-256: ${row.toSha ?? "not present"}`,
      "",
    );
    lines.push(
      `### ${input.from}`,
      "",
      row.kind === "added" ? "Not present in this version." : exactVersionText(row, "before"),
      "",
      `### ${input.to}`,
      "",
      row.kind === "removed" ? "Not present in this version." : exactVersionText(row, "after"),
      "",
    );
  }

  if (input.punctuationOnly.length > 0) {
    lines.push(
      "## Punctuation-only differences",
      "",
      "The stored publisher text differs, but the wording comparison classifies only punctuation changes.",
      "",
      input.punctuationOnly.join(", "),
      "",
    );
  }
  if (input.unchanged.length > 0) {
    lines.push("## Identical provisions", "", input.unchanged.join(", "), "");
  }

  return `${lines.join("\n").trimEnd()}\n`;
}

export function evidenceFilename(work: string, suffix: string): string {
  const safe = `${work}-${suffix}`.toLowerCase().replace(/[^a-z0-9._-]+/g, "-").replace(/^-+|-+$/g, "");
  return `${safe || "lex-evidence"}.md`;
}

export async function copyText(value: string): Promise<void> {
  if (!navigator.clipboard?.writeText) throw new Error("Clipboard access is unavailable");
  await navigator.clipboard.writeText(value);
}

export function downloadMarkdown(filename: string, value: string): void {
  const url = URL.createObjectURL(new Blob([value], { type: "text/markdown;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.hidden = true;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
