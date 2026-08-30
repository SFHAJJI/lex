import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { safeHttpsUrl, tool } from "./api";
import { facetLabel as label, jurisdictionForPublisher, jurisdictionLabel } from "./facets";
import {
  parsePublisherMetadata,
  publisherMetadataCaption,
  publisherMetadataFilterArguments,
  type PublisherMetadata,
} from "./publisherMetadata";
import { ScopeFilters } from "./ScopeFilters";
import { envelopeStripRows, type EnvelopeStripRow } from "./envelopeStrip";
import { normalizeSearchResponse, type PublisherPopulation } from "./searchPopulation";
import { anyRowSetTruncated, metadataOnlyFromResponse,
  type PopulationEntry } from "./matchLanes";
import { MetadataOnlyNotice } from "./metadataOnlyNotice";
import { fuzzyModeFor, retainedForQuery } from "./api";
import { clearedSearchResults, LIMITATION_EXPLANATION, parseGovernedResponse,
  projectSearchResponse, searchEmptyPresentation, searchResultsFromError,
  withholdingSentence,
  type SearchResultsState, type WithheldClaims } from "./limitations";
import { PartialResponseNotice, PopulationFooter, PublisherLimitations } from "./views";
import type { State } from "./state";
import { shorten } from "./pickers";
import { ResultsSkeleton } from "./Skeleton";
import { fusePublisherHits } from "./searchFusion";
import { intervalLabel } from "./temporal";
import { searchSubmission, type SearchSubmission } from "./searchSubmission";
import { groupSearchResults } from "./searchResults";

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
  onSubmit: (submission: SearchSubmission) => void;
  onAsOf: (d?: string) => void;
  onRefine: (next: Partial<State>) => void;
  onMonitor: () => void;
  /**
   * The index identity behind these results, lifted so the shell's EnvelopeStrip describes the
   * response actually on screen. Trust rule 4 puts freshness on every data view, and the search
   * surface is the most used one.
   */
  onEnvelopes: (rows: EnvelopeStripRow[]) => void;
}

