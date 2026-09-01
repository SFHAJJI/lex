// Mutate every guard in the React shell and report which ones no test kills.
//
// Batched deliberately. Running one mutation, waiting, reading, and running the next is most of
// where verification wall-clock goes, and none of that waiting adds evidence. This applies each
// mutation, recompiles, runs the suite, restores, and prints one table.
//
// A control mutation is included on purpose. If the harness reports a survivor for something
// that obviously changes behaviour, the run is invalid rather than informative, which is a
// distinction this project has got wrong before: a mutation that breaks compilation reads as a
// survivor if nobody checks that the suite still ran the expected number of tests.

import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';

const TARGET = 'app/Document.jsx';
const RENDERER = 'app/render-document.mjs';
const DOSSIER = 'app/Dossier.jsx';
const REFUSAL = 'app/RefusalCard.jsx';

/** @type {{name: string, file: string, from: string, to: string}[]} */
const MUTATIONS = [
  {
    name: 'locale is not checked against the reviewed set',
    file: TARGET,
    from: 'if (!CHROME_LOCALES.includes(locale)) {',
    to: 'if (false) {',
  },
  {
    name: 'copy locale is not checked against the reviewed set',
    file: TARGET,
    from: 'if (!CHROME_LOCALES.includes(copyLocale)) {',
    to: 'if (false) {',
  },
  {
    name: 'a page may be labelled one locale while its copy is another',
    file: TARGET,
    from: 'if (locale !== copyLocale) {',
    to: 'if (false) {',
  },
  {
    name: 'a page need not say what it is',
    file: TARGET,
    from: "if (typeof state !== 'string' || state.length === 0) {",
    to: 'if (false) {',
  },
  {
    name: 'a page need not carry a title',
    file: TARGET,
    from: "if (typeof title !== 'string' || title.length === 0) {",
    to: 'if (false) {',
  },
  {
    name: 'the stylesheet is resolved against the page path',
    file: TARGET,
    from: 'href="/styles.css"',
    to: 'href="./styles.css"',
  },
  {
    name: 'the icon is resolved against the page path',
    file: TARGET,
    from: 'href="/favicon.svg"',
    to: 'href="./favicon.svg"',
  },
  {
    name: 'the shell attributes ride on the page even when no shell is named',
    file: TARGET,
    from: "shell === null ? {} : { 'data-shell': shell, 'data-density': density ?? '' };",
    to: "{ 'data-shell': shell ?? '', 'data-density': density ?? '' };",
  },
  {
    name: 'the synthetic banner is dropped',
    file: TARGET,
    from: '<SyntheticBanner />',
    to: '<></>',
  },
  {
    name: 'the preview state is hardcoded',
    file: TARGET,
    from: 'data-preview-state={state}',
    to: 'data-preview-state="proof"',
  },
  {
    name: 'the language tag is hardcoded',
    file: TARGET,
    from: '<html lang={locale}',
    to: '<html lang="en"',
  },
  {
    name: 'a fragment is accepted as a whole document',
    file: RENDERER,
    from: "if (!markup.startsWith('<html')) {",
    to: 'if (false) {',
  },
  {
    name: 'the status chip may appear without its caption',
    file: DOSSIER,
    from: '<p className="dossier-status-caption">{STATUS_CAPTION}</p>',
    to: '<p className="dossier-status-caption" />',
  },
  {
    name: 'a derived value is accepted as a publisher flag',
    file: DOSSIER,
    from: 'if (!PUBLISHER_FLAG.test(status.binding_status)) {',
    to: 'if (false) {',
  },
  {
    name: 'zero states held reports no gaps',
    file: DOSSIER,
    from: 'if (coverage.states_held === 0) {',
    to: 'if (false) {',
  },
  {
    name: 'the record clock accepts a calendar date',
    file: DOSSIER,
    from: "const wantsInstant = row.role === 'observed_from';",
    to: 'const wantsInstant = false;',
  },
  {
    name: 'a reversed coverage hole renders',
    file: DOSSIER,
    from: 'if (!isOrderedInterval(hole.from, hole.to) || hole.from === hole.to) {',
    to: 'if (false) {',
  },
  {
    name: 'the title language is hardcoded',
    file: DOSSIER,
    from: 'lang={card.identity.title_language}',
    to: 'lang="en"',
  },
  {
    name: 'an absent date need not say what it waits for',
    file: DOSSIER,
    from: "if (typeof row.awaiting !== 'string' || row.awaiting.trim().length === 0) {",
    to: 'if (false) {',
  },
  {
    name: 'the refusal route policy is bypassed',
    file: REFUSAL,
    from: '<a href={handoffUri(one.href)}>{one.label}</a>',
    to: '<a href={one.href}>{one.label}</a>',
  },
  {
    name: 'CONTROL: the main landmark is removed',
    file: TARGET,
    from: '<main id="main">{children}</main>',
    to: '<div>{children}</div>',
  },
];

function suite() {
  try {
    const out = execFileSync('npm', ['test'], { encoding: 'utf8', shell: true });
    return { failed: 0, total: Number(/# pass (\d+)/.exec(out)?.[1] ?? 0) };
  } catch (error) {
    const out = `${error.stdout ?? ''}`;
    return {
      failed: Number(/# fail (\d+)/.exec(out)?.[1] ?? -1),
      total: Number(/# pass (\d+)/.exec(out)?.[1] ?? 0),
    };
  }
}

const baseline = suite();
if (baseline.failed !== 0) {
  throw new Error(`the suite is not green before mutating: ${JSON.stringify(baseline)}`);
}
process.stdout.write(`baseline: ${baseline.total} passing\n\n`);

const results = [];
for (const mutation of MUTATIONS) {
  const original = readFileSync(mutation.file, 'utf8');
  if (!original.includes(mutation.from)) {
    results.push({ name: mutation.name, verdict: 'NOT APPLIED' });
    continue;
  }
  writeFileSync(mutation.file, original.replace(mutation.from, mutation.to), 'utf8');
  try {
    const run = suite();
    // A mutation that leaves the same number of passing tests and none failing is a survivor.
    // One that changes the total without failing anything broke compilation, and that is an
    // invalid run rather than a result.
    const verdict =
      run.failed > 0
        ? 'killed'
        : run.total === baseline.total
          ? 'SURVIVED'
          : `INVALID (${run.total} vs ${baseline.total})`;
    results.push({ name: mutation.name, verdict });
  } finally {
    writeFileSync(mutation.file, original, 'utf8');
  }
}

const survivors = results.filter((r) => r.verdict !== 'killed');
for (const result of results) {
  process.stdout.write(`${result.verdict === 'killed' ? '  ' : '! '}${result.verdict.padEnd(10)} ${result.name}\n`);
}
process.stdout.write(`\n${results.length - survivors.length}/${results.length} killed\n`);
process.exitCode = survivors.length > 0 ? 1 : 0;
