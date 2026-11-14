// The law page (the owner's order of 2026-11-13, section B): the act's own page, at its readable address
// (`/lu-legilux/{work}`), read from its dossier, with nothing to type.
//
// - The header: the act's own title (never the publisher's "Version consolidée applicable au …" prefix, which names a
//   version), its status today in words, and its article and version counts (`LawHeader`). No identifier: the owner's
//   order of 2026-11-14 keeps the first screen to the title, the status, the counts, the versions on one line, the date of
//   a claim and the change in four numbers.
// - Its versions as points in time, oldest first, one language (`LawTimeline`): a click on a version chooses it to read; a
//   click on a second compares the two; the page opens comparing the latest version with the one before it. The date of a
//   claim chooses the version that applied on that date.
// - The comparison is the compare page's own (`CompareAnswerView`, the `diff` operation read by `compare-answer.mjs`): the
//   counts, then the changed, added and removed articles with the words inserted underlined and the words deleted struck
//   through, then the unchanged ones one disclosure away, "unchanged since" the earlier version's year. Each article has its
//   official source, a citation to copy and its history (`article_history`, read by `history-answer.mjs`), asked in place
//   and said in a line per version (`HistoryLines`).
// - At the foot, one disclosure, "Technical details": the versions compared with their addresses and digests, the platform's
//   rules and notes (in English), and the act's full record (the `children`). Nothing in English outside it on a page in
//   another language.
//
// The server renders the header, the versions and the two chosen; the comparison is asked once the page is in the browser,
// so the page reads without a script and hydration changes nothing the server rendered. Every sentence is the copy table's.

import { useEffect, useRef, useState } from 'react';

import { CHROME_LOCALE, countedEntry, displayDate, fillCounted, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { Say } from './LiveAnswer.jsx';
import { createCompareSession } from '../scripts/live-compare.mjs';
import { CompareAnswerView, CompareTechnical } from './LiveCompare.jsx';
import { datedByEntryIntoForce } from '../scripts/date-role.mjs';
import { createHistorySession } from '../scripts/live-history.mjs';
import { HistoryAnswerView } from './LiveHistory.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import { isSiteAddress, permalinkHref } from '../scripts/permalinks.mjs';
import { actTitle, afterClick, articleNumberOf, defaultPair, lawStatus, readClaimDate, versionOn, versionsOf } from '../scripts/law-page.mjs';

const COPY = liveChrome().dossier;
const HISTORY = liveChrome().history;

/**
 * The act's heading, the page's own (`h1`): its own title, its status today and its counts (the latest version's articles,
 * the versions held).
 */
export function LawHeader({ view, today = null }) {
  const named = actTitle(view);
  const versions = versionsOf(view);
  const latest = versions.at(-1) ?? null;
  const status = lawStatus(view, today);
  return (
    <header className="law-header" data-law-header="">
      {named === null
        ? <h1>{view.workKey}</h1>
        : <h1><span lang={quotationLanguageTag(named.language)}>{named.title}</span></h1>}
      <p className="law-facts">
        {status === null ? null : <strong className="law-status" data-law-status={status}>{COPY.lawStatus[status]}</strong>}
        {status === null ? null : ' · '}
        {latest === null ? null : <Say template={countedEntry(COPY.lawArticles, latest.articleCount)} values={{ count: latest.articleCount }} />}
        {latest === null ? null : ' · '}
        <Say template={countedEntry(COPY.lawVersions, versions.length)} values={{ count: versions.length }} />
      </p>
    </header>
  );
}

/** The citation of one article of the version read: its number, the act's title and the version's date. */
function citationOf(row, view, version) {
  const named = actTitle(view);
  return fillText(COPY.citation, { article: articleNumberOf(row.publisherId), title: named?.title ?? view.workKey, from: version.applicabilityDate });
}

/**
 * An article's history on the law page, a line per version that carries it: its dates, and whether its wording changed
 * from the version before, with a link to read that version; the versions that do not carry it, counted. The table with
 * each version's address and digest stays on the history page. A refusal or a failure is said as there.
 */
function HistoryLines({ outcome, onRetry }) {
  if (outcome.state !== 'success') return <HistoryAnswerView outcome={outcome} onRetry={onRetry} />;
  const { view } = outcome;
  const seen = new Set();
  return (
    <section data-answer-state="success" data-history-lines={view.rows.length}>
      <ol className="history-lines">
        {view.rows.map((row) => {
          const first = !seen.has(row.language);
          seen.add(row.language);
          const kind = first ? 'first' : row.wordingChanged ? 'changed' : 'unchanged';
          // A version dated by its entry into force is said as that, never as applying from it (R21).
          const dates = datedByEntryIntoForce(row)
            ? fillText(COPY.entryIntoForceDate, { date: row.applicabilityDate })
            : row.nextApplicabilityDate === null
              ? fillText(COPY.versionFrom, { from: row.applicabilityDate })
              : fillText(COPY.versionFromReplaced, { from: row.applicabilityDate, until: row.nextApplicabilityDate });
          return (
            <li key={row.stateSha256} data-row={row.stateSha256} data-wording={kind}>
              {fillText(COPY.historyLine, { dates, wording: HISTORY.wording[kind] })}
              {isSiteAddress(row.permalink) ? <>{' '}<a href={permalinkHref(row.permalink, CHROME_LOCALE)}>{COPY.readChosen}</a></> : null}
            </li>
          );
        })}
      </ol>
      {view.absent.length > 0 ? <p data-absent={view.absent.length}>{fillCounted(HISTORY.absent, view.absent.length, {})}</p> : null}
    </section>
  );
}

/**
 * One article's actions on the law page: its official source, its citation to copy, its history asked in place (at once when
 * `openHistory`, as an answer's "see at another date" asks it).
 */
function ArticleActions({ row, language, view, version, contract, fetchImpl, openHistory = false }) {
  const [copied, setCopied] = useState(false);
  const [history, setHistory] = useState(null);
  const session = useRef(null);
  useEffect(() => () => session.current?.cancel(), []);
  const identifier = `/lu-legilux/${view.workKey}`;
  useEffect(() => {
    if (!openHistory || contract === undefined) return;
    if (session.current === null) session.current = createHistorySession({ contract, fetchImpl, onOutcome: setHistory });
    session.current.ask({ identifier, anchor: row.publisherId, language });
  }, [openHistory]);
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
      {history === null ? null : <HistoryLines outcome={history} onRetry={() => session.current?.retry()} />}
    </div>
  );
}

