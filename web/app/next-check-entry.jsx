// The side build's pieces rendered to strings, for `scripts/next-check.mjs` and `test/next.test.mjs` (no browser): the dock
// drawn from one answer of the new engine, the three counted pieces drawn from their answers, and the references panel.
// Bundled for Node by `scripts/react-build.mjs`; nothing here is shipped to a reader.

import { renderToStaticMarkup } from 'react-dom/server';
import { NextDockView, OPENING_PAGES, runQuestion, runReplay, typedDay } from './NextDock.jsx';
import { ContractBlock } from './NextContract.jsx';

/** The dock's one request, its replay by token and its reading of a typed day, as they are (`fetchImpl` is the test's). */
export { OPENING_PAGES, runQuestion, runReplay, typedDay };

/** One block of an answer in the contract, its quotation widened to `level` from the `context` the server answered. */
export function renderContractBlock(block, { locale, state = 'answer', level = 'lines', context = null } = {}) {
  return renderToStaticMarkup(<ContractBlock block={block} state={state} locale={locale} onOpen={() => {}} initialLevel={level} initialContext={context} />);
}
import { CorpusView, HowView, ResultsView } from './NextPages.jsx';
import { DecisionsView, HowForPipeline } from './NextHow.jsx';
import { CoverageView } from './NextCoverage.jsx';
import { ArticleReferences, TextLinks, TextOutline, linksAddress, outlineAddress, referencesAddress } from './TextPage.jsx';

/** A text's table of contents as the text page draws it (in the language the bundle was built for), and the address asked. */
export const renderOutline = (outline, { current = null, order = [], identifier = '/lu-legilux/loi-1997-07-27-n10' } = {}) => renderToStaticMarkup(<TextOutline outline={outline} current={current} order={order} identifier={identifier} />);
export { outlineAddress };

/** The dock showing one answer (`result`) to `question`, in `locale`, on `page`. */
export function renderDockAnswer(result, { locale, page = 'live-home', today = null, day = null, dateOpen = false } = {}) {
  const shown = { id: 'check', kind: 'done', question: result?.question ?? '', day, result };
  return renderToStaticMarkup(<NextDockView locale={locale} page={page} shown={shown} today={today} initialDateOpen={dateOpen} />);
}

/** The dock in a state that is not an answer: the first screen, loading, a failure, too many questions, folded, kept. */
export function renderDockState(state, { locale, page = 'live-home' } = {}) {
  const turn = { id: 'check', kind: state, question: 'Question' };
  if (state === 'first') return renderToStaticMarkup(<NextDockView locale={locale} page={page} />);
  if (state === 'folded') return renderToStaticMarkup(<NextDockView locale={locale} page={page} collapsed />);
  if (state === 'kept') return renderToStaticMarkup(<NextDockView locale={locale} page="live-text" kept={{ ...turn, kind: 'done' }} />);
  return renderToStaticMarkup(<NextDockView locale={locale} page={page} shown={turn} />);
}

export const renderHow = (corpus, { locale, state = 'ready' } = {}) => renderToStaticMarkup(<HowView corpus={corpus} state={state} locale={locale} />);
export const renderResults = (results, { locale, state = 'ready', config = null } = {}) => renderToStaticMarkup(<ResultsView results={results} state={state} locale={locale} config={config} />);

/** pages2: a text as the engine holds it, read into the text page's state (`/api/next/text`), and its source's sentence. */
export { nextTextAddress, nextTextState, textSourceLine } from './TextPage.jsx';
export const renderCorpus = (corpus, { locale, state = 'ready' } = {}) => renderToStaticMarkup(<CorpusView corpus={corpus} state={state} locale={locale} />);
export const renderHowFunnel = (corpus, config, { locale, state = 'ready', configState = 'ready', dictionary = null, shapes = null } = {}) => renderToStaticMarkup(<HowForPipeline corpus={corpus} config={config} state={state} configState={configState} locale={locale} dictionary={dictionary} shapes={shapes} />);
export const renderDecisions = ({ locale, pipeline = 'funnel', state = 'ready' } = {}) => renderToStaticMarkup(<DecisionsView pipeline={pipeline} state={state} locale={locale} />);
export const renderCoverage = (coverage, { locale } = {}) => renderToStaticMarkup(<CoverageView coverage={coverage} locale={locale} />);

/** repair-pages (22/11/2026): the pieces added tonight, for the tests. */
export { whyWords, coverageView } from './NextCoverage.jsx';
export { prose } from './NextResults.jsx';
import { FreshnessStrip, engineIndexFacts } from './Freshness.jsx';
export { engineIndexFacts };
export const renderFreshness = (facts, engine, answers = true) => renderToStaticMarkup(<FreshnessStrip facts={facts} engine={engine} answers={answers} />);

/** The references panel of an open article (in the language the bundle was built for). */
export const renderReferences = (references, identifier) => renderToStaticMarkup(<ArticleReferences references={references} identifier={identifier} />);

/** The folded section of a text's links with other texts (in the language the bundle was built for), and the addresses asked. */
export const renderLinks = (links) => renderToStaticMarkup(<TextLinks links={links} />);
export { linksAddress, referencesAddress };

/** dossier-page (22/11/2026): the architecture dossier drawn from live reads (each address's `{ state, data }`) and their time. */
import { ArchitectureView, LIVE_ADDRESSES, exampleOf, funnelLevels, liveState, loadingLive, readAt } from './NextArchitecture.jsx';
export { LIVE_ADDRESSES, exampleOf, funnelLevels, liveState, loadingLive, readAt };
export const renderArchitecture = (live, { locale, at = null } = {}) => renderToStaticMarkup(<ArchitectureView live={live} at={at} locale={locale} />);
