// The Assistant panel, as React (R47, R52, R55, R45(c)-(e); docs/ANSWER-DOSSIER-1.md).
//
// Reached by one page, the assistant's (`live-assistant-page.jsx`), which the server renders and serves only while it reports
// the assistant switched on; no built page carries the panel, and no other bundle carries its copy (the web suite checks both).
// The panel draws an answer_dossier/1 and adds nothing to
// it: every sentence of an answer is text the server filled from its closed template set, a verbatim quotation or a span of
// the question; the panel's own words come from its copy table, and every rule lives in the shared module.
//
// Accessibility, as R45(d) rules it: the validated plan is drawn first (R45(c)); one polite live region makes two
// announcements, one when the verdict is known and one when the answer is complete, never an alert; focus stays where the
// reader put it, and removing a context chip returns it to the question field. Follow-up context is removable chips
// (R45(e)), and no earlier question is ever sent again.

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
  verdictWordKey,
} from '../scripts/assistant-panel.mjs';
import { articleHeadingOf, articleNumberOf } from '../scripts/law-page.mjs';

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

/**
 * One quotation as an answer card (the owner's order of 2026-11-13, section C), in this order: the article's number and its
 * heading (read from its text as the search reads it), the act's own title (`titles`, from its dossier, when asked), the
 * quotation in its paragraphs, the version it is from, and three ways on: the act's law page at this article ("see at another
 * date": its history; "what changed": its comparison) and the official source. The quotation's heading claim, which names the
 * act by its address, and the claims for checking it are one action away (R51). Every block of the group is drawn.
 */
function QuoteGroup({ group, copy, locale, titles = {} }) {
  const checks = group.blocks.filter(isCheck);
  const quote = group.blocks.find((block) => block.kind === 'quote') ?? null;
  if (quote !== null) {
    const heading = group.blocks.find((block) => block.kind === 'claim' && block.template_id === 'law.quote_heading') ?? null;
    const rest = group.blocks.filter((block) => !isCheck(block) && block !== quote && block !== heading);
    const where = quoteWork(quote);
    const number = articleNumberOf(quote.anchor);
    const articleHeading = articleHeadingOf(quote.text);
    const title = where === null ? null : titles[where.address] ?? null;
    return (
      <div className="assistant-quote-group assistant-card" data-quote-language={group.language ?? ''}>
        <p className="assistant-article" data-anchor={quote.anchor}>
          {articleHeading === '' ? fill(copy.articleLine, { number }) : fill(copy.articleLineHeading, { number, heading: articleHeading })}
        </p>
        {title === null ? null : <p className="assistant-act" lang={tagOf(title.language)}>{title.title}</p>}
        <Text block={quote} />
        {typeof quote.from === 'string' ? <p className="assistant-version-badge">{fill(copy.versionBadge, { date: quote.from })}</p> : null}
        {rest.map((block, at) => <Block block={block} key={at} />)}
        <p className="assistant-actions">
          {where === null ? null : <a className="assistant-action" href={lawPageHref(where, quote.anchor, 'article', locale)} data-action="other-date">{copy.otherDate}</a>}
          {where === null ? null : <a className="assistant-action" href={lawPageHref(where, quote.anchor, 'changes', locale)} data-action="what-changed">{copy.whatChanged}</a>}
          {typeof quote.official_source === 'string' ? <a className="assistant-action" href={quote.official_source} rel="external" data-action="official-source">{copy.officialSource}</a> : null}
        </p>
        <details className="assistant-checks">
          <summary>{copy.checksSummary}</summary>
          {heading === null ? null : <p className="assistant-quote-heading" data-template={heading.template_id}>{heading.text}</p>}
          {checks.map((block, at) => <Text block={block} key={at} />)}
        </details>
      </div>
    );
  }
  return (
    <div className="assistant-quote-group" data-quote-language={group.language ?? ''}>
      {group.blocks.filter((block) => !isCheck(block)).map((block, at) =>
        block.kind === 'claim' && block.template_id === 'law.quote_heading'
          ? <p className="assistant-quote-heading" data-template={block.template_id} key={at}>{block.text}</p>
          : <Block block={block} key={at} />)}
      {checks.length === 0 ? null : (
        <details className="assistant-checks">
          <summary>{copy.checksSummary}</summary>
          {checks.map((block, at) => <Text block={block} key={at} />)}
        </details>
      )}
    </div>
  );
}

/**
 * One part of an answer: what comes before its first quotation, then its quotations in the interface's language first (the
 * others, the same article in the other held language, one action away). Every block of the part is drawn.
 */
function Part({ part, locale, copy, titles }) {
  const { lead, groups } = quoteGroups(part.blocks);
  const { shown, other } = groupsForLocale(groups, locale);
  return (
    <section className="assistant-part" data-verdict={part.verdict}>
      {lead.map((block, at) => <Block block={block} key={at} />)}
      {shown.map((group, at) => <QuoteGroup group={group} copy={copy} locale={locale} titles={titles} key={at} />)}
      {other.length === 0 ? null : (
        <details className="assistant-other-languages">
          <summary>{copy.otherLanguages}</summary>
          {other.map((group, at) => <QuoteGroup group={group} copy={copy} locale={locale} titles={titles} key={at} />)}
        </details>
      )}
    </section>
  );
}

