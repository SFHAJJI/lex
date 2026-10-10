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
  {
    // The strongest anti-colour-alone check in the build, and until now the only evidence that it
    // worked was that it had never complained. An element whose meaning is carried only by paint
    // disappears entirely under forced colours.
    name: "an element carrying its meaning only as paint, invisible under forced colours",
    expect: /element\(s\) carry meaning only as paint, which forced colours removes/i,
    async apply(root) {
      await injectBeforeBodyEnd(root, "trust-surface.html", '<span class="probe-paint"></span>');
      await appendCss(
        root,
        ".probe-paint { display: inline-block; width: 48px; height: 48px;" +
          " background-image: linear-gradient(45deg, #2b2b2b 25%, transparent 25%); }",
      );
    },
  },
  {
    // The policy is compared byte for byte against the declared object precisely so that a
    // permissive addition cannot pass as a policy. unsafe-inline in script-src is the whole
    // failure mode: it is still a Content-Security-Policy, and it is no longer a restriction.
    name: "a served policy that quietly admits unsafe-inline",
    expect: /served policy does not match the declared one/i,
    async apply(root) {
      const file = join(root, "trust-surface.html");
      const html = await readFile(file, "utf8");
      const widened = html.replace(
        "script-src &#39;self&#39;;",
        "script-src &#39;self&#39; &#39;unsafe-inline&#39;;",
      );
      if (widened === html) {
        throw new Error("the CSP meta did not match the shape this mutation edits");
      }
      await writeFile(file, widened, "utf8");
    },
  },
  {
    // Keyboard reachability without a visible indicator is not keyboard access: the focus is
    // somewhere and the person cannot see where.
    name: "the focus indicator removed, so keyboard focus lands invisibly",
    expect: /focus stop\(s\) with no visible focus indicator/i,
    async apply(root) {
      await appendCss(
        root,
        ":focus, :focus-visible { outline: none !important; box-shadow: none !important; }",
      );
    },
  },
  {
    // The count and the walk are deliberately different measurements. A link with tabindex="-1"
    // still matches a[href], so it stays focusable by the DOM and drops out of the Tab order,
    // which is exactly the gap the two-measurement design exists to expose.
    name: "a link removed from the tab order while still counted as focusable",
    expect: /\d+ focusable elements but \d+ reachable by Tab/i,
    async apply(root) {
      const file = join(root, "trust-surface.html");
      const html = await readFile(file, "utf8");
      const index = html.indexOf("<a href=");
      if (index < 0) {
        throw new Error("no anchor to remove from the tab order");
      }
      await writeFile(
        file,
        `${html.slice(0, index)}<a tabindex="-1" href=${html.slice(index + "<a href=".length)}`,
        "utf8",
      );
    },
  },
  {
    // A control the accessibility tree cannot name is a control a screen reader cannot offer.
    // Sized well above the target floor on purpose, so the failure that fires is the naming one
    // and not the target-size gate standing in for it.
    name: "an interactive control with no accessible name",
    expect: /interactive node\(s\) with no accessible name/i,
    async apply(root) {
      await injectBeforeBodyEnd(root, "trust-surface.html", '<button class="probe-unnamed"></button>');
      await appendCss(root, ".probe-unnamed { width: 44px; height: 44px; }");
    },
  },
  {
    // Two main landmarks is not a richer page, it is a page where "skip to the main content" has
    // no answer.
    name: "a second main landmark, so the primary region is ambiguous",
    expect: /expected exactly one main landmark, found 2/i,
    async apply(root) {
      await injectBeforeBodyEnd(root, "trust-surface.html", "<main><p>probe</p></main>");
    },
  },
];

/**
 * Append a rule to the served stylesheet.
 */
async function appendCss(root, rule) {
  const file = join(root, "styles.css");
  const css = await readFile(file, "utf8");
  await writeFile(file, `${css}\n${rule}\n`, "utf8");
}

/**
 * Insert markup immediately before the closing body tag of one served page.
 *
 * Fails loudly rather than silently doing nothing: a mutation that edits no bytes would be
 * reported as caught by a harness that never saw it, which is the failure this whole file exists
 * to prevent.
 */
async function injectBeforeBodyEnd(root, page, markup) {
  const file = join(root, page);
  const html = await readFile(file, "utf8");
  if (!html.includes("</body>")) {
    throw new Error(`${page} has no closing body tag to inject before`);
  }
  await writeFile(file, html.replace("</body>", `${markup}</body>`), "utf8");
}

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
