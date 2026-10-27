// The release path's checks against the release serving now (the audit's gap 6), run by the release command
// (`image-rehearsal.mjs`) before it builds anything, and published with the release:
// - R106, the release-over-release check: `Lex.V3.Tool release-checks` compares the candidate's mount with the mount of the
//   release serving now (located by `--serving-release` and `--serving-mount`; the mount is held first to that release's
//   signed mount report), per publisher and class, works, versions, articles and annexes kept, added, changed and lost. Any
//   lost or changed served identity or text digest that no typed reason explains (the data's, or an entry of the release's
//   explanations, `--release-explanations`) refuses the release, with each item listed. Written as `release-comparison.json`.
// - R86(2): the same command lists every admitted consolidation file that yields no text, by identity, with its typed outcome;
//   a failure that is no typed, explained outcome refuses the release. Written as `consolidation-text.json`, always.
// - R88, the manifest diff: the serving release's signed manifest and mount report against the candidate's, as JSON
//   (`manifest-diff.json`) and as a page a person reads (`manifest-diff.md`): version, source, image, corpus, each asset and
//   each mount file, kept, added, changed or removed, the mount's counts and the text yield per class with their changes, and
//   every drop beyond ten per cent named. The diff covers every asset but itself; the read-back holds it to the manifest.
// A release signed by the release signer must name the release serving now; a rehearsal may leave it out, and says so.

import { spawnSync } from "node:child_process";
import { createPublicKey, verify } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdir, readFile } from "node:fs/promises";
import { basename, join } from "node:path";
import { sha256Hex } from "./large-bytes.mjs";

export const COMPARISON_FILE = "release-comparison.json";
export const CONSOLIDATION_TEXT_FILE = "consolidation-text.json";
export const EXPLANATIONS_FILE = "release-explanations.json";
export const MANIFEST_DIFF_FILE = "manifest-diff.json";
export const MANIFEST_DIFF_PAGE = "manifest-diff.md";
export const MANIFEST_DIFF_SCHEMA = "lex-v3-manifest-diff/1";
export const COMPARISON_SCHEMA = "lex-v3-release-comparison/1";
export const CONSOLIDATION_TEXT_SCHEMA = "lex-v3-consolidation-text/1";
export const EXPLANATIONS_SCHEMA = "lex-v3-release-explanations/1";
/** The release manifest's and its signature's names (as `release-assets.mjs` writes them). */
const MANIFEST = "release-manifest.json";
const SIGNATURE = "release-manifest.sig.json";
const MOUNT_REPORT = "mount-report.json";
/** The assets the diff describes the release by, which are therefore not in its own asset list. */
export const DIFF_ASSETS = Object.freeze([MANIFEST_DIFF_FILE, MANIFEST_DIFF_PAGE]);
/** How many items a refusal names in its message; the report lists them all. */
const NAMED = 40;

/**
 * The release serving now, read back for the comparison, as `{ failures, manifest, mountReport }`: its manifest's signature
 * under `publicKeyPem`, signed by the release signer `identity` and never a rehearsal, its version naming its directory, and
 * its mount report present with the size and digest the manifest records. The image is not read again here: the serving
 * release was read back whole when it was deployed, and only its manifest and mount report are compared.
 */
