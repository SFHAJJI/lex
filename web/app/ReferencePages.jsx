// The three reference pages, live: Get help, How to read Lex with its glossary, and Developers (the specification of record's
// launch set; R42, R46, R48, R50, R55; `scripts/reference-pages.mjs`).
//
// Built into `dist-live/{language}/{file}` by `scripts/build-live.mjs`, once per interface language, and served by both
// servers as the files they are, as the legal pages are. Each says its chrome section's words (`live-chrome.mjs`), filled
// with the few values its placeholders name: the footer's statements, links to the site's other pages and to the
// publishers' sites, and other entries of the chrome table said as they stand where the site prints them. A page asks
// nothing, ships no script and is never indexed (`indexing` stays `never`); its footer's freshness strip says it carries no
// answer (`answers` false). One interface serves everyone (R47), so each is in the shell every live page is in.
//
// The Developers page is laid out from the contracts it is handed (`developersModel`, `developers.mjs`): every operation of
// the registry, served or not, with its REST path, its MCP tool, its parameters as its request schema states them, its
// answer's objects and its two schemas, linked where this site publishes them, with their digests; the envelope's members;
// the verdicts; the refusal codes with the fields each payload must carry and its payload's schema; what Lex never holds;
// the failures below the envelope; citing, the limits, the events log, the replay guarantees and how the API changes. It
// describes nothing the contracts do not hold, and throws without them. Both servers render it from their own answer to
// `browse` (`ssr-entry.jsx`), which it shows as its live examples (`live`); as built it says it carries none.

import { Fragment } from 'react';

