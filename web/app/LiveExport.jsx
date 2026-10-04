// The export composer, asked of the live API: pin articles of one reading and save them as JSON, CSV or PDF.
//
// The server renders the form and the idle state, and the browser asks only when the reader submits;
// pinning, composing and saving ask nothing. What an answer means is decided elsewhere:
// `scripts/live-reading.mjs` builds the request and maps the answer to a view state (the export asks
// the reading), `scripts/export-build.mjs` composes what the pinned articles carry, and
// `scripts/live-export.mjs` holds the pins and saves a file. This file lays the view out and marks the
// states (`data-answer-state` on the reading, `data-export-state` on the export).
//
// The reading screen's rules carry over: the publisher's text is quoted in its state's language, an
// article held without text is named as such, the controls carry no `name`, and permalinks are
// printed, not linked. The export panel shows, before anything is saved, what the file will carry:
// the counts, the watermark, the rights, each item's citation and digest, the exclusions with their
// reason, and the JSON itself.
//
// An EU reading is pinned from as the reading screen shows it: below Decision 95's acknowledgement and
// authenticity statement, each wording headed by its own date as a wording's, with the annexes it does
// not serve as text, which no pin reaches. Its panel says the acknowledgement every exported article
// carries and the authenticity statement, dates each item as its wording is, and lists the annexes of
// each wording an article is pinned from as excluded, with the platform's reason and official source.

import { useEffect, useRef, useState } from 'react';

import { EuropeWordingHeading, QuoteEvidence, ReadingAnswerView, ReadingForm } from './LiveReading.jsx';
import { createReadingSession, quotationLanguageTag } from '../scripts/live-reading.mjs';
import { EXPORT_FORMATS, LIVE_EXPORT_IDLE, exportState, formatRefused, pinKey, saveExport } from '../scripts/live-export.mjs';
import { EUROPE_EXPORT_SCHEMA, exportJson } from '../scripts/export-build.mjs';
import { ENGLISH_LANG, countedEntry, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { EuropeAnnexes, LiveAnswer, Said, Say, StatusSentence, inEnglish } from './LiveAnswer.jsx';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().export;
// The export reads what the reading screen reads, so a state is headed as the reading screen heads it.
const READING = liveChrome().reading;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_EXPORT_IDLE });

function Pin({ checked, onPin, label }) {
  return (
    <label>
      <input type="checkbox" data-pin="" checked={checked} onChange={(event) => onPin(event.target.checked)} /> {label}
    </label>
  );
}

/**
 * The articles held without text in a state or a wording (whose digest is `digest`), each with a pin: an export
 * records a pinned one as excluded, with its reason.
 */
function WithoutTextPins({ entries, digest, pins, onPin }) {
  if (entries.length === 0) return null;
  return (
    <>
      <h3>{COPY.withoutTextHeading}</h3>
      <ul data-without-text={entries.length}>
        {entries.map((entry) => {
          const key = pinKey(digest, entry.publisherId);
          return (
            <li key={entry.articleIdentitySha256}>
              <Pin checked={pins.has(key)} onPin={(on) => onPin(key, on)} label={fillText(COPY.pin, { article: entry.publisherId })} />{' '}
              {COPY.withoutTextNote}
            </li>
          );
        })}
      </ul>
    </>
  );
}

function StatePins({ state, workKey, pins, onPin }) {
  return (
    <section data-state={state.stateSha256}>
      <h2>
        {state.nextApplicabilityDate === null ? (
          <Say template={READING.stateHeading} values={{ work: workKey, language: state.language, from: state.applicabilityDate }} />
        ) : (
          <Say template={READING.stateHeadingNext} values={{ work: workKey, language: state.language, from: state.applicabilityDate, next: state.nextApplicabilityDate }} />
        )}
      </h2>
      <p>
        <code>{state.permalink}</code>
      </p>
      <ol className="articles">
        {state.articles.map((article) => {
          const key = pinKey(state.stateSha256, article.publisherId);
          return (
            <li key={article.articleIdentitySha256} data-article={article.publisherId}>
              <Pin checked={pins.has(key)} onPin={(on) => onPin(key, on)} label={fillText(COPY.pin, { article: article.publisherId })} />
              <blockquote lang={quotationLanguageTag(state.language)}>{article.text}</blockquote>
              <QuoteEvidence article={article} />
            </li>
          );
        })}
      </ol>
      <WithoutTextPins entries={state.articlesWithoutText} digest={state.stateSha256} pins={pins} onPin={onPin} />
    </section>
  );
}

/**
 * One EU wording to pin from, headed as the reading screen heads it: each quoted article with its pin, its heading
 * and text in the wording's language, and its evidence; the articles held without text; and the annexes the wording
 * does not serve as text.
 */
