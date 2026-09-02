/**
 * Induced mutations against the browser evidence harness.
 *
 * Each mutation breaks exactly one property the harness claims to check, serves the
 * broken copy, and requires the harness to fail naming that property. A gate nobody has
 * watched fail is a gate nobody should trust, and both gates exercised here replace
 * checks that were green on pages that violated them: the contrast sweep measured the
 * parent `li` and never the anchor inside it, and the harness collected headings but
 * never looked at their levels.
 *
 * Run: node scripts/evidence-mutations.mjs
 */
import { spawn } from "node:child_process";
import { cp, mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const MUTATIONS = [
  {
    name: "a link with almost no contrast against the page",
    // Deliberately narrow. `/contrast/` alone matched the ordinary status line, which
    // prints `contrast=74` on a clean run, so the mutation would have been reported as
    // caught by evidence that proves nothing. The pattern requires the failure sentence
    // and requires the offending element to be the anchor.
    expect: /element\(s\) below required contrast, worst [\d.]+ on <a>/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      // Both schemes, so the mutation cannot be masked by whichever one is measured.
      await writeFile(
        file,
        `${css}\na, a:visited { color: #f2efe6; }\n` +
          "@media (prefers-color-scheme: dark) { a, a:visited { color: #131a15; } }\n",
        "utf8",
      );
    },
  },
  {
    name: "a heading level skipped from h1 to h3",
    expect: /heading level jumps/i,
    async apply(root) {
      for (const name of await readdir(root)) {
        if (!name.endsWith(".html")) continue;
        const file = join(root, name);
        const html = await readFile(file, "utf8");
        if (!html.includes("<h2>")) continue;
        await writeFile(
          file,
          html.replace("<h2>", "<h3>").replace("</h2>", "</h3>"),
          "utf8",
        );
      }
    },
  },
  {
    name: "the only h1 demoted, leaving no level-one heading",
    expect: /first heading is h2|h1 elements/i,
    async apply(root) {
      for (const name of await readdir(root)) {
        if (!name.endsWith(".html")) continue;
        const file = join(root, name);
        const html = await readFile(file, "utf8");
        await writeFile(
          file,
          html.replace(/<h1([ >])/, "<h2$1").replace("</h1>", "</h2>"),
          "utf8",
        );
      }
    },
  },
  {
    // The defect this replaces was real and shipped: the components emitted adjacent spans
    // with no whitespace and the stylesheet gave them no layout, so a token label and the
    // text it qualifies were painted as one word. Contrast, overflow, console and the
    // accessibility tree were all clean on that page.
    name: "the component separation rules removed, so labels and values paint flush",
    expect: /painted flush against each other/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(
        file,
        `${css}\n.token, .verify-hash, .verify-cluster, .refusal-head, .refusal-header,\n` +
          ".status-strip-flag { display: inline; gap: 0; }\n" +
          ".envelope-strip summary > * + * { margin-inline-start: 0; }\n",
        "utf8",
      );
    },
  },
  {
    name: "a control with no handler on a page that loads no script",
    expect: /control\(s\) with no activation path/i,
    async apply(root) {
      for (const name of await readdir(root)) {
        if (!name.endsWith(".html")) continue;
        const file = join(root, name);
        const html = await readFile(file, "utf8");
        if (!html.includes("</main>")) continue;
        await writeFile(
          file,
          html.replace("</main>", '<button type="button">Copy the full digest</button></main>'),
          "utf8",
        );
      }
    },
  },
  {
    // A shell that declares a density in the markup and gets no rule from the stylesheet
    // looks correct in every single-page check: the attribute is there, the tests pass, and
    // all three shells render identically. Only a comparison across pages can see it.
    name: "the shell densities declared but not styled, so three shells lay out identically",
    expect: /a density that changes nothing is not a density|claims density monospace/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(
        file,
        `${css}\n[data-density="comfortable"] main,\n[data-density="compact"] main,\n` +
          '[data-density="monospace"] main { line-height: 1.5; font-size: 1rem;\n' +
          "  font-family: system-ui, sans-serif; }\n",
        "utf8",
      );
    },
  },
  {
    // Lighthouse found three of these at 19 to 21 CSS px and every check here passed. The
    // gate now measures every target, and turning the rule off has to bring them all back.
    name: "the target-size floor removed, so list targets fall under 24 CSS px",
    expect: /below the WCAG 2.2 24 CSS px minimum/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(
        file,
        `${css}\nli > a, summary, .verify-cluster a ` +
          "{ min-block-size: 0; min-inline-size: 0; padding-block: 0; }\n",
        "utf8",
      );
    },
  },
  {
    name: "a visible action pointing at a page that does not exist",
    expect: /answers 404|could not be requested/i,
    async apply(root) {
      const file = join(root, "trust-surface.html");
      const html = await readFile(file, "utf8");
      await writeFile(
        file,
        html.replace('href="/provenance/', 'href="/no-such-screen/'),
        "utf8",
      );
    },
  },
];

function run(root) {
  return new Promise((resolveRun) => {
    const child = spawn(process.execPath, ["scripts/browser-evidence.mjs"], {
      cwd: process.cwd(),
      env: { ...process.env, LEX_EVIDENCE_ROOT: root },
      stdio: ["ignore", "pipe", "pipe"],
    });
    let output = "";
    child.stdout.on("data", (chunk) => {
      output += chunk;
    });
    child.stderr.on("data", (chunk) => {
      output += chunk;
    });
    child.on("close", (code) => resolveRun({ code, output }));
  });
}

let failures = 0;
for (const mutation of MUTATIONS) {
  const root = await mkdtemp(join(tmpdir(), "lex-evidence-"));
  try {
    await cp(join(process.cwd(), "dist"), root, { recursive: true });
    await mutation.apply(root);
    const { code, output } = await run(root);
    if (code === 0) {
      console.log(`STILL GREEN  ${mutation.name}`);
      failures += 1;
    } else if (!mutation.expect.test(output)) {
      console.log(`WRONG REASON ${mutation.name}`);
      console.log(output.split("\n").filter((l) => /:/.test(l)).slice(0, 4).join("\n"));
      failures += 1;
    } else {
      const line = output.split("\n").find((l) => mutation.expect.test(l)) ?? "";
      console.log(`caught       ${mutation.name}`);
      console.log(`             ${line.trim().slice(0, 140)}`);
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

if (failures > 0) {
  console.error(`\n${failures} induced mutation(s) were not caught.`);
  process.exit(1);
}
console.log(`\nall ${MUTATIONS.length} induced mutations were caught.`);
