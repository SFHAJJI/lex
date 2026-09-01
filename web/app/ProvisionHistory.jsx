// S4, one provision's history, in React.
//
// The rules live in `scripts/provision-history.mjs` and this file renders what they return. It
// derives nothing and decides nothing: `provisionHistoryModel` validates the payload, derives the
// publisher vocabulary, works out whether each row carries a validity conflict, and hands back
// claims. If this component ever computed one of those itself, the two surfaces would have two
// answers and only one of them would be tested.
//
// The three sentences that carry the trust are imported constants rather than JSX prose, so the
// string renderer and this component cannot drift on the wording of a claim.

import { TEXT_INTERVAL_NOTE, EMPTY_NOTE, RENUMBER_BASIS, provisionHistoryModel } from '../scripts/provision-history.mjs';

/** One text state: when it applied, which text it was, and where to read it. */
function StateRow({ state }) {
  return (
    <li className="provision-state">
      <span className="provision-when">{state.interval}</span>
      <code className="provision-digest">{state.textDigest.slice(0, 8)}</code>
      {state.conflict ? (
        // Both publisher dates. The provision says when it took effect and the version says when
        // it applied, and the publisher has said both, so neither is chosen for the reader.
        <span className="provision-conflict">
          {`The publisher gives two dates for this text: the provision takes effect ` +
            `${state.articleFrom} and the version it sits in applies from ${state.versionFrom}. ` +
            `Both are shown because both come from the publisher.`}
        </span>
      ) : null}
      <a className="provision-link" href={state.permalink}>
        Read this wording
      </a>
    </li>
  );
}

/**
 * The provision history.
 *
 * @param {object} props the same shape `provisionHistoryModel` takes
 */
export function ProvisionHistory(props) {
  const model = provisionHistoryModel(props);

  if (model.states.length === 0) {
    return (
      <section className="provision-history provision-history-empty">
        <h2>{model.anchor}</h2>
        <p className="provision-history-empty-note">{EMPTY_NOTE}</p>
      </section>
    );
  }

  return (
    <section className="provision-history">
      <h2>{model.anchor}</h2>
      {/* Said once, above the rows, because a reader who has already counted them has counted
          the wrong thing. */}
      <p className="provision-history-note">{TEXT_INTERVAL_NOTE}</p>
      <ol className="provision-states">
        {model.states.map((state) => (
          <StateRow key={state.textDigest} state={state} />
        ))}
      </ol>
      {model.truncated ? (
        <p className="provision-truncated">
          {`Showing ${model.states.length} of ${model.distinctTexts} distinct wordings.`}
        </p>
      ) : null}
      {model.anchorEvents.length === 0 ? null : (
        <section className="provision-events">
          <h3>Lifecycle</h3>
          <ul>
            {model.anchorEvents.map((event, index) => (
              <li className="provision-event" key={`${event.kind}-${index}`}>
                <span>{event.kind}</span>
                {event.kind === 'renumbered' ? (
                  <>
                    {' '}
                    <span className="provision-event-basis">{RENUMBER_BASIS}</span>
                  </>
                ) : null}
              </li>
            ))}
          </ul>
        </section>
      )}
    </section>
  );
}
