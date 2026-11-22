// The architecture dossier (`architecture.html`, dossier-page 22/11/2026): how Lex is built, top to bottom, for a technical reader
// to walk through in ten minutes, in the V4 pages' look and menu.
//
//   1 the corpus (what is held and not held, the shape decision of each part), 2 the data structure (text → version → part of
//   the table of contents → article → unit → line, with the live counts and one real path), 3 the indexes, 4 the reference graph
//   (how it is built, how the search uses it, what it is worth, how precise it is, and what it is not), 5 the funnel on one real
//   answer of the live engine (`dossier-trace.json`), 6 the evaluation, 7 operations and governance, 8 what comes next.
//
// Every word is `scripts/dossier-copy.mjs`'s. Every figure is read live when the page opens (`LIVE_ADDRESSES`: GET only, no
// model call), or comes from `dossier-facts.json` (built from the named result files of the release that answers, each with its
// source and date, listed at the end of the page) or from the stored answer. A figure not read is shown as a dash, never guessed.
// Built into its page in its loading state; `client-live-architecture-entry.jsx` draws it again once the page has asked.

import { useEffect, useState } from 'react';
import { SHAPE_REASONS_FR, countedForm, decimal, fillNext, groupedCount, nextCopy, numericDate } from '../scripts/next-copy.mjs';
import { dossierCopy } from '../scripts/dossier-copy.mjs';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { keptLine, modelRows, residencyLine } from './NextHow.jsx';
import FACTS from './dossier-facts.json';
import TRACE from './dossier-trace.json';

/** The root the dossier is drawn into, and its script. */
export const NEXT_ARCHITECTURE_ROOT = 'lx-next-architecture';
export const ARCHITECTURE_SCRIPT = 'client-live-architecture.js';
export const ARCHITECTURE_PAGE = 'architecture.html';

/** The addresses read when the page opens: GET only, none of them calls a model. */
export const LIVE_ADDRESSES = Object.freeze({
  corpus: '/api/next/corpus',
  config: '/api/next/config',
  coverage: '/api/next/coverage',
  results: '/api/next/results',
  shapes: '/api/next/shapes',
  dictionary: '/api/next/dictionary',
  outline: `/api/next/text-outline?work=${encodeURIComponent(FACTS.example.work)}`,
  article: `/api/next/article-by?work=${encodeURIComponent(FACTS.example.work)}&anchor=${encodeURIComponent(FACTS.example.anchor)}`,
});

/** The parts, in their order, and who acts in each (code, a model, or both). */
export const PARTS = Object.freeze([
  Object.freeze({ key: 'corpus', by: ['code'] }),
  Object.freeze({ key: 'data', by: ['code'] }),
  Object.freeze({ key: 'indexes', by: ['code', 'model'] }),
  Object.freeze({ key: 'references', by: ['code', 'model'] }),
  Object.freeze({ key: 'funnel', by: ['code', 'model'] }),
  Object.freeze({ key: 'evaluation', by: ['code'] }),
  Object.freeze({ key: 'operations', by: [] }),
  Object.freeze({ key: 'next', by: [] }),
]);

/** The confidence level of every interval the page shows (the result files' own). */
const CONFIDENCE = 95;
const NB = ' ';

