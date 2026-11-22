// The side build's three pieces that read the new engine's own counts at run time, in the current pages' look:
//
//   - How it works (the body of `evidence.html`): the seven bricks, each with who does it and its counts, the choices and
//     why, the limits with their figures, and what Lex commits to. The counts are `GET /api/next/corpus`'s.
//   - Results (`results.html`): the scored tests, from `GET /api/next/results`: first the fresh questions written by
//     someone else (the proof), then the same earlier questions to the live Lex and to this version, the bench last.
//   - "What the assistant reads", the section at the top of Trust and Coverage (`coverage.html`), from the corpus counts.
//
// Each is built into its page in its loading state (no count is known at build), and drawn again by
// `client-live-next-entry.jsx` once the page has asked. The words are `scripts/next-copy.mjs`'s; no count is typed here.
// The classes are the existing pages' (`.lx-how-*`, `.lx-how-table`, `.lx-prose`): no inline style, as the policy asks.

import { useEffect, useState } from 'react';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { countWord, countedForm, decimal, fillNext, groupedCount, nextCopy, numericDate } from '../scripts/next-copy.mjs';
import { Results2View, isResults2, sameEngine } from './NextResults.jsx';
import { coverageLines } from '../scripts/next-view.mjs';

/** The roots the browser's script draws into, and the addresses asked. */
export const NEXT_HOW_ROOT = 'lx-next-how';
export const NEXT_RESULTS_ROOT = 'lx-next-results';
export const NEXT_CORPUS_ROOT = 'lx-next-corpus';
export const CORPUS_ADDRESS = '/api/next/corpus';
export const RESULTS_ADDRESS = '/api/next/results';
export const CONFIG_ADDRESS = '/api/next/config';

/** The script every such page loads (`scripts/build-live.mjs` builds it under each language). */
export const NEXT_SCRIPT = 'client-live-next.js';

const rowsOf = (value) => (Array.isArray(value) ? value.filter((row) => Array.isArray(row)) : []);
const isCount = (value) => typeof value === 'number' && Number.isFinite(value);

/** One address, asked once after the page is drawn: `{ state: 'loading' | 'ready' | 'failed', data }`. */
function useJson(address, fetchImpl) {
  const [answer, setAnswer] = useState({ state: 'loading', data: null });
  useEffect(() => {
    let live = true;
    (async () => {
      try {
        const response = await (fetchImpl ?? globalThis.fetch)(address, { method: 'GET', headers: { accept: 'application/json' } });
        if (!response.ok) throw new Error('not answered');
        const data = await response.json();
        if (data === null || typeof data !== 'object') throw new Error('not an object');
        if (live) setAnswer({ state: 'ready', data });
      } catch {
        if (live) setAnswer({ state: 'failed', data: null });
      }
    })();
    return () => { live = false; };
  }, [address, fetchImpl]);
  return answer;
}

// ---------------------------------------------------------------------------------------------------------------------------
// How it works.

/** The counts a brick's sentence names, from the corpus answer; null while one of them is not known. */
export function brickCounts(template, corpus, locale) {
  if (typeof template !== 'string' || corpus === null) return null;
  const sure = rowsOf(corpus.edges_by_certainty).find((row) => row[0] === 'same_act' && row[1] === 'sure')?.[2];
  const check = corpus.conservation !== null && typeof corpus.conservation === 'object' ? corpus.conservation : {};
  const values = {
    acts: corpus.acts, versions: corpus.versions, nodes: corpus.toc_nodes, articles: corpus.articles, lines: corpus.lines,
    edges: corpus.edges, sure, searchable: corpus.articles_searchable,
    links: corpus.links_between_texts?.total, changes: corpus.amendments?.changes, namingArticle: corpus.amendments?.naming_an_article,
    wordings: corpus.wordings?.total, severalWordings: corpus.wordings?.articles_with_several,
    outlineTexts: corpus.outline?.texts, outlineRows: corpus.outline?.rows,
    deletedAll: corpus.deleted_by_publisher?.articles_all_versions, keywordArticles: corpus.keyword_index?.articles,
    files: check.files ?? check.files_checked, passed: check.passed, failed: check.failed, bodyWords: check.body_words, storedWords: check.stored_law_words,
    deletedWords: check.deleted_words, notStoredWords: check.not_stored_words, numbersMissing: check.numbers_missing,
  };
  const named = [...template.matchAll(/\{([a-zA-Z]+)\}/gu)].map((match) => match[1]);
  if (named.some((name) => !isCount(values[name]))) return null;
  return fillNext(template, Object.fromEntries(named.map((name) => [name, groupedCount(values[name], locale)])));
}

