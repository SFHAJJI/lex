// The reading screen, asked of the live API: the text of a work as it stood on one date.
//
// The server renders the form and the idle state, and the browser asks only when the reader
// submits. What an answer means is decided elsewhere: `scripts/live-reading.mjs` builds the request
// and maps the answer to a view state, `scripts/reading-answer.mjs` reads the evidence bundle, and
// `RefusalCard` lays out a refusal. This file lays the view out and marks the state on the answer
// (`data-answer-state`).
//
// Three rules of the pre-V3 reading screen carry over, because each is a sentence the page would
// otherwise say that is not true. The publisher's text is quoted in its state's language
// (`lang` on the quotation, never the interface's). An article whose own date differs from its
// state's shows both dates, and this page does not decide which controlled. An article held without
// text is named as such, never shown as an empty quotation. The form's controls carry no `name`, and
// permalinks are printed, not linked.
//
// An EU work's reading is the wording that answers the date asked, original or consolidated: each wording
// is headed by its own date as a wording's (never an applicability date), and the acknowledgement and
// authenticity statement Decision 95 requires stand above the text, as the platform words them (in
// English, marked English on a page in another language).

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { EuropeAnnexes, LiveAnswer, Say, StatusSentence, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import {
  LIVE_READING_IDLE,
  READING_LANGUAGES,
  createReadingSession,
  quotationLanguageTag,
} from '../scripts/live-reading.mjs';
import { ENGLISH_LANG, countedEntry, liveChrome } from '../scripts/live-chrome.mjs';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().reading;
const COMMON = liveChrome().common;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_READING_IDLE });

/**
 * What a quotation carries, beside it (the launch contract's first promise): its text digest, its body
 * digest, its official source and the permalink that pins its work, article, date, language and state.
 * Shared with the export composer, which quotes the same articles (review of #805).
 */
export function QuoteEvidence({ article }) {
  return (
    <p data-quote-evidence="">
      <Say
        template={COPY.evidence}
        values={{
          text: <code>{article.textSha256}</code>,
          body: <code>{article.bodySha256}</code>,
          source: <code>{article.officialSource}</code>,
          permalink: <code>{article.permalink}</code>,
        }}
      />
    </p>
  );
}

function Article({ article, state }) {
  return (
    <li id={`${state.language}-${article.publisherId}`} data-article={article.publisherId}>
      <h3>{article.publisherId}</h3>
      <blockquote lang={quotationLanguageTag(state.language)}>{article.text}</blockquote>
      {article.validityConflict ? (
        <p data-validity-conflict="">
          <Say template={COPY.validityConflict} values={{ own: article.validFrom, state: state.applicabilityDate }} />
        </p>
      ) : null}
      {article.notes.length > 0 ? (
        <ol data-notes={article.notes.length}>
          {article.notes.map((note, index) => (
            <li key={`${index}.${note.marker}`}>
              <span>[{note.marker}]</span> <span lang={quotationLanguageTag(state.language)}>{note.text}</span>
            </li>
          ))}
        </ol>
      ) : null}
      <QuoteEvidence article={article} />
    </li>
  );
}

function StateReading({ state, workKey }) {
  return (
    <section data-state={state.stateSha256}>
      <h2>
        {state.nextApplicabilityDate === null ? (
          <Say template={COPY.stateHeading} values={{ work: workKey, language: state.language, from: state.applicabilityDate }} />
        ) : (
          <Say template={COPY.stateHeadingNext} values={{ work: workKey, language: state.language, from: state.applicabilityDate, next: state.nextApplicabilityDate }} />
        )}
      </h2>
      <p>
        <code>{state.permalink}</code>
      </p>
      <p data-counts="">
        <Say
          template={countedEntry(COPY.counts, state.articles.length)}
          values={{
            count: state.articles.length,
            withoutText: state.articlesWithoutText.length,
            notAdmitted: state.articlesNotAdmitted,
            conflicts: state.validityConflictCount,
          }}
        />
      </p>
      <ol className="articles">
        {state.articles.map((article) => (
          <Article key={article.articleIdentitySha256} article={article} state={state} />
        ))}
      </ol>
      {state.articlesWithoutText.length > 0 ? (
        <p data-without-text="">
          <Say template={COPY.withoutText} values={{ articles: state.articlesWithoutText.map((entry) => entry.publisherId).join(', ') }} />
        </p>
      ) : null}
    </section>
  );
}

