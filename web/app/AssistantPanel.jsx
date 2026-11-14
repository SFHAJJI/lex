// The Assistant panel, as React (R47, R52, R55, R45(c)-(e); docs/ANSWER-DOSSIER-1.md).
//
// Reached by one page, the assistant's (`live-assistant-page.jsx`), which the server renders and serves only while it reports
// the assistant switched on; no built page carries the panel, and no other bundle carries its copy (the web suite checks both).
// The panel draws an answer_dossier/1 and adds nothing to it: every sentence of an answer is text the server filled from its
// closed template set, a verbatim quotation or a span of the question; the panel's own words come from its copy table, and
// every rule lives in the shared modules.
//
// The answer screen (the owner's order of 2026-11-14, evening): the question on one line; the one sentence of the quotation
// that answers, in the law's type, the words that answer marked (`answer-view.mjs`: a span of the quotation's own characters);
// one line that cites it (the article, the act's own title, the date the law was read at); three ways on (the full article,
// the act's versions on its law page, the official source); and under a rule, the work, closed: four boxes (the text read,
// the plan, the search, the quotation's check), each saying whether the model or code did it, then the time, the cost and the
// raw plan. A question no article answers says so in one sentence, and offers the search on its words.
//
// Accessibility, as R45(d) rules it: one polite live region makes two announcements, one when the verdict is known and one
// when the answer is complete, never an alert (it is read, not shown); focus stays where the reader put it, and removing a
// context chip returns it to the question field. Follow-up context is removable chips (R45(e)), and no earlier question is
// ever sent again.

import { useEffect, useRef, useState } from 'react';
import {
  ASSISTANT_PANEL_MARKER,
  QUOTE_CHECK_TEMPLATES,
  actTitlesOf,
  announcements,
  askOutcome,
  askRequest,
  chipLabel,
  fill,
  groupsForLocale,
  lawPageHref,
  panelCopy,
  quoteGroups,
  quoteParagraphs,
  quoteWork,
  readAskFragment,
  validateDossier,
} from '../scripts/assistant-panel.mjs';
import { articleHeadingOf, articleNumberOf } from '../scripts/law-page.mjs';
import { answerSentence, dateFor, quoteVerifies, searchCount, searchStepOf, stepsRun } from '../scripts/answer-view.mjs';
import { searchFragment } from '../scripts/search-address.mjs';
import { EVALUATION_RECORD } from '../scripts/evaluation-record.mjs';

/** The language tag of a held language, as a quotation carries it. */
const tagOf = (language) => (language === 'eng' ? 'en' : language === 'deu' ? 'de' : 'fr');

/**
 * One visible text of an answer: a quotation in the held text's own language, set in the paragraphs its own numbering shows
 * (`quoteParagraphs`: no word added, dropped or changed), or a sentence in the answer's.
 */
function Text({ block }) {
  if (block.kind === 'quote') {
    // The quotation carries its held expression's language, so a screen reader reads it in that voice.
    const lang = tagOf(block.language);
    return (
      <blockquote className="assistant-quote" lang={lang} data-text-sha256={block.article_text_sha256}>
        {quoteParagraphs(block.text).map((paragraph, at) => <p key={at}>{paragraph}</p>)}
      </blockquote>
    );
  }
  if (block.kind === 'question_span') return <q className="assistant-question-span">{block.text}</q>;
  return <p className="assistant-claim" data-template={block.template_id}>{block.text}</p>;
}

/** One block of a part: a visible text, or a typed block with its sentence. */
function Block({ block }) {
  if (block.kind === 'claim' || block.kind === 'quote' || block.kind === 'question_span') return <Text block={block} />;
  if (block.kind === 'handoff') {
    return (
      <p className="assistant-handoff" data-role={block.role}>
        <a href={block.link}>{block.claim.text}</a>
      </p>
    );
  }
  return (
    <p className={`assistant-${block.kind}`} data-kind={block.kind}>
      {block.claim.text}
    </p>
  );
}

