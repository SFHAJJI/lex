// The workspace is itself an MCP client: mode switches call the public /mcp endpoint
// directly, with no model in the loop. Only free text goes to /api/ask. That keeps
// "play with it" instant and deterministic — and it means the demo is the strongest
// possible proof that the published API actually works.

import { MAX_PRODUCER_COUNT } from "./searchPopulation.ts";

let id = 0;

async function mcpJson(r: Response): Promise<any> {
  const body = await r.text();
  if (!r.headers.get("content-type")?.startsWith("text/event-stream")) return JSON.parse(body);
  const data = body.split("\n").filter(line => line.startsWith("data: ")).at(-1)?.slice(6);
  if (!data) throw new Error("MCP event stream returned no data event");
  return JSON.parse(data);
}

export async function tool<T = any>(name: string, args: Record<string, unknown>): Promise<T> {
  const r = await fetch("/mcp", {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json, text/event-stream" },
    body: JSON.stringify({ jsonrpc: "2.0", id: ++id, method: "tools/call", params: { name, arguments: args } }),
  });
  if (!r.ok) throw new Error(`tool ${name} failed (${r.status})`);
  const j = await mcpJson(r);
  const text = j?.result?.content?.[0]?.text;
  if (typeof text !== "string") throw new Error(`tool ${name} returned no content`);
  return JSON.parse(text) as T;
}

/** Tools answer per mounted index; take the first envelope that carries something. */
export function first<T extends object>(res: T | T[], has: (x: T) => boolean): T | undefined {
  const list = Array.isArray(res) ? res : [res];
  return list.find(has) ?? list[0];
}

export interface AskReply {
  reply: string;
  /** Opaque, process-local bearer for the next turn; never persist or place in a URL. */
  thread_token?: string;
  trace?: Record<string, unknown>[];
  ui?: UiEffect;
  operations?: OperationReply[];
  clarification?: AskClarification;
  error?: string;
  model_usage?: { input_tokens?: number; output_tokens?: number; total_tokens?: number };
  model_identity?: { resource_host?: string; deployment?: string };
  timing?: { planner_ms?: number; mcp_ms?: number; synthesis_ms?: number | null };
  /** False when the answer was a refusal: its steps are withheld from the transcript. */
  narrated?: boolean;
}
export interface OperationReply {
  operation_id: string;
  order: number;
  tool?: string;
  result_class?: string;
  disposition?: string;
  legal_outcome: string;
  transport_outcome: string;
  effects: string[];
  ui?: UiEffect;
}

export interface AskExecutionDetails {
  /** Deterministic identity resolution, decided in code before any model was called. */
  subject_resolution?: {
    status: string;
    works: string[];
    article_number?: string;
    authority_sources?: { work: string; kind: string }[];
    runner_up?: string;
  };
  /** The exact server-validated operation_plan trace object returned by /api/ask/stream. */
  operation_plan: Record<string, unknown> | null;
  /** Compact terminal outcomes; the potentially large legal/UI payload remains in the answer. */
  operation_outcomes: {
    operation_id: string;
    order: number;
    tool?: string;
    result_class?: string;
    disposition?: string;
    legal_outcome: string;
    transport_outcome: string;
    effects: string[];
  }[];
  /** What the optional prose layer committed to, never the prose itself. */
  synthesis?: {
    status: string;
    draft_status?: string;
    claims: { kind: string; evidence_ids: string[] }[];
    permalink_count: number;
    judge?: { disposition: string; issue_count: number };
  };
  model_usage?: { input_tokens: number; output_tokens: number; total_tokens: number };
  model_identity?: { resource_host: string; deployment: string };
  timing?: { planner_ms: number; mcp_ms: number; synthesis_ms?: number };
}

const boundedText = (value: unknown, maximum = 200) =>
  typeof value === "string" && value.length > 0 && value.length <= maximum ? value : undefined;
const boundedNumber = (value: unknown) =>
  typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : undefined;
const boundedInteger = (value: unknown) =>
  typeof value === "number" && Number.isSafeInteger(value) && value >= 0 ? value : undefined;
const asArray = (value: unknown): unknown[] => (Array.isArray(value) ? value : []);
const asObject = (value: unknown): Record<string, unknown> | undefined =>
  value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown> : undefined;
const boundedStrings = (value: unknown, cap: number) => asArray(value)
  .map((item) => boundedText(item))
  .filter((item): item is string => item !== undefined).slice(0, cap);
const tracePhase = (reply: AskReply, phase: string) => asArray(reply.trace)
  .map((entry) => asObject(entry))
  .find((entry) => entry?.phase === phase);

/**
 * Exposes the subject resolution decided in code, the exact frozen operation plan with the
 * repairs that froze it, a deliberately compact terminal outcome summary, and the contract
 * outcome of the optional prose layer. Reply prose, claim text, legal/UI payloads and the
 * thread capability are not duplicated in the disclosure.
 */