/** How it works, drawn from the corpus counts (`corpus` null while they are not known: the page then stands without them). */
export function HowView({ corpus = null, state = 'loading', locale = CHROME_LOCALE }) {
  const table = nextCopy(locale);
  const copy = table.how;
  const dock = table.dock;
  const tagOf = (by) => (by === 'code' ? dock.tagCode : by === 'model' ? dock.tagModel : dock.tagBoth);
  const lawLang = locale === 'fr' ? undefined : 'fr';
  // Each reason in words; a reason the copy does not name is left out of the row, never shown as the engine's key.
  const leftOut = corpus === null ? [] : rowsOf(corpus.articles_left_out).filter((row) => isCount(row[1]) && Object.hasOwn(copy.limits.leftOutReasons, row[0]));
  // What Lex does not hold, and the first date held of each code: counted by code (`coverage_gaps` of the corpus answer).
  const notHeld = corpus === null ? [] : coverageLines(corpus.coverage_gaps, locale);
  return (
    <div className="lx-how" data-next-page="how" data-next-state={state}>
      <div className="lx-how-first">
        <div className="lx-how-flow" data-reference-section="bricks">
          <h1>{copy.heading}</h1>
          <p className="lx-how-intro">{copy.intro}</p>
          {state === 'ready' ? null : <p className="lx-how-intro" role="status" data-next-status="">{state === 'failed' ? copy.failed : copy.loading}</p>}
          <ol className="lx-how-stages">
            {copy.bricks.map((brick, at) => {
              // Each sentence of counts is said only when every count it names is known.
              const counts = brick.counts.map((template) => brickCounts(template, corpus, locale)).filter((line) => line !== null);
              return (
                <li className="lx-how-stage" data-stage={brick.key} key={brick.key}>
                  <span className="lx-how-n" aria-hidden="true">{at + 1}</span>
                  <div className="lx-how-name">
                    <h2>{brick.name}</h2>
                    <span className={brick.by === 'code' ? 'lx-how-tag' : 'lx-how-tag lx-how-tag-model'} data-by={brick.by}>{tagOf(brick.by)}</span>
                    {brick.who === null ? null : <span className="lx-how-decision-cost">{brick.who}</span>}
                  </div>
                  <div className="lx-how-body">
                    <p>{brick.text}</p>
                    {counts.length === 0 ? null : <pre className="lx-how-record" data-brick-counts=""><code>{counts.join('\n')}</code></pre>}
                  </div>
                </li>
              );
            })}
          </ol>
        </div>
        <aside className="lx-how-panel" aria-labelledby="how-choices">
          <section data-reference-section="choices">
            <h2 id="how-choices">{fillNext(copy.choicesHeading, { count: countWord(copy.choices.length, locale) })}</h2>
            <ul className="lx-how-decisions">
              {copy.choices.map((choice) => (
                <li key={choice.key} data-choice={choice.key}>
                  <div className="lx-how-choice">
                    <span className="lx-how-decision-title">{choice.title}</span>
                    <span className="lx-how-decision-cost">{choice.why}</span>
                  </div>
                </li>
              ))}
            </ul>
          </section>
        </aside>
      </div>
      <div className="lx-how-more">
        <section className="lx-how-block lx-how-wide" aria-labelledby="how-limits" data-reference-section="limits" id="limits">
          <h2 id="how-limits">{copy.limitsHeading}</h2>
          <div className="lx-how-table-scroll">
            <table className="lx-how-table">
              <tbody>
                {corpus === null ? null : rowsOf(corpus.codes).filter((row) => typeof row[0] === 'string' && isCount(row[3])).map((row) => (
                  <tr key={row[0]} data-limit="code">
                    <th scope="row" lang={lawLang}>{row[0]}</th>
                    <td>{countedForm(copy.limits.codeVersions, row[3], locale, { from: numericDate(row[1]), to: numericDate(row[2]) })}</td>
                  </tr>
                ))}
                {leftOut.length === 0 ? null : (
                  <tr data-limit="left-out">
                    <th scope="row">{copy.limits.leftOut}</th>
                    <td>
                      {leftOut.map((row) => fillNext(copy.limits.leftOutItem, { reason: copy.limits.leftOutReasons[row[0]], count: groupedCount(row[1], locale) })).join(dock.linkSeparator)}
                    </td>
                  </tr>
                )}
                {corpus === null || !isCount(corpus.files_not_read) ? null : (
                  <tr data-limit="files"><th scope="row">{copy.limits.filesNotRead}</th><td>{groupedCount(corpus.files_not_read, locale)}</td></tr>
                )}
                <tr data-limit="not-read"><th scope="row">{copy.limits.notRead}</th><td>{copy.limits.notReadText}</td></tr>
                <tr data-limit="references"><th scope="row">{copy.limits.references}</th><td>{copy.limits.referencesText}</td></tr>
                <tr data-limit="dictionary"><th scope="row">{copy.limits.dictionary}</th><td>{copy.limits.dictionaryText}</td></tr>
                <tr data-limit="languages"><th scope="row">{copy.limits.languages}</th><td>{copy.limits.languagesText}</td></tr>
                <tr data-limit="european"><th scope="row">{copy.limits.european}</th><td>{copy.limits.europeanText}</td></tr>
                {notHeld.length === 0 ? null : (
                  <tr data-limit="not-held"><th scope="row">{copy.limits.notHeld}</th><td>{notHeld.map((line) => line.text).join(' ')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </section>
        <section className="lx-how-block lx-how-wide" aria-labelledby="how-commitments" data-reference-section="commitments" id="commitments">
          <h2 id="how-commitments">{copy.commitmentsHeading}</h2>
          <ul className="lx-how-lines">
            {copy.commitments.map((line) => <li key={line}>{line}</li>)}
          </ul>
        </section>
      </div>
    </div>
  );
}

/** How it works, live: built in its loading state, the counts asked once the page is drawn. */
export function NextHow({ fetchImpl = undefined, locale = CHROME_LOCALE }) {
  const answer = useJson(CORPUS_ADDRESS, fetchImpl);
  return <HowView corpus={answer.data} state={answer.state} locale={locale} />;
}

// ---------------------------------------------------------------------------------------------------------------------------
// Results.

const share = (part, whole) => (isCount(part) && isCount(whole) && whole > 0 ? String(Math.round((100 * part) / whole)) : null);
const pair = (value) => (Array.isArray(value) && isCount(value[0]) && isCount(value[1]) ? value : null);

/** The rows of "by the number of searches", by the engine's key (`by_agreement[].detectors`), in the page's order. */
const AGREEMENT_ROWS = Object.freeze({ 3: 'three', 2: 'two', 1: 'one', named: 'named' });

/** The rows of "by the degree of certainty shown", by the test's key (`fresh.by_confidence[].confidence`), in the page's order. */
const CONFIDENCE_ROWS = Object.freeze(['firm', 'supported', 'single', 'named']);

const objectOr = (value) => (value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null);
/** The rows of a list that are objects naming themselves by `key` (a non-empty string). */
const namedRows = (value, key) => (Array.isArray(value) ? value.filter((row) => objectOr(row) !== null && typeof row[key] === 'string' && row[key].length > 0) : []);
/** The most questions first; rows of the same count keep the test's order. */
const mostFirst = (rows) => [...rows].sort((a, b) => (isCount(b.n) ? b.n : -1) - (isCount(a.n) ? a.n : -1));

/**
 * The test that counts: fresh questions, written from the law by someone who never saw Lex, asked once (`results.fresh`).
 * Each line is said only when every count it names is known; a style or a text the copy does not name is shown by its key.
 */
function FreshSection({ fresh, locale }) {
  const table = nextCopy(locale);
  const copy = table.results.fresh;
  const counted = (value) => (isCount(value) ? groupedCount(value, locale) : '');
  const interval = (value) => {
    const known = pair(value);
    return known === null ? '' : fillNext(copy.interval, { low: decimal(known[0], locale), high: decimal(known[1], locale) });
  };
  const answerRows = [['right', 'right', 'right_interval'], ['wrong', 'wrong', 'wrong_interval'], ['shown', 'shown_not_asserted', null], ['notFound', 'not_found', null]]
    .filter(([, field]) => isCount(fresh[field]));
  const withInterval = answerRows.some(([, , range]) => range !== null && pair(fresh[range]) !== null);
  // The wrong answers, by what was wrong in them: each kind said only when its count is known.
  const split = [['article', fresh.wrong_article], ['version', fresh.wrong_version], ['lines', fresh.wrong_lines]]
    .filter(([, count]) => isCount(count)).map(([key, count]) => fillNext(copy.split[key], { count: groupedCount(count, locale) }));
  const exceptions = objectOr(fresh.exceptions) ?? {};
  const dated = objectOr(fresh.dated) ?? {};
  const confidence = CONFIDENCE_ROWS.map((key) => namedRows(fresh.by_confidence, 'confidence').find((row) => row.confidence === key))
    .filter((row) => row !== undefined && (isCount(row.asserted) || isCount(row.not_right)));
  const styles = mostFirst(namedRows(fresh.by_style, 'style'));
  const laws = mostFirst(namedRows(fresh.by_law, 'law'));
  const label = typeof fresh.label === 'string' ? fresh.label.trim().replace(/[.\s]+$/u, '') : '';
  const note = typeof fresh.note === 'string' ? fresh.note.trim() : '';
  const lines = [
    ['no-answer', isCount(fresh.no_answer_right) && isCount(fresh.n_no_answer) && fresh.n_no_answer > 0
      ? fillNext(copy.noAnswer, { right: groupedCount(fresh.no_answer_right, locale), n: groupedCount(fresh.n_no_answer, locale) }) : null],
    ['exceptions', [exceptions.right, exceptions.n, exceptions.right_and_other_article_shown].every(isCount)
      ? fillNext(copy.exceptions, { right: groupedCount(exceptions.right, locale), n: groupedCount(exceptions.n, locale), cases: countedForm(copy.cases, exceptions.right_and_other_article_shown, locale) }) : null],
    ['dated', [dated.right, dated.n, dated.wrong_version].every(isCount)
      ? fillNext(copy.dated, { right: groupedCount(dated.right, locale), n: groupedCount(dated.n, locale), wrongVersion: groupedCount(dated.wrong_version, locale) }) : null],
  ].filter(([, line]) => line !== null);
  const time = isCount(fresh.median_ms) && isCount(fresh.p90_ms)
    ? fillNext(copy.time, { median: decimal(fresh.median_ms / 1000, locale, 1), slow: decimal(fresh.p90_ms / 1000, locale, 1) }) : null;
  const cost = isCount(fresh.usd_per_answer) ? fillNext(copy.cost, { cost: decimal(fresh.usd_per_answer, locale) }) : null;
  const assertedKnown = isCount(fresh.asserted) && isCount(fresh.right);
  // Nothing known to say: no heading standing over an empty section.
  if (label.length === 0 && note.length === 0 && answerRows.length === 0 && !assertedKnown && lines.length === 0 && confidence.length === 0
    && styles.length === 0 && laws.length === 0 && time === null && cost === null) return null;
  return (
    <section aria-labelledby="results-fresh" data-results-fresh="" data-run={typeof fresh.run === 'string' ? fresh.run : undefined}>
      <h2 id="results-fresh">{copy.heading}</h2>
      {label.length === 0 ? null : <p className="lx-muted" lang="fr" data-fresh-label="">{`${label}.`}</p>}
      {note.length === 0 ? null : <p lang="fr" data-fresh-note="">{note}</p>}
      {answerRows.length === 0 ? null : (
        <>
          <h3 id="results-fresh-answer">{copy.answerHeading}</h3>
          <div className="lx-how-table-scroll">
            <table className="lx-how-table lx-next-counts" aria-labelledby="results-fresh-answer" data-fresh-answer="">
              <thead>
                <tr>
                  <th scope="col">{copy.columns.outcome}</th>
                  <th scope="col">{isCount(fresh.n_answer) ? countedForm(copy.outOf, fresh.n_answer, locale) : copy.columns.count}</th>
                  {withInterval ? <th scope="col">{copy.columns.interval}</th> : null}
                </tr>
              </thead>
              <tbody>
                {answerRows.map(([key, field, range]) => (
                  <tr key={key} data-fresh={field}>
                    <th scope="row">
                      {copy.rows[key]}
                      {key === 'wrong' && split.length > 0 ? <span className="lx-how-decision-cost" data-fresh-split="">{split.join(table.clauseSeparator)}</span> : null}
                    </th>
                    <td>{counted(fresh[field])}</td>
                    {withInterval ? <td>{range === null ? '' : interval(fresh[range])}</td> : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
      {assertedKnown ? (
        <p data-fresh-asserted="">
          <strong>{fillNext(copy.asserted, { asserted: countedForm(copy.assertedCount, fresh.asserted, locale), right: countedForm(copy.rightCount, fresh.right, locale) })}</strong>
        </p>
      ) : null}
      {lines.map(([key, line]) => <p key={key} data-fresh-line={key}>{line}</p>)}
      {confidence.length === 0 ? null : (
        <>
          <h3 id="results-fresh-confidence">{copy.confidence.heading}</h3>
          <table className="lx-how-table lx-next-counts" aria-labelledby="results-fresh-confidence" data-fresh-confidence="">
            <thead>
              <tr><th scope="col">{copy.confidence.columns.confidence}</th><th scope="col">{copy.confidence.columns.asserted}</th><th scope="col">{copy.confidence.columns.wrong}</th></tr>
            </thead>
            <tbody>
              {confidence.map((row) => (
                <tr key={row.confidence} data-confidence={row.confidence}>
                  <th scope="row">{copy.confidence.rows[row.confidence]}</th><td>{counted(row.asserted)}</td><td>{counted(row.not_right)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      {styles.length === 0 && laws.length === 0 ? null : (
        <details className="lx-how-fold" data-fresh-detail="">
          <summary>{copy.foldSummary}</summary>
          {styles.length === 0 ? null : (
            <div className="lx-how-table-scroll">
              <table className="lx-how-table lx-next-counts" data-fresh-styles="">
                <thead>
                  <tr>{['style', 'n', 'right', 'wrong'].map((key) => <th scope="col" key={key}>{copy.styleColumns[key]}</th>)}</tr>
                </thead>
                <tbody>
                  {styles.map((row) => (
                    <tr key={row.style} data-style={row.style}>
                      <th scope="row">{Object.hasOwn(copy.styles, row.style) ? copy.styles[row.style] : row.style}</th>
                      <td>{counted(row.n)}</td><td>{counted(row.right)}</td><td>{counted(row.wrong)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {laws.length === 0 ? null : (
            <div className="lx-how-table-scroll">
              <table className="lx-how-table lx-next-counts" data-fresh-laws="">
                <thead>
                  <tr>{['law', 'n', 'right', 'wrong'].map((key) => <th scope="col" key={key}>{copy.lawColumns[key]}</th>)}</tr>
                </thead>
                <tbody>
                  {laws.map((row) => (
                    <tr key={row.law} data-law={row.law}>
                      <th scope="row">{Object.hasOwn(table.results.laws, row.law) ? table.results.laws[row.law] : row.law}</th>
                      <td>{counted(row.n)}</td><td>{counted(row.right)}</td><td>{counted(row.wrong)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </details>
      )}
      {time === null && cost === null ? null : (
        <p className="lx-muted" data-fresh-time="">{[time, cost].filter((line) => line !== null).join(' ')}</p>
      )}
    </section>
  );
}

/** One row of the overview: a group's counts, the figure to look at first in bold. */
function ResultRow({ group, name, locale }) {
  const cell = (value) => (isCount(value) ? groupedCount(value, locale) : '');
  return (
    <tr data-group={name}>
      <th scope="row">{name}</th>
      <td>{cell(group.n)}</td>
      <td>{cell(group.read)}</td>
      <td>{cell(group.asserted)}</td>
      <td>{cell(group.asserted_with_expected)}</td>
      <td><strong>{cell(group.asserted_without_expected)}</strong></td>
      <td>{cell(group.partial)}</td>
      <td>{cell(group.candidates)}</td>
      <td>{cell(group.not_found)}</td>
    </tr>
  );
}

/**
 * pages2 (21/11/2026, K-UI-7): which build the results were measured on, against the build that answers on this site (the
 * server's configuration): `{ same, text }`, or null while either is not known. A file that names no build is said so.
 */
export function resultsBuildNote(results, config, locale) {
  const copy = nextCopy(locale).results3;
  const now = config !== null && typeof config === 'object' ? config.build : null;
  if (now === null || typeof now !== 'object' || typeof now.code !== 'string' || typeof now.data !== 'string' || results === null || typeof results !== 'object') return null;
  const pipeline = copy.pipelines[config.pipeline] ?? null;
  const answering = fillNext(pipeline === null ? copy.answeringBare : copy.answering, { code: now.code, data: now.data, ...(pipeline === null ? {} : { pipeline }) });
  const measured = results.build !== null && typeof results.build === 'object' && typeof results.build.code === 'string' && typeof results.build.data === 'string' ? results.build : null;
  if (measured !== null && measured.code === now.code && measured.data === now.data) return { same: true, text: fillNext(copy.same, { code: now.code, data: now.data }) };
  // rc3 (22/11/2026): another code tag on the same data whose server reports the fingerprint of the files measured
  // (app/engine_id.py) runs the engine measured, and is said to.
  if (sameEngine(measured, now)) return { same: true, engine: true, text: fillNext(copy.sameEngine, { measured: measured.code, code: now.code, data: now.data, engine: now.engine }) };
  const before = measured === null ? copy.unnamed : fillNext(copy.measured, { code: measured.code, data: measured.data });
  return { same: false, text: `${before} ${answering}` };
}

/** The note drawn under the results' heading. */
function BuildNote({ note }) {
  if (note === null) return null;
  return <p className={note.same ? 'lx-muted' : 'lx-muted lx-next-gap'} data-results-build={note.same ? (note.engine ? 'same-engine' : 'same') : 'other'}>{note.text}</p>;
}

/** The scored test, drawn from the results answer (`results` null while it is not known). */
export function ResultsView({ results = null, state = 'loading', locale = CHROME_LOCALE, config = null }) {
  const table = nextCopy(locale);
  const copy = table.results;
  const dataLang = locale === 'fr' ? undefined : 'fr';
  if (state !== 'ready' || results === null) {
    return (
      <div data-next-page="results" data-next-state={state}>
        <h1>{copy.heading}</h1>
        <p role="status" data-next-status="">{state === 'failed' ? copy.failed : copy.loading}</p>
      </div>
    );
  }
  const note = <BuildNote note={resultsBuildNote(results, config, locale)} />;
  // An evaluation in the schema of RESULTS-SCHEMA.md is drawn by its own view; an earlier file as it was.
  if (isResults2(results)) return <Results2View results={results} locale={locale} note={note} served={config?.build ?? null} />;
  const bench = objectOr(results.all);
  const fresh = objectOr(results.fresh);
  const independent = objectOr(results.independent);
  if (results.none === true || (bench === null && fresh === null && independent === null)) {
    return (
      <div data-next-page="results" data-next-state="none">
        <h1>{copy.heading}</h1>
        <p>{copy.none}</p>
      </div>
    );
  }
  // The bench may be missing while the fresh test is there: its part of the page is then left out, never drawn empty.
  const all = bench ?? {};
  const without = isCount(all.asserted_without_expected) ? all.asserted_without_expected : 0;
  const withoutInterval = pair(all.asserted_without_expected_interval);
  const readInterval = pair(all.read_interval);
  const assertedInterval = pair(all.asserted_interval);
  const summaryValues = {
    readShare: share(all.read, all.n), readLow: readInterval && decimal(readInterval[0], locale), readHigh: readInterval && decimal(readInterval[1], locale),
    assertedShare: share(all.asserted, all.n), assertedLow: assertedInterval && decimal(assertedInterval[0], locale), assertedHigh: assertedInterval && decimal(assertedInterval[1], locale),
    median: isCount(all.median_ms) ? decimal(all.median_ms / 1000, locale, 1) : null, slow: isCount(all.p90_ms) ? decimal(all.p90_ms / 1000, locale, 1) : null,
    cost: isCount(all.usd_per_answer) ? decimal(all.usd_per_answer, locale) : null,
  };
  const summaryKnown = Object.values(summaryValues).every((value) => typeof value === 'string');
  const groups = Array.isArray(results.groups) ? results.groups.filter((group) => group !== null && typeof group === 'object') : [];
  const lost = Object.entries(results.lost_by_step ?? {}).filter(([, count]) => isCount(count)).sort((a, b) => b[1] - a[1]);
  const compared = results.compared_with !== null && typeof results.compared_with === 'object' ? results.compared_with : null;
  const flips = Array.isArray(compared?.flips) ? compared.flips : [];
  const doubtful = Array.isArray(results.doubtful) ? results.doubtful : [];
  const stateName = (value) => copy.states[value] ?? value;
  const columns = copy.columns;
  const live = independent !== null && independent.live !== null && typeof independent.live === 'object' ? independent.live : {};
  const byLaw = Array.isArray(independent?.by_law) ? independent.by_law.filter((row) => row !== null && typeof row === 'object' && typeof row.law === 'string') : [];
  const switchedOff = Array.isArray(results.switched_off) ? results.switched_off.filter((row) => row !== null && typeof row === 'object' && Object.hasOwn(copy.bricks.rows, row.what) && row.what !== 'all') : [];
  const counted = (value) => (isCount(value) ? groupedCount(value, locale) : '');
  // In the order of the page: all three searches, two, one, then the articles the person named.
  const agreement = ['3', '2', '1', 'named']
    .map((key) => (Array.isArray(results.by_agreement) ? results.by_agreement : []).find((row) => row !== null && typeof row === 'object' && String(row.detectors) === key))
    .filter((row) => row !== undefined);
  return (
    <div data-next-page="results" data-next-state="ready" data-run={results.run ?? ''}>
      <h1>{copy.heading}</h1>
      {note}
      {fresh === null ? null : <FreshSection fresh={fresh} locale={locale} />}
      {independent === null ? null : (
        <section aria-labelledby="results-independent" data-results-independent="">
          <h2 id="results-independent">{copy.independent.heading}</h2>
          {typeof independent.label === 'string' || isCount(independent.n) ? (
            <p className="lx-muted">
              {typeof independent.label === 'string' ? <span lang={dataLang}>{independent.label.replace(/[.\s]+$/u, '')}</span> : null}
              {typeof independent.label === 'string' && isCount(independent.n) ? table.listSeparator : null}
              {isCount(independent.n) ? countedForm(copy.independent.questions, independent.n, locale) : null}
              {'.'}
            </p>
          ) : null}
          <table className="lx-how-table lx-next-counts" data-independent-outcomes="">
            <thead><tr><th scope="col">{copy.independent.columns.outcome}</th><th scope="col">{copy.independent.columns.live}</th><th scope="col">{copy.independent.columns.next}</th></tr></thead>
            <tbody>
              {[['right', 'right'], ['shown', 'shown_not_asserted'], ['wrong', 'wrong'], ['notFound', 'not_found']].map(([key, field]) => (
                <tr key={key} data-independent={field}><th scope="row">{copy.independent.rows[key]}</th><td>{counted(live[field])}</td><td>{counted(independent[field])}</td></tr>
              ))}
            </tbody>
          </table>
          {byLaw.length === 0 ? null : (
            <table className="lx-how-table lx-next-counts" data-independent-laws="">
              <thead><tr><th scope="col">{copy.independent.lawColumns.law}</th><th scope="col">{copy.independent.lawColumns.n}</th><th scope="col">{copy.independent.lawColumns.right}</th></tr></thead>
              <tbody>
                {byLaw.map((row) => <tr key={row.law} data-law={row.law}><th scope="row">{copy.laws[row.law] ?? row.law}</th><td>{counted(row.n)}</td><td>{counted(row.right)}</td></tr>)}
              </tbody>
            </table>
          )}
          {typeof independent.note === 'string' && independent.note.length > 0 ? <p lang={dataLang} data-independent-note="">{independent.note}</p> : null}
        </section>
      )}
      {bench === null ? null : (
        <>
          {fresh === null && independent === null ? null : <h2>{copy.benchHeading}</h2>}
          <p className="lx-muted" data-results-intro="">
            {/* repair-pages: the file's label is the evaluation's own words (French): marked as such on the English page. */}
            {fillNext(copy.intro, { label: '\u0000', date: numericDate(results.made) }).split('\u0000')[0]}
            <span lang={typeof results.label === 'string' ? dataLang : undefined}>{results.label ?? results.run ?? ''}</span>
            {fillNext(copy.intro, { label: '\u0000', date: numericDate(results.made) }).split('\u0000')[1]}
            {typeof results.models === 'string' ? <> <span lang={dataLang}>{results.models}</span></> : null}
          </p>
          {results.kind === 'bench' ? <p data-results-bench=""><strong>{copy.benchLead}</strong> {copy.benchText}</p> : null}
          <h2>{copy.firstHeading}</h2>
          <p data-results-first=""><strong>{countedForm(copy.firstLine, without, locale, { n: groupedCount(all.n, locale) })}</strong></p>
          {withoutInterval === null ? null : (
            <p className="lx-muted">{fillNext(copy.firstNote, { low: decimal(withoutInterval[0], locale), high: decimal(withoutInterval[1], locale) })}</p>
          )}
          <h2>{copy.overviewHeading}</h2>
          <div className="lx-how-table-scroll">
            <table className="lx-how-table" data-results-overview="">
              <thead>
                <tr>
                  {['questions', 'n', 'read', 'asserted', 'withExpected', 'without', 'partial', 'candidates', 'notFound'].map((key) => <th scope="col" key={key}>{columns[key]}</th>)}
                </tr>
              </thead>
              <tbody>
                <ResultRow group={all} name={copy.all} locale={locale} />
                {groups.map((group, at) => <ResultRow group={group} name={copy.groups[group.name] ?? group.name ?? String(at + 1)} locale={locale} key={group.name ?? at} />)}
              </tbody>
            </table>
          </div>
          {summaryKnown ? <p className="lx-muted" data-results-summary="">{fillNext(copy.summary, summaryValues)}</p> : null}
          {agreement.length === 0 ? null : (
            <>
              <h2>{copy.agreement.heading}</h2>
              <table className="lx-how-table lx-next-counts" data-results-agreement="">
                <thead>
                  <tr><th scope="col">{copy.agreement.columns.detectors}</th><th scope="col">{copy.agreement.columns.asserted}</th><th scope="col">{copy.agreement.columns.without}</th></tr>
                </thead>
                <tbody>
                  {agreement.map((row) => (
                    <tr key={row.detectors} data-agreement={row.detectors}>
                      <th scope="row">{copy.agreement.rows[AGREEMENT_ROWS[row.detectors]]}</th><td>{counted(row.asserted)}</td><td>{counted(row.without_expected)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
          {switchedOff.length === 0 ? null : (
            <>
              <h2>{copy.bricks.heading}</h2>
              <div className="lx-how-table-scroll">
                <table className="lx-how-table lx-next-counts" data-results-bricks="">
                  <thead>
                    <tr>
                      <th scope="col">{copy.bricks.columns.what}</th><th scope="col">{copy.bricks.columns.asserted}</th>
                      <th scope="col">{copy.bricks.columns.without}</th><th scope="col">{copy.bricks.columns.withException}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {[{ ...all, what: 'all' }, ...switchedOff].map((row) => (
                      <tr key={row.what} data-brick={row.what}>
                        <th scope="row">{copy.bricks.rows[row.what]}</th>
                        <td>{counted(row.asserted)}</td><td>{counted(row.asserted_without_expected)}</td><td>{counted(row.with_exception_block)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
          {lost.length === 0 ? null : (
            <>
              <h2>{copy.lostHeading}</h2>
              <table className="lx-how-table lx-next-counts" data-results-lost="">
                <thead><tr><th scope="col">{copy.lostColumns.step}</th><th scope="col">{copy.lostColumns.count}</th></tr></thead>
                <tbody>
                  {lost.map(([step, count]) => <tr key={step} data-lost={step}><th scope="row">{copy.lost[step] ?? step}</th><td>{groupedCount(count, locale)}</td></tr>)}
                </tbody>
              </table>
            </>
          )}
          {compared === null ? null : (
            <>
              <h2>{fillNext(copy.comparedHeading, { run: compared.run ?? '' })}</h2>
              {[compared.changed, compared.better, compared.worse, compared.all?.asserted_without_expected].every(isCount) ? (
                <p data-results-compared="">
                  {fillNext(copy.comparedText, {
                    changed: countedForm(copy.changed, compared.changed, locale),
                    better: groupedCount(compared.better, locale),
                    worse: groupedCount(compared.worse, locale),
                    before: groupedCount(compared.all.asserted_without_expected, locale),
                    now: groupedCount(without, locale),
                  })}
                </p>
              ) : null}
              {flips.length === 0 ? null : (
                <details className="lx-how-fold">
                  <summary>{copy.flipsSummary}</summary>
                  <div className="lx-how-table-scroll">
                    <table className="lx-how-table">
                      <thead><tr><th scope="col">{copy.flipColumns.question}</th><th scope="col">{copy.flipColumns.before}</th><th scope="col">{copy.flipColumns.after}</th></tr></thead>
                      <tbody>
                        {flips.map((flip, at) => (
                          <tr key={flip.id ?? at}><th scope="row" lang={dataLang}>{flip.question}</th><td>{stateName(flip.before)}</td><td>{stateName(flip.after)}</td></tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </details>
              )}
            </>
          )}
          <details className="lx-how-fold" data-results-doubtful="">
            <summary>{countedForm(copy.doubtfulSummary, doubtful.length, locale)}</summary>
            {doubtful.length === 0 ? null : (
              <div className="lx-how-table-scroll">
                <table className="lx-how-table">
                  <thead><tr><th scope="col">{copy.doubtfulColumns.question}</th><th scope="col">{copy.doubtfulColumns.expected}</th><th scope="col">{copy.doubtfulColumns.gave}</th></tr></thead>
                  <tbody>
                    {doubtful.map((entry, at) => (
                      <tr key={entry.id ?? at}>
                        <th scope="row" lang={dataLang}>{entry.question}</th>
                        <td><code>{(Array.isArray(entry.expected) ? entry.expected : []).join(table.listSeparator)}</code></td>
                        <td><code>{(Array.isArray(entry.gave) ? entry.gave : []).join(table.listSeparator)}</code></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </details>
        </>
      )}
    </div>
  );
}

/** Results, live: built in its loading state, the test asked once the page is drawn. */
export function NextResults({ fetchImpl = undefined, locale = CHROME_LOCALE }) {
  const answer = useJson(RESULTS_ADDRESS, fetchImpl);
  // The build that answers on this site, to say whether the results were measured on it (pages2).
  const config = useJson(CONFIG_ADDRESS, fetchImpl);
  return <ResultsView results={answer.data} state={answer.state} locale={locale} config={config.data} />;
}

// ---------------------------------------------------------------------------------------------------------------------------
// "What the assistant reads", at the top of Trust and Coverage.

/** One table of counts under its heading: each row a label and its count; a row whose count is not known is left out. */
function Counts({ id, heading, rows, columns, locale }) {
  const summed = new Map();
  for (const [label, count] of rows) if (isCount(count)) summed.set(label, (summed.get(label) ?? 0) + count);
  const known = [...summed];
  if (known.length === 0) return null;
  return (
    <>
      <h3 id={id}>{heading}</h3>
      <table className="lx-how-table lx-next-counts" aria-labelledby={id} data-corpus-table={id}>
        <thead><tr><th scope="col">{columns.what}</th><th scope="col">{columns.count}</th></tr></thead>
        <tbody>
          {known.map(([label, count]) => <tr key={label}><th scope="row">{label}</th><td>{groupedCount(count, locale)}</td></tr>)}
        </tbody>
      </table>
    </>
  );
}

/** The section, drawn from the corpus counts: nothing at all when they could not be read. */
export function CorpusView({ corpus = null, state = 'loading', locale = CHROME_LOCALE }) {
  const copy = nextCopy(locale).corpus;
  if (state === 'failed') return null;
  if (state !== 'ready' || corpus === null) {
    return (
      <section aria-labelledby="next-corpus-heading" data-next-section="corpus" data-next-state="loading">
        <h2 id="next-corpus-heading">{copy.heading}</h2>
        <p className="lx-muted" role="status" data-next-status="">{copy.loading}</p>
      </section>
    );
  }
  const certainty = (value) => (value === 'sure' ? copy.sure : copy.unsure);
  const notHeld = coverageLines(corpus.coverage_gaps, locale);
  return (
    <section aria-labelledby="next-corpus-heading" data-next-section="corpus" data-next-state="ready">
      <h2 id="next-corpus-heading">{copy.heading}</h2>
      <p className="lx-muted">{copy.intro}</p>
      {notHeld.length === 0 ? null : (
        <>
          <h3 id="next-corpus-not-held">{copy.notHeldHeading}</h3>
          <p className="lx-muted">{copy.notHeldIntro}</p>
          {notHeld.map((line) => <p key={line.key} data-corpus-not-held={line.key}>{line.text}</p>)}
        </>
      )}
      <Counts
        id="next-corpus-texts" heading={copy.textsHeading} columns={copy.columns} locale={locale}
        rows={[
          [copy.texts.acts, corpus.acts], [copy.texts.versions, corpus.versions], [copy.texts.several, corpus.acts_with_several_versions],
          [copy.texts.articles, corpus.articles], [copy.texts.lines, corpus.lines], [copy.texts.nodes, corpus.toc_nodes], [copy.texts.filesNotRead, corpus.files_not_read],
        ]}
      />
      <Counts
        id="next-corpus-read" heading={copy.readHeading} columns={copy.columns} locale={locale}
        rows={[
          [copy.read.current, corpus.current_articles], [copy.read.searchable, corpus.articles_searchable], [copy.read.keywordIndex, corpus.keyword_index?.articles],
          [copy.read.deleted, corpus.deleted_by_publisher?.articles_current],
          // Each reason in words; one the copy does not name is not shown as the engine's key. The articles the publisher
          // deleted have their own row just above: the reason that names them is not said twice.
          ...rowsOf(corpus.articles_left_out).filter((row) => Object.hasOwn(copy.leftOutReasons, row[0]))
            .map((row) => [fillNext(copy.read.leftOut, { reason: copy.leftOutReasons[row[0]] }), row[1]]),
        ]}
      />
      <Counts
        id="next-corpus-units" heading={copy.unitsHeading} columns={copy.columns} locale={locale}
        rows={rowsOf(corpus.units_by_kind_current).map((row) => [Object.hasOwn(copy.unitKinds, row[0]) ? copy.unitKinds[row[0]] : copy.unitKinds.other, row[1]])}
      />
      <Counts
        id="next-corpus-edges" heading={copy.edgesHeading} columns={copy.columns} locale={locale}
        rows={[
          [copy.edges, corpus.edges],
          ...rowsOf(corpus.edges_by_certainty).map((row) => [fillNext(copy.edgeRow, { scope: copy.edgeScopes[row[0]] ?? copy.edgeScopes.other, certainty: certainty(row[1]) }), row[2]]),
        ]}
      />
      <Counts
        id="next-corpus-edge-types" heading={copy.edgeTypesHeading} columns={copy.columns} locale={locale}
        rows={rowsOf(corpus.edges_by_type).map((row) => [copy.edgeTypes[row[0]] ?? row[0], row[1]])}
      />
      <Counts
        id="next-corpus-act-types" heading={copy.actTypesHeading} columns={copy.columns} locale={locale}
        rows={rowsOf(corpus.acts_by_type).map((row) => [copy.actTypes[row[0]] ?? row[0], row[1]])}
      />
    </section>
  );
}

/** The section, live: built in its loading state, the counts asked once the page is drawn. */
export function NextCorpus({ fetchImpl = undefined, locale = CHROME_LOCALE }) {
  const answer = useJson(CORPUS_ADDRESS, fetchImpl);
  return <CorpusView corpus={answer.data} state={answer.state} locale={locale} />;
}