const isCount = (value) => typeof value === 'number' && Number.isFinite(value);
const obj = (value) => (value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null);
const arr = (value) => (Array.isArray(value) ? value : []);
const txt = (value) => (typeof value === 'string' && value.trim().length > 0 ? value.trim() : null);
/** A trigger as the parser matched it, cut inside a word (« à l’exception d »): said with an ellipsis. */
const trigger = (value) => (/(?:^|[\s'’])[dl]$/u.test(String(value ?? '')) ? `${value}…` : String(value ?? ''));

/** Every live address in its first state. */
export const loadingLive = () => Object.fromEntries(Object.keys(LIVE_ADDRESSES).map((key) => [key, { state: 'loading', data: null }]));

/** The live addresses' answers, asked once by GET after the page is drawn: `{ live, at }`. */
function useLiveDossier(fetchImpl) {
  const [answer, setAnswer] = useState(() => ({ live: loadingLive(), at: null }));
  useEffect(() => {
    let on = true;
    const ask = fetchImpl ?? globalThis.fetch;
    Promise.all(Object.entries(LIVE_ADDRESSES).map(async ([key, address]) => {
      try {
        const response = await ask(address, { method: 'GET', headers: { accept: 'application/json' } });
        if (!response.ok) throw new Error('not answered');
        const data = await response.json();
        if (obj(data) === null || data.none === true) throw new Error('nothing to show');
        return [key, { state: 'ready', data }];
      } catch {
        return [key, { state: 'failed', data: null }];
      }
    })).then((rows) => { if (on) setAnswer({ live: Object.fromEntries(rows), at: new Date() }); });
    return () => { on = false; };
  }, [fetchImpl]);
  return answer;
}

/** The helpers every part formats its figures with, in `locale`; `blank` stands for a figure not read. */
function formatters(locale, blank) {
  const count = (value) => (isCount(value) ? groupedCount(value, locale) : blank);
  const dec = (value, digits = null) => (isCount(value) ? decimal(value, locale, digits) : blank);
  const percent = (value, digits = 1) => (isCount(value) ? `${decimal(value, locale, digits)}${locale === 'fr' ? NB : ''}%` : blank);
  const share = (part, whole) => (isCount(part) && isCount(whole) && whole > 0 ? percent((100 * part) / whole) : blank);
  const usd = (value) => (isCount(value) ? decimal(Number(value.toFixed(4)), locale) : blank);
  const p = (value) => {
    if (!isCount(value)) return blank;
    if (value < 0.001) return `< ${decimal(0.001, locale)}`;
    const digits = value >= 0.1 ? 2 : value >= 0.01 ? 3 : 4;
    return `= ${decimal(value, locale, digits)}`;
  };
  const date = (iso) => numericDate(iso) || blank;
  return { count, dec, percent, share, usd, p, date, ci: `${CONFIDENCE}${locale === 'fr' ? NB : ''}%` };
}

/** The time a live read was made, as the page's language writes it: `{ date, time }`. */
export function readAt(at, locale) {
  const when = at instanceof Date ? at : typeof at === 'string' ? new Date(at) : null;
  if (when === null || Number.isNaN(when.getTime())) return null;
  const two = (value) => String(value).padStart(2, '0');
  const date = `${two(when.getDate())}/${two(when.getMonth() + 1)}/${when.getFullYear()}`;
  return { date, time: locale === 'fr' ? `${two(when.getHours())}${NB}h${NB}${two(when.getMinutes())}` : `${two(when.getHours())}:${two(when.getMinutes())}` };
}

/** The state of the live read as a whole: loading, ready, partial (with the addresses not answered) or failed. */
export function liveState(live) {
  const states = Object.values(live).map((one) => one?.state ?? 'loading');
  if (states.includes('loading')) return { state: 'loading', missing: [] };
  const missing = Object.entries(live).filter(([, one]) => one.state !== 'ready').map(([key]) => key);
  return { state: missing.length === 0 ? 'ready' : missing.length === states.length ? 'failed' : 'partial', missing };
}

/**
 * The example path of part two: from the live reads of the text's table of contents and of its article when both answered,
 * else the copy read on the day the facts were made (said so). The neighbours of the article and what cites the citing
 * article always come from that copy.
 */
export function exampleOf(live) {
  const stored = { ...FACTS.example, from: 'stored' };
  const outline = obj(live?.outline?.data);
  const article = obj(live?.article?.data);
  if (live?.outline?.state !== 'ready' || live?.article?.state !== 'ready' || outline === null || article === null || obj(outline.version) === null || arr(article.lines).length === 0) return stored;
  const rows = arr(outline.outline);
  const path = [];
  let parent = null;
  for (const segment of (txt(article.path) ?? '').split(' > ')) {
    const row = rows.find((one) => (one.parent ?? null) === parent && txt(one.label) === segment.trim());
    if (row === undefined) break;
    path.push({ node_id: row.node_id, label: row.label, articles: row.articles, first: row.first_article, last: row.last_article });
    parent = row.node_id;
  }
  const history = arr(article.history);
  return {
    ...stored,
    title: txt(outline.title) ?? stored.title,
    version: outline.version,
    held: obj(outline.held) ?? stored.held,
    path: path.length > 0 ? path : stored.path,
    article: {
      article_pk: article.article_pk, label: article.num_raw, heading: article.heading, wording_since: article.wording_since,
      versions_same_text: history.length > 0 && new Set(history.map((row) => row.text_sha)).size === 1, versions: history.length, lines: article.lines,
    },
    cited_by: arr(article.cited_by).map((row) => ({ article: row.article, heading: row.heading, type: row.type, trigger: row.trigger, certainty: row.certainty, raw: row.raw })),
    from: 'live',
  };
}

/** A tag saying who acts. */
function Tag({ by, copy }) {
  return <span className={by === 'model' ? 'lx-how-tag lx-how-tag-model' : 'lx-how-tag'} data-by={by}>{copy.tags[by]}</span>;
}

/** Who acts at a level of the trace ("code+model", "code+embedding" …), as tags: an embedding is code calling a model to compute a vector. */
function tagsOf(by) {
  const parts = String(by ?? '').split('+');
  return [...(parts.some((part) => part.startsWith('code') || part === 'embedding') ? ['code'] : []), ...(parts.includes('model') ? ['model'] : [])];
}

/** The head of a part: its number, its heading, who acts. */
function PartHead({ part, at, heading, copy }) {
  return (
    <div className="lx-arch-part-head">
      <span className="lx-arch-n" aria-hidden="true">{at + 1}</span>
      <h2 id={`arch-${part.key}-h`}>{heading}</h2>
      {part.by.length === 0 ? null : <span className="lx-arch-tags">{part.by.map((by) => <Tag key={by} by={by} copy={copy} />)}</span>}
    </div>
  );
}

/** A row of figures, each with what it counts. */
function Tiles({ rows, name }) {
  return (
    <ul className="lx-arch-tiles" data-arch-tiles={name}>
      {rows.map(([key, figure, label]) => <li key={key} data-tile={key}><strong>{figure}</strong><span>{label}</span></li>)}
    </ul>
  );
}

/** A thin bar for a share of a whole, drawn (decorative: the figure stands beside it). */
function ShareBar({ part, whole }) {
  const width = isCount(part) && isCount(whole) && whole > 0 ? Math.max(1, Math.round((240 * part) / whole)) : 0;
  return (
    <svg className="lx-arch-bar" viewBox="0 0 240 8" width="240" height="8" aria-hidden="true" focusable="false">
      <rect x="0" y="0" width="240" height="8" rx="4" className="lx-arch-bar-track" />
      {width > 0 ? <rect x="0" y="0" width={width} height="8" rx="4" className="lx-arch-bar-fill" /> : null}
    </svg>
  );
}

// ---- 1. the corpus ---------------------------------------------------------------------------------------------------------

function CorpusPart({ ctx }) {
  const { copy, live, f, locale } = ctx;
  const c = copy.corpus;
  const coverage = obj(live.coverage.data) ?? {};
  const served = obj(coverage.served) ?? {};
  const why = obj(served.in_force_not_served) ?? {};
  const byType = arr(coverage.by_type);
  const law = byType.find((row) => row.type === 'LOI') ?? {};
  const rgd = byType.find((row) => row.type === 'RGD') ?? {};
  const shapes = arr(obj(live.shapes.data)?.rows).filter((row) => obj(row) !== null && txt(row.part) !== null && txt(row.shape) !== null);
  const status = obj(coverage.status) ?? {};
  const versions = obj(coverage.versions) ?? {};
  const order = obj(obj(coverage.checks)?.conservation_order) ?? {};
  const lawLang = locale === 'fr' ? undefined : 'fr';
  const reasonOf = (row) => (Object.hasOwn(c.reasonOverrides, row.part) ? { text: c.reasonOverrides[row.part], lang: undefined } : locale === 'fr' && Object.hasOwn(SHAPE_REASONS_FR, row.part) ? { text: SHAPE_REASONS_FR[row.part], lang: undefined } : txt(row.reason) === null ? null : { text: row.reason.trim(), lang: locale === 'en' ? undefined : 'en' });
  return (
    <>
      <p className="lx-arch-say">{c.say}</p>
      <Tiles
        name="corpus"
        rows={[
          ['luHeld', f.count(served.luxembourg_acts_held), c.tiles.luHeld],
          ['inForce', f.count(served.luxembourg_in_force_held), c.tiles.inForce],
          ['served', f.count(served.luxembourg_served_as_todays_law), c.tiles.served],
          ['older', f.count(served.luxembourg_in_force_held_not_served), c.tiles.older],
          ['eu', f.count(served.eu_acts_held), fillNext(c.tiles.eu, { served: f.count(served.eu_served_as_todays_law) })],
          ['ocr', f.count(obj(coverage.text_flags)?.ocr), c.tiles.ocr],
        ]}
      />
      <p className="lx-arch-line" data-arch="older-why">{fillNext(c.olderWhy, { older: f.count(served.luxembourg_in_force_held_not_served), original: f.count(why.original_amended_since), newer: f.count(why.newer_version_not_held), german: f.count(why.held_only_in_another_language) })}</p>
      <div className="lx-arch-split">
        <section aria-labelledby="arch-corpus-types">
          <h3 id="arch-corpus-types">{c.byTypeHeading}</h3>
          <div className="lx-how-table-scroll">
            <table className="lx-how-table lx-arch-num" data-arch="by-type">
              <thead><tr>{['type', 'held', 'inForce', 'served'].map((key) => <th scope="col" key={key}>{c.byTypeColumns[key]}</th>)}</tr></thead>
              <tbody>
                {arr(served.by_type).filter((row) => obj(row) !== null && txt(row.type) !== null).map((row) => (
                  <tr key={row.type} data-type={row.type}><th scope="row">{c.types[row.type] ?? row.type}</th><td>{f.count(row.held)}</td><td>{f.count(row.in_force)}</td><td>{f.count(row.served_today)}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
        <section aria-labelledby="arch-corpus-not">
          <h3 id="arch-corpus-not">{c.notHeldHeading}</h3>
          <ul className="lx-arch-list" data-arch="not-held">
            <li>{c.notHeld.kinds}</li>
            <li>{c.notHeld.communal}</li>
            <li>{fillNext(c.notHeld.catalogue, { laws: f.count(law.not_held), lawsListed: f.count(law.listed), rgd: f.count(rgd.not_held), rgdListed: f.count(rgd.listed) })}</li>
            <li>{fillNext(c.notHeld.older, { older: f.count(served.luxembourg_in_force_held_not_served) })}</li>
            <li>{fillNext(c.notHeld.annexes, { pdf: f.count(obj(obj(coverage.annexes)?.today_versions)?.pdf_only) })}</li>
          </ul>
        </section>
      </div>
      <h3>{c.shapesHeading}</h3>
      <p className="lx-arch-line">{c.shapesLead}</p>
      <dl className="lx-arch-kinds" data-arch="shape-kinds">
        {['typed', 'pile', 'case'].map((shape) => (
          <div key={shape} data-shape-kind={shape}>
            <dt>{c.shapeKinds[shape].name}</dt>
            <dd>{c.shapeKinds[shape].text}</dd>
          </div>
        ))}
      </dl>
      <p className="lx-arch-line lx-arch-honest">{c.shapesHonest}</p>
      {shapes.length === 0 ? null : (
        <div className="lx-how-table-scroll">
          <table className="lx-how-table lx-arch-num" data-arch="shapes">
            <thead><tr>{['part', 'shape', 'held', 'inForce', 'served'].map((key) => <th scope="col" key={key}>{c.shapesColumns[key]}</th>)}</tr></thead>
            {['part', 'cross_cutting'].map((kind) => {
              const these = shapes.filter((row) => (row.kind ?? 'part') === kind);
              if (these.length === 0) return null;
              return (
                <tbody key={kind}>
                  <tr className="lx-arch-group"><th scope="rowgroup" colSpan={5}>{c.shapeKindsRow[kind]}</th></tr>
                  {these.map((row) => (
                    <tr key={row.part} data-shape={row.shape} data-part={row.part}>
                      <th scope="row">{(locale === 'fr' ? txt(row.label_fr) : txt(row.label_en)) ?? txt(row.label_en) ?? row.part}</th>
                      <td className="lx-arch-left"><span className="lx-arch-chip" data-chip={row.shape}>{c.shapeKinds[row.shape]?.name ?? row.shape}</span></td>
                      <td>{f.count(row.acts_held)}</td><td>{f.count(row.in_force)}</td><td>{f.count(row.served_today)}</td>
                    </tr>
                  ))}
                </tbody>
              );
            })}
          </table>
        </div>
      )}
      {shapes.length === 0 ? null : (
        <details className="lx-how-fold" data-arch-fold="shape-reasons">
          <summary>{c.shapesReasons}</summary>
          <dl className="lx-arch-reasons">
            {shapes.map((row) => {
              const reason = reasonOf(row);
              return reason === null ? null : (
                <div key={row.part}><dt>{(locale === 'fr' ? txt(row.label_fr) : txt(row.label_en)) ?? row.part}</dt><dd lang={reason.lang}>{reason.text}</dd></div>
              );
            })}
          </dl>
        </details>
      )}
      <details className="lx-how-fold" data-arch-fold="corpus-detail">
        <summary>{c.detailSummary}</summary>
        <ul className="lx-arch-list">
          <li>{fillNext(c.statusLine, { inForce: f.count(status['LU:in_force']), repealed: f.count(status['LU:repealed']), ended: f.count(status['LU:no_longer_in_force']), notYet: f.count(status['LU:not_yet_in_force']) })}</li>
          <li>{fillNext(c.versionsLine, { versions: f.count(versions.lu_versions), open: f.count(versions.open_still_law), unknown: f.count(versions.end_not_known) })}</li>
          <li>{fillNext(c.orderLine, { passed: f.count(order.passed), checked: f.count(order.files_checked), failed: f.count(order.failed) })}</li>
        </ul>
      </details>
    </>
  );
}

// ---- 2. the data structure -------------------------------------------------------------------------------------------------

const LEVELS = Object.freeze(['act', 'version', 'node', 'article', 'unit', 'line']);
const SIDE = Object.freeze({ act: 'links', article: 'annexes', unit: 'definitions', line: 'tables' });

/** The levels of the data, drawn: one box a level with its live count, the tables beside, the references from a line to an article. */
export function StructureDiagram({ counts, copy, f }) {
  const d = copy.data;
  const box = { x: 46, w: 246, h: 56, gap: 30 };
  const side = { x: 318, w: 142, h: 48 };
  const top = (at) => at * (box.h + box.gap);
  const height = LEVELS.length * box.h + (LEVELS.length - 1) * box.gap;
  const middle = (at) => top(at) + box.h / 2;
  const from = middle(LEVELS.indexOf('line'));
  const to = middle(LEVELS.indexOf('article'));
  return (
    <svg className="lx-arch-svg" viewBox={`0 0 460 ${height}`} role="img" aria-labelledby="arch-svg-title arch-svg-desc" data-arch="structure">
      <title id="arch-svg-title">{d.svgTitle}</title>
      <desc id="arch-svg-desc">{d.svgDesc}</desc>
      <defs>
        <marker id="lx-arch-head" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0 0 L10 5 L0 10 z" className="lx-arch-svg-head" />
        </marker>
      </defs>
      {LEVELS.map((key, at) => (
        <g key={key} data-level={key}>
          <rect x={box.x} y={top(at)} width={box.w} height={box.h} rx="6" className="lx-arch-svg-box" />
          <text x={box.x + 14} y={top(at) + 23} className="lx-arch-svg-name">{d.levels[key].name}</text>
          <text x={box.x + 14} y={top(at) + 43} className="lx-arch-svg-count">{key === 'unit' ? d.levels.unit.count : fillNext(d.levels[key].count, { count: f.count(counts[key]) })}</text>
          {at < LEVELS.length - 1 ? <line x1={box.x + box.w / 2} y1={top(at) + box.h} x2={box.x + box.w / 2} y2={top(at + 1) - 3} className="lx-arch-svg-link" markerEnd="url(#lx-arch-head)" /> : null}
          {Object.hasOwn(SIDE, key) ? (
            <g data-side={SIDE[key]}>
              <line x1={box.x + box.w} y1={middle(at)} x2={side.x} y2={middle(at)} className="lx-arch-svg-tie" />
              <rect x={side.x} y={middle(at) - side.h / 2} width={side.w} height={side.h} rx="6" className="lx-arch-svg-side" />
              <text x={side.x + 10} y={middle(at) - 4} className="lx-arch-svg-sname">{d.side[SIDE[key]].name}</text>
              <text x={side.x + 10} y={middle(at) + 14} className="lx-arch-svg-scount">{fillNext(d.side[SIDE[key]].count, { count: f.count(counts[SIDE[key]]) })}</text>
            </g>
          ) : null}
        </g>
      ))}
      <path d={`M ${box.x} ${from} C ${box.x - 44} ${from}, ${box.x - 44} ${to}, ${box.x - 3} ${to}`} className="lx-arch-svg-ref" markerEnd="url(#lx-arch-head)" />
      <text transform={`translate(${box.x - 18} ${(from + to) / 2}) rotate(-90)`} textAnchor="middle" className="lx-arch-svg-rname">{fillNext(d.side.refs.count, { count: f.count(counts.refs) })}</text>
    </svg>
  );
}

function DataPart({ ctx }) {
  const { copy, live, f, locale } = ctx;
  const d = copy.data;
  const corpus = obj(live.corpus.data) ?? {};
  const coverage = obj(live.coverage.data) ?? {};
  const annexes = obj(obj(coverage.annexes)?.today_versions) ?? {};
  const tables = obj(coverage.tables) ?? {};
  const counts = {
    act: corpus.acts, version: corpus.versions, node: corpus.toc_nodes, article: corpus.articles, line: corpus.lines,
    links: obj(corpus.links_between_texts)?.total, annexes: annexes.text, definitions: FACTS.store.definitions, tables: tables.tables, refs: corpus.edges,
  };
  const ex = ctx.example;
  const e = d.example;
  const lawLang = locale === 'fr' ? undefined : 'fr';
  const version = obj(ex.version) ?? {};
  const until = txt(version.until) !== null ? fillNext(e.untilDate, { until: f.date(version.until) }) : version.until_known ? e.untilOpen : e.untilUnknown;
  const lines = arr(ex.article?.lines);
  const units = lines.map((line) => line.unit).filter(Boolean);
  const [first, second] = arr(ex.neighbours);
  const sideText = {
    links: fillNext(d.side.links.text, { count: f.count(counts.links) }),
    annexes: fillNext(d.side.annexes.text, { text: f.count(annexes.text), pdf: f.count(annexes.pdf_only) }),
    definitions: fillNext(d.side.definitions.text, { count: f.count(FACTS.store.definitions), terms: f.count(FACTS.store.defined_terms) }),
    tables: fillNext(d.side.tables.text, { count: f.count(tables.tables), withHeader: f.count(tables.tables_with_header) }),
    refs: fillNext(d.side.refs.text, { count: f.count(corpus.edges) }),
  };
  return (
    <>
      <p className="lx-arch-say">{d.say}</p>
      <div className="lx-arch-split lx-arch-split-wide">
        <figure className="lx-arch-figure">
          <StructureDiagram counts={counts} copy={copy} f={f} />
        </figure>
        <dl className="lx-arch-levels-text" data-arch="levels">
          {LEVELS.map((key) => <div key={key} data-level={key}><dt>{d.levels[key].name}</dt><dd>{d.levels[key].text}</dd></div>)}
          {Object.values(SIDE).concat('refs').map((key) => <div key={key} data-side={key}><dt>{d.side[key].name}</dt><dd>{sideText[key]}</dd></div>)}
        </dl>
      </div>
      <section className="lx-arch-example" aria-labelledby="arch-example-h" data-arch="example" data-example-from={ex.from}>
        <h3 id="arch-example-h">{d.exampleHeading}</h3>
        <ol className="lx-arch-path">
          <li data-step="act"><span className="lx-arch-step">{d.exampleLevels.act}</span><span lang={lawLang}>{fillNext(e.act, { title: String(ex.title ?? '').replace(/\.\s*$/u, ''), address: ex.act_id, type: ex.type, since: f.date(ex.in_force_since) })}</span></li>
          <li data-step="version"><span className="lx-arch-step">{d.exampleLevels.version}</span><span>{fillNext(e.version, { from: f.date(version.from), until, articles: f.count(version.articles), versions: f.count(ex.held?.versions), first: f.date(ex.held?.first) })}</span></li>
          <li data-step="node"><span className="lx-arch-step">{d.exampleLevels.node}</span><span lang={lawLang}>{arr(ex.path).map((node) => fillNext(e.node, { label: node.label, count: f.count(node.articles) })).join(' › ')}</span></li>
          <li data-step="article"><span className="lx-arch-step">{d.exampleLevels.article}</span><span>{fillNext(ex.article?.versions_same_text ? e.article : e.articleChanged, { label: ex.article?.label, heading: ex.article?.heading, since: f.date(ex.article?.wording_since), versions: f.count(ex.article?.versions) })}</span></li>
          <li data-step="unit">
            <span className="lx-arch-step">{d.exampleLevels.unit}</span>
            <span>{units.length === 1 && first && second
              ? fillNext(e.unit, { unit: units[0], neighbour: first.label, units: arr(first.units).join(', '), other: second.label, otherUnits: arr(second.units).join(', ') })
              : fillNext(e.unitPlain, { units: units.join(', ') })}</span>
          </li>
          {lines.map((line) => (
            <li data-step="line" key={line.n}><span className="lx-arch-step">{d.exampleLevels.line}</span><span>{fillNext(e.line, { n: line.n })} <q className="lx-arch-law" lang={lawLang}>{line.text}</q></span></li>
          ))}
        </ol>
        {arr(ex.cited_by).map((row) => <p className="lx-arch-line" key={`${row.article}-${row.raw}`} data-arch="cited-by">{fillNext(e.citedBy, { article: row.article, heading: row.heading, trigger: trigger(row.trigger), raw: row.raw })}</p>)}
        <p className="lx-next-note" data-arch="example-source">{ex.from === 'live' ? d.exampleSource.live : fillNext(d.exampleSource.stored, { date: f.date(FACTS.sources.example.date), source: FACTS.sources.example.file })}</p>
      </section>
      <details className="lx-how-fold" data-arch-fold="tables">
        <summary>{d.tablesSummary}</summary>
        <dl className="lx-arch-reasons">
          {d.tables.map((row) => <div key={row.name}><dt><code>{row.name}</code></dt><dd>{row.text}</dd></div>)}
        </dl>
        <p className="lx-arch-line">{d.invariants}</p>
      </details>
    </>
  );
}

// ---- 3. the indexes --------------------------------------------------------------------------------------------------------

function IndexCard({ name, rows, labels, card }) {
  return (
    <article className="lx-arch-card" data-index={card}>
      <h3>{name}</h3>
      <dl>
        {rows.filter(([, value]) => value !== null).map(([key, value]) => <div key={key} data-row={key}><dt>{labels[key]}</dt><dd>{value}</dd></div>)}
      </dl>
    </article>
  );
}

function IndexesPart({ ctx }) {
  const { copy, live, f, locale } = ctx;
  const x = copy.indexes;
  const coverage = obj(live.coverage.data) ?? {};
  const index = obj(coverage.index) ?? {};
  const vectors = obj(index.vectors) ?? {};
  const routing = obj(index.routing_lines) ?? {};
  const config = obj(live.config.data) ?? {};
  const dictionary = obj(obj(live.dictionary.data)?.counts) ?? {};
  const leftOut = obj(vectors.left_out) ?? {};
  const b = FACTS.budgets;
  return (
    <>
      <p className="lx-arch-say">{x.say}</p>
      <div className="lx-arch-cards">
        <IndexCard
          card="keyword" name={x.keyword.name} labels={x.labels}
          rows={[
            ['engine', fillNext(x.keyword.engine, { engine: b.keyword_engine })],
            ['count', fillNext(x.keyword.count, { articles: f.count(obj(index.keyword_index)?.articles), terms: f.count(FACTS.store.defined_terms) })],
            ['row', x.keyword.row], ['use', x.keyword.use], ['by', x.keyword.by],
          ]}
        />
        <IndexCard
          card="meaning" name={x.meaning.name} labels={x.labels}
          rows={[
            ['engine', fillNext(x.meaning.engine, { model: txt(config.embedding) ?? txt(vectors.model) ?? f.count(null), dims: f.count(vectors.dims) })],
            ['count', fillNext(x.meaning.count, { rows: f.count(vectors.rows), units: f.count(vectors.articles_with_rows), inUse: f.count(FACTS.store.vector_rows_in_use) })],
            ['row', fillNext(x.meaning.row, { first: f.count(b.row_first_chars), stretch: f.count(b.row_stretch_chars) })],
            ['use', x.meaning.use], ['by', x.meaning.by],
            ['leftOut', fillNext(x.meaning.leftOut, { amending: f.count(leftOut.amending), notApplicable: f.count(leftOut.not_applicable_act), personal: f.count(leftOut.personal_measure) })],
          ]}
        />
        <IndexCard
          card="routing" name={x.routing.name} labels={x.labels}
          rows={[
            ['engine', x.routing.engine],
            ['count', fillNext(x.routing.count, { lines: f.count(routing.lines), acts: f.count(routing.acts), groups: f.count(routing.groups) })],
            ['row', x.routing.row],
            ['use', fillNext(x.routing.use, { top: f.count(b.routing_top), shown: f.count(b.corpus_shown) })],
            ['by', fillNext(x.routing.by, { model: b.routing_model, prompt: b.routing_prompt })],
            ['status', x.routing.status],
          ]}
        />
        <IndexCard
          card="dictionary" name={x.dictionary.name} labels={x.labels}
          rows={[
            ['engine', x.dictionary.engine],
            ['count', fillNext(x.dictionary.count, { rows: f.count(dictionary.rows), concepts: f.count(dictionary.concepts), names: f.count(dictionary.act_names), validated: isCount(dictionary.validated) ? countedForm(x.validated, dictionary.validated, locale) : f.count(null) })],
            ['row', x.dictionary.row],
            ['use', fillNext(x.dictionary.use, { weight: f.dec(b.weight_dictionary, 1) })],
            ['by', x.dictionary.by], ['status', x.dictionary.status],
          ]}
        />
      </div>
      <p className="lx-arch-line">{fillNext(x.weights, { question: f.dec(b.weight_question, 1), dictionary: f.dec(b.weight_dictionary, 1), model: f.dec(b.weight_model, 1) })}</p>
      <p className="lx-arch-line">{x.others}</p>
    </>
  );
}

// ---- 4. the reference graph ------------------------------------------------------------------------------------------------

const KIND_ORDER = Object.freeze(['exception', 'not_applicable', 'definition', 'preserved', 'applicable', 'pointer', 'untyped']);
const OTHER_ORDER = Object.freeze(['resolved_article', 'resolved_act', 'article_not_found', 'not_held', 'unresolved']);

/** The hops of the stored answer's references level. */
const traceLevel = (name) => arr(TRACE?.trace?.levels).find((level) => level?.level === name) ?? null;

function ReferencesPart({ ctx }) {
  const { copy, live, f, locale } = ctx;
  const r = copy.references;
  const corpus = obj(live.corpus.data) ?? {};
  const coverage = obj(live.coverage.data) ?? {};
  const refs = obj(coverage.references) ?? {};
  const totals = obj(refs.lu_totals) ?? {};
  const totalOther = OTHER_ORDER.reduce((sum, key) => sum + (isCount(totals[key]) ? totals[key] : 0), 0);
  const byType = Object.fromEntries(arr(corpus.edges_by_type).filter((row) => Array.isArray(row)).map(([type, n]) => [type, n]));
  const certainty = arr(corpus.edges_by_certainty).filter((row) => Array.isArray(row));
  const certaintyOf = (scope, mark) => certainty.find((row) => row[0] === scope && row[1] === mark)?.[2] ?? null;
  const results = obj(live.results.data) ?? {};
  const ablation = arr(obj(results.ablation)?.rows);
  const full = ablation.find((row) => row.what === 'all' && row.run === 'R1') ?? {};
  const off = ablation.find((row) => row.what === 'no_references') ?? {};
  const [onOnly, offOnly] = arr(FACTS.ablation_discordant.no_references);
  const precision = obj(refs.precision);
  const dev = FACTS.refs_experiment;
  const b = FACTS.budgets;
  const ex = ctx.example;
  const lawLang = locale === 'fr' ? undefined : 'fr';
  const hops = arr(traceLevel('references')?.hops);
  const definition = hops.find((hop) => hop.direction === 'definition');
  const term = /«\s*([^»]+?)\s*»/u.exec(definition?.why ?? '')?.[1] ?? null;
  const edgeLines = [
    ...arr(ex.cited_by).map((row) => ({ from: row.article, to: ex.article?.label, ...row })),
    ...arr(ex.cited_by_of_24).map((row) => ({ from: row.article, to: arr(ex.cited_by)[0]?.article, ...row })),
  ].filter((row) => txt(row.from) !== null && txt(row.to) !== null);
  const funnelRows = [
    ['found', FACTS.store.refs_found], ['edges', corpus.edges], ['toArticle', FACTS.store.edges_to_an_article], ['toUnit', obj(refs.unit_ties)?.tied],
  ];
  return (
    <>
      <p className="lx-arch-say">{r.say}</p>
      <div className="lx-arch-split">
        <section aria-labelledby="arch-refs-funnel">
          <h3 id="arch-refs-funnel">{r.funnelHeading}</h3>
          <table className="lx-how-table lx-arch-num lx-arch-bars" data-arch="refs-funnel">
            <tbody>
              {funnelRows.map(([key, value]) => (
                <tr key={key} data-row={key}><th scope="row">{r.funnelRows[key]}</th><td>{f.count(value)}</td><td className="lx-arch-bar-cell"><ShareBar part={value} whole={FACTS.store.refs_found} /></td></tr>
              ))}
            </tbody>
          </table>
        </section>
        <section aria-labelledby="arch-refs-other">
          <h3 id="arch-refs-other">{r.otherHeading}</h3>
          <table className="lx-how-table lx-arch-num lx-arch-bars" data-arch="refs-other">
            <thead><tr>{['status', 'count', 'share'].map((key) => <th scope="col" key={key}>{r.otherColumns[key]}</th>)}</tr></thead>
            <tbody>
              {OTHER_ORDER.map((key) => (
                <tr key={key} data-status={key}><th scope="row">{r.otherRows[key]}</th><td>{f.count(totals[key])}</td><td>{f.share(totals[key], totalOther)}</td></tr>
              ))}
            </tbody>
          </table>
        </section>
      </div>
      <h3>{r.kindsHeading}</h3>
      <div className="lx-how-table-scroll">
        <table className="lx-how-table lx-arch-kinds-table" data-arch="kinds">
          <thead><tr>{['kind', 'words', 'count', 'role'].map((key) => <th scope="col" key={key}>{r.kindsColumns[key]}</th>)}</tr></thead>
          <tbody>
            {KIND_ORDER.map((key) => (
              <tr key={key} data-kind={key}><th scope="row">{r.kinds[key].name}</th><td lang={key === 'untyped' ? undefined : lawLang}>{r.kinds[key].words}</td><td className="lx-arch-count">{f.count(byType[key])}</td><td>{r.kinds[key].role}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="lx-arch-line">{r.nearest}</p>
      <p className="lx-arch-line">{fillNext(r.certainty, { sure: f.count(certaintyOf('same_act', 'sure')), likely: f.count(certaintyOf('other_act', 'likely')), unsure: f.count(certaintyOf('other_act', 'unsure')) })}</p>
      <section className="lx-arch-example" aria-labelledby="arch-refs-example">
        <h3 id="arch-refs-example">{r.exampleHeading}</h3>
        <p className="lx-arch-line">{r.exampleEdges}</p>
        <ul className="lx-arch-edges" data-arch="edges">
          {edgeLines.map((row) => <li key={`${row.from}-${row.to}`}>{fillNext(r.edgeLine, { from: row.from, to: row.to, kind: r.kinds[row.type]?.name ?? row.type, trigger: trigger(row.trigger), raw: row.raw, certainty: r.certaintyNames[row.certainty] ?? row.certainty })}</li>)}
          {definition && term ? <li>{fillNext(r.definitionLine, { from: definition.from, to: definition.to, term })}</li> : null}
        </ul>
      </section>
      <h3>{r.howHeading}</h3>
      <ol className="lx-arch-flow" data-arch="refs-how">
        {r.how.map((step, at) => (
          <li key={step.key} data-step={step.key}>
            <span className="lx-arch-flow-n" aria-hidden="true">{at + 1}</span>
            <div>
              <h4>{step.name} <Tag by={step.by} copy={copy} /></h4>
              <p>{fillNext(step.text, { budget: f.count(b.hop_budget), targets: f.count(b.hop_targets), hub: f.count(b.hub) })}</p>
            </div>
          </li>
        ))}
      </ol>
      <h3>{r.measuredHeading}</h3>
      <ul className="lx-arch-list" data-arch="refs-measured">
        <li data-arch="refs-sealed">{fillNext(r.sealed, { n: f.count(full.n), off: f.count(off.right), on: f.count(full.right), onOnly: f.count(onOnly), offOnly: f.count(offOnly), p: f.p(off.p_vs_R1) })}</li>
        <li data-arch="refs-dev">{fillNext(r.dev, { half: f.count(dev.half_n), refItems: f.count(dev.reference_items), before: f.dec(dev.right_before, 1), after: f.dec(dev.right_after, 1), noise: f.count(dev.noise_right), cBefore: f.dec(dev.complete_before, 1), cAfter: f.dec(dev.complete_after, 1), cNoise: f.count(dev.noise_complete), twoHops: f.dec(dev.right_two_hops, 1), usd: f.usd(dev.usd_extra), share: f.percent(dev.second_reading_share, 0) })}</li>
      </ul>
      <h3>{r.precisionHeading}</h3>
      <ul className="lx-arch-list" data-arch="precision">
        {precision === null ? null : <li data-sample="stratified">{fillNext(r.precision.stratified, { right: f.count(precision.right), size: f.count(precision.size) })}</li>}
        {FACTS.precision.map((row) => <li key={row.key} data-sample={row.key}>{fillNext(r.precision[row.key], { right: f.count(row.right), resolved: f.count(row.resolved), refused: f.count(row.refused), notServed: f.count(row.not_served) })}</li>)}
      </ul>
      <p className="lx-arch-line lx-arch-honest">{fillNext(r.who, { sheet: f.count(FACTS.review.references) })}</p>
      <section className="lx-arch-note" aria-labelledby="arch-graph-h" data-arch="graph-rag">
        <h3 id="arch-graph-h">{r.graphHeading}</h3>
        <p>{r.graph}</p>
      </section>
      <section className="lx-arch-note lx-arch-fault" aria-labelledby="arch-refs-faults">
        <h3 id="arch-refs-faults">{r.faultsHeading}</h3>
        <p>{r.faults}</p>
      </section>
    </>
  );
}

// ---- 5. the funnel on one real answer --------------------------------------------------------------------------------------

/** The sources of the corpus level, in the order the page says them. */
const SOURCE_ORDER = Object.freeze(['named', 'lines_words', 'lines_meaning', 'articles_meaning', 'articles_words', 'transposes', 'replaces']);

/** A detector's or a source's ranks, in words: "meaning #1, words #5". */
function ranksOf(foundBy, names, copy, locale) {
  return Object.entries(obj(foundBy) ?? {}).map(([key, rank]) => fillNext(copy.funnel.rank, { name: names[key] ?? key, rank: groupedCount(rank, locale) })).join(nextCopy(locale).listSeparator);
}

/** The levels of the stored answer, read for the page: each `{ key, by, text, detail }`. */
export function funnelLevels(trace, ctx) {
  const { copy, f, locale } = ctx;
  const u = copy.funnel;
  const level = (name) => arr(trace?.trace?.levels).find((one) => one?.level === name) ?? null;
  const fields = Object.fromEntries(arr(trace?.trace?.question?.fields).map((field) => [field.name, field.value]));
  const list = nextCopy(locale).listSeparator;
  const out = [];
  const date = obj(fields.date);
  out.push({
    key: 'question', by: ['code', 'model'],
    text: fillNext(u.levels.question.text, {
      language: nextCopy(locale).languages[fields.language] ?? fields.language ?? f.count(null),
      date: date === null ? f.count(null) : txt(date.text) === null ? fillNext(u.noDate, { date: f.date(date.from) }) : txt(date.text),
      named: arr(fields.named_articles).length + arr(fields.named_acts).length === 0 ? u.noneNamed : [...arr(fields.named_articles), ...arr(fields.named_acts)].map(String).join(list),
      type: nextCopy(locale).questionTypes[fields.question_type] ?? fields.question_type ?? f.count(null),
      rows: f.count(arr(fields.dictionary_rows).length),
    }),
  });
  const corpus = level('corpus');
  if (corpus !== null) {
    const removed = arr(corpus.filters).reduce((sum, row) => sum + (isCount(row.removed) ? row.removed : 0), 0);
    out.push({
      key: 'corpus', by: tagsOf(corpus.by), level: corpus,
      text: fillNext(u.levels.corpus.text, {
        considered: f.count(corpus.considered),
        sources: SOURCE_ORDER.map((key) => [key, obj(corpus.sources)?.[key]]).filter(([, n]) => isCount(n) && n > 0).map(([key, n]) => fillNext(u.counts, { name: u.sourceNames[key] ?? key, count: groupedCount(n, locale) })).join(list),
        removed: f.count(removed),
        filters: arr(corpus.filters).map((row) => fillNext(u.counts, { name: u.filterNames[row.rule] ?? row.rule, count: groupedCount(row.removed, locale) })).join(list),
        shown: f.count(corpus.shown_to_model), kept: countedForm(u.texts, corpus.kept, locale),
        reason: arr(corpus.acts).map((act) => txt(act.reason)).filter(Boolean).join(' '),
      }),
    });
  }
  const document = level('document');
  if (document !== null) {
    out.push({
      key: 'document', by: tagsOf(document.by), level: document,
      text: fillNext(u.levels.document.text, {
        steps: f.count(arr(document.steps).length), picks: f.count(FACTS.budgets.walk_picks),
        stops: [...new Set(arr(document.final).map((row) => row.stopped))].map((stop) => u.stopNames[stop] ?? stop).join(list),
        articles: f.count(isCount(document.articles) ? document.articles : arr(document.articles).length),
      }),
    });
  }
  const detectors = level('detectors');
  if (detectors !== null) {
    const tally = {};
    for (const candidate of arr(detectors.candidates)) for (const key of Object.keys(obj(candidate.found_by) ?? {})) tally[key] = (tally[key] ?? 0) + 1;
    out.push({
      key: 'detectors', by: tagsOf(detectors.by), level: detectors,
      text: fillNext(u.levels.detectors.text, {
        n: f.count(arr(detectors.candidates).length),
        counts: Object.entries(tally).map(([key, n]) => fillNext(u.counts, { name: u.detectorNames[key] ?? key, count: groupedCount(n, locale) })).join(list),
        skipped: arr(detectors.skipped).map((row) => u.skippedNames[row.why] ?? `${u.detectorNames[row.detector] ?? row.detector} (${row.why})`).join(list) || u.noneFlagged,
      }),
    });
  }
  const arbiter = level('arbiter');
  if (arbiter !== null) {
    const roles = {};
    for (const candidate of arr(arbiter.candidates)) roles[candidate.role] = (roles[candidate.role] ?? 0) + 1;
    out.push({
      key: 'arbiter', by: tagsOf(arbiter.by), level: arbiter,
      text: fillNext(u.levels.arbiter.text, {
        shown: f.count(arr(arbiter.candidates).length), cap: f.count(FACTS.budgets.arbiter_shown),
        roles: Object.entries(roles).map(([role, n]) => (u.roleCounts[role] ? countedForm(u.roleCounts[role], n, locale) : `${groupedCount(n, locale)} ${role}`)).join(list),
      }),
    });
  }
  const read = level('read');
  if (read !== null) {
    const pending = arr(read.rounds).flatMap((round) => arr(round.pending_references));
    out.push({
      key: 'read', by: tagsOf(read.by), level: read,
      text: fillNext(u.levels.read.text, {
        read: f.count(read.articles_read), setBy: u.setByNames[read.read_amount_set_by] ?? read.read_amount_set_by ?? f.count(null),
        quoted: countedForm(u.linesQuoted, read.lines_quoted, locale), pending: pending.length === 0 ? u.noneFlagged : f.count(pending.length),
      }),
    });
  }
  const references = level('references');
  if (references !== null) {
    out.push({
      key: 'references', by: tagsOf(references.by), level: references,
      text: fillNext(u.levels.references.text, { trigger: u.triggerNames[references.trigger] ?? references.trigger ?? f.count(null), hops: '' }).trim(),
      hops: arr(references.hops).map((hop) => txt(hop.why)).filter(Boolean),
    });
  }
  const decision = level('decision');
  const consistency = level('consistency');
  if (decision !== null) {
    out.push({
      key: 'decision', by: tagsOf(decision.by), level: decision,
      text: fillNext(u.levels.decision.text, { rule: decision.rule, state: u.stateNames[decision.state] ?? decision.state, checked: f.count(consistency?.checked), dropped: countedForm(u.dropped, arr(consistency?.dropped).length, locale) }),
    });
  }
  return out;
}

/** What the trace holds at one level, shown under a fold. */
function LevelDetail({ row, ctx }) {
  const { copy, locale } = ctx;
  const u = copy.funnel;
  const lawLang = locale === 'fr' ? undefined : 'fr';
  const level = row.level;
  if (row.key === 'corpus') {
    return (
      <div className="lx-how-table-scroll">
        <table className="lx-how-table" data-arch="corpus-lines">
          <thead><tr>{['line', 'foundBy', 'tally'].map((key) => <th scope="col" key={key}>{u.lineColumns[key]}</th>)}</tr></thead>
          <tbody>
            {arr(level.lines).map((line) => (
              <tr key={line.id}><th scope="row"><code>{line.id}</code> <code>{line.act_id}{line.node_id ? ` › ${line.node_id}` : ''}</code></th><td>{ranksOf(line.found_by, u.sourceNames, copy, locale)}</td><td>{groupedCount(line.tally, locale)}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }
  if (row.key === 'document') {
    return (
      <>
        {arr(level.steps).map((step) => (
          <div key={step.depth} data-walk-step={step.depth}>
            <p className="lx-arch-line">{fillNext(u.stepLine, { depth: groupedCount(step.depth, locale), shown: groupedCount(step.shown, locale), by: u.byNames[step.by] ?? step.by })}</p>
            <div className="lx-how-table-scroll">
              <table className="lx-how-table" data-arch="walk-picks">
                <thead><tr>{['heading', 'tally', 'reason', 'stop'].map((key) => <th scope="col" key={key}>{u.pickColumns[key]}</th>)}</tr></thead>
                <tbody>
                  {arr(step.picked).map((pick) => (
                    <tr key={pick.node_id}><th scope="row" lang={lawLang}>{pick.heading}</th><td>{groupedCount(pick.tally, locale)}</td><td lang={lawLang}>{pick.reason}</td><td>{pick.stop ? u.stopNames[pick.stop] ?? pick.stop : u.open}</td></tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ))}
      </>
    );
  }
  if (row.key === 'arbiter') {
    return (
      <div className="lx-how-table-scroll">
        <table className="lx-how-table" data-arch="arbiter">
          <thead><tr>{['article', 'role', 'reason', 'foundBy'].map((key) => <th scope="col" key={key}>{u.arbiterColumns[key]}</th>)}</tr></thead>
          <tbody>
            {arr(level.candidates).map((candidate) => (
              <tr key={candidate.article_pk} data-role={candidate.role}><th scope="row" lang={lawLang}>{candidate.label}</th><td>{u.roleNames[candidate.role] ?? candidate.role}</td><td lang={lawLang}>{candidate.reason}</td><td>{ranksOf(candidate.found_by, u.detectorNames, copy, locale)}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }
  if (row.key === 'references') {
    return <ul className="lx-arch-list">{row.hops.map((hop) => <li key={hop} lang={lawLang}>{hop}</li>)}</ul>;
  }
  return null;
}

function FunnelPart({ ctx }) {
  const { copy, f, locale } = ctx;
  const u = copy.funnel;
  const trace = TRACE;
  const t = obj(trace.trace) ?? {};
  const levels = funnelLevels(trace, ctx);
  const lawLang = locale === 'fr' ? undefined : 'fr';
  const blocks = arr(trace.blocks);
  const typed = obj(trace.typed_answer) ?? {};
  const read = traceLevel('read');
  const arbiter = traceLevel('arbiter');
  const rejected = arr(arbiter?.candidates).filter((candidate) => candidate.role === 'reject');
  const walkOnly = rejected.filter((candidate) => { const keys = Object.keys(obj(candidate.found_by) ?? {}); return keys.length === 1 && keys[0] === 'outline'; });
  const inbound = arr(traceLevel('references')?.hops).find((hop) => hop.direction === 'inbound');
  const pending = arr(read?.rounds).flatMap((round) => arr(round.pending_references));
  const b = FACTS.budgets;
  const w = FACTS.walk;
  const token = txt(trace.meta?.replay);
  const until = (version) => (txt(version?.until) !== null ? fillNext(copy.data.example.untilDate, { until: f.date(version.until) }) : version?.until_known ? copy.data.example.untilOpen : copy.data.example.untilUnknown);
  return (
    <>
      <p className="lx-arch-say">{u.say}</p>
      <p className="lx-arch-line">{fillNext(u.asked, { date: f.date(FACTS.trace.asked), code: trace.meta?.build?.code ?? FACTS.code, data: trace.meta?.build?.data ?? FACTS.data })}</p>
      <p className="lx-arch-question" lang="fr" data-arch="question">{trace.question}</p>
      <ol className="lx-arch-ladder" data-arch="funnel">
        {levels.map((row, at) => (
          <li key={row.key} data-level={row.key}>
            <span className="lx-arch-flow-n" aria-hidden="true">{at + 1}</span>
            <div className="lx-arch-ladder-name"><h3>{u.levels[row.key].name}</h3>{row.by.map((by) => <Tag key={by} by={by} copy={copy} />)}</div>
            <div className="lx-arch-ladder-body">
              <p>{row.text}</p>
              {row.key === 'references' ? <ul className="lx-arch-hops">{row.hops.map((hop) => <li key={hop} lang={lawLang}>{hop}</li>)}</ul> : null}
              {['corpus', 'document', 'arbiter'].includes(row.key) ? (
                <details className="lx-how-fold" data-arch-fold={`trace-${row.key}`}>
                  <summary>{u.detailSummary}</summary>
                  <LevelDetail row={row} ctx={ctx} />
                </details>
              ) : null}
            </div>
          </li>
        ))}
      </ol>
      <p className="lx-arch-line" data-arch="totals">{fillNext(u.totals, { calls: f.count(arr(t.calls).length), model: f.count(t.model_calls), embedding: f.count(t.embedding_calls), usd: f.usd(t.cost_usd), seconds: f.dec(isCount(t.ms) ? t.ms / 1000 : null, 1) })}</p>
      <section className="lx-arch-answer" aria-labelledby="arch-answer-h" data-arch="answer">
        <h3 id="arch-answer-h">{u.answerHeading} <span className="lx-arch-chip" data-chip="state">{u.stateNames[trace.state] ?? trace.state}</span></h3>
        {obj(typed.law_words) === null ? null : (
          <div className="lx-arch-typed">
            <p><strong>{u.lawWords}</strong> <q className="lx-arch-law" lang={lawLang}>{typed.law_words.text}</q> <span className="lx-arch-cite" lang={lawLang}>({typed.law_words.label})</span></p>
            {obj(typed.value) === null ? null : <p>{fillNext(u.value, { value: typed.value.text, words: typed.value.from_words })}</p>}
            {obj(typed.summary) === null ? null : <p><strong>{u.summary}</strong> <span lang={lawLang}>{typed.summary.text}</span></p>}
          </div>
        )}
        <ul className="lx-arch-blocks">
          {blocks.map((block) => (
            <li key={`${block.article_pk}-${block.role}`} data-role={block.role} data-via={block.retrieved_via}>
              <p className="lx-arch-block-head"><span className="lx-arch-chip" data-chip={block.role}>{u.roleNames[block.role] ?? block.role}</span> <strong lang={lawLang}>{block.article}</strong> <span lang={lawLang}>{block.act_title}</span></p>
              <p className="lx-next-note">{fillNext(u.versionLine, { from: f.date(block.version?.from), until: until(block.version) })} {u.whyBy[block.why_by] ?? ''} <span lang={lawLang}>{block.why}</span></p>
              <blockquote className="lx-arch-quote" lang={lawLang}>
                {arr(block.quote).map((line) => <p key={line.n}><span className="lx-arch-ln">{line.n}</span> {line.text}</p>)}
              </blockquote>
            </li>
          ))}
        </ul>
        {token === null ? null : <p className="lx-how-all"><a href={`/${locale}/#r=${encodeURIComponent(token)}`} data-arch="replay">{u.replay}</a></p>}
      </section>
      <section className="lx-arch-note" aria-labelledby="arch-observe-h" data-arch="observations">
        <h3 id="arch-observe-h">{u.observationsHeading}</h3>
        <ul className="lx-arch-list">
          {obj(blocks[0]?.found_by) !== null ? <li>{fillNext(u.observations.found, { foundBy: ranksOf(blocks[0].found_by, u.detectorNames, copy, locale) })}</li> : null}
          <li>{fillNext(u.observations.rejected, { rejected: f.count(rejected.length), shown: f.count(arr(arbiter?.candidates).length), walkOnly: f.count(walkOnly.length) })}</li>
          {pending.length === 0 && inbound ? <li>{fillNext(u.observations.exception, { why: inbound.why })}</li> : null}
          {obj(typed.value) !== null ? <li>{fillNext(u.observations.value, { value: typed.value.text, words: typed.value.from_words })}</li> : null}
        </ul>
      </section>
      <section className="lx-arch-note" aria-labelledby="arch-topk-h" data-arch="topk">
        <h3 id="arch-topk-h">{u.topkHeading}</h3>
        <p>{fillNext(u.topk, { small: f.count(b.small_articles), chars: f.count(b.small_chars), list: f.count(b.list_articles), max: f.count(b.read_articles), maxChars: f.count(b.read_chars), read: f.count(read?.articles_read), n: f.count(w.n), mean: f.dec(w.read_with, 1) })}</p>
        <p>{fillNext(u.topkHonest, { without: f.count(w.right_without), with: f.count(w.right_with), p: f.p(w.p), calls: f.dec(w.calls_extra, 1), seconds: f.dec(w.seconds_extra, 1), usd: f.usd(w.usd_extra) })}</p>
      </section>
    </>
  );
}

// ---- 6. evaluation ---------------------------------------------------------------------------------------------------------

/** One run's right answers with their interval, drawn on a common scale (decorative: the figures stand beside it). */
function IntervalMark({ right, n, interval, main, label }) {
  const low = 50; const high = 90;
  const x = (value) => Math.max(0, Math.min(300, ((value - low) / (high - low)) * 300));
  if (!isCount(right) || !isCount(n) || n === 0) return null;
  const share = (100 * right) / n;
  const [a, b] = Array.isArray(interval) && isCount(interval[0]) && isCount(interval[1]) ? interval : [share, share];
  return (
    <svg className="lx-arch-interval" viewBox="0 0 300 20" width="300" height="20" role="img" aria-label={label}>
      <title>{label}</title>
      {[60, 70, 80].map((tick) => <line key={tick} x1={x(tick)} x2={x(tick)} y1="2" y2="18" className="lx-arch-interval-grid" />)}
      <line x1={x(a)} x2={x(b)} y1="10" y2="10" className={main ? 'lx-arch-interval-line lx-arch-main' : 'lx-arch-interval-line'} />
      <circle cx={x(share)} cy="10" r="5" className={main ? 'lx-arch-interval-dot lx-arch-main' : 'lx-arch-interval-dot'} />
    </svg>
  );
}

function EvaluationPart({ ctx }) {
  const { copy, live, f, locale } = ctx;
  const v = copy.evaluation;
  const results = obj(live.results.data) ?? {};
  const totals = obj(results.totals) ?? {};
  const intervals = obj(totals.intervals) ?? {};
  const set = obj(results.set) ?? {};
  const n = isCount(set.n) ? set.n : null;
  const retrieval = obj(results.retrieval) ?? {};
  const misses = obj(results.misses) ?? {};
  const ablation = arr(obj(results.ablation)?.rows);
  const noise = obj(results.noise) ?? {};
  const previous = obj(results.previous) ?? {};
  const baseline = obj(results.baseline) ?? {};
  const names = nextCopy(locale).results2;
  const range = (pair) => (Array.isArray(pair) ? [f.dec(pair[0], 1), `${f.dec(pair[1], 1)}${locale === 'fr' ? NB : ''}%`] : [f.count(null), f.count(null)]);
  const [rLow, rHigh] = range(intervals.right);
  const [wLow, wHigh] = range(intervals.wrong);
  const r1 = ablation.find((row) => row.what === 'all' && row.run === 'R1');
  const r2 = ablation.find((row) => row.what === 'all' && row.run === 'R2');
  const offs = ablation.filter((row) => row.what !== 'all');
  const chart = [
    ...(r1 ? [{ key: 'R1', label: v.rows.R1, right: r1.right, n: r1.n, interval: r1.interval, main: true, effect: '' }] : []),
    ...(r2 ? [{ key: 'R2', label: v.rows.R2, right: r2.right, n: r2.n, interval: r2.interval, main: true, effect: Array.isArray(noise.discordant) ? fillNext(v.effect.noise, { a: f.count(noise.discordant[0]), b: f.count(noise.discordant[1]) }) : '' }] : []),
    ...offs.map((row) => ({ key: row.what, label: names.ablation[row.what] ?? row.what, right: row.right, n: row.n, interval: row.interval, main: false, effect: fillNext(row.effect === 'measured' ? v.effect.measured : v.effect.none, { p: f.p(row.p_vs_R1) }) })),
    ...(isCount(previous.right) ? [{ key: 'previous', label: fillNext(v.rows.previous, { code: obj(previous.build)?.code ?? f.count(null) }), right: previous.right, n: previous.n, interval: previous.interval, main: false, effect: fillNext(v.effect.pairs, { better: f.count(previous.better), worse: f.count(previous.worse), p: f.p(previous.p) }) }] : []),
    ...(isCount(baseline.right) ? [{ key: 'naive', label: v.rows.naive, right: baseline.right, n: baseline.n, interval: baseline.interval, main: false, effect: fillNext(v.effect.pairs, { better: f.count(baseline.lex_right_naive_not), worse: f.count(baseline.naive_right_lex_not), p: f.p(baseline.p) }) }] : []),
  ];
  const w = FACTS.walk;
  const fs = FACTS.final_score;
  const sc = FACTS.sealed_check;
  return (
    <>
      <p className="lx-arch-say">{v.say}</p>
      <Tiles
        name="evaluation"
        rows={[
          ['right', f.count(totals.right), fillNext(v.tiles.right, { n: f.count(n), pct: f.share(totals.right, n), ci: f.ci, low: rLow, high: rHigh })],
          ['wrong', f.count(totals.wrong), fillNext(v.tiles.wrong, { low: wLow, high: wHigh })],
          ['retrieval', f.count(retrieval.right_article_read), fillNext(v.tiles.retrieval, { of: f.count(retrieval.of) })],
          ['misses', f.count(isCount(n) && isCount(totals.right) ? n - totals.right : null), fillNext(v.tiles.misses, { corpus: f.count(misses.corpus_gap), retrieval: f.count(misses.retrieval), reading: f.count(misses.reading), gate: f.count(misses.gate) })],
        ]}
      />
      <h3>{v.chartHeading}</h3>
      <p className="lx-arch-line">{fillNext(v.chartNote, { n: f.count(n), ci: f.ci })} {txt(names.ablationRule) ?? ''}</p>
      <div className="lx-how-table-scroll">
        <table className="lx-how-table lx-arch-chart" data-arch="runs">
          <thead>
            <tr>
              <th scope="col">{v.chartColumns.run}</th><th scope="col">{v.chartColumns.right}</th>
              <th scope="col" className="lx-arch-chart-scale">{fillNext(v.chartColumns.interval, { ci: f.ci })}<span className="lx-arch-ticks" aria-hidden="true">{[50, 60, 70, 80, 90].map((tick) => <span key={tick}>{`${tick}${locale === 'fr' ? NB : ''}%`}</span>)}</span></th>
              <th scope="col">{v.chartColumns.effect}</th>
            </tr>
          </thead>
          <tbody>
            {chart.map((row) => (
              <tr key={row.key} data-run={row.key} className={row.main ? 'lx-arch-main-row' : undefined}>
                <th scope="row">{row.label}</th>
                <td>{f.count(row.right)}</td>
                <td className="lx-arch-chart-cell"><IntervalMark right={row.right} n={row.n} interval={row.interval} main={row.main} label={`${row.label}: ${f.count(row.right)} / ${f.count(row.n)}`} /></td>
                <td>{row.effect}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <details className="lx-how-fold" data-arch-fold="slices">
        <summary>{v.slicesSummary}</summary>
        <div className="lx-how-table-scroll">
          <table className="lx-how-table lx-arch-num" data-arch="slices">
            <thead><tr>{['slice', 'n', 'right', 'interval', 'r2', 'previous', 'wrong'].map((key) => <th scope="col" key={key}>{fillNext(v.slicesColumns[key], { ci: f.ci })}</th>)}</tr></thead>
            <tbody>
              {arr(results.slices).filter((row) => obj(row) !== null && txt(row.slice) !== null).map((row) => {
                const [low, high] = range(row.interval);
                return (
                  <tr key={row.slice} data-slice={row.slice}><th scope="row">{names.slices[row.slice] ?? row.slice}</th><td>{f.count(row.n)}</td><td>{f.count(row.right)}</td><td>{`${low}–${high}`}</td><td>{f.count(row.right_r2)}</td><td>{f.count(row.right_t8)}</td><td>{f.count(row.wrong)}</td></tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </details>
      <section className="lx-arch-note" aria-labelledby="arch-walk-h" data-arch="walk">
        <h3 id="arch-walk-h">{v.walkHeading}</h3>
        <p>{fillNext(v.walk, {
          without: f.count(w.right_without), with: f.count(w.right_with), n: f.count(w.n), low: f.dec(w.gain_low, 1).replace('-', '−'), high: `+${f.dec(w.gain_high, 1)}`, detectable: f.dec(w.detectable_points, 1),
          found: f.count(w.walk_found), expected: f.count(w.expected), others: f.count(w.others_found), bigA: f.count(w.recall_big_codes[0]), bigB: f.count(w.recall_big_codes[1]),
          smallA: f.count(w.recall_small_texts[0]), smallB: f.count(w.recall_small_texts[1]), mWithout: f.count(w.meaning_only_without), mWith: f.count(w.meaning_only_with),
          calls: f.dec(w.calls_extra, 1), seconds: f.dec(w.seconds_extra, 1), usd: f.usd(w.usd_extra),
        })}</p>
      </section>
      <h3>{v.setsHeading}</h3>
      <ul className="lx-arch-list" data-arch="sets">
        <li>{fillNext(v.dev, { devN: f.count(FACTS.refs_experiment.set_n) })}</li>
        <li>{fillNext(v.sealed, { n: f.count(n), writtenBy: txt(obj(set.written_by)?.[locale]) ?? f.count(null) })}</li>
        <li>{fillNext(v.recheck, { asRunRight: f.count(sc.right_as_run), asRunWrong: f.count(sc.wrong_as_run), right: f.count(sc.right_checked), wrong: f.count(sc.wrong_checked), random: f.count(sc.random_rights_checked) })}</li>
        <li>{fillNext(v.fresh, { n: f.count(fs.n), date: f.date(FACTS.sources.final_score.date), right: f.count(fs.right), firm: f.count(fs.firm), firmWrong: countedForm(v.firmWrong, fs.firm_wrong, locale) })}</li>
      </ul>
      <p className="lx-how-all"><a href={`/${locale}/results.html`}>{v.resultsLink}</a></p>
    </>
  );
}

// ---- 7. operations and governance -----------------------------------------------------------------------------------------

function OperationsPart({ ctx }) {
  const { copy, live, f, locale } = ctx;
  const o = copy.operations;
  const config = live.config.data;
  const models = modelRows(config, locale);
  const residency = residencyLine(config, locale);
  const kept = keptLine(config, locale);
  const results = obj(live.results.data) ?? {};
  const cost = obj(results.cost) ?? {};
  const latency = obj(results.latency) ?? {};
  const t = obj(TRACE.trace) ?? {};
  const fs = FACTS.final_score;
  const seconds = (value) => (isCount(value) ? fillNext(o.seconds, { value: decimal(value, locale, 1) }) : f.count(null));
  const liveEngine = txt(obj(obj(config)?.build)?.engine);
  const measured = txt(obj(results.build)?.engine);
  const dictionary = obj(obj(live.dictionary.data)?.counts) ?? {};
  const routing = obj(obj(obj(live.coverage.data)?.index)?.routing_lines) ?? {};
  const review = FACTS.review;
  return (
    <>
      <p className="lx-arch-say">{o.say}</p>
      <div className="lx-arch-split">
        <section aria-labelledby="arch-models-h">
          <h3 id="arch-models-h">{o.modelsHeading}</h3>
          {models.length === 0 ? <p className="lx-arch-line">{f.count(null)}</p> : (
            <table className="lx-how-table lx-next-counts" data-arch="models">
              <tbody>{models.map((row) => <tr key={`${row.key}-${row.deployment}`} data-model-role={row.key}><th scope="row">{row.role}</th><td><code>{row.deployment}</code></td></tr>)}</tbody>
            </table>
          )}
        </section>
        <section aria-labelledby="arch-where-h">
          <h3 id="arch-where-h">{o.whereHeading}</h3>
          <p className="lx-arch-line">{residency ?? f.count(null)}</p>
          <h3>{o.keptHeading}</h3>
          <p className="lx-arch-line">{kept ?? f.count(null)}</p>
        </section>
      </div>
      <h3>{o.costHeading}</h3>
      <div className="lx-how-table-scroll">
        <table className="lx-how-table lx-arch-num" data-arch="cost">
          <thead><tr>{['where', 'usd', 'calls', 'median', 'slow'].map((key) => <th scope="col" key={key}>{o.costColumns[key]}</th>)}</tr></thead>
          <tbody>
            <tr data-cost="sealed"><th scope="row">{o.costRows.sealed}</th><td>{f.usd(obj(cost.usd_per_answer)?.mean)}</td><td>{f.dec(obj(cost.model_calls_per_answer)?.mean, 1)}</td><td>{seconds(isCount(latency.median_ms) ? latency.median_ms / 1000 : null)}</td><td>{seconds(isCount(latency.p90_ms) ? latency.p90_ms / 1000 : null)}</td></tr>
            <tr data-cost="fresh"><th scope="row">{o.costRows.fresh}</th><td>{f.usd(fs.usd)}</td><td>{f.dec(fs.calls, 1)}</td><td>{seconds(fs.median_s)}</td><td>{seconds(fs.p90_s)}</td></tr>
            <tr data-cost="example"><th scope="row">{o.costRows.example}</th><td>{f.usd(t.cost_usd)}</td><td>{f.count(t.model_calls)}</td><td>{seconds(isCount(t.ms) ? t.ms / 1000 : null)}</td><td>{copy.missing}</td></tr>
          </tbody>
        </table>
      </div>
      {txt(obj(cost.price_source)?.[locale]) === null ? null : <p className="lx-next-note">{cost.price_source[locale]}</p>}
      <div className="lx-arch-split">
        <section aria-labelledby="arch-replay-h">
          <h3 id="arch-replay-h">{o.replayHeading}</h3>
          <p className="lx-arch-line">{fillNext(o.replay, { signature: FACTS.budgets.replay_signature, calls: countedForm(o.calls, FACTS.trace.replay_model_calls, locale) })}</p>
        </section>
        <section aria-labelledby="arch-print-h" data-arch="fingerprint">
          <h3 id="arch-print-h">{o.fingerprintHeading}</h3>
          <p className="lx-arch-line">
            {fillNext(o.fingerprint, { live: liveEngine ?? f.count(null), measured: measured ?? f.count(null) })}{' '}
            {liveEngine === null || measured === null ? null : liveEngine === measured ? o.fingerprintSame : o.fingerprintOther}
          </p>
        </section>
      </div>
      <h3>{o.uncheckedHeading}</h3>
      <ul className="lx-arch-list" data-arch="unchecked">
        <li>{fillNext(o.unchecked.dictionary, { rows: f.count(dictionary.rows), validated: isCount(dictionary.validated) ? countedForm(copy.indexes.validated, dictionary.validated, locale) : f.count(null) })}</li>
        <li>{fillNext(o.unchecked.routing, { lines: f.count(routing.lines) })}</li>
        <li>{o.unchecked.references}</li>
        <li>{o.unchecked.shapes}</li>
        <li>{fillNext(o.unchecked.sheets, { date: f.date(review.date), dictionary: f.count(review.dictionary_rows), texts: f.count(review.routing_texts), lines: f.count(review.routing_lines), references: f.count(review.references) })}</li>
      </ul>
    </>
  );
}

// ---- 8. what comes next, and the sources -----------------------------------------------------------------------------------

function NextPart({ ctx }) {
  const x = ctx.copy.next;
  return (
    <>
      <ol className="lx-arch-next" data-arch="next">{x.items.map((item) => <li key={item}>{item}</li>)}</ol>
      <p className="lx-next-note">{x.note}</p>
    </>
  );
}

function Sources({ ctx }) {
  const { copy, live, f } = ctx;
  const s = copy.sources;
  return (
    <details className="lx-how-fold lx-arch-sources" data-arch-fold="sources" id="arch-sources">
      <summary>{s.heading}</summary>
      <p className="lx-arch-line">{s.live}</p>
      <ul className="lx-arch-list">{Object.entries(LIVE_ADDRESSES).map(([key, address]) => <li key={key} data-live={key} data-live-state={live[key]?.state ?? 'loading'}><code>GET {address}</code> ({s.stateNames[live[key]?.state ?? 'loading']})</li>)}</ul>
      <p className="lx-arch-line">{fillNext(s.files, { code: FACTS.code, data: FACTS.data, engine: FACTS.engine })}</p>
      <ul className="lx-arch-list">{Object.entries(FACTS.sources).map(([key, row]) => <li key={key} data-source={key}>{fillNext(s.line, { name: s.names[key] ?? key, file: row.file, date: f.date(row.date) })}</li>)}</ul>
    </details>
  );
}

// ---- the page --------------------------------------------------------------------------------------------------------------

const BODIES = Object.freeze({ corpus: CorpusPart, data: DataPart, indexes: IndexesPart, references: ReferencesPart, funnel: FunnelPart, evaluation: EvaluationPart, operations: OperationsPart, next: NextPart });

/** The dossier drawn from the live reads (`live`: each address's `{ state, data }`) and the time they were made (`at`). */
export function ArchitectureView({ live = loadingLive(), at = null, locale = CHROME_LOCALE }) {
  const copy = dossierCopy(locale);
  const whole = liveState(live);
  const f = formatters(locale, whole.state === 'loading' ? copy.waiting : copy.missing);
  const ctx = { copy, live, f, locale, example: exampleOf(live) };
  const when = readAt(at, locale);
  const status = whole.state === 'loading' || when === null && whole.state !== 'failed' ? copy.status.loading
    : whole.state === 'failed' ? copy.status.failed
      : whole.state === 'partial' ? fillNext(copy.status.partial, { date: when.date, time: when.time, missing: whole.missing.map((key) => LIVE_ADDRESSES[key]).join(nextCopy(locale).listSeparator) })
        : fillNext(copy.status.ready, { date: when.date, time: when.time });
  return (
    <div className="lx-arch" data-next-page="architecture" data-next-state={whole.state}>
      <header className="lx-arch-head">
        <h1>{copy.heading}</h1>
        <p className="lx-arch-lede">{copy.lede}</p>
        <p className="lx-arch-status" role="status" data-arch-status={whole.state}>{status} <a href={`/${locale}/evidence.html`}>{copy.howLink}</a></p>
        <nav className="lx-arch-toc" aria-label={copy.tocLabel}>
          <ol>{PARTS.map((part) => <li key={part.key}><a href={`#arch-${part.key}`}>{copy.partNames[part.key]}</a></li>)}</ol>
        </nav>
      </header>
      {PARTS.map((part, at2) => {
        const Body = BODIES[part.key];
        return (
          <section className="lx-arch-part" id={`arch-${part.key}`} key={part.key} aria-labelledby={`arch-${part.key}-h`} data-arch-part={part.key}>
            <PartHead part={part} at={at2} heading={copy[part.key].heading} copy={copy} />
            <Body ctx={ctx} />
          </section>
        );
      })}
      <Sources ctx={ctx} />
    </div>
  );
}

/** The dossier, live: built in its loading state; the live addresses asked once the page is drawn. */
export function NextArchitecture({ fetchImpl = undefined, locale = CHROME_LOCALE }) {
  const { live, at } = useLiveDossier(fetchImpl);
  return <ArchitectureView live={live} at={at} locale={locale} />;
}