/** One reading view, laid out: each state's quoted text and what the bundle does not hold. */
export function ReadingView({ view }) {
  return (
    <>
      <p data-rights="">
        {view.language === null ? (
          <Say template={COPY.rights} values={{ rights: view.rightsDisposition, date: view.date }} />
        ) : (
          <Say template={COPY.rightsIn} values={{ rights: view.rightsDisposition, date: view.date, language: view.language }} />
        )}
      </p>
      {view.states.map((state) => (
        <StateReading key={state.stateSha256} state={state} workKey={view.workKey} />
      ))}
      <h3>{COPY.notHeldHeading}</h3>
      <ul data-not-held={view.notHeld.length}>
        {view.notHeld.map((row) => (
          <li key={row.item}>
            <Say template={COMMON.notHeldRow} values={{ item: <strong>{row.item}</strong>, reason: inEnglish(row.reason) }} />
          </li>
        ))}
      </ul>
    </>
  );
}

function EuropeArticle({ article, wording }) {
  return (
    <li id={`${wording.language}-${article.publisherId}`} data-article={article.publisherId}>
      <h3>
        {article.publisherId}
        {article.heading.length > 0 ? (
          <>
            {' '}
            <span lang={quotationLanguageTag(wording.language)}>{article.heading}</span>
          </>
        ) : null}
      </h3>
      <blockquote lang={quotationLanguageTag(wording.language)}>{article.text}</blockquote>
      <QuoteEvidence article={article} />
    </li>
  );
}

/** One disclosure line beside an EU wording: how many other versions there are and whether their text is held here. */
function EuropeDisclosure({ template, rows, name }) {
  if (rows.length === 0) return null;
  const held = rows.filter((row) => row.textHeld).length;
  return (
    <p data-disclosure={name}>
      <Say template={countedEntry(template, rows.length)} values={{ count: rows.length, held, notHeld: rows.length - held }} />
    </p>
  );
}

function EuropeWordingReading({ wording, celex }) {
  return (
    <section data-wording={wording.wordingSha256}>
      <h2>
        <Say
          template={wording.nextDate === undefined
            ? COPY.europeWordingHeading
            : wording.kind === 'consolidated_version' ? COPY.europeConsolidatedHeading : COPY.europeOriginalHeading}
          values={{ celex, language: wording.language, date: wording.wordingDate }}
        />
      </h2>
      {wording.nextDate === undefined ? null : (
        <p data-wording-holds="">
          {wording.nextDate === null
            ? COPY.europeLatest
            : <Say template={COPY.europeHoldsUntil} values={{ date: wording.wordingDate, next: wording.nextDate }} />}
        </p>
      )}
      <EuropeDisclosure template={COPY.europeSameDateWorks} rows={wording.sameDateWorks} name="same-date" />
      <EuropeDisclosure template={COPY.europeUnplaced} rows={wording.unplacedVersions} name="unplaced" />
      <p>
        <code>{wording.permalink}</code>
      </p>
      <p data-counts="">
        <Say
          template={countedEntry(COPY.europeCounts, wording.articles.length)}
          values={{ count: wording.articles.length, withoutText: wording.articlesWithoutText.length }}
        />
      </p>
      <ol className="articles">
        {wording.articles.map((article) => (
          <EuropeArticle key={article.articleIdentitySha256} article={article} wording={wording} />
        ))}
      </ol>
      {wording.articlesWithoutText.length > 0 ? (
        <p data-without-text="">
          <Say template={COPY.withoutText} values={{ articles: wording.articlesWithoutText.map((entry) => entry.publisherId).join(', ') }} />
        </p>
      ) : null}
      <EuropeAnnexes rows={wording.annexesNotServed} language={wording.language} />
    </section>
  );
}

