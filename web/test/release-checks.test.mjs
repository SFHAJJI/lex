// The release path's checks against the release serving now (`scripts/release-checks.mjs`; the audit's gap 6): R88's manifest
// diff over a small real extract (Release 1's signed manifest and mount report, as published on 2026-10-26) and a candidate,
// the checks' reports read and their refusals named (R106, R86(2)), the serving release read back under its signer's key, the
// published checks held to the manifest they describe, and the rules for when a release names the release serving now.
import assert from "node:assert/strict";
import { generateKeyPairSync } from "node:crypto";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  COMPARISON_FILE, CONSOLIDATION_TEXT_FILE, DIFF_ASSETS, MANIFEST_DIFF_FILE, MANIFEST_DIFF_PAGE, MANIFEST_DIFF_SCHEMA,
  explanationsFailures, manifestDiff, publishedCheckFailures, releaseCheckFailures, servingReleaseFailures,
} from "../scripts/release-checks.mjs";
import { releaseCheckOptionFailures, releaseSigner, rehearsalSigner, servingCommandOptions } from "../scripts/image-rehearsal.mjs";
import { publishRelease, releaseManifestOf, releaseVersion } from "../scripts/release-assets.mjs";

// Release 1 as published (v3-20261026T180319Z-8958ec5fa63f): its manifest, and its mount report's files and counts.
const SERVING_MANIFEST = {
  schema: "lex-v3-release/1",
  version: "v3-20261026T180319Z-8958ec5fa63f",
  rehearsal: false,
  signer: "lex-v3-release",
  source: { epoch: 1793037799, from: "commit", commit: "8958ec5fa63f54de1e003fc7be7c921521fe09b3", clean: true },
  image: { manifest_digest: "sha256:93cdd51ef83f5c8f96478014e5ce89eb734c7d97c59172f4d294c348052f96b2" },
  corpus: { sha256: "9e218b39db8b6d30b976351a34e762ef2ef88c6658a7779ab715728d2b214c94" },
  assets: [
    { name: "evaluation-card.json", size: 13987, sha256: "58c643ecc9e3afd59987b1ac6fd64dff30bd18d3f08734a46dde5edff5669e24" },
    { name: "lex-v3-image.oci.tar", size: 2672865792, sha256: "574d804fe26d38ea0dad45fbff69117a2a95fb29edac7be2db46fff720d91d57" },
    { name: "lex-v3-image.sig.json", size: 705, sha256: "b8d68bc7834f54aa2b5ae7ddbe9952d6f8f06045b4e9fe5a784c9315653e8796" },
    { name: "mount-report.json", size: 5311, sha256: "428d190fcc3f6db0c81ee714b6374bcd534395aeb77f1c660e6926a5e77d9072" },
  ],
};
const SERVING_REPORT = Buffer.from(JSON.stringify({
  schema: "lex-v3-first-mount-report/1",
  derivationInputUtc: "2026-10-12T18:17:05+00:00",
  corpusMembers: 262092,
  builtTwiceAndEqual: true,
  files: [
    { Name: "lex-corpus-6.json", ByteLength: 206850906, Sha256: "bad814f77b453a836f1175a2c85866407fdccdd14bcc66a51dea02f206f993ea" },
    { Name: "luxembourg-index.sqlite3", ByteLength: 909426688, Sha256: "6d7d969cb1da5b07df6a6d87ceadd0cfa5ae0886d572149b210bc1b3d9aab530" },
    { Name: "europe-index.sqlite3", ByteLength: 1403584512, Sha256: "3083749f301bd62eaa9f3f28b731480160c06b91afd13353c6873684891be9e7" },
  ],
}));