export function executionDetails(reply: AskReply): AskExecutionDetails | undefined {
  const rawPlan = tracePhase(reply, "operation_plan");
  const rawSubject = tracePhase(reply, "subject_resolution");
  const subjectStatus = boundedText(rawSubject?.status);
  const articleNumber = boundedText(rawSubject?.article_number);
  const runnerUp = boundedText(rawSubject?.runner_up);
  const authoritySources = asArray(rawSubject?.authority_sources).slice(0, 8).flatMap((value) => {
    const source = asObject(value);
    const work = boundedText(source?.work);
    const kind = boundedText(source?.kind);
    return work && kind ? [{ work, kind }] : [];
  });
  const subjectResolution = subjectStatus ? {
    status: subjectStatus,
    works: boundedStrings(rawSubject?.works, 8),
    ...(articleNumber ? { article_number: articleNumber } : {}),
    ...(authoritySources.length > 0 ? { authority_sources: authoritySources } : {}),
    ...(runnerUp ? { runner_up: runnerUp } : {}),
  } : undefined;
  const rawSynthesis = tracePhase(reply, "synthesis");
  const synthesisStatus = boundedText(rawSynthesis?.status);
  const draftStatus = boundedText(rawSynthesis?.draft_status);
  const rawJudge = asObject(rawSynthesis?.judge);
  const judgeDisposition = boundedText(rawJudge?.disposition);
  const synthesis = synthesisStatus ? {
    status: synthesisStatus,
    ...(draftStatus ? { draft_status: draftStatus } : {}),
    claims: asArray(rawSynthesis?.claims).slice(0, 32).flatMap((value) => {
      const claim = asObject(value);
      const kind = boundedText(claim?.kind);
      return kind ? [{ kind, evidence_ids: boundedStrings(claim?.evidence_ids, 8) }] : [];
    }),
    // Counted, never rendered. The answer contract bounds a permalink at 2,000 characters, so
    // the 200-character bound the label fields use would undercount long ones to zero.
    permalink_count: asArray(rawSynthesis?.permalinks).slice(0, 64)
      .filter((link) => boundedText(link, 2_000) !== undefined).length,
    ...(judgeDisposition ? {
      judge: {
        disposition: judgeDisposition,
        issue_count: boundedInteger(rawJudge?.issue_count) ?? 0,
      },
    } : {}),
  } : undefined;
  const replyOperations: unknown[] = Array.isArray(reply.operations) ? reply.operations : [];
  const outcomes = replyOperations.slice(0, 8).flatMap((value) => {
    if (!value || typeof value !== "object" || Array.isArray(value)) return [];
    const operation = value as Record<string, unknown>;
    const operationId = boundedText(operation.operation_id);
    const order = boundedInteger(operation.order);
    const legalOutcome = boundedText(operation.legal_outcome);
    const transportOutcome = boundedText(operation.transport_outcome);
    if (!operationId || order === undefined || !legalOutcome || !transportOutcome) return [];
    const tool = boundedText(operation.tool);
    const resultClass = boundedText(operation.result_class);
    const disposition = boundedText(operation.disposition);
    return [{
      operation_id: operationId,
      order,
      ...(tool ? { tool } : {}),
      ...(resultClass ? { result_class: resultClass } : {}),
      ...(disposition ? { disposition } : {}),
      legal_outcome: legalOutcome,
      transport_outcome: transportOutcome,
      effects: (Array.isArray(operation.effects) ? operation.effects : [])
        .map((effect) => boundedText(effect))
        .filter((effect): effect is string => effect !== undefined).slice(0, 16),
    }];
  });
  const inputTokens = boundedInteger(reply.model_usage?.input_tokens);
  const outputTokens = boundedInteger(reply.model_usage?.output_tokens);
  const totalTokens = boundedInteger(reply.model_usage?.total_tokens);
  const resourceHost = boundedText(reply.model_identity?.resource_host);
  const deployment = boundedText(reply.model_identity?.deployment);
  const plannerMs = boundedNumber(reply.timing?.planner_ms);
  const mcpMs = boundedNumber(reply.timing?.mcp_ms);
  const synthesisMs = boundedNumber(reply.timing?.synthesis_ms);
  if (!rawPlan && outcomes.length === 0) return undefined;
  return {
    ...(subjectResolution ? { subject_resolution: subjectResolution } : {}),
    operation_plan: rawPlan ?? null,
    operation_outcomes: outcomes,
    ...(synthesis ? { synthesis } : {}),
    model_usage: inputTokens !== undefined && outputTokens !== undefined
      && totalTokens !== undefined
      ? { input_tokens: inputTokens, output_tokens: outputTokens, total_tokens: totalTokens }
      : undefined,
    model_identity: resourceHost && deployment
      ? { resource_host: resourceHost, deployment }
      : undefined,
    timing: plannerMs !== undefined && mcpMs !== undefined
      ? { planner_ms: plannerMs, mcp_ms: mcpMs,
          ...(synthesisMs !== undefined ? { synthesis_ms: synthesisMs } : {}) }
      : undefined,
  };
}

