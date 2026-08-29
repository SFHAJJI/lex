import { provisionSourceUrl, type ProvisionItem } from "./api.ts";
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
  /** Document-level digests. `recordSha256` covers serialized version metadata and `bodySha256`
      the publisher's own body. Neither digests the ordered provisions on screen, so neither makes
      the displayed wording checkable; both are carried labelled with the scope they do have. */
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

/**
 * The provision a reader asked to compare, and its identity on each side.
 *
 * Deliberately separate from `rows`. `rows` is what the visual diff found, and two things never
 * reach it: an anchor whose text is identical on both sides is filed as unchanged before any text
 * is fetched, and an anchor whose only differences are typographic is moved to the punctuation
 * list. Reading the citation's scope off `rows.length` therefore answers a different question from
 * the one the reader asked, and it fails in the worst direction: an article-scoped comparison of
 * wording that did not change is exactly the case where both exact digests exist, and it was the
 * case that lost them.
 */
export interface ComparisonScope {
  anchor: string;
  label: string;
  fromSha?: string;
  toSha?: string;
  fromPresent: boolean;
  toPresent: boolean;
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
  /** Set when the reader scoped the comparison to one provision, whatever the diff then found. */
  scope?: ComparisonScope;
  rows: ComparisonRow[];
  unchanged: string[];
  punctuationOnly: string[];
  exportedAt: string;
}

const oneLine = (value: string) => value.replace(/\s+/g, " ").trim();
const itemLabel = (item: ProvisionItem) => oneLine(item.num ?? item.anchor);
const itemSha = (item: ProvisionItem) => item.text_sha256;

/**
 * A value is called a SHA-256 here only if it is one.
 *
 * Nothing between the SQLite column and this function checks these fields at runtime. The index
 * builder recomputes each provision digest and refuses a mismatch, so `text_sha256` is sound by
 * construction when the index is built; `record_sha256` and `body_sha256` are copied onto the index
 * without a recompute, the MCP response carries no output schema, and the browser parses the whole
 * payload through a single unchecked cast. A guarantee that holds by construction somewhere else is
 * not a check here, and here is where the value stops being data and becomes a provenance claim a
 * reader is invited to act on.
 *
 * Case is part of the test, not pedantry. The upstream comparisons are case-insensitive, so an
 * uppercase digest passes every existing check and arrives intact. It is a real digest and it is
 * still not the string a reader can paste into a checker and match, so it is not offered as one.
 */
const isDigest = (value?: string): value is string =>
  value !== undefined && /^[0-9a-f]{64}$/.test(value);

/**
 * Said once when any digest reaching a citation was present but unusable.
 *
 * Dropping it silently would be the safer-looking choice and the wrong one: the reader would see a
 * citation that merely looks thin, with no way to tell an absent digest from a corrupt one, and
 * nobody would ever learn the index had a problem.
 */
const UNRECOGNISED = "digest withheld, not in a recognised form";

const unusable = (...values: (string | undefined)[]): boolean =>
  values.some((value) => value !== undefined && value !== "" && !isDigest(value));

/** The long form of the same rule, for the Markdown export. */
const digestOr = (value: string | undefined, absent: string): string =>
  isDigest(value) ? value : value ? UNRECOGNISED : absent;

/**
 * Every digest stays inside the claim it can support.
 *
 * The exact text digest is the narrowest and strongest: it covers precisely the wording the reader
 * saw. It exists only when one article is on screen. Where several are, there is no digest of the
 * rendered wording at all, and the citation says so rather than substituting a weaker one.
 *
 * `record_sha256` hashes serialized version metadata; `body_sha256` is the publisher's own body.
 * Neither is a digest of the ordered provisions displayed, so where they are carried they are
 * labelled with what they actually cover. An earlier revision let the record digest stand in for a
 * wording digest, which made a multi-article citation look verifiable against text it does not
 * cover.
 */
function contentDigestFields(input: LawEvidence): string[] {
  const item = input.provisions.length === 1 ? input.provisions[0] : undefined;
  const exact = item ? itemSha(item) : undefined;
  const note = unusable(exact, input.recordSha256, input.bodySha256) ? [UNRECOGNISED] : [];

  // One article on screen with its own digest: the narrowest and strongest claim available, and it
  // covers precisely the wording the reader saw. Nothing else needs saying.
  if (isDigest(exact)) return [`text SHA-256 ${exact}`, ...note];

  // Otherwise there is no digest of the rendered wording, and that has to be said rather than
  // covered over. `record_sha256` hashes serialized version metadata and `body_sha256` is the
  // publisher's own body; neither is a digest of the ordered provisions on screen, so both are
  // carried with the claim they can actually support written next to them.
  return [
    "no aggregate text digest recorded",
    isDigest(input.recordSha256)
      ? `record SHA-256 ${input.recordSha256} (version metadata)` : undefined,
    isDigest(input.bodySha256) ? `body SHA-256 ${input.bodySha256} (publisher body)` : undefined,
    ...note,
  ].filter(Boolean) as string[];
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
    ...contentDigestFields(input),
    NOT_OFFICIAL,
  ].filter(Boolean).join(" | ");
}

