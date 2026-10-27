// The breadcrumbs of a law's pages (the specification of record's R48; `breadcrumbs.mjs` says which crumbs a page has and
// what each says): a navigation landmark named by the table, an ordered list in the record's order (publisher, act,
// article, version), the page's own crumb marked current and no link, and nothing at all for a page with no crumbs. The
// separators are the stylesheet's, so a screen reader hears the list, not the marks between its items.

import { liveChrome } from '../scripts/live-chrome.mjs';

export function Breadcrumbs({ crumbs }) {
  if (!Array.isArray(crumbs) || crumbs.length === 0) return null;
  return (
    <nav className="breadcrumbs" aria-label={liveChrome().breadcrumbs.label} data-breadcrumbs={crumbs.map((crumb) => crumb.part).join(' ')}>
      <ol>
        {crumbs.map((crumb) => {
          const text = crumb.code ? <code>{crumb.text}</code> : crumb.text;
          return (
            <li key={crumb.part} data-crumb={crumb.part} aria-current={crumb.current ? 'page' : undefined}>
              {crumb.href === null ? text : <a href={crumb.href}>{text}</a>}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
