// How current the law on this site is, and the commits behind the answer (the specification of record's R87 and R23; the
// plan's items 6.1 and 6.5): the strip every live page carries in its footer, and the banner under its header when a
// publisher channel's data is past the declared maximum age. Both are said from an answer's facts (`freshness.mjs`), never
// computed here: a page the server rendered from an answer says them from it, a page as built says the strip's as-built
// line, and either says them again from each answer it later receives (`freshness-client.jsx`).
//
// Each stands in a container of its own (`FRESHNESS_STRIP_ID`, `STALENESS_BANNER_ID`) outside the page's hydrated root, so
// the browser can draw it again without touching what the server rendered for the root. The strip holds no section inside
// it, so the deploy probe can set it aside when it holds a rendered home page to the page as built (`deploy-probe.mjs`).

import { fillCounted, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { freshnessKey, staleChannels } from '../scripts/freshness.mjs';

/** The footer's container of the strip. */
export const FRESHNESS_STRIP_ID = 'site-freshness';

/** The container under the header that holds the staleness banner when there is one. */
export const STALENESS_BANNER_ID = 'site-staleness';

function Commit({ label, commit, unstated }) {
  return (
    <div className="strip-row">
      <dt>{label}</dt>
      <dd>{commit === null ? unstated : <code className="commit">{commit}</code>}</dd>
    </div>
  );
}

/** The strip: the index's build date, each channel's last complete collection and its lag, and the two commits. */
export function FreshnessStrip({ facts = null }) {
  const copy = liveChrome().freshness;
  if (facts === null) {
    return (
      <section className="freshness-strip" aria-label={copy.heading} data-freshness="as-built">
        <p>{copy.asBuilt}</p>
      </section>
    );
  }
  return (
    <section className="freshness-strip" aria-label={copy.heading} data-freshness="answered" data-stale={String(facts.stale)}>
      <p data-index-built={facts.builtOn ?? ''}>{facts.builtOn === null ? copy.builtUndated : fillText(copy.built, { date: facts.builtOn })}</p>
      {facts.channels.length === 0 ? null : (
        <ul className="freshness-channels">
          {facts.channels.map((channel) => (
            <li key={channel.publisher} data-channel={channel.publisher} data-completed-on={channel.completedOn ?? ''} data-stale={channel.stale === null ? '' : String(channel.stale)}>
              {channel.completedOn === null
                ? fillText(copy.collectedUnstated, { channel: copy.channels[channel.publisher] })
                : fillCounted(copy.collected, channel.ageDays, { channel: copy.channels[channel.publisher], date: channel.completedOn, today: facts.today })}
            </li>
          ))}
        </ul>
      )}
      <dl className="freshness-commits">
        <Commit label={copy.indexCommit} commit={facts.indexCommit} unstated={copy.commitUnstated} />
        <Commit label={copy.serviceCommit} commit={facts.serviceCommit} unstated={copy.commitUnstated} />
      </dl>
    </section>
  );
}

/**
 * The banner, for each channel past the maximum age; nothing when none is. Its heading is one line, and each channel's
 * sentence one step away (`<details>`, which works without scripts), so the home page keeps its box and the sentence under
 * it on a phone's first screen (R47) while the warning stands above them; the footer's strip says the same dates in full.
 */
export function StalenessBanner({ facts = null }) {
  const stale = staleChannels(facts);
  if (stale.length === 0) return null;
  const copy = liveChrome().freshness;
  return (
    <aside className="staleness-banner" role="note" aria-label={copy.staleHeading} data-stale-channels={stale.map((channel) => channel.publisher).join(' ')}>
      <details>
        <summary><strong>{copy.staleHeading}</strong></summary>
        {stale.map((channel) => (
          <p key={channel.publisher} data-channel={channel.publisher}>
            {fillCounted(copy.stale, facts.maximumAgeDays, { channel: copy.channels[channel.publisher], date: channel.completedOn, today: facts.today })}
          </p>
        ))}
      </details>
    </aside>
  );
}

/** The strip's container in the footer, holding the strip for `facts`, keyed so a page draws it again only for new facts. */
export function FreshnessStripSlot({ facts = null }) {
  return (
    <div id={FRESHNESS_STRIP_ID} data-freshness-key={freshnessKey(facts)}>
      <FreshnessStrip facts={facts} />
    </div>
  );
}

/** The banner's container under the header, empty unless a channel is past the maximum age. */
export function StalenessBannerSlot({ facts = null }) {
  return (
    <div id={STALENESS_BANNER_ID} data-freshness-key={freshnessKey(facts)}>
      <StalenessBanner facts={facts} />
    </div>
  );
}
