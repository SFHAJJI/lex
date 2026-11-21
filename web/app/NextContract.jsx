// One answer in the engine's contract (`lex-next/TRACE-CONTRACT.md`), drawn in the dock's look (`NextDock.jsx` holds the
// shell, the conversation and the asking). In order:
//
//   - the state, as a clear state and never a percentage (K-UI-6): its badge, what it means, the missing piece of a partial
//     answer (K-GEN-6);
//   - what Lex understood: the date the law was read on and where it came from, then each field of the question, each
//     marked as read by code or by a model; the date is a button to read the law on another day;
//   - the texts not held that the question needs, by name (K-SCOPE-5), whatever the state;
//   - a clarification's options, each a button that asks again;
//   - the list's completeness, by code from the structure (K-UI-5);
//   - one card a block: its role, the law's own lines highlighted, the article and its act, the version with its dates, the
//     reason, a link that opens the article in the page (K-UI-1), and a choice to widen the quotation to the alinéa, the
//     article or the section (K-UI-3);
//   - for a "not found" or proposed passages, what Lex checked, in plain words (K-UI-4, K-GEN-5);
//   - the trace, folded (K-UI-2): the funnel's levels with their counts, each row by code or by a model; the model calls,
//     measured; the cost; the versions of the code and the data; the link that shows the answer again from its token.
//
// Every word is `scripts/next-copy.mjs`'s; every value the answer's (`scripts/next-contract.mjs`).

import { useEffect, useState } from 'react';
import { textHref } from '../scripts/text-address.mjs';
import { countedForm, fillNext, nextCopy } from '../scripts/next-copy.mjs';
import {
  CONTRACT_ASSERTED, callRows, readableReason, clarifiedQuestion, clarifyOf, contextAddress, contractBlockView, contractBuild, contractFoot, contractStateOf, dateLine,
  evidenceView, listStatement, notHeldLines, replayToken, replayTokenHref, traceLevels, understoodFields, widened,
  blockSource, blockVia, checksView, contractVersions, engineSaid, listItemsView, notesView, partsView, pointedLines, reasonBy, seeAlsoView, sourcesAddress,
} from '../scripts/next-contract.mjs';

/** The language tag the law's own words are marked with on a page written in another language. */
const lawLang = (locale) => (locale === 'fr' ? undefined : 'fr');

/** The tag of a row: code, a model, or both. */
function Tag({ by, copy }) {
  return (
    <span className={by === 'code' ? 'lx-dock-tag' : 'lx-dock-tag lx-dock-tag-model'} data-stage-by={by}>
      {by === 'code' ? copy.tagCode : by === 'model' ? copy.tagModel : copy.tagBoth}
    </span>
  );
}

/** Reads one address of the engine, once, by GET: `{ state, data }`. No word of a question is ever in it. */
export async function readContext(address, fetchImpl) {
  try {
    const response = await (fetchImpl ?? globalThis.fetch)(address, { method: 'GET', headers: { accept: 'application/json' } });
    if (!response.ok) return { state: 'failed', data: null };
    const data = await response.json();
    return data !== null && typeof data === 'object' ? { state: 'ready', data } : { state: 'failed', data: null };
  } catch {
    return { state: 'failed', data: null };
  }
}

/** The state at the head: the badge, the lead, what it names. */
function StateHead({ answer, locale }) {
  const copy = nextCopy(locale).contract;
  const state = contractStateOf(answer, locale);
  if (state === null) return null;
  const asserted = CONTRACT_ASSERTED.includes(state.key) && state.key !== 'partial';
  return (
    <div className={asserted ? 'lx-dock-state' : 'lx-dock-card lx-dock-state'} data-dock-card={asserted ? undefined : 'outcome'} data-dock-state={state.key}>
      <p className="lx-dock-state-badge"><span className="lx-visually-hidden">{copy.stateLabel} </span><span className="lx-dock-chip lx-dock-chip-state" data-state-badge={state.key}>{state.badge}</span></p>
      <p className="lx-dock-lead lx-dock-outcome" data-dock-outcome={state.key}>{state.lead}</p>
      {state.more === null ? null : (
        <p className="lx-dock-words" data-dock-more="">
          {state.more}
          {/* What is missing is the reading model's own sentence (K-GEN-7). */}
          {state.moreBy === 'model' ? <> <Tag by="model" copy={nextCopy(locale).dock} /></> : null}
        </p>
      )}
    </div>
  );
}

