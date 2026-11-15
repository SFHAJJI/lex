// Writes `registry-record.mjs`, the MLflow model registry of the assistant as the "How it works" page shows it (the owner's
// order of 2026-11-15: MLflow visible in the app), from the export the evaluation harness writes with MLflow's own client
// (`python -m lex_eval.export_registry OUT.json`, github.com/SFHAJJI/lex-eval): the registered model, its aliases, every
// version with its tags, its run's parameters, metrics and logged artifacts. Copied as it is; nothing is computed here.
//
//   node scripts/write-registry-record.mjs <registry export .json>

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const [source] = process.argv.slice(2);
if (source === undefined) {
  console.error('usage: node scripts/write-registry-record.mjs <registry export .json>');
  process.exit(2);
}
const registry = JSON.parse(readFileSync(source, 'utf8'));
if (registry.model !== 'lex-assistant' || !Array.isArray(registry.versions) || registry.versions.length === 0) {
  throw new Error(`${source} is no export of the lex-assistant registry`);
}
const target = fileURLToPath(new URL('./registry-record.mjs', import.meta.url));
writeFileSync(target, [
  '// Written by scripts/write-registry-record.mjs from the MLflow registry export of the evaluation harness (MLflow\'s own',
  '// client: the registered model lex-assistant, its aliases, every version and its run). Do not edit: run the script again.',
  '',
  `export const REGISTRY_RECORD = Object.freeze(${JSON.stringify(registry, null, 2)});`,
  '',
].join('\n'));
console.log(`wrote ${target}`);