import { Document, SITE_ORIGIN } from './Document.jsx';
import { Say, inEnglish } from './LiveAnswer.jsx';
import { countedEntry, fillText, liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { renderDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { OFFICIAL_SITES } from '../scripts/live-failure.mjs';
import { legalPage } from '../scripts/legal-pages.mjs';
import { REFERENCE_PAGES, glossaryRows, referencePage } from '../scripts/reference-pages.mjs';
import {
  CLIENT_LIMIT, KEY_SET_PATH, LIVE_EXAMPLE_REQUEST, MAXIMUM_REQUEST_BYTES, MCP_ENDPOINT, MCP_INSTRUCTIONS, MCP_METHODS, MCP_PROTOCOL_VERSION,
  PRESENTATION_RESULTS, PREVIOUS_REGISTRY_SCHEMA, REGISTRY_VERSION_SINCE, REPLAY_GUARANTEES, REPLAY_TESTS, RETENTION_NIGHTLY_DAYS,
  REST_PREFIX, TRANSPORT_PROBLEMS, TRANSPORT_PROBLEM_SCHEMA, liveAnswerText, liveExamples, liveNextExamples, liveRestExample, mcpClientExample,
  mcpExample, parameterSchemaText, restExample,
} from '../scripts/developers.mjs';
import { longDate } from '../scripts/live-search.mjs';
import { CHROME_LOCALE, ENGLISH_LANG } from '../scripts/live-chrome.mjs';
import { EVALUATION_RECORD } from '../scripts/evaluation-record.mjs';
import { COVERAGE_RECORD } from '../scripts/coverage-record.mjs';
import { UNSEEN_RECORD } from '../scripts/unseen-record.mjs';
import { REGISTRY_RECORD } from '../scripts/registry-record.mjs';
import { OPENAPI_FILE, OPENAPI_ORIGIN } from '../scripts/openapi.mjs';
import { plannerName } from '../scripts/answer-view.mjs';
import { HOW_ANSWER_RECORD } from '../scripts/how-answer-record.mjs';
import { howAnswerStages } from '../scripts/how-answer.mjs';
import { textHref } from '../scripts/text-address.mjs';
import { NEXT_HOW_ROOT, NEXT_SCRIPT } from './NextPages.jsx';
import { nextCopy } from '../scripts/next-copy.mjs';
import { NEXT_DECISIONS_ROOT, NextDecisions, NextHowFunnel } from './NextHow.jsx';

/** A link to another page of the site, in this bundle's language, with the words given. */
const pageLink = (file, text) => <a href={livePath(file)}>{text}</a>;

/** A publisher's official site, by its key in `OFFICIAL_SITES`, linked by its host name. */
function officialSite(key) {
  const site = OFFICIAL_SITES.find((one) => one.key === key);
  return <a href={site.uri} rel="external" data-publisher={site.publisher}>{new URL(site.uri).host}</a>;
}

/** Inline code. */
const code = (text) => <code>{text}</code>;

/** Values said one after another, each as code, with the table's separator for codes between them. */
function codeList(values) {
  const separator = liveChrome().developers.codeSeparator;
  return values.map((value, index) => <span key={value}>{index > 0 ? separator : null}<code>{value}</code></span>);
}

/** A sentence of a page, filled with the values its own placeholders name, and no other. */
function Sentence({ template, values }) {
  const named = Object.fromEntries([...template.matchAll(/\{([A-Za-z]+)\}/g)].map(([, name]) => {
    if (!Object.hasOwn(values, name)) throw new Error(`a reference page's sentence names {${name}}, which the page does not fill: ${JSON.stringify(template)}`);
    return [name, values[name]];
  }));
  return <Say template={template} values={named} />;
}

/** A page's parts, in its table's order: each under its heading, its paragraphs, a `list` as one list. */
function Sections({ pageKey, sections, values }) {
  return Object.entries(sections).map(([name, section]) => (
    <section key={name} className="reference-section" aria-labelledby={`${pageKey}-${name}`} data-reference-section={name}>
      <h2 id={`${pageKey}-${name}`}>{section.heading}</h2>
      {Object.entries(section).filter(([key]) => key !== 'heading').map(([key, value]) => (typeof value === 'string'
        ? <p key={key}><Sentence template={value} values={values} /></p>
        : (
          <ul key={key}>
            {Object.entries(value).map(([item, text]) => <li key={item}><Sentence template={text} values={values} /></li>)}
          </ul>
        )))}
    </section>
  ));
}

/** A long page's contents: a link to each of its parts, by its heading. */
function Contents({ entries }) {
  const copy = liveChrome().reference;
  return (
    <nav aria-label={copy.contents} className="reference-contents" data-reference-contents="">
      <ul>
        {entries.map(([id, heading]) => <li key={id}><a href={`#${id}`}>{heading}</a></li>)}
      </ul>
    </nav>
  );
}

/** The values Get help's sentences may name. */
export function helpValues() {
  const copy = liveChrome();
  return {
    notAdvice: copy.site.notAdvice,
    authenticity: copy.site.authenticity,
    failureLine: copy.failure.line,
    developersPage: pageLink(referencePage('developers').file, copy.reference.inline.developers),
    guidePage: pageLink(referencePage('guide').file, copy.reference.inline.guide),
    privacyPage: pageLink(legalPage('privacy').file, copy.legal.inline.privacy),
    legiluxSite: officialSite('legilux'),
    eurlexSite: officialSite('eurlex'),
  };
}

/** The values How to read Lex's sentences may name: the site's own words, as the pages that print them say them. */
export function guideValues() {
  const copy = liveChrome();
  return {
    authenticity: copy.site.authenticity,
    noDateMark: fillText(copy.dossier.entryIntoForceDate, { date: '…' }),
    asPublishedLabel: copy.common.asPublished.label,
    withoutText: copy.export.withoutTextHeading,
    freshnessHeading: copy.freshness.heading,
    staleHeading: copy.freshness.staleHeading,
    cite: copy.reading.cite,
    conventional: copy.reading.citeConventional,
    withPermalink: copy.reading.citeWithPermalink,
    evidence: copy.reading.citeEvidence,
    skip: copy.site.skip,
  };
}

/** A number as this page's language writes it: 1.92 is "1.92" in English, "1,92" in French. */
const decimal = (value) => value.toLocaleString(CHROME_LOCALE === 'fr' ? 'fr-FR' : 'en-GB', { minimumFractionDigits: 1, maximumFractionDigits: 2 });

/** A count as this page's language writes it: 1 234 in French, 1,234 in English. */
const count = (value) => value.toLocaleString(CHROME_LOCALE === 'fr' ? 'fr-FR' : 'en-GB');

/** A number of seconds as this page's language writes it, to one decimal: 1.52 is "1,5" in French. */
const decimal1 = (value) => value.toLocaleString(CHROME_LOCALE === 'fr' ? 'fr-FR' : 'en-GB', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** A cost as this page's language writes it, to three decimals: 0.001 is "0,001" in French. */
const decimal3 = (value) => value.toLocaleString(CHROME_LOCALE === 'fr' ? 'fr-FR' : 'en-GB', { minimumFractionDigits: 3, maximumFractionDigits: 3 });

/**
 * The figures the Decisions and Evidence pages print, each a test run's (`evaluation-record.mjs`) or the coverage answer's
 * (`coverage-record.mjs`) own; a sum is of a run's own counts (the questions that call for an article, those left without one).
 */
export function measured() {
  const dev = EVALUATION_RECORD.dev;
  const heldout = EVALUATION_RECORD.heldout;
  return {
    cases: count(dev.cases),
    right: count(dev.rightArticle),
    wrong: count(dev.wrongArticle),
    notFound: count(dev.notFound),
    wronglyRefused: count(dev.wronglyRefused),
    rightBehaviour: count(dev.rightBehaviour),
    expecting: count(dev.rightArticle + dev.wrongArticle + dev.notFound + dev.wronglyRefused),
    unanswered: count(dev.notFound + dev.wronglyRefused),
    heldoutRight: count(heldout.rightArticle),
    heldoutWrong: count(heldout.wrongArticle),
    heldoutCases: count(heldout.cases),
    heldoutDate: numericDate(heldout.date),
    devDate: numericDate(dev.date),
    cost: decimal3(dev.costEur),
    typical: decimal1(dev.p50),
    slow: decimal1(dev.p95),
    model: dev.modelDeployment,
    acts: count(COVERAGE_RECORD.works),
    versions: count(COVERAGE_RECORD.states),
    articles: count(COVERAGE_RECORD.articles),
    catalogued: count(COVERAGE_RECORD.versionsCatalogued),
    withText: count(COVERAGE_RECORD.versionsWithText),
    withoutText: count(COVERAGE_RECORD.versionsCatalogued - COVERAGE_RECORD.versionsWithText),
    worksWithText: count(COVERAGE_RECORD.worksWithText),
    share: Math.floor((100 * COVERAGE_RECORD.versionsWithText) / COVERAGE_RECORD.versionsCatalogued + 0.5),
    files: count(COVERAGE_RECORD.files),
    withheld: count(COVERAGE_RECORD.filesWithheld),
    builtOn: numericDate(COVERAGE_RECORD.builtOn),
    // What the assistant reads, of what Lex holds, of what the publisher's catalogue lists (`coverage-record.mjs`).
    assistantActs: count(COVERAGE_RECORD.assistantActs),
    luActs: count(COVERAGE_RECORD.works),
    actsCatalogued: COVERAGE_RECORD.actsCatalogued === null ? '' : count(COVERAGE_RECORD.actsCatalogued),
    // The sealed set of unseen questions, asked once (`unseen-record.mjs`); every figure is that run's own count.
    unseenCases: count(UNSEEN_RECORD.cases),
    unseenWrong: count(UNSEEN_RECORD.otherLaws.wrong + UNSEEN_RECORD.insurance.wrong),
    otherCases: count(UNSEEN_RECORD.otherLaws.cases),
    otherWith: count(UNSEEN_RECORD.otherLaws.candidatesWith + UNSEEN_RECORD.otherLaws.right),
    otherWithout: count(UNSEEN_RECORD.otherLaws.candidatesWithout),
    otherNone: count(UNSEEN_RECORD.otherLaws.notFound + UNSEEN_RECORD.otherLaws.refused),
    insuranceCases: count(UNSEEN_RECORD.insurance.cases),
    insuranceRight: count(UNSEEN_RECORD.insurance.right),
    insuranceWith: count(UNSEEN_RECORD.insurance.candidatesWith),
    noArticleCases: count(UNSEEN_RECORD.noArticle.cases),
    noArticleExpected: count(UNSEEN_RECORD.noArticle.asExpected),
  };
}

/** A template filled with the figures it names, of all `values` (`fillText` takes no value a template has no place for). */
const say = (template, values) => fillText(template, Object.fromEntries([...template.matchAll(/\{([A-Za-z]+)\}/g)].map(([, name]) => [name, values[name]])));

/** A calendar date as the V4 pages write it in both languages, dd/mm/yyyy (the brief's section 9): 2026-11-15 is 15/11/2026. */
const numericDate = (iso) => (typeof iso === 'string' && /^\d{4}-\d{2}-\d{2}/u.test(iso) ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}` : iso);

/** The groups of decision cards, each a key and its cards' keys, in the page's order (the owner's order of 2026-11-14). */
export const DECISION_GROUPS = Object.freeze([
  Object.freeze({ key: 'answering', cards: Object.freeze(['codeWrites', 'notFound']) }),
  Object.freeze({ key: 'search', cards: Object.freeze(['words', 'oneCall']) }),
  Object.freeze({ key: 'data', cards: Object.freeze(['dated', 'licence']) }),
  Object.freeze({ key: 'built', cards: Object.freeze(['agent']) }),
]);

/**
 * Decisions (V4, V5How's typography): the decisions in four groups, each a row of its title, what it gains and what it costs
 * side by side, and where there is one, a grey line naming the incident or the open problem behind it. Each decision has its
 * own anchor (`decision-{key}`), which How it works links. Every figure is measured.
 */
export function DecisionsBody() {
  const page = liveChrome().decisions;
  const values = measured();
  return (
    <div className="lx-dec">
      <h1>{page.heading}</h1>
      {DECISION_GROUPS.map((group) => (
        <section className="lx-dec-group" aria-labelledby={`decisions-${group.key}`} data-reference-section={group.key} key={group.key}>
          <h2 id={`decisions-${group.key}`}>{page.groups[group.key]}</h2>
          <ul className="lx-dec-list">
            {group.cards.map((key) => {
              const card = page.cards[key];
              return (
                <li className="lx-dec-item" id={`decision-${key}`} data-decision={key} key={key}>
                  <h3>{card.title}</h3>
                  <p className="lx-dec-gain"><strong>{page.gain}</strong> {say(card.gain, values)}</p>
                  <p className="lx-dec-cost"><strong>{page.cost}</strong> {say(card.cost, values)}</p>
                  {card.note === undefined ? null : <p className="lx-dec-note">{say(card.note, values)}</p>}
                </li>
              );
            })}
          </ul>
        </section>
      ))}
    </div>
  );
}

/** A row of steps (a flow): each a step's words, the one the flow turns on marked (`accent`: the model, the owner). */
function Flow({ name, steps, accent }) {
  return (
    <ol className="lx-how-steps" data-flow={name}>
      {Object.entries(steps).map(([key, text]) => (
        <li key={key} className="lx-how-step" data-accent={key === accent ? '' : undefined} data-step={key}>{text}</li>
      ))}
    </ol>
  );
}

/** A share as a whole percentage, rounded half up: 0.946 is 95. */
const percentOf = (rate) => Math.floor(100 * rate + 0.5);

/** The public addresses the page links to: the code, the harness, the monitor's and the nightly check's runs. */
export const PUBLIC_LINKS = Object.freeze({
  code: 'https://github.com/SFHAJJI/lex',
  harness: 'https://github.com/SFHAJJI/lex-eval',
  registryCode: 'https://github.com/SFHAJJI/lex-eval/blob/main/src/lex_eval/run.py',
  monitor: 'https://github.com/SFHAJJI/lex/actions/workflows/v3-monitor.yml',
  nightly: 'https://github.com/SFHAJJI/lex-eval/actions/workflows/nightly.yml',
});

/** The registry's figures the page prints (`registry-record.mjs`, MLflow's own export). */
function registryValues() {
  const versions = REGISTRY_RECORD.versions;
  const champion = REGISTRY_RECORD.aliases.champion ?? null;
  const accuracyOf = (number) => versions.find((version) => version.version === number)?.metrics?.verdict_accuracy ?? null;
  return {
    registryModel: REGISTRY_RECORD.model,
    mlflowVersion: REGISTRY_RECORD.mlflow_version,
    registryVersions: count(versions.length),
    alias: 'champion',
    champion: champion === null ? '–' : String(champion),
    runs: count(REGISTRY_RECORD.runs_logged),
    firstPercent: accuracyOf(1) === null ? '–' : String(percentOf(accuracyOf(1))),
    fifthPercent: accuracyOf(5) === null ? '–' : String(percentOf(accuracyOf(5))),
  };
}

/** The model registry, as MLflow holds it: one row a version, the champion marked. */
function RegistryTable({ page }) {
  const columns = ['version', 'registered', 'planner', 'code', 'data', 'result', 'gates'];
  return (
    <div className="lx-how-table-scroll">
      <table className="lx-how-table" data-registry-versions={REGISTRY_RECORD.versions.length}>
        <thead><tr>{columns.map((column) => <th key={column} scope="col">{page.registryColumns[column]}</th>)}</tr></thead>
        <tbody>
          {REGISTRY_RECORD.versions.map((version) => {
            const champion = version.aliases.includes('champion');
            const metrics = version.metrics;
            const result = typeof metrics.outcome_right_article === 'number'
              ? say(page.registryArticles, { right: count(metrics.outcome_right_article), wrong: count(metrics.outcome_wrong_article ?? 0), cases: count(metrics.cases) })
              : say(page.registryVerdicts, { percent: String(percentOf(metrics.verdict_accuracy ?? 0)), cases: count(metrics.cases ?? 0) });
            return (
              <tr key={version.version} data-registry-version={version.version} className={champion ? 'registry-champion' : undefined}>
                <th scope="row">{champion ? `${version.version} · champion` : version.version}</th>
                <td>{numericDate(version.created)}</td>
                <td>{plannerName(version.tags.planner_id)}</td>
                <td><code>{String(version.tags.serving_commit ?? '').slice(0, 8)}</code></td>
                <td><code>{String(version.tags.snapshot_id ?? '').replace(/^corpus-/u, '').slice(0, 8)}</code></td>
                <td>{result}</td>
                <td>{page.registryGates[version.tags.gates] ?? version.tags.gates}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

/** One titled block of the page under the first screen, its heading's id derived from its key; `wide` spans both columns. */
function Block({ id, heading, wide = false, children }) {
  return (
    <section className={wide ? 'lx-how-block lx-how-wide' : 'lx-how-block'} aria-labelledby={`how-${id}`} data-reference-section={id} id={id}>
      <h2 id={`how-${id}`}>{heading}</h2>
      {children}
    </section>
  );
}

/** The parts of How it works, in its order (the reference pages' test reads them): the first screen's three, then Release 13's. */
export const HOW_IT_WORKS_PARTS = Object.freeze([
  'answer', 'measured', 'decisions',
  'card', 'architecture', 'registry', 'api', 'supervision', 'improvement', 'quality', 'coverage', 'incidents', 'safeguards', 'limits', 'built',
]);

/** The outcomes the bar and its legend count, in their order, each with the test run's own count's name. */
const OUTCOMES = Object.freeze([
  ['right', 'rightArticle'], ['wrong', 'wrongArticle'], ['notFound', 'notFound'], ['wronglyRefused', 'wronglyRefused'], ['rightBehaviour', 'rightBehaviour'],
]);

/** The bar's width in the panel, in CSS pixels (the panel's 400 less its padding), and its height. */
const BAR_WIDTH = 324;
const BAR_HEIGHT = 30;

/**
 * The test questions as one bar, a segment per outcome in proportion to its count (a count of 0 draws none), each segment
 * with its count when it is wide enough to hold it. Drawn: the legend under it says the same counts in words.
 */
function MeasuredBar({ run }) {
  const total = OUTCOMES.reduce((sum, [, field]) => sum + run[field], 0);
  let x = 0;
  const segments = OUTCOMES.filter(([, field]) => run[field] > 0).map(([key, field]) => {
    const width = total === 0 ? 0 : (BAR_WIDTH * run[field]) / total;
    const segment = { key, value: run[field], x, width };
    x += width;
    return segment;
  });
  const round = (value) => Math.round(value * 10) / 10;
  return (
    <svg className="lx-how-bar" viewBox={`0 0 ${BAR_WIDTH} ${BAR_HEIGHT}`} width={BAR_WIDTH} height={BAR_HEIGHT} aria-hidden="true" focusable="false" data-bar="">
      <defs><clipPath id="lx-how-bar-clip"><rect width={BAR_WIDTH} height={BAR_HEIGHT} rx="6" /></clipPath></defs>
      <g clipPath="url(#lx-how-bar-clip)">
        {segments.map((segment) => <rect key={segment.key} className="lx-how-bar-part" data-outcome={segment.key} x={round(segment.x)} y="0" width={round(segment.width)} height={BAR_HEIGHT} />)}
      </g>
      {segments.filter((segment) => segment.width >= 18).map((segment) => (
        <text key={segment.key} className="lx-how-bar-count" data-outcome={segment.key} x={round(segment.x + segment.width / 2)} y={BAR_HEIGHT / 2} textAnchor="middle" dominantBaseline="central">{count(segment.value)}</text>
      ))}
    </svg>
  );
}

/** One stage of the answer: its number, its name, its tag (code, or the model's only call), its sentence and its record. */
function Stage({ index, name, model, sentence, children, stage }) {
  const page = liveChrome().evidence;
  return (
    <li className="lx-how-stage" data-stage={stage}>
      <span className="lx-how-n" aria-hidden="true">{index}</span>
      <div className="lx-how-name">
        <h2>{name}</h2>
        <span className={model ? 'lx-how-tag lx-how-tag-model' : 'lx-how-tag'}>{model ? page.tagModel : page.tagCode}</span>
      </div>
      <div className="lx-how-body">
        <p>{sentence}</p>
        <pre className="lx-how-record"><code>{children}</code></pre>
      </div>
    </li>
  );
}

/**
 * How it works, first screen (V5How): on the left the recorded answer (`how-answer-record.mjs`) through its stages, each
 * value read from the answer, and the answer itself with the link that opens it on the text page; on the right the test
 * questions' outcomes, the held-out result, the time and cost per answer, and the decisions with what each costs.
 */
function FirstScreen({ values }) {
  const copy = liveChrome();
  const page = copy.evidence;
  const decisions = copy.decisions;
  const stages = howAnswerStages(HOW_ANSWER_RECORD, CHROME_LOCALE);
  const run = EVALUATION_RECORD.dev;
  const asked = stages.language === 'fr' || stages.language === 'en' || stages.language === 'de' ? stages.language : undefined;
  const words = (text) => <span lang={asked}>{text}</span>;
  // What each stage's record shows under its own line: what the code read of the question, and the gates' counts.
  const read = [
    say(page.parseLanguage, { language: page.languages[stages.language] ?? stages.language }),
    stages.date.today ? say(page.parseToday, { date: numericDate(stages.date.date) }) : say(page.parseDate, { date: numericDate(stages.date.date) }),
    stages.cues === 0 ? page.parseNoCues : say(stages.ruleQuestion ? page.parseCuesRule : page.parseCues, { count: count(stages.cues) }),
  ];
  const checked = [
    say(page.composeQuotes, { count: count(stages.gates.quotes) }),
    say(page.composeOutside, { count: count(stages.gates.outside) }),
    say(page.composeNotes, { count: count(stages.gates.notes) }),
  ];
  const recordOf = (row) => (row.key === 'masking' ? (stages.masked === null ? page.maskNone : <Say template={page.maskDone} values={{ question: words(stages.masked) }} />)
    : row.key === 'question' ? [row.line, ...read].join('\n')
      : row.key === 'checks' || row.key === 'composer' ? checked.join('\n')
        : row.line);
  const sentenceOf = (row) => (row.key === 'masking' ? page.maskText
    : row.model ? `${page.stageTexts[row.key] ?? ''} ${say(page.modelLine, { model: stages.model })}`.trim()
      : page.stageTexts[row.key] ?? '');
  const answer = stages.answer;
  const open = answer === null ? null : textHref({ identifier: answer.identifier, article: answer.anchor }, CHROME_LOCALE);
  const statute = stages.act.language === 'fra' ? 'fr' : undefined;
  const answerSentence = answer === null ? null
    : answer.kind === 'rule' ? <Say template={page.answerRule} values={{ article: answer.article, cases: count(answer.cases), title: <span lang={statute}>{stages.act.title}</span> }} />
      : answer.kind === 'operations' ? say(answer.shortened ? page.answerPart : page.answerFull, { article: answer.article })
        : say(page.answerLines, { count: count(answer.lines) });
  return (
    <div className="lx-how-first">
      <div className="lx-how-flow" data-reference-section="answer">
        <h1>{page.heading}</h1>
        <p className="lx-how-intro">{page.answerIntro}</p>
        <p className="lx-how-bubble" lang={asked}>{stages.question}</p>
        <ol className="lx-how-stages" data-recorded={HOW_ANSWER_RECORD.recordedAt}>
          {stages.rows.map((row, at) => (
            <Stage key={row.key} index={String(at + 1)} stage={row.key} name={row.name} model={row.model} sentence={sentenceOf(row)}>{recordOf(row)}</Stage>
          ))}
        </ol>
        {answer === null ? null : (
          <div className="lx-how-answer" data-answer="">
            <span className="lx-how-n" aria-hidden="true" />
            <p className="lx-how-answer-label">{page.answerLabel}</p>
            <p className="lx-how-body">
              {answerSentence}
              {' '}
              {open === null ? null : <a href={open} data-open-answer="">{page.answerOpen}</a>}
            </p>
          </div>
        )}
        <p className="lx-how-intro" data-other-laws="">{page.otherLaws}</p>
      </div>
      <aside className="lx-how-panel" aria-labelledby="how-measured">
        <section data-reference-section="measured">
          <h2 id="how-measured">{say(page.measuredHeading, values)}</h2>
          <p className="lx-how-when">{say(page.measuredWhen, { date: numericDate(run.date) })}</p>
          <MeasuredBar run={run} />
          <ul className="lx-how-legend">
            {OUTCOMES.map(([key, field]) => <li key={key} data-count={key}><strong>{count(run[field])}</strong><span>{page.counts[key]}</span></li>)}
          </ul>
          <p className="lx-how-heldout" data-heldout="">{page.gateRule} {say(page.heldout, values)}</p>
          <p className="lx-how-cost">{say(page.perAnswer, values)}</p>
        </section>
        <section data-reference-section="decisions" aria-labelledby="how-decisions">
          <h2 id="how-decisions">{page.decisionsHeading}</h2>
          <ul className="lx-how-decisions">
            {DECISION_GROUPS.flatMap((group) => group.cards).map((key) => (
              <li key={key}>
                <a href={`${livePath(referencePage('decisions').file)}#decision-${key}`} data-decision-link={key}>
                  <span className="lx-how-decision-title">{decisions.cards[key].title}</span>
                  <span className="lx-how-decision-cost">{decisions.cost} {say(decisions.cards[key].cost, values)}</span>
                </a>
              </li>
            ))}
          </ul>
          <p className="lx-how-all"><a href={livePath(referencePage('decisions').file)}>{page.allDecisions}</a></p>
        </section>
      </aside>
    </div>
  );
}

