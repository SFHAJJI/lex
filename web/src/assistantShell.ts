import type { UiEffect } from "./api";

export const STARTER_PROMPTS = [
  "Show Article 6 of the GDPR as it stood on 1 January 2021.",
  "Compare Article 92 of the CRR between 2020 and 2024.",
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

/** Maps only typed, server-validated effects to the workspace. Model prose is never a URL. */
export function assistantWorkspaceUrl(ui?: UiEffect): string | undefined {
  if (!ui) return undefined;
  if (ui.diff?.subject.work) return workspaceUrl({
    space: "law", work: ui.diff.subject.work, date: ui.diff.from_date, to: ui.diff.to_date,
    anchor: ui.diff.subject.anchor, mode: "compare",
  });
  const legal = ui.provision ?? ui.history;
  if (legal?.subject.work) return workspaceUrl({
    space: "law", work: legal.subject.work,
    date: "valid_from" in legal ? legal.subject.date ?? legal.valid_from : legal.subject.date,
    anchor: legal.subject.anchor ?? ("anchor" in legal ? legal.anchor : undefined),
  });
  if (ui.ranking) return workspaceUrl({
    space: "time", from: ui.ranking.from_date, until: ui.ranking.to_date,
    order: ui.ranking.order,
  });
  if (ui.in_force) return workspaceUrl({ space: "search", asOf: ui.in_force.date });
  return undefined;
}

export function stepWorkspaceUrl(step: { work?: string; date?: string; anchor?: string }): string | undefined {
  return step.work ? workspaceUrl({
    space: "law", work: step.work, date: step.date, anchor: step.anchor,
  }) : undefined;
}
