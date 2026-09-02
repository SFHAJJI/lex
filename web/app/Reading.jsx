// The provision reading view, as React: one work's text as it stood on one date.
//
// This screen is a composition and nothing here re-implements a component it composes. The
// two clocks, the three qualifications, the quotation, the typed refusals, the verification
// cluster and the permalink all exist once, elsewhere, and are called.
//
// Every rule stays in `scripts/reading.mjs` and is applied by `validateReading` and
// `validateProvision`. What they return is the checked input for the components below, never
// their markup, so the string surface and this one apply one implementation of eight rules
// about what a page of statute may say. A framework that quietly became a second home for
// those rules would be the worst available outcome of adopting one, and this split is the
// specific thing that prevents it.
//
// Two facts this component never needs are worth naming, because both were props somewhere
// before. It needs no `work`: the work is `state.lex_id`, and a page told a different one
// mints permalinks for a work it is not showing, with every link resolving. And it needs no
// vocabulary: which clock a publisher's dates are on is a property of the publisher, read
// through `semanticsOf`, so this page cannot label Luxembourg applicability dates with the
// Union's words for a consolidated wording state. A caller may still state either, and is
// then refused when it states something the record contradicts, which is the only honest
// answer to a caller that believes something false.

import { Fragment } from 'react';

import {
  localizationUnavailableParts,
  quotedLawParts,
  unofficialRenderingParts,
} from '../scripts/localization.mjs';
import { NO_LEGAL_EFFECT, unchangedSince, validateReading } from '../scripts/reading.mjs';
import { SUPERSEDED_NOTE, validateSupersededState } from '../scripts/refusal-card.mjs';
import { Mark, RefusalCard } from './RefusalCard.jsx';
import { StateBanner } from './StateBanner.jsx';
import { Hole, Provisional, ValidityConflict } from './StateQualifiers.jsx';
import { VerifyCluster } from './VerifyCluster.jsx';

/**
 * A string the chrome has no reviewed copy of, said rather than substituted.
 *
 * A reader who sees English where they asked for Luxembourgish deserves to know it was not a
 * translation, so nothing falls back and the locales it does exist in are named.
 */
function LocalizationUnavailable({ note }) {
  const parts = localizationUnavailableParts(note);
  return (
    <p className="localization-unavailable" data-code={parts.code} data-locale={parts.locale}>
      <code>{parts.code}</code> {parts.sentence} {parts.available}{' '}
      <span className="localization-key">{parts.key}</span>
    </p>
  );
}

/**
 * A quoted statutory span, in the expression's own language.
 *
 * The chrome locale is the language of this interface and reaches only the authenticity note.
 * Binding the quotation to it would put `lang="en"` on 38,000 words of French and make a
 * screen reader read French aloud in an English voice.
 */
function QuotedLaw({ quotation }) {
  const quoted = quotedLawParts(quotation);
  return (
    <>
      <blockquote className="law" lang={quoted.language}>
        {quoted.text}
      </blockquote>
      {quoted.note === null ? null : quoted.note.status === 'ok' ? (
        <p className="law-authenticity" lang={quoted.note.locale}>
          {quoted.note.text}
        </p>
      ) : (
        <LocalizationUnavailable note={quoted.note} />
      )}
    </>
  );
}

/** A body that is not the authentic text, labelled as one and routed to the text that is. */
function UnofficialRendering({ rendering }) {
  const parts = unofficialRenderingParts(rendering);
  return (
    <section className="unofficial-rendering">
      <p className="unofficial-head">
        <Mark name="--unofficial">{parts.heading}</Mark>
      </p>
      <blockquote className="body" lang={parts.language}>
        {parts.text}
      </blockquote>
      <p className="unofficial-note">{parts.note}</p>
      <p className="unofficial-official">
        <a href={parts.official} rel="external">
          The authentic text, at the publisher
        </a>
      </p>
    </section>
  );
}

/**
 * The sentence a consolidation cannot render text without.
 *
 * Beside every block of text rather than once at the top of the page: a reader scrolling to
 * the fourth article never saw the one at the top, and copying an article carried the text
 * away from the only thing that qualified it. Whether it applies is `consolidation_status` on
 * the record, because a publisher's own original official expression is not a consolidation
 * and printing this against it would be false in the other direction.
 */
function NoLegalEffect({ consolidated }) {
  if (!consolidated) return null;
  return <p className="reading-no-legal-effect">{NO_LEGAL_EFFECT}</p>;
}

