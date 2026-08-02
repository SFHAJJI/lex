import { useEffect, useState } from "react";
import { first, tool, type ProvisionItem, type RankingRow, type UiEffect } from "./api";
import { diffWords, changed, type Piece } from "./diff";
import { publisherOf, workSlug, type State } from "./state";

const permalink = (work: string, date: string, anchor?: string) =>
  `/${publisherOf(work)}/${workSlug(work)}/${date}${anchor ? `#${anchor}` : ""}`;

export function Provision({ items, validFrom, validTo, work, onPick }: {
  items: ProvisionItem[]; validFrom: string; validTo?: string; work: string;
  onPick: (anchor: string) => void;
}) {
  // A large law arrives as an outline (no text): render it as a table of contents the
  // reader picks from, rather than pulling thousands of articles into the page.
  const outline = items.length > 0 && items.every((p) => !p.text);
  return (
    <>
      <div className="cnt">
        <span className="tag">in force {validFrom} → {validTo ?? "open"}</span>
        <span className="tag">{items.length} article{items.length === 1 ? "" : "s"}</span>
        {outline ? <span className="tag">outline — pick an article to read it</span> : null}
      </div>
      {outline ? (
        <ul className="rows">
          {items.map((p) => (
            <li key={p.anchor}>
              <button className="rowbtn" onClick={() => onPick(p.anchor)}>
                <span>{p.num ?? p.anchor}{p.heading ? ` — ${p.heading}` : ""}</span>
              </button>
            </li>
          ))}
        </ul>
      ) : items.map((p) => (
        <article key={p.anchor} className="art">
          <h4>
            <a href={permalink(work, validFrom, p.anchor)}>{p.num ?? p.anchor}</a>
            {p.heading ? <span className="sub"> — {p.heading}</span> : null}
          </h4>
          <div className="lawtxt">{p.text}</div>
          {p.sha ? <div className="sha">sha256 {p.sha.slice(0, 16)}…</div> : null}
        </article>
      ))}
    </>
  );
}

export function HistoryRail({ states, anchor, work }: {
  states: { valid_from: string; valid_to?: string; sha?: string }[]; anchor: string; work: string;
}) {
  if (states.length === 0) return <Empty>No recorded history for this article.</Empty>;
  const t = (d: string) => Date.parse(`${d}T00:00:00Z`);
  const lo = t(states[0].valid_from);
  const hi = t(states[states.length - 1].valid_from);
  const span = Math.max(1, hi - lo);
  return (
    <>
      <div className="cnt">
        <span className="tag">{states.length} distinct text{states.length === 1 ? "" : "s"}</span>
        <span className="tag">{anchor}</span>
      </div>
      <div className="rail">
        <div className="axis" />
        {states.map((s) => (
          <a key={s.valid_from} className="tick"
             style={{ left: `${((t(s.valid_from) - lo) / span) * 97 + 1.5}%` }}
             href={permalink(work, s.valid_from, anchor)} title={s.valid_from} />
        ))}
      </div>
      <ol className="states">
        {states.map((s) => (
          <li key={s.valid_from}>
            <a href={permalink(work, s.valid_from, anchor)}>
              <b>{s.valid_from}</b> → {s.valid_to ?? "open"}
            </a>
            {s.sha ? <span className="sha"> {s.sha.slice(0, 12)}</span> : null}
          </li>
        ))}
      </ol>
    </>
  );
}

