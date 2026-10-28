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
import { AsPublished, EuropeAnnexes, EuropeForceDates, FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, TableRegion, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import { CHROME_LOCALE, ENGLISH_LANG, countedEntry, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { parseSitePath, permalinkHref } from '../scripts/permalinks.mjs';
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

/** The classes of identifier every relation family read in full covers, or null when one names none (`subject_classes`). */
function relationClasses(relations) {
  const acquired = relations.families.filter((family) => family.acquired);
  if (acquired.length === 0 || acquired.some((family) => family.subjectClasses === null)) return null;
  return [...new Set(acquired.flatMap((family) => family.subjectClasses))].sort();
}

/** Whether the classes are exactly the codes, laws and grand-ducal regulations the fixed sentences name. */
function codesLawsRegulations(classes) {
  return classes !== null && classes.length === 3 && classes[0] === 'code' && classes[1] === 'loi' && classes[2] === 'rgd';
}

/** A list of class codes, each set as code, between the table's separators. */
function classCodes(classes) {
  return classes.map((value, index) => <Fragment key={value}>{index === 0 ? '' : COMMON.listSeparator}<code>{value}</code></Fragment>);
}

/**
 * The other act an edge or R72's notice names: its title in its own language where the catalogue states one, else its
 * identifier as code, linked to this site's page for it (its dossier, or the catalogue's record) where it has one; an act whose
 * title R92 withholds is named by its identifier, with the table's words for that.
 */
function OtherActName({ other, self = false }) {
  if (self) return <>{COPY.relationSelf}</>;
  const name = other.title === null
    ? <code>{other.iri}</code>
    : <span lang={quotationLanguageTag(other.titleLanguage)}>{other.title}</span>;
  const linked = other.address === null ? name : <a href={permalinkHref(other.address, CHROME_LOCALE)}>{name}</a>;
  return other.titleWithheld ? <>{linked} ({COPY.relationTitleWithheld})</> : linked;
}

/** One edge: the other act, and its own publication date and entry into force, each with its role (R21). */
function RelationRow({ edge }) {
  const act = <OtherActName other={edge.other} self={edge.isSelfReference} />;
  const { publicationDate, entryIntoForce } = edge.other;
  const said = publicationDate === null
    ? <Say template={COPY.relationUndated} values={{ act }} />
    : entryIntoForce === null
      ? <Say template={COPY.relationPublished} values={{ act, published: <time dateTime={publicationDate}>{publicationDate}</time> }} />
      : <Say template={COPY.relationPublishedEntry} values={{
          act,
          published: <time dateTime={publicationDate}>{publicationDate}</time>,
          entry: <time dateTime={entryIntoForce}>{entryIntoForce}</time>,
        }} />;
  return (
    <li data-relation-evidence={edge.evidenceSha256}>
      {said}
      {edge.consolidatedBy !== null ? (
        <> <Say template={COPY.relationIntegrated} values={{ date: <time dateTime={edge.consolidatedBy.applicabilityDate}>{edge.consolidatedBy.applicabilityDate}</time> }} /></>
      ) : edge.consolidatedByIri !== null ? (
        <> <Say template={COPY.relationIntegratedOther} values={{ version: <code>{edge.consolidatedByIri}</code> }} /></>
      ) : null}
    </li>
  );
}

/**
 * The publisher's relation records of the act (index 10's `publisher_relations`, read by `publisher-relations.mjs`; R29):
 * one tab per kind of relation the records name, each a section reached from the list at its head (an in-page link, so it
 * works without the script and prints whole), saying who records it (an act's own record, or derived from the publisher's
 * forward relation), then the kinds acquired in full that name no act, the kinds not acquired, and what the family does not
 * see. Nothing on an index of schema 9, 8 or 6.
 */
function PublisherRelations({ relations }) {
  if (relations === null) return null;
  if (relations.edgeCount === 0 && relations.families.every((family) => !family.acquired)) {
    return (
      <section data-relations="not_acquired">
        <h3>{COPY.relationsHeading}</h3>
        <p>{COPY.relationsNotAcquired}</p>
      </section>
    );
  }
  const listed = relations.groups.filter((group) => group.edges.length > 0);
  const none = relations.groups.filter((group) => group.edges.length === 0 && group.family.acquired);
  const notHeld = relations.groups.filter((group) => !group.family.acquired);
  const names = (groups) => groups.map((group, index) => (
    <Fragment key={group.view}>{index === 0 ? '' : COMMON.listSeparator}{COPY.relationGroups[group.view]}</Fragment>
  ));
  const classes = relationClasses(relations);
  return (
    <section data-relations={relations.edgeCount}>
      <h3>{COPY.relationsHeading}</h3>
      <p>{COPY.relationsIntro}</p>
      {listed.length > 0 ? (
        <nav aria-label={COPY.relationsNav}>
          <ul data-relation-tabs={listed.length}>
            {listed.map((group) => (
              <li key={group.view}>
                <a href={`#relations-${group.view}`}>
                  <Say template={COPY.relationTab} values={{ group: COPY.relationGroups[group.view], count: group.edges.length }} />
                </a>
              </li>
            ))}
          </ul>
        </nav>
      ) : null}
      {listed.map((group) => (
        <section key={group.view} id={`relations-${group.view}`} data-relation-group={group.view} data-derived={group.derived ? 'true' : 'false'}>
          <h4>{COPY.relationGroups[group.view]}</h4>
          {group.edgeType === 'impact' ? <p>{COPY.relationImpact}</p> : group.derived ? <p data-derived-label="">{COPY.relationDerived}</p> : null}
          <ul>
            {group.edges.map((edge, index) => <RelationRow key={`${index}.${edge.other.iri}`} edge={edge} />)}
          </ul>
        </section>
      ))}
      {none.length > 0 ? <p data-relations-none={none.length}><Say template={COPY.relationsNone} values={{ groups: names(none) }} /></p> : null}
      {notHeld.length > 0 ? <p data-relations-not-held={notHeld.length}><Say template={COPY.relationsNotHeldFor} values={{ groups: names(notHeld) }} /></p> : null}
      {classes === null ? null : (
        <p data-relations-not-seen="">
          {codesLawsRegulations(classes)
            ? COPY.relationsNotSeen
            : <Say template={COPY.relationsNotSeenClasses} values={{ classes: classCodes(classes) }} />}
        </p>
      )}
    </section>
  );
}

/**
 * R72's banner (index 10's `amended_after_consolidation`, read by `publisher-relations.mjs`): when the publisher's relation
 * records name an act published after every consolidated version in its catalogue, that act (or each such act) with its
 * publication date, that no consolidated version was published after it, the latest one's publication date or that there is
 * none, and that the notice is derived and names the act, never its effect. Nothing otherwise, and nothing on an index of
 * schema 9, 8 or 6.
 */
function AmendedAfterBanner({ amendedAfter, relations }) {
  if (amendedAfter === null || amendedAfter.status !== 'notice') return null;
  // The note says what the modifies family does not see, by that family's own classes; when it was not read in full, only that
  // an amending act whose record was not read is not seen (the French review of the relation tabs, point 3).
  const modifies = relations === null ? undefined : relations.families.find((family) => family.edgeType === 'modifies');
  const classes = modifies === undefined || !modifies.acquired ? null : modifies.subjectClasses;
  const [first] = amendedAfter.acts;
  return (
    <aside className="relation-notice" role="note" aria-label={COPY.amendedAfterHeading} data-amended-after={amendedAfter.acts.length}>
      <p><strong>{COPY.amendedAfterHeading}</strong></p>
      {amendedAfter.acts.length === 1 ? (
        <p>
          <Say template={COPY.amendedAfterOne} values={{
            date: <time dateTime={first.publicationDate}>{first.publicationDate}</time>,
            act: <OtherActName other={first.other} />,
          }} />
        </p>
      ) : (
        <>
          <p>{COPY.amendedAfterMany}</p>
          <ul>
            {amendedAfter.acts.map((act) => (
              <li key={act.actingIri}>
                <Say template={COPY.amendedAfterAct} values={{ act: <OtherActName other={act.other} />, date: <time dateTime={act.publicationDate}>{act.publicationDate}</time> }} />
              </li>
            ))}
          </ul>
        </>
      )}
      <p>
        {amendedAfter.latestConsolidationPublicationDate === null
          ? COPY.amendedAfterNoConsolidation
          : <Say template={COPY.amendedAfterLatest} values={{ date: <time dateTime={amendedAfter.latestConsolidationPublicationDate}>{amendedAfter.latestConsolidationPublicationDate}</time> }} />}
      </p>
      <p data-derived-label="">
        {classes === null
          ? COPY.amendedAfterNoteUnscoped
          : codesLawsRegulations(classes)
            ? COPY.amendedAfterNote
            : <Say template={COPY.amendedAfterNoteClasses} values={{ classes: classCodes(classes) }} />}
      </p>
    </aside>
  );
}

/**
 * One dossier view, laid out: the work, R72's banner when it is raised, its titles, its held states, today (`today`, the date
 * in Luxembourg the answer was given on, or null), its relations in the publisher's records and what it does not hold.
 */
export function DossierView({ view, today = null }) {
  return (
    <>
      <h2>{view.workKey}</h2>
      <AmendedAfterBanner amendedAfter={view.amendedAfter ?? null} relations={view.relations ?? null} />
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
      <PublisherRelations relations={view.relations ?? null} />
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
