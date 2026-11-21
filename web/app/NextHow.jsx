// "How it works" and "Design choices" for the engine's funnel (`lex-next/TRACE-CONTRACT.md`), in the V4 pages' look.
//
//   - How it works (`evidence.html`): on the first screen, the funnel's seven levels, each with who does it (code, a model,
//     or both), and beside it the architecture on one panel: the models in use by role, read from the server's own
//     configuration (`GET /api/next/config`: K-OPS-6), where the calls run (the EU data zone: K-OPS-7), what the server
//     keeps, and that what a reader types is read as data (K-OPS-8). Under it: the limits with their figures (each code's
//     earliest version held), what Lex commits to, and the corpus in figures behind one fold.
//   - Design choices (`decisions.html`): each choice with what it brings and what it costs.
//
// Every word is `scripts/next-copy.mjs`'s (`how2`, `decisions2`, and the earlier `how` for the limits and commitments);
// every count and every model name is the server's. Built into its page in its loading state; `client-live-next-entry.jsx`
// draws it again once the page has asked.

import { useEffect, useState } from 'react';
import { CHROME_LOCALE, livePath } from '../scripts/live-chrome.mjs';
import { countedForm, fillNext, nextCopy, numericDate } from '../scripts/next-copy.mjs';
import { HowView, brickCounts } from './NextPages.jsx';

/** The pipelines whose design is the funnel's; any other (topk, the earlier one) is said by the earlier page. */
export const FUNNEL_PIPELINES = Object.freeze(['funnel', 'combo']);

/** The root the design choices are drawn into. */
export const NEXT_DECISIONS_ROOT = 'lx-next-decisions';

/** The addresses asked. */
export const CONFIG_ADDRESS = '/api/next/config';
const CORPUS = '/api/next/corpus';

const isCount = (value) => typeof value === 'number' && Number.isFinite(value);
const objectOr = (value) => (value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null);
const text = (value) => (typeof value === 'string' && value.trim().length > 0 ? value.trim() : null);
const rowsOf = (value) => (Array.isArray(value) ? value.filter((row) => Array.isArray(row)) : []);

/** One address asked once by GET after the page is drawn: `{ state, data }`. */
function useJson(address, fetchImpl) {
  const [answer, setAnswer] = useState({ state: 'loading', data: null });
  useEffect(() => {
    let live = true;
    (async () => {
      try {
        const response = await (fetchImpl ?? globalThis.fetch)(address, { method: 'GET', headers: { accept: 'application/json' } });
        if (!response.ok) throw new Error('not answered');
        const data = await response.json();
        if (objectOr(data) === null) throw new Error('not an object');
        if (live) setAnswer({ state: 'ready', data });
      } catch {
        if (live) setAnswer({ state: 'failed', data: null });
      }
    })();
    return () => { live = false; };
  }, [address, fetchImpl]);
  return answer;
}

/** The tag of who does a stage. */
function Tag({ by, dock }) {
  return <span className={by === 'code' ? 'lx-how-tag' : 'lx-how-tag lx-how-tag-model'} data-by={by}>{by === 'code' ? dock.tagCode : by === 'model' ? dock.tagModel : dock.tagBoth}</span>;
}

/**
 * The models in use, by role, from the server's configuration: `[{ key, role, deployment }]`, the embedding and the fallback
 * after the roles. A role the copy does not name is shown by its key; the same deployment under two roles is said twice.
 */
export function modelRows(config, locale) {
  const copy = nextCopy(locale).how2;
  const value = objectOr(config);
  if (value === null) return [];
  const rows = (Array.isArray(value.models) ? value.models : []).filter((row) => text(row?.role) !== null && text(row?.deployment) !== null)
    .map((row) => ({ key: row.role, role: copy.roles[row.role] ?? row.role.replaceAll('_', ' '), deployment: row.deployment }));
  if (text(value.embedding) !== null) rows.push({ key: 'embedding', role: copy.roles.embedding, deployment: value.embedding });
  if (text(value.fallback) !== null) rows.push({ key: 'fallback', role: copy.roles.fallback, deployment: value.fallback });
  return rows;
}

