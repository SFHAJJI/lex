// The Results page for an evaluation in the schema `lex-results/2` (`web/RESULTS-SCHEMA.md`), in the earlier pages' look
// (`.lx-how-table`, `.lx-how-fold`, `.lx-prose`): whether the numbers describe the build answering on this site (said first,
// in words, before any number), the wrong answers asserted, the questions and how they were scored, every question by
// result, the search measured apart, the slices with their counts and margins, by text and by kind (folded), the misses
// labelled, the questions whose answer Lex does not hold, each brick switched off, a naive pipeline on the same questions,
// the previous Lex on the same questions, the same questions asked again, cost and time. Every figure is the file's; a
// section the file does not carry is left out; a key the copy does not name is shown as the file writes it.

import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { countedForm, decimal, fillNext, groupedCount, nextCopy, numericDate } from '../scripts/next-copy.mjs';

/** The schema this view draws. */
export const RESULTS_SCHEMA = 'lex-results/2';

const isCount = (value) => typeof value === 'number' && Number.isFinite(value);
const objectOr = (value) => (value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null);
const text = (value) => (typeof value === 'string' && value.trim().length > 0 ? value.trim() : null);
const pairOf = (value) => (Array.isArray(value) && isCount(value[0]) && isCount(value[1]) ? value : null);
const rowsOf = (value, key) => (Array.isArray(value) ? value.filter((row) => objectOr(row) !== null && text(row[key]) !== null) : []);

/** Whether a results answer is in this schema. */
export const isResults2 = (results) => objectOr(results) !== null && results.schema === RESULTS_SCHEMA;

/**
 * repair-pages (22/11/2026): a sentence the evaluation wrote, in the page's language when the file gives it in both
 * (`{ fr, en }`), else as written, marked with the language it is in (French or English, told by its words) so an English
 * page never shows French unmarked: `{ text, lang }`, `lang` undefined when it is the page's own. Null for nothing.
 */
export function prose(value, locale) {
  const own = objectOr(value);
  if (own !== null) {
    if (text(own[locale]) !== null) return { text: text(own[locale]), lang: undefined };
    const other = ['fr', 'en'].find((key) => text(own[key]) !== null);
    return other === undefined ? null : { text: text(own[other]), lang: other === locale ? undefined : other };
  }
  const said = text(value);
  if (said === null) return null;
  const french = /[àâçéèêëîïôûùœ’]|\b(?:les|des|une|est|du|aux|qui|avec|pour|sans|sur|dans|ont|été|écrites?)\b/iu.test(said);
  const lang = french ? 'fr' : 'en';
  return { text: said, lang: lang === locale ? undefined : lang };
}

/** A build `{ code, data }` the file names, or null. */
const buildOf = (value) => (text(objectOr(value)?.code) !== null && text(objectOr(value)?.data) !== null ? { code: value.code.trim(), data: value.data.trim() } : null);
const sameBuild = (a, b) => a !== null && b !== null && a.code === b.code && a.data === b.data;
/** rc3: a brick's row key, with the run's name when the file gives one (two full runs are two rows "all"). */
export const ablationKey = (row) => (text(row?.run) === null ? row.what : `${row.what}-${row.run.trim()}`);
const fingerprintOf = (value) => (typeof objectOr(value)?.engine === 'string' && /^[0-9a-f]{16}$/u.test(value.engine) ? value.engine : null);

/**
 * rc3 (22/11/2026): whether two builds `{ code, data, engine }` run the same engine on the same data: the same data tag and the
 * same fingerprint of the files that compute an answer (`app/engine_id.py`; the server reports its own in `/api/next/config`).
 */
export const sameEngine = (a, b) => {
  const one = objectOr(a);
  const two = objectOr(b);
  return one !== null && two !== null && text(one.data) !== null && one.data === two.data && fingerprintOf(one) !== null && fingerprintOf(one) === fingerprintOf(two);
};

