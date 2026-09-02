// S12, get help, in React.
//
// The rules live in `scripts/get-help.mjs`. This file renders them and decides nothing.
//
// The empty state is the shipped state, so it gets the same care as the populated one: a reader
// arrives here having already been refused once, and a destination that does not resolve is a
// second refusal wearing the word help.

import {
  BOUNDARY_NOTE,
  NO_COUNTER_NOTE,
  admissibleCounters,
  admissibleOfficialRoutes,
} from '../scripts/get-help.mjs';

/**
 * The get-help page.
 *
 * @param {object} props
 * @param {Array} [props.counters]       verified counters, each `{ label, href }`
 * @param {Array} props.officialRoutes   the publisher routes, which are true regardless
 */
export function GetHelp({ counters = [], officialRoutes }) {
  const admitted = admissibleCounters({ counters, officialRoutes });
  // The same validator the string renderer uses. Rendering these directly is what left this
  // surface accepting a javascript: URI after the other one was repaired.
  const routes = admissibleOfficialRoutes(officialRoutes);

  return (
    <section className="get-help">
      <h2>Getting advice</h2>
      {/* The boundary is why this page exists, so it is stated before anything is offered. */}
      <p className="get-help-boundary">{BOUNDARY_NOTE}</p>
      {admitted.length === 0 ? (
        // Not an empty list. An empty list is indistinguishable from a build that never had one,
        // and those are different facts about this build.
        <p className="get-help-none">{NO_COUNTER_NOTE}</p>
      ) : (
        <ul className="get-help-counters">
          {admitted.map((counter) => (
            <li key={counter.href}>
              <a href={counter.href} rel="external">
                {counter.label}
              </a>
            </li>
          ))}
        </ul>
      )}
      <h3>The publisher, directly</h3>
      <ul className="get-help-official">
        {routes.map((route) => (
          <li key={route.uri}>
            <a href={route.uri} rel="external">
              {route.label}
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
}
