// "What Lex holds, and what it does not" (K-SCOPE-4), at the top of Trust and Coverage (`coverage.html`), from the file the
// data builder writes (`data/coverage.json`, served as it is by `GET /api/next/coverage`): held and not held by type of text,
// the share of the texts in force that Lex holds, each code's earliest version held (the date floor), the texts not held by
// name, the annexes held only as PDF, and the source and licence lines as the file states them.
//
// The file's fields are read by several names (the builder may write either), and a section the file does not carry is
// left out; nothing is drawn while the file is missing (`{"none": true}`). Every figure is the file's; the share in force is
// the file's two counts divided, said with both counts. Built in its loading state; `client-live-next-entry.jsx` draws it.

import { useEffect, useState } from 'react';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { countedForm, fillNext, groupedCount, nextCopy, numericDate } from '../scripts/next-copy.mjs';

export const NEXT_COVERAGE_ROOT = 'lx-next-coverage';
export const COVERAGE_ADDRESS = '/api/next/coverage';

const isCount = (value) => typeof value === 'number' && Number.isFinite(value);
const objectOr = (value) => (value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null);
const text = (value) => (typeof value === 'string' && value.trim().length > 0 ? value.trim() : null);
const first = (object, ...keys) => {
  for (const key of keys) if (object !== null && object !== undefined && object[key] !== undefined && object[key] !== null) return object[key];
  return undefined;
};
const iso = (value) => typeof value === 'string' && /^[0-9]{4}-[0-9]{2}-[0-9]{2}/u.test(value);

