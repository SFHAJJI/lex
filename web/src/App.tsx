import { useCallback, useEffect, useRef, useState } from "react";
import { askStreaming, first, tool, type AskReply, type ProvisionItem, type Step, type UiEffect } from "./api";
import { LAYERS, publisherOf, useWorkspace, workSlug, type Space, type State } from "./state";
import { CitedBy, Empty, Gap, InForce, Provision, Ranking, VersionRail, hasView, modeFor } from "./views";
import { Compare } from "./Compare";
import { LawPicker, shorten } from "./pickers";
import AskPanel from "./AskPanel";
import Search from "./Search";
import Period from "./Period";
import Coach, { COACH_KEY } from "./Coach";
import { CompareSkeleton, LawSkeleton, ReportSkeleton } from "./Skeleton";

const today = () => new Date().toISOString().slice(0, 10);

/** Language codes as a reader recognises them, for the switcher's tooltip. */
const NAMES: Record<string, string> = {
  fr: "French", de: "German", lb: "Luxembourgish", en: "English", nl: "Dutch", it: "Italian",
};

/** Rows per page in the period view. Enough to scan, small enough to arrive quickly. */
const PAGE = 25;

/** Follow-ups derived from the view on screen — always valid, and free. */
function chipsFor(s: State, ui?: UiEffect, hasText = true): { label: string; go: Partial<State> }[] {
  // Offering a window the reader is already looking at is noise, so the twelve-month chip only
  // appears when the twelve months are not already on screen.
  if (ui?.ranking) {
    const from = shift(today(), -365);
    return s.from === from && s.until === today() ? []
         : [{ label: "Try the last twelve months", go: { from, until: today(), mode: "read" } }];
  }
  if (s.mode === "compare") return [{ label: "Read the later version", go: { mode: "read", date: s.to, to: undefined } }];
  if (s.work && hasText) return [{ label: "Read the current text", go: { mode: "read", date: today(), to: undefined } }];
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
  const [loaded, setLoaded] = useState<{ items: ProvisionItem[]; from: string; to?: string; profile?: string; source?: string }>();
  const [toc, setToc] = useState<ProvisionItem[]>([]);
  const [title, setTitle] = useState<string>();
  const [versions, setVersions] = useState<string[]>([]);
  const [langs, setLangs] = useState<string[]>([]);
  // The language actually served, read back from the document rather than assumed. The switcher
  // first highlighted langs[0], which is alphabetical, so the Constitution showed French articles
  // under a chip reading DE: the index prefers the language a work is mostly published in, and
  // for this work that is French while "de" sorts first. A control that misreports the state it
  // controls is worse than no control.
  const [servedLang, setServedLang] = useState<string>();
  const [held, setHeld] = useState<{ text: number; total: number; official?: string }>();
  const [page, setPage] = useState(0);
  const [states, setStates] = useState<string[]>([]);
  const [coached, setCoached] = useState(() => {
    try { return localStorage.getItem(COACH_KEY) === "1"; } catch { return true; }
  });
  const abort = useRef<AbortController>();
  // Whether the article on screen was picked by the reader or opened for them. Not in the URL:
  // it changes nothing about what is displayed, only which timeline the rail belongs to.
  const chosenAnchor = useRef(false);

  // The server reserves the workspace's first-paint height so the explanatory content below it
  // does not jump when React arrives. Release that temporary reservation only after this tree is
  // committed. Without JavaScript the server's class-adding script never runs, so the plain
  // noscript path receives no artificial empty space.
  useEffect(() => { document.documentElement.classList.remove("workspace-loading"); }, []);

  // The marketing below the fold belongs to a first-time visitor, not to someone reading a
  // law. One flag on <body> lets the server-rendered page get out of the way.
  useEffect(() => {
    const busyWith = s.work || s.q || s.from;
    document.body.dataset.workspace = busyWith ? "active" : "";
  }, [s.work, s.q, s.from]);

  // Every version of the loaded work: powers the version count and the previous/next stepper,
  // which is the most-wanted action in a point-in-time reader and did not exist.
  //
  // It also answers a question that used to be asked one date at a time: does this work have text
  // AT ALL? Legilux publishes no text file for 1,768 of the 4,703 snapshots, and for some works
  // that means every single one. Code de l'environnement has 195 versions and text on none of
  // them, so a visitor could step the whole rail and be refused at every stop, with the interface
  // implying each refusal was about that date. Knowing the work-level answer up front lets the
  // reader say it once, and lets the chips stop offering text that does not exist.
  useEffect(() => {
    chosenAnchor.current = false;
    if (!s.work) { setVersions([]); setLangs([]); setServedLang(undefined); setHeld(undefined); return; }
    let live = true;
    tool<any>("timeline", { work: s.work, limit: 400 })
      .then((res) => {
        if (!live) return;
        const one = first<any>(res, (x) => Array.isArray(x?.versions) && x.versions.length > 0);
        const vs = (one?.versions ?? []) as any[];
        const dates = [...new Set(vs.map((v) => String(v.valid_from)))] as string[];
        setVersions(dates.sort());
        // Which languages this work exists in. The Constitution is published in French, German
        // and Luxembourgish, and its stored title is German for all three, so a reader looking
        // at the French text sees a German heading above it and reasonably concludes the page is
        // broken. Naming the language being read costs one chip and removes the contradiction.
        setLangs([...new Set(vs.map((v) => String(v.language)).filter(Boolean))].sort());
        setHeld({ text: vs.filter((v) => v.text_available).length, total: vs.length,
                  official: vs[vs.length - 1]?.source_uri });
      })
      .catch(() => { if (live) { setVersions([]); setLangs([]); setHeld(undefined); } });
    return () => { live = false; };
  }, [s.work]);

  // The outline belongs to (law, date) — never to the focused article. It used to be fetched
  // as part of the text, so opening an article replaced the contents with that one article
  // and re-dating dropped you at the top of a document you were reading the middle of.
  useEffect(() => {
    if (!s.work) { setToc([]); return; }
    let live = true;
    tool<any>("as_of", { work: s.work, date: s.date ?? today(), mode: "outline",
                         ...(s.language ? { language: s.language } : {}) })
      .then((res) => {
        if (!live) return;
        const one = first<any>(res, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
        setToc((one?.provisions ?? []) as ProvisionItem[]);
        // The law's name belongs to the law, not to the mode you are reading it in. It used to
        // be set only on the read path, so opening a comparison showed the raw work slug as
        // the heading — the one place a reader most needs to know which law they are looking at.
        const t = shorten((one?.document ?? one)?.title);
        if (t) setTitle(t);
        setServedLang((one?.document ?? one)?.language);
      })
      .catch(() => live && setToc([]));
    return () => { live = false; };
  }, [s.work, s.date, s.language]);

  // Deterministic loading: changing date, article or mode calls the public MCP endpoint
  // directly. No model in the loop — playing with the workspace must be instant and repeatable.
  useEffect(() => {
    if (!s.work || s.mode !== "read") return;
    let live = true;
    // Never show one law's text under another's heading: clear before fetching.
    setLoaded(undefined);
    const date = s.date ?? today();
    const lang = s.language ? { language: s.language } : {};
    // A code can carry thousands of articles: ask for the outline first and only pull full
    // text when it is small enough to read. Otherwise leave the reader in the contents —
    // dumping a whole code would freeze the tab and help nobody.
    //
    // The ceiling was 80 articles, set when the outline took the whole width and a scrolling
    // reader had nothing to navigate with. With the contents pinned beside the text, whole-act
    // reading is the better default: GDPR is 99 articles and 196 KB. The Consolidated CRR is
    // 500+ and 2.2 MB, which is what this threshold is actually for.
    const fetchRead = async () => {
      if (s.anchor)
        return tool<any>("as_of", { work: s.work, date, mode: "select", anchors: s.anchor, ...lang });
      const outline = await tool<any>("as_of", { work: s.work, date, mode: "outline", ...lang });
      const o = first<any>(outline, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
      const n = o?.provisions?.length ?? 0;
      if (n === 0 || n > 200) return outline;
      return tool<any>("as_of", { work: s.work, date, mode: "full", ...lang });
    };
    fetchRead()
      .then((res) => {
        if (!live) return;
        const one = first<any>(res, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
        const doc = one?.document ?? one;
        setTitle(shorten(doc?.title));
        const items = (one?.provisions ?? []) as ProvisionItem[];
        // Only claim a validity interval when a version actually resolved. `?? date` filled the
        // gap with the date that was ASKED for, so opening the Code penal at 1200-01-01 answered
        // "no version covers that date" in the body while the header above it said, in a green
        // pill, "in force 1200-01-01 → today". A refusal the chrome contradicts is worse than
        // either half alone: one of them is lying and the reader cannot tell which.
        setLoaded(doc?.valid_from
          ? { items, from: doc.valid_from, to: doc?.valid_to,
              profile: doc?.extraction_profile, source: doc?.source_uri }
          : undefined);
        if (items.length === 0)
          setUi({ gap: { status: one?.envelope?.status ?? "no_result", explanation: "No text is held for this law on that date.", available: [] } });
        else setUi(undefined);
      })
      .catch(() => live && setUi({ gap: { status: "error", explanation: "That version could not be loaded.", available: [] } }));
    return () => { live = false; };
  }, [s.work, s.date, s.mode, s.anchor, s.language]);

  // Time workspace: a period loads the ranking deterministically, so the follow-up chips
  // and any /?from=&until= link land on real content instead of an empty panel.
  useEffect(() => {
    if (s.work || !s.from || !s.until) return;
    let live = true;
    // The layer is now a filter the server applies, so the list is no longer ranked and then
    // thinned: every row that comes back belongs to the chosen layer. That also removes the old
    // failure where a longer window returned FEWER laws, because collections had eaten the top 25
    // before they were folded away.
    const layer = LAYERS.find((l) => l.id === (s.layer ?? "instruments")) ?? LAYERS[0];
    tool<any>("changes_in_period", {
      from_date: s.from, to_date: s.until, order: s.order ?? "by_churn",
      document_type: layer.types, limit: PAGE, offset: page * PAGE })
      .then((res) => {
        if (!live) return;
        // changes_in_period asks ACROSS the corpus, so its answer is the union of the
        // publishers, not the first one that happens to reply. Taking the first envelope with
        // rows reported 3 EU acts for the pandemic and silently dropped the hundreds of
        // Luxembourg ones behind it, because the EU index answers first.
        const envs = (Array.isArray(res) ? res : [res]) as any[];
        const rows = envs.flatMap((e) => e?.changes ?? []);
        const by = s.order ?? "by_churn";
        rows.sort((a: any, b: any) => by === "by_churn"
          ? (b.versions_in_period ?? 0) - (a.versions_in_period ?? 0)
          : String(b.last_change ?? "").localeCompare(String(a.last_change ?? "")));
        setUi(rows.length
          ? { ranking: { from_date: s.from!, to_date: s.until!, order: by,
                         works_changed: envs.reduce((n, e) => n + (e?.works_changed ?? 0), 0),
                         new_versions: envs.reduce((n, e) => n + (e?.new_versions ?? 0), 0),
                         rows } }
          : { gap: { status: "no_changes_in_period", explanation: "Nothing changed in that window.", available: [] } });
      })
      .catch(() => {});
    return () => { live = false; };
  }, [s.work, s.from, s.until, s.order, s.layer, page]);

  // "What was in force on this day": the compliance question, answered deterministically like
  // every other control in the workspace rather than only through the assistant.
  useEffect(() => {
    // A date with no question is itself a question: what applied that day. Not a mode, just what
    // an empty search means once a date is set.
    if (s.work || s.q || !s.asOf || s.space === "time") return;
    let live = true;
    tool<any>("in_force_on", { date: s.asOf, limit: 60 })
      .then((res) => {
        if (!live) return;
        const envs = (Array.isArray(res) ? res : [res]) as any[];
        // in_force_on returns `works` with a `total_works_in_force` count, and its rows carry
        // work/title/document_type/valid_from. Mapped here to the shape the view already speaks.
        const rows = envs.flatMap((e) => (e?.works ?? []).map((w: any) => ({
          work: w.work, title: w.title, kind: w.document_type,
          valid_from: w.valid_from, permalink: w.permalink,
        })));
        setUi(rows.length
          ? { in_force: { date: s.asOf!, total: envs.reduce((n, e) => n + (e?.total_works_in_force ?? 0), 0), rows } }
          : { gap: { status: "no_result", explanation: `Nothing is recorded as in force on ${s.asOf}.`, available: [] } });
      })
      .catch(() => {});
    return () => { live = false; };
  }, [s.space, s.asOf, s.work, s.q]);

  // With an article open the rail narrows to THAT article's distinct texts — the question a
  // reader actually has ("when did this paragraph change?") rather than "when was anything
  // in this law touched?". Falls back to the law's versions when no per-article history exists.
  useEffect(() => {
    if (!s.work || !s.anchor) { setStates([]); return; }
    let live = true;
    tool<any>("article_history", { work: s.work, anchor: s.anchor })
      .then((res) => {
        if (!live) return;
        const one = first<any>(res, (x) => Array.isArray(x?.states) && x.states.length > 0);
        setStates(((one?.states ?? []) as { valid_from: string }[]).map((x) => x.valid_from).sort());
      })
      .catch(() => live && setStates([]));
    return () => { live = false; };
  }, [s.work, s.anchor]);

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
      // Controls the assistant set on the way to its answer. Applied before the view, so the
      // layer tabs and the page already read correctly when the rows land under them.
      if (r.ui?.workspace) {
        const w = r.ui.workspace;
        if (typeof w.page === "number") setPage(Math.max(0, w.page));
        if (w.layer) go({ layer: w.layer as State["layer"] });
      }
      if (hasView(r.ui)) {
        setUi(r.ui);
        const subj = r.ui!.provision?.subject ?? r.ui!.history?.subject ?? r.ui!.diff?.subject;
        const m = modeFor(r.ui);
        // The space is set explicitly, never inferred. The reader is somewhere when they ask, and
        // "somewhere" is now a pinned value: answering a "what changed" question from the search
        // page used to print the prose and leave the table behind the space it belonged to.
        if (subj?.work) {
          setTitle(subj.title);
          if (r.ui!.provision) setLoaded({ items: r.ui!.provision.provisions, from: r.ui!.provision.valid_from, to: r.ui!.provision.valid_to });
          go({ work: subj.work, date: subj.date ?? r.ui!.provision?.valid_from, anchor: subj.anchor, mode: m ?? "read",
               space: "law",
               ...(r.ui!.diff ? { date: r.ui!.diff.from_date, to: r.ui!.diff.to_date } : {}) });
        } else if (r.ui!.ranking) {
          go({ work: undefined, from: r.ui!.ranking.from_date, until: r.ui!.ranking.to_date,
               order: r.ui!.ranking.order as State["order"], mode: "read", space: "time" });
        }
      }
    } catch { setSaid("The request failed, try again."); }
    finally { setBusy(false); }
  }, [busy, go]);

  // Open on the text in force TODAY, never on the oldest version — the oldest is the one most
  // likely to have no stored text, so the old behaviour greeted every visitor with a refusal.
  const pickLaw = (h: { work: string; title: string }) => {
    setUi(undefined); setTitle(h.title); setVersions([]);
    go({ work: h.work, date: undefined, anchor: undefined, to: undefined, mode: "read", space: "law" });
  };

  // What the rail is a rail OF: this article's texts when one is open, else the law's versions.
  // The rail is a rail OF the law, unless the reader deliberately narrowed it to one article.
  //
  // A code too large to render whole opens on its first article, so that arriving at the Code du
  // travail means arriving at some law rather than at an apology. That article was then treated
  // as a choice: the rail retargeted to its history, and a law with 61 versions announced "6
  // texts of this article". The single idea the product exists to show, that a law is a series of
  // dated versions, was being hidden by the convenience that lands you in it.
  const narrowed = !!s.anchor && chosenAnchor.current && states.length > 0;
  const railDates = narrowed ? states : versions;
  const railScope = narrowed ? "texts of this article" : "versions";
  const at = loaded?.from && railDates.includes(loaded.from) ? loaded.from
           : railDates.filter((d) => d <= (s.date ?? today())).pop();

  const openLaw = (work: string, date: string) => { setUi(undefined); go({ work, date, to: undefined, anchor: undefined, mode: "read", space: "law" }); };
  const openDiff = (work: string, from: string, to: string) => { setUi(undefined); go({ work, date: from, to, mode: "compare", space: "law" }); };

  // Which framework is on screen: whatever the URL says, else inferred from what is loaded.
  const space: Space = s.space ?? (s.work ? "law" : (s.from || ui?.ranking) ? "time" : "search");

  const switchTo = (sp: Space) => {
    setUi(undefined);
    setSaid(undefined);
    if (sp === "time") go({ space: sp, work: undefined, anchor: undefined, from: s.from ?? shift(today(), -365), until: s.until ?? today(), order: s.order ?? "by_churn" });
    else go({ space: sp, work: undefined, anchor: undefined, from: undefined, until: undefined });
  };

  // Home is the search surface, and it stays until a law is open OR the reader asks for the
  // report. The old flag was "nothing chosen yet", which made the report render underneath a
  // search box that had nothing to do with it.
  const front = !s.work && space === "search";

  return (
    <div className="ws">
      {front ? (
        <Search
          state={s} today={today()}
          onQuery={(q) => { setUi(undefined); go({ q: q || undefined, work: undefined, from: undefined, until: undefined, space: "search" }); }}
          onAsOf={(d) => { setUi(undefined); go({ asOf: d }); }}
          onOpen={(work, date, anchor) => { setUi(undefined); go({ work, date, anchor, mode: "read", space: "law" }); }}
          onMonitor={() => switchTo("time")}
        />
      ) : (
        <nav className="doors">
          <button className="backhome" onClick={() => { setUi(undefined); setSaid(undefined); go({ work: undefined, q: undefined, from: undefined, until: undefined, anchor: undefined, to: undefined, space: undefined }); }}>
            ← everything
          </button>
          <button className={space === "search" ? "on" : ""} onClick={() => switchTo("search")}>search</button>
          <button className={space === "time" ? "on" : ""} onClick={() => switchTo("time")}>what changed</button>
        </nav>
      )}

      {space === "time" && !s.work ? (
        <Period from={s.from ?? shift(today(), -365)} until={s.until ?? today()}
                order={s.order ?? "by_churn"} layer={s.layer ?? "instruments"} today={today()}
                onWindow={(from, until) => { setPage(0); setUi(undefined); go({ from, until }); }}
                onOrder={(o) => { setPage(0); setUi(undefined); go({ order: o }); }}
                onLayer={(l) => { setPage(0); setUi(undefined); go({ layer: l }); }} />
      ) : null}

      {coached ? null : (
        <Coach state={s} onDone={() => {
          try { localStorage.setItem(COACH_KEY, "1"); } catch { /* private mode, teach again */ }
          setCoached(true);
        }} />
      )}

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
              {/* Which language you are reading. It only appears when the work exists in more
                  than one, which is rare and always worth saying: the Constitution is published
                  in French, German and Luxembourgish, and one stored title serves all three, so
                  a German heading can sit above French articles and read as a broken page. */}
              {langs.length > 1 ? (
                <span className="langs" role="group" aria-label="Language of this text">
                  {langs.map((l) => (
                    <button key={l} className={l === (s.language ?? servedLang) ? "on" : ""}
                            aria-pressed={l === (s.language ?? servedLang)}
                            title={`Read this law in ${NAMES[l] ?? l}`}
                            onClick={() => { setUi(undefined); go({ language: l }); }}>{l}</button>
                  ))}
                </span>
              ) : null}
              <span className="grow" />
              <label className="pick"><i>{s.mode === "compare" ? "from" : "showing"}</i>
                <input type="date" value={s.date ?? today()} aria-label="Date to show the law as it stood"
                       onChange={(e) => go({ date: e.target.value })} />
              </label>
              <LawPicker current="change law" onPick={pickLaw} />
              <a className="pick" href={`/${publisherOf(s.work)}/${workSlug(s.work)}`}>permalink ↗</a>
            </div>
          </div>
        </header>
      ) : null}

      {space === "law" && s.work ? (
        <VersionRail dates={railDates} current={at} compareTo={s.mode === "compare" ? s.to : undefined}
                     scope={railScope} today={today()}
                     onPick={(d) => { setUi(undefined); go({ date: d, to: undefined, mode: "read" }); }}
                     onCompare={(d) => {
                       // Shift-click makes the pair, so comparing never means retyping a date
                       // that is already on screen. Order the pair; a diff runs forwards.
                       const from = at && at < d ? at : d;
                       const to = at && at < d ? d : at ?? d;
                       if (from === to) return;
                       setUi(undefined); go({ date: from, to, mode: "compare" });
                     }}
                     onClear={() => { setUi(undefined); go({ to: undefined, mode: "read" }); }} />
      ) : null}

      <div className="work">
        {ui?.gap ? <Gap {...ui.gap} held={s.work ? held : undefined} /> :
         ui?.ranking ? <Ranking rows={ui.ranking.rows} worksChanged={ui.ranking.works_changed}
                                newVersions={ui.ranking.new_versions} from={ui.ranking.from_date}
                                to={ui.ranking.to_date} onOpen={openDiff} onOpenRecord={openLaw}
                                layer={s.layer ?? "instruments"} page={page}
                                hasMore={ui.ranking.rows.length >= PAGE}
                                onPage={(p) => { setPage(Math.max(0, p)); setUi(undefined); }} /> :
         ui?.cited_by ? <CitedBy view={ui.cited_by}
                                 onOpen={(w, d, a) => { setUi(undefined); go({ work: w, date: d, anchor: a, mode: "read", space: "law" }); }} /> :
         ui?.in_force ? <InForce date={ui.in_force.date} total={ui.in_force.total} rows={ui.in_force.rows} onOpen={openLaw} /> :
         s.work && s.mode === "compare" ? <Compare work={s.work} from={s.date ?? today()} to={s.to ?? today()} anchor={s.anchor} /> :
         s.work && loaded ? <Provision items={loaded.items} toc={toc} validFrom={loaded.from} validTo={loaded.to}
                                       work={s.work} anchor={s.anchor} profile={loaded.profile}
                                       source={loaded.source}
                                       onCite={(w) => { setUi(undefined); go({ work: w, date: undefined, anchor: undefined, to: undefined, mode: "read", space: "law" }); }}
                                       onPick={(a, auto) => { chosenAnchor.current = !auto; go({ anchor: a }); }}
                                       onClear={() => go({ anchor: undefined })} /> :
         // The shape of what is coming, not the word for waiting: a code takes a moment to
         // arrive and an empty screen saying "Loading" answers none of the questions a reader
         // has while it does.
         s.work && s.mode === "compare" ? <CompareSkeleton /> :
         s.work ? <LawSkeleton /> :
         !front && space === "time" && (s.from || s.until) ? <ReportSkeleton /> :
         !front && space === "time" ? <Empty>Pick a period above.</Empty> :
         null}
      </div>

      {(s.work || ui) ? (
        <div className="chips">
          {chipsFor(s, ui, (held?.text ?? 1) > 0).map((c) => (
            <button key={c.label} className="chip" onClick={() => { setUi(undefined); go(c.go); }}>{c.label}</button>
          ))}
        </div>
      ) : null}

      <AskPanel q={q} setQ={setQ} busy={busy} steps={steps} said={said} onSubmit={submit}
                followUps={chipsFor(s, ui, (held?.text ?? 1) > 0).map((c) => ({
                  label: c.label, run: () => { setUi(undefined); go(c.go); } }))}
                onOpenStep={(st) => { setUi(undefined); go({ work: st.work, date: st.date, anchor: st.anchor, mode: "read", space: "law" }); }} />
    </div>
  );
}

/**
 * Two examples, set as a sentence rather than as buttons. A row of four chips reads as four
 * more decisions stacked under the one decision that matters; a line of prose reads as help.
 */