function WordingPins({ wording, celex, pins, onPin }) {
  const statuteLanguage = quotationLanguageTag(wording.language);
  return (
    <section data-wording={wording.wordingSha256}>
      <h2>
        <EuropeWordingHeading wording={wording} celex={celex} />
      </h2>
      <p>
        <code>{wording.permalink}</code>
      </p>
      <ol className="articles">
        {wording.articles.map((article) => {
          const key = pinKey(wording.wordingSha256, article.publisherId);
          return (
            <li key={article.articleIdentitySha256} data-article={article.publisherId}>
              <Pin checked={pins.has(key)} onPin={(on) => onPin(key, on)} label={fillText(COPY.pin, { article: article.publisherId })} />
              {article.heading.length > 0 ? (
                <>
                  {' '}
                  <span lang={statuteLanguage}>{article.heading}</span>
                </>
              ) : null}
              <blockquote lang={statuteLanguage}>{article.text}</blockquote>
              <QuoteEvidence article={article} />
            </li>
          );
        })}
      </ol>
      <WithoutTextPins entries={wording.articlesWithoutText} digest={wording.wordingSha256} pins={pins} onPin={onPin} />
      <EuropeAnnexes rows={wording.annexesNotServed} language={wording.language} />
    </section>
  );
}

/** A Luxembourg export's items and exclusions, each dated by its state. */
function ExportEntries({ model }) {
  return (
    <>
      <ol data-export-items={model.items.length}>
        {model.items.map((item) => (
          <li key={item.citation}>
            <Say
              template={COPY.item}
              values={{
                article: <strong>{item.publisherId}</strong>,
                language: item.language,
                from: item.appliesFrom,
                citation: <code>{item.citation}</code>,
                digest: <code>{item.textSha256}</code>,
                source: <code>{item.officialSource}</code>,
              }}
            />
          </li>
        ))}
      </ol>
      {model.excluded.length > 0 ? (
        <ul data-export-excluded={model.excluded.length}>
          {model.excluded.map((entry) => (
            <li key={`${entry.statePermalink}#${entry.publisherId}`}>
              <Say
                template={COPY.excluded}
                values={{
                  article: <strong>{entry.publisherId}</strong>,
                  language: entry.language,
                  from: entry.appliesFrom,
                  reason: entry.reason,
                  citation: <code>{entry.citation}</code>,
                }}
              />
            </li>
          ))}
        </ul>
      ) : null}
    </>
  );
}

/**
 * An EU export's items and exclusions, each dated as its wording is, and the annexes of each wording an article is
 * pinned from, which the file lists as excluded and never holds the text of.
 */
function EuropeExportEntries({ model }) {
  return (
    <>
      <ol data-export-items={model.items.length}>
        {model.items.map((item) => (
          <li key={item.citation}>
            <Say
              template={COPY.europeItem}
              values={{
                article: <strong>{item.publisherId}</strong>,
                language: item.language,
                date: item.wordingDate,
                citation: <code>{item.citation}</code>,
                digest: <code>{item.textSha256}</code>,
                source: <code>{item.officialSource}</code>,
              }}
            />
          </li>
        ))}
      </ol>
      {model.excluded.length > 0 ? (
        <ul data-export-excluded={model.excluded.length}>
          {model.excluded.map((entry) => (
            <li key={entry.citation}>
              <Say
                template={COPY.europeExcluded}
                values={{
                  article: <strong>{entry.publisherId}</strong>,
                  language: entry.language,
                  date: entry.wordingDate,
                  reason: entry.reason,
                  citation: <code>{entry.citation}</code>,
                }}
              />
            </li>
          ))}
        </ul>
      ) : null}
      {model.annexesNotServed.length > 0 ? (
        <div data-export-annexes={model.annexesNotServed.length}>
          {model.annexesNotServed.map((row) => (
            <EuropeAnnexes key={`${row.wordingPermalink} ${row.disposition}`} rows={[row]} language={row.language} />
          ))}
        </div>
      ) : null}
    </>
  );
}