/** The coverage file read into the page's sections; null when there is nothing to say. */
export function coverageView(coverage, locale) {
  const copy = nextCopy(locale).coverage;
  const file = objectOr(coverage);
  if (file === null || file.none === true) return null;
  const typeName = (key) => copy.types[key] ?? key;
  const byType = (Array.isArray(first(file, 'by_type', 'types')) ? first(file, 'by_type', 'types') : []).filter((row) => objectOr(row) !== null && text(first(row, 'type', 'key')) !== null).map((row) => {
    const listed = first(row, 'listed', 'catalogue', 'in_catalogue');
    const held = first(row, 'held');
    const notHeld = first(row, 'not_held', 'missing');
    return {
      key: first(row, 'type', 'key'),
      name: text(row.label) ?? typeName(first(row, 'type', 'key')),
      listed: isCount(listed) ? listed : null,
      held: isCount(held) ? held : null,
      notHeld: isCount(notHeld) ? notHeld : isCount(listed) && isCount(held) ? listed - held : null,
      inForceListed: isCount(first(row, 'in_force_listed', 'in_force_catalogue')) ? first(row, 'in_force_listed', 'in_force_catalogue') : null,
      inForceHeld: isCount(first(row, 'in_force_held')) ? first(row, 'in_force_held') : null,
    };
  });
  const inForce = objectOr(first(file, 'in_force', 'in_force_share'));
  const inForceHeld = first(inForce, 'held');
  const inForceListed = first(inForce, 'listed', 'catalogue', 'in_catalogue', 'total');
  const share = isCount(inForceHeld) && isCount(inForceListed) && inForceListed > 0
    ? fillNext(copy.inForce, { held: groupedCount(inForceHeld, locale), listed: groupedCount(inForceListed, locale), share: String(Math.round((100 * inForceHeld) / inForceListed)) })
    : null;
  const named = (Array.isArray(first(file, 'not_held_named', 'named_not_held', 'not_held')) ? first(file, 'not_held_named', 'named_not_held', 'not_held') : [])
    .filter((row) => text(typeof row === 'string' ? row : first(row, 'name', 'title')) !== null)
    .map((row) => (typeof row === 'string' ? { name: row.trim(), why: null } : { name: text(first(row, 'name', 'title')), why: text(first(row, 'why', 'note', 'reason')) }));
  const floors = (Array.isArray(first(file, 'date_floor', 'date_floors', 'floors')) ? first(file, 'date_floor', 'date_floors', 'floors') : [])
    .filter((row) => objectOr(row) !== null && text(first(row, 'title', 'name', 'act_id')) !== null && iso(first(row, 'first_held', 'floor', 'from')))
    .map((row) => ({
      title: text(first(row, 'title', 'name', 'act_id')),
      floor: numericDate(first(row, 'first_held', 'floor', 'from')),
      versions: isCount(first(row, 'versions_held', 'versions')) ? first(row, 'versions_held', 'versions') : null,
      listed: isCount(first(row, 'versions_listed', 'versions_catalogue')) ? first(row, 'versions_listed', 'versions_catalogue') : null,
    }));
  const pdf = first(file, 'annexes_pdf_only', 'pdf_only', 'pdf_only_annexes');
  const pdfCount = isCount(pdf) ? pdf : isCount(objectOr(pdf)?.count) ? pdf.count : null;
  // A title the publisher wrote over several lines (a written "\n") is read on one line.
  const pdfItems = (Array.isArray(objectOr(pdf)?.items) ? pdf.items : []).map((item) => (typeof item === 'string' ? text(item) : text(first(item, 'title', 'name'))))
    .filter((item) => item !== null).map((item) => item.replace(/\\n/gu, ' ').replace(/\s+/gu, ' '));
  const sourceField = first(file, 'sources', 'source', 'licence', 'license');
  const sources = (Array.isArray(sourceField) ? sourceField : sourceField === undefined ? [] : [sourceField])
    .map((item) => (typeof item === 'string' ? text(item) : text(first(item, 'line', 'statement', 'text', 'licence')))).filter((item) => item !== null);
  const made = first(file, 'made', 'built_on', 'date');
  // pages2 (21/11/2026): the rest of the file the data builders write (ingest\build_coverage.py), every figure the file's.
  const heldCounts = objectOr(file.held);
  const held = heldCounts !== null && isCount(heldCounts.luxembourg_acts) && isCount(heldCounts.eu_acts)
    ? fillNext(copy.held, {
      lu: groupedCount(heldCounts.luxembourg_acts, locale), eu: groupedCount(heldCounts.eu_acts, locale),
      luVersions: countedForm(copy.versions, isCount(heldCounts.luxembourg_versions) ? heldCounts.luxembourg_versions : 0, locale),
      euVersions: countedForm(copy.versions, isCount(heldCounts.eu_versions) ? heldCounts.eu_versions : 0, locale),
    }) : null;
  const statusCounts = objectOr(file.status);
  // In the copy's order (Luxembourg first), whatever the file's.
  const status = statusCounts === null ? [] : Object.keys(copy.status).filter((key) => isCount(statusCounts[key]))
    .map((key) => ({ key, name: copy.status[key], count: statusCounts[key] }));
  const keyTexts = (Array.isArray(file.named) ? file.named : []).filter((row) => objectOr(row) !== null && text(row.name) !== null).map((row) => {
    const version = objectOr(row.today_version);
    const versionLine = version === null || !iso(version.from) ? null
      : iso(version.until) ? fillNext(copy.versionSpan, { from: numericDate(version.from), until: numericDate(version.until) })
        : fillNext(version.until_known === 0 || version.until_known === false ? copy.versionUnsure : copy.versionOpen, { from: numericDate(version.from) });
    const group = isCount(row.count) ? fillNext(copy.group, { held: groupedCount(row.count, locale), inForce: groupedCount(isCount(row.in_force) ? row.in_force : 0, locale) }) : null;
    return {
      key: row.act_id ?? row.name, name: text(row.name), held: row.held === true, group,
      status: typeof row.status === 'string' ? copy.statusWords[row.status] ?? null : null,
      version: row.held === true ? versionLine : null,
      why: typeof row.why === 'string' && row.why !== 'applicable_today' ? copy.why[row.why] ?? null : text(row.held === false ? row.why : null),
    };
  });
  const todayWhy = objectOr(objectOr(file.versions)?.today_version_why);
  const today = todayWhy === null ? [] : Object.keys(copy.today).filter((key) => isCount(todayWhy[key])).map((key) => ({ key, name: copy.today[key], count: todayWhy[key] }));
  const conservation = objectOr(objectOr(file.checks)?.conservation);
  const order = objectOr(objectOr(file.checks)?.conservation_order);
  const euFiles = objectOr(objectOr(file.checks)?.eu_file_check);
  const checks = [
    conservation !== null && isCount(conservation.passed) && isCount(conservation.files_checked) && order !== null && isCount(order.passed) && isCount(order.files_checked)
      ? fillNext(copy.conservation, { passed: groupedCount(conservation.passed, locale), files: groupedCount(conservation.files_checked, locale), orderPassed: groupedCount(order.passed, locale), orderFiles: groupedCount(order.files_checked, locale) }) : null,
    euFiles !== null && isCount(euFiles.failing) && isCount(euFiles.files) ? fillNext(copy.euCheck, { failing: groupedCount(euFiles.failing, locale), files: groupedCount(euFiles.files, locale) }) : null,
  ].filter((line) => line !== null);
  const quarantined = objectOr(file.ingest)?.quarantined;
  const quarantine = Array.isArray(quarantined) ? countedForm(copy.quarantine, quarantined.length, locale) : null;
  const precision = objectOr(objectOr(file.references)?.precision);
  const references = precision !== null && isCount(precision.right) && isCount(precision.size) ? fillNext(copy.references, { right: groupedCount(precision.right, locale), size: groupedCount(precision.size, locale) }) : null;
  const outlineCounts = objectOr(file.outline);
  const outline = outlineCounts !== null && isCount(outlineCounts.acts_with_outline_today) && isCount(outlineCounts.acts)
    ? fillNext(copy.outline, { with: groupedCount(outlineCounts.acts_with_outline_today, locale), acts: groupedCount(outlineCounts.acts, locale) }) : null;
  const view = {
    made: iso(made) ? numericDate(made) : null, byType, share, named, floors, pdfCount, pdfItems, sources: sources.map(datesInLine),
    held, status, keyTexts, today, checks, quarantine, references, outline,
  };
  return byType.length === 0 && share === null && named.length === 0 && floors.length === 0 && pdfCount === null && sources.length === 0 && held === null && keyTexts.length === 0 ? null : view;
}

