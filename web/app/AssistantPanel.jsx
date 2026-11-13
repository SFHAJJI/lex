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
  announcements,
  askOutcome,
  askRequest,
  chipLabel,
  fill,
  groupsForLocale,
  panelCopy,
  quoteGroups,
  quoteParagraphs,
  readAskFragment,
  validateDossier,
} from '../scripts/assistant-panel.mjs';

/**
 * One visible text of an answer: a quotation in the held text's own language, set in the paragraphs its own numbering shows
 * (`quoteParagraphs`: no word added, dropped or changed), or a sentence in the answer's.
 */
function Text({ block }) {
  if (block.kind === 'quote') {
    // The quotation carries its held expression's language, so a screen reader reads it in that voice.
    const lang = block.language === 'eng' ? 'en' : block.language === 'deu' ? 'de' : 'fr';
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
 * One quotation with its heading, interval and disclosures, and the claims that are for checking it one action away (R51:
 * hashes and identifiers in the detailed view). Every block of the group is drawn.
 */
function QuoteGroup({ group, copy }) {
  const checks = group.blocks.filter(isCheck);
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
function Part({ part, locale, copy }) {
  const { lead, groups } = quoteGroups(part.blocks);
  const { shown, other } = groupsForLocale(groups, locale);
  return (
    <section className="assistant-part" data-verdict={part.verdict}>
      {lead.map((block, at) => <Block block={block} key={at} />)}
      {shown.map((group, at) => <QuoteGroup group={group} copy={copy} key={at} />)}
      {other.length === 0 ? null : (
        <details className="assistant-other-languages">
          <summary>{copy.otherLanguages}</summary>
          {other.map((group, at) => <QuoteGroup group={group} copy={copy} key={at} />)}
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
export function AssistantPanel({ locale, dossier = null, phase = 'idle', chips = [], preview = true, message = null, prefill = null, onSubmit = () => {}, onRemoveChip = () => {} }) {
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
      <p className="assistant-processor">{copy.processor}</p>
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
          <ol className="assistant-plan">
            {view.plan.map((line, at) => (
              <li key={at}>
                <Text block={line} />
              </li>
            ))}
          </ol>
          <p className="assistant-label" data-verdict={view.verdict}>
            {view.label.text}
          </p>
          {view.notices.map((notice) => (
            <p className="assistant-model-notice" key={notice.template_id}>
              {notice.text}
            </p>
          ))}
          {view.parts.map((part) => (
            <Part part={part} locale={locale} copy={copy} key={part.partId} />
          ))}
          <details className="assistant-details">
            <summary>{copy.detailsSummary}</summary>
            <pre className="assistant-trace">{JSON.stringify(view.trace, null, 2)}</pre>
          </details>
        </article>
      )}
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
    setPhase('verdict');
    setTimeout(() => setPhase('complete'), 0);
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
  return <AssistantPanel locale={locale} dossier={dossier} phase={phase} message={message} prefill={prefill} onSubmit={submit} />;
}
