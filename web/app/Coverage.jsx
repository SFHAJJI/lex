// The coverage page, as React.
//
// Same split as the refusal card, the dossier and the provenance page: every rule stays in
// `scripts/coverage.mjs` and is applied by `readCoverage`. This file decides how a read
// `coverage_report` looks. It sums nothing and bounds nothing; each count and each relation arrives
// already decided, so a rule cannot be repaired in the string renderer while this one keeps the
// defect.
//
// It does decide SEVEN absences -- no language row, no gap token, no measured capability, no
// unrouted operation, no unserved capability, no retention line, no build recorded in the mounted
// log -- and an earlier header claimed it decided none. Each
// is a branch on a list being empty, and each branch's SENTENCE is imported rather than written
// here, because the page's own prose was the last thing left in two copies. One of those sentences
// was false on an answer the producer really sends, and the parity test that was supposed to hold
// the copies level compares them after tags are stripped: it reports that two renderers agree and
// cannot report that both are wrong.
//
// THAT IS THE DEFECT THIS FILE USED TO HAVE. Its header said so plainly -- "the guards below also
// exist in `scripts/coverage.mjs`" -- and named the reason, which was that the module exported no
// validator. It exports one now, so the two copies are gone rather than held level by a parity test
// that fed both renderers the same inputs and asserted they refused the same way. A parity test can
// only compare the rules that exist in both places; it cannot notice one that was added to neither.
//
// One thing lives in the markup rather than in the validator, and it is load-bearing: adjacent
// evidence values carry a space between them. React puts none there, the string renderer joins with
// one, and two identifiers run together are one identifier a reader cannot look up.
//
// The answer's own sentences (its scope, its notes, the data an unserved operation needs, the reason
// each thing is not held) are the platform's English, shown as sent and marked English on a page in
// another language (`ENGLISH_LANG`, `inEnglish`), so a screen reader does not read them in its voice.
//
// There is no build instant on the counts anywhere on this page, and no retention sentence, and both
// are deliberate rather than pending. The answer's own `not_held` carries a row saying it states no build
// time and another saying no observation time is held, and both are rendered with the rest. The page
// this replaced stamped `Counts as of index build <instant>.` into its body and both its captions,
// and printed `Observation history begins August 2026`. The calendar dates that remain -- each
// language's state range and each measured capability's period -- are the publisher's facts about
// the law, not a claim about when the counting happened, and the note above the counts says so.
// The V2 page's qualification of the last state date is restored under the table, in words that do
// not assume a present date this mount does not hold.
//
// The one place an instant appears is the history section, and it is not a date of the counts: the
// answer's `history` lists the builds the mounted log records, each with its build time, and its own
// note says every such time is "a build's, an upper bound on observation, never an observation time".
// Each is printed verbatim beside the build it belongs to, and the note is printed under them.

import { Fragment } from 'react';

import { Say, inEnglish } from './LiveAnswer.jsx';
import { ENGLISH_LANG, fillText } from '../scripts/live-chrome.mjs';
import {
  COVERAGE_COLUMNS,
  COVERAGE_COPY,
  NOT_STATED,
  capabilityAbsence,
  europeLanguagesSentence,
  gapsSentence,
  narrowedNote,
  publisherSiteLabel,
  readCoverage,
  scopeClassLabel,
  servedSentence,
  textYieldAnnexRows,
  textYieldClassLabel,
  textYieldClassRows,
  textYieldPublishersWithoutAnnexes,
  textYieldPublishersWithoutFileFormat,
  textYieldReasonRows,
  textYieldSentences,
  unservedCapabilities,
  unservedCapabilityNote,
} from '../scripts/coverage.mjs';

