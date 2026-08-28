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

test("the notice renders the frozen copy, both actions, and rebuilt links", () => {
  const html = render([
    { work: "lu-legilux:loi-2008-07-09-a105", title: "Loi sur les chiens" },
    { work: "eu-eurlex:reg-2016-679", title: "Regulation" },
  ]);
  assert.ok(html.includes('data-testid="metadata-only-notice"'));
  assert.equal(html.split(`<b>${METADATA_ONLY_HEADING}</b>`).length - 1, 1);
  assert.ok(html.includes(METADATA_ONLY_BODY.replace(/"/g, "&quot;"))
    || html.includes(METADATA_ONLY_BODY), "complete frozen body must render");
  assert.ok(html.includes(`<summary>${METADATA_ONLY_DISCLOSURE}</summary>`),
    "matches live under the subordinate disclosure");
  assert.ok(html.includes('href="/lu-legilux/loi-2008-07-09-a105"'),
    "the link is rebuilt from the parsed coordinate");
  assert.ok(html.includes("matched in metadata"));
  assert.ok(html.includes('href="/coverage"'), "the coverage action is present");
  // Both agreed actions: coverage plus one exact-host official search per collection.
  assert.ok(html.includes('href="https://legilux.public.lu"'));
  assert.ok(html.includes('href="https://eur-lex.europa.eu"'));
});

test("hostile rows are dropped without suppressing the notice or guessing hosts", () => {
  const html = render([
    { work: "evil host:w1", title: "bad publisher" },
    { work: "lu-legilux:../../etc", title: "traversal" },
    { work: "nocolon", title: "no coordinate" },
    { work: "lu-legilux:javascript:alert(1)", title: "scheme in group" },
    { work: 42 as unknown as string, title: "wrong type" },
  ]);
  assert.ok(html.includes(METADATA_ONLY_HEADING), "notice survives all-invalid evidence");
  assert.ok(!html.includes("<details"), "no disclosure from invalid rows");
  assert.ok(!html.includes("evil host"));
  assert.ok(!html.includes("../../etc"));
  assert.ok(!html.includes("javascript:alert"));
  assert.ok(!html.includes("https://"), "no official host is guessed from invalid rows");

  // An unknown but well-formed collection falls back internally, never to a guessed URL.
  const unknown = render([{ work: "x-unknown:w1", title: "t" }]);
  assert.ok(!unknown.includes("https://"));
  assert.ok(unknown.includes('href="/search"'));
});

test("the disclosure is capped at ten, deduplicated, and absent when nothing validates", () => {
  const many = Array.from({ length: 14 }, (_, index) => ({
    work: `lu-legilux:w-${index}`, title: `Work ${index}` }));
  const html = render(many);
  assert.equal(html.split("<li>").length - 1, 10, "at most ten disclosed matches");

  const duplicated = render([
    { work: "lu-legilux:w-1", title: "Work" },
    { work: "lu-legilux:w-1", title: "Work again" },
  ]);
  assert.equal(duplicated.split("<li>").length - 1, 1, "one row per logical work");

  const bare = render([]);
  assert.ok(bare.includes(METADATA_ONLY_HEADING), "the notice never depends on the list");
  assert.ok(!bare.includes("<details"), "no empty disclosure shell");
});
