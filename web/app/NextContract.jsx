// One answer in the engine's contract (`lex-next/TRACE-CONTRACT.md`), drawn in the dock's look (`NextDock.jsx` holds the
// shell, the conversation and the asking). In order:
//
//   - the state, as a clear state and never a percentage (K-UI-6): its badge, what it means, the missing piece of a partial
//     answer (K-GEN-6);
//   - what Lex understood: the date the law was read on and where it came from, then each field of the question, each
//     marked as read by code or by a model; the date is a button to read the law on another day;
//   - the texts not held that the question needs, by name (K-SCOPE-5), whatever the state;
//   - a clarification's options, each a button that asks again;
//   - the list's completeness, by code from the structure (K-UI-5);
//   - one card a block: its role, the law's own lines highlighted, the article and its act, the version with its dates, the
//     reason, a link that opens the article in the page (K-UI-1), and a choice to widen the quotation to the alinéa, the
//     article or the section (K-UI-3);
//   - for a "not found" or proposed passages, what Lex checked, in plain words (K-UI-4, K-GEN-5);
//   - the trace, folded (K-UI-2): the funnel's levels with their counts, each row by code or by a model; the model calls,
//     measured; the cost; the versions of the code and the data; the link that shows the answer again from its token.
//
// Every word is `scripts/next-copy.mjs`'s; every value the answer's (`scripts/next-contract.mjs`).

import { useEffect, useState } from 'react';
import { textHref } from '../scripts/text-address.mjs';
import { countedForm, fillNext, nextCopy } from '../scripts/next-copy.mjs';
import {
  CONTRACT_ASSERTED, callRows, readableReason, clarifiedQuestion, clarifyOf, contextAddress, contractBlockView, contractBuild, contractFoot, contractStateOf, dateLine,
  evidenceView, listStatement, notHeldLines, replayToken, replayTokenHref, traceLevels, understoodFields, widened,
} from '../scripts/next-contract.mjs';

/** The language tag the law's own words are marked with on a page written in another language. */
const lawLang = (locale) => (locale === 'fr' ? undefined : 'fr');

/** The tag of a row: code, a model, or both. */
function Tag({ by, copy }) {
  return (
    <span className={by === 'code' ? 'lx-dock-tag' : 'lx-dock-tag lx-dock-tag-model'} data-stage-by={by}>
      {by === 'code' ? copy.tagCode : by === 'model' ? copy.tagModel : copy.tagBoth}
    </span>
  );
}

/** Reads one address of the engine, once, by GET: `{ state, data }`. No word of a question is ever in it. */
export async function readContext(address, fetchImpl) {
  try {
    const response = await (fetchImpl ?? globalThis.fetch)(address, { method: 'GET', headers: { accept: 'application/json' } });
    if (!response.ok) return { state: 'failed', data: null };
    const data = await response.json();
    return data !== null && typeof data === 'object' ? { state: 'ready', data } : { state: 'failed', data: null };
  } catch {
    return { state: 'failed', data: null };
  }
}

/** The state at the head: the badge, the lead, what it names. */
function StateHead({ answer, locale }) {
  const copy = nextCopy(locale).contract;
  const state = contractStateOf(answer, locale);
  if (state === null) return null;
  const asserted = CONTRACT_ASSERTED.includes(state.key) && state.key !== 'partial';
  return (
    <div className={asserted ? 'lx-dock-state' : 'lx-dock-card lx-dock-state'} data-dock-card={asserted ? undefined : 'outcome'} data-dock-state={state.key}>
      <p className="lx-dock-state-badge"><span className="lx-visually-hidden">{copy.stateLabel} </span><span className="lx-dock-chip lx-dock-chip-state" data-state-badge={state.key}>{state.badge}</span></p>
      <p className="lx-dock-lead lx-dock-outcome" data-dock-outcome={state.key}>{state.lead}</p>
      {state.more === null ? null : <p className="lx-dock-words" data-dock-more="">{state.more}</p>}
    </div>
  );
}

