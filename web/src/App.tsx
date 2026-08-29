import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { asOfResult, compoundOperationViews, first, provisionEmptyExplanation, provisionItemsOf, provisionResponseMeta, summedCount, summedPopulation, tool,
  unionKnownExclusions,
  type AskReply,
  type OperationReply, type ProvisionItem, type UiEffect } from "./api";
import type { EnvelopeStripRow } from "./envelopeStrip";
import { publisherOf, useWorkspace, workSlug, type Space, type State } from "./state";
import { CitedBy, ComparisonLimitations, CoveragePanel, Empty, EnvelopeStrip, EvidenceCoordinates, Gap, InForce, PartialResponseNotice, Provision, PublisherLimitations, Ranking, Timeline,
  VerificationPanel, VersionRail, hasView } from "./views";
import { limitationsFromEffect } from "./limitations";
import { Compare } from "./Compare";
import { LawPicker, shorten } from "./pickers";
import AssistantController from "./AssistantController";
import { assistantProvisionLoad, assistantTimelineSeed, assistantWorkspaceState,
  reportedTruncation } from "./assistantShell";
import Search from "./Search";
import Period from "./Period";
import Coach, { COACH_KEY } from "./Coach";
import { CompareSkeleton, LawSkeleton, ReportSkeleton } from "./Skeleton";
import { jurisdictionForPublisher, jurisdictionLabel } from "./facets";
import { latestStateLabel, temporalStatusLabel } from "./temporal";
import { AMBIGUOUS_ONLY_SENTENCE, INCOMPLETE_RESPONSE_SENTENCE, LIMITATION_EXPLANATION,
  MIXED_ZERO_SENTENCES, NO_CORPUS_SENTENCE, projectGovernedEmptiness } from "./limitations";

const utcDay = () => new Date().toISOString().slice(0, 10);

/**
 * The current UTC day, as live state rather than a value read once per render.
 *
 * "Today" is a fact about the world that changes without anyone touching the page. Reading it at
 * render time meant the default date moved when something else happened to re-render, and stood
 * still otherwise: a tab left open across midnight kept asking for yesterday, and a rerender for
 * an unrelated reason moved the visible default while the request that produced the rows on screen
 * had already been made against the previous day.
 *
 * Two triggers, because either alone leaves a gap. A timer at the next UTC boundary catches a page
 * left open, and a visibility check catches a tab that was asleep when the boundary passed, since
 * background timers are throttled and may fire late or not at all.
 */
function useUtcDay(): string {
  const [day, setDay] = useState(utcDay);
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const refresh = () => {
      // Clear before scheduling, always. Without this every visibility return left the previous
      // midnight callback running and scheduled another beside it, so a reader who switched tabs
      // a few times accumulated one timer per switch and each of those went on breeding nightly.
      if (timer !== undefined) clearTimeout(timer);
      setDay((current) => {
        const now = utcDay();
        return current === now ? current : now;
      });
      const now = new Date();
      const nextBoundary = Date.UTC(
        now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1);
      timer = setTimeout(refresh, Math.max(1, nextBoundary - now.getTime()));
    };
    refresh();
    // On `document`, which is where the platform dispatches it. It bubbles to the window, so a
    // window listener also fires, but binding the semantic target is what makes a test exact.
    const onVisible = () => { if (!document.hidden) refresh(); };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      if (timer !== undefined) clearTimeout(timer);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);
  return day;
}

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
function chipsFor(
  s: State, today: string, ui?: UiEffect, hasText = true,
): { label: string; go: Partial<State> }[] {
  // Offering a window the reader is already looking at is noise, so the twelve-month chip only
  // appears when the twelve months are not already on screen.
  if (ui?.ranking) {
    const from = shift(today, -365);
    return s.from === from && s.until === today ? []
         : [{ label: "Try the last twelve months", go: { from, until: today, mode: "read" } }];
  }
  if (s.mode === "compare") return [{ label: "Read the later version", go: { mode: "read", date: s.to, to: undefined } }];
  if (s.work && hasText) return [{ label: "Read the current text", go: { mode: "read", date: today, to: undefined } }];
  return [];
}

