// The workspace state IS the URL. Three doors write it — the command bar, the pickers,
// and every link inside a view — so nothing is isolated and every view a visitor reaches
// is shareable and bookmarkable, including the one the assistant chose for them.
import { useEffect, useState } from "react";

/** Reading one version, or comparing two. History is not a mode — the rail always shows it. */
export type Mode = "read" | "compare";
/** The three subjects a question can be about — a law, a stretch of time, or words. */
/**
 * Three places, not four query types.
 *
 * "search" is the one box and the one date: finding a law, finding words, and reading either as
 * they stood on a day are the same act with a different noun. "law" is reading one. "time" is the
 * corpus-wide report, which is a different job entirely, watching rather than looking up.
 *
 * The old spaces (topic, onDate) were query TYPES, which asked a reader to classify their own
 * question before asking it. A date is not a kind of question; it is a modifier on every question.
 */
export type Space = "search" | "law" | "time";

export interface State {
  space?: Space;
  q?: string;         // topic query
  asOf?: string;      // topic: restrict to versions valid on this date; onDate: THE date
  work?: string;      // "lu-legilux:loi-2020-07-17-a624"
  date?: string;      // point in time for read/compare-from
  to?: string;        // compare-to
  anchor?: string;    // focused article
  mode: Mode;
  from?: string;      // period start (time workspace)
  until?: string;     // period end
  order?: "by_date" | "by_churn";
  retrieval?: "keyword" | "hybrid";
  jurisdiction?: string;
  hierarchy?: string;
  domain?: string;
  sourceClass?: string;
  actForm?: string;
  bindingStatus?: string;
  // Which language to read a law in, for the few works published in more than one. In the URL
  // for the same reason: "the Constitution in German" has to be a link someone can send.
  language?: string;
}

const LEGACY_LAYERS: Record<string, string | undefined> = {
  instruments: undefined,
  constitution: "Constitution,CONV,PROT,TC,ORD",
  statutes: "LOI,CODE",
  regulations: "RGD,RMIN,AMIN,AGD,RGC,AGC,ARGD,RI",
  collections: "RECUEIL,CODE_RECUEIL",
};

export function read(): State {
  const p = new URLSearchParams(location.search);
  // ?mode=history predates the rail and is still in shared links: it means "show me this
  // law's versions", which is now unconditional. Read it, then let it go.
  const mode: Mode = p.get("mode") === "compare" ? "compare" : "read";
  const legacyLayer = p.get("layer");
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
    retrieval: (p.get("retrieval") as State["retrieval"]) || undefined,
    jurisdiction: p.get("jurisdiction") || undefined,
    hierarchy: p.get("hierarchy") || undefined,
    domain: p.get("domain") || undefined,
    sourceClass: p.get("sourceClass") || (legacyLayer ? LEGACY_LAYERS[legacyLayer] : undefined),
    actForm: p.get("actForm") || undefined,
    bindingStatus: p.get("bindingStatus") || undefined,
    language: p.get("language") || undefined,
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
