// The evaluation card, read: what the Trust and Coverage page shows of the machine gates.
//
// Owner ruling 2 of 2026-09-30 (Decision 95): the evaluation card is served on the Trust and Coverage
// page and beside the release assets. The card is rendered by the platform (`EvaluationCard.Render`,
// held as `schemas/v3-platform/evaluation-card.json`, `V3_RENDER_EVALUATION_CARD=1`); this reader holds
// every rule `36-ideal-evaluation.md` sections 5 and 6 and Decision 92 state about it, and recomputes
// what can be recomputed, so the page never prints a number the card's own rules contradict:
//  - every gate's verdict is in the closed vocabulary; exactly a verdict of not measured carries a
//    reason from its vocabulary and no value; a pass is at or above its threshold, a fail below it;
//  - every measured rate carries its Wilson 95 percent interval, recomputed here from the value and
//    the stratum; anchor nDCG, a graded mean, carries none;
//  - every value of exactly 1 carries the rule-of-three bound on its failure rate, recomputed here;
//  - every case set has its shuffled control, in order, with its verdict in the closed vocabulary;
//  - every statistical row is `not_yet_labelled` (the launch contract's words, and Decision 92: the
//    purpose-built cases prove the harness and never gate a statistical claim);
//  - the card says what it was run over (`target`), which the page prints as it is.
// It renders nothing itself.

export const CARD_SCHEMA = 'lex-v3-evaluation-card/1';
/** Where the card is served as JSON for machines, beside the page that renders it for people (36 s6). */
export const CARD_ROUTE = '/evaluation-card.json';
export const GATE_VERDICTS = Object.freeze(['pass', 'fail', 'not_measured']);
export const NOT_MEASURED_REASONS = Object.freeze(['no_relevant_judgment', 'no_measurable_query', 'stratum_below_floor']);
export const CONTROL_VERDICTS = Object.freeze(['caught_the_shuffle', 'missed_the_shuffle', 'not_applicable']);
export const NOT_YET_LABELLED = 'not_yet_labelled';

/** The gates that are graded means rather than shares of cases, so they carry no binomial interval. */
const GRADED_GATES = new Set(['anchor_ndcg_at_10']);

/**
 * The gates each case set must carry, in order, and the shuffled control that set runs
 * (`EvaluationCard.Temporal`, `Verdict` and `Retrieval`; `EvaluationGateNames`, `ShuffledControlNames`). A set
 * missing a gate would otherwise read as clean with nothing measured (review of #792).
 */
export const SET_GATES = Object.freeze({
  temporal: Object.freeze(['temporal_exactness']),
  refusal: Object.freeze(['verdict_exact_match']),
  retrieval: Object.freeze(['anchor_ndcg_at_10', 'no_hit_accuracy', 'resolver_exactness']),
});
export const SET_CONTROLS = Object.freeze({ temporal: 'date_shuffle', refusal: 'verdict_shuffle', retrieval: 'qrels_shuffle' });
const DIGEST = /^[0-9a-f]{64}$/;
const Z95 = 1.959963984540054;

function round4(value) {
  // As the platform rounds (Math.Round to four places, midpoint to even does not arise here).
  return Math.round(value * 10_000) / 10_000;
}

/** The Wilson score interval at 95 percent, rounded to four places, as the platform computes it. */
export function wilson95(successes, n) {
  const p = successes / n;
  const z2 = Z95 * Z95;
  const denominator = 1 + z2 / n;
  const centre = (p + z2 / (2 * n)) / denominator;
  const half = (Z95 * Math.sqrt((p * (1 - p)) / n + z2 / (4 * n * n))) / denominator;
  return [round4(Math.max(0, centre - half)), round4(Math.min(1, centre + half))];
}

/** The rule-of-three 95 percent upper bound on a failure rate with no failure in `n` cases. */
export function ruleOfThree(n) {
  return round4(Math.min(1, 3 / n));
}

function requireOwn(object, key, where) {
  if (object === null || typeof object !== 'object' || !Object.hasOwn(object, key)) {
    throw new Error(`${where} does not carry ${key}`);
  }
  return object[key];
}

function requireText(value, where) {
  if (typeof value !== 'string' || value.trim().length === 0) throw new Error(`${where} is not text`);
  return value;
}

function requireCount(value, where, least = 0) {
  if (!Number.isInteger(value) || value < least) throw new Error(`${where} is not a whole number of at least ${least}`);
  return value;
}

function requireList(value, where, least = 0) {
  if (!Array.isArray(value) || value.length < least) throw new Error(`${where} is not a list of at least ${least}`);
  return value;
}

