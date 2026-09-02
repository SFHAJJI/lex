// The results listbox, with roving tabindex and the interpretation announcement above it.
//
// UX spec section 2: "results are a listbox with roving tabindex", "every match badge has a text
// label", and "the interpretation banner is aria-live='polite' so screen reader users hear that
// their query was rewritten before they hear results".
//
// That last clause is an ordering rule rather than a styling one. A reader whose words were
// silently replaced is reading answers to a question they did not ask. A sighted reader sees the
// banner above the list; a screen reader user hears whatever comes first in the document. So the
// banner is before the list in DOM order, not merely above it in the layout, and there is a test
// for that rather than a comment.
//
// Three things this component used to take as parameters it now reads off the records, because a
// renderer that accepts a fact it can derive is a renderer a caller can contradict:
//
//   - The badge text. Each row arrived carrying `match_label`, a finished sentence, so a row
//     whose `match_reasons` said `semantic` could be badged "matched your words" and nothing on
//     the page would disagree. The label is derived from `match_reasons` through one closed
//     table, and a reason with no label fails at import rather than printing nothing on the one
//     page whose job is to say why a row is there.
//   - Whether the query was rewritten. `expansions` and `understoodAs` were separate props and
//     could contradict the relaxation account the same screen discloses. Both now come out of
//     the account, so the banner and the disclosures cannot disagree.
//   - Which relaxation produced a row. A badge saying "semantic match" is standing evidence that
//     semantic retrieval ran. Inside a result set whose account says it did not, one of those two
//     is false, and the reader is looking at the badge. They are cross-checked here, where the
//     badge is rendered, rather than in a caller that may forget.
//
// Roving tabindex exists because a listbox with fifty tabbable rows makes Tab useless: reaching
// the content after the results means fifty presses. That property was asserted by counting
// `tabindex="0"` in the markup, and it was false anyway: every row carried a nested Read button,
// which is tabbable without any tabindex attribute at all, so fifty rows were fifty tab stops and
// the assertion could not see it. The row is now the only focusable thing in the list and it is
// the thing that opens, and the test counts focusable elements rather than one attribute.

import { useCallback, useEffect, useId, useRef, useState } from 'react';

import { INTERVAL_SENTENCE, semanticsOf } from '../scripts/publisher-vocabulary.mjs';
import { publisherOf } from '../scripts/record-identity.mjs';
import { MATCH_REASONS } from '../scripts/search-results.mjs';
import { isCalendarDate } from '../scripts/temporal.mjs';
import { interpretationOf, requireRelaxationAccount } from './RelaxationDisclosures.jsx';

/**
 * A lookup that holds only what it was given.
 *
 * The shape `publisher-vocabulary.mjs` adopted, and for its reason: an object literal answers
 * `constructor` and `toString` with inherited members, so a table written to fail closed quietly
 * succeeds for keys nobody put in it.
 */
function closedTable(entries) {
  return Object.freeze(Object.assign(Object.create(null), entries));
}

/**
 * What each match reason says on the badge. The words are the string renderer's, verbatim.
 *
 * Exported so a test can prove this table and the retrieval enum name the same set. The import
 * check below is a tripwire that fires on a future edit and cannot be observed today, because no
 * fixture can add a member to a frozen enum in another module; the exported table can be compared
 * directly, and that is the assertion that actually holds this closed.
 */
export const BADGE_LABELS = closedTable({
  exact_title: 'matched on title, not wording',
  keyword: 'matched your words',
  interpreted: 'interpreted (editorial layer, versioned, non-official)',
  semantic: 'semantic match',
});

// Checked against the retrieval enum at import, not at render. A reason added to `MATCH_REASONS`
// and not to this table would otherwise show nothing on a row, and only for the queries that
// happen to produce it.
for (const reason of MATCH_REASONS) {
  if (BADGE_LABELS[reason] === undefined) {
    throw new Error(`${reason} is a match reason with no badge label in the results listbox`);
  }
}
for (const labelled of Object.keys(BADGE_LABELS)) {
  if (!MATCH_REASONS.includes(labelled)) {
    throw new Error(`${labelled} is badged here and is not a match reason the service returns`);
  }
}

/**
 * Which relaxation a badge is evidence of.
 *
 * `exact_title` and `keyword` are the reader's own words and imply nothing about what else ran.
 * The other two are this service having gone beyond them: `interpreted` is the editorial
 * crosswalk, `semantic` is semantic retrieval.
 *
 * Exported so the screen's test can enumerate the rule rather than restate it.
 */
