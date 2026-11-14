// Writes `coverage-record.mjs`, what the Decisions and Evidence pages say Lex holds and misses (the owner's order of
// 2026-11-14, evening: "Every number on screen comes from live data, never typed by hand"), from the site's own `coverage`
// answer, asked of an origin: its totals, its index's build date, and its text yield summed over its publishers and classes
// (the versions the catalogues list, those with text here, the files held back by their licence). Nothing else is computed.
//
//   node scripts/write-coverage-record.mjs https://law.soufien.lu

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const origin = (process.argv[2] ?? '').replace(/\/$/u, '');
if (!/^https?:\/\//u.test(origin)) {
  console.error('usage: node scripts/write-coverage-record.mjs <origin>');
  process.exit(2);
}

const response = await fetch(`${origin}/api/v3/coverage`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ operation_id: 'coverage', parameters: {} }) });
const envelope = await response.json();
const value = envelope?.result?.value;
if (value?.totals === undefined) throw new Error(`${origin} answered no coverage totals`);
const classes = (value.text_yield?.publishers ?? []).flatMap((publisher) => publisher.classes ?? []);
const sum = (pick) => classes.reduce((total, one) => total + (pick(one) ?? 0), 0);
const builtAt = envelope?.context?.freshness?.index_built_at;
const record = {
  origin,
  servingCommit: envelope?.context?.commits?.serving_runtime_source_commit ?? null,
  builtOn: typeof builtAt === 'string' ? builtAt.slice(0, 10) : null,
  works: value.totals.works,
  states: value.totals.states,
  articles: value.totals.articles,
  versionsCatalogued: sum((one) => one.versions),
  versionsWithText: sum((one) => one.versions_with_text),
  files: sum((one) => one.files),
  filesWithheld: sum((one) => (one.files_withheld ?? []).reduce((total, withheld) => total + (withheld.files ?? 0), 0)),
};
const target = fileURLToPath(new URL('./coverage-record.mjs', import.meta.url));
writeFileSync(target, [
  '// Written by scripts/write-coverage-record.mjs from the site\'s own `coverage` answer: its totals, its index\'s build date,',
  '// and its text yield summed over publishers and classes. Do not edit: run the script again.',
  '',
  `export const COVERAGE_RECORD = Object.freeze(${JSON.stringify(record, null, 2)});`,
  '',
].join('\n'));
console.log(`wrote ${target}`);
console.log(JSON.stringify(record));
