// The provenance page, as React.
//
// Same split as the refusal card and the dossier: every rule stays in `scripts/provenance.mjs`
// and is applied by `readProvenance`. This file decides how a read provenance record looks and
// re-derives nothing. It computes no digest, no count, no interval and no disclosure; each of
// those arrives already decided, so a rule cannot be repaired in the string renderer while this
// one keeps the defect.
//
// Three things are visible in the markup rather than in the validator, and all three are
// load-bearing.
//
// The publisher's title carries the record's own language, never the chrome's. The chrome here
// is English about a French or English law, and a screen reader handed the wrong tag reads one
// language in the voice of another.
//
// The legal interval is phrased by `INTERVAL_SENTENCE`, keyed by the semantics the validator
// derived from the record's publisher. Nothing in this file spells an interval, because the
// Union does not date applicability and Luxembourg does, and a component that composed the
// sentence itself would eventually compose the wrong one.
//
// The stamp sits in the section about the build, not the section about the record, and carries
// the sentence saying the payload never states what it signs. Moving it up beside the digests
// would turn a fact about the index into a claim about the bytes of one law.

import { STAMP_SCOPE_NOTE, PROFILE_NOTE, readProvenance } from '../scripts/provenance.mjs';
import { Mark, RefusalCard } from './RefusalCard.jsx';

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

function Chain({ events }) {
  return (
    <ol className="provenance-events">
      {events.map((event, index) => (
        // Two events can arrive at the same instant with the same name, which the live Union
        // record does, so the key is the position rather than a composite that would collide.
        <li className="provenance-event" key={`${index}:${event.event}:${event.observed_from}`}>
          <dl>
            <Row label="event">
              <Evidence value={event.event} />
            </Row>
            <Row label="scope">
              <Evidence value={event.scope} />
            </Row>
            <Row label="observed">
              <Evidence value={event.observed_from} />
            </Row>
            <Row label="detail">
              {event.detail === null ? 'not recorded' : <Evidence value={event.detail} />}
            </Row>
          </dl>
        </li>
      ))}
    </ol>
  );
}

function History({ observations }) {
  return (
    <ol className="provenance-observations">
      {observations.map((observation, index) => (
        <li
          className="provenance-observation"
          key={`${index}:${observation.sha256}:${observation.observed_from}`}
        >
          <dl>
            <Row label="language">
              <Evidence value={observation.language} />
            </Row>
            <Row label="expression dated">
              <Evidence value={observation.expr_valid_from} />
            </Row>
            <Row label="sha256">
              <Evidence value={observation.sha256} />
            </Row>
            <Row label="observed from">
              <Evidence value={observation.observed_from} />
            </Row>
            <Row label="observed to">
              {observation.observed_to === null ? (
                'still held'
              ) : (
                <Evidence value={observation.observed_to} />
              )}
            </Row>
            <Row label="is this record’s body">
              {observation.is_record_body
                ? 'yes, this digest is the one the record names'
                : 'no'}
            </Row>
          </dl>
        </li>
      ))}
    </ol>
  );
}

/**
 * The provenance page for one record, or the honest refusal when there is none.
 *
 * @param {object} props the same shape `readProvenance` takes: `requested`, `record`, `holdings`
 */
