// The assistant docked beside the page, over the new answer engine (the side build). The shell is the current dock's
// (`AssistantDock.jsx`, kept in place and no longer wired): the same aside, the same head with its notice and "Hide", the
// same body, bubble, cards, loading skeleton and field, the same classes. Only what is shown inside changes:
//
//   - one request, `POST /api/next/ask` with the JSON body `{ question, date }` (a plain fetch on the page's own origin;
//     the reader's words travel in the body, never in an address, as the site's privacy notice says), and its JSON drawn
//     as it is: what Lex understood (chips), the outcome in one sentence, one card per block in the order given (the law's
//     own lines, the article, the act, its place in the table of contents, the version, the official text and a link that
//     opens the article in the page), the notes, then how the answer was built (the funnel, one row a step, the foot line);
//     under the outcome of an asserted answer, how many of the three searches found the article; in each card, since when
//     the wording is unchanged; and when nothing is asserted, what Lex searched (the words and how many articles hold each),
//     which, when nothing was found, stands before the closest passages (under their own heading: they do not answer);
//   - an asserted answer opens its first article in the main part of the page, as the current dock does, but only from
//     the home page or a text page: a text page standing at that text opens it in place (`lex:open`), otherwise the page
//     goes to the text's address (a law's name, a date, an article: no word of the question); on any other page (search,
//     changes) the dock does not navigate by itself, the card's link does when the reader follows it. The lines quoted are
//     said to the page (`lex:mark`), which marks them;
//   - the date chip lets the reader choose another day to read the law on: the question is asked again with `date=`;
//   - a page opened with `#q=...&on=YYYY-MM-DD` asks that question for that day, once, and the fragment is then cleared (the
//     "replay" link of every answer: the question stands after "#", which a browser never sends).
//
// The conversation is kept in this tab's session storage (read and written in try/catch; the dock works without it) and
// restored after hydration, so that the server's render and the browser's first render are the same tree.
//
// Every word comes from `scripts/next-copy.mjs`; every value from the answer (`scripts/next-view.mjs`).

import { useCallback, useEffect, useRef, useState } from 'react';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { DOCK_EVENT, MARK_EVENT, OPEN_EVENT, textHref } from '../scripts/text-address.mjs';
import { todayInLuxembourg } from '../scripts/temporal.mjs';
import { nextCopy } from '../scripts/next-copy.mjs';
import {
  answerOpening, askRequest, blockView, buildLine, builtBy, confidenceOf, coverageLines, euNotHeldLines, footLine, funnelRows, isAnswer, isIsoDate, marksOf,
  noteLines, outcomeOf, referenceDay, replayHref, replayOf, searchedView, stepRows, understoodChips,
} from '../scripts/next-view.mjs';
import { replacePageAddress } from './page-address.mjs';
import { ContractAnswer } from './NextContract.jsx';
import { contractMarks, contractOpening, isContractAnswer, isFullContract, legacyAnswer, replayRequest, replayTokenOf } from '../scripts/next-contract.mjs';

/** Whether a value is an answer of the engine, in either shape: the contract's (`state`) or the earlier one (`outcome`). */
export const isAnyAnswer = (value) => isContractAnswer(value) || isAnswer(value);

/** Where an asserted answer opens in the page, in either shape. */
export const openingOf = (result, locale) => (isFullContract(result) ? contractOpening(result, locale) : answerOpening(isContractAnswer(result) ? legacyAnswer(result) : result, locale));

/** An answer as the earlier view reads it: the earlier pipeline's own shape, its contract's fields set aside. */
const earlierShape = (result) => (isContractAnswer(result) ? legacyAnswer(result) : result);

/** The dock's root element's id: the server renders the dock into it and `client-live-dock-entry.jsx` hydrates it. */
export const LIVE_DOCK_ROOT = 'lx-dock-root';

/** The key the conversation is kept under in this tab's session storage, and how many turns are kept. */
export const MEMORY_KEY = 'lex-next-dock-conversation';
const MEMORY_TURNS = 6;

/** The pages from which an asserted answer opens its article by itself: the home page, and a text page (in place there). */
export const OPENING_PAGES = Object.freeze(['live-home', 'live-text', 'live-dossier', 'live-reading']);

/** The language tag the law's own words are marked with on a page written in another language. */
const lawLang = (locale) => (locale === 'fr' ? undefined : 'fr');