function requireOneOf(value, vocabulary, where) {
  if (!vocabulary.includes(value)) throw new Error(`${where} ${JSON.stringify(value)} is not one of ${vocabulary.join(', ')}`);
  return value;
}

function sameInterval(left, right) {
  return Array.isArray(left) && left.length === 2 && left[0] === right[0] && left[1] === right[1];
}

function readGate(gate, where, caseCount) {
  const name = requireText(requireOwn(gate, 'gate', where), `${where} gate`);
  const at = `${where} (${name})`;
  const verdict = requireOneOf(requireOwn(gate, 'verdict', at), GATE_VERDICTS, `${at}'s verdict`);
  const n = requireCount(requireOwn(gate, 'n', at), `${at} n`, 1);
  if (n > caseCount) throw new Error(`${at} counts ${n} cases in its stratum, and its set holds ${caseCount}`);
  const threshold = requireOwn(gate, 'threshold', at);
  if (typeof threshold !== 'number' || threshold < 0 || threshold > 1) throw new Error(`${at}'s threshold is not a number from 0 to 1`);
  const value = requireOwn(gate, 'value', at);
  const reason = Object.hasOwn(gate, 'not_measured_reason') ? gate.not_measured_reason : null;

  if (verdict === 'not_measured') {
    if (value !== null) throw new Error(`${at} is not measured, so it carries no value`);
    requireOneOf(reason, NOT_MEASURED_REASONS, `${at}'s not_measured_reason`);
    if (Object.hasOwn(gate, 'wilson_95') || Object.hasOwn(gate, 'rule_of_three_failure_upper_95')) {
      throw new Error(`${at} is not measured, so it carries no interval and no bound`);
    }
    return Object.freeze({ gate: name, verdict, value: null, reason, threshold, n, wilson95: null, ruleOfThree: null });
  }

  if (reason !== null) throw new Error(`${at} is ${verdict}, and only a gate not measured carries a reason`);
  if (typeof value !== 'number' || value < 0 || value > 1) throw new Error(`${at}'s value is not a number from 0 to 1`);
  if ((verdict === 'pass') !== (value >= threshold)) {
    throw new Error(`${at} says ${verdict} for ${value} against its threshold ${threshold}`);
  }

  let interval = null;
  if (GRADED_GATES.has(name)) {
    if (Object.hasOwn(gate, 'wilson_95')) throw new Error(`${at} is a graded mean, not a rate, so it carries no Wilson interval`);
  } else {
    const successes = Math.round(value * n);
    if (Math.abs(successes / n - value) > 0.00005) throw new Error(`${at}'s value ${value} is not a share of its ${n} cases`);
    interval = wilson95(successes, n);
    if (!sameInterval(requireOwn(gate, 'wilson_95', at), interval)) {
      throw new Error(`${at}'s Wilson interval is not ${interval.join(' to ')}, the interval of ${successes} of ${n}`);
    }
  }

  let bound = null;
  if (value === 1) {
    bound = ruleOfThree(n);
    if (requireOwn(gate, 'rule_of_three_failure_upper_95', at) !== bound) {
      throw new Error(`${at} is 1 over ${n} cases, so its rule-of-three bound is ${bound}`);
    }
  } else if (Object.hasOwn(gate, 'rule_of_three_failure_upper_95')) {
    throw new Error(`${at} is below 1, so it carries no rule-of-three bound`);
  }

  return Object.freeze({ gate: name, verdict, value, reason: null, threshold, n, wilson95: interval, ruleOfThree: bound });
}

function readSet(set, index) {
  const where = `machine_gates[${index}]`;
  const caseCount = requireCount(requireOwn(set, 'cases', where), `${where} cases`, 1);
  const casesSha256 = requireOwn(set, 'cases_sha256', where);
  if (!DIGEST.test(casesSha256)) throw new Error(`${where} cases_sha256 is not a SHA-256 digest`);
  const name = requireOneOf(requireOwn(set, 'set', where), Object.keys(SET_GATES), `${where}'s set`);
  const gates = Object.freeze(requireList(requireOwn(set, 'gates', where), `${where} gates`, 1).map((gate, gateIndex) => readGate(gate, `${where} gates[${gateIndex}]`, caseCount)));
  const names = gates.map((gate) => gate.gate);
  if (names.join() !== SET_GATES[name].join()) {
    throw new Error(`${where} is a ${name} set, so its gates are ${SET_GATES[name].join(', ')}, and it carries ${names.join(', ')}`);
  }
  return Object.freeze({
    set: name,
    arm: requireText(requireOwn(set, 'arm', where), `${where} arm`),
    cases: caseCount,
    casesSha256,
    gates,
  });
}

