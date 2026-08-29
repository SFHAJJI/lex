/**
 * The publisher's own stated date, kept rather than stripped.
 *
 * Legilux consolidated titles begin "Version consolidee applicable au DD/MM/YYYY : ", and the list
 * views strip that prefix as noise. It is noise only while it agrees with the date Lex displays.
 * Measured against the mounted Luxembourg index: 2,783 of 4,649 records carry such a date, and 272
 * of them, 9.8 percent, state a date different from the record's own valid_from, sometimes by more
 * than a year. Every record claims valid_time_source "publisher", so both dates are presented as the
 * publisher's and they disagree.
 *
 * Stripping is therefore not a formatting choice in those 272 cases. It removes the only evidence a
 * reader has that the two claims differ, on a product whose whole proposition is that an answer can
 * be checked rather than trusted. This module does not decide which date is correct. It makes the
 * disagreement visible and leaves the judgement to the reader.
 */

/** ISO date if the title states a publisher applicability date, otherwise undefined. */
export function publisherStatedDate(title?: string): string | undefined {
  if (!title) return undefined;
  const m = /applicable au (\d{1,2})[./](\d{1,2})[./](\d{4})/.exec(title)
    ?? /applicable au (\d{4})-(\d{2})-(\d{2})/.exec(title);
  if (!m) return undefined;
  // The two accepted forms are day-first and ISO. Distinguish by which group holds the year.
  const iso = m[1].length === 4
    ? `${m[1]}-${m[2]}-${m[3]}`
    : `${m[3]}-${m[2].padStart(2, "0")}-${m[1].padStart(2, "0")}`;
  return /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/.test(iso) ? iso : undefined;
}

export interface TitleDateDisagreement {
  /** What the publisher's own title says this version applies from. */
  publisher: string;
  /** What Lex is displaying as the version date. */
  displayed: string;
}

/**
 * A disagreement, or undefined when there is nothing to show. Undefined covers three different
 * situations and deliberately does not distinguish them, because none of them is a disagreement:
 * the title states no date, the displayed date is unknown, or the two agree.
 */
export function titleDateDisagreement(
  title?: string,
  displayed?: string,
): TitleDateDisagreement | undefined {
  const publisher = publisherStatedDate(title);
  if (!publisher || !displayed) return undefined;
  return publisher === displayed.slice(0, 10) ? undefined : { publisher, displayed: displayed.slice(0, 10) };
}
