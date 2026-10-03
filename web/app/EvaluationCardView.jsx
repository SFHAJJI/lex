// The evaluation card on the Trust and Coverage page (Decision 95, ruling 2).
//
// What the card means is decided in `scripts/evaluation-card.mjs` (`readEvaluationCard`), which
// holds and recomputes its rules; this file lays it out and decides nothing. The card says what it
// was run over first, as the card states it, because every number below is only as good as that
// sentence. Each case set is a table with its caption, every verdict a word, every rate beside its
// Wilson interval and every 1 beside its rule-of-three bound; the shuffled controls are shown like
// any other number; the statistical rows say "not yet labelled"; the negative results are listed.
// The card's own sentences (its target, reasons, notes, rows and results) are the platform's English,
// marked English on a page in another language (`inEnglish`).

import { Say, inEnglish } from './LiveAnswer.jsx';
import { fillCounted, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { CARD_ROUTE } from '../scripts/evaluation-card.mjs';

/** The card's words, from the interface copy table. */
const CARD = liveChrome().card;
const COMMON = liveChrome().common;

function Summary({ view }) {
  if (view.gatesNotPassing.length === 0 && view.controlsNotCaught.length === 0) {
    return <p data-card-summary="clean">{CARD.clean}</p>;
  }
  const gates = fillCounted(CARD.gatesNotPassing, view.gatesNotPassing.length);
  const controls = fillCounted(CARD.controlsNotCaught, view.controlsNotCaught.length);
  const listed = (summary, rows, template, name) => (rows.length === 0 ? summary : fillText(CARD.listed, {
    summary,
    list: rows.map((row) => fillText(template, { [name]: row[name], set: row.set, arm: row.arm, verdict: name === 'gate' ? CARD.verdict[row.verdict] : CARD.controlVerdict[row.verdict] })).join(COMMON.listSeparator),
  }));
  return (
    <p data-card-summary="not-clean">
      <Say
        template={CARD.notClean}
        values={{
          gates: listed(gates, view.gatesNotPassing, CARD.gateListed, 'gate'),
          controls: listed(controls, view.controlsNotCaught, CARD.controlListed, 'control'),
        }}
      />
    </p>
  );
}

function GateRow({ gate }) {
  const verdict = CARD.verdict[gate.verdict];
  return (
    <tr data-verdict={gate.verdict}>
      <th scope="row">{gate.gate}</th>
      <td>{gate.reason === null ? verdict : <Say template={CARD.verdictReason} values={{ verdict, reason: inEnglish(gate.reason) }} />}</td>
      <td>{gate.value === null ? CARD.none : gate.value}</td>
      <td>{gate.threshold}</td>
      <td>{gate.n}</td>
      <td>{gate.wilson95 === null ? CARD.notARate : fillText(CARD.interval, { low: gate.wilson95[0], high: gate.wilson95[1] })}</td>
      <td>{gate.ruleOfThree === null ? CARD.none : fillText(CARD.ruleOfThree, { bound: gate.ruleOfThree })}</td>
    </tr>
  );
}

/** The card, laid out from the view `readEvaluationCard` returns. */
export function EvaluationCardView({ view }) {
  return (
    <section data-evaluation-card="">
      <h2>{CARD.heading}</h2>
      <p data-card-target=""><Say template={CARD.target} values={{ target: inEnglish(view.target) }} /></p>
      <p data-card-json=""><a href={CARD_ROUTE}>{CARD.machineReadable}</a></p>
      <Summary view={view} />
      {view.sets.map((set) => (
        <table key={`${set.set}/${set.arm}`} data-set={set.set}>
          <caption>
            <Say template={CARD.caption} values={{ set: set.set, arm: set.arm, cases: set.cases, digest: <code>{set.casesSha256}</code> }} />
          </caption>
          <thead>
            <tr>
              <th scope="col">{CARD.columns.gate}</th>
              <th scope="col">{CARD.columns.verdict}</th>
              <th scope="col">{CARD.columns.value}</th>
              <th scope="col">{CARD.columns.threshold}</th>
              <th scope="col">{CARD.columns.cases}</th>
              <th scope="col">{CARD.columns.wilson}</th>
              <th scope="col">{CARD.columns.ruleOfThree}</th>
            </tr>
          </thead>
          <tbody>
            {set.gates.map((gate) => <GateRow key={gate.gate} gate={gate} />)}
          </tbody>
        </table>
      ))}
      <h3>{CARD.controlsHeading}</h3>
      <ul data-controls={view.controls.length}>
        {view.controls.map((control) => (
          <li key={`${control.control}/${control.set}/${control.arm}`} data-control-verdict={control.verdict}>
            <Say
              template={control.note === null ? CARD.control : CARD.controlNote}
              values={{
                control: control.control,
                set: control.set,
                arm: control.arm,
                verdict: CARD.controlVerdict[control.verdict],
                reason: inEnglish(control.reason),
                seed: control.seed,
                cases: control.cases,
                digest: <code>{control.casesSha256}</code>,
                ...(control.note === null ? {} : { note: inEnglish(control.note) }),
              }}
            />
          </li>
        ))}
      </ul>
      <h3>{CARD.statisticalHeading}</h3>
      <ul data-statistical-rows={view.statisticalRows.length}>
        {view.statisticalRows.map((row) => (
          <li key={row.dataset}>
            <Say template={CARD.statistical} values={{ dataset: row.dataset, name: inEnglish(row.name), gates: inEnglish(row.gates), governedBy: inEnglish(row.governedBy) }} />
          </li>
        ))}
      </ul>
      <h3>{CARD.negativeHeading}</h3>
      <ul data-negative-results={view.negativeResults.length}>
        {view.negativeResults.map((row) => (
          <li key={row.hypothesis}>
            <Say
              template={CARD.negative}
              values={{
                hypothesis: inEnglish(row.hypothesis),
                dataset: inEnglish(row.dataset),
                result: inEnglish(row.result),
                decision: inEnglish(row.decision),
                reverse: inEnglish(row.whatWouldReverseIt),
              }}
            />
          </li>
        ))}
      </ul>
    </section>
  );
}