/** Preserve every terminal operation view in user order for compound requests. */
export function compoundOperationViews(reply: AskReply): OperationReply[] {
  const visible = (reply.operations ?? [])
    .filter((operation): operation is OperationReply & { ui: UiEffect } => !!operation.ui)
    .sort((left, right) => left.order - right.order);
  return visible.length > 1 ? visible : [];
}

export function signatureStatusLabel(value: boolean | undefined): string {
  return value === true ? "signature verified"
    : value === false ? "signature verification failed"
    : "signature unavailable";
}

export function safeHttpsUrl(...candidates: (string | undefined)[]): string | undefined {
  for (const candidate of candidates) {
    if (!candidate) continue;
    try {
      const parsed = new URL(candidate);
      if (parsed.protocol === "https:") return parsed.href;
    } catch { /* Relative and malformed values are not external links. */ }
  }
  return undefined;
}

export class AssistantResponseError extends Error {
  readonly status?: number;
  constructor(message: string, status?: number) {
    super(message);
    this.status = status;
  }
}

function boundedAssistantError(value: unknown, fallback: string): string {
  const error = typeof value === "string" ? value.trim() : "";
  return error.length > 0 && error.length <= 200 ? error : fallback;
}

export function populationScopeLabel(value: number | undefined): string | undefined {
  return value === undefined ? undefined : `${value.toLocaleString()} works in selected scope`;
}

/**
 * in_force_on publishes `works_covered` from `Coverage(1).Groups`, which counts a publisher's
 * versioned works and is NOT narrowed by the request's metadata filters, unlike the ranking
 * surface's filter-aware `works_in_scope`. Reusing "in selected scope" here would overstate what
 * was actually covered under any filter, which is the false denominator that trust rule 6 and
 * never-implied rule 7 exist to prevent. The producer publishes its own `basis` string for
 * exactly this reason, so it is rendered instead of a basis the browser invents.
 */
/**
 * Known exclusions arrive per publisher. The previous union built a Set of the arrays themselves,
 * so identical exclusions across two publishers never de-duplicated and a publisher with none
 * contributed a truthy empty array that rendered as a stray separator under "Known exclusions:".
 * Flattening first makes the Set do the work it was there to do.
 */
/**
 * Bounds on a population disclosure, applied at the point the value crosses into the product.
 *
 * These are not defensive decoration. A population figure is a legal scope claim: it tells the
 * reader how much of the corpus an answer speaks for. Anything that reaches the screen as a
 * denominator has to be a value the producer could actually have minted, and a transport object
 * is not the producer.
 */
export const POPULATION_LIMITS = { maxExclusions: 20, maxExclusionLength: 300 };

/**
 * A count the producer could have minted: a non-negative integer inside its own Int32 range.
 *
 * The ceiling is IMPORTED rather than restated, from the module that owns it, so the three
 * places that had to agree about it now read one value. This helper used to stop at
 * `Number.isSafeInteger`, which admits everything from 2^31 to 2^53: numbers that are exact
 * integers and that `SearchPopulationTotal`, `PopulationTotal` and `Coverage(1).Groups`, all
 * declared `int`, cannot return. A denominator in that range reached the ranking and in-force
 * headers as a legal figure while the same value was refused by the parser one module away.
 */
function populationCount(value: unknown): number | undefined {
  return typeof value === "number" && Number.isSafeInteger(value)
    && value >= 0 && value <= MAX_PRODUCER_COUNT
    ? value : undefined;
}

/**
 * The summed population across publishers, or undefined if it cannot be stated honestly.
 *
 * Refuses rather than coerces. The previous form added `?? 0` per entry, so a string, a fraction,
 * a negative or a missing value silently became zero and shrank a denominator the reader was
 * invited to check an answer against. Overflow refuses too: two individually valid counts can sum
 * past the safe integer range, and a number that has lost precision is not a denominator.
 */
/**
 * The summed value of a top-level count across publishers, or undefined if it cannot be stated.
 *
 * Same refusal as summedPopulation and for the same reason, but for the counts that sit beside the
 * rows rather than inside the population object: works changed, new versions, total works in
 * force. Adding with a zero default let a string, a fraction, a negative or a missing value become
 * a silently smaller legal count, and two individually valid counts can still sum past the safe
 * integer range, where a number has lost precision and is no longer a count.
 *
 * The range is checked HERE as well as at classification, which is a change from what this
 * comment used to claim. It said the producer's range was enforced upstream so this only had to
 * guard the arithmetic. That was true of the entries the governed partition hands it and false of
 * the helper itself, which is exported and took any safe integer: everything from 2^31 to 2^53
 * passed, and those are values `ChangeTotals` and `InForcePage.TotalGroups`, both `int`, cannot
 * return. One constant, imported from the module that owns it, is now read by all three doors.
 */
/**
 * The two change counts, each labelled with the grain the producer actually measured.
 *
 * `IndexReader.ChangeTotals` returns `(int Works, int Versions)` and `McpCore` publishes the first
 * as `works_changed` and the second as `new_versions`. The ranking header rendered the work count
 * as "received publisher versions", which puts a version label on a work count. That is a false
 * dimension rather than a wording preference: a reader comparing the two numbers was comparing
 * versions to versions, and one of them was works.
 *
 * Neutral about what a version means, because that differs between publishers and the timeline
 * semantics are disclosed separately. It says a version was dated in the window, not that any
 * wording changed, which the producer does not claim.
 */