/** One labelled row, in the same layout every strip on the site uses. */
function Row({ label, children }) {
  return (
    <div className="strip-row">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

/** A value that is evidence: whole, selectable, never truncated for display. */
function Evidence({ value }) {
  return <code>{value}</code>;
}

/**
 * A run of evidence values with a space between them, which React does not put there.
 *
 * `{list.map(...)}` emits `<code>fra</code><code>deu</code>` with nothing between, and the string
 * renderer joins the same list with a space. The parity test could not see the difference, because
 * it replaced every tag with a space before comparing, so `fra deu` and `fradeu` read alike to it.
 * `styles.css` already records this defect shipping once, on another surface: "Chrome rendered ...
 * as one word". Two identifiers run together are one identifier a reader cannot look up.
 */
function Spaced({ values }) {
  return values.map((value, index) => (
    <Fragment key={value}>
      {index === 0 ? null : ' '}
      <Evidence value={value} />
    </Fragment>
  ));
}

/**
 * A table in its own scroll box.
 *
 * The box is keyboard focusable whether or not it asks to be, because a scrollable region is, so it
 * carries a role and an accessible name rather than becoming a tab stop that announces nothing. The
 * caption says what the table counts and not when it was counted, because nothing on this answer
 * says when.
 */
function FacetTable({ caption, head, children, copy }) {
  return (
    <div className="coverage-scroll" role="region" tabIndex={0} aria-label={fillText(copy.scrollable, { caption })}>
      <table className="coverage-table">
        <caption>{caption}</caption>
        <thead>
          <tr>
            {head.map((heading) => (
              <th scope="col" key={heading}>
                {heading}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

function LanguageRow({ language, copy }) {
  return (
    <tr>
      <td>{language.language}</td>
      <td>{language.works}</td>
      <td>{language.states}</td>
      <td>{language.articles}</td>
      <td>{copy.held[language.searchable_text_held]}</td>
      <td>{language.articles_with_searchable_text}</td>
      <td>{language.articles_without_publisher_date}</td>
      <td>{language.first_state_date}</td>
      <td>{language.last_state_date}</td>
    </tr>
  );
}

/** Tokens with a comma between them, as the string renderer joins them. */
function Listed({ values }) {
  return values.map((value, index) => (
    <Fragment key={value}>
      {index === 0 ? null : ', '}
      <Evidence value={value} />
    </Fragment>
  ));
}

/**
 * The mounted log's history, or nothing when no Luxembourg index is mounted. Each time in it is a
 * build's, printed verbatim beside the build it belongs to, with the platform's note under it.
 */
function History({ history, copy }) {
  if (history === null) return null;
  if (!history.logRecordsBuilds) {
    return (
      <section className="coverage-block">
        <h2>{copy.headings.history}</h2>
        <p className="coverage-held">{copy.noBuildsRecorded}</p>
        <p className="coverage-note" lang={ENGLISH_LANG}>{history.note}</p>
      </section>
    );
  }
  const policy = history.retentionPolicy;
  return (
    <section className="coverage-block">
      <h2>{copy.headings.history}</h2>
      <dl className="coverage-facts">
        <Row label={copy.facts.snapshotsInLog}>{String(history.snapshotsInLog)}</Row>
        <Row label={copy.facts.historyBegins}>{history.historyBegins}</Row>
        {policy === null ? (
          <Row label={copy.facts.retentionPolicy}>{copy.noRetentionPolicy}</Row>
        ) : (
          <>
            <Row label={copy.facts.retentionPolicy}><Evidence value={policy.id} /></Row>
            <Row label={copy.facts.nightlyDays}>{String(policy.nightlyDays)}</Row>
            <Row label={copy.facts.retentionEvaluatedAt}>{policy.evaluatedAt}</Row>
          </>
        )}
        <Row label={copy.facts.snapshotsWithoutText}>{String(history.snapshotsWithoutText)}</Row>
      </dl>
      <FacetTable caption={copy.captions.snapshots} head={heads('snapshots', copy)} copy={copy}>
        {history.snapshotsWithText.map((snapshot) => (
          <tr key={snapshot.snapshotId}>
            <td><Evidence value={snapshot.snapshotId} /></td>
            <td>{snapshot.observation}</td>
            <td>{snapshot.builtAt}</td>
            <td><Listed values={snapshot.retainedAs} /></td>
          </tr>
        ))}
      </FacetTable>
      <p className="coverage-note" lang={ENGLISH_LANG}>{history.note}</p>
    </section>
  );
}

/** A class's cell: its name where the copy has one, and always its tokens as the data sends them. */
function ClassCell({ row, copy }) {
  const label = textYieldClassLabel(row, copy);
  const tokens = row.family === null
    ? <Evidence value={row.publisher} />
    : <Spaced values={[row.publisher, `${row.family}/${row.kind}`]} />;
  return label === null ? tokens : <>{label}{' '}{tokens}</>;
}

/**
 * The text yield per class (R86(4)): R26's sentence, the classes with each publisher's own row, why a
 * version or a file has no text, the annex counts and the platform's note; for a mount whose build
 * reported none, the sentence saying so and the note. Every row and sentence is decided in
 * `coverage.mjs`, as the string renderer's are.
 */
function TextYield({ view, copy }) {
  const textYield = view.textYield;
  if (textYield === null) {
    return (
      <section className="coverage-block">
        <h2>{copy.headings.textYield}</h2>
        <p className="coverage-held">{copy.noTextYield}</p>
        <p className="coverage-note" lang={ENGLISH_LANG}>{view.textYieldNote}</p>
      </section>
    );
  }
  const reasons = textYieldReasonRows(textYield);
  const annexes = textYieldAnnexRows(textYield);
  const keyOf = (row) => [row.publisher, row.family, row.kind, row.outcome, row.reason].join('\u0000');
  return (
    <section className="coverage-block">
      <h2>{copy.headings.textYield}</h2>
      {textYieldSentences(textYield, copy).map((sentence) => (
        <p className="coverage-held" data-text-yield-sentence="" key={sentence}>{sentence}</p>
      ))}
      <dl className="coverage-facts">
        <Row label={copy.facts.textYieldBlock}><Evidence value={textYield.schema} /></Row>
      </dl>
      <FacetTable caption={copy.captions.yieldClasses} head={heads('yieldClasses', copy)} copy={copy}>
        {textYieldClassRows(textYield).map((row) => (
          <tr key={keyOf(row)}>
            <td><ClassCell row={row} copy={copy} /></td>
            {row.cells.map((value, index) => (
              <td key={COVERAGE_COLUMNS.yieldClasses[index + 1]}>{value === null ? copy.notRecorded : value}</td>
            ))}
          </tr>
        ))}
      </FacetTable>
      {reasons.length === 0 ? (
        <p className="coverage-note">{copy.noTextYieldReasons}</p>
      ) : (
        <FacetTable caption={copy.captions.yieldReasons} head={heads('yieldReasons', copy)} copy={copy}>
          {reasons.map((reason) => (
            <tr key={keyOf(reason)}>
              <td><ClassCell row={reason} copy={copy} /></td>
              <td>{copy.textYieldOutcomes[reason.outcome]}</td>
              <td>{reason.reason === null ? <span className="coverage-not-stated">{NOT_STATED}</span> : <Evidence value={reason.reason} />}</td>
              <td>{reason.count}</td>
            </tr>
          ))}
        </FacetTable>
      )}
      {annexes.length === 0 ? null : (
        <FacetTable caption={copy.captions.yieldAnnexes} head={heads('yieldAnnexes', copy)} copy={copy}>
          {annexes.map((annex) => (
            <tr key={keyOf(annex)}>
              <td><ClassCell row={annex} copy={copy} /></td>
              {annex.cells.map((value, index) => (
                <td key={COVERAGE_COLUMNS.yieldAnnexes[index + 1]}>{value}</td>
              ))}
            </tr>
          ))}
        </FacetTable>
      )}
      {textYieldPublishersWithoutFileFormat(textYield).map((publisher) => (
        <p className="coverage-note" key={`format-${publisher}`}>{copy.noFileFormat[publisher]}</p>
      ))}
      {textYieldPublishersWithoutAnnexes(textYield).map((publisher) => (
        <p className="coverage-note" key={publisher}>{copy.noAnnexOutcomes[publisher]}</p>
      ))}
      <p className="coverage-note" lang={ENGLISH_LANG}>{view.textYieldNote}</p>
    </section>
  );
}

/**
 * What the mount declares beside its counts (R12, R82, R78), or nothing on an answer that carries none: the sentences and the
 * table `coverage.mjs` decides, as the string renderer lays them out.
 */
function DeclaredScope({ view, copy }) {
  const declared = view.declaredScope;
  if (declared === null) return null;
  const languages = europeLanguagesSentence(declared, copy);
  const columns = copy.observationColumns;
  return (
    <section className="coverage-block">
      <h2>{copy.declaredHeading}</h2>
      <FacetTable caption={copy.observationsCaption} head={[columns.publisher, columns.begins, columns.basis]} copy={copy}>
        {declared.observationsBegin.map((begins) => (
          <tr key={begins.publisher}>
            <td>{publisherSiteLabel(begins.publisher, copy)}{' '}<Evidence value={begins.publisher} /></td>
            <td>{begins.date === null ? <span className="coverage-not-stated">{NOT_STATED}</span> : begins.date}</td>
            <td>{copy.observationBases[begins.basis]}{' '}<Evidence value={begins.basis} /></td>
          </tr>
        ))}
      </FacetTable>
      {languages === null ? null : <p className="coverage-held" data-europe-languages="">{languages}</p>}
      <FacetTable caption={copy.classesCaption} head={[copy.classColumns.scopeClass, copy.classColumns.scopeDisposition]} copy={copy}>
        {declared.classes.map((row) => {
          const label = scopeClassLabel(row, copy);
          return (
            <tr key={row.class}>
              <td>{label === null ? <Evidence value={row.class} /> : <>{label}{' '}<Evidence value={row.class} /></>}</td>
              <td>{copy.classDispositions[row.disposition]}{' '}<Evidence value={row.disposition} /></td>
            </tr>
          );
        })}
      </FacetTable>
      <p className="coverage-note">{copy.classesHeld}</p>
      <p className="coverage-note" lang={ENGLISH_LANG}>{declared.rule}</p>
    </section>
  );
}

/** A table's column headings, in its order, in the copy's words. */
function heads(table, copy) {
  return COVERAGE_COLUMNS[table].map((key) => copy.columns[key]);
}

/**
 * The coverage page, in the words of `copy`: English from `coverage.mjs` unless a page passes its
 * chrome table's.
 */
export function Coverage({ answer, copy = COVERAGE_COPY }) {
  const view = readCoverage(answer);
  const unserved = unservedCapabilities(view);
  return (
    <section className="coverage">
      <section className="coverage-block">
        <h2>{copy.headings.about}</h2>
        <p className="coverage-scope" lang={ENGLISH_LANG}>{view.scope}</p>
        <dl className="coverage-facts">
          <Row label={copy.facts.publisher}><Evidence value={view.mounted.publisher} /></Row>
          <Row label={copy.facts.corpus}><Evidence value={view.mounted.corpus_sha256} /></Row>
          <Row label={copy.facts.index}><Evidence value={view.mounted.index_sha256} /></Row>
          <Row label={copy.facts.registry}><Evidence value={view.mounted.registry_sha256} /></Row>
        </dl>
        <p className="coverage-note">{copy.countsProvenance}</p>
      </section>
      <section className="coverage-block">
        <h2>{copy.headings.counted}</h2>
        <p className="coverage-note" lang={ENGLISH_LANG}>{view.countsNote}</p>
      </section>
      <section className="coverage-block">
        <h2>{copy.headings.holds}</h2>
        <dl className="coverage-facts">
          <Row label={copy.facts.works}>{String(view.totals.works)}</Row>
          <Row label={copy.facts.states}>{String(view.totals.states)}</Row>
          <Row label={copy.facts.articles}>{String(view.totals.articles)}</Row>
          <Row label={copy.facts.members}>{String(view.totals.members)}</Row>
        </dl>
        {view.requestedLanguage === null ? null : (
          <p className="coverage-note">{narrowedNote(view.requestedLanguage, copy)}</p>
        )}
        <p className="coverage-held">
          <Say template={copy.languagesHeld} values={{ languages: <Spaced values={view.languagesHeld} /> }} />
        </p>
        {view.languages.length === 0 ? (
          <p className="coverage-note">{copy.noLanguageRows}</p>
        ) : (
          <FacetTable caption={copy.captions.languages} head={heads('languages', copy)} copy={copy}>
            {view.languages.map((language) => (
              <LanguageRow key={language.language} language={language} copy={copy} />
            ))}
          </FacetTable>
        )}
        {view.languages.length === 0 ? null : (
          <p className="coverage-note">{copy.stateRange}</p>
        )}
      </section>
      <TextYield view={view} copy={copy} />
      <DeclaredScope view={view} copy={copy} />
      <section className="coverage-block">
        <h2>{copy.headings.recorded}</h2>
        <FacetTable caption={copy.captions.outcomes} head={heads('outcomes', copy)} copy={copy}>
          {view.members.byOutcome.map((outcome) => (
            <tr key={outcome.outcome}>
              <td><Evidence value={outcome.outcome} /></td>
              <td>{outcome.members}</td>
            </tr>
          ))}
        </FacetTable>
        <p className="coverage-held">
          {gapsSentence(view.members.withGaps, view.totals.members, copy)}
        </p>
        {view.members.gaps.length === 0 ? (
          <p className="coverage-note">{copy.noGapTokens}</p>
        ) : (
          <FacetTable caption={copy.captions.gaps} head={heads('gaps', copy)} copy={copy}>
            {view.members.gaps.map((gap) => (
              <tr key={gap.gap}>
                <td><Evidence value={gap.gap} /></td>
                <td>{gap.members}</td>
              </tr>
            ))}
          </FacetTable>
        )}
        <p className="coverage-note" lang={ENGLISH_LANG}>{view.members.gapsNote}</p>
        {view.members.articleOutcomes.length === 0 ? (
          <p className="coverage-note">{copy.noArticleOutcomes}</p>
        ) : (
          <FacetTable caption={copy.captions.articleOutcomes} head={heads('articleOutcomes', copy)} copy={copy}>
            {view.members.articleOutcomes.map((outcome) => (
              <tr key={outcome.disposition}>
                <td><Evidence value={outcome.disposition} /></td>
                <td>{outcome.outcomes}</td>
              </tr>
            ))}
          </FacetTable>
        )}
        <p className="coverage-note" lang={ENGLISH_LANG}>{view.members.articleOutcomesNote}</p>
      </section>
      <section className="coverage-block">
        <h2>{copy.headings.asked}</h2>
        <p className="coverage-held">
          {servedSentence(view.operations.served.length, view.operations.registered, copy)}
        </p>
        <dl className="coverage-facts">
          <Row label={copy.facts.answered}><Spaced values={view.operations.served} /></Row>
          <Row label={copy.facts.notRouted}>
            {view.operations.notServed.length === 0
              ? copy.none
              : <Spaced values={view.operations.notServed} />}
          </Row>
        </dl>
        {view.operations.notServedData.length === 0 ? null : (
          <FacetTable caption={copy.captions.notServedData} head={heads('notServedData', copy)} copy={copy}>
            {view.operations.notServedData.map((row) => (
              <tr key={row.operation}>
                <td><Evidence value={row.operation} /></td>
                <td><Evidence value={row.reason} /></td>
                <td><Evidence value={row.requestedMode} /></td>
                <td lang={ENGLISH_LANG}>{row.dataNeeded}</td>
              </tr>
            ))}
          </FacetTable>
        )}
        <p className="coverage-note" lang={ENGLISH_LANG}>{view.operations.note}</p>
      </section>
      <History history={view.history} copy={copy} />
      <section className="coverage-block">
        <h2>{copy.headings.measured}</h2>
        {view.capabilityCells.length === 0 ? (
          <p className="coverage-note">{capabilityAbsence(view.requestedLanguage, copy)}</p>
        ) : (
          <FacetTable caption={copy.captions.capabilities} head={heads('capabilities', copy)} copy={copy}>
            {view.capabilityCells.map((measured) => (
              <tr
                key={[measured.operation, measured.column, measured.field, measured.language,
                  measured.period_from, measured.period_to].join('\u0000')}
              >
                <td><Evidence value={measured.operation} /></td>
                <td><Evidence value={measured.column} /></td>
                <td><Evidence value={measured.field} /></td>
                <td><Evidence value={measured.language} /></td>
                <td>{measured.period_from}</td>
                <td>{measured.period_to}</td>
                <td>{measured.population}</td>
              </tr>
            ))}
          </FacetTable>
        )}
        {unserved.length === 0 ? null : (
          <p className="coverage-note">{unservedCapabilityNote(unserved, copy)}</p>
        )}
      </section>
      <section className="coverage-block">
        <h2>{copy.headings.notHeld}</h2>
        <ul className="coverage-not-held">
          {view.notHeld.map((held) => (
            <li key={held.item}>
              <Say template={copy.notHeldRow} values={{ item: <Evidence value={held.item} />, reason: inEnglish(held.reason) }} />
            </li>
          ))}
        </ul>
      </section>
    </section>
  );
}
