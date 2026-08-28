import { createElement as h, type ReactNode } from "react";
import {
  METADATA_ONLY_BODY, METADATA_ONLY_DISCLOSURE, METADATA_ONLY_HEADING,
} from "./matchLanes.ts";

export interface MetadataOnlyWork {
  work: string;
  title: string;
}

/**
 * The Decision 41 metadata_only notice for the workspace (B2). Renders the frozen heading and
 * complete body, the suppressed matches under one subordinate disclosure so the association
 * evidence stays inspectable without ever being presented as an answer, and the coverage
 * action. Kept JSX-free so the node test harness server-renders the real component the
 * workspace mounts, not a copy.
 */
export function MetadataOnlyNotice(
  { works }: { works: readonly MetadataOnlyWork[] },
): ReactNode {
  return h(
    "div",
    { className: "trust-notice", role: "note", "data-testid": "metadata-only-notice",
      "aria-label": METADATA_ONLY_HEADING },
    h("b", null, METADATA_ONLY_HEADING),
    h("p", { className: "sub" }, METADATA_ONLY_BODY),
    works.length > 0
      ? h(
          "details",
          null,
          h("summary", null, METADATA_ONLY_DISCLOSURE),
          h("ul", null, works.slice(0, 10).map((work) =>
            h(
              "li",
              { key: work.work },
              h("a", { href: `/${work.work.replace(":", "/")}` }, work.title || work.work),
              " ",
              h("span", { className: "sub mono" }, `${work.work} · matched in metadata`),
            ))),
        )
      : null,
    h("p", { className: "sub" },
      h("a", { href: "/coverage" }, "View coverage and known gaps")),
  );
}
