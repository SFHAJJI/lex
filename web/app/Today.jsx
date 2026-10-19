// Today, the calendar date in Luxembourg, printed on every page that answers "as of today" (the specification of record's
// R13; the plan's item 3.4): "It is always printed, and written into the URL and the trace."
//
// Code resolves it, never a model and never the build's time: the coded EU summer-time rule (`temporal.mjs`, plan 0.8),
// read at the instant the server answered when the page carries an answer (`context.freshness.observed_at`), so the page
// the server rendered and the page the browser hydrates name one date; else at this browser's clock, once hydrated, so the
// page as built is the page hydrated. A clock outside the years the rule holds names no date rather than a wrong one.

import { useEffect, useState } from 'react';

import { fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { todayInLuxembourg } from '../scripts/temporal.mjs';

/**
 * Today in Luxembourg for a screen: `known`, the date the server's answer gives (a calendar date, or null), when there is
 * one; else this browser's clock, read once after hydration (null until then, and for a clock the rule does not read).
 */
export function useLuxembourgToday(known = null) {
  const [clock, setClock] = useState(null);
  useEffect(() => {
    if (known !== null) return;
    const now = todayInLuxembourg();
    if (now !== null) setClock(now);
  }, [known]);
  return known ?? clock;
}

/**
 * The line that says today beside a date field (`form.today`: "Today in Luxembourg: {date}."), under the id the field names
 * in `aria-describedby`; nothing while today is not known. A page holds one form with a date, so the id is the page's own
 * (an id React mints is minted from the tree's root, which the server's render and the browser's hydration do not share).
 */
export function TodayHint({ id, today }) {
  if (today === null || today === undefined) return null;
  return (
    <p id={id} className="field-hint" data-today={today}>
      {fillText(liveChrome().form.today, { date: today })}
    </p>
  );
}

/** Today said on a line of its own (`form.today`), on a page that shows an answer with no date field beside it: the dossier. */
export function TodayLine({ today }) {
  if (today === null || today === undefined) return null;
  return <p data-today={today}>{fillText(liveChrome().form.today, { date: today })}</p>;
}
