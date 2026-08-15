// The workspace is itself an MCP client: mode switches call the public /mcp endpoint
// directly, with no model in the loop. Only free text goes to /api/ask. That keeps
// "play with it" instant and deterministic — and it means the demo is the strongest
// possible proof that the published API actually works.

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

/**
 * Exposes the exact frozen operation plan already returned to this browser, plus a deliberately
 * compact terminal outcome summary. The other trace phases, reply prose, legal/UI payloads and
 * thread capability are not duplicated in the disclosure.
 */
export function executionDetails(reply: AskReply): AskExecutionDetails | undefined {
  const rawPlan = Array.isArray(reply.trace)
    ? reply.trace.find((entry) => entry && typeof entry === "object" && !Array.isArray(entry)
      && entry.phase === "operation_plan") : undefined;
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
    operation_plan: rawPlan ?? null,
    operation_outcomes: outcomes,
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
                                 citations?: Citation[]; text_omitted?: boolean;
                                 text_omitted_reason?: string; permalink?: string }
export interface UiEffect {
  provision?: { subject: Subject; valid_from: string; valid_to?: string; provisions: ProvisionItem[]; permalink?: string;
                evidence?: EvidenceContext[]; total_provisions?: number; truncated?: boolean;
                text_truncated?: boolean; outline_only?: boolean };
  diff?: { subject: Subject; from_date: string; to_date: string; note?: string; status?: string;
           anchor_from_present?: boolean; anchor_to_present?: boolean; anchor_text_equal?: boolean;
           provision_level_comparable?: boolean;
           evidence?: EvidenceContext[] };
  history?: { subject: Subject; anchor: string; distinct_texts: number; states: { valid_from: string; valid_to?: string; sha?: string; permalink?: string }[]; evidence?: EvidenceContext[] };
  timeline?: { subject: Subject; rows: { lex_id?: string; valid_from: string; valid_to?: string;
                title?: string; language?: string; permalink?: string; record_sha256?: string }[];
                total_count: number; truncated: boolean;
                evidence?: EvidenceContext[] };
  ranking?: { from_date: string; to_date: string; order: string; works_changed: number; new_versions: number;
              population_works?: number; population_basis?: string; known_exclusions?: string[];
              rows: RankingRow[]; status?: string; evidence?: EvidenceContext[] };
  in_force?: { date: string; total: number; status?: string; evidence?: EvidenceContext[]; rows: {
    work: string; title?: string; kind?: string; valid_from: string; permalink?: string;
    jurisdiction?: string; hierarchy?: string; timeline_semantics?: string;
  }[] };
  cited_by?: { cited_work: string; citing_articles: number; status?: string; evidence?: EvidenceContext[];
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
    evidence?: EvidenceContext[];
  };
  gap?: { status: string; work?: string; date?: string; explanation: string; available: string[]; evidence?: EvidenceContext[] };
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
