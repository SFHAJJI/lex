// The reading's weight and speed (the plan's item 3.7; the specification of record's R73 and R107): what a reading page
// weighs and how long the server takes to answer and render it, measured without a browser.
//
// Two measures, each printed as JSON:
//  - `--origin <api> --address <path> [--address <path> ...]`: for each law's address (under an interface language, as a
//    reader opens it), the API's answer to the request the page itself sends (`sitePageFor`: its bytes and the time to
//    answer it, the median of `--runs`), and the page the API serves at that address (its bytes and the time to render it
//    from that answer, GET minus the answer's median). The API must be running with its pages (`v3-web`) beside it; nothing
//    leaves this machine: the origin must be a loopback address.
//  - `--census <envelope-samples.json> --ssr <dir> --articles <n> --shape whole|page`: the server renderer (`ssr/fr.js`)
//    run on its own, in a context of its own as both servers run it, on a reading of `n` articles made from the census's
//    fixture state (its 49 articles' text repeated, each under its own id, as a code's articles are), whole (as the API
//    answered before reading by page) or as its first page (as it answers now): the page's bytes, the envelope's, and the
//    render time. The renderer of a build before reading by page renders the whole shape.
//
// Synthetic text only: the census's fixture text is the repository's own; no publisher is asked anything.

import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import vm from 'node:vm';

import { sitePageFor } from './permalinks.mjs';
import { cutIntoPages } from './reading-page-samples.mjs';
import { invokedDirectly } from './invoked-directly.mjs';

const median = (values) => {
  const sorted = [...values].sort((left, right) => left - right);
  return sorted[Math.floor(sorted.length / 2)];
};

/** The renderer of `dir` (`fr.js`), loaded as `serve-live.mjs` loads it. */
export async function loadRenderer(dir, language = 'fr') {
  const context = vm.createContext({ TextEncoder });
  vm.runInContext(await readFile(join(dir, `${language}.js`), 'utf8'), context, { timeout: 30_000 });
  return context.lexSsr;
}

