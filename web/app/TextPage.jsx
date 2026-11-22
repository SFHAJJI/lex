// The text page (the owner's brief of 2026-11-15, section 4; the validated mockup's V2Read and V3Compare): one page per text,
// its versions on a time axis, its articles, its wording, and two versions compared in place.
//
// `text.html` under each interface language; its state is in the query (`text-address.mjs`): the text, the version's date,
// the article, the compare pair, the view. The page as built is the same for every text (the server renders no text here),
// so the server render and the browser's first render are the loading state, and the page asks, once hydrated:
//   - `dossier` for the text's own title (trap 2) and, for an EU act, its wording;
//   - `changes_in_period` over the text's whole history, once: every version and the articles changed in it (the bars);
//   - `evidence_bundle` for the version read, with its contents (every article, its page), then the pages read;
//   - `diff` for each version with changes against the one before, after first paint (which versions changed each article);
//   - in compare mode, `diff` for the two versions, with the old and new wording of each changed article.
// About a dozen requests for the insurance contract law; never more than the reader's per-minute allowance (R109) at once.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { askV3 } from '../scripts/v3-client.mjs';
import { CHROME_LOCALE, countedEntry, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { DOCK_EVENT, MARK_EVENT, OPEN_EVENT, quotedParagraphs, textHref, textStateOf } from '../scripts/text-address.mjs';
import {
  alignParagraphs, articleNumberOf, articleParts, axisGeometry, changeIndex, comparable, ownTitleOf, sameVisibleWording, versionAt, versionsOf,
} from '../scripts/text-page.mjs';
import { todayInLuxembourg } from '../scripts/temporal.mjs';
import { FindIcon } from './Document.jsx';
import { actNameOf } from '../scripts/act-names.mjs';
import { countedForm, fillNext, nextCopy, numericDate as nextDate } from '../scripts/next-copy.mjs';
import { cleanTitle, identifierOf, oneLine, wordingLine } from '../scripts/next-view.mjs';

export const LIVE_TEXT_ROOT = 'live-text-root';

const DIFF_CONCURRENCY = 2;
const DIFF_CAP = 30;

/** dd/mm/yyyy in both interface languages (the brief, section 9). */
export function numericDate(date) {
  const match = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/u.exec(date ?? '');
  return match === null ? '' : `${match[3]}/${match[2]}/${match[1]}`;
}

/** A dd/mm/yyyy the reader typed, as yyyy-mm-dd, or null. */
export function typedDate(value) {
  const match = /^\s*([0-9]{1,2})[/.-]([0-9]{1,2})[/.-]([0-9]{4})\s*$/u.exec(value ?? '');
  if (match === null) return null;
  const date = `${match[3]}-${match[2].padStart(2, '0')}-${match[1].padStart(2, '0')}`;
  const [y, m, d] = date.split('-').map(Number);
  const check = new Date(Date.UTC(y, m - 1, d));
  return check.getUTCFullYear() === y && check.getUTCMonth() === m - 1 && check.getUTCDate() === d ? date : null;
}

/**
 * A copy template filled with the values it has a place for: a value it names nowhere is left out rather than refused (one
 * template of a pair, "in force today" and "applicable until", names fewer places than the other).
 */
const fill = (template, values = {}) => fillText(template, Object.fromEntries(Object.entries(values).filter(([name]) => template.includes(`{${name}}`))));
const grouped = (count) => count.toLocaleString(CHROME_LOCALE === 'fr' ? 'fr-FR' : 'en-GB');
const counted = (entry, count, values = {}) => fill(countedEntry(entry, count), { ...values, count: grouped(count) });

/** The language a text is read in: a Luxembourg text in French; an EU act in the interface's language when held. */
function readingLanguage(publisher, available) {
  if (publisher !== 'eu-eurlex') return 'fra';
  const wanted = CHROME_LOCALE === 'fr' ? 'fra' : 'eng';
  return Array.isArray(available) && available.length > 0 && !available.includes(wanted) ? available[0] : wanted;
}

/** The EU act's title in `language` (the long title), or null. */
function europeTitle(dossier, language) {
  const entries = dossier?.titles?.entries ?? [];
  const pick = entries.find((entry) => entry.kind === 'title' && entry.language === language) ?? entries.find((entry) => entry.kind === 'title');
  if (typeof pick?.text !== 'string') return null;
  const long = pick.text.replace(/\s*\((?:Text with EEA relevance|Texte présentant de l['’]intérêt pour l['’]EEE)\)\s*$/u, '');
  const short = /\(([^()]{8,80})\)\s*$/u.exec(long)?.[1];
  return short ? short.charAt(0).toUpperCase() + short.slice(1) : long;
}

/** An EU article's parts: the text runs "Article 5 Principles … 1. Personal data shall be: (a) …", spaces kept. */
function europeParts(text) {
  const body = String(text ?? '').replace(/^\s*Article\s+(?:premier|first|[0-9]+[a-z]?)\s*/iu, '');
  const firstPoint = body.search(/(?:^|\s)(?:1\.\s|\(1\)\s)/u);
  let heading = null;
  let rest = body;
  if (firstPoint > 0 && firstPoint <= 160) {
    heading = body.slice(0, firstPoint).trim();
    rest = body.slice(firstPoint).trim();
  }
  const blocks = rest.split(/\s(?=[0-9]{1,2}\.\s[A-ZÀ-Ý])|\s(?=\([a-z]\)\s)/u).map((block) => block.trim()).filter(Boolean);
  const paragraphs = blocks.map((block) => {
    const match = /^([0-9]{1,2}\.|\([a-z]\))\s+/u.exec(block);
    return match === null ? { marker: null, text: block } : { marker: match[1], text: block.slice(match[0].length) };
  });
  return { heading, paragraphs };
}

/** The ten-tick strip: one tick per version held, indigo where the article's wording changed. */
function Strip({ versions, changedDates }) {
  if (versions.length < 2) return null;
  const width = versions.length * 8 - 3;
  return (
    <svg className="lx-strip" width={width} height="12" viewBox={`0 0 ${width} 12`} aria-hidden="true" focusable="false">
      {versions.map((version, index) => (
        <rect key={version.date} x={index * 8} y="0" width="5" height="12" rx="1.5" className={changedDates.includes(version.date) ? 'lx-tick lx-tick-changed' : 'lx-tick'} />
      ))}
    </svg>
  );
}

/** The time axis (V2Read): one mark per version in proportion to the calendar, the bars, the interval read, the pair compared. */
function Axis({ versions, today, width, read, compare, pending, onMark }) {
  const copy = liveChrome().text;
  const geometry = axisGeometry(versions, today, width);
  const base = 68;
  const max = Math.max(1, ...versions.map((v) => v.bar ?? 0));
  const height = (bar) => 14 + 26 * Math.sqrt(Math.max(0, bar - 1) / Math.max(1, max - 1));
  const selected = new Set(compare ? [compare.from, compare.to] : read ? [read.date] : []);
  const pill = (date, filled, key) => {
    const x = Math.min(Math.max(geometry.at(date), 44), width - 44);
    return (
      <g key={key} className={filled ? 'lx-pill lx-pill-filled' : 'lx-pill'}>
        <rect x={x - 42} y="4" width="84" height="22" rx="11" />
        <text x={x} y="19.5" textAnchor="middle">{numericDate(date)}</text>
      </g>
    );
  };
  const readIndex = read ? versions.findIndex((v) => v.date === read.date) : -1;
  const intervalEnd = readIndex >= 0 && readIndex < versions.length - 1 ? versions[readIndex + 1].date : today;
  return (
    <svg className="lx-axis" width={width} height="104" viewBox={`0 0 ${width} 104`} role="group" aria-label={copy.axisLabel}>
      <line className="lx-axis-line" x1={geometry.x0} y1={base} x2={geometry.x1} y2={base} />
      {geometry.ticks.map((tick) => (
        <g key={tick.year}>
          <line className="lx-axis-tick" x1={tick.x} y1={base} x2={tick.x} y2={base + 5} />
          <text className="lx-axis-year" x={tick.x} y="88" textAnchor="middle">{tick.year}</text>
        </g>
      ))}
      <text className="lx-axis-today" x={geometry.x1} y="88" textAnchor="end">{copy.today}</text>
      <line className="lx-axis-end" x1={geometry.x1} y1={base - 6} x2={geometry.x1} y2={base + 6} />
      {compare ? <rect className="lx-axis-span" x={geometry.at(compare.from)} y="30" width={Math.max(2, geometry.at(compare.to) - geometry.at(compare.from))} height={base - 30} /> : null}
      {!compare && read ? <line className="lx-axis-read" x1={geometry.at(read.date)} y1={base} x2={geometry.at(intervalEnd)} y2={base} /> : null}
      {versions.map((version, index) => {
        const x = geometry.at(version.date);
        const isSelected = selected.has(version.date);
        const label = version.first
          ? fill(copy.markFirst, { date: numericDate(version.date) })
          : !version.textHeld
            ? fill(copy.markNotHeld, { date: numericDate(version.date) })
            : fill(countedEntry(copy.markLabel, version.bar ?? 0), { date: numericDate(version.date), count: grouped(version.bar ?? 0) });
        const kind = version.first ? 'first' : !version.textHeld ? 'notheld' : (version.bar ?? 0) === 0 ? 'zero' : 'bar';
        const h = kind === 'bar' ? height(version.bar) : 0;
        return (
          <g key={version.date} className={`lx-mark lx-mark-${kind}${isSelected ? ' lx-mark-selected' : ''}${pending === version.date ? ' lx-mark-pending' : ''}`}>
            <a href="#" role="button" aria-label={label} aria-pressed={isSelected ? 'true' : 'false'} onClick={(event) => { event.preventDefault(); onMark(version.date); }}>
              <rect className="lx-mark-hit" x={x - 9} y="20" width="18" height={base - 12} />
              {kind === 'bar' ? <rect className="lx-mark-bar" x={x - 3.5} y={base - h} width="7" height={h} rx="3.5" /> : null}
              {kind === 'zero' ? <circle className="lx-mark-zero" cx={x} cy={base} r="4.5" /> : null}
              {kind === 'first' ? <circle className="lx-mark-first" cx={x} cy={base} r="4.5" /> : null}
              {kind === 'notheld' ? <line className="lx-mark-notheld" x1={x} y1={base - 14} x2={x} y2={base} /> : null}
              {kind === 'first' && index === 0 ? <text className="lx-mark-count" x={x} y={base - 12} textAnchor="middle">{copy.firstHeld}</text> : null}
              {kind === 'bar' || kind === 'zero' ? (
                <text className={(version.bar ?? 0) >= 5 ? 'lx-mark-count lx-mark-count-strong' : 'lx-mark-count'} x={x} y={base - h - 6 - (kind === 'zero' ? 6 : 0)} textAnchor="middle">{grouped(version.bar ?? 0)}</text>
              ) : null}
            </a>
          </g>
        );
      })}
      {compare ? [pill(compare.from, false, 'from'), pill(compare.to, true, 'to')] : read ? pill(read.date, true, 'read') : null}
    </svg>
  );
}

/** The paragraphs of an article, numbered points apart; the ones an answer quotes are marked (`marked`, their indexes). */
function Paragraphs({ parts, marked = [] }) {
  return parts.paragraphs.map((paragraph, index) => (
    <div className={marked.includes(index) ? 'lx-para lx-para-quoted' : 'lx-para'} data-quoted={marked.includes(index) ? '' : undefined} key={index}>
      <span className="lx-para-marker">{paragraph.marker ?? ''}</span>
      <p>{paragraph.text}</p>
    </div>
  ));
}

/** "Source and citation" (the only place identifiers and digests appear): a disclosure under the article's margin. */
function SourceAndCitation({ article, version }) {
  const copy = liveChrome().text.citation;
  return (
    <details className="lx-cite">
      <summary>{liveChrome().text.sourceAndCitation}</summary>
      <div className="lx-cite-panel">
        <dl>
          {article?.official_source ? (<><dt>{copy.official}</dt><dd><a href={article.official_source} rel="noopener noreferrer">{article.official_source}</a></dd></>) : null}
          {version ? (<><dt>{copy.version}</dt><dd>{numericDate(version.date)}</dd></>) : null}
          {article?.publisher_id ? (<><dt>{copy.article}</dt><dd><code>{article.publisher_id}</code></dd></>) : null}
          {article?.article_permalink ? (<><dt>{copy.permalink}</dt><dd><code>{article.article_permalink}</code></dd></>) : null}
          {article?.wording_sha256 ? (<><dt>{copy.wording}</dt><dd><code>{article.wording_sha256}</code></dd></>) : null}
        </dl>
      </div>
    </details>
  );
}

/** How many references of one side are listed before "show all". */
const REFERENCES_SHOWN = 8;

/** The address the side build's engine answers an article's references at, named as the current site names the article. */
export function referencesAddress(identifier, article, date) {
  const work = /^\/lu-legilux\/([^/?#]+)$/u.exec(identifier ?? '')?.[1];
  if (work === undefined || typeof article !== 'string' || article.length === 0) return null;
  return `/api/next/article-by?work=${encodeURIComponent(work)}&anchor=${encodeURIComponent(article)}${/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(date ?? '') ? `&date=${date}` : ''}`;
}

/** One side of an article's references ("Refers to", "Cited by"): each with its type, how sure it is, and a link when it can be opened. */
function ReferenceList({ side, heading, entries, identifier, onOpen }) {
  const copy = nextCopy(CHROME_LOCALE).refs;
  const [all, setAll] = useState(false);
  if (entries.length === 0) return null;
  const shown = all ? entries : entries.slice(0, REFERENCES_SHOWN);
  return (
    <section data-refs={side}>
      <h3 className="lx-refs-heading">{heading}</h3>
      <ul className="lx-refs-list">
        {shown.map((entry, at) => {
          // A European text is named by its CELEX alone (`identifierOf`), as the reading page reads it; a Luxembourg one by its work.
          const named = identifierOf(entry.work, entry.act_id ?? null);
          const target = named !== null && typeof entry.anchor === 'string' && entry.anchor.length > 0 ? { identifier: named, article: entry.anchor, date: entry.date ?? null } : null;
          const href = target === null ? null : textHref(target);
          const name = String(entry.article ?? entry.raw ?? '').replace(/[.\s]+$/u, '');
          const said = typeof entry.heading === 'string' && entry.heading.length > 0 ? fillNext(copy.item, { article: name, heading: entry.heading }) : name;
          const sub = typeof entry.sub === 'string' && entry.sub.length > 0 ? fillNext(copy.sub, { sub: entry.sub }) : '';
          return (
            <li key={`${entry.article_pk ?? 'raw'}-${at}`} data-ref-type={entry.type ?? 'none'}>
              <span className="lx-refs-what" lang="fr">
                {href === null ? said : (
                  <a
                    href={href}
                    onClick={(event) => {
                      if (target.identifier !== identifier || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
                      event.preventDefault();
                      onOpen(target.article);
                    }}
                  >
                    {said}
                  </a>
                )}
              </span>
              <span className="lx-refs-type">{copy.types[entry.type] ?? copy.types.none}{sub}</span>
              <span className="lx-refs-certainty" data-certainty={entry.certainty === 'sure' ? 'sure' : 'unsure'}>{entry.certainty === 'sure' ? copy.sure : copy.unsure}</span>
              {target !== null && target.identifier !== identifier && typeof entry.act_title === 'string' ? <span className="lx-refs-act" lang="fr">{entry.act_title}</span> : null}
            </li>
          );
        })}
      </ul>
      {entries.length > shown.length ? (
        <button type="button" className="lx-refs-more" onClick={() => setAll(true)}>{countedForm(copy.more, entries.length, CHROME_LOCALE)}</button>
      ) : null}
    </section>
  );
}

/**
 * The side build's panel under the article that is open: what it refers to and what cites it, as the new engine holds them
 * (`GET /api/next/article-by`). Nothing at all when the engine did not answer or holds no reference of the article.
 */
export function ArticleReferences({ references, identifier, onOpen = () => {} }) {
  const table = nextCopy(CHROME_LOCALE);
  const copy = table.refs;
  const sides = [['refers_to', copy.refersTo], ['cited_by', copy.citedBy]].map(([side, heading]) => [side, heading, Array.isArray(references?.[side]) ? references[side].filter((entry) => entry !== null && typeof entry === 'object') : []]);
  const changes = Array.isArray(references?.changed_by) ? references.changed_by.filter((change) => change !== null && typeof change === 'object') : [];
  // Since when the wording is unchanged, with the change that took effect when it began (as an answer pairs them).
  const wording = references === null || typeof references !== 'object' ? null
    : wordingLine({ ...references, last_change: [...changes].reverse().find((change) => change.effective === references.wording_since) ?? null }, CHROME_LOCALE);
  if (wording === null && changes.length === 0 && sides.every(([, , entries]) => entries.length === 0)) return null;
  return (
    <aside className="lx-refs" aria-label={copy.label} data-article-refs="">
      {wording === null ? null : <span className="lx-refs-wording" data-article-wording="">{wording}</span>}
      {changes.length === 0 ? null : (
        <section data-refs="changed_by">
          <h3 className="lx-refs-heading">{table.wording.changedHeading}</h3>
          <ul className="lx-refs-list">
            {changes.map((change, at) => {
              const title = typeof change.title === 'string' && cleanTitle(change.title).length > 0 ? cleanTitle(change.title) : null;
              // Always "amended by" its text, whatever kind the publisher records.
              return (
                <li key={`${change.effective ?? ''}-${at}`} data-change={change.kind ?? ''}>
                  <span className="lx-refs-type">{nextDate(change.effective)}</span>
                  <span className="lx-refs-type">{table.wording.changedBy}</span>
                  <span className="lx-refs-what" lang={title === null ? undefined : 'fr'}>{title ?? table.wording.changedUnheld}</span>
                </li>
              );
            })}
          </ul>
        </section>
      )}
      {sides.map(([side, heading, entries]) => <ReferenceList key={side} side={side} heading={heading} entries={entries} identifier={identifier} onOpen={onOpen} />)}
    </aside>
  );
}

/**
 * The address the engine answers a text's table of contents at (`GET /api/next/text-outline`): a Luxembourg text by its work,
 * a European one by its CELEX, for the version applicable on `date`. Null for a text the engine cannot name.
 */
export function outlineAddress(identifier, date) {
  const work = /^\/lu-legilux\/([^/?#]+)$/u.exec(identifier ?? '')?.[1] ?? (/^[0-9CE][0-9]{4}[A-Z]{1,2}[0-9]{4}/u.test(identifier ?? '') ? identifier : undefined);
  if (work === undefined) return null;
  return `/api/next/text-outline?work=${encodeURIComponent(work)}${/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(date ?? '') ? `&date=${date}` : ''}`;
}

/**
 * The text's table of contents as navigation (the engine's outline of the version read): the version's dates, the earliest
 * version held (the date floor), then each heading, indented by its depth, a link to its first article. Nothing at all when
 * the engine holds no heading of the text.
 */
export function TextOutline({ outline, current = null, order = [], onSelect = () => {}, identifier = null, date = null }) {
  const table = nextCopy(CHROME_LOCALE);
  const copy = table.outline;
  const rows = Array.isArray(outline?.outline) ? outline.outline.filter((row) => row !== null && typeof row === 'object' && typeof row.label === 'string' && row.label.trim().length > 0) : [];
  const version = outline?.version ?? null;
  const iso = (value) => typeof value === 'string' && /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(value);
  const dates = version === null || !iso(version.from) ? null
    : iso(version.until) ? fillNext(copy.versionSpan, { from: nextDate(version.from), until: nextDate(version.until) })
      : fillNext(version.until_known === false ? copy.versionUnsure : copy.versionOpen, { from: nextDate(version.from) });
  const held = outline?.held ?? null;
  const floor = held !== null && iso(held.first) && Number.isFinite(held.versions)
    ? fillNext(copy.floor, { date: nextDate(held.first), versions: countedForm(copy.versions, held.versions, CHROME_LOCALE) }) : null;
  const before = outline?.before_first_held === true && held !== null && iso(held.first) ? fillNext(copy.beforeFloor, { date: nextDate(held.first) }) : null;
  if (rows.length === 0 && dates === null && floor === null) return null;
  // The heading that holds the article open: the deepest one whose first and last articles frame it, in the order of the text.
  const at = new Map(order.map((id, index) => [id, index]));
  const place = current === null ? undefined : at.get(current);
  const open = place === undefined ? null : rows.filter((row) => at.has(row.first_anchor) && at.has(row.last_anchor) && at.get(row.first_anchor) <= place && place <= at.get(row.last_anchor))
    .reduce((best, row) => (best === null || (Number(row.depth) || 0) >= (Number(best.depth) || 0) ? row : best), null)?.node_id ?? null;
  return (
    <details className="lx-outline" data-text-outline="" open>
      <summary>{countedForm(copy.summary, rows.length, CHROME_LOCALE)}</summary>
      {dates === null ? null : <p className="lx-outline-dates" data-outline-version="">{dates}</p>}
      {floor === null ? null : <p className="lx-outline-dates" data-outline-floor="">{floor}</p>}
      {before === null ? null : <p className="lx-outline-dates lx-next-gap" data-outline-before="">{before}</p>}
      {rows.length === 0 ? null : (
        <ol aria-label={copy.label}>
          {rows.map((row) => {
            const target = typeof row.first_anchor === 'string' && row.first_anchor.length > 0 ? row.first_anchor : null;
            const count = Number.isFinite(row.articles) && row.articles > 0 ? countedForm(copy.articles, row.articles, CHROME_LOCALE) : '';
            const label = oneLine(row.label);
            return (
              <li key={row.node_id} data-depth={Math.min(5, Number(row.depth) || 0)} data-outline-node={row.node_id}>
                {target === null ? <span lang="fr">{label}</span> : (
                  <a
                    href={identifier === null ? '#' : textHref({ identifier, date, article: target })}
                    aria-current={open === row.node_id ? 'true' : undefined}
                    onClick={(event) => { event.preventDefault(); onSelect(target); }}
                  >
                    <span lang="fr">{label}</span>
                    {count ? <span className="lx-outline-count">{count}</span> : null}
                  </a>
                )}
              </li>
            );
          })}
        </ol>
      )}
    </details>
  );
}

/**
 * pages2 (21/11/2026): the address the engine answers a whole text at (`GET /api/next/text`), for a text the reading service
 * does not hold (the texts added to the engine's store since: the codes read from the publisher's PDF, the EU insurance
 * texts, the older laws): its versions, the version read on `date`, its articles in order and one page of their lines.
 */
export function nextTextAddress(identifier, date, { page = null, article = null } = {}) {
  const work = /^\/lu-legilux\/([^/?#]+)$/u.exec(identifier ?? '')?.[1] ?? (/^[0-9CE][0-9]{4}[A-Z]{1,2}[0-9]{4}/u.test(identifier ?? '') ? identifier : undefined);
  if (work === undefined) return null;
  const query = [`work=${encodeURIComponent(work)}`];
  if (/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(date ?? '')) query.push(`date=${date}`);
  if (Number.isInteger(page) && page > 0) query.push(`page=${page}`);
  else if (typeof article === 'string' && article.length > 0) query.push(`article=${encodeURIComponent(article)}`);
  return `/api/next/text?${query.join('&')}`;
}

/** One article of the engine's text, in the shape the page draws: its id, its lines (each a paragraph) and its source. */
function nextArticle(entry) {
  const lines = Array.isArray(entry?.lines) ? entry.lines.filter((line) => typeof line?.text === 'string' && line.text.trim().length > 0) : [];
  return Object.freeze({
    publisher_id: entry.publisher_id,
    heading: typeof entry.heading === 'string' && entry.heading.trim().length > 0 ? entry.heading.trim() : null,
    lines,
    text: lines.map((line) => line.text).join('\n'),
    official_source: /^https:\/\//u.test(entry.official_source ?? '') ? entry.official_source : null,
    language: 'fra',
    fromEngine: true,
  });
}

/**
 * The engine's answer for a text read into the page's state: the text (versions, contents, its source) and the page of
 * articles it carries. Null when the answer is not a text.
 */
export function nextTextState(value, identifier) {
  if (value === null || typeof value !== 'object' || !Array.isArray(value.contents) || value.page === null || typeof value.page !== 'object') return null;
  const iso = (date) => typeof date === 'string' && /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(date);
  const europe = value.europe === true;
  const read = value.version ?? {};
  const held = (Array.isArray(value.versions) ? value.versions : []).filter((version) => iso(version?.from));
  // An EU act is read in one wording on the axis, as the reading service reads it; a Luxembourg text with each of its versions.
  // repair-pages (22/11/2026): the header says how many dated wordings the engine holds (Solvency II: 3), as the outline does,
  // not "one version held" (`heldCount`, `heldFirst`).
  const chosen = europe ? held.filter((version) => version.id === read.id) : held;
  // Every dated wording held, as the outline counts them (a wording that applies later included).
  const heldToday = held;
  const versions = Object.freeze(chosen.map((version, at) => Object.freeze({
    date: version.from, until: iso(version.until) ? version.until : null, articleCount: Number.isInteger(version.articles) ? version.articles : null,
    first: at === 0, changed: null, added: null, removed: null, unchanged: null, bar: null, previous: at > 0 ? chosen[at - 1].from : null, textHeld: true,
  })));
  const contents = value.contents.filter((item) => typeof item?.publisher_id === 'string').map((item) => Object.freeze({
    id: item.publisher_id, page: Number.isInteger(item.page) ? item.page : 1, textHeld: true, number: typeof item.number === 'string' ? item.number : '',
  }));
  const source = value.source !== null && typeof value.source === 'object' ? value.source : null;
  // The language the store holds the version in (a law of 1937 is held only in German): the page marks its words so.
  const language = ['fra', 'deu', 'eng'].includes(source?.language) ? source.language : 'fra';
  const articles = (Array.isArray(value.page.articles) ? value.page.articles : []).filter((entry) => typeof entry?.publisher_id === 'string').map((entry) => ({ ...nextArticle(entry), language }));
  return {
    text: {
      identifier, publisher: europe ? 'eu-eurlex' : 'lu-legilux',
      // An EU title without the Official Journal's "(Texte présentant de l'intérêt pour l'EEE)", as the reading service's page names it.
      title: typeof value.title === 'string' ? cleanTitle(value.title.replace(/\s*\((?:Text with EEA relevance|Texte présentant de l['’]intérêt pour l['’]EEE)\)\s*$/u, '')) : null, language, versions,
      contents, articleCount: Number.isInteger(read.articles) ? read.articles : contents.length, official: /^https:\/\//u.test(value.official ?? '') ? value.official : null,
      holder: { date: iso(read.from) ? read.from : null, permalink: null }, euDate: null, source: 'next', textSource: source,
      heldCount: heldToday.length, heldFirst: heldToday[0]?.from ?? null,
    },
    page: Number.isInteger(value.page.number) ? value.page.number : 1,
    articles,
  };
}

/** Reads the engine's text at an address, once: its value, or null. */
async function readNextText(address, fetchImpl) {
  if (address === null) return null;
  try {
    const response = await (fetchImpl ?? globalThis.fetch)(address, { method: 'GET', headers: { accept: 'application/json' } });
    if (!response.ok) return null;
    return await response.json();
  } catch {
    return null;
  }
}

/** The sentence a text read from the publisher's PDF, or from a character recognition of a scanned page, carries; or null. */
export function textSourceLine(source, locale = CHROME_LOCALE) {
  const copy = nextCopy(locale).textSource;
  if (source === null || typeof source !== 'object') return null;
  const other = source.language === 'deu' ? copy.german : null;
  if (source.ocr === true) return other === null ? copy.ocr : `${copy.ocr} ${other}`;
  if (source.pdf === true) return other === null ? copy.pdf : `${copy.pdf} ${other}`;
  return other;
}

/** The address the side build's engine answers a text's links with other texts at, named as the current site names the text. */
export function linksAddress(identifier) {
  const work = /^\/lu-legilux\/([^/?#]+)$/u.exec(identifier ?? '')?.[1];
  return work === undefined ? null : `/api/next/act-by?work=${encodeURIComponent(work)}`;
}

/** One kind of link of a text ("Amended by", "This text repeals"): each other text by its title, with a link when Lex holds it. */
function LinkGroup({ heading, entries }) {
  const copy = nextCopy(CHROME_LOCALE).refs.links;
  const [all, setAll] = useState(false);
  const shown = all ? entries : entries.slice(0, REFERENCES_SHOWN);
  return (
    <section>
      <h3 className="lx-refs-heading">{heading}</h3>
      <ul className="lx-refs-list">
        {shown.map((entry, at) => {
          const named = identifierOf(entry.work, entry.act_id ?? null);
          const href = named === null ? null : textHref({ identifier: named });
          const title = typeof entry.title === 'string' && entry.title.trim().length > 0 ? oneLine(entry.title) : null;
          const said = title ?? <code>{String(entry.address ?? '')}</code>;
          return <li key={`${entry.address ?? ''}-${at}`}><span className="lx-refs-what" lang={title === null ? undefined : 'fr'}>{href === null ? said : <a href={href}>{said}</a>}</span></li>;
        })}
      </ul>
      {entries.length > shown.length ? (
        <button type="button" className="lx-refs-more" onClick={() => setAll(true)}>{countedForm(copy.more, entries.length, CHROME_LOCALE)}</button>
      ) : null}
    </section>
  );
}

/**
 * The side build's folded section of a Luxembourg text: its links with other texts, as the new engine holds them
 * (`GET /api/next/act-by`): what it amends, repeals, rests on, transposes, and what does so to it. Nothing at all when
 * the engine did not answer or holds no link.
 */
export function TextLinks({ links }) {
  const copy = nextCopy(CHROME_LOCALE).refs.links;
  const groups = [];
  for (const [side, field] of [['out', 'links_out'], ['in', 'links_in']]) {
    const byType = new Map();
    for (const entry of Array.isArray(links?.[field]) ? links[field] : []) {
      if (entry === null || typeof entry !== 'object') continue;
      const type = Object.hasOwn(copy[side], entry.type) && entry.type !== 'other' ? entry.type : 'other';
      byType.set(type, [...(byType.get(type) ?? []), entry]);
    }
    for (const [type, entries] of byType) groups.push({ key: `${side}-${type}`, heading: copy[side][type], entries });
  }
  const total = groups.reduce((sum, group) => sum + group.entries.length, 0);
  if (total === 0) return null;
  return (
    <details className="lx-links" data-text-links="">
      <summary>{countedForm(copy.summary, total, CHROME_LOCALE)}</summary>
      <div className="lx-refs lx-links-body">
        {groups.map((group) => <LinkGroup key={group.key} heading={group.heading} entries={group.entries} />)}
      </div>
    </details>
  );
}

/** One article in reading mode: the margin (number, strip, sentence, links) and the body (heading, paragraphs). */
export function ReadArticle({ entry, article, parts, versions, changedDates, identifier, highlighted, version, onCompareOn, quotes = null, references = null, onOpenReference = () => {} }) {
  const copy = liveChrome().text;
  // The lines an answer quotes are marked alone; with none met in the paragraphs, the article opened is marked whole.
  const marked = article && parts && quotes ? quotedParagraphs(parts.paragraphs, quotes) : [];
  const number = entry.number;
  // The versions that changed it are read after first paint (the pairwise diffs): until then, none is said.
  const last = (changedDates ?? []).at(-1) ?? null;
  const previous = last ? versions[versions.findIndex((v) => v.date === last) - 1]?.date ?? null : null;
  let sentence = null;
  if (versions.length > 1 && changedDates !== null) {
    if (last === null) sentence = fill(copy.sameAll, { count: grouped(versions.length), date: numericDate(versions[0].date) });
  }
  return (
    <article className={highlighted && marked.length === 0 ? 'lx-article lx-article-highlighted' : 'lx-article'} id={`article-${entry.id}`} data-article={entry.id} aria-labelledby={`article-${entry.id}-number`}>
      <div className="lx-margin">
        <span className="lx-margin-number" id={`article-${entry.id}-number`}>{fill(copy.articleShort, { number })}</span>
        <Strip versions={versions} changedDates={changedDates ?? []} />
        {sentence ? <span>{sentence}</span> : null}
        {last && previous ? (
          <a className="lx-margin-changed" href={textHref({ identifier, from: previous, to: last, article: entry.id })} onClick={(event) => { event.preventDefault(); onCompareOn(previous, last, entry.id); }}>
            {fill(copy.changedOn, { date: numericDate(last) })}
          </a>
        ) : null}
        {article ? <SourceAndCitation article={article} version={version} /> : null}
      </div>
      <div className="lx-article-body" lang={article?.language === 'eng' ? 'en' : article?.language === 'deu' ? 'de' : 'fr'}>
        {parts?.heading ? <h2>{parts.heading}</h2> : null}
        {article && parts ? <Paragraphs parts={parts} marked={marked} /> : entry.textHeld === false ? <p className="lx-without-text">{copy.withoutText}</p> : <p className="lx-skeleton" aria-hidden="true" />}
        {article && references ? <ArticleReferences references={references} identifier={identifier} onOpen={onOpenReference} /> : null}
      </div>
    </article>
  );
}

/** One changed article in compare mode (V3Compare): its two wordings side by side or in one text, paragraph by paragraph. */
export function CompareArticle({ row, versions, changedDates, compare, view, index, total, onStep, labelOf, textLang = 'fr' }) {
  const copy = liveChrome().text;
  if (row === null) return <p className="lx-muted">{copy.compareLoading}</p>;
  const id = row.publisher_id;
  const fromText = row.from?.[0]?.text ?? null;
  const toText = row.to?.[0]?.text ?? null;
  const before = fromText === null ? null : articleParts(fromText);
  const after = toText === null ? null : articleParts(toText);
  const heading = after?.heading ?? before?.heading ?? null;
  const fromVersion = versionAt(versions, compare.from);
  const toVersion = versionAt(versions, compare.to);
  const fromIndex = versions.findIndex((v) => v.date === fromVersion?.date);
  const inPlaceFrom = (() => {
    // When the left wording began: the last change of the article on or before the left version, else the first held.
    const earlier = (changedDates ?? []).filter((date) => date <= compare.from);
    return earlier.at(-1) ?? versions[0]?.date ?? compare.from;
  })();
  const toNext = versions[versions.findIndex((v) => v.date === toVersion?.date) + 1]?.date ?? null;
  const later = (changedDates ?? []).filter((date) => date > compare.to);
  const rightNote = later.length === 0 && toNext === null ? copy.inForceToday : later.length === 0 ? copy.inForceToday : fill(copy.appliedUntil, { date: numericDate(later[0]) });
  const leftNote = fill(copy.inPlaceFrom, { date: numericDate(inPlaceFrom) });
  void fromIndex;
  let body;
  if (row.status === 'added') {
    body = <p className="lx-muted">{fill(copy.addedArticle, { date: numericDate(compare.to) })}</p>;
  } else if (row.status === 'removed') {
    body = <p className="lx-muted">{fill(copy.removedArticle, { date: numericDate(compare.to) })}</p>;
  } else if (fromText !== null && toText !== null && sameVisibleWording(fromText, toText)) {
    body = <p className="lx-novisible">{copy.noVisible}</p>;
  } else {
    body = null;
  }
  const rows = before && after ? alignParagraphs(before.paragraphs, after.paragraphs) : null;
  const strip = changedDates ?? [];
  const sentence = strip.length === 1
    ? fill(copy.changedOnce, { versions: grouped(versions.length), date: numericDate(strip[0]) })
    : strip.length > 1 ? fill(copy.changedTimes, { versions: grouped(versions.length), count: grouped(strip.length), date: numericDate(strip.at(-1)) }) : null;
  const run = (r, i) => (r.kind === 'removed' ? <del key={i}>{r.text}</del> : r.kind === 'added' ? <ins key={i}>{r.text}</ins> : <span key={i}>{r.text}</span>);
  return (
    <section className="lx-compare-article" aria-labelledby="lx-compare-heading">
      <div className="lx-compare-head">
        <div>
          <div className="lx-compare-title">
            <span className="lx-compare-number">{fill(copy.articleShort, { number: labelOf(id) })}</span>
            {heading ? <h2 id="lx-compare-heading" lang={textLang}>{heading}</h2> : <h2 id="lx-compare-heading" className="lx-visually-hidden">{fill(copy.articleShort, { number: labelOf(id) })}</h2>}
          </div>
          <div className="lx-compare-sub">
            <Strip versions={versions} changedDates={strip} />
            {sentence ? <span>{sentence}</span> : null}
          </div>
        </div>
        <span className="lx-compare-nav">
          <span className="lx-muted">{fill(copy.changedOfTotal, { index: grouped(index + 1), count: grouped(total) })}</span>
          {index > 0 ? <a href="#" onClick={(event) => { event.preventDefault(); onStep(-1); }}>{copy.previous}</a> : null}
          {index < total - 1 ? <a className="lx-strong" href="#" onClick={(event) => { event.preventDefault(); onStep(1); }}>{fill(copy.next, { number: labelOf(null, 1) })}</a> : null}
        </span>
      </div>
      {body}
      {rows && body === null ? (
        view === 'inline' ? (
          <div className="lx-compare-inline" lang={textLang}>
            {rows.map((r, i) => (
              <p key={i} className={r.kind === 'added' ? 'lx-block-added' : r.kind === 'removed' ? 'lx-block-removed' : undefined}>
                {r.kind === 'changed' ? [...r.diff.left.filter((x) => x.kind !== 'same').length ? interleave(r.diff) : [r.right.text]] : r.kind === 'removed' ? <del>{r.left.text}</del> : r.kind === 'added' ? <ins>{r.right.text}</ins> : r.left.text}
              </p>
            ))}
          </div>
        ) : (
          <div className="lx-compare-grid">
            <div className="lx-compare-col-head"><div className="lx-strong">{fill(copy.versionOf, { date: numericDate(compare.from) })}</div><div className="lx-muted">{leftNote}</div></div>
            <div className="lx-compare-col-head"><div className="lx-strong">{fill(copy.versionOf, { date: numericDate(compare.to) })}</div><div className="lx-muted">{rightNote}</div></div>
            {rows.flatMap((r, i) => {
              const marker = (p) => (p?.marker ? `${p.marker} ` : '');
              if (r.kind === 'same') return [<p key={`l${i}`} className="lx-cmp-p" lang={textLang}>{marker(r.left)}{r.left.text}</p>, <p key={`r${i}`} className="lx-cmp-p" lang={textLang}>{marker(r.right)}{r.right.text}</p>];
              if (r.kind === 'changed') return [<p key={`l${i}`} className="lx-cmp-p" lang={textLang}>{marker(r.left)}{r.diff.left.map(run)}</p>, <p key={`r${i}`} className="lx-cmp-p" lang={textLang}>{marker(r.right)}{r.diff.right.map(run)}</p>];
              if (r.kind === 'added') return [<p key={`l${i}`} className="lx-cmp-none">{copy.notInVersion}</p>, <p key={`r${i}`} className="lx-cmp-p lx-block-added" lang={textLang}>{marker(r.right)}{r.right.text}</p>];
              return [<p key={`l${i}`} className="lx-cmp-p lx-block-removed" lang={textLang}>{marker(r.left)}{r.left.text}</p>, <p key={`r${i}`} className="lx-cmp-none">{copy.notInVersion}</p>];
            })}
          </div>
        )
      ) : null}
    </section>
  );
}

/** "In one text": the removed words struck and the added ones marked, in the order of the text. */
function interleave(diff) {
  const out = [];
  let l = 0;
  let r = 0;
  const left = diff.left;
  const right = diff.right;
  while (l < left.length || r < right.length) {
    const a = left[l];
    const b = right[r];
    if (a && a.kind === 'removed') { out.push(<del key={`d${l}`}>{a.text}</del>); l += 1; continue; }
    if (b && b.kind === 'added') { out.push(<ins key={`i${r}`}>{b.text}</ins>); r += 1; continue; }
    if (b) { out.push(<span key={`s${r}`}>{b.text}</span>); }
    l += 1;
    r += 1;
  }
  return out;
}

/**
 * The page's status, said to a screen reader (a polite live region present from the first render): loading, the text's
 * title once it is read, or that it could not be loaded. `data-answer-state` names the state for the journeys.
 */
function Status({ state, message }) {
  return <p className="lx-visually-hidden" role="status" aria-live="polite" data-answer-state={state}>{message}</p>;
}

/** The page. `fetchImpl` is injectable for the tests; `now` fixes today. */
export function TextPage({ contract, fetchImpl, now = null }) {
  const copy = liveChrome().text;
  const ask = useCallback((operation, parameters, objectType) => askV3(operation, parameters, { contract, objectType, ...(fetchImpl ? { fetchImpl } : {}) }), [contract, fetchImpl]);
  const [address, setAddress] = useState(null); // the query's state, read once hydrated
  const [load, setLoad] = useState({ state: 'loading' });
  const [text, setText] = useState(null); // { identifier, publisher, title, language, versions, contents, pagesHeld }
  const [pages, setPages] = useState(new Map()); // page number → articles
  const [diffs, setDiffs] = useState([]);
  const [compareAnswer, setCompareAnswer] = useState(null);
  const [pending, setPending] = useState(null);
  const [filter, setFilter] = useState('all');
  const [find, setFind] = useState('');
  const [width, setWidth] = useState(900);
  const [copied, setCopied] = useState(false);
  const [highlight, setHighlight] = useState(null);
  const [marks, setMarks] = useState(null);
  const [typed, setTyped] = useState(null);
  const [diffsTried, setDiffsTried] = useState(false);
  const [textLinks, setTextLinks] = useState(null); // the text's links with other texts, as the side build's engine holds them
  const [references, setReferences] = useState(null); // the open article's references, as the side build's engine holds them
  const [outline, setOutline] = useState(null); // the text's table of contents for the version read, as the engine holds it
  const axisBox = useRef(null);
  const today = now ?? todayInLuxembourg();

  // The query, once hydrated (the server render and the first render are the loading state).
  useEffect(() => {
    const state = textStateOf(window.location.search);
    setAddress(state);
    // The article a link opens is marked, as the assistant's answer opens it (the brief, section 5).
    if (state.article !== null && state.from === null) setHighlight(state.article);
  }, []);

  /** Writes the state into the page's own address (a law's identifier, dates and an article id: never a word). */
  const go = useCallback((next) => {
    setAddress((current) => {
      const merged = { ...current, ...next };
      const href = textHref(merged, CHROME_LOCALE);
      if (href !== null) window.history.replaceState(window.history.state, '', href);
      return Object.freeze(merged);
    });
  }, []);

  // The text: its dossier and its versions.
  useEffect(() => {
    if (address === null) return;
    if (address.identifier === null) {
      setLoad({ state: 'none' });
      return;
    }
    if (text !== null && text.identifier === address.identifier) return;
    let live = true;
    (async () => {
      setLoad({ state: 'loading' });
      const dossier = await ask('dossier', { identifier: address.identifier }, 'work_record');
      if (!live) return;
      if (dossier.state !== 'success') {
        // A text the reading service does not hold: read from the engine's own store, when it holds it (pages2).
        const engine = nextTextState(await readNextText(nextTextAddress(address.identifier, address.date ?? today, { article: address.article }), fetchImpl), address.identifier);
        if (!live) return;
        if (engine === null) {
          setLoad({ state: 'failed' });
          return;
        }
        setText(engine.text);
        setPages(new Map([[engine.page, engine.articles]]));
        setDiffs([]);
        setDiffsTried(true);
        setLoad({ state: 'ready' });
        return;
      }
      const value = dossier.envelope.result.value;
      const publisher = value.publisher === 'eu-eurlex' || typeof value.celex === 'string' ? 'eu-eurlex' : 'lu-legilux';
      const language = readingLanguage(publisher, value.available_languages);
      let versions = [];
      if (publisher === 'lu-legilux') {
        const begins = /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(value.history_begins ?? '') ? value.history_begins : '1900-01-01';
        const changes = await ask('changes_in_period', { identifier: address.identifier, date_from: begins, date_to: today, limit: 200 }, 'change_list');
        if (!live) return;
        if (changes.state === 'success') versions = versionsOf(changes.envelope.result.value);
      }
      // The text's own title; with none held, the act's name from its key (a dated law, a code), never its identifier.
      const title = (publisher === 'eu-eurlex' ? europeTitle(value, language) : ownTitleOf(value))
        ?? actNameOf(address.identifier.replace(/^\/lu-legilux\//u, ''));
      setText({ identifier: address.identifier, publisher, title, language, versions, contents: null, articleCount: null, euDate: null });
      setPages(new Map());
      setDiffs([]);
      setDiffsTried(false);
    })();
    return () => { live = false; };
  }, [address, ask, text, today]);

  const readDate = address?.date ?? today;
  const compare = address?.from && address?.to ? { from: address.from <= address.to ? address.from : address.to, to: address.from <= address.to ? address.to : address.from } : null;
  const readVersion = text ? versionAt(text.versions, readDate) : null;
  const versionKey = text?.publisher === 'eu-eurlex' ? 'eu' : readVersion?.date ?? readDate;

  // The version read: its contents and the page holding the article asked for (or the first).
  useEffect(() => {
    if (text === null || address === null || compare !== null) return;
    let live = true;
    (async () => {
      // The engine's text: the version read and the page holding the article asked for, from the engine's store.
      const fromEngine = async () => {
        const engine = nextTextState(await readNextText(nextTextAddress(text.identifier, readDate, { article: address.article }), fetchImpl), text.identifier);
        if (!live) return;
        if (engine === null) {
          setLoad({ state: 'failed' });
          return;
        }
        setText((current) => ({ ...current, ...engine.text }));
        setPages(new Map([[engine.page, engine.articles]]));
        setDiffsTried(true);
        setLoad({ state: 'ready' });
      };
      if (text.source === 'next') {
        await fromEngine();
        return;
      }
      const parameters = { identifier: text.identifier, date: readDate, language: text.language, contents: true };
      if (address.article) parameters.article = address.article;
      const answer = await ask('evidence_bundle', parameters, 'evidence_bundle');
      if (!live) return;
      if (answer.state !== 'success') {
        await fromEngine();
        return;
      }
      const value = answer.envelope.result.value;
      const holder = value.states?.[0] ?? value.wordings?.[0] ?? null;
      if (holder === null) {
        setLoad({ state: 'failed' });
        return;
      }
      const contents = (holder.contents ?? []).map((item) => ({
        id: item.publisher_id,
        page: item.page,
        textHeld: item.text_held !== false,
        number: item.heading ? item.heading.replace(/^Article\s+/u, '') : articleNumberOf(item.publisher_id),
      }));
      const versions = text.publisher === 'eu-eurlex'
        ? [Object.freeze({ date: holder.wording_date, until: null, first: true, bar: null, textHeld: true, articleCount: holder.page?.articles_in_version ?? null })]
        : text.versions;
      setText((current) => ({ ...current, versions, contents, articleCount: holder.page?.articles_in_version ?? contents.length, official: holder.sources?.[0]?.official_source ?? holder.articles?.[0]?.official_source ?? null, holder: { date: holder.applicability_date ?? holder.wording_date, permalink: holder.permalink } }));
      setPages(new Map([[holder.page?.number ?? 1, holder.articles ?? []]]));
      setLoad({ state: 'ready' });
    })();
    return () => { live = false; };
    // The version read is what matters, not the exact date typed inside it.
  }, [text?.identifier, versionKey, compare === null, address?.article === null]); // eslint-disable-line react-hooks/exhaustive-deps

  /** Loads one page of the version read, if not yet loaded. */
  const loadPage = useCallback(async (number) => {
    if (text === null || pages.has(number)) return;
    if (text.source === 'next') {
      const engine = nextTextState(await readNextText(nextTextAddress(text.identifier, readDate, { page: number }), fetchImpl), text.identifier);
      if (engine !== null) setPages((current) => new Map([...current, [engine.page, engine.articles]]));
      return;
    }
    const answer = await ask('evidence_bundle', { identifier: text.identifier, date: readDate, language: text.language, page: number }, 'evidence_bundle');
    if (answer.state !== 'success') return;
    const holder = answer.envelope.result.value.states?.[0] ?? answer.envelope.result.value.wordings?.[0];
    setPages((current) => new Map([...current, [number, holder?.articles ?? []]]));
  }, [ask, pages, readDate, text]);

  // For a short text, every page at once (three for the insurance contract law); a long one is read page by page.
  useEffect(() => {
    if (text?.contents == null || load.state !== 'ready') return;
    const count = Math.max(...text.contents.map((item) => item.page), 1);
    if (count <= 4) for (let n = 1; n <= count; n += 1) if (!pages.has(n)) loadPage(n);
  }, [text?.contents, load.state]); // eslint-disable-line react-hooks/exhaustive-deps

  // Which versions changed each article: each version with changes against the one before, a few at a time, after first paint.
  useEffect(() => {
    if (text === null || text.publisher !== 'lu-legilux' || text.source === 'next' || load.state !== 'ready' || diffs.length > 0) return;
    const pairs = text.versions.filter((v) => !v.first && v.previous && (v.bar ?? 0) + (v.removed ?? 0) > 0).slice(-DIFF_CAP);
    let live = true;
    (async () => {
      const results = [];
      for (let i = 0; i < pairs.length; i += DIFF_CONCURRENCY) {
        const batch = await Promise.all(pairs.slice(i, i + DIFF_CONCURRENCY).map((v) => ask('diff', { identifier: text.identifier, date_from: v.previous, date_to: v.date, language: text.language }, 'diff')));
        if (!live) return;
        for (const answer of batch) if (answer.state === 'success') results.push(answer.envelope.result.value);
        setDiffs([...results]);
      }
      if (live) setDiffsTried(true);
    })();
    return () => { live = false; };
  }, [text?.identifier, load.state]); // eslint-disable-line react-hooks/exhaustive-deps

  // Compare mode: the two versions' comparison.
  useEffect(() => {
    if (text === null || compare === null) return;
    let live = true;
    setCompareAnswer(null);
    (async () => {
      const answer = await ask('diff', { identifier: text.identifier, date_from: compare.from, date_to: compare.to, language: text.language }, 'diff');
      if (!live) return;
      setCompareAnswer(answer.state === 'success' ? answer.envelope.result.value : { failed: true });
      if (load.state !== 'ready') setLoad({ state: 'ready' });
    })();
    return () => { live = false; };
  }, [text?.identifier, compare?.from, compare?.to]); // eslint-disable-line react-hooks/exhaustive-deps

  // The side build: the text's links with other texts, asked once a Luxembourg text is read. Nothing is shown when the
  // engine does not answer.
  const linksAt = text !== null && load.state === 'ready' ? linksAddress(text.identifier) : null;
  useEffect(() => {
    setTextLinks(null);
    if (linksAt === null) return undefined;
    let live = true;
    (async () => {
      try {
        const response = await (fetchImpl ?? globalThis.fetch)(linksAt, { method: 'GET', headers: { accept: 'application/json' } });
        if (!response.ok) return;
        const value = await response.json();
        if (live && value !== null && typeof value === 'object') setTextLinks({ at: linksAt, value });
      } catch {
        // No section: the page reads as it does today.
      }
    })();
    return () => { live = false; };
  }, [linksAt]); // eslint-disable-line react-hooks/exhaustive-deps

  // The side build: the open article's references ("Refers to", "Cited by"), asked of the new engine by the names the
  // current site gives the text and the article. Nothing is shown when it does not answer (a 404, a failure).
  const referencesAt = text !== null && compare === null && load.state === 'ready' ? referencesAddress(text.identifier, address?.article ?? null, readDate) : null;
  useEffect(() => {
    setReferences(null);
    if (referencesAt === null) return undefined;
    let live = true;
    (async () => {
      try {
        const response = await (fetchImpl ?? globalThis.fetch)(referencesAt, { method: 'GET', headers: { accept: 'application/json' } });
        if (!response.ok) return;
        const value = await response.json();
        if (live && value !== null && typeof value === 'object') setReferences({ at: referencesAt, article: address?.article ?? null, value });
      } catch {
        // No panel: the page reads as it does today.
      }
    })();
    return () => { live = false; };
  }, [referencesAt]); // eslint-disable-line react-hooks/exhaustive-deps

  // The text's table of contents (the engine's outline of the version read), asked once a text is read, again when the
  // version changes. Nothing is shown when the engine does not answer.
  const outlineAt = text !== null && compare === null && load.state === 'ready' ? outlineAddress(text.identifier, readVersion?.date ?? readDate) : null;
  useEffect(() => {
    if (outlineAt === null) return undefined;
    let live = true;
    (async () => {
      try {
        const response = await (fetchImpl ?? globalThis.fetch)(outlineAt, { method: 'GET', headers: { accept: 'application/json' } });
        if (!response.ok) return;
        const value = await response.json();
        if (live && value !== null && typeof value === 'object') setOutline({ at: outlineAt, value });
      } catch {
        // No table of contents: the article list reads as it does today.
      }
    })();
    return () => { live = false; };
  }, [outlineAt]); // eslint-disable-line react-hooks/exhaustive-deps

  // The dock folds to its tab in compare mode (V3Compare) and opens again when reading.
  // The dock's own script may attach after this page's: the fold is said again once it has had time to listen.
  useEffect(() => {
    const say = () => window.dispatchEvent(new CustomEvent(DOCK_EVENT, { detail: { collapsed: compare !== null } }));
    say();
    const later = [setTimeout(say, 800), setTimeout(say, 2500)];
    return () => later.forEach(clearTimeout);
  }, [compare !== null]); // eslint-disable-line react-hooks/exhaustive-deps

  // The assistant opens an article here when it is this text's (`lex:open`).
  useEffect(() => {
    const onOpen = (event) => {
      const detail = event.detail ?? {};
      if (text === null || detail.identifier !== text.identifier) return;
      event.preventDefault();
      setHighlight(detail.highlight ? detail.article : null);
      go({ date: detail.date ?? null, article: detail.article ?? null, from: null, to: null });
    };
    window.addEventListener(OPEN_EVENT, onOpen);
    return () => window.removeEventListener(OPEN_EVENT, onOpen);
  }, [text, go]);

  // The lines the answer in sight quotes (`lex:mark`), kept whatever text is open: they are read against this text's
  // identifier when its articles are drawn.
  useEffect(() => {
    const onMark = (event) => {
      const detail = event.detail ?? {};
      setMarks(typeof detail.identifier === 'string' && Array.isArray(detail.marks) && detail.marks.length > 0 ? { identifier: detail.identifier, marks: detail.marks } : null);
    };
    window.addEventListener(MARK_EVENT, onMark);
    return () => window.removeEventListener(MARK_EVENT, onMark);
  }, []);

  // An article asked for by the address is highlighted when the assistant opened it (`highlight=1` is not written: the
  // dock's own event says it); "Find in this law" from the dock focuses the field.
  useEffect(() => {
    const onFind = () => document.getElementById('lx-law-find')?.focus();
    window.addEventListener('lex:find-in-law', onFind);
    return () => window.removeEventListener('lex:find-in-law', onFind);
  }, []);

  // The axis is drawn at the width it has.
  useEffect(() => {
    const box = axisBox.current;
    if (box === null || typeof ResizeObserver === 'undefined') return undefined;
    const observer = new ResizeObserver((entries) => setWidth(Math.max(320, Math.floor(entries[0].contentRect.width))));
    observer.observe(box);
    return () => observer.disconnect();
  }, [load.state]);

  // The article asked for is brought into view once its page is there.
  const scrolled = useRef(null);
  useEffect(() => {
    if (compare !== null || address?.article == null || scrolled.current === `${versionKey}:${address.article}`) return;
    const element = document.getElementById(`article-${address.article}`);
    const loaded = [...pages.values()].some((articles) => articles.some((a) => a.publisher_id === address.article));
    if (element !== null && loaded) {
      const column = element.closest('.lx-reading');
      if (column !== null) column.scrollTop += element.getBoundingClientRect().top - column.getBoundingClientRect().top - 8;
      scrolled.current = `${versionKey}:${address.article}`;
    }
  });

  // The quoted line of the article asked for is brought into view once it is drawn (a long article's line may be far below
  // its heading), once per answer and article.
  const shownMark = useRef(null);
  useEffect(() => {
    if (compare !== null || address?.article == null || marks === null || text === null || marks.identifier !== text.identifier) return;
    const key = `${versionKey}:${address.article}:${marks.marks.map((mark) => mark.article + mark.text.length).join(',')}`;
    if (shownMark.current === key || scrolled.current !== `${versionKey}:${address.article}`) return;
    const line = document.getElementById(`article-${address.article}`)?.querySelector('[data-quoted]') ?? null;
    if (line === null) return;
    const column = line.closest('.lx-reading');
    if (column !== null) {
      const top = line.getBoundingClientRect().top - column.getBoundingClientRect().top;
      if (top < 0 || top > column.clientHeight * 0.6) column.scrollTop += top - 72;
    }
    shownMark.current = key;
  });

  const index = useMemo(() => changeIndex(diffs), [diffs]);
  const diffsDone = text !== null && text.publisher === 'lu-legilux' && diffsTried || text !== null && text.publisher === 'lu-legilux' && diffs.length >= text.versions.filter((v) => !v.first && v.previous && (v.bar ?? 0) + (v.removed ?? 0) > 0).slice(-DIFF_CAP).length;
  const articlesById = useMemo(() => {
    const map = new Map();
    for (const articles of pages.values()) for (const article of articles) map.set(article.publisher_id, article);
    return map;
  }, [pages]);
  // An article read from the engine's store comes as its numbered lines: each line is a paragraph, as the store holds it.
  const partsOf = useCallback((article) => {
    if (!article) return null;
    if (article.fromEngine === true) return { heading: article.heading, paragraphs: article.lines.map((line) => ({ marker: null, text: line.text })) };
    return text?.publisher === 'eu-eurlex' ? europeParts(article.text) : articleParts(article.text);
  }, [text?.publisher]);

  const onMark = (date) => {
    if (text === null || text.versions.length < 2) return;
    // A text read from the engine's store is read at a date; two versions are compared only by the reading service.
    if (text.source === 'next') {
      go({ date, article: address?.article ?? null });
      return;
    }
    if (compare !== null) {
      // In compare mode, a click moves the nearer end of the pair.
      const nearFrom = Math.abs(date.localeCompare(compare.from)) <= Math.abs(date.localeCompare(compare.to));
      go(nearFrom ? { from: date, to: compare.to } : { from: compare.from, to: date });
      return;
    }
    if (pending !== null && pending !== date) {
      const [from, to] = pending < date ? [pending, date] : [date, pending];
      setPending(null);
      go({ from, to, date: null });
      return;
    }
    setPending(date);
    go({ date, article: address?.article ?? null });
  };

  // ----- Rendering -----

  if (load.state === 'none') {
    return <div className="lx-text lx-text-message"><Status state="refusal" message="" /><p>{copy.none}</p></div>;
  }
  if (load.state === 'failed') {
    return (
      <div className="lx-text lx-text-message">
        <Status state="transport_failure" message={copy.failed} />
        <p>{copy.failed}</p>
        <button type="button" className="lx-button" onClick={() => { setText(null); setLoad({ state: 'loading' }); setAddress(textStateOf(window.location.search)); }}>{copy.retry}</button>
      </div>
    );
  }
  if (text === null || (load.state === 'loading' && compare === null)) {
    return (
      <div className="lx-text" aria-busy="true">
        <Status state="loading" message={copy.loading} />
        <div className="lx-text-top">
          <p className="lx-muted">{copy.loading}</p>
          <div className="lx-axis-box" ref={axisBox} />
        </div>
        <div className="lx-text-row">
          <div className="lx-list" />
          <div className="lx-reading"><p className="lx-skeleton" /><p className="lx-skeleton" /><p className="lx-skeleton lx-skeleton-short" /></div>
        </div>
      </div>
    );
  }

  const versions = text.versions;
  const isEurope = text.publisher === 'eu-eurlex';
  // A text read from the engine's store: no comparison of two versions (the reading service compares), its source said.
  const fromEngine = text.source === 'next';
  const sourceLine = fromEngine ? textSourceLine(text.textSource) : null;
  // The statute's language, apart from the interface's (the launch contract): French for Luxembourg, the wording read for the EU.
  const textLang = text.language === 'eng' ? 'en' : text.language === 'deu' ? 'de' : 'fr';
  const contents = text.contents ?? [];
  const labelById = new Map(contents.map((item) => [item.id, item.number]));
  const comparison = compareAnswer?.comparisons?.[0] ?? null;
  const changedRows = comparison ? comparison.articles.filter((a) => a.status !== 'unchanged') : [];
  const compareIndex = Math.max(0, changedRows.findIndex((row) => row.publisher_id === address?.article));
  const compareRow = changedRows[compareIndex] ?? null;
  const counts = comparison?.counts ?? null;
  const changedList = new Set([...index.keys()]);
  const listed = compare !== null
    ? changedRows.map((row) => ({ id: row.publisher_id, number: labelById.get(row.publisher_id) ?? articleNumberOf(row.publisher_id), textHeld: true }))
    : contents.filter((item) => (filter === 'changed' ? changedList.has(item.id) : true));
  const needle = comparable(find).toLowerCase();
  const matches = (item) => {
    if (needle.length === 0) return true;
    const article = articlesById.get(item.id);
    return `${item.number} ${article ? comparable(article.text).toLowerCase() : ''}`.includes(needle);
  };
  const shown = listed.filter(matches);
  const headingOf = (id) => {
    const article = articlesById.get(id);
    if (article) return partsOf(article)?.heading ?? '';
    const row = changedRows.find((r) => r.publisher_id === id);
    const text0 = row?.to?.[0]?.text ?? row?.from?.[0]?.text;
    return text0 ? articleParts(text0).heading ?? '' : '';
  };
  const yearsOf = (id) => [...new Set((index.get(id) ?? []).map((date) => date.slice(0, 4)))].join(', ');
  const select = (id) => {
    if (compare !== null) {
      go({ article: id });
      return;
    }
    const item = contents.find((c) => c.id === id);
    go({ article: id });
    scrolled.current = null;
    if (item && !pages.has(item.page)) loadPage(item.page);
  };
  const loadedPages = [...pages.keys()].sort((a, b) => a - b);
  const pageCount = Math.max(1, ...contents.map((c) => c.page));
  const readArticles = contents.filter((item) => loadedPages.includes(item.page));

  // repair-pages: an EU text read from the engine says how many dated wordings it holds, from the first.
  const heldCount = isEurope && text.source === 'next' && Number.isInteger(text.heldCount) && text.heldCount > 1 ? text.heldCount : null;
  const facts = heldCount !== null
    ? `${counted(copy.articles, text.articleCount ?? contents.length)} ${fill(countedEntry(copy.versionsHeld, heldCount), { ...(countedEntry(copy.versionsHeld, heldCount).includes('{count}') ? { count: grouped(heldCount) } : {}), first: numericDate(text.heldFirst) })}`
    : isEurope
    ? `${counted(copy.articles, text.articleCount ?? contents.length)} ${copy.oneVersion}.`
    : `${counted(copy.articles, text.articleCount ?? contents.length)} ${fill(countedEntry(copy.versionsHeld, versions.length), { ...(countedEntry(copy.versionsHeld, versions.length).includes('{count}') ? { count: grouped(versions.length) } : {}), first: numericDate(versions[0]?.date) })}`;
  const until = readVersion?.until ?? null;
  const beforeFirst = !isEurope && versions.length > 0 && readDate < versions[0].date;

  return (
    <div className={compare ? 'lx-text lx-text-compare' : 'lx-text'} data-text-page="" data-mode={compare ? 'compare' : 'read'}>
      <Status state="success" message={text.title ?? ''} />
      <div className="lx-text-top">
        <div className="lx-text-head">
          <div className="lx-text-titles">
            <h1 lang={textLang}>{text.title ?? ''}</h1>
            <p className="lx-muted">
              {isEurope ? (text.source === 'next' ? copy.kindEuNext : copy.kindEu) : copy.kindLu} {compare ? copy.compareHint : facts}
              {isEurope && text.source !== 'next' ? ` ${copy.euDate}` : ''}
            </p>
            {sourceLine === null ? null : <p className="lx-muted lx-next-gap" data-text-source={text.textSource?.ocr === true ? 'ocr' : text.textSource?.pdf === true ? 'pdf' : 'language'}>{sourceLine}</p>}
          </div>
          <div className="lx-text-actions">
            {text.official ? <a className="lx-button" href={text.official} rel="noopener noreferrer">{copy.officialSource}</a> : null}
            <button type="button" className="lx-button" onClick={() => { navigator.clipboard?.writeText(window.location.href).then(() => setCopied(true), () => {}); }}>{copied ? copy.copied : copy.copyLink}</button>
          </div>
        </div>
        <div className="lx-text-line">
          {compare ? (
            <span data-compare-summary="">
              {fill(copy.comparing, {
                from: numericDate(compare.from),
                to: numericDate(compare.to),
                summary: counts
                  ? [
                    counts.changed > 0 || (counts.added === 0 && counts.removed === 0) ? counted(copy.summaryChanged, counts.changed) : null,
                    counts.added > 0 ? counted(copy.summaryAdded, counts.added) : null,
                    counts.removed > 0 ? counted(copy.summaryRemoved, counts.removed) : null,
                    counted(copy.summarySame, counts.unchanged),
                  ].filter(Boolean).join(', ')
                  : '…',
              })}
            </span>
          ) : beforeFirst ? (
            <span>{fill(copy.beforeFirst, { date: numericDate(versions[0].date) })} <a href="#" onClick={(e) => { e.preventDefault(); go({ date: versions[0].date }); }}>{fill(copy.openFirst, { date: numericDate(versions[0].date) })}</a></span>
          ) : isEurope ? (
            <span>{fill(copy.versionOf, { date: numericDate(versions[0]?.date) })}</span>
          ) : (
            <span>
              {fill(until ? copy.readingUntil : copy.readingToday, until ? { date: '\u0000', until: numericDate(until) } : { date: '\u0000' }).split('\u0000').flatMap((part, i) => (i === 0 ? [part] : [<strong className="lx-date" key="d">{numericDate(readVersion?.date)}</strong>, part]))}
              {fromEngine ? null : <>{' '}{pending ? copy.pickSecond : copy.barsHint}</>}
            </span>
          )}
          <span className="lx-text-controls">
            {compare ? (
              <>
                <span className="lx-segmented" role="group" aria-label={copy.viewLabel}>
                  <button type="button" aria-pressed={address.view !== 'inline'} onClick={() => go({ view: null })}>{copy.sideBySide}</button>
                  <button type="button" aria-pressed={address.view === 'inline'} onClick={() => go({ view: 'inline' })}>{copy.inOneText}</button>
                </span>
                <a className="lx-button lx-button-outline" href={textHref({ identifier: text.identifier, date: compare.to, article: address.article })} onClick={(e) => { e.preventDefault(); setPending(null); go({ from: null, to: null, date: compare.to }); }}>{copy.stopComparing}</a>
              </>
            ) : isEurope ? null : (
              <>
                <form className="lx-showon" onSubmit={(e) => { e.preventDefault(); const date = typedDate(typed ?? ''); if (date) { setPending(null); setTyped(null); go({ date }); } }}>
                  <label htmlFor="lx-showon-date">{copy.showOn}</label>
                  <input id="lx-showon-date" type="text" inputMode="numeric" autoComplete="off" value={typed ?? numericDate(readDate)} onChange={(e) => setTyped(e.target.value)} />
                  <button type="submit" className="lx-visually-hidden">{copy.showOnSubmit}</button>
                </form>
                {versions.length > 1 && !fromEngine ? (
                  <button type="button" className="lx-button lx-button-indigo" onClick={() => {
                    const at = versions.findIndex((v) => v.date === readVersion?.date);
                    const to = versions[Math.max(1, at)]?.date;
                    const from = versions[Math.max(0, Math.max(1, at) - 1)]?.date;
                    setPending(null);
                    go({ from, to, date: null });
                  }}>{copy.compareButton}</button>
                ) : null}
              </>
            )}
          </span>
        </div>
        <div className="lx-axis-box" ref={axisBox}>
          <Axis versions={versions} today={today} width={width} read={compare ? null : readVersion} compare={compare} pending={pending} onMark={onMark} />
        </div>
      </div>
      <div className="lx-text-row">
        <nav className="lx-list" aria-label={copy.articlesNav}>
          <div className="lx-list-tools">
            <div className="lx-law-find">
              <FindIcon />
              <label className="lx-visually-hidden" htmlFor="lx-law-find">{copy.findInLaw}</label>
              <input id="lx-law-find" type="search" placeholder={copy.findInLaw} autoComplete="off" value={find} onChange={(e) => setFind(e.target.value)} />
            </div>
            {compare ? (
              <span className="lx-segmented" role="group" aria-label={copy.filterLabel}>
                <button type="button" aria-pressed="false" onClick={() => go({ from: null, to: null, date: compare.to })}>{fill(copy.allArticles, { count: grouped(counts ? counts.changed + counts.added + counts.removed + counts.unchanged : contents.length) })}</button>
                <button type="button" aria-pressed="true">{fill(copy.changedArticles, { count: grouped(changedRows.length) })}</button>
              </span>
            ) : isEurope || fromEngine ? null : (
              <span className="lx-segmented" role="group" aria-label={copy.filterLabel}>
                <button type="button" aria-pressed={filter === 'all'} onClick={() => setFilter('all')}>{fill(copy.allArticles, { count: grouped(text.articleCount ?? contents.length) })}</button>
                <button type="button" aria-pressed={filter === 'changed'} onClick={() => setFilter('changed')} disabled={!diffsDone}>{fill(copy.changedArticles, { count: diffsDone ? grouped(changedList.size) : '…' })}</button>
              </span>
            )}
            {compare || isEurope || fromEngine ? null : <p className="lx-list-hint">{copy.yearHint}</p>}
          </div>
          {compare === null && outline !== null && outline.at === outlineAt ? (
            <TextOutline outline={outline.value} current={address?.article ?? null} order={contents.map((item) => item.id)} identifier={text.identifier} date={address?.date ?? null} onSelect={(id) => { setHighlight(id); select(id); }} />
          ) : null}
          <ul>
            {shown.map((item) => (
              <li key={item.id}>
                <a href={textHref(compare ? { identifier: text.identifier, from: compare.from, to: compare.to, article: item.id } : { identifier: text.identifier, date: address.date, article: item.id })} 
                  aria-current={(compare ? compareRow?.publisher_id : address.article) === item.id ? 'true' : undefined}
                  onClick={(e) => { e.preventDefault(); select(item.id); }}>
                  <span className="lx-list-number">{item.number}</span>
                  <span className="lx-list-heading" lang={textLang}>{headingOf(item.id)}</span>
                  <span className="lx-list-years">{compare ? '' : yearsOf(item.id)}</span>
                </a>
              </li>
            ))}
          </ul>
          {shown.length === 0 && needle.length > 0 ? <p className="lx-list-note">{copy.noMatch}</p> : null}
          {compare && counts ? (
            <p className="lx-list-note">
              {counted(copy.identicalNote, counts.unchanged)} {counts.added === 0 && counts.removed === 0 ? copy.noneAddedRemoved : [counts.added > 0 ? counted(copy.summaryAdded, counts.added) : null, counts.removed > 0 ? counted(copy.summaryRemoved, counts.removed) : null].filter(Boolean).join(', ')}
            </p>
          ) : null}
        </nav>
        <div className="lx-reading" data-reading="">
          {compare ? (
            compareAnswer?.failed ? <p role="alert">{copy.failed}</p>
              : comparison && changedRows.length === 0 ? <p className="lx-muted">{copy.sameVersion}</p>
                : (
                  <CompareArticle
                    row={comparison ? compareRow : null}
                    versions={versions}
                    changedDates={compareRow ? index.get(compareRow.publisher_id) ?? null : null}
                    compare={compare}
                    view={address.view}
                    index={compareIndex}
                    total={changedRows.length}
                    labelOf={(id, step = 0) => {
                      const target = step === 0 ? id : changedRows[compareIndex + step]?.publisher_id;
                      return labelById.get(target) ?? articleNumberOf(target);
                    }}
                    onStep={(step) => go({ article: changedRows[compareIndex + step]?.publisher_id ?? null })}
                    textLang={textLang}
                  />
                )
          ) : (
            <>
              {/* The side build: the text's links with other texts, folded, while no article is selected. */}
              {textLinks !== null && textLinks.at === linksAt && (address?.article ?? null) === null ? <TextLinks links={textLinks.value} /> : null}
              {loadedPages.length > 0 && loadedPages[0] > 1 ? (
                <button type="button" className="lx-button lx-more" onClick={() => loadPage(loadedPages[0] - 1)}>{copy.earlier}</button>
              ) : null}
              {readArticles.map((entry) => {
                const article = articlesById.get(entry.id) ?? null;
                return (
                  <ReadArticle
                    key={entry.id}
                    entry={entry}
                    article={article}
                    parts={partsOf(article)}
                    versions={versions}
                    changedDates={isEurope ? [] : diffsDone || index.has(entry.id) ? index.get(entry.id) ?? [] : null}
                    identifier={text.identifier}
                    highlighted={highlight === entry.id}
                    quotes={marks !== null && marks.identifier === text.identifier ? marks.marks.filter((mark) => mark.article === entry.id).map((mark) => mark.text) : null}
                    version={readVersion}
                    onCompareOn={(from, to, id) => go({ from, to, article: id, date: null })}
                    references={references !== null && references.at === referencesAt && references.article === entry.id ? references.value : null}
                    onOpenReference={(id) => { setHighlight(id); select(id); }}
                  />
                );
              })}
              {loadedPages.length > 0 && loadedPages.at(-1) < pageCount ? (
                <button type="button" className="lx-button lx-more" onClick={() => loadPage(loadedPages.at(-1) + 1)}>{copy.later}</button>
              ) : null}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
