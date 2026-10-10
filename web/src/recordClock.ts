/**
 * The record clock: when the publisher published a dated state, and when Lex first observed it.
 *
 * Trust rule 3 of the UX specification, section 14: "Both clocks on every dated object: legal
 * interval plus published/observed." The legal clock is `temporal.ts`, which phrases an interval
 * in the publisher's own vocabulary. This is the other one, and the product is not checkable
 * without it: 2,748 of the 4,656 Luxembourg states this corpus holds were published AFTER the
 * date they claim to govern. A row that states legal time alone tells a reader that a wording
 * applied from a date, while withholding the fact that the text saying so did not exist yet.
 * That is section 15's stale-revision failure, and both clocks side by side are the whole
 * countermeasure. Section 6 states the convention in words: "Top: when the publisher says the
 * state applied. Bottom: when the publisher published it. These routinely differ."
 *
 * TWO FIELDS, READ ONCE, NEVER CROSSED. `DocRow.PublicationDate` is nullable and
 * `DocRow.ObservedFrom` is not, so an absent publication date is a real state of the record
 * rather than a transport failure, and it is disclosed as absent. It is never filled in from
 * the observation instant, which is when LEX saw the file rather than when the publisher issued
 * it, and never from `valid_from`, which is the publisher's legal claim. Either substitution
 * would manufacture the exact fact the reader opened the row to check.
 *
 * Every sentence below is scoped to THIS RESPONSE rather than to the corpus. "No publication
 * date in this response" is a statement about what arrived; "the publisher recorded none" would
 * be a claim about the record that a client holding one envelope cannot make.
 */

/** The record clock one response carried for one dated object. Both members may be absent. */
export interface RecordClock {
  /** `publication_date` verbatim; nullable at the producer, so absent is a real answer. */
  publicationDate?: string;
  /** `observed_from` verbatim: the UTC instant Lex first held this record. */
  observedFrom?: string;
}

export const NO_RECORD_CLOCK = "No record clock in this response.";
export const NO_PUBLICATION_DATE = "No publication date in this response.";
export const NO_OBSERVATION = "No first observation in this response.";
/**
 * Two publisher states on one legal date do not collapse into one record clock. Picking either
 * would assert a resolution nobody made, which section 14 forbids by name: the product never
 * implies "that the product chose between overlapping states".
 */
export const SEVERAL_STATES_ON_DATE =
  "Several publisher states carry this date, with different record clocks.";

const fieldsOf = (row: unknown): Record<string, unknown> =>
  typeof row === "object" && row !== null && !Array.isArray(row)
    ? row as Record<string, unknown>
    : {};

/** A field the response actually carried. An empty string is not a date. */
const carried = (value: unknown): string | undefined =>
  typeof value === "string" && value.trim().length > 0 ? value : undefined;

/**
 * The record clock of one row, read from the two wire fields and from nothing else.
 *
 * Every surface goes through here so that no renderer can invent a third field name, and so
 * that the one place a substitution could be written is the one place a test can watch.
 */
export function recordClockOf(row: unknown): RecordClock {
  const fields = fieldsOf(row);
  return {
    publicationDate: carried(fields.publication_date),
    observedFrom: carried(fields.observed_from),
  };
}

/** The record-clock line, in the grammar section 5 gives the state banner. */
export function recordClockLabel(clock: RecordClock | undefined): string {
  const published = clock?.publicationDate;
  const observed = clock?.observedFrom;
  if (published === undefined && observed === undefined) return NO_RECORD_CLOCK;
  const first = `Published ${published ?? observed}.`;
  const second = observed === undefined ? NO_OBSERVATION : `First observed ${observed}.`;
  return `${first} ${second}`;
}

/** One legal date's record clock, or the statement that the date carries more than one. */
export interface DatedRecordClock {
  clock: RecordClock;
  /** The publisher put more than one state on this date and their record clocks disagree. */
  several: boolean;
}

/**
 * The record clock per legal date, for a rail whose ticks are dates rather than versions.
 *
 * Same-dated states with the SAME record clock are one clock, because there is no disagreement
 * to disclose. Same-dated states that disagree become `several`, and carry no clock at all.
 */
export function recordClocksByDate(rows: readonly unknown[]): Map<string, DatedRecordClock> {
  const byDate = new Map<string, DatedRecordClock>();
  for (const row of rows) {
    const date = carried(fieldsOf(row).valid_from);
    if (date === undefined) continue;
    const clock = recordClockOf(row);
    const held = byDate.get(date);
    if (held === undefined) { byDate.set(date, { clock, several: false }); continue; }
    if (held.several) continue;
    if (held.clock.publicationDate === clock.publicationDate
      && held.clock.observedFrom === clock.observedFrom) continue;
    byDate.set(date, { clock: {}, several: true });
  }
  return byDate;
}

/** The rail's line for one tick: its record clock, or the several-states disclosure. */
export function railClockLabel(entry: DatedRecordClock | undefined): string {
  return entry?.several ? SEVERAL_STATES_ON_DATE : recordClockLabel(entry?.clock);
}