/** What Lex understood: the date (a button that opens the field for another day) and each field, marked code or model. */
function Understood({ answer, turn, locale, onAsk, typedDay, initialDateOpen }) {
  const table = nextCopy(locale);
  const copy = table.contract;
  const [open, setOpen] = useState(initialDateOpen);
  const [value, setValue] = useState('');
  const date = dateLine(answer, locale);
  const fields = understoodFields(answer, locale).filter((field) => field.key !== 'date');
  if (date === null && fields.length === 0) return null;
  const formId = `lx-dock-${turn.id}-date`;
  return (
    <div className="lx-dock-understood" data-dock-understood="">
      <p className="lx-dock-more">
        <span className="lx-dock-understood-label">{table.dock.understood}</span>
        {date === null ? null : (
          <button type="button" className="lx-dock-chip lx-dock-chip-date" data-chip="date" aria-expanded={open} aria-controls={formId} title={table.dock.changeDate} onClick={() => setOpen(!open)}>{date}</button>
        )}
        {fields.map((field) => (
          <span className="lx-dock-chip" key={field.key} data-chip={field.key} data-chip-by={field.by ?? undefined} title={field.by === null ? undefined : copy.fieldBy[field.by]}>
            {field.value === null ? field.name : fillNext(copy.fieldItem, { name: field.name, value: field.value })}
          </span>
        ))}
      </p>
      {date === null ? null : (
        <form
          className="lx-dock-date"
          id={formId}
          hidden={!open}
          onSubmit={(event) => {
            event.preventDefault();
            const day = typedDay(value);
            if (day !== null) onAsk(turn.question, day);
          }}
        >
          <label className="lx-dock-date-label" htmlFor={`${formId}-field`}>{table.dock.dateLabel}</label>
          <span className="lx-dock-date-row">
            <input id={`${formId}-field`} type="text" inputMode="numeric" autoComplete="off" placeholder={table.dock.datePlaceholder} value={value} onChange={(event) => setValue(event.target.value)} />
            <button type="submit" className="lx-dock-action">{table.dock.dateSubmit}</button>
          </span>
        </form>
      )}
    </div>
  );
}

/** The texts not held that the answer names. */
function NotHeld({ answer, locale }) {
  const copy = nextCopy(locale).contract;
  const lines = notHeldLines(answer, locale);
  if (lines.length === 0) return null;
  return (
    <div className="lx-dock-card lx-dock-notheld" data-dock-card="not-held">
      <p className="lx-dock-lead">{copy.notHeldHeading}</p>
      <ul className="lx-dock-step-list">{lines.map((line) => <li key={line.name} data-not-held="">{line.line}</li>)}</ul>
    </div>
  );
}

/** A clarification: the question, then each option a button that asks again. */
function Clarify({ answer, turn, locale, onAsk }) {
  const copy = nextCopy(locale).contract;
  const clarify = clarifyOf(answer);
  if (clarify === null) return null;
  return (
    <div className="lx-dock-card" data-dock-card="clarify">
      {clarify.question === null ? null : <p className="lx-dock-lead" data-clarify-question="">{clarify.question}</p>}
      {clarify.options.length === 0 ? null : (
        <>
          <p className="lx-dock-words">{copy.clarifyPick}</p>
          <p className="lx-dock-actions lx-dock-options">
            {clarify.options.map((option) => (
              <button type="button" className="lx-dock-action" key={option} data-clarify-option="" onClick={() => onAsk(clarifiedQuestion(turn.question, option))}>{option}</button>
            ))}
          </p>
        </>
      )}
    </div>
  );
}

/** The list's completeness, said by code. */
function ListStatement({ answer, locale }) {
  const statement = listStatement(answer, locale);
  if (statement === null) return null;
  return (
    <p className={statement.complete ? 'lx-dock-remark' : 'lx-dock-remark lx-dock-caution'} data-dock-list={statement.complete ? 'complete' : 'incomplete'}>
      {statement.main}{statement.stated === null ? null : <> {statement.stated}</>}
    </p>
  );
}

const WIDEN_LEVELS = Object.freeze(['lines', 'alinea', 'article', 'section']);

/** One line drawn, marked when the answer quotes it. */
function Line({ line, first, last, table }) {
  const words = line.quoted ? <mark className="lx-dock-mark">{line.text}</mark> : line.text;
  return <p className={line.quoted ? 'lx-dock-quote' : 'lx-dock-quote lx-dock-context'} data-line={line.n ?? undefined}>{first ? table.quoteOpen : null}{words}{last ? table.quoteClose : null}</p>;
}