/**
 * How Lex works (at evidence.html; V5How, the owner's brief of 2026-11-15): the first screen follows one recorded answer
 * through the six stages beside the measured results and the decisions; under it, Release 13's parts in V4's type: the
 * system card, the architecture, the model registry as MLflow holds it (its versions folded), the API, supervision,
 * improvement, the evaluation, what Lex holds, incidents, safeguards, limits and how it is built. Every figure is the recorded
 * answer's, a test run's, the registry's, the coverage answer's or the code's.
 */
export function EvidenceBody() {
  const page = liveChrome().evidence;
  const dev = EVALUATION_RECORD.dev;
  const heldout = EVALUATION_RECORD.heldout;
  const noPlan = heldout.questions.filter((question) => question.verdict === 'assistant_v3_unavailable').length;
  const values = {
    ...measured(),
    ...registryValues(),
    sameVerdict: String(percentOf(dev.replaySameVerdict)),
    samePlan: String(percentOf(dev.replaySamePlan)),
    heldoutRefused: count(heldout.questions.filter((question) => question.outcome === 'wrongly_refused').length),
    heldoutNoPlan: count(noPlan),
    heldoutNotFound: count(heldout.questions.filter((question) => question.outcome === 'not_found').length - noPlan),
    perMinute: count(CLIENT_LIMIT.apiPerMinute),
    inFlight: count(CLIENT_LIMIT.apiInFlight),
    artifacts: [...new Set(REGISTRY_RECORD.versions.flatMap((version) => version.artifacts))].join(', '),
  };
  values.model = dev.modelDeployment;
  // The call the API section shows is the recorded answer's own request, sent where it was sent.
  const question = HOW_ANSWER_RECORD.request.parameters.question;
  const external = (href, text) => <a href={href} rel="external">{text}</a>;
  return (
    <div className="lx-how">
      <FirstScreen values={values} />
      <div className="lx-how-more">
        <Block id="card" heading={page.cardHeading} wide>
          <dl className="lx-how-card">
            {Object.entries(page.card).map(([key, item]) => (
              <div data-card={key} key={key}><dt>{item.title}</dt><dd>{say(item.text, values)}</dd></div>
            ))}
          </dl>
        </Block>
        <Block id="architecture" heading={page.architectureHeading} wide>
          <p className="lx-how-lane">{page.questionLane}</p>
          <Flow name="question" steps={page.questionSteps} accent="rank" />
          <p className="lx-how-lane">{page.releaseLane}</p>
          <Flow name="release" steps={page.releaseSteps} accent="owner" />
          <p className="lx-how-line">{page.hosting}</p>
        </Block>
        <Block id="registry" heading={page.registryHeading} wide>
          <ul className="lx-how-lines">
            <li data-registry-model="">{say(page.registryModel, { ...values, model: values.registryModel, versions: values.registryVersions })}</li>
          </ul>
          <details className="lx-how-fold">
            <summary>{say(page.registryEvery, { versions: values.registryVersions })}</summary>
            <RegistryTable page={page} />
          </details>
          <ul className="lx-how-lines">
            <li>{say(page.registryTags, values)}</li>
            <li>{external(PUBLIC_LINKS.registryCode, page.registryCode)}</li>
          </ul>
        </Block>
        <Block id="api" heading={page.apiHeading}>
          <ul className="lx-how-lines">
            <li>{page.apiDoors}</li>
            <li>{page.apiDocumented} <a href={livePath(referencePage('developers').file)}>{page.apiDevelopers}</a> · <a href={`/${OPENAPI_FILE}`} data-openapi="">{page.apiOpenApi}</a></li>
            <li>{page.apiCall}</li>
          </ul>
          <pre className="lx-how-record lx-how-call"><code>{`curl -X POST ${OPENAPI_ORIGIN}${REST_PREFIX}ask \\\n  -H 'content-type: application/json' \\\n  -d '{"operation_id":"ask","parameters":{"question":"${question.replace(/'/gu, "'\\''")}"}}'`}</code></pre>
        </Block>
        <Block id="supervision" heading={page.supervisionHeading}>
          <ul className="lx-how-lines">
            <li>{page.supAvailability} {external(PUBLIC_LINKS.monitor, page.supAvailabilityLink)}</li>
            <li>{say(page.supNightly, values)} {external(PUBLIC_LINKS.nightly, page.supNightlyLink)}</li>
            <li>{say(page.supLimits, values)}</li>
          </ul>
        </Block>
        <Block id="improvement" heading={page.improvementHeading}>
          <ul className="lx-how-lines">{Object.entries(page.improvement).map(([key, line]) => <li key={key}>{say(line, values)}</li>)}</ul>
        </Block>
        <Block id="quality" heading={say(page.qualityHeading, values)}>
          <ul className="lx-how-lines">
            <li data-stability="">{say(page.stability, values)}</li>
          </ul>
          <details className="lx-how-fold">
            <summary>{say(page.everyQuestion, values)}</summary>
            <table className="lx-how-table lx-how-questions">
              <tbody>
                {dev.questions.map((one) => (
                  <tr key={one.id} data-outcome={one.outcome}>
                    <th scope="row" lang={one.language}>{one.question.replace(/[^\s@]+@[^\s@,]+/gu, page.maskedEmail)}</th>
                    <td>{page.outcomes[one.outcome] ?? one.outcome}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </details>
        </Block>
        <Block id="coverage" heading={page.coverageHeading}>
          <ul className="lx-how-lines">
            <li data-coverage-held="">{say(page.held, values)}</li>
            <li data-coverage-missing="">{say(page.missing, values)}</li>
            {COVERAGE_RECORD.actsCatalogued === null ? null : <li data-coverage-assistant="">{say(page.assistantReads, values)}</li>}
          </ul>
        </Block>
        <Block id="incidents" heading={page.incidentsHeading}>
          <ul className="lx-how-lines">{Object.entries(page.incidents).map(([key, line]) => <li key={key}>{line}</li>)}</ul>
        </Block>
        <Block id="safeguards" heading={page.safeguardsHeading}>
          <ul className="lx-how-lines">{Object.entries(page.safeguards).map(([key, line]) => <li key={key}>{say(line, values)}</li>)}</ul>
        </Block>
        <Block id="limits" heading={page.limitsHeading}>
          <ul className="lx-how-lines">{Object.entries(page.limits).map(([key, line]) => <li key={key}>{say(line, values)}</li>)}</ul>
        </Block>
        <Block id="built" heading={page.builtHeading}>
          <ul className="lx-how-lines">
            <li>{external(PUBLIC_LINKS.code, page.builtCode)} · {external(PUBLIC_LINKS.harness, page.builtEval)}</li>
            <li>{page.builtCi}</li>
            <li>{page.builtRelease}</li>
          </ul>
        </Block>
      </div>
    </div>
  );
}

/** Get help's content. */
export function HelpBody() {
  const page = liveChrome().help;
  return (
    <>
      <p className="eyebrow">{page.eyebrow}</p>
      <h1>{page.heading}</h1>
      <p>{page.intro}</p>
      <Sections pageKey="help" sections={page.sections} values={helpValues()} />
    </>
  );
}

/** The glossary: each term as the site prints it, in this language's order, and what it means. */
export function Glossary() {
  const copy = liveChrome();
  const glossary = copy.guide.glossary;
  return (
    <section className="reference-section" aria-labelledby="guide-glossary" data-reference-section="glossary">
      <h2 id="guide-glossary">{glossary.heading}</h2>
      <p>{glossary.intro}</p>
      <dl className="glossary" data-glossary="">
        {glossaryRows(copy, CHROME_LOCALE).map((row) => (
          <div key={row.key} id={`term-${row.key}`} data-term={row.key}>
            <dt>{row.term}</dt>
            <dd>{row.definition}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

/** How to read Lex's content: its parts, then the glossary. */
export function GuideBody() {
  const page = liveChrome().guide;
  return (
    <>
      <p className="eyebrow">{page.eyebrow}</p>
      <h1>{page.heading}</h1>
      <p>{page.intro}</p>
      <Contents entries={[...Object.entries(page.sections).map(([name, section]) => [`guide-${name}`, section.heading]), ['guide-glossary', page.glossary.heading]]} />
      <Sections pageKey="guide" sections={page.sections} values={guideValues()} />
      <Glossary />
    </>
  );
}

/** A block of code, whole and wrapped, so it never scrolls and never takes a tab stop of its own; the sentence before it says what it is. */
function CodeBlock({ children }) {
  return <pre className="reference-code"><code>{children}</code></pre>;
}

/** A schema's link, by its published path, and its digest. */
function SchemaLink({ schema, text }) {
  const copy = liveChrome().developers.schemas;
  return (
    <>
      <a href={schema.path} data-schema={schema.id}>{text}</a>
      {' ('}
      <Say template={copy.digest} values={{ digest: <code>{schema.sha256}</code> }} />
      {')'}
    </>
  );
}

/** One operation: what it does, whether it is served, and its facts, each from the contracts. */
function Operation({ operation }) {
  const copy = liveChrome().developers.operations;
  const description = copy.descriptions[operation.id];
  if (typeof description !== 'string') throw new Error(`the registry's operation ${operation.id} has no description on the Developers page`);
  return (
    <section className="api-operation" aria-labelledby={`operation-${operation.id}`} data-operation={operation.id} data-served={operation.served ? 'true' : 'false'}>
      <h3 id={`operation-${operation.id}`}><code>{operation.id}</code></h3>
      <p>{description}</p>
      {operation.served ? null : (
        <p data-not-served="" data-refusal-code={operation.refusalCode}>
          <Say template={copy.notServed} values={{ code: code(operation.refusalCode), mode: code(operation.requestedMode), data: inEnglish(operation.dataNeeded) }} />
        </p>
      )}
      <dl className="api-facts">
        <div className="strip-row"><dt>{copy.rest}</dt><dd><code>{`POST ${operation.rest}`}</code></dd></div>
        <div className="strip-row"><dt>{copy.tool}</dt><dd><code>{operation.tool}</code></dd></div>
        <div className="strip-row">
          <dt>{copy.parameters}</dt>
          <dd>
            {operation.parameters.length === 0 ? copy.noParameters : (
              <ul className="api-parameters">
                {operation.parameters.map((parameter) => (
                  <li key={parameter.name} data-parameter={parameter.name} data-required={parameter.required ? 'true' : 'false'}>
                    <code>{parameter.name}</code>{` (${parameter.required ? copy.required : copy.optional}) `}<code>{parameterSchemaText(parameter.schema)}</code>
                  </li>
                ))}
              </ul>
            )}
          </dd>
        </div>
        <div className="strip-row"><dt>{copy.objects}</dt><dd>{codeList(operation.objectTypes)}</dd></div>
        <div className="strip-row">
          <dt>{copy.schemas}</dt>
          <dd>
            <SchemaLink schema={operation.requestSchema} text={copy.requestSchema} />
            {liveChrome().common.listSeparator}
            <SchemaLink schema={operation.resultSchema} text={copy.resultSchema} />
          </dd>
        </div>
      </dl>
    </section>
  );
}

/** The parts of the Developers page, in order. */
export const DEVELOPERS_PARTS = Object.freeze(['access', 'examples', 'operations', 'envelope', 'refusals', 'citing', 'limits', 'events', 'replay', 'changes', 'schemas']);

/** The parts of the Developers page, each with its id and heading, in order. */
function developersParts(copy) {
  return DEVELOPERS_PARTS.map((name) => [`developers-${name}`, copy[name].heading]);
}

/**
 * The live examples: as the server rendered the page, the request it asked itself and its whole answer, then the next requests
 * about the first act it holds; as built, the sentence that says the page carries none.
 */
function LiveExamples({ live }) {
  const copy = liveChrome().developers.examples;
  const request = code(JSON.stringify(LIVE_EXAMPLE_REQUEST));
  if (live === null) return <p data-live-examples="built"><Say template={copy.built} values={{ request }} /></p>;
  const next = live.work === null || live.refused ? null : liveNextExamples(SITE_ORIGIN, live.work);
  return (
    <div data-live-examples="live">
      <p>{copy.live}</p>
      <CodeBlock>{liveRestExample(SITE_ORIGIN)}</CodeBlock>
      <p>{copy.answer}</p>
      <div lang={ENGLISH_LANG}><CodeBlock>{liveAnswerText(live.envelope)}</CodeBlock></div>
      {live.refused ? <p>{copy.refused}</p> : null}
      {!live.refused && live.work === null ? <p>{copy.none}</p> : null}
      {next === null ? null : (
        <>
          <p>
            <Say
              template={live.work.date === null ? copy.nextUndated : copy.next}
              values={{ identifier: code(live.work.identifier), ...(live.work.date === null ? {} : { date: code(live.work.date) }) }}
            />
          </p>
          <CodeBlock>{next.rest}</CodeBlock>
          <CodeBlock>{next.mcp}</CodeBlock>
        </>
      )}
    </div>
  );
}

/** What Lex never holds: each class of the out-of-scope list, with its official place and who to ask. */
function OutOfScope({ list }) {
  const copy = liveChrome().developers.refusals.outOfScope;
  const link = (href, text) => <a href={href} rel="external">{text}</a>;
  return (
    <>
      <h3>{copy.heading}</h3>
      <p><Say template={copy.text} values={{ code: code('out_of_corpus_scope'), list: code(list.schema), digest: code(list.sha256) }} /></p>
      <ul data-out-of-scope="">
        {list.classes.map((entry) => (
          <li key={entry.id} data-out-of-scope-class={entry.id}>
            <Say
              template={copy.row}
              values={{
                instrument: inEnglish(entry.instrument),
                reason: inEnglish(entry.reason),
                examples: codeList(entry.examples),
                official: link(entry.officialSource, entry.officialSource),
                who: link(entry.whoToAsk.link, inEnglish(entry.whoToAsk.name)),
              }}
            />
          </li>
        ))}
      </ul>
      <p><Say template={copy.pending} values={{ code: code('identifier_unknown') }} /></p>
      <p data-out-of-scope-population=""><Say template={copy.population} values={{ code: code('out_of_corpus_scope') }} /></p>
    </>
  );
}

/** The replay guarantees, G1 to G5, each as this server supports it, with the test that holds it. */
function Replay() {
  const copy = liveChrome().developers.replay;
  const values = {
    fileReplaced: code('file_replaced'), observedAt: code('context.freshness.observed_at'), requestRef: code('request_ref'),
    servingCommit: code('context.commits.serving_runtime_source_commit'), ageDays: code('context.freshness.channels[].age_days'),
    channelStale: code('context.freshness.channels[].stale'), stale: code('context.freshness.stale'),
    recognisedDate: code('result.value.reference.recognised.date'), rateLimited: code('rate_limited'),
    asObserved: code('as_observed'), knowableOn: code('knowable_on'), provenance: code('provenance'), bundle: code('evidence_bundle'),
    days: RETENTION_NIGHTLY_DAYS,
  };
  return (
    <>
      <p><Say template={copy.text} values={{ tests: code(REPLAY_TESTS), first: REPLAY_GUARANTEES[0].id, last: REPLAY_GUARANTEES.at(-1).id }} /></p>
      <dl className="api-members" data-replay="">
        {REPLAY_GUARANTEES.map((guarantee) => {
          const said = copy.guarantees[guarantee.id];
          if (said === undefined) throw new Error(`the replay guarantee ${guarantee.id} has no words on the Developers page`);
          return (
            <div key={guarantee.id} className="strip-row" data-guarantee={guarantee.id}>
              <dt>{`${guarantee.id}: ${said.name}`}</dt>
              <dd>
                <Sentence template={said.text} values={values} />
                {' '}
                <Say template={copy.test} values={{ test: code(guarantee.test) }} />
              </dd>
            </div>
          );
        })}
      </dl>
    </>
  );
}

/** A part of the Developers page, under its heading. */
function Part({ name, heading, children }) {
  return (
    <section className="reference-section" aria-labelledby={`developers-${name}`} data-reference-section={name}>
      <h2 id={`developers-${name}`}>{heading}</h2>
      {children}
    </section>
  );
}

/** The Developers page's content, from the contracts (`developersModel`), with its live examples when the server rendered it (`live`). */
export function DevelopersBody({ model, live = null }) {
  if (model === null || typeof model !== 'object' || !Array.isArray(model.operations)) {
    throw new Error('the Developers page is generated from the contracts, and was given none');
  }
  const chrome = liveChrome();
  const copy = chrome.developers;
  const endpoint = <code>{`${SITE_ORIGIN}${MCP_ENDPOINT}`}</code>;
  const count = (value) => new Intl.NumberFormat(CHROME_LOCALE).format(value);
  const bytes = count(MAXIMUM_REQUEST_BYTES);
  const events = model.events;
  return (
    <>
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <Contents entries={developersParts(copy)} />

      <Part name="access" heading={copy.access.heading}>
        <p><Say template={copy.access.rest} values={{ request: code('POST /api/v3/{operation}'), body: code('{"operation_id": "…", "parameters": {…}}') }} /></p>
        <p><Say template={copy.access.mcp} values={{ endpoint, version: code(MCP_PROTOCOL_VERSION), methods: codeList(MCP_METHODS), parameters: code('parameters') }} /></p>
        <p><Say template={copy.access.same} values={{ isError: code('isError: false') }} /></p>
        <p><Say template={copy.access.instructions} values={{ initialize: code('initialize'), instructions: code('instructions'), ping: code('ping') }} /></p>
        <blockquote className="reference-quote" data-mcp-instructions=""><p>{inEnglish(MCP_INSTRUCTIONS)}</p></blockquote>
        <p>{copy.access.restExample}</p>
        <CodeBlock>{restExample(SITE_ORIGIN)}</CodeBlock>
        <p>{copy.access.mcpExample}</p>
        <CodeBlock>{mcpExample()}</CodeBlock>
        <p><Say template={copy.access.client} values={{ endpoint }} /></p>
        <CodeBlock>{mcpClientExample(SITE_ORIGIN)}</CodeBlock>
      </Part>

      <Part name="examples" heading={copy.examples.heading}>
        <LiveExamples live={liveExamples(live)} />
      </Part>

      <Part name="operations" heading={copy.operations.heading}>
        <p data-operations-count={model.operations.length} data-served-count={model.served}>
          <Say template={countedEntry(copy.operations.summary, model.operations.length)} values={{ count: model.operations.length, served: model.served }} />
        </p>
        <p><Say template={copy.operations.europe} values={{ code: code('retrieval_mode_unavailable'), member: code('operations.refused_for_eu') }} /></p>
        {model.operations.map((operation) => <Operation key={operation.id} operation={operation} />)}
      </Part>

      <Part name="envelope" heading={copy.envelope.heading}>
        <p><Say template={copy.envelope.text} values={{ schema: <SchemaLink schema={model.envelope.schema} text={model.envelope.schema.id} /> }} /></p>
        <dl className="api-members" data-envelope-members="">
          {model.envelope.members.map((member) => {
            const said = copy.envelope.members[member];
            if (typeof said !== 'string') throw new Error(`the envelope's member ${member} has no description on the Developers page`);
            return <div key={member} className="strip-row" data-member={member}><dt><code>{member}</code></dt><dd>{said}</dd></div>;
          })}
        </dl>
        <p><Say template={copy.envelope.contextText} values={{ context: code('context') }} /></p>
        <dl className="api-members" data-context-members="">
          {model.envelope.contextMembers.map((member) => {
            const said = copy.envelope.context[member];
            if (typeof said !== 'string') throw new Error(`the envelope context's member ${member} has no description on the Developers page`);
            const values = {
              commits: { builder: code('index_builder_source_commit'), builderLabel: chrome.freshness.indexCommit, runtime: code('serving_runtime_source_commit'), runtimeLabel: chrome.freshness.serviceCommit },
              provisional: { value: code('false') },
              timeline_semantics: { luxembourg: code('publisher_applicability'), europe: code('official_consolidation_state') },
            }[member] ?? {};
            return <div key={member} className="strip-row" data-member={`context.${member}`}><dt><code>{member}</code></dt><dd><Sentence template={said} values={values} /></dd></div>;
          })}
        </dl>
        <p><Say template={copy.envelope.verdictText} values={{ verdict: code('verdict') }} /></p>
        <dl className="api-members" data-verdicts="">
          {model.verdicts.map((verdict) => {
            const said = copy.envelope.verdicts[verdict];
            if (typeof said !== 'string') throw new Error(`the verdict ${verdict} has no description on the Developers page`);
            return <div key={verdict} className="strip-row" data-verdict={verdict}><dt><code>{verdict}</code></dt><dd><Sentence template={said} values={{ ask: code('ask') }} /></dd></div>;
          })}
        </dl>
        <p data-registry-sha256={model.registry.sha256}><Say template={copy.envelope.registry} values={{ member: code('registry_sha256'), digest: code(model.registry.sha256) }} /></p>
      </Part>

      <Part name="refusals" heading={copy.refusals.heading}>
        <p data-refusals-count={model.refusals.length}>
          <Say
            template={countedEntry(copy.refusals.text, model.refusals.length)}
            values={{ count: model.refusals.length, refuse: code('refuse'), schema: <SchemaLink schema={model.refusalSchema} text={model.refusalSchema.id} /> }}
          />
        </p>
        <p>{copy.refusals.pages}</p>
        <dl className="api-members" data-refusal-codes="">
          {model.refusals.map((refusal) => {
            const said = copy.refusals.codes[refusal.code];
            if (typeof said !== 'string') throw new Error(`the refusal code ${refusal.code} has no description on the Developers page`);
            return (
              <div key={refusal.code} id={`refusal-${refusal.code}`} className="strip-row" data-refusal={refusal.code}>
                <dt><code>{refusal.code}</code></dt>
                <dd>
                  {said}
                  {' '}
                  <Say template={copy.refusals.fields} values={{ fields: codeList(refusal.fields), schema: <SchemaLink schema={refusal.payloadSchema} text={refusal.payloadSchema.id} /> }} />
                </dd>
              </div>
            );
          })}
        </dl>
        <OutOfScope list={model.outOfScope} />
        <h3>{copy.refusals.presentationHeading}</h3>
        <ul data-presentation-results="">
          {PRESENTATION_RESULTS.map((result) => (
            <li key={result} data-presentation={result}><Sentence template={copy.refusals.presentation[result]} values={{ code: code(result), ask: code('ask') }} /></li>
          ))}
        </ul>
        <h3>{copy.refusals.transportHeading}</h3>
        <p><Say template={copy.refusals.transport} values={{ schema: code(TRANSPORT_PROBLEM_SCHEMA) }} /></p>
        <ul data-transport-problems="">
          {TRANSPORT_PROBLEMS.map((problem) => (
            <li key={problem.code} data-transport-problem={problem.code}>
              <Say template={copy.refusals.transportRow} values={{ code: code(problem.code), status: problem.status, title: inEnglish(problem.title) }} />
            </li>
          ))}
        </ul>
      </Part>

      <Part name="citing" heading={copy.citing.heading}>
        <p>
          <Say
            template={copy.citing.quote}
            values={{
              text: code('text_sha256'), body: code('body_sha256'), source: code('official_source'), permalink: code('article_permalink'),
              shape: code('/{publisher}/{work}/{date}--{digest}/{language}/{article}'),
            }}
          />
        </p>
        <p><Say template={copy.citing.verify} values={{ verify: code('verify') }} /></p>
        <p data-key-set={KEY_SET_PATH}><Say template={copy.citing.keys} values={{ keySet: code(`${SITE_ORIGIN}${KEY_SET_PATH}`) }} /></p>
        <p><Say template={copy.citing.forms} values={{ conventional: chrome.reading.citeConventional, withPermalink: chrome.reading.citeWithPermalink, evidence: chrome.reading.citeEvidence }} /></p>
      </Part>

      <Part name="limits" heading={copy.limits.heading}>
        <p><Say template={copy.limits.anonymous} values={{ privacyPage: pageLink(legalPage('privacy').file, chrome.legal.inline.privacy) }} /></p>
        <p data-request-bytes={MAXIMUM_REQUEST_BYTES}><Say template={copy.limits.size} values={{ bytes, code: code('request_too_large') }} /></p>
        <p><Say template={copy.limits.paging} values={{ limit: code('limit'), after: code('after') }} /></p>
        <p data-client-limit={`${CLIENT_LIMIT.apiPerMinute}/${CLIENT_LIMIT.apiInFlight}/${CLIENT_LIMIT.status}`}>
          <Say
            template={copy.limits.rate}
            values={{
              perMinute: count(CLIENT_LIMIT.apiPerMinute), atOnce: count(CLIENT_LIMIT.apiInFlight), status: CLIENT_LIMIT.status,
              code: code('rate_limited'), retryAfter: code('retry_after'), retryAfterHeader: code('Retry-After'),
            }}
          />
        </p>
        <p data-render-limit={CLIENT_LIMIT.rendersPerMinute}><Say template={copy.limits.pages} values={{ renders: count(CLIENT_LIMIT.rendersPerMinute) }} /></p>
        <p><Say template={copy.limits.availability} values={{ termsPage: pageLink(legalPage('terms').file, chrome.legal.inline.terms) }} /></p>
      </Part>

      <Part name="events" heading={copy.events.heading}>
        <p>
          <Say
            template={copy.events.text}
            values={{ events: code('events'), first: code('first_sighting'), after: code('after'), nextAfter: code('next_after'), limit: code('limit'), event: code('event') }}
          />
        </p>
        <p><Say template={copy.events.atLeastOnce} values={{ seq: code('seq'), logId: code('log_id') }} /></p>
        <p>{copy.events.names} {codeList(events.names)}.</p>
        <p><Say template={copy.events.cursor} values={{ pattern: code(events.cursorPattern) }} /></p>
        <p><Say template={copy.events.drift} values={{ drift: code('answer_drift') }} /></p>
        <p>{copy.events.platform}</p>
        <blockquote className="reference-quote" data-events-delivery=""><p>{inEnglish(events.delivery)}</p></blockquote>
      </Part>

      <Part name="replay" heading={copy.replay.heading}>
        <Replay />
      </Part>

      <Part name="changes" heading={copy.changes.heading}>
        <p><Say template={copy.changes.versioned} values={{ example: code(model.envelope.schema.id), registry: code(model.registry.schema) }} /></p>
        <p>{copy.changes.additive}</p>
        <p><Say template={copy.changes.digest} values={{ code: code('out_of_corpus_scope'), scopeList: code('scope_list'), scopeListDigest: code('scope_list_sha256') }} /></p>
        <p><Say template={copy.changes.registryInEnvelope} values={{ member: code('registry_schema') }} /></p>
        <p>{copy.changes.closed}</p>
        <p>{copy.changes.notice}</p>
        <p>
          <Say
            template={copy.changes.history}
            values={{
              code: code('out_of_corpus_scope'), registry: code(model.registry.schema), previous: code(PREVIOUS_REGISTRY_SCHEMA),
              date: <time dateTime={REGISTRY_VERSION_SINCE}>{longDate(REGISTRY_VERSION_SINCE)}</time>,
            }}
          />
        </p>
      </Part>

      <Part name="schemas" heading={copy.schemas.heading}>
        <p><Say template={copy.schemas.text} values={{ dialect: code(model.envelope.schema.dialect) }} /></p>
        <ul data-schemas="">
          <li><Say template={copy.schemas.envelope} values={{ link: <SchemaLink schema={model.envelope.schema} text={model.envelope.schema.id} /> }} /></li>
          <li><Say template={copy.schemas.refusal} values={{ link: <SchemaLink schema={model.refusalSchema} text={model.refusalSchema.id} /> }} /></li>
          <li>{copy.schemas.payloads}</li>
        </ul>
      </Part>
    </>
  );
}

/** The reference pages in the V4 layout. */
export const V4_REFERENCE_PAGES = Object.freeze(['decisions', 'evidence']);

/**
 * One reference page, by its chrome section's key (`REFERENCE_PAGES`); the Developers page needs the contracts' `developers`,
 * and, when a server renders it, `live`, the envelope of its own answer to `browse` for the first act it holds.
 */
export function renderLiveReferencePage(pageKey, { developers = null, live = null } = {}) {
  const page = referencePage(pageKey);
  if (page === undefined) throw new Error(`${JSON.stringify(pageKey)} is none of the reference pages: ${REFERENCE_PAGES.map((one) => one.key).join(', ')}`);
  if (live !== null && pageKey !== 'developers') throw new Error(`only the Developers page is rendered from an answer, not ${pageKey}`);
  // The side build: How it works is the new engine's page (`NextPages.jsx`), built in its loading state and drawn again by its
  // script once it has asked the engine for its counts; `EvidenceBody`, the current site's, stays in place, unwired.
  const how = (
    <>
      <div id={NEXT_HOW_ROOT}><NextHowFunnel /></div>
      <script src={livePath(NEXT_SCRIPT)} defer />
    </>
  );
  // Design choices: the funnel's (`NextHow.jsx`), rewritten to the real design; the earlier `DecisionsBody` described another
  // engine and stays unwired (release-a, 21/11/2026, had sent its address to How it works).
  const decisions = (
    <>
      <div id={NEXT_DECISIONS_ROOT}><NextDecisions /></div>
      <script src={livePath(NEXT_SCRIPT)} defer />
    </>
  );
  const body = { decisions, evidence: how, help: <HelpBody />, guide: <GuideBody />, developers: <DevelopersBody model={developers} live={live} /> }[pageKey];
  // How it works and Decisions are V4 pages (the owner's brief of 2026-11-15, V5How): the V4 header and frame, no dock and
  // no skin of their own; Get help, How to read Lex and Developers keep their look.
  const v4 = V4_REFERENCE_PAGES.includes(pageKey);
  // pages2 (21/11/2026): the tab of `decisions.html` names what the page shows (Design choices), not the earlier "Décisions".
  const title = pageKey === 'decisions' ? nextCopy(CHROME_LOCALE).decisions2.title : liveChrome()[pageKey].title;
  return renderDocument(
    <Document
      state={page.state}
      title={title}
      shell={v4 ? null : 'ask'}
      density={v4 ? null : skinFor('ask').density}
      banner="live"
      localePage={page.file}
      answers={false}
      layout={v4 ? 'v4' : null}
    >
      {body}
    </Document>,
  );
}

export const renderLiveDecisionsPage = () => renderLiveReferencePage('decisions');
export const renderLiveEvidencePage = () => renderLiveReferencePage('evidence');
export const renderLiveHelpPage = () => renderLiveReferencePage('help');
export const renderLiveGuidePage = () => renderLiveReferencePage('guide');
export const renderLiveDevelopersPage = ({ developers, live = null } = {}) => renderLiveReferencePage('developers', { developers, live });
