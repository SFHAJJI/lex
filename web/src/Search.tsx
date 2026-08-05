import { useEffect, useRef, useState } from "react";
import { tool } from "./api";
import { LAYERS, type LayerId, type State } from "./state";
import { shorten } from "./pickers";
import { ResultsSkeleton } from "./Skeleton";

/**
 * One box, one date.
 *
 * What was here before was four tabs: a law, a topic, a period, a date. Those are four query
 * types, and they mapped one to one onto the tools underneath, which is the tell: the interface
 * was a picture of the machine. It made a visitor classify their own question before they could
 * ask it, and two of the four sounded like the same thing unless you already knew the difference.
 *
 * Three things about this corpus that the four tabs ignored:
 *
 * The DATE is not a query type, it is the product. "A law is not one document" is the whole
 * thesis, so "as of when" belongs beside everything, always visible, never a tab you might not
 * click.
 *
 * There is only ONE search. "32016r0679", "code du travail" and "conge parental" are all just
 * what are you looking for, and telling them apart is the machine's job, not the reader's.
 *
 * And "what changed across the corpus" is not a search at all, it is a report: not looking
 * something up but watching a body of law. It lives elsewhere.
 */
export interface SearchProps {
  state: State;
  today: string;
  onOpen: (work: string, date?: string, anchor?: string) => void;
  onQuery: (q: string) => void;
  onAsOf: (d?: string) => void;
  onMonitor: () => void;
}

type WorkHit = { work: string; title: string; validFrom?: string };
type ArticleHit = { work: string; title: string; anchor: string; num?: string; snippet?: string; validFrom: string };

/**
 * The suggested starting points, emitted by the server from the index that will serve them.
 *
 * They used to be hand-written here, and one of the three pointed at "lu-legilux:code-penal",
 * which is not a work: the Code penal is loi-1879-06-18-n1. A visitor who took one of only three
 * invitations on the front page was told the work was unknown. Nothing could catch that from in
 * here, because this bundle has no idea what the index holds.
 */
type Door = { work: string; label: string };

const DOORS: Door[] = (() => {
  try {
    const el = document.getElementById("doors");
    const parsed = el?.textContent ? JSON.parse(el.textContent) : [];
    return Array.isArray(parsed) ? (parsed as Door[]) : [];
  } catch {
    return [];   // a malformed block costs the suggestions, never the search box
  }
})();

/** A bare date is a question in itself: what applied that day. */
const DATE_ONLY = /^\s*(\d{4}-\d{2}-\d{2})\s*$/;

