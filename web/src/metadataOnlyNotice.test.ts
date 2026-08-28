import assert from "node:assert/strict";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import {
  METADATA_ONLY_BODY, METADATA_ONLY_DISCLOSURE, METADATA_ONLY_HEADING,
} from "./matchLanes.ts";
import { MetadataOnlyNotice, type MetadataOnlyWork } from "./metadataOnlyNotice.ts";

// These render the REAL component the workspace mounts in the metadata_only state.
const render = (works: readonly MetadataOnlyWork[]) =>
  renderToStaticMarkup(h(MetadataOnlyNotice, { works }));

test("the notice renders the frozen copy with the disclosed matches beneath it", () => {
  const html = render([
    { work: "lu-legilux:loi-2008-07-09-a105", title: "Loi sur les chiens" },
  ]);
  assert.ok(html.includes('data-testid="metadata-only-notice"'));
  assert.equal(html.split(`<b>${METADATA_ONLY_HEADING}</b>`).length - 1, 1);
  assert.ok(html.includes(METADATA_ONLY_BODY.replace(/"/g, "&quot;"))
    || html.includes(METADATA_ONLY_BODY), "complete frozen body must render");
  assert.ok(html.includes(`<summary>${METADATA_ONLY_DISCLOSURE}</summary>`),
    "matches live under the subordinate disclosure");
  assert.ok(html.includes('href="/lu-legilux/loi-2008-07-09-a105"'),
    "the link is rebuilt from the work coordinate");
  assert.ok(html.includes("matched in metadata"));
  assert.ok(html.includes('href="/coverage"'), "the coverage action is present");
});

test("the disclosure is capped at ten and absent when nothing validates", () => {
  const many = Array.from({ length: 14 }, (_, index) => ({
    work: `lu-legilux:w-${index}`, title: `Work ${index}` }));
  const html = render(many);
  assert.equal(html.split("<li>").length - 1, 10, "at most ten disclosed matches");

  const bare = render([]);
  assert.ok(bare.includes(METADATA_ONLY_HEADING), "the notice never depends on the list");
  assert.ok(!bare.includes("<details"), "no empty disclosure shell");
});
