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
import { invokedDirectly } from "./invoked-directly.mjs";

export const MUTATIONS = [
  {
    name: "a link with almost no contrast against the page",
    pages: ["citation-checker.html"],
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
    pages: ["search-react.html"],
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
    pages: ["search-react.html"],
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
    pages: ["search-react.html"],
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
    pages: ["search-react.html"],
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
    pages: ["search-react.html"],
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
    pages: ["search-react.html"],
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
    pages: ["hydration.html", "search-react.html"],
    expect: /console output during the tab walk, driven actions or the minute of page time run after them/i,
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
  // Compare arming. With nothing selected at load and no lex_id in the markup, the server HTML under
  // each of these is the HTML a rebuild from the mutated source would serve, so mutating the bundle
  // alone is not a hydration mismatch the page could be failed for instead.
  {
    // The defect this probe was written against: the preview's only second row of the work shared
    // the first row's lex_id, so selecting it deselected the first and nothing could ever arm.
    name: "the second synthetic state collapsed onto the first lex_id",
    pages: ["search-react.html"],
    expect:
      /with two states of one work selected \("[^"]+", "[^"]+"\), Compare stayed aria-disabled="true"/,
    async apply(root) {
      await replaceOnce(
        join(root, "client.js"),
        /(?<head>lex_id:`\$\{[A-Za-z_$][\w$]*\}:)1998-07-01`/,
        "$<head>2001-01-01`",
      );
    },
  },
  {
    // The work comparison removed from the rule, so any two rows arm. The sentence for two works
    // survives in the bundle; only the condition that chooses it is gone.
    name: "the arming rule made to ignore the work",
    pages: ["search-react.html"],
    expect: /with rows of two different works selected, Compare was armed \(aria-disabled="false"\)/,
    async apply(root) {
      await replaceOnce(
        join(root, "client.js"),
        /[A-Za-z_$][\w$]*!==[A-Za-z_$][\w$]*\?"These are two different works/,
        '!1?"These are two different works',
      );
    },
  },
  {
    // Three rows still do not arm, because `armedBy` counts to two on its own; what goes is the
    // sentence that tells the reader why. The control then says a pair is selected over three rows.
    name: "the three-row refusal removed",
    pages: ["search-react.html"],
    expect:
      /with three rows selected, the compare control said "Two states of one work selected\.", not "A comparison is between two states/,
    async apply(root) {
      await replaceOnce(
        join(root, "client.js"),
        /if\([A-Za-z_$][\w$]*\.length>2\)return"A comparison is between two states/,
        'if(!1)return"A comparison is between two states',
      );
    },
  },
  {
    // Space still claims the key -- preventDefault stays -- and selects nothing, which is what a
    // handler bug looks like. Every step after load then reads a list nobody could arm.
    name: "Space made inert on a result row",
    pages: ["search-react.html"],
    expect:
      /after Space on one state, the compare control said "Select two states to compare them\.", not "One state selected/,
    async apply(root) {
      await replaceOnce(
        join(root, "client.js"),
        /(\.key==="Spacebar"\)\{[A-Za-z_$][\w$]*\.preventDefault\(\)),[A-Za-z_$][\w$]*\([A-Za-z_$][\w$]*\[[A-Za-z_$][\w$]*\]\);return\}/,
        "$1;return}",
      );
    },
  },
  {
    // The selection works and the control arms, and the list never says which rows are chosen. A
    // screen reader hears "Two states of one work selected" and cannot find either of them.
    name: "rows never say they are selected",
    pages: ["search-react.html"],
    expect:
      /after Space on one state, row "[^"]+" is aria-selected="false", not "true"; the list does not say which rows are armed/,
    async apply(root) {
      await replaceOnce(
        join(root, "client.js"),
        /"aria-selected":[A-Za-z_$][\w$]*\.has\([A-Za-z_$][\w$]*\.lex_id\)/,
        '"aria-selected":!1',
      );
    },
  },
  {
    // The sentence still in the page, resolved by aria-describedby, and shown to nobody. Every
    // attribute the probe used to read survives this, so only reading what the page renders can
    // see it. A sighted keyboard reader meets a button that does nothing and no reason.
    name: "the compare control's sentence hidden from the page",
    pages: ["search-react.html"],
    expect:
      /the compare control's sentence is in the page and not shown \(the hidden attribute, 0x0 box, the words "[^"]*" in the document only\); a reason a reader cannot see is not a reason/,
    async apply(root) {
      await replaceOnce(
        join(root, "search-react.html"),
        /<p class="compare-arming-state"/,
        '<p hidden class="compare-arming-state"',
      );
    },
  },
  {
    // The screen-reader-only pattern turned on the sentence: rendered, visible to every property
    // check, one pixel of it on screen. The reviewer's variant on #683 in its own place.
    name: "the compare sentence clipped to one pixel by the stylesheet",
    pages: ["search-react.html"],
    expect: /the compare control's sentence is not painted: \d+ ink pixel\(s\), [\d.]+% of its 1x1 box, differ from the background; a reason nobody can read is not a reason/,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(
        file,
        `${css}
.compare-arming-state { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
`,
        "utf8",
      );
    },
  },
  {
    // The words drawn in nothing. innerText returns them, the box is full size, checkVisibility
    // is true, and the reader sees an empty line under a button that will not press.
    name: "the compare sentence drawn in transparent text",
    pages: ["search-react.html"],
    expect: /the compare control's sentence is not painted: \d+ ink pixel\(s\)/,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(
        file,
        `${css}
.compare-arming-state, .compare-arming-state * { color: transparent; }
@media (prefers-color-scheme: dark) { .compare-arming-state, .compare-arming-state * { color: transparent; } }
`,
        "utf8",
      );
    },
  },
  {
    // Zero opacity: caught by the as-shown check before the pixels are counted, and kept because
    // a sweep that only proves the newest check has stopped proving the older one.
    name: "the compare sentence given zero opacity",
    pages: ["search-react.html"],
    expect: /the compare control's sentence is in the page and not shown \(display block, visibility visible/,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(file, `${css}
.compare-arming-state { opacity: 0; }
`, "utf8");
    },
  },
  {
    // The control kept out of the accessibility tree, with the DOM untouched. A screen reader is
    // given neither the button nor its sentence; every attribute check still passes.
    name: "the compare control hidden from assistive technology",
    pages: ["search-react.html"],
    expect:
      /the Compare button is not in the accessibility tree; a control a screen reader is never given cannot tell anyone why states cannot be compared/,
    async apply(root) {
      await replaceOnce(
        join(root, "search-react.html"),
        /<div class="compare-arming">/,
        '<div class="compare-arming" aria-hidden="true">',
      );
    },
  },
  {
    // The whole control gone from the built page, with the list and its selection intact. The
    // probe finds nothing to drive, and a page declared for it must not read that as clean. Both
    // the served markup and the bundle change: a production hydrate keeps the server's attributes,
    // so renaming the class in the bundle alone leaves the control on the page.
    name: "the compare control no longer rendered",
    pages: ["search-react.html"],
    expect: /the compare probe declares rows for this page and the page has no compare control/,
    async apply(root) {
      await replaceOnce(join(root, "search-react.html"), /class="compare-arming"/, 'class="compare-gone"');
      await replaceOnce(join(root, "client.js"), /className:"compare-arming",/, 'className:"compare-gone",');
    },
  },
  {
    // S5-A10: translation is never the default view. The trust surface carries exactly one
    // unofficial rendering; opening it in the served markup is the defect in its plainest form.
    name: "an unofficial rendering served open, so it is the default view",
    pages: ["trust-surface.html"],
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
    pages: ["trust-surface.html"],
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
    pages: ["reading.html", "trust-surface.html"],
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
    pages: ["trust-surface.html"],
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
    pages: ["trust-surface.html"],
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
    pages: ["reading.html"],
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
    pages: ["reading.html", "trust-surface.html"],
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is not painted: \d+ ink pixel\(s\)/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(file, `${css}\n.unofficial-head .token-label { font-size: 0; line-height: 0; }\n`, "utf8");
    },
  },
  {
    name: "the UNOFFICIAL label made fully transparent by the stylesheet",
    pages: ["reading.html", "trust-surface.html"],
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is not painted: \d+ ink pixel\(s\).*\(display, visibility or opacity hides it\)/i,
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
    pages: ["reading.html", "trust-surface.html"],
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is not painted: \d+ ink pixel\(s\).*\(its text colour is transparent\)/i,
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
    pages: ["reading.html", "trust-surface.html"],
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is not painted: \d+ ink pixel\(s\).*\(its box is/i,
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
    // Four more ways to hide the word that no property list named: a filter, a text fill colour, a
    // clip path and an alpha of 0.01. Each leaves the label's box, font size, colour alpha and centre
    // hit-test looking fine, and the reviewer ran each with every gate green. Only the pixels the
    // browser painted can see them.
    name: "the UNOFFICIAL label hidden by a filter",
    pages: ["reading.html", "trust-surface.html"],
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is not painted: \d+ ink pixel\(s\)/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(file, `${css}\n.unofficial-head .token-label { filter: opacity(0); }\n`, "utf8");
    },
  },
  {
    name: "the UNOFFICIAL label's text filled transparent",
    pages: ["reading.html", "trust-surface.html"],
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is not painted: \d+ ink pixel\(s\)/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(file, `${css}\n.unofficial-head .token-label { -webkit-text-fill-color: transparent; }\n`, "utf8");
    },
  },
  {
    name: "the UNOFFICIAL label clipped to a one-pixel circle",
    pages: ["reading.html", "trust-surface.html"],
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is not painted: \d+ ink pixel\(s\)/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(file, `${css}\n.unofficial-head .token-label { clip-path: circle(1px at 50% 50%); }\n`, "utf8");
    },
  },
  {
    name: "the UNOFFICIAL label drawn at an alpha of 0.01",
    pages: ["reading.html", "trust-surface.html"],
    expect: /the UNOFFICIAL label of unofficial rendering \d+ of \d+ is not painted: \d+ ink pixel\(s\)/i,
    async apply(root) {
      const file = join(root, "styles.css");
      const css = await readFile(file, "utf8");
      await writeFile(
        file,
        `${css}\n.unofficial-head .token-label { color: rgba(0, 0, 0, 0.01); }\n` +
          "@media (prefers-color-scheme: dark) { .unofficial-head .token-label { color: rgba(255, 255, 255, 0.01); } }\n",
        "utf8",
      );
    },
  },
  {
    // A summary with nothing to say. The accessible-name check listed the role as "disclosure
    // triangle" while Chrome reports `DisclosureTriangle`, so no summary was ever held to having
    // a name. The expected sentence names the role, so only the corrected spelling can match it.
    name: "a summary emptied of its name",
    pages: ["state-success.html"],
    expect: /interactive node\(s\) with no accessible name: DisclosureTriangle/i,
    async apply(root) {
      await replaceOnce(join(root, "state-success.html"), /<summary>[\s\S]*?<\/summary>/, "<summary></summary>");
    },
  },
  {
    // A page that keeps reaching out after it has everything it needs. Silent in the console and
    // invisible in every screenshot; only a count of requests after the page settled can see it.
    // Ten seconds, not a fraction of one: a real polling loop is slow, and a gate that watched the
    // page only while the harness happened to be on it saw nothing slower than it stayed.
    name: "a same-origin polling loop after load, every ten seconds",
    pages: ["hydration.html", "search-react.html"],
    expect: /request\(s\) after the page settled, during the tab walk, the driven actions or the minute of page time run after them: \/pages\.json/i,
    async apply(root) {
      const file = join(root, "client.js");
      const code = await readFile(file, "utf8");
      await writeFile(file, `${code}\n;setInterval(function(){fetch("/pages.json");},10000);\n`, "utf8");
    },
  },
  {
    // One request, once, half a minute after load: no loop to catch in the act, only a timer the
    // page left armed.
    name: "a single same-origin request thirty seconds after load",
    pages: ["hydration.html", "search-react.html"],
    expect: /: 1 request\(s\) after the page settled, during the tab walk, the driven actions or the minute of page time run after them: \/pages\.json/i,
    async apply(root) {
      const file = join(root, "client.js");
      const code = await readFile(file, "utf8");
      await writeFile(file, `${code}\n;setTimeout(function(){fetch("/pages.json");},30000);\n`, "utf8");
    },
  },
  {
    // A missing file under the object-URL grammar is answered 200 with the stand-in page, so a status
    // check reads it as present. A broken image logs nothing either; only its media type says an
    // image is not what came back.
    name: "an image whose file is missing, answered 200 by the stand-in page",
    pages: ["trust-surface.html"],
    expect: /an Image request for \/images\/missing\.png was answered with text\/html/i,
    async apply(root) {
      await replaceOnce(
        join(root, "trust-surface.html"),
        /<\/main>/,
        '<img src="/images/missing.png" alt="An image that is missing on purpose"></main>',
      );
    },
  },
  {
    name: "a toggle whose pressed state is not a boolean",
    pages: ["search-react.html"],
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
    pages: ["citation-checker.html"],
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
    pages: ["citation-checker.html"],
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
    pages: ["compare.html"],
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
    pages: ["citation-checker.html"],
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
    pages: "all",
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
    pages: ["export-composer.html"],
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
    pages: ["trust-surface.html"],
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

/**
 * Whether this sweep measures every page for every mutation.
 *
 * Scoped is the default: a mutation of one page was measured on the thirty-two it cannot touch,
 * at about two minutes a mutation and 45 mutations. `LEX_EVIDENCE_SCOPE=full` runs every mutation
 * over every page, which is what a declaration is checked against: run it when a mutation is
 * added, when a page is added, and whenever a declaration is in doubt. The clean
 * `npm run evidence` is unscoped always and is where the 495-combination claim comes from.
 */
const FULL = process.env.LEX_EVIDENCE_SCOPE === "full";

/**
 * The mutations this run sweeps: all of them, or the ones whose name contains `only`.
 *
 * A declaration is judged by a full run, and a full run of all 45 is a hundred minutes. One
 * mutation over every page is three, which is what a new mutation or a new page costs to prove.
 * A selection that names nothing is refused rather than sweeping nothing and reporting it clean.
 */
export function mutationsToSweep(mutations, only) {
  const wanted = (only ?? "").trim();
  if (wanted === "") return mutations;
  const chosen = mutations.filter((mutation) => mutation.name.includes(wanted));
  if (chosen.length === 0) {
    throw new Error(`no mutation's name contains ${JSON.stringify(wanted)}; this run would sweep nothing`);
  }
  return chosen;
}

