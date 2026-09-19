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
    // The reviewer's own mutation, ported to the bundle: disable the movement table so ArrowDown
    // resolves to nothing. The initial markup is untouched -- exactly one option still carries
    // tabindex="0" -- which is why the attribute-shape gate cannot see this and the driven gate
    // must. This is the pair that showed my first version of these gates proved nothing.
    name: "the listbox key handler disabled, so arrow keys move nothing",
    expect: /ArrowDown moved focus nowhere in a listbox/i,
    async apply(root) {
      const file = join(root, "client.js");
      const code = await readFile(file, "utf8");
      await writeFile(file, code.replace("ArrowDown:", "ArrowDownDisabled:"), "utf8");
    },
  },
  {
    // A toggle whose state can never change. aria-pressed keeps rendering a correct boolean from
    // the model, so every attribute check still passes; only activating it shows the model is inert.
    name: "the filter toggle made inert, so pressing a chip changes nothing",
    expect: /left aria-pressed at|state it represents stayed/i,
    async apply(root) {
      const file = join(root, "client.js");
      const code = await readFile(file, "utf8");
      await writeFile(file, code.replace(/onToggle:[A-Za-z_$][A-Za-z0-9_$]*\}/, "onToggle:()=>{}}"), "utf8");
    },
  },
  {
    // Deliberately makes a SECOND option tabbable rather than removing the group. A mutation that
    // deleted the listbox would be caught by the landmark and interactive counts and would prove
    // nothing about roving; this leaves the group intact and breaks only the invariant.
    name: "a second result made tabbable, so the listbox no longer roves",
    expect: /listbox with \d+ option\(s\) has 2 tabbable/i,
    async apply(root) {
      for (const name of await readdir(root)) {
        if (!name.endsWith(".html")) continue;
        const file = join(root, name);
        const html = await readFile(file, "utf8");
        if (!html.includes('role="option"')) continue;
        // The first non-tabbable option becomes tabbable alongside the real one.
        await writeFile(
          file,
          html.replace('tabindex="-1"', 'tabindex="0"'),
          "utf8",
        );
      }
    },
  },
  {
    // Home is declared in the movement table and was never pressed by any gate, so a dead Home
    // stayed green. The mutation keeps the key in the table and makes it move nowhere, which is
    // what a handler bug looks like; deleting the key would only exercise the browser default.
    name: "the Home key made to stay put, so it moves nothing",
    expect: /Home moved focus to option \d+, expected 0/i,
    async apply(root) {
      await replaceOnce(join(root, "client.js"), /Home:\(\)=>0/, "Home:(e)=>e");
    },
  },
  {
    // The tab stop is clamped to the rows that exist. Removing the clamp restores the defect the
    // gate was written against: stand on the last row, filter the list shorter, and no option is
    // tabbable. The pattern is the clamp's shape, not its minified names, and must match once.
    name: "the tab-stop clamp removed, so a shortened list loses its tab stop",
    expect: /options are tabbable; the listbox drops out of the Tab order/i,
    async apply(root) {
      await replaceOnce(
        join(root, "client.js"),
        /Math\.min\(([A-Za-z_$][A-Za-z0-9_$]*),[A-Za-z_$][A-Za-z0-9_$]*\.length-1\)/,
        "$1",
      );
    },
  },
  {
    // The clamp replaced by a stop that jumps to the first row whenever the list shortens under it.
    // Exactly one option stays tabbable, so the "drops out of the Tab order" check passes; only the
    // check that the stop stays on the nearest row that exists can see the reader was moved.
    name: "the tab stop sent to the first row when a filter shortens the list",
    expect: /the tab stop moved to option \d+; it should stay on the nearest row that exists/i,
    async apply(root) {
      await replaceOnce(
        join(root, "client.js"),
        /Math\.min\(([A-Za-z_$][A-Za-z0-9_$]*),([A-Za-z_$][A-Za-z0-9_$]*)\.length-1\)/,
        "($1<$2.length?$1:0)",
      );
    },
  },
  {
    // A handler that logs is invisible at load: the load-time console check had already passed
    // and the next navigation cleared the buffer. This listener only ever fires on a key, so the
    // load check stays clean and only the check after the tab walk and driven probe can see it.
    name: "a console error raised by every key press after load",
    expect: /console output during the tab walk or driven actions/i,
    async apply(root) {
      const file = join(root, "client.js");
      const code = await readFile(file, "utf8");
      await writeFile(
        file,
        `${code}\n;document.addEventListener("keydown",function(){console.error("induced: keydown");});\n`,
        "utf8",
      );
    },
  },
  {
    // S5-A10: translation is never the default view. The trust surface carries exactly one
    // unofficial rendering; opening it in the served markup is the defect in its plainest form.
    name: "an unofficial rendering served open, so it is the default view",
    expect: /unofficial rendering 1 of 1 is shown by default \(open true/i,
    async apply(root) {
      await replaceOnce(
        join(root, "trust-surface.html"),
        /<details class="unofficial-rendering">/,
        '<details class="unofficial-rendering" open>',
      );
    },
  },
  {
    // The shape this slice replaced: the rendering in a plain section, visible with no action.
    name: "an unofficial rendering taken out of its disclosure",
    expect: /unofficial rendering 1 of 1 is a <section>, not a closed disclosure/i,
    async apply(root) {
      await replaceOnce(
        join(root, "trust-surface.html"),
        /<details class="unofficial-rendering"><summary class="unofficial-head">([\s\S]*?)<\/summary>([\s\S]*?)<\/details>/,
        '<section class="unofficial-rendering"><p class="unofficial-head">$1</p>$2</section>',
      );
    },
  },
  {
    // The disclosure stays closed and the stylesheet shows its content anyway. The `open`
    // attribute is false throughout, so only a gate that measures what is rendered, rather than
    // what the markup says, can see the text on screen.
    name: "an unofficial rendering shown by the stylesheet while its disclosure stays closed",
    expect: /is shown by default \(open false, text visible true, text on screen true\)/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(
        file,
        `${css}\ndetails.unofficial-rendering::details-content { content-visibility: visible; display: block; }\n`,
        "utf8",
      );
    },
  },
  {
    // The control keeps its icon and heading and loses the word. A reader then opens a body that
    // is not the law without having been told so first.
    name: "the UNOFFICIAL label removed from the control that opens a rendering",
    expect: /the control that opens unofficial rendering 1 of 1 does not say UNOFFICIAL/i,
    async apply(root) {
      await replaceOnce(
        join(root, "trust-surface.html"),
        /<span class="token-label">UNOFFICIAL<\/span>/,
        "",
      );
    },
  },
  {
    // Out of the Tab order. Focus by script still lands on it, which is why the probe also
    // requires a tab stop; without that this mutation would pass.
    name: "the control that opens a rendering taken out of the Tab order",
    expect: /the UNOFFICIAL control of unofficial rendering 1 of 1 cannot take keyboard focus/i,
    async apply(root) {
      await replaceOnce(
        join(root, "trust-surface.html"),
        /<summary class="unofficial-head">/,
        '<summary class="unofficial-head" tabindex="-1">',
      );
    },
  },
  {
    // The second of two renderings out of the Tab order. The first version of the probe drove only
    // the first summary on a page, so this passed every gate while the load-time checks held.
    name: "the second rendering's control taken out of the Tab order",
    expect: /the UNOFFICIAL control of unofficial rendering 2 of 2 cannot take keyboard focus/i,
    async apply(root) {
      await replaceOnce(
        join(root, "reading.html"),
        /(<summary class="unofficial-head">[\s\S]*?)<summary class="unofficial-head">/,
        '$1<summary class="unofficial-head" tabindex="-1">',
      );
    },
  },
  {
    // The word stays in the markup, the accessible name keeps it, and a sighted reader never sees
    // it. `textContent` ignores CSS, so the first version of the label check passed this; only
    // measuring the rendered label catches it.
    name: "the UNOFFICIAL label shrunk to nothing by the stylesheet",
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is in the markup but not shown/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(file, `${css}\n.unofficial-head .token-label { font-size: 0; line-height: 0; }\n`, "utf8");
    },
  },
  {
    name: "the UNOFFICIAL label made fully transparent by the stylesheet",
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is in the markup but not shown: display, visibility or opacity hides it/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(file, `${css}\n.unofficial-head .token-label { opacity: 0; }\n`, "utf8");
    },
  },
  {
    // Text coloured transparent. The contrast sweep catches this too; the label check has to catch it
    // on its own, and names the reason.
    name: "the UNOFFICIAL label's text made transparent by the stylesheet",
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is in the markup but not shown: its text colour is transparent/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(file, `${css}\n.unofficial-head .token-label { color: transparent; }\n`, "utf8");
    },
  },
  {
    // The screen-reader-only pattern: one pixel, clipped. A screen reader still announces the word;
    // no sighted reader can see it.
    name: "the UNOFFICIAL label clipped to one pixel by the stylesheet",
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is in the markup but not shown: its box is/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(
        file,
        `${css}\n.unofficial-head .token-label { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }\n`,
        "utf8",
      );
    },
  },
  {
    // A summary with nothing to say. The accessible-name check listed the role as "disclosure
    // triangle" while Chrome reports `DisclosureTriangle`, so no summary was ever held to having
    // a name. The expected sentence names the role, so only the corrected spelling can match it.
    name: "a summary emptied of its name",
    expect: /interactive node\(s\) with no accessible name: DisclosureTriangle/i,
    async apply(root) {
      await replaceOnce(join(root, "state-success.html"), /<summary>[\s\S]*?<\/summary>/, "<summary></summary>");
    },
  },
  {
    name: "a toggle whose pressed state is not a boolean",
    expect: /aria-pressed="[^"]*" is not a boolean/i,
    async apply(root) {
      for (const name of await readdir(root)) {
        if (!name.endsWith(".html")) continue;
        const file = join(root, name);
        const html = await readFile(file, "utf8");
        if (!html.includes("aria-pressed=")) continue;
        await writeFile(
          file,
          html.replace(/aria-pressed="(true|false)"/, 'aria-pressed="mixed"'),
          "utf8",
        );
      }
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

/**
 * Replaces exactly one match. A pattern that matches nothing leaves the copy unmutated and the run
 * green, which would read as a gate that failed to catch a mutation; one that matches twice
 * mutates more than the property the mutation names. Either way the mutation proves nothing, so
 * both stop the run.
 */
async function replaceOnce(file, pattern, replacement) {
  const code = await readFile(file, "utf8");
  const matches = code.match(new RegExp(pattern.source, `${pattern.flags.replace("g", "")}g`)) ?? [];
  if (matches.length !== 1) {
    throw new Error(`${pattern} matched ${matches.length} times in ${file}; expected exactly one`);
  }
  await writeFile(file, code.replace(pattern, replacement), "utf8");
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
