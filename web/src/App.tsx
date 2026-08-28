import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { compoundOperationViews, first, tool, type AskReply, type OperationReply,
  type ProvisionItem, type UiEffect } from "./api";
import { publisherOf, useWorkspace, workSlug, type Space, type State } from "./state";
import { CitedBy, CoveragePanel, Empty, EvidenceCoordinates, Gap, InForce, Provision, PublisherLimitations, Ranking, Timeline,
  VerificationPanel, VersionRail, hasView } from "./views";
import { limitationsFromEffect } from "./limitations";
import { Compare } from "./Compare";
import { LawPicker, shorten } from "./pickers";
import AssistantController from "./AssistantController";
import { assistantProvisionLoad, assistantTimelineSeed, assistantWorkspaceState } from "./assistantShell";
import Search from "./Search";
import Period from "./Period";
import Coach, { COACH_KEY } from "./Coach";
import { CompareSkeleton, LawSkeleton, ReportSkeleton } from "./Skeleton";
import { jurisdictionForPublisher, jurisdictionLabel } from "./facets";
import { latestStateLabel, temporalStatusLabel } from "./temporal";
import { everyPublisherRefused, LIMITATION_EXPLANATION, limitationsFromEnvelopes } from "./limitations";

const today = () => new Date().toISOString().slice(0, 10);

/** Language codes as a reader recognises them, for the switcher's tooltip. */
const NAMES: Record<string, string> = {
  fr: "French", de: "German", lb: "Luxembourgish", en: "English", nl: "Dutch", it: "Italian",
};