export function Provenance({ requested, record, holdings }) {
  const view = readProvenance({ requested, record, holdings });

  if (view.kind === 'refusal') {
    return (
      <section className="provenance provenance-refused">
        <h2>No record at this identifier</h2>
        <p className="provenance-asked">
          This page was asked about <Evidence value={view.requestedLexId} />.
        </p>
        <RefusalCard code={view.code} sentence={view.sentence} payload={view.payload} />
        {/* The card the React runtime ships does not lay out payload members, and the
            population disclosure is the one this refusal is required to carry: a reader told a
            record is absent has to be told the size of what was searched. It is rendered from
            the same validated payload rather than written again here. */}
        <p className="provenance-note">{view.payload.population_disclosure}</p>
      </section>
    );
  }

  return (
    <section className="provenance">
      <section className="provenance-block">
        <h2>The record this page is about</h2>
        <p className="provenance-title" lang={view.titleLanguage}>
          {view.title}
        </p>
        <dl>
          <Row label="lex_id">
            <Evidence value={view.document.lex_id} />
          </Row>
          <Row label="publisher">
            <Evidence value={view.identity.publisher} />
          </Row>
          <Row label="work">
            <Evidence value={view.identity.work} />
          </Row>
          <Row label="version key">
            <Evidence value={view.document.version_key} />
          </Row>
          <Row label="document type">
            <Evidence value={view.documentType} />
          </Row>
          <Row label="language of this record">
            <Evidence value={view.titleLanguage} />
          </Row>
          <Row label="publisher name for the work">
            {/* A name, not a link: both publishers issue these identifiers over http. */}
            <Evidence value={view.workIdentifier} />
          </Row>
          <Row label="this service’s address for it">
            <Evidence value={view.permalink} />
          </Row>
          <Row label="withdrawn by the publisher">{view.withdrawn ? 'yes' : 'no'}</Row>
          <Row label="extraction profile">
            {view.extractionProfile === null ? (
              'not recorded'
            ) : (
              <Evidence value={view.extractionProfile} />
            )}
          </Row>
        </dl>
        <p className="provenance-note">{PROFILE_NOTE}</p>
        <p className="provenance-source">
          <a href={view.sourceUri} rel="external">
            The publisher’s own file for this record
          </a>
        </p>
      </section>

      <section className="provenance-block">
        <h2>The two clocks</h2>
        <p className="provenance-legal">
          <Mark name="--time-legal">{view.sentences.legal}</Mark>
        </p>
        <p className="provenance-record-time">
          <Mark name="--time-record">{view.sentences.recordTime}</Mark>
        </p>
        <p className="provenance-note">{view.sentences.validTime}</p>
        {view.validTimeSource.derived ? (
          <p className="provenance-derived">
            <Mark name="--derived">
              The legal-time dates above were worked out by this service.
            </Mark>
          </p>
        ) : null}
      </section>

      <section className="provenance-block">
        <h2>What this service observed, and when</h2>
        <p className="provenance-count">
          {`${view.sentences.events}. Every time below is record time, in UTC, as this service `
            + 'recorded it.'}
        </p>
        <Chain events={view.events} />
      </section>

      <section className="provenance-block">
        <h2>The bodies this service has held</h2>
        <dl>
          {view.digests.map((digest) => (
            <Row label={digest.kind} key={digest.kind}>
              <Evidence value={digest.value} />
            </Row>
          ))}
        </dl>
        {view.sentences.noText === null ? null : (
          <p className="provenance-note">{view.sentences.noText}</p>
        )}
        {view.sentences.narrowed === null ? null : (
          <p className="provenance-note">{view.sentences.narrowed}</p>
        )}
        <p className="provenance-count">{`${view.sentences.observations}.`}</p>
        <History observations={view.observations} />
      </section>

      <section className="provenance-block">
        <h2>What served this answer</h2>
        <dl>
          <Row label="corpus commit">
            <Evidence value={view.envelope.freshness.corpus_commit} />
          </Row>
          <Row label="index built">
            <Evidence value={view.envelope.freshness.built_at} />
          </Row>
          <Row label="manifest set id">
            <Evidence value={view.envelope.artifact.manifest_set_id} />
          </Row>
          <Row label="content digest">
            <Evidence value={view.envelope.artifact.content_digest} />
          </Row>
          <Row label="stamp signature valid">
            {view.stamp.signature_valid ? (
              'yes'
            ) : (
              // Not the conflict token, which says two publisher dates disagree. An invalid
              // stamp is plain, emphatic text, exactly as the string strip renders it.
              <strong>no, this stamp signature did NOT verify</strong>
            )}
          </Row>
          <Row label="algorithm">
            <Evidence value={view.stamp.algorithm} />
          </Row>
          <Row label="public key">
            <code className="provenance-pem">{view.stamp.public_key}</code>
          </Row>
          <Row label="signature">
            <code className="provenance-pem">{view.stamp.signature}</code>
          </Row>
        </dl>
        <p className="provenance-note">{STAMP_SCOPE_NOTE}</p>
      </section>

      <section className="provenance-block">
        <h2>What this corpus holds</h2>
        <p className="provenance-holdings">{view.sentences.holdings}</p>
        <p className="provenance-note">{view.sentences.population}</p>
      </section>
    </section>
  );
}
