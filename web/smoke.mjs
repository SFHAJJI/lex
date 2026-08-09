// Does the bundle actually mount? A build can typecheck, bundle and serve with the right
// MIME type and still throw on the first line — which is exactly what happened: library
// mode leaves process.env.NODE_ENV unsubstituted, React reads it at import, the bundle
// dies, and the page silently degrades to its server-rendered half. Nothing else in the
// pipeline notices, because every artifact is present and correct.
import fs from "node:fs";
import { JSDOM } from "jsdom";

const bundle = process.argv[2] ?? "../src/Lex.Web/wwwroot/app/workspace.js";
const code = fs.readFileSync(bundle, "utf8");

// The workspace reads its whole state from the URL, so each surface is one more mount at a
// different address. Only the home surface was ever checked, which is why deleting the finder
// could take the report's own controls with it and still pass.
async function mount(url, answer) {
  // The server emits the suggested starting points, validated against the index, so the mount
  // has to be given one to exercise the path that reads them.
  const doors = JSON.stringify([{ work: "lu-legilux:loi-1879-06-18-n1", label: "Code penal" }]);
  const facets = JSON.stringify({
    jurisdictions: [
      { value: "lu", code: "LU", hierarchies: [], domains: [], source_classes: ["LOI", "RGD"], act_forms: [], binding_statuses: [], languages: ["fr"] },
      { value: "eu", code: "EU", hierarchies: ["primary_eu_law", "secondary_eu_law"], domains: ["financial-services"], source_classes: ["REG", "DIR"], act_forms: ["REG", "DIR"], binding_statuses: ["in_force"], languages: ["en", "fr"] },
    ],
    hierarchies: ["primary_eu_law", "secondary_eu_law"], domains: ["financial-services"],
    source_classes: ["LOI", "RGD", "REG", "DIR"], act_forms: ["REG", "DIR"],
    binding_statuses: ["in_force"], languages: ["fr", "en"],
  });
  const dom = new JSDOM(
    `<!doctype html><html><body>` +
    `<script type="application/json" id="doors">${doors}</script>` +
    `<script type="application/json" id="search-facets">${facets}</script>` +
    `<div id="workspace"></div></body></html>`, {
    runScripts: "outside-only",
    url,
  });
  // JSDOM does not implement scrolling; the real browser does. The version rail uses it only to
  // keep the current tick visible, which is unrelated to this mount contract.
  dom.window.HTMLElement.prototype.scrollTo = () => {};
  dom.window.fetch = (_url, init) => {
    if (!answer) return Promise.resolve({
      ok: true,
      headers: { get: () => "application/json" },
      text: async () => "{}",
    });
    const request = JSON.parse(init?.body ?? "{}");
    const payload = answer(request.params?.name, request.params?.arguments ?? {});
    const rpc = { jsonrpc: "2.0", id: request.id, result: { content: [{ type: "text", text: JSON.stringify(payload) }] } };
    return Promise.resolve({
      ok: true,
      headers: { get: () => "text/event-stream" },
      text: async () => `event: message\ndata: ${JSON.stringify(rpc)}\n\n`,
    });
  };
  try {
    dom.window.eval(code);
  } catch (e) {
    console.error(`FAIL — bundle threw on load at ${url}: ${e.name}: ${e.message}`);
    process.exit(1);
  }
  // React 18 renders concurrently: the tree is not in the DOM synchronously after render().
  await new Promise((r) => setTimeout(r, 250));
  const out = dom.window.document.getElementById("workspace").innerHTML;
  if (out.length === 0) {
    console.error(`FAIL — bundle loaded but rendered nothing into #workspace at ${url}`);
    process.exit(1);
  }
  return out;
}

const controlsWithoutIdentity = (markup) => {
  const probe = new JSDOM(`<body>${markup}</body>`);
  return [...probe.window.document.querySelectorAll("input, select, textarea")]
    .filter((control) => !control.id && !control.getAttribute("name"));
};

