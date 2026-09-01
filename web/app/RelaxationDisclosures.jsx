// Disclosure of relaxation, as React, and the account every other part of the screen reads.
//
// A relaxation is anything the search did to the query other than run it. Expanding "many" to
// "mady", reading "caution" as "garantie locative", ranking by meaning rather than by words. Each
// is defensible and each changes what the reader is looking at, so UX spec section 2 requires
// three independent, visually distinct disclosures, each with its own one-tap revert, and states
// the rule as "no relaxation ever runs without its banner".
//
// The account is required and closed. An omitted account is not "nothing ran", it is a caller who
// did not say, and a screen that does not know whether the crosswalk fired cannot honestly
// disclose that it did not. This is the rule that was found defaulted to an empty value on a
// sibling branch, and the reason it mattered is that every check downstream was written over the
// account's own keys: an omission did not fail the disclosure contract, it skipped the contract
// entirely. A screen that discloses only when it is handed something to disclose cannot be told
// apart from a screen that never discloses.
//
// Every rule that decides whether an account may render at all lives in `scripts/relaxation.mjs`
// and is applied by calling its renderer, whose markup is then discarded. That module has no
// separate validator to import, and a second copy of "the crosswalk must carry its review date"
// living in a component is the worst available outcome of adopting a framework: a rule repaired
// in one renderer and left broken in the other. What this file adds is the shape the account has
// to have before anyone can read it, because three other parts of this screen read it and the
// string renderer only checks it on the path where something applied.

import { isCalendarDate } from '../scripts/temporal.mjs';
import {
  RELAXATIONS,
  renderRelaxationDisclosures,
  revertPath,
} from '../scripts/relaxation.mjs';
import { SHELLS, shellUrl } from '../scripts/urls.mjs';

/**
 * The account, complete and closed, or a refusal naming what is missing.
 *
 * Exported because a caller can need the contract without needing the markup. The results list
 * has to know the account is whole before it can cross-check its badges against it, and the
 * no-hit card has to know it before it can say which words were substituted. Re-implementing the
 * same three checks in each of them is how two versions of one contract start to disagree.
 *
 * @param {object} relaxations one entry per member of RELAXATIONS, each with `applied`
 */
export function requireRelaxationAccount(relaxations) {
  if (relaxations === null || typeof relaxations !== 'object' || Array.isArray(relaxations)) {
    throw new Error(
      'this screen declares every relaxation and whether it applied; an absent account is not ' +
        '"none ran", it is a caller who did not say, and a screen that does not know cannot ' +
        'disclose',
    );
  }
  for (const relaxation of RELAXATIONS) {
    if (typeof relaxations[relaxation]?.applied !== 'boolean') {
      throw new Error(
        `${relaxation} must declare whether it was applied; an absent relaxation is not "off", ` +
          'it is a caller who did not say, and a screen that does not know cannot disclose',
      );
    }
  }
  const extra = Object.keys(relaxations).filter((name) => !RELAXATIONS.includes(name));
  if (extra.length > 0) {
    throw new Error(
      `${extra.join(', ')} is not a relaxation this interface can disclose; adding one to the ` +
        'retrieval path without adding it here is how a silent relaxation ships',
    );
  }
  return relaxations;
}

/**
 * What the query became, read off the account rather than taken beside it.
 *
 * The expansions and the crosswalk's reading used to arrive as their own parameters, so a screen
 * could announce "Understood as: garantie locative" while the same screen's disclosures said the
 * crosswalk never ran, and could list substitutions attributed to nothing. Deriving both from the
 * account makes the contradiction unrepresentable instead of merely refused.
 */
export function interpretationOf(relaxations) {
  requireRelaxationAccount(relaxations);

  const fuzzy = relaxations.fuzzy;
  let expansions = [];
  if (fuzzy.applied) {
    if (!Array.isArray(fuzzy.expansions) || fuzzy.expansions.length === 0) {
      throw new Error(
        'a fuzzy relaxation must list the expansions it applied, verbatim; the live service ' +
          'expanded "many" to "mady" and returned nothing, and a reader who cannot see that ' +
          'cannot understand the result',
      );
    }
    expansions = fuzzy.expansions;
  }

  const crosswalk = relaxations.crosswalk;
  let understoodAs = null;
  if (crosswalk.applied) {
    if (typeof crosswalk.understood_as !== 'string' || crosswalk.understood_as.trim().length === 0) {
      throw new Error(
        'the crosswalk understood_as is required: a crosswalk that ran and will not say what it ' +
          'read the query as has rewritten the question without disclosing the rewrite',
      );
    }
    understoodAs = crosswalk.understood_as;
  }

  return { expansions, understoodAs };
}

/**
 * The search path a revert goes back to, refused when it is not a path inside this product.
 *
 * `revertPath` accepts anything that starts with a slash, and `//evil.example/x` starts with one:
 * protocol-relative is off-site. That would put a one-tap trip to another origin behind a label
 * promising the reader their own words back, which is the most trusted control on the screen.
 *
 * The path is checked by rebuilding it with `shellUrl`, the same builder the entry screens use,
 * and requiring the result to be the string it was handed. A search always happens inside a
 * shell, so a revert leads back into one; and validating by reconstruction is what stops this
 * check from being a second, weaker grammar beside the one the links are minted from.
 *
 * Exported so the screen guards its own path once, before anything is rendered from it.
 */