/** A line the file writes, its dates (yyyy-mm-dd) said as dd/mm/yyyy (the owner's standard). */
export const datesInLine = (line) => String(line).replace(/\b([0-9]{4})-([0-9]{2})-([0-9]{2})\b/gu, '$3/$2/$1');

/** The section, drawn from the coverage file: nothing at all while there is none. */
export function CoverageView({ coverage = null, locale = CHROME_LOCALE }) {
  const copy = nextCopy(locale).coverage;
  const view = coverageView(coverage, locale);
  if (view === null) return null;
  const lawLang = locale === 'fr' ? undefined : 'fr';
  const count = (value) => (isCount(value) ? groupedCount(value, locale) : '');
  const inForceColumn = view.byType.some((row) => row.inForceListed !== null || row.inForceHeld !== null);
  return (
    <section aria-labelledby="next-coverage-heading" data-next-section="coverage">
      <h2 id="next-coverage-heading">{copy.heading}</h2>
      {view.made === null ? null : <p className="lx-muted">{fillNext(copy.made, { date: view.made })}</p>}
      {view.held === null ? null : <p data-coverage="held">{view.held}</p>}
      {view.share === null ? null : <p data-coverage="in-force"><strong>{view.share}</strong></p>}
      {view.keyTexts.length === 0 ? null : (
        <>
          <h3 id="next-coverage-key">{copy.namedHeading2}</h3>
          <div className="lx-how-table-scroll">
            <table className="lx-how-table lx-next-counts" data-coverage="key-texts">
              <thead><tr><th scope="col">{copy.namedColumns.text}</th><th scope="col">{copy.namedColumns.held}</th><th scope="col">{copy.namedColumns.status}</th><th scope="col">{copy.namedColumns.version}</th></tr></thead>
              <tbody>
                {view.keyTexts.map((row) => (
                  <tr key={row.key} data-key-text={row.held ? 'held' : 'not-held'}>
                    <th scope="row" lang={lawLang}>{row.name}</th>
                    <td className={row.held ? undefined : 'lx-next-gap'}>{row.group ?? (row.held ? copy.yes : copy.no)}</td>
                    <td>{row.status ?? ''}</td>
                    <td>{[row.version, row.why].filter((part) => part !== null).join(' — ')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
      {view.status.length === 0 ? null : (
        <>
          <h3 id="next-coverage-status">{copy.statusHeading}</h3>
          <p className="lx-next-note">{copy.statusNote}</p>
          <div className="lx-how-table-scroll">
            <table className="lx-how-table lx-next-counts" data-coverage="status">
              <thead><tr><th scope="col">{copy.statusColumns.what}</th><th scope="col">{copy.statusColumns.count}</th></tr></thead>
              <tbody>{view.status.map((row) => <tr key={row.key} data-status={row.key}><th scope="row">{row.name}</th><td>{count(row.count)}</td></tr>)}</tbody>
            </table>
          </div>
        </>
      )}
      {view.byType.length === 0 ? null : (
        <div className="lx-how-table-scroll">
          <table className="lx-how-table lx-next-counts" data-coverage="by-type">
            <thead>
              <tr>
                <th scope="col">{copy.columns.type}</th><th scope="col">{copy.columns.listed}</th><th scope="col">{copy.columns.held}</th><th scope="col">{copy.columns.notHeld}</th>
                {inForceColumn ? <th scope="col">{copy.columns.inForce}</th> : null}
              </tr>
            </thead>
            <tbody>
              {view.byType.map((row) => (
                <tr key={row.key} data-type={row.key}>
                  <th scope="row">{row.name}</th><td>{count(row.listed)}</td><td>{count(row.held)}</td><td className={row.notHeld > 0 ? 'lx-next-gap' : undefined}>{count(row.notHeld)}</td>
                  {inForceColumn ? <td>{row.inForceHeld !== null && row.inForceListed !== null ? fillNext(copy.ofListed, { held: count(row.inForceHeld), listed: count(row.inForceListed) }) : ''}</td> : null}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {view.named.length === 0 ? null : (
        <>
          <h3 id="next-coverage-named">{copy.namedHeading}</h3>
          <ul className="lx-how-lines" data-coverage="named">
            {view.named.map((row) => <li key={row.name}><span lang={lawLang}>{row.name}</span>{row.why === null ? null : <span className="lx-muted" lang={lawLang}> — {row.why}</span>}</li>)}
          </ul>
        </>
      )}
      {view.floors.length === 0 ? null : (
        <>
          <h3 id="next-coverage-floors">{copy.floorsHeading}</h3>
          <div className="lx-how-table-scroll">
            <table className="lx-how-table lx-next-counts" data-coverage="floors">
              <thead><tr><th scope="col">{copy.columns.text}</th><th scope="col">{copy.columns.floor}</th><th scope="col">{copy.columns.versions}</th></tr></thead>
              <tbody>
                {view.floors.map((row) => (
                  <tr key={row.title}>
                    <th scope="row" lang={lawLang}>{row.title}</th><td>{row.floor}</td>
                    <td>{row.versions === null ? '' : row.listed === null ? count(row.versions) : fillNext(copy.ofListed, { held: count(row.versions), listed: count(row.listed) })}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
      {view.pdfCount === null && view.pdfItems.length === 0 ? null : (
        <>
          <h3 id="next-coverage-pdf">{copy.pdfHeading}</h3>
          {view.pdfCount === null ? null : <p>{countedForm(copy.pdfCount, view.pdfCount, locale)}</p>}
          {view.pdfItems.length === 0 ? null : (
            <details data-coverage="pdf-items">
              <summary>{countedForm(copy.pdfMore, view.pdfItems.length, locale)}</summary>
              <ul className="lx-how-lines">{[...new Set(view.pdfItems)].map((item) => <li key={item} lang={lawLang}>{item}</li>)}</ul>
            </details>
          )}
        </>
      )}
      {view.today.length === 0 && view.checks.length === 0 && view.quarantine === null && view.references === null && view.outline === null ? null : (
        <details data-coverage="details">
          <summary>{copy.detailsSummary}</summary>
          {view.today.length === 0 ? null : (
            <>
              <h3 id="next-coverage-today">{copy.todayHeading}</h3>
              <div className="lx-how-table-scroll">
                <table className="lx-how-table lx-next-counts" data-coverage="today">
                  <tbody>{view.today.map((row) => <tr key={row.key} data-today={row.key}><th scope="row">{row.name}</th><td>{count(row.count)}</td></tr>)}</tbody>
                </table>
              </div>
            </>
          )}
          {view.checks.length === 0 && view.quarantine === null && view.references === null && view.outline === null ? null : (
            <>
              <h3 id="next-coverage-checks">{copy.checksHeading}</h3>
              {view.checks.map((line) => <p key={line} data-coverage="check">{line}</p>)}
              {view.quarantine === null ? null : <p data-coverage="quarantine">{view.quarantine}</p>}
              {view.references === null ? null : <p data-coverage="references">{view.references}</p>}
              {view.outline === null ? null : <p data-coverage="outline">{view.outline}</p>}
            </>
          )}
        </details>
      )}
      {view.sources.length === 0 ? null : (
        <>
          <h3 id="next-coverage-sources">{copy.sourcesHeading}</h3>
          {view.sources.map((line) => <p key={line} lang={lawLang} data-coverage="source">{line}</p>)}
        </>
      )}
    </section>
  );
}

/** The section, live: built empty; the file asked once the page is drawn. */
export function NextCoverage({ fetchImpl = undefined, locale = CHROME_LOCALE }) {
  const [coverage, setCoverage] = useState(null);
  useEffect(() => {
    let live = true;
    (async () => {
      try {
        const response = await (fetchImpl ?? globalThis.fetch)(COVERAGE_ADDRESS, { method: 'GET', headers: { accept: 'application/json' } });
        if (!response.ok) return;
        const data = await response.json();
        if (live && objectOr(data) !== null) setCoverage(data);
      } catch {
        // No section: the page reads as it does without it.
      }
    })();
    return () => { live = false; };
  }, [fetchImpl]);
  return <CoverageView coverage={coverage} locale={locale} />;
}
