// The workspace state IS the URL. Three doors write it — the command bar, the pickers,
// and every link inside a view — so nothing is isolated and every view a visitor reaches
// is shareable and bookmarkable, including the one the assistant chose for them.
import { useEffect, useState } from "react";

export type Mode = "read" | "history" | "compare" | "provenance";
/** The three subjects a question can be about — a law, a stretch of time, or words. */
export type Space = "law" | "time" | "topic";

export interface State {
  space?: Space;
  q?: string;         // topic query
  asOf?: string;      // topic: restrict to versions valid on this date
  work?: string;      // "lu-legilux:loi-2020-07-17-a624"
  date?: string;      // point in time for read/compare-from
  to?: string;        // compare-to
  anchor?: string;    // focused article
  mode: Mode;
  from?: string;      // period start (time workspace)
  until?: string;     // period end
  order?: "by_date" | "by_churn";
}

export function read(): State {
  const p = new URLSearchParams(location.search);
  const mode = (p.get("mode") as Mode) || "read";
  return {
    space: (p.get("space") as Space) || undefined,
    q: p.get("q") || undefined,
    asOf: p.get("asOf") || undefined,
    work: p.get("work") || undefined,
    date: p.get("date") || undefined,
    to: p.get("to") || undefined,
    anchor: p.get("anchor") || undefined,
    from: p.get("from") || undefined,
    until: p.get("until") || undefined,
    order: (p.get("order") as State["order"]) || undefined,
    mode,
  };
}

function toSearch(s: State): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(s)) if (v && !(k === "mode" && v === "read")) p.set(k, String(v));
  const q = p.toString();
  return q ? `?${q}` : location.pathname;
}

/** Writes state to the URL; `push` makes it a history entry so Back undoes it. */
export function useWorkspace(): [State, (next: Partial<State>, push?: boolean) => void] {
  const [state, setState] = useState<State>(read);

  useEffect(() => {
    const onPop = () => setState(read());
    addEventListener("popstate", onPop);
    return () => removeEventListener("popstate", onPop);
  }, []);

  const go = (next: Partial<State>, push?: boolean) => {
    const merged = { ...state, ...next };
    // Back should undo "I opened another law", not "I nudged the date". Every control tweak
    // pushing a history entry meant Back had to be pressed a dozen times to leave a page.
    if (push === undefined)
      push = next.work !== undefined && next.work !== state.work
          || next.q !== undefined && next.q !== state.q
          || next.space !== undefined && next.space !== state.space;
    const url = toSearch(merged);
    if (push) history.pushState(null, "", url);
    else history.replaceState(null, "", url);
    setState(merged);
  };

  return [state, go];
}

export const workSlug = (work?: string) => (work ? work.split(":").slice(1).join(":") : "");
export const publisherOf = (work?: string) => (work ? work.split(":")[0] : "");
