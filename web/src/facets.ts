/**
 * Filter vocabulary emitted by the mounted indexes with the page that mounts the workspace.
 * The client owns friendly labels, never the inventory: adding a reviewed publisher or legal
 * class must not require a second hard-coded scope list in React.
 */
export type FacetValues = {
  hierarchies: string[];
  domains: string[];
  source_classes: string[];
  act_forms: string[];
  binding_statuses: string[];
  languages: string[];
};

export type JurisdictionFacet = FacetValues & { value: string; code: string; publisher?: string };
export type SearchFacets = FacetValues & { jurisdictions: JurisdictionFacet[] };

const EMPTY_VALUES: FacetValues = {
  hierarchies: [], domains: [], source_classes: [], act_forms: [],
  binding_statuses: [], languages: [],
};

export const SEARCH_FACETS: SearchFacets = (() => {
  const empty: SearchFacets = { jurisdictions: [], ...EMPTY_VALUES };
  try {
    const text = document.getElementById("search-facets")?.textContent;
    return text ? { ...empty, ...JSON.parse(text) } : empty;
  } catch {
    return empty;
  }
})();

/** Values relevant to the selected jurisdiction; the union when none is selected. */
export function scopedFacets(jurisdiction?: string): FacetValues {
  if (!jurisdiction) return SEARCH_FACETS;
  return SEARCH_FACETS.jurisdictions.find((item) =>
    item.value.toLowerCase() === jurisdiction.toLowerCase()
    || item.code.toLowerCase() === jurisdiction.toLowerCase())
    ?? EMPTY_VALUES;
}

/** Canonical filter value for URL/UI state; accepts the code casing an assistant may emit. */
export const jurisdictionValue = (value?: string) => value
  ? SEARCH_FACETS.jurisdictions.find((item) =>
      item.value.toLowerCase() === value.toLowerCase()
      || item.code.toLowerCase() === value.toLowerCase())?.value ?? value
  : undefined;

const LABELS: Record<string, string> = {
  primary_eu_law: "EU primary law", secondary_eu_law: "EU secondary law",
  "financial-services": "Financial services", "aml-corporate": "AML and corporate",
  "consumer-environment": "Consumer and environment", "procurement-and-ip": "Procurement and IP",
  "judicial-cooperation": "Judicial cooperation",
  REG: "Regulation", REG_DEL: "Delegated regulation", REG_IMPL: "Implementing regulation",
  DIR: "Directive", DIR_DEL: "Delegated directive", DIR_IMPL: "Implementing directive",
  DEC: "Decision", DEC_DEL: "Delegated decision", DEC_IMPL: "Implementing decision",
  DEC_ENTSCHEID: "Decision", TREATY: "Treaty", CORRIGENDUM: "Corrigendum", CHARTER: "Charter",
  A: "Order", AGC: "Government-in-Council order", AGD: "Grand-ducal order",
  AMIN: "Ministerial order", ARGD: "Royal grand-ducal order", CODE: "Code",
  CODE_RECUEIL: "Code collection", CONV: "Convention", Constitution: "Constitution",
  DIV: "Other", LOI: "Law", ORD: "Ordinance", PA: "Administrative publication",
  PROT: "Protocol", RBCL: "Central Bank of Luxembourg regulation",
  RECUEIL: "Thematic collection", RGC: "Government-in-Council regulation",
  RGD: "Grand-ducal regulation", RI: "Internal rules", RMIN: "Ministerial regulation",
  ST: "Statutes", TC: "Consolidated text",
  in_force: "In force", not_in_force: "Not in force", unknown: "Not classified",
};

export const facetLabel = (value: string) => LABELS[value]
  ?? value.replaceAll("_", " ").replaceAll("-", " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());

export const jurisdictionLabel = (code: string) => code.toUpperCase() === "EU"
  ? "European Union"
  : new Intl.DisplayNames(["en"], { type: "region" }).of(code.toUpperCase()) ?? code;

/** Resolve a work's publisher through server-emitted metadata, never through known ID prefixes. */
export const jurisdictionForPublisher = (publisher: string) =>
  SEARCH_FACETS.jurisdictions.find((item) => item.publisher === publisher)?.code;

export const languageLabel = (code: string) =>
  new Intl.DisplayNames(["en"], { type: "language" }).of(code) ?? code;