/** One block: its role, the lines it quotes (widened on demand), the article and its act, the version and its dates. */
export function ContractBlock({ block, state, locale, onOpen, fetchImpl, answer = null, initialLevel = 'lines', initialContext = null }) {
  const table = nextCopy(locale);
  const copy = table.contract;
  const view = contractBlockView(block, locale, state);
  const [level, setLevel] = useState(initialLevel);
  const [context, setContext] = useState(initialContext === null ? { state: 'idle', data: null } : { state: 'ready', data: initialContext });
  const address = contextAddress(view.articlePk);
  // The article's name in the page, when the block does not carry it: read from the context, as widening reads it.
  const anchor = view.anchor ?? (typeof context.data?.article?.anchor === 'string' ? context.data.article.anchor : null);
  const work = view.identifier;
  useEffect(() => {
    if (address === null || context.state !== 'idle' || (level === 'lines' && view.anchor !== null)) return undefined;
    let live = true;
    setContext({ state: 'loading', data: null });
    readContext(address, fetchImpl).then((answer) => { if (live) setContext(answer); });
    return () => { live = false; };
  }, [address, level]); // eslint-disable-line react-hooks/exhaustive-deps
  const opening = work === null || anchor === null ? null : { identifier: work, article: anchor, date: view.date };
  const href = opening === null ? null : textHref(opening, locale);
  const lang = lawLang(locale);
  const quotedNumbers = view.lines.filter((line) => !line.gap).map((line) => line.n);
  const wide = level === 'lines' || context.state !== 'ready' ? null : widened(context.data, level, quotedNumbers);
  const quoteLines = view.lines.filter((line) => !line.gap);
  const marked = view.role === 'rule' || view.role === 'exception';
  return (
    <article className="lx-dock-card lx-dock-answer lx-dock-block" data-dock-card="block" data-role={view.role ?? ''} aria-label={view.label}>
      <div className="lx-dock-card-head"><span className="lx-dock-verdict">{view.label}</span></div>
      {view.why === null ? null : <p className="lx-dock-remark" lang={lang} data-dock-why="">{readableReason(view.why, answer ?? {})}</p>}
      {wide === null ? (
        quoteLines.length === 0 ? null : (
          <div className="lx-dock-extract" data-dock-extract={anchor ?? ''} lang={lang}>
            {view.lines.map((line, at) => {
              if (line.gap) return <p className="lx-dock-remark" key={`gap-${at}`} aria-hidden="true">{table.dock.gap}</p>;
              return <Line key={`line-${at}`} line={{ ...line, quoted: marked }} first={line === quoteLines[0]} last={line === quoteLines.at(-1)} table={table} />;
            })}
          </div>
        )
      ) : wide.kind === 'section' ? (
        <div className="lx-dock-extract lx-dock-wide" data-dock-wide="section" lang={lang}>
          {wide.label === null ? null : <p className="lx-dock-remark" data-dock-section="">{fillNext(copy.sectionOf, { label: wide.label })}</p>}
          {wide.articles.map((entry) => (
            <div key={entry.key} data-dock-wide-article={entry.anchor ?? ''}>
              <p className="lx-dock-cite">{entry.heading === null ? entry.label : `${entry.label}, ${entry.heading}`}</p>
              {entry.lines.map((line, at) => <Line key={`w-${at}`} line={line} first={false} last={false} table={table} />)}
            </div>
          ))}
          {wide.cut ? <p className="lx-dock-remark">{copy.widen.cut}</p> : null}
        </div>
      ) : (
        <div className="lx-dock-extract lx-dock-wide" data-dock-wide={wide.kind} lang={lang}>
          {wide.lines.map((line, at) => <Line key={`w-${at}`} line={line} first={at === 0} last={at === wide.lines.length - 1} table={table} />)}
        </div>
      )}
      {level !== 'lines' && context.state === 'loading' ? <p className="lx-dock-remark" role="status">{copy.widen.loading}</p> : null}
      {level !== 'lines' && context.state === 'failed' ? <p className="lx-dock-remark" role="status">{copy.widen.failed}</p> : null}
      {address === null ? null : (
        <p className="lx-dock-widen" role="group" aria-label={copy.widen.label} data-dock-widen="">
          <span className="lx-dock-widen-label">{copy.widen.label}</span>
          {WIDEN_LEVELS.map((key) => (
            <button type="button" key={key} className="lx-dock-widen-choice" aria-pressed={level === key} data-widen={key} onClick={() => setLevel(key)}>{copy.widen[key]}</button>
          ))}
        </p>
      )}
      {view.cite === null && view.title === null ? null : (
        <p className="lx-dock-cite" lang={lang}>{[view.cite, view.title].filter((part) => part !== null).join(' ')}</p>
      )}
      <p className="lx-dock-remark" data-dock-version="">
        {view.version}
        {view.official !== null || href !== null ? ' ' : null}
        {view.official === null ? null : <a className="lx-dock-link" href={view.official} rel="noopener noreferrer" data-dock-official="">{table.dock.official}</a>}
        {view.official !== null && href !== null ? table.dock.linkSeparator : null}
        {href === null ? null : (
          <a
            className="lx-dock-link"
            href={href}
            data-dock-open=""
            onClick={(event) => {
              if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
              event.preventDefault();
              onOpen(opening);
            }}
          >
            {table.dock.openArticle}
          </a>
        )}
      </p>
    </article>
  );
}

