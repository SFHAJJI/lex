import { useEffect, useRef, useState } from "react";
import { tool } from "./api";
import { jurisdictionForPublisher, jurisdictionLabel } from "./facets";
import { fusePublisherHits } from "./searchFusion";

/** A work the user can choose, collapsed from provision-level search hits. */
export interface WorkHit { work: string; title: string; validFrom?: string; jurisdiction?: string }

/**
 * Searchable law picker. Uses the public `search` tool — including its
 * identifier/title fallback — so typing "32016r0679", "GDPR" or "code du travail"
 * all resolve. No model involved: picking a law must be instant and repeatable.
 */
export function LawPicker({ current, onPick, inline }: { current?: string; onPick: (w: WorkHit) => void; inline?: boolean }) {
  // Inline means the search field IS the control, with results beneath it, rather than a button
  // that opens a popover. In the finder card there is nothing to hide behind, so hiding it costs
  // a click and buys nothing.
  const [open, setOpen] = useState(!!inline);
  const [q, setQ] = useState("");
  const [hits, setHits] = useState<WorkHit[]>([]);
  const [busy, setBusy] = useState(false);
  const box = useRef<HTMLDivElement>(null);

  // Dismissal belongs to the POPOVER, never to the inline field. Inline, this listener closed the
  // front page's only control on the first click landing anywhere else, with nothing to reopen it,
  // so the card sat there empty under its own heading and the site looked broken on arrival.
  useEffect(() => {
    if (inline) return;
    const away = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false); };
    const esc = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    addEventListener("mousedown", away);
    addEventListener("keydown", esc);
    return () => { removeEventListener("mousedown", away); removeEventListener("keydown", esc); };
  }, [inline]);

  useEffect(() => {
    if (!open || q.trim().length < 2) { setHits([]); setBusy(false); return; }
    let live = true;
    setBusy(true);
    const timer = setTimeout(async () => {
      try {
        const res = await tool<any>("search", { query: q.trim(), limit: 12 });
        const all = fusePublisherHits<any>(res);
        const seen = new Map<string, WorkHit>();
        for (const h of all) {
          const lex = String(h.lex_id ?? "");
          const work = lex.split(":").slice(0, 2).join(":");
          if (work && !seen.has(work))
            seen.set(work, { work, title: shorten(h.title) ?? work, validFrom: h.valid_from,
                             jurisdiction: h._jurisdiction ?? jurisdictionForPublisher(work.split(":")[0]) });
        }
        if (live) setHits([...seen.values()].slice(0, 10));
      } catch { if (live) setHits([]); }
      finally { if (live) setBusy(false); }
    }, 220);
    return () => { live = false; clearTimeout(timer); };
  }, [q, open]);

  return (
    <div className={inline ? "picker inline" : "picker"} ref={box}>
      {inline ? null : (
        <button className="pick main" onClick={() => setOpen((v) => !v)}
                aria-expanded={open} aria-controls="lawpop">
          <i>law</i>{current ?? "choose a law"} ▾
        </button>
      )}
      {open ? (
        <div className={inline ? "pop open" : "pop"} id="lawpop">
          <input name="law-query" autoFocus={!inline} value={q} onChange={(e) => setQ(e.target.value)}
                 placeholder="name, subject, or identifier (32016r0679)" aria-label="Find a law" />
          {busy ? (
            <ul className="rows">
              {[0, 1, 2].map((i) => (
                <li key={i} className="skrow"><span className="sk" style={{ width: `${78 - i * 9}%`, height: 15 }} /></li>
              ))}
            </ul>
          ) : null}
          {!busy && q.trim().length >= 2 && hits.length === 0 ? <div className="popnote">nothing matches</div> : null}
          <ul aria-label="Law suggestions">
            {hits.map((h) => (
              <li key={h.work}>
                <button onClick={() => { onPick(h); if (!inline) setOpen(false); setQ(""); }}>
                  <span>{h.title}</span>
                  <span className="sub">{h.jurisdiction ? jurisdictionLabel(h.jurisdiction) : h.work.split(":")[0]}
                    <span className="mono"> · {h.work.split(":").slice(1).join(":")}</span></span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}

/** Legilux titles are prefixed "Version consolidée applicable au …  : " — noise in a list. */
export function shorten(t?: string): string | undefined {
  if (!t) return t;
  const i = t.indexOf(" : ");
  const body = i > 0 && /^(Version consolidée|Version rectifiée|Konsolidierte|Konsolidéiert)/i.test(t)
    ? t.slice(i + 3) : t;
  return body.length > 90 ? `${body.slice(0, 90).trimEnd()}…` : body;
}
