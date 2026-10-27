// The refusal card, as React.
//
// The first end-to-end port, and the shape every later one follows: every rule stays in
// `scripts/refusal-card.mjs` and is applied by `validateRefusal`. This file decides how a
// validated refusal looks and re-derives nothing. React is presentation and runtime here, never
// a source of legal facts, so a rule cannot be repaired in one renderer and left broken in the
// other. A parallel implementation of the truth rules would be the worst possible outcome of
// adopting a framework, and it is the specific thing this split prevents.
//
// Two rules are visible in the markup rather than in a validator, and both are load-bearing.
// A refusal carries no `role="alert"` and no live region: a refusal is an answer, and announcing
// it as an alert is the aural equivalent of a red error toast. And the quotation carries the
// expression's own language, because hardcoding French mislabels every EU expression and makes a
// screen reader read English law in a French voice.

import { REFUSAL_CARD_COPY, candidateView, reasonView, validateRefusal, versionNameKey } from '../scripts/refusal-card.mjs';
import { CHROME_LOCALE, ENGLISH_LANG, fillText } from '../scripts/live-chrome.mjs';
import { permalinkHref } from '../scripts/permalinks.mjs';
import { Say } from './LiveAnswer.jsx';
import { TOKENS } from '../scripts/design-tokens.mjs';
import { handoffUri } from '../scripts/routes.mjs';

/**
 * A semantic token: icon, label and text, matching the string renderer's `mark()`.
 *
 * The icon is aria-hidden because it repeats the label, and a screen reader announcing an
 * emoji before every refusal is noise that teaches a reader to stop listening.
 */
export function Mark({ name, label, children }) {
  const token = TOKENS.find((one) => one.name === name);
  if (!token) {
    throw new Error(`unknown semantic token ${name}`);
  }
  return (
    <span className={`token token${token.name}`}>
      <span className="token-icon" aria-hidden="true">
        {token.icon}
      </span>
      <span className="token-label">{label ?? token.label}</span>
      <span className="token-text">{children}</span>
    </span>
  );
}

/**
 * What this service holds and does not hold, and what would answer the question.
 *
 * The note is the product's oldest invariant said out loud, and it comes from the shared
 * module rather than from a literal here: a card that showed the routes without the note
 * would let a reader read "no state held" as "no such law".
 */
function AbsenceEvidence({ absence, copy }) {
  if (absence === null) return null;
  return (
    <div className="refusal-absence">
      <p className="refusal-absence-note">{copy.absenceNote}</p>
      <h3>{copy.absenceHeading}</h3>
      <ul>
        {absence.routeCodes.map((route) => (
          <li key={route}>{copy.routes[route]}</li>
        ))}
      </ul>
    </div>
  );
}

/** One offered state, in the card's words: its date, its hash as code, its publication and standing. */
function Candidate({ candidate, copy }) {
  // An act as published is "published" as an act, in a language that agrees the word (the French review of S7b and S7c).
  const published = candidate.publicationDate === null
    ? copy.publicationNotStated
    : fillText(candidate.datedByEntryIntoForce ? copy.publishedAct : copy.published, { date: candidate.publicationDate });
  // An act dated by its entry into force is offered as that, never as applicable from it (R21; index 9's candidate roles).
  const template = candidate.datedByEntryIntoForce
    ? (candidate.withdrawalStated ? copy.candidateEntryIntoForce : copy.candidateEntryIntoForceWithdrawalNotStated)
    : (candidate.withdrawalStated ? copy.candidate : copy.candidateWithdrawalNotStated);
  return (
    <Say
      template={template}
      values={{ validFrom: candidate.validFrom, hash: <code>{candidate.hashPrefix}</code>, published }}
    />
  );
}

/**
 * The mandatory helpful payload: the candidates, the anchors this version does contain, and
 * the labelled rows.
 *
 * A refusal without it is the sterile refusal `validateRefusal` already forbids, and a
 * renderer that dropped it would produce one anyway.
 */
