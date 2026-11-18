// The assistant docked beside the page (the owner's brief of 2026-11-15, sections 5 and 6; the mockup's V1Home, V2Read,
// V3Compare and V4Changes): 384 px, in the same place on Home, the text page and Changes, can be hidden to a narrow tab, and
// its conversation survives navigation.
//
// The search-first rule: a question is first given to `search`; when search reads it as an exact reference that opens
// (phase `exact_reference`, outcome `opens`), the text opens there and the dock says "Opened by code, no model call". Only
// otherwise is `ask` called. An answer opens the text page at its article, at the answer's date, highlighted: the dock asks
// a text page standing at that text to open it in place (`lex:open`, `text-address.mjs`), and goes to the text's address
// when no page does. Every word the reader types goes to the API in a request's body (`askV3`), never into an address.
//
// The conversation is kept in this tab's session storage (the driver's binding instruction of 2026-11-15; read and written
// in try/catch, and the dock works without it), and restored after hydration, so that the server's render and the browser's
// first render are the same tree: the dock before any conversation.

import { Fragment, useCallback, useEffect, useRef, useState } from 'react';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { askV3 } from '../scripts/v3-client.mjs';
import { DOCK_EVENT, OPEN_EVENT, textHref } from '../scripts/text-address.mjs';
import {
  answerDate,
  bricksBody,
  bricksOf,
  builtFoot,
  clarifyCandidates,
  dockExamples,
  dockTurnOf,
  fill,
  leadQuote,
  panelCopy,
  quoteNotes,
  searchedWords,
  stageRows,
  stripSentence,
} from '../scripts/assistant-panel.mjs';
import { articleStrip, bundleFacts, firstSentenceOf, numericDate, plannerName, referenceOpening, registryVersionFor } from '../scripts/answer-view.mjs';
import { articleNumberOf } from '../scripts/law-page.mjs';
import { QUOTE_CLOSE, QUOTE_OPEN } from '../scripts/quotation-marks.mjs';
import { EVALUATION_RECORD } from '../scripts/evaluation-record.mjs';
import { REGISTRY_RECORD } from '../scripts/registry-record.mjs';
import { contract } from '../../schemas/v3-platform/envelope-samples.json';

/** The dock's root element's id: the server renders the dock into it and `client-live-dock-entry.jsx` hydrates it. */
export const LIVE_DOCK_ROOT = 'lx-dock-root';

/** The pages that are a text page (the dock there says the article is open on the left, and offers "Find in this law"). */
export const TEXT_PAGES = Object.freeze(['live-text', 'live-dossier', 'live-reading']);

/** The key the conversation is kept under in this tab's session storage, and how many turns are kept. */
export const MEMORY_KEY = 'lex-dock-conversation';
const MEMORY_TURNS = 6;

/** The window event a text page listens to for "Find in this law" (the not-found state's second action). */
export const FIND_IN_LAW_EVENT = 'lex:find-in-law';

/** The header's "Find a text" field, which "Find a text by name" focuses (it never navigates with the reader's words). */
const FIND_FIELD = 'lx-find-q';

/** The language tag of a held language, as an answer carries it. */
const tagOf = (language) => (language === 'eng' ? 'en' : language === 'deu' ? 'de' : language === 'fra' ? 'fr' : undefined);

/** The search's strict lane, as the search page asks it (`SEARCH_MODE` in `live-search.mjs`, which a test holds equal). */
export const DOCK_SEARCH_MODE = 'strict';

/** The search language the dock reads a sentence in first: the interface's. */
const SEARCH_LANGUAGE = Object.freeze({ en: 'eng', fr: 'fra' });

/** The latest test run's average cost of an answer, the cost the dock says as "about" when the trace holds none. */
const AVERAGE_COST = EVALUATION_RECORD.dev.costEur;

/** A copy template filled with values that may be elements (a title marked in its own language), as a list of nodes. */
function fillNodes(template, values) {
  return template.split(/(\{[a-zA-Z]+\})/u).map((part, at) => {
    const name = /^\{([a-zA-Z]+)\}$/u.exec(part)?.[1];
    if (name === undefined) return part;
    if (!Object.hasOwn(values, name)) throw new Error(`no value for ${part}`);
    const value = values[name];
    return typeof value === 'object' && value !== null ? <Fragment key={at}>{value}</Fragment> : String(value);
  });
}

// ---------------------------------------------------------------------------------------------------------------------------
// Asking: the search first, then ask; the facts an answer's card needs after it (the text's title, the article's versions).

/**
 * Whether the sentence is an exact reference that opens a text: `search` asked with it in the interface's language, then
 * once in the language the act is held in when search says it is held in another (`language_not_available`). The opening,
 * or null.
 */