/**
 * pages2: the engine's notes said in words (a model that did not answer, passages that disagree, the text changed in the
 * period), and the one check a reader can weigh (found by meaning alone; or the searches that found it).
 */
function NotesAndChecks({ answer, locale }) {
  const notes = notesView(answer, locale);
  const checks = checksView(answer, locale);
  const lead = checks?.lead ?? null;
  if (notes.length === 0 && lead === null) return null;
  return (
    <div className="lx-dock-notes" data-dock-notes="">
      {lead === null ? null : <p className={lead.caution ? 'lx-dock-remark lx-dock-caution' : 'lx-dock-remark'} data-dock-check={lead.caution ? 'meaning-only' : 'found-by'}>{lead.text}</p>}
      {notes.map((note) => <p key={note.key} className={note.caution ? 'lx-dock-remark lx-dock-caution' : 'lx-dock-remark'} data-dock-note={note.key}>{note.text}</p>)}
    </div>
  );
}

/** pages2: the words of the law that give the short answer (engine2's `short_answer`), with who found them. */
function ShortAnswer({ answer, locale }) {
  const said = engineSaid(answer, locale).shortAnswer;
  if (said === null) return null;
  const copy = nextCopy(locale).contract2.shortAnswer;
  return (
    <div className="lx-dock-short" data-dock-short-answer="">
      <p className="lx-dock-remark"><span className="lx-dock-why-by">{copy.heading}</span></p>
      <p className="lx-dock-words" lang={lawLang(locale)}><strong>{said.line}</strong></p>
      <p className="lx-dock-remark">{copy.by}{said.kind === null ? null : <> {said.kind}</>} <Tag by="both" copy={nextCopy(locale).dock} /></p>
    </div>
  );
}

/** pages2: what engine2 says beside the answer: texts named and no longer law on that date, a directive's Luxembourg law, EU texts not held. */
function EngineNotes({ answer, locale }) {
  const said = engineSaid(answer, locale);
  const copy = nextCopy(locale).contract2;
  const cards = [
    ['repealed', copy.repealedHeading, said.repealed],
    ['transposed-by', copy.directiveHeading, said.transposedBy],
    ['eu-not-held', copy.euNotHeldHeading, said.euNotHeld],
  ].filter(([, , lines]) => lines.length > 0);
  if (cards.length === 0) return null;
  return cards.map(([key, heading, lines]) => (
    <div className="lx-dock-card lx-dock-notheld" data-dock-card={key} key={key}>
      <p className="lx-dock-lead">{heading}</p>
      <ul className="lx-dock-step-list">{lines.map((line) => <li key={line} lang={key === 'eu-not-held' ? undefined : lawLang(locale)}>{line}</li>)}</ul>
    </div>
  ));
}

/** pages2: the items of a list made of headings, in the text's order, each opening the article that stands for it. */
function ListItems({ answer, locale, onOpen }) {
  const view = listItemsView(answer, locale);
  if (view === null) return null;
  const copy = nextCopy(locale).contract2.list;
  const lang = lawLang(locale);
  const blocks = Array.isArray(answer.blocks) ? answer.blocks : [];
  return (
    <div className="lx-dock-card lx-dock-list-items" data-dock-card="list-items">
      {view.items.length === 0 ? null : (
        <>
          <p className="lx-dock-lead">{copy.heading}</p>
          <ol className="lx-dock-items" data-dock-items="">
            {view.items.map((item) => {
              const block = item.block === null ? null : contractBlockView(blocks[item.block], locale, answer.state);
              const opening = block === null || block.identifier === null || block.anchor === null ? null : { identifier: block.identifier, article: block.anchor, date: block.date };
              const href = opening === null ? null : textHref(opening, locale);
              const where = [block?.cite ?? (item.first === null ? null : fillNext(copy.from, { article: item.first })), item.articles].filter((part) => part !== null && part !== '').join(' · ');
              return (
                <li key={item.key} data-list-item="">
                  {href === null ? <span lang={lang}>{item.heading}</span> : (
                    <a className="lx-dock-link" href={href} lang={lang} onClick={(event) => {
                      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
                      event.preventDefault();
                      onOpen(opening);
                    }}>{item.heading}</a>
                  )}
                  {where.length === 0 ? null : <span className="lx-dock-row-time" lang={lang}> · {where}</span>}
                </li>
              );
            })}
          </ol>
        </>
      )}
      {view.deleted === null ? null : <p className="lx-dock-remark" data-dock-list-deleted="">{view.deleted}</p>}
    </div>
  );
}