export function changeCountLabels(
  worksChanged: number | undefined, newVersions: number | undefined): string[] {
  const labels: string[] = [];
  if (worksChanged !== undefined)
    labels.push(`${worksChanged.toLocaleString()} work${worksChanged === 1 ? "" : "s"} `
      + "with a new publisher version");
  if (newVersions !== undefined)
    labels.push(`${newVersions.toLocaleString()} publisher version`
      + `${newVersions === 1 ? "" : "s"} dated in this window`);
  return labels;
}

export function summedCount(entries: any[], field: string): number | undefined {
  let total = 0;
  let seen = false;
  for (const entry of entries) {
    const raw = (entry as any)?.[field];
    if (raw === undefined) continue;
    // The same producer range as every other count: `ChangeTotals` is `(int Works, int
    // Versions)` and `InForcePage.TotalGroups` is `int`, so a value above this is one the
    // producer cannot have sent rather than one that is merely large.
    if (typeof raw !== "number" || !Number.isSafeInteger(raw) || raw < 0
      || raw > MAX_PRODUCER_COUNT) return undefined;
    if (raw > Number.MAX_SAFE_INTEGER - total) return undefined;
    total += raw;
    seen = true;
  }
  return seen ? total : undefined;
}

export function summedPopulation(
  entries: any[], field: "works_in_scope" | "works_covered"): number | undefined {
  let total = 0;
  let seen = false;
  for (const entry of entries) {
    const raw = (entry as any)?.population?.[field];
    if (raw === undefined) continue;
    const value = populationCount(raw);
    if (value === undefined) return undefined;
    if (value > Number.MAX_SAFE_INTEGER - total) return undefined;
    total += value;
    seen = true;
  }
  return seen ? total : undefined;
}

/** Bounded, de-duplicated, never interpreted. Overlong or oversized sets are refused, not cut. */
export function unionKnownExclusions(entries: any[]): string[] {
  const all = entries.flatMap((e) => e?.population?.known_exclusions ?? []);
  if (!all.every((x: unknown): x is string =>
    typeof x === "string" && x.trim().length > 0
    && x.length <= POPULATION_LIMITS.maxExclusionLength)) return [];
  const unique = [...new Set(all as string[])];
  return unique.length <= POPULATION_LIMITS.maxExclusions ? unique : [];
}

/**
 * Whether the reader's "exact words" override applies to the query now on screen.
 *
 * Trust rule 9 requires a one-tap revert of any relaxation. The override is bound to the exact
 * query it was chosen for: a reader who turned off spelling fallback for one question has said
 * nothing about the next one, and carrying the decision forward would silently narrow a search
 * they never narrowed. Anything but an exact match resolves to the default.
 */
export function fuzzyModeFor(
  exactQuery: string | undefined, query: string): "auto" | "off" {
  return exactQuery !== undefined && exactQuery === query.trim() ? "off" : "auto";
}


/**
 * The same rule for any state a reader bound to one question: kept while that question is on
 * screen, cleared the moment a different one is submitted.
 *
 * Hiding such a state when the question differs is not the same as clearing it. A hidden value
 * is dormant, and returning to the earlier question later silently reapplies a narrowing the
 * reader authorised once, on a visit they never authorised. That distinction cost this lane two
 * separate defects, one on the exact-words override and one on the publisher metadata filter,
 * so the rule is exported once and both callers use it.
 */
export function retainedForQuery<T extends { query: string }>(
  current: T | undefined, submittedQuery: string): T | undefined {
  // Trimmed on both sides, because that is the identity the request carries and the identity
  // the component is keyed by. Comparing raw strings while the key trims left a gap: a padded
  // resubmission did not remount, so the filter was hidden rather than discarded, and the
  // unpadded question then reactivated it. Three notions of one question is two too many.
  return current !== undefined && current.query.trim() !== submittedQuery.trim()
    ? undefined : current;
}

export function populationCoverageLabel(
  works: number | undefined,
  basis: string | undefined,
  scopeFiltersApplied: boolean,
): string | undefined {
  if (works === undefined) return undefined;
  // Whether the request carried filters is a fact about what this client sent, not an inference
  // about the corpus, so saying so is honest where silently implying the filters narrowed the
  // denominator would not be.
  const counted = `${works.toLocaleString()} works covered`
    + (scopeFiltersApplied ? "" : " before the selected filters");
  const stated = typeof basis === "string" ? basis.trim() : "";
  return stated.length > 0 && stated.length <= 120 ? `${counted}, ${stated}` : counted;
}

/** Page-specific actions are useful only after an answer that did not end in a gap. */
export function shouldOfferContextualFollowUps(reply: AskReply): boolean {
  return !reply.error && !reply.clarification && !reply.ui?.gap;
}
export interface AskMessage {
  role: "user" | "assistant";
  content: string;
  /** Tab-memory-only projection used by the ordinary per-answer execution disclosure. */
  execution?: AskExecutionDetails;
}
export interface ClarificationChoice { label: string; value?: string }
export interface AskClarification {
  question: string;
  options: string[];
  choices?: { label: string; value: string }[];
}

