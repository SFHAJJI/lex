import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { first, tool, type ProvisionItem, type RankingRow, type UiEffect } from "./api";
import { diffWords, changed, type Piece } from "./diff";
import { publisherOf, workSlug, type State } from "./state";

const permalink = (work: string, date: string, anchor?: string) =>
  `/${publisherOf(work)}/${workSlug(work)}/${date}${anchor ? `#${anchor}` : ""}`;

const ms = (d: string) => Date.parse(`${d}T00:00:00Z`);

/**
 * The reader: the law's structure on the left, its text on the right.
 *
 * The outline used to appear only for laws too large to render, and vanished the moment you
 * opened an article — so re-dating dropped you at the top of a document you were reading the
 * middle of. It is the one control a point-in-time reader uses constantly, so it stays put.
 */
export function Provision({ items, toc, validFrom, validTo, work, anchor, onPick, onClear }: {
  items: ProvisionItem[]; toc: ProvisionItem[]; validFrom: string; validTo?: string;
  work: string; anchor?: string; onPick: (anchor: string) => void; onClear: () => void;
}) {
  const outlineOnly = items.length > 0 && items.every((p) => !p.text);
  const nav = toc.length >= 6 || outlineOnly;
  const body = (
    <div className="text">
      <div className="cnt">
        <span className="tag">in force {validFrom} → {validTo ?? "open"}</span>
        {anchor ? (
          <button className="tag act" onClick={onClear}>article {anchor} ✕</button>
        ) : (
          <span className="tag">{items.length} article{items.length === 1 ? "" : "s"}</span>
        )}
      </div>
      {outlineOnly ? (
        // A blank pane next to a table of contents is a dead end. Offer the thing a reader
        // opening a code actually wants: the first article, one click away.
        <div className="empty">
          <p>{toc.length.toLocaleString()} articles, too many to render at once.
             Pick one from the contents, or start at the beginning.</p>
          {toc.length > 0 ? (
            <button className="chip" onClick={() => onPick(toc[0].anchor)}>
              Read from {toc[0].num ?? toc[0].anchor} →
            </button>
          ) : null}
        </div>
      ) : items.map((p) => (
        <article key={p.anchor} className="art" id={p.anchor}>
          <h4>
            <a href={permalink(work, validFrom, p.anchor)}>{p.num ?? p.anchor}</a>
            {p.heading ? <span className="sub">, {plain(p.heading)}</span> : null}
          </h4>
          <div className="lawtxt">{p.text}</div>
          {p.sha ? <div className="sha">sha256 {p.sha.slice(0, 16)}…</div> : null}
        </article>
      ))}
    </div>
  );
  if (!nav) return body;
  return (
    <div className="reader">
      <aside className="toccol"><Outline items={toc} current={anchor} onPick={onPick} /></aside>
      {body}
    </div>
  );
}

/**
 * Every version of what is on screen, always visible.
 *
 * Read / History / Compare were three tabs over ONE dimension — one point in time, all of
 * them, two of them — and each tab discarded the others' state. The rail collapses them:
 * click a version to read it, shift-click a second to compare the pair. There is nothing to
 * navigate to, because the history is the navigation. Scope follows the reader: with an
 * article open it shows that article's distinct texts, otherwise the law's own versions.
 */