/**
 * The state the publisher replaced this one with, and this one, still addressable.
 *
 * The publisher ranked the two, so no choice is asked of the reader. A reader who followed an
 * old link needs to be told their state was superseded rather than find it silently gone.
 */
function SupersededState({ publisher, work, superseded }) {
  const pair = validateSupersededState({
    publisher,
    work,
    live: superseded.live,
    withdrawn: superseded.withdrawn,
  });
  return (
    <section className="superseded-state">
      <p className="superseded-live">
        <a href={pair.live.href}>
          The state the publisher holds, applicable from {pair.live.valid_from}, hash{' '}
          <code>{pair.live.short_hash}</code>
        </a>
      </p>
      <p className="superseded-note">{SUPERSEDED_NOTE}</p>
      <ul className="superseded-siblings">
        {pair.withdrawn.map((one) => (
          <li key={one.href}>
            <a href={one.href}>
              applicable from {one.valid_from}, hash <code>{one.short_hash}</code>, published{' '}
              {one.publication_date}
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * One provision: its number, its dating, its text or the reason there is none, its
 * renderings, its permalink and its digest.
 *
 * @param {object} props
 * @param {object} props.provision  a provision already through `validateProvision`
 */
function Provision({ provision }) {
  return (
    <article className="reading-provision" id={provision.anchor}>
      <h3 className="reading-num">{provision.num}</h3>
      {provision.conflicted ? (
        <ValidityConflict
          stateValidFrom={provision.stateValidFrom}
          wordingValidFrom={provision.wordingValidFrom}
        />
      ) : (
        <p className="reading-unchanged">{unchangedSince(provision.wordingValidFrom)}</p>
      )}
      {provision.quotation === null ? (
        // Withheld by licence, or held by nobody. Two absences that are not the same
        // absence, each with the typed refusal that names which.
        <RefusalCard {...provision.refusal} />
      ) : (
        <>
          <QuotedLaw quotation={provision.quotation} />
          <NoLegalEffect consolidated={provision.consolidated} />
        </>
      )}
      {provision.renderings.map((rendering, index) => (
        <Fragment key={`${rendering.language}:${index}`}>
          <UnofficialRendering rendering={rendering} />
          <NoLegalEffect consolidated={provision.consolidated} />
        </Fragment>
      ))}
      <p className="reading-permalink">
        <a href={provision.permalink}>Permalink</a> <code>{provision.permalink}</code>
      </p>
      {provision.verify === null ? null : (
        // Only the three facts the record supplies. The publisher is not among them, because
        // the cluster reads it off the identifier it is already given.
        <VerifyCluster
          sourceUri={provision.verify.sourceUri}
          lexId={provision.verify.lexId}
          hash={provision.verify.hash}
        />
      )}
    </article>
  );
}

/**
 * One work's text as it stood on one date.
 *
 * The whole props object goes to `validateReading`, not a destructured copy of it, so a
 * caller that passes `provisional`, `language`, `consolidation`, `no_legal_effect` or
 * `text_available` is refused rather than quietly ignored. Every one of those is answered by
 * a record on this page, and a fact taken from the caller is a fact about the caller.
 *
 * @see validateReading, which holds every rule this renders.
 */
export function Reading(props) {
  const view = validateReading(props);
  const { state } = view;

  // The header is built before the request is answered, so the refusal below is a page about
  // a resolved version rather than a page about nothing. A refusal that dropped the two
  // clocks would leave a reader unable to say which version did not contain their anchor.
  const head = (
    <header className="reading-state">
      <StateBanner state={state} />
      {view.provisional ? <Provisional validFrom={state.valid_from} asOf={view.asOf} /> : null}
      <p className="reading-as-of">Read as of {view.asOf}.</p>
      <VerifyCluster
        sourceUri={view.stateVerify.sourceUri}
        lexId={view.stateVerify.lexId}
        hash={view.stateVerify.hash}
      />
    </header>
  );

  if (view.anchorRefusal !== null) {
    return (
      <section className="reading reading-anchor-refused">
        {head}
        <RefusalCard {...view.anchorRefusal} />
      </section>
    );
  }

  return (
    <section className="reading">
      {head}
      {view.superseded === null ? null : (
        <SupersededState
          publisher={view.identity.publisher}
          work={view.identity.work}
          superseded={view.superseded}
        />
      )}
      {view.holes.map((hole, index) => (
        <Hole key={`${hole?.kind}:${hole?.from}:${hole?.to}:${index}`} kind={hole?.kind} from={hole?.from} to={hole?.to} />
      ))}
      {view.provisions.map((provision) => (
        <Provision key={provision.anchor} provision={provision} />
      ))}
    </section>
  );
}