/**
 * The panel: the R44 notice above the field, with the preview line while the people's reviews are not done and the
 * processor sentence (the owner's order of 2026-10-28; shown only where the panel is, which is only when the assistant is
 * switched on), the field, the kept context chips, and, once an answer arrives, its plan first,
 * then its label, its notices and its parts. `phase` is 'idle', 'verdict' or 'complete', and says which announcement the
 * polite region holds. Nothing here asks the server; the page that will mount the panel, once the assistant is switched on,
 * sends `followUpRequest(question, chips)` and nothing else.
 */
export function AssistantPanel({ locale, dossier = null, phase = 'idle', chips = [], preview = true, message = null, prefill = null, titles = {}, onSubmit = () => {}, onRemoveChip = () => {} }) {
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
  return (
    <section className="assistant-panel" aria-labelledby="assistant-heading" {...{ [ASSISTANT_PANEL_MARKER]: 'hidden' }}>
      <h2 id="assistant-heading">{copy.heading}</h2>
      <p className="assistant-notice">{copy.notice}</p>
      {preview ? <p className="assistant-preview">{copy.preview}</p> : null}
      <form
        className="assistant-form"
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit(field.current?.value ?? '');
        }}
      >
        <label htmlFor="assistant-question">{copy.inputLabel}</label>
        <textarea id="assistant-question" name="question" ref={field} maxLength={1000} rows={3} />
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
      <div className="assistant-status" role="status" aria-live="polite" aria-atomic="true">
        {announced}
      </div>
      {view === null ? null : (
        <article className="assistant-answer" lang={view.locale} aria-labelledby="assistant-answer-heading">
          <h3 id="assistant-answer-heading">{copy.answerHeading}</h3>
          {/* The answer card (the owner's order of 2026-11-13, section C): the verdict in a word first, with the answer's own
              label; then each quotation's card; how Lex found it (the validated plan and the trace, R45(c) and R32) one action
              away; and the answer's notices in one line under it, not three blocks above it. */}
          <p className="assistant-verdict" data-verdict={view.verdict}>
            <strong>{copy[verdictWordKey(view.verdict)]}</strong>
            {' \u00b7 '}
            <span className="assistant-label">{view.label.text}</span>
          </p>
          {view.parts.map((part) => (
            <Part part={part} locale={locale} copy={copy} titles={titles} key={part.partId} />
          ))}
          <details className="assistant-how">
            <summary>{copy.howFound}</summary>
            <ol className="assistant-plan">
              {view.plan.map((line, at) => (
                <li key={at}>
                  <Text block={line} />
                </li>
              ))}
            </ol>
            <details className="assistant-details">
              <summary>{copy.detailsSummary}</summary>
              <pre className="assistant-trace">{JSON.stringify(view.trace, null, 2)}</pre>
            </details>
          </details>
          <p className="assistant-disclaimer">
            {view.notices.map((notice, at) => (
              <span className="assistant-model-notice" data-template={notice.template_id} key={notice.template_id}>
                {at === 0 ? '' : ' '}{notice.text}
              </span>
            ))}
          </p>
        </article>
      )}
      {/* What the assistant sends and to whom, at the foot of the assistant's page (the owner's order of 2026-11-13, section C). */}
      <p className="assistant-processor">{copy.processor}</p>
    </section>
  );
}

/**
 * The panel on the assistant's page: it asks `ask` with the question alone (ask/1 carries nothing else: no earlier question,
 * no context, R45(e)), draws the answer the envelope carries (`askOutcome`), and makes the two polite announcements, the
 * verdict and then completion (R45(d)). Focus stays in the field. A presentation result is said as one sentence, a type
 * apart from the six verdicts.
 */
export function LiveAssistant({ locale, fetchImpl = (...args) => globalThis.fetch(...args) }) {
  const [dossier, setDossier] = useState(null);
  // The acts the quotations are from, by their own titles (their dossiers, asked once the answer is drawn): never an address
  // where a title exists (the owner's order of 2026-11-13, section C).
  const [titles, setTitles] = useState({});
  const [phase, setPhase] = useState('idle');
  const [message, setMessage] = useState(null);
  const submit = async (question) => {
    if (typeof question !== 'string' || question.trim().length === 0) return;
    setPhase('idle');
    setMessage(null);
    let outcome;
    try {
      const response = await fetchImpl('/api/v3/ask', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(askRequest(question)) });
      outcome = askOutcome(await response.json(), locale);
    } catch {
      outcome = askOutcome(null, locale);
    }

    if (outcome.dossier === null) {
      setDossier(null);
      setMessage(outcome.sentence);
      return;
    }

    setDossier(outcome.dossier);
    setTitles({});
    setPhase('verdict');
    setTimeout(() => setPhase('complete'), 0);
    actTitlesOf(outcome.dossier, fetchImpl).then(setTitles, () => {});
  };
  // The home page's question box and example questions open this page with the question in its address's fragment
  // (`#q=…`, which the browser never sends): it is asked once, as the field would ask it.
  const [prefill, setPrefill] = useState(null);
  useEffect(() => {
    const asked = readAskFragment(globalThis.location?.hash ?? '');
    if (asked === null) return;
    setPrefill(asked);
    submit(asked);
  }, []);
  return <AssistantPanel locale={locale} dossier={dossier} phase={phase} message={message} prefill={prefill} titles={titles} onSubmit={submit} />;
}
