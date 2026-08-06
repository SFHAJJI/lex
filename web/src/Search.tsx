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

type HitMeta = {
  validFrom?: string; validTo?: string; hierarchy?: string; language?: string;
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
type SearchFacets = {
  jurisdictions: { value: string; code: string }[];
  hierarchies: string[]; domains: string[]; act_forms: string[];
  binding_statuses: string[]; languages: string[];
};

const DOORS: Door[] = (() => {
  try {
    const el = document.getElementById("doors");
    const parsed = el?.textContent ? JSON.parse(el.textContent) : [];
    return Array.isArray(parsed) ? (parsed as Door[]) : [];
  } catch {
    return [];   // a malformed block costs the suggestions, never the search box
  }
})();

const SEARCH_FACETS: SearchFacets = (() => {
  const empty: SearchFacets = { jurisdictions: [], hierarchies: [], domains: [], act_forms: [],
                                binding_statuses: [], languages: [] };
  try {
    const text = document.getElementById("search-facets")?.textContent;
    return text ? { ...empty, ...JSON.parse(text) } : empty;
  } catch {
    return empty;
  }
})();

const LABELS: Record<string, string> = {
  primary_eu_law: "EU primary law", secondary_eu_law: "EU secondary law",
  "financial-services": "Financial services", "aml-corporate": "AML and corporate",
  "consumer-environment": "Consumer and environment", "procurement-and-ip": "Procurement and IP",
  "judicial-cooperation": "Judicial cooperation",
  REG: "Regulation", DIR: "Directive", DEC: "Decision", TREATY: "Treaty", CHARTER: "Charter",
  in_force: "In force", not_in_force: "Not in force", unknown: "Not classified",
};
const label = (value: string) => LABELS[value] ?? value.replaceAll("_", " ").replaceAll("-", " ")
  .replace(/\b\w/g, (letter) => letter.toUpperCase());
const jurisdictionLabel = ({ code }: { code: string }) => code === "EU" ? "European Union" :
  new Intl.DisplayNames(["en"], { type: "region" }).of(code) ?? code;
const languageLabel = (code: string) =>
  new Intl.DisplayNames(["en"], { type: "language" }).of(code) ?? code;

/** A bare date is a question in itself: what applied that day. */
const DATE_ONLY = /^\s*(\d{4}-\d{2}-\d{2})\s*$/;

export default function Search(p: SearchProps) {
  const [text, setText] = useState(p.state.q ?? "");
  const [works, setWorks] = useState<WorkHit[]>([]);
  const [articles, setArticles] = useState<ArticleHit[]>([]);
  const [busy, setBusy] = useState(false);
  const [layer, setLayer] = useState<LayerId | "">("");
  const [retrieval, setRetrieval] = useState<"keyword" | "hybrid">("keyword");
  const [jurisdiction, setJurisdiction] = useState("");
  const [hierarchy, setHierarchy] = useState("");
  const [domain, setDomain] = useState("");
  const [actForm, setActForm] = useState("");
  const [bindingStatus, setBindingStatus] = useState("");
  const [language, setLanguage] = useState("");
  const [modeUsed, setModeUsed] = useState("keyword");
  const [expansions, setExpansions] = useState<string[]>([]);
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
  const activeFilters = [retrieval === "hybrid", jurisdiction, hierarchy, domain, layer,
                         actForm, bindingStatus, language]
    .filter(Boolean).length;

  useEffect(() => {
    if (!q.trim()) { setWorks([]); setArticles([]); return; }
    let live = true;
    setBusy(true);
    const types = LAYERS.find((l) => l.id === layer)?.types;
    tool<any>("search", { query: q.trim(), limit: 40, time_scope: "as_of", as_of: asOf ?? p.today,
                          retrieval_mode: retrieval, fuzzy: "auto",
                          ...(jurisdiction ? { jurisdiction } : {}),
                          ...(hierarchy ? { hierarchy } : {}), ...(domain ? { domain } : {}),
                          ...(actForm ? { act_form: actForm } : {}),
                          ...(bindingStatus ? { binding_status: bindingStatus } : {}),
                          ...(language ? { language } : {}),
                          ...(types ? { document_type: types } : {}) })
      .then((res) => {
        if (!live) return;
        const envelopes = Array.isArray(res) ? res : [res];
        const hits = envelopes.flatMap((e: any) => e?.hits ?? []);
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
      .catch(() => { if (live) { setWorks([]); setArticles([]); } })
      .finally(() => { if (live) setBusy(false); });
    return () => { live = false; };
  }, [q, asOf, layer, retrieval, jurisdiction, hierarchy, domain, actForm, bindingStatus, language]);

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
            <span className="badge">{modeUsed === "hybrid" ? "keyword + meaning" : "keyword search"}</span>
            <span className="grow" />
          </div>

          <details className="search-filters">
            <summary>Narrow results{activeFilters ? ` (${activeFilters} active)` : ""}</summary>
            <div className="filter-grid">
              <label><span>Search method</span>
                <select className="reslayer" value={retrieval}
                        onChange={(e) => setRetrieval(e.target.value as "keyword" | "hybrid")}>
                  <option value="keyword">Exact keywords</option>
                  <option value="hybrid">Keywords + meaning (preview)</option>
                </select>
              </label>
              <label><span>Jurisdiction</span>
                <select className="reslayer" value={jurisdiction}
                        onChange={(e) => setJurisdiction(e.target.value)}>
                  <option value="">Every jurisdiction</option>
                  {SEARCH_FACETS.jurisdictions.map((item) =>
                    <option key={item.value} value={item.value}>{jurisdictionLabel(item)}</option>)}
                </select>
              </label>
              <label><span>Hierarchy</span>
                <select className="reslayer" value={hierarchy}
                        onChange={(e) => setHierarchy(e.target.value)}>
                  <option value="">Every hierarchy</option>
                  {SEARCH_FACETS.hierarchies.map((value) =>
                    <option key={value} value={value}>{label(value)}</option>)}
                </select>
              </label>
              <label><span>Practice area</span>
                <select className="reslayer" value={domain} onChange={(e) => setDomain(e.target.value)}>
                  <option value="">Every practice area</option>
                  {SEARCH_FACETS.domains.map((value) =>
                    <option key={value} value={value}>{label(value)}</option>)}
                </select>
              </label>
              <label><span>Kind of law</span>
                <select className="reslayer" value={layer}
                        onChange={(e) => setLayer(e.target.value as LayerId | "")}>
                  <option value="">Every kind of law</option>
                  {LAYERS.map((l) => <option key={l.id} value={l.id}>{l.label}</option>)}
                </select>
              </label>
              <label><span>EU legal form</span>
                <select className="reslayer" value={actForm} onChange={(e) => setActForm(e.target.value)}>
                  <option value="">Every legal form</option>
                  {SEARCH_FACETS.act_forms.map((value) =>
                    <option key={value} value={value}>{label(value)}</option>)}
                </select>
              </label>
              <label><span>EU legal status</span>
                <select className="reslayer" value={bindingStatus}
                        onChange={(e) => setBindingStatus(e.target.value)}>
                  <option value="">Every status</option>
                  {SEARCH_FACETS.binding_statuses.map((value) =>
                    <option key={value} value={value}>{label(value)}</option>)}
                </select>
              </label>
              <label><span>Language</span>
                <select className="reslayer" value={language} onChange={(e) => setLanguage(e.target.value)}>
                  <option value="">Every language</option>
                  {SEARCH_FACETS.languages.map((value) =>
                    <option key={value} value={value}>{languageLabel(value)}</option>)}
                </select>
              </label>
            </div>
          </details>

          {expansions.length > 0 ? <p className="sub expansion">Spelling fallback tried: {expansions.join(", ")}</p> : null}

          {busy && works.length === 0 && articles.length === 0 ? <ResultsSkeleton /> : null}

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
                {articles.map((a, i) => (
                  <li key={`${a.work}-${a.anchor}-${i}`}>
                    <button className="rowbtn" onClick={() => p.onOpen(a.work, a.validFrom, a.anchor)}>
                      <span>{a.num ?? a.anchor} <span className="sub">· {a.title}</span></span>
                      {a.snippet ? <span className="sub"><Marked text={a.snippet} /></span> : null}
                      <span className="hitmeta"><Validity hit={a} />{a.language ? <span>{a.language.toUpperCase()}</span> : null}</span>
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

function Validity({ hit }: { hit: HitMeta }) {
  if (!hit.validFrom) return null;
  return <span>valid {hit.validFrom} → {hit.validTo ?? "ongoing"}</span>;
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