function shift(date: string, days: number) {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

export default function App() {
  const today = useUtcDay();
  const [s, go] = useWorkspace();
  /**
   * The date these effects actually ask for. Not `s.date`: with no explicit date the request
   * falls back to today, and today moves on its own. Watching `s.date` alone meant a tab open
   * across a UTC boundary kept showing the previous day, with no request to correct it.
   */
  const readDate = s.date ?? today;
  const [ui, setUi] = useState<UiEffect>();
  /**
   * The governed-response generation, for the two effects this branch touches.
   *
   * A boolean captured per effect run is flipped by passive cleanup, which runs after the
   * next paint, so a response arriving in that interval was still live. Worse, an accepted
   * assistant view can install a new view without changing the request tuple at all, so the
   * effect never re-runs, its cleanup never fires, and an older held response could later
   * overwrite a view the reader had already been given.
   *
   * Advanced wherever the view is replaced: the shared clear, the assistant install, and the
   * start of each governed request. Every completion compares the generation it captured.
   */
  const governedGeneration = useRef(0);
  /**
   * The accepted view already applied, so one turn applies once.
   *
   * The assistant delivers a turn twice, once as the streamed operation result and once in the
   * final reply, and both run through the callback captured when the reader asked. Applying the
   * same accepted view a second time rewrites state that no effect will re-run to repair, since
   * the request tuple did not change between the two applies.
   *
   * Written and read by the same function on purpose. A ref one function sets and another
   * consumes is not stale-closure safe, which is exactly how the one-shot design considered
   * here would have stranded on its first turn.
   */
  const appliedReply = useRef<string>();
  /**
   * One canonical request per accepted governed destination.
   *
   * An event counter in state, not a consumable token in a ref. It sits in both governed dependency
   * lists, so incrementing it re-runs the effect whether or not the destination tuple changed, and
   * there is nothing to consume, nothing to match and nothing to strand for a later navigation. A
   * one-shot token was considered here and rejected: it would have been written by this callback
   * and consumed by an effect, which is not stale-closure safe, and an unconsumed key would have
   * turned a later Back into an empty view with no request.
   */
  const [governedRefresh, setGovernedRefresh] = useState(0);
  /**
   * The law-request generation, for the outline and read paths.
   *
   * Those two cleared their content in a passive effect or after a response, so at a UTC rollover
   * React could commit the new default date while the previous day's provisions, gap and index
   * strip survived a paint. A reader saw one frame of yesterday's law under today's date.
   */
  const lawGeneration = useRef(0);
  // One generation per governing request identity, not one for the whole surface. The four law
  // effects are keyed on different dependency sets, so a single generation would invalidate an
  // in-flight request whose own effect will not re-run, and strand its rail empty for good.
  const workGeneration = useRef(0);
  const outlineGeneration = useRef(0);
  const anchorGeneration = useRef(0);

  /**
   * The law transition, before paint.
   *
   * Keyed on exactly what those two effects ask for. An explicit date does not move at a rollover,
   * so a reader pinned to a date is never cleared by the clock; only the default-date routes are,
   * which is the case where the question itself changed.
   */
  useLayoutEffect(() => {
    lawGeneration.current += 1;
    if (!s.work) return;
    setLoaded(undefined);
    setUi(undefined);
    setStrip([]);
    return () => { lawGeneration.current += 1; };
  }, [s.work, readDate, s.mode, s.anchor, s.language]);

  /**
   * The rest of the law surface, cleared at the identity that repopulates it rather than all at
   * once. A work switch used to leave the previous law title, version rail, languages, held
   * summary and article history under the next law route, because the only complete reset sat
   * behind a no-work guard that runs when the reader leaves the surface, never when they move
   * from one law to another. Observed rather than reasoned: with the next work held, the heading
   * still named the previous law over an empty body.
   *
   * Keyed separately because the identities differ. Title, contents and served language belong to
   * the outline request; the rails belong to the work; article history belongs to work and anchor.
   * Clearing any of them on the widest key would empty a rail whose effect does not re-run, which
   * is the failure this split exists to avoid.
   */
  useLayoutEffect(() => {
    outlineGeneration.current += 1;
    if (!s.work) return;
    setToc([]);
    setTitle(undefined);
    setServedLang(undefined);
    return () => { outlineGeneration.current += 1; };
  }, [s.work, readDate, s.language]);

  useLayoutEffect(() => {
    workGeneration.current += 1;
    if (!s.work) return;
    setVersions([]);
    setVersionsPartial(undefined);
    setLangs([]);
    setHeld(undefined);
    setTimelineSemantics(undefined);
    return () => { workGeneration.current += 1; };
  }, [s.work]);

  useLayoutEffect(() => {
    anchorGeneration.current += 1;
    if (!s.work || !s.anchor) return;
    setStates([]);
    setStatesPartial(undefined);
    return () => { anchorGeneration.current += 1; };
  }, [s.work, s.anchor]);
  const [operationViews, setOperationViews] = useState<OperationReply[]>([]);
  const [assistantPresentationId, setAssistantPresentationId] = useState<string>();
  const pendingPresentations = useRef(new Set<string>());
  const measuredPresentations = useRef(new Set<string>());
  const [loaded, setLoaded] = useState<{ items: ProvisionItem[]; from: string; to?: string;
                                        profile?: string; source?: string;
                                        totalProvisions?: number; totalProvisionGaps?: number;
                                        truncated?: boolean; textTruncated?: boolean;
                                        textCompleteness?: string;
                                        recordSha256?: string; bodySha256?: string }>();
  const [toc, setToc] = useState<ProvisionItem[]>([]);
  const [title, setTitle] = useState<string>();
  const [versions, setVersions] = useState<string[]>([]);
  // Whether the version list is the whole list. The producer says so on every timeline
  // response and both paths that fill the rail used to drop it, so a rail built from a
  // page of versions counted them as if they were all of them.
  const [versionsPartial, setVersionsPartial] = useState<boolean>();
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
  // The index identity behind whatever data view is showing, kept from the response that produced
  // it rather than fetched separately, so the strip describes THIS answer's index.
  const [strip, setStrip] = useState<EnvelopeStripRow[]>([]);
  const [states, setStates] = useState<string[]>([]);
  const [statesPartial, setStatesPartial] = useState<boolean>();
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
    // The index identity belongs to the response that produced the view. Opening a law after a
    // search would otherwise leave the search's strip above a law it never described.
    setStrip([]);
    if (!s.work) { setVersions([]); setVersionsPartial(undefined); setLangs([]); setServedLang(undefined); setTimelineSemantics(undefined); setHeld(undefined); return; }
    // Never carry one publisher's time semantics across a work switch while the next timeline
    // is loading. The work-id fallback remains correct for currently mounted legacy artifacts.
    setTimelineSemantics(undefined);
    const mine = workGeneration.current;
    const live = () => mine === workGeneration.current;
    tool<any>("timeline", { work: s.work, limit: 400 })
      .then((res) => {
        if (!live()) return;
        const one = first<any>(res, (x) => Array.isArray(x?.versions) && x.versions.length > 0);
        const vs = (one?.versions ?? []) as any[];
        setTimelineSemantics(one?.envelope?.timeline_semantics);
        const dates = [...new Set(vs.map((v) => String(v.valid_from)))] as string[];
        setVersions(dates.sort());
        // Identity, not truthiness: this value decides whether the rail may present its
        // count as the law's version count, and the response is not validated on the way in.
        setVersionsPartial(reportedTruncation(one?.truncated));
        // Which languages this work exists in. The Constitution is published in French, German
        // and Luxembourgish, and its stored title is German for all three, so a reader looking
        // at the French text sees a German heading above it and reasonably concludes the page is
        // broken. Naming the language being read costs one chip and removes the contradiction.
        setLangs([...new Set(vs.map((v) => String(v.language)).filter(Boolean))].sort());
        setHeld({ text: vs.filter((v) => v.text_available).length, total: vs.length,
                  official: vs[vs.length - 1]?.source_uri,
                  kind: vs[vs.length - 1]?.document_type });
      })
      .catch(() => { if (live()) { setVersions([]); setVersionsPartial(undefined); setLangs([]); setTimelineSemantics(undefined); setHeld(undefined); } });
  }, [s.work]);

  // The outline belongs to (law, date) — never to the focused article. It used to be fetched
  // as part of the text, so opening an article replaced the contents with that one article
  // and re-dating dropped you at the top of a document you were reading the middle of.
  useEffect(() => {
    if (!s.work) { setToc([]); return; }
    const mine = outlineGeneration.current;
    const live = () => mine === outlineGeneration.current;
    tool<any>("as_of", { work: s.work, date: readDate, mode: "outline",
                         ...(s.language ? { language: s.language } : {}) })
      .then((res) => {
        if (!live()) return;
        const one = asOfResult<any>(res);
        setToc(provisionItemsOf(one));
        // The law's name belongs to the law, not to the mode you are reading it in. It used to
        // be set only on the read path, so opening a comparison showed the raw work slug as
        // the heading — the one place a reader most needs to know which law they are looking at.
        const t = shorten((one?.document ?? one)?.title);
        if (t) setTitle(t);
        setServedLang((one?.document ?? one)?.language);
      })
      .catch(() => { if (live()) setToc([]); });
  }, [s.work, readDate, s.language]);

  // Deterministic loading: changing date, article or mode calls the public MCP endpoint
  // directly. No model in the loop — playing with the workspace must be instant and repeatable.
  useEffect(() => {
    if (!s.work || s.mode !== "read") return;
    // Same construction as the outline effect, and for the same reason. A boolean flipped by
    // this passive cleanup is not enough on its own: the layout transition above has already
    // bumped the generation and cleared the view before paint, while the passive phase runs
    // after it. A request issued under the previous UTC day can resolve inside that gap with
    // its flag still true, and write yesterday provisions under today heading. Comparing the
    // generation the request was issued under cannot be late, because the value it compares
    // against is the one the transition itself already changed.
    const mine = lawGeneration.current;
    const live = () => mine === lawGeneration.current;
    // Never show one law's text under another's heading: clear before fetching.
    setLoaded(undefined);
    const date = readDate;
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
      const o = asOfResult<any>(outline);
      const n = provisionItemsOf(o).length;
      if (n === 0 || n > 200) return outline;
      return tool<any>("as_of", { work: s.work, date, mode: "full", ...lang });
    };
    fetchRead()
      .then((res) => {
        if (!live()) return;
        const one = asOfResult<any>(res);
        const doc = one?.document ?? one;
        setTitle(shorten(doc?.title));
        const items = provisionItemsOf(one);
        const meta = provisionResponseMeta(one);
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
              profile: doc?.extraction_profile, source: doc?.source_uri,
              // Without the digest the copied citation carries a permalink and nothing to
              // check it against on any view holding more than one article.
              recordSha256: doc?.record_sha256,
              bodySha256: doc?.body_sha256,
              ...meta }
          : undefined);
        if (items.length === 0)
          setUi({ gap: {
            status: meta.textCompleteness === "partial" || meta.textTruncated
              ? "partial_response"
              : one?.envelope?.status ?? "no_result",
            explanation: provisionEmptyExplanation(meta),
            available: [],
            total_provisions: meta.totalProvisions,
            total_provision_gaps: meta.totalProvisionGaps,
            truncated: meta.truncated,
            text_truncated: meta.textTruncated,
            text_completeness: meta.textCompleteness,
          } });
        else setUi(undefined);
      })
      .catch(() => { if (live()) setUi({ gap: { status: "error", explanation: "That version could not be loaded.", available: [] } }); });
  }, [s.work, readDate, s.mode, s.anchor, s.language]);

  // Time workspace: a period loads the ranking deterministically, so the follow-up chips
  // and any /?from=&until= link land on real content instead of an empty panel.
  useEffect(() => {
    if (s.work || !s.from || !s.until) return;
    governedGeneration.current += 1;
    const mine = governedGeneration.current;
    const live = () => mine === governedGeneration.current;
    // Before the request, not after it. The previous window's ranking and index identity
    // describe a period the reader has already left, and leaving the rows up also kept
    // ReportSkeleton unreachable, so the stale table was the entire loading state.
    setUi(undefined);
    setStrip([]);
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
        if (!live()) return;
        // changes_in_period asks ACROSS the corpus, so its answer is the union of the
        // publishers, not the first one that happens to reply. Taking the first envelope with
        // rows reported 3 EU acts for the pandemic and silently dropped the hundreds of
        // Luxembourg ones behind it, because the EU index answers first.
        // Row authority: only envelopes that ran a coherent response contribute rows or
        // counts. The closed classification and the typed empty decision both come from the
        // one shared projector the tests call (round 4, O1/O2/O4).
        const first = projectGovernedEmptiness("changes_in_period", res, 1);
        const partition = first.partition;
        const ran = partition.ran as any[];
        const rows = ran.flatMap((e) => (e?.changes ?? []).map((row: any) => ({
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
        // beside them, and only the typed empty states speak for absence.
        const decision = projectGovernedEmptiness(
          "changes_in_period", res, visibleRows.length);
        setStrip(partition.stripRows);
        setUi(decision.empty === null
          ? { ranking: { from_date: s.from!, to_date: s.until!, order: by,
                         works_changed: summedCount(ran, "works_changed"),
                         new_versions: summedCount(ran, "new_versions"),
                         // Refuses rather than coerces. A string, a fraction, a negative or
                         // an overflowing sum yields no figure at all, because a denominator
                         // the reader is invited to check against must be one the producer
                         // could have minted.
                         population_works: summedPopulation(ran, "works_in_scope"),
                         population_basis: "sum of the selected publisher scopes",
                         known_exclusions: unionKnownExclusions(ran),
                         rows: visibleRows },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : decision.empty === "all_refused"
          ? { gap: { status: "filter_not_supported_by_index",
                     explanation: LIMITATION_EXPLANATION, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          // An incomplete response claims nothing at all (round 4, O1/O2).
          // A server with no mounted index is a terminal deployment state; retrying
          // cannot help, so the copy never suggests it.
          : decision.empty === "no_corpus"
          ? { gap: { status: "no_corpus_mounted",
                     explanation: NO_CORPUS_SENTENCE, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : decision.empty === "ambiguous_only"
          ? { gap: { status: "ambiguous_only",
                     explanation: AMBIGUOUS_ONLY_SENTENCE, available: [] },
              publisher_limitations: partition.limitations,
              // An unusable sibling is still disclosed beside the ambiguity message.
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : decision.empty === "incomplete_response"
          ? { gap: { status: "incomplete_response",
                     explanation: INCOMPLETE_RESPONSE_SENTENCE, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          // Mixed zero: a publisher refused, so a whole-scope absence claim is unprovable;
          // the copy names only the publishers that ran.
          : decision.empty === "mixed_no_match"
          ? { gap: { status: "mixed_no_match",
                     explanation: MIXED_ZERO_SENTENCES.changes_in_period, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : { gap: { status: "no_changes_in_period", explanation: "Nothing changed in that window.", available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers });
      })
      .catch(() => { if (live()) setUi({ gap: { status: "error", explanation: "The change report could not be loaded. Try again.", available: [] } }); });
  }, [s.work, s.from, s.until, s.order, s.jurisdiction, s.hierarchy, s.domain,
      s.sourceClass, s.actForm, s.bindingStatus, s.language, page, governedRefresh]);

  // "What was in force on this day": the compliance question, answered deterministically like
  // every other control in the workspace rather than only through the assistant.
  useEffect(() => {
    // A date with no question is itself a question: what applied that day. Not a mode, just what
    // an empty search means once a date is set.
    if (s.work || s.q || !s.asOf || s.space === "time") return;
    governedGeneration.current += 1;
    const mine = governedGeneration.current;
    const live = () => mine === governedGeneration.current;
    // Before the request. A reader who changes the date must never see the previous date's
    // list, or its index identity, presented as the answer for the new one.
    setUi(undefined);
    setStrip([]);
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
        if (!live()) return;
        const first = projectGovernedEmptiness("in_force_on", res, 1);
        const partition = first.partition;
        setStrip(partition.stripRows);
        const ran = partition.ran as any[];
        // in_force_on returns `works` with a `total_works_in_force` count, and its rows carry
        // work/title/document_type/valid_from. Mapped here to the shape the view already speaks.
        const rows = ran.flatMap((e) => (e?.works ?? []).map((w: any) => ({
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
        // Ambiguity units are held content the reader must see, not a silent contribution to
        // the total (PR293 exact review, O1). They render beside normal rows, carry their own
        // marker, and count as page units so pagination describes what is actually shown.
        const ambiguityDecision = projectGovernedEmptiness("in_force_on", res, rows.length);
        const ambiguityRows = ambiguityDecision.ambiguous.map((unit: any) => ({
          work: unit.lex_id
            ? String(unit.lex_id).split(":").slice(0, 2).join(":")
            : String(unit.work ?? ""),
          title: unit.title, kind: unit.document_type,
          valid_from: unit.valid_from ?? s.asOf!, permalink: unit.permalink,
          jurisdiction: unit.jurisdiction, hierarchy: unit.hierarchy,
          ambiguous: true,
        }));
        const pageUnits = [...rows, ...ambiguityRows];
        const visibleRows = pageUnits.slice(page * PAGE, (page + 1) * PAGE);
        const decision = projectGovernedEmptiness("in_force_on", res, visibleRows.length);
        // Trust rule 6: the producer publishes the population behind this list and the client
        // discarded it, so the reader saw a count of states with nothing to read it against.
        // Summed only across the publishers that actually ran. The basis is shown only when every
        // contributing publisher states the same one, because a single label cannot honestly
        // describe two different populations.
        const bases = [...new Set(ran.map((e) => e?.population?.basis)
          .filter((b: unknown): b is string => typeof b === "string" && b.trim().length > 0))];
        // Refuses rather than coerces. The previous form added a zero for every entry whose
        // count was missing or malformed, so a string, a fraction or a negative silently
        // shrank a denominator the reader is invited to check an answer against.
        const covered = summedPopulation(ran, "works_covered");
        // works_covered comes from Coverage(1).Groups, which counts a publisher's versioned works
        // and is never narrowed by the metadata filters this request sent. Presenting it beside a
        // filtered list without saying so would imply the filters reduced the denominator.
        const scopeFiltersApplied = !(s.jurisdiction || s.hierarchy || s.domain || s.sourceClass
          || s.actForm || s.bindingStatus || s.language);
        setUi(decision.empty === null
          ? { in_force: { date: s.asOf!, total: summedCount(ran, "total_works_in_force"),
                          population_works: covered,
                          population_basis: bases.length === 1 ? bases[0] : undefined,
                          population_scope_filters_applied: scopeFiltersApplied,
                          known_exclusions: unionKnownExclusions(ran),
                          rows: visibleRows },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : decision.empty === "all_refused"
          ? { gap: { status: "filter_not_supported_by_index",
                     explanation: LIMITATION_EXPLANATION, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : decision.empty === "no_corpus"
          ? { gap: { status: "no_corpus_mounted",
                     explanation: NO_CORPUS_SENTENCE, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : decision.empty === "ambiguous_only"
          ? { gap: { status: "ambiguous_only",
                     explanation: AMBIGUOUS_ONLY_SENTENCE, available: [] },
              publisher_limitations: partition.limitations,
              // An unusable sibling is still disclosed beside the ambiguity message.
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : decision.empty === "incomplete_response"
          ? { gap: { status: "incomplete_response",
                     explanation: INCOMPLETE_RESPONSE_SENTENCE, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : decision.empty === "mixed_no_match"
          ? { gap: { status: "mixed_no_match",
                     explanation: MIXED_ZERO_SENTENCES.in_force_on, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers }
          : { gap: { status: "no_result", explanation: `No publisher state covers ${s.asOf} in this scope.`, available: [] },
              publisher_limitations: partition.limitations,
              partial_response: decision.partial,
              conflicted_publishers: partition.conflictedPublishers });
      })
      .catch(() => { if (live()) setUi({ gap: { status: "error", explanation: "The in-force list could not be loaded. Try again.", available: [] } }); });
  }, [s.space, s.asOf, s.work, s.q, s.jurisdiction, s.hierarchy, s.domain,
      s.sourceClass, s.actForm, s.bindingStatus, s.language, page, governedRefresh]);

  // With an article open the rail narrows to THAT article's distinct texts — the question a
  // reader actually has ("when did this paragraph change?") rather than "when was anything
  // in this law touched?". Falls back to the law's versions when no per-article history exists.
  useEffect(() => {
    if (!s.work || !s.anchor) { setStates([]); setStatesPartial(undefined); return; }
    const mine = anchorGeneration.current;
    const live = () => mine === anchorGeneration.current;
    tool<any>("article_history", { work: s.work, anchor: s.anchor })
      .then((res) => {
        if (!live()) return;
        const one = first<any>(res, (x) => Array.isArray(x?.states) && x.states.length > 0);
        setStates([...new Set(((one?.states ?? []) as { valid_from: string }[])
          .map((x) => x.valid_from))].sort());
        setStatesPartial(reportedTruncation(one?.truncated));
      })
      .catch(() => { if (live()) { setStates([]); setStatesPartial(undefined); } });
  }, [s.work, s.anchor]);

  const applyAssistantReply = useCallback((r: AskReply) => {
      // Operation ids as well as the view, so two identical questions stay distinguishable.
      // If the two payloads ever serialise differently this fails open to applying twice,
      // which is the behaviour it replaces rather than a new one.
      const identity = JSON.stringify(
        [r.operations?.map((operation) => operation.operation_id) ?? [], r.ui ?? null]);
      if (identity === appliedReply.current) return;
      appliedReply.current = identity;
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
        // Invalidate only once there is an accepted view to replace with. This used to run for
        // every callback, including a streaming one carrying no view, which invalidated an
        // outstanding governed request without installing a replacement and without changing
        // any dependency, so nothing re-ran and the held completion was stranded. The comment
        // above it said an accepted view is a new answer on this route, which was the intent
        // and not what the code did.
        governedGeneration.current += 1;
        setUi(r.ui);
        const timeline = assistantTimelineSeed(r.ui);
        if (timeline) {
          setVersions(timeline.versions);
          // assistantTimelineSeed has always returned this. Dropping it let an assistant
          // answer replace a complete rail with a page of it and say nothing.
          setVersionsPartial(timeline.truncated);
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
          // The deterministic effect owns these two, so the assistant answer is not installed as
          // the primary view. Its payload is a 20 row page while the effect asks for 25 and slices
          // in 25s, so installing it would leave the reader on a page the URL beside it does not
          // reproduce. Clear, navigate through the now idempotent go, and let the effect answer.
          setUi(undefined);
          setStrip([]);
          setPage(Math.max(0, r.ui!.workspace?.page ?? 0));
          go(assistantWorkspaceState(r.ui)!);
          setGovernedRefresh((n) => n + 1);
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
    governedGeneration.current += 1;
    setUi(undefined);
    // The strip describes the response that produced the view being cleared. Leaving it up
    // across a route or space change states an index identity for an answer that is gone.
    setStrip([]);
    setOperationViews([]);
    pendingPresentations.current.clear();
    setAssistantPresentationId(undefined);
  }, []);

  /**
   * Back and Forward reach the same destinations the controls do, and must leave the same
   * state behind.
   *
   * Every in-app transition routes through `clearAssistantView`, so the parent view never
   * outlives the route that produced it. History navigation bypasses all of them: `popstate`
   * only re-reads the URL, and the effects below return early when the destination no longer
   * qualifies, so nothing clears. Going back from a change report, an in-force list or a law
   * gap to a search URL left the previous ranking, list or gap rendered in the work area
   * indefinitely, describing a route the reader had already left.
   *
   * Clearing here cannot destroy a newly accepted assistant view. The assistant navigates by
   * pushing a history entry, which does not fire `popstate`; only the reader moving through
   * history does, and at that point the accepted view describes the destination being left.
   */
  useEffect(() => {
    const onPop = () => clearAssistantView();
    addEventListener("popstate", onPop);
    return () => removeEventListener("popstate", onPop);
  }, [clearAssistantView]);

  // Open on the text in force TODAY, never on the oldest version — the oldest is the one most
  // likely to have no stored text, so the old behaviour greeted every visitor with a refusal.
  const pickLaw = (h: { work: string; title: string }) => {
    clearAssistantView(); setTitle(h.title); setVersions([]); setVersionsPartial(undefined);
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
  const railScope = narrowed
    ? `distinct article text date${railDates.length === 1 ? "" : "s"}`
    : `distinct version date${railDates.length === 1 ? "" : "s"}`;
  // A narrowed rail is article states from article_history, a different response with its
  // own completeness. Carrying the timeline flag onto it would qualify the wrong list.
  const railPartial = narrowed ? statesPartial : versionsPartial;
  const at = loaded?.from && railDates.includes(loaded.from) ? loaded.from
           : railDates.filter((d) => d <= (s.date ?? today)).pop();

  const openLaw = (work: string, date: string) => { clearAssistantView(); go({ work, date, to: undefined, anchor: undefined, mode: "read", space: "law" }); };
  const openDiff = (work: string, from: string, to: string) => { clearAssistantView(); go({ work, date: from, to, mode: "compare", space: "law" }); };

  // Which framework is on screen: whatever the URL says, else inferred from what is loaded.
  const space: Space = s.space ?? (s.work ? "law" : (s.from || ui?.ranking) ? "time" : "search");

  const switchTo = (sp: Space) => {
    clearAssistantView();
    setPage(0);
    if (sp === "time") go({ space: sp, work: undefined, anchor: undefined, from: s.from ?? shift(today, -365), until: s.until ?? today, order: s.order ?? "by_churn" });
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
      rows={view.in_force.rows} populationWorks={view.in_force.population_works}
      populationBasis={view.in_force.population_basis}
      populationScopeFiltersApplied={view.in_force.population_scope_filters_applied}
      knownExclusions={view.in_force.known_exclusions}
      page={0} hasMore={false} onPage={() => {}} onOpen={openLaw} />;
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
      </div> : view.diff.changed !== true && view.diff.changed !== false ? null : <div className="cnt">
        {/* A whole-work comparison has no anchor, so none of the provision-level tags above apply
            and until now it rendered no outcome at all: a reader was told a comparison happened and
            left to guess how it came out. `changed` is the only typed outcome this case has.

            It is a record fact, whether the two dates resolved to different publisher versions, so
            the wording speaks about versions and never about the law. "Nothing changed" would be a
            legal claim this field cannot support and Decision 44 forbids.

            Both branches test identity, not truthiness, and the guard above admits only the two
            real booleans. The stream parser casts parsed JSON to `OperationReply` without runtime
            validation, so `changed?: boolean` describes what should arrive and constrains nothing
            that does. Under truthiness a `null` from version skew or malformed transport lands on
            the reassuring branch and states that the same version applied, which is a claim the
            producer never made. Every value that is not exactly `true` or `false` is a value this
            panel cannot interpret, and an uninterpretable outcome is no outcome. */}
        <span className="tag">{view.diff.changed === true
          ? "different versions on these dates"
          : "the same version applied on both dates"}</span>
      </div>}
      <ComparisonLimitations limitations={view.diff.comparison_limitations}
        malformed={view.diff.comparison_limitations_malformed} />
      {view.diff.note ? <p>{view.diff.note}</p> : null}
      <button className="operation-open" onClick={() => openDiff(
        view.diff!.subject.work, view.diff!.from_date, view.diff!.to_date)}>
        Open comparison
      </button>
    </section>;
    const subject = view.provision?.subject ?? view.history?.subject;
    if (subject?.work) {
      const from = view.provision?.valid_from ?? subject.date ?? today;
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
          // Keyed by the submitted question on purpose. Authorizations a reader gives for
          // one question, the exact-words override and the publisher metadata filter, are
          // held in this component, and a clearing effect could not discard them safely:
          // it only clears when it observes the intervening question, so a fast
          // q1, q2, Back to q1 never observed q2 and left the override armed. Remounting
          // discards them during render, so there is no window and nothing to observe.
          // Trimmed, because that is the identity fuzzyModeFor already compares. Keying on the
          // raw string would make "x" and "  x  " two questions and drop an authorization the
          // reader gave for what is visibly one.
          key={(s.q ?? "").trim()}
          state={s} today={today}
          onSubmit={({ query, asOf }) => { setPage(0); clearAssistantView(); go({
            q: query || undefined,
            ...(asOf ? { asOf } : {}),
            work: undefined, from: undefined, until: undefined, space: "search",
          }); }}
          onAsOf={(d) => { setPage(0); clearAssistantView(); go({ asOf: d }); }}
          onRefine={(next) => { setPage(0); clearAssistantView(); go(next); }}
          onOpen={(work, date, anchor) => { clearAssistantView(); go({ work, date, anchor, mode: "read", space: "law" }); }}
          onMonitor={() => switchTo("time")}
          onEnvelopes={setStrip}
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
                followUps={chipsFor(s, today, ui, (held?.text ?? 1) > 0).map((c) => ({
                  label: c.label, run: () => { clearAssistantView(); go(c.go); } }))}
                onOpenStep={(st) => { clearAssistantView(); go({ work: st.work, date: st.date, anchor: st.anchor, mode: "read", space: "law" }); }} />

      {space === "time" && !s.work ? (
        <Period from={s.from ?? shift(today, -365)} until={s.until ?? today}
                order={s.order ?? "by_churn"} today={today}
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
                <input name="publisher-date" type="date" value={s.date ?? today} aria-label="Date whose publisher state to show"
                       onChange={(e) => go({ date: e.target.value })} />
              </label>
              <LawPicker current="choose another" onPick={pickLaw} />
              <a className="pick" href={`/${publisherOf(s.work)}/${workSlug(s.work)}`}>permalink ↗</a>
            </div>
          </div>
        </header>
      ) : null}

      {space === "law" && s.work ? (
        <VersionRail dates={railDates} current={at} partial={railPartial}
                     compareTo={s.mode === "compare" ? s.to : undefined}
                     scope={railScope} today={today} work={s.work} timelineSemantics={timelineSemantics}
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
        <PartialResponseNotice partial={ui?.partial_response}
                               conflicted={ui?.conflicted_publishers} />
        {ui?.gap ? <Gap {...ui.gap} held={s.work ? held : undefined} /> :
         ui?.ranking ? <Ranking rows={ui.ranking.rows} worksChanged={ui.ranking.works_changed}
                                newVersions={ui.ranking.new_versions} from={ui.ranking.from_date}
                                jurisdiction={s.jurisdiction}
                                populationWorks={ui.ranking.population_works}
                                knownExclusions={ui.ranking.known_exclusions}
                                to={ui.ranking.to_date} onOpen={openDiff} onOpenRecord={openLaw}
                                page={page}
                                hasMore={ui.ranking.works_changed !== undefined
                                  && (page * PAGE) + ui.ranking.rows.length < ui.ranking.works_changed}
                                onPage={(p) => { setPage(Math.max(0, p)); clearAssistantView(); }} /> :
         ui?.cited_by ? <CitedBy view={ui.cited_by}
                                 onOpen={(w, d, a) => { clearAssistantView(); go({ work: w, date: d, anchor: a, mode: "read", space: "law" }); }} /> :
         ui?.coverage ? <CoveragePanel view={ui.coverage} /> :
         ui?.verification ? <VerificationPanel view={ui.verification} /> :
         ui?.in_force ? <InForce date={ui.in_force.date} total={ui.in_force.total} rows={ui.in_force.rows}
                                  populationWorks={ui.in_force.population_works}
                                  populationBasis={ui.in_force.population_basis}
                                  populationScopeFiltersApplied={ui.in_force.population_scope_filters_applied}
                                  knownExclusions={ui.in_force.known_exclusions}
                                  page={page} hasMore={ui.in_force.total !== undefined
                                    && (page * PAGE) + ui.in_force.rows.length < ui.in_force.total}
                                  onPage={(p) => { setPage(Math.max(0, p)); clearAssistantView(); }} onOpen={openLaw} /> :
         s.work && s.mode === "compare" ? <Compare work={s.work} title={title ?? s.work} from={s.date ?? today} to={s.to ?? today} anchor={s.anchor} /> :
         s.work && loaded ? <Provision items={loaded.items} toc={toc} validFrom={loaded.from} validTo={loaded.to}
                                        work={s.work} title={title ?? s.work} language={servedLang}
                                        anchor={s.anchor} profile={loaded.profile}
                                        timelineSemantics={timelineSemantics}
                                        recordSha256={loaded.recordSha256}
                                        bodySha256={loaded.bodySha256}
                                       source={loaded.source}
                                       textCompleteness={loaded.textCompleteness}
                                       totalProvisions={loaded.totalProvisions}
                                       totalProvisionGaps={loaded.totalProvisionGaps}
                                       truncated={loaded.truncated}
                                       textTruncated={loaded.textTruncated}
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
        <EnvelopeStrip rows={strip} />
      </div>

      {(s.work || ui) ? (
        <div className="chips">
          {chipsFor(s, today, ui, (held?.text ?? 1) > 0).map((c) => (
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
