// The workspace is itself an MCP client: mode switches call the public /mcp endpoint
// directly, with no model in the loop. Only free text goes to /api/ask. That keeps
// "play with it" instant and deterministic — and it means the demo is the strongest
// possible proof that the published API actually works.

let id = 0;

export async function tool<T = any>(name: string, args: Record<string, unknown>): Promise<T> {
  const r = await fetch("/mcp", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ jsonrpc: "2.0", id: ++id, method: "tools/call", params: { name, arguments: args } }),
  });
  if (!r.ok) throw new Error(`tool ${name} failed (${r.status})`);
  const j = await r.json();
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
  error?: string;
}

export interface Subject { work: string; title?: string; date?: string; anchor?: string }
export interface ProvisionItem { anchor: string; num?: string; heading?: string; text: string; sha?: string }
export interface UiEffect {
  provision?: { subject: Subject; valid_from: string; valid_to?: string; provisions: ProvisionItem[]; permalink?: string };
  diff?: { subject: Subject; from_date: string; to_date: string; note?: string };
  history?: { subject: Subject; anchor: string; distinct_texts: number; states: { valid_from: string; valid_to?: string; sha?: string; permalink?: string }[] };
  ranking?: { from_date: string; to_date: string; order: string; works_changed: number; new_versions: number; rows: RankingRow[] };
  in_force?: { date: string; total: number; rows: { work: string; title?: string; kind?: string; valid_from: string; permalink?: string }[] };
  gap?: { status: string; work?: string; date?: string; explanation: string; available: string[] };
}
export interface RankingRow {
  work: string; title?: string; versions_in_period: number; versions_total: number;
  first_change: string; last_change: string; permalink?: string; diff_permalink?: string;
}

export async function ask(question: string, signal?: AbortSignal): Promise<AskReply> {
  const r = await fetch("/api/ask", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ messages: [{ role: "user", content: question }] }),
    signal,
  });
  return (await r.json()) as AskReply;
}
