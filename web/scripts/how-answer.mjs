// How it works, first screen (the owner's brief of 2026-11-15, V5How): one real answer followed through the six stages,
// every value read from the recorded answer (`how-answer-record.mjs`), never typed: whether anything was masked (the
// question the trace names against the question sent), the language and the date the code read, the advice cues it found,
// the plan the model returned and the span it points at, whether the plan ran, each operation run with its parameters,
// and the gates' counts. Data and pure functions only; `ReferencePages.jsx` lays it out with the chrome table's words.

import { actTitleOf, articleNumberOf } from './law-page.mjs';

/** The model a planner identity names (`azure-openai/gpt-4.1-mini/prompt-…` is `gpt-4.1-mini`), or null. */
export function plannerModel(plannerId) {
  const match = /^[^/]+\/([^/]+)\//u.exec(String(plannerId ?? ''));
  return match === null ? null : match[1];
}

/** A value on one line, JSON with a space inside braces and after each comma: `{ "span": [12, 58] }`. */
export function inlineJson(value) {
  if (Array.isArray(value)) return `[${value.map(inlineJson).join(', ')}]`;
  if (value !== null && typeof value === 'object') {
    const members = Object.entries(value);
    return members.length === 0 ? '{}' : `{ ${members.map(([key, member]) => `${JSON.stringify(key)}: ${inlineJson(member)}`).join(', ')} }`;
  }
  return JSON.stringify(value);
}

/**
 * A value as compact JSON lines no wider than `width` where they can be: an object's members are packed on a line while
 * they fit, the next lines aligned under the first member; an array too wide for its line opens it and sets its items on
 * the lines below, indented under the line it opens on. The value is printed whole, members in the order the answer gives
 * them; only the white space is chosen.
 */
export function compactJson(value, width = 76) {
  return layout(value, 0, 0, width).join('\n');
}

/** The lines of `value` printed from `column`, its member's name at `name`; the first line is the rest of that line. */
function layout(value, column, name, width) {
  const one = inlineJson(value);
  if (value === null || typeof value !== 'object' || column + one.length <= width) return [one];
  const isArray = Array.isArray(value);
  const items = isArray ? value.map((member) => ['', member]) : Object.entries(value).map(([key, member]) => [`${JSON.stringify(key)}: `, member]);
  if (items.length === 0) return [one];
  // An array's items go on the lines below its opening, indented under its member's name; an object's members follow its brace.
  const inner = isArray ? name + 2 : column + 2;
  const lines = [];
  let line = isArray ? '[' : '{ ';
  let start = column;
  let placed = !isArray;
  let fresh = !isArray;
  const flush = () => {
    lines.push(line);
    line = ' '.repeat(inner);
    start = 0;
    placed = false;
    fresh = false;
  };
  if (isArray) flush();
  items.forEach(([prefix, member], index) => {
    const last = index === items.length - 1;
    const tail = last ? (isArray ? ' ]' : ' }') : ',';
    const piece = `${prefix}${inlineJson(member)}${tail}`;
    const gap = placed && !fresh ? 1 : 0;
    if (start + line.length + gap + piece.length <= width) {
      line += `${gap === 1 ? ' ' : ''}${piece}`;
      placed = true;
      fresh = false;
      return;
    }
    if (placed && !fresh) flush();
    const sub = layout(member, start + line.length + prefix.length, start + line.length, width);
    line += `${prefix}${sub[0]}`;
    for (const next of sub.slice(1)) {
      lines.push(line);
      line = next;
      start = 0;
    }
    line += tail;
    placed = true;
    fresh = false;
    if (!last) flush();
  });
  lines.push(line);
  return lines;
}

/**
 * The six stages of one recorded answer (`record`: `{ request, envelope, act }`), as the page says them:
 *
 *   masked     the question the model was given, when it differs from the question sent; null when nothing was masked
 *   language   the answer's language, as the code read it (`fr`)
 *   date       `{ today, date }`: the date the code read (`today` when the question gives none)
 *   cues       how many advice cues the code found
 *   model      the planner's model; `span` the characters of the question the search step points at, `[from, to]`
 *   plan       the plan's steps and follow-ups as returned, as compact JSON
 *   accepted   whether the plan ran (a refused plan runs no step)
 *   runs       each operation run, in order: `{ kind: 'search', query }`, `{ kind: 'single' }` before a follow-up the search
 *              allowed, `{ kind: 'read', article, date }`, or `{ kind: 'other', operation }`
 *   gates      `{ quotes, outside, notes }`: quotations, strings outside the templates, disclosures shown with the quotes
 *   answer     `{ article, shortened, identifier, anchor, date }`: the article quoted and where the text page opens it
 *   act        the act's own title and its language (`fra`)
 */
export function howAnswerStages(record) {
  const value = record?.envelope?.result?.value;
  const trace = value?.trace;
  if (value === undefined || value === null || trace === undefined || trace === null) throw new Error('the recorded answer carries no trace');
  const asked = record.request.parameters.question;
  const rendered = value.plan?.rendered ?? [];
  const dated = rendered.find((claim) => /^plan\.date_/u.test(claim.template_id ?? '')) ?? null;
  const dateFact = dated?.facts?.find((fact) => fact.kind === 'calendar_date')?.value ?? trace.today;
  const search = trace.plan.steps.find((step) => step.operation_id === 'search') ?? null;
  const quote = value.parts.flatMap((part) => part.blocks).find((block) => block.kind === 'quote') ?? null;
  const runs = [];
  for (const step of trace.steps.filter((one) => one.fired)) {
    if (step.operation_id === 'search') runs.push({ kind: 'search', query: step.parameters.query });
    else {
      const followUp = trace.plan.follow_ups.find((one) => one.step_id === step.step_id) ?? null;
      if (followUp?.condition === 'on_single_article') runs.push({ kind: 'single' });
      if (step.operation_id === 'evidence_bundle' && typeof step.parameters.article === 'string') {
        runs.push({ kind: 'read', article: articleNumberOf(step.parameters.article), date: step.parameters.date });
      } else {
        runs.push({ kind: 'other', operation: step.operation_id });
      }
    }
  }
  return Object.freeze({
    question: asked,
    masked: trace.question === asked ? null : trace.question,
    language: trace.answer_locale,
    date: Object.freeze({ today: dated?.template_id === 'plan.date_today', date: dateFact }),
    cues: value.parts.reduce((sum, part) => sum + (part.cues?.length ?? 0), 0),
    model: plannerModel(trace.planner_id),
    span: search?.arguments?.query?.span ?? null,
    plan: compactJson({ steps: trace.plan.steps, follow_ups: trace.plan.follow_ups }),
    accepted: trace.steps.some((step) => step.fired),
    runs: Object.freeze(runs),
    gates: Object.freeze({ quotes: value.gates.quotes, outside: value.gates.non_template_strings, notes: value.gates.disclosures }),
    answer: quote === null ? null : Object.freeze({
      article: articleNumberOf(quote.anchor),
      shortened: quote.shortened === true,
      identifier: quote.work,
      anchor: quote.anchor,
      date: quote.from,
    }),
    act: Object.freeze({ title: actTitleOf(record.act.title).replace(/\.$/u, ''), language: record.act.language }),
  });
}
