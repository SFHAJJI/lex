// The Results page for an evaluation in the schema `lex-results/2` (`web/RESULTS-SCHEMA.md`), in the earlier pages' look
// (`.lx-how-table`, `.lx-how-fold`, `.lx-prose`): the questions and how they were scored, the wrong answers asserted first,
// every question by result, the search measured apart, the slices with their counts and margins, by text and by kind
// (folded), the misses labelled, the questions whose answer Lex does not hold, each brick switched off, a naive pipeline on
// the same questions, the same questions asked again, cost and time. Every figure is the file's; a section the file does
// not carry is left out; a key the copy does not name is shown as the file writes it.

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

/** A table of rows under its column names; each cell a string. */
function Table({ columns, rows, mark }) {
  return (
    <div className="lx-how-table-scroll">
      <table className="lx-how-table lx-next-counts" data-results2={mark}>
        <thead><tr>{columns.map((column) => <th scope="col" key={column}>{column}</th>)}</tr></thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} data-row={row.key} className={row.className}>
              <th scope="row" lang={row.lang}>{row.head}</th>
              {row.cells.map((cell, at) => <td key={at} lang={row.cellLang?.[at]}>{cell}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** The results in `lex-results/2`, drawn whole. */
export function Results2View({ results, locale = CHROME_LOCALE, note = null }) {
  const table = nextCopy(locale);
  const copy = table.results2;
  const dataLang = locale === 'fr' ? undefined : 'fr';
  const count = (value) => (isCount(value) ? groupedCount(value, locale) : '');
  const interval = (value) => {
    const pair = pairOf(value);
    return pair === null ? '' : fillNext(copy.interval, { low: decimal(pair[0], locale), high: decimal(pair[1], locale) });
  };
  const set = objectOr(results.set);
  const scoring = objectOr(results.scoring);
  const build = objectOr(results.build);
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
  const noise = objectOr(results.noise);
  const cost = objectOr(results.cost);
  const latency = objectOr(results.latency);
  const name = (map, key) => (Object.hasOwn(map, key) ? map[key] : key);
  const lawName = (key) => name(table.results.laws, key);
  const typeName = (key) => name(table.questionTypes, key);
  const usd = (value) => (isCount(value) ? decimal(value, locale) : null);
  const seconds = (ms) => (isCount(ms) ? decimal(ms / 1000, locale, 1) : null);
  const runLine = text(results.run) !== null && text(results.made) !== null && text(build?.code) !== null && text(build?.data) !== null
    ? fillNext(copy.run, { run: results.run, date: numericDate(results.made), code: build.code, data: build.data }) : null;
  return (
    <div data-next-page="results" data-next-state="ready" data-results-schema={RESULTS_SCHEMA} data-run={results.run ?? ''}>
      <h1>{table.results.heading}</h1>
      {note}

      {set === null && scoring === null && runLine === null ? null : (
        <section aria-labelledby="results2-set" data-results2-section="set">
          <h2 id="results2-set">{copy.setHeading}</h2>
          {set === null || n === null ? null : (
            <p>
              {fillNext(copy.set, { n: countedForm(copy.questions, n, locale), name: text(set.name) ?? '', who: '\u0000' }).split('\u0000')[0]}
              <span lang={dataLang}>{text(set.written_by) ?? ''}</span>
              {fillNext(copy.set, { n: '', name: '', who: '\u0000' }).split('\u0000')[1]}
              {set.fixed_before_run === true ? <> {copy.fixed}</> : null}
              {' '}<strong data-results2-used={set.used_before === true ? 'before' : 'once'}>{set.used_before === true ? copy.usedBefore : copy.fresh}</strong>
            </p>
          )}
          {text(set?.note) === null ? null : <p lang={dataLang}>{set.note}</p>}
          {runLine === null ? null : <p className="lx-muted">{runLine}</p>}
          {scoring === null ? null : (
            <p className="lx-muted" data-results2-scoring="">
              {Object.hasOwn(copy.scoredBy, scoring.by) ? copy.scoredBy[scoring.by] : null}
              {text(scoring.rule) === null ? null : <> {fillNext(copy.rule, { rule: '\u0000' }).split('\u0000')[0]}<span lang={dataLang}>{scoring.rule.trim().replace(/[.\s]+$/u, '')}</span>{fillNext(copy.rule, { rule: '\u0000' }).split('\u0000')[1]}</>}
              {(Array.isArray(scoring.lenient) ? scoring.lenient : []).filter((rule) => text(rule) !== null).map((rule) => (
                <span key={rule} data-results2-lenient=""> {fillNext(copy.lenient, { rule: '\u0000' }).split('\u0000')[0]}<span lang={dataLang}>{rule.trim().replace(/[.\s]+$/u, '')}</span>{fillNext(copy.lenient, { rule: '\u0000' }).split('\u0000')[1]}</span>
              ))}
            </p>
          )}
        </section>
      )}

      {totals === null && wrong.length === 0 ? null : (
        <section aria-labelledby="results2-wrong" data-results2-section="wrong">
          <h2 id="results2-wrong">{copy.wrongHeading}</h2>
          {wrongCount === 0 ? <p data-results2-wrong-count="0"><strong>{copy.wrongNone}</strong></p> : (
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

      {totals === null ? null : (
        <section aria-labelledby="results2-totals" data-results2-section="totals">
          <h2 id="results2-totals">{copy.totalsHeading}</h2>
          <Table
            mark="totals"
            columns={[copy.totalsColumns.state, n === null ? copy.totalsColumns.count : countedForm(copy.outOf, n, locale), copy.totalsColumns.interval]}
            rows={Object.keys(copy.states).filter((key) => isCount(totals[key])).map((key) => ({
              key, head: copy.states[key], cells: [count(totals[key]), interval(totals.intervals?.[key])],
            }))}
          />
        </section>
      )}

      {retrieval === null || !isCount(retrieval.right_article_read) || !isCount(retrieval.of) ? null : (
        <section aria-labelledby="results2-retrieval" data-results2-section="retrieval">
          <h2 id="results2-retrieval">{copy.retrievalHeading}</h2>
          <p>{fillNext(copy.retrieval, { right: count(retrieval.right_article_read), of: count(retrieval.of), interval: interval(retrieval.interval) || '—' })}</p>
        </section>
      )}

      {slices.length === 0 ? null : (
        <section aria-labelledby="results2-slices" data-results2-section="slices">
          <h2 id="results2-slices">{copy.slicesHeading}</h2>
          <Table
            mark="slices"
            columns={['slice', 'n', 'right', 'wrong', 'notFound', 'interval'].map((key) => copy.slicesColumns[key])}
            rows={slices.map((row) => ({ key: row.slice, head: name(copy.slices, row.slice), cells: [count(row.n), count(row.right), count(row.wrong), count(row.not_found), interval(row.interval)] }))}
          />
        </section>
      )}

      {byLaw.length === 0 && byType.length === 0 ? null : (
        <details className="lx-how-fold" data-results2-section="detail">
          <summary>{copy.foldSummary}</summary>
          {byLaw.length === 0 ? null : (
            <Table mark="laws" columns={['law', 'n', 'right', 'wrong'].map((key) => copy.lawColumns[key])}
              rows={byLaw.map((row) => ({ key: row.law, head: lawName(row.law), cells: [count(row.n), count(row.right), count(row.wrong)] }))} />
          )}
          {byType.length === 0 ? null : (
            <Table mark="types" columns={['type', 'n', 'right', 'wrong'].map((key) => copy.typeColumns[key])}
              rows={byType.map((row) => ({ key: row.type, head: typeName(row.type), cells: [count(row.n), count(row.right), count(row.wrong)] }))} />
          )}
        </details>
      )}

      {misses === null ? null : (
        <section aria-labelledby="results2-misses" data-results2-section="misses">
          <h2 id="results2-misses">{copy.missesHeading}</h2>
          <Table
            mark="misses"
            columns={[copy.missesColumns.label, copy.missesColumns.count]}
            rows={Object.keys(copy.missesLabels).filter((key) => isCount(misses[key])).map((key) => ({ key, head: copy.missesLabels[key], cells: [count(misses[key])] }))}
          />
          {missItems.length === 0 ? null : (
            <details className="lx-how-fold">
              <summary>{copy.missesItems}</summary>
              <Table
                mark="miss-items"
                columns={['question', 'label', 'note'].map((key) => copy.missesItemColumns[key])}
                rows={missItems.map((row, at) => ({ key: row.id ?? String(at), head: row.question, lang: dataLang, cells: [name(copy.missesLabels, row.label ?? ''), text(row.note) ?? ''], cellLang: [undefined, dataLang] }))}
              />
            </details>
          )}
        </section>
      )}

      {absent === null || !isCount(absent.n) ? null : (
        <section aria-labelledby="results2-absent" data-results2-section="absent">
          <h2 id="results2-absent">{copy.absentHeading}</h2>
          <p>{fillNext(copy.absentIntro, { n: countedForm(copy.questions, absent.n, locale) })}</p>
          <Table
            mark="absent"
            columns={[copy.absentColumns.what, copy.absentColumns.count]}
            rows={Object.keys(copy.absentRows).filter((key) => isCount(absent[key])).map((key) => ({ key, head: copy.absentRows[key], cells: [count(absent[key])] }))}
          />
        </section>
      )}

      {ablationRows.length === 0 ? null : (
        <section aria-labelledby="results2-ablation" data-results2-section="ablation">
          <h2 id="results2-ablation">{copy.ablationHeading}</h2>
          {text(ablation.build?.code) === null || text(ablation.build?.data) === null ? null : <p className="lx-muted">{fillNext(copy.ablationBuild, { code: ablation.build.code, data: ablation.build.data })}</p>}
          <Table
            mark="ablation"
            columns={['what', 'right', 'wrong', 'interval'].map((key) => copy.ablationColumns[key])}
            rows={ablationRows.map((row) => ({ key: row.what, head: name(copy.ablation, row.what), cells: [count(row.right), count(row.wrong), interval(row.interval)] }))}
          />
        </section>
      )}

      {baseline === null || !isCount(baseline.right) ? null : (
        <section aria-labelledby="results2-baseline" data-results2-section="baseline">
          <h2 id="results2-baseline">{copy.baselineHeading}</h2>
          {text(baseline.what) === null ? null : <p className="lx-muted">{fillNext(copy.baselineWhat, { what: '\u0000' }).split('\u0000')[0]}<span lang={dataLang}>{baseline.what.trim().replace(/[.\s]+$/u, '')}</span>{fillNext(copy.baselineWhat, { what: '\u0000' }).split('\u0000')[1]}</p>}
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

      {noise === null || !isCount(noise.runs) || !Array.isArray(noise.right) ? null : (
        <section aria-labelledby="results2-noise" data-results2-section="noise">
          <h2 id="results2-noise">{copy.noiseHeading}</h2>
          <p>{fillNext(copy.noise, { runs: countedForm(copy.runs, noise.runs, locale), right: noise.right.filter(isCount).map(count).join(table.listSeparator), wrong: (Array.isArray(noise.wrong) ? noise.wrong : []).filter(isCount).map(count).join(table.listSeparator) || '—' })}</p>
        </section>
      )}

      {cost === null && latency === null ? null : (
        <section aria-labelledby="results2-cost" data-results2-section="cost">
          <h2 id="results2-cost">{copy.costHeading}</h2>
          {[usd(cost?.usd_per_answer?.mean), usd(cost?.usd_per_answer?.median), usd(cost?.usd_per_answer?.p90)].every((value) => value !== null) ? (
            <p>{fillNext(copy.cost, { mean: usd(cost.usd_per_answer.mean), median: usd(cost.usd_per_answer.median), slow: usd(cost.usd_per_answer.p90) })}</p>
          ) : null}
          {isCount(cost?.model_calls_per_answer?.mean) && isCount(cost?.model_calls_per_answer?.max) ? (
            <p>{fillNext(copy.calls, { mean: decimal(cost.model_calls_per_answer.mean, locale, 1), max: count(cost.model_calls_per_answer.max) })}</p>
          ) : null}
          {[seconds(latency?.median_ms), seconds(latency?.p90_ms), seconds(latency?.max_ms)].every((value) => value !== null) ? (
            <p>{fillNext(copy.latency, { median: seconds(latency.median_ms), slow: seconds(latency.p90_ms), max: seconds(latency.max_ms) })}</p>
          ) : null}
          {text(cost?.price_source) === null ? null : <p className="lx-muted" lang={dataLang}>{cost.price_source}</p>}
        </section>
      )}
    </div>
  );
}
