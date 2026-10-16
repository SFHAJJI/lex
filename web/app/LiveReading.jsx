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
// text is named as such, never shown as an empty quotation. The form's controls carry no `name`.
//
// A permalink is a link to this site's own reading of it (`permalinks.mjs`), and the page served at a permalink asks
// that reading itself once it has loaded: the permalink itself, with its date and the text's language it names, one
// request (R64), so the API answers the version it pins or refuses (`pinned_digest_mismatch`, with the current link), never
// another version in its place; an act's readable address (R67) asks the work at today's date in its language. What the
// page says first, the cited article and what changed since, or the current link, is `PinnedReading.jsx`'s. The answer is
// still held to the address's pin: when no state (or EU wording) it shows is the one the address pins, the page says so
// above the reading rather than letting another version pass for it, and brings that notice into view; otherwise what it
// says first, or the article the address names (its last segment, or after `#` on an address moved from the shape before
// R64), or the state is brought into view, and given the focus.
//
// An EU work's reading is the wording that answers the date asked, original or consolidated: each wording
// is headed by its own date as a wording's (never an applicability date), and the acknowledgement and
// authenticity statement Decision 95 requires stand above the text, as the platform words them (in
// English, marked English on a page in another language).
//
// Between the form and the reading, the sentence that says what Lex is not (the specification of record's R50).

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { PinnedLead, citedTitle } from './PinnedReading.jsx';
import { AsPublished, EuropeAnnexes, FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import {
  LIVE_READING_IDLE,
  READING_LANGUAGES,
  createReadingSession,
  quotationLanguageTag,
  readingOutcome,
} from '../scripts/live-reading.mjs';
import { ENGLISH_LANG, countedEntry, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { anchorOf, anchorText } from '../scripts/permalinks.mjs';
import { answerHandedWith } from '../scripts/v3-client.mjs';
import { readingAddressShown, readingPinOf } from '../scripts/initial-answer.mjs';
import { writeLawPageLinks } from './head-links.mjs';

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
export function QuoteEvidence({ article, linked = true }) {
  return (
    <p data-quote-evidence="">
      <Say
        template={COPY.evidence}
        values={{
          text: <code>{article.textSha256}</code>,
          body: <code>{article.bodySha256}</code>,
          source: <code>{article.officialSource}</code>,
          permalink: <Permalink value={article.permalink} linked={linked} />,
        }}
      />
    </p>
  );
}

/**
 * One article's text as the publisher wrote it, in its state's language, with its own date when it differs from the
 * state's (`applicabilityDate`, a Luxembourg state's; null for an EU wording) and its notes. Shared with the cited article
 * (`PinnedReading.jsx`), which quotes the same article first.
 */
export function ArticleText({ article, language, applicabilityDate }) {
  return (
    <>
      <blockquote lang={quotationLanguageTag(language)}>{article.text}</blockquote>
      {applicabilityDate !== null && article.validityConflict ? (
        <p data-validity-conflict="">
          <Say template={COPY.validityConflict} values={{ own: article.validFrom, state: applicabilityDate }} />
        </p>
      ) : null}
      {(article.notes ?? []).length > 0 ? (
        <ol data-notes={article.notes.length}>
          {article.notes.map((note, index) => (
            <li key={`${index}.${note.marker}`}>
              <span>[{note.marker}]</span> <span lang={quotationLanguageTag(language)}>{note.text}</span>
            </li>
          ))}
        </ol>
      ) : null}
    </>
  );
}

function Article({ article, state }) {
  return (
    <li id={`${state.language}-${article.publisherId}`} data-article={article.publisherId}>
      <h3>{article.publisherId}</h3>
      <ArticleText article={article} language={state.language} applicabilityDate={state.applicabilityDate} />
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
        <Permalink value={state.permalink} />
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
      <AsPublished asPublished={view.asPublished} />
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

/**
 * An EU wording's heading: its act, its language, and its date as the original or the consolidated wording's, never
 * an applicability date. Shared with the export composer, which reads the same wordings.
 */
export function EuropeWordingHeading({ wording, celex }) {
  return (
    <Say
      template={wording.nextDate === undefined
        ? COPY.europeWordingHeading
        : wording.kind === 'consolidated_version' ? COPY.europeConsolidatedHeading : COPY.europeOriginalHeading}
      values={{ celex, language: wording.language, date: wording.wordingDate }}
    />
  );
}

function EuropeWordingReading({ wording, celex }) {
  return (
    <section data-wording={wording.wordingSha256}>
      <h2>
        <EuropeWordingHeading wording={wording} celex={celex} />
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
        <Permalink value={wording.permalink} />
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
export function ReadingAnswerView({ outcome, onRetry }) {
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
        <FailureFrame outcome={outcome} onRetry={onRetry}>
          <RefusalCard code={outcome.code} sentence={outcome.sentence} payload={outcome.payload} copy={refusalCardCopyFor(outcome.context)} />
        </FailureFrame>
      </section>
    );
  }

  return (
    <section data-answer-state={outcome.state}>
      <FailureFrame outcome={outcome} onRetry={onRetry}>
        <StatusSentence outcome={outcome} />
      </FailureFrame>
    </section>
  );
}

/**
 * The reading form: a work identifier, a date and a language, handed to `onAsk` on submit. Shared
 * with the export composer, which asks the same reading.
 */
export function ReadingForm({ onAsk, submitLabel = FORM.submit.reading, initial = null }) {
  const [identifier, setIdentifier] = useState(initial?.identifier ?? '');
  const [date, setDate] = useState(initial?.date ?? '');
  const [language, setLanguage] = useState(initial?.language ?? '');
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
export function LiveReading({ contract, fetchImpl, initial = null }) {
  // A page the server rendered from its own answer (`initial`: the envelope and the path it answered, R67) starts with
  // that reading, read as an answer the page asked for; the browser reads the same answer from the page, so both render
  // one tree and the page asks nothing as it loads.
  const [outcome, setOutcome] = useState(() => (initial === null
    ? IDLE
    : readingOutcome(answerHandedWith('evidence_bundle', initial.envelope, { contract, objectType: 'evidence_bundle' }))));
  // The law's address this page was opened at, if any (a permalink, or an act's readable address): what it names and the
  // request that asks it (`readingPinOf`; null on a page opened at reading.html). `pinned` holds while the reading shown is
  // that request's: until the reader asks another.
  const [pin, setPin] = useState(() => (initial === null ? null : readingPinOf(initial.path, initial.envelope)));
  const [pinned, setPinned] = useState(() => initial !== null && readingPinOf(initial.path, initial.envelope) !== null);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createReadingSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  // Opened at a permalink the server did not render: ask the reading it names, once, after hydration (the server
  // rendered the idle form).
  useEffect(() => {
    if (initial !== null) return;
    const named = readingPinOf(window.location.pathname);
    if (named === null) return;
    setPin(named);
    setPinned(true);
    session.current.ask(named.request);
  }, []);

  // What the address names, brought into view once the reading is shown and given the focus, as a browser does for an
  // anchor (so the keyboard and a screen reader continue from there): what the page says first (`PinnedLead`: what changed
  // since, then the cited article), else the article the address names in the pinned state (or wording), or that state
  // when no article is named. When the reading shows another version, its notice instead, and for a permalink refused as
  // no version held at its date, the notice with the current link: a reader must not land on an article without meeting
  // the notice that it is not the version the link pins. The head names the address the reading shows as the page's own,
  // and the cited article's title, as the server's render does (`readingLawPath`, `readingTitle`).
  useEffect(() => {
    if (!pinned || (outcome.state !== 'success' && outcome.state !== 'refusal')) return;
    const view = outcome.state === 'success' ? outcome.view : null;
    writeLawPageLinks(document, readingAddressShown(view, pin.address, outcome.state === 'refusal' ? { code: outcome.code, payload: outcome.payload } : null));
    const title = citedTitle(outcome, pin);
    if (title !== null) document.title = fillText(liveChrome().shell.title, { title });
    let target;
    if (view === null) {
      target = document.querySelector('[data-pin-not-held]');
    } else if (pin.kind === 'state' && !readingHolds(view, pin.permalink)) {
      target = document.querySelector('[data-pin-replaced]');
    } else if (document.querySelector('[data-pinned-lead]') !== null) {
      target = document.querySelector('[data-pinned-lead]');
    } else {
      const section = pin.digest === null ? null : document.querySelector(`[data-state="${pin.digest}"], [data-wording="${pin.digest}"]`);
      const anchor = anchorText(pin.anchor) ?? anchorOf(window.location.hash);
      const named = anchor === null ? undefined : [...(section ?? document).querySelectorAll('[data-article]')].find((element) => element.getAttribute('data-article') === anchor);
      // An article the state does not hold (a link made by hand, or to another version) brings the state into view.
      target = named ?? section;
    }
    if (!target) return;
    target.scrollIntoView();
    if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
    target.focus({ preventScroll: true });
  }, [pinned, pin, outcome]);

  const ask = (request) => {
    setPinned(false);
    session.current.ask(request);
  };
  // The failure panel's retry (R60): the last reading asked, or on a page the server rendered from a failure, the reading
  // its permalink names, as the page would have asked it. The pin stays as it was.
  const retry = () => {
    if (!session.current.retry() && pin !== null) session.current.ask(pin.request);
  };
  const replaced = pinned && pin.kind === 'state' && outcome.state === 'success' && !readingHolds(outcome.view, pin.permalink);
  return (
    <div>
      <ReadingForm key={pin === null ? 'blank' : pin.address} initial={pin?.form ?? null} onAsk={ask} />
      {/* What Lex is not, above the text it shows (the specification of record's R50: Reading says it). */}
      <p className="not-advice" data-not-advice="">{liveChrome().site.notAdvice}</p>
      <LiveAnswer>
        {pinned && !replaced ? <PinnedLead outcome={outcome} pin={pin} /> : null}
        {replaced ? (
          <p data-pin-replaced="">
            <Say
              template={outcome.view.publisher === 'eu-eurlex' ? COPY.europePinReplaced : COPY.pinReplaced}
              values={{ pinned: <code>{pin.permalink}</code> }}
            />
          </p>
        ) : null}
        <ReadingAnswerView outcome={outcome} onRetry={retry} />
      </LiveAnswer>
    </div>
  );
}

/** Whether a reading view shows the state (or EU wording) a permalink pins. */
export function readingHolds(view, permalink) {
  if (view.publisher === 'eu-eurlex') return view.wordings.some((wording) => wording.permalink === permalink);
  return view.states.some((state) => state.permalink === permalink);
}