/** A dd/mm/yyyy the reader typed, as yyyy-mm-dd, or null. */
export function typedDay(value) {
  const match = /^\s*([0-9]{1,2})[/.-]([0-9]{1,2})[/.-]([0-9]{4})\s*$/u.exec(value ?? '');
  if (match === null) return null;
  const day = `${match[3]}-${match[2].padStart(2, '0')}-${match[1].padStart(2, '0')}`;
  const [y, m, d] = day.split('-').map(Number);
  const check = new Date(Date.UTC(y, m - 1, d));
  return check.getUTCFullYear() === y && check.getUTCMonth() === m - 1 && check.getUTCDate() === d ? day : null;
}

// ---------------------------------------------------------------------------------------------------------------------------
// Asking.

/**
 * One question, asked of the engine: the turn it makes. `done` with the answer, `rate` when the server says too many
 * questions were asked (429), `failure` for anything else. `day` is the day the reader chose to read the law on, or null.
 */
export async function runQuestion(question, day, fetchImpl) {
  const doFetch = fetchImpl ?? globalThis.fetch;
  try {
    const request = askRequest(question, day);
    const response = await doFetch(request.address, request.init);
    if (response.status === 429) return { kind: 'rate', question, day };
    if (!response.ok) return { kind: 'failure', question, day };
    const result = await response.json();
    return isAnyAnswer(result) ? { kind: 'done', question, day, result } : { kind: 'failure', question, day };
  } catch {
    return { kind: 'failure', question, day };
  }
}

/**
 * An answer shown again from its token (`#r=...`): the server re-renders it without asking any model. `done` with the
 * answer, `failure` otherwise; the question is the answer's own.
 */
export async function runReplay(token, fetchImpl) {
  const doFetch = fetchImpl ?? globalThis.fetch;
  try {
    const request = replayRequest(token);
    const response = await doFetch(request.address, request.init);
    if (!response.ok) return { kind: 'failure', question: '', day: null, replay: true };
    const result = await response.json();
    const question = typeof result?.question === 'string' ? result.question : typeof result?.trace?.question?.text === 'string' ? result.trace.question.text : '';
    return isAnyAnswer(result) ? { kind: 'done', question, day: null, result, replay: true } : { kind: 'failure', question, day: null, replay: true };
  } catch {
    return { kind: 'failure', question: '', day: null, replay: true };
  }
}

// ---------------------------------------------------------------------------------------------------------------------------
// The conversation, kept in this tab's session storage (try/catch: the dock works without it).

function readMemory() {
  try {
    const raw = window.sessionStorage.getItem(MEMORY_KEY);
    const memory = raw === null ? null : JSON.parse(raw);
    return memory !== null && typeof memory === 'object' && Array.isArray(memory.turns) ? memory : null;
  } catch {
    return null;
  }
}

function writeMemory(memory) {
  try {
    window.sessionStorage.setItem(MEMORY_KEY, JSON.stringify({ ...memory, turns: memory.turns.slice(-MEMORY_TURNS) }));
  } catch {
    // No storage (private mode, a full quota, storage switched off): the conversation lasts as long as the page.
  }
}

// ---------------------------------------------------------------------------------------------------------------------------
// Drawing.

/** "Lex understood": the chips; the date's is a button that opens the field for another day. */
function Understood({ turn, copy, locale, onAsk, initialDateOpen = false }) {
  const [open, setOpen] = useState(initialDateOpen);
  const field = useRef(null);
  const chips = understoodChips(turn.result, locale, turn.day ?? null);
  if (chips.length === 0) return null;
  const formId = `lx-dock-${turn.id}-date`;
  return (
    <div className="lx-dock-understood" data-dock-understood="">
      <p className="lx-dock-more">
        <span className="lx-dock-understood-label">{copy.understood}</span>
        {chips.map((chip) => (chip.key === 'date' ? (
          <button type="button" className="lx-dock-chip lx-dock-chip-date" key={chip.key} data-chip={chip.key} aria-expanded={open} aria-controls={formId} title={copy.changeDate} onClick={() => setOpen(!open)}>
            {chip.text}
          </button>
        ) : <span className="lx-dock-chip" key={chip.key} data-chip={chip.key}>{chip.text}</span>))}
      </p>
      <form
        className="lx-dock-date"
        id={formId}
        hidden={!open}
        onSubmit={(event) => {
          event.preventDefault();
          const day = typedDay(field.current?.value ?? '');
          if (day !== null) onAsk(turn.question, day);
        }}
      >
        <label className="lx-dock-date-label" htmlFor={`${formId}-field`}>{copy.dateLabel}</label>
        <span className="lx-dock-date-row">
          <input id={`${formId}-field`} type="text" inputMode="numeric" autoComplete="off" placeholder={copy.datePlaceholder} ref={field} />
          <button type="submit" className="lx-dock-action">{copy.dateSubmit}</button>
        </span>
      </form>
    </div>
  );
}

