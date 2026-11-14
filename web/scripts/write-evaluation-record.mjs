// Writes `evaluation-record.mjs`, the measured results the Decisions and Evidence pages and the answer's "show the work"
// print (the owner's order of 2026-11-14, evening: "Every number on screen comes from live data, never typed by hand"), from
// two lex-eval runs against the site: the test questions' run (the release gate's) and the held-out questions' run. Each
// figure is the run's own `run.json` metric, copied as it is; nothing is computed here but the run's date from its folder.
//
//   node scripts/write-evaluation-record.mjs <dev run dir> <held-out run dir>

import { readFileSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const [devDir, heldoutDir] = process.argv.slice(2);
if (devDir === undefined || heldoutDir === undefined) {
  console.error('usage: node scripts/write-evaluation-record.mjs <dev run dir> <held-out run dir>');
  process.exit(2);
}

function runOf(dir) {
  const run = JSON.parse(readFileSync(join(dir, 'run.json'), 'utf8'));
  const metric = (name) => {
    const value = run.metrics[name];
    if (typeof value !== 'number') throw new Error(`${dir}: run.json has no metric ${name}`);
    return value;
  };
  const day = /^(\d{4})(\d{2})(\d{2})-/u.exec(basename(dir));
  return {
    run: basename(dir),
    runId: run.run_id,
    date: day === null ? null : `${day[1]}-${day[2]}-${day[3]}`,
    servingCommit: run.facts.serving_commit,
    plannerId: run.facts.planner_id,
    modelDeployment: run.facts.model_deployment,
    cases: metric('cases'),
    rightArticle: metric('outcome.right_article'),
    wrongArticle: metric('outcome.wrong_article'),
    notFound: metric('outcome.not_found'),
    wronglyRefused: metric('outcome.wrongly_refused'),
    rightBehaviour: metric('outcome.right_behaviour'),
    wrongBehaviour: metric('outcome.wrong_behaviour'),
    quotes: metric('quotes'),
    quoteVerifiedRate: metric('quote_verified_rate'),
    p50: metric('latency_p50_s'),
    p95: metric('latency_p95_s'),
    costEur: metric('cost_eur_per_question_estimate'),
  };
}

/** A question's language, for its `lang`: German or English by its opening words, else French (the sets hold only these). */
const languageOf = (question) => (/^Was /u.test(question) ? 'de' : /^(What|Which|When|Within|Under|My|SYSTEM|How|Does|Is|Can)\b/u.test(question) ? 'en' : 'fr');

/** Every question of a run, in its order: its id, its words (the cases file's), its language and the run's outcome. */
function questionsOf(dir, casesFile) {
  const cases = new Map(readFileSync(casesFile, 'utf8').split('\n').filter((line) => line.trim() !== '').map((line) => JSON.parse(line)).map((one) => [one.id, one.question]));
  return readFileSync(join(dir, 'results.jsonl'), 'utf8').split('\n').filter((line) => line.trim() !== '').map((line) => JSON.parse(line)).map((result) => {
    const question = cases.get(result.id);
    if (typeof question !== 'string') throw new Error(`${dir}: ${result.id} is in no case of ${casesFile}`);
    return { id: result.id, question, language: languageOf(question), outcome: result.outcome };
  });
}

const casesFile = process.argv[4] ?? 'C:/lex-v3/lex-eval/cases/lex-assistant-v2.jsonl';
const record = { dev: { ...runOf(devDir), questions: questionsOf(devDir, casesFile) }, heldout: runOf(heldoutDir) };
const target = fileURLToPath(new URL('./evaluation-record.mjs', import.meta.url));
writeFileSync(target, [
  '// Written by scripts/write-evaluation-record.mjs from two lex-eval runs\' run.json (the test questions\', the held-out',
  '// questions\'): every figure is a run\'s own metric, copied as it is. Do not edit: run the script again.',
  '',
  `export const EVALUATION_RECORD = Object.freeze(${JSON.stringify(record, null, 2)});`,
  '',
].join('\n'));
console.log(`wrote ${target}`);
