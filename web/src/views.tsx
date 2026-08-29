import { useEffect, useLayoutEffect, useRef, useState } from "react";
import {
  boundedPublisherTextLabel, changeCountLabels, isTypedProvisionGap, populationCoverageLabel,
  populationScopeLabel, provisionCountLabel, provisionSourceUrl, safeHttpsUrl,
  signatureStatusLabel, typedProvisionGapLabel,
  type ProvisionItem, type RankingRow, type UiEffect,
} from "./api";
import { facetLabel, jurisdictionLabel } from "./facets";
import { indexFreshnessLabel, type EnvelopeStripRow } from "./envelopeStrip";
import { populationExclusions, queriedDenominator, unqueriedPopulations,
  type PublisherPopulation } from "./searchPopulation";
import { publisherOf, workSlug } from "./state";
import { shorten } from "./pickers";
import { assistantTimelineRows } from "./assistantShell";
import { EvidenceActions } from "./EvidenceActions";
import { citationText, evidenceFilename, lawEvidenceMarkdown } from "./export";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { remarkLegalText } from "./legalText.ts";
import {
  futureStateLabel, intervalLabel, usesPublisherVersionDates,
} from "./temporal";
import { extractionDisclosure } from "./extractionProfile";
import { HISTORICAL_DENSITY, historicalDensityApplies } from "./notices";
import { gapBadgeStatus, LIMITATION_EXPLANATION, limitationsFromEffect,
  conflictedPublishersSentence, PARTIAL_RESPONSE_SENTENCE, scopedLimitations,
  type PublisherLimitation } from "./limitations";

const permalink = (work: string, date: string, anchor?: string) =>
  `/${publisherOf(work)}/${workSlug(work)}/${date}${anchor ? `#${anchor}` : ""}`;

const ms = (d: string) => Date.parse(`${d}T00:00:00Z`);

/**
 * The reader: the law's structure on the left, its text on the right.
 *
 * The outline used to appear only for laws too large to render, and vanished the moment you
 * opened an article — so re-dating dropped you at the top of a document you were reading the
 * middle of. It is the one control a point-in-time reader uses constantly, so it stays put.
 */