/** A table of rows under its column names; each cell a string. */
function Table({ columns, rows, mark, className = 'lx-how-table lx-next-counts', stack = false }) {
  return (
    <div className="lx-how-table-scroll">
      <table className={className} data-results2={mark}>
        <thead><tr>{columns.map((column) => <th scope="col" key={column}>{column}</th>)}</tr></thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} data-row={row.key} className={row.className}>
              <th scope="row" lang={row.lang}>{row.head}</th>
              {row.cells.map((cell, at) => <td key={at} lang={row.cellLang?.[at]} data-label={stack ? columns[at + 1] : undefined}>{cell}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** A sentence of the copy with one place filled by a marked piece (`{ text, lang }`). */
function Filled({ template, place, piece, values = {} }) {
  const [before, after] = fillNext(template, { ...values, [place]: '\u0000' }).split('\u0000');
  return <>{before}<span lang={piece.lang}>{piece.text.replace(/[.\s]+$/u, '')}</span>{after}</>;
}

/**
 * fix-pages3 (22/11/2026): the other runs the file carries (`runs`, RESULTS-SCHEMA.md), each a run in the same shape as the
 * file's own, with a unique `key`; an entry without a key or a set is left out.
 */
export function moreRuns(results) {
  const seen = new Set();
  return (Array.isArray(results?.runs) ? results.runs : []).filter((run) => {
    const key = text(objectOr(run)?.key);
    if (key === null || key === 'main' || !/^[a-z0-9-]+$/u.test(key) || seen.has(key) || objectOr(run.set) === null) return false;
    seen.add(key);
    return true;
  });
}

/** Whether a run's build is the one answering on the site (`served`, `/api/next/config` `build`): true, false, or null unknown. */
export function describesSite(run, served) {
  const own = buildOf(run?.build);
  const now = buildOf(served);
  if (own === null || now === null) return null;
  return sameBuild(own, now) || sameEngine(run.build, served);
}

/** A run is drawn whole only when final; a run the file marks otherwise is named, with no figure. */
const isFinal = (run) => run?.status === undefined || run?.status === 'final';

/**
 * The runs in the order they are drawn: the one that describes the site's answers first, then the others by the day they were
 * scored, newest first (the file's own run before its `runs` on the same day).
 */
export function orderedRuns(results, served) {
  const all = [{ run: results, key: 'main', at: 0 }, ...moreRuns(results).map((run, at) => ({ run, key: run.key.trim(), at: at + 1 }))];
  const rank = (entry) => (describesSite(entry.run, served) === true ? 0 : 1);
  return all.sort((a, b) => rank(a) - rank(b) || String(b.run.made ?? '').localeCompare(String(a.run.made ?? '')) || a.at - b.at);
}

/**
 * The results in `lex-results/2`, drawn whole. `served` is the build answering on the site (`/api/next/config`), or null. One
 * run: as before. Several runs (`runs`) or the walk's value (`walk_value`): a table of the runs first, the walk's value, then
 * each run under its own heading, with `noteFor(run)` (whether it describes the site's answers) before any of its numbers.
 */
export function Results2View({ results, locale = CHROME_LOCALE, note = null, served = null, noteFor = null }) {
  const table = nextCopy(locale);
  const runs = moreRuns(results);
  const walkValue = objectOr(results.walk_value);
  if (runs.length === 0 && walkValue === null) {
    return (
      <div data-next-page="results" data-next-state="ready" data-results-schema={RESULTS_SCHEMA} data-run={results.run ?? ''}>
        <h1>{table.results.heading}</h1>
        {note}
        <RunBody results={results} locale={locale} served={served} />
      </div>
    );
  }
  const ordered = orderedRuns(results, served);
  return (
    <div data-next-page="results" data-next-state="ready" data-results-schema={RESULTS_SCHEMA} data-run={results.run ?? ''} data-results-runs={String(ordered.length)}>
      <h1>{table.results.heading}</h1>
      <RunsTable entries={ordered} locale={locale} served={served} />
      <WalkValue value={walkValue} locale={locale} served={served} />
      {ordered.map(({ run, key }) => {
        const title = runTitle(run, locale);
        return (
          <section className="lx-next-run" key={key} id={`run-${key === 'main' ? text(run.run) ?? 'main' : key}`} data-results-run={key} aria-labelledby={`run-${key}-heading`}>
            <h2 id={`run-${key}-heading`} lang={title.lang}>{title.text}</h2>
            {noteFor === null ? null : noteFor(run)}
            {isFinal(run) ? <RunBody results={run} locale={locale} served={served} level="h3" idPrefix={key === 'main' ? '' : `${key}-`} />
              : <p className="lx-muted lx-next-gap" data-results-pending="">{runPendingLine(run, locale)}</p>}
          </section>
        );
      })}
    </div>
  );
}

/** A run's heading: its own title, else its set and its size. */
function runTitle(run, locale) {
  const copy = nextCopy(locale);
  const own = prose(run?.title, locale);
  if (own !== null) return own;
  const set = objectOr(run?.set);
  const name = text(set?.name) ?? text(run?.run) ?? '';
  return { text: isCount(set?.n) ? fillNext(copy.results4.runTitle, { name, n: countedForm(copy.results2.questions, set.n, locale) }) : name, lang: undefined };
}

/** A run not scored yet: that no figure is shown, its build, the build it is compared with. */
function runPendingLine(run, locale) {
  const copy = nextCopy(locale).results4;
  const build = buildOf(run?.build);
  const previous = buildOf(run?.previous?.build);
  const parts = [copy.pending];
  if (build !== null) parts.push(fillNext(copy.pendingBuild, build));
  if (previous !== null) parts.push(fillNext(copy.pendingAgainst, previous));
  return parts.join(' ');
}

/** The table of the runs: the set, the version measured, right and wrong asserted with their margins, the decision, the site. */
function RunsTable({ entries, locale, served }) {
  const table = nextCopy(locale);
  const copy = table.results4;
  const count = (value) => (isCount(value) ? groupedCount(value, locale) : '');
  const interval = (value) => {
    const pair = pairOf(value);
    return pair === null ? '' : fillNext(table.results2.interval, { low: decimal(pair[0], locale), high: decimal(pair[1], locale) });
  };
  return (
    <section aria-labelledby="results-runs" data-results-runs-table="">
      <h2 id="results-runs">{copy.runsHeading}</h2>
      <Table
        mark="runs"
        className="lx-how-table lx-next-runs lx-next-stack"
        stack
        columns={['run', 'build', 'right', 'wrong', 'decision', 'site'].map((key) => copy.runsColumns[key])}
        rows={entries.map(({ run, key }) => {
          const title = runTitle(run, locale);
          const build = buildOf(run.build);
          const totals = objectOr(run.totals);
          const n = isCount(objectOr(run.set)?.n) ? run.set.n : null;
          const final = isFinal(run);
          const site = describesSite(run, served);
          const result = text(objectOr(run.decision)?.result);
          const against = buildOf(run.previous?.build);
          const said = (value, pair) => (isCount(value) && n !== null ? fillNext(copy.runsCount, { count: count(value), n: count(n), interval: interval(pair) || '—' }) : count(value));
          return {
            key,
            head: title.text,
            lang: title.lang,
            cells: [
              [build === null ? null : fillNext(copy.runsBuild, build), against === null ? null : fillNext(copy.runsAgainst, against)].filter((one) => one !== null).join(' '),
              final ? said(totals?.right, totals?.intervals?.right) : copy.pendingShort,
              final ? said(totals?.wrong, totals?.intervals?.wrong) : '',
              !final || result === null ? '' : copy.decisions[result] ?? result,
              site === null ? '—' : site ? copy.siteYes : copy.siteNo,
            ],
          };
        })}
      />
    </section>
  );
}

/** fix-pages3: what the walk down the tables of contents adds (`walk_value`, written by walk-analysis), when the file says it. */
function WalkValue({ value, locale, served }) {
  if (value === null) return null;
  const table = nextCopy(locale);
  const copy = table.results4.walk;
  const count = (one) => (isCount(one) ? groupedCount(one, locale) : '');
  const interval = (one) => {
    const pair = pairOf(one);
    return pair === null ? '' : fillNext(table.results2.interval, { low: decimal(pair[0], locale), high: decimal(pair[1], locale) });
  };
  const pValue = (one) => (isCount(one) ? (one < 0.001 ? `< ${decimal(0.001, locale)}` : decimal(Number(one.toPrecision(2)), locale)) : '—');
  const search = (key) => (Object.hasOwn(copy.searches, key) ? copy.searches[key] : key);
  const found = (row) => (isCount(row.found) && isCount(row.of) ? fillNext(copy.outOf, { count: count(row.found), of: count(row.of) }) : count(row.found));
  const build = buildOf(value.build);
  const site = describesSite(value, served);
  const method = prose(value.method, locale);
  const verdict = prose(value.verdict, locale);
  const findings = (Array.isArray(value.findings) ? value.findings : []).map((one) => prose(one, locale)).filter((one) => one !== null);
  const recall = rowsOf(value.recall, 'search');
  const only = rowsOf(value.only, 'search');
  const ablation = objectOr(value.ablation);
  const withRun = objectOr(ablation?.with);
  const withoutRun = objectOr(ablation?.without);
  const discordant = pairOf(ablation?.discordant);
  const slices = rowsOf(ablation?.slices, 'slice');
  const cost = objectOr(value.cost);
  const costKeys = ['calls_per_answer', 'share_of_calls', 'ms_per_answer', 'share_of_time', 'usd_per_answer', 'share_of_cost'];
  return (
    <section aria-labelledby="results-walk" data-results-walk="">
      <h2 id="results-walk">{copy.heading}</h2>
      {verdict === null ? null : <p lang={verdict.lang} data-results-walk-verdict=""><strong>{verdict.text}</strong></p>}
      {build === null || text(value.run) === null ? null : (
        <p className={site === false ? 'lx-muted lx-next-gap' : 'lx-muted'} data-results-walk-build="">
          {fillNext(copy.build, { run: value.run, code: build.code, data: build.data })}{site === false ? <> {table.results2.notServed}</> : null}
        </p>
      )}
      {method === null ? null : <p className="lx-muted" lang={method.lang}>{method.text}</p>}
      {findings.length === 0 ? null : <ul className="lx-next-findings" data-results-walk-findings="">{findings.map((one) => <li key={one.text} lang={one.lang}>{one.text}</li>)}</ul>}
      {recall.length === 0 ? null : (
        <>
          <h3>{copy.recallHeading}</h3>
          <Table mark="walk-recall" columns={['search', 'found', 'interval'].map((key) => copy.recallColumns[key])}
            rows={recall.map((row) => ({ key: row.search, head: search(row.search), cells: [found(row), interval(row.interval)] }))} />
        </>
      )}
      {only.length === 0 ? null : (
        <>
          <h3>{copy.onlyHeading}</h3>
          <Table mark="walk-only" columns={['search', 'found', 'right'].map((key) => copy.onlyColumns[key])}
            rows={only.map((row) => ({ key: row.search, head: search(row.search), cells: [found(row), count(row.right)] }))} />
        </>
      )}
      {ablation === null || !isCount(withRun?.right) || !isCount(withoutRun?.right) || !isCount(ablation.n) ? null : (
        <>
          <h3>{copy.ablationHeading}</h3>
          <p data-results-walk-ablation="">
            {fillNext(copy.ablation, { with: count(withRun.right), without: count(withoutRun.right), n: count(ablation.n), a: discordant === null ? '—' : count(discordant[0]), b: discordant === null ? '—' : count(discordant[1]), p: pValue(ablation.p) })}
          </p>
          {slices.length === 0 ? null : (
            <details className="lx-how-fold">
              <summary>{copy.slicesSummary}</summary>
              <Table mark="walk-slices" columns={['slice', 'n', 'with', 'without', 'discordant', 'p'].map((key) => copy.sliceColumns[key])}
                rows={slices.map((row) => {
                  const pair = pairOf(row.discordant);
                  return { key: row.slice, head: Object.hasOwn(table.results2.slices, row.slice) ? table.results2.slices[row.slice] : row.slice, cells: [count(row.n), count(row.with), count(row.without), pair === null ? '' : fillNext(copy.pair, { a: count(pair[0]), b: count(pair[1]) }), pValue(row.p)] };
                })} />
            </details>
          )}
        </>
      )}
      {cost === null || costKeys.some((key) => !isCount(cost[key])) ? null : (
        <p data-results-walk-cost="">
          {fillNext(copy.cost, { calls: decimal(cost.calls_per_answer, locale, 1), callsShare: decimal(cost.share_of_calls, locale, 1), seconds: decimal(cost.ms_per_answer / 1000, locale, 1), timeShare: decimal(cost.share_of_time, locale, 1), usd: decimal(cost.usd_per_answer, locale), costShare: decimal(cost.share_of_cost, locale, 1) })}
        </p>
      )}
    </section>
  );
}

/** One run's sections, the body of the page for one run: `level` the tag of its section headings, `idPrefix` its ids' prefix. */
function RunBody({ results, locale = CHROME_LOCALE, served = null, level = 'h2', idPrefix = '' }) {
  const H = level;
  const table = nextCopy(locale);
  const copy = table.results2;
  const pipelines = table.results3.pipelines;
  const dataLang = locale === 'fr' ? undefined : 'fr';
  const count = (value) => (isCount(value) ? groupedCount(value, locale) : '');
  const interval = (value) => {
    const pair = pairOf(value);
    return pair === null ? '' : fillNext(copy.interval, { low: decimal(pair[0], locale), high: decimal(pair[1], locale) });
  };
  const set = objectOr(results.set);
  const scoring = objectOr(results.scoring);
  const build = buildOf(results.build);
  const servedBuild = buildOf(served);
  const totals = objectOr(results.totals);
  const n = isCount(set?.n) ? set.n : null;
  const wrong = rowsOf(results.wrong, 'question');
  const wrongCount = isCount(totals?.wrong) ? totals.wrong : wrong.length;
  const wrongInterval = pairOf(totals?.intervals?.wrong);
  const retrieval = objectOr(results.retrieval);
  const slices = rowsOf(results.slices, 'slice');
  const byLaw = rowsOf(results.by_law, 'law');
  const byType = rowsOf(results.by_type, 'type');
  const misses = objectOr(results.misses);
  const missItems = rowsOf(misses?.items, 'question');
  const absent = objectOr(results.absent);
  const ablation = objectOr(results.ablation);
  const ablationRows = rowsOf(ablation?.rows, 'what');
  const baseline = objectOr(results.baseline);
  const previous = objectOr(results.previous);
  const previousItems = rowsOf(previous?.items, 'question');
  const noise = objectOr(results.noise);
  const cost = objectOr(results.cost);
  const latency = objectOr(results.latency);
  const name = (map, key) => (Object.hasOwn(map, key) ? map[key] : key);
  const lawName = (key) => name(table.results.laws, key);
  const typeName = (key) => name(table.questionTypes, key);
  const stateName = (key) => name(copy.itemStates, key);
  const usd = (value) => (isCount(value) ? decimal(value, locale) : null);
  const seconds = (ms) => (isCount(ms) ? decimal(ms / 1000, locale, 1) : null);
  const measuredPipeline = text(results.pipeline) ?? null;
  const runLine = text(results.run) !== null && text(results.made) !== null && build !== null
    ? fillNext(measuredPipeline !== null && Object.hasOwn(pipelines, measuredPipeline) ? copy.runPipeline : copy.run, {
      run: results.run, date: numericDate(results.made), code: build.code, data: build.data,
      ...(measuredPipeline !== null && Object.hasOwn(pipelines, measuredPipeline) ? { pipeline: pipelines[measuredPipeline] } : {}),
    }) : null;
  // rc3 (22/11/2026): a build the file names answers on this site when it is the one served, or when it is the build measured
  // and the server runs the engine measured on the same data (`sameEngine`).
  const notAnswering = (own) => servedBuild !== null && own !== null && !sameBuild(own, servedBuild) && !(sameBuild(own, build) && sameEngine(results.build, served));
  // The build a section names, said under its heading, and said not to be the one answering when it is not.
  const sectionBuild = (value, key) => {
    const own = buildOf(value);
    if (own === null || sameBuild(own, build)) return null;
    const line = fillNext(copy.sectionBuild, { code: own.code, data: own.data });
    return <p className={notAnswering(own) ? 'lx-muted lx-next-gap' : 'lx-muted'} data-results2-build={key}>{line}{notAnswering(own) ? <> {copy.notServed}</> : null}</p>;
  };
  const ablationBuild = buildOf(ablation?.build);
  const pValue = (value) => (isCount(value) ? (value < 0.001 ? `< ${decimal(0.001, locale)}` : decimal(Number(value.toPrecision(2)), locale)) : null);
  const effectCell = (row) => {
    const said = row.effect === 'measured' ? copy.ablationEffect.measured : row.effect === 'no measured effect on this set' ? copy.ablationEffect.none : text(row.effect);
    if (said === null) return '';
    const first = pValue(row.p_vs_R1);
    const second = pValue(row.p_vs_R2);
    return first !== null && second !== null ? fillNext(copy.ablationEffectP, { effect: said, first, second }) : said;
  };
  const hasEffect = ablationRows.some((row) => text(row.effect) !== null);
  // The rule the effects are said by: the page's own sentence when the file's rule is that one (exact McNemar, p < 0.05,
  // against both full runs), else the file's words, marked with their language.
  const ablationRule = text(ablation?.rule) === null ? null
    : /mcnemar/iu.test(ablation.rule) && /p\s*<\s*0\.05/u.test(ablation.rule) && /\bboth\b/iu.test(ablation.rule) ? { text: copy.ablationRule, lang: undefined } : prose(ablation.rule, locale);
  const checkedBy = prose(scoring?.checked_by, locale);
  const gateMeans = prose(misses?.gate_means, locale);
  const written = prose(set?.written_by, locale);
  const setNote = prose(set?.note, locale);
  const rule = prose(scoring?.rule, locale);
  const lenient = (Array.isArray(scoring?.lenient) ? scoring.lenient : []).map((one) => prose(one, locale)).filter((one) => one !== null);
  const baselineWhat = prose(baseline?.what, locale);
  const priceSource = prose(cost?.price_source, locale);
  const conditions = prose(latency?.conditions, locale);
  const walk = retrieval !== null && isCount(retrieval.walk_found) && isCount(retrieval.walk_of) ? retrieval : null;
  const datedOf = objectOr(results.dated);
  const dated = datedOf !== null && ['n', 'right', 'read_right_version', 'wrong_version', 'wrong_asserted'].every((key) => isCount(datedOf[key])) ? datedOf : null;
  const listOf = objectOr(results.list);
  const listed = listOf !== null && ['n', 'right', 'lines_complete', 'count_said', 'count_equal'].every((key) => isCount(listOf[key])) ? listOf : null;
  // fix-pages3: the version compared with, named by the file (rc3, the live version), and said to be the one answering when it is.
  const previousName = prose(previous?.name, locale);
  const previousBuild = buildOf(previous?.build);
  const previousServed = previous !== null && describesSite(previous, served) === true;
  const decision = objectOr(results.decision);
  const decisionResult = text(decision?.result);
  const decisionRule = text(decision?.rule) !== null || objectOr(decision?.rule) !== null ? prose(decision.rule, locale) : null;
  const decisionChecks = (Array.isArray(decision?.checks) ? decision.checks : []).filter((check) => text(check?.pair) !== null && pairOf(check?.right) !== null);
  const copy4 = table.results4;
  return (
    <>
      {totals === null && wrong.length === 0 ? null : (
        <section aria-labelledby={`${idPrefix}results2-wrong`} data-results2-section="wrong">
          <H id={`${idPrefix}results2-wrong`}>{copy.wrongHeading}</H>
          {wrongCount === 0 ? <p data-results2-wrong-count="0"><strong>{copy.wrongNone}</strong>{n !== null && wrongInterval !== null ? <> {fillNext(copy.wrongLine, { n: count(n), low: decimal(wrongInterval[0], locale), high: decimal(wrongInterval[1], locale) })}</> : null}</p> : (
            <p data-results2-wrong-count={String(wrongCount)}>
              <strong>{countedForm(copy.wrongCount, wrongCount, locale)}</strong>
              {n !== null && wrongInterval !== null ? <> {fillNext(copy.wrongLine, { n: count(n), low: decimal(wrongInterval[0], locale), high: decimal(wrongInterval[1], locale) })}</> : null}
            </p>
          )}
          {wrong.length === 0 ? null : (
            <Table
              mark="wrong"
              columns={['question', 'expected', 'given', 'what'].map((key) => copy.wrongColumns[key])}
              rows={wrong.map((row, at) => ({
                key: row.id ?? String(at), head: row.question, lang: dataLang, className: 'lx-next-wrong',
                cells: [text(row.expected) ?? '', text(row.given) ?? '', name(copy.wrongWhat, row.what ?? '')], cellLang: [dataLang, dataLang, undefined],
              }))}
            />
          )}
        </section>
      )}

      {set === null && scoring === null && runLine === null ? null : (
        <section aria-labelledby={`${idPrefix}results2-set`} data-results2-section="set">
          <H id={`${idPrefix}results2-set`}>{copy.setHeading}</H>
          {set === null || n === null ? null : (
            <p>
              {written === null
                ? fillNext(copy.setBare, { n: countedForm(copy.questions, n, locale), name: text(set.name) ?? '' })
                : <Filled template={copy.set} place="who" piece={written} values={{ n: countedForm(copy.questions, n, locale), name: text(set.name) ?? '' }} />}
              {set.fixed_before_run === true ? <> {copy.fixed}</> : null}
              {' '}<strong data-results2-used={set.used_before === true ? 'before' : 'once'}>{set.used_before === true ? copy.usedBefore : copy.fresh}</strong>
            </p>
          )}
          {setNote === null ? null : <p lang={setNote.lang}>{setNote.text}</p>}
          {runLine === null ? null : <p className="lx-muted" data-results2-run="">{runLine}</p>}
          {scoring === null ? null : (
            <p className="lx-muted" data-results2-scoring="">
              {Object.hasOwn(copy.scoredBy, scoring.by) ? copy.scoredBy[scoring.by] : null}
              {rule === null ? null : <> <Filled template={copy.rule} place="rule" piece={rule} /></>}
              {lenient.map((one) => <span key={one.text} data-results2-lenient=""> <Filled template={copy.lenient} place="rule" piece={one} /></span>)}
            </p>
          )}
          {checkedBy === null ? null : <p className="lx-muted" lang={checkedBy.lang} data-results2-checked="">{checkedBy.text}</p>}
        </section>
      )}

      {totals === null ? null : (
        <section aria-labelledby={`${idPrefix}results2-totals`} data-results2-section="totals">
          <H id={`${idPrefix}results2-totals`}>{copy.totalsHeading}</H>
          <Table
            mark="totals"
            columns={[copy.totalsColumns.state, n === null ? copy.totalsColumns.count : countedForm(copy.outOf, n, locale), copy.totalsColumns.interval]}
            rows={Object.keys(copy.states).filter((key) => isCount(totals[key])).map((key) => ({
              key, head: copy.states[key], cells: [count(totals[key]), interval(totals.intervals?.[key])],
            }))}
          />
        </section>
      )}

      {retrieval === null || ((!isCount(retrieval.right_article_read) || !isCount(retrieval.of)) && walk === null) ? null : (
        <section aria-labelledby={`${idPrefix}results2-retrieval`} data-results2-section="retrieval">
          <H id={`${idPrefix}results2-retrieval`}>{copy.retrievalHeading}</H>
          {isCount(retrieval.right_article_read) && isCount(retrieval.of) ? <p>{fillNext(copy.retrieval, { right: count(retrieval.right_article_read), of: count(retrieval.of), interval: interval(retrieval.interval) || '—' })}</p> : null}
          {walk === null ? null : <p data-results2-walk="">{fillNext(copy.walk, { right: count(walk.walk_found), of: count(walk.walk_of), interval: interval(walk.walk_interval) || '—' })}</p>}
        </section>
      )}

      {slices.length === 0 ? null : (
        <section aria-labelledby={`${idPrefix}results2-slices`} data-results2-section="slices">
          <H id={`${idPrefix}results2-slices`}>{copy.slicesHeading}</H>
          <Table
            mark="slices"
            columns={['slice', 'n', 'right', 'wrong', 'notFound', 'interval'].map((key) => copy.slicesColumns[key])}
            rows={slices.map((row) => ({ key: row.slice, head: name(copy.slices, row.slice), cells: [count(row.n), count(row.right), count(row.wrong), count(row.not_found), interval(row.interval)] }))}
          />
          {/* fix-pages3: the dated questions and the lists, measured apart (protocol K-TIME-5 and the list items), when the file counts them. */}
          {dated === null ? null : <p data-results2-dated=""><strong>{copy4.datedHeading}</strong> {fillNext(copy4.dated, { n: count(dated.n), right: count(dated.right), read: count(dated.read_right_version), version: count(dated.wrong_version), wrong: count(dated.wrong_asserted) })}</p>}
          {listed === null ? null : <p data-results2-list=""><strong>{copy4.listHeading}</strong> {fillNext(copy4.list, { n: count(listed.n), right: count(listed.right), complete: count(listed.lines_complete), said: count(listed.count_said), equal: count(listed.count_equal) })}</p>}
        </section>
      )}

      {byLaw.length === 0 && byType.length === 0 ? null : (
        <details className="lx-how-fold" data-results2-section="detail">
          <summary>{copy.foldSummary}</summary>
          {byLaw.length === 0 ? null : (
            <Table mark="laws" columns={['law', 'n', 'right', 'wrong', 'interval'].map((key) => copy.lawColumns[key] ?? copy.slicesColumns[key])}
              rows={byLaw.map((row) => ({ key: row.law, head: lawName(row.law), lang: Object.hasOwn(table.results.laws, row.law) ? undefined : dataLang, cells: [count(row.n), count(row.right), count(row.wrong), interval(row.interval)] }))} />
          )}
          {byType.length === 0 ? null : (
            <Table mark="types" columns={['type', 'n', 'right', 'wrong', 'interval'].map((key) => copy.typeColumns[key] ?? copy.slicesColumns[key])}
              rows={byType.map((row) => ({ key: row.type, head: typeName(row.type), cells: [count(row.n), count(row.right), count(row.wrong), interval(row.interval)] }))} />
          )}
        </details>
      )}

      {misses === null ? null : (
        <section aria-labelledby={`${idPrefix}results2-misses`} data-results2-section="misses">
          <H id={`${idPrefix}results2-misses`}>{copy.missesHeading}</H>
          <Table
            mark="misses"
            columns={[copy.missesColumns.label, copy.missesColumns.count]}
            rows={Object.keys(copy.missesLabels).filter((key) => isCount(misses[key])).map((key) => ({ key, head: copy.missesLabels[key], cells: [count(misses[key])] }))}
          />
          {gateMeans === null || !isCount(misses.gate) ? null : <p className="lx-muted" lang={gateMeans.lang} data-results2-gate="">{gateMeans.text}</p>}
          {missItems.length === 0 ? null : (
            <details className="lx-how-fold">
              <summary>{copy.missesItems}</summary>
              <Table
                mark="miss-items"
                columns={['question', 'label', 'note'].map((key) => copy.missesItemColumns[key])}
                rows={missItems.map((row, at) => {
                  const said = prose(row.note, locale);
                  return { key: row.id ?? String(at), head: row.question, lang: dataLang, cells: [name(copy.missesLabelsShort, row.label ?? ''), said?.text ?? ''], cellLang: [undefined, said?.lang] };
                })}
              />
            </details>
          )}
        </section>
      )}

      {absent === null || !isCount(absent.n) ? null : (
        <section aria-labelledby={`${idPrefix}results2-absent`} data-results2-section="absent">
          <H id={`${idPrefix}results2-absent`}>{copy.absentHeading}</H>
          <p>{fillNext(copy.absentIntro, { n: countedForm(copy.questions, absent.n, locale) })}</p>
          <Table
            mark="absent"
            columns={[copy.absentColumns.what, copy.absentColumns.count]}
            rows={Object.keys(copy.absentRows).filter((key) => isCount(absent[key])).map((key) => ({ key, head: copy.absentRows[key], cells: [count(absent[key])] }))}
          />
        </section>
      )}

      {ablationRows.length === 0 ? null : (
        <section aria-labelledby={`${idPrefix}results2-ablation`} data-results2-section="ablation">
          <H id={`${idPrefix}results2-ablation`}>{copy.ablationHeading}</H>
          {ablationBuild === null ? null : (
            <p className={notAnswering(ablationBuild) ? 'lx-muted lx-next-gap' : 'lx-muted'} data-results2-build="ablation">
              {fillNext(copy.ablationBuild, ablationBuild)}
              {notAnswering(ablationBuild) ? <> {copy.notServed}</> : null}
            </p>
          )}
          <Table
            mark="ablation"
            columns={['what', 'right', 'wrong', 'interval', ...(hasEffect ? ['effect'] : [])].map((key) => copy.ablationColumns[key])}
            rows={ablationRows.map((row) => ({
              key: ablationKey(row),
              head: text(row.run) === null ? name(copy.ablation, row.what) : fillNext(copy.ablationRun, { what: name(copy.ablation, row.what), run: row.run.trim() }),
              cells: [count(row.right), count(row.wrong), interval(row.interval), ...(hasEffect ? [effectCell(row)] : [])],
            }))}
          />
          {hasEffect && ablationRule !== null ? <p className="lx-muted" data-results2-ablation-rule="" lang={ablationRule.lang}>{ablationRule.text}</p> : null}
        </section>
      )}

      {baseline === null || !isCount(baseline.right) ? null : (
        <section aria-labelledby={`${idPrefix}results2-baseline`} data-results2-section="baseline">
          <H id={`${idPrefix}results2-baseline`}>{copy.baselineHeading}</H>
          {sectionBuild(baseline.build, 'baseline')}
          {baselineWhat === null ? null : <p className="lx-muted"><Filled template={copy.baselineWhat} place="what" piece={baselineWhat} /></p>}
          <Table
            mark="baseline"
            columns={['who', 'right', 'wrong', 'interval'].map((key) => copy.baselineColumns[key])}
            rows={[
              ...(isCount(totals?.right) ? [{ key: 'lex', head: copy.baselineLex, cells: [count(totals.right), count(totals.wrong), interval(totals.intervals?.right)] }] : []),
              { key: 'naive', head: copy.baselineNaive, cells: [count(baseline.right), count(baseline.wrong), interval(baseline.interval)] },
            ]}
          />
          {isCount(baseline.naive_right_lex_not) && isCount(baseline.lex_right_naive_not) ? (
            <p data-results2-baseline-wins="">{fillNext(copy.baselineWins, { naive: count(baseline.naive_right_lex_not), lex: count(baseline.lex_right_naive_not) })}</p>
          ) : null}
        </section>
      )}

      {previous === null || !isCount(previous.right) ? null : (
        <section aria-labelledby={`${idPrefix}results2-previous`} data-results2-section="previous">
          <H id={`${idPrefix}results2-previous`} lang={previousName?.lang}>{previousName === null ? copy.previousHeading : fillNext(copy4.previousNamedHeading, { name: previousName.text })}</H>
          {previousServed ? <p className="lx-muted" data-results2-previous-served="">{copy4.previousServed}</p> : null}
          <Table
            mark="previous"
            columns={['who', 'right', 'wrong', 'notFound', 'interval'].map((key) => copy.previousColumns[key])}
            rows={[
              ...(isCount(totals?.right) ? [{ key: 'now', head: build === null ? copy.previousNowBare : fillNext(copy.previousNow, build), cells: [count(totals.right), count(totals.wrong), count(totals.not_found), interval(totals.intervals?.right)] }] : []),
              {
                key: 'before',
                head: previousName !== null ? (previousBuild === null ? previousName.text : fillNext(copy4.previousNamed, { name: previousName.text, ...previousBuild }))
                  : previousBuild === null ? copy.previousBeforeBare : fillNext(copy.previousBefore, previousBuild),
                cells: [count(previous.right), count(previous.wrong), count(previous.not_found), interval(previous.interval)],
              },
            ]}
          />
          {isCount(previous.n) && n !== null && previous.n !== n ? <p className="lx-muted lx-next-gap" data-results2-previous-n="">{fillNext(copy.previousOtherN, { n: count(previous.n), now: count(n) })}</p> : null}
          {isCount(previous.better) && isCount(previous.worse) ? (
            <p data-results2-previous-changed="">
              {fillNext(copy.previousChanged, { better: count(previous.better), worse: count(previous.worse) })}
              {isCount(previous.p) ? <> {fillNext(copy4.previousP, { p: pValue(previous.p) })}</> : null}
            </p>
          ) : null}
          {previousItems.length === 0 ? null : (
            <details className="lx-how-fold">
              <summary>{copy.previousItems}</summary>
              <Table
                mark="previous-items"
                columns={['question', 'before', 'now'].map((key) => copy.previousItemColumns[key])}
                rows={previousItems.map((row, at) => ({ key: row.id ?? String(at), head: row.question, lang: dataLang, cells: [stateName(row.before ?? ''), stateName(row.now ?? '')] }))}
              />
            </details>
          )}
        </section>
      )}

      {decisionResult === null ? null : (
        <section aria-labelledby={`${idPrefix}results2-decision`} data-results2-section="decision">
          <H id={`${idPrefix}results2-decision`}>{copy4.decisionHeading}</H>
          <p data-results2-decision={decisionResult}><strong>{fillNext(copy4.decisionLine, { result: copy4.decisions[decisionResult] ?? decisionResult })}</strong></p>
          {decisionRule === null ? null : <p className="lx-muted" lang={decisionRule.lang} data-results2-decision-rule="">{decisionRule.text}</p>}
          {decisionChecks.length === 0 ? null : (
            <Table
              mark="decision"
              columns={['pair', 'right', 'discordant', 'p', 'wrong'].map((key) => copy4.decisionColumns[key])}
              rows={decisionChecks.map((check) => {
                const right = pairOf(check.right);
                const discordant = pairOf(check.discordant);
                const wrongPair = pairOf(check.wrong_asserted);
                return {
                  key: check.pair, head: check.pair,
                  cells: [fillNext(copy4.pair, { a: count(right[0]), b: count(right[1]) }), discordant === null ? '' : fillNext(copy4.pair, { a: count(discordant[0]), b: count(discordant[1]) }), pValue(check.p) ?? '', wrongPair === null ? '' : fillNext(copy4.pair, { a: count(wrongPair[0]), b: count(wrongPair[1]) })],
                };
              })}
            />
          )}
        </section>
      )}

      {noise === null || !isCount(noise.runs) || !Array.isArray(noise.right) ? null : (
        <section aria-labelledby={`${idPrefix}results2-noise`} data-results2-section="noise">
          <H id={`${idPrefix}results2-noise`}>{copy.noiseHeading}</H>
          {sectionBuild(noise.build, 'noise')}
          <p>{fillNext(copy.noise, { runs: countedForm(copy.runs, noise.runs, locale), right: noise.right.filter(isCount).map(count).join(table.listSeparator), wrong: (Array.isArray(noise.wrong) ? noise.wrong : []).filter(isCount).map(count).join(table.listSeparator) || '—' })}</p>
        </section>
      )}

      {cost === null && latency === null ? null : (
        <section aria-labelledby={`${idPrefix}results2-cost`} data-results2-section="cost">
          <H id={`${idPrefix}results2-cost`}>{copy.costHeading}</H>
          {[usd(cost?.usd_per_answer?.mean), usd(cost?.usd_per_answer?.median), usd(cost?.usd_per_answer?.p90)].every((value) => value !== null) ? (
            <p>{fillNext(copy.cost, { mean: usd(cost.usd_per_answer.mean), median: usd(cost.usd_per_answer.median), slow: usd(cost.usd_per_answer.p90) })}</p>
          ) : null}
          {isCount(cost?.model_calls_per_answer?.mean) && isCount(cost?.model_calls_per_answer?.max) ? (
            <p>{fillNext(copy.calls, { mean: decimal(cost.model_calls_per_answer.mean, locale, 1), max: count(cost.model_calls_per_answer.max) })}</p>
          ) : null}
          {[seconds(latency?.median_ms), seconds(latency?.p90_ms), seconds(latency?.max_ms)].every((value) => value !== null) ? (
            <p>{fillNext(copy.latency, { median: seconds(latency.median_ms), slow: seconds(latency.p90_ms), max: seconds(latency.max_ms) })}</p>
          ) : null}
          {conditions === null ? null : <p className="lx-muted" data-results2-conditions=""><Filled template={copy.conditions} place="how" piece={conditions} /></p>}
          {priceSource === null ? null : <p className="lx-muted" lang={priceSource.lang}>{priceSource.text}</p>}
        </section>
      )}
    </>
  );
}
