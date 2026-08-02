import { useCallback, useEffect, useRef, useState } from "react";
import { ask, first, tool, type AskReply, type ProvisionItem, type UiEffect } from "./api";
import { publisherOf, useWorkspace, workSlug, type State } from "./state";
import { Compare, Empty, Gap, HistoryRail, InForce, Provision, Ranking, hasView, modeFor } from "./views";

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
    if (s.mode === "read") {
      const date = s.date ?? today();
      tool<any>("as_of", { work: s.work, date, mode: s.anchor ? "select" : "full", ...(s.anchor ? { anchors: s.anchor } : {}) })
        .then((res) => {
          if (!live) return;
          const one = first<any>(res, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
          const doc = one?.document ?? one;
          setTitle(doc?.title);
          setLoaded({ items: one?.provisions ?? [], from: doc?.valid_from ?? date, to: doc?.valid_to });
          if (!one?.provisions?.length)
            setUi({ gap: { status: one?.envelope?.status ?? "no_result", explanation: "No text is held for this law on that date.", available: [], } });
          else setUi(undefined);
        })
        .catch(() => live && setUi({ gap: { status: "error", explanation: "That version could not be loaded.", available: [] } }));
    }
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

  const openLaw = (work: string, date: string) => { setUi(undefined); go({ work, date, to: undefined, anchor: undefined, mode: "read" }); };
  const openDiff = (work: string, from: string, to: string) => { setUi(undefined); go({ work, date: from, to, mode: "compare" }); };

  const isTime = !s.work && (!!s.from || !!ui?.ranking || !!ui?.in_force);

  return (
    <div className="ws">
      <form className="cmd" onSubmit={(e) => { e.preventDefault(); submit(q); setQ(""); }}>
        <span className="brand">Lex</span>
        <input value={q} onChange={(e) => setQ(e.target.value)} disabled={busy}
               placeholder="Ask anything — or pick a law below" aria-label="Ask" />
        <button type="submit" disabled={busy}>{busy ? "…" : "Ask"}</button>
      </form>

      {said ? <div className="said"><b>what I found</b>{said}</div> : null}

      {s.work ? (
        <>
          <div className="sel">
            <span className="pick main"><i>law</i>{title ?? workSlug(s.work)}</span>
            <label className="pick"><i>{s.mode === "compare" ? "from" : "date"}</i>
              <input type="date" value={s.date ?? today()} onChange={(e) => go({ date: e.target.value })} />
            </label>
            {s.mode === "compare" ? (
              <label className="pick"><i>to</i>
                <input type="date" value={s.to ?? today()} onChange={(e) => go({ to: e.target.value })} />
              </label>
            ) : null}
            {s.anchor ? <span className="pick"><i>article</i>{s.anchor}</span> : null}
            <span className="grow" />
            <a className="pick" href={`/${publisherOf(s.work)}/${workSlug(s.work)}`}>permalink ↗</a>
          </div>

          <nav className="modes">
            {(["read", "history", "compare"] as const).map((m) => (
              <button key={m} className={s.mode === m ? "on" : ""}
                      disabled={m === "history" && !s.anchor}
                      title={m === "history" && !s.anchor ? "Pick an article first" : undefined}
                      onClick={() => go({ mode: m, ...(m === "compare" && !s.to ? { to: today() } : {}) })}>
                {m === "read" ? "Read" : m === "history" ? "History" : "Compare"}
              </button>
            ))}
          </nav>
        </>
      ) : null}

      <div className="work">
        {ui?.gap ? <Gap {...ui.gap} /> :
         ui?.ranking ? <Ranking rows={ui.ranking.rows} worksChanged={ui.ranking.works_changed}
                                newVersions={ui.ranking.new_versions} from={ui.ranking.from_date}
                                to={ui.ranking.to_date} onOpen={openDiff} /> :
         ui?.in_force ? <InForce date={ui.in_force.date} total={ui.in_force.total} rows={ui.in_force.rows} onOpen={openLaw} /> :
         ui?.history ? <HistoryRail states={ui.history.states} anchor={ui.history.anchor} work={ui.history.subject.work} /> :
         s.work && s.mode === "compare" ? <Compare work={s.work} from={s.date ?? today()} to={s.to ?? today()} anchor={s.anchor} /> :
         s.work && s.mode === "read" && loaded ? <Provision items={loaded.items} validFrom={loaded.from} validTo={loaded.to} work={s.work} /> :
         s.work ? <Empty>Loading…</Empty> :
         isTime ? <Empty>Pick a period, or ask a question.</Empty> :
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
