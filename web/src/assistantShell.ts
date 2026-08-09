import type { UiEffect } from "./api";
import type { State } from "./state";

export const STARTER_PROMPTS = [
  "Show Article 6 of the GDPR as it stood on 1 January 2021.",
  "Compare Article 92 of the CRR between 1 January 2020 and 31 December 2024.",
  "When did Article 92 of the CRR change?",
  "Which Luxembourg and EU laws changed most during 2024?",
];

export interface AssistantPanelState { open: boolean; minimized: boolean }

export function parseAssistantPanelState(raw: string | null): AssistantPanelState {
  if (!raw) return { open: false, minimized: false };
  try {
    const value = JSON.parse(raw) as Partial<AssistantPanelState>;
    const open = value.open === true;
    return { open, minimized: open && value.minimized === true };
  } catch {
    return { open: false, minimized: false };
  }
}

function workspaceUrl(values: Record<string, string | undefined>): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) if (value) query.set(key, value);
  return `/?${query.toString()}`;
}

/** Aggregate and filter tools define a complete workspace scope, including cleared old state. */
export function assistantWorkspaceState(ui?: UiEffect): Partial<State> | undefined {
  if (!ui || (!ui.ranking && !ui.in_force && !ui.workspace)) return undefined;
  const ranking = ui.ranking;
  const workspace = ui.workspace;
  return {
    space: ranking ? "time" : "search", q: workspace?.query, asOf: ui.in_force?.date,
    work: undefined, date: undefined, to: undefined, anchor: undefined, mode: "read",
    from: ranking?.from_date, until: ranking?.to_date,
    order: ranking?.order as State["order"],
    retrieval: undefined,
    jurisdiction: workspace?.jurisdiction, hierarchy: workspace?.hierarchy,
    domain: workspace?.domain, sourceClass: workspace?.source_class,
    actForm: workspace?.act_form, bindingStatus: workspace?.binding_status,
    language: workspace?.language,
  };
}

/** Maps only typed, server-validated effects to the workspace. Model prose is never a URL. */
export function assistantWorkspaceUrl(ui?: UiEffect): string | undefined {
  if (!ui) return undefined;
  if (ui.diff?.subject.work) return workspaceUrl({
    space: "law", work: ui.diff.subject.work, date: ui.diff.from_date, to: ui.diff.to_date,
    anchor: ui.diff.subject.anchor, mode: "compare",
  });
  const legalSubject = ui.provision?.subject ?? ui.history?.subject ?? ui.timeline?.subject;
  if (legalSubject?.work) return workspaceUrl({
    space: "law", work: legalSubject.work,
    date: ui.provision ? legalSubject.date ?? ui.provision.valid_from : legalSubject.date,
    anchor: legalSubject.anchor ?? ui.history?.anchor,
  });
  if (ui.ranking || ui.in_force || ui.workspace) {
    const state = assistantWorkspaceState(ui)!;
    return workspaceUrl({
      space: state.space, asOf: state.asOf, from: state.from, until: state.until,
      order: state.order,
      jurisdiction: state.jurisdiction, hierarchy: state.hierarchy, domain: state.domain,
      sourceClass: state.sourceClass, actForm: state.actForm,
      bindingStatus: state.bindingStatus, language: state.language,
    });
  }
  return undefined;
}

export function stepWorkspaceUrl(step: { work?: string; date?: string; anchor?: string }): string | undefined {
  return step.work ? workspaceUrl({
    space: "law", work: step.work, date: step.date, anchor: step.anchor,
  }) : undefined;
}