/** What Lex checked, for a "not found" or proposed passages: what it holds, the words, the sweep, the closest passage. */
function Evidence({ answer, locale }) {
  const copy = nextCopy(locale).contract.evidence;
  const view = evidenceView(answer, locale);
  if (view === null || CONTRACT_ASSERTED.includes(answer.state)) return null;
  const lang = lawLang(locale);
  return (
    <div className="lx-dock-card" data-dock-card="searched" data-dock-evidence="">
      <p className="lx-dock-lead">{copy.heading}</p>
      {view.coverage === null ? null : <p className="lx-dock-words" data-evidence="coverage"><strong>{copy.coverage}</strong>{' '}<span lang={lang}>{view.coverage}</span></p>}
      {view.terms.length === 0 ? null : (
        <table className="lx-dock-table" data-evidence="terms">
          <thead><tr><th scope="col">{copy.terms.term}</th><th scope="col">{copy.terms.variants}</th><th scope="col">{copy.terms.hits}</th></tr></thead>
          <tbody>
            {view.terms.map((term) => (
              <tr key={term.term} data-none={term.hits === 0 ? '' : undefined}>
                <th scope="row" lang={lang}>{term.term}</th>
                <td lang={lang}>{term.variants.join(nextCopy(locale).listSeparator)}</td>
                <td>{term.said}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {view.swept === null ? null : <p className="lx-dock-words" data-evidence="sweep">{view.swept}</p>}
      {view.closest === null ? null : <p className="lx-dock-words" data-evidence="closest">{view.closest}</p>}
    </div>
  );
}

/** The trace, folded: the levels, the model calls, the foot line, the versions, the replay link. */
function Trace({ answer, turn, locale }) {
  const table = nextCopy(locale);
  const dock = table.dock;
  const copy = table.contract.trace;
  const levels = traceLevels(answer, locale);
  const calls = callRows(answer, locale);
  const foot = contractFoot(answer, locale);
  const build = contractBuild(answer, locale);
  const replay = replayTokenHref(replayToken(answer), locale);
  if (levels.length === 0 && calls.length === 0 && foot === null) return null;
  const count = Array.isArray(answer?.trace?.calls) ? answer.trace.calls.length : null;
  return (
    <details className="lx-dock-card lx-dock-built lx-dock-trace" data-dock-built="" data-dock-trace="">
      <summary className="lx-dock-built-head">
        <span className="lx-dock-built-title">{copy.summary}</span>
        {count === null ? null : <span className="lx-dock-built-facts"><span className="lx-dock-built-by" data-dock-by={count > 0 ? 'model' : 'code'}>{countedForm(dock.builtBy, count, locale)}</span></span>}
      </summary>
      {levels.map((level) => (
        <section className="lx-dock-level" key={level.key} data-level={level.level}>
          <h4 className="lx-dock-level-head"><span className="lx-dock-row-name">{level.name}</span><Tag by={level.by} copy={dock} /></h4>
          <ol className="lx-dock-rows">
            {level.rows.map((row, at) => (
              <li className="lx-dock-row lx-dock-step" key={`${row.key}-${at}`} data-row={row.key}>
                {row.text.length === 0 ? null : <span className="lx-dock-row-line">{row.text}</span>}
                {row.items.length === 0 ? null : (
                  <ul className="lx-dock-step-list">
                    {row.items.map((item, i) => (
                      <li key={`${item.title}-${i}`}>
                        <span lang={lawLang(locale)}>{item.title}</span>
                        {item.tally ? <span className="lx-dock-row-time"> · {item.tally}</span> : null}
                        {item.reason ? <span className="lx-dock-why"> — {item.reason}</span> : null}
                        {' '}<Tag by={item.by} copy={dock} />
                      </li>
                    ))}
                  </ul>
                )}
                {row.table === null ? null : (
                  <div className="lx-dock-table-scroll">
                    <table className="lx-dock-table" data-trace-table={level.level}>
                      <thead><tr>{row.table.columns.map((column) => <th scope="col" key={column}>{column}</th>)}</tr></thead>
                      <tbody>
                        {row.table.rows.map((entry) => (
                          <tr key={entry.key} data-meaning-only={entry.note === null ? undefined : ''}>
                            <th scope="row" lang={lawLang(locale)}>{entry.head}{entry.note === null ? null : <span className="lx-dock-caution-note"> ({entry.note})</span>}</th>
                            {entry.cells.map((cell, i) => <td key={i}>{cell}</td>)}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </li>
            ))}
          </ol>
        </section>
      ))}
      {calls.length === 0 ? null : (
        <section className="lx-dock-level" data-level="calls">
          <h4 className="lx-dock-level-head"><span className="lx-dock-row-name">{copy.callsHeading}</span><Tag by="model" copy={dock} /></h4>
          <div className="lx-dock-table-scroll">
            <table className="lx-dock-table" data-trace-table="calls">
              <thead><tr>{['step', 'model', 'time', 'tokens'].map((key) => <th scope="col" key={key}>{copy.callColumns[key]}</th>)}</tr></thead>
              <tbody>{calls.map((call) => <tr key={call.key}><th scope="row"><code>{call.step}</code></th><td><code>{call.model}</code></td><td>{call.time}</td><td>{call.tokens}</td></tr>)}</tbody>
            </table>
          </div>
        </section>
      )}
      {foot === null ? null : <p className="lx-dock-built-note" data-dock-cost="">{foot}</p>}
      {build === null ? null : <p className="lx-dock-registry" data-dock-build="">{build}</p>}
      {replay === null ? null : <p className="lx-dock-registry"><a className="lx-dock-link" href={replay} data-dock-replay="">{copy.replay}</a></p>}
    </details>
  );
}

/**
 * One turn whose answer is in the contract: the state, what Lex understood, the texts not held, the clarification, the list's
 * completeness, the blocks (under the evidence for a "not found"), and the trace.
 */
export function ContractAnswer({ turn, locale, onAsk, onOpen, typedDay, fetchImpl, initialDateOpen = false }) {
  const table = nextCopy(locale);
  const answer = turn.result;
  const blocks = Array.isArray(answer.blocks) ? answer.blocks.filter((block) => block !== null && typeof block === 'object') : [];
  const cards = blocks.map((block, at) => (
    <ContractBlock block={block} answer={answer} state={answer.state} locale={locale} onOpen={onOpen} fetchImpl={fetchImpl} key={`${block.article_pk ?? 'block'}-${at}`} />
  ));
  // Nothing found: the state, then what Lex checked, and only then the closest passages, under a heading that says they do not answer.
  const notFound = answer.state === 'not_found';
  return (
    <>
      <StateHead answer={answer} locale={locale} />
      <Understood answer={answer} turn={turn} locale={locale} onAsk={onAsk} typedDay={typedDay} initialDateOpen={initialDateOpen} />
      <NotHeld answer={answer} locale={locale} />
      <Clarify answer={answer} turn={turn} locale={locale} onAsk={onAsk} />
      <ListStatement answer={answer} locale={locale} />
      {notFound ? null : cards}
      <Evidence answer={answer} locale={locale} />
      {notFound && cards.length > 0 ? (
        <>
          <h3 className="lx-dock-passages-heading" data-dock-passages="">{table.dock.passagesHeading}</h3>
          {cards}
        </>
      ) : null}
      <Trace answer={answer} turn={turn} locale={locale} />
    </>
  );
}
