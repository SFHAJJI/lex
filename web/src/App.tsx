import { useCallback, useEffect, useRef, useState } from "react";
import { askStreaming, first, tool, type AskReply, type ProvisionItem, type Step, type UiEffect } from "./api";
import { publisherOf, useWorkspace, workSlug, type Space, type State } from "./state";
import { Compare, Empty, Gap, HistoryRail, InForce, Provision, Ranking, WorkTimeline, hasView, modeFor } from "./views";
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
  const [steps, setSteps] = useState<Step[]>([]);
  const [said, setSaid] = useState<string>();
  const [ui, setUi] = useState<UiEffect>();
  const [loaded, setLoaded] = useState<{ items: ProvisionItem[]; from: string; to?: string }>();
  const [title, setTitle] = useState<string>();
  const [versions, setVersions] = useState<string[]>([]);
  const abort = useRef<AbortController>();

  // The marketing below the fold belongs to a first-time visitor, not to someone reading a
  // law. One flag on <body> lets the server-rendered page get out of the way.
  useEffect(() => {
    const busyWith = s.work || s.q || s.from;
    document.body.dataset.workspace = busyWith ? "active" : "";
  }, [s.work, s.q, s.from]);

  // Every version of the loaded work: powers the version count and the previous/next stepper,
  // which is the most-wanted action in a point-in-time reader and did not exist.
  useEffect(() => {
    if (!s.work) { setVersions([]); return; }
    let live = true;
    tool<any>("timeline", { work: s.work, limit: 400 })
      .then((res) => {
        if (!live) return;
        const one = first<any>(res, (x) => Array.isArray(x?.versions) && x.versions.length > 0);
        const dates = [...new Set((one?.versions ?? []).map((v: any) => String(v.valid_from)))] as string[];
        setVersions(dates.sort());
      })
      .catch(() => live && setVersions([]));
    return () => { live = false; };
  }, [s.work]);

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
    setBusy(true); setSaid(undefined); setSteps([]);
    abort.current?.abort();
    abort.current = new AbortController();
    try {
      const r: AskReply = await askStreaming(
        text.trim(),
        (step) => setSteps((prev) => [...prev, step]),
        abort.current.signal);
      setSaid(r.error ?? r.reply);
      // A refusal keeps its steps out of the transcript: visible effort followed by a weak
      // answer measures WORSE than the same answer delivered instantly and quietly.
      if (r.narrated === false) setSteps([]);
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

  // Open on the text in force TODAY, never on the oldest version — the oldest is the one most
  // likely to have no stored text, so the old behaviour greeted every visitor with a refusal.
  const pickLaw = (h: { work: string; title: string }) => {
    setUi(undefined); setTitle(h.title); setVersions([]);
    go({ work: h.work, date: undefined, anchor: undefined, to: undefined, mode: "read", space: "law" });
  };

  // Where the current date sits among the versions, for the stepper.
  const step = (() => {
    if (versions.length === 0 || !loaded) return {} as { prev?: string; next?: string };
    const i = versions.indexOf(loaded.from);
    if (i < 0) return {};
    return { prev: i > 0 ? versions[i - 1] : undefined, next: i < versions.length - 1 ? versions[i + 1] : undefined };
  })();

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

      {steps.length > 0 ? (
        <ol className="steps" aria-live="polite" aria-label="What the assistant is finding">
          {steps.map((st, i) => (
            <li key={i} className={st.kind}>
              <span>{st.text}</span>
              {st.work ? (
                <button className="chipmini" onClick={() => {
                  setUi(undefined);
                  go({ work: st.work, date: st.date, anchor: st.anchor, mode: "read", space: "law" });
                }}>open →</button>
              ) : null}
            </li>
          ))}
          {busy ? <li className="pending"><span>working…</span></li> : null}
        </ol>
      ) : null}

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

      {space === "law" && s.work ? (
        <header className="lawhead">
          <div className="t">
            <h2>{title ?? workSlug(s.work)}</h2>
            <div className="meta">
              {loaded ? (
                <span className={`pill ${loaded.to ? "old" : "live"}`}>
                  {loaded.to ? "superseded" : "in force"}
                </span>
              ) : null}
              {loaded ? <span>{loaded.from} → {loaded.to ?? "today"}</span> : null}
              {versions.length > 1 ? <><span>·</span><span>{versions.length} versions</span></> : null}
            </div>
          </div>
          <LawPicker current="change law" onPick={pickLaw} />
        </header>
      ) : null}

      {space === "law" ? (
        <>
          <div className="sel">
            {!s.work ? <LawPicker current={undefined} onPick={pickLaw} /> : null}
            {s.work ? (
            <label className="pick"><i>{s.mode === "compare" ? "from" : "showing"}</i>
              <input type="date" value={s.date ?? today()} onChange={(e) => go({ date: e.target.value })} />
            </label>) : null}
            {s.work && step.prev ? <button className="pick" onClick={() => go({ date: step.prev })}>← previous version</button> : null}
            {s.work && step.next ? <button className="pick" onClick={() => go({ date: step.next })}>next version →</button> : null}
            {s.work && step.next ? <button className="pick" onClick={() => go({ date: versions[versions.length - 1] })}>jump to today</button> : null}
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
                      aria-pressed={s.mode === m}
                      onClick={() => go({
                        mode: m,
                        // Compare defaults to the neighbouring version, not to today: comparing
                        // a 2021 text against 2026 is never what someone means by "compare".
                        ...(m === "compare" && !s.to ? { to: step.next ?? versions[versions.length - 1] ?? today() } : {}),
                      })}>
                {m === "read" ? "Read" : m === "history" ? "History" : "Compare"}
                {m === "history" && versions.length > 1 ? <span className="cnt">{versions.length}</span> : null}
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
         s.work && s.mode === "history" && !s.anchor ?
           <WorkTimeline versions={versions} current={loaded?.from}
                         onPick={(d) => { setUi(undefined); go({ date: d, mode: "read" }); }} /> :
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