/** pages2: "See also": the articles the engine names beside the answer, each with why. */
function SeeAlso({ answer, locale }) {
  const entries = seeAlsoView(answer);
  if (entries.length === 0) return null;
  const copy = nextCopy(locale).contract2.seeAlso;
  return (
    <div className="lx-dock-card" data-dock-card="see-also">
      <p className="lx-dock-lead">{copy.heading}</p>
      <ul className="lx-dock-step-list">
        {entries.map((entry) => (
          <li key={entry.key} lang={lawLang(locale)}>{entry.why === null ? [entry.article, entry.heading].filter(Boolean).join(', ') : fillNext(copy.item, { article: [entry.article, entry.heading].filter(Boolean).join(', '), why: entry.why })}</li>
        ))}
      </ul>
    </div>
  );
}

/** What Lex understood: the date (a button that opens the field for another day) and each field, marked code or model. */
function Understood({ answer, turn, locale, onAsk, typedDay, initialDateOpen }) {
  const table = nextCopy(locale);
  const copy = table.contract;
  const [open, setOpen] = useState(initialDateOpen);
  const [value, setValue] = useState('');
  const date = dateLine(answer, locale);
  const fields = understoodFields(answer, locale).filter((field) => field.key !== 'date');
  if (date === null && fields.length === 0) return null;
  const formId = `lx-dock-${turn.id}-date`;
  return (
    <div className="lx-dock-understood" data-dock-understood="">
      <p className="lx-dock-more">
        <span className="lx-dock-understood-label">{table.dock.understood}</span>
        {date === null ? null : (
          <button type="button" className="lx-dock-chip lx-dock-chip-date" data-chip="date" aria-expanded={open} aria-controls={formId} title={table.dock.changeDate} onClick={() => setOpen(!open)}>{date}</button>
        )}
        {fields.map((field) => (
          <span className="lx-dock-chip" key={field.key} data-chip={field.key} data-chip-by={field.by ?? undefined} title={field.by === null ? undefined : copy.fieldBy[field.by]}>
            {field.value === null ? field.name : fillNext(copy.fieldItem, { name: field.name, value: field.value })}
          </span>
        ))}
      </p>
      {date === null ? null : (
        <form
          className="lx-dock-date"
          id={formId}
          hidden={!open}
          onSubmit={(event) => {
            event.preventDefault();
            const day = typedDay(value);
            if (day !== null) onAsk(turn.question, day);
          }}
        >
          <label className="lx-dock-date-label" htmlFor={`${formId}-field`}>{table.dock.dateLabel}</label>
          <span className="lx-dock-date-row">
            <input id={`${formId}-field`} type="text" inputMode="numeric" autoComplete="off" placeholder={table.dock.datePlaceholder} value={value} onChange={(event) => setValue(event.target.value)} />
            <button type="submit" className="lx-dock-action">{table.dock.dateSubmit}</button>
          </span>
        </form>
      )}
    </div>
  );
}

/** The texts not held that the answer names. */
function NotHeld({ answer, locale }) {
  const copy = nextCopy(locale).contract;
  const lines = notHeldLines(answer, locale);
  if (lines.length === 0) return null;
  return (
    <div className="lx-dock-card lx-dock-notheld" data-dock-card="not-held">
      <p className="lx-dock-lead">{copy.notHeldHeading}</p>
      <ul className="lx-dock-step-list">{lines.map((line) => <li key={line.name} data-not-held="">{line.line}</li>)}</ul>
    </div>
  );
}

/** A clarification: the question, then each option a button that asks again. */
function Clarify({ answer, turn, locale, onAsk }) {
  const copy = nextCopy(locale).contract;
  const clarify = clarifyOf(answer);
  if (clarify === null) return null;
  return (
    <div className="lx-dock-card" data-dock-card="clarify">
      {clarify.question === null ? null : <p className="lx-dock-lead" data-clarify-question="">{clarify.question}</p>}
      {clarify.options.length === 0 ? null : (
        <>
          <p className="lx-dock-words">{copy.clarifyPick}</p>
          <p className="lx-dock-actions lx-dock-options">
            {clarify.options.map((option) => (
              <button type="button" className="lx-dock-action" key={option} data-clarify-option="" onClick={() => onAsk(clarifiedQuestion(turn.question, option))}>{option}</button>
            ))}
          </p>
        </>
      )}
    </div>
  );
}