/**
 * Whether a mutation's declaration matches where its defect was actually caught.
 *
 * Judged only on a full run, because only a full run has seen every page. A declared page that
 * caught nothing is a wrong declaration and fails: every scoped sweep afterwards would look for
 * this defect where it is not, and a mutation that catches nothing anywhere is a gate that stopped
 * working. A page that caught it and was not declared is reported, not failed: the declaration
 * says where the defect can be seen, and one page is enough for the sweep's question, so a defect
 * that also shows elsewhere is news about coverage rather than a wrong declaration. Naming it
 * keeps the choice visible: a reader of a full run can widen the declaration deliberately.
 *
 * @param {string[]|"all"} pages  what the mutation declares
 * @param {string[]} matching  the output lines that matched the mutation's expectation
 */
export function declarationVerdict(pages, matching) {
  if (pages === "all") return { failures: [], notes: [] };
  // One rule, used for both halves. A failure sentence opens with the page it is about, so the
  // page is read from the start of the line and nowhere else. Asking whether the name appears
  // anywhere in the line is a different question with a different answer: a sentence about one
  // page can quote another page's name in a path, and a declared page that caught nothing would
  // pass on a sentence that was never about it.
  const about = matching.map(pageOf).filter((page) => page !== null);
  const failures = pages
    .filter((page) => !about.includes(page))
    .map((page) => `declares ${page} and no failure naming ${page} caught it`);
  const notes = [...new Set(about.filter((page) => !pages.includes(page)))].sort();
  return { failures, notes };
}

