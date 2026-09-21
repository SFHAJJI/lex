// The coverage page, as React.
//
// Same split as the refusal card, the dossier and the provenance page: every rule stays in
// `scripts/coverage.mjs` and is applied by `readCoverage`. This file decides how a read
// `coverage_report` looks and re-derives nothing. It sums nothing, bounds nothing and decides no
// absence; each arrives already decided, so a rule cannot be repaired in the string renderer while
// this one keeps the defect.
//
// THAT IS THE DEFECT THIS FILE USED TO HAVE. Its header said so plainly -- "the guards below also
// exist in `scripts/coverage.mjs`" -- and named the reason, which was that the module exported no
// validator. It exports one now, so the two copies are gone rather than held level by a parity test
// that fed both renderers the same inputs and asserted they refused the same way. A parity test can
// only compare the rules that exist in both places; it cannot notice one that was added to neither.
//
// Two things live in the markup rather than in the validator, and both are load-bearing.
//
// A date the platform does not hold is printed as the sentence saying so, never as a blank cell. A
// blank reads as a fact about the corpus; the sentence is a fact about what the platform said.
//
// There is no build instant anywhere on this page, and no retention sentence, and both are
// deliberate rather than pending. The answer's own `not_held` carries a row saying no build time is
// held and another saying no observation time is, and both are rendered with the rest. The page
// this replaced stamped `Counts as of index build <instant>.` into its body and both its captions,
// and printed `Observation history begins August 2026`. The calendar dates that remain -- each
// language's state range and each measured capability's period -- are the publisher's facts about
// the law, not a claim about when the counting happened, and the note above the counts says so.

