// The two-clocks state banner, as React.
//
// Same split as the refusal card and the dossier: every rule stays in
// `scripts/state-banner.mjs` and is applied by `stateBannerSentences`. This file decides how
// two checked sentences look and re-derives nothing. A second implementation of a legal rule
// is a second place for it to be wrong, and the copy that drifts is always the one nobody
// tested, which is the specific thing this split prevents.
//
// One difference from the string renderer is deliberate and is the whole point of the port.
// `renderStateBanner` is told which publisher's vocabulary to speak, through
// `envelope.timeline_semantics`, because it is also given bare states that carry nothing but
// dates. This component is only ever given a state that names itself, so it reads the
// publisher off `lex_id` and asks `semanticsOf` which clock that publisher's dates are on.
// Nobody chooses, so nobody can choose wrong: that is what turned a defect repaired five
// times in one day into a defect that cannot be written.

import { semanticsOf } from '../scripts/publisher-vocabulary.mjs';
import { publisherOf } from '../scripts/record-identity.mjs';
import { stateBannerSentences } from '../scripts/state-banner.mjs';
import { Mark } from './RefusalCard.jsx';

/**
 * One state's two clocks: what the publisher says about legal time, and what this corpus
 * says about when it saw the record.
 *
 * @param {object} props
 * @param {object} props.state  the state, carrying its own `lex_id` and both clocks
 */
export function StateBanner({ state }) {
  const semantics = semanticsOf(
    publisherOf(state?.lex_id, 'the state banner'),
    'the state banner',
  );
  const { legal, record } = stateBannerSentences({ semantics, state });

  return (
    <div className="state-banner">
      <p className="state-banner-legal">
        <Mark name="--time-legal">{legal}</Mark>
      </p>
      <p className="state-banner-record">
        <Mark name="--time-record">{record}</Mark>
      </p>
    </div>
  );
}