/** Compare: fetches both versions itself, then shows only what moved. */
export function Compare({ work, from, to, anchor }: {
  work: string; from: string; to: string; anchor?: string;
}) {
  const [state, setState] = useState<{ loading: boolean; error?: string; rows?: [string, Piece[]][];
                                       unchanged?: number; added?: number; removed?: number }>({ loading: true });
  const [showAll, setShowAll] = useState(false);

  useEffect(() => {
    let live = true;
    setState({ loading: true });
    const load = async (date: string) => {
      const res = await tool<any>("as_of", { work, date, mode: anchor ? "select" : "full", ...(anchor ? { anchors: anchor } : {}) });
      const one = first<any>(res, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
      const map = new Map<string, ProvisionItem>();
      for (const p of one?.provisions ?? []) map.set(p.anchor, p);
      return map;
    };
    Promise.all([load(from), load(to)])
      .then(([a, b]) => {
        if (!live) return;
        const keys = [...new Set([...a.keys(), ...b.keys()])].sort();
        const rows: [string, Piece[]][] = [];
        let same = 0;
        let added = 0;
        let removed = 0;
        for (const k of keys) {
          // Added/removed is about PRESENCE, not about which edit pieces appear: an
          // article that only gained a sentence is amended, not added.
          if (!a.has(k)) { added++; }
          else if (!b.has(k)) { removed++; }
          const pieces = diffWords(a.get(k)?.text ?? "", b.get(k)?.text ?? "");
          if (changed(pieces)) rows.push([a.get(k)?.num ?? b.get(k)?.num ?? k, pieces]);
          else same++;
        }
        setState({ loading: false, rows, unchanged: same, added, removed });
      })
      .catch((e) => live && setState({ loading: false, error: String(e.message ?? e) }));
    return () => { live = false; };
  }, [work, from, to, anchor]);

  if (state.loading) return <Empty>Comparing {from} with {to}…</Empty>;
  if (state.error) return <Empty>Could not compare these versions: {state.error}</Empty>;

  const rows = state.rows ?? [];
  return (
    <>
      <div className="cnt">
        <span className="tag">{rows.length} changed</span>
        <span className="tag">{state.added ?? 0} added</span>
        <span className="tag">{state.removed ?? 0} removed</span>
        <span className="tag">{state.unchanged} unchanged{state.unchanged ? " · hidden" : ""}</span>
        <span className="tag mono">{from} → {to}</span>
      </div>
      {rows.length === 0 ? (
        <Empty>Nothing changed in the text between these two dates.</Empty>
      ) : (
        (showAll ? rows : rows.slice(0, 12)).map(([label, pieces]) => (
          <article key={label} className="art">
            <h4>{label}</h4>
            <div className="lawtxt">
              {pieces.map((p, i) =>
                p.k === "+" ? <ins key={i}>{p.t}</ins> :
                p.k === "-" ? <del key={i}>{p.t}</del> :
                <span key={i}>{p.t}</span>)}
            </div>
          </article>
        ))
      )}
      {!showAll && rows.length > 12 ? (
        <button className="ghost" onClick={() => setShowAll(true)}>
          Show the other {rows.length - 12} changed articles
        </button>
      ) : null}
    </>
  );
}

export function Ranking({ rows, worksChanged, newVersions, from, to, onOpen }: {
  rows: RankingRow[]; worksChanged: number; newVersions: number; from: string; to: string;
  onOpen: (work: string, from: string, to: string) => void;
}) {
  const max = Math.max(1, ...rows.map((r) => r.versions_in_period));
  return (
    <>
      <div className="cnt">
        <span className="tag">{worksChanged.toLocaleString()} laws changed</span>
        <span className="tag">{newVersions.toLocaleString()} new versions</span>
        <span className="tag mono">{from} → {to}</span>
      </div>
      <div className="bars">
        {rows.map((r) => (
          <button key={r.work} className="bar" onClick={() => onOpen(r.work, r.first_change, r.last_change)}>
            <span className="track">
              <span className="fill" style={{ width: `${(r.versions_in_period / max) * 100}%` }} />
              <span className="lbl">{r.title ?? r.work}</span>
            </span>
            <span className="num">{r.versions_in_period}</span>
          </button>
        ))}
      </div>
    </>
  );
}

export function InForce({ date, total, rows, onOpen }: {
  date: string; total: number;
  rows: { work: string; title?: string; kind?: string; valid_from: string }[];
  onOpen: (work: string, date: string) => void;
}) {
  return (
    <>
      <div className="cnt">
        <span className="tag">{total.toLocaleString()} in force</span>
        <span className="tag mono">on {date}</span>
      </div>
      <ul className="rows">
        {rows.map((r) => (
          <li key={r.work}>
            <button className="rowbtn" onClick={() => onOpen(r.work, r.valid_from)}>
              <span>{r.title ?? r.work}</span>
              <span className="sub mono">{r.kind ?? ""} · since {r.valid_from}</span>
            </button>
          </li>
        ))}
      </ul>
    </>
  );
}

export function Gap({ status, explanation, available }: { status: string; explanation: string; available: string[] }) {
  return (
    <div className="gap">
      <div className="cnt"><span className="tag warn mono">{status}</span></div>
      <p>{explanation}</p>
      {available.length > 0 ? (
        <p className="sub">What does exist: {available.slice(0, 10).join(" · ")}</p>
      ) : null}
      <p className="sub">
        <a href="/coverage">See exactly what Lex holds and what it lacks →</a>
      </p>
    </div>
  );
}

export function Empty({ children }: { children: React.ReactNode }) {
  return <div className="empty">{children}</div>;
}

export const hasView = (ui?: UiEffect) =>
  !!(ui && (ui.provision || ui.diff || ui.history || ui.ranking || ui.in_force || ui.gap));

export function modeFor(ui?: UiEffect): State["mode"] | undefined {
  if (ui?.diff) return "compare";
  if (ui?.history) return "history";
  if (ui?.provision) return "read";
  return undefined;
}