export async function exactReference(question, locale, fetchImpl) {
  const first = SEARCH_LANGUAGE[locale] ?? 'fra';
  const search = (language) => askV3('search', { query: question, language, mode: DOCK_SEARCH_MODE }, { contract, objectType: 'quote', fetchImpl });
  let asked = await search(first);
  if (asked.state === 'refusal' && asked.envelope.refusal?.code === 'language_not_available') {
    const held = asked.envelope.refusal.helpful_payload?.available_languages?.[0];
    if (typeof held === 'string' && held !== first) asked = await search(held);
  }
  return asked.state === 'success' ? referenceOpening(asked.envelope.result.value) : null;
}

/**
 * The text's own title and the article's heading, from `evidence_bundle` at the article and date (`bundleFacts`), in the
 * held language asked; null when it cannot be read (evidence_bundle asks for a date: none, nothing is asked).
 */
export async function articleFacts({ identifier, article, date, language = null }, fetchImpl) {
  if (typeof identifier !== 'string' || typeof date !== 'string') return null;
  const parameters = { identifier, date, ...(typeof article === 'string' ? { article } : {}) };
  const asked = await askV3('evidence_bundle', parameters, { contract, objectType: 'evidence_bundle', fetchImpl });
  return asked.state === 'success' ? bundleFacts(asked.envelope.result.value, article, language) : null;
}

/** The article's versions as the ten-tick strip draws them (`article_history`, one request). */
export async function articleVersions({ identifier, article, language }, fetchImpl) {
  const asked = await askV3('article_history', { identifier, anchor: article }, { contract, objectType: 'provision_history', fetchImpl });
  return asked.state === 'success' ? articleStrip(asked.envelope.result.value, language ?? null) : null;
}

/**
 * One question, as the dock asks it: the search first, and `ask` only when the search opened nothing. The turn it makes:
 * `opened` (an exact reference, no model call), or the kind `dockTurnOf` reads from ask's answer, with the time ask took.
 */
export async function runQuestion(question, locale, fetchImpl, onPhase = () => {}) {
  let opening = null;
  try {
    opening = await exactReference(question, locale, fetchImpl);
  } catch {
    opening = null;
  }
  if (opening !== null) return { kind: 'opened', question, opening };
  onPhase('ask');
  const started = globalThis.performance?.now?.() ?? Date.now();
  let asked;
  try {
    asked = await askV3('ask', { question }, { contract, fetchImpl });
  } catch {
    asked = { state: 'transport_failure', code: 'network_error' };
  }
  const seconds = ((globalThis.performance?.now?.() ?? Date.now()) - started) / 1000;
  return { ...dockTurnOf(asked, locale), question, seconds };
}