/**
 * The page a failure sentence is about: the one it opens with, or null when it names none.
 *
 * The character class is what the build can emit and no more: `provenance.mjs` admits
 * `[A-Za-z0-9._:-]` in a lexId and writes `:` as `~`, so a page name holds letters, digits, dot,
 * underscore, tilde and hyphen. A wider class would be defensive about names nothing produces.
 */
export function pageOf(line) {
  return /^\s*([A-Za-z0-9._~-]+\.html)(?=[\s:]|$)/.exec(line)?.[1] ?? null;
}

/** A run's output as lines, with carriage returns dropped so a page is read the same on any host. */
const lineOf = (output) => output.split("\n").map((line) => line.replace(/\r$/, ""));

/**
 * The scope one mutation's run is given: the pages it declares, or null for every page.
 *
 * Null on a full sweep and for a mutation declaring `"all"`, which is a defect only a comparison
 * across pages can show.
 */
export function scopeFor(pages, full) {
  return full || pages === "all" ? null : pages.join(",");
}

/**
 * The environment the browser evidence run is given.
 *
 * Built here, and not spread at the call site, because the caller's own `LEX_EVIDENCE_PAGES` must
 * not survive into it. A caller who exports that variable and asks for a full sweep would otherwise
 * get runs scoped to their pages while the sweep says it measured every one — and on a full sweep
 * that is not merely a smaller run: `declarationVerdict` would judge every declaration against
 * output from a run that could not have seen the pages it is judging, and report the declarations
 * sound. Wrong in the direction that reads as right. The sweep's own scope is the only scope the
 * child is given, and its absence means every page.
 */
