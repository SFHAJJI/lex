// The five legal pages, live: the legal notice, the privacy notice, the terms of use, the accessibility statement and the
// cookies page (the plan's item 7.1; the specification of record's R49; `scripts/legal-pages.mjs`).
//
// Built into `dist-live/{language}/{file}` (by `scripts/build-live.mjs`), once per interface language, and served by both
// servers as the files they are. Each page says its chrome section's words (`live-chrome.mjs`), in its parts' order, with
// the publisher's details from the one data file the owner fills (`legal-publisher.mjs`): a detail still unfilled is
// printed as its placeholder, «LIKE_THIS», and marked (`data-legal-placeholder`), so the opening's check finds it and the
// site does not open with it. A page asks nothing, ships no script and is never indexed (`indexing` stays `never`).

import { Document } from './Document.jsx';
import { Say } from './LiveAnswer.jsx';
import { fillCounted, liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { renderDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { longDate } from '../scripts/live-search.mjs';
import { LEGAL_PUBLISHER } from '../scripts/legal-publisher.mjs';
import {
  ACCESSIBILITY_LAW, CNPD_SITE, EURLEX_SITE, LEGAL_PAGES, LEGAL_TEXTS_UPDATED, PLACEHOLDER_ATTRIBUTE, PUBLISHER_PLACEHOLDERS, isUnfilled, legalPage,
} from '../scripts/legal-pages.mjs';

/** An unfilled detail: its placeholder, marked, whatever the data file holds in its place (an empty value included). */
function Unfilled({ field }) {
  const placeholder = PUBLISHER_PLACEHOLDERS[field];
  return <span {...{ [PLACEHOLDER_ATTRIBUTE]: placeholder.slice(1, -1) }}>{placeholder}</span>;
}

/**
 * Every value a legal page's sentences may name (`live-chrome.mjs`, the comment above `legal`), for the page's language:
 * the publisher's details, the footer's two statements, the links to the other legal pages and to the CNPD, the EU
 * acknowledgement's address and the accessibility law's French title.
 */
export function legalValues(publisher = LEGAL_PUBLISHER) {
  const copy = liveChrome();
  const detail = (field, filled) => (isUnfilled(publisher?.[field]) ? <Unfilled field={field} /> : filled(publisher[field]));
  const pageLink = (key) => <a href={livePath(legalPage(key).file)}>{copy.legal.inline[key]}</a>;
  return {
    publisherName: detail('publisherName', (value) => value),
    publisherAddress: detail('publisherAddress', (value) => value),
    contactEmail: detail('contactEmail', (value) => <a href={`mailto:${value}`}>{value}</a>),
    retention: detail('reportRetentionMonths', (value) => fillCounted(copy.legal.months, Number(value))),
    notAdvice: copy.site.notAdvice,
    authenticity: copy.site.authenticity,
    termsPage: pageLink('terms'),
    cookiesPage: pageLink('cookies'),
    cnpdSite: <a href={CNPD_SITE.href} rel="external">{CNPD_SITE.text}</a>,
    eurlexSite: EURLEX_SITE,
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
export function LegalPageBody({ pageKey, publisher = LEGAL_PUBLISHER }) {
  const copy = liveChrome();
  const page = copy[pageKey];
  const values = legalValues(publisher);
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

/** One legal page, by its chrome section's key (`LEGAL_PAGES`), with the publisher's details given (the data file's by default). */
export function renderLiveLegalPage(pageKey, { publisher = LEGAL_PUBLISHER } = {}) {
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
    >
      <LegalPageBody pageKey={pageKey} publisher={publisher} />
    </Document>,
  );
}

export const renderLiveLegalNoticePage = (options) => renderLiveLegalPage('legalNotice', options);
export const renderLivePrivacyPage = (options) => renderLiveLegalPage('privacy', options);
export const renderLiveTermsPage = (options) => renderLiveLegalPage('terms', options);
export const renderLiveAccessibilityPage = (options) => renderLiveLegalPage('accessibility', options);
export const renderLiveCookiesPage = (options) => renderLiveLegalPage('cookies', options);
