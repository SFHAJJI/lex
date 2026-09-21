// The coverage page, as React.
//
// Same split as the refusal card, the dossier and the provenance page: every rule stays in
// `scripts/coverage.mjs` and is applied by `readCoverage`. This file decides how a read
// `coverage_report` looks. It sums nothing and bounds nothing; each count and each relation arrives
// already decided, so a rule cannot be repaired in the string renderer while this one keeps the
// defect.
//
// It does decide FIVE absences -- no language row, no gap token, no measured capability, no
// unrouted operation, no unserved capability -- and an earlier header claimed it decided none. Each
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
// There is no build instant anywhere on this page, and no retention sentence, and both are
// deliberate rather than pending. The answer's own `not_held` carries a row saying no build time is
// held and another saying no observation time is, and both are rendered with the rest. The page
// this replaced stamped `Counts as of index build <instant>.` into its body and both its captions,
// and printed `Observation history begins August 2026`. The calendar dates that remain -- each
// language's state range and each measured capability's period -- are the publisher's facts about
// the law, not a claim about when the counting happened, and the note above the counts says so.
// The V2 page's qualification of the last state date is restored under the table, in words that do
// not assume a present date this mount does not hold.

import { Fragment } from 'react';

import {
  COUNTS_PROVENANCE_NOTE,
  HELD,
  NO_ARTICLE_OUTCOMES,
  NO_GAP_TOKENS,
  NO_LANGUAGE_ROWS,
  STATE_RANGE_NOTE,
  capabilityAbsence,
  gapsSentence,
  narrowedNote,
  readCoverage,
  servedSentence,
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
      <td>{language.first_state_date}</td>
      <td>{language.last_state_date}</td>
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
        <p className="coverage-note">{COUNTS_PROVENANCE_NOTE}</p>
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
          Languages held: <Spaced values={view.languagesHeld} />
        </p>
        {view.languages.length === 0 ? (
          <p className="coverage-note">{NO_LANGUAGE_ROWS}</p>
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
        {view.languages.length === 0 ? null : (
          <p className="coverage-note">{STATE_RANGE_NOTE}</p>
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
          {gapsSentence(view.members.withGaps, view.totals.members)}
        </p>
        {view.members.gaps.length === 0 ? (
          <p className="coverage-note">{NO_GAP_TOKENS}</p>
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
        {view.members.articleOutcomes.length === 0 ? (
          <p className="coverage-note">{NO_ARTICLE_OUTCOMES}</p>
        ) : (
          <FacetTable
            caption="Legal-content outcomes the corpus recorded, by disposition"
            head={['disposition', 'outcomes']}
          >
            {view.members.articleOutcomes.map((outcome) => (
              <tr key={outcome.disposition}>
                <td><Evidence value={outcome.disposition} /></td>
                <td>{outcome.outcomes}</td>
              </tr>
            ))}
          </FacetTable>
        )}
        <p className="coverage-note">{view.members.articleOutcomesNote}</p>
      </section>
      <section className="coverage-block">
        <h2>What can be asked of this mount</h2>
        <p className="coverage-held">
          {servedSentence(view.operations.served.length, view.operations.registered)}
        </p>
        <dl className="coverage-facts">
          <Row label="answered"><Spaced values={view.operations.served} /></Row>
          <Row label="registered, with no route on this mount">
            {view.operations.notServed.length === 0
              ? 'none'
              : <Spaced values={view.operations.notServed} />}
          </Row>
        </dl>
        <p className="coverage-note">{view.operations.note}</p>
      </section>
      <section className="coverage-block">
        <h2>What this mount measured it can answer</h2>
        {view.capabilityCells.length === 0 ? (
          <p className="coverage-note">{capabilityAbsence(view.requestedLanguage)}</p>
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