export interface AskStreamHandlers {
  onStep: (step: Step) => void;
  onOperation: (operation: OperationReply) => void;
  onSynthesis?: (status: string) => void;
  onPhase?: (phase: "resolution" | "planning" | "execution" | "composition",
             status: "started" | "completed" | "unavailable") => void;
}

interface AskStreamEnvelope<T = unknown> {
  version: "1";
  request_id: string;
  sequence: number;
  payload: T;
}

function acceptedStreamEnvelope<T>(
  value: unknown,
  requestId: string,
  lastSequence: number,
): AskStreamEnvelope<T> | undefined {
  if (!value || typeof value !== "object") return undefined;
  const envelope = value as Partial<AskStreamEnvelope<T>>;
  return envelope.version === "1"
    && envelope.request_id === requestId
    && Number.isSafeInteger(envelope.sequence)
    && (envelope.sequence ?? 0) > lastSequence
    && "payload" in envelope
    ? envelope as AskStreamEnvelope<T>
    : undefined;
}

const MAX_ASK_QUESTION_CHARS = 1000;

export function askQuestionError(value: string): string | undefined {
  return value.trim().length > MAX_ASK_QUESTION_CHARS
    ? "Questions are capped at 1,000 characters. Please narrow this question."
    : undefined;
}

export function actionableClarificationChoices(
  clarification: AskClarification): ClarificationChoice[] | undefined {
  if (!Array.isArray(clarification.options)
      || clarification.options.length < 2 || clarification.options.length > 4
      || clarification.options.some(option => typeof option !== "string" || option.length > 100))
    return undefined;
  if (clarification.choices === undefined)
    return clarification.options.map(label => ({ label }));
  if (!Array.isArray(clarification.choices)
      || clarification.choices.length !== clarification.options.length)
    return undefined;
  const valid = clarification.choices.every((choice, index) =>
    choice && typeof choice.label === "string" && typeof choice.value === "string"
    && choice.label === clarification.options[index]
    && choice.label.length <= 100 && choice.value.length > 0 && choice.value.length <= 1_000);
  return valid ? clarification.choices : undefined;
}

export function clarificationFollowUp(context: string, choice: ClarificationChoice): string {
  return choice.value
    ? choice.value
    : `${context}\nClarification choice: ${choice.label}`;
}

export function validAskThreadToken(value: unknown): value is string {
  return typeof value === "string" && /^[A-Za-z0-9_-]{43}$/.test(value);
}

export interface Subject { work: string; title?: string; date?: string; anchor?: string; language?: string }
export interface EvidenceContext {
  publisher?: string; jurisdiction?: string; timeline_semantics?: string;
  requested_date?: string; requested_from_date?: string; requested_to_date?: string;
  observed_at?: string; valid_from?: string; valid_to?: string; provisional: boolean;
  source_uri?: string; extraction_profile?: string; record_sha256?: string;
  body_sha256?: string; text_sha256?: string; artifact_manifest_id?: string;
  content_digest?: string; signature_valid?: boolean;
}
export interface Citation { work: string; href: string; text?: string }
export interface ProvisionItem { anchor: string; num?: string; heading?: string; text?: string; text_sha256?: string; path?: string;
                                 citations?: Citation[];
                                 /** The citation row budget ran out on this provision, so its
                                     references were cut. Without it a provision whose references
                                     were all cut is indistinguishable from one that refers to
                                     nothing: absent evidence rendered as a negative fact. */
                                 citations_truncated?: boolean; text_omitted?: boolean;
                                 text_omitted_reason?: string; permalink?: string;
                                 document_order?: number; text_available?: boolean;
                                 text_unavailable_reason?: string; source_uri?: string;
                                 official_source?: string; eli?: string }

export interface ProvisionResponseMeta {
  totalProvisions?: number;
  totalProvisionGaps?: number;
  truncated: boolean;
  textTruncated: boolean;
  textCompleteness?: "complete" | "partial" | "unavailable";
}

function boundedCount(value: unknown): number | undefined {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 0
    ? value
    : undefined;
}

/** Bounded response facts are authoritative metadata, not guesses from the returned row page. */
export function provisionResponseMeta(result: any): ProvisionResponseMeta {
  const completeness = result?.text_completeness;
  const totalProvisions = boundedCount(result?.total_provisions);
  const totalProvisionGaps = boundedCount(result?.total_provision_gaps);
  return {
    ...(totalProvisions !== undefined ? { totalProvisions } : {}),
    ...(totalProvisionGaps !== undefined ? { totalProvisionGaps } : {}),
    truncated: result?.truncated === true,
    textTruncated: result?.text_truncated === true,
    ...(completeness === "complete" || completeness === "partial"
      || completeness === "unavailable"
      ? { textCompleteness: completeness }
      : {}),
  };
}

export function isTypedProvisionGap(item: ProvisionItem): boolean {
  return item.text_available === false && Boolean(item.text_unavailable_reason);
}

