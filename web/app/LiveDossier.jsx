// The dossier screen, asked of the live API.
//
// The server renders the form and the idle state, and the browser asks only when the reader submits,
// so hydration changes nothing the server rendered and each answer arrives as a state change. What
// an answer means is decided elsewhere: `scripts/live-dossier.mjs` builds the request and maps the
// answer to a view state, `scripts/dossier-answer.mjs` reads it, and `RefusalCard` lays out a
// refusal. This file lays the view out and marks the state on the answer (`data-answer-state`).
//
// The form's controls carry no `name`, as the search screen's do: a submit the browser performs
// itself, before hydration or without the bundle, then sends nothing. Permalinks are printed, not
// linked: this origin serves no reading page for them yet.
//
// An EU work's dossier (`view.publisher` "eu-eurlex") is laid out with its own words: its CELEX and
// work IRI, and each expression the EU index holds with the date of its original wording (never an
// applicability date or a state; the answer's `date_semantics` says what the date is, shown as sent),
// its article count and the permalink that pins that wording. The consolidated wordings the EU time
// view serves are read on the reading screen.
//
// Today, the calendar date in Luxembourg on the day the server answered (R13; the plan's item 3.4), is said under the
// count of the versions held, so a reader holds each version's dates against today, never against a day assumed.