const html = await mount("https://law.soufien.lu/");
// The front page is one search box and one date, and that is a decision worth pinning: the four
// tabs it replaced were four query TYPES, which asked a reader to classify their own question
// before they could ask it. A stale bundle would still mount and still pass every check above.
for (const [re, want, why] of [
  [/class="finder"/, true, "the search card is gone"],
  [/class="onebox"/, true, "the single search input is gone"],
  [/class="door"/, true, "the suggested starting points are gone"],
  [/Code penal/, true, "the doors did not come from the server-emitted block"],
  [/class="asof"/, true, "the as-of date control is gone; the date IS the product"],
  [/class="asklaunch"/, true, "the assistant launcher is gone"],
  [/aria-label="Ask about any law"/, true, "the assistant launcher's accessible name diverges from its visible label"],
  [/class="al-short">Ask</, true, "the compact mobile assistant label is gone"],
  [/class="fin-tab/, false, "the query-type tabs are back; one box decides for the reader"],
  [/>\s*A topic\s*</, false, "the old topic tab is back"],
  [/>\s*History\s*</, false, "the History tab is back; the rail is meant to replace it"],
]) {
  if (re.test(html) !== want) { console.error(`FAIL — ${why}`); process.exit(1); }
}
if (controlsWithoutIdentity(html).length > 0) {
  console.error("FAIL - an interactive home control has neither id nor name");
  process.exit(1);
}
// The report is a second surface, not a tab of the first: it must bring its own window, its own
// ordering and mounted-index jurisdiction scope, and must NOT render the search box underneath it.
const report = await mount("https://law.soufien.lu/?space=time&from=2025-01-01&until=2026-01-01");
for (const [re, want, why] of [
  [/class="period"/, true, "the report lost its header"],
  [/type="date"/, true, "the report lost its window controls"],
  [/class="seg"/, true, "the report lost its ordering control"],
  [/class="period-scope"/, true, "the report lost its jurisdiction control"],
  [/Every jurisdiction/, true, "the report no longer defaults to every mounted jurisdiction"],
  [/European Union/, true, "the EU jurisdiction emitted by the mounted index is missing"],
  [/class="layer/, false, "the Luxembourg-only legal layer tabs returned to the mixed report"],
  [/class="onebox"/, false, "the search box renders inside the report"],
]) {
  if (re.test(report) !== want) { console.error(`FAIL (report) — ${why}`); process.exit(1); }
}

if (controlsWithoutIdentity(report).length > 0) {
  console.error("FAIL (report) - an interactive report control has neither id nor name");
  process.exit(1);
}

// Evidence extraction is a reader action, so pin it on real reader states rather than merely
// checking that its words survived minification. These are the same MCP shapes production sends.
const document = (date) => ({
  work: "eu-eurlex:32016R0679", title: "General Data Protection Regulation", language: "en",
  valid_from: date, source_uri: `https://eur-lex.example/${date}`,
  permalink: `https://law.soufien.lu/eu-eurlex/32016R0679/${date}`,
});
const provision = (text, sha = "abc123def456") => ({
  anchor: "art_1", num: "Article 1", text, text_sha256: sha,
});
const lawAnswer = (name, args) => {
  if (name === "timeline") return { versions: [{ ...document("2018-05-25"), text_available: true }] };
  if (name === "as_of" && args.mode === "outline")
    return { document: document(String(args.date)), provisions: [provision(null)] };
  if (name === "as_of")
    return { document: document(String(args.date)), provisions: [provision("Exact publisher wording.")] };
  return {};
};
const law = await mount(
  "https://law.soufien.lu/?space=law&work=eu-eurlex%3A32016R0679&date=2018-05-25",
  lawAnswer,
);
for (const [re, why] of [
  [/copy citation/, "the article reader lost citation copying"],
  [/download evidence \(\.md\)/, "the article reader lost evidence download"],
  [/sha256 abc123def456/, "the reader is not using MCP's text_sha256 field"],
]) {
  if (!re.test(law)) { console.error(`FAIL (law export) - ${why}`); process.exit(1); }
}
if (controlsWithoutIdentity(law).length > 0) {
  console.error("FAIL (law) - an interactive reader control has neither id nor name");
  process.exit(1);
}

const compareAnswer = (name, args) => {
  if (name === "timeline") return {
    versions: ["2020-01-01", "2021-01-01"].map((date) => ({ ...document(date), text_available: true })),
  };
  if (name !== "as_of") return {};
  const before = args.date === "2020-01-01";
  const text = args.mode === "outline" ? null : before ? "The rate is five." : "The rate is six.";
  return {
    document: document(String(args.date)),
    provisions: [provision(text, before ? "old" : "new")],
  };
};
const comparison = await mount(
  "https://law.soufien.lu/?space=law&work=eu-eurlex%3A32016R0679&date=2020-01-01&to=2021-01-01&mode=compare",
  compareAnswer,
);
for (const [re, why] of [
  [/1 changed/, "the structured comparison did not render"],
  [/copy citation/, "the comparison lost citation copying"],
  [/download evidence \(\.md\)/, "the comparison lost evidence download"],
]) {
  if (!re.test(comparison)) { console.error(`FAIL (comparison export) - ${why}`); process.exit(1); }
}
if (controlsWithoutIdentity(comparison).length > 0) {
  console.error("FAIL (comparison) - an interactive comparison control has neither id nor name");
  process.exit(1);
}

// Missing publisher evidence is not a deletion. Pin the refusal so an absent body can never be
// rendered as every article being added/removed after a future refactor of the comparison path.
const unavailableAnswer = (name, args) => {
  if (name === "timeline") return {
    versions: ["2020-01-01", "2021-01-01"].map((date) => ({ ...document(date), text_available: date !== "2020-01-01" })),
  };
  if (name !== "as_of") return {};
  if (args.date === "2020-01-01") return {
    envelope: { status: "text_withheld" }, document: document(String(args.date)), provisions: [],
  };
  return { envelope: { status: "ok" }, document: document(String(args.date)),
           provisions: [provision(args.mode === "outline" ? null : "The rate is six.", "new")] };
};
const unavailable = await mount(
  "https://law.soufien.lu/?space=law&work=eu-eurlex%3A32016R0679&date=2020-01-01&to=2021-01-01&mode=compare",
  unavailableAnswer,
);
if (!/will not turn missing text into apparent additions or removals/.test(unavailable)
    || /1 added|1 removed/.test(unavailable)) {
  console.error("FAIL (comparison evidence gap) - unavailable text was presented as a legal change");
  process.exit(1);
}

console.log(`ok - home ${html.length} chars, report ${report.length} chars, law and comparison evidence actions`);
