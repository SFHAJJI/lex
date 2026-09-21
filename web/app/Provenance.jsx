// The provenance page, as React.
//
// Same split as the refusal card and the dossier: every rule stays in `scripts/provenance.mjs`
// and is applied by `readProvenance`. This file decides how a read `provenance_chain` looks and
// re-derives nothing. It computes no digest, no count and no absence; each arrives already
// decided, so a rule cannot be repaired in the string renderer while this one keeps the defect.
// That is not a style preference: the dossier carried its flag rules in both renderers, against
// this same header, and a change reached one copy and missed the other with nothing to say so.
//
// Two things are visible in the markup rather than in the validator, and both are load-bearing.
//
// A value the platform does not hold is printed as the sentence saying so, never as a blank cell.
// A blank reads as a fact about the publisher's document; the sentence is a fact about what this
// corpus retained, and the two are not the same claim.
//
// There is no stamp anywhere on this page, and that is deliberate rather than pending. The
// answer's own `not_held` carries a row saying no signature is held or made, and it is rendered
// with the others. The page this replaced printed "stamp signature valid: yes"; printing one here
// would assert authenticity the platform refuses to claim, and the only party available to mint a
// signature is us.

import { NOT_STATED, PROFILE_NOTE, narrowedNote, readProvenance } from '../scripts/provenance.mjs';

/** One labelled row, in the same layout the string renderer and every strip on the site use. */
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
  return <span className="provenance-not-stated">{NOT_STATED}</span>;
}

function Source({ source }) {
  return (
    <li className="provenance-source">
      <dl className="provenance-facts">
        <Row label="object reference"><Evidence value={source.object_ref_sha256} /></Row>
        <Row label="body digest">
          {source.body_sha256 === null ? <NotStated /> : <Evidence value={source.body_sha256} />}
        </Row>
        <Row label="body bytes">
          {source.body_byte_length === null ? <NotStated /> : String(source.body_byte_length)}
        </Row>
        <Row label="body receipt">
          {source.body_receipt_sha256 === null
            ? <NotStated />
            : <Evidence value={source.body_receipt_sha256} />}
        </Row>
        <Row label="outcome"><Evidence value={source.outcome} /></Row>
        <Row label="rights disposition">
          {source.rights_disposition === null
            ? <NotStated />
            : <Evidence value={source.rights_disposition} />}
        </Row>
        <Row label="gaps recorded">
          {source.gaps.length === 0
            ? 'none recorded'
            : source.gaps.map((gap) => <Evidence key={gap} value={gap} />)}
        </Row>
        <Row label="article outcomes">
          {source.article_outcomes.length === 0 ? 'none recorded' : (
            <ul className="provenance-outcomes">
              {source.article_outcomes.map((outcome) => (
                <li key={outcome.disposition}>
                  <Evidence value={outcome.disposition} />: {String(outcome.outcomes)}
                </li>
              ))}
            </ul>
          )}
        </Row>
      </dl>
    </li>
  );
}

function State({ state }) {
  return (
    <section className="provenance-state">
      <h3>
        {state.language}, applicable from {state.applicability_date}
      </h3>
      <dl className="provenance-facts">
        <Row label="state digest"><Evidence value={state.state_sha256} /></Row>
        <Row label="permalink"><Evidence value={state.permalink} /></Row>
        <Row label="stable coordinate"><Evidence value={state.stable_coordinate} /></Row>
        <Row label="expression"><Evidence value={state.expression_iri} /></Row>
        <Row label="publisher work"><Evidence value={state.publisher_work_iri} /></Row>
        <Row label="publisher legal resource">
          <Evidence value={state.publisher_legal_resource_iri} />
        </Row>
        <Row label="articles">{String(state.articles)}</Row>
        <Row label="article identities digest">
          <Evidence value={state.article_identities_sha256} />
        </Row>
        <Row label="rule profiles">
          {state.rule_profile_sha256s.map((profile) => <Evidence key={profile} value={profile} />)}
        </Row>
      </dl>
      <p className="provenance-profile-note">{PROFILE_NOTE}</p>
      <h4>The documents these articles came from</h4>
      <ul className="provenance-sources">
        {state.sources.map((source) => (
          <Source key={source.object_ref_sha256} source={source} />
        ))}
      </ul>
    </section>
  );
}

/** The provenance page. */
export function Provenance({ answer }) {
  const view = readProvenance(answer);
  return (
    <section className="provenance">
      <section className="provenance-block">
        <h2>What this page is about</h2>
        <p className="provenance-scope">{view.scope}</p>
        <dl className="provenance-facts">
          <Row label="publisher"><Evidence value={view.publisher} /></Row>
          <Row label="work"><Evidence value={view.workKey} /></Row>
          <Row label="identifier asked for"><Evidence value={view.requestedIdentifier} /></Row>
          <Row label="date asked for">{view.requestedDate}</Row>
          <Row label="languages held">
            {view.availableLanguages.map((language) => (
              <Evidence key={language} value={language} />
            ))}
          </Row>
        </dl>
        {view.requestedLanguage === null ? null : (
          <p className="provenance-narrowed">{narrowedNote(view.requestedLanguage)}</p>
        )}
      </section>
      <section className="provenance-block">
        <h2>The states this answer describes</h2>
        {view.states.map((state) => <State key={state.state_sha256} state={state} />)}
      </section>
      <section className="provenance-block">
        <h2>What the corpus recorded for each document</h2>
        <p className="provenance-sources-note">{view.sourcesNote}</p>
      </section>
      <section className="provenance-block">
        <h2>How the state digest is derived</h2>
        <p className="provenance-derivation">{view.derivation}</p>
      </section>
      <section className="provenance-block">
        <h2>What verified this answer</h2>
        <dl className="provenance-facts">
          <Row label="corpus"><Evidence value={view.verifiedBy.corpus_sha256} /></Row>
          <Row label="index"><Evidence value={view.verifiedBy.index_sha256} /></Row>
          <Row label="operation registry"><Evidence value={view.verifiedBy.registry_sha256} /></Row>
        </dl>
      </section>
      <section className="provenance-block">
        <h2>What this answer does not hold</h2>
        <ul className="provenance-not-held">
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