export function typedProvisionGapLabel(
  items: ProvisionItem[], textCompleteness?: string,
): string | undefined {
  const gaps = items.filter(isTypedProvisionGap).length;
  if (gaps === 0) return undefined;
  if (textCompleteness === "unavailable") return "publisher text unavailable";
  if (textCompleteness === "partial") return "partial publisher text";
  if (textCompleteness === "complete") return undefined;
  return gaps === items.length ? "publisher text unavailable" : "partial publisher text";
}

/** Keep source candidates independent until the HTTPS-only boundary chooses one. */
export function provisionSourceUrl(item: ProvisionItem): string | undefined {
  return safeHttpsUrl(item.permalink, item.eli, item.source_uri, item.official_source);
}

export function provisionCountLabel(items: ProvisionItem[], totalProvisions?: number): string {
  const shown = items.length;
  const total = boundedCount(totalProvisions);
  const coordinates = items.some(isTypedProvisionGap);
  const noun = coordinates ? "publisher coordinates" : `article${shown === 1 ? "" : "s"}`;
  return total !== undefined && total > shown
    ? `Showing ${shown.toLocaleString("en-US")} of ${total.toLocaleString("en-US")} ${noun}`
    : `${shown.toLocaleString("en-US")} ${noun}`;
}

export function boundedPublisherTextLabel(
  items: ProvisionItem[], textTruncated?: boolean,
): string | undefined {
  if (textTruncated) return "some held publisher text omitted from this response";
  return items.some((item) => item.text_omitted)
    ? "text shortened for this response"
    : undefined;
}

export function provisionEmptyExplanation(meta: ProvisionResponseMeta): string {
  return meta.textCompleteness === "partial" || meta.textTruncated
    ? "This bounded response did not include a publisher coordinate. Its partial-text metadata does not establish that publisher text is absent."
    : "No text is held for this law on that date.";
}

/** Merge canon/2 text rows and textless gap coordinates without changing legacy V3 order. */
export function provisionItemsOf(result: any): ProvisionItem[] {
  const text = Array.isArray(result?.provisions) ? result.provisions as ProvisionItem[] : [];
  const gaps = Array.isArray(result?.provision_gaps)
    ? (result.provision_gaps as ProvisionItem[]).map((gap) => ({
        ...gap,
        text: undefined,
        text_sha256: undefined,
        text_available: false,
      }))
    : [];
  if (gaps.length === 0) return [...text];
  return [...text, ...gaps]
    .map((item, index) => ({ item, index }))
    .sort((a, b) => (a.item.document_order ?? Number.MAX_SAFE_INTEGER)
      - (b.item.document_order ?? Number.MAX_SAFE_INTEGER) || a.index - b.index)
    .map(({ item }) => item);
}

export function asOfResult<T = any>(result: T | T[]): T | undefined {
  const list = (Array.isArray(result) ? result : [result]) as T[];
  return list.find((item: any) =>
    (Array.isArray(item?.provisions) && item.provisions.length > 0)
      || (Array.isArray(item?.provision_gaps) && item.provision_gaps.length > 0))
    ?? list.find((item: any) =>
      Array.isArray(item?.provisions) || Array.isArray(item?.provision_gaps)
        || Boolean(item?.document))
    ?? list[0];
}

