import { useCallback, useEffect, useRef, useState } from "react";
import { ask, first, tool, type AskReply, type ProvisionItem, type UiEffect } from "./api";
import { publisherOf, useWorkspace, workSlug, type Space, type State } from "./state";
import { Compare, Empty, Gap, HistoryRail, InForce, Provision, Ranking, hasView, modeFor } from "./views";
import { LawPicker, PeriodPicker, TopicSearch, shorten } from "./pickers";

const today = () => new Date().toISOString().slice(0, 10);

/** Follow-ups derived from the view on screen — always valid, and free. */
function chipsFor(s: State, ui?: UiEffect): { label: string; go: Partial<State> }[] {
  if (ui?.ranking) return [{ label: "Try the last twelve months", go: { from: shift(today(), -365), until: today(), mode: "read" } }];
  if (s.mode === "read" && s.work) return [
    { label: "How did this change?", go: { mode: "history" } },
    { label: "Compare with a year later", go: { mode: "compare", to: shift(s.date ?? today(), 365) } },
  ];
  if (s.mode === "history") return [{ label: "Read the current text", go: { mode: "read", date: today() } }];
  if (s.mode === "compare") return [{ label: "Read the later version", go: { mode: "read", date: s.to } }];
  return [];
}

function shift(date: string, days: number) {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

export default function App() {
  const [s, go] = useWorkspace();
  const [q, setQ] = useState("");
  const [busy, setBusy] = useState(false);
  const [said, setSaid] = useState<string>();
  const [ui, setUi] = useState<UiEffect>();
  const [loaded, setLoaded] = useState<{ items: ProvisionItem[]; from: string; to?: string }>();
  const [title, setTitle] = useState<string>();
  const abort = useRef<AbortController>();

  // Deterministic loading: switching mode or date calls the public MCP endpoint directly.
  // No model in the loop — playing with the workspace must be instant and repeatable.
  useEffect(() => {
    if (!s.work) return;
    let live = true;
    // Never show one law's text under another's heading: clear before fetching.
    setLoaded(undefined);
    if (s.mode === "read") {
      const date = s.date ?? today();
      // A code can carry thousands of articles: ask for the outline first and only pull
      // full text when it is small enough to read. Otherwise show the outline and let the
      // reader choose — dumping a whole code would freeze the tab and help nobody.
      const fetchRead = async () => {
        if (s.anchor)
          return tool<any>("as_of", { work: s.work, date, mode: "select", anchors: s.anchor });
        const outline = await tool<any>("as_of", { work: s.work, date, mode: "outline" });
        const o = first<any>(outline, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
        const n = o?.provisions?.length ?? 0;
        if (n === 0 || n > 80) return outline;
        return tool<any>("as_of", { work: s.work, date, mode: "full" });
      };
      fetchRead()
        .then((res) => {
          if (!live) return;
          const one = first<any>(res, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
          const doc = one?.document ?? one;
          setTitle(shorten(doc?.title));
          const items = (one?.provisions ?? []) as ProvisionItem[];
          setLoaded({ items, from: doc?.valid_from ?? date, to: doc?.valid_to });
          if (items.length === 0)
            setUi({ gap: { status: one?.envelope?.status ?? "no_result", explanation: "No text is held for this law on that date.", available: [] } });
          else setUi(undefined);
        })
        .catch(() => live && setUi({ gap: { status: "error", explanation: "That version could not be loaded.", available: [] } }));
    }
    return () => { live = false; };
  }, [s.work, s.date, s.mode, s.anchor]);

  // Time workspace: a period loads the ranking deterministically, so the follow-up chips
  // and any /?from=&until= link land on real content instead of an empty panel.
  useEffect(() => {
    if (s.work || !s.from || !s.until) return;
    let live = true;
    tool<any>("changes_in_period", { from_date: s.from, to_date: s.until, order: s.order ?? "by_churn", limit: 25 })
      .then((res) => {
        if (!live) return;
        const one = first<any>(res, (x) => Array.isArray(x?.changes) && x.changes.length > 0);
        setUi(one?.changes?.length
          ? { ranking: { from_date: s.from!, to_date: s.until!, order: s.order ?? "by_churn",
                         works_changed: one.works_changed, new_versions: one.new_versions, rows: one.changes } }
          : { gap: { status: "no_changes_in_period", explanation: "Nothing changed in that window.", available: [] } });
      })
      .catch(() => {});
    return () => { live = false; };
  }, [s.work, s.from, s.until, s.order]);

  useEffect(() => {
    let live = true;
    if (s.mode === "history" && s.anchor) {
      tool<any>("article_history", { work: s.work, anchor: s.anchor })
        .then((res) => {
          if (!live) return;
          const one = first<any>(res, (x) => Array.isArray(x?.states) && x.states.length > 0);
          setUi(one?.states?.length ? { history: { subject: { work: s.work! }, anchor: s.anchor!, distinct_texts: one.distinct_texts, states: one.states } }
                                    : { gap: { status: one?.envelope?.status ?? "no_provision_history", explanation: "No per-article history is held for this article.", available: [] } });
        }).catch(() => {});
    }
    return () => { live = false; };
  }, [s.work, s.date, s.mode, s.anchor]);

  const submit = useCallback(async (text: string) => {
    if (!text.trim() || busy) return;
    setBusy(true); setSaid(undefined);
    abort.current?.abort();
    abort.current = new AbortController();
    try {
      const r: AskReply = await ask(text.trim(), abort.current.signal);
      setSaid(r.error ?? r.reply);
      if (hasView(r.ui)) {
        setUi(r.ui);
        const subj = r.ui!.provision?.subject ?? r.ui!.history?.subject ?? r.ui!.diff?.subject;
        const m = modeFor(r.ui);
        if (subj?.work) {
          setTitle(subj.title);
          if (r.ui!.provision) setLoaded({ items: r.ui!.provision.provisions, from: r.ui!.provision.valid_from, to: r.ui!.provision.valid_to });
          go({ work: subj.work, date: subj.date ?? r.ui!.provision?.valid_from, anchor: subj.anchor, mode: m ?? "read",
               ...(r.ui!.diff ? { date: r.ui!.diff.from_date, to: r.ui!.diff.to_date } : {}) });
        } else if (r.ui!.ranking) {
          go({ work: undefined, from: r.ui!.ranking.from_date, until: r.ui!.ranking.to_date, order: r.ui!.ranking.order as State["order"], mode: "read" });
        }
      }
    } catch { setSaid("The request failed — try again."); }
    finally { setBusy(false); }
  }, [busy, go]);

  const openLaw = (work: string, date: string) => { setUi(undefined); go({ work, date, to: undefined, anchor: undefined, mode: "read", space: "law" }); };
  const openDiff = (work: string, from: string, to: string) => { setUi(undefined); go({ work, date: from, to, mode: "compare", space: "law" }); };

  // Which framework is on screen: whatever the URL says, else inferred from what is loaded.
  const space: Space = s.space ?? (s.work ? "law" : s.q ? "topic" : (s.from || ui?.ranking) ? "time" : "law");

  const switchTo = (sp: Space) => {
    setUi(undefined);
    setSaid(undefined);
    if (sp === "time") go({ space: sp, work: undefined, anchor: undefined, from: s.from ?? shift(today(), -365), until: s.until ?? today(), order: s.order ?? "by_churn" });
    else if (sp === "topic") go({ space: sp, work: undefined, anchor: undefined });
    else go({ space: sp, from: undefined, until: undefined });
  };

  return (
    <div className="ws">
      <form className="cmd" onSubmit={(e) => { e.preventDefault(); submit(q); setQ(""); }}>
        <span className="brand">Lex</span>
        <input value={q} onChange={(e) => setQ(e.target.value)} disabled={busy}
               placeholder="Ask anything — or pick a law below" aria-label="Ask" />
        <button type="submit" disabled={busy}>{busy ? "…" : "Ask"}</button>
      </form>

      {said ? <div className="said"><b>what I found</b>{said}</div> : null}

      <nav className="spaces">
        {(["law", "time", "topic"] as const).map((sp) => (
          <button key={sp} className={space === sp ? "on" : ""} onClick={() => switchTo(sp)}>
            {sp === "law" ? "A law" : sp === "time" ? "A period" : "A topic"}
          </button>
        ))}
      </nav>

      {space === "time" ? (
        <PeriodPicker from={s.from ?? shift(today(), -365)} until={s.until ?? today()}
                      order={s.order ?? "by_churn"}
                      onChange={(next) => { setUi(undefined); go({ ...next, work: undefined }); }} />
      ) : null}

      {space === "topic" ? (
        <TopicSearch q={s.q ?? ""} asOf={s.asOf}
                     onQuery={(query, asOf) => go({ q: query, asOf, work: undefined })}
                     onOpen={(work, date, anchor) => { setUi(undefined); go({ work, date, anchor, mode: "read", space: "law" }); }} />
      ) : null}

      {space === "law" ? (
        <>
          <div className="sel">
            <LawPicker current={title ?? (s.work ? workSlug(s.work) : undefined)}
                       onPick={(h) => { setUi(undefined); setTitle(h.title); go({ work: h.work, date: h.validFrom ?? today(), anchor: undefined, to: undefined, mode: "read" }); }} />
            {s.work ? (
            <label className="pick"><i>{s.mode === "compare" ? "from" : "date"}</i>
              <input type="date" value={s.date ?? today()} onChange={(e) => go({ date: e.target.value })} />
            </label>) : null}
            {s.mode === "compare" ? (
              <label className="pick"><i>to</i>
                <input type="date" value={s.to ?? today()} onChange={(e) => go({ to: e.target.value })} />
              </label>
            ) : null}
            {s.anchor ? (
              <button className="pick" onClick={() => go({ anchor: undefined, mode: "read" })}>
                <i>article</i>{s.anchor} ✕</button>
            ) : null}
            <span className="grow" />
            {s.work ? <a className="pick" href={`/${publisherOf(s.work)}/${workSlug(s.work)}`}>permalink ↗</a> : null}
          </div>

          {s.work ? (
          <nav className="modes">
            {(["read", "history", "compare"] as const).map((m) => (
              <button key={m} className={s.mode === m ? "on" : ""}
                      disabled={m === "history" && !s.anchor}
                      title={m === "history" && !s.anchor ? "Pick an article first" : undefined}
                      onClick={() => go({ mode: m, ...(m === "compare" && !s.to ? { to: today() } : {}) })}>
                {m === "read" ? "Read" : m === "history" ? "History" : "Compare"}
              </button>
            ))}
          </nav>) : null}
        </>
      ) : null}

      <div className="work">
        {space === "topic" ? null :
         ui?.gap ? <Gap {...ui.gap} /> :
         ui?.ranking ? <Ranking rows={ui.ranking.rows} worksChanged={ui.ranking.works_changed}
                                newVersions={ui.ranking.new_versions} from={ui.ranking.from_date}
                                to={ui.ranking.to_date} onOpen={openDiff} /> :
         ui?.in_force ? <InForce date={ui.in_force.date} total={ui.in_force.total} rows={ui.in_force.rows} onOpen={openLaw} /> :
         ui?.history ? <HistoryRail states={ui.history.states} anchor={ui.history.anchor} work={ui.history.subject.work} /> :
         s.work && s.mode === "compare" ? <Compare work={s.work} from={s.date ?? today()} to={s.to ?? today()} anchor={s.anchor} /> :
         s.work && s.mode === "read" && loaded ? <Provision items={loaded.items} validFrom={loaded.from} validTo={loaded.to} work={s.work} onPick={(a) => go({ anchor: a })} /> :
         s.work ? <Empty>Loading…</Empty> :
         space === "time" ? <Empty>Pick a period above.</Empty> :
         <Intro onPick={submit} />}
      </div>

      {(s.work || ui) ? (
        <div className="chips">
          {chipsFor(s, ui).map((c) => (
            <button key={c.label} className="chip" onClick={() => { setUi(undefined); go(c.go); }}>{c.label}</button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function Intro({ onPick }: { onPick: (q: string) => void }) {
  const examples = [
    "What did the Covid rules say on 1 February 2021?",
    "Which Luxembourg laws changed most during the pandemic?",
    "How has Article 92 of the CRR changed?",
    "Que disait le Code du travail en 2019 ?",
  ];
  return (
    <div className="intro">
      <p className="lede">Ask in plain language, then keep playing — every answer becomes a workspace
        you can re-date, compare and trace without asking again.</p>
      <div className="chips">
        {examples.map((e) => <button key={e} className="chip" onClick={() => onPick(e)}>{e}</button>)}
      </div>
    </div>
  );
}