export const REASON_EVIDENCES = closedTable({
  interpreted: 'crosswalk',
  semantic: 'semantic',
});

/**
 * What the search did to the query before it ran, announced before the results.
 *
 * Rendered even when nothing was relaxed, because a screen that only speaks when it rewrote
 * something teaches a reader that silence means their words were used, and silence is also what
 * a broken disclosure looks like.
 *
 * Exported because there is one more place a reader ends up with no rows in front of them: every
 * row hidden by a filter they turned on. That is not a corpus miss and not the no-hit card, and
 * it is still a moment where the reader must be told their query was rewritten.
 */
export function Interpretation({ relaxations }) {
  const { expansions, understoodAs } = interpretationOf(relaxations);
  const rewritten = expansions.length > 0 || understoodAs !== null;
  return (
    <p className="results-interpretation" aria-live="polite">
      {rewritten ? (
        <>
          Your query was changed before it ran.
          {understoodAs === null ? null : ` Understood as: ${understoodAs}.`}
          {expansions.length === 0 ? null : ` Expansions applied: ${expansions.join(', ')}.`}
        </>
      ) : (
        'Your exact words were searched.'
      )}
    </p>
  );
}

/** The interval a row states about itself, refused rather than guessed at when it is not one. */
function requireInterval(hit, where) {
  if (!isCalendarDate(hit?.valid_from)) {
    throw new Error(`${where} valid_from is not a calendar date, so its interval cannot be said`);
  }
  if (hit.valid_to !== null && !isCalendarDate(hit.valid_to)) {
    throw new Error(`${where} valid_to is neither null nor a calendar date`);
  }
}

/**
 * The language a title is written in, from the record.
 *
 * A title is published as part of the expression it belongs to, so the expression's own
 * `language` answers when there is no separate `title_language`. Neither present is refused: an
 * unlabelled title is read in the voice of the chrome around it, and this corpus routinely puts
 * French statute inside English chrome.
 */
function titleLanguageOf(hit, where) {
  const declared = hit.title_language ?? hit.language;
  if (typeof declared !== 'string' || !/^[a-z]{2}$/.test(declared)) {
    throw new Error(
      `${where} carries a title and neither it nor the record says what language it is in; an ` +
        'unlabelled title is read in the voice of the chrome around it',
    );
  }
  return declared;
}

/** The badges a row has earned, cross-checked against what this result set says ran. */
function badgesOf(hit, where, relaxations) {
  const reasons = hit?.match_reasons;
  if (!Array.isArray(reasons) || reasons.length === 0) {
    throw new Error(
      `${where} does not say why it matched; a row that will not say cannot be told apart from ` +
        "one the reader's own words found",
    );
  }
  return reasons.map((reason) => {
    const label = BADGE_LABELS[reason];
    if (label === undefined) {
      throw new Error(
        `${JSON.stringify(reason)} is not a match reason; the set is ${MATCH_REASONS.join(', ')}`,
      );
    }
    const evidenced = REASON_EVIDENCES[reason];
    if (evidenced !== undefined && relaxations[evidenced].applied !== true) {
      throw new Error(
        `${where} is badged ${JSON.stringify(reason)}, which is evidence that the ${evidenced} ` +
          `relaxation ran, while this result set declares ${evidenced} as not applied; the ` +
          'badge and the account cannot both be true',
      );
    }
    return { reason, label };
  });
}

/** Where each key takes the tab stop. Closed, so an unhandled key falls through to the browser. */
const MOVES = closedTable({
  ArrowDown: (index, last) => Math.min(index + 1, last),
  ArrowUp: (index) => Math.max(index - 1, 0),
  Home: () => 0,
  End: (index, last) => last,
});

/**
 * The results listbox.
 *
 * @param {object} props
 * @param {Array} props.hits         rows, each carrying its own lex_id so it keeps its publisher
 * @param {object} props.relaxations the closed relaxation account this result set was produced
 *                                   under; the banner and the badge cross-check both read it
 * @param {Array} props.selected     the rows armed for comparison, in selection order
 * @param {Function} props.onOpen    called with the row the reader opened
 * @param {Function} props.onToggleSelect called with the row the reader armed or disarmed
 */
