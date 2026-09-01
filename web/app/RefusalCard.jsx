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

import { RETRY_SENTENCE, validateRefusal } from '../scripts/refusal-card.mjs';
import { TOKENS } from '../scripts/design-tokens.mjs';
import { handoffUri } from '../scripts/routes.mjs';

/**
 * A semantic token: icon, label and text, matching the string renderer's `mark()`.
 *
 * The icon is aria-hidden because it repeats the label, and a screen reader announcing an
 * emoji before every refusal is noise that teaches a reader to stop listening.
 */
export function Mark({ name, children }) {
  const token = TOKENS.find((one) => one.name === name);
  if (!token) {
    throw new Error(`unknown semantic token ${name}`);
  }
  return (
    <span className={`token token${token.name}`}>
      <span className="token-icon" aria-hidden="true">
        {token.icon}
      </span>
      <span className="token-label">{token.label}</span>
      <span className="token-text">{children}</span>
    </span>
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
 * @param {object} props the same shape `renderRefusalCard` takes, validated identically
 */
export function RefusalCard({ code, sentence, payload, governingText, handoff }) {
  const card = validateRefusal({ code, sentence, payload, governingText, handoff });
  return (
    <section className="refusal-card">
      <p className="refusal-head">
        <Mark name="--refusal">{card.sentence}</Mark>
        <code className="refusal-code">{card.code}</code>
      </p>
      {card.retryable ? <p className="refusal-retry">{RETRY_SENTENCE}</p> : null}
      {card.note ? <p className="refusal-note">{card.note}</p> : null}
      <Handoff handoffs={card.handoffs} />
    </section>
  );
}

