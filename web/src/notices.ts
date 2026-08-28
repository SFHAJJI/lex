/**
 * Phase 0 trust notices, workspace side (Decision 41). The browser never infers legal state: it
 * renders a frozen disclosure when typed, server-provided facts satisfy the condition. The
 * historical-density condition uses only the server-echoed report window and the jurisdictions
 * of the rows the server returned.
 */

/** Luxembourg holds fewer dated consolidation states before this date (Decision 41 copy). */
export const LU_DENSITY_BOUNDARY = "2017-01-01";

/**
 * True when the "what changed" report window reaches before 2017 while Luxembourg law is in
 * scope: either the reader scoped the report to Luxembourg, or the server returned at least one
 * Luxembourg-owned row. Both inputs are server-provided typed facts, never browser inference.
 */
export function historicalDensityApplies(
  fromDate: string,
  scopedJurisdiction: string | undefined,
  rowJurisdictions: (string | undefined)[],
): boolean {
  if (!fromDate || fromDate >= LU_DENSITY_BOUNDARY) return false;
  const lu = (value: string | undefined) => {
    const v = (value ?? "").toLowerCase();
    return v === "lu" || v === "lu-legilux" || v === "luxembourg";
  };
  if (scopedJurisdiction !== undefined) return lu(scopedJurisdiction);
  return rowJurisdictions.some(lu);
}

/** The frozen Decision 41 copy for the historical_density notice. */
export const HISTORICAL_DENSITY = {
  heading: "Historical coverage is less dense",
  body:
    "For Luxembourg periods before 2017, Lex holds fewer dated consolidation states. This " +
    "result counts changes observed in held states, not every legal change. A lower count may " +
    "reflect coverage.",
  actions: [
    { label: "View coverage for this period", href: "/coverage" },
    { label: "Open the official publisher", href: "https://legilux.public.lu" },
  ],
} as const;