/**
 * Each side of a comparison is held to the same rule as a law citation: a digest is stated with the
 * claim it supports, and a metadata digest never stands in for a wording one.
 *
 * A side can also hold no provision at all. Where one article is compared and it was added or
 * removed, the absent side has no wording to digest, and reporting a missing digest there states the
 * wrong condition: it reads as text whose digest went unrecorded rather than text that never
 * existed. The rendered comparison already draws that distinction and says `not in this version`;
 * the copied citation now keeps it, and carries no digest for a side with nothing to digest.
 */
function comparisonSideDigest(
  label: string, present: boolean, textSha?: string, recordSha?: string,
): string[] {
  if (!present) return [`${label} not present in this version`];
  const note = unusable(textSha, recordSha) ? [`${label} ${UNRECOGNISED}`] : [];
  if (isDigest(textSha)) return [`${label} text SHA-256 ${textSha}`, ...note];
  return [
    `${label} no aggregate text digest recorded`,
    isDigest(recordSha) ? `${label} record SHA-256 ${recordSha} (version metadata)` : undefined,
    ...note,
  ].filter(Boolean) as string[];
}

/**
 * The official source for each side, labelled by its date.
 *
 * `ComparisonEvidence` already carried both and the Markdown export already rendered both; only the
 * citation dropped them. That is the wrong place to drop them. The citation is the artifact people
 * paste into documents, and the disclaimer beside it exists to say this is a reading aid rather
 * than the publisher's record. Saying so without naming the record leaves the reader nowhere to go
 * to check, which is the same failure as a digest that supports no claim.
 */
function comparisonSourceFields(input: ComparisonEvidence): string[] {
  return [
    input.fromSource ? `${input.from} source ${input.fromSource}` : undefined,
    input.toSource ? `${input.to} source ${input.toSource}` : undefined,
  ].filter(Boolean) as string[];
}

export function comparisonCitationText(input: ComparisonEvidence): string {
  // The recorded scope answers what the reader asked. `rows` is consulted only when no scope was
  // recorded, which is a whole-document comparison, and there a lone changed row is still the one
  // provision the citation can speak for.
  const scope = input.scope;
  const row = scope ? undefined : (input.rows.length === 1 ? input.rows[0] : undefined);
  // Presence is a property of the compared article, not of its digest. A whole-document comparison
  // showing several rows has provisions on both sides, so both are present.
  const fromPresent = scope ? scope.fromPresent : row?.kind !== "added";
  const toPresent = scope ? scope.toPresent : row?.kind !== "removed";
  return [
    oneLine(input.title),
    scope ? oneLine(scope.label) : row ? oneLine(row.label) : undefined,
    `comparison ${input.from} to ${input.to}`,
    input.work,
    input.permalink,
    ...comparisonSourceFields(input),
    ...comparisonSideDigest(input.from, fromPresent, scope ? scope.fromSha : row?.fromSha,
      input.fromRecordSha256),
    ...comparisonSideDigest(input.to, toPresent, scope ? scope.toSha : row?.toSha,
      input.toRecordSha256),
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
    if (item.text_available === false && item.text_unavailable_reason) {
      const officialSource = provisionSourceUrl(item);
      lines.push(
        `## ${itemLabel(item)}${heading}`,
        "",
        `- Anchor: ${item.anchor}`,
        `- Text: unavailable (${item.text_unavailable_reason})`,
        `- Official source: ${officialSource ?? "not recorded"}`,
        "",
      );
      continue;
    }
    lines.push(
      `## ${itemLabel(item)}${heading}`,
      "",
      `- Anchor: ${item.anchor}`,
      `- Text SHA-256: ${digestOr(itemSha(item), "not recorded")}`,
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
      `- ${input.from} text SHA-256: ${digestOr(row.fromSha, "not present")}`,
      `- ${input.to} text SHA-256: ${digestOr(row.toSha, "not present")}`,
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
