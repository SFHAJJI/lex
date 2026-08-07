import { useEffect, useRef, useState } from "react";
import { tool } from "./api";
import { facetLabel as label, jurisdictionForPublisher, jurisdictionLabel } from "./facets";
import { ScopeFilters } from "./ScopeFilters";
import type { State } from "./state";
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
  onRefine: (next: Partial<State>) => void;
  onMonitor: () => void;
}

type HitMeta = {
  validFrom?: string; validTo?: string; hierarchy?: string; language?: string;
  jurisdiction?: string;
  domains?: string[]; consolidationStatus?: string; matchReasons?: string[];
};
type WorkHit = HitMeta & { work: string; title: string };
type ArticleHit = HitMeta & {
  work: string; title: string; anchor: string; num?: string; snippet?: string; validFrom: string;
};

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
const INITIAL_ARTICLES = 8;

export default function Search(p: SearchProps) {
  const [text, setText] = useState(p.state.q ?? "");
  const [works, setWorks] = useState<WorkHit[]>([]);
  const [articles, setArticles] = useState<ArticleHit[]>([]);
  const [busy, setBusy] = useState(false);
  const [modeUsed, setModeUsed] = useState("keyword");
  const [expansions, setExpansions] = useState<string[]>([]);
  const [error, setError] = useState<string>();
  const [articleLimit, setArticleLimit] = useState(INITIAL_ARTICLES);
  const box = useRef<HTMLInputElement>(null);

  useEffect(() => setText(p.state.q ?? ""), [p.state.q]);
  // Desktop keyboard users benefit from landing in the primary control. On a touch device,
  // focusing it on arrival opens the software keyboard before the reader can inspect the page.
  useEffect(() => {
    if (typeof window.matchMedia === "function" && window.matchMedia("(pointer: fine)").matches)
      box.current?.focus();
  }, []);

  const q = p.state.q ?? "";
  const asOf = p.state.asOf;
  const retrieval = p.state.retrieval ?? "keyword";
  const jurisdiction = p.state.jurisdiction ?? "";
  const hierarchy = p.state.hierarchy ?? "";
  const domain = p.state.domain ?? "";
  const sourceClass = p.state.sourceClass ?? "";
  const actForm = p.state.actForm ?? "";
  const bindingStatus = p.state.bindingStatus ?? "";
  const language = p.state.language ?? "";

  useEffect(() => {
    if (!q.trim()) { setWorks([]); setArticles([]); setError(undefined); return; }
    let live = true;
    setArticleLimit(INITIAL_ARTICLES);
    setBusy(true);
    setError(undefined);
    setExpansions([]);
    setWorks([]);
    setArticles([]);
    tool<any>("search", { query: q.trim(), limit: 40, time_scope: "as_of", as_of: asOf ?? p.today,
                          retrieval_mode: retrieval, fuzzy: "auto",
                          ...(jurisdiction ? { jurisdiction } : {}),
                          ...(hierarchy ? { hierarchy } : {}), ...(domain ? { domain } : {}),
                          ...(actForm ? { act_form: actForm } : {}),
                          ...(bindingStatus ? { binding_status: bindingStatus } : {}),
                          ...(language ? { language } : {}),
                          ...(sourceClass ? { source_class: sourceClass } : {}) })
      .then((res) => {
        if (!live) return;
        const envelopes = Array.isArray(res) ? res : [res];
        const hits = envelopes.flatMap((e: any) => (e?.hits ?? []).map((hit: any) => ({
          ...hit,
          _jurisdiction: e?.envelope?.jurisdiction,
        })));
        setModeUsed(envelopes.some((e: any) => e?.retrieval_mode === "hybrid") ? "hybrid" : "keyword");
        setExpansions([...new Set(envelopes.flatMap((e: any) => e?.query_expansions ?? []))] as string[]);
        // The same hits answer two different questions, so they are split rather than ranked
        // together: "which law is this" and "where is this said". A reader almost always wants
        // the first when they typed a name, and the second when they typed words.
        const byWork = new Map<string, WorkHit>();
        const arts: ArticleHit[] = [];
        for (const h of hits) {
          const work = String(h.lex_id ?? "").split(":").slice(0, 2).join(":");
          if (!work) continue;
          const title = shorten(h.title) ?? work;
          const meta: HitMeta = {
            jurisdiction: h._jurisdiction ?? jurisdictionForPublisher(work.split(":")[0]),
            validFrom: h.valid_from, validTo: h.valid_to, hierarchy: h.hierarchy,
            language: h.language, domains: Array.isArray(h.domains) ? h.domains : [],
            consolidationStatus: h.consolidation_status,
            matchReasons: Array.isArray(h.match_reasons) ? h.match_reasons : [],
          };
          if (!byWork.has(work)) byWork.set(work, { work, title, ...meta });
          if (h.anchor)
            arts.push({ work, title, anchor: h.anchor, num: h.provision_num,
                        snippet: h.snippet, ...meta, validFrom: String(h.valid_from) });
        }
        setWorks([...byWork.values()].slice(0, 8));
        setArticles(arts.slice(0, 25));
      })
      .catch(() => { if (live) { setWorks([]); setArticles([]); setError("Search could not be reached. Try again."); } })
      .finally(() => { if (live) setBusy(false); });
    return () => { live = false; };
  }, [q, asOf, retrieval, jurisdiction, hierarchy, domain, sourceClass, actForm, bindingStatus, language]);

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

      {!q && asOf ? (
        <div className="date-scope">
          <ScopeFilters values={p.state} onChange={p.onRefine} summary="Narrow laws in force" />
        </div>
      ) : null}

      {q ? (
        <div className="results">
          <div className="res-head">
            <span className="sub">{busy ? "Searching…" : `${works.length} law${works.length === 1 ? "" : "s"}, ${articles.length} article${articles.length === 1 ? "" : "s"} shown`}</span>
            <span className="badge">{modeUsed === "hybrid" ? "words + meaning" : "exact words"}</span>
            <span className="grow" />
            <div className="search-mode" role="group" aria-label="Search method">
              <button className={retrieval === "keyword" ? "on" : ""}
                      aria-pressed={retrieval === "keyword"}
                      onClick={() => p.onRefine({ retrieval: "keyword" })}>Exact words</button>
              <button className={retrieval === "hybrid" ? "on" : ""}
                      aria-pressed={retrieval === "hybrid"}
                      title="Adds multilingual meaning search; exact legal identifiers still use exact lookup"
                      onClick={() => p.onRefine({ retrieval: "hybrid" })}>Words + meaning <small>preview</small></button>
            </div>
          </div>

          {retrieval === "hybrid" ? (
            <p className="mode-note">Concept search across French and English. It can take a few seconds;
              identifiers and quoted legal text still use exact lookup.</p>
          ) : null}

          <ScopeFilters values={p.state} onChange={p.onRefine} />

          {expansions.length > 0 ? <p className="sub expansion">Spelling fallback tried: {expansions.join(", ")}</p> : null}

          {busy && works.length === 0 && articles.length === 0 ? <ResultsSkeleton /> : null}

          {error ? <div className="empty"><p>{error}</p></div> : null}

          {works.length > 0 ? (
            <>
              <h4 className="res-h">Laws</h4>
              <ul className="rows">
                {works.map((w) => (
                  <li key={w.work}>
                    <button className="rowbtn" onClick={() => p.onOpen(w.work, asOf)}>
                      <span>{w.title}</span>
                      <span className="hitmeta">
                        <span className="mono">{w.work.split(":")[1]}</span>
                        <Validity hit={w} />
                        <HitContext hit={w} />
                        {w.consolidationStatus === "not_published"
                          ? <span className="warntext">official merged wording not published</span> : null}
                      </span>
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
                {articles.slice(0, articleLimit).map((a, i) => (
                  <li key={`${a.work}-${a.anchor}-${i}`}>
                    <button className="rowbtn" onClick={() => p.onOpen(a.work, a.validFrom, a.anchor)}>
                      <span>{a.num ?? a.anchor} <span className="sub">· {a.title}</span></span>
                      {a.snippet ? <span className="sub"><Marked text={a.snippet} /></span> : null}
                      <span className="hitmeta"><Validity hit={a} /><HitContext hit={a} />
                        {a.language ? <span>{a.language.toUpperCase()}</span> : null}</span>
                    </button>
                  </li>
                ))}
              </ul>
              {articles.length > articleLimit ? (
                <button className="ghost more-results"
                        onClick={() => setArticleLimit((current) => Math.min(articles.length, current + 8))}>
                  Show {Math.min(8, articles.length - articleLimit)} more articles
                </button>
              ) : null}
            </>
          ) : null}

          {!busy && !error && works.length === 0 && articles.length === 0 ? (
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

function Validity({ hit }: { hit: HitMeta }) {
  if (!hit.validFrom) return null;
  return <span>valid {hit.validFrom} → {hit.validTo ?? "ongoing"}</span>;
}

function HitContext({ hit }: { hit: HitMeta }) {
  const reasons = (hit.matchReasons ?? []).map((reason) => reason === "semantic" ? "meaning match" :
    reason === "exact_identifier" ? "exact identifier" : reason === "fuzzy" ? "spelling match" :
    reason === "keyword" ? "word match" : label(reason));
  return <>
    {hit.jurisdiction ? <span>{jurisdictionLabel(hit.jurisdiction)}</span> : null}
    {hit.hierarchy ? <span>{label(hit.hierarchy)}</span> : null}
    {(hit.domains ?? []).slice(0, 2).map((domain) => <span key={domain}>{label(domain)}</span>)}
    {reasons.map((reason) => <span key={reason}>{reason}</span>)}
  </>;
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