/** The list's completeness, said by code. */
function ListStatement({ answer, locale }) {
  const statement = listStatement(answer, locale);
  if (statement === null) return null;
  return (
    <p className={statement.complete ? 'lx-dock-remark' : 'lx-dock-remark lx-dock-caution'} data-dock-list={statement.complete ? 'complete' : 'incomplete'}>
      {statement.main}{statement.stated === null ? null : <> {statement.stated}</>}
    </p>
  );
}

const WIDEN_LEVELS = Object.freeze(['lines', 'alinea', 'article', 'section']);

/** One line drawn, marked when the answer quotes it. */
function Line({ line, first, last, table }) {
  const words = line.quoted ? <mark className="lx-dock-mark">{line.text}</mark> : line.text;
  return <p className={line.quoted ? 'lx-dock-quote' : 'lx-dock-quote lx-dock-context'} data-line={line.n ?? undefined}>{first ? table.quoteOpen : null}{words}{last ? table.quoteClose : null}</p>;
}

/** One block: its role, the lines it quotes (widened on demand), the article and its act, the version and its dates. */
export function ContractBlock({ block, state, locale, onOpen, fetchImpl, answer = null, initialLevel = 'lines', initialContext = null, versionSource = null, explain = true, explainException = true }) {
  const table = nextCopy(locale);
  const copy = table.contract;
  const view = contractBlockView(block, locale, state);
  const [level, setLevel] = useState(initialLevel);
  const [context, setContext] = useState(initialContext === null ? { state: 'idle', data: null } : { state: 'ready', data: initialContext });
  const address = contextAddress(view.articlePk);
  // The article's name in the page, when the block does not carry it: read from the context, as widening reads it.
  const anchor = view.anchor ?? (typeof context.data?.article?.anchor === 'string' ? context.data.article.anchor : null);
  const work = view.identifier;
  useEffect(() => {
    if (address === null || context.state !== 'idle' || (level === 'lines' && view.anchor !== null)) return undefined;
    let live = true;
    setContext({ state: 'loading', data: null });
    readContext(address, fetchImpl).then((answer) => { if (live) setContext(answer); });
    return () => { live = false; };
  }, [address, level]); // eslint-disable-line react-hooks/exhaustive-deps
  const opening = work === null || anchor === null ? null : { identifier: work, article: anchor, date: view.date };
  const href = opening === null ? null : textHref(opening, locale);
  const lang = lawLang(locale);
  const quotedNumbers = view.lines.filter((line) => !line.gap).map((line) => line.n);
  const wide = level === 'lines' || context.state !== 'ready' ? null : widened(context.data, level, quotedNumbers);
  const quoteLines = view.lines.filter((line) => !line.gap);
  const marked = view.role === 'rule' || view.role === 'exception';
  // pages2: when code widened the reading's lines to their paragraph, the lines the reading pointed at are the ones marked.
  const pointed = pointedLines(block);
  const extra = table.contract2;
  const via = blockVia(block, locale);
  // The block's own word on its source, else the store's (`/api/next/sources`, asked once for the answer's versions).
  const source = blockSource(block) ?? (versionSource?.ocr === true ? 'ocr' : versionSource?.pdf === true ? 'pdf' : null);
  const item = typeof block?.list_item === 'string' && block.list_item.trim().length > 0 ? block.list_item.trim() : null;
  const whyBy = reasonBy(block);
  // An exception is read with the rule it may set aside (K-REF-6): said once on its card.
  const exceptionNote = explainException && view.role === 'exception' && (state === 'answer' || state === 'answer_with_exception');
  return (
    <article className="lx-dock-card lx-dock-answer lx-dock-block" data-dock-card="block" data-role={view.role ?? ''} aria-label={view.label}>
      <div className="lx-dock-card-head"><span className="lx-dock-verdict">{view.label}</span></div>
      {item === null ? null : <p className="lx-dock-remark" data-dock-list-item="">{fillNext(extra.listItem, { heading: item })}</p>}
      {/* pages2 (K-GEN-7): the reason under a quotation says who wrote it: a model (the reading, the arbiter) or code (a
          reference followed, a widening, a definition); a model's sentence is never shown as Lex's statement of the law. */}
      {view.why === null || item !== null ? null : (
        <p className="lx-dock-remark" data-dock-why="" data-why-by={whyBy}>
          <span className="lx-dock-why-by">{whyBy === 'model' ? extra.whyModel : extra.whyCode}</span>{' '}
          <span lang={lang}>{readableReason(view.why, answer ?? {})}</span>
        </p>
      )}
      {via === null ? null : <p className="lx-dock-remark" data-dock-via={block?.inbound === true ? 'inbound' : block?.retrieved_via ?? ''}>{via}</p>}
      {exceptionNote ? <p className="lx-dock-remark lx-dock-caution" data-dock-exception-note="">{extra.exceptionNote}</p> : null}
      {source === null ? null : <p className={source === 'ocr' ? 'lx-dock-remark lx-dock-caution' : 'lx-dock-remark'} data-dock-source={source}>{extra.source[source]}</p>}
      {wide === null ? (
        quoteLines.length === 0 ? null : (
          <div className="lx-dock-extract" data-dock-extract={anchor ?? ''} lang={lang}>
            {view.lines.map((line, at) => {
              if (line.gap) return <p className="lx-dock-remark" key={`gap-${at}`} aria-hidden="true">{table.dock.gap}</p>;
              return <Line key={`line-${at}`} line={{ ...line, quoted: marked && (pointed === null || pointed.has(line.n)) }} first={line === quoteLines[0]} last={line === quoteLines.at(-1)} table={table} />;
            })}
          </div>
        )
      ) : wide.kind === 'section' ? (
        <div className="lx-dock-extract lx-dock-wide" data-dock-wide="section" lang={lang}>
          {wide.label === null ? null : <p className="lx-dock-remark" data-dock-section="">{fillNext(copy.sectionOf, { label: wide.label })}</p>}
          {wide.articles.map((entry) => (
            <div key={entry.key} data-dock-wide-article={entry.anchor ?? ''}>
              <p className="lx-dock-cite">{entry.heading === null ? entry.label : `${entry.label}, ${entry.heading}`}</p>
              {entry.lines.map((line, at) => <Line key={`w-${at}`} line={line} first={false} last={false} table={table} />)}
            </div>
          ))}
          {wide.cut ? <p className="lx-dock-remark">{copy.widen.cut}</p> : null}
        </div>
      ) : (
        <div className="lx-dock-extract lx-dock-wide" data-dock-wide={wide.kind} lang={lang}>
          {wide.lines.map((line, at) => <Line key={`w-${at}`} line={line} first={at === 0} last={at === wide.lines.length - 1} table={table} />)}
        </div>
      )}
      {explain && wide === null && marked && (pointed !== null || Object.hasOwn(extra.pointedUnits, block?.context_unit ?? '')) ? (
        <p className="lx-dock-remark" data-dock-pointed={block?.context_unit ?? ''}>{extra.pointedUnits[block?.context_unit ?? ''] ?? extra.pointed}</p>
      ) : null}
      {typeof block?.version?.until_cut === 'string' && block.version.until_cut.length > 0 ? <p className="lx-dock-remark" data-dock-until-cut="">{extra.untilCut}</p> : null}
      {view.missingNow ? <p className="lx-dock-remark lx-dock-caution" data-dock-missing-now="">{copy.trace.missingNow}</p> : null}
      {level !== 'lines' && context.state === 'loading' ? <p className="lx-dock-remark" role="status">{copy.widen.loading}</p> : null}
      {level !== 'lines' && context.state === 'failed' ? <p className="lx-dock-remark" role="status">{copy.widen.failed}</p> : null}
      {address === null ? null : (
        <p className="lx-dock-widen" role="group" aria-label={copy.widen.label} data-dock-widen="">
          <span className="lx-dock-widen-label">{copy.widen.label}</span>
          {WIDEN_LEVELS.map((key) => (
            <button type="button" key={key} className="lx-dock-widen-choice" aria-pressed={level === key} data-widen={key} onClick={() => setLevel(key)}>{copy.widen[key]}</button>
          ))}
        </p>
      )}
      {view.cite === null && view.title === null ? null : (
        <p className="lx-dock-cite" lang={lang}>{[view.cite, view.title].filter((part) => part !== null).join(' ')}</p>
      )}
      <p className="lx-dock-remark" data-dock-version="">
        {view.version}
        {view.official !== null || href !== null ? ' ' : null}
        {view.official === null ? null : <a className="lx-dock-link" href={view.official} rel="noopener noreferrer" data-dock-official="">{table.dock.official}</a>}
        {view.official !== null && href !== null ? table.dock.linkSeparator : null}
        {href === null ? null : (
          <a
            className="lx-dock-link"
            href={href}
            data-dock-open=""
            onClick={(event) => {
              if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
              event.preventDefault();
              onOpen(opening);
            }}
          >
            {table.dock.openArticle}
          </a>
        )}
      </p>
    </article>
  );
}