/** Rows per page in the period view. Enough to scan, small enough to arrive quickly. */
const PAGE = 25;
const PRESENTATION_FRAME_ATTEMPTS = 8;
const presentationMark = (operationId: string) =>
  `lex-operation-result-received:${operationId}`;
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
  const [ui, setUi] = useState<UiEffect>();
  const [operationViews, setOperationViews] = useState<OperationReply[]>([]);
  const [assistantPresentationId, setAssistantPresentationId] = useState<string>();
  const pendingPresentations = useRef(new Set<string>());
  const measuredPresentations = useRef(new Set<string>());
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
  const [timelineSemantics, setTimelineSemantics] = useState<string>();
  const [held, setHeld] = useState<{ text: number; total: number; official?: string; kind?: string }>();
  const [page, setPage] = useState(0);
  const [states, setStates] = useState<string[]>([]);
  const [coached, setCoached] = useState(() => {
    try { return localStorage.getItem(COACH_KEY) === "1"; } catch { return true; }
  });
  // Whether the article on screen was picked by the reader or opened for them. Not in the URL:
  // it changes nothing about what is displayed, only which timeline the rail belongs to.
  const chosenAnchor = useRef(false);

  // The server reserves the workspace's first-paint height so the explanatory content below it
  // does not jump when React arrives. Release that temporary reservation only after this tree is
  // committed. Without JavaScript the server's class-adding script never runs, so the plain
  // noscript path receives no artificial empty space.
  useEffect(() => { document.documentElement.classList.remove("workspace-loading"); }, []);

  // This measurement belongs to the browser, not the HTTP evaluator. It fires only after React
  // committed a non-empty typed result and the next animation frame made its box paint-ready.
  useLayoutEffect(() => {
    if (!assistantPresentationId || typeof performance === "undefined") return;
    const mark = presentationMark(assistantPresentationId);
    let attempt = 0;
    let frame = 0;
    let cancelled = false;
    const inspect = () => {
      if (cancelled) return;
      const result = [...document.querySelectorAll<HTMLElement>(
        "[data-lex-operation-result-id]")].find((element) =>
          element.dataset.lexOperationResultId === assistantPresentationId);
      const received = performance.getEntriesByName(mark).at(-1);
      if (!result || !received || result.getBoundingClientRect().height <= 0) {
        attempt += 1;
        if (attempt < PRESENTATION_FRAME_ATTEMPTS) frame = requestAnimationFrame(inspect);
        else {
          pendingPresentations.current.delete(assistantPresentationId);
          performance.clearMarks(mark);
          setAssistantPresentationId((current) =>
            current === assistantPresentationId ? undefined : current);
        }
        return;
      }
      const duration = Math.max(0, performance.now() - received.startTime);
      performance.measure("lex-operation-result-received-to-presented", {
        start: received.startTime,
        duration,
      });
      window.dispatchEvent(new CustomEvent("lex:operation-result-presented", {
        detail: { duration_ms: duration, operation_id: assistantPresentationId },
      }));
      pendingPresentations.current.delete(assistantPresentationId);
      measuredPresentations.current.add(assistantPresentationId);
      performance.clearMarks(mark);
    };
    frame = requestAnimationFrame(inspect);
    return () => {
      cancelled = true;
      cancelAnimationFrame(frame);
      pendingPresentations.current.delete(assistantPresentationId);
      performance.clearMarks(mark);
    };
  }, [assistantPresentationId]);

  // The marketing below the fold belongs to a first-time visitor, not to someone reading a
  // law. One flag on <body> lets the server-rendered page get out of the way.
  useEffect(() => {
    const busyWith = s.work || s.q || s.from || s.asOf;
    document.body.dataset.workspace = busyWith ? "active" : "";
  }, [s.work, s.q, s.from, s.asOf]);

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
    if (!s.work) { setVersions([]); setLangs([]); setServedLang(undefined); setTimelineSemantics(undefined); setHeld(undefined); return; }
    // Never carry one publisher's time semantics across a work switch while the next timeline
    // is loading. The work-id fallback remains correct for currently mounted legacy artifacts.
    setTimelineSemantics(undefined);
    let live = true;
    tool<any>("timeline", { work: s.work, limit: 400 })
      .then((res) => {
        if (!live) return;
        const one = first<any>(res, (x) => Array.isArray(x?.versions) && x.versions.length > 0);
        const vs = (one?.versions ?? []) as any[];
        setTimelineSemantics(one?.envelope?.timeline_semantics);
        const dates = [...new Set(vs.map((v) => String(v.valid_from)))] as string[];
        setVersions(dates.sort());
        // Which languages this work exists in. The Constitution is published in French, German
        // and Luxembourgish, and its stored title is German for all three, so a reader looking
        // at the French text sees a German heading above it and reasonably concludes the page is
        // broken. Naming the language being read costs one chip and removes the contradiction.
        setLangs([...new Set(vs.map((v) => String(v.language)).filter(Boolean))].sort());
        setHeld({ text: vs.filter((v) => v.text_available).length, total: vs.length,
                  official: vs[vs.length - 1]?.source_uri,
                  kind: vs[vs.length - 1]?.document_type });
      })
      .catch(() => { if (live) { setVersions([]); setLangs([]); setTimelineSemantics(undefined); setHeld(undefined); } });
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
        if (items.length === 0 && doc?.text_omitted) items.push({
          anchor: "", heading: doc?.title, text: "", text_omitted: true,
          text_omitted_reason: doc?.text_omitted_reason,
          permalink: doc?.permalink ?? doc?.source_uri,
        });
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
    tool<any>("changes_in_period", {
      from_date: s.from, to_date: s.until, order: s.order ?? "by_churn",
      source_class: s.sourceClass ?? "!RECUEIL,!CODE_RECUEIL",
      ...(s.jurisdiction ? { jurisdiction: s.jurisdiction } : {}),
      ...(s.hierarchy ? { hierarchy: s.hierarchy } : {}),
      ...(s.domain ? { domain: s.domain } : {}),
      ...(s.actForm ? { act_form: s.actForm } : {}),
      ...(s.bindingStatus ? { binding_status: s.bindingStatus } : {}),
      ...(s.language ? { language: s.language } : {}),
      // Each publisher ranks independently. Asking each for the prefix needed by this page,
      // then sorting and slicing once below, produces one honest cross-publisher pagination.
      limit: (page + 1) * PAGE, offset: 0 })
      .then((res) => {
        if (!live) return;
        // changes_in_period asks ACROSS the corpus, so its answer is the union of the
        // publishers, not the first one that happens to reply. Taking the first envelope with
        // rows reported 3 EU acts for the pandemic and silently dropped the hundreds of
        // Luxembourg ones behind it, because the EU index answers first.
        const envs = (Array.isArray(res) ? res : [res]) as any[];
        const periodLimitations = limitationsFromEnvelopes("changes_in_period", envs);
        const rows = envs.flatMap((e) => (e?.changes ?? []).map((row: any) => ({
          ...row,
          jurisdiction: e?.envelope?.jurisdiction,
          timeline_semantics: e?.envelope?.timeline_semantics,
        })));
        const by = s.order ?? "by_churn";
        rows.sort((a: any, b: any) => by === "by_churn"
          ? (b.versions_in_period ?? 0) - (a.versions_in_period ?? 0)
          : String(b.last_change ?? "").localeCompare(String(a.last_change ?? "")));
        const visibleRows = rows.slice(page * PAGE, (page + 1) * PAGE);
        // Supported rows and typed refusals coexist: rows render, the limitation renders
        // beside them, and only an all-refused call keeps the full typed gap.
        setUi(visibleRows.length
          ? { ranking: { from_date: s.from!, to_date: s.until!, order: by,
                         works_changed: envs.reduce((n, e) => n + (e?.works_changed ?? 0), 0),
                         new_versions: envs.reduce((n, e) => n + (e?.new_versions ?? 0), 0),
                         population_works: envs.reduce((n, e) => n + (e?.population?.works_in_scope ?? 0), 0),
                         population_basis: "sum of the selected publisher scopes",
                         known_exclusions: [...new Set(envs.map(e => e?.population?.known_exclusions)
                           .filter(Boolean))] as string[],
                         rows: visibleRows },
              publisher_limitations: periodLimitations }
          : everyPublisherRefused(envs)
          ? { gap: { status: "filter_not_supported_by_index",
                     explanation: LIMITATION_EXPLANATION, available: [] },
              publisher_limitations: periodLimitations }
          : { gap: { status: "no_changes_in_period", explanation: "Nothing changed in that window.", available: [] },
              publisher_limitations: periodLimitations });
      })
      .catch(() => { if (live) setUi({ gap: { status: "error", explanation: "The change report could not be loaded. Try again.", available: [] } }); });
    return () => { live = false; };
  }, [s.work, s.from, s.until, s.order, s.jurisdiction, s.hierarchy, s.domain,
      s.sourceClass, s.actForm, s.bindingStatus, s.language, page]);

  // "What was in force on this day": the compliance question, answered deterministically like
  // every other control in the workspace rather than only through the assistant.
  useEffect(() => {
    // A date with no question is itself a question: what applied that day. Not a mode, just what
    // an empty search means once a date is set.
    if (s.work || s.q || !s.asOf || s.space === "time") return;
    let live = true;
    tool<any>("in_force_on", {
      date: s.asOf, limit: (page + 1) * PAGE, offset: 0,
      ...(s.jurisdiction ? { jurisdiction: s.jurisdiction } : {}),
      ...(s.hierarchy ? { hierarchy: s.hierarchy } : {}),
      ...(s.domain ? { domain: s.domain } : {}),
      ...(s.sourceClass ? { source_class: s.sourceClass } : {}),
      ...(s.actForm ? { act_form: s.actForm } : {}),
      ...(s.bindingStatus ? { binding_status: s.bindingStatus } : {}),
      ...(s.language ? { language: s.language } : {}),
    })
      .then((res) => {
        if (!live) return;
        const envs = (Array.isArray(res) ? res : [res]) as any[];
        const inForceLimitations = limitationsFromEnvelopes("in_force_on", envs);
        // in_force_on returns `works` with a `total_works_in_force` count, and its rows carry
        // work/title/document_type/valid_from. Mapped here to the shape the view already speaks.
        const rows = envs.flatMap((e) => (e?.works ?? []).map((w: any) => ({
          work: w.lex_id
            ? String(w.lex_id).split(":").slice(0, 2).join(":")
            : `${e?.envelope?.publisher}:${w.work}`,
          title: w.title, kind: w.document_type,
          valid_from: w.valid_from, permalink: w.permalink,
          jurisdiction: e?.envelope?.jurisdiction,
          hierarchy: w.hierarchy,
          timeline_semantics: e?.envelope?.timeline_semantics,
        })));
        rows.sort((a: any, b: any) => String(a.title ?? a.work).localeCompare(String(b.title ?? b.work)));
        const visibleRows = rows.slice(page * PAGE, (page + 1) * PAGE);
        setUi(visibleRows.length
          ? { in_force: { date: s.asOf!, total: envs.reduce((n, e) => n + (e?.total_works_in_force ?? 0), 0), rows: visibleRows },
              publisher_limitations: inForceLimitations }
          : everyPublisherRefused(envs)
          ? { gap: { status: "filter_not_supported_by_index",
                     explanation: LIMITATION_EXPLANATION, available: [] },
              publisher_limitations: inForceLimitations }
          : { gap: { status: "no_result", explanation: `No publisher state covers ${s.asOf} in this scope.`, available: [] },
              publisher_limitations: inForceLimitations });
      })
      .catch(() => { if (live) setUi({ gap: { status: "error", explanation: "The in-force list could not be loaded. Try again.", available: [] } }); });
    return () => { live = false; };
  }, [s.space, s.asOf, s.work, s.q, s.jurisdiction, s.hierarchy, s.domain,
      s.sourceClass, s.actForm, s.bindingStatus, s.language, page]);

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

  const applyAssistantReply = useCallback((r: AskReply) => {
      setOperationViews(compoundOperationViews(r));
      const presentation = r.operations?.find((operation) => hasView(operation.ui));
      if (presentation
          && typeof performance !== "undefined"
          && !pendingPresentations.current.has(presentation.operation_id)
          && !measuredPresentations.current.has(presentation.operation_id)) {
        const mark = presentationMark(presentation.operation_id);
        performance.clearMarks(mark);
        performance.mark(mark);
        pendingPresentations.current.add(presentation.operation_id);
        setAssistantPresentationId(presentation.operation_id);
      }
      // Controls the assistant set on the way to its answer. Applied before the view, so
      // jurisdiction and legal metadata already agree with the rows that land under them.
      let refinement: Partial<State> = {};
      if (r.ui?.workspace) {
        const w = r.ui.workspace;
        if (typeof w.page === "number") setPage(Math.max(0, w.page));
        refinement = {
          ...(w.jurisdiction ? { jurisdiction: w.jurisdiction } : {}),
          ...(w.hierarchy ? { hierarchy: w.hierarchy } : {}),
          ...(w.domain ? { domain: w.domain } : {}),
          ...(w.source_class ? { sourceClass: w.source_class } : {}),
          ...(w.act_form ? { actForm: w.act_form } : {}),
          ...(w.binding_status ? { bindingStatus: w.binding_status } : {}),
          ...(w.language ? { language: w.language } : {}),
        };
      }
      let navigated = false;
      if (hasView(r.ui)) {
        setUi(r.ui);
        const timeline = assistantTimelineSeed(r.ui);
        if (timeline) {
          setVersions(timeline.versions);
          setLangs(timeline.languages);
        }
        // The rendered view owns navigation. A comparison turn may also read each side via
        // as_of for grounded prose; those supporting provision effects must not steal the
        // diff's verified article anchor or change its destination.
        const subj = r.ui!.diff?.subject ?? r.ui!.provision?.subject
          ?? r.ui!.history?.subject ?? r.ui!.timeline?.subject;
        // The space is set explicitly, never inferred. The reader is somewhere when they ask, and
        // "somewhere" is now a pinned value: answering a "what changed" question from the search
        // page used to print the prose and leave the table behind the space it belonged to.
        if (subj?.work) {
          setTitle(subj.title);
          // Navigation and cached publisher text are one state transition. An outline,
          // truncated response, timeline or diff must never inherit the previous law's body.
          setLoaded(assistantProvisionLoad(r.ui));
          go(assistantWorkspaceState(r.ui)!);
          navigated = true;
        } else if (r.ui!.ranking || r.ui!.in_force) {
          setPage(r.ui!.workspace?.page ?? 0);
          go(assistantWorkspaceState(r.ui)!);
          navigated = true;
        }
      }
      if (!navigated) {
        const destination = assistantWorkspaceState(r.ui);
        if (destination) go(destination);
        else if (Object.keys(refinement).length > 0) go(refinement);
      }
  }, [go]);

  const clearAssistantView = useCallback(() => {
    setUi(undefined);
    setOperationViews([]);
    pendingPresentations.current.clear();
    setAssistantPresentationId(undefined);
  }, []);

  // Open on the text in force TODAY, never on the oldest version — the oldest is the one most
  // likely to have no stored text, so the old behaviour greeted every visitor with a refusal.
  const pickLaw = (h: { work: string; title: string }) => {
    clearAssistantView(); setTitle(h.title); setVersions([]);
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

  const openLaw = (work: string, date: string) => { clearAssistantView(); go({ work, date, to: undefined, anchor: undefined, mode: "read", space: "law" }); };
  const openDiff = (work: string, from: string, to: string) => { clearAssistantView(); go({ work, date: from, to, mode: "compare", space: "law" }); };

  // Which framework is on screen: whatever the URL says, else inferred from what is loaded.
  const space: Space = s.space ?? (s.work ? "law" : (s.from || ui?.ranking) ? "time" : "search");

  const switchTo = (sp: Space) => {
    clearAssistantView();
    setPage(0);
    if (sp === "time") go({ space: sp, work: undefined, anchor: undefined, from: s.from ?? shift(today(), -365), until: s.until ?? today(), order: s.order ?? "by_churn" });
    else go({ space: sp, work: undefined, anchor: undefined, from: undefined, until: undefined });
  };

  // Home is the search surface, and it stays until a law is open OR the reader asks for the
  // report. The old flag was "nothing chosen yet", which made the report render underneath a
  // search box that had nothing to do with it.
  const front = !s.work && space === "search";

  const renderOperation = (operation: OperationReply) => {
    const view = operation.ui!;
    if (view.gap) return <Gap {...view.gap} held={s.work ? held : undefined} />;
    if (view.coverage) return <CoveragePanel view={view.coverage} />;
    if (view.verification) return <VerificationPanel view={view.verification} />;
    if (view.ranking) return <Ranking rows={view.ranking.rows}
      worksChanged={view.ranking.works_changed} newVersions={view.ranking.new_versions}
      populationWorks={view.ranking.population_works}
      knownExclusions={view.ranking.known_exclusions} jurisdiction={s.jurisdiction}
      from={view.ranking.from_date} to={view.ranking.to_date} onOpen={openDiff}
      onOpenRecord={openLaw} page={0} hasMore={false} onPage={() => {}} />;
    if (view.cited_by) return <CitedBy view={view.cited_by}
      onOpen={(work, date, anchor) => {
        clearAssistantView();
        go({ work, date, anchor, mode: "read", space: "law" });
      }} />;
    if (view.in_force) return <InForce date={view.in_force.date} total={view.in_force.total}
      rows={view.in_force.rows} page={0} hasMore={false} onPage={() => {}} onOpen={openLaw} />;
    if (view.timeline) return <Timeline view={view.timeline}
      onOpen={(date) => {
        clearAssistantView();
        go({ work: view.timeline!.subject.work, date, mode: "read", space: "law" });
      }} />;
    if (view.diff) return <section className="evidence-panel" aria-label="Comparison result">
      {view.diff.subject.anchor ? <div className="cnt">
        <span className="tag mono">{view.diff.subject.anchor}</span>
        {view.diff.provision_level_comparable && view.diff.anchor_text_equal === true
          ? <span className="tag">same wording</span>
          : view.diff.anchor_from_present === false ? <span className="tag">added</span>
          : view.diff.anchor_to_present === false ? <span className="tag">removed</span>
          : view.diff.provision_level_comparable && view.diff.anchor_text_equal === false
            ? <span className="tag">wording changed</span>
          : null}
      </div> : null}
      {view.diff.note ? <p>{view.diff.note}</p> : null}
      <button className="operation-open" onClick={() => openDiff(
        view.diff!.subject.work, view.diff!.from_date, view.diff!.to_date)}>
        Open comparison
      </button>
    </section>;
    const subject = view.provision?.subject ?? view.history?.subject;
    if (subject?.work) {
      const from = view.provision?.valid_from ?? subject.date ?? today();
      return <button className="operation-open" onClick={() => openLaw(subject.work, from)}>
        Open {view.history ? "article history" : view.provision?.outline_only
            ? "table of contents" : "publisher text"}
      </button>;
    }
    if (view.workspace) return <p className="sub">The matching search workspace is open.</p>;
    return null;
  };

  return (
    <div className="ws">
      {front ? (
        <Search
          state={s} today={today()}
          onSubmit={({ query, asOf }) => { setPage(0); clearAssistantView(); go({
            q: query || undefined,
            ...(asOf ? { asOf } : {}),
            work: undefined, from: undefined, until: undefined, space: "search",
          }); }}
          onAsOf={(d) => { setPage(0); clearAssistantView(); go({ asOf: d }); }}
          onRefine={(next) => { setPage(0); clearAssistantView(); go(next); }}
          onOpen={(work, date, anchor) => { clearAssistantView(); go({ work, date, anchor, mode: "read", space: "law" }); }}
          onMonitor={() => switchTo("time")}
        />
      ) : (
        <nav className="doors">
          <button className="backhome" onClick={() => { setPage(0); clearAssistantView(); go({
            work: undefined, q: undefined, asOf: undefined, from: undefined, until: undefined,
            anchor: undefined, to: undefined, space: undefined, jurisdiction: undefined,
            hierarchy: undefined, domain: undefined, sourceClass: undefined, actForm: undefined,
            bindingStatus: undefined, language: undefined,
          }); }}>
            ← home
          </button>
          <button className={space === "search" ? "on" : ""} onClick={() => switchTo("search")}>search</button>
          <button className={space === "time" ? "on" : ""} onClick={() => switchTo("time")}>what changed</button>
        </nav>
      )}

      <AssistantController onReply={applyAssistantReply}
                followUps={chipsFor(s, ui, (held?.text ?? 1) > 0).map((c) => ({
                  label: c.label, run: () => { clearAssistantView(); go(c.go); } }))}
                onOpenStep={(st) => { clearAssistantView(); go({ work: st.work, date: st.date, anchor: st.anchor, mode: "read", space: "law" }); }} />

      {space === "time" && !s.work ? (
        <Period from={s.from ?? shift(today(), -365)} until={s.until ?? today()}
                order={s.order ?? "by_churn"} today={today()}
                jurisdiction={s.jurisdiction} hierarchy={s.hierarchy} domain={s.domain}
                sourceClass={s.sourceClass} actForm={s.actForm}
                bindingStatus={s.bindingStatus} language={s.language}
                onWindow={(from, until) => { setPage(0); clearAssistantView(); go({ from, until }); }}
                onOrder={(o) => { setPage(0); clearAssistantView(); go({ order: o }); }}
                onRefine={(next) => { setPage(0); clearAssistantView(); go(next); }} />
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
              {jurisdictionForPublisher(publisherOf(s.work)) ? (
                <span className="pill jurisdiction">{jurisdictionLabel(jurisdictionForPublisher(publisherOf(s.work))!)}</span>
              ) : null}
              {loaded ? (
                <span className={`pill ${loaded.to ? "old" : "live"}`}>
                  {temporalStatusLabel(s.work, loaded.to, timelineSemantics)}
                </span>
              ) : null}
              {loaded ? <span>{loaded.from} → {loaded.to ?? latestStateLabel(s.work, timelineSemantics)}</span> : null}
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
                            onClick={() => { clearAssistantView(); go({ language: l }); }}>{l}</button>
                  ))}
                </span>
              ) : null}
              <span className="grow" />
              <label className="pick"><i>{s.mode === "compare" ? "from" : "showing"}</i>
                <input name="publisher-date" type="date" value={s.date ?? today()} aria-label="Date whose publisher state to show"
                       onChange={(e) => go({ date: e.target.value })} />
              </label>
              <LawPicker current="choose another" onPick={pickLaw} />
              <a className="pick" href={`/${publisherOf(s.work)}/${workSlug(s.work)}`}>permalink ↗</a>
            </div>
          </div>
        </header>
      ) : null}

      {space === "law" && s.work ? (
        <VersionRail dates={railDates} current={at} compareTo={s.mode === "compare" ? s.to : undefined}
                     scope={railScope} today={today()} work={s.work} timelineSemantics={timelineSemantics}
                     onPick={(d) => { clearAssistantView(); go({ date: d, to: undefined, mode: "read" }); }}
                     onCompare={(d) => {
                       // Shift-click makes the pair, so comparing never means retyping a date
                       // that is already on screen. Order the pair; a diff runs forwards.
                       const from = at && at < d ? at : d;
                       const to = at && at < d ? d : at ?? d;
                       if (from === to) return;
                       clearAssistantView(); go({ date: from, to, mode: "compare" });
                     }}
                     onClear={() => { clearAssistantView(); go({ to: undefined, mode: "read" }); }} />
      ) : null}

      <div className="work"
           data-lex-operation-result-id={operationViews.length <= 1 && hasView(ui)
             ? assistantPresentationId : undefined}>
        {operationViews.length > 1 ? (
          <div className="operation-results" aria-label="Requested operation results">
            {operationViews.map((operation) => (
              <section className="operation-result" key={operation.operation_id}
                       data-lex-operation-result-id={hasView(operation.ui)
                         ? operation.operation_id : undefined}
                       aria-label={`Result ${operation.order + 1}`}>
                <p className="operation-label">Result {operation.order + 1}</p>
                <PublisherLimitations
                  items={limitationsFromEffect(operation.ui?.publisher_limitations)} />
                {renderOperation(operation)}
                <EvidenceCoordinates ui={operation.ui!} />
              </section>
            ))}
          </div>
        ) : <>
        <PublisherLimitations items={limitationsFromEffect(ui?.publisher_limitations)} />
        {ui?.gap ? <Gap {...ui.gap} held={s.work ? held : undefined} /> :
         ui?.ranking ? <Ranking rows={ui.ranking.rows} worksChanged={ui.ranking.works_changed}
                                newVersions={ui.ranking.new_versions} from={ui.ranking.from_date}
                                jurisdiction={s.jurisdiction}
                                populationWorks={ui.ranking.population_works}
                                knownExclusions={ui.ranking.known_exclusions}
                                to={ui.ranking.to_date} onOpen={openDiff} onOpenRecord={openLaw}
                                page={page}
                                hasMore={(page * PAGE) + ui.ranking.rows.length < ui.ranking.works_changed}
                                onPage={(p) => { setPage(Math.max(0, p)); clearAssistantView(); }} /> :
         ui?.cited_by ? <CitedBy view={ui.cited_by}
                                 onOpen={(w, d, a) => { clearAssistantView(); go({ work: w, date: d, anchor: a, mode: "read", space: "law" }); }} /> :
         ui?.coverage ? <CoveragePanel view={ui.coverage} /> :
         ui?.verification ? <VerificationPanel view={ui.verification} /> :
         ui?.in_force ? <InForce date={ui.in_force.date} total={ui.in_force.total} rows={ui.in_force.rows}
                                  page={page} hasMore={(page * PAGE) + ui.in_force.rows.length < ui.in_force.total}
                                  onPage={(p) => { setPage(Math.max(0, p)); clearAssistantView(); }} onOpen={openLaw} /> :
         s.work && s.mode === "compare" ? <Compare work={s.work} title={title ?? s.work} from={s.date ?? today()} to={s.to ?? today()} anchor={s.anchor} /> :
         s.work && loaded ? <Provision items={loaded.items} toc={toc} validFrom={loaded.from} validTo={loaded.to}
                                       work={s.work} title={title ?? s.work} language={servedLang}
                                       anchor={s.anchor} profile={loaded.profile}
                                       timelineSemantics={timelineSemantics}
                                       source={loaded.source}
                                       onCite={(w) => { clearAssistantView(); go({ work: w, date: undefined, anchor: undefined, to: undefined, mode: "read", space: "law" }); }}
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
        </>}
        {operationViews.length <= 1 && ui ? <EvidenceCoordinates ui={ui} /> : null}
      </div>

      {(s.work || ui) ? (
        <div className="chips">
          {chipsFor(s, ui, (held?.text ?? 1) > 0).map((c) => (
            <button key={c.label} className="chip" onClick={() => { clearAssistantView(); go(c.go); }}>{c.label}</button>
          ))}
        </div>
      ) : null}

    </div>
  );
}

/**
 * Two examples, set as a sentence rather than as buttons. A row of four chips reads as four
 * more decisions stacked under the one decision that matters; a line of prose reads as help.
 */
