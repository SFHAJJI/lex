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

import { REFUSAL_CARD_COPY, candidateView, validateRefusal } from '../scripts/refusal-card.mjs';
import { fillText } from '../scripts/live-chrome.mjs';
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
  const published = candidate.publicationDate === null ? copy.publicationNotStated : fillText(copy.published, { date: candidate.publicationDate });
  return (
    <Say
      template={candidate.withdrawalStated ? copy.candidate : copy.candidateWithdrawalNotStated}
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
          {item.values.map(candidateView).map((candidate) => (
            <li className="refusal-candidate" key={candidate.href}>
              <a href={candidate.href}>
                <Candidate candidate={candidate} copy={copy} />
              </a>
            </li>
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
              <dd>{row.declaredNull ? copy.nullSentences[row.key] : row.value}</dd>
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
      {card.note ? <p className="refusal-note">{copy.notes[card.code]}</p> : null}
      <AbsenceEvidence absence={card.absence} copy={copy} />
      <Payload parts={card.payloadParts} copy={copy} />
      <Handoff handoffs={card.handoffs} />
    </section>
  );
}