function Payload({ parts, copy }) {
  if (parts.structured.length === 0 && parts.rows.length === 0) return null;
  return (
    <>
      {parts.structured.map((item) => (item.kind === 'candidates' ? (
        <ul className="refusal-candidates" key={item.key}>
          {item.values.map((value, index) => candidateView(value, item.dateRoles, item.places?.[index] ?? null)).map((candidate, index) => (
            candidate.coordinate !== null ? (
              // A state the catalogue names no expression of (index 9): named by its kind and role, with no link (the review of #987).
              <li className="refusal-candidate" key={`${candidate.coordinate}#${index}`} data-candidate-unexpressed="">
                <Say
                  template={copy.candidateUnexpressed}
                  values={{
                    version: fillText(copy.versions[versionNameKey(candidate.version.kind, candidate.version.role)], { date: candidate.validFrom }),
                    hash: <code>{candidate.hashPrefix}</code>,
                  }}
                />
                {' ('}<code>{candidate.coordinate}</code>{')'}
              </li>
            ) : (
              <li className="refusal-candidate" key={candidate.href}>
                {/* A candidate is a permalink as the API mints it: linked under the page's own language (R61). */}
                <a href={permalinkHref(candidate.href, CHROME_LOCALE)}>
                  <Candidate candidate={candidate} copy={copy} />
                </a>
              </li>
            )
          ))}
        </ul>
      ) : (
        <ul className={item.className} key={item.key}>
          {item.values.map((value) => (
            <li key={value}>
              <code>{value}</code>
            </li>
          ))}
        </ul>
      )))}
      {parts.rows.length === 0 ? null : (
        <dl className="refusal-payload">
          {parts.rows.map((row) => (
            <div className="strip-row" key={row.key}>
              <dt>{row.key}</dt>
              {/* A declared null is the card's own sentence; any other value is the platform's, in English
                  where it is words, so a page in another language marks it English. */}
              <dd lang={row.declaredNull ? undefined : ENGLISH_LANG}>
                {row.declaredNull
                  ? copy.nullSentences[row.key]
                  : row.href === undefined ? row.value : <a href={row.href} rel="external">{row.value}</a>}
              </dd>
            </div>
          ))}
        </dl>
      )}
    </>
  );
}

/** The publisher's own next step, one validated link per entry. */
// The route policy is imported, never injected. Taking the validator as a prop let a caller
// supply a permissive one, and a hostile javascript: href walked straight through the port
// while the string renderer refused it. A caller that can choose its own validator can
// validate its way to anything, which is the same defect as a caller declaring which search
// layers were applicable.
function Handoff({ handoffs }) {
  if (handoffs.length === 0) return null;
  return (
    <ul className="refusal-handoff">
      {handoffs.map((one) => (
        <li key={`${one.label}:${one.href}`}>
          {/* Validated, not merely escaped: `javascript:alert(1)` escapes to a safe attribute
              value and remains a working link. */}
          <a href={handoffUri(one.href)}>{one.label}</a>
        </li>
      ))}
    </ul>
  );
}

/**
 * The refusal card.
 *
 * @param {object} props the same shape `renderRefusalCard` takes, validated identically, and the
 *   card's words (`copy`): English from `refusal-card.mjs` unless a page passes its chrome table's
 */
export function RefusalCard({ code, sentence, payload, governingText, handoff, copy = REFUSAL_CARD_COPY }) {
  const card = validateRefusal({ code, sentence, payload, governingText, handoff });

  // Refused rather than dropped. `advice_boundary` exists to refuse the question and still
  // hand over the text the reader may have, so a card that silently omitted the co-delivered
  // provisions would keep the refusal and lose the half that makes it acceptable. This
  // runtime does not render a quotation inside a card yet, and saying so is the honest
  // failure; rendering three quarters of the contract is not.
  if (card.governingText !== null) {
    throw new Error(
      'this runtime does not render co-delivered governing text yet, and a card that dropped '
        + 'it would refuse the question while withholding the text the reader may still have',
    );
  }

  return (
    <section className="refusal-card">
      <p className="refusal-head">
        <Mark name="--refusal" label={copy.tokenLabel}>{card.sentence}</Mark>
        <code className="refusal-code">{card.code}</code>
      </p>
      {card.retryable ? <p className="refusal-retry">{copy.retry}</p> : null}
      {/* Index 9's reasons and textless versions, in this page's words and the payload's values (S7c). */}
      {(reasonView(card.code, card.payload, copy) ?? []).map((line) => (
        <p className="refusal-reason" key={line.key} data-reason={line.key}>{line.text}</p>
      ))}
      {card.note ? <p className="refusal-note">{copy.notes[card.code]}</p> : null}
      <AbsenceEvidence absence={card.absence} copy={copy} />
      <Payload parts={card.payloadParts} copy={copy} />
      <Handoff handoffs={card.handoffs} />
    </section>
  );
}