/** A candidate's mount report with a text yield (R86(4)): Luxembourg's codes and lois, as derive-r1 reports them, and a drop. */
function candidateReport({ loiArticles = 7586 } = {}) {
  const row = (family, kind, versions, withText, works, worksWithText, articles, files, filesWithText) => ({
    family, kind, works, works_with_text: worksWithText, versions, versions_with_text: withText, articles, files, files_with_text: filesWithText,
  });
  return Buffer.from(JSON.stringify({
    schema: "lex-v3-first-mount-report/2",
    derivationInputUtc: "2026-10-12T18:17:05+00:00",
    corpusMembers: 262092,
    builtTwiceAndEqual: true,
    files: [
      { Name: "lex-corpus-6.json", ByteLength: 247350034, Sha256: "8239fb0665cd164965bea053641290c5dcbe871e3dc901cd77e29000c2e20d74" },
      { Name: "luxembourg-index.sqlite3", ByteLength: 1378668544, Sha256: "da7725df60c95223432c51aec847f4bf518a932499373ac3dd9b3d54210e42b5" },
      { Name: "europe-index.sqlite3", ByteLength: 1403584512, Sha256: "3083749f301bd62eaa9f3f28b731480160c06b91afd13353c6873684891be9e7" },
    ],
    textYield: {
      schema: "lex-v3-text-yield/1",
      publishers: [{
        publisher: "lu-legilux",
        classes: [row("code", "code", 914, 126, 17, 8, 119730, 189, 126), row("loi", "as_published", 9238, 689, 9238, 689, loiArticles, 3108, 689)],
      }],
    },
  }));
}

const CHECK_ASSETS = [
  [CONSOLIDATION_TEXT_FILE, Buffer.from(JSON.stringify({ schema: "lex-v3-consolidation-text/1", verdict: "pass", unexplained: 0, listed: [] }))],
  [COMPARISON_FILE, Buffer.from(JSON.stringify({ schema: "lex-v3-release-comparison/1", verdict: "pass", unexplained: 0, items_listed: 0, items: [] }))],
];