function readControl(control, index, set) {
  const where = `shuffled_controls[${index}]`;
  const forSet = requireText(requireOwn(control, 'set', where), `${where} set`);
  const arm = requireText(requireOwn(control, 'arm', where), `${where} arm`);
  if (forSet !== set.set || arm !== set.arm) {
    throw new Error(`${where} is the control of ${forSet}, ${arm}, and the set in its place is ${set.set}, ${set.arm}`);
  }
  const casesSha256 = requireOwn(control, 'cases_sha256', where);
  if (!DIGEST.test(casesSha256)) throw new Error(`${where} cases_sha256 is not a SHA-256 digest`);
  const cases = requireCount(requireOwn(control, 'cases', where), `${where} cases`, 1);
  if (cases > set.cases) throw new Error(`${where} ran over ${cases} cases, and its set holds ${set.cases}`);
  if (cases < set.cases && !Object.hasOwn(control, 'note')) {
    throw new Error(`${where} ran over fewer cases than its set, and does not say why`);
  }
  return Object.freeze({
    control: requireOneOf(requireOwn(control, 'control', where), [SET_CONTROLS[set.set]], `${where}'s control for a ${set.set} set`),
    set: forSet,
    arm,
    verdict: requireOneOf(requireOwn(control, 'verdict', where), CONTROL_VERDICTS, `${where}'s verdict`),
    reason: requireText(requireOwn(control, 'reason', where), `${where} reason`),
    seed: requireCount(requireOwn(control, 'seed', where), `${where} seed`),
    cases,
    casesSha256,
    note: Object.hasOwn(control, 'note') ? requireText(control.note, `${where} note`) : null,
  });
}

/**
 * The card as the page shows it. Throws where the card breaks one of its own rules, so a card the
 * page cannot vouch for is never printed as if it could.
 */
export function readEvaluationCard(card) {
  if (requireOwn(card, 'schema', 'the card') !== CARD_SCHEMA) throw new Error(`the card is not ${CARD_SCHEMA}`);
  const sets = Object.freeze(requireList(requireOwn(card, 'machine_gates', 'the card'), 'machine_gates', 1).map(readSet));
  const controlsValue = requireList(requireOwn(card, 'shuffled_controls', 'the card'), 'shuffled_controls');
  if (controlsValue.length !== sets.length) {
    throw new Error(`the card has ${controlsValue.length} shuffled controls for ${sets.length} case sets, and each set has one`);
  }
  const controls = Object.freeze(controlsValue.map((control, index) => readControl(control, index, sets[index])));
  const statisticalRows = Object.freeze(requireList(requireOwn(card, 'statistical_rows', 'the card'), 'statistical_rows', 1).map((row, index) => {
    const where = `statistical_rows[${index}]`;
    if (requireOwn(row, 'status', where) !== NOT_YET_LABELLED) {
      throw new Error(`${where} says ${JSON.stringify(row.status)}; a statistical row is ${NOT_YET_LABELLED} until its dataset is labelled (Decision 92)`);
    }
    return Object.freeze({
      dataset: requireText(requireOwn(row, 'dataset', where), `${where} dataset`),
      name: requireText(requireOwn(row, 'name', where), `${where} name`),
      gates: requireText(requireOwn(row, 'gates', where), `${where} gates`),
      status: NOT_YET_LABELLED,
      governedBy: requireText(requireOwn(row, 'governed_by', where), `${where} governed_by`),
    });
  }));
  const negativeResults = Object.freeze(requireList(requireOwn(card, 'negative_results', 'the card'), 'negative_results').map((row, index) => {
    const where = `negative_results[${index}]`;
    return Object.freeze(Object.fromEntries(['hypothesis', 'dataset', 'result', 'decision', 'what_would_reverse_it'].map((key) => [
      key === 'what_would_reverse_it' ? 'whatWouldReverseIt' : key,
      requireText(requireOwn(row, key, where), `${where} ${key}`),
    ])));
  }));
  const failing = sets.flatMap((set) => set.gates.filter((gate) => gate.verdict !== 'pass').map((gate) => ({ set: set.set, arm: set.arm, gate: gate.gate, verdict: gate.verdict })));
  const missed = controls.filter((control) => control.verdict !== 'caught_the_shuffle');
  return Object.freeze({
    schema: CARD_SCHEMA,
    target: requireText(requireOwn(card, 'target', 'the card'), 'target'),
    sets,
    controls,
    statisticalRows,
    negativeResults,
    gatesNotPassing: Object.freeze(failing),
    controlsNotCaught: Object.freeze(missed),
  });
}