export function hasTypedProvisionGaps(result: any, anchor?: string): boolean {
  if (anchor) return Array.isArray(result?.provision_gaps)
    && result.provision_gaps.some((gap: any) => gap?.anchor === anchor);
  return (Array.isArray(result?.provision_gaps) && result.provision_gaps.length > 0)
    || (typeof result?.total_provision_gaps === "number" && result.total_provision_gaps > 0)
    || result?.text_completeness === "partial"
    || result?.text_completeness === "unavailable";
}
export interface SearchFact {
  work: string; lex_id: string; anchor: string; number?: string; heading?: string;
  snippet?: string; title?: string; valid_from?: string; source_uri?: string; permalink?: string;
}
export interface UiEffect {
  /** Additive: typed per-publisher capability refusals beside a primary view. Validated fail
      closed in the browser; malformed entries are ignored and never suppress a view. */
  publisher_limitations?: unknown;
  /** Verified rows rendered while a sibling publisher response was unusable (PR293 O1). */
  partial_response?: boolean;
  /**
   * Publishers whose own response contradicted itself, so every claim they made was withheld.
   * Named rather than counted: a reader who knows which publisher is missing can judge the gap,
   * and "these results are incomplete" alone does not tell them whether the missing part is the
   * one they care about.
   */
  conflicted_publishers?: string[];
  provision?: { subject: Subject; valid_from: string; valid_to?: string; provisions: ProvisionItem[]; permalink?: string;
                evidence?: EvidenceContext[]; total_provisions?: number; truncated?: boolean;
                text_truncated?: boolean; outline_only?: boolean;
                provision_gaps?: ProvisionItem[]; total_provision_gaps?: number;
                text_completeness?: string };
  diff?: { subject: Subject; from_date: string; to_date: string; note?: string; status?: string;
           anchor_from_present?: boolean; anchor_to_present?: boolean; anchor_text_equal?: boolean;
           provision_level_comparable?: boolean;
           /** Whether the two dates resolved to different publisher versions, or for an anchored
               comparison whether that provision moved. A whole-work comparison has no other typed
               outcome, so without this the reader is told a comparison happened and left to guess
               how it came out. It is a record fact about versions, never a claim about the law. */
           changed?: boolean;
           /** Typed reasons the comparison is limited, as the producer classified them.
               The same facts are also written into `note`, and prose was the only form that
               reached a reader; a surface cannot branch on a paragraph. */
           comparison_limitations?: string[];
           /** The producer field was present but not wholly usable. Valid siblings remain. */
           comparison_limitations_malformed?: boolean;
           evidence?: EvidenceContext[] };
  history?: { subject: Subject; anchor: string; distinct_texts: number; truncated?: boolean; states: { valid_from: string; valid_to?: string; sha?: string; permalink?: string }[]; evidence?: EvidenceContext[] };
  timeline?: { subject: Subject; rows: { lex_id?: string; valid_from: string; valid_to?: string;
                title?: string; language?: string; permalink?: string; record_sha256?: string }[];
                total_count: number; truncated?: boolean;
                evidence?: EvidenceContext[] };
  ranking?: { from_date: string; to_date: string; order: string;
              // Absent when the producer's counts could not be summed honestly. A count that
              // has lost precision, or that was assembled from a malformed value, is not a
              // smaller truth: it is no count, and the surface must say nothing rather than a
              // number nothing stands behind.
              works_changed: number | undefined; new_versions: number | undefined;
              population_works?: number; population_basis?: string; known_exclusions?: string[];
              rows: RankingRow[]; status?: string; evidence?: EvidenceContext[] };
  in_force?: { date: string; total: number | undefined; status?: string; evidence?: EvidenceContext[];
               population_works?: number; population_basis?: string;
               population_scope_filters_applied?: boolean; known_exclusions?: string[]; rows: {
    work: string; title?: string; kind?: string; valid_from: string; permalink?: string;
    jurisdiction?: string; hierarchy?: string; timeline_semantics?: string;
  }[] };
  cited_by?: { cited_work: string; citing_articles: number; status?: string; evidence?: EvidenceContext[];
               /** The response returned fewer rows than it found. Absent means the response
                   carried no receipt, which is not the same as a complete answer, so it is
                   never read as false. */
               rows_truncated?: boolean;
               /** What this list is evidence of, and the two things the producer did not
                   assess. A count of referring articles with no scope beside it reads as a
                   wider claim than the producer makes. */
               evidence_scope?: string;
               current_legal_effect_assessed?: boolean;
               relationship_type_assessed?: boolean;
               rows: { work: string; title?: string; valid_from: string; anchor: string; num?: string;
                       permalink?: string; jurisdiction?: string }[] };
  coverage?: { evidence?: EvidenceContext[]; publishers: {
    publisher: string; name?: string; tier?: string; works: number; versions: number;
    versions_with_text: number; versions_without_text: number; earliest?: string; latest?: string;
    inventory_status?: string; build_complete?: boolean; signature_valid?: boolean;
    known_gaps: string[];
  }[] };
  verification?: {
    lex_id: string; title?: string; source_uri?: string; record_sha256?: string;
    body_sha256?: string; permalink?: string; signature_valid?: boolean; algorithm?: string;
    evidence?: EvidenceContext[];
  };
  // Not a view: how the workspace should be SET. The assistant reaches the same controls a reader
  // does, so "show me EU regulations" leaves the matching jurisdiction and legal metadata
  // selected rather than describing filters the visitor then has to find.
  workspace?: {
    query?: string;
    jurisdiction?: string; hierarchy?: string; domain?: string; source_class?: string;
    act_form?: string; binding_status?: string; page?: number; language?: string;
    work?: string; date?: string; anchor?: string;
    results?: SearchFact[];
    evidence?: EvidenceContext[];
  };
  gap?: { status: string; work?: string; date?: string; explanation: string; available: string[];
          evidence?: EvidenceContext[]; provision_gaps?: ProvisionItem[];
          total_provision_gaps?: number; truncated?: boolean; total_provisions?: number;
          text_truncated?: boolean; text_completeness?: string };
}
export interface RankingRow {
  work: string; title?: string; versions_in_period: number; versions_total: number;
  global_rank?: number;
  first_change: string; last_change: string; permalink?: string; diff_permalink?: string;
  // Where a comparison should start: the version in force before the window touched this law.
  // The row used to be opened as first_change vs last_change, and those are the same date
  // whenever a work moved exactly once, so the comparison ran a version against itself and
  // correctly reported nothing. Null when the window's first change is the work's first version.
  baseline?: string | null; diff_from?: string; diff_to?: string;
  // How many distinct wordings the comparison span actually holds. 1 means the act was reissued
  // without a word changing, which is why a row could say "2" and its comparison say nothing.
  distinct_texts?: number; wording_changed?: boolean; text_comparable?: boolean;
  jurisdiction?: string; hierarchy?: string; domains?: string[]; source_class?: string;
  act_form?: string; binding_status?: string; language?: string;
}