/** A loopback origin, or a throw: this measure asks only a server on this machine. */
function loopback(origin) {
  const url = new URL(origin);
  if (!['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname)) throw new Error(`${origin} is not a loopback address`);
  return url.origin;
}

/** The API's answer and the served page at each address, measured `runs` times each. */
export async function measureAddresses({ origin, addresses, runs = 5, today = null }) {
  const base = loopback(origin);
  const rows = [];
  for (const address of addresses) {
    const [path, search = ''] = address.split(/(?=\?)/);
    const page = sitePageFor(path, { today, search });
    if (page === null) throw new Error(`${address} is no law's address the server renders`);
    const body = JSON.stringify({ operation_id: page.operation, parameters: page.parameters });
    const answers = [];
    let answerBytes = 0;
    let state = null;
    for (let run = 0; run <= runs; run += 1) {
      const started = performance.now();
      const answer = await fetch(`${base}/api/v3/${page.operation}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body });
      const bytes = Buffer.from(await answer.arrayBuffer());
      if (run > 0) answers.push(performance.now() - started);
      answerBytes = bytes.length;
      const envelope = JSON.parse(bytes.toString('utf8'));
      const version = envelope.result?.value?.states?.[0] ?? envelope.result?.value?.wordings?.[0] ?? null;
      state = envelope.refusal?.code ?? (version === null ? 'answered' : `${version.articles.length} articles${version.page ? ` (page ${version.page.number} of ${version.page.pages}, ${version.page.articles_in_version} in the version)` : ''}`);
    }
    const gets = [];
    let pageBytes = 0;
    let rendered = false;
    let shows = null;
    for (let run = 0; run <= runs; run += 1) {
      const started = performance.now();
      const served = await fetch(`${base}${address}`);
      const bytes = Buffer.from(await served.arrayBuffer());
      if (run > 0) gets.push(performance.now() - started);
      pageBytes = bytes.length;
      rendered = bytes.includes('lex-initial-answer');
      // What the page shows: the answer state it was rendered in, and how many articles it lays out.
      const html = bytes.toString('utf8');
      shows = `${/data-answer-state="([a-z_]+)"/.exec(html)?.[1] ?? 'no answer'}, ${(html.match(/ data-article="/g) ?? []).length} articles laid out`;
    }
    rows.push({
      address,
      answered: state,
      answer_bytes: answerBytes,
      answer_ms: Math.round(median(answers)),
      page_bytes: pageBytes,
      server_rendered: rendered,
      page_shows: shows,
      page_ms: Math.round(median(gets)),
      render_ms: Math.max(0, Math.round(median(gets) - median(answers))),
    });
  }
  return rows;
}

/**
 * A reading of `count` articles made from the census's fixture state: its articles repeated in order, the k-th copy of
 * each under its own id and identity, with the state's counts. `shape` `whole` is the answer as one version, `page` its
 * first page (the API's page rule cuts it every 50 articles of this size).
 */
export function syntheticReading(census, count, shape) {
  const entry = census.envelopes.find((candidate) => candidate.operation === 'evidence_bundle' && candidate.scenario.startsWith("a state's permalink at its own date"));
  const envelope = structuredClone(entry.envelope);
  const state = envelope.result.value.states[0];
  const source = state.articles;
  const articles = Array.from({ length: count }, (_, index) => {
    const article = structuredClone(source[index % source.length]);
    const id = index === 0 ? 'art_1er' : `art_${index + 1}`;
    article.publisher_id = id;
    article.article_identity_sha256 = (index.toString(16).padStart(8, '0') + article.article_identity_sha256).slice(0, 64);
    article.article_permalink = `${state.permalink}/${id}`;
    if (Object.hasOwn(article, 'position')) article.position = index + 1;
    return article;
  });
  state.articles = articles;
  state.validity_conflict_count = articles.filter((article) => article.validity_conflict).length;
  if (shape === 'whole') {
    for (const key of ['page', 'pages', 'contents']) delete state[key];
    for (const article of articles) delete article.position;
    for (const key of ['requested_page', 'requested_article', 'page_rule']) delete envelope.result.value[key];
    return envelope;
  }
  state.page = { ...state.page, articles_in_version: count, quoted_in_version: count, without_text_in_version: 0, validity_conflicts_in_version: state.validity_conflict_count };
  state.pages = [];
  const [first] = cutIntoPages(envelope.result.value, 50);
  envelope.result.value = first;
  return envelope;
}

/** The renderer run on a synthetic reading: the page's bytes, the envelope's, and the render time (median of `runs`). */
export async function measureSynthetic({ census, ssr, count, shape, runs = 5 }) {
  const renderer = await loadRenderer(ssr);
  const envelope = syntheticReading(census, count, shape);
  const json = JSON.stringify(envelope);
  const path = `/fr${envelope.result.value.states[0].permalink}`;
  const times = [];
  let html = '';
  for (let run = 0; run <= runs; run += 1) {
    const started = performance.now();
    html = renderer.render('reading', json, path);
    if (run > 0) times.push(performance.now() - started);
  }
  if (typeof html !== 'string' || !html.startsWith('<!doctype html>')) throw new Error('the renderer returned no page');
  return {
    articles: count,
    shape,
    articles_on_the_page: (html.match(/data-article="/g) ?? []).length,
    envelope_bytes: Buffer.byteLength(json, 'utf8'),
    page_bytes: Buffer.byteLength(html, 'utf8'),
    render_ms: Math.round(median(times)),
  };
}

async function main(argv) {
  const values = (name) => argv.flatMap((value, index) => (argv[index - 1] === name ? [value] : []));
  const value = (name) => values(name)[0] ?? null;
  const runs = Number(value('--runs') ?? 5);
  if (value('--census') !== null) {
    const census = JSON.parse(await readFile(value('--census'), 'utf8'));
    const result = await measureSynthetic({ census, ssr: value('--ssr'), count: Number(value('--articles') ?? 3000), shape: value('--shape') ?? 'page', runs });
    console.log(JSON.stringify(result, null, 2));
    return 0;
  }
  const rows = await measureAddresses({ origin: value('--origin'), addresses: values('--address'), runs, today: value('--today') });
  console.log(JSON.stringify(rows, null, 2));
  return 0;
}

if (invokedDirectly(import.meta.url, process.argv[1])) {
  main(process.argv.slice(2)).then((code) => process.exit(code), (error) => {
    console.error(error.message);
    process.exit(2);
  });
}