/** Where the calls run, in one sentence from the server's configuration; null when it says nothing. */
export function residencyLine(config, locale) {
  const copy = nextCopy(locale).how2;
  const residency = objectOr(objectOr(config)?.residency);
  const calls = objectOr(residency?.model_calls);
  const server = objectOr(residency?.server);
  if (calls === null || server === null || calls.zone !== 'EU' || text(calls.deployment_type) === null || text(calls.resource_region) === null || text(server.region) === null) return null;
  const region = (key) => copy.regions[key] ?? key;
  return fillNext(copy.residency, { type: calls.deployment_type, modelRegion: region(calls.resource_region), serverRegion: region(server.region) });
}

/** What the server keeps, in one sentence: nothing of a question or an answer, or what the configuration says it keeps. */
export function keptLine(config, locale) {
  const copy = nextCopy(locale).how2;
  const kept = objectOr(objectOr(config)?.kept_on_server);
  if (kept === null) return null;
  if (kept.questions === false && kept.answers === false) return copy.keptNothing;
  const what = Object.entries(kept).filter(([, value]) => value !== false && value !== null).map(([key, value]) => (typeof value === 'string' ? `${key} (${value})` : key));
  return what.length === 0 ? null : fillNext(copy.keptSome, { what: what.join(nextCopy(locale).listSeparator) });
}