/** One composed export, laid out as the file will carry it, with the buttons that save it. */
export function ExportPreview({ model, onSave }) {
  const europe = model.schema === EUROPE_EXPORT_SCHEMA;
  const total = model.items.length + model.excluded.length;
  // A format that cannot carry this model (a PDF whose text the standard fonts cannot set) is not
  // offered, and the page says why, rather than a button that fails.
  const judged = EXPORT_FORMATS.map((format) => ({ format, reason: format.refusal(model) }));
  const offered = judged.filter((entry) => entry.reason === null).map((entry) => entry.format);
  const refused = judged.filter((entry) => entry.reason !== null);
  return (
    <>
      <p data-export-counts="">
        <Say template={countedEntry(COPY.counts, total)} values={{ count: total, withText: model.items.length, excluded: model.excluded.length }} />
      </p>
      {/* The watermark is the file's own English, and the rights rule the platform's. */}
      <p data-watermark="" lang={ENGLISH_LANG}>{model.watermark}</p>
      {europe ? (
        <>
          {/* EU text has no rights disposition: it is served with Decision 95's acknowledgement and authenticity statement. */}
          <p data-rights="">
            <Say template={COPY.europeRights} values={{ acknowledgement: inEnglish(model.acknowledgement) }} /> {inEnglish(model.rightsRule)}
          </p>
          <p data-authenticity="" lang={ENGLISH_LANG}>{model.authenticity}</p>
        </>
      ) : (
        <p data-rights="">
          <Say template={COPY.rights} values={{ rights: model.rightsDisposition }} /> {inEnglish(model.rightsRule)}
        </p>
      )}
      <p>
        <Say
          template={COPY.snapshot}
          values={{
            date: model.date,
            observedAt: model.observedAt,
            corpus: <code>{model.verifiedBy.corpusSha256}</code>,
            index: <code>{model.verifiedBy.indexSha256}</code>,
            registry: <code>{model.verifiedBy.registrySha256}</code>,
          }}
        />
      </p>
      {europe ? <EuropeExportEntries model={model} /> : <ExportEntries model={model} />}
      <p>
        {offered.map((format) => (
          <button key={format.id} type="button" data-save={format.id} onClick={() => onSave(format)}>
            {format.label}
          </button>
        ))}
      </p>
      {refused.map(({ format, reason }) => (
        <p key={format.id} data-format-refused={format.id}>
          <Said statement={formatRefused(format, reason)} />
        </p>
      ))}
      <details>
        <summary>{COPY.jsonSummary}</summary>
        <pre>{exportJson(model)}</pre>
      </details>
    </>
  );
}

/** The export panel for a reading outcome and its pins: nothing, a sentence, or the export. */
export function ExportPanel({ outcome, pins, onSave }) {
  const panel = exportState(outcome, pins);
  if (panel.state === 'none') return null;
  return (
    <section data-export-state={panel.state}>
      <h2>{COPY.panelHeading}</h2>
      {panel.state === 'composed' ? <ExportPreview model={panel.model} onSave={onSave} /> : <StatusSentence outcome={panel} />}
    </section>
  );
}

/** The reading to pin from, or whatever state the reading is in. */
export function ExportAnswerView({ outcome, pins, onPin }) {
  if (outcome.state !== 'success') return <ReadingAnswerView outcome={outcome} />;
  const { view } = outcome;
  return (
    <section data-answer-state="success">
      <p>
        {view.language === null ? (
          <Say template={COPY.readOn} values={{ date: view.date }} />
        ) : (
          <Say template={COPY.readOnIn} values={{ date: view.date, language: view.language }} />
        )}
      </p>
      {view.publisher === 'eu-eurlex' ? (
        <>
          {/* EU text stands below Decision 95's acknowledgement and authenticity statement, as on the reading screen. */}
          <p data-acknowledgement="" lang={ENGLISH_LANG}>{view.acknowledgement}</p>
          <p data-authenticity="" lang={ENGLISH_LANG}>{view.authenticity}</p>
          {view.wordings.map((wording) => (
            <WordingPins key={wording.wordingSha256} wording={wording} celex={view.celex} pins={pins} onPin={onPin} />
          ))}
        </>
      ) : (
        view.states.map((state) => (
          <StatePins key={state.stateSha256} state={state} workKey={view.workKey} pins={pins} onPin={onPin} />
        ))
      )}
    </section>
  );
}

/**
 * The live screen: the reading form, the reading to pin from, and the export of what is pinned. A new
 * reading clears the pins, since a pin names an article of the reading it was made on.
 */
export function LiveExport({ contract, fetchImpl, save = saveExport }) {
  const [outcome, setOutcome] = useState(IDLE);
  const [pins, setPins] = useState(() => new Set());
  const session = useRef(null);
  if (session.current === null) {
    session.current = createReadingSession({
      contract,
      fetchImpl,
      onOutcome: (next) => {
        setOutcome(next);
        setPins(new Set());
      },
    });
  }

  useEffect(() => () => session.current.cancel(), []);

  const onPin = (key, on) => setPins((current) => {
    const next = new Set(current);
    if (on) next.add(key); else next.delete(key);
    return next;
  });

  return (
    <div>
      <ReadingForm submitLabel={FORM.submit.export} onAsk={(request) => session.current.ask(request)} />
      <LiveAnswer>
        <ExportAnswerView outcome={outcome} pins={pins} onPin={onPin} />
      </LiveAnswer>
      {/* The export is its own live region: pinning changes it, and the reading above does not change. */}
      <LiveAnswer>
        <ExportPanel outcome={outcome} pins={pins} onSave={(format) => {
          const panel = exportState(outcome, pins);
          if (panel.state === 'composed') save(panel.model, format);
        }} />
      </LiveAnswer>
    </div>
  );
}
