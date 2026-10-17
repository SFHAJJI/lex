// What a reading at a law's address shows before the reading itself (the specification of record's R64 and R67; the
// plan's item 3.2): the article the address cites, first, and what has changed since the version it is quoted from; or,
// when the address pins a version the site does not hold at its date, the link to the one it holds.
//
// Three rules, each a sentence the page would otherwise get wrong:
//   - The cited article comes first, under its own title (its publisher id, and an EU provision's heading), quoted as the
//     reading quotes it (`ArticleText`, `QuoteEvidence`), with the version it is quoted from and a link to the whole act at
//     that version. The page of the act that holds it follows (the plan's item 3.7: the API answers the page that holds the
//     article an address names), the cited article in its place too, with the act's contents and its other pages.
//   - An address whose article the version does not hold is refused (`anchor_not_in_version`, with the ids nearest it):
//     the page says so on the refusal card, and links to the whole act, at that version or today.
//   - A permalink always shows the bytes it cites (R64), so a version that is not the latest held says so: "A newer version
//     is applicable from {date} (publisher)" for a Luxembourg state, in the publisher's words (R21), and "A later wording of
//     this act is held" for an EU wording, whose date is never an applicability date. The date is the answer's next date.
//   - A permalink whose digest is not the version the site holds at its date is refused (`pinned_digest_mismatch`), never
//     answered with another version: the page says so, with the current version's link, above the refusal card.
//
// The cited article carries its citations (`Citations`): the one citation generator's three forms (`citation.mjs`; the plan's
// item 3.3, R65), each said in the language of the text it cites, under the page's labels, as text a reader copies.
//
// What it does not say: "The publisher replaced this file on {date}". The API's typed fact for it, a chained log's
// `file_replaced` event, holds no date of the replacement (no observation time is held; a build's time only bounds it
// from above), so the sentence would state a date Lex does not hold (STATUS-WEB.md, driver decisions).

import { Permalink, Say } from './LiveAnswer.jsx';
import { ArticleText, EuropeWordingHeading, QuoteEvidence } from './LiveReading.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import { CHROME_LOCALE, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { anchorText, currentAddress, permalinkHref } from '../scripts/permalinks.mjs';
import { citationForms, citationLang } from '../scripts/citation.mjs';

const COPY = liveChrome().reading;

/**
 * The version (a Luxembourg state or an EU wording) a reading shows for what a pin names (`readingPinOf`): at a permalink,
 * the one it pins; at a readable address, the one in its language; undefined when the reading shows none of them.
 */
export function versionShown(view, pin) {
  const versions = view.publisher === 'eu-eurlex' ? view.wordings : view.states;
  return pin.kind === 'state'
    ? versions.find((version) => version.permalink === pin.permalink)
    : versions.find((version) => version.language === pin.language);
}

/**
 * The article a pin names, as the version shown holds it: `{ publisherId, quoted }`, `quoted` null for an article held
 * without text; null when the pin names no article or the version holds none with that publisher id.
 */
export function citedArticle(version, pin) {
  const publisherId = anchorText(pin.anchor);
  if (version === undefined || publisherId === null) return null;
  const quoted = version.articles.find((article) => article.publisherId === publisherId);
  if (quoted !== undefined) return Object.freeze({ publisherId, quoted });
  return version.articlesWithoutText.some((article) => article.publisherId === publisherId)
    ? Object.freeze({ publisherId, quoted: null })
    : null;
}

/** The page's own title at an address that cites an article the version shown holds (R64), or null. */
export function citedTitle(outcome, pin) {
  if (pin === null || outcome.state !== 'success') return null;
  const cited = citedArticle(versionShown(outcome.view, pin), pin);
  if (cited === null) return null;
  return fillText(COPY.citedTitle, { work: outcome.view.publisher === 'eu-eurlex' ? outcome.view.celex : outcome.view.workKey, article: cited.publisherId });
}