/**
 * The article an answer's card opened the page at, from the address's hash: `#article-{id}` (its history) or `#changes-{id}`
 * (the comparison, at that article); null for any other.
 */
export function focusOf(hash) {
  const match = /^#(article|changes)-(.+)$/u.exec(typeof hash === 'string' ? hash : '');
  if (match === null) return null;
  try {
    return Object.freeze({ kind: match[1], anchor: decodeURIComponent(match[2]) });
  } catch {
    return null;
  }
}

/**
 * The page's one disclosure at its foot (the owner's order of 2026-11-14): "Technical details", closed, with the act's key,
 * the comparison's versions, rules and notes (`CompareTechnical`) once it is drawn, and the act's full record.
 */
function LawTechnical({ view, outcome, children }) {
  return (
    <details className="law-technical" data-law-technical="">
      <summary>{COPY.lawTechnical}{' · '}<code>{view.workKey}</code></summary>
      {outcome?.state === 'success' ? <CompareTechnical view={outcome.view} /> : null}
      <section data-law-record="" aria-labelledby="law-record">
        <h3 id="law-record">{COPY.lawRecord}</h3>
        {children}
      </section>
    </details>
  );
}

/**
 * The act's versions in time and the comparison of the two chosen (one click reads a version, a second compares the two;
 * the latest with the one before it when the page opens), and the date of a claim, which chooses the version that applied;
 * then `foot` (the span of the versions held, in sight) and the technical details, which hold the record (`children`).
 */