/** Where an answer's text opens: its quotation's text, its article, and its date (none when it is the answer's today). */
export function answerOpening(turn, locale) {
  const quote = leadQuote(turn.dossier, locale);
  if (quote === null || typeof quote.work !== 'string') return null;
  const date = answerDate(turn.dossier, quote);
  return Object.freeze({
    identifier: quote.work,
    article: typeof quote.anchor === 'string' ? quote.anchor : null,
    date: date !== null && date !== turn.dossier?.trace?.today ? date : null,
    language: quote.language ?? null,
  });
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

/** The ten-tick strip of an article's versions and its sentence. */
function Strip({ strip, locale }) {
  if (strip === null || strip === undefined || strip.ticks.length === 0) return null;
  const width = strip.ticks.length * 8 - 3;
  return (
    <div className="lx-dock-strip" data-dock-strip="">
      <svg className="lx-dock-ticks" width={width} height="12" viewBox={`0 0 ${width} 12`} aria-hidden="true" focusable="false">
        {strip.ticks.map((tick, at) => (
          <rect key={tick.date} className={tick.changed ? 'lx-dock-tick lx-dock-tick-changed' : 'lx-dock-tick'} x={at * 8} y="0" width="5" height="12" rx="1.5" />
        ))}
      </svg>
      <span className="lx-dock-strip-line">{stripSentence(strip, locale)}</span>
    </div>
  );
}

/** "The whole article is open on the left" on a text page, else a link to the text at that article. */
function WholeArticle({ opening, onText, copy, locale }) {
  if (opening === null) return null;
  if (onText) return copy.dockWholeOpen;
  const href = textHref({ identifier: opening.identifier, date: opening.date, article: opening.article }, locale);
  return href === null ? null : <a className="lx-dock-link" href={href}>{copy.dockWholeLink}</a>;
}

/** The article and the text by name: "Article 44 of the Loi du 27 juillet 1997 sur le contrat d'assurance." */
function Cite({ number, facts, copy }) {
  if (facts?.title) return fillNodes(copy.dockCite, { number, title: <span lang={tagOf(facts.titleLanguage)}>{facts.title}</span> });
  return fill(copy.dockCiteNoTitle, { number });
}

/**
 * A bricks answer's body (V4): the server's sentences and the law's lines, in the answer's own order. The first line quoted
 * is marked and named with its text's title; each other line is named by its article and heading, with a way to the whole
 * article; the further articles are links to the text page.
 */
function BricksBody({ turn, copy, locale, onText }) {
  const body = bricksBody(turn.dossier);
  const lead = body.find((entry) => entry.type === 'quote') ?? null;
  const opening = answerOpening(turn, locale);
  return body.map((entry, at) => {
    if (entry.type === 'line') return <p className="lx-dock-line" key={at}>{entry.text}</p>;
    if (entry.type === 'value') return <p className="lx-dock-value" key={at}>{entry.text}</p>;
    if (entry.type === 'remark') return <p className="lx-dock-remark" key={at}>{entry.text}</p>;
    if (entry.type === 'more') {
      return (
        <p className="lx-dock-more" key={at}>
          {entry.articles.map((article) => {
            const href = textHref({ identifier: article.identifier, date: article.date, article: article.article }, locale);
            const label = fill(copy.dockChoiceBare, { number: articleNumberOf(article.article) });
            return href === null ? <span className="lx-dock-chip" key={article.article}>{label}</span> : <a className="lx-dock-chip" href={href} key={article.article}>{label}</a>;
          })}
        </p>
      );
    }
    const quote = entry.quote;
    const first = entry === lead;
    const number = articleNumberOf(quote.anchor ?? '');
    const href = textHref({ identifier: quote.work, date: entry.date, article: quote.anchor }, locale);
    const lines = entry.lines.map((line, index) => <Fragment key={index}>{index === 0 ? null : <br />}{line}</Fragment>);
    return (
      <div className="lx-dock-extract" key={at} data-dock-extract={quote.anchor}>
        <p className="lx-dock-quote" lang={tagOf(quote.language)} data-text-sha256={quote.article_text_sha256}>
          {QUOTE_OPEN}{first ? <mark className="lx-dock-mark">{lines}</mark> : lines}{QUOTE_CLOSE}
        </p>
        <p className="lx-dock-cite">
          {first
            ? <Cite number={number} facts={turn.facts} copy={copy} />
            : entry.heading === null ? fill(copy.dockCiteNoTitle, { number }) : `${fill(copy.dockChoice, { number, heading: entry.heading })}.`}
          {' '}
          {first ? <WholeArticle opening={opening} onText={onText} copy={copy} locale={locale} /> : href === null ? null : <a className="lx-dock-link" href={href}>{copy.dockWholeLink}</a>}
        </p>
      </div>
    );
  });
}

/** The answer card: the verdict label, its notes, the first sentence of the quote, the article by name, the strip. */
function AnswerCard({ turn, copy, locale, onText, initialNotes = false }) {
  const [notesOpen, setNotesOpen] = useState(initialNotes);
  const quote = leadQuote(turn.dossier, locale);
  if (quote === null) return null;
  const date = answerDate(turn.dossier, quote);
  const today = turn.dossier?.trace?.today ?? null;
  const notes = quoteNotes(turn.dossier, quote);
  const first = firstSentenceOf(quote.text);
  const opening = answerOpening(turn, locale);
  const label = date === null || date === today ? copy.dockVerdictToday : fill(copy.dockVerdictOn, { date: numericDate(date) });
  const notesId = `lx-dock-${turn.id}-notes`;
  return (
    <article className="lx-dock-card lx-dock-answer" data-dock-card="answer" aria-label={label}>
      <div className="lx-dock-card-head">
        <span className="lx-dock-verdict">{label}</span>
        {notes.length === 0 ? null : (
          <button type="button" className="lx-dock-link lx-dock-notes-toggle" aria-expanded={notesOpen} aria-controls={notesId} onClick={() => setNotesOpen(!notesOpen)}>
            {notes.length === 1 ? copy.dockNotesOne : fill(copy.dockNotes, { count: notes.length })}
          </button>
        )}
      </div>
      {notes.length === 0 ? null : (
        <div className="lx-dock-notes" id={notesId} hidden={!notesOpen} role="region" aria-label={copy.dockNotesHeading}>
          <ul lang={turn.dossier.answer_locale}>
            {notes.map((note, at) => <li key={at}>{note}</li>)}
          </ul>
        </div>
      )}
      {bricksOf(turn.dossier) !== null ? <BricksBody turn={turn} copy={copy} locale={locale} onText={onText} /> : (
        <>
          {first === null ? null : (
            <p className="lx-dock-quote" lang={tagOf(quote.language)} data-text-sha256={quote.article_text_sha256}>
              {QUOTE_OPEN}<mark className="lx-dock-mark">{first.sentence}</mark>{first.more ? ' […]' : ''}{QUOTE_CLOSE}
            </p>
          )}
          <p className="lx-dock-cite">
            <Cite number={articleNumberOf(quote.anchor ?? '')} facts={turn.facts} copy={copy} />
            {' '}
            <WholeArticle opening={opening} onText={onText} copy={copy} locale={locale} />
          </p>
        </>
      )}
      <Strip strip={turn.strip} locale={locale} />
    </article>
  );
}

/** "How this answer was built": the five stages, each tagged code or AI model, the time and cost, and the plan as returned. */
function Built({ turn, copy, locale, initialPlan = false }) {
  const [planOpen, setPlanOpen] = useState(initialPlan);
  const dossier = turn.dossier;
  if (dossier === null || dossier === undefined) return null;
  const trace = dossier.trace ?? {};
  const rows = stageRows(dossier, locale);
  const registry = registryVersionFor(REGISTRY_RECORD, trace.planner_id, turn.servingCommit ?? null);
  const headingId = `lx-dock-${turn.id}-built`;
  const planId = `lx-dock-${turn.id}-plan`;
  return (
    <section className="lx-dock-card lx-dock-built" aria-labelledby={headingId} data-dock-built="">
      <div className="lx-dock-built-head">
        <h3 className="lx-dock-built-title" id={headingId}>{copy.dockBuilt}</h3>
        <span className="lx-dock-built-foot" data-dock-cost="">{builtFoot(turn.seconds ?? null, AVERAGE_COST, locale)}</span>
      </div>
      <ol className="lx-dock-rows">
        {rows.map((row) => (
          <li className="lx-dock-row" data-stage={row.key} key={row.key}>
            <span className="lx-dock-row-name">{row.name}</span>
            <span className="lx-dock-row-line">{row.line}</span>
            <span className={row.model ? 'lx-dock-tag lx-dock-tag-model' : 'lx-dock-tag'} data-stage-by={row.model ? 'model' : 'code'}>{row.model ? copy.dockTagModel : copy.dockTagCode}</span>
          </li>
        ))}
      </ol>
      <p className="lx-dock-built-note">
        {bricksOf(dossier) === null ? copy.dockPlanNote : rows.some((row) => row.model) ? copy.dockBricksNote : copy.dockBricksNoModel}{' '}
        <button type="button" className="lx-dock-link" aria-expanded={planOpen} aria-controls={planId} onClick={() => setPlanOpen(!planOpen)} data-dock-see-plan="">
          {planOpen ? copy.dockHidePlan : copy.dockSeePlan}
        </button>
      </p>
      {typeof trace.planner_id === 'string' ? (
        <p className="lx-dock-registry" data-work-planner={trace.planner_id}>
          {fill(copy.workPlanner, { planner: plannerName(trace.planner_id) })}{' · '}
          <a className="lx-dock-link" href={`/${locale}/evidence.html#registry`} data-registry-version={registry?.version ?? ''}>
            {registry === null ? fill(copy.workRegistryNone, { model: REGISTRY_RECORD.model }) : fill(copy.workRegistry, { model: REGISTRY_RECORD.model, version: registry.version })}
          </a>
        </p>
      ) : null}
      <div className="lx-dock-plan" id={planId} hidden={!planOpen} data-dock-plan="">
        {planOpen ? (
          <>
            <h4 className="lx-dock-plan-heading">{copy.dockPlanHeading}</h4>
            <pre className="lx-dock-json" translate="no" data-dock-json="plan">{JSON.stringify(trace.plan ?? null, null, 2)}</pre>
            <h4 className="lx-dock-plan-heading">{copy.dockStepsHeading}</h4>
            <pre className="lx-dock-json" translate="no" data-dock-json="steps">{JSON.stringify(trace.steps ?? null, null, 2)}</pre>
          </>
        ) : null}
      </div>
    </section>
  );
}

/** A candidate of a clarify, by the text's title and the article's heading, never by its address. */
function Choice({ candidate, facts, copy, onChoose }) {
  const number = candidate.article === null ? null : articleNumberOf(candidate.article);
  const heading = facts?.heading ?? null;
  return (
    <li>
      <button type="button" className="lx-dock-choice" onClick={() => onChoose(candidate, facts)}>
        {number === null ? null : (
          <span className="lx-dock-choice-article">
            {heading === null ? fill(copy.dockChoiceBare, { number }) : fillNodes(copy.dockChoice, { number, heading: <span lang={tagOf(facts?.titleLanguage)}>{heading}</span> })}
          </span>
        )}
        {facts?.title ? <span className="lx-dock-choice-title" lang={tagOf(facts.titleLanguage)}>{facts.title}</span> : null}
      </button>
    </li>
  );
}

/** The sentence that says why no answer came: switched off, a question it could not answer, the limit, the language, a failure. */
function whyNone(turn, copy) {
  if (turn.kind === 'off') return copy.dockOff;
  if (turn.kind === 'couldnot') return copy.dockCouldNot;
  if (turn.kind === 'rate') return copy.rateLimited;
  if (turn.kind === 'locale') return turn.sentence ?? copy.dockCouldNot;
  return copy.dockFailure;
}

/** One turn of the conversation as the dock draws it: the question as a bubble, then its card, by kind. */
function Turn({ turn, copy, locale, onText, onAsk, onChoose, onFindByName, onFindInLaw, initialNotes, initialPlan }) {
  const bubbleLang = turn.dossier?.answer_locale ?? undefined;
  const bubble = <p className="lx-dock-bubble" lang={bubbleLang} data-dock-question="">{turn.question}</p>;
  if (turn.kind === 'pending') {
    return (
      <>
        {bubble}
        <div className="lx-dock-card lx-dock-loading" data-dock-card="loading" aria-busy="true">
          <p className="lx-dock-loading-line">{turn.phase === 'ask' ? copy.dockLoadingAsk : copy.dockLoadingSearch}</p>
          <span className="lx-dock-skeleton" aria-hidden="true" />
          <span className="lx-dock-skeleton lx-dock-skeleton-short" aria-hidden="true" />
        </div>
      </>
    );
  }
  if (turn.kind === 'opened') {
    const where = turn.opening;
    const number = where.article === null ? null : articleNumberOf(where.article);
    const title = where.title ? <span lang={tagOf(where.titleLanguage)}>{where.title}</span> : null;
    const date = numericDate(where.date ?? '');
    const line = number === null
      ? (title === null ? null : fillNodes(copy.dockOpenedText, { title, date }))
      : title === null ? fill(copy.dockOpenedLineNoTitle, { number, date }) : fillNodes(copy.dockOpenedLine, { number, title, date });
    return (
      <>
        {bubble}
        <article className="lx-dock-card lx-dock-answer" data-dock-card="opened" aria-label={copy.dockOpenedLabel}>
          <div className="lx-dock-card-head"><span className="lx-dock-verdict">{copy.dockOpenedLabel}</span></div>
          <p className="lx-dock-cite">{line}{' '}<WholeArticle opening={where} onText={onText} copy={copy} locale={locale} /></p>
          <Strip strip={turn.strip} locale={locale} />
        </article>
      </>
    );
  }
  if (turn.kind === 'answer') {
    return (
      <>
        {bubble}
        <AnswerCard turn={turn} copy={copy} locale={locale} onText={onText} initialNotes={initialNotes} />
        <Built turn={turn} copy={copy} locale={locale} initialPlan={initialPlan} />
      </>
    );
  }
  if (turn.kind === 'said') {
    return (
      <>
        {bubble}
        <article className="lx-dock-card lx-dock-answer" data-dock-card="said" aria-label={copy.dockSaidLabel} lang={bubbleLang}>
          <BricksBody turn={turn} copy={copy} locale={locale} onText={onText} />
        </article>
        <Built turn={turn} copy={copy} locale={locale} initialPlan={initialPlan} />
      </>
    );
  }
  if (turn.kind === 'notfound') {
    const words = searchedWords(turn.dossier, turn.question);
    return (
      <>
        {bubble}
        <div className="lx-dock-card" data-dock-card="notfound">
          <p className="lx-dock-lead">{copy.dockNotFound}</p>
          <p className="lx-dock-words">{fillNodes(copy.dockSearched, { words: <span lang={bubbleLang}>{words}</span> })}</p>
          <p className="lx-dock-actions">
            <button type="button" className="lx-dock-action" onClick={onFindByName}>{copy.dockFindByName}</button>
            {onText ? <button type="button" className="lx-dock-action" onClick={() => onFindInLaw(words)}>{copy.dockFindInLaw}</button> : null}
          </p>
        </div>
        <Built turn={turn} copy={copy} locale={locale} initialPlan={initialPlan} />
      </>
    );
  }
  if (turn.kind === 'clarify') {
    const candidates = clarifyCandidates(turn.dossier);
    return (
      <>
        {bubble}
        <div className="lx-dock-card" data-dock-card="clarify">
          <p className="lx-dock-lead">{copy.dockClarify}</p>
          {turn.choices === undefined ? <p className="lx-dock-pending">{copy.dockChoicesPending}</p> : null}
          <ul className="lx-dock-choices">
            {candidates.map((candidate, at) => (
              <Choice candidate={candidate} facts={turn.choices?.[at] ?? null} copy={copy} onChoose={onChoose} key={`${candidate.identifier}|${candidate.article}`} />
            ))}
          </ul>
        </div>
        <Built turn={turn} copy={copy} locale={locale} initialPlan={initialPlan} />
      </>
    );
  }
  if (turn.kind === 'advice') {
    const example = dockExamples(locale)[0];
    return (
      <>
        {bubble}
        <div className="lx-dock-card" data-dock-card="advice">
          <p className="lx-dock-lead">{copy.dockAdvice}</p>
          <p className="lx-dock-words">
            {copy.dockAdviceExample}{' '}
            <button type="button" className="lx-dock-link" lang={example.lang} onClick={() => onAsk(example.question)}>{example.question}</button>
          </p>
        </div>
        <Built turn={turn} copy={copy} locale={locale} initialPlan={initialPlan} />
      </>
    );
  }
  return (
    <>
      {bubble}
      <div className="lx-dock-card" data-dock-card={turn.kind}>
        <p className="lx-dock-lead">{whyNone(turn, copy)}</p>
        {turn.kind === 'failure' || turn.kind === 'rate' ? (
          <p className="lx-dock-actions"><button type="button" className="lx-dock-action" onClick={() => onAsk(turn.question)}>{copy.dockTryAgain}</button></p>
        ) : null}
      </div>
    </>
  );
}

/** The first screen: on Home (or with no conversation yet) the intro, the three examples and what it does and does not do. */
function Intro({ copy, locale, onAsk }) {
  return (
    <>
      <p className="lx-dock-intro">{copy.dockIntro}</p>
      {dockExamples(locale).map((example) => (
        <button type="button" className="lx-dock-example" key={example.question} onClick={() => onAsk(example.question)}>
          <span className="lx-dock-example-question" lang={example.lang}>{example.question}</span>
          <span className="lx-dock-example-caption">{example.caption}</span>
        </button>
      ))}
      <div className="lx-dock-about">
        <p><strong>{copy.dockDoes}</strong> {copy.dockDoesText}</p>
        <p><strong>{copy.dockDoesNot}</strong> {copy.dockDoesNotText}</p>
        <p><a className="lx-dock-link" href={`/${locale}/evidence.html`}>{copy.dockHowLink}</a></p>
      </div>
    </>
  );
}

/** On another page, a conversation kept: the sentence that says so and the last question, a card that reopens its answer. */
function Kept({ turn, copy, locale, onReopen }) {
  const anchor = turn.kind === 'answer' ? leadQuote(turn.dossier, locale)?.anchor ?? null : turn.kind === 'opened' ? turn.opening.article : null;
  const number = anchor === null ? null : articleNumberOf(anchor);
  const caption = number === null ? copy.dockAgain : fill(turn.kind === 'answer' ? copy.dockAgainAnswer : copy.dockAgainOpened, { number });
  return (
    <>
      <p className="lx-dock-intro">{copy.dockStays}</p>
      <button type="button" className="lx-dock-example" onClick={onReopen} data-dock-reopen="">
        <span className="lx-dock-example-question" lang={turn.dossier?.answer_locale ?? undefined}>{turn.question}</span>
        <span className="lx-dock-example-caption">{caption}</span>
      </button>
    </>
  );
}

/**
 * The dock as drawn from its state: folded to its tab, or the header, the conversation (or the first screen) and the field.
 * `shown` is the turn drawn in full, or null for the first screen; `kept` the last turn, drawn on other pages as a card.
 */
export function DockView({
  locale = CHROME_LOCALE, page = null, collapsed = false, shown = null, kept = null, announce = '', fieldRef = null,
  onHide = () => {}, onShow = () => {}, onAsk = () => {}, onReopen = () => {}, onChoose = () => {},
  onFindByName = () => {}, onFindInLaw = () => {}, initialNotes = false, initialPlan = false,
}) {
  const copy = panelCopy(locale);
  const onText = TEXT_PAGES.includes(page);
  const home = page === 'live-home';
  if (collapsed) {
    return (
      <div className="lx-dock-inner lx-dock-folded" data-dock-page={page ?? ''} data-dock-locale={locale}>
        <button type="button" className="lx-dock-tab" onClick={onShow} data-dock-tab="">
          <svg className="lx-dock-tab-icon" width="20" height="20" viewBox="0 0 20 20" aria-hidden="true" focusable="false">
            <path d="M3 4.5h14v9H9l-4 3v-3H3z" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" />
          </svg>
          <span className="lx-dock-tab-label">{copy.dockOpen}</span>
        </button>
      </div>
    );
  }
  const placeholder = shown !== null ? copy.dockPlaceholderAnother : home || kept === null ? copy.dockPlaceholder : copy.dockPlaceholderText;
  return (
    <div className="lx-dock-inner" data-dock-page={page ?? ''} data-dock-locale={locale}>
      <div className="lx-dock-head">
        <h2 className="lx-dock-title">{copy.heading}</h2>
        <details className="lx-dock-notice" data-dock-notices="">
          <summary className="lx-dock-notice-summary">{copy.dockPreview}</summary>
          <div className="lx-dock-notice-body">
            <p>{copy.notice}</p>
            <p>{copy.preview}</p>
            <p>{copy.processor}</p>
          </div>
        </details>
        <button type="button" className="lx-dock-hide" onClick={onHide} data-dock-hide="">{copy.dockHide}</button>
      </div>
      <div className="lx-dock-body" data-dock-body="">
        {shown !== null ? (
          <Turn
            turn={shown} copy={copy} locale={locale} onText={onText} onAsk={onAsk} onChoose={onChoose}
            onFindByName={onFindByName} onFindInLaw={onFindInLaw} initialNotes={initialNotes} initialPlan={initialPlan}
          />
        ) : !home && kept !== null ? (
          <Kept turn={kept} copy={copy} locale={locale} onReopen={onReopen} />
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
          <label className="lx-visually-hidden" htmlFor="lx-dock-q">{copy.dockQuestionLabel}</label>
          <input id="lx-dock-q" type="text" ref={fieldRef} placeholder={placeholder} maxLength={1000} autoComplete="off" />
          <button type="submit" className="lx-dock-submit">{copy.dockAsk}</button>
        </div>
      </form>
    </div>
  );
}

/** A turn's id: unique in this tab's conversation. */
const turnId = () => `t${Date.now().toString(36)}${Math.floor(Math.random() * 1e6).toString(36)}`;

/**
 * The live dock: its conversation, its field, the search-first rule, the text opened at each answer, the tab it folds to.
 * `fetchImpl` is the browser's fetch in a page, injectable in tests.
 */
export function AssistantDock({ locale = CHROME_LOCALE, page = null, fetchImpl = undefined }) {
  const [turns, setTurns] = useState([]);
  const [shownId, setShownId] = useState(null);
  const [pending, setPending] = useState(null);
  const [hidden, setHidden] = useState(false);
  const [folded, setFolded] = useState(false);
  const [restored, setRestored] = useState(false);
  const [announce, setAnnounce] = useState('');
  const field = useRef(null);
  const asking = useRef(false);
  const fetching = useRef(new Set());
  const latest = useRef(turns);
  latest.current = turns;
  const copy = panelCopy(locale);
  const fetchRef = useRef(fetchImpl);
  // Undefined in a page: `askV3` then asks with the browser's own fetch (the one way the site asks the API, `v3-client.mjs`).
  fetchRef.current = fetchImpl;

  // After hydration: the conversation kept in this tab, and whether the reader hid the dock. The turn the dock opened a text
  // for is drawn in full on the page it opened (`arrival`); any other page shows the conversation as kept.
  useEffect(() => {
    const memory = readMemory();
    if (memory !== null) {
      setTurns(memory.turns);
      setHidden(memory.hidden === true);
      if (typeof memory.arrival === 'string' && memory.turns.some((turn) => turn.id === memory.arrival)) setShownId(memory.arrival);
    }
    setRestored(true);
  }, []);

  // Kept on every change; the turn a navigation opens a text for (`leaving`) stays named until the page goes. Nothing is
  // written while there is nothing to keep (no turn, the dock not hidden): a page the reader only reads writes no storage
  // (the journeys hold every page to it).
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

  // The aside the dock stands in (`aside[data-dock]`, the shell's) narrows to the tab while it is folded: the shell's
  // `.lx-dock[data-collapsed="true"]` is 48 px wide; unfolded, the attribute is removed.
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

  const update = useCallback((id, patch) => setTurns((all) => all.map((turn) => (turn.id === id ? { ...turn, ...patch } : turn))), []);

  // The facts a card needs once drawn, each asked once: an answer's or an opening's title and versions, a clarify's titles.
  const shown = turns.find((turn) => turn.id === shownId) ?? null;
  useEffect(() => {
    if (shown === null || fetching.current.has(shown.id)) return;
    const doFetch = fetchRef.current;
    const where = shown.kind === 'answer' ? answerOpening(shown, locale) : shown.kind === 'opened' ? shown.opening : null;
    if (where !== null && where.article !== null && (shown.strip === undefined || (shown.kind === 'answer' && shown.facts === undefined))) {
      fetching.current.add(shown.id);
      const facts = shown.kind === 'answer' && shown.facts === undefined
        ? articleFacts({ identifier: where.identifier, article: where.article, date: answerDate(shown.dossier, leadQuote(shown.dossier, locale)), language: where.language }, doFetch).catch(() => null)
        : Promise.resolve(shown.facts);
      const strip = shown.strip === undefined
        ? articleVersions({ identifier: where.identifier, article: where.article, language: where.language }, doFetch).catch(() => null)
        : Promise.resolve(shown.strip);
      Promise.all([facts, strip]).then(([factsRead, stripRead]) => update(shown.id, shown.kind === 'answer' ? { facts: factsRead, strip: stripRead } : { strip: stripRead }));
    } else if (shown.kind === 'clarify' && shown.choices === undefined) {
      fetching.current.add(shown.id);
      Promise.all(clarifyCandidates(shown.dossier).map((candidate) => articleFacts(candidate, doFetch).catch(() => null)))
        .then((choices) => update(shown.id, { choices }));
    }
  }, [shown, locale, update]);

  // Opens a text at an article: a text page standing at it opens it in place (it cancels `lex:open`); otherwise the page goes
  // to the text's address (a law's address, a date and an article id: never a word the reader typed), the conversation kept
  // with the turn to draw on arrival.
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

  const ask = useCallback(async (raw) => {
    const question = typeof raw === 'string' ? raw.trim() : '';
    if (question.length === 0 || asking.current) return;
    asking.current = true;
    if (field.current !== null) field.current.value = '';
    setPending({ id: 'pending', kind: 'pending', question, phase: 'search' });
    setAnnounce(copy.dockLoadingSearch);
    let turn;
    try {
      turn = await runQuestion(question, locale, fetchRef.current, (phase) => setPending({ id: 'pending', kind: 'pending', question, phase }));
    } catch {
      turn = { kind: 'failure', question };
    }
    const made = { ...turn, id: turnId() };
    const next = [...latest.current, made].slice(-MEMORY_TURNS);
    latest.current = next;
    setTurns(next);
    setShownId(made.id);
    setPending(null);
    asking.current = false;
    setAnnounce(made.kind === 'answer' || made.kind === 'opened' ? copy.announceComplete : '');
    const where = made.kind === 'opened' ? made.opening : made.kind === 'answer' ? answerOpening(made, locale) : null;
    if (where !== null) openText(where, next, made.id);
  }, [locale, copy, openText]);

  // A clarify's choice opens the text at that article (by code: no model call).
  const choose = useCallback((candidate, facts) => {
    const opening = { identifier: candidate.identifier, article: candidate.article, date: candidate.date, title: facts?.title ?? null, titleLanguage: facts?.titleLanguage ?? null, language: null };
    const made = { kind: 'opened', question: shown?.question ?? '', opening, id: turnId() };
    const next = [...latest.current, made].slice(-MEMORY_TURNS);
    latest.current = next;
    setTurns(next);
    setShownId(made.id);
    openText(opening, next, made.id);
  }, [shown, openText]);

  return (
    <div className="lx-dock-frame">
      <DockView
        locale={locale}
        page={page}
        collapsed={collapsed}
        shown={pending ?? shown}
        kept={turns.at(-1) ?? null}
        announce={announce}
        fieldRef={field}
        onHide={() => setHidden(true)}
        onShow={() => {
          setHidden(false);
          setFolded(false);
        }}
        onAsk={ask}
        onReopen={() => setShownId(turns.at(-1)?.id ?? null)}
        onChoose={choose}
        onFindByName={() => document.getElementById(FIND_FIELD)?.focus()}
        onFindInLaw={(words) => window.dispatchEvent(new CustomEvent(FIND_IN_LAW_EVENT, { detail: { words } }))}
      />
    </div>
  );
}

/** The tree the server renders and the browser hydrates: the dock before any conversation, the same on both sides. */
export function liveDockTree(page = null) {
  return <AssistantDock locale={CHROME_LOCALE} page={page} />;
}