/** A claim of a quotation that is for checking it: its digest, its permalink, its official source. */
const isCheck = (block) => block.kind === 'claim' && QUOTE_CHECK_TEMPLATES.includes(block.template_id);

/** A number of seconds or euros as the page's language writes it (1,6 in French, 1.6 in English). */
const decimal = (value, locale, digits) => value.toLocaleString(locale === 'fr' ? 'fr-FR' : 'en-GB', { minimumFractionDigits: digits, maximumFractionDigits: digits });

/** The date the law was read at, from a quotation's heading claim (its `date` fact), or null. */
function readAt(heading) {
  const fact = heading?.facts?.find((candidate) => candidate?.name === 'date' && candidate?.kind === 'calendar_date');
  return typeof fact?.value === 'string' ? fact.value : null;
}

/**
 * The sentence that answers, in the law's type and language, the words that answer marked: a span of the quotation's own
 * characters (`answerSentence`), or nothing when the quotation has none.
 */
function AnswerSentence({ quote, question }) {
  const sentence = answerSentence(quote.text, question ?? '');
  if (sentence === null) return null;
  return (
    <p className="answer-sentence" lang={tagOf(quote.language)} data-answer-sentence="">
      {sentence.parts.map((part, at) => (part.mark ? <mark key={at}>{part.text}</mark> : <span key={at}>{part.text}</span>))}
    </p>
  );
}

/**
 * One quotation as the answer: the sentence that answers (the first card only), one line that cites it, three ways on, and
 * the full article one action away: the quotation in its paragraphs, the version it is from, its other claims and the claims
 * for checking it. Every block of the group is drawn.
 */
function QuoteGroup({ group, copy, locale, titles = {}, question = null, lead = false }) {
  const [open, setOpen] = useState(false);
  const checks = group.blocks.filter(isCheck);
  const quote = group.blocks.find((block) => block.kind === 'quote') ?? null;
  if (quote !== null) {
    const heading = group.blocks.find((block) => block.kind === 'claim' && block.template_id === 'law.quote_heading') ?? null;
    const rest = group.blocks.filter((block) => !isCheck(block) && block !== quote && block !== heading);
    const where = quoteWork(quote);
    const number = articleNumberOf(quote.anchor);
    const articleHeading = articleHeadingOf(quote.text);
    const act = where === null ? null : titles[where.address] ?? null;
    const at = readAt(heading);
    const regionId = `answer-article-${quote.quote_id ?? quote.anchor}`;
    return (
      <div className="assistant-quote-group assistant-card" data-quote-language={group.language ?? ''}>
        {lead ? <AnswerSentence quote={quote} question={question} /> : null}
        <p className="answer-cite" data-anchor={quote.anchor}>
          <span className="assistant-article">{fill(copy.articleLine, { number })}</span>
          {articleHeading === '' ? null : <>{' · '}<span lang={tagOf(quote.language)}>{articleHeading}</span></>}
          {act === null ? null : <>{' · '}<span className="assistant-act" lang={tagOf(act.language)}>{act.title}</span></>}
          {at === null ? null : <>{' · '}<span data-read-at={at}>{fill(copy.asOf, { date: dateFor(at, locale) })}</span></>}
        </p>
        <p className="answer-links">
          <button type="button" className="link-button" aria-expanded={open} aria-controls={regionId} onClick={() => setOpen(!open)} data-action="full-article">
            {copy.fullArticle}
          </button>
          {where === null ? null : (
            <a href={lawPageHref(where, quote.anchor, 'article', locale)} data-action="versions">
              {act?.versions > 0 ? fill(act.versions === 1 ? copy.versionsOne : copy.versionsSince, { count: act.versions, year: act.since }) : copy.lawPage}
            </a>
          )}
          {typeof quote.official_source === 'string' ? <a href={quote.official_source} rel="external" data-action="official-source">{copy.officialSource}</a> : null}
        </p>
        {locale === 'en' && quote.language === 'fra' ? <p className="answer-note">{copy.originalFrench}</p> : null}
        <div className="answer-article" id={regionId} hidden={!open} data-answer-article="">
          <Text block={quote} />
          {typeof quote.from === 'string' ? <p className="assistant-version-badge">{fill(copy.versionBadge, { date: dateFor(quote.from, locale) })}</p> : null}
          {rest.map((block, index) => <Block block={block} key={index} />)}
          <div className="assistant-checks">
            {heading === null ? null : <p className="assistant-quote-heading" data-template={heading.template_id}>{heading.text}</p>}
            {checks.map((block, index) => <Text block={block} key={index} />)}
          </div>
        </div>
      </div>
    );
  }
  return (
    <div className="assistant-quote-group" data-quote-language={group.language ?? ''}>
      {group.blocks.map((block, index) =>
        block.kind === 'claim' && block.template_id === 'law.quote_heading'
          ? <p className="assistant-quote-heading" data-template={block.template_id} key={index}>{block.text}</p>
          : <Block block={block} key={index} />)}
    </div>
  );
}