/** The outcome: one line above the cards for an answer, a card of its own for anything else. */
function Outcome({ result, locale }) {
  const outcome = outcomeOf(result, locale);
  if (outcome === null) return null;
  if (!outcome.card) {
    const confidence = confidenceOf(result, locale);
    return (
      <>
        <p className="lx-dock-lead lx-dock-outcome" data-dock-outcome={outcome.key}>{outcome.lead}</p>
        {confidence === null ? null : (
          <p className={confidence.caution ? 'lx-dock-remark lx-dock-caution' : 'lx-dock-remark'} data-dock-confidence={confidence.key}>{confidence.text}</p>
        )}
      </>
    );
  }
  return (
    <div className="lx-dock-card" data-dock-card="outcome" data-dock-outcome={outcome.key}>
      <p className="lx-dock-lead">{outcome.lead}</p>
      {outcome.more === null ? null : <p className="lx-dock-words">{outcome.more}</p>}
    </div>
  );
}

/** One block, in the answer card's look: the role as its label, why it is there, the law's lines, and where they stand. */
function Block({ block, outcome, copy, locale, onOpen }) {
  const view = blockView(block, locale, outcome);
  const table = nextCopy(locale);
  const quoted = view.lines.filter((line) => !line.gap);
  const href = view.opening === null ? null : textHref(view.opening, locale);
  const lang = lawLang(locale);
  return (
    <article className="lx-dock-card lx-dock-answer lx-dock-block" data-dock-card="block" data-role={view.role ?? ''} aria-label={view.label}>
      <div className="lx-dock-card-head"><span className="lx-dock-verdict">{view.label}</span></div>
      {view.why === null ? null : <p className="lx-dock-remark" lang={lang} data-dock-why="">{view.why}</p>}
      {quoted.length === 0 ? null : (
        <div className="lx-dock-extract" data-dock-extract={block.anchor ?? ''} lang={lang}>
          {view.lines.map((line, at) => {
            if (line.gap) return <p className="lx-dock-remark" key={`gap-${at}`} aria-hidden="true">{copy.gap}</p>;
            const first = line === quoted[0];
            const last = line === quoted.at(-1);
            const words = view.role === 'rule' ? <mark className="lx-dock-mark">{line.text}</mark> : line.text;
            return <p className="lx-dock-quote" key={`line-${at}`} data-line={line.n ?? undefined}>{first ? table.quoteOpen : null}{words}{last ? table.quoteClose : null}</p>;
          })}
        </div>
      )}
      {view.cite === null && view.title === null ? null : (
        <p className="lx-dock-cite" lang={lang}>
          {view.cite}{view.cite !== null && view.title !== null ? ' ' : null}{view.title}
        </p>
      )}
      {view.path === null ? null : <p className="lx-dock-remark" lang={lang} data-dock-path="">{view.path}</p>}
      {view.wording === null ? null : <p className="lx-dock-remark" data-dock-wording="">{view.wording}</p>}
      {view.repeal === null ? null : <p className="lx-dock-remark lx-dock-caution" data-dock-repeal="">{view.repeal}</p>}
      <p className="lx-dock-remark" data-dock-version="">
        {view.version}
        {view.version !== null && (view.official !== null || href !== null) ? ' ' : null}
        {view.official === null ? null : <a className="lx-dock-link" href={view.official} rel="noopener noreferrer" data-dock-official="">{copy.official}</a>}
        {view.official !== null && href !== null ? copy.linkSeparator : null}
        {href === null ? null : (
          <a
            className="lx-dock-link"
            href={href}
            data-dock-open=""
            onClick={(event) => {
              if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
              event.preventDefault();
              onOpen(view.opening);
            }}
          >
            {copy.openArticle}
          </a>
        )}
      </p>
    </article>
  );
}