/** The heading of the version an article is quoted from, as the reading heads it. */
function VersionHeading({ view, version }) {
  if (view.publisher === 'eu-eurlex') return <EuropeWordingHeading wording={version} celex={view.celex} />;
  return version.nextApplicabilityDate === null
    ? <Say template={COPY.stateHeading} values={{ work: view.workKey, language: version.language, from: version.applicabilityDate }} />
    : <Say template={COPY.stateHeadingNext} values={{ work: view.workKey, language: version.language, from: version.applicabilityDate, next: version.nextApplicabilityDate }} />;
}

/**
 * What changed since the version shown: a newer version (or a later EU wording) and the act today. A Luxembourg state's next
 * date is a state the index holds, which the sentence names and says nothing of its text. An EU wording's next date bounds it
 * whether or not the next wording's text is held here (review of #972), so it is said held only when it is, and otherwise
 * as the publisher's record names it, with no link to a text this site does not hold.
 */
function ChangedSince({ view, version, pin, cited }) {
  const europe = view.publisher === 'eu-eurlex';
  const next = europe ? version.nextDate : version.nextApplicabilityDate;
  if (typeof next !== 'string') return null;
  const notHeld = europe && version.nextTextHeld !== true;
  // At a permalink, the act as it stands today, at its readable address (R67); a readable address is that page already.
  const today = pin.kind === 'state' && !notHeld
    ? permalinkHref(currentAddress({ publisher: view.publisher, work: europe ? view.celex : view.workKey, language: version.language, publisherId: cited?.publisherId ?? null }), CHROME_LOCALE)
    : null;
  return (
    <p data-changed-since="">
      <Say template={notHeld ? COPY.europeLaterWordingNotHeld : europe ? COPY.europeNewerWording : COPY.newerVersion} values={{ date: next }} />
      {today === null ? null : (
        <>
          {' '}
          <a href={today}>{COPY.openToday}</a>
        </>
      )}
    </p>
  );
}

/**
 * One article's citations (R65), from the one generator: the usual form, the usual form with the dated permalink, and the
 * evidence form, line by line, each marked in the language of the text it cites, under the page's labels. Nothing when the
 * text's language is one no citation is composed in; the usual form says why it is not composed when it is not.
 */
export function Citations({ view, version, article, observedAt }) {
  // The date Lex was consulted is the answer's own: an outcome that carries none is cited by its permalink alone.
  if (typeof observedAt !== 'string') return null;
  const forms = citationForms({ view, version, article, observedAt });
  if (forms === null) return null;
  const lang = citationLang(forms.language);
  return (
    <section data-citation={forms.language}>
      <h3>{COPY.cite}</h3>
      <dl>
        <dt>{COPY.citeConventional}</dt>
        {forms.conventional === null
          ? <dd data-citation-form="conventional" data-not-composed="">{COPY.citeNotComposed}</dd>
          : <dd data-citation-form="conventional" lang={lang}>{forms.conventional}</dd>}
        <dt>{COPY.citeWithPermalink}</dt>
        <dd data-citation-form="permalink" lang={lang}>{forms.withPermalink}</dd>
        <dt>{COPY.citeEvidence}</dt>
        {/* The permalink and text digest the form carries, which a check of it asks `verify` to confirm (the journeys). */}
        <dd data-citation-form="evidence" lang={lang} data-permalink={forms.permalink} data-text-sha256={forms.textSha256 ?? ''}>
          {forms.evidenceLines.map((line, index) => (
            <span key={index}>
              {index === 0 ? null : <br />}
              {line}
            </span>
          ))}
        </dd>
      </dl>
    </section>
  );
}