/**
 * One part of an answer: what comes before its first quotation, then its quotations in the interface's language first (the
 * others, the same article in the other held language, one action away). Every block of the part is drawn; a part that
 * quotes nothing has its sentences under the work (`NoQuote`).
 */
function Part({ part, locale, copy, titles, question, first }) {
  const { lead, groups } = quoteGroups(part.blocks);
  const { shown, other } = groupsForLocale(groups, locale);
  return (
    <section className="assistant-part" data-verdict={part.verdict}>
      {lead.map((block, at) => <Block block={block} key={at} />)}
      {shown.map((group, at) => <QuoteGroup group={group} copy={copy} locale={locale} titles={titles} question={question} lead={first && at === 0} key={at} />)}
      {other.length === 0 ? null : (
        <details className="assistant-other-languages">
          <summary>{copy.otherLanguages}</summary>
          {other.map((group, at) => <QuoteGroup group={group} copy={copy} locale={locale} titles={titles} key={at} />)}
        </details>
      )}
    </section>
  );
}

/** The plan in plain words: the steps the answer ran, as its trace records them (`stepsRun`). */
function planWords(trace, copy, locale) {
  const language = (code) => ({ fra: copy.languageFra, eng: copy.languageEng, deu: copy.languageDeu })[code] ?? code;
  const said = stepsRun(trace).map(({ operation, parameters }) => {
    if (operation === 'search') return fill(copy.planSearch, { query: parameters.query, language: language(parameters.language) });
    if (typeof parameters.article === 'string') return fill(copy.planArticle, { number: articleNumberOf(parameters.article) });
    if (operation === 'dossier') return copy.planDossier;
    return fill(copy.planOther, { operation });
  });
  const dated = (trace?.steps ?? []).map((step) => step?.parameters?.date).find((date) => typeof date === 'string' && /^\d{4}-\d{2}-\d{2}$/u.test(date));
  const steps = said.length === 0 ? copy.planNothing : said.join(copy.planThen);
  return dated === undefined ? steps : `${fill(copy.planDate, { date: dateFor(dated, locale) })}${steps}`;
}

/**
 * The work behind an answer, closed (the owner's order of 2026-11-14, evening): four boxes in a row, each one line of what
 * happened for this question and one grey line saying whether the model or code did it; under them, the time it took, the
 * estimated cost of a question (the latest test run's), and the raw plan: the plan's sentences and the trace (R45(c), R32).
 */
