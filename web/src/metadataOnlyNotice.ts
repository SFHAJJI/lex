import { createElement as h, Fragment, type ReactNode } from "react";
import {
  METADATA_ONLY_BODY, METADATA_ONLY_DISCLOSURE, METADATA_ONLY_HEADING, officialSearchHref,
} from "./matchLanes.ts";
import { IDENTIFIER, PUBLISHER } from "./workCandidates.ts";

export interface MetadataOnlyWork {
  work: string;
  title: string;
}

interface DisclosedRow {
  publisher: string;
  group: string;
  title: string;
}

/**
 * A disclosed row exists only when its work coordinate parses against the B1 grammar:
 * publisher and group are validated separately and the internal link is REBUILT from the
 * parsed parts, never interpolated from the payload (Codex B2 review, O4). Invalid rows are
 * omitted without suppressing the primary notice.
 */
function validateRow(candidate: MetadataOnlyWork): DisclosedRow | null {
  if (typeof candidate.work !== "string") return null;
  const separator = candidate.work.indexOf(":");
  if (separator <= 0 || separator === candidate.work.length - 1) return null;
  const publisher = candidate.work.slice(0, separator);
  const group = candidate.work.slice(separator + 1);
  if (!PUBLISHER.test(publisher) || !IDENTIFIER.test(group)) return null;
  const title = typeof candidate.title === "string" && candidate.title.length > 0
    ? candidate.title.slice(0, 300)
    : group;
  return { publisher, group, title };
}

/**
 * The Decision 41 metadata_only notice for the workspace (B2). Renders the frozen heading and
 * complete body, the suppressed matches under one subordinate disclosure so the association
 * evidence stays inspectable without ever being presented as an answer, and BOTH agreed
 * actions: coverage, plus the exact-host official publisher search for every represented
 * collection. Kept JSX-free so the node test harness server-renders the real component the
 * workspace mounts, not a copy.
 */
export function MetadataOnlyNotice(
  { works }: { works: readonly MetadataOnlyWork[] },
): ReactNode {
  const seen = new Set<string>();
  const rows: DisclosedRow[] = [];
  for (const candidate of works) {
    const row = validateRow(candidate);
    if (row === null) continue;
    const key = `${row.publisher}:${row.group}`;
    if (seen.has(key)) continue;
    seen.add(key);
    rows.push(row);
  }
  const officials = [...new Set(rows.map((row) => officialSearchHref(row.publisher)))];
  return h(
    "div",
    { className: "trust-notice", role: "note", "data-testid": "metadata-only-notice",
      "aria-label": METADATA_ONLY_HEADING },
    h("b", null, METADATA_ONLY_HEADING),
    h("p", { className: "sub" }, METADATA_ONLY_BODY),
    rows.length > 0
      ? h(
          "details",
          null,
          h("summary", null, METADATA_ONLY_DISCLOSURE),
          h("ul", null, rows.slice(0, 10).map((row) =>
            h(
              "li",
              { key: `${row.publisher}:${row.group}` },
              h("a", { href: `/${row.publisher}/${row.group}` }, row.title),
              " ",
              h("span", { className: "sub mono" },
                `${row.group} · ${row.publisher} · matched in metadata`),
            ))),
          // C3 ruling: the count covers only valid deduplicated rows in this complete bounded
          // response, minus the ten shown; identical wording to the server surface. Truncated
          // responses cannot reach this component because they cannot authorise metadata_only.
          rows.length > 10
            ? h("span", { className: "sub" }, `and ${rows.length - 10} more returned matches`)
            : null,
        )
      : null,
    h(
      "p",
      { className: "sub" },
      h("a", { href: "/coverage" }, "View coverage and known gaps"),
      officials.map((href) => h(
        Fragment,
        { key: href },
        "  ",
        href.startsWith("https://")
          ? h("a", { href, rel: "noopener" }, "Search the official publisher")
          : h("a", { href }, "Search Lex"),
      )),
    ),
  );
}
