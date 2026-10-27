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
// the verdicts; the refusal codes with the fields each payload must carry; the failures below the envelope; citing, the
// limits and the events log. It describes nothing the contracts do not hold, and throws without them.

import { Document, SITE_ORIGIN } from './Document.jsx';
import { Say, inEnglish } from './LiveAnswer.jsx';
import { countedEntry, fillText, liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { renderDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { OFFICIAL_SITES } from '../scripts/live-failure.mjs';
import { legalPage } from '../scripts/legal-pages.mjs';
import { REFERENCE_PAGES, glossaryRows, referencePage } from '../scripts/reference-pages.mjs';
import {
  MAXIMUM_REQUEST_BYTES, MCP_ENDPOINT, MCP_METHODS, MCP_PROTOCOL_VERSION, PRESENTATION_RESULTS, TRANSPORT_PROBLEMS,
  TRANSPORT_PROBLEM_SCHEMA, mcpClientExample, mcpExample, parameterSchemaText, restExample,
} from '../scripts/developers.mjs';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';

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
        <p data-not-served="">
          <Say template={copy.notServed} values={{ code: code('operation_not_served'), data: inEnglish(operation.dataNeeded) }} />
        </p>
      )}
      <dl className="api-facts">
        <div className="strip-row"><dt>{copy.rest}</dt><dd><code>{`POST ${operation.rest}`}</code></dd></div>
        <div className="strip-row"><dt>{copy.tool}</dt><dd>{operation.tool === null ? copy.notATool : <code>{operation.tool}</code>}</dd></div>
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

/** The parts of the Developers page, each with its id and heading, in order. */
function developersParts(copy) {
  return ['access', 'operations', 'envelope', 'refusals', 'citing', 'limits', 'events', 'schemas'].map((name) => [`developers-${name}`, copy[name].heading]);
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

/** The Developers page's content, from the contracts (`developersModel`). */
export function DevelopersBody({ model }) {
  if (model === null || typeof model !== 'object' || !Array.isArray(model.operations)) {
    throw new Error('the Developers page is generated from the contracts, and was given none');
  }
  const chrome = liveChrome();
  const copy = chrome.developers;
  const endpoint = <code>{`${SITE_ORIGIN}${MCP_ENDPOINT}`}</code>;
  const bytes = new Intl.NumberFormat(CHROME_LOCALE).format(MAXIMUM_REQUEST_BYTES);
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
        <p>{copy.access.restExample}</p>
        <CodeBlock>{restExample(SITE_ORIGIN)}</CodeBlock>
        <p>{copy.access.mcpExample}</p>
        <CodeBlock>{mcpExample()}</CodeBlock>
        <p><Say template={copy.access.client} values={{ endpoint }} /></p>
        <CodeBlock>{mcpClientExample(SITE_ORIGIN)}</CodeBlock>
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
                <dd>{said} <Say template={copy.refusals.fields} values={{ fields: codeList(refusal.fields) }} /></dd>
              </div>
            );
          })}
        </dl>
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
        <p><Say template={copy.citing.forms} values={{ conventional: chrome.reading.citeConventional, withPermalink: chrome.reading.citeWithPermalink, evidence: chrome.reading.citeEvidence }} /></p>
      </Part>

      <Part name="limits" heading={copy.limits.heading}>
        <p><Say template={copy.limits.anonymous} values={{ privacyPage: pageLink(legalPage('privacy').file, chrome.legal.inline.privacy) }} /></p>
        <p data-request-bytes={MAXIMUM_REQUEST_BYTES}><Say template={copy.limits.size} values={{ bytes, code: code('request_too_large') }} /></p>
        <p><Say template={copy.limits.paging} values={{ limit: code('limit'), after: code('after') }} /></p>
        <p><Say template={copy.limits.rate} values={{ code: code('rate_limited'), retryAfter: code('retry_after') }} /></p>
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

      <Part name="schemas" heading={copy.schemas.heading}>
        <p><Say template={copy.schemas.text} values={{ dialect: code(model.envelope.schema.dialect) }} /></p>
        <ul data-schemas="">
          <li><Say template={copy.schemas.envelope} values={{ link: <SchemaLink schema={model.envelope.schema} text={model.envelope.schema.id} /> }} /></li>
          <li><Say template={copy.schemas.refusal} values={{ link: <SchemaLink schema={model.refusalSchema} text={model.refusalSchema.id} /> }} /></li>
        </ul>
      </Part>
    </>
  );
}

/** One reference page, by its chrome section's key (`REFERENCE_PAGES`); the Developers page needs the contracts' `developers`. */
export function renderLiveReferencePage(pageKey, { developers = null } = {}) {
  const page = referencePage(pageKey);
  if (page === undefined) throw new Error(`${JSON.stringify(pageKey)} is none of the reference pages: ${REFERENCE_PAGES.map((one) => one.key).join(', ')}`);
  const body = { help: <HelpBody />, guide: <GuideBody />, developers: <DevelopersBody model={developers} /> }[pageKey];
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
      {body}
    </Document>,
  );
}

export const renderLiveHelpPage = () => renderLiveReferencePage('help');
export const renderLiveGuidePage = () => renderLiveReferencePage('guide');
export const renderLiveDevelopersPage = ({ developers } = {}) => renderLiveReferencePage('developers', { developers });
