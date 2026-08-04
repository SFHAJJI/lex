// The workspace state IS the URL. Three doors write it — the command bar, the pickers,
// and every link inside a view — so nothing is isolated and every view a visitor reaches
// is shareable and bookmarkable, including the one the assistant chose for them.
import { useEffect, useState } from "react";

/** Reading one version, or comparing two. History is not a mode — the rail always shows it. */
export type Mode = "read" | "compare";
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
  // Which legal layer the period view is showing. In the URL so a filtered view is shareable
  // and the Back button undoes a filter change like any other move.
  layer?: LayerId;
}

/**
 * The layers, by legal weight rather than by the publisher's fifteen type codes.
 *
 * A reader asks "what changed in the law", not "what changed in documents of type RGC". And a
 * thematic collection is, as it turns out, simply another document type, so it needs no special
 * case anywhere: it is one layer among the others, just not the default one.
 */
export type LayerId = "instruments" | "constitution" | "statutes" | "regulations" | "collections";

export const LAYERS: { id: LayerId; label: string; hint: string; types: string }[] = [
  { id: "instruments",  label: "Laws",          hint: "everything anyone voted or enacted",
    types: "!RECUEIL,!CODE_RECUEIL" },
  { id: "constitution", label: "Constitution",  hint: "and international conventions",
    types: "Constitution,CONV,PROT,TC,ORD" },
  { id: "statutes",     label: "Statutes",      hint: "lois and codes enacted as law",
    types: "LOI,CODE" },
  { id: "regulations",  label: "Regulations",   hint: "grand-ducal and ministerial",
    types: "RGD,RMIN,AMIN,AGD,RGC,AGC,ARGD,RI" },
  { id: "collections",  label: "Collections",   hint: "subject shelves, not instruments",
    types: "RECUEIL,CODE_RECUEIL" },
];

export function read(): State {
  const p = new URLSearchParams(location.search);
  // ?mode=history predates the rail and is still in shared links: it means "show me this
  // law's versions", which is now unconditional. Read it, then let it go.
  const mode: Mode = p.get("mode") === "compare" ? "compare" : "read";
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
    layer: (p.get("layer") as LayerId) || undefined,
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