/** "What Lex searched", when nothing is asserted: in the look of the not-found card, with the words and the articles that hold each. */
function Searched({ result, copy, locale }) {
  const view = searchedView(result, locale);
  if (view === null) return null;
  const lang = lawLang(locale);
  return (
    <div className="lx-dock-card" data-dock-card="searched">
      <p className="lx-dock-lead">{copy.searched.heading}</p>
      {view.scope === null ? null : <p className="lx-dock-words" data-searched="scope">{view.scope}</p>}
      {view.outlines.length === 0 ? null : (
        <p className="lx-dock-words" data-searched="outlines">{copy.searched.outlines}{' '}<span lang={lang}>{view.outlines.join(nextCopy(locale).clauseSeparator)}</span></p>
      )}
      {view.words.length === 0 ? null : (
        <table className="lx-dock-table" data-searched="words">
          <thead><tr><th scope="col">{copy.searched.columns.word}</th><th scope="col">{copy.searched.columns.articles}</th></tr></thead>
          <tbody>
            {view.words.map((row) => <tr key={row.word} data-none={row.count === 0 ? '' : undefined}><th scope="row" lang={lang}>{row.word}</th><td>{row.said}</td></tr>)}
          </tbody>
        </table>
      )}
      {view.closest === null ? null : <p className="lx-dock-words" data-searched="closest">{view.closest}</p>}
    </div>
  );
}

