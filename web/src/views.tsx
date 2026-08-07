import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { first, tool, type ProvisionItem, type RankingRow, type UiEffect } from "./api";
import { LAYERS, publisherOf, workSlug, type LayerId, type State } from "./state";
import { shorten } from "./pickers";
import { EvidenceActions } from "./EvidenceActions";
import { citationText, evidenceFilename, lawEvidenceMarkdown } from "./export";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";

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
export function Provision({ items, toc, validFrom, validTo, work, title, language, anchor, profile, source, onPick, onClear, onCite }: {
  items: ProvisionItem[]; toc: ProvisionItem[]; validFrom: string; validTo?: string;
  work: string; title: string; language?: string; anchor?: string; profile?: string; source?: string;
  // `auto` marks an article the reader did not ask for. The rail uses it to decide whether to
  // stay on the law's versions or narrow to this article's texts.
  onPick: (anchor: string, auto?: boolean) => void; onClear: () => void; onCite?: (work: string) => void;
}) {
  // Where this text came from. Publisher markup and a read PDF are not the same claim, and the
  // difference has to reach the person reading the words, not stop at a field in a JSON file.
  // Two levels, because the risks differ: pdf-lu/1 read a document that IS this act, so the doubt
  // is only about where an article begins. pdf-memorial-lu/1 had to find this act inside a whole
  // issue of the official gazette, so the doubt extends to whether this is the right act at all.
  const fromPdf = profile === "pdf-lu/1";
  const fromGazette = profile === "pdf-memorial-lu/1";
  const outlineOnly = items.length > 0 && items.every((p) => !p.text);
  const nav = toc.length >= 6 || outlineOnly;
  const pageUrl = typeof window === "undefined" ? "" : window.location.href;
  const evidence = () => ({
    title, work, validFrom, validTo, language, source, permalink: pageUrl,
    extractionProfile: profile, provisions: items, exportedAt: new Date().toISOString(),
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
        <span className="tag">in force {validFrom} → {validTo ?? "open"}</span>
        {anchor ? (
          <button className="tag act" onClick={onClear}>article {anchor} ✕</button>
        ) : (
          <span className="tag">{items.length} article{items.length === 1 ? "" : "s"}</span>
        )}
        {fromPdf ? <span className="tag warn">read from the publisher's PDF</span> : null}
        {fromGazette ? <span className="tag warn">cut from a gazette issue</span> : null}
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
            So the words below are the publisher's, but two things are ours: finding where this act
            begins and ends inside that issue, and dividing it into articles. Both are inferred from
            the layout. An error here would not misplace a heading, it would show you a different
            act, so treat this as a reading aid and confirm anything that matters.
          </p>
          {source ? (
            <p>
              <a href={source} target="_blank" rel="noopener noreferrer">
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
      ) : items.map((p) => (
        <article key={p.anchor} className="art" id={p.anchor}>
          <h4>
            <a href={permalink(work, validFrom, p.anchor)}>{p.num ?? p.anchor}</a>
            {p.heading ? <span className="sub">, {plain(p.heading)}</span> : null}
          </h4>
          {/* Publisher text never becomes executable markup: react-markdown creates React nodes
              and ignores raw HTML by default. Export and comparison keep the untouched string. */}
          <div className="lawtxt"><Markdown remarkPlugins={[remarkGfm]}>{p.text}</Markdown></div>
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
            </div>
          ) : null}
          {p.text_sha256 ? <div className="sha">sha256 {p.text_sha256.slice(0, 16)}…</div> : null}
        </article>
      ))}
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
export function VersionRail({ dates, current, compareTo, scope, today, onPick, onCompare, onClear }: {
  dates: string[]; current?: string; compareTo?: string; scope: string; today: string;
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
        <span className="tag">{dates.length} {scope}</span>
        {median > 0 ? <span className="tag">every {median} days (median)</span> : null}
        {ahead > 0 ? <span className="tag warn">{ahead} not yet in force</span> : null}
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
                    style={{ left: xs[k] }} title={`${d}${d > today ? ", not yet in force" : ""}`}
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

/** Compare: fetches both versions itself, then shows only what moved. */
function useRowFacts(rows: RankingRow[]) {
  const [facts, setFacts] = useState<Record<string, { text: boolean; title?: string; kind?: string }>>({});
  const key = rows.map((r) => r.work).join("|");
  useEffect(() => {
    let live = true;
    setFacts({});
    const queue = rows.slice();
    const worker = async () => {
      for (;;) {
        const r = queue.shift();
        if (!r || !live) return;
        let fact: { text: boolean; title?: string; kind?: string } = { text: false };
        try {
          const res = await tool<any>("as_of", { work: r.work, date: r.last_change, mode: "outline" });
          const one = first<any>(res, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0)
                   ?? (Array.isArray(res) ? res[0] : res);
          const doc = one?.document ?? one;
          fact = { text: (one?.provisions?.length ?? 0) > 0, title: label(doc?.title),
                   kind: doc?.document_type ?? undefined };
        } catch { /* a row that cannot be checked stays unmarked rather than wrongly marked */ }
        if (live) setFacts((f) => ({ ...f, [r.work]: fact }));
      }
    };
    void Promise.all(Array.from({ length: Math.min(6, rows.length) }, worker));
    return () => { live = false; };
  }, [key]);
  return facts;
}

/**
 * What changed in a period.
 *
 * This used to rank everything together and then fold the thematic collections away, which had a
 * perverse effect: a collection restamps whenever anything on its shelf moves, so over a longer
 * window they crowded the top and squeezed the laws out. Six years showed 7 laws where one year
 * showed 16, while the totals above correctly rose from 264 to 860.
 *
 * A collection turned out to be nothing special: it is simply another document type. So the layer
 * is a filter the server applies, every row that arrives already belongs to the chosen layer, and
 * the widget that used to hide them is gone. Layers are by legal weight rather than by the
 * publisher's fifteen codes, because a reader asks about laws and regulations, not about RGC.
 */
export function Ranking({ rows, worksChanged, newVersions, from, to, layer, page, hasMore,
                          onOpen, onOpenRecord, onPage }: {
  rows: RankingRow[]; worksChanged: number; newVersions: number; from: string; to: string;
  layer: LayerId; page: number; hasMore: boolean;
  onOpen: (work: string, from: string, to: string) => void;
  onOpenRecord: (work: string, date: string) => void;
  onPage: (p: number) => void;
}) {
  const facts = useRowFacts(rows);
  const max = Math.max(1, ...rows.map((r) => r.versions_in_period));
  const current = LAYERS.find((l) => l.id === layer) ?? LAYERS[0];

  // The layer tabs used to live here, above the rows, which meant a filter that matched nothing
  // took its own escape hatch down with it. They belong with the window and the ordering.
  return (
    <>
      {/* One row, not three. The layer's meaning belongs beside its counts, because "820 changed"
          only means anything once you know 820 of what. */}
      <div className="cnt">
        <span className="tag">{worksChanged.toLocaleString()} changed</span>
        <span className="tag">{newVersions.toLocaleString()} new versions</span>
        <span className="tag mono">{from} → {to}</span>
        <span className="layers-hint">{current.hint}</span>
      </div>

      <div className="bars">
        {rows.map((r) => {
          const f = facts[r.work];
          const name = f?.title ?? label(r.title) ?? humanSlug(r.work);
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
          const comparable = !!f?.text && from !== to && !reissued;
          const why = !f?.text ? "No text is published for this version, open its record"
            : reissued ? `Reissued ${n} time${n === 1 ? "" : "s"} in this window without the wording changing`
            : comparable ? `Compare ${from} with ${to}`
            : "This law's first version falls in this window, so there is nothing earlier to compare it with";
          return (
            <button key={r.work} className={"bar" + (f && !f.text ? " notext" : "")}
                    title={why}
                    onClick={() => comparable
                      ? onOpen(r.work, from, to)
                      : onOpenRecord(r.work, r.last_change)}>
              <span className="track">
                <span className="fill" style={{ width: `${(n / max) * 100}%` }} />
                <span className="lbl">{name}</span>
                {f && !f.text ? <span className="mark">record only</span>
                  : reissued ? <span className="mark">same wording</span>
                  : !comparable ? <span className="mark">first version</span> : null}
              </span>
              {/* "4" beside a comparison showing one edit read as a contradiction. It counts
                  versions the publisher issued, which is not the same as edits to the wording. */}
              <span className="num" title={`${n} new version${n === 1 ? "" : "s"} in this window`}>{n}</span>
            </button>
          );
        })}
      </div>

      {rows.length === 0 ? (
        <Empty>Nothing in this layer changed in that window.</Empty>
      ) : null}

      {(page > 0 || hasMore) ? (
        <div className="pager">
          <button className="ghost" disabled={page === 0} onClick={() => onPage(page - 1)}>
            ← previous
          </button>
          <span className="sub mono">{page * 25 + 1}–{page * 25 + rows.length} of {worksChanged.toLocaleString()}</span>
          <button className="ghost" disabled={!hasMore} onClick={() => onPage(page + 1)}>
            next →
          </button>
        </div>
      ) : null}
    </>
  );
}

export function InForce({ date, total, rows, onOpen }: {
  date: string; total: number;
  rows: { work: string; title?: string; kind?: string; valid_from: string }[];
  onOpen: (work: string, date: string) => void;
}) {
  return (
    <>
      <div className="cnt">
        <span className="tag">{total.toLocaleString()} in force</span>
        <span className="tag mono">on {date}</span>
      </div>
      <ul className="rows">
        {rows.map((r) => (
          <li key={r.work}>
            <button className="rowbtn" onClick={() => onOpen(r.work, r.valid_from)}>
              <span>{r.title ?? r.work}</span>
              <span className="sub mono">{r.kind ?? ""} · since {r.valid_from}</span>
            </button>
          </li>
        ))}
      </ul>
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
export function Gap({ status, explanation, available, held }: {
  status: string; explanation: string; available: string[];
  held?: { text: number; total: number; official?: string };
}) {
  const whole = held && held.total > 0 && held.text === 0;
  return (
    <div className="gap">
      <div className="cnt"><span className="tag warn mono">{status}</span></div>
      {whole ? (
        <>
          <p><b>Lex holds the amendment record for this law, not its wording.</b></p>
          <p className="sub">
            Lex reads the publisher's machine-readable XML, because XML is the only format that
            marks where each article begins and ends, which is what makes an article citable,
            hashable and comparable across dates. For this law the publisher does not issue it, on
            any of its {held!.total.toLocaleString()} versions, so no date here will show wording.
          </p>
          <p className="sub">
            What is held for every one of them: the dates it applied between, the source it came
            from, and the hash of the record. The rail above is the history itself, and it is
            complete. The wording usually still exists at the publisher, as a PDF.
          </p>
          {held!.official ? (
            <p className="sub">
              <a href={held!.official} target="_blank" rel="noopener noreferrer">Open this law at the publisher ↗</a>
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
  !!(ui && (ui.provision || ui.diff || ui.history || ui.ranking || ui.in_force || ui.cited_by || ui.gap));

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
        <span className="tag">{view.citing_articles.toLocaleString()} article{view.citing_articles === 1 ? "" : "s"} refer to it</span>
        <span className="tag mono">{view.cited_work}</span>
      </div>
      <ul className="rows">
        {view.rows.map((r, i) => (
          <li key={`${r.work}-${r.anchor}-${i}`}>
            <button className="rowbtn" onClick={() => onOpen(r.work, r.valid_from, r.anchor)}>
              <span>{label(r.title) ?? humanSlug(r.work)}{r.num ? `, ${r.num}` : ""}</span>
              <span className="sub mono">{r.valid_from} · {r.anchor}</span>
            </button>
          </li>
        ))}
      </ul>
    </>
  );
}

/** A history answer needs no mode of its own: the rail is already showing it. */
export function modeFor(ui?: UiEffect): State["mode"] | undefined {
  if (ui?.diff) return "compare";
  if (ui?.history || ui?.provision) return "read";
  return undefined;
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
      <input className="filter" value={q} onChange={(e) => setQ(e.target.value)}
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
