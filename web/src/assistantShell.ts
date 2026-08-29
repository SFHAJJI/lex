import { provisionItemsOf, type UiEffect } from "./api.ts";
import type { State } from "./state";

export const STARTER_PROMPTS = [
  "Show Article 6 of the GDPR as it stood on 1 January 2021.",
  "Compare Article 92 of the CRR between 1 January 2020 and 31 December 2024.",
  "When did Article 92 of the CRR change?",
  "Which Luxembourg and EU laws changed most during 2024?",
];

export interface AssistantPanelState { open: boolean; minimized: boolean }

/** An untrusted boundary value licenses a completeness claim only when it is exactly false. */
export function reportedTruncation(value: unknown): boolean | undefined {
  return value === true ? true : value === false ? false : undefined;
}

/** Only an unbounded provision effect may seed the reader without a follow-up fetch. */
export function assistantProvisionLoad(ui?: UiEffect) {
  const provision = ui?.provision;
  if (!provision || provision.truncated || provision.text_truncated || provision.outline_only)
    return undefined;
  const evidence = provision.evidence?.[0];
  return {
    items: provisionItemsOf(provision),
    from: provision.valid_from,
    to: provision.valid_to,
    ...(evidence?.source_uri ? { source: evidence.source_uri } : {}),
    ...(evidence?.extraction_profile ? { profile: evidence.extraction_profile } : {}),
    ...(provision.text_completeness
      ? { textCompleteness: provision.text_completeness }
      : {}),
  };
}

/** Authoritative timeline rows can paint the rail while the normal workspace refresh catches up. */
export function assistantTimelineSeed(ui?: UiEffect) {
  const timeline = ui?.timeline;
  if (!timeline) return undefined;
  return {
    versions: [...new Set(timeline.rows.map((row) => row.valid_from))].sort(),
    languages: [...new Set(timeline.rows.map((row) => row.language).filter(
      (value): value is string => Boolean(value)))].sort(),
    total: timeline.total_count,
    truncated: reportedTruncation(timeline.truncated),
  };
}

export function assistantTimelineRows(ui?: UiEffect) {
  const rows = ui?.timeline?.rows ?? [];
  const dates = new Map<string, number>();
  for (const row of rows) dates.set(row.valid_from, (dates.get(row.valid_from) ?? 0) + 1);
  return rows.map((row, index) => ({
    ...row,
    key: `${row.lex_id ?? row.permalink ?? row.record_sha256 ?? row.valid_from}:${index}`,
    canOpenByDate: dates.get(row.valid_from) === 1,
  }));
}

export function parseAssistantPanelState(raw: string | null): AssistantPanelState {
  // No stored state means a first arrival, and the assistant is the product. It opens, and it
  // animates in rather than being there already. A reader who closes it is remembered, because
  // the stored state is then an explicit false rather than an absent value.
  if (!raw) return { open: true, minimized: false };
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

/** Every typed operation defines a complete workspace scope, including cleared old state. */
export function assistantWorkspaceState(ui?: UiEffect): Partial<State> | undefined {
  if (!ui) return undefined;
  const gapSubject = ui.gap?.work ? {
    work: ui.gap.work,
    date: ui.gap.date,
    anchor: ui.gap.provision_gaps?.length === 1
      ? ui.gap.provision_gaps[0]?.anchor
      : undefined,
    language: undefined,
  } : undefined;
  const legalSubject = ui.diff?.subject ?? ui.provision?.subject
    ?? ui.history?.subject ?? ui.timeline?.subject ?? gapSubject;
  if (legalSubject?.work) return {
    space: "law", q: undefined, asOf: undefined,
    work: legalSubject.work,
    date: ui.diff?.from_date ?? (ui.provision
      ? legalSubject.date ?? ui.provision.valid_from
      : ui.gap?.date ?? legalSubject.date),
    to: ui.diff?.to_date, anchor: legalSubject.anchor ?? ui.history?.anchor,
    mode: ui.diff ? "compare" : "read",
    from: undefined, until: undefined, order: undefined, retrieval: undefined,
    jurisdiction: undefined, hierarchy: undefined, domain: undefined,
    sourceClass: undefined, actForm: undefined, bindingStatus: undefined,
    language: legalSubject.language,
  };
  if (ui.workspace?.work) return {
    space: "law", q: undefined, asOf: undefined,
    work: ui.workspace.work, date: ui.workspace.date, to: undefined,
    anchor: ui.workspace.anchor, mode: "read",
    from: undefined, until: undefined, order: undefined, retrieval: undefined,
    jurisdiction: undefined, hierarchy: undefined, domain: undefined,
    sourceClass: undefined, actForm: undefined, bindingStatus: undefined,
    language: ui.workspace.language,
  };
  if (!ui.ranking && !ui.in_force && !ui.workspace) return undefined;
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
  const state = assistantWorkspaceState(ui);
  return state ? workspaceUrl({
    space: state.space, q: state.q, asOf: state.asOf, work: state.work,
    date: state.date, to: state.to, anchor: state.anchor,
    mode: state.mode === "compare" ? state.mode : undefined,
    from: state.from, until: state.until, order: state.order,
    jurisdiction: state.jurisdiction, hierarchy: state.hierarchy, domain: state.domain,
    sourceClass: state.sourceClass, actForm: state.actForm,
    bindingStatus: state.bindingStatus, language: state.language,
  }) : undefined;
}

export function stepWorkspaceUrl(step: { work?: string; date?: string; anchor?: string }): string | undefined {
  return step.work ? workspaceUrl({
    space: "law", work: step.work, date: step.date, anchor: step.anchor,
  }) : undefined;
}