export function Provision({ items, toc, validFrom, validTo, work, title, language, anchor, profile,
  source, textCompleteness, totalProvisions, totalProvisionGaps, truncated, textTruncated,
  timelineSemantics, recordSha256, bodySha256,
  onPick, onClear, onCite }: {
  items: ProvisionItem[]; toc: ProvisionItem[]; validFrom: string; validTo?: string;
  work: string; title: string; language?: string; anchor?: string; profile?: string; source?: string;
  textCompleteness?: string; totalProvisions?: number; totalProvisionGaps?: number;
  truncated?: boolean; textTruncated?: boolean; timelineSemantics?: string;
  /** Version-metadata digest. It identifies the version record, not the wording on screen. */
  recordSha256?: string;
  /** Publisher body digest. Covers the publisher body, not the ordered provisions on screen. */
  bodySha256?: string;
  // `auto` marks an article the reader did not ask for. The rail uses it to decide whether to
  // stay on the law's versions or narrow to this article's texts.
  onPick: (anchor: string, auto?: boolean) => void; onClear: () => void; onCite?: (work: string) => void;
}) {
  // Where this text came from. Publisher markup and a read PDF are not the same claim, and the
  // difference has to reach the person reading the words, not stop at a field in a JSON file.
  // Two levels, because the risks differ: pdf-lu profiles read a document that IS this act, so
  // the doubt is only about where an article begins. pdf-memorial-lu profiles had to find this
  // act inside a whole official-gazette issue. Classify by immutable profile family so a new,
  // narrower profile version cannot accidentally lose the disclosure in the reader.
  const disclosure = extractionDisclosure(profile);
  const officialSource = safeHttpsUrl(source);
  const fromPdf = disclosure === "publisher-pdf";
  const fromGazette = disclosure === "gazette";
  const availableItems = items.filter((item) => !isTypedProvisionGap(item));
  const typedGapLabel = typedProvisionGapLabel(items, textCompleteness);
  const outlineOnly = availableItems.length > 0
    && availableItems.every((p) => !p.text && !p.text_omitted);
  const boundedText = boundedPublisherTextLabel(items, textTruncated);
  const nav = toc.length >= 6 || outlineOnly;
  const pageUrl = typeof window === "undefined" ? "" : window.location.href;
  const evidence = () => ({
    title, work, validFrom, validTo, language, source, permalink: pageUrl, timelineSemantics,
    extractionProfile: profile, recordSha256, bodySha256, provisions: items,
    exportedAt: new Date().toISOString(),
  });

  // A code too large to render whole used to open onto an apology with a button beside it. Open
  // the first article instead, so arriving at the Code du travail means arriving at some law.
  // Once per work: clearing the article is a deliberate act, and should give back the contents
  // rather than bounce straight to Article 1 again.
  const opened = useRef<string>();
  useEffect(() => {
    if (!outlineOnly || anchor || toc.length === 0 || opened.current === work) return;
    opened.current = work;
    onPick(toc[0].anchor, true);
  }, [outlineOnly, anchor, work, toc]);
  const body = (
    <div className="text">
      <div className="cnt">
        <span className="tag">{intervalLabel(work, validFrom, validTo, timelineSemantics)}</span>
        {anchor ? (
          <button className="tag act" onClick={onClear}>article {anchor} ✕</button>
        ) : (
          <span className="tag">{provisionCountLabel(items,
            truncated || textTruncated ? totalProvisions : undefined)}</span>
        )}
        {fromPdf ? <span className="tag warn">read from the publisher's PDF</span> : null}
        {fromGazette ? <span className="tag warn">cut from a gazette issue</span> : null}
        {boundedText ? <span className="tag warn">{boundedText}</span> : null}
        {typedGapLabel ? <span className="tag warn">{typedGapLabel}</span> : null}
        {(truncated || textTruncated) && typeof totalProvisionGaps === "number"
          && totalProvisionGaps > 0 ? (
          <span className="tag warn">{totalProvisionGaps.toLocaleString()} coordinate{totalProvisionGaps === 1 ? "" : "s"} without certified text</span>
        ) : null}
        {!outlineOnly && items.length > 0 ? (
          <EvidenceActions citation={citationText(evidence())} markdown={() => lawEvidenceMarkdown(evidence())}
                           filename={evidenceFilename(work, anchor ?? validFrom)} />
        ) : null}
      </div>
      {fromPdf ? (
        <p className="pdfnote">
          The publisher issues no machine-readable XML for this version, so the wording below was
          read from its official PDF. The words are the publisher's; the division into articles is
          ours, inferred from the layout rather than taken from publisher markup. Check anything
          that turns on exact numbering against the source.
        </p>
      ) : null}
      {fromGazette ? (
        <div className="pdfnote strong">
          <p><b>Read this one with more care than the rest.</b></p>
          <p>
            Legilux publishes no machine-readable text for this version, and no separate document
            for it either. What it offers is the issue of the <i>Mémorial</i>, the official gazette,
            that the consolidated text appeared in, which is an entire day's journal and can contain
            several unrelated acts.
          </p>
          <p>
            The extractor verifies the requested act before showing any wording. The words below
            are the publisher's, but finding the relevant section inside the issue and, where the
            document permits it, dividing that section into articles are ours. Those boundaries are
            inferred from layout, so treat this as a reading aid and confirm anything that matters.
          </p>
          {officialSource ? (
            <p>
              <a href={officialSource} target="_blank" rel="noopener noreferrer">
                Read the official gazette at Legilux ↗
              </a>
            </p>
          ) : null}
        </div>
      ) : null}
      {outlineOnly ? (
        // A blank pane next to a table of contents is a dead end. Offer the thing a reader
        // opening a code actually wants: the first article, one click away.
        <div className="empty">
          <p>{toc.length.toLocaleString()} articles, too many to render at once.
             Pick one from the contents, or start at the beginning.</p>
          {toc.length > 0 ? (
            <button className="chip" onClick={() => onPick(toc[0].anchor)}>
              Read from {toc[0].num ?? toc[0].anchor} →
            </button>
          ) : null}
        </div>
      ) : items.map((p) => {
        const exactTextUrl = provisionSourceUrl(p);
        const hasAnchor = p.anchor.length > 0;
        const typedGap = isTypedProvisionGap(p);
        return (
        <article key={p.anchor || p.permalink || "document-text"} className="art"
                 id={hasAnchor ? p.anchor : undefined}>
          <h4>
            {hasAnchor
              ? <a href={permalink(work, validFrom, p.anchor)}>{p.num ?? p.anchor}</a>
              : <span>Document text</span>}
            {p.heading ? <span className="sub">{hasAnchor ? ", " : " — "}{plain(p.heading)}</span> : null}
          </h4>
          {/* Publisher text never becomes executable markup: react-markdown creates React nodes
              and ignores raw HTML by default. Export and comparison keep the untouched string. */}
          {typedGap ? (
            <div className="pdfnote" role="note">
              <p><strong>Text unavailable.</strong> Lex preserved this publisher coordinate but
                could not certify wording for it (<span className="mono">{p.text_unavailable_reason}</span>).</p>
              {exactTextUrl ? <p><a href={exactTextUrl} target="_blank" rel="noopener noreferrer">
                Open the official publisher source ↗
              </a></p> : null}
            </div>
          ) : p.text_omitted ? (
            <div className="pdfnote">
              <p>This publisher text is held, but it exceeded the bounded API response.</p>
              {exactTextUrl ? <p><a href={exactTextUrl} target="_blank" rel="noopener noreferrer">
                Open the exact publisher text ↗
              </a></p> : null}
            </div>
          ) : (
            <div className="lawtxt"><Markdown remarkPlugins={[remarkGfm, remarkLegalText]}>{p.text}</Markdown></div>
          )}
          {/* The acts this article points at. The publisher writes them into the text and the
              derive step captures them with their ELI target, so they can be followed rather
              than merely read. This is the shape legal research actually has: one rule leads to
              another, and a search box cannot express that. */}
          {p.citations && p.citations.length > 0 ? (
            <div className="cites-out">
              <span className="cites-h">Refers to</span>
              {p.citations.map((c, i) => (
                <button key={i} className="citelink" onClick={() => onCite?.(c.work)}
                        title={c.work}>{c.text ?? c.work}</button>
              ))}
              {/* The list is what fitted, not what exists. Shown beside the references that did
                  arrive, because a list silently cut reads as a complete one. */}
              {p.citations_truncated === true
                ? <span className="cites-h">more not returned in this response</span> : null}
            </div>
          ) : p.citations_truncated === true ? (
            /* The budget cut every reference this provision has. Rendering nothing here would put
               it in the same shape as an article that refers to nothing, which is the one thing
               this response cannot say. The heading stays so the reader sees the same section
               with an honest value in it rather than a silent omission. */
            <div className="cites-out">
              <span className="cites-h">Refers to</span>
              <span className="cites-h">not returned in this response</span>
            </div>
          ) : null}
          {p.text_sha256 ? <div className="sha">sha256 {p.text_sha256.slice(0, 16)}…</div> : null}
        </article>
        );
      })}
    </div>
  );
  if (!nav) return body;
  return (
    <div className="reader">
      <aside className="toccol"><Outline items={toc} current={anchor} onPick={onPick} /></aside>
      {body}
    </div>
  );
}

/**
 * Every version of what is on screen, always visible.
 *
 * Read / History / Compare were three tabs over ONE dimension — one point in time, all of
 * them, two of them — and each tab discarded the others' state. The rail collapses them:
 * click a version to read it, shift-click a second to compare the pair. There is nothing to
 * navigate to, because the history is the navigation. Scope follows the reader: with an
 * article open it shows that article's distinct texts, otherwise the law's own versions.
 */
export function VersionRail({ dates, current, compareTo, scope, today, work, timelineSemantics, partial, onPick, onCompare, onClear }: {
  dates: string[]; current?: string; compareTo?: string; scope: string; today: string; work: string; timelineSemantics?: string; partial?: boolean;
  onPick: (d: string) => void; onCompare: (d: string) => void; onClear: () => void;
}) {
  const box = useRef<HTMLDivElement>(null);
  const [w, setW] = useState(720);
  // Comparing used to be shift-click and nothing else, which meant it did not exist at all on a
  // phone: there is no shift key to hold. Arming the rail from a button gives the same feature a
  // door that a finger can open, and leaves shift-click in place for anyone who already knows it.
  const [arming, setArming] = useState(false);

  useLayoutEffect(() => {
    const el = box.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(([e]) => setW(e.contentRect.width));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  useEffect(() => {
    if (!arming) return;
    const esc = (e: KeyboardEvent) => { if (e.key === "Escape") setArming(false); };
    addEventListener("keydown", esc);
    return () => removeEventListener("keydown", esc);
  }, [arming]);

  // A comparison landing on screen means the job is done; stop asking for a second date.
  useEffect(() => { if (compareTo) setArming(false); }, [compareTo]);

  const i = current ? dates.indexOf(current) : -1;
  const j = compareTo ? dates.indexOf(compareTo) : -1;
  const { xs, width } = layout(dates, w);

  // A rail that scrolls can hold the version you are reading off screen. Centre it whenever it
  // moves, so stepping with the arrows keeps the marker in sight instead of leaving it behind.
  useEffect(() => {
    const el = box.current;
    if (!el || i < 0) return;
    el.scrollTo({ left: Math.max(0, xs[i] - el.clientWidth / 2), behavior: "smooth" });
  }, [i, width]);

  if (dates.length === 0) return null;
  const labels = labelled(dates, xs, width, i, j);
  const ahead = dates.filter((d) => d > today).length;
  const gaps = dates.slice(1).map((d, k) => (ms(d) - ms(dates[k])) / 86400000).sort((a, b) => a - b);
  const median = gaps.length ? Math.round(gaps[Math.floor(gaps.length / 2)]) : 0;
  const [a, b] = i >= 0 && j >= 0 ? [Math.min(xs[i], xs[j]), Math.max(xs[i], xs[j])] : [0, 0];
  const pick = (d: string, shift: boolean) => {
    // In a comparison, a tick is the other end of it. Leaving the comparison to read one version
    // is what the ✕ is for, and losing the pair on a stray tap was the worse failure.
    if (shift || arming || compareTo) { if (d !== current) onCompare(d); return; }
    onPick(d);
  };

  return (
    <div className="railbox">
      <div className="railhead">
        {/* The count is a claim about the law until it is qualified. "12 versions" says the
            law has twelve; when the response returned a page of them it has more, and the
            rail was the last place a reader would look for that. Only an explicit true
            qualifies it, so an unvalidated value cannot silently withdraw the claim. */}
        <span className="tag">{dates.length} {scope}
          {partial === true ? " returned in this response" : ""}</span>
        {median > 0 ? <span className="tag">every {median} days (median)</span> : null}
        {ahead > 0 ? <span className="tag warn">{futureStateLabel(work, ahead, timelineSemantics)}</span> : null}
        {/* Keyed off compareTo, not off finding it on the rail: a compared date need not be one
            of these ticks (article texts are a subset of the law's versions), and a comparison
            with no visible way out is a trap. */}
        {compareTo ? (
          <>
            <button className="tag act" onClick={onClear}>
              comparing {current && current < compareTo ? current : compareTo} →{" "}
              {current && current < compareTo ? compareTo : current} ✕
            </button>
            <span className="hint">tap any version to move the other end</span>
          </>
        ) : arming ? (
          <span className="hint arm">now pick the version to compare it with</span>
        ) : null}
        <span className="grow" />
        {/* One click, because the question a tick poses is nearly always "what changed HERE",
            meaning against the version before it. Choosing an arbitrary pair is the rarer case and
            keeps its own path: while a comparison is on screen, clicking any tick moves the other
            end, which works with a finger where shift-click never could. */}
        {compareTo ? null : i > 0 ? (
          <button className="stepbtn wide" onClick={() => onCompare(dates[i - 1])}>
            What changed here
          </button>
        ) : (
          <button className={"stepbtn wide" + (arming ? " on" : "")} disabled={dates.length < 2}
                  aria-pressed={arming} onClick={() => setArming((v) => !v)}>
            {arming ? "Cancel" : "Compare"}
          </button>
        )}
        <button className="stepbtn" disabled={i <= 0} aria-label="Previous version"
                onClick={() => onPick(dates[i - 1])}>←</button>
        <button className="stepbtn" disabled={i < 0 || i >= dates.length - 1} aria-label="Next version"
                onClick={() => onPick(dates[i + 1])}>→</button>
      </div>
      {/* The ticks keep a minimum spacing and the rail scrolls when they no longer fit. It used to
          squeeze them into the available width instead, so the laws with the most versions, which
          are exactly the ones worth scrubbing, ended up with targets under 4px wide. */}
      <div className={"rail" + (arming ? " arming" : "")} ref={box}>
        <div className="railtrack" style={{ width }}>
          <div className="axis" />
          {j >= 0 ? <div className="band" style={{ left: a, width: Math.max(2, b - a) }} /> : null}
          {dates.map((d, k) => (
            <button key={d}
                    className={`tick${k === i ? " on" : ""}${k === j ? " cmp" : ""}${d > today ? " future" : ""}`}
                    style={{ left: xs[k] }} title={`${d}${d > today
                      ? `, ${usesPublisherVersionDates(work, timelineSemantics) ? "publisher version dated after today" : "not yet in force"}`
                      : ""}`}
                    tabIndex={labels.has(k) || k === i ? 0 : -1}
                    aria-label={`${d}${k === i ? " (showing)" : ""}`}
                    onClick={(e) => pick(d, e.shiftKey)} />
          ))}
          {[...labels].map((k) => (
            <span key={k} className={`rlbl${k === i ? " on" : ""}`} style={{ left: xs[k] }}>{dates[k]}</span>
          ))}
        </div>
      </div>
    </div>
  );
}

export function Timeline({ view, onOpen }: {
  view: NonNullable<UiEffect["timeline"]>;
  onOpen: (date: string) => void;
}) {
  const rows = assistantTimelineRows({ timeline: view });
  return (
    <section className="evidence-panel" aria-labelledby="timeline-result-title">
      <div className="cnt">
        <span className="tag">{view.total_count.toLocaleString()} dated versions</span>
        {view.truncated ? <span className="tag">showing {view.rows.length}</span> : null}
      </div>
      <h2 id="timeline-result-title">Version history</h2>
      <ol className="rows">
        {rows.map((row) => (
          <li key={row.key}>
            {row.canOpenByDate ? (
              <button className="operation-open" onClick={() => onOpen(row.valid_from)}>
                {row.valid_from}{row.valid_to ? ` to ${row.valid_to}` : " onward"}
              </button>
            ) : (
              <span>{row.valid_from}{row.valid_to ? ` to ${row.valid_to}` : " onward"}</span>
            )}
            {row.language ? <span className="sub">{row.language.toUpperCase()}</span> : null}
          </li>
        ))}
      </ol>
      {view.truncated ? (
        <p className="sub" role="status">
          This result is incomplete. Open the law to load the complete version rail.
        </p>
      ) : null}
    </section>
  );
}

/**
 * Time-proportional, so the rhythm of amendment stays legible — a law rewritten every three
 * weeks looks nothing like one revised twice a century. Then ticks are pushed apart to keep a
 * clickable target each, because proportional alone collapses dense runs into an unhittable smear.
 *
 * MIN is a floor, not a preference. It used to be `Math.min(9, usable / (n - 1))`, which reads as
 * a floor but is the opposite: the denser the law, the smaller the target it produced, and a
 * squeeze pass afterwards pulled everything back inside the box. A 195-version code came out at
 * 3.6px per tick. The rail now overflows and scrolls instead, so scrubbing stays possible however
 * many versions there are.
 */
const MIN = 8;
function layout(dates: string[], width: number): { xs: number[]; width: number } {
  if (dates.length === 0) return { xs: [], width };
  const pad = 12;
  const usable = Math.max(1, width - pad * 2);
  const lo = ms(dates[0]);
  const span = Math.max(1, ms(dates[dates.length - 1]) - lo);
  const xs = dates.map((d) => pad + ((ms(d) - lo) / span) * usable);
  for (let k = 1; k < xs.length; k++) xs[k] = Math.max(xs[k], xs[k - 1] + MIN);
  return { xs, width: Math.max(width, xs[xs.length - 1] + pad) };
}

/** As many date labels as fit without touching; the version on screen always keeps its own. */
function labelled(dates: string[], xs: number[], width: number, cur: number, cmp: number): Set<number> {
  const W = 82;
  const room = Math.max(2, Math.floor(width / W));
  const placed: number[] = [];
  const keep = new Set<number>();
  const fits = (k: number) => placed.every((p) => Math.abs(xs[p] - xs[k]) >= W);
  // Priority: what you are reading, what you are comparing against, then the two ends,
  // then an even spread through whatever room is left.
  const order = [cur, cmp, 0, dates.length - 1];
  for (let k = 0; k < dates.length; k++) order.push(k);
  for (const k of order) {
    if (k < 0 || keep.has(k) || keep.size >= room) continue;
    if (k === cur || fits(k)) { keep.add(k); placed.push(k); }
  }
  return keep;
}

/**
 * What changed in a period.
 *
 * This used to rank everything together and then fold the thematic collections away, which had a
 * perverse effect: a collection restamps whenever anything on its shelf moves, so over a longer
 * window they crowded the top and squeezed the laws out. Six years showed 7 laws where one year
 * showed 16, while the totals above correctly rose from 264 to 860.
 *
 * A collection is simply another source class. Jurisdiction and legal metadata are applied by the
 * server before ranking, and every mixed-corpus row keeps the jurisdiction that owns its work.
 */
export function Ranking({ rows, worksChanged, newVersions, populationWorks, knownExclusions,
                          from, to, page, hasMore, jurisdiction,
                          onOpen, onOpenRecord, onPage }: {
  rows: RankingRow[]; worksChanged: number | undefined; newVersions: number | undefined;
  from: string; to: string;
  populationWorks?: number; knownExclusions?: string[]; jurisdiction?: string;
  page: number; hasMore: boolean;
  onOpen: (work: string, from: string, to: string) => void;
  onOpenRecord: (work: string, date: string) => void;
  onPage: (p: number) => void;
}) {
  const max = Math.max(1, ...rows.map((r) => r.versions_in_period));
  const populationLabel = populationScopeLabel(populationWorks);
  // Phase 0 trust notice (Decision 41): a pre-2017 Luxembourg window counts held states, not
  // legal change. The condition reads only server-provided facts (the echoed window and the
  // rows' own jurisdictions); the browser never infers legal state.
  const density = historicalDensityApplies(from, jurisdiction, rows.map((r) => r.jurisdiction));

  // Scope controls stay above the rows, so a filter that matches nothing never removes its own
  // escape hatch.
  return (
    <>
      {/* One row, not three. The layer's meaning belongs beside its counts, because "820 changed"
          only means anything once you know 820 of what. */}
      <div className="cnt">
        {changeCountLabels(worksChanged, newVersions).map((label) => (
          <span className="tag" key={label}>{label}</span>
        ))}
        {populationLabel ? <span className="tag">{populationLabel}</span> : null}
        <span className="tag mono">{from} → {to}</span>
        <span className="layers-hint">Every selected jurisdiction shares one dated ranking</span>
      </div>
      {knownExclusions && knownExclusions.length > 0
        ? <p className="sub">Known exclusions: {knownExclusions.join(" · ")}</p>
        : null}
      {density ? (
        <div className="trust-notice" role="note" aria-label={HISTORICAL_DENSITY.heading}>
          <b>{HISTORICAL_DENSITY.heading}</b>
          {HISTORICAL_DENSITY.body}
          <div className="actions">
            {HISTORICAL_DENSITY.actions.map((a) => (
              <a key={a.href} href={a.href}
                 {...(a.href.startsWith("https://") ? { rel: "noopener" } : {})}>{a.label}</a>
            ))}
          </div>
        </div>
      ) : null}

      <div className="bars">
        {rows.map((r) => {
          const name = label(r.title) ?? humanSlug(r.work);
          // Where the comparison starts. The server sends the version in force BEFORE the
          // window; without it the row opened first_change against last_change, and those are
          // the same date whenever a law moved once, so the comparison ran a version against
          // itself and reported nothing. A law whose first-ever version falls inside the window
          // has nothing earlier to compare against, so it opens for reading instead.
          const from = r.diff_from ?? r.baseline ?? r.first_change;
          const to = r.diff_to ?? r.last_change;
          const n = r.versions_in_period;
          // A publisher can reissue an act without altering a word, so a row can honestly say
          // "2 new versions" and its comparison honestly say nothing changed. Sending a reader
          // into that comparison makes working software look broken, so the row says it instead
          // and opens for reading. distinct_texts counts wordings, not versions.
          const reissued = r.distinct_texts === 1;
          const comparable = r.text_comparable === true && from !== to && !reissued;
          const why = r.distinct_texts === 0 ? "No text is published for these states, open the dated record"
            : !r.text_comparable ? "Both endpoints do not carry comparable provision text; open the later record"
            : reissued ? `Reissued ${n} time${n === 1 ? "" : "s"} in this window without the wording changing`
            : comparable ? `Compare ${from} with ${to}`
            : "This law's first version falls in this window, so there is nothing earlier to compare it with";
          const state = r.distinct_texts === 0 ? "record only" : !r.text_comparable ? "comparison unavailable"
            : reissued ? "same wording"
            : !comparable ? "first version" : undefined;
          const badge = [r.jurisdiction?.toUpperCase(), state].filter(Boolean).join(" · ");
          const legalContext = [r.jurisdiction ? jurisdictionLabel(r.jurisdiction) : undefined,
                                r.hierarchy ? facetLabel(r.hierarchy) : undefined]
            .filter(Boolean).join(" · ");
          return (
            <button key={r.work} className={"bar" + (r.distinct_texts === 0 ? " notext" : "")}
                    title={legalContext ? `${legalContext}: ${why}` : why}
                    onClick={() => comparable
                      ? onOpen(r.work, from, to)
                      : onOpenRecord(r.work, r.last_change)}>
              <span className="track">
                <span className="fill" style={{ width: `${(n / max) * 100}%` }} />
                <span className="lbl">{name}</span>
                {badge ? <span className="mark">{badge}</span> : null}
              </span>
              {/* "4" beside a comparison showing one edit read as a contradiction. It counts
                  versions the publisher issued, which is not the same as edits to the wording. */}
              <span className="num" title={`${n} new version${n === 1 ? "" : "s"} in this window`}>{n}</span>
            </button>
          );
        })}
      </div>

      {rows.length === 0 ? (
        <Empty>Nothing in this legal scope changed in that window.</Empty>
      ) : null}

      {(page > 0 || hasMore) ? (
        <div className="pager">
          <button className="ghost" disabled={page === 0} onClick={() => onPage(page - 1)}>
            ← previous
          </button>
          <span className="sub mono">{page * 25 + 1}–{page * 25 + rows.length}
            {worksChanged !== undefined ? ` of ${worksChanged.toLocaleString()}` : ""}</span>
          <button className="ghost" disabled={!hasMore} onClick={() => onPage(page + 1)}>
            next →
          </button>
        </div>
      ) : null}
    </>
  );
}

/**
 * The denominator behind a result list, an empty result, or a refusal.
 *
 * Trust rule 6 requires the population behind every list and count, and never-implied rules 3 and
 * 7 forbid reading a zero as an absence of law or implying the search reached beyond what it
 * disclosed. A zero-hit screen with no denominator makes exactly the claim rule 3 forbids.
 *
 * A publisher that did not run the query is shown separately and never added in. Its scope is a
 * real fact about what is mounted, but adding it to a "searched N works" sentence would claim the
 * query covered ground it never touched. When nothing ran at all there is no denominator rather
 * than a zero, because zero asserts that an empty corpus was searched.
 */
export function PopulationFooter(
  { rows, incomplete }: { rows: PublisherPopulation[]; incomplete?: boolean }) {
  if (rows.length === 0) return null;
  // Three states, not two. A missing total means either that no publisher ran the query or
  // that the disclosed scopes cannot be added into one honest number, and the sentence for
  // one is false for the other. Reading the number alone loses that distinction.
  const denominator = queriedDenominator(rows);
  const unqueried = unqueriedPopulations(rows);
  const exclusions = populationExclusions(rows);
  return (
    <div className="population-footer" data-testid="population-footer">
      {denominator.kind === "total"
        ? <p data-testid="population-searched">
            {incomplete
              // Says what the figure covers, never why it is short. A withheld publisher need
              // not have rows on screen, and the withholding may be entirely unattributed, so
              // both "shown above" and "another publisher" can be false. The withholding
              // notice states the cause; this states the scope.
              ? `Searched ${denominator.works.toLocaleString()} works across the publishers `
                + "whose disclosed scope this query could use. That is less than the scope you "
                + "selected."
              : `Searched ${denominator.works.toLocaleString()} works in the selected scope.`}
          </p>
        : denominator.kind === "none_ran"
        ? <p data-testid="population-searched">
            No publisher ran this query, so no works were searched.
          </p>
        : <p data-testid="population-searched">
            The publishers that ran this query disclosed scopes that cannot be added into one
            number, so no total is shown.
          </p>}
      {unqueried.map((r) => (
        <p key={r.publisher ?? "unnamed"} className="sub" data-testid="population-not-queried">
          {r.publisher ?? "One selected publisher"}: {r.population.works_in_scope.toLocaleString()}
          {r.population.scope_filters_applied
            ? " works in the selected scope, not queried."
            : " works mounted before the unsupported filters, not queried."}
        </p>
      ))}
      {exclusions.length > 0
        ? <p className="sub" data-testid="population-exclusions">
            Known exclusions: {exclusions.join(" · ")} <a href="/coverage">See coverage</a>
          </p>
        : null}
    </div>
  );
}

/**
 * The product's signature line: which index answered, how fresh it is, and whether its stamp
 * verified. Trust rule 4 puts this on every data view without exception, and rule 8 forbids
 * implying data is fresher than its build, which is what an undated screen does.
 *
 * One line per mounted publisher, because freshness is a property of each index rather than of
 * the product. The publisher identifier renders verbatim; there is no publisher display name in
 * the envelope, and inventing one would put a word on screen that no response carried.
 */
export function EnvelopeStrip({ rows }: { rows: EnvelopeStripRow[] }) {
  if (rows.length === 0) return null;
  const identities = (r: EnvelopeStripRow) => ([
    ["corpus commit", r.corpusCommit], ["code commit", r.codeCommit],
    ["manifest set", r.manifestSetId], ["content digest", r.contentDigest],
  ] as const).filter(([, v]) => v !== undefined);
  return (
    <details className="envelope-strip" data-testid="envelope-strip">
      <summary>
        {rows.map((r) => (
          <span className="envelope-line" key={r.publisher}>
            <span className="mono">{r.publisher}</span>
            {r.timelineSemantics ? <span>{facetLabel(r.timelineSemantics)}</span> : null}
            <span data-testid="envelope-built-at">{indexFreshnessLabel(r.builtAt)}</span>
            <span>{signatureStatusLabel(r.signatureValid)}</span>
          </span>
        ))}
      </summary>
      {rows.map((r) => (
        <dl className="envelope-identity" key={r.publisher}>
          {identities(r).map(([label, value]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd className="mono">{value}</dd>
            </div>
          ))}
        </dl>
      ))}
    </details>
  );
}

export function InForce({ date, total, rows, populationWorks, populationBasis,
                          populationScopeFiltersApplied, knownExclusions,
                          page, hasMore, onOpen, onPage }: {
  date: string; total: number | undefined;
  populationWorks?: number; populationBasis?: string;
  populationScopeFiltersApplied?: boolean; knownExclusions?: string[];
  rows: { work: string; title?: string; kind?: string; valid_from: string;
          jurisdiction?: string; hierarchy?: string; timeline_semantics?: string;
          /** The publisher exposes several identified versions for this work on this date. */
          ambiguous?: boolean }[];
  page: number; hasMore: boolean;
  onOpen: (work: string, date: string) => void;
  onPage: (page: number) => void;
}) {
  // Trust rule 6: a count of states means nothing without the population it was drawn from.
  const populationLabel = populationCoverageLabel(
    populationWorks, populationBasis, populationScopeFiltersApplied !== false);
  return (
    <>
      <div className="cnt">
        {total !== undefined ? (
          <span className="tag">{total.toLocaleString()} publisher states</span>
        ) : null}
        {populationLabel
          ? <span className="tag" data-testid="in-force-population">{populationLabel}</span>
          : null}
        <span className="tag mono">on {date}</span>
      </div>
      {knownExclusions && knownExclusions.length > 0
        ? <p className="sub" data-testid="in-force-exclusions">
            Known exclusions: {knownExclusions.join(" · ")} <a href="/coverage">See coverage</a>
          </p>
        : null}
      <ul className="rows">
        {rows.map((r) => (
          <li key={r.work}>
            <button className="rowbtn" onClick={() => onOpen(r.work, r.valid_from)}>
              <span>{r.title ?? r.work}</span>
              <span className="hitmeta">
                {/* An ambiguity unit is NOT a determinate version. Rendering it unmarked
                    would assert the publisher identified one, which it did not. */}
                {r.ambiguous
                  ? <span className="tag warn" data-testid="ambiguous-version-row">
                      several identified versions
                    </span>
                  : null}
                {r.jurisdiction ? <span>{jurisdictionLabel(r.jurisdiction)}</span> : null}
                {r.hierarchy ? <span>{facetLabel(r.hierarchy)}</span> : null}
                {r.kind ? <span>{facetLabel(r.kind)}</span> : null}
                <span className="mono">
                  {r.ambiguous
                    ? `choose a version for ${r.valid_from}`
                    : usesPublisherVersionDates(r.work, r.timeline_semantics) ? `publisher version ${r.valid_from}` : `in force since ${r.valid_from}`}
                </span>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {(page > 0 || hasMore) ? (
        <div className="pager">
          <button className="ghost" disabled={page === 0} onClick={() => onPage(page - 1)}>← previous</button>
          <span className="sub mono">{page * 25 + 1}–{page * 25 + rows.length}
            {total !== undefined ? ` of ${total.toLocaleString()}` : ""}</span>
          <button className="ghost" disabled={!hasMore} onClick={() => onPage(page + 1)}>next →</button>
        </div>
      ) : null}
    </>
  );
}

/**
 * A refusal, and what stands behind it.
 *
 * The date-level version of this message was misleading on the works that need it most. Told
 * "no text is held for this law on that date", a reader reasonably tries another date. For
 * Code de l'environnement that is 195 wrong guesses: Legilux publishes the amendment record for
 * these consolidated codes and no text file for any snapshot of them. So when the whole work is
 * textless, say THAT, once, and then show what Lex does hold, because dated versions with sources
 * and hashes are a real answer to a real question, just not to the question about wording.
 */
export function Gap({ status, explanation, available, held, provision_gaps,
  total_provision_gaps, truncated, total_provisions, text_truncated, text_completeness }: {
  status: string; explanation: string; available: string[];
  held?: { text: number; total: number; official?: string; kind?: string };
  provision_gaps?: ProvisionItem[];
  total_provision_gaps?: number;
  truncated?: boolean;
  total_provisions?: number;
  text_truncated?: boolean;
  text_completeness?: string;
}) {
  const whole = held && held.total > 0 && held.text === 0;
  const collection = whole && (held?.kind === "RECUEIL" || held?.kind === "CODE_RECUEIL");
  return (
    <div className="gap">
      {/* Client presentation states are not wire statuses and never wear the badge (O5);
          the decision lives in the tested seam, not in this markup. */}
      {gapBadgeStatus(status) === null ? null
        : <div className="cnt"><span className="tag warn mono">{gapBadgeStatus(status)}</span></div>}
      {whole ? (
        <>
          {collection ? (
            <>
              <p><b>This is a publisher collection, not one legal instrument.</b></p>
              <p className="sub">
                Legilux uses this record as a thematic shelf containing many separate acts. Lex
                keeps its {held!.total.toLocaleString()} dated catalogue states, but will not join
                the shelf's PDFs into invented wording for a single “law”. Search or browse the
                individual instruments in the collection to read and compare authoritative text.
              </p>
            </>
          ) : (
            <>
              <p><b>Lex holds this instrument's publisher record, but no safely extracted wording.</b></p>
              <p className="sub">
                Ingestion checked the official structured text, standalone document PDF and
                gazette fallback available for these {held!.total.toLocaleString()} states. None
                produced article text with boundaries Lex can defend, so it refuses to manufacture
                a citable or comparable version.
              </p>
              <p className="sub">
                The publisher dates, source links and record hashes remain available in the rail.
                Use the official record below for the wording.
              </p>
            </>
          )}
          {held!.official ? (
            <p className="sub">
              <a href={held!.official} target="_blank" rel="noopener noreferrer">Open the publisher record ↗</a>
            </p>
          ) : null}
        </>
      ) : (
        <>
          <p>{explanation}</p>
          {available.length > 0 ? (
            <p className="sub">What does exist: {available.slice(0, 10).join(" · ")}</p>
          ) : null}
        </>
      )}
      {provision_gaps && provision_gaps.length > 0 ? (
        <div className="gap-provisions" aria-label="Publisher coordinates without certified text">
          {(truncated || text_truncated) && typeof total_provisions === "number"
            && total_provisions > provision_gaps.length ? (
            <p className="sub">Showing {provision_gaps.length.toLocaleString()} of {total_provisions.toLocaleString()} publisher coordinates in this bounded response.</p>
          ) : truncated && typeof total_provision_gaps === "number"
            && total_provision_gaps > provision_gaps.length ? (
            <p className="sub">Showing {provision_gaps.length.toLocaleString()} of {total_provision_gaps.toLocaleString()} publisher coordinates without certified text.</p>
          ) : null}
          {text_truncated ? <p className="sub">Some held publisher text was omitted from this bounded response.</p> : null}
          {typedProvisionGapLabel(provision_gaps, text_completeness) === "partial publisher text"
            ? <p className="sub">Other publisher coordinates in this state have certified text.</p>
            : null}
          {provision_gaps.map((item) => {
            const source = provisionSourceUrl(item);
            return (
              <article className="art" id={item.anchor || undefined}
                       key={`${item.document_order ?? "gap"}:${item.anchor}`}>
                <h4>{item.num ?? item.anchor}
                  {item.heading ? <span className="sub">, {plain(item.heading)}</span> : null}
                </h4>
                <div className="pdfnote" role="note">
                  <p><strong>Text unavailable.</strong> Lex preserved this publisher coordinate
                    but could not certify wording for it (<span className="mono">
                      {item.text_unavailable_reason}
                    </span>).</p>
                  {source ? <p><a href={source} target="_blank" rel="noopener noreferrer">
                    Open the official publisher source ↗
                  </a></p> : null}
                </div>
              </article>
            );
          })}
        </div>
      ) : null}
      <p className="sub">
        <a href="/coverage">See exactly what Lex holds and what it lacks →</a>
      </p>
    </div>
  );
}

export function Empty({ children }: { children: React.ReactNode }) {
  return <div className="empty">{children}</div>;
}

export const hasView = (ui?: UiEffect) =>
  !!(ui && (ui.provision || ui.diff || ui.history || ui.timeline || ui.ranking || ui.in_force
    || ui.cited_by || ui.coverage || ui.verification || ui.gap
    // A partial result whose only additive disclosure is a capability limitation is still a
    // result; discarding it would silently hide the one publisher that answered honestly.
    || limitationsFromEffect(ui.publisher_limitations).length > 0));

/**
 * Typed publisher capability limitations, rendered beside supported rows, never instead of
 * them. Information, not error: the reader keeps the answer and learns exactly which publisher
 * did not run the query and for which governed filters. Input is validated fail closed by the
 * caller or here; malformed entries never render and never suppress the primary view.
 */
/**
 * Disclosed beside verified rows when a sibling publisher response could not be read
 * (PR293 review, O1). Round 6 computed this state and rendered nothing, so an incomplete
 * answer presented itself as the complete holding.
 */
export function PartialResponseNotice(
  { partial, conflicted }: { partial?: boolean; conflicted?: string[] }) {
  if (!partial) return null;
  // Named when they can be named. A withheld publisher is not merely missing: it sent more than
  // one answer for this query, so every claim it made was withheld, and a reader deciding
  // whether this answer covers what they care about needs to know which publisher that was.
  //
  // This comment used to say the publisher contradicted itself. That stopped being true when a
  // duplicate became incoherent even where the two units agree, and it outlived the copy it was
  // written to explain by one commit. The sentence itself is built and validated below.
  const names = conflictedPublishersSentence(conflicted ?? []);
  return (
    <div className="trust-notice" role="note" data-testid="partial-response-notice"
         aria-label="Incomplete response">
      <b>These results are incomplete</b>
      <p className="sub">{PARTIAL_RESPONSE_SENTENCE}</p>
      {names !== undefined ? (
        <p className="sub" data-testid="conflicted-publishers">{names}</p>
      ) : null}
    </div>
  );
}

export function PublisherLimitations({ items, tool }: {
  items: PublisherLimitation[];
  /**
   * The operation this surface renders (round 4, O6). When given, only limitations carrying
   * this tool render here; when absent the surface is multi-tool and every row labels its
   * operation visibly, so a search limitation can never masquerade as in-force evidence.
   */
  tool?: string;
}) {
  const scoped = scopedLimitations(items, tool);
  if (scoped.length === 0) return null;
  return (
    <div className="trust-notice" role="note" aria-label="Publisher limitation">
      <b>Some publishers did not run this query</b>
      {scoped.map((item, index) => (
        <p key={index} className="limitation-row">
          {(item.publisher ?? item.jurisdiction ?? "One selected publisher")}
          {": the filter"}{item.unsupported_filters.length > 1 ? "s" : ""}{" "}
          <code>{item.unsupported_filters.join(", ")}</code>
          {" "}{item.unsupported_filters.length > 1 ? "are" : "is"} not described by its index
          for this scope.
          {tool === undefined
            ? <span className="sub mono"> ({item.tool})</span>
            : null}
        </p>
      ))}
      <p className="limitation-row sub">{LIMITATION_EXPLANATION}</p>
    </div>
  );
}

export function EvidenceCoordinates({ ui }: { ui: UiEffect }) {
  const evidence = ui.provision?.evidence ?? ui.diff?.evidence ?? ui.history?.evidence
    ?? ui.timeline?.evidence ?? ui.ranking?.evidence ?? ui.in_force?.evidence
    ?? ui.cited_by?.evidence ?? ui.coverage?.evidence ?? ui.verification?.evidence
    ?? ui.gap?.evidence ?? ui.workspace?.evidence ?? [];
  if (evidence.length === 0) return null;
  const publishers = [...new Set(evidence.map(item => item.publisher).filter(Boolean))];
  const semantics = [...new Set(evidence.map(item => item.timeline_semantics).filter(Boolean))];
  const provisional = evidence.some(item => item.provisional);
  const verificationFailed = evidence.some(item => item.signature_valid === false);
  return (
    <aside className={"evidence-coordinates" + (provisional || verificationFailed ? " warn" : "")}
           aria-label="Evidence coordinates">
      {publishers.length > 0 ? <span>{publishers.join(" + ")}</span> : null}
      {semantics.map(value => <span key={value}>{value === "official_consolidation_state"
        ? "official publisher wording states"
        : value === "publisher_applicability" ? "publisher applicability dates" : value}</span>)}
      {provisional ? <strong>Provisional future-dated publisher state</strong> : null}
      {verificationFailed ? <strong>Signature verification failed</strong> : null}
    </aside>
  );
}

export function CoveragePanel({ view }: { view: NonNullable<UiEffect["coverage"]> }) {
  const works = view.publishers.reduce((total, item) => total + item.works, 0);
  const versions = view.publishers.reduce((total, item) => total + item.versions, 0);
  return (
    <section className="evidence-panel" aria-labelledby="coverage-result-title">
      <div className="cnt">
        <span className="tag">{works.toLocaleString()} works</span>
        <span className="tag">{versions.toLocaleString()} versions</span>
      </div>
      <h2 id="coverage-result-title">Mounted legal coverage</h2>
      <ul className="rows">
        {view.publishers.map((publisher) => (
          <li key={publisher.publisher}>
            <div className="evidence-row">
              <b>{publisher.name ?? publisher.publisher}</b>
              <span className="hitmeta">
                <span>{publisher.works.toLocaleString()} works</span>
                <span>{publisher.versions.toLocaleString()} versions</span>
                <span>{publisher.versions_with_text.toLocaleString()} with text</span>
                <span>{signatureStatusLabel(publisher.signature_valid)}</span>
              </span>
              {publisher.known_gaps.length > 0
                ? <p className="sub">Known gaps: {publisher.known_gaps.join(" · ")}</p>
                : null}
            </div>
          </li>
        ))}
      </ul>
      <p className="sub"><a href="/coverage">Open the complete coverage report →</a></p>
    </section>
  );
}

export function VerificationPanel({ view }: { view: NonNullable<UiEffect["verification"]> }) {
  const source = safeHttpsUrl(view.source_uri);
  return (
    <section className="evidence-panel" aria-labelledby="verification-result-title">
      <div className="cnt">
        <span className={`tag ${view.signature_valid === true ? "" : "warn"}`}>
          {signatureStatusLabel(view.signature_valid)}
        </span>
        {view.algorithm ? <span className="tag mono">{view.algorithm}</span> : null}
      </div>
      <h2 id="verification-result-title">Artifact proof</h2>
      <p><b>{view.title ?? view.lex_id}</b></p>
      <dl className="proof-list">
        <dt>Lex ID</dt><dd className="mono">{view.lex_id}</dd>
        {view.record_sha256 ? <><dt>Record SHA-256</dt><dd className="mono">{view.record_sha256}</dd></> : null}
        {view.body_sha256 ? <><dt>Body SHA-256</dt><dd className="mono">{view.body_sha256}</dd></> : null}
      </dl>
      {source ? <p className="sub"><a href={source} target="_blank" rel="noopener noreferrer">Open the official source ↗</a></p> : null}
    </section>
  );
}

/**
 * Which articles point at one law.
 *
 * The publisher writes its cross-references into the text and the derive step captures them with
 * their ELI target, so this direction is a lookup rather than a search. It is also the question a
 * search box structurally cannot answer: "what depends on this law" is about the edges of a graph,
 * not about the words in a document.
 */
export function CitedBy({ view, onOpen }: {
  view: NonNullable<UiEffect["cited_by"]>;
  onOpen: (work: string, date: string, anchor?: string) => void;
}) {
  return (
    <>
      <div className="cnt">
        {/* "N articles refer to it" is a claim about the law. When the response was cut, N is
            what fitted (McpCore sets citing_articles to the returned hits), so the same
            sentence would understate the total and, at zero, assert an absence. Identity
            comparison only: an absent or malformed receipt is not a complete answer. */}
        <span className="tag">{view.rows_truncated === false
          ? `${view.citing_articles.toLocaleString()} article${view.citing_articles === 1 ? "" : "s"} refer to it`
          : `${view.citing_articles.toLocaleString()} returned in this response`}</span>
        <span className="tag mono">{view.cited_work}</span>
      </div>
      <ul className="rows">
        {view.rows.map((r, i) => (
          <li key={`${r.work}-${r.anchor}-${i}`}>
            <button className="rowbtn" onClick={() => onOpen(r.work, r.valid_from, r.anchor)}>
              <span>{label(r.title) ?? humanSlug(r.work)}{r.num ? `, ${r.num}` : ""}</span>
              <span className="hitmeta">
                {r.jurisdiction ? <span>{jurisdictionLabel(r.jurisdiction)}</span> : null}
                <span className="mono">{r.valid_from} · {r.anchor}</span>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {view.rows.length > 0 ? (
        /* A returned row proves that at least one article refers. It does not prove that the
           number beside it is the total, so only a receipt of false leaves the rows
           unqualified. */
        view.rows_truncated === true
          ? <Empty>This response returned fewer rows than it found.</Empty>
        : view.rows_truncated === false ? null
        : <Empty>This response does not record whether it was complete.</Empty>
      ) : view.rows_truncated === true ? (
        /* Rows were cut and none survived for this unit. The receipt is response-wide, so
           it says nothing about which unit was cut, only that absence cannot be claimed. */
        <Empty>This response returned fewer rows than it found, so an empty list here is
          not evidence that nothing refers to this law.</Empty>
      ) : view.rows_truncated === false ? (
        /* The only branch that may state an absence, because it is the only one holding a
           receipt that nothing was cut. */
        <Empty>No held provision version in this corpus refers to this law.</Empty>
      ) : (
        /* No receipt, or one that is not a boolean. Absent evidence is not a negative
           fact, so this says what happened and claims nothing about the corpus. */
        <Empty>No rows were returned. This response does not record whether it was
          complete.</Empty>
      )}
    </>
  );
}


/**
 * One name for a law, everywhere it appears.
 *
 * There used to be two. The pickers used `shorten`, which drops the "Version consolidée applicable
 * au … :" that Legilux prefixes to every consolidated title. The ranking used `clean`, which only
 * collapsed whitespace and cut at 120 characters. So the same law read as "Code du travail" in one
 * list and as boilerplate severed mid-word in the next, and neither list was wrong on its own.
 */
export const label = (t?: string) => shorten(t?.replace(/\s+/g, " ").trim());

/**
 * Last resort, for the handful of works the publisher never titled. `lu-legilux:st-1994-01-19-n1`
 * is not a name; the date inside it is. Types Lex can name are named, and the rest keep their
 * identifier rather than being given a legal category they may not belong to.
 */
const MONTHS = ["janvier", "février", "mars", "avril", "mai", "juin",
                "juillet", "août", "septembre", "octobre", "novembre", "décembre"];
const KINDS: Record<string, string> = {
  loi: "Loi", rgd: "Règlement grand-ducal", rmin: "Règlement ministériel",
  amin: "Arrêté ministériel", agd: "Arrêté grand-ducal",
};
export function humanSlug(work: string) {
  const slug = work.includes(":") ? work.slice(work.indexOf(":") + 1) : work;
  const m = /^([a-z]+)-(\d{4})-(\d{2})-(\d{2})/.exec(slug);
  const kind = m && KINDS[m[1]];
  return kind ? `${kind} du ${Number(m![4])} ${MONTHS[Number(m![3]) - 1]} ${m![2]}` : slug;
}

/** Strip the Markdown emphasis publishers put in structural headings. */
const plain = (s: string) => s.replace(/\*+/g, "").replace(/\s+/g, " ").trim();

/**
 * Hierarchical table of contents for a large code. Groups by the first level of each
 * provision's path (Book / Title / Chapter / Section) and opens one section at a time, with
 * a filter box over article numbers and headings. Precedent: legislation.gov.uk's ToC view,
 * which exists for the same reason — a national code is not a scrollable list.
 */
function Outline({ items, current, onPick }: {
  items: ProvisionItem[]; current?: string; onPick: (anchor: string) => void;
}) {
  const [open, setOpen] = useState<string>();
  const [q, setQ] = useState("");

  const needle = q.trim().toLowerCase();
  const matches = (p: ProvisionItem) =>
    !needle || `${p.num ?? ""} ${p.heading ?? ""} ${p.anchor}`.toLowerCase().includes(needle);

  const groups = new Map<string, ProvisionItem[]>();
  const sectionOf = new Map<string, string>();
  for (const p of items) {
    const top = plain((p.path ?? "").split(" / ")[0] || "Without a section");
    sectionOf.set(p.anchor, top);
    if (!matches(p)) continue;
    (groups.get(top) ?? groups.set(top, []).get(top)!).push(p);
  }
  // The section holding the article you are reading opens itself — otherwise the outline
  // answers "where am I?" with a list of closed boxes.
  const here = current ? sectionOf.get(current) : undefined;
  const single = groups.size === 1;

  return (
    <>
      <div className="tochead">
        <b>Contents</b>
        <span className="sub mono">{items.length}</span>
      </div>
      <input name="outline-filter" className="filter" value={q} onChange={(e) => setQ(e.target.value)}
             aria-label="Filter articles by number or heading"
             placeholder="Filter articles…" />
      {needle && groups.size === 0 ? <p className="sub">No article matches “{q}”.</p> : null}
      <ul className="toc">
        {[...groups].map(([section, list]) => {
          const expanded = single || open === section || needle.length > 0 || (open === undefined && section === here);
          return (
            <li key={section}>
              {single ? null : (
                <button className="toch" aria-expanded={expanded}
                        onClick={() => setOpen(expanded && !needle ? "" : section)}>
                  <span>{expanded ? "▾" : "▸"} {section}</span>
                  <span className="sub mono">{list.length}</span>
                </button>
              )}
              {expanded ? (
                <ul className="rows">
                  {list.map((p) => (
                    <li key={p.anchor}>
                      <button className={`rowbtn${p.anchor === current ? " on" : ""}`}
                              aria-current={p.anchor === current ? "true" : undefined}
                              onClick={() => onPick(p.anchor)}>
                        <span>{p.num ?? p.anchor}{p.heading ? `, ${plain(p.heading)}` : ""}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
            </li>
          );
        })}
      </ul>
    </>
  );
}