export function VersionRail({ dates, current, compareTo, scope, today, onPick, onCompare, onClear }: {
  dates: string[]; current?: string; compareTo?: string; scope: string; today: string;
  onPick: (d: string) => void; onCompare: (d: string) => void; onClear: () => void;
}) {
  const box = useRef<HTMLDivElement>(null);
  const [w, setW] = useState(720);

  useLayoutEffect(() => {
    const el = box.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(([e]) => setW(e.contentRect.width));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  if (dates.length === 0) return null;
  const i = current ? dates.indexOf(current) : -1;
  const j = compareTo ? dates.indexOf(compareTo) : -1;
  const xs = layout(dates, w);
  const labels = labelled(dates, xs, w, i, j);
  const ahead = dates.filter((d) => d > today).length;
  const gaps = dates.slice(1).map((d, k) => (ms(d) - ms(dates[k])) / 86400000).sort((a, b) => a - b);
  const median = gaps.length ? Math.round(gaps[Math.floor(gaps.length / 2)]) : 0;
  const [a, b] = i >= 0 && j >= 0 ? [Math.min(xs[i], xs[j]), Math.max(xs[i], xs[j])] : [0, 0];

  return (
    <div className="railbox">
      <div className="railhead">
        <span className="tag">{dates.length} {scope}</span>
        {median > 0 ? <span className="tag">every {median} days (median)</span> : null}
        {ahead > 0 ? <span className="tag warn">{ahead} not yet in force</span> : null}
        {/* Keyed off compareTo, not off finding it on the rail: a compared date need not be one
            of these ticks (article texts are a subset of the law's versions), and a comparison
            with no visible way out is a trap. */}
        {compareTo ? (
          <button className="tag act" onClick={onClear}>
            comparing {current && current < compareTo ? current : compareTo} →{" "}
            {current && current < compareTo ? compareTo : current} ✕
          </button>
        ) : (
          <span className="hint">shift-click a second version to compare</span>
        )}
        <span className="grow" />
        <button className="stepbtn" disabled={i <= 0} aria-label="Previous version"
                onClick={() => onPick(dates[i - 1])}>←</button>
        <button className="stepbtn" disabled={i < 0 || i >= dates.length - 1} aria-label="Next version"
                onClick={() => onPick(dates[i + 1])}>→</button>
      </div>
      <div className="rail" ref={box}>
        <div className="axis" />
        {j >= 0 ? <div className="band" style={{ left: a, width: Math.max(2, b - a) }} /> : null}
        {dates.map((d, k) => (
          <button key={d}
                  className={`tick${k === i ? " on" : ""}${k === j ? " cmp" : ""}${d > today ? " future" : ""}`}
                  style={{ left: xs[k] }} title={`${d}${d > today ? ", not yet in force" : ""}`}
                  tabIndex={labels.has(k) || k === i ? 0 : -1}
                  aria-label={`${d}${k === i ? " (showing)" : ""}`}
                  onClick={(e) => (e.shiftKey ? onCompare(d) : onPick(d))} />
        ))}
        {[...labels].map((k) => (
          <span key={k} className={`rlbl${k === i ? " on" : ""}`} style={{ left: xs[k] }}>{dates[k]}</span>
        ))}
      </div>
    </div>
  );
}

/**
 * Time-proportional, so the rhythm of amendment stays legible — a law rewritten every three
 * weeks looks nothing like one revised twice a century. Then ticks are pushed apart to keep a
 * clickable target each, because proportional alone collapses dense runs into an unhittable smear.
 */
function layout(dates: string[], width: number): number[] {
  const pad = 12;
  const usable = Math.max(1, width - pad * 2);
  const lo = ms(dates[0]);
  const span = Math.max(1, ms(dates[dates.length - 1]) - lo);
  const xs = dates.map((d) => pad + ((ms(d) - lo) / span) * usable);
  const min = Math.min(9, usable / Math.max(1, dates.length - 1));
  for (let k = 1; k < xs.length; k++) xs[k] = Math.max(xs[k], xs[k - 1] + min);
  const last = xs.length - 1;
  if (xs[last] > pad + usable) {
    xs[last] = pad + usable;
    for (let k = last - 1; k >= 0; k--) xs[k] = Math.min(xs[k], xs[k + 1] - min);
  }
  return xs;
}

/** As many date labels as fit without touching; the version on screen always keeps its own. */
function labelled(dates: string[], xs: number[], width: number, cur: number, cmp: number): Set<number> {
  const W = 82;
  const room = Math.max(2, Math.floor(width / W));
  const placed: number[] = [];
  const keep = new Set<number>();
  const fits = (k: number) => placed.every((p) => Math.abs(xs[p] - xs[k]) >= W);
  // Priority: what you are reading, what you are comparing against, then the two ends,
  // then an even spread through whatever room is left.
  const order = [cur, cmp, 0, dates.length - 1];
  for (let k = 0; k < dates.length; k++) order.push(k);
  for (const k of order) {
    if (k < 0 || keep.has(k) || keep.size >= room) continue;
    if (k === cur || fits(k)) { keep.add(k); placed.push(k); }
  }
  return keep;
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
      // Read is guarded against pulling a whole code into the tab; Compare was not, and it
      // pulls TWO. A 1,160-article code froze the page. Above the threshold, compare the
      // focused article instead and say so.
      if (!anchor) {
        const outline = await tool<any>("as_of", { work, date, mode: "outline" });
        const o = first<any>(outline, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
        if ((o?.provisions?.length ?? 0) > 120) throw new Error("TOO_LARGE");
      }
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
  if (state.error === "TOO_LARGE")
    return <Empty>This law is too large to compare whole, open an article first, then compare.</Empty>;
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
              <span className="lbl">{clean(r.title) ?? r.work}</span>
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

/** A history answer needs no mode of its own: the rail is already showing it. */
export function modeFor(ui?: UiEffect): State["mode"] | undefined {
  if (ui?.diff) return "compare";
  if (ui?.history || ui?.provision) return "read";
  return undefined;
}

/** Publishers put escaped newlines and editorial notes inside titles. A bar label is one line,
 *  so collapse the whitespace and cut at the first sentence-ending break. */
export const clean = (t?: string) => t
  ? t.replace(/\n/g, " ").replace(/\s+/g, " ").trim().slice(0, 120)
  : t;

/** Strip the Markdown emphasis publishers put in structural headings. */
const plain = (s: string) => s.replace(/\*+/g, "").replace(/\s+/g, " ").trim();

/**
 * Hierarchical table of contents for a large code. Groups by the first level of each
 * provision's path (Book / Title / Chapter / Section) and opens one section at a time, with
 * a filter box over article numbers and headings. Precedent: legislation.gov.uk's ToC view,
 * which exists for the same reason — a national code is not a scrollable list.
 */
function Outline({ items, current, onPick }: {
  items: ProvisionItem[]; current?: string; onPick: (anchor: string) => void;
}) {
  const [open, setOpen] = useState<string>();
  const [q, setQ] = useState("");

  const needle = q.trim().toLowerCase();
  const matches = (p: ProvisionItem) =>
    !needle || `${p.num ?? ""} ${p.heading ?? ""} ${p.anchor}`.toLowerCase().includes(needle);

  const groups = new Map<string, ProvisionItem[]>();
  const sectionOf = new Map<string, string>();
  for (const p of items) {
    const top = plain((p.path ?? "").split(" / ")[0] || "Without a section");
    sectionOf.set(p.anchor, top);
    if (!matches(p)) continue;
    (groups.get(top) ?? groups.set(top, []).get(top)!).push(p);
  }
  // The section holding the article you are reading opens itself — otherwise the outline
  // answers "where am I?" with a list of closed boxes.
  const here = current ? sectionOf.get(current) : undefined;
  const single = groups.size === 1;

  return (
    <>
      <div className="tochead">
        <b>Contents</b>
        <span className="sub mono">{items.length}</span>
      </div>
      <input className="filter" value={q} onChange={(e) => setQ(e.target.value)}
             aria-label="Filter articles by number or heading"
             placeholder="Filter articles…" />
      {needle && groups.size === 0 ? <p className="sub">No article matches “{q}”.</p> : null}
      <ul className="toc">
        {[...groups].map(([section, list]) => {
          const expanded = single || open === section || needle.length > 0 || (open === undefined && section === here);
          return (
            <li key={section}>
              {single ? null : (
                <button className="toch" aria-expanded={expanded}
                        onClick={() => setOpen(expanded && !needle ? "" : section)}>
                  <span>{expanded ? "▾" : "▸"} {section}</span>
                  <span className="sub mono">{list.length}</span>
                </button>
              )}
              {expanded ? (
                <ul className="rows">
                  {list.map((p) => (
                    <li key={p.anchor}>
                      <button className={`rowbtn${p.anchor === current ? " on" : ""}`}
                              aria-current={p.anchor === current ? "true" : undefined}
                              onClick={() => onPick(p.anchor)}>
                        <span>{p.num ?? p.anchor}{p.heading ? `, ${plain(p.heading)}` : ""}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
            </li>
          );
        })}
      </ul>
    </>
  );
}