export default function Search(p: SearchProps) {
  const [text, setText] = useState(p.state.q ?? "");
  const [works, setWorks] = useState<WorkHit[]>([]);
  const [articles, setArticles] = useState<ArticleHit[]>([]);
  const [busy, setBusy] = useState(false);
  const [layer, setLayer] = useState<LayerId | "">("");
  const box = useRef<HTMLInputElement>(null);

  useEffect(() => setText(p.state.q ?? ""), [p.state.q]);
  useEffect(() => { box.current?.focus(); }, []);

  const q = p.state.q ?? "";
  const asOf = p.state.asOf;

  useEffect(() => {
    if (!q.trim()) { setWorks([]); setArticles([]); return; }
    let live = true;
    setBusy(true);
    const types = LAYERS.find((l) => l.id === layer)?.types;
    tool<any>("search", { query: q.trim(), limit: 40, ...(asOf ? { as_of: asOf } : {}),
                          ...(types ? { document_type: types } : {}) })
      .then((res) => {
        if (!live) return;
        const hits = (Array.isArray(res) ? res : [res]).flatMap((e: any) => e?.hits ?? []);
        // The same hits answer two different questions, so they are split rather than ranked
        // together: "which law is this" and "where is this said". A reader almost always wants
        // the first when they typed a name, and the second when they typed words.
        const byWork = new Map<string, WorkHit>();
        const arts: ArticleHit[] = [];
        for (const h of hits) {
          const work = String(h.lex_id ?? "").split(":").slice(0, 2).join(":");
          if (!work) continue;
          const title = shorten(h.title) ?? work;
          if (!byWork.has(work)) byWork.set(work, { work, title, validFrom: h.valid_from });
          if (h.anchor)
            arts.push({ work, title, anchor: h.anchor, num: h.provision_num,
                        snippet: h.snippet, validFrom: h.valid_from });
        }
        setWorks([...byWork.values()].slice(0, 8));
        setArticles(arts.slice(0, 25));
      })
      .catch(() => { if (live) { setWorks([]); setArticles([]); } })
      .finally(() => { if (live) setBusy(false); });
    return () => { live = false; };
  }, [q, asOf, layer]);

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    const m = DATE_ONLY.exec(text);
    if (m) { p.onAsOf(m[1]); p.onQuery(""); return; }
    p.onQuery(text);
  };

  return (
    <section className="finder" aria-label="Search the corpus">
      <form className="one" onSubmit={submit}>
        <input ref={box} className="onebox" value={text} onChange={(e) => setText(e.target.value)}
               placeholder="A law, an identifier, or words in the text"
               aria-label="Search for a law, an identifier, or words in the text" />
        {/* The date sits beside the question, not behind a tab, because every question this
            corpus answers has an "as of when" and that is the entire point of it. */}
        <label className="asof" title="Read everything as it stood on this date">
          <i>as of</i>
          <input type="date" value={asOf ?? ""} max="2030-12-31"
                 aria-label="Read everything as it stood on this date"
                 onChange={(e) => p.onAsOf(e.target.value || undefined)} />
        </label>
        <button type="submit">Search</button>
      </form>

      <div className="onehint">
        {asOf
          ? <>Reading the corpus as it stood on <b>{asOf}</b>. <button className="linky" onClick={() => p.onAsOf(undefined)}>use today instead</button></>
          : <>Reading the corpus as it stands today. Set a date to read it as it was.</>}
      </div>

      {/* This row is always here. It used to appear only on an empty box, which meant that the
          moment you searched for anything, the only route to the report disappeared with it. */}
      <div className="doors">
        {q ? (
          <button className="door" onClick={() => { setText(""); p.onQuery(""); }}>clear</button>
        ) : DOORS.length > 0 ? (
          <>
            <span className="doors-h">Try</span>
            {DOORS.map((d) => (
              <button key={d.work} className="door" onClick={() => p.onOpen(d.work, asOf)}>{d.label}</button>
            ))}
          </>
        ) : null}
        <button className="door" onClick={p.onMonitor}>What changed recently</button>
      </div>

      {q ? (
        <div className="results">
          <div className="res-head">
            <span className="sub">{busy ? "Searching…" : `${works.length} law${works.length === 1 ? "" : "s"}, ${articles.length} article${articles.length === 1 ? "" : "s"}`}</span>
            <span className="grow" />
            <select className="reslayer" aria-label="Narrow to a layer of the law"
                    value={layer} onChange={(e) => setLayer(e.target.value as LayerId | "")}>
              <option value="">every kind of law</option>
              {LAYERS.map((l) => <option key={l.id} value={l.id}>{l.label}</option>)}
            </select>
          </div>

          {busy && works.length === 0 && articles.length === 0 ? <ResultsSkeleton /> : null}

          {works.length > 0 ? (
            <>
              <h4 className="res-h">Laws</h4>
              <ul className="rows">
                {works.map((w) => (
                  <li key={w.work}>
                    <button className="rowbtn" onClick={() => p.onOpen(w.work, asOf)}>
                      <span>{w.title}</span>
                      <span className="sub mono">{w.work.split(":")[1]}</span>
                    </button>
                  </li>
                ))}
              </ul>
            </>
          ) : null}

          {articles.length > 0 ? (
            <>
              <h4 className="res-h">Where it is said</h4>
              <ul className="rows">
                {articles.map((a, i) => (
                  <li key={`${a.work}-${a.anchor}-${i}`}>
                    <button className="rowbtn" onClick={() => p.onOpen(a.work, a.validFrom, a.anchor)}>
                      <span>{a.num ?? a.anchor} <span className="sub">· {a.title}</span></span>
                      {a.snippet ? <span className="sub"><Marked text={a.snippet} /></span> : null}
                    </button>
                  </li>
                ))}
              </ul>
            </>
          ) : null}

          {!busy && works.length === 0 && articles.length === 0 ? (
            <div className="empty">
              <p>Nothing in the corpus matches that.</p>
              <p className="sub">
                Search reads the versions that carry text. Lex also holds dated versions whose
                wording the publisher never issued, and those can be dated but not searched.{" "}
                <a href="/coverage">What Lex holds, and lacks →</a>
              </p>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

/** The index marks matched words with guillemets; they render as marks, not as punctuation. */
function Marked({ text }: { text: string }) {
  const parts = text.replace(/\*+/g, "").split(/(«[^»]*»)/g);
  return (
    <>
      {parts.map((x, i) =>
        x.startsWith("«") && x.endsWith("»")
          ? <mark key={i}>{x.slice(1, -1).trim()}</mark>
          : <span key={i}>{x}</span>)}
    </>
  );
}