function TheWork({ view, copy, locale, work, children = null }) {
  const quotes = view.parts.flatMap((part) => part.blocks.filter((block) => block.kind === 'quote'));
  const first = quotes[0] ?? null;
  const document = first === null ? copy.documentNone : fill(copy.documentLine, { date: dateFor(first.from ?? '', locale) });
  const search = work?.search ?? null;
  const kept = quotes.length;
  // What the search did: how many articles the plan's search matched and how many the answer kept; an article read by its
  // number when the plan ran no search; nothing when it ran no operation at all (a question declined before any).
  const retrieval = searchStepOf(view.trace) !== null
    ? search === null
      ? copy.retrievalPending
      : fill(search.more ? copy.retrievalMore : copy.retrievalLine, { matched: search.matched, kept })
    : stepsRun(view.trace).length === 0
      ? copy.retrievalNone
      : fill(copy.retrievalDirect, { kept });
  const check = kept === 0
    ? copy.checkNone
    : work?.verified === null || work?.verified === undefined
      ? copy.checkPending
      : fill(copy.checkLine, { verified: work.verified.ok, quotes: work.verified.checked });
  const boxes = [
    ['document', copy.workDocument, document, copy.workCode],
    ['question', copy.workQuestion, planWords(view.trace, copy, locale), copy.workModel],
    ['retrieval', copy.workRetrieval, retrieval, copy.workCode],
    ['answer', copy.workAnswer, check, copy.workCode],
  ];
  return (
    <details className="answer-work" data-answer-work="">
      <summary>{copy.showWork}</summary>
      <ol className="work-boxes">
        {boxes.map(([key, title, line, who]) => (
          <li key={key} data-work={key} className={key === 'question' ? 'work-model' : undefined}>
            <strong>{title}</strong>
            <span>{line}</span>
            <small>{who}</small>
          </li>
        ))}
      </ol>
      <p className="work-foot">
        {work?.seconds === undefined || work?.seconds === null ? null : <span data-work-time="">{fill(copy.workTime, { seconds: decimal(work.seconds, locale, 1) })}</span>}
        <span data-work-cost="">{fill(copy.workCost, { cost: decimal(EVALUATION_RECORD.dev.costEur, locale, 3) })}</span>
      </p>
      {children}
      <details className="assistant-how">
        <summary>{copy.rawPlan}</summary>
        <ol className="assistant-plan">
          {view.plan.map((line, at) => (
            <li key={at}>
              <Text block={line} />
            </li>
          ))}
        </ol>
        <pre className="assistant-trace">{JSON.stringify(view.trace, null, 2)}</pre>
      </details>
    </details>
  );
}

/** Whether an answer quotes the law. */
const quotesLaw = (view) => view.parts.some((part) => part.blocks.some((block) => block.kind === 'quote'));

/** The search page with the words of the answer's search (or the question's), in the page's language, or null. */
function searchHref(view, question, locale) {
  const step = searchStepOf(view.trace);
  try {
    return `/${locale}/search.html${searchFragment({ query: step?.query ?? question ?? '', language: step?.language ?? (locale === 'en' ? 'eng' : 'fra') }, { work: false })}`;
  } catch {
    return null;
  }
}

/**
 * The panel: the question on one line, the kept context chips, and, once an answer arrives, the answer screen; then R44's
 * notice, with the preview line while the people's reviews are not done, and the processor sentence (the owner's order of
 * 2026-10-28; shown only where the panel is, which is only when the assistant is switched on). `phase` is 'idle', 'verdict'
 * or 'complete', and says which announcement the polite region holds. Nothing here asks the server; the page sends
 * `askRequest(question)` and hands back what it learnt (`question`, `work`).
 */
