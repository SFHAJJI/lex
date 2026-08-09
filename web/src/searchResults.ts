export type SearchWork = {
  work: string;
  title: string;
  jurisdiction?: string;
};

export type SearchPassage = SearchWork & {
  anchor: string;
  validFrom: string;
};

export type GroupedSearchWork<W extends SearchWork, P extends SearchPassage> = W & {
  passages: P[];
};

export type SearchJurisdiction<W extends SearchWork, P extends SearchPassage> = {
  jurisdiction: string;
  works: GroupedSearchWork<W, P>[];
};

/**
 * Jurisdiction is the only disjoint result partition. Passages are children of the law that
 * contains them; they are not a second competing result list. Order follows the fused ranking,
 * and a passage-only work remains reachable even if the service did not emit a separate work hit.
 */
export function groupSearchResults<W extends SearchWork, P extends SearchPassage>(
  works: W[], passages: P[],
): SearchJurisdiction<W, P>[] {
  const sections = new Map<string, SearchJurisdiction<W, P>>();
  const groupedWorks = new Map<string, GroupedSearchWork<W, P>>();

  const ensure = (candidate: W): GroupedSearchWork<W, P> => {
    const existing = groupedWorks.get(candidate.work);
    if (existing) return existing;
    const jurisdiction = candidate.jurisdiction || "Other";
    let section = sections.get(jurisdiction);
    if (!section) {
      section = { jurisdiction, works: [] };
      sections.set(jurisdiction, section);
    }
    const grouped = { ...candidate, passages: [] } as GroupedSearchWork<W, P>;
    section.works.push(grouped);
    groupedWorks.set(candidate.work, grouped);
    return grouped;
  };

  works.forEach(ensure);
  for (const passage of passages)
    ensure(passage as unknown as W).passages.push(passage);

  return [...sections.values()];
}