/** A step the agent completed, naming what it found. */
export interface Step { kind: string; text: string; work?: string; date?: string; anchor?: string }

/**
 * Streams the answer. The 30-70s wait is filled with what the agent FOUND — named laws,
 * dates and articles — because content-bearing updates measurably beat a spinner on
 * perceived speed and trust, and the gap widens the longer the wait. A failed POST is
 * never repeated automatically: callers may retry only with the same idempotency key.
 */
export async function askStreaming(
  message: string,
  handlers: AskStreamHandlers,
  signal?: AbortSignal,
  idempotencyKey: string = crypto.randomUUID(),
  threadToken?: string,
): Promise<AskReply> {
  const questionError = askQuestionError(message);
  if (!message.trim() || questionError) throw new RangeError(
    questionError ?? "A question is required.");
  if (threadToken !== undefined && !validAskThreadToken(threadToken))
    throw new Error("Invalid assistant thread token.");
  if (typeof performance !== "undefined") {
    performance.clearMeasures("lex-operation-result-received-to-presented");
  }
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "Idempotency-Key": idempotencyKey,
    "X-Lex-Stream-Version": "1",
  };
  if (threadToken) headers["X-Lex-Thread-Token"] = threadToken;
  const r = await fetch("/api/ask/stream", {
    method: "POST",
    headers,
    body: JSON.stringify({ message }),
    signal,
  });
  if (!r.ok) {
    const fallback = `Assistant request failed (${r.status}).`;
    try {
      const body = await r.json() as { error?: unknown };
      throw new AssistantResponseError(boundedAssistantError(body.error, fallback), r.status);
    } catch (cause) {
      if (cause instanceof AssistantResponseError) throw cause;
      throw new AssistantResponseError(fallback, r.status);
    }
  }
  if (!r.body) throw new Error("Assistant stream returned no body.");
  const requestId = r.headers.get("X-Lex-Request-Id");
  if (!requestId || !/^[a-f0-9]{32}$/.test(requestId))
    throw new Error("Assistant stream returned no valid request identity.");

  const reader = r.body.getReader();
  const decoder = new TextDecoder();
  let buf = "";
  let done: AskReply | undefined;
  let transportError: string | undefined;
  let lastSequence = 0;

  for (;;) {
    const { value, done: finished } = await reader.read();
    if (finished) break;
    buf += decoder.decode(value, { stream: true });
    // SSE frames are separated by a blank line; keep any partial frame in the buffer.
    const frames = buf.split("\n\n");
    buf = frames.pop() ?? "";
    for (const frame of frames) {
      const ev = /^event: (.+)$/m.exec(frame)?.[1];
      const raw = /^data: (.*)$/m.exec(frame)?.[1];
      if (!ev || !raw) continue;
      try {
        const envelope = acceptedStreamEnvelope<unknown>(
          JSON.parse(raw), requestId, lastSequence);
        if (!envelope) continue;
        lastSequence = envelope.sequence;
        if (ev === "step") handlers.onStep(envelope.payload as Step);
        else if (ev === "operation_result")
          handlers.onOperation(envelope.payload as OperationReply);
        else if (ev === "synthesis")
          handlers.onSynthesis?.(String((envelope.payload as { status?: unknown })?.status ?? ""));
        else if (ev === "phase") {
          const update = envelope.payload as { phase?: unknown; status?: unknown };
          const phase = update?.phase;
          const status = update?.status;
          if ((phase === "resolution" || phase === "planning" || phase === "execution"
                || phase === "composition")
              && (status === "started" || status === "completed" || status === "unavailable"))
            handlers.onPhase?.(phase, status);
        }
        else if (ev === "done") done = envelope.payload as AskReply;
        else if (ev === "transport_error")
          transportError = boundedAssistantError(
            (envelope.payload as { error?: unknown })?.error,
            "Assistant transport failed.");
      } catch { /* a malformed frame must not kill the stream */ }
    }
  }
  if (transportError) throw new AssistantResponseError(transportError);
  if (!done) throw new Error("The answer stream ended before a terminal result.");
  if (done.thread_token !== undefined && !validAskThreadToken(done.thread_token))
    throw new Error("Assistant stream returned an invalid thread token.");
  return done;
}

export async function ask(
  message: string,
  signal?: AbortSignal,
  threadToken?: string,
): Promise<AskReply> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (threadToken) headers["X-Lex-Thread-Token"] = threadToken;
  const r = await fetch("/api/ask", {
    method: "POST",
    headers,
    body: JSON.stringify({ message }),
    signal,
  });
  return (await r.json()) as AskReply;
}

export async function resetAskThread(threadToken: string): Promise<void> {
  if (!validAskThreadToken(threadToken)) return;
  await fetch("/api/ask/thread/reset", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Idempotency-Key": crypto.randomUUID(),
      "X-Lex-Thread-Token": threadToken,
    },
    body: "{}",
  });
}