export function AssistantPanel({ locale, dossier = null, phase = 'idle', chips = [], preview = true, message = null, prefill = null, titles = {}, question = null, work = null, onSubmit = () => {}, onRemoveChip = () => {} }) {
  const copy = panelCopy(locale);
  const field = useRef(null);
  // A question the page's address asks (`#q=…`) is shown in the field it is asked from.
  useEffect(() => {
    if (prefill !== null && field.current !== null) field.current.value = prefill;
  }, [prefill]);
  const view = dossier === null ? null : validateDossier(dossier);
  const [verdictLine, completeLine] = view === null ? ['', ''] : announcements(view, locale);
  // A presentation result is said once in the same polite region, never as an alert, and is no verdict (R45(a)).
  const announced = message !== null ? message : phase === 'verdict' ? verdictLine : phase === 'complete' ? completeLine : '';
  const quoted = view !== null && quotesLaw(view);
  const search = view === null || quoted ? null : searchHref(view, question, locale);
  const pointed = view !== null && !quoted && view.verdict === 'point';
  return (
    <section className="assistant-panel" aria-labelledby="assistant-heading" {...{ [ASSISTANT_PANEL_MARKER]: 'hidden' }}>
      <h2 id="assistant-heading" className="visually-hidden">{copy.heading}</h2>
      <form
        className="assistant-form"
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit(field.current?.value ?? '');
        }}
      >
        <label htmlFor="assistant-question" className="visually-hidden">{copy.inputLabel}</label>
        <textarea id="assistant-question" name="question" ref={field} maxLength={1000} rows={1} />
        {chips.length > 0 ? (
          <ul className="assistant-chips" aria-label={copy.contextLabel}>
            {chips.map((chip) => (
              <li key={`${chip.kind}:${chip.value}`}>
                <span>{chipLabel(chip, locale)}</span>
                <button
                  type="button"
                  aria-label={fill(copy.removeChip, { chip: chipLabel(chip, locale) })}
                  onClick={() => {
                    onRemoveChip(chip);
                    field.current?.focus();
                  }}
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        ) : null}
        <button type="submit">{copy.submit}</button>
      </form>
      {/* Read to a screen reader, not shown: the two announcements (R45(d)) are no status sentences on the screen. */}
      <div className="assistant-status visually-hidden" role="status" aria-live="polite" aria-atomic="true">
        {announced}
      </div>
      {message !== null && view === null ? <p className="answer-message">{message}</p> : null}
      {view === null ? null : (
        <article className="assistant-answer" lang={view.locale} aria-labelledby="assistant-answer-heading" data-verdict={view.verdict}>
          <h3 id="assistant-answer-heading" className="visually-hidden">{fill(copy.answerHeadingLabel, { label: view.label.text })}</h3>
          {pointed ? <p className="answer-none" data-answer-none="">{copy.notFound}</p> : null}
          {/* No article found (the owner's order of 2026-11-14, evening): one sentence and the search on the question's words;
              the server's own sentences for it (what was searched, the official site) are drawn in the work. */}
          {pointed ? null : view.parts.map((part, at) => (
            <Part part={part} locale={locale} copy={copy} titles={titles} question={question} first={at === 0} key={part.partId} />
          ))}
          {search === null ? null : <p className="answer-links"><a href={search} data-action="keyword-search">{copy.keywordSearch}</a></p>}
          <hr className="answer-rule" />
          <TheWork view={view} copy={copy} locale={locale} work={work}>
            {pointed ? view.parts.map((part) => <Part part={part} locale={locale} copy={copy} titles={titles} question={question} first={false} key={part.partId} />) : null}
          </TheWork>
          <p className="assistant-disclaimer">
            {view.notices.map((notice, at) => (
              <span className="assistant-model-notice" data-template={notice.template_id} key={notice.template_id}>
                {at === 0 ? '' : ' '}{notice.text}
              </span>
            ))}
          </p>
        </article>
      )}
      {/* R44's notice and the preview line under the answer, the question box first (the owner's order of 2026-11-14); what
          the assistant sends and to whom at the foot of the assistant's page (the owner's order of 2026-11-13, section C). */}
      <p className="assistant-notice">{copy.notice}</p>
      {preview ? <p className="assistant-preview">{copy.preview}</p> : null}
      <p className="assistant-processor">{copy.processor}</p>
    </section>
  );
}

/** The quotations of an answer, each with its article's permalink, for the check the work box shows. */
const quotesOf = (dossier) => (dossier?.parts ?? []).flatMap((part) => part.blocks ?? []).filter((block) => block?.kind === 'quote' && typeof block.article_permalink === 'string');

/** One operation of the site's API, asked as the panel's page asks it; its envelope, or null when it cannot be read. */
async function operation(fetchImpl, operationId, parameters) {
  try {
    const response = await fetchImpl(`/api/v3/${operationId}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ operation_id: operationId, parameters }) });
    return await response.json();
  } catch {
    return null;
  }
}

/**
 * The work behind an answer, learnt from the site's own operations once the answer is drawn (as lex-eval learns it): the
 * plan's search asked again as the trace records it, for how many articles it matched, and each quotation's article asked of
 * `verify`, for whether its digest is the published text's.
 */
async function workOf(dossier, seconds, fetchImpl) {
  const step = searchStepOf(dossier?.trace);
  const [searched, verifications] = await Promise.all([
    step === null ? Promise.resolve(null) : operation(fetchImpl, 'search', step),
    Promise.all(quotesOf(dossier).map(async (quote) => quoteVerifies(quote, await operation(fetchImpl, 'verify', { identifier: quote.article_permalink })))),
  ]);
  return {
    seconds,
    search: step === null ? null : searchCount(searched),
    verified: { checked: verifications.length, ok: verifications.filter(Boolean).length },
  };
}

/**
 * The panel on the assistant's page: it asks `ask` with the question alone (ask/1 carries nothing else: no earlier question,
 * no context, R45(e)), draws the answer the envelope carries (`askOutcome`), and makes the two polite announcements, the
 * verdict and then completion (R45(d)). Focus stays in the field. A presentation result is said as one sentence, a type
 * apart from the six verdicts.
 */
export function LiveAssistant({ locale, fetchImpl = (...args) => globalThis.fetch(...args) }) {
  const [dossier, setDossier] = useState(null);
  // The acts the quotations are from, by their own titles and their versions (their dossiers, asked once the answer is
  // drawn): never an address where a title exists (the owner's order of 2026-11-13, section C).
  const [titles, setTitles] = useState({});
  const [phase, setPhase] = useState('idle');
  const [message, setMessage] = useState(null);
  const [asked, setAsked] = useState(null);
  const [work, setWork] = useState(null);
  const submit = async (question) => {
    if (typeof question !== 'string' || question.trim().length === 0) return;
    setPhase('idle');
    setMessage(null);
    setWork(null);
    let outcome;
    const started = globalThis.performance?.now?.() ?? Date.now();
    try {
      const response = await fetchImpl('/api/v3/ask', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(askRequest(question)) });
      outcome = askOutcome(await response.json(), locale);
    } catch {
      outcome = askOutcome(null, locale);
    }
    const seconds = ((globalThis.performance?.now?.() ?? Date.now()) - started) / 1000;

    if (outcome.dossier === null) {
      setDossier(null);
      setMessage(outcome.sentence);
      return;
    }

    setAsked(question);
    setDossier(outcome.dossier);
    setTitles({});
    setPhase('verdict');
    setTimeout(() => setPhase('complete'), 0);
    setWork({ seconds, search: null, verified: null });
    actTitlesOf(outcome.dossier, fetchImpl).then(setTitles, () => {});
    workOf(outcome.dossier, seconds, fetchImpl).then(setWork, () => {});
  };
  // The home page's question box and example questions open this page with the question in its address's fragment
  // (`#q=…`, which the browser never sends): it is asked once, as the field would ask it.
  const [prefill, setPrefill] = useState(null);
  useEffect(() => {
    const fragment = readAskFragment(globalThis.location?.hash ?? '');
    if (fragment === null) return;
    setPrefill(fragment);
    submit(fragment);
  }, []);
  return <AssistantPanel locale={locale} dossier={dossier} phase={phase} message={message} prefill={prefill} titles={titles} question={asked} work={work} onSubmit={submit} />;
}