/** What Lex checked, for a "not found" or proposed passages: what it holds, the words, the sweep, the closest passage. */
function Evidence({ answer, locale }) {
  const copy = nextCopy(locale).contract.evidence;
  const found = evidenceView(answer, locale);
  // pages2 (K-UI-4): a partial answer shows what Lex checked for the part missing too, without a "closest passage" (the
  // passage shown answers in part).
  const partial = answer.state === 'partial';
  if (found === null || (CONTRACT_ASSERTED.includes(answer.state) && !partial)) return null;
  const view = partial ? { ...found, closest: null } : found;
  if (partial && view.coverage === null && view.terms.length === 0 && view.swept === null) return null;
  const lang = lawLang(locale);
  return (
    <div className="lx-dock-card" data-dock-card="searched" data-dock-evidence="">
      <p className="lx-dock-lead">{partial ? nextCopy(locale).contract2.evidencePartial : copy.heading}</p>
      {view.coverage === null ? null : <p className="lx-dock-words" data-evidence="coverage"><strong>{copy.coverage}</strong>{' '}<span lang={lang}>{view.coverage}</span></p>}
      {(view.floors ?? []).map((line) => <p className="lx-dock-words" data-evidence="floor" key={line}>{line}</p>)}
      {view.terms.length === 0 ? null : (
        <table className="lx-dock-table" data-evidence="terms">
          <thead><tr><th scope="col">{copy.terms.term}</th><th scope="col">{copy.terms.variants}</th><th scope="col">{copy.terms.hits}</th></tr></thead>
          <tbody>
            {view.terms.map((term) => (
              <tr key={term.term} data-none={term.hits === 0 ? '' : undefined}>
                <th scope="row" lang={lang}>{term.term}</th>
                <td lang={lang}>{term.variants.join(nextCopy(locale).listSeparator)}</td>
                <td>{term.said}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {view.swept === null ? null : <p className="lx-dock-words" data-evidence="sweep">{view.swept}</p>}
      {view.closest === null ? null : <p className="lx-dock-words" data-evidence="closest">{view.closest} <Tag by="model" copy={nextCopy(locale).dock} /></p>}
    </div>
  );
}

/** The trace, folded: the levels, the model calls, the foot line, the versions, the replay link. */
function Trace({ answer, turn, locale }) {
  const table = nextCopy(locale);
  const dock = table.dock;
  const copy = table.contract.trace;
  const levels = traceLevels(answer, locale);
  const calls = callRows(answer, locale);
  const foot = contractFoot(answer, locale);
  const build = contractBuild(answer, locale);
  const versions = contractVersions(answer, locale);
  const checks = checksView(answer, locale);
  const extra = table.contract2;
  const replay = replayTokenHref(replayToken(answer), locale);
  if (levels.length === 0 && calls.length === 0 && foot === null) return null;
  const count = Array.isArray(answer?.trace?.calls) ? answer.trace.calls.length : null;
  return (
    <details className="lx-dock-card lx-dock-built lx-dock-trace" data-dock-built="" data-dock-trace="">
      <summary className="lx-dock-built-head">
        <span className="lx-dock-built-title">{copy.summary}</span>
        {count === null ? null : <span className="lx-dock-built-facts"><span className="lx-dock-built-by" data-dock-by={count > 0 ? 'model' : 'code'}>{countedForm(dock.builtBy, count, locale)}</span></span>}
      </summary>
      {levels.map((level) => (
        <section className="lx-dock-level" key={level.key} data-level={level.level}>
          <h4 className="lx-dock-level-head"><span className="lx-dock-row-name">{level.name}</span><Tag by={level.by} copy={dock} /></h4>
          <ol className="lx-dock-rows">
            {level.rows.map((row, at) => (
              <li className="lx-dock-row lx-dock-step" key={`${row.key}-${at}`} data-row={row.key}>
                {row.text.length === 0 ? null : <span className="lx-dock-row-line">{row.text}</span>}
                {row.items.length === 0 ? null : (
                  <ul className="lx-dock-step-list">
                    {row.items.map((item, i) => (
                      <li key={`${item.title}-${i}`}>
                        <span lang={lawLang(locale)}>{item.title}</span>
                        {item.tally ? <span className="lx-dock-row-time"> · {item.tally}</span> : null}
                        {item.reason ? <span className="lx-dock-why"> — {item.reason}</span> : null}
                        {' '}<Tag by={item.by} copy={dock} />
                      </li>
                    ))}
                  </ul>
                )}
                {row.table === null ? null : (
                  <div className="lx-dock-table-scroll">
                    <table className="lx-dock-table" data-trace-table={level.level}>
                      <thead><tr>{row.table.columns.map((column) => <th scope="col" key={column}>{column}</th>)}</tr></thead>
                      <tbody>
                        {row.table.rows.map((entry) => (
                          <tr key={entry.key} data-meaning-only={entry.note === null ? undefined : ''}>
                            <th scope="row" lang={lawLang(locale)}>{entry.head}{entry.note === null ? null : <span className="lx-dock-caution-note"> ({entry.note})</span>}</th>
                            {entry.cells.map((cell, i) => <td key={i}>{cell}</td>)}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </li>
            ))}
          </ol>
        </section>
      ))}
      {checks === null || checks.rows.length === 0 ? null : (
        <section className="lx-dock-level" data-level="checks">
          <h4 className="lx-dock-level-head"><span className="lx-dock-row-name">{extra.checks.heading}</span><Tag by="code" copy={dock} /></h4>
          <ul className="lx-dock-step-list">
            {checks.rows.map((row) => <li key={row.key} data-check={row.key}>{fillNext(nextCopy(locale).contract.fieldItem, { name: row.name, value: row.value })}</li>)}
          </ul>
        </section>
      )}
      {calls.length === 0 ? null : (
        <section className="lx-dock-level" data-level="calls">
          <h4 className="lx-dock-level-head"><span className="lx-dock-row-name">{copy.callsHeading}</span><Tag by="model" copy={dock} /></h4>
          <div className="lx-dock-table-scroll">
            <table className="lx-dock-table" data-trace-table="calls">
              <thead><tr>{['step', 'model', 'time', 'tokens'].map((key) => <th scope="col" key={key}>{copy.callColumns[key]}</th>)}</tr></thead>
              <tbody>{calls.map((call) => <tr key={call.key} data-call-note={call.note ? '' : undefined}><th scope="row"><code>{call.step}</code></th><td><code>{call.model}</code>{call.note ? <span className="lx-dock-caution-note"> ({call.note})</span> : null}</td><td>{call.time}</td><td>{call.tokens}</td></tr>)}</tbody>
            </table>
          </div>
        </section>
      )}
      {foot === null ? null : <p className="lx-dock-built-note" data-dock-cost="">{foot}</p>}
      {build === null ? null : <p className="lx-dock-registry" data-dock-build="">{build}{versions === null ? null : <>{nextCopy(locale).clauseSeparator}{versions}</>}</p>}
      {/* pages2 (K-UI-2): the trace is not logged on the server, a deliberate choice, said where the trace is. */}
      <p className="lx-dock-built-note" data-dock-trace-kept="">{extra.traceNotKept}</p>
      {replay === null ? null : <p className="lx-dock-registry"><a className="lx-dock-link" href={replay} data-dock-replay="">{copy.replay}</a></p>}
    </details>
  );
}

/**
 * One turn whose answer is in the contract: the state, what Lex understood, the texts not held, the clarification, the list's
 * completeness, the blocks (under the evidence for a "not found"), and the trace.
 */
export function ContractAnswer({ turn, locale, onAsk, onOpen, typedDay, fetchImpl, initialDateOpen = false }) {
  const table = nextCopy(locale);
  const answer = turn.result;
  const blocks = Array.isArray(answer.blocks) ? answer.blocks.filter((block) => block !== null && typeof block === 'object') : [];
  // pages2: where the quoted versions' words come from (an OCR of a scanned Mémorial page is said on its card), asked once.
  const [sources, setSources] = useState({});
  const sourcesAt = sourcesAddress(answer);
  useEffect(() => {
    if (sourcesAt === null) return undefined;
    let live = true;
    readContext(sourcesAt, fetchImpl).then((read) => { if (live && read.state === 'ready') setSources(read.data); });
    return () => { live = false; };
  }, [sourcesAt]); // eslint-disable-line react-hooks/exhaustive-deps
  // The sentences that explain how lines are marked and how an exception is read are said once, on the first card that needs them.
  const firstPointed = blocks.findIndex((block) => ['rule', 'exception'].includes(block.role) && (pointedLines(block) !== null || Object.hasOwn(table.contract2.pointedUnits, block.context_unit ?? '')));
  const firstException = blocks.findIndex((block) => block.role === 'exception');
  const card = (block, at) => (
    <ContractBlock block={block} answer={answer} state={answer.state} locale={locale} onOpen={onOpen} fetchImpl={fetchImpl} key={`${block.article_pk ?? 'block'}-${at}`}
      versionSource={sources[String(block?.version?.id)] ?? null} explain={at === firstPointed} explainException={at === firstException} />
  );
  const cards = blocks.map(card);
  // Nothing found: the state, then what Lex checked, and only then the closest passages, under a heading that says they do not answer.
  const notFound = answer.state === 'not_found';
  // pages2: a compound question is drawn part by part: each part's question and whether it is answered, then its passages.
  const parts = notFound ? null : partsView({ ...answer, blocks }, locale);
  const extra = table.contract2.parts;
  const body = parts === null ? cards : (
    <div className="lx-dock-parts" data-dock-parts={parts.grouped ? 'grouped' : 'listed'}>
      <p className={parts.complete ? 'lx-dock-remark' : 'lx-dock-remark lx-dock-caution'} data-dock-parts-summary="">{parts.heading}. {parts.summary}</p>
      {parts.grouped ? null : (
        <ol className="lx-dock-items" data-dock-parts-list="">
          {parts.rows.map((row) => <li key={row.n} data-part={row.n} data-part-status={row.status}><span>{row.question}</span> <span className="lx-dock-chip" data-part-badge="">{row.label}</span></li>)}
        </ol>
      )}
      {parts.rows.map((row) => (!parts.grouped ? null : (
        <section className="lx-dock-part" key={row.n} data-part={row.n} data-part-status={row.status} aria-label={fillNext(extra.part, { n: String(row.n) })}>
          <h3 className="lx-dock-passages-heading"><span>{fillNext(extra.part, { n: String(row.n) })}</span> <span className="lx-dock-chip" data-part-badge="">{row.label}</span></h3>
          <p className="lx-dock-words" data-part-question="">{row.question}</p>
          {row.blocks.length === 0 ? <p className="lx-dock-remark">{extra.noPassage}</p> : row.blocks.map((index) => card(blocks[index], index))}
        </section>
      )))}
      {parts.grouped && parts.loose.length > 0 ? (
        <section className="lx-dock-part" data-part="whole">
          <h3 className="lx-dock-passages-heading">{extra.whole}</h3>
          {parts.loose.map((index) => card(blocks[index], index))}
        </section>
      ) : null}
      {parts.grouped ? null : cards}
    </div>
  );
  return (
    <>
      <StateHead answer={answer} locale={locale} />
      <ShortAnswer answer={answer} locale={locale} />
      <NotesAndChecks answer={answer} locale={locale} />
      <Understood answer={answer} turn={turn} locale={locale} onAsk={onAsk} typedDay={typedDay} initialDateOpen={initialDateOpen} />
      <NotHeld answer={answer} locale={locale} />
      <EngineNotes answer={answer} locale={locale} />
      <Clarify answer={answer} turn={turn} locale={locale} onAsk={onAsk} />
      <ListStatement answer={answer} locale={locale} />
      <ListItems answer={answer} locale={locale} onOpen={onOpen} />
      {notFound ? null : body}
      <SeeAlso answer={answer} locale={locale} />
      <Evidence answer={answer} locale={locale} />
      {notFound && cards.length > 0 ? (
        <>
          <h3 className="lx-dock-passages-heading" data-dock-passages="">{table.dock.passagesHeading}</h3>
          {cards}
        </>
      ) : null}
      <Trace answer={answer} turn={turn} locale={locale} />
    </>
  );
}
