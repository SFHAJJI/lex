import { facetLabel, jurisdictionLabel, jurisdictionValue, languageLabel, scopedFacets, SEARCH_FACETS } from "./facets";
import type { State } from "./state";

type Values = Pick<State, "jurisdiction" | "hierarchy" | "domain" | "sourceClass" |
  "actForm" | "bindingStatus" | "language">;

export function ScopeFilters({ values, onChange, summary = "Narrow results",
                               showJurisdiction = true,
                               sourceDefault = "Every source class" }: {
  values: Values;
  onChange: (next: Partial<State>) => void;
  summary?: string;
  showJurisdiction?: boolean;
  sourceDefault?: string;
}) {
  const facets = scopedFacets(values.jurisdiction);
  const active = [showJurisdiction ? values.jurisdiction : undefined, values.hierarchy,
    values.domain, values.sourceClass, values.actForm, values.bindingStatus, values.language]
    .filter(Boolean).length;

  return (
    <details className="search-filters">
      <summary>{summary}{active ? ` (${active} active)` : ""}</summary>
      <div className="filter-grid">
        {showJurisdiction ? (
          <label><span>Jurisdiction</span>
            <select name="jurisdiction" className="reslayer" value={jurisdictionValue(values.jurisdiction) ?? ""}
                    onChange={(e) => onChange({
                      jurisdiction: e.target.value || undefined,
                      hierarchy: undefined, domain: undefined, sourceClass: undefined,
                      actForm: undefined, bindingStatus: undefined, language: undefined,
                    })}>
              <option value="">Every jurisdiction</option>
              {SEARCH_FACETS.jurisdictions.map((item) =>
                <option key={item.value} value={item.value}>{jurisdictionLabel(item.code)}</option>)}
            </select>
          </label>
        ) : null}
        <label><span>Hierarchy</span>
          <select name="hierarchy" className="reslayer" value={values.hierarchy ?? ""}
                  onChange={(e) => onChange({ hierarchy: e.target.value || undefined })}>
            <option value="">Every hierarchy</option>
            {facets.hierarchies.map((value) =>
              <option key={value} value={value}>{facetLabel(value)}</option>)}
          </select>
        </label>
        <label><span>Practice area</span>
          <select name="domain" className="reslayer" value={values.domain ?? ""}
                  onChange={(e) => onChange({ domain: e.target.value || undefined })}>
            <option value="">Every practice area</option>
            {facets.domains.map((value) =>
              <option key={value} value={value}>{facetLabel(value)}</option>)}
          </select>
        </label>
        <label><span>Source class</span>
          <select name="source-class" className="reslayer" value={values.sourceClass ?? ""}
                  onChange={(e) => onChange({ sourceClass: e.target.value || undefined })}>
            <option value="">{sourceDefault}</option>
            {facets.source_classes.map((value) =>
              <option key={value} value={value}>{facetLabel(value)}</option>)}
          </select>
        </label>
        <label><span>Legal form</span>
          <select name="act-form" className="reslayer" value={values.actForm ?? ""}
                  onChange={(e) => onChange({ actForm: e.target.value || undefined })}>
            <option value="">Every legal form</option>
            {facets.act_forms.map((value) =>
              <option key={value} value={value}>{facetLabel(value)}</option>)}
          </select>
        </label>
        <label><span>Legal status</span>
          <select name="binding-status" className="reslayer" value={values.bindingStatus ?? ""}
                  onChange={(e) => onChange({ bindingStatus: e.target.value || undefined })}>
            <option value="">Every status</option>
            {facets.binding_statuses.map((value) =>
              <option key={value} value={value}>{facetLabel(value)}</option>)}
          </select>
        </label>
        <label><span>Language</span>
          <select name="language" className="reslayer" value={values.language ?? ""}
                  onChange={(e) => onChange({ language: e.target.value || undefined })}>
            <option value="">Every language</option>
            {facets.languages.map((value) =>
              <option key={value} value={value}>{languageLabel(value)}</option>)}
          </select>
        </label>
      </div>
    </details>
  );
}
