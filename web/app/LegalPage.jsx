// The five legal pages, live: the legal notice, the privacy notice, the terms of use, the accessibility statement and the
// cookies page (the plan's item 7.1; the specification of record's R49, as the owner's decision of 2026-10-19 departs from
// it; `scripts/legal-pages.mjs`).
//
// Built into `dist-live/{language}/{file}` (by `scripts/build-live.mjs`), once per interface language, and served by both
// servers as the files they are. Each page says its chrome section's words (`live-chrome.mjs`), in its parts' order. By
// the owner's decision of 2026-10-19 they name no person and give no postal address, no e-mail address and no contact, so
// a page holds nothing but the table's words and the few values below. A page asks nothing, ships no script and is never
// indexed (`indexing` stays `never`); so its footer's freshness strip says it carries no answer (`answers` false).

import { Document } from './Document.jsx';
import { Say, inEnglish } from './LiveAnswer.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { renderDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { longDate } from '../scripts/live-search.mjs';
import { ACCESSIBILITY_LAW, CNPD_SITE, EURLEX_ACKNOWLEDGEMENT, LEGAL_PAGES, LEGAL_TEXTS_UPDATED, legalPage } from '../scripts/legal-pages.mjs';

/**
 * Every value a legal page's sentences may name (`live-chrome.mjs`, the comment above `legal`), for the page's language:
 * the footer's two statements, the links to other legal pages, the CNPD's site (the regulator a reader may complain to, a
 * link and no postal address), the EU texts' acknowledgement (in English, marked so on a French page) and the
 * accessibility law's French title.
 */
export function legalValues() {
  const copy = liveChrome();
  const pageLink = (key) => <a href={livePath(legalPage(key).file)}>{copy.legal.inline[key]}</a>;
  return {
    notAdvice: copy.site.notAdvice,
    authenticity: copy.site.authenticity,
    legalNoticePage: pageLink('legalNotice'),
    cookiesPage: pageLink('cookies'),
    cnpdSite: <a href={CNPD_SITE.href} rel="external">{CNPD_SITE.text}</a>,
    eurlexAcknowledgement: inEnglish(EURLEX_ACKNOWLEDGEMENT),
    accessibilityLaw: <span lang="fr">{ACCESSIBILITY_LAW}</span>,
  };
}

/** A sentence of a legal page, filled with the values its own placeholders name, and no other. */
function Sentence({ template, values }) {
  const named = Object.fromEntries([...template.matchAll(/\{([A-Za-z]+)\}/g)].map(([, name]) => {
    if (!Object.hasOwn(values, name)) throw new Error(`a legal page's sentence names {${name}}, which no legal page fills: ${JSON.stringify(template)}`);
    return [name, values[name]];
  }));
  return <Say template={template} values={named} />;
}

/** A legal page's content: its eyebrow, heading and introduction, its parts in order, and the date its text was last changed. */
export function LegalPageBody({ pageKey }) {
  const copy = liveChrome();
  const page = copy[pageKey];
  const values = legalValues();
  return (
    <>
      <p className="eyebrow">{page.eyebrow}</p>
      <h1>{page.heading}</h1>
      <p>{page.intro}</p>
      {Object.entries(page.sections).map(([name, section]) => (
        <section key={name} className="legal-section" aria-labelledby={`legal-${name}`} data-legal-section={name}>
          <h2 id={`legal-${name}`}>{section.heading}</h2>
          {Object.entries(section).filter(([key]) => key !== 'heading').map(([key, value]) => (typeof value === 'string'
            ? <p key={key}><Sentence template={value} values={values} /></p>
            : (
              <ul key={key}>
                {Object.entries(value).map(([item, text]) => <li key={item}><Sentence template={text} values={values} /></li>)}
              </ul>
            )))}
        </section>
      ))}
      <p className="legal-updated" data-legal-updated="">
        <Say template={copy.legal.updated} values={{ date: <time dateTime={LEGAL_TEXTS_UPDATED}>{longDate(LEGAL_TEXTS_UPDATED)}</time> }} />
      </p>
    </>
  );
}

/** One legal page, by its chrome section's key (`LEGAL_PAGES`). */
export function renderLiveLegalPage(pageKey) {
  const page = legalPage(pageKey);
  if (page === undefined) throw new Error(`${JSON.stringify(pageKey)} is none of the legal pages: ${LEGAL_PAGES.map((one) => one.key).join(', ')}`);
  return renderDocument(
    <Document
      state={page.state}
      title={liveChrome()[pageKey].title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
      localePage={page.file}
      answers={false}
    >
      <LegalPageBody pageKey={pageKey} />
    </Document>,
  );
}

export const renderLiveLegalNoticePage = () => renderLiveLegalPage('legalNotice');
export const renderLivePrivacyPage = () => renderLiveLegalPage('privacy');
export const renderLiveTermsPage = () => renderLiveLegalPage('terms');
export const renderLiveAccessibilityPage = () => renderLiveLegalPage('accessibility');
export const renderLiveCookiesPage = () => renderLiveLegalPage('cookies');
