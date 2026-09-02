// The as-of date control.
//
// UX spec section 5. Two rules here are not interaction preferences.
//
// "No silent default anywhere: empty means the control shows today as a removable chip before
// any query runs." Today is the date a reader will not think to check, precisely because it is
// the one they would have assumed. A field that quietly resolves to now returns an answer about
// now to someone who may have been asking about a contract signed in 2019, and nothing on the
// page says which question was answered. So the default is shown before it is used, and it can
// be removed.
//
// "On submit the field announces the resolved state via aria-live and visible text." The
// announcement is the accessibility spec for the whole subsystem: assistive technology hears the
// resolved interval before the content loads. A reader who hears the text without the interval
// has no way to know which state they are being read.
//
// The vocabulary comes from the publisher, as everywhere else: the Union does not date
// applicability.

import { useCallback, useId, useState } from 'react';

import { RESOLUTION_SENTENCE, semanticsOf } from '../scripts/publisher-vocabulary.mjs';
import { identityOf } from '../scripts/record-identity.mjs';

const ISO = /^(\d{4})-(\d{2})-(\d{2})$/;
const EUROPEAN = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/;

/**
 * Read a date the way a reader wrote it, or say it could not be read.
 *
 * Text first, both spellings, and no guessing: a string this cannot parse is refused rather than
 * coerced, because a coerced date answers a question nobody asked.
 */
export function parseAsOf(value) {
  const text = String(value ?? '').trim();
  const iso = ISO.exec(text);
  if (iso) return isReal(text) ? text : null;
  const european = EUROPEAN.exec(text);
  if (!european) return null;
  const [, day, month, year] = european;
  const candidate = `${year}-${month.padStart(2, '0')}-${day.padStart(2, '0')}`;
  return isReal(candidate) ? candidate : null;
}

/** A calendar date that exists. 2021-02-30 parses and is not a day. */
function isReal(iso) {
  const [year, month, day] = iso.split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  return (
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
  );
}

/**
 * The resolution sentence, in the publisher's own vocabulary.
 *
 * The publisher is read out of the resolved state's own identifier rather than taken as a
 * separate field. A resolution that could be labelled with one publisher's words while naming
 * another publisher's record is the defect this interface exists to prevent, and it is not worth
 * reintroducing here for the sake of one shorter parameter.
 *
 * Exported so the screen and its test read the same words, and so no caller can compose a
 * different sentence beside the same resolution.
 */
export function resolutionSentence(resolved) {
  const identity = identityOf(resolved?.lex_id, 'a date resolution');
  const semantics = semanticsOf(identity.publisher, 'a date resolution');
  return RESOLUTION_SENTENCE[semantics](
    resolved.valid_from,
    resolved.valid_to,
    resolved.publication_date,
  );
}

/**
 * The date field.
 *
 * @param {object} props
 * @param {string} props.today       the date this page was rendered for, never read from a clock
 * @param {object|null} props.resolved the state the last submitted date resolved to
 * @param {Function} props.onSubmit  called with an ISO date
 */
export function DateField({ today, resolved = null, onSubmit }) {
  if (!ISO.test(String(today ?? ''))) {
    throw new Error(
      'the date field is told what today is rather than reading a clock, because a control that ' +
        'consults its own clock answers a question the reader did not ask and cannot reproduce',
    );
  }

  // Minted per instance, never written by hand. A hardcoded id is a control that may appear once
  // per document, and this one appears on every screen that scopes anything to a date: two of
  // them on one page silently break the label association for both, so a screen reader announces
  // an unlabelled text box and a click on the label focuses the other field.
  const fieldId = useId();

  const [text, setText] = useState('');
  const [defaultRemoved, setDefaultRemoved] = useState(false);
  const [unreadable, setUnreadable] = useState(false);

  const submit = useCallback(
    (event) => {
      event.preventDefault();
      const parsed = parseAsOf(text);
      if (parsed === null) {
        // Refused, not coerced. A date this cannot read is a question it must not answer.
        setUnreadable(true);
        return;
      }
      setUnreadable(false);
      onSubmit(parsed);
    },
    [onSubmit, text],
  );

  const showingDefault = text === '' && !defaultRemoved;

  return (
    <form className="date-field" onSubmit={submit}>
      <label htmlFor={fieldId}>Date to read the law at</label>
      <input
        id={fieldId}
        name="as-of"
        type="text"
        inputMode="numeric"
        autoComplete="off"
        placeholder="yyyy-mm-dd or dd/mm/yyyy"
        value={text}
        onChange={(event) => setText(event.target.value)}
      />

      {/* The default is shown before it is used, and can be removed. A field that silently
          resolves to now answers about now, and nothing on the page says so. */}
      {showingDefault ? (
        <p className="date-default">
          <span>No date entered. This will read the law as it stands on {today}.</span>
          <button type="button" onClick={() => setDefaultRemoved(true)}>
            Remove this default and choose a date
          </button>
        </p>
      ) : null}

      {unreadable ? (
        <p className="date-unreadable" role="alert">
          That date could not be read. Write it as yyyy-mm-dd or dd/mm/yyyy. Nothing has been
          searched, and no date has been assumed.
        </p>
      ) : null}

      <button type="submit">Read the law at this date</button>

      {/* The resolution, announced. Assistive technology hears the interval before the content. */}
      <p className="date-resolution" aria-live="polite">
        {resolved === null ? '' : resolutionSentence(resolved)}
      </p>
    </form>
  );
}