/** The limits, with their figures: each code's versions held and the earliest, then the limits the copy states. */
function Limits({ corpus, locale }) {
  const table = nextCopy(locale);
  const copy = table.how;
  // pages2 (21/11/2026, K-UI-7): the funnel's own limits (references to other texts followed, the EU insurance texts held),
  // over the earlier engine's, which the topk page keeps.
  const limits = { ...copy.limits, ...table.how2.limits };
  const lawLang = locale === 'fr' ? undefined : 'fr';
  const said = [['not-read', 'notRead', 'notReadText'], ['references', 'references', 'referencesText'], ['dictionary', 'dictionary', 'dictionaryText'], ['languages', 'languages', 'languagesText'], ['european', 'european', 'europeanText']]
    .filter(([, head, body]) => typeof limits[head] === 'string' && typeof limits[body] === 'string');
  return (
    <section className="lx-how-block lx-how-wide" aria-labelledby="how-limits" data-reference-section="limits" id="limits">
      <h2 id="how-limits">{copy.limitsHeading}</h2>
      <div className="lx-how-table-scroll">
        <table className="lx-how-table">
          <tbody>
            {corpus === null ? null : rowsOf(corpus.codes).filter((row) => typeof row[0] === 'string' && isCount(row[3])).map((row) => (
              <tr key={row[0]} data-limit="code">
                <th scope="row" lang={lawLang}>{row[0]}</th>
                <td>{countedForm(limits.codeVersions, row[3], locale, { from: numericDate(row[1]), to: numericDate(row[2]) })}</td>
              </tr>
            ))}
            {said.map(([key, head, body]) => <tr key={key} data-limit={key}><th scope="row">{limits[head]}</th><td>{limits[body]}</td></tr>)}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/** How it works, drawn from the corpus counts and the server's configuration (each null while not known). */
export function HowFunnelView({ corpus = null, config = null, state = 'loading', configState = 'loading', locale = CHROME_LOCALE }) {
  const table = nextCopy(locale);
  const copy = table.how2;
  const dock = table.dock;
  const models = modelRows(config, locale);
  const residency = residencyLine(config, locale);
  const kept = keptLine(config, locale);
  const counts = corpus === null ? [] : table.how.bricks.flatMap((brick) => brick.counts.map((template) => brickCounts(template, corpus, locale))).filter((line) => line !== null);
  return (
    <div className="lx-how" data-next-page="how" data-next-state={state} data-how-version="funnel">
      <div className="lx-how-first">
        <div className="lx-how-flow" data-reference-section="funnel">
          <h1>{copy.heading}</h1>
          <p className="lx-how-intro">{copy.intro}</p>
          <h2 className="lx-visually-hidden">{copy.funnelHeading}</h2>
          <ol className="lx-how-stages" aria-label={copy.funnelHeading}>
            {copy.stages.map((stage, at) => (
              <li className="lx-how-stage" data-stage={stage.key} key={stage.key}>
                <span className="lx-how-n" aria-hidden="true">{at + 1}</span>
                <div className="lx-how-name">
                  <h3>{stage.name}</h3>
                  <Tag by={stage.by} dock={dock} />
                </div>
                <div className="lx-how-body">
                  <p>{stage.text}</p>
                  {stage.key === 'detectors' && config?.pipeline === 'combo' ? <p data-how-combo="">{copy.comboNote}</p> : null}
                </div>
              </li>
            ))}
          </ol>
        </div>
        <aside className="lx-how-panel" aria-labelledby="how-architecture" data-reference-section="architecture">
          <h2 id="how-architecture">{copy.architectureHeading}</h2>
          <section aria-labelledby="how-models" data-how-models="">
            <h3 id="how-models">{copy.modelsHeading}</h3>
            {models.length === 0 ? (
              configState === 'failed' ? <p className="lx-muted" role="status">{copy.configFailed}</p> : null
            ) : (
              <>
                <table className="lx-how-table lx-next-counts">
                  <tbody>
                    {models.map((row) => <tr key={`${row.key}-${row.deployment}`} data-model-role={row.key}><th scope="row">{row.role}</th><td><code>{row.deployment}</code></td></tr>)}
                  </tbody>
                </table>
                <p className="lx-next-note">{copy.modelsNote}</p>
              </>
            )}
          </section>
          {residency === null ? null : (
            <section aria-labelledby="how-residency" data-how-residency="">
              <h3 id="how-residency">{copy.residencyHeading}</h3>
              <p>{residency}</p>
            </section>
          )}
          {kept === null ? null : (
            <section aria-labelledby="how-kept" data-how-kept="">
              <h3 id="how-kept">{copy.keptHeading}</h3>
              <p>{kept}</p>
            </section>
          )}
          <section aria-labelledby="how-data" data-how-data="">
            <h3 id="how-data">{copy.dataHeading}</h3>
            <p>{copy.dataText}</p>
          </section>
          <p className="lx-how-all">
            <a href={livePath('coverage.html')}>{copy.coverageLink}</a><br />
            <a href={livePath('decisions.html')}>{copy.decisionsLink}</a><br />
            <a href={livePath('results.html')}>{copy.resultsLink}</a>
          </p>
        </aside>
      </div>
      <div className="lx-how-more">
        <Limits corpus={corpus} locale={locale} />
        <section className="lx-how-block lx-how-wide" aria-labelledby="how-commitments" data-reference-section="commitments" id="commitments">
          <h2 id="how-commitments">{table.how.commitmentsHeading}</h2>
          <ul className="lx-how-lines">{copy.commitments.map((line) => <li key={line}>{line}</li>)}</ul>
        </section>
        {counts.length === 0 ? null : (
          <details className="lx-how-fold lx-how-wide" data-how-counts="">
            <summary>{copy.countsSummary}</summary>
            <pre className="lx-how-record" data-brick-counts=""><code>{counts.join('\n')}</code></pre>
          </details>
        )}
      </div>
    </div>
  );
}

/**
 * How it works, live, for the pipeline that answers visitors (the server's configuration says which): the funnel's page, or
 * the earlier pipeline's (`HowView`). Until the configuration is read, the heading and a loading line only: the page never
 * describes a design before it knows it is the one running.
 */
export function HowForPipeline({ corpus = null, config = null, state = 'loading', configState = 'loading', locale = CHROME_LOCALE }) {
  const pipeline = objectOr(config)?.pipeline;
  if (configState === 'ready' && typeof pipeline === 'string' && !FUNNEL_PIPELINES.includes(pipeline)) return <HowView corpus={corpus} state={state} locale={locale} />;
  if (configState === 'ready' && FUNNEL_PIPELINES.includes(pipeline)) return <HowFunnelView corpus={corpus} config={config} state={state} configState={configState} locale={locale} />;
  const copy = nextCopy(locale).how2;
  return (
    <div className="lx-how" data-next-page="how" data-next-state={configState === 'failed' ? 'failed' : 'loading'}>
      <div className="lx-how-first"><div className="lx-how-flow"><h1>{copy.heading}</h1><p className="lx-how-intro" role="status" data-next-status="">{configState === 'failed' ? copy.configFailed : nextCopy(locale).how.loading}</p></div></div>
    </div>
  );
}

/** How it works, live: built in its loading state; the corpus counts and the configuration asked once the page is drawn. */
export function NextHowFunnel({ fetchImpl = undefined, locale = CHROME_LOCALE }) {
  const corpus = useJson(CORPUS, fetchImpl);
  const config = useJson(CONFIG_ADDRESS, fetchImpl);
  return <HowForPipeline corpus={corpus.data} config={config.data} state={corpus.state} configState={config.state} locale={locale} />;
}

/** The design choices, live: the configuration asked once the page is drawn. */
export function NextDecisions({ fetchImpl = undefined, locale = CHROME_LOCALE }) {
  const config = useJson(CONFIG_ADDRESS, fetchImpl);
  return <DecisionsView pipeline={typeof config.data?.pipeline === 'string' ? config.data.pipeline : null} state={config.state} locale={locale} />;
}

/**
 * The design choices of the pipeline that answers visitors, by group: each its title, what it brings and what it costs. A
 * choice made for one design only (`only`) is said when that design runs; until the configuration is read, the heading and a
 * loading line only.
 */
export function DecisionsView({ pipeline = null, state = 'ready', locale = CHROME_LOCALE }) {
  const copy = nextCopy(locale).decisions2;
  const groups = Object.keys(copy.groups);
  const design = typeof pipeline === 'string' ? (FUNNEL_PIPELINES.includes(pipeline) ? 'funnel' : 'topk') : null;
  if (design === null) {
    return (
      <div className="lx-dec" data-next-page="decisions" data-next-state={state === 'failed' ? 'failed' : 'loading'}>
        <h1>{copy.heading}</h1>
        <p className="lx-how-intro" role="status" data-next-status="">{state === 'failed' ? nextCopy(locale).how2.configFailed : nextCopy(locale).how.loading}</p>
      </div>
    );
  }
  return (
    <div className="lx-dec" data-next-page="decisions" data-next-state="ready" data-design={design}>
      <h1>{copy.heading}</h1>
      <p className="lx-how-intro">{copy.intro}</p>
      {groups.map((group) => {
        const items = copy.items.filter((item) => item.group === group && (item.only === undefined || item.only === design));
        if (items.length === 0) return null;
        return (
          <section className="lx-dec-group" aria-labelledby={`decisions-${group}`} data-reference-section={group} key={group}>
            <h2 id={`decisions-${group}`}>{copy.groups[group]}</h2>
            <ul className="lx-dec-list">
              {items.map((item) => (
                <li className="lx-dec-item" id={`decision-${item.key}`} data-decision={item.key} key={item.key}>
                  <h3>{item.title}</h3>
                  <p className="lx-dec-gain"><strong>{copy.gain}</strong> {item.gain}</p>
                  <p className="lx-dec-cost"><strong>{copy.cost}</strong> {item.cost}</p>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

