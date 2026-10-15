// The region a live screen's answer is written into, announced to a screen reader when it changes.
//
// A live region must exist before its content changes, or assistive technology has nothing to watch:
// so the server renders it around the idle or loading state, and the answer, the refusal card or the
// sentence a state carries replaces what is inside it. `polite` waits for the reader to finish what
// they are hearing; nothing on these screens is urgent enough to interrupt them. The launch contract
// asks for keyboard and screen-reader paths through the eight screens, and this is the screen-reader
// half of the answer.
//
// `Say` lays out one sentence of the interface copy table (`live-chrome.mjs`): the template's text with
// its values in their places, where a value may be an element (a quotation in its own language, an
// identifier set as code).
//
// English on a page in another language is marked English, so a screen reader reads it as English:
// the platform's own phrases (`inEnglish`, `ENGLISH_LANG`) and the runs of a state's sentence that
// have no reviewed wording in the page's language (`Said`). On an English page none of this adds a
// tag or an attribute, so the English pages are exactly what they were.
//
// A state that is a service failure (`isFailure`, `live-failure.mjs`: the request did not complete, or was refused for a
// reason about this service) is laid out inside the failure panel (`FailureFrame`), never as an answer: the
// specification's line first, then what the screen says of it (its sentence, or the refusal card from the refusal's
// payload), a retry that asks the same request again, and the publishers' official sites (R60).

import { Fragment } from 'react';

import { CHROME_LOCALE, ENGLISH_LANG, countedEntry, fillParts, liveChrome } from '../scripts/live-chrome.mjs';
import { OFFICIAL_SITES, isFailure } from '../scripts/live-failure.mjs';
import { isSiteAddress, permalinkHref } from '../scripts/permalinks.mjs';

/**
 * A permalink as every live page prints it: its text as code, inside a link to this site's page for it (`permalinks.mjs`),
 * under the page's interface language, when it is an address this site serves; an address it does not serve stays text,
 * so no link leads nowhere. `linked` false keeps it text on a page a link would make the reader lose work on.
 */
export function Permalink({ value, linked = true }) {
  return linked && isSiteAddress(value) ? <a href={permalinkHref(value, CHROME_LOCALE)}><code>{value}</code></a> : <code>{value}</code>;
}

/**
 * A table too wide for a small phone, in a region that scrolls sideways on its own (WCAG 2.2, 1.4.10: two-dimensional
 * content may scroll; the page must not). The region is in the tab order, so a keyboard can scroll it, and is named by
 * the sentence above the table (`labelledBy`, that sentence's id), so it needs no words of its own.
 */
export function TableRegion({ labelledBy, children }) {
  return (
    <div className="table-scroll" role="region" tabIndex={0} aria-labelledby={labelledBy}>
      {children}
    </div>
  );
}

export function Say({ template, values }) {
  return fillParts(template, values).map((part, index) => (typeof part === 'string' ? part : <Fragment key={index}>{part}</Fragment>));
}

/**
 * A phrase the platform sends in English (a reason, a rule, a scope, a statement), as a value of a sentence or a
 * child of an element: the text itself on an English page, and the text marked `lang="en"` on a page in another.
 */
export function inEnglish(text) {
  return ENGLISH_LANG === undefined ? text : <span lang={ENGLISH_LANG}>{text}</span>;
}

/**
 * The annexes an EU wording or expression lists as not served (`scripts/europe-annexes.mjs`), one line per disposition: how
 * many annexes of the wording in `language` are not served as text and never searched, quoted or exported, the platform's
 * reason (in English) and the official source, printed, not linked.
 */
export function EuropeAnnexes({ rows, language }) {
  if (rows.length === 0) return null;
  const common = liveChrome().common;
  return rows.map((row) => (
    <p key={row.disposition} data-annexes-not-served={row.count} data-annex-disposition={row.disposition}>
      <Say
        template={countedEntry(common.europeAnnexes, row.count)}
        values={{
          count: row.count,
          language: common.languageNames[language] ?? language,
          reason: inEnglish(row.reason),
          source: <code>{row.officialSource}</code>,
        }}
      />
    </p>
  ));
}

/**
 * A Luxembourg act held only as first published (`scripts/as-published.mjs`; the plan's item 0.6), said in the page's
 * language wherever the act is read or listed: the label, what this site holds of later versions (the consolidated
 * versions the publisher's record names, each with its publisher dates, and its end-of-force date, each printed as the
 * record gives it), and the authenticity sentence of its case with the act as published, printed, not linked. Nothing
 * for a work whose consolidated state is held (`asPublished` null).
 */