/** "How this answer was built": the funnel as rows with counts, one row a step tagged code or model, and the foot line. */
function Built({ turn, copy, locale, today }) {
  const result = turn.result;
  const steps = stepRows(result, locale, turn.day ?? null);
  if (steps.length === 0) return null;
  const funnel = funnelRows(result, locale);
  const by = builtBy(result, locale);
  const foot = footLine(result, locale);
  const replay = replayHref(turn.question, referenceDay(result, turn.day ?? null, today), locale);
  const headingId = `lx-dock-${turn.id}-built`;
  const modelCalls = Number(result?.trace?.model_calls) > 0;
  return (
    <section className="lx-dock-card lx-dock-built" aria-labelledby={headingId} data-dock-built="">
      <div className="lx-dock-built-head">
        <h3 className="lx-dock-built-title" id={headingId}>{copy.built}</h3>
        {by === null ? null : (
          <span className="lx-dock-built-facts"><span className="lx-dock-built-by" data-dock-by={modelCalls ? 'model' : 'code'}>{by}</span></span>
        )}
      </div>
      {funnel.length === 0 ? null : (
        <ol className="lx-dock-rows lx-dock-funnel" aria-label={copy.funnelLabel} data-dock-funnel="">
          {funnel.map((row) => (
            <li className="lx-dock-row" data-funnel={row.key} key={row.key}>
              <span className="lx-dock-row-name lx-dock-row-count">{row.count}</span>
              <span className="lx-dock-row-line">{row.label}</span>
            </li>
          ))}
        </ol>
      )}
      {/* A step on two lines: its name, then its tag and its time on the right; under them, what was done, across the card. */}
      <ol className="lx-dock-rows" aria-label={copy.stepsLabel} data-dock-steps="">
        {steps.map((row) => (
          <li className="lx-dock-row lx-dock-step" data-stage={row.step} key={row.key}>
            <span className="lx-dock-step-head">
              <span className="lx-dock-row-name">{row.name}</span>
              <span className={row.who === 'code' ? 'lx-dock-tag' : 'lx-dock-tag lx-dock-tag-model'} data-stage-by={row.who}>
                {row.who === 'code' ? copy.tagCode : row.who === 'model' ? copy.tagModel : copy.tagBoth}
              </span>
              <span className="lx-dock-row-time">{row.time}</span>
            </span>
            {row.line.length === 0 ? null : <span className="lx-dock-row-line">{row.line}</span>}
            {row.items.length === 0 ? null : (
              <ul className="lx-dock-step-list">
                {row.items.map((item, at) => (
                  <li key={`${item.title}-${at}`}>
                    <span lang={lawLang(locale)}>{item.title}</span>
                    {copy.summaries.outlineSeparator}
                    {item.parts === null ? copy.summaries.outlineNoPart : <span lang={lawLang(locale)}>{item.parts}</span>}
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
      </ol>
      {foot === null ? null : <p className="lx-dock-built-note" data-dock-cost="">{foot}</p>}
      {buildLine(result, locale) === null ? null : <p className="lx-dock-registry" data-dock-build="">{buildLine(result, locale)}</p>}
      {replay === null ? null : <p className="lx-dock-registry"><a className="lx-dock-link" href={replay} data-dock-replay="">{copy.replay}</a></p>}
    </section>
  );
}

/** One turn of the conversation: the question as a bubble, then what came back. */
function Turn({ turn, copy, locale, today, onAsk, onOpen, initialDateOpen, fetchImpl }) {
  const bubble = turn.question ? <p className="lx-dock-bubble" data-dock-question="">{turn.question}</p> : null;
  if (turn.kind === 'pending') {
    return (
      <>
        {bubble}
        <div className="lx-dock-card lx-dock-loading" data-dock-card="loading" aria-busy="true">
          <p className="lx-dock-loading-line">{copy.loading}</p>
          <span className="lx-dock-skeleton" aria-hidden="true" />
          <span className="lx-dock-skeleton lx-dock-skeleton-short" aria-hidden="true" />
        </div>
      </>
    );
  }
  if (turn.kind !== 'done' || !isAnyAnswer(turn.result)) {
    return (
      <>
        {bubble}
        <div className="lx-dock-card" data-dock-card={turn.kind === 'rate' ? 'rate' : 'failure'}>
          <p className="lx-dock-lead">{turn.kind === 'rate' ? copy.rateLimited : turn.replay ? nextCopy(locale).contract.trace.replayFailed : copy.failure}</p>
          {turn.question ? <p className="lx-dock-actions"><button type="button" className="lx-dock-action" onClick={() => onAsk(turn.question, turn.day ?? null)}>{copy.tryAgain}</button></p> : null}
        </div>
      </>
    );
  }
  // The funnel's answers (the contract's whole trace) are drawn by the contract's view; the earlier pipeline's by its own.
  if (isFullContract(turn.result)) {
    return (
      <>
        {bubble}
        <ContractAnswer turn={turn} locale={locale} onAsk={onAsk} onOpen={onOpen} typedDay={typedDay} fetchImpl={fetchImpl} initialDateOpen={initialDateOpen} />
      </>
    );
  }
  const result = earlierShape(turn.result);
  const blocks = Array.isArray(result.blocks) ? result.blocks : [];
  const cards = blocks.map((block, at) => <Block block={block} outcome={result.outcome} copy={copy} locale={locale} onOpen={onOpen} key={`${block?.article_pk ?? 'block'}-${at}`} />);
  // Nothing found: the sentence, then what Lex searched, and only then the closest passages, under a heading that says
  // they do not answer. Never a passage before the evidence.
  const notFound = result.outcome === 'not_found';
  return (
    <>
      {bubble}
      <Understood turn={turn} copy={copy} locale={locale} onAsk={onAsk} initialDateOpen={initialDateOpen} />
      <Outcome result={result} locale={locale} />
      {/* A European text the question names and Lex does not hold: the engine's sentence, in the question's language. */}
      {euNotHeldLines(result).map((line) => (
        <p className="lx-dock-remark lx-dock-caution" data-dock-eu-not-held="" key={line.key} lang={line.lang === locale ? undefined : line.lang}>{line.text}</p>
      ))}
      {notFound ? null : cards}
      <Searched result={result} copy={copy} locale={locale} />
      {/* Nothing found: what Lex does not hold, and the first date held of each code, so that "not found" is never read as "not in the law". */}
      {notFound ? coverageLines(result.coverage, locale).map((line) => (
        <p className="lx-dock-remark" data-dock-coverage={line.key} key={line.key}>{line.text}</p>
      )) : null}
      {notFound && cards.length > 0 ? (
        <>
          <h3 className="lx-dock-passages-heading" data-dock-passages="">{copy.passagesHeading}</h3>
          {cards}
        </>
      ) : null}
      {noteLines(result, locale).map((note) => <p className="lx-dock-remark lx-dock-note" data-dock-note={note.key} key={note.key}>{note.text}</p>)}
      <Built turn={{ ...turn, result }} copy={copy} locale={locale} today={today} />
    </>
  );
}

/** The first screen: the intro, the examples, what the assistant does and does not, and the two pages that say how. */
function Intro({ copy, locale, onAsk }) {
  return (
    <>
      <p className="lx-dock-intro">{copy.intro}</p>
      {copy.examples.map((example) => (
        <button type="button" className="lx-dock-example" key={example.question} onClick={() => onAsk(example.question)}>
          <span className="lx-dock-example-question" lang={example.lang === locale ? undefined : example.lang}>{example.question}</span>
          <span className="lx-dock-example-caption">{example.caption}</span>
        </button>
      ))}
      <div className="lx-dock-about">
        <p><strong>{copy.does}</strong> {copy.doesText}</p>
        <p><strong>{copy.doesNot}</strong> {copy.doesNotText}</p>
        <p>
          <a className="lx-dock-link" href={`/${locale}/evidence.html`}>{copy.howLink}</a>
          {copy.linkSeparator}
          <a className="lx-dock-link" href={`/${locale}/results.html`}>{copy.resultsLink}</a>
        </p>
      </div>
    </>
  );
}

/** On another page, a conversation kept: the sentence that says so and the last question, a card that reopens its answer. */
function Kept({ turn, copy, onReopen }) {
  return (
    <>
      <p className="lx-dock-intro">{copy.stays}</p>
      <button type="button" className="lx-dock-example" onClick={onReopen} data-dock-reopen="">
        <span className="lx-dock-example-question">{turn.question}</span>
        <span className="lx-dock-example-caption">{copy.again}</span>
      </button>
    </>
  );
}

/**
 * The dock as drawn from its state: folded to its tab, or the head, the conversation (or the first screen) and the field.
 * `shown` is the turn drawn in full, or null for the first screen; `kept` the last turn, drawn on other pages as a card.
 */
export function NextDockView({
  locale = CHROME_LOCALE, page = null, collapsed = false, shown = null, kept = null, announce = '', fieldRef = null, today = null,
  onHide = () => {}, onShow = () => {}, onAsk = () => {}, onReopen = () => {}, onOpen = () => {}, initialDateOpen = false, fetchImpl = undefined,
}) {
  const copy = nextCopy(locale).dock;
  const home = page === 'live-home';
  if (collapsed) {
    return (
      <div className="lx-dock-inner lx-dock-folded" data-dock-page={page ?? ''} data-dock-locale={locale}>
        <button type="button" className="lx-dock-tab" onClick={onShow} data-dock-tab="">
          <svg className="lx-dock-tab-icon" width="20" height="20" viewBox="0 0 20 20" aria-hidden="true" focusable="false">
            <path d="M3 4.5h14v9H9l-4 3v-3H3z" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" />
          </svg>
          <span className="lx-dock-tab-label">{copy.open}</span>
        </button>
      </div>
    );
  }
  return (
    <div className="lx-dock-inner" data-dock-page={page ?? ''} data-dock-locale={locale} data-dock-engine="next">
      <div className="lx-dock-head">
        <h2 className="lx-dock-title">{copy.heading}</h2>
        <details className="lx-dock-notice" data-dock-notices="">
          <summary className="lx-dock-notice-summary">{copy.noticeSummary}</summary>
          <div className="lx-dock-notice-body">
            {copy.noticeLines.map((line) => <p key={line}>{line}</p>)}
          </div>
        </details>
        <button type="button" className="lx-dock-hide" onClick={onHide} data-dock-hide="">{copy.hide}</button>
      </div>
      <div className="lx-dock-body" data-dock-body="">
        {shown !== null ? (
          <Turn turn={shown} copy={copy} locale={locale} today={today} onAsk={onAsk} onOpen={onOpen} initialDateOpen={initialDateOpen} fetchImpl={fetchImpl} />
        ) : !home && kept !== null ? (
          <Kept turn={kept} copy={copy} onReopen={onReopen} />
        ) : (
          <Intro copy={copy} locale={locale} onAsk={onAsk} />
        )}
      </div>
      {/* Read to a screen reader, not shown: what the dock just did, said once, politely. */}
      <div className="lx-visually-hidden" role="status" aria-live="polite" aria-atomic="true">{announce}</div>
      <form
        className="lx-dock-ask"
        onSubmit={(event) => {
          event.preventDefault();
          onAsk(fieldRef?.current?.value ?? '');
        }}
      >
        <div className="lx-dock-field">
          <label className="lx-visually-hidden" htmlFor="lx-dock-q">{copy.questionLabel}</label>
          <input id="lx-dock-q" type="text" ref={fieldRef} placeholder={shown !== null ? copy.placeholderAnother : copy.placeholder} maxLength={600} autoComplete="off" />
          <button type="submit" className="lx-dock-submit">{copy.ask}</button>
        </div>
      </form>
    </div>
  );
}

/** A turn's id: unique in this tab's conversation. */
const turnId = () => `t${Date.now().toString(36)}${Math.floor(Math.random() * 1e6).toString(36)}`;

/**
 * The live dock: its conversation, its field, the article opened at each asserted answer, the tab it folds to.
 * `fetchImpl` is the browser's fetch in a page, injectable in tests.
 */
export function NextDock({ locale = CHROME_LOCALE, page = null, fetchImpl = undefined }) {
  const [turns, setTurns] = useState([]);
  const [shownId, setShownId] = useState(null);
  const [pending, setPending] = useState(null);
  const [hidden, setHidden] = useState(false);
  const [folded, setFolded] = useState(false);
  const [restored, setRestored] = useState(false);
  const [announce, setAnnounce] = useState('');
  const [today, setToday] = useState(null);
  const field = useRef(null);
  const asking = useRef(false);
  const latest = useRef(turns);
  latest.current = turns;
  const copy = nextCopy(locale).dock;
  const fetchRef = useRef(fetchImpl);
  fetchRef.current = fetchImpl;

  // After hydration: today in Luxembourg, the conversation kept in this tab, and whether the reader hid the dock. The turn
  // the dock opened a text for is drawn in full on the page it opened (`arrival`); any other page shows it as kept.
  useEffect(() => {
    setToday(todayInLuxembourg());
    const memory = readMemory();
    if (memory !== null) {
      setTurns(memory.turns);
      setHidden(memory.hidden === true);
      if (typeof memory.arrival === 'string' && memory.turns.some((turn) => turn.id === memory.arrival)) setShownId(memory.arrival);
    }
    setRestored(true);
  }, []);

  // Kept on every change; the turn a navigation opens a text for (`leaving`) stays named until the page goes. Nothing is
  // written while there is nothing to keep (no turn, the dock not hidden).
  const leaving = useRef(null);
  useEffect(() => {
    if (restored && (turns.length > 0 || hidden)) writeMemory({ turns, hidden, arrival: leaving.current });
  }, [restored, turns, hidden]);

  // The text page folds the dock to its tab in compare mode and opens it again after (`lex:dock`).
  useEffect(() => {
    const listener = (event) => setFolded(event?.detail?.collapsed === true);
    window.addEventListener(DOCK_EVENT, listener);
    return () => window.removeEventListener(DOCK_EVENT, listener);
  }, []);

  // The aside the dock stands in narrows to the tab while it is folded (`.lx-dock[data-collapsed="true"]`, the shell's).
  const collapsed = hidden || folded;
  useEffect(() => {
    const aside = document.querySelector('aside[data-dock]');
    if (aside === null) return;
    if (collapsed) aside.setAttribute('data-collapsed', 'true');
    else aside.removeAttribute('data-collapsed');
  }, [collapsed]);

  // Keyboard focus follows the fold: to the tab when the dock folds, to the field when it opens.
  const wasCollapsed = useRef(collapsed);
  useEffect(() => {
    if (wasCollapsed.current === collapsed) return;
    wasCollapsed.current = collapsed;
    const root = document.getElementById(LIVE_DOCK_ROOT);
    (collapsed ? root?.querySelector('[data-dock-tab]') : field.current)?.focus?.();
  }, [collapsed]);

  const shown = turns.find((turn) => turn.id === shownId) ?? null;

  // The lines the answer in sight quotes are said to the page (`lex:mark`): a text page marks those paragraphs. Said again
  // once the page's own script has had time to listen, as the current dock does.
  useEffect(() => {
    if (typeof window === 'undefined') return undefined;
    const contract = shown?.kind === 'done' && isFullContract(shown.result);
    const earlier = shown?.kind === 'done' && !contract ? earlierShape(shown.result) : null;
    const first = shown?.kind !== 'done' ? null : contract ? contractOpening({ ...shown.result, state: 'answer' }, locale) : blockView(earlier?.blocks?.[0] ?? null, locale).opening;
    const detail = first === null ? { identifier: null, marks: [] } : { identifier: first.identifier, marks: contract ? contractMarks(shown.result, first.identifier, locale) : marksOf(earlier, first.identifier) };
    const say = () => window.dispatchEvent(new CustomEvent(MARK_EVENT, { detail }));
    say();
    const later = [setTimeout(say, 800), setTimeout(say, 2500)];
    return () => later.forEach(clearTimeout);
  }, [shown?.id, shown?.kind, locale]); // eslint-disable-line react-hooks/exhaustive-deps

  // Opens a text at an article: a text page standing at it opens it in place (it cancels `lex:open`); otherwise the page
  // goes to the text's address, the conversation kept with the turn to draw on arrival.
  const openText = useCallback((where, nextTurns, id) => {
    const detail = { identifier: where.identifier, date: where.date ?? null, article: where.article ?? null, highlight: true };
    let handled = false;
    try {
      handled = !window.dispatchEvent(new CustomEvent(OPEN_EVENT, { detail, cancelable: true }));
    } catch {
      handled = false;
    }
    if (handled) return;
    const href = textHref(detail, locale);
    if (href === null) return;
    leaving.current = id;
    writeMemory({ turns: nextTurns, hidden, arrival: id });
    window.location.assign(href);
  }, [hidden, locale]);

  const ask = useCallback(async (raw, day = null) => {
    const question = typeof raw === 'string' ? raw.trim() : '';
    if (question.length < 2 || asking.current) return;
    asking.current = true;
    if (field.current !== null) field.current.value = '';
    setPending({ id: 'pending', kind: 'pending', question });
    setAnnounce(copy.loading);
    const turn = await runQuestion(question, isIsoDate(day) ? day : null, fetchRef.current);
    const made = { ...turn, id: turnId() };
    const next = [...latest.current, made].slice(-MEMORY_TURNS);
    latest.current = next;
    setTurns(next);
    setShownId(made.id);
    setPending(null);
    asking.current = false;
    setAnnounce(made.kind === 'done' ? copy.announceDone : '');
    // Only from the home page or a text page: on the others (search, changes) the reader stays where they are, with the link.
    const where = made.kind === 'done' && OPENING_PAGES.includes(page) ? openingOf(made.result, locale) : null;
    if (where !== null) openText(where, next, made.id);
  }, [locale, copy, openText, page]);

  // An answer shown again from its token: drawn as a turn of its own, nothing asked of any model.
  const show = useCallback(async (token) => {
    if (asking.current) return;
    asking.current = true;
    setPending({ id: 'pending', kind: 'pending', question: '' });
    setAnnounce(copy.loading);
    const turn = await runReplay(token, fetchRef.current);
    const made = { ...turn, id: turnId() };
    const next = [...latest.current, made].slice(-MEMORY_TURNS);
    latest.current = next;
    setTurns(next);
    setShownId(made.id);
    setPending(null);
    asking.current = false;
    setAnnounce(made.kind === 'done' ? copy.announceDone : '');
  }, [copy]);

  // On a phone the frame stacks: the aside says when it holds an answer, so that the answer is drawn before the page.
  const holding = (pending ?? shown) !== null;
  useEffect(() => {
    const aside = document.querySelector('aside[data-dock]');
    if (aside === null) return;
    if (holding) aside.setAttribute('data-dock-answer', 'true');
    else aside.removeAttribute('data-dock-answer');
  }, [holding]);

  // A page opened with `#q=...&on=YYYY-MM-DD` asks that question for that day, once: the fragment is then cleared (and the
  // language links written again without it), so that reloading the page does not ask (and pay for) the question again.
  const replayed = useRef(false);
  useEffect(() => {
    if (!restored || replayed.current) return;
    replayed.current = true;
    const token = replayTokenOf(window.location.hash);
    const replay = token === null ? replayOf(window.location.hash) : null;
    if (token === null && replay === null) return;
    // The fragment is cleared, so that reloading the page does not ask (or show) again; the address stays as it is if it cannot be.
    try {
      replacePageAddress(window, { hash: '' });
    } catch {
      // The question is asked, or the answer shown, all the same.
    }
    if (token !== null) show(token);
    else ask(replay.question, replay.day);
  }, [restored, ask, show]);

  return (
    <div className="lx-dock-frame">
      <NextDockView
        locale={locale}
        page={page}
        collapsed={collapsed}
        shown={pending ?? shown}
        kept={turns.at(-1) ?? null}
        announce={announce}
        fieldRef={field}
        today={today}
        onHide={() => setHidden(true)}
        onShow={() => {
          setHidden(false);
          setFolded(false);
        }}
        onAsk={ask}
        onReopen={() => setShownId(turns.at(-1)?.id ?? null)}
        onOpen={(where) => openText(where, latest.current, shown?.id ?? null)}
        fetchImpl={fetchImpl}
      />
    </div>
  );
}

/** The tree the server renders and the browser hydrates: the dock before any conversation, the same on both sides. */
export function liveDockTree(page = null) {
  return <NextDock locale={CHROME_LOCALE} page={page} />;
}
