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
  trace?: { tool: string; status?: string }[];
  ui?: UiEffect;
  clarification?: AskClarification;
  error?: string;
  /** False when the answer was a refusal: its steps are withheld from the transcript. */
  narrated?: boolean;
}

/** Page-specific actions are useful only after an answer that did not end in a gap. */
export function shouldOfferContextualFollowUps(reply: AskReply): boolean {
  return !reply.error && !reply.clarification && !reply.ui?.gap;
}
export interface AskMessage { role: "user" | "assistant"; content: string }
export interface ClarificationChoice { label: string; value?: string }
export interface AskClarification {
  question: string;
  options: string[];
  choices?: { label: string; value: string }[];
}

const MAX_ASK_HISTORY = 12;
const MAX_ASK_QUESTION_CHARS = 1000;
const MAX_ASK_ASSISTANT_CHARS = 4000;
const TRUNCATED_ASSISTANT_SUFFIX = "\n\n[Earlier answer shortened in conversation memory.]";

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

export function boundedAskHistory(value: unknown): AskMessage[] {
  if (!Array.isArray(value)) return [];
  const history = value.filter((item): item is AskMessage =>
    (item?.role === "user" || item?.role === "assistant")
    && typeof item?.content === "string"
    && item.content.trim().length > 0)
    .map((item) => {
      if (item.role === "user" && item.content.length > MAX_ASK_QUESTION_CHARS)
        throw new RangeError("A user message exceeds the server limit.");
      return { ...item, content: item.role === "assistant"
        ? item.content.length <= MAX_ASK_ASSISTANT_CHARS ? item.content
          : item.content.slice(0, MAX_ASK_ASSISTANT_CHARS - TRUNCATED_ASSISTANT_SUFFIX.length)
            + TRUNCATED_ASSISTANT_SUFFIX
        : item.content };
    })
    .slice(-MAX_ASK_HISTORY);
  while (history[0]?.role === "assistant") history.shift();
  return history;
}

export interface Subject { work: string; title?: string; date?: string; anchor?: string }
export interface Citation { work: string; href: string; text?: string }
export interface ProvisionItem { anchor: string; num?: string; heading?: string; text?: string; text_sha256?: string; path?: string;
                                 citations?: Citation[] }
export interface UiEffect {
  provision?: { subject: Subject; valid_from: string; valid_to?: string; provisions: ProvisionItem[]; permalink?: string };
  diff?: { subject: Subject; from_date: string; to_date: string; note?: string; status?: string };
  history?: { subject: Subject; anchor: string; distinct_texts: number; states: { valid_from: string; valid_to?: string; sha?: string; permalink?: string }[] };
  timeline?: { subject: Subject };
  ranking?: { from_date: string; to_date: string; order: string; works_changed: number; new_versions: number; rows: RankingRow[] };
  in_force?: { date: string; total: number; rows: {
    work: string; title?: string; kind?: string; valid_from: string; permalink?: string;
    jurisdiction?: string; hierarchy?: string; timeline_semantics?: string;
  }[] };
  cited_by?: { cited_work: string; citing_articles: number;
               rows: { work: string; title?: string; valid_from: string; anchor: string; num?: string;
                       permalink?: string; jurisdiction?: string }[] };
  // Not a view: how the workspace should be SET. The assistant reaches the same controls a reader
  // does, so "show me EU regulations" leaves the matching jurisdiction and legal metadata
  // selected rather than describing filters the visitor then has to find.
  workspace?: {
    query?: string;
    jurisdiction?: string; hierarchy?: string; domain?: string; source_class?: string;
    act_form?: string; binding_status?: string; page?: number; language?: string;
  };
  gap?: { status: string; work?: string; date?: string; explanation: string; available: string[] };
}
export interface RankingRow {
  work: string; title?: string; versions_in_period: number; versions_total: number;
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
 * perceived speed and trust, and the gap widens the longer the wait. Falls back to the
 * plain endpoint if the stream is unavailable.
 */
export async function askStreaming(
  messages: AskMessage[],
  onStep: (s: Step) => void,
  signal?: AbortSignal,
): Promise<AskReply> {
  const r = await fetch("/api/ask/stream", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ messages }),
    signal,
  });
  if (!r.ok || !r.body) return ask(messages, signal);

  const reader = r.body.getReader();
  const decoder = new TextDecoder();
  let buf = "";
  let done: AskReply | undefined;

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
        const data = JSON.parse(raw);
        if (ev === "step") onStep(data as Step);
        else if (ev === "done") done = data as AskReply;
      } catch { /* a malformed frame must not kill the stream */ }
    }
  }
  return done ?? { reply: "The answer stream ended early. Try again." };
}

export async function ask(messages: AskMessage[], signal?: AbortSignal): Promise<AskReply> {
  const r = await fetch("/api/ask", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ messages }),
    signal,
  });
  return (await r.json()) as AskReply;
}