type HitMeta = {
  validFrom?: string; validTo?: string; hierarchy?: string; language?: string;
  jurisdiction?: string;
  timelineSemantics?: string;
  domains?: string[]; consolidationStatus?: string; matchReasons?: string[];
  publisherMetadata?: PublisherMetadata;
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

const INITIAL_ARTICLES = 8;

export default function Search(p: SearchProps) {
  const [text, setText] = useState(p.state.q ?? "");
  const [results, setResults] =
    useState<SearchResultsState<WorkHit, ArticleHit>>(clearedSearchResults);
  // The B2 response-level state, decided from the RAW envelopes before fusion, the display cap
  // and the passage filter, so it describes the whole response rather than the visible slice.
  // The server page reached this months ago; this lane rendered work_metadata-only hits as
  // answers because matchLanes.ts was in the tree with no production import at all.
  const [metadataOnly, setMetadataOnly] = useState(false);
  const [metadataPopulation, setMetadataPopulation] = useState<PopulationEntry[]>([]);
  const [responseTruncated, setResponseTruncated] = useState(false);
  const { works, articles, error, modeUnavailable, expansions, limitations } = results;
  const allRefused = results.absence === "all_refused";
  const [busy, setBusy] = useState(false);
  // The denominator behind whatever this response showed, kept from the response itself so the
  // footer can never describe a different query than the one on screen.
  const [populations, setPopulations] = useState<PublisherPopulation[]>([]);
  /**
   * What this response could not attribute. A publisher whose population authority is void
   * loses its rows as well as its denominator, and rows the reader cannot check against a
   * denominator are the exact claim this lane exists to stop. Withholding them silently would
   * trade one wrong answer for a quieter one, so it is disclosed and it suppresses the
   * confident empty sentence, which would otherwise report "nothing matched" for a response
   * that was cut down.
   */
  const [withheld, setWithheld] = useState<WithheldClaims>();
  /**
   * Trust rule 9: a reader told their query was rewritten must be able to undo it. The override
   * stores the exact query it was chosen for, so it can never silently apply to a different one.
   * Typing a new question restores the default rather than carrying a decision the reader made
   * about words they are no longer searching for.
   */
  const [exactQuery, setExactQuery] = useState<string>();

  /**
   * Every piece of state derived from a response, cleared together. Rows, limitations, the
   * denominator and the index identity all describe one query; clearing them in separate places
   * is how a screen ends up showing three different answers at once.
   */
  const clearResponseState = useCallback(() => {
    setResults(clearedSearchResults);
    setMetadataOnly(false);
    setMetadataPopulation([]);
    setResponseTruncated(false);
    setPopulations([]);
    setWithheld(undefined);
    p.onEnvelopes([]);
  }, [p.onEnvelopes]);



  const [articleLimit, setArticleLimit] = useState(INITIAL_ARTICLES);
  const [metadataFilter, setMetadataFilter] = useState<{
    query: string; metadata: PublisherMetadata;
  }>();
  const box = useRef<HTMLInputElement>(null);

  useEffect(() => setText(p.state.q ?? ""), [p.state.q]);
  // Desktop keyboard users benefit from landing in the primary control. On a touch device,
  // focusing it on arrival opens the software keyboard before the reader can inspect the page.
  useEffect(() => {
    if (typeof window.matchMedia === "function" && window.matchMedia("(pointer: fine)").matches)
      box.current?.focus();
  }, []);

  const q = p.state.q ?? "";
  // Bound to the exact submitted query. Any other query resolves to the default, so the override
  // cannot outlive the words it was chosen for.
  const fuzzyMode = fuzzyModeFor(exactQuery, q);
  const asOf = p.state.asOf;
  /**
   * The date the request actually carries. It is not `asOf`: with no explicit date the
   * request falls back to today, and today moves. Depending on `asOf` alone meant a
   * rerender after the calendar day changed updated the visible default while the rows,
   * strip, population and limitations stayed on the previous day, and no new request was
   * issued. One value, used by the request and by both dependency lists, so the thing
   * sent and the thing watched cannot drift apart.
   */
  const requestAsOf = asOf ?? p.today;
  const retrieval = p.state.retrieval ?? "keyword";
  const jurisdiction = p.state.jurisdiction ?? "";
  const hierarchy = p.state.hierarchy ?? "";
  const domain = p.state.domain ?? "";
  const sourceClass = p.state.sourceClass ?? "";
  const actForm = p.state.actForm ?? "";
  const bindingStatus = p.state.bindingStatus ?? "";
  const language = p.state.language ?? "";
  // Bind the component-memory filter to the exact submitted query that produced its server row.
  // A new query drops it synchronously, without putting an opaque publisher URI in URL state.
  const activeMetadata = retainedForQuery(metadataFilter, q)?.metadata;
  const metadataArguments = activeMetadata
    ? publisherMetadataFilterArguments(activeMetadata)
    : undefined;
  const metadataIdentifier = metadataArguments?.publisher_metadata_identifier;

  /**
   * The request generation. Every response carries the generation it was asked under, and
   * only the current generation may write. A boolean captured per effect run could not do
   * this job: it is flipped by passive cleanup, which runs after the next paint, so a
   * response arriving in that interval was still live and still allowed to write.
   */
  const generation = useRef(0);

  /**
   * The state transition, before paint.
   *
   * Clearing in a passive effect left a committed frame in which the request arguments had
   * already changed and the previous answer was still on screen, so a reader saw rows,
   * limitations and a denominator attributed to a date, a retrieval mode or a scope filter
   * they had already left. Changing the question remounts and needs none of this; changing
   * anything else about the request does not, which is the case this covers.
   *
   * Cleanup advances the generation too, so unmounting invalidates an outstanding request
   * rather than leaving it able to write into a component that no longer exists.
   */
  useLayoutEffect(() => {
    generation.current += 1;
    clearResponseState();
    if (!q.trim()) { setBusy(false); return; }
    setArticleLimit(INITIAL_ARTICLES);
    setBusy(true);
    return () => { generation.current += 1; };
  }, [q, requestAsOf, retrieval, jurisdiction, hierarchy, domain, sourceClass, actForm,
      bindingStatus, language, metadataIdentifier, fuzzyMode, clearResponseState]);

  useEffect(() => {
    if (!q.trim()) return;
    // Read after the layout effect above has advanced it, so this is the generation this
    // request belongs to.
    const mine = generation.current;
    const live = () => mine === generation.current;
    tool<any>("search", { query: q.trim(), limit: 40, time_scope: "as_of", as_of: requestAsOf,
                          retrieval_mode: retrieval, fuzzy: fuzzyMode,
                          ...(jurisdiction ? { jurisdiction } : {}),
                          ...(hierarchy ? { hierarchy } : {}), ...(domain ? { domain } : {}),
                          ...(actForm ? { act_form: actForm } : {}),
                          ...(bindingStatus ? { binding_status: bindingStatus } : {}),
                          ...(language ? { language } : {}),
                          ...(sourceClass ? { source_class: sourceClass } : {}),
                          ...(metadataArguments ?? {}) })
      .then((res) => {
        if (!live()) return;
        // ONE PARSE, and it comes FIRST. Rows, the index strip, the denominator, the
        // withholding disclosure, the retrieval mode, the expansions and the absence
        // state are seven views of this single typed result. This used to be three
        // separate walks over `res` in three consecutive statements, and the comment that
        // stood here promised the opposite of what the code did: it said one normalized
        // set fed rows, the denominator and absence authority, above code where a footer
        // could describe a publisher whose rows had been withheld one statement later.
        // The strip was the last raw walk and the worst of them, because it published a
        // build date and a valid-signature badge for units this parse had rejected (O1).
        // There is nothing left to disagree with.
        const parsed = parseGovernedResponse("search", res);
        p.onEnvelopes(envelopeStripRows(parsed));
        // Read off the raw response, deliberately: the decision needs the authoritative
        // population, and everything below this line narrows it for display.
        const decision = metadataOnlyFromResponse(res);
        setMetadataOnly(decision.metadataOnly);
        setMetadataPopulation(decision.population);
        setResponseTruncated(anyRowSetTruncated(res));
        const answer = normalizeSearchResponse(parsed);
        setPopulations(answer.populations);
        // Typed causes, carried rather than merged: the sentence a reader is shown has to
        // be the one the parse established for that publisher (O3).
        setWithheld(answer.complete ? undefined : answer.withheld);
        // Round 4 (O3/O4): the ONE production projector reads the parse closed, derives
        // mode and expansion facts from the validated ran units only, and types the
        // absence state; the callback below is presentation mapping, not decision.
        setResults(projectSearchResponse<WorkHit, ArticleHit>(
          parsed,
          (ranUnits) => {
          // Adapted from the parsed units, never re-read off the response. Publisher,
          // jurisdiction, timeline semantics and rows all come from the one parse, so a
          // hit can no longer reach fusion carrying an identity the table refused.
          const hits = fusePublisherHits<any>(ranUnits.map((unit) => ({
            envelope: {
              publisher: unit.publisher,
              jurisdiction: unit.jurisdiction,
              timeline_semantics: unit.timelineSemantics,
            },
            hits: unit.rows,
          })));
          // The same hits answer two different questions, so they are split rather than
          // ranked together: "which law is this" and "where is this said".
          const byWork = new Map<string, WorkHit>();
          const arts: ArticleHit[] = [];
          for (const h of hits) {
            const work = String(h.lex_id ?? "").split(":").slice(0, 2).join(":");
            if (!work) continue;
            const title = shorten(h.title) ?? work;
            const meta: HitMeta = {
              jurisdiction: h._jurisdiction ?? jurisdictionForPublisher(work.split(":")[0]),
              timelineSemantics: h._timelineSemantics,
              validFrom: h.valid_from, validTo: h.valid_to, hierarchy: h.hierarchy,
              language: h.language, domains: Array.isArray(h.domains) ? h.domains : [],
              consolidationStatus: h.consolidation_status,
              matchReasons: Array.isArray(h.match_reasons) ? h.match_reasons : [],
              publisherMetadata: parsePublisherMetadata(h.matched_publisher_metadata),
            };
            const existing = byWork.get(work);
            if (!existing) byWork.set(work, { work, title, ...meta });
            else if (!existing.publisherMetadata && meta.publisherMetadata)
              byWork.set(work, { ...existing, publisherMetadata: meta.publisherMetadata });
            if (h.anchor)
              arts.push({ work, title, anchor: h.anchor, num: h.provision_num,
                          snippet: h.snippet, ...meta, validFrom: String(h.valid_from) });
          }
          const visibleWorks = [...byWork.values()].slice(0, 8);
          const visibleWorkIds = new Set(visibleWorks.map((work) => work.work));
          // Passages explain why one of the visible laws matched; they never introduce a
          // ninth law after the work cap.
          return {
            works: visibleWorks,
            articles: arts.filter((article) => visibleWorkIds.has(article.work)).slice(0, 25),
            ranHitCount: hits.length,
          };
        }));
      })
      .catch(() => {
        if (live()) {
          setResults(searchResultsFromError("Search could not be reached. Try again."));
          setMetadataOnly(false);
          setMetadataPopulation([]);
          setResponseTruncated(false);
        }
      })
      .finally(() => { if (live()) setBusy(false); });
  }, [q, requestAsOf, retrieval, jurisdiction, hierarchy, domain, sourceClass, actForm, bindingStatus,
      language, metadataIdentifier, fuzzyMode, clearResponseState]);

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    p.onSubmit(searchSubmission(text));
  };

  // has_results and partial_results both mean rows rendered, so neither can reach the empty
  // branch; mapping them to no_match keeps the presentation total without widening its type.
  const emptyPresentation = searchEmptyPresentation(
    results.absence === "has_results" || results.absence === "partial_results"
      ? "no_match" : results.absence);
  /**
   * Whether the response fell short of the scope the reader selected, from any cause.
   *
   * Deriving this from `withheld` alone was too narrow: that is only the case this module
   * detects. The projector independently classifies a malformed or unattributable sibling as
   * partial or incomplete, and in that state the unqualified sentence claims the whole selected
   * scope was searched while the notice beside it says the response was not coherent. The
   * denominator has to answer to the final authority, not to the half of it this file owns.
   */
  const authorityIncomplete = withheld !== undefined
    || results.absence === "partial_results"
    || results.absence === "incomplete_response"
    || results.absence === "mixed_no_match";
  const groupedResults = groupSearchResults(works, articles);
  const visiblePassages = new Set(articles.slice(0, articleLimit));
  const resultLawCount = groupedResults.reduce((count, section) => count + section.works.length, 0);
  const laws = `${resultLawCount} law${resultLawCount === 1 ? "" : "s"}`;
  const passages =
    `${articles.length} matching passage${articles.length === 1 ? "" : "s"}`;
  // The same authority the denominator answers to. Testing only the withholding this file
  // detects left a valid publisher beside an invalid sibling rendering a bare count under a
  // partial notice, which is the claim the comment above already said was not authorized.
  const countedResults = error !== undefined || authorityIncomplete
    ? `Showing ${laws}, ${passages}`
    : `${laws}, ${passages}`;

  return (
    <section className="finder" aria-label="Search the corpus">
      <form className="one" onSubmit={submit}>
        <input ref={box} name="query" className="onebox" value={text} onChange={(e) => setText(e.target.value)}
               placeholder="A law, an identifier, or words in the text"
               aria-label="Search for a law, an identifier, or words in the text" />
        {/* The date sits beside the question, not behind a tab, because every question this
            corpus answers has an "as of when" and that is the entire point of it. */}
        <label className="asof" title="Select the publisher state covering this date">
          <i>as of</i>
          <input name="as-of" type="date" value={asOf ?? ""} max="2030-12-31"
                 aria-label="Select the publisher state covering this date"
                 onChange={(e) => p.onAsOf(e.target.value || undefined)} />
        </label>
        <button type="submit">Search</button>
      </form>

      <div className="onehint">
        {asOf
          ? <>Searching publisher states covering <b>{asOf}</b>. <button className="linky" onClick={() => p.onAsOf(undefined)}>use today instead</button></>
          : <>Reading the corpus as it stands today. Set a date to read it as it was.</>}
      </div>

      {/* This row is always here. It used to appear only on an empty box, which meant that the
          moment you searched for anything, the only route to the report disappeared with it. */}
      <div className="doors">
        {q ? (
          <button className="door" onClick={() => { setText(""); p.onSubmit({ query: "" }); }}>clear</button>
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
          <ScopeFilters values={p.state} onChange={p.onRefine} summary="Narrow the legal scope" />
        </div>
      ) : null}

      {q ? (
        <div className="results">
          <div className="res-head">
            {/* A bare count is a claim about the answer. It is only that when the response
                was authoritative and complete: after a transport error, or when a
                publisher's rows were withheld, "0 laws" reads as an absence the response
                cannot support. In those states the header says only what is on screen. */}
            <span className="sub">{busy ? "Searching…" : countedResults}</span>
            {/* An unknown mode is not an unavailable mode: in flight and after a transport
                error the badge states nothing rather than a false capability claim. */}
            {results.modeUsed === undefined
              ? (results.modeUnavailable
                  ? <span className="badge">meaning unavailable</span>
                  : null)
              : <span className="badge">{results.modeUsed === "hybrid"
                  ? "words + meaning" : "exact words"}</span>}
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

          {retrieval === "hybrid" && modeUnavailable ? (
            <p className="mode-note" role="status">{modeUnavailable}</p>
          ) : retrieval === "hybrid" ? (
            <p className="mode-note">Concept search across French and English. It can take a few seconds;
              identifiers and quoted legal text still use exact lookup.</p>
          ) : null}

          <ScopeFilters values={p.state} onChange={p.onRefine} />

          <PublisherLimitations items={limitations} tool="search" />
          {/* Rows rendered, but a sibling response was unusable: say so rather than
              letting an incomplete answer look complete (PR293 review, O1). */}
          <PartialResponseNotice partial={results.absence === "partial_results"} />

          {activeMetadata ? (
            <div className="metadata-filter" role="status">
              <span>Filtering by {publisherMetadataCaption(activeMetadata.kind)}: <b>{activeMetadata.displayLabel}</b></span>
              <button type="button" className="linky" onClick={() => setMetadataFilter(undefined)}>
                clear publisher filter
              </button>
            </div>
          ) : null}

          {expansions.length > 0 && fuzzyMode === "auto" ? (
            <div className="sub expansion" role="note" aria-label="Query interpretation"
                 data-testid="interpretation-notice">
              <p>Spelling fallback tried: {expansions.join(", ")}</p>
              <button type="button" className="ghost" data-testid="relaxation-revert"
                      onClick={() => {
                        // Clear before the rerun, not after it: rows, limitations and the
                        // denominator all describe the relaxed query, and leaving them on screen
                        // during the refetch shows results attributed to a query no longer running.
                        clearResponseState();
                        setExactQuery(q.trim());
                      }}>
                Search these exact words instead
              </button>
            </div>
          ) : null}
          {fuzzyMode === "off" ? (
            <p className="sub expansion" data-testid="exact-words-notice">
              Searching these exact words. No spelling fallback was applied.{" "}
              <button type="button" className="linklike" data-testid="relaxation-restore"
                      onClick={() => {
                        // Both directions of this control clear, not just one. Restoring the
                        // fallback reruns the query exactly as reverting to exact words does,
                        // so leaving the exact-words rows and denominator on screen during the
                        // rerun would attribute them to a search that is no longer running.
                        clearResponseState();
                        setExactQuery(undefined);
                      }}>
                Allow spelling fallback again
              </button>
            </p>
          ) : null}

          {busy && works.length === 0 && articles.length === 0 ? <ResultsSkeleton /> : null}

          {error ? <div className="empty"><p>{error}</p></div> : null}

          {/*
            * The whole response matched only in metadata, so the records are disclosed and none
            * of them is presented as an answer. Decided on the raw envelopes before fusion, the
            * display cap and the passage filter, so it describes the response rather than what
            * survived to the screen. The results below are suppressed rather than shown beneath
            * it, because a record match rendered as a hit IS the claim this notice refuses.
            */}
          {metadataOnly
            ? <MetadataOnlyNotice works={metadataPopulation} truncated={responseTruncated} />
            : null}

          {metadataOnly ? null : groupedResults.map((section) => (
            <section className="res-jurisdiction" key={section.jurisdiction}>
              <h4 className="res-h">
                {jurisdictionLabel(section.jurisdiction)}
                <small>{section.works.length} law{section.works.length === 1 ? "" : "s"}</small>
              </h4>
              <div className="res-worklist">
                {section.works.map((w) => {
                  const passages = w.passages.filter((passage) => visiblePassages.has(passage));
                  return (
                    <article className="res-work" key={w.work}>
                      <button className="rowbtn res-work-head" onClick={() => p.onOpen(w.work, asOf)}>
                        <span>{w.title}</span>
                        <span className="hitmeta">
                          <span className="mono">{w.work.split(":")[1]}</span>
                          <Validity hit={w} />
                          <HitContext hit={w} />
                          {w.consolidationStatus === "not_published"
                            ? <span className="warntext">official merged wording not published</span> : null}
                        </span>
                      </button>
                      <PublisherMetadataContext
                        metadata={w.publisherMetadata}
                        activeIdentifier={metadataIdentifier}
                        onFilter={(metadata) => setMetadataFilter(metadata
                          ? { query: q, metadata }
                          : undefined)} />
                      {passages.length > 0 ? (
                        <div className="res-passages">
                          <div className="res-passages-label">Matching passages</div>
                          <ul className="rows">
                            {passages.map((a, i) => (
                              <li key={`${a.work}-${a.anchor}-${i}`}>
                                <button className="rowbtn" onClick={() => p.onOpen(a.work, a.validFrom, a.anchor)}>
                                  <span>{a.num ?? a.anchor}</span>
                                  {a.snippet ? <span className="sub"><Marked text={a.snippet} /></span> : null}
                                  <span className="hitmeta"><Validity hit={a} /><HitContext hit={a} />
                                    {a.language ? <span>{a.language.toUpperCase()}</span> : null}</span>
                                </button>
                                <PublisherMetadataContext
                                  metadata={a.publisherMetadata}
                                  activeIdentifier={metadataIdentifier}
                                  onFilter={(metadata) => setMetadataFilter(metadata
                                    ? { query: q, metadata }
                                    : undefined)} />
                              </li>
                            ))}
                          </ul>
                        </div>
                      ) : null}
                    </article>
                  );
                })}
              </div>
            </section>
          ))}

          {articles.length > articleLimit ? (
            <button className="ghost more-results"
                    onClick={() => setArticleLimit((current) => Math.min(articles.length, current + 8))}>
              Show {Math.min(8, articles.length - articleLimit)} more matching passages
            </button>
          ) : null}

          {!busy && !error && withheld !== undefined ? (
            // Disclosed, never silent. A publisher whose population authority is void loses its
            // rows along with its denominator, so this response is narrower than the scope the
            // reader asked for, and nothing left in it can support an absence claim.
            <div className="sub expansion" role="note" data-testid="withholding-notice">
              <p>{withholdingSentence(withheld)}</p>
            </div>
          ) : null}

          {!busy && !error && withheld === undefined
           && works.length === 0 && articles.length === 0 ? (
            // The empty sentence is a typed truth claim scoped by searchEmptyPresentation
            // (review round 2, O1): corpus-wide only when every publisher ran; scoped to the
            // publishers that ran when one refused; coverage-only when all refused. It is
            // withdrawn entirely when anything was withheld, because a narrowed response cannot
            // support a claim about the scope the reader actually asked about.
            <div className="empty" data-search-empty={emptyPresentation.kind}>
              <p>{emptyPresentation.sentence}</p>
              {allRefused ? (
                <p className="sub">{LIMITATION_EXPLANATION}{" "}
                  <a href="/coverage">What Lex holds, and lacks →</a></p>
              ) : (
                <p className="sub">
                  Search reads the versions that carry text. Lex also holds dated versions whose
                  wording the publisher never issued, and those can be dated but not searched.{" "}
                  <a href="/coverage">What Lex holds, and lacks →</a>
                </p>
              )}
            </div>
          ) : null}

          {/* Rule 6: the population behind the list, the zero, or the refusal alike. Rendered
              from validated envelopes only, so an invalid sibling contributes nothing. */}
          {!busy && !error
            ? <PopulationFooter rows={populations} incomplete={authorityIncomplete} />
            : null}
        </div>
      ) : null}
    </section>
  );
}

