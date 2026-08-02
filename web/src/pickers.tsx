import { useEffect, useRef, useState } from "react";
import { tool } from "./api";

/** A work the user can choose, collapsed from provision-level search hits. */
export interface WorkHit { work: string; title: string; validFrom?: string }

/**
 * Searchable law picker. Uses the public `search` tool — including its
 * identifier/title fallback — so typing "32016r0679", "GDPR" or "code du travail"
 * all resolve. No model involved: picking a law must be instant and repeatable.
 */
export function LawPicker({ current, onPick }: { current?: string; onPick: (w: WorkHit) => void }) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState("");
  const [hits, setHits] = useState<WorkHit[]>([]);
  const [busy, setBusy] = useState(false);
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const away = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false); };
    const esc = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    addEventListener("mousedown", away);
    addEventListener("keydown", esc);
    return () => { removeEventListener("mousedown", away); removeEventListener("keydown", esc); };
  }, []);

  useEffect(() => {
    if (!open || q.trim().length < 2) { setHits([]); setBusy(false); return; }
    let live = true;
    setBusy(true);
    const timer = setTimeout(async () => {
      try {
        const res = await tool<any>("search", { query: q.trim(), limit: 12 });
        const all = (Array.isArray(res) ? res : [res]).flatMap((c: any) => c?.hits ?? []);
        const seen = new Map<string, WorkHit>();
        for (const h of all) {
          const lex = String(h.lex_id ?? "");
          const work = lex.split(":").slice(0, 2).join(":");
          if (work && !seen.has(work))
            seen.set(work, { work, title: shorten(h.title) ?? work, validFrom: h.valid_from });
        }
        if (live) setHits([...seen.values()].slice(0, 10));
      } catch { if (live) setHits([]); }
      finally { if (live) setBusy(false); }
    }, 220);
    return () => { live = false; clearTimeout(timer); };
  }, [q, open]);

  return (
    <div className="picker" ref={box}>
      <button className="pick main" onClick={() => setOpen((v) => !v)}
              aria-expanded={open} aria-haspopup="listbox" aria-controls="lawpop">
        <i>law</i>{current ?? "choose a law"} ▾
      </button>
      {open ? (
        <div className="pop" id="lawpop" role="listbox">
          <input autoFocus value={q} onChange={(e) => setQ(e.target.value)}
                 placeholder="name, subject, or identifier (32016r0679)" aria-label="Find a law" />
          {busy ? <div className="popnote">searching…</div> : null}
          {!busy && q.trim().length >= 2 && hits.length === 0 ? <div className="popnote">nothing matches</div> : null}
          <ul>
            {hits.map((h) => (
              <li key={h.work} role="option" aria-selected={false}>
                <button onClick={() => { onPick(h); setOpen(false); setQ(""); }}>
                  <span>{h.title}</span>
                  <span className="sub mono">{h.work}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}

/** Period controls for the analytics workspace — dates plus how to rank. */
export function PeriodPicker({ from, until, order, onChange }: {
  from: string; until: string; order: "by_date" | "by_churn";
  onChange: (next: { from?: string; until?: string; order?: "by_date" | "by_churn" }) => void;
}) {
  return (
    <div className="sel">
      <label className="pick"><i>from</i>
        <input type="date" aria-label="Period start" value={from} onChange={(e) => onChange({ from: e.target.value })} /></label>
      <label className="pick"><i>to</i>
        <input type="date" aria-label="Period end" value={until} onChange={(e) => onChange({ until: e.target.value })} /></label>
      <label className="pick"><i>rank by</i>
        <select aria-label="How to rank the results" value={order} onChange={(e) => onChange({ order: e.target.value as "by_date" | "by_churn" })}>
          <option value="by_churn">most changed</option>
          <option value="by_date">most recent</option>
        </select></label>
      <span className="grow" />
      {/* Two shortcuts, not three: one ordinary window and one that shows what the tool is for. */}
      <span className="quick">
        <button className="eg" onClick={() => onChange({ from: shift(-365), until: iso(new Date()) })}>last year</button>
        <span className="or">·</span>
        <button className="eg" onClick={() => onChange({ from: "2020-03-01", until: "2021-07-01", order: "by_churn" })}>the pandemic</button>
      </span>
    </div>
  );
}

/** Topic workspace: words in, article-level hits out, each one a door into a law. */
export function TopicSearch({ q, asOf, onQuery, onOpen }: {
  q: string; asOf?: string;
  onQuery: (q: string, asOf?: string) => void;
  onOpen: (work: string, date: string, anchor?: string) => void;
}) {
  const [text, setText] = useState(q);
  const [hits, setHits] = useState<any[]>([]);
  const [busy, setBusy] = useState(false);

  useEffect(() => { setText(q); }, [q]);
  useEffect(() => {
    if (!q.trim()) { setHits([]); return; }
    let live = true;
    setBusy(true);
    tool<any>("search", { query: q, limit: 20, ...(asOf ? { as_of: asOf } : {}) })
      .then((res) => {
        if (!live) return;
        setHits((Array.isArray(res) ? res : [res]).flatMap((c: any) => c?.hits ?? []));
      })
      .catch(() => live && setHits([]))
      .finally(() => live && setBusy(false));
    return () => { live = false; };
  }, [q, asOf]);

  return (
    <>
      <form className="sel" onSubmit={(e) => { e.preventDefault(); onQuery(text, asOf); }}>
        <input aria-label="Words to search for in the text" style={{ flex: 1, minWidth: 0 }} value={text} onChange={(e) => setText(e.target.value)}
               placeholder="words in the text — congé parental, breach notification, own funds" />
        <label className="pick"><i>as of</i>
          <input type="date" aria-label="Only versions in force on this date" value={asOf ?? ""} onChange={(e) => onQuery(q, e.target.value || undefined)} /></label>
        <button type="submit">Search</button>
      </form>
      {busy ? <div className="empty">Searching…</div> : null}
      {!busy && q && hits.length === 0 ? <div className="empty">Nothing in the corpus matches that.</div> : null}
      <ul className="rows">
        {hits.map((h, i) => {
          const work = String(h.lex_id ?? "").split(":").slice(0, 2).join(":");
          return (
            <li key={`${h.provision_id ?? h.lex_id}-${i}`}>
              <button className="rowbtn" onClick={() => onOpen(work, h.valid_from, h.anchor)}>
                <span>{shorten(h.title) ?? work}{h.provision_num ? ` — ${h.provision_num}` : ""}</span>
                {h.snippet ? <span className="sub">{stripMarks(h.snippet)}</span> : null}
                <span className="sub mono">{h.valid_from}{h.anchor ? ` · ${h.anchor}` : ""}</span>
              </button>
            </li>
          );
        })}
      </ul>
    </>
  );
}

const iso = (d: Date) => d.toISOString().slice(0, 10);
function shift(days: number) { const d = new Date(); d.setUTCDate(d.getUTCDate() + days); return iso(d); }
const stripMarks = (s: string) => s.replace(/[«»]/g, "");

/** Legilux titles are prefixed "Version consolidée applicable au …  : " — noise in a list. */
export function shorten(t?: string): string | undefined {
  if (!t) return t;
  const i = t.indexOf(" : ");
  const body = i > 0 && /^(Version consolidée|Konsolidierte)/i.test(t) ? t.slice(i + 3) : t;
  return body.length > 90 ? `${body.slice(0, 90).trimEnd()}…` : body;
}
