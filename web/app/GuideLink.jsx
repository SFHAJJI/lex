// A banner's info control (the specification of record's R48: "A 'How to read Lex' guide and a French and English glossary,
// linked from each banner's info control"; the plan's item 6.6): a link to the section of How to read Lex that explains the
// banner it stands in. Its visible words are the table's (`reference.info`), and its name holds them followed by the
// section's own heading (`reference.infoNamed`), so a reader who lists the links hears where each one leads (WCAG 2.4.4),
// and the name a voice user says is the one they see first (2.5.3). It is a plain link, which works with no script.
//
// The banners and their sections: the staleness banner and the freshness strip (`freshness`), the as-published label and the
// EU acknowledgement and authenticity statement (`versions`).

import { fillText, liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { referencePage } from '../scripts/reference-pages.mjs';

/** The guide's sections a banner can point to, each by its key in `guide.sections`. */
export const GUIDE_LINK_SECTIONS = Object.freeze(['freshness', 'versions']);

/** The address of a section of How to read Lex in this bundle's language: its page, and the section's heading as anchor. */
export function guideSectionHref(section) {
  if (!GUIDE_LINK_SECTIONS.includes(section)) throw new Error(`${JSON.stringify(section)} is no section a banner points to`);
  return `${livePath(referencePage('guide').file)}#guide-${section}`;
}

export function GuideLink({ section }) {
  const copy = liveChrome();
  return (
    <a
      className="info-control"
      href={guideSectionHref(section)}
      aria-label={fillText(copy.reference.infoNamed, { section: copy.guide.sections[section].heading })}
      data-guide-link={section}
    >
      {copy.reference.info}
    </a>
  );
}