function Validity({ hit }: { hit: HitMeta & { work: string } }) {
  if (!hit.validFrom) return null;
  return <span>{intervalLabel(hit.work, hit.validFrom, hit.validTo, hit.timelineSemantics)}</span>;
}

function HitContext({ hit }: { hit: HitMeta }) {
  const reasons = (hit.matchReasons ?? [])
    .filter((reason) => !hit.publisherMetadata
      || reason !== "work_metadata" && !reason.endsWith("_publisher_short_title"))
    .map((reason) => reason === "semantic" ? "meaning match" :
      reason === "exact_identifier" ? "exact identifier" : reason === "fuzzy" ? "spelling match" :
      reason === "keyword" ? "word match" : label(reason));
  return <>
    {hit.hierarchy ? <span>{label(hit.hierarchy)}</span> : null}
    {(hit.domains ?? []).slice(0, 2).map((domain) => <span key={domain}>{label(domain)}</span>)}
    {reasons.map((reason) => <span key={reason}>{reason}</span>)}
  </>;
}

function PublisherMetadataContext({ metadata, activeIdentifier, onFilter }: {
  metadata?: PublisherMetadata;
  activeIdentifier?: string;
  onFilter: (metadata?: PublisherMetadata) => void;
}) {
  if (!metadata) return null;
  const filter = publisherMetadataFilterArguments(metadata);
  const active = filter?.publisher_metadata_identifier === activeIdentifier;
  const source = safeHttpsUrl(metadata.sourceUri);
  const caption = publisherMetadataCaption(metadata.kind);
  return (
    <div className="publisher-metadata">
      {filter ? (
        <button type="button" className="publisher-metadata-chip" aria-pressed={active}
                title={`Filter this search by the exact official ${caption}`}
                onClick={() => onFilter(active ? undefined : metadata)}>
          <span>{caption}</span>
          <b>{metadata.displayLabel}</b>
        </button>
      ) : (
        <span className="publisher-metadata-chip contextual">
          <span>{caption}</span>
          <b>{metadata.displayLabel}</b>
        </span>
      )}
      {source ? (
        <a className="publisher-metadata-source" href={source} target="_blank"
           rel="noopener noreferrer">publisher source ↗</a>
      ) : (
        <span className="publisher-metadata-source" title={metadata.sourceUri}>
          publisher source URI
        </span>
      )}
    </div>
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