/**
 * One EU reading view, laid out: the acknowledgement and authenticity statement, each held wording's
 * quoted text, and what the bundle does not hold.
 */
export function EuropeReadingView({ view }) {
  return (
    <>
      <p data-acknowledgement="" lang={ENGLISH_LANG}>{view.acknowledgement}</p>
      <p data-authenticity="" lang={ENGLISH_LANG}>{view.authenticity}</p>
      {view.wordings.map((wording) => (
        <EuropeWordingReading key={wording.wordingSha256} wording={wording} celex={view.celex} />
      ))}
      <h3>{COPY.notHeldHeading}</h3>
      <ul data-not-held={view.notHeld.length}>
        {view.notHeld.map((row) => (
          <li key={row.item}>
            <Say template={COMMON.notHeldRow} values={{ item: <strong>{row.item}</strong>, reason: inEnglish(row.reason) }} />
          </li>
        ))}
      </ul>
    </>
  );
}

/** One view state, laid out: the reading, the refusal card, or the sentence a state carries. */
export function ReadingAnswerView({ outcome }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        {outcome.view.publisher === 'eu-eurlex' ? <EuropeReadingView view={outcome.view} /> : <ReadingView view={outcome.view} />}
      </section>
    );
  }

  if (outcome.state === 'refusal' && outcome.card) {
    return (
      <section data-answer-state="refusal">
        <RefusalCard code={outcome.code} sentence={outcome.sentence} payload={outcome.payload} copy={refusalCardCopyFor(outcome.context)} />
      </section>
    );
  }

  return (
    <section data-answer-state={outcome.state}>
      <StatusSentence outcome={outcome} />
    </section>
  );
}

/**
 * The reading form: a work identifier, a date and a language, handed to `onAsk` on submit. Shared
 * with the export composer, which asks the same reading.
 */
export function ReadingForm({ onAsk, submitLabel = FORM.submit.reading }) {
  const [identifier, setIdentifier] = useState('');
  const [date, setDate] = useState('');
  const [language, setLanguage] = useState('');
  return (
    <form
      role="search"
      onSubmit={(event) => {
        event.preventDefault();
        onAsk({ identifier, date, language });
      }}
    >
      <label>
        {FORM.workIdentifier}{' '}
        <input
          type="text"
          value={identifier}
          autoComplete="off"
          spellCheck={false}
          onChange={(event) => setIdentifier(event.target.value)}
        />
      </label>{' '}
      <label>
        {FORM.date}{' '}
        <input
          type="text"
          inputMode="numeric"
          placeholder={FORM.datePlaceholder}
          value={date}
          autoComplete="off"
          onChange={(event) => setDate(event.target.value)}
        />
      </label>{' '}
      <label>
        {FORM.language}{' '}
        <select value={language} onChange={(event) => setLanguage(event.target.value)}>
          <option value="">{FORM.anyLanguage}</option>
          {READING_LANGUAGES.map((offered) => (
            <option key={offered.code} value={offered.code}>
              {offered.label}
            </option>
          ))}
        </select>
      </label>{' '}
      <button type="submit">{submitLabel}</button>
    </form>
  );
}

/**
 * The live screen: a form, and the state of the last reading asked. A reading is asked when the
 * reader submits, never while rendering; the session cancels the one in flight when another is
 * asked, and unmounting cancels whatever is left.
 */
export function LiveReading({ contract, fetchImpl }) {
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createReadingSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  return (
    <div>
      <ReadingForm onAsk={(request) => session.current.ask(request)} />
      <LiveAnswer>
        <ReadingAnswerView outcome={outcome} />
      </LiveAnswer>
    </div>
  );
}
