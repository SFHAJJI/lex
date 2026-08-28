import { createElement as h, Fragment, type ReactNode } from "react";
import {
  UNKNOWN_WORK_BODY, UNKNOWN_WORK_CANDIDATES_HEADING, UNKNOWN_WORK_HEADING,
  workCandidateHref, workCandidatesFrom,
} from "./workCandidates.ts";

/**
 * The status-explanation path of the Gap surface. For unknown_work the primary Decision 41
 * notice with the frozen heading and complete body renders for EVERY such gap, zero or five
 * candidates: the candidate list is optional evidence, the notice is not, and the mapper's
 * bald non-holding sentence is suppressed so the honest boundary is never contradicted
 * beside it. Any other status keeps the mapper explanation. Kept JSX-free so the node test
 * harness server-renders the real component, not a copy.
 */
export function GapExplanation({ status, explanation, candidates }: {
  status: string; explanation: string; candidates?: unknown;
}): ReactNode {
  if (status !== "unknown_work") return h("p", null, explanation);
  // Decision 41: candidates validated fail closed; links rebuilt from validated coordinates.
  const heldCandidates = workCandidatesFrom(candidates);
  return h(
    "div",
    { className: "trust-notice", role: "note", "data-testid": "unknown-work-notice",
      "aria-label": UNKNOWN_WORK_HEADING },
    h("b", null, UNKNOWN_WORK_HEADING),
    h("p", { className: "sub" }, UNKNOWN_WORK_BODY),
    heldCandidates.length > 0
      ? h(
          Fragment,
          null,
          h("p", { className: "sub" }, h("b", null, UNKNOWN_WORK_CANDIDATES_HEADING)),
          h("ul", null, heldCandidates.map((candidate) =>
            h(
              "li",
              { key: `${candidate.publisher}:${candidate.work}` },
              h("a", { href: workCandidateHref(candidate) }, candidate.title ?? candidate.work),
              " ",
              h("span", { className: "sub mono" },
                `${candidate.work} · ${candidate.publisher}`),
            ))),
        )
      : null,
  );
}
