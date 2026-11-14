// The law page (the owner's order of 2026-11-13, section B): the act's own page, at its readable address
// (`/lu-legilux/{work}`), read from its dossier, with nothing to type.
//
// - The header: the act's own title (never the publisher's "Version consolidée applicable au …" prefix, which names a
//   version), its status today in words, and its article and version counts (`LawHeader`).
// - Its versions as points in time, oldest first, one language (`LawTimeline`): a click on a version chooses it to read; a
//   click on a second compares the two; the page opens comparing the latest version with the one before it. The date of a
//   claim chooses the version that applied on that date.
// - The comparison is the compare page's own (`CompareAnswerView`, the `diff` operation read by `compare-answer.mjs`): the
//   counts, then the changed, added and removed articles with the words inserted underlined and the words deleted struck
//   through, then the unchanged ones one disclosure away, "unchanged since" the earlier version's year. Each article has its
//   official source, a citation to copy and its history (`article_history`, read by `history-answer.mjs`), asked in place.
//
// The server renders the header, the versions and the two chosen; the comparison is asked once the page is in the browser,
// so the page reads without a script and hydration changes nothing the server rendered. Every sentence is the copy table's.

import { useEffect, useRef, useState } from 'react';

import { CHROME_LOCALE, countedEntry, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { Say } from './LiveAnswer.jsx';
import { createCompareSession } from '../scripts/live-compare.mjs';
import { CompareAnswerView } from './LiveCompare.jsx';
import { createHistorySession } from '../scripts/live-history.mjs';
import { HistoryAnswerView } from './LiveHistory.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import { isSiteAddress, permalinkHref } from '../scripts/permalinks.mjs';
import { actTitle, afterClick, articleNumberOf, defaultPair, lawStatus, versionOn, versionsOf } from '../scripts/law-page.mjs';

const COPY = liveChrome().dossier;

/** The act's heading: its own title, its status today and its counts (the latest version's articles, the versions held). */
export function LawHeader({ view, today = null }) {
  const named = actTitle(view);
  const versions = versionsOf(view);
  const latest = versions.at(-1) ?? null;
  const status = lawStatus(view, today);
  return (
    <header className="law-header" data-law-header="">
      {named === null
        ? <h2>{view.workKey}</h2>
        : <h2><span lang={quotationLanguageTag(named.language)}>{named.title}</span></h2>}
      <p className="law-facts">
        {status === null ? null : <strong className="law-status" data-law-status={status}>{COPY.lawStatus[status]}</strong>}
        {status === null ? null : ' · '}
        {latest === null ? null : <Say template={countedEntry(COPY.lawArticles, latest.articleCount)} values={{ count: latest.articleCount }} />}
        {latest === null ? null : ' · '}
        <Say template={countedEntry(COPY.lawVersions, versions.length)} values={{ count: versions.length }} />
      </p>
      <p data-work-key=""><code>{view.workKey}</code></p>
    </header>
  );
}

/** The citation of one article of the version read: its number, the act's title and the version's date. */
function citationOf(row, view, version) {
  const named = actTitle(view);
  return fillText(COPY.citation, { article: articleNumberOf(row.publisherId), title: named?.title ?? view.workKey, from: version.applicabilityDate });
}

/** One article's actions on the law page: its official source, its citation to copy, its history asked in place. */
function ArticleActions({ row, language, view, version, contract, fetchImpl }) {
  const [copied, setCopied] = useState(false);
  const [history, setHistory] = useState(null);
  const session = useRef(null);
  useEffect(() => () => session.current?.cancel(), []);
  const identifier = `/lu-legilux/${view.workKey}`;
  const toggleHistory = () => {
    if (history !== null) {
      session.current?.cancel();
      setHistory(null);
      return;
    }
    if (session.current === null) session.current = createHistorySession({ contract, fetchImpl, onOutcome: setHistory });
    session.current.ask({ identifier, anchor: row.publisherId, language });
  };
  const copy = () => {
    const text = citationOf(row, view, version);
    Promise.resolve(globalThis.navigator?.clipboard?.writeText?.(text)).then(() => setCopied(true), () => setCopied(false));
  };
  return (
    <div className="article-actions" data-article-actions={row.publisherId}>
      <p className="change-nav">
        <a href={version.publisherLegalResourceIri} rel="external">{COPY.articleSource}</a>
        {' · '}
        <button type="button" onClick={copy} data-copy-citation="">{copied ? COPY.articleCited : COPY.articleCite}</button>
        {' · '}
        <button type="button" onClick={toggleHistory} aria-expanded={history !== null} data-article-history="">
          {history === null ? COPY.articleHistory : COPY.articleHistoryHide}
        </button>
      </p>
      {history === null ? null : <HistoryAnswerView outcome={history} onRetry={() => session.current?.retry()} />}
    </div>
  );
}

/**
 * The act's versions in time and the comparison of the two chosen (one click reads a version, a second compares the two;
 * the latest with the one before it when the page opens), and the date of a claim, which chooses the version that applied.
 */
export function LawTimeline({ view, today = null, contract, fetchImpl }) {
  const versions = versionsOf(view);
  const start = defaultPair(versions);
  const [selection, setSelection] = useState(() => (start === null
    ? { read: Math.max(0, versions.length - 1), other: null }
    : { read: start[0], other: start[1] }));
  const [claimDate, setClaimDate] = useState('');
  const [outcome, setOutcome] = useState(null);
  const session = useRef(null);
  if (session.current === null && contract !== undefined) {
    session.current = createCompareSession({ contract, fetchImpl, onOutcome: setOutcome });
  }
  useEffect(() => () => session.current?.cancel(), []);

  const read = versions[selection.read] ?? null;
  const other = selection.other === null ? null : versions[selection.other];
  const identifier = `/lu-legilux/${view.workKey}`;
  // The comparison of the two chosen, asked once the page is in the browser and again whenever the choice changes; one
  // version chosen asks nothing.
  useEffect(() => {
    if (read === null || other === null || session.current === null) {
      session.current?.cancel();
      setOutcome(null);
      return;
    }
    session.current.ask({ identifier, dateFrom: read.applicabilityDate, dateTo: other.applicabilityDate, language: read.language });
  }, [selection.read, selection.other]);

  if (versions.length === 0) return null;
  const claimIndex = claimDate === '' ? null : versionOn(versions, claimDate);
  const law = {
    actions: (row, language) => (
      <ArticleActions row={row} language={language} view={view} version={other ?? read} contract={contract} fetchImpl={fetchImpl} />
    ),
  };
  return (
    <section className="law-timeline" data-law-timeline={versions.length} aria-labelledby="law-timeline">
      <h3 id="law-timeline">{COPY.timelineHeading}</h3>
      <p>{COPY.timelineHelp}</p>
      <ol className="timeline-dots">
        {versions.map((version, index) => {
          const chosen = index === selection.read || index === selection.other;
          return (
            <li key={version.stateSha256} data-version-date={version.applicabilityDate} data-chosen={chosen ? '' : undefined}>
              <button
                type="button"
                className="timeline-dot"
                aria-pressed={chosen}
                aria-label={fillText(COPY.timelineVersion, { date: version.applicabilityDate })}
                onClick={() => {
                  setClaimDate('');
                  setSelection(afterClick(selection, index));
                }}
              >
                <span className="dot" aria-hidden="true" />
                <span className="dot-date">{version.applicabilityDate}</span>
              </button>
            </li>
          );
        })}
      </ol>
      <form className="claim-date" data-claim-date="" onSubmit={(event) => event.preventDefault()}>
        <label>
          {COPY.claimDateLabel}{' '}
          <input
            type="date"
            value={claimDate}
            onChange={(event) => {
              const date = event.target.value;
              setClaimDate(date);
              const index = versionOn(versions, date);
              if (index >= 0) setSelection({ read: index, other: null });
            }}
          />
        </label>
      </form>
      {claimIndex === null ? null : (
        <p role="status" data-claim-version={claimIndex >= 0 ? versions[claimIndex].applicabilityDate : 'none'}>
          {claimIndex >= 0
            ? fillText(COPY.claimDateFound, { date: claimDate, from: versions[claimIndex].applicabilityDate })
            : fillText(COPY.claimDateNone, { date: claimDate })}
        </p>
      )}
      <p data-chosen-versions={other === null ? read.applicabilityDate : `${read.applicabilityDate}/${other.applicabilityDate}`}>
        {other === null
          ? fillText(COPY.timelineRead, { from: read.applicabilityDate })
          : fillText(COPY.timelineCompared, { from: read.applicabilityDate, to: other.applicabilityDate })}
        {isSiteAddress((other ?? read).permalink) ? <>{' '}<a href={permalinkHref((other ?? read).permalink, CHROME_LOCALE)}>{COPY.readChosen}</a></> : null}
      </p>
      {outcome === null ? null : <CompareAnswerView outcome={outcome} onRetry={() => session.current?.retry()} onPage={(page) => session.current?.askPage(page)} law={law} />}
    </section>
  );
}