export function LawTimeline({ view, today = null, contract, fetchImpl, foot = null, children = null }) {
  const versions = versionsOf(view);
  // The article an answer's card opened the page at (`focusOf`), read once the page is in the browser: the server renders
  // no hash, so hydration changes nothing it rendered.
  const [focus, setFocus] = useState(null);
  useEffect(() => {
    setFocus(focusOf(globalThis.location?.hash));
  }, []);
  const start = defaultPair(versions);
  const [selection, setSelection] = useState(() => (start === null
    ? { read: Math.max(0, versions.length - 1), other: null }
    : { read: start[0], other: start[1] }));
  // The date of a claim as typed (day first, as a reader writes it), the calendar date it names (`readClaimDate`), and
  // whether the reader is done typing it (Enter or leaving the field), before which an unread date says nothing.
  const [claimText, setClaimText] = useState('');
  const [claimDone, setClaimDone] = useState(false);
  const claimDate = claimText.trim() === '' ? '' : readClaimDate(claimText);
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

  // "What changed" at one article: once the comparison is drawn, its row is opened (an unchanged one is one disclosure away)
  // and brought into view.
  useEffect(() => {
    if (focus?.kind !== 'changes' || outcome?.state !== 'success') return;
    const row = globalThis.document?.querySelector?.(`[data-article="${CSS.escape(focus.anchor)}"]`);
    if (!row) return;
    const disclosure = row.closest('details');
    if (disclosure) disclosure.open = true;
    row.scrollIntoView({ block: 'start' });
  }, [focus, outcome]);

  if (versions.length === 0) return <>{foot}<LawTechnical view={view} outcome={null}>{children}</LawTechnical></>;
  const claimIndex = claimDate === '' || claimDate === null ? null : versionOn(versions, claimDate);
  const law = {
    actions: (row, language) => (
      <ArticleActions row={row} language={language} view={view} version={other ?? read} contract={contract} fetchImpl={fetchImpl} />
    ),
  };
  const latest = versions.at(-1);
  return (
    <section className="law-timeline" data-law-timeline={versions.length} aria-labelledby="law-timeline">
      {focus?.kind === 'article' ? (
        // "See at another date": the article's own history, asked at once, above the act's versions.
        <section className="law-focus" data-law-focus={focus.anchor} aria-labelledby="law-focus">
          <h2 id="law-focus">{fillText(COPY.articleLabel, { number: articleNumberOf(focus.anchor) })}</h2>
          <ArticleActions row={{ publisherId: focus.anchor }} language={latest.language} view={view} version={latest} contract={contract} fetchImpl={fetchImpl} openHistory />
        </section>
      ) : null}
      <h2 id="law-timeline">{COPY.timelineHeading}</h2>
      <p className="timeline-help">{COPY.timelineHelp}</p>
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
                  setClaimText('');
                  setClaimDone(false);
                  setSelection(afterClick(selection, index));
                }}
              >
                <span className="dot" aria-hidden="true" />
                <span className="dot-date">{displayDate(version.applicabilityDate)}</span>
              </button>
            </li>
          );
        })}
      </ol>
      <div className="law-choice">
      <form
        className="claim-date"
        data-claim-date=""
        onSubmit={(event) => {
          event.preventDefault();
          setClaimDone(true);
        }}
      >
        <label>
          {COPY.claimDateLabel}{' '}
          <input
            type="text"
            inputMode="numeric"
            autoComplete="off"
            spellCheck={false}
            size={10}
            placeholder={COPY.claimDatePlaceholder}
            value={claimText}
            onBlur={() => setClaimDone(true)}
            onChange={(event) => {
              const text = event.target.value;
              setClaimText(text);
              setClaimDone(false);
              const index = versionOn(versions, readClaimDate(text));
              if (index >= 0) setSelection({ read: index, other: null });
            }}
          />
        </label>
      </form>
      <p data-chosen-versions={other === null ? read.applicabilityDate : `${read.applicabilityDate}/${other.applicabilityDate}`}>
        {other === null
          ? fillText(COPY.timelineRead, { from: read.applicabilityDate })
          : fillText(COPY.timelineCompared, { from: read.applicabilityDate, to: other.applicabilityDate })}
        {isSiteAddress((other ?? read).permalink) ? <>{' '}<a href={permalinkHref((other ?? read).permalink, CHROME_LOCALE)}>{COPY.readChosen}</a></> : null}
      </p>
      </div>
      {claimIndex === null ? null : (
        <p role="status" data-claim-version={claimIndex >= 0 ? versions[claimIndex].applicabilityDate : 'none'}>
          {claimIndex >= 0
            ? fillText(COPY.claimDateFound, { date: claimDate, from: versions[claimIndex].applicabilityDate })
            : fillText(COPY.claimDateNone, { date: claimDate })}
        </p>
      )}
      {claimDate === null && claimDone ? (
        <p role="status" data-claim-unread="">{fillText(COPY.claimDateUnread, { example: latest.applicabilityDate })}</p>
      ) : null}
      {outcome === null ? null : <CompareAnswerView outcome={outcome} onRetry={() => session.current?.retry()} onPage={(page) => session.current?.askPage(page)} law={law} />}
      {foot}
      <LawTechnical view={view} outcome={outcome}>{children}</LawTechnical>
    </section>
  );
}