export function ResultList({ hits, relaxations, selected, onOpen, onToggleSelect }) {
  if (!Array.isArray(hits) || hits.length === 0) {
    throw new Error(
      'a results listbox needs rows; an empty list is the no-hit card, which says what ran and ' +
        'what this corpus holds, and is not this component',
    );
  }
  if (!Array.isArray(selected)) {
    throw new Error(
      'a results listbox states which rows are armed for comparison; without the selection it ' +
        'would report every row as unselected, which is a claim rather than a gap',
    );
  }
  requireRelaxationAccount(relaxations);

  // The tab stop, not the selection. Nothing is selected by arriving at it: moving focus through
  // candidates is not choosing one, and a listbox that marked the focused row as selected would
  // be answering on the reader's behalf.
  // Minted per instance for the same reason the date field mints its own: a hardcoded id is a
  // component that may appear once per document, and aria-describedby pointing at a duplicate id
  // resolves to whichever one the parser saw first.
  const helpId = useId();

  const [focused, setFocused] = useState(0);
  const rows = useRef([]);
  const moved = useRef(false);

  useEffect(() => {
    // Only after a keypress. Focusing on first render would steal focus from the query field the
    // reader is still typing in.
    if (moved.current) rows.current[focused]?.focus();
  }, [focused]);

  const onKeyDown = useCallback(
    (event, index) => {
      // Enter reads, Space arms. Both are explicit acts on the row the reader is standing on,
      // which is what keeps arriving at a row and choosing it separate.
      if (event.key === 'Enter') {
        event.preventDefault();
        onOpen(hits[index]);
        return;
      }
      if (event.key === ' ' || event.key === 'Spacebar') {
        event.preventDefault();
        onToggleSelect(hits[index]);
        return;
      }
      const move = MOVES[event.key];
      if (move === undefined) return;
      event.preventDefault();
      moved.current = true;
      setFocused(move(index, hits.length - 1));
    },
    [hits, onOpen, onToggleSelect],
  );

  const armed = new Set(selected.map((one) => one.lex_id));

  return (
    <>
      <Interpretation relaxations={relaxations} />
      {/* Said once, before the list, rather than repeated into every row. A per-row control is
          what made Tab useless here in the first place. */}
      <p className="results-help" id={helpId}>
        Arrow keys move between results. Enter reads the state you are on. Space selects it for
        comparison; selecting two states of one work arms Compare.
      </p>
      <ul
        className="results-list"
        role="listbox"
        aria-label="Search results"
        aria-describedby={helpId}
        // Two rows can be armed at once, and the rows say which. Without this a screen reader
        // announces a single-choice list and the second selection reads as a mistake.
        aria-multiselectable="true"
      >
        {hits.map((hit, index) => {
          const where = `hit ${index + 1}`;
          requireInterval(hit, where);
          const semantics = semanticsOf(publisherOf(hit.lex_id, where), where);
          const badges = badgesOf(hit, where, relaxations);
          const hasTitle = typeof hit.title === 'string' && hit.title.trim().length > 0;
          return (
            <li
              // Two provisions of one state legitimately share a lex_id; the search preview's own
              // fixture has two. A row is a position in one served page, and the position is what
              // distinguishes it.
              key={`${index}:${hit.lex_id}`}
              role="option"
              // Focus is not selection. That holds for the focused row too, and only the reader's
              // explicit Space changes it.
              aria-selected={armed.has(hit.lex_id)}
              className="results-row"
              // Exactly one row is reachable by Tab, and nothing inside a row is focusable at
              // all. That is what makes the content after a long list reachable in one press.
              tabIndex={index === focused ? 0 : -1}
              ref={(el) => {
                rows.current[index] = el;
              }}
              onKeyDown={(event) => onKeyDown(event, index)}
              onClick={() => onOpen(hit)}
              onFocus={() => setFocused(index)}
            >
              {hasTitle ? (
                // The title carries its own language. The chrome around it is routinely another
                // one, and an unlabelled French title inside English chrome is read aloud in an
                // English voice.
                <p className="results-title" lang={titleLanguageOf(hit, where)}>
                  {hit.title}
                </p>
              ) : null}
              {/* Block, not inline. Rendered as adjacent spans the title and the interval are
                  painted flush against each other, which the browser run reports and a reader
                  sees as one run of text: the date a state applied reads as part of its name. */}
              <p className="results-when">
                {INTERVAL_SENTENCE[semantics](hit.valid_from, hit.valid_to)}
              </p>
              {/* Every match badge carries a text label; colour and position are not the message.
                  The label is derived from the row's own reasons. */}
              <ul className="results-badges">
                {badges.map((badge) => (
                  <li key={badge.reason} className="results-badge">
                    {badge.label}
                  </li>
                ))}
              </ul>
            </li>
          );
        })}
      </ul>
    </>
  );
}