export async function servingReleaseFailures(directory, { publicKeyPem, identity }) {
  const failures = [];
  if (!existsSync(join(directory, MANIFEST)) || !existsSync(join(directory, SIGNATURE))) {
    return { failures: [`${directory} holds no signed release manifest`], manifest: null, mountReport: null };
  }
  const manifestBytes = await readFile(join(directory, MANIFEST));
  let signed = null;
  try { signed = JSON.parse(await readFile(join(directory, SIGNATURE), "utf8")); } catch { failures.push("the serving release's manifest signature is not JSON"); }
  if (signed !== null && !verify("sha256", manifestBytes, createPublicKey(publicKeyPem), Buffer.from(signed.signature ?? "", "base64"))) {
    failures.push("the serving release's manifest signature does not verify with the release signer's key");
  }
  const manifest = JSON.parse(manifestBytes.toString("utf8"));
  if (manifest.rehearsal !== false || manifest.signer !== identity || signed?.signer !== identity || signed?.rehearsal !== false) {
    failures.push(`the serving release is not signed by the release signer ${identity}`);
  }
  if (basename(directory) !== manifest.version) failures.push(`the serving release is published as ${basename(directory)}, not as its version ${manifest.version}`);
  const listed = (manifest.assets ?? []).find((asset) => asset.name === MOUNT_REPORT);
  let mountReport = null;
  if (!listed) failures.push(`the serving release lists no ${MOUNT_REPORT}`);
  else if (!existsSync(join(directory, MOUNT_REPORT))) failures.push(`the serving release holds no ${MOUNT_REPORT}`);
  else {
    mountReport = await readFile(join(directory, MOUNT_REPORT));
    if (mountReport.length !== listed.size || sha256Hex(mountReport) !== listed.sha256) failures.push(`the serving release's ${MOUNT_REPORT} is not the one its manifest records`);
  }
  return { failures, manifest, mountReport };
}

/** A release's explanations file's serving release, or a failure: it must name the release it is compared with. */
export function explanationsFailures(bytes, servingVersion) {
  let parsed;
  try { parsed = JSON.parse(bytes.toString("utf8")); } catch { return ["the release's explanations are not JSON"]; }
  if (parsed?.schema !== EXPLANATIONS_SCHEMA) return [`the release's explanations are not ${EXPLANATIONS_SCHEMA}`];
  return parsed.serving_release === servingVersion ? [] : [`the release's explanations explain changes from ${parsed.serving_release}, and the release serving now is ${servingVersion}`];
}

/**
 * Why the checks' reports refuse the release, each item named (the first `NAMED` of each report, then how many more): the
 * comparison's unexplained changes and R86(2)'s unexplained consolidation files. Empty when both pass. A report that is not
 * its schema, or whose verdict does not follow from its items, is a failure too.
 */
export function releaseCheckFailures({ comparison = null, consolidation = null }) {
  const failures = [];
  if (comparison !== null) {
    const report = JSON.parse(comparison.toString("utf8"));
    if (report.schema !== COMPARISON_SCHEMA) failures.push(`the release-over-release comparison is not ${COMPARISON_SCHEMA}`);
    const unexplained = (report.items ?? []).filter((item) => item.explained_by === null);
    if ((report.verdict === "pass") !== (unexplained.length === 0) || report.unexplained !== unexplained.length) {
      failures.push("the release-over-release comparison's verdict does not follow from its items");
    }
    failures.push(...unexplained.slice(0, NAMED).map((item) => `R106: ${item.change} at ${item.address} (${item.publisher} ${item.family}/${item.kind}; key ${item.key}), explained by no typed reason`));
    if (unexplained.length > NAMED) failures.push(`R106: and ${unexplained.length - NAMED} more unexplained changes, each listed in ${COMPARISON_FILE}`);
  }
  if (consolidation !== null) {
    const report = JSON.parse(consolidation.toString("utf8"));
    if (report.schema !== CONSOLIDATION_TEXT_SCHEMA) failures.push(`the consolidation text check is not ${CONSOLIDATION_TEXT_SCHEMA}`);
    const blocking = (report.listed ?? []).filter((item) => item.blocking === true);
    if ((report.verdict === "pass") !== (blocking.length === 0) || report.unexplained !== blocking.length) {
      failures.push("the consolidation text check's verdict does not follow from its items");
    }
    failures.push(...blocking.slice(0, NAMED).map((item) => `R86(2): ${item.publisher} ${item.family}/${item.kind} ${item.identity ?? `file ${item.object_ref_sha256}`} (body ${item.body_sha256}) is admitted and yields no text: ${item.outcome}, no typed, explained outcome`));
    if (blocking.length > NAMED) failures.push(`R86(2): and ${blocking.length - NAMED} more, each listed in ${CONSOLIDATION_TEXT_FILE}`);
  }
  return failures;
}

