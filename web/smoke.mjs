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
async function mount(url) {
  const dom = new JSDOM(`<!doctype html><html><body><div id="workspace"></div></body></html>`, {
    runScripts: "outside-only",
    url,
  });
  dom.window.fetch = () => Promise.resolve({ ok: true, json: async () => ({}) });
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

const html = await mount("https://law.soufien.lu/");
// The front page is one search box and one date, and that is a decision worth pinning: the four
// tabs it replaced were four query TYPES, which asked a reader to classify their own question
// before they could ask it. A stale bundle would still mount and still pass every check above.
for (const [re, want, why] of [
  [/class="finder"/, true, "the search card is gone"],
  [/class="onebox"/, true, "the single search input is gone"],
  [/class="asof"/, true, "the as-of date control is gone; the date IS the product"],
  [/class="asklaunch"/, true, "the assistant launcher is gone"],
  [/class="fin-tab/, false, "the query-type tabs are back; one box decides for the reader"],
  [/>\s*A topic\s*</, false, "the old topic tab is back"],
  [/>\s*History\s*</, false, "the History tab is back; the rail is meant to replace it"],
]) {
  if (re.test(html) !== want) { console.error(`FAIL — ${why}`); process.exit(1); }
}
// The report is a second surface, not a tab of the first: it must bring its own window, its own
// ordering and its own layers, and must NOT render the search box underneath itself.
const report = await mount("https://law.soufien.lu/?space=time&from=2025-01-01&until=2026-01-01");
for (const [re, want, why] of [
  [/class="period"/, true, "the report lost its header"],
  [/type="date"/, true, "the report lost its window controls"],
  [/class="seg"/, true, "the report lost its ordering control"],
  [/class="layer/, true, "the report lost its layer tabs"],
  [/class="onebox"/, false, "the search box renders inside the report"],
]) {
  if (re.test(report) !== want) { console.error(`FAIL (report) — ${why}`); process.exit(1); }
}

console.log(`ok — home ${html.length} chars (one box + one date), report ${report.length} chars (window + order + layers)`);