import { Fragment, useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { DOSSIER_LANGUAGES, LIVE_DOSSIER_IDLE, createDossierSession, dossierOutcome } from '../scripts/live-dossier.mjs';
import { AsPublished, EuropeAnnexes, EuropeForceDates, EuropePublication, FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, TableRegion, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import { ENGLISH_LANG, countedEntry, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { parseSitePath } from '../scripts/permalinks.mjs';
import { answerHandedWith } from '../scripts/v3-client.mjs';
import { datedByEntryIntoForce } from '../scripts/date-role.mjs';
import { catalogueRecord, fillCard, versionNameKey } from '../scripts/refusal-card.mjs';
import { todayAt } from '../scripts/initial-answer.mjs';
import { TodayLine } from './Today.jsx';
import { Breadcrumbs } from './Breadcrumbs.jsx';
import { dossierCrumbs } from '../scripts/breadcrumbs.mjs';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().dossier;
const COMMON = liveChrome().common;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_DOSSIER_IDLE });

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

export function DossierTitles({ titles }) {
  if (titles.length === 0) {
    return <p data-titles="none">{COPY.noTitle}</p>;
  }
  return (
    <ul data-titles={titles.length}>
      {titles.map((group) => (
        <li key={`${group.language}.${group.expressionIri}`}>
          {/* The publisher's titles are statute text, marked in their own language, apart from the
              interface's (the launch contract's statute-language line). The label's colon and the
              separator between titles are the table's, so each language sets its own typography. */}
          <Say
            template={COPY.titleGroup}
            values={{
              language: group.language,
              titles: [
                ...group.titles.map((entry) => ({ title: entry.title, short: false })),
                ...group.shortTitles.map((entry) => ({ title: entry.title, short: true })),
              ].map((entry, index) => {
                const title = <span lang={quotationLanguageTag(group.language)}>{entry.title}</span>;
                return (
                  <span key={`${index}.${entry.title}`}>
                    {index === 0 ? '' : COMMON.listSeparator}
                    {entry.short ? <Say template={COPY.shortTitle} values={{ title }} /> : title}
                  </span>
                );
              }),
            }}
          />
        </li>
      ))}
    </ul>
  );
}

/**
 * The span of a dossier's states, said with the first state's role (R21, S7b): an act as published dated by its entry into
 * force is said as that, alone, or as the first of a span whose latest applies from its date; any other span as before.
 */
function StatesSpan({ view }) {
  const first = view.states[0];
  const latest = view.states[view.states.length - 1];
  const count = view.stateCount;
  const from = view.historyBegins;
  const to = view.latestApplicabilityDate;
  const language = view.language;
  if (!datedByEntryIntoForce(first)) {
    return language === null
      ? <Say template={countedEntry(COPY.states, count)} values={{ count, from, to }} />
      : <Say template={countedEntry(COPY.statesIn, count)} values={{ count, language, from, to }} />;
  }
  // Every state an act as published dated by its entry into force: one per language, all on its one date (so one state in a
  // language asked, which the uncounted form says; the French review of S7b and S7c, point 4).
  if (datedByEntryIntoForce(latest) && (language === null || count === 1)) {
    return language === null
      ? <Say template={countedEntry(COPY.statesEntryIntoForce, count)} values={{ count, from }} />
      : <Say template={COPY.statesInEntryIntoForce} values={{ count, language, from }} />;
  }
  return language === null
    ? <Say template={COPY.statesFromEntryIntoForce} values={{ count, from, to }} />
    : <Say template={COPY.statesInFromEntryIntoForce} values={{ count, language, from, to }} />;
}

/**
 * The publisher's catalogue between the held versions (index 9's chain, S7b; its page S7c): the versions it lists whose text
 * this site does not hold, each named as the refusal card names it and why, with its own link (its permalink refuses for its
 * text) or its coordinate; then its gaps, overlaps, same-date groups, unclear end days and invalid intervals, in the page's
 * words with the answer's dates, and that a gap in the catalogue is not one in the law. Nothing on an index of schema 8 or 6.
 */
function CatalogueChain({ chain }) {
  if (chain === null) return null;
  const card = liveChrome().refusalCard;
  const lines = [
    ...chain.gaps.map((gap) => ['gap', fillText(COPY.gap, gap)]),
    ...chain.overlaps.map((overlap) => ['overlap', fillText(COPY.overlap, { from: overlap.from, to: overlap.to })]),
    ...chain.sameDate.map((group) => ['same-date', fillText(COPY.sameDate, group)]),
    ...chain.unclearEndDays.map((day) => ['unclear-end-day', fillText(COPY.unclearEndDay, day)]),
    ...chain.invalidIntervals.map((interval) => ['invalid-interval', fillText(COPY.invalidInterval, interval)]),
  ];
  if (chain.textless.length === 0 && lines.length === 0) return null;
  return (
    <section data-catalogue-chain="">
      <h3>{COPY.chainHeading}</h3>
      {chain.textless.length > 0 ? (
        <>
          <h4>{COPY.textlessHeading}</h4>
          <ul data-textless={chain.textless.length}>
            {chain.textless.map((row) => {
              const version = fillCard(card.versions[versionNameKey(row.stateKind, row.dateRole)], { date: row.applicabilityDate });
              // The row is a sentence, so its first letter is a capital (the French review of S7b and S7c, point 2).
              const said = fillText(COPY.textlessRow, { version, why: card.textNotHeld[row.textStatus] });
              return (
                <li key={row.stateSha256} data-text-status={row.textStatus}>
                  {`${said.charAt(0).toUpperCase()}${said.slice(1)}`}{' '}
                  {row.permalink === null ? <code>{row.stableCoordinate}</code> : <Permalink value={row.permalink} />}
                </li>
              );
            })}
          </ul>
        </>
      ) : null}
      {lines.length > 0 ? (
        <ul data-chain={lines.length}>
          {lines.map(([kind, text], index) => <li key={`${kind}.${index}`} data-chain-kind={kind}>{text}</li>)}
        </ul>
      ) : null}
      <p>{COPY.chainNote}</p>
    </section>
  );
}

/**
 * The act's force as the publisher records it (R79, E3; index 9's `force`, read by `dossier-answer.mjs`): its status in the
 * publisher's own token, its entry into force and the date from which it is no longer in force, each said with its role and as
 * the publisher's (R21), and a note that this is the publisher's statement about the whole act, not a reading of it for anyone's
 * situation. Nothing on an index of schema 8 or 6.
 */
function ForceFacts({ force }) {
  if (force === null) return null;
  const codes = (values) => values.map((value, index) => <Fragment key={value}>{index === 0 ? '' : COMMON.listSeparator}<code>{value}</code></Fragment>);
  const dated = (values) => values.map((value, index) => (
    <Fragment key={value}>{index === 0 ? '' : COMMON.listSeparator}<time dateTime={value}>{value}</time></Fragment>
  ));
  return (
    <section data-force="">
      <h3>{COPY.forceHeading}</h3>
      <ul>
        <li data-force-status={force.statuses.length}>
          {force.statuses.length === 0
            ? COPY.forceStatusNone
            : <Say template={COPY.forceStatus} values={{ status: codes(force.statuses) }} />}
        </li>
        <li data-date-role="entry_into_force">
          {force.entryIntoForce.length === 0
            ? COPY.forceEntryNone
            : <Say template={COPY.forceEntry} values={{ date: dated(force.entryIntoForce) }} />}
        </li>
        {force.noLongerInForce.length === 0 ? null : (
          <li data-date-role="no_longer_in_force"><Say template={COPY.forceEnd} values={{ date: dated(force.noLongerInForce) }} /></li>
        )}
      </ul>
      {force.otherForms === 0 ? null : <p>{COPY.forceOtherForms}</p>}
      <p>{COPY.forceNote}</p>
    </section>
  );
}

/**
 * One dossier view, laid out: the work, its titles, its held states, today (`today`, the date in Luxembourg the answer was
 * given on, or null) and what it does not hold.
 */
export function DossierView({ view, today = null }) {
  return (
    <>
      <h2>{view.workKey}</h2>
      <p>
        <Say template={COPY.heldIn} values={{ iri: <code>{view.publisherWorkIri}</code>, languages: view.availableLanguages.join(', ') }} />
      </p>
      <DossierTitles titles={view.titles} />
      <AsPublished asPublished={view.asPublished} />
      <ForceFacts force={view.force} />
      <p data-history="" id="dossier-states">
        <StatesSpan view={view} />
      </p>
      <TodayLine today={today} />
      <TableRegion labelledBy="dossier-states">
      <table>
        <thead>
          <tr>
            <th scope="col">{COMMON.language}</th>
            <th scope="col">{COMMON.appliesFrom}</th>
            <th scope="col">{COMMON.nextFrom}</th>
            <th scope="col">{COPY.articlesHeld}</th>
            <th scope="col">{COPY.articlesNotAdmitted}</th>
            <th scope="col">{COMMON.permalink}</th>
          </tr>
        </thead>
        <tbody>
          {view.states.map((state) => (
            <tr key={state.stateSha256} data-state={state.stateSha256}>
              <td>{state.language}</td>
              <td data-date-role={state.dateRole ?? undefined}>
                {datedByEntryIntoForce(state) ? <Say template={COPY.entryIntoForceDate} values={{ date: state.applicabilityDate }} /> : state.applicabilityDate}
              </td>
              <td>{state.nextApplicabilityDate ?? COMMON.noneHeld}</td>
              <td>{state.articleCount}</td>
              <td>{state.articlesNotAdmitted}</td>
              <td><Permalink value={state.permalink} /></td>
            </tr>
          ))}
        </tbody>
      </table>
      </TableRegion>
      {view.states.some(datedByEntryIntoForce) ? <p data-entry-into-force-note="">{COPY.entryIntoForceNote}</p> : null}
      <CatalogueChain chain={view.chain} />
      <p lang={ENGLISH_LANG}>{asSentence(view.articlesNotAdmittedNote)}</p>
      <h3>{COPY.notHeldHeading}</h3>
      <ul data-not-held={view.notHeld.length}>
        {view.notHeld.map((row) => (
          <li key={row.item}>
            <Say template={COMMON.notHeldRow} values={{ item: <strong>{row.item}</strong>, reason: inEnglish(row.reason) }} />
          </li>
        ))}
      </ul>
      <p lang={ENGLISH_LANG}>{asSentence(view.scope)}</p>
    </>
  );
}

/**
 * One EU dossier view, laid out: the work, its expressions with the original wording of each, pinned, today (`today`, as
 * for a Luxembourg work) and what it does not hold.
 */
export function EuropeDossierView({ view, today = null }) {
  return (
    <>
      <h2>{view.celex}</h2>
      {/* The act's titles and Official Journal reference, over an EU index /7 (Release 3's item 1): its original wording's. */}
      <EuropePublication
        titles={view.titles ?? null}
        officialJournal={view.officialJournal ?? null}
        expressions={view.expressions.map((expression) => ({ expressionIri: expression.expressionIri, language: expression.language }))}
      />
      <p>
        <Say template={COPY.heldIn} values={{ iri: <code>{view.publisherWorkIri}</code>, languages: view.availableLanguages.join(', ') }} />
      </p>
      <p data-expressions={view.expressionCount} id="dossier-expressions">
        <Say template={countedEntry(COPY.euExpressions, view.expressionCount)} values={{ count: view.expressionCount }} />
      </p>
      <TodayLine today={today} />
      <TableRegion labelledBy="dossier-expressions">
      <table>
        <thead>
          <tr>
            <th scope="col">{COMMON.language}</th>
            <th scope="col">{COPY.wordingDate}</th>
            <th scope="col">{COPY.articlesHeld}</th>
            <th scope="col">{COMMON.permalink}</th>
          </tr>
        </thead>
        <tbody>
          {view.expressions.map((expression) => (
            <tr key={expression.expressionIri} data-pinned-wording={expression.wording.wordingSha256}>
              <td>{expression.language}</td>
              <td>{expression.wording.wordingDate}</td>
              <td>{expression.articleCount}</td>
              <td><Permalink value={expression.wording.permalink} /></td>
            </tr>
          ))}
        </tbody>
      </table>
      </TableRegion>
      {view.expressions.map((expression) => (
        <EuropeAnnexes key={expression.expressionIri} rows={expression.annexesNotServed} language={expression.language} />
      ))}
      <EuropeForceDates forceDates={view.forceDates ?? null} />
      <p data-date-semantics="" lang={ENGLISH_LANG}>{asSentence(view.dateSemantics)}</p>
      <h3>{COPY.notHeldHeading}</h3>
      <ul data-not-held={view.notHeld.length}>
        {view.notHeld.map((row) => (
          <li key={row.item}>
            <Say template={COMMON.notHeldRow} values={{ item: <strong>{row.item}</strong>, reason: inEnglish(row.reason) }} />
          </li>
        ))}
      </ul>
      <p lang={ENGLISH_LANG}>{asSentence(view.scope)}</p>
    </>
  );
}

/** One view state, laid out: the dossier, the refusal card, or the sentence a state carries. */
export function DossierAnswerView({ outcome, onRetry }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        {outcome.view.publisher === 'eu-eurlex'
          ? <EuropeDossierView view={outcome.view} today={todayAt(outcome.context?.freshness?.observed_at)} />
          : <DossierView view={outcome.view} today={todayAt(outcome.context?.freshness?.observed_at)} />}
      </section>
    );
  }

  if (outcome.state === 'refusal' && outcome.card) {
    return (
      <section data-answer-state="refusal">
        {/* An act the publisher's catalogue holds and this site holds no version of (R77): its work and its title, in the
            title's own language, above its record. */}
        {catalogueRecord(outcome.code, outcome.payload) ? (
          <>
            <h2 data-catalogue-record="">{outcome.payload.work_key}</h2>
            {typeof outcome.payload.title === 'string' ? <p data-catalogue-title=""><span lang={quotationLanguageTag(outcome.payload.title_language)}>{outcome.payload.title}</span></p> : null}
          </>
        ) : null}
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
 * The live screen: a form, and the state of the last dossier asked. A dossier is asked when the
 * reader submits, never while rendering; the session cancels the one in flight when another is
 * asked, and unmounting cancels whatever is left. `contract` and `fetchImpl` must be stable for the
 * screen's life.
 */
export function LiveDossier({ contract, fetchImpl, initial = null }) {
  // A page the server rendered from its own answer (`initial`, R67) starts with that dossier and the work its address
  // names in the form; the browser reads the same answer from the page, so both render one tree.
  const [identifier, setIdentifier] = useState(() => (initial === null ? '' : (workOf(initial.path) ?? '')));
  const [language, setLanguage] = useState('');
  const [outcome, setOutcome] = useState(() => (initial === null
    ? IDLE
    : dossierOutcome(answerHandedWith('dossier', initial.envelope, { contract, objectType: 'work_record' }))));
  const session = useRef(null);
  if (session.current === null) {
    session.current = createDossierSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  // Opened at a work's address (`/lu-legilux/{work}`, `/eu-eurlex/{celex}`) the server did not render: ask its dossier,
  // once, after hydration.
  useEffect(() => {
    if (initial !== null) return;
    const work = workOf(window.location.pathname);
    if (work === null) return;
    setIdentifier(work);
    session.current.ask({ identifier: work, language: '' });
  }, []);

  // The failure panel's retry (R60): the last dossier asked, or on a page the server rendered from a failure, the dossier
  // its address names, as the page would have asked it.
  const retry = () => {
    if (session.current.retry() || initial === null) return;
    const work = workOf(initial.path);
    if (work !== null) session.current.ask({ identifier: work, language: '' });
  };

  return (
    <div>
      {/* Where the act shown stands (R48): its publisher, and the act, which is this page; none until one is shown. */}
      <Breadcrumbs crumbs={outcome.state === 'success' ? dossierCrumbs(outcome.view) : []} />
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          session.current.ask({ identifier, language });
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
          {FORM.language}{' '}
          <select value={language} onChange={(event) => setLanguage(event.target.value)}>
            <option value="">{FORM.anyLanguage}</option>
            {DOSSIER_LANGUAGES.map((offered) => (
              <option key={offered.code} value={offered.code}>
                {offered.label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit">{FORM.submit.dossier}</button>
      </form>
      <LiveAnswer>
        <DossierAnswerView outcome={outcome} onRetry={retry} />
      </LiveAnswer>
    </div>
  );
}

/** The work a site path's address names, as the API is asked for it, or null. */
function workOf(pathname) {
  const named = parseSitePath(pathname);
  return named !== null && named.kind === 'work' ? named.identifier : null;
}