export function AsPublished({ asPublished }) {
  if (asPublished === null) return null;
  const common = liveChrome().common;
  const copy = common.asPublished;
  const { consolidations, endOfForce } = asPublished.laterVersions;
  const label = `${copy.label.charAt(0).toUpperCase()}${copy.label.slice(1)}.`;
  return (
    <section data-as-published={asPublished.authenticity.basis}>
      <p data-as-published-label=""><strong>{label}</strong></p>
      <p data-later-versions={consolidations.length}>
        {consolidations.length === 0
          ? copy.laterNotHeld
          : <Say template={countedEntry(copy.laterNamed, consolidations.length)} values={{ count: consolidations.length }} />}
      </p>
      {consolidations.length > 0 ? (
        <ul data-consolidations={consolidations.length}>
          {consolidations.map((consolidation) => {
            const iri = <code>{consolidation.legalResourceIri}</code>;
            return (
              <li key={consolidation.legalResourceIri}>
                {consolidation.applicabilityDate === null ? (
                  <Say template={copy.consolidationUndated} values={{ iri }} />
                ) : consolidation.endApplicabilityDate === null ? (
                  <Say template={copy.consolidationFrom} values={{ iri, from: consolidation.applicabilityDate }} />
                ) : (
                  <Say template={copy.consolidationFromTo} values={{ iri, from: consolidation.applicabilityDate, to: consolidation.endApplicabilityDate }} />
                )}
              </li>
            );
          })}
        </ul>
      ) : null}
      {endOfForce.length > 0 ? (
        <p data-end-of-force="">
          <Say template={copy.endOfForce} values={{ date: endOfForce.join(common.listSeparator) }} />
        </p>
      ) : null}
      <p data-as-published-authenticity="">
        <Say template={copy.authenticity[asPublished.authenticity.basis]} values={{ act: <code>{asPublished.journalOfficielIri}</code> }} />
      </p>
    </section>
  );
}

/** A statement (`statement` in `live-chrome.mjs`) laid out: its sentence, or its runs with each English run marked. */
export function Said({ statement }) {
  if (statement.runs === undefined) return statement.sentence;
  return statement.runs.map((run, index) => (typeof run === 'string'
    ? <Fragment key={index}>{run}</Fragment>
    : <span key={index} lang={run.lang}>{run.text}</span>));
}

/** The sentence a state carries, as the screen's status. */
export function StatusSentence({ outcome }) {
  return (
    <p role="status">
      <Said statement={outcome} />
    </p>
  );
}

/**
 * The publishers' official sites (R60): Legilux for Luxembourg law and EUR-Lex for EU law, each linked to its home page and
 * named by what it publishes. Each is a constant `live-failure.mjs` holds, which a test holds to the publisher-link policy.
 */
export function OfficialSites() {
  const copy = liveChrome().failure;
  return (
    <div className="official-sites" data-official-sites="">
      <p>{copy.official}</p>
      <ul>
        {OFFICIAL_SITES.map((site) => (
          <li key={site.key}>
            <a href={site.uri} rel="external" data-publisher={site.publisher}>{copy.sites[site.key]}</a>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * A screen's state, inside the failure panel when it is a service failure (`isFailure`), as itself otherwise. The panel says
 * the specification's line first, so that nothing below it reads as an answer about the law; then what the screen says of
 * the failure (`children`: its sentence, or the refusal card rendered from the refusal's payload); then a retry, which
 * calls `onRetry` to ask the same request again; then the official sites (R60). `data-failure` names the state, or the
 * refusal's code.
 */
export function FailureFrame({ outcome, onRetry, children }) {
  if (!isFailure(outcome)) return children;
  const copy = liveChrome().failure;
  return (
    <div className="failure-panel" data-failure={outcome.state === 'refusal' ? outcome.code : outcome.state}>
      <p className="failure-line" data-failure-line=""><strong>{copy.line}</strong></p>
      {children}
      <p>
        <button type="button" className="failure-retry" data-retry="" onClick={onRetry}>{copy.retry}</button>
      </p>
      <OfficialSites />
    </div>
  );
}

/**
 * The refusal card's words for a refusal's publisher: an EU refusal's declared nulls, offered candidates and their
 * note speak of wordings and their dates, never of states or of what applies (the review of #903; the French review's
 * item 3).
 */
export function refusalCardCopyFor(context) {
  const copy = liveChrome().refusalCard;
  return context?.publisher === 'eu-eurlex'
    ? {
      ...copy,
      notes: { ...copy.notes, ...copy.europeNotes },
      nullSentences: copy.europeNullSentences,
      candidate: copy.europeCandidate,
      candidateWithdrawalNotStated: copy.europeCandidateWithdrawalNotStated,
      published: copy.europePublished,
    }
    : copy;
}

export function LiveAnswer({ children }) {
  return (
    <div className="live-answer" aria-live="polite" data-live-answer="">
      {children}
    </div>
  );
}