/**
 * Runs `Lex.V3.Tool release-checks` (`runner`: the tool's DLL) over the candidate's mount, and the serving mount when one is
 * named, into `into`, and answers `{ comparison, consolidation, stdout }` (each report's bytes, or null). Exit 0 and 4 are
 * measured (4: a check refuses, which `releaseCheckFailures` names); any other exit throws with the tool's output.
 */
export async function runReleaseChecks({ runner, mountPath, into, servingMount = null, servingReport = null, explanations = null }) {
  await mkdir(into, { recursive: true });
  const out = join(into, "checks");
  const args = [runner, "release-checks", "--mount", mountPath, "--out", out];
  if (servingMount !== null) args.push("--serving-mount", servingMount, "--serving-report", servingReport);
  if (explanations !== null) args.push("--explanations", explanations);
  const result = spawnSync("dotnet", args, { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
  if (result.status !== 0 && result.status !== 4) {
    throw new Error(`Lex.V3.Tool release-checks could not measure the release (${result.status}): ${(result.stdout ?? "").slice(-2000)}${(result.stderr ?? "").slice(-2000)}`);
  }
  const read = async (name) => (existsSync(join(out, name)) ? readFile(join(out, name)) : null);
  return { comparison: await read(COMPARISON_FILE), consolidation: await read(CONSOLIDATION_TEXT_FILE), stdout: result.stdout ?? "", exit: result.status };
}

const pick = (manifest) => ({
  version: manifest.version ?? null,
  schema: manifest.schema ?? null,
  rehearsal: manifest.rehearsal ?? null,
  signer: manifest.signer ?? null,
  commit: manifest.source?.commit ?? null,
  source_epoch: manifest.source?.epoch ?? null,
  image_manifest_digest: manifest.image?.manifest_digest ?? null,
  corpus_sha256: manifest.corpus?.sha256 ?? null,
});

/** Each named entry of two lists (`key` names it), as kept, added, changed or removed, in name order. */
function listDiff(before, after, key, same) {
  const was = new Map(before.map((entry) => [entry[key], entry]));
  const now = new Map(after.map((entry) => [entry[key], entry]));
  return [...new Set([...was.keys(), ...now.keys()])].sort().map((name) => {
    const [a, b] = [was.get(name), now.get(name)];
    const change = a === undefined ? "added" : b === undefined ? "removed" : same(a, b) ? "kept" : "changed";
    return { name, change, serving: a ?? null, candidate: b ?? null };
  });
}

/** The text yield per class of a mount report (R86(4)), as rows keyed `publisher family/kind`, or an empty list for a report with none. */
function yieldRows(report) {
  const rows = [];
  for (const publisher of report?.textYield?.publishers ?? []) {
    for (const row of publisher.classes ?? []) {
      rows.push({
        class: `${publisher.publisher} ${row.family}/${row.kind}`,
        versions: row.versions, versions_with_text: row.versions_with_text, works: row.works, works_with_text: row.works_with_text, articles: row.articles, files: row.files, files_with_text: row.files_with_text,
      });
    }
  }
  return rows;
}

const YIELD_COUNTS = ["versions", "versions_with_text", "works", "works_with_text", "articles", "files", "files_with_text"];

/**
 * R88's manifest diff between the release serving now (`serving`, its signed manifest and its mount report's bytes) and the
 * candidate (`candidate`, the manifest it is published under, less the two diff assets, and its mount report's bytes), as
 * `{ json, page, document }`: the machine-readable diff, the page a person reads, and the object both are written from.
 */
export function manifestDiff({ serving, candidate }) {
  const servingReport = JSON.parse(serving.mountReport.toString("utf8"));
  const candidateReport = JSON.parse(candidate.mountReport.toString("utf8"));
  const fields = (() => {
    const [a, b] = [pick(serving.manifest), pick(candidate.manifest)];
    return Object.keys(a).map((field) => ({ field, serving: a[field], candidate: b[field], change: a[field] === b[field] ? "kept" : "changed" }));
  })();
  const assets = listDiff(serving.manifest.assets ?? [], (candidate.manifest.assets ?? []).filter((asset) => !DIFF_ASSETS.includes(asset.name)), "name",
    (a, b) => a.size === b.size && a.sha256 === b.sha256);
  const files = listDiff(servingReport.files ?? [], candidateReport.files ?? [], "Name", (a, b) => a.Sha256 === b.Sha256 && a.ByteLength === b.ByteLength)
    .map(({ name, change, serving: a, candidate: b }) => ({
      name, change,
      serving: a === null ? null : { bytes: a.ByteLength, sha256: a.Sha256 },
      candidate: b === null ? null : { bytes: b.ByteLength, sha256: b.Sha256 },
    }));
  const counts = ["schema", "corpusMembers", "derivationInputUtc", "builtTwiceAndEqual", "indexBuilderSource"].map((field) => ({
    field, serving: servingReport[field] ?? null, candidate: candidateReport[field] ?? null,
    change: JSON.stringify(servingReport[field] ?? null) === JSON.stringify(candidateReport[field] ?? null) ? "kept" : "changed",
  }));
  const yields = listDiff(yieldRows(servingReport), yieldRows(candidateReport), "class", (a, b) => YIELD_COUNTS.every((count) => a[count] === b[count]))
    .map(({ name, change, serving: a, candidate: b }) => ({
      class: name, change,
      counts: Object.fromEntries(YIELD_COUNTS.map((count) => [count, { serving: a?.[count] ?? null, candidate: b?.[count] ?? null }])),
    }));
  const drops = [];
  for (const row of yields) {
    for (const count of YIELD_COUNTS) {
      const { serving: was, candidate: now } = row.counts[count];
      if (Number.isInteger(was) && was > 0 && (now ?? 0) * 10 < was * 9) {
        drops.push({ class: row.class, count, serving: was, candidate: now ?? 0, per_cent: Math.round(((now ?? 0) - was) * 1000 / was) / 10 });
      }
    }
  }
  const textYield = {
    serving: servingReport.textYield ? "reported" : "not reported (the mount report carries no text yield)",
    candidate: candidateReport.textYield ? "reported" : "not reported (the mount report carries no text yield)",
  };
  const document = {
    schema: MANIFEST_DIFF_SCHEMA,
    serving: { version: serving.manifest.version, manifest_sha256: serving.manifestSha256 ?? null },
    candidate: { version: candidate.manifest.version, assets: (candidate.manifest.assets ?? []).filter((asset) => !DIFF_ASSETS.includes(asset.name)) },
    note: "R88: the candidate release against the release serving now. The diff covers every asset of the candidate but the diff itself (manifest-diff.json and manifest-diff.md), which the candidate's signed manifest lists; drops beyond ten per cent of any count of the text yield are named.",
    fields, assets, mount_files: files, mount_counts: counts, text_yield: textYield, text_yield_classes: yields,
    drops_beyond_ten_percent: drops,
  };
  const json = Buffer.from(`${JSON.stringify(document, null, 2)}\n`, "utf8");
  return { json, page: Buffer.from(diffPage(document), "utf8"), document };
}

const shortDigest = (value) => (typeof value === "string" && /^(sha256:)?[0-9a-f]{64}$/.test(value) ? `${value.replace(/^sha256:/, "").slice(0, 12)}…` : String(value));

/** The diff as a page a person reads: what changed first, then everything kept, in plain sentences and tables. */
function diffPage(document) {
  const lines = [];
  lines.push(`# Release ${document.candidate.version} against ${document.serving.version}`, "");
  lines.push("What the release serving now and this release hold, side by side (R88). Each digest is shortened to 12 characters; the JSON beside this page has them whole.", "");
  lines.push("## The release", "", "| Field | Serving | This release | |", "|---|---|---|---|");
  for (const row of document.fields) lines.push(`| ${row.field} | ${shortDigest(row.serving)} | ${shortDigest(row.candidate)} | ${row.change} |`);
  lines.push("", "## Assets", "", "| Asset | Serving | This release | |", "|---|---|---|---|");
  for (const row of document.assets) {
    const said = (asset) => (asset === null ? "none" : `${asset.size} B, ${shortDigest(asset.sha256)}`);
    lines.push(`| ${row.name} | ${said(row.serving)} | ${said(row.candidate)} | ${row.change} |`);
  }
  lines.push("", "## The mount", "", "| File | Serving | This release | |", "|---|---|---|---|");
  for (const row of document.mount_files) {
    const said = (file) => (file === null ? "none" : `${file.bytes} B, ${shortDigest(file.sha256)}`);
    lines.push(`| ${row.name} | ${said(row.serving)} | ${said(row.candidate)} | ${row.change} |`);
  }
  lines.push("", "| Count | Serving | This release | |", "|---|---|---|---|");
  for (const row of document.mount_counts) lines.push(`| ${row.field} | ${shortDigest(row.serving)} | ${shortDigest(row.candidate)} | ${row.change} |`);
  lines.push("", "## The text yield per class", "", `Serving: ${document.text_yield.serving}. This release: ${document.text_yield.candidate}.`, "");
  if (document.text_yield_classes.length > 0) {
    lines.push("| Class | Versions with text | Versions | Acts with text | Articles | Files with text | |", "|---|---|---|---|---|---|---|");
    for (const row of document.text_yield_classes) {
      const pair = (count) => `${row.counts[count].serving ?? "none"} → ${row.counts[count].candidate ?? "none"}`;
      lines.push(`| ${row.class} | ${pair("versions_with_text")} | ${pair("versions")} | ${pair("works_with_text")} | ${pair("articles")} | ${pair("files_with_text")} | ${row.change} |`);
    }
    lines.push("");
  }
  lines.push("## Drops beyond ten per cent", "");
  if (document.drops_beyond_ten_percent.length === 0) lines.push("None.");
  for (const drop of document.drops_beyond_ten_percent) lines.push(`- ${drop.class}, ${drop.count}: ${drop.serving} to ${drop.candidate} (${drop.per_cent} per cent).`);
  lines.push("");
  return `${lines.join("\n")}\n`;
}

/**
 * Why a release's published check assets do not hold, as failures (empty when they do; `held` maps each listed asset's name
 * to its bytes, `manifest` is the release's own manifest): a comparison or consolidation report that refuses, and a manifest
 * diff that does not describe this release's manifest (its version and every asset but the diff's own, by size and digest).
 * A release that carries none of them has nothing to hold here.
 */
export function publishedCheckFailures(held, manifest) {
  const failures = [];
  const comparison = held.get(COMPARISON_FILE) ?? null;
  const consolidation = held.get(CONSOLIDATION_TEXT_FILE) ?? null;
  if (comparison !== null || consolidation !== null) {
    try {
      failures.push(...releaseCheckFailures({ comparison, consolidation }).map((failure) => `the release carries a check that refuses it: ${failure}`));
    } catch (error) {
      failures.push(`the release's checks do not read: ${error.message}`);
    }
  }
  const diff = held.get(MANIFEST_DIFF_FILE);
  if (diff !== undefined) {
    let document = null;
    try { document = JSON.parse(diff.toString("utf8")); } catch { failures.push(`${MANIFEST_DIFF_FILE} is not JSON`); }
    if (document !== null) {
      const expected = (manifest.assets ?? []).filter((asset) => !DIFF_ASSETS.includes(asset.name)).map(({ name, size, sha256 }) => ({ name, size, sha256 }));
      const said = (document.candidate?.assets ?? []).map(({ name, size, sha256 }) => ({ name, size, sha256 }));
      if (document.schema !== MANIFEST_DIFF_SCHEMA || document.candidate?.version !== manifest.version || JSON.stringify(said) !== JSON.stringify(expected)) {
        failures.push(`${MANIFEST_DIFF_FILE} does not describe this release: its version and every asset but the diff's own must be the manifest's`);
      }
    }
    if (!held.has(MANIFEST_DIFF_PAGE)) failures.push(`the release carries ${MANIFEST_DIFF_FILE} without ${MANIFEST_DIFF_PAGE}`);
  }
  return failures;
}
