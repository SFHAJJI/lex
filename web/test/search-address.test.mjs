// A search in the page's address, after "#" (the plan's item 3.5; the specification of record's R109): the fragment's round
// trip, what it refuses and how safely, and what it leaves alone.
import assert from "node:assert/strict";
import test from "node:test";

import { SEARCH_FRAGMENT_KEYS, SEARCH_FRAGMENT_MAX, readSearchFragment, searchFragment } from "../scripts/search-address.mjs";
import { SEARCH_QUERY_MAX, searchParameters } from "../scripts/live-search.mjs";

const PHRASE = "congé parental";

test("a search is written after # in a fixed order, each value percent-encoded, the work and the date only when given", () => {
  assert.deepEqual(SEARCH_FRAGMENT_KEYS, ["q", "lang", "work", "date"]);
  assert.equal(searchFragment({ query: PHRASE, language: "fra" }), "#q=cong%C3%A9%20parental&lang=fra");
  assert.equal(searchFragment({ query: PHRASE, language: "fra", date: "2026-10-18" }), "#q=cong%C3%A9%20parental&lang=fra&date=2026-10-18");
  assert.equal(
    searchFragment({ query: "personal data", language: "eng", identifier: "32016R0679", date: "" }),
    "#q=personal%20data&lang=eng&work=32016R0679",
  );
  // Every character that would end a value or the fragment, or start another, is escaped.
  assert.equal(searchFragment({ query: "a&b=c#d%e+f?g", language: "fra" }), "#q=a%26b%3Dc%23d%25e%2Bf%3Fg&lang=fra");
});

test("a fragment round-trips to the very request the form sends, the phrase byte for byte", () => {
  const requests = [
    { query: PHRASE, language: "fra", identifier: "", date: "" },
    { query: PHRASE, language: "fra", identifier: "", date: "2026-10-18" },
    { query: "  espaces autour  ", language: "deu", identifier: "", date: "2024-02-29" },
    { query: "personal data", language: "eng", identifier: "32016R0679", date: "" },
    { query: "« guillemets » d’apostrophe’ 😀 L. 121-6", language: "fra", identifier: "/lu-legilux/loi-1991-08-10-n3", date: "2000-01-01" },
    { query: "a&b=c#d%e+f?g", language: "fra", identifier: "", date: "" },
    { query: "é".repeat(SEARCH_QUERY_MAX), language: "fra", identifier: "", date: "" },
    { query: "\u{1F600}".repeat(SEARCH_QUERY_MAX / 2), language: "fra", identifier: "", date: "" },
  ];
  for (const request of requests) {
    const fragment = searchFragment(request);
    assert.ok(fragment.length <= SEARCH_FRAGMENT_MAX, `${fragment.length} characters`);
    const read = readSearchFragment(fragment);
    assert.equal(read.state, "search", `${JSON.stringify(request)}: ${read.reason}`);
    assert.deepEqual({ ...read.request }, request);
    assert.deepEqual(searchParameters(read.request), searchParameters(request), "the same parameters are sent");
  }
});

test("the home page's form names no work: it writes none and reads none", () => {
  assert.throws(() => searchFragment({ query: PHRASE, language: "fra", identifier: "32016R0679" }, { work: false }), /no named work/);
  assert.equal(searchFragment({ query: PHRASE, language: "fra", date: "2026-10-18" }, { work: false }), "#q=cong%C3%A9%20parental&lang=fra&date=2026-10-18");
  assert.equal(readSearchFragment("#q=x&lang=eng&work=32016R0679", { work: false }).state, "refused");
  assert.equal(readSearchFragment("#q=x&lang=eng&work=32016R0679").state, "search");
});

test("no fragment, or a position after #, is no search: nothing is asked and nothing is said", () => {
  for (const hash of [undefined, null, "", "#", "#main", "#art_15", "#L.%20121-6", "#%C3%A9", "#a&b"]) {
    assert.equal(readSearchFragment(hash).state, "none", JSON.stringify(hash));
  }
});

test("a fragment written as a search that the form will not send is refused, whole, with a reason that repeats none of it", () => {
  const refused = [
    ["no phrase", "#lang=fra"],
    ["an empty phrase", "#q=&lang=fra"],
    ["a blank phrase", "#q=%20%20&lang=fra"],
    ["a key it does not know", "#q=x&lang=fra&mode=relaxed"],
    ["a query string's key", "#query=x"],
    ["a key twice", "#q=x&q=y"],
    ["a part with no value", "#q=x&lang"],
    ["an empty key", "#=x&q=y"],
    ["a value that does not decode", "#q=%E9t%C3"],
    ["a lone percent", "#q=100%"],
    ["an empty value", "#q=x&date="],
    ["a language the form does not offer", "#q=x&lang=ita"],
    ["a language in capitals", "#q=x&lang=FRA"],
    ["a date that is no calendar date", "#q=x&date=2025-02-29"],
    ["a date in another shape", "#q=x&date=18%2F10%2F2026"],
    ["a date with a space", "#q=x&date=%202026-10-18"],
    ["a blank work", "#q=x&work=%20"],
    ["a phrase past the platform's ceiling", `#q=${"a".repeat(SEARCH_QUERY_MAX + 1)}`],
    ["a phrase of too many words", `#q=${Array.from({ length: 33 }, (_, index) => `w${index}`).join("%20")}`],
    ["a fragment past its ceiling", `#q=${"%C3%A9".repeat(SEARCH_FRAGMENT_MAX)}`],
  ];
  for (const [what, hash] of refused) {
    const read = readSearchFragment(hash);
    assert.equal(read.state, "refused", what);
    assert.equal(read.request, undefined, `${what}: no request`);
    assert.ok(!read.reason.includes("<") && read.reason.length < 200, `${what}: ${read.reason}`);
  }
  // A phrase in the fragment is never echoed in the reason, whatever it holds.
  assert.ok(!readSearchFragment("#q=secret%20words&lang=ita").reason.includes("secret"));
});

test("a fragment with no language takes the form's first, and the writer refuses what the form would not send", () => {
  assert.deepEqual({ ...readSearchFragment("#q=cong%C3%A9").request }, { query: "congé", language: "fra", identifier: "", date: "" });
  for (const request of [
    { query: "", language: "fra" },
    { query: "x", language: "ita" },
    { query: "x", language: "fra", date: "2025-02-29" },
    { query: "a".repeat(SEARCH_QUERY_MAX + 1), language: "fra" },
  ]) {
    assert.throws(() => searchFragment(request), JSON.stringify(request));
  }
});
