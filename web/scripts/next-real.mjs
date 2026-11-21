// Draws answers the engine really gave (a JSON list of answers, as `experiments\funnel\ask.py --json` writes them) with the
// dock, in French and English, reads each rendering against its answer (`contractFaults` or, for the earlier pipeline,
// `dockFaults`), and writes each as HTML and as plain text beside the file. No browser.
//
//   node scripts\next-real.mjs <answers.json> [out folder]

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

import { contractFaults, dockFaults, loadRenderers } from './next-check.mjs';
import { isFullContract, legacyAnswer } from './next-contract.mjs';

/** A rendering as plain text, one block a line, to read without a browser. */
export function asText(html) {
  return html.replace(/<(p|li|tr|h1|h2|h3|h4|summary|section|article|div|table)[^>]*>/gu, '\n').replace(/<\/t[hd]>/gu, ' | ').replace(/<[^>]+>/gu, '')
    .replace(/&#x27;/gu, '\'').replace(/&quot;/gu, '"').replace(/&amp;/gu, '&').replace(/&lt;/gu, '<').replace(/&gt;/gu, '>')
    .split('\n').map((line) => line.trim()).filter((line) => line.length > 0).join('\n');
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [source, folder] = process.argv.slice(2);
  const answers = JSON.parse(await readFile(source, 'utf8'));
  const out = new URL(`${pathToFileURL(folder ?? `${source}.renders`).href}/`);
  await mkdir(out, { recursive: true });
  const renderers = await loadRenderers('real');
  let faulty = 0;
  for (const [at, answer] of (Array.isArray(answers) ? answers : [answers]).entries()) {
    for (const locale of ['fr', 'en']) {
      const html = renderers[locale].renderDockAnswer(answer, { locale, today: '2026-11-21' });
      const faults = isFullContract(answer) ? contractFaults(html, answer, locale) : dockFaults(html, legacyAnswer(answer), locale);
      faulty += faults.length;
      await writeFile(new URL(`answer-${at + 1}.${locale}.html`, out), `${html}\n`, 'utf8');
      await writeFile(new URL(`answer-${at + 1}.${locale}.txt`, out), `${asText(html)}\n`, 'utf8');
      console.log(`${faults.length === 0 ? 'ok  ' : 'FAIL'} ${at + 1} ${locale} ${answer.state ?? answer.outcome}${faults.length === 0 ? '' : `\n     ${faults.join('\n     ')}`}`);
    }
  }
  console.log(`${faulty} fault(s); renderings in ${out.pathname}`);
  process.exitCode = faulty === 0 ? 0 : 1;
}
