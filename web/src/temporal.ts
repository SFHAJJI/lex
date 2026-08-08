/**
 * EUR-Lex expression dates in this corpus identify official consolidated wording states. They
 * are not, in general, the act's entry-into-force or application dates. Luxembourg's Legilux
 * timeline is publisher applicability data. Keeping the distinction here prevents presentation
 * code from turning one kind of legal time into the other.
 */
export const usesPublisherVersionDates = (work: string, semantics?: string) =>
  semantics ? semantics === "official_consolidation_state" : work.startsWith("eu-eurlex:");

export function intervalLabel(work: string, from: string, to?: string, semantics?: string): string {
  return usesPublisherVersionDates(work, semantics)
    ? `publisher version ${from} → ${to ?? "latest held"}`
    : `in force ${from} → ${to ?? "open"}`;
}

export function temporalStatusLabel(work: string, to?: string, semantics?: string): string {
  if (usesPublisherVersionDates(work, semantics))
    return to ? "earlier publisher version" : "latest publisher version";
  return to ? "superseded" : "in force";
}

export function futureStateLabel(work: string, count: number, semantics?: string): string {
  return usesPublisherVersionDates(work, semantics)
    ? `${count} publisher version${count === 1 ? "" : "s"} dated after today`
    : `${count} not yet in force`;
}

export const latestStateLabel = (work: string, semantics?: string) =>
  usesPublisherVersionDates(work, semantics) ? "latest held" : "today";

export const evidenceIntervalLabel = (work: string, from: string, to?: string, semantics?: string) =>
  usesPublisherVersionDates(work, semantics)
    ? `publisher version dated ${from}${to ? ` to ${to}` : ""}`
    : `version in force from ${from}${to ? ` to ${to}` : ""}`;

export const evidenceIntervalField = (work: string, from: string, to?: string, semantics?: string) =>
  usesPublisherVersionDates(work, semantics)
    ? `Publisher version date: ${from} to ${to ?? "latest held"}`
    : `Version in force: ${from} to ${to ?? "open"}`;
