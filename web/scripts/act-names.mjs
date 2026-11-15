// An act of Luxembourg named in words from its work key, for a list that names many acts and asks for none of their titles
// (the changes page; the owner's order of 2026-11-15, "Changes made readable"):
//
// - A law or a grand-ducal regulation by its kind and its date, as Luxembourg names its acts ("loi du 29 octobre 2025"): the
//   key is the ELI's path (`loi-2025-10-29-a477` for `eli/etat/leg/loi/2025/10/29/a477`), so its date is the act's own. The
//   date is written as the page writes every date (`displayDate`: 29/10/2025 on a French page, 2025-10-29 on an English one),
//   never with a month's name (`live-a11y.test.mjs`). Two acts of one kind can share a date; the list tells them apart by the
//   permalink each row prints, which holds the key (a part of a key shown alone would be text that is neither the table's
//   nor the answer's).
// - A code by its name, as the title its dossier holds names it (read on 2026-11-15 from the live dossier of each).
//
// Any other key has no name here (null), and the list shows the key itself. Data and a pure function only.

import { CHROME_LOCALE, displayDate, fillText, liveChrome } from './live-chrome.mjs';
import { isCalendarDate } from './temporal.mjs';

const DATED_ACT = /^(loi|rgd)-(\d{4}-\d{2}-\d{2})-[a-z0-9]+$/u;

/** The act's name in words in `locale`, or null when its key names no act this table names. */
export function actNameOf(workKey, locale = CHROME_LOCALE) {
  if (typeof workKey !== 'string') return null;
  const copy = liveChrome(locale).common.actNames;
  const dated = DATED_ACT.exec(workKey);
  if (dated !== null) {
    return isCalendarDate(dated[2]) ? fillText(copy.dated[dated[1]], { date: displayDate(dated[2], locale) }) : null;
  }
  return Object.hasOwn(copy.codes, workKey) ? copy.codes[workKey] : null;
}
