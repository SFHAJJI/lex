// The home page's example searches derived over a mount through a real API (`home-examples.mjs` says the rule): the API's
// build output started over the mount, as the journeys start it (`startApi`: a copy of the output, the mount beside it, its
// own temporary folder, all removed when it closes), asked `coverage`, `browse`, `evidence_bundle` and `search` over HTTP,
// and closed. The journeys run it before they build the pages, and the release before it builds its image, so the pages a
// release serves carry examples its own corpus answers.
//
//   node scripts/home-examples-run.mjs --api <Lex.V3.Api build output> --mount <a v3-corpus directory> [--out <file>]
//
// prints the record (and writes it to `--out`), or says that no corpus was counted. No browser, no publisher, nothing kept.
//
// `homeExampleFailures` holds a served site to its examples over HTTP, with no browser: each home page's examples, read
// from the page itself (the search its link holds and the article it says it opens), asked of the same server as the
// search page asks them, must open that very article; and, when the record the build carried is given, the page must list
// exactly its examples. The release's image probe asks it.

import { readFile, writeFile } from "node:fs/promises";

import { deriveHomeExamples, exampleHref } from "./home-examples.mjs";
import { readSearchFragment } from "./search-address.mjs";
import { searchParameters } from "./live-search.mjs";
import { luxembourgDate } from "./temporal.mjs";
import { invokedDirectly } from "./invoked-directly.mjs";

/** The contract the API's envelopes are read against: the census's, as the pages read them. */
async function censusContract() {
  return JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8")).contract;
}

/**
 * The record of example searches over `mount`, through the API built at `apiOutput` (or null when it counts no corpus), on
 * `today` in Luxembourg (by default, today by this machine's clock). `log` hears each act read.
 */
export async function homeExamplesOverMount({ apiOutput, mount, today = luxembourgDate(new Date().toISOString()), log = () => {} }) {
  if (!mount) return null;
  const { askOperation, startApi } = await import("./journey.mjs");
  const contract = await censusContract();
  const api = await startApi(apiOutput, mount);
  try {
    return await deriveHomeExamples({ ask: (operation, parameters) => askOperation(api.origin, operation, parameters), contract, today, log });
  } finally {
    await api.close();
  }
}

/** The example searches a home page's bytes list: each link's address and the article it says it opens, in order. */
export function examplesListed(html) {
  return [...html.matchAll(/<a href="([^"]*)" data-home-example="([^"]*)"/g)].map(([, href, opens]) => ({ href: href.replaceAll("&amp;", "&"), opens }));
}

/**
 * What is wrong with the example searches the site at `origin` serves, as failures: on each interface language's home page,
 * each example asked of the same server as the search page asks it (`search`, the strict lane, the link's language, no date)
 * must open the article the page says it opens; with `expected` (the record the build carried, or null for none), the page
 * must list exactly those examples. `fetchImpl` stands for `fetch`.
 */
export async function homeExampleFailures(origin, { expected = undefined, languages = ["fr", "en"], fetchImpl = fetch } = {}) {
  const failures = [];
  for (const language of languages) {
    const response = await fetchImpl(`${origin}/${language}/`);
    if (response.status !== 200) {
      failures.push(`GET /${language}/ answered ${response.status}, so its example searches could not be read`);
      continue;
    }
    const listed = examplesListed(await response.text());
    if (expected !== undefined) {
      const wanted = (expected?.examples ?? []).map((example) => ({ href: exampleHref(example, language), opens: example.opens }));
      if (JSON.stringify(listed) !== JSON.stringify(wanted)) {
        failures.push(`/${language}/ lists the example searches ${JSON.stringify(listed)}, not the build's ${JSON.stringify(wanted)}`);
      }
    }
    for (const { href, opens } of listed) {
      const read = readSearchFragment(href.slice(href.indexOf("#")), { work: false });
      if (!href.startsWith(`/${language}/search.html#`) || read.state !== "search") {
        failures.push(`/${language}/ links an example search the search page cannot read: ${href}`);
        continue;
      }
      const parameters = searchParameters(read.request);
      const answer = await fetchImpl(`${origin}/api/v3/search`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ operation_id: "search", parameters }),
      });
      const envelope = answer.ok ? await answer.json() : null;
      const reference = envelope?.result?.value?.reference ?? null;
      if (reference?.outcome !== "opens" || reference.opens?.page !== "reading" || reference.opens.address !== opens) {
        failures.push(`/${language}/'s example "${read.request.query}" does not open ${opens}: the search answers ${JSON.stringify(reference?.opens?.address ?? reference?.outcome ?? answer.status)}`);
      }
    }
  }
  return failures;
}

async function main(argv) {
  const argument = (name) => {
    const index = argv.indexOf(name);
    return index < 0 || index + 1 >= argv.length ? null : argv[index + 1];
  };
  const apiOutput = argument("--api");
  const mount = argument("--mount");
  if (apiOutput === null || mount === null) throw new Error("usage: node scripts/home-examples-run.mjs --api <Lex.V3.Api build output> --mount <v3-corpus directory> [--out <file>]");
  const record = await homeExamplesOverMount({ apiOutput, mount, log: (line) => console.error(line) });
  if (record === null) {
    console.log("no corpus counted: no example searches");
    return 1;
  }
  const text = `${JSON.stringify(record, null, 2)}\n`;
  if (argument("--out") !== null) await writeFile(argument("--out"), text, "utf8");
  process.stdout.write(text);
  return 0;
}

if (invokedDirectly(import.meta.url, process.argv[1])) {
  main(process.argv.slice(2)).then((code) => process.exit(code), (error) => {
    console.error(error.message);
    process.exit(2);
  });
}
