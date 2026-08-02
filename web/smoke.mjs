// Does the bundle actually mount? A build can typecheck, bundle and serve with the right
// MIME type and still throw on the first line — which is exactly what happened: library
// mode leaves process.env.NODE_ENV unsubstituted, React reads it at import, the bundle
// dies, and the page silently degrades to its server-rendered half. Nothing else in the
// pipeline notices, because every artifact is present and correct.
import fs from "node:fs";
import { JSDOM } from "jsdom";

const bundle = process.argv[2] ?? "../src/Lex.Web/wwwroot/app/workspace.js";
const dom = new JSDOM(`<!doctype html><html><body><div id="workspace"></div></body></html>`, {
  runScripts: "outside-only",
  url: "https://law.soufien.lu/",
});
dom.window.fetch = () => Promise.resolve({ ok: true, json: async () => ({}) });

try {
  dom.window.eval(fs.readFileSync(bundle, "utf8"));
} catch (e) {
  console.error(`FAIL — bundle threw on load: ${e.name}: ${e.message}`);
  process.exit(1);
}

// React 18 renders concurrently: the tree is not in the DOM synchronously after render().
await new Promise((r) => setTimeout(r, 250));

const html = dom.window.document.getElementById("workspace").innerHTML;
if (html.length === 0) {
  console.error("FAIL — bundle loaded but rendered nothing into #workspace");
  process.exit(1);
}
for (const expected of ["A law", "A period", "A topic"]) {
  if (!html.includes(expected)) {
    console.error(`FAIL — rendered, but the ${expected} framework is missing`);
    process.exit(1);
  }
}
console.log(`ok — workspace mounted, ${html.length} chars, all three frameworks present`);