import {
  HELD,
  NOT_STATED,
  narrowedNote,
  readCoverage,
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

/** What the platform does not state, said out loud in the cell where the value would be. */
function NotStated() {
  return <span className="coverage-not-stated">{NOT_STATED}</span>;
}

/**
 * A table in its own scroll box.
 *
 * The box is keyboard focusable whether or not it asks to be, because a scrollable region is, so it
 * carries a role and an accessible name rather than becoming a tab stop that announces nothing. The
 * caption says what the table counts and not when it was counted, because nothing on this answer
 * says when.
 */
function FacetTable({ caption, head, children }) {
  return (
    <div className="coverage-scroll" role="region" tabIndex={0} aria-label={`${caption}, scrollable`}>
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

function LanguageRow({ language }) {
  return (
    <tr>
      <td>{language.language}</td>
      <td>{language.works}</td>
      <td>{language.states}</td>
      <td>{language.articles}</td>
      <td>{HELD[language.searchable_text_held]}</td>
      <td>{language.articles_with_searchable_text}</td>
      <td>{language.articles_without_publisher_date}</td>
      <td>{language.first_state_date === null ? <NotStated /> : language.first_state_date}</td>
      <td>{language.last_state_date === null ? <NotStated /> : language.last_state_date}</td>
    </tr>
  );
}

/** The coverage page. */
export function Coverage({ answer }) {
  const view = readCoverage(answer);
  const unserved = unservedCapabilities(view);
  return (
    <section className="coverage">
      <section className="coverage-block">
        <h2>What this page is about</h2>
        <p className="coverage-scope">{view.scope}</p>
        <dl className="coverage-facts">
          <Row label="publisher"><Evidence value={view.mounted.publisher} /></Row>
          <Row label="corpus"><Evidence value={view.mounted.corpus_sha256} /></Row>
          <Row label="index"><Evidence value={view.mounted.index_sha256} /></Row>
          <Row label="operation registry"><Evidence value={view.mounted.registry_sha256} /></Row>
        </dl>
        <p className="coverage-note">
          {'These counts were taken from the corpus and index named above. Nothing here says when '
            + 'they were taken: no build time of either is held. The digests say exactly which '
            + 'artifacts were counted, which a date does not. The calendar dates further down are '
            + 'the publisher’s, about the law, and not about when this was counted.'}
        </p>
      </section>
      <section className="coverage-block">
        <h2>How these counts are counted</h2>
        <p className="coverage-note">{view.countsNote}</p>
      </section>
      <section className="coverage-block">
        <h2>What this mount holds</h2>
        <dl className="coverage-facts">
          <Row label="works">{String(view.totals.works)}</Row>
          <Row label="states">{String(view.totals.states)}</Row>
          <Row label="articles">{String(view.totals.articles)}</Row>
          <Row label="members">{String(view.totals.members)}</Row>
        </dl>
        {view.requestedLanguage === null ? null : (
          <p className="coverage-note">{narrowedNote(view.requestedLanguage)}</p>
        )}
        <p className="coverage-held">
          Languages held:{' '}
          {view.languagesHeld.map((language) => (
            <Evidence key={language} value={language} />
          ))}
        </p>
        {view.languages.length === 0 ? (
          <p className="coverage-note">
            No language has a row here, so nothing below breaks these totals down.
          </p>
        ) : (
          <FacetTable
            caption="Held works, states and articles by language"
            head={['language', 'works', 'states', 'articles', 'searchable text held',
              'articles with searchable text', 'articles with no publisher date', 'first state',
              'last state']}
          >
            {view.languages.map((language) => (
              <LanguageRow key={language.language} language={language} />
            ))}
          </FacetTable>
        )}
      </section>
      <section className="coverage-block">
        <h2>What the corpus recorded for its members</h2>
        <FacetTable caption="Members by the outcome the corpus recorded" head={['outcome', 'members']}>
          {view.members.byOutcome.map((outcome) => (
            <tr key={outcome.outcome}>
              <td><Evidence value={outcome.outcome} /></td>
              <td>{outcome.members}</td>
            </tr>
          ))}
        </FacetTable>
        <p className="coverage-held">
          {`${view.members.withGaps} of ${view.totals.members} members recorded a gap.`}
        </p>
        {view.members.gaps.length === 0 ? (
          <p className="coverage-note">
            No gap token is counted here, so where a member above recorded a gap this page cannot
            say which.
          </p>
        ) : (
          <FacetTable
            caption="Gap tokens the corpus recorded, counted by member"
            head={['gap', 'members']}
          >
            {view.members.gaps.map((gap) => (
              <tr key={gap.gap}>
                <td><Evidence value={gap.gap} /></td>
                <td>{gap.members}</td>
              </tr>
            ))}
          </FacetTable>
        )}
        <p className="coverage-note">{view.members.gapsNote}</p>
      </section>
      <section className="coverage-block">
        <h2>What can be asked of this mount</h2>
        <p className="coverage-held">
          {`${view.operations.served.length} of ${view.operations.registered} registered operations `
            + 'are answered here.'}
        </p>
        <dl className="coverage-facts">
          <Row label="answered">
            {view.operations.served.map((operation) => (
              <Evidence key={operation} value={operation} />
            ))}
          </Row>
          <Row label="registered, with no route on this mount">
            {view.operations.notServed.length === 0
              ? 'none'
              : view.operations.notServed.map((operation) => (
                <Evidence key={operation} value={operation} />
              ))}
          </Row>
        </dl>
        <p className="coverage-note">{view.operations.note}</p>
      </section>
      <section className="coverage-block">
        <h2>What this mount measured it can answer</h2>
        {view.capabilityCells.length === 0 ? (
          <p className="coverage-note">
            No capability was measured, so nothing here says what this mount can be asked of any
            period.
          </p>
        ) : (
          <FacetTable
            caption="Measured capabilities, by operation, column, field, language and period"
            head={['operation', 'column', 'field', 'language', 'from', 'to', 'population']}
          >
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
          <p className="coverage-note">{unservedCapabilityNote(unserved)}</p>
        )}
      </section>
      <section className="coverage-block">
        <h2>What this mount does not hold</h2>
        <ul className="coverage-not-held">
          {view.notHeld.map((held) => (
            <li key={held.item}>
              <Evidence value={held.item} />: {held.reason}
            </li>
          ))}
        </ul>
      </section>
    </section>
  );
}