export function requireSameOriginSearchPath(searchPath) {
  const refuse = () => {
    throw new Error(
      `a revert needs the current same-origin search path; ${JSON.stringify(searchPath)} is not ` +
        'one, and a revert that leaves this origin is not a revert',
    );
  };
  if (typeof searchPath !== 'string' || !searchPath.startsWith('/')) refuse();
  if (searchPath.includes('#') || searchPath.includes('\\')) refuse();
  const [path] = searchPath.split('?');
  const [shell, ...rest] = path.slice(1).split('/');
  if (!SHELLS.includes(shell)) refuse();
  try {
    if (shellUrl(shell, rest.length === 0 ? '/' : `/${rest.join('/')}`) !== path) refuse();
  } catch {
    refuse();
  }
  return searchPath;
}

/** One disclosure block, in the fixed shape all three share. */
function Disclosure({ relaxation, searchPath, heading, revertLabel, children }) {
  return (
    <div className={`relaxation relaxation-${relaxation}`} data-relaxation={relaxation}>
      <p className="relaxation-heading">{heading}</p>
      {children}
      <p className="relaxation-revert">
        <a href={revertPath(searchPath, relaxation)}>{revertLabel}</a>
      </p>
    </div>
  );
}

/** The substitutions, verbatim. "many" became "mady" on the live service, and it was nonsense. */
function Fuzzy({ state, searchPath }) {
  return (
    <Disclosure
      relaxation="fuzzy"
      searchPath={searchPath}
      heading="Fuzzy expansions applied"
      revertLabel="Turn fuzzy expansion off"
    >
      <ul className="relaxation-expansions">
        {state.expansions.map((one) => (
          <li key={one}>
            <code>{one}</code>
          </li>
        ))}
      </ul>
    </Disclosure>
  );
}

/**
 * The editorial reading, and that it is editorial.
 *
 * The note is this component's words, not the caller's. "Editorial crosswalk, not official" is
 * the whole reason the disclosure exists, and a caller who could phrase it would eventually
 * phrase it away.
 */
function Crosswalk({ state, searchPath }) {
  if (!isCalendarDate(state.reviewed_on)) {
    throw new Error(
      `the crosswalk must carry its review date: ${JSON.stringify(state.reviewed_on)}; it is ` +
        'editorial and not official, so when somebody last looked at it is part of the claim',
    );
  }
  return (
    <Disclosure
      relaxation="crosswalk"
      searchPath={searchPath}
      heading={`Understood as: ${state.understood_as}`}
      revertLabel="Search my exact words instead"
    >
      <p className="relaxation-note">
        Editorial crosswalk, not official. Version {state.version}, reviewed {state.reviewed_on}.
      </p>
    </Disclosure>
  );
}

/** Ranked by meaning, and by which encoder. */
function Semantic({ state, searchPath }) {
  return (
    <Disclosure
      relaxation="semantic"
      searchPath={searchPath}
      heading="Ranked by meaning, not only by words"
      revertLabel="Rank by keywords instead"
    >
      <p className="relaxation-note">
        Encoder {state.encoder}, passing benchmark {state.benchmark}. Semantic ranking serves only
        behind that gate.
      </p>
    </Disclosure>
  );
}

const BLOCK = Object.freeze(
  Object.assign(Object.create(null), { fuzzy: Fuzzy, crosswalk: Crosswalk, semantic: Semantic }),
);

/**
 * Every relaxation, declared, and a disclosure for each one that ran.
 *
 * @param {object} props
 * @param {string} props.searchPath  the current search path, which the reverts are built from
 * @param {object} props.relaxations one entry per member of RELAXATIONS, each with `applied`
 */
export function RelaxationDisclosures({ searchPath, relaxations }) {
  requireSameOriginSearchPath(searchPath);

  // The string renderer is the validator. Its markup is discarded; what is wanted is its
  // refusals, so that a rule such as "the crosswalk must carry its review date" cannot be
  // repaired in one renderer and left standing in the other. There is no `validateRelaxations`
  // to import, and writing one here would be the second copy this avoids.
  renderRelaxationDisclosures({ searchPath, relaxations: requireRelaxationAccount(relaxations) });

  const applied = RELAXATIONS.filter((relaxation) => relaxations[relaxation].applied);
  if (applied.length === 0) return null;

  // Three independent blocks, in the fixed order, each visually distinct and separately
  // revertible. Not one merged banner: merging them makes the cheapest undo undo everything, so
  // a reader who wants their own words back also turns off semantic ranking they never objected
  // to.
  return (
    <div className="relaxations">
      {applied.map((relaxation) => {
        const Block = BLOCK[relaxation];
        return (
          <Block key={relaxation} state={relaxations[relaxation]} searchPath={searchPath} />
        );
      })}
    </div>
  );
}