export function childEnv(env, root, scope) {
  const given = { ...env, LEX_EVIDENCE_ROOT: root };
  delete given.LEX_EVIDENCE_PAGES;
  if (scope !== null) given.LEX_EVIDENCE_PAGES = scope;
  return given;
}

function run(root, pages) {
  return new Promise((resolveRun) => {
    const child = spawn(process.execPath, ["scripts/browser-evidence.mjs"], {
      cwd: process.cwd(),
      env: childEnv(process.env, root, scopeFor(pages, FULL)),
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

// One private copy of dist, taken before the first mutation. Every mutation starts from it rather
// than from the live dist: a build in the same checkout during the sweep (`npm run evidence`
// rebuilds dist) leaked a hand-applied stylesheet into later mutations and reported them caught for
// the wrong reason.
/**
 * What the sweep says about one mutation, and whether it counts against the head.
 *
 * Pure, so the node tests hold every line the sweep prints and every verdict it counts. The
 * browser run's exit code and output go in; the report and the verdict come out. The sweep itself
 * only prints what this returns and counts what it says, and `sweepWith` below is driven by the
 * tests with a fake run, so a call site that stops asking, or stops counting, fails there.
 *
 * @param {object} mutation  the declared mutation
 * @param {{code: number, output: string}} result  what the browser evidence run said
 * @param {boolean} full  whether every page was measured, which is when a declaration can be judged
 */
export function judgeMutation(mutation, result, full) {
  const { code, output } = result;
  if (mutation.pages !== "all" && !Array.isArray(mutation.pages)) {
    throw new Error(
      `${mutation.name} declares no pages; every mutation says where its defect can be seen, ` +
        'or "all" when it can be seen only across pages',
    );
  }
  if (code === 0) {
    return { failed: true, kind: "uncaught", report: [`STILL GREEN  ${mutation.name}`] };
  }
  if (!mutation.expect.test(output)) {
    // Every failure line, not the first four, so a wrong reason can be told from a flake.
    const lines = lineOf(output).filter((l) => /^\s+\S.*: /.test(l));
    // A run that ended without judging anything is not a wrong reason, and it has no failure
    // lines to print: its last words are what names the crash.
    const body = lines.length > 0
      ? lines.join("\n")
      : `             it judged nothing and ended ${code}; its last output:\n` +
        lineOf(output).filter((l) => l.trim() !== "").slice(-12).map((l) => `             ${l}`).join("\n");
    return { failed: true, kind: "uncaught", report: [`WRONG REASON ${mutation.name}`, body] };
  }
  const matching = lineOf(output).filter((l) => mutation.expect.test(l));
  const line = matching[0] ?? "";
  // Only a full run can judge a declaration, and only a full run has the evidence: a scoped run
  // measures the declared pages and nothing else, so every sentence it sees comes from one of them
  // by construction. A *wholly* wrong declaration still fails there, as a mutation nobody caught,
  // which is how mine failed. A partly wrong one does not: a declaration of two pages where only
  // the first catches the defect passes every scoped sweep, and only a full run names the second.
  if (full) {
    const verdict = declarationVerdict(mutation.pages, matching);
    const notes = verdict.notes.map((note) => `             also on ${note}`);
    if (verdict.failures.length > 0) {
      return {
        failed: true,
        kind: "misdeclared",
        report: [...notes, `WRONG PAGE   ${mutation.name}`, ...verdict.failures.map((f) => `             ${f}`)],
      };
    }
    return { failed: false, kind: "caught", report: [...notes, `caught       ${mutation.name}`, `             ${line.trim().slice(0, 140)}`] };
  }
  return { failed: false, kind: "caught", report: [`caught       ${mutation.name}`, `             ${line.trim().slice(0, 140)}`] };
}

/**
 * The sweep itself, with what it talks to passed in: the copy it mutates, the browser run and the
 * printing. Production passes the real three; the tests pass fakes and no browser starts, which is
 * how the counting and the reporting are held rather than asserted about a function nobody calls.
 */
export async function sweepWith({
  mutations,
  prepare,
  run,
  full = FULL,
  log = console.log,
  judge = judgeMutation,
}) {
  const counts = { uncaught: 0, misdeclared: 0 };
  for (const mutation of mutations) {
    const root = await prepare();
    try {
      await mutation.apply(root);
      const outcome = judge(mutation, await run(root, mutation.pages, mutation.name), full);
      for (const line of outcome.report) log(line);
      if (outcome.failed) {
        // A failure of a kind nobody counts would leave the count `NaN`, and `NaN > 0` is false:
        // the sweep would end 0 with failures printed above it. Refused rather than counted.
        if (!(outcome.kind in counts)) {
          throw new Error(`${mutation.name} failed as ${JSON.stringify(outcome.kind)}, which the sweep does not count`);
        }
        counts[outcome.kind] += 1;
      }
    } finally {
      await rm(root, { recursive: true, force: true });
    }
  }
  return counts;
}

/**
 * What a finished sweep says it did: how many mutations, over what, and how many of the whole set
 * were selected. Exported so the count a sweep reports is the count it swept, held by a test: the
 * selection was dropped once between the filter and this line and nothing failed.
 */
export function sweepSummary(swept, total, full) {
  const over = full ? "over every page" : "over the pages each declares";
  const selection = swept === total ? "" : ` (${swept} of ${total} selected)`;
  return `all ${swept} induced mutations were caught ${over}${selection}.`;
}

/**
 * What a sweep that failed says it found, counting the two failures apart.
 *
 * A mutation nobody caught and a mutation caught on a page its declaration does not name are not
 * the same defect and do not send the reader to the same place: the first says a gate stopped
 * working, the second says the gate works and the map to it is wrong. Counting both as "not
 * caught" tells a reader to go looking for a broken gate that is not broken.
 */
export function sweepFailureSummary({ uncaught, misdeclared }) {
  const said = [];
  if (uncaught > 0) said.push(`${uncaught} induced mutation(s) were not caught`);
  if (misdeclared > 0) {
    said.push(`${misdeclared} caught mutation(s) declare a page that caught nothing`);
  }
  return `${said.join("; ")}.`;
}

async function sweep() {
  // One private copy of dist, taken before the first mutation. Every mutation starts from it
  // rather than from the live dist: a build in the same checkout during the sweep (`npm run
  // evidence` rebuilds dist) leaked a hand-applied stylesheet into later mutations and reported
  // them caught for the wrong reason.
  // Before the copy: a selection that names nothing should cost nothing.
  const swept = mutationsToSweep(MUTATIONS, process.env.LEX_EVIDENCE_ONLY);
  const base = await mkdtemp(join(tmpdir(), "lex-evidence-base-"));
  let counts;
  try {
    await cp(join(process.cwd(), "dist"), base, { recursive: true });
    counts = await sweepWith({
      mutations: swept,
      prepare: async () => {
        const root = await mkdtemp(join(tmpdir(), "lex-evidence-"));
        await cp(base, root, { recursive: true });
        return root;
      },
      run,
    });
  } finally {
    // A copy of the whole build, left behind on every throw between here and the end.
    await rm(base, { recursive: true, force: true });
  }

  if (counts.uncaught + counts.misdeclared > 0) {
    console.error(`\n${sweepFailureSummary(counts)}`);
    process.exit(1);
  }
  console.log(`\n${sweepSummary(swept.length, MUTATIONS.length, FULL)}`);
}

// Only when invoked directly, so the list can be imported and its declarations proven without
// running a single browser. Real paths on both sides: through a junction, comparing the spellings
// makes this file a script that sweeps nothing, says nothing and exits 0.
if (invokedDirectly(import.meta.url, process.argv[1])) {
  await sweep();
}