/** The cited article, first: its title, the version it is quoted from, its text and evidence, its citations, and the whole act. */
function CitedArticle({ view, version, pin, cited, observedAt }) {
  const europe = view.publisher === 'eu-eurlex';
  const heading = europe ? cited.quoted?.heading ?? '' : '';
  return (
    <section className="cited-article" data-cited-article={cited.publisherId}>
      <h2>
        {cited.publisherId}
        {heading.length > 0 ? (
          <>
            {' '}
            <span lang={quotationLanguageTag(version.language)}>{heading}</span>
          </>
        ) : null}
      </h2>
      {/* An article held without text is named, never said to be quoted (the French review of 2026-10-16). */}
      {cited.quoted === null ? (
        <p data-cited-from="" data-without-text="">
          <Say template={COPY.citedWithoutText} values={{ source: <VersionHeading view={view} version={version} /> }} />
        </p>
      ) : (
        <>
          <p data-cited-from="">
            <Say template={COPY.citedFrom} values={{ source: <VersionHeading view={view} version={version} /> }} />
          </p>
          <ArticleText article={cited.quoted} language={version.language} applicabilityDate={europe ? null : version.applicabilityDate} />
          <QuoteEvidence article={cited.quoted} />
        </>
      )}
      <Citations view={view} version={version} article={cited.quoted ?? { publisherId: cited.publisherId }} observedAt={observedAt} />
      <p data-whole-act="">
        {/* At a readable address, the act the answer names, never the request's (review of #972). */}
        <a href={permalinkHref(pin.kind === 'state' ? version.permalink : currentAddress({ publisher: view.publisher, work: europe ? view.celex : view.workKey, language: version.language }), CHROME_LOCALE)}>
          {europe ? COPY.europeWholeAct : COPY.wholeAct}
        </a>
      </p>
    </section>
  );
}

/**
 * What a reading at a law's address shows before the reading (`pin`, `readingPinOf`): for a permalink refused because its
 * digest is not the version held at its date, the notice with the current version's link; for a reading, what changed
 * since the version shown, then the cited article. Null when there is nothing to say first. The page brings it into view
 * and gives it the focus (`LiveReading.jsx`), so a reader meets the notice before the text it is about.
 */
export function PinnedLead({ outcome, pin }) {
  if (outcome.state === 'refusal' && outcome.code === 'anchor_not_in_version') {
    // The version is held; the article is not: the whole act, at the version the address pins or at today's (R64, R67).
    const europe = outcome.context?.publisher === 'eu-eurlex';
    const act = pin.kind === 'state' ? pin.permalink : pin.act;
    return (
      <p data-pinned-lead="" data-article-not-held="">
        <a href={permalinkHref(act, CHROME_LOCALE)}>{europe ? COPY.europeWholeAct : COPY.wholeAct}</a>
      </p>
    );
  }
  if (outcome.state === 'refusal') {
    const current = outcome.code === 'pinned_digest_mismatch' && pin.kind === 'state' ? outcome.payload?.current_hash_pinned_url : undefined;
    if (typeof current !== 'string') return null;
    return (
      <p data-pinned-lead="" data-pin-not-held="">
        <Say
          template={outcome.context?.publisher === 'eu-eurlex' ? COPY.europePinNotHeld : COPY.pinNotHeld}
          values={{ pinned: <code>{pin.permalink}</code>, current: <Permalink value={current} /> }}
        />
      </p>
    );
  }
  if (outcome.state !== 'success') return null;
  const version = versionShown(outcome.view, pin);
  if (version === undefined) return null;
  const cited = citedArticle(version, pin);
  const changed = <ChangedSince view={outcome.view} version={version} pin={pin} cited={cited} />;
  if (cited === null && (outcome.view.publisher === 'eu-eurlex' ? typeof version.nextDate !== 'string' : version.nextApplicabilityDate === null)) return null;
  return (
    <div data-pinned-lead="">
      {changed}
      {cited === null ? null : <CitedArticle view={outcome.view} version={version} pin={pin} cited={cited} observedAt={outcome.context?.freshness?.observed_at} />}
    </div>
  );
}
