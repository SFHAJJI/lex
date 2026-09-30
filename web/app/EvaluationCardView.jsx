// The evaluation card on the Trust and Coverage page (Decision 95, ruling 2).
//
// What the card means is decided in `scripts/evaluation-card.mjs` (`readEvaluationCard`), which
// holds and recomputes its rules; this file lays it out and decides nothing. The card says what it
// was run over first, as the card states it, because every number below is only as good as that
// sentence. Each case set is a table with its caption, every verdict a word, every rate beside its
// Wilson interval and every 1 beside its rule-of-three bound; the shuffled controls are shown like
// any other number; the statistical rows say "not yet labelled"; the negative results are listed.

import { Fragment } from 'react';

const VERDICT_WORDS = Object.freeze({ pass: 'pass', fail: 'fail', not_measured: 'not measured' });
const CONTROL_WORDS = Object.freeze({
  caught_the_shuffle: 'caught the shuffle',
  missed_the_shuffle: 'missed the shuffle',
  not_applicable: 'not applicable',
});

function Summary({ view }) {
  if (view.gatesNotPassing.length === 0 && view.controlsNotCaught.length === 0) {
    return <p data-card-summary="clean">Every machine gate on this card passes, and every shuffled control caught its shuffle.</p>;
  }
  return (
    <p data-card-summary="not-clean">
      {view.gatesNotPassing.length} {view.gatesNotPassing.length === 1 ? 'gate does' : 'gates do'} not pass
      {view.gatesNotPassing.length === 0 ? '' : ` (${view.gatesNotPassing.map((row) => `${row.gate} in ${row.set}, ${row.arm}: ${VERDICT_WORDS[row.verdict]}`).join('; ')})`},
      and {view.controlsNotCaught.length} shuffled {view.controlsNotCaught.length === 1 ? 'control' : 'controls'} did not catch the shuffle
      {view.controlsNotCaught.length === 0 ? '' : ` (${view.controlsNotCaught.map((row) => `${row.control} in ${row.set}, ${row.arm}: ${CONTROL_WORDS[row.verdict]}`).join('; ')})`}.
    </p>
  );
}

function GateRow({ gate }) {
  return (
    <tr data-verdict={gate.verdict}>
      <th scope="row">{gate.gate}</th>
      <td>{VERDICT_WORDS[gate.verdict]}{gate.reason === null ? '' : ` (${gate.reason})`}</td>
      <td>{gate.value === null ? 'none' : gate.value}</td>
      <td>{gate.threshold}</td>
      <td>{gate.n}</td>
      <td>{gate.wilson95 === null ? 'not a rate' : `${gate.wilson95[0]} to ${gate.wilson95[1]}`}</td>
      <td>{gate.ruleOfThree === null ? 'none' : `failure rate below ${gate.ruleOfThree}`}</td>
    </tr>
  );
}

/** The card, laid out from the view `readEvaluationCard` returns. */
export function EvaluationCardView({ view }) {
  return (
    <section data-evaluation-card="">
      <h2>Evaluation card</h2>
      <p data-card-target="">Run over: {view.target}</p>
      <Summary view={view} />
      {view.sets.map((set) => (
        <table key={`${set.set}/${set.arm}`} data-set={set.set}>
          <caption>
            {set.set}, {set.arm}: {set.cases} cases, digest <code>{set.casesSha256}</code>
          </caption>
          <thead>
            <tr>
              <th scope="col">Gate</th>
              <th scope="col">Verdict</th>
              <th scope="col">Value</th>
              <th scope="col">Threshold</th>
              <th scope="col">Cases</th>
              <th scope="col">Wilson 95%</th>
              <th scope="col">Rule of three (95%)</th>
            </tr>
          </thead>
          <tbody>
            {set.gates.map((gate) => <GateRow key={gate.gate} gate={gate} />)}
          </tbody>
        </table>
      ))}
      <h3>Shuffled controls</h3>
      <ul data-controls={view.controls.length}>
        {view.controls.map((control) => (
          <li key={`${control.control}/${control.set}/${control.arm}`} data-control-verdict={control.verdict}>
            {control.control} on {control.set}, {control.arm}: {CONTROL_WORDS[control.verdict]}, {control.reason} (seed{' '}
            {control.seed}, {control.cases} cases, digest <code>{control.casesSha256}</code>)
            {control.note === null ? null : <Fragment>. {control.note}</Fragment>}.
          </li>
        ))}
      </ul>
      <h3>Statistical rows</h3>
      <ul data-statistical-rows={view.statisticalRows.length}>
        {view.statisticalRows.map((row) => (
          <li key={row.dataset}>
            {row.dataset}, {row.name}: not yet labelled. Gates {row.gates}. {row.governedBy}.
          </li>
        ))}
      </ul>
      <h3>Negative results</h3>
      <ul data-negative-results={view.negativeResults.length}>
        {view.negativeResults.map((row) => (
          <li key={row.hypothesis}>
            Hypothesis: {row.hypothesis}. Dataset: {row.dataset}. Result: {row.result}. Decision: {row.decision}. What
            would reverse it: {row.whatWouldReverseIt}.
          </li>
        ))}
      </ul>
    </section>
  );
}