test("R88's diff says what the release serving now and the candidate hold, side by side, and names every drop beyond ten per cent", () => {
  const signer = rehearsalSigner();
  const source = { epoch: 1793120000, from: "commit", commit: "0123456789abcdef0123456789abcdef01234567", clean: true };
  const assets = [["evaluation-card.json", Buffer.from("card")], ["mount-report.json", candidateReport({ loiArticles: 6000 })], ...CHECK_ASSETS];
  const manifest = releaseManifestOf({ version: releaseVersion(source), source, manifestDigest: "sha256:" + "e".repeat(64), corpusSha256: "0746826f74ed3a0da6c22a04897c0feb35d3383c350070033358f7a39fbc6f3e", assets, signer });
  const { json, page, document } = manifestDiff({
    serving: { manifest: SERVING_MANIFEST, mountReport: SERVING_REPORT, manifestSha256: "f".repeat(64) },
    candidate: { manifest, mountReport: candidateReport({ loiArticles: 6000 }) },
  });
  assert.equal(document.schema, MANIFEST_DIFF_SCHEMA);
  assert.deepEqual(JSON.parse(json.toString("utf8")), document, "the JSON is the document");
  const field = (name) => document.fields.find((row) => row.field === name);
  assert.equal(field("version").change, "changed");
  assert.equal(field("corpus_sha256").serving, "9e218b39db8b6d30b976351a34e762ef2ef88c6658a7779ab715728d2b214c94");
  assert.equal(field("signer").change, "changed", "a rehearsal is not the release signer");
  const asset = (name) => document.assets.find((row) => row.name === name)?.change;
  assert.equal(asset("lex-v3-image.oci.tar"), "removed");
  assert.equal(asset("evaluation-card.json"), "changed");
  assert.equal(asset(COMPARISON_FILE), "added");
  assert.ok(document.assets.every((row) => !DIFF_ASSETS.includes(row.name)), "the diff never lists itself");
  const file = (name) => document.mount_files.find((row) => row.name === name)?.change;
  assert.equal(file("europe-index.sqlite3"), "kept");
  assert.equal(file("luxembourg-index.sqlite3"), "changed");
  assert.equal(document.mount_counts.find((row) => row.field === "schema").change, "changed");
  assert.equal(document.text_yield.serving, "not reported (the mount report carries no text yield)");
  assert.equal(document.text_yield_classes.length, 2);
  // Against the serving release, which reports no yield, every class is added; the drop shows between two candidates.
  assert.deepEqual(document.drops_beyond_ten_percent, []);
  const before = releaseManifestOf({ version: releaseVersion(source), source, manifestDigest: "sha256:" + "e".repeat(64), corpusSha256: "c".repeat(64), assets, signer });
  const twoCandidates = manifestDiff({ serving: { manifest: before, mountReport: candidateReport() }, candidate: { manifest, mountReport: candidateReport({ loiArticles: 6000 }) } });
  assert.deepEqual(twoCandidates.document.drops_beyond_ten_percent, [{ class: "lu-legilux loi/as_published", count: "articles", serving: 7586, candidate: 6000, per_cent: -20.9 }]);
  const text = twoCandidates.page.toString("utf8");
  assert.match(text, /^# Release v3-rehearsal-\S+ against v3-rehearsal-\S+$/m);
  assert.match(text, /- lu-legilux loi\/as_published, articles: 7586 to 6000 \(-20\.9 per cent\)\./);
  assert.match(page.toString("utf8"), /\| lex-v3-image\.oci\.tar \| 2672865792 B, 574d804fe26d… \| none \| removed \|/);
  assert.match(page.toString("utf8"), /## Drops beyond ten per cent\n\nNone\./);
});

test("the checks' reports refuse the release with each unexplained item named, and a verdict that does not follow from them is a failure", () => {
  assert.deepEqual(releaseCheckFailures({ comparison: CHECK_ASSETS[1][1], consolidation: CHECK_ASSETS[0][1] }), []);
  const item = (key, explained) => ({
    change: "version_identity_changed", publisher: "lu-legilux", family: "loi", kind: "as_published", key, address: `/lu-legilux/${key}`,
    serving: {}, candidate: {}, explained_by: explained ? { reason: "derivation_rule_changed", source: "explanations", entry: 0 } : null,
  });
  const comparison = Buffer.from(JSON.stringify({
    schema: "lex-v3-release-comparison/1", verdict: "refuse", unexplained: 1, items_listed: 2, items: [item("a", true), item("b", false)],
  }));
  assert.deepEqual(releaseCheckFailures({ comparison }), [
    "R106: version_identity_changed at /lu-legilux/b (lu-legilux loi/as_published; key b), explained by no typed reason",
  ]);
  const consolidation = Buffer.from(JSON.stringify({
    schema: "lex-v3-consolidation-text/1", verdict: "refuse", unexplained: 1,
    listed: [
      { publisher: "eu-eurlex", family: "eu", kind: "consolidation", identity: null, object_ref_sha256: "a".repeat(64), body_sha256: "b".repeat(64), outcome: "formex_main_body_package_rejected", blocking: true },
      { publisher: "lu-legilux", family: "code", kind: "code", identity: "http://data.legilux.public.lu/eli/etat/leg/code/civil/20190428", object_ref_sha256: "c".repeat(64), body_sha256: "d".repeat(64), outcome: "akn_upstream_not_inventoried", blocking: false },
    ],
  }));
  assert.deepEqual(releaseCheckFailures({ consolidation }), [
    `R86(2): eu-eurlex eu/consolidation file ${"a".repeat(64)} (body ${"b".repeat(64)}) is admitted and yields no text: formex_main_body_package_rejected, no typed, explained outcome`,
  ]);
  // A verdict that says pass over an unexplained item, or counts otherwise, is no verdict.
  const lying = Buffer.from(JSON.stringify({ schema: "lex-v3-release-comparison/1", verdict: "pass", unexplained: 0, items: [item("b", false)] }));
  assert.ok(releaseCheckFailures({ comparison: lying }).includes("the release-over-release comparison's verdict does not follow from its items"));
  // Many items: the first forty are named and the rest counted.
  const many = Buffer.from(JSON.stringify({ schema: "lex-v3-release-comparison/1", verdict: "refuse", unexplained: 45, items: Array.from({ length: 45 }, (_, index) => item(`k${index}`, false)) }));
  const named = releaseCheckFailures({ comparison: many });
  assert.equal(named.length, 41);
  assert.equal(named.at(-1), `R106: and 5 more unexplained changes, each listed in ${COMPARISON_FILE}`);
});

test("a published release's checks may not refuse it, and its manifest diff must describe its own manifest", () => {
  const signer = rehearsalSigner();
  const source = { epoch: 1793120000, from: "commit", commit: "0123456789abcdef0123456789abcdef01234567", clean: true };
  const assets = [["evaluation-card.json", Buffer.from("card")], ["mount-report.json", candidateReport()], ...CHECK_ASSETS];
  const releaseOf = { version: releaseVersion(source), source, manifestDigest: "sha256:" + "e".repeat(64), corpusSha256: "c".repeat(64), signer };
  const diff = manifestDiff({ serving: { manifest: SERVING_MANIFEST, mountReport: SERVING_REPORT }, candidate: { manifest: releaseManifestOf({ ...releaseOf, assets }), mountReport: candidateReport() } });
  const published = [...assets, [MANIFEST_DIFF_FILE, diff.json], [MANIFEST_DIFF_PAGE, diff.page]];
  const manifest = releaseManifestOf({ ...releaseOf, assets: published });
  assert.deepEqual(publishedCheckFailures(new Map(published), manifest), []);
  assert.deepEqual(publishedCheckFailures(new Map(assets.slice(0, 2)), releaseManifestOf({ ...releaseOf, assets: assets.slice(0, 2) })), [], "a release before these checks carries none");

  // An asset changed after the diff was made: the diff no longer describes the release.
  const changed = published.map(([name, bytes]) => [name, name === "evaluation-card.json" ? Buffer.from("another card") : bytes]);
  assert.deepEqual(publishedCheckFailures(new Map(changed), releaseManifestOf({ ...releaseOf, assets: changed })),
    [`${MANIFEST_DIFF_FILE} does not describe this release: its version and every asset but the diff's own must be the manifest's`]);
  // A comparison that refuses cannot be published.
  const refusing = published.map(([name, bytes]) => [name, name === COMPARISON_FILE
    ? Buffer.from(JSON.stringify({ schema: "lex-v3-release-comparison/1", verdict: "refuse", unexplained: 1, items: [{ change: "version_lost", publisher: "lu-legilux", family: "loi", kind: "as_published", key: "k", address: "/a", explained_by: null }] }))
    : bytes]);
  assert.match(publishedCheckFailures(new Map(refusing), manifest)[0], /^the release carries a check that refuses it: R106: version_lost at \/a/);
  // The page goes with the JSON.
  assert.ok(publishedCheckFailures(new Map(published.filter(([name]) => name !== MANIFEST_DIFF_PAGE)), manifest).includes(`the release carries ${MANIFEST_DIFF_FILE} without ${MANIFEST_DIFF_PAGE}`));
});

test("the release serving now is read back under its signer's key: its manifest's signature, its signer and its mount report", async () => {
  const { privateKey } = generateKeyPairSync("ec", { namedCurve: "P-256" });
  const pem = privateKey.export({ type: "pkcs8", format: "pem" });
  const signer = releaseSigner({ privateKeyPem: pem, identity: "lex-v3-release" });
  const root = await mkdtemp(join(tmpdir(), "lex-serving-release-"));
  try {
    const source = { epoch: 1793037799, from: "commit", commit: "8958ec5fa63f54de1e003fc7be7c921521fe09b3", clean: true };
    const version = releaseVersion(source, { rehearsal: false });
    const { directory } = await publishRelease(root, {
      version, source, manifestDigest: "sha256:" + "e".repeat(64), corpusSha256: "c".repeat(64), signer,
      assets: [["mount-report.json", SERVING_REPORT], ["evaluation-card.json", Buffer.from("card")]],
    });
    const read = await servingReleaseFailures(directory, { publicKeyPem: signer.publicKeyPem, identity: "lex-v3-release" });
    assert.deepEqual(read.failures, []);
    assert.equal(read.manifest.version, version);
    assert.ok(read.mountReport.equals(SERVING_REPORT));
    assert.deepEqual(explanationsFailures(Buffer.from(JSON.stringify({ schema: "lex-v3-release-explanations/1", serving_release: version })), version), []);
    assert.deepEqual(explanationsFailures(Buffer.from(JSON.stringify({ schema: "lex-v3-release-explanations/1", serving_release: "v3-other" })), version),
      [`the release's explanations explain changes from v3-other, and the release serving now is ${version}`]);

    // Another identity, another key, a changed mount report: each refused.
    assert.deepEqual((await servingReleaseFailures(directory, { publicKeyPem: signer.publicKeyPem, identity: "someone-else" })).failures,
      ["the serving release is not signed by the release signer someone-else"]);
    const other = generateKeyPairSync("ec", { namedCurve: "P-256" }).publicKey.export({ type: "spki", format: "pem" });
    assert.deepEqual((await servingReleaseFailures(directory, { publicKeyPem: other, identity: "lex-v3-release" })).failures,
      ["the serving release's manifest signature does not verify with the release signer's key"]);
    await writeFile(join(directory, "mount-report.json"), Buffer.concat([await readFile(join(directory, "mount-report.json")), Buffer.from(" ")]));
    assert.deepEqual((await servingReleaseFailures(directory, { publicKeyPem: signer.publicKeyPem, identity: "lex-v3-release" })).failures,
      ["the serving release's mount-report.json is not the one its manifest records"]);
    // A rehearsal never stands for the release serving now.
    const rehearsal = rehearsalSigner();
    const rehearsed = await publishRelease(root, { version: releaseVersion(source), source, manifestDigest: "sha256:" + "e".repeat(64), corpusSha256: "c".repeat(64), signer: rehearsal, assets: [["mount-report.json", SERVING_REPORT]] });
    assert.deepEqual((await servingReleaseFailures(rehearsed.directory, { publicKeyPem: rehearsal.publicKeyPem, identity: "lex-v3-release" })).failures,
      ["the serving release is not signed by the release signer lex-v3-release"]);
    assert.deepEqual((await servingReleaseFailures(join(root, "nothing"), { publicKeyPem: rehearsal.publicKeyPem, identity: "lex-v3-release" })).failures,
      [`${join(root, "nothing")} holds no signed release manifest`]);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("a release signed by the release signer names the release serving now; a rehearsal may leave it out; the options go together", () => {
  assert.deepEqual(releaseCheckOptionFailures({ signed: false, mountKind: "real" }), []);
  assert.deepEqual(releaseCheckOptionFailures({ signed: true, mountKind: "real", servingRelease: "r", servingMount: "m", explanations: "e" }), []);
  assert.deepEqual(releaseCheckOptionFailures({ signed: true, mountKind: "real" }),
    ["a release signed by the release signer is compared with the release serving now (R106): name it with --serving-release <its directory> --serving-mount <its mount>"]);
  assert.deepEqual(releaseCheckOptionFailures({ signed: false, mountKind: "real", servingRelease: "r" }),
    ["--serving-release and --serving-mount go together: the release serving now and the mount it serves"]);
  assert.deepEqual(releaseCheckOptionFailures({ signed: false, mountKind: "real", explanations: "e" }),
    ["--release-explanations explain changes from the release serving now, which --serving-release names"]);
  assert.deepEqual(releaseCheckOptionFailures({ signed: false, mountKind: "fixture", servingRelease: "r", servingMount: "m" }),
    ["the journey's fixture mount is never compared with a release"]);
  assert.deepEqual(servingCommandOptions(["--mount", "m", "--serving-release", "C:/r", "--serving-mount", "C:/m", "--release-explanations", "x.json"]),
    { servingRelease: "C:/r", servingMount: "C:/m", servingPublicKey: null, servingSigner: null, explanations: "x.json" });
});
