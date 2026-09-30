// The reproducible image (`image-reproducible.mjs`): the SDK's time stamps and process-named headers
// are rewritten away, and nothing else is.
//
// The fixture is an image as the .NET SDK writes it: a gzipped base layer, and a gzipped app layer
// whose every entry has a pax header named after the SDK's process id and carrying the time the build
// ran, with the build time in the config's `created` and its history. Two such builds of the same files
// must come out as one image; a build of different files, or of the same files with another mode, must
// not.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import { gzipSync } from "node:zlib";

import { imageFailures, layerTar, readLayout, readOciImage, readTar, reproductionFailures } from "../scripts/image-rehearsal.mjs";
import { REPRODUCIBLE_LAYER, reproducibleImage, sourceDate, writeLayout, writeTar } from "../scripts/image-reproducible.mjs";

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");
const EPOCH = 1790798393;
const LONG = `app/preview-graph/sqlite_index.${"b".repeat(64)}.sqlite3`;

/** A header block, as the tests need to write one. */
function block({ name, mode = 0o644, size = 0, mtime = 0, type = "0" }) {
  const header = Buffer.alloc(512);
  header.write(name.slice(0, 100), 0);
  header.write(`${mode.toString(8).padStart(7, "0")}\0`, 100);
  header.write("0000000\0", 108);
  header.write("0000000\0", 116);
  header.write(`${size.toString(8).padStart(11, "0")}\0`, 124);
  header.write(`${Math.floor(mtime).toString(8).padStart(11, "0")}\0`, 136);
  header.write("        ", 148);
  header.write(type, 156);
  header.write("ustar\0", 257);
  header.write("00", 263);
  let sum = 0;
  for (const byte of header) sum += byte;
  header.write(`${sum.toString(8).padStart(6, "0")}\0 `, 148);
  return header;
}
const pad = (bytes) => Buffer.concat([bytes, Buffer.alloc((512 - (bytes.length % 512)) % 512)]);

/** An app layer's tar as the SDK writes it: a pax header per entry, named with its process id. */
function sdkTar(entries, { pid, time }) {
  const parts = [];
  for (const [position, { path, type = "file", mode = 0o755, bytes = Buffer.alloc(0) }] of entries.entries()) {
    const mtime = time + position / 10;
    const records = [` path=${path}\n`, ` mtime=${mtime}\n`].map((body) => {
      let length = body.length + 1;
      while (`${length}${body}`.length !== length) length += 1;
      return `${length}${body}`;
    }).join("");
    parts.push(block({ name: `./PaxHeaders.${pid}/.`, size: records.length, type: "x" }), pad(Buffer.from(records)));
    parts.push(block({ name: path, mode, size: type === "file" ? bytes.length : 0, mtime, type: type === "file" ? "0" : "5" }));
    if (type === "file") parts.push(pad(bytes));
  }
  parts.push(Buffer.alloc(1024));
  return Buffer.concat(parts);
}

const FILES = [
  { path: "app", type: "directory" },
  { path: "app/Lex.V3.Api.dll", bytes: Buffer.from("MZ the api") },
  { path: LONG, bytes: Buffer.from("sqlite") },
  { path: "app/v3-web/index.html", mode: 0o644, bytes: Buffer.from("<!doctype html><title>Trust and Coverage</title>") },
  { path: "app/v3-corpus/build-report.json", mode: 0o644, bytes: Buffer.from("{\"files\":[]}") },
];

/** A build of the image as the SDK would make it at `time`, by process `pid`. */
function sdkImage({ files = FILES, pid = 10172, time = 1790798592 } = {}) {
  const baseTar = Buffer.concat([block({ name: "usr/share/dotnet/dotnet", mode: 0o755, size: 3, mtime: 1786000000 }), pad(Buffer.from("elf")), Buffer.alloc(1024)]);
  const base = gzipSync(baseTar);
  const appTar = sdkTar(files, { pid, time });
  const app = gzipSync(appTar);
  const created = new Date(time * 1000).toISOString();
  const config = Buffer.from(JSON.stringify({
    config: { User: "1654", Entrypoint: ["dotnet", "/app/Lex.V3.Api.dll"], Labels: { "org.opencontainers.image.base.name": `mcr.microsoft.com/dotnet/aspnet:10.0@sha256:${"c".repeat(64)}` } },
    created,
    rootfs: { type: "layers", diff_ids: [`sha256:${sha256(baseTar)}`, `sha256:${sha256(appTar)}`] },
    architecture: "amd64",
    os: "linux",
    history: [
      { created: "2026-08-10T17:01:17.9907477Z", created_by: "COPY /dotnet /usr/share/dotnet # buildkit" },
      { created: "2026-08-10T17:01:18Z", created_by: "ENV APP_UID=1654", empty_layer: true },
      { author: ".NET SDK", created, created_by: ".NET SDK Container Tooling" },
    ],
  }));
  const manifest = Buffer.from(JSON.stringify({
    schemaVersion: 2,
    mediaType: "application/vnd.oci.image.manifest.v1+json",
    config: { mediaType: "application/vnd.oci.image.config.v1+json", size: config.length, digest: `sha256:${sha256(config)}` },
    layers: [
      { mediaType: "application/vnd.docker.image.rootfs.diff.tar.gzip", size: base.length, digest: `sha256:${sha256(base)}` },
      { mediaType: "application/vnd.oci.image.layer.v1.tar+gzip", size: app.length, digest: `sha256:${sha256(app)}` },
    ],
  }));
  const index = { schemaVersion: 2, manifests: [{ mediaType: "application/vnd.oci.image.manifest.v1+json", size: manifest.length, digest: `sha256:${sha256(manifest)}`, annotations: { "org.opencontainers.image.ref.name": "rehearsal" } }] };
  const blobs = new Map([base, app, config, manifest].map((bytes) => [`sha256:${sha256(bytes)}`, bytes]));
  return { index, blobs, base };
}

test("the layer writer depends on its entries and the source date alone", () => {
  const entries = FILES.map(({ path, type = "file", mode = 0o755, bytes }) => ({ path, type, mode, uid: 0, gid: 0, bytes }));
  const written = writeTar(entries, { mtime: EPOCH });
  assert.ok(written.equals(writeTar([...entries].reverse(), { mtime: EPOCH })), "the order entries arrive in does not matter");
  assert.ok(!written.equals(writeTar(entries, { mtime: EPOCH + 1 })), "the source date is written");

  const read = readTar(written);
  assert.deepEqual(read.map((entry) => entry.path), ["app/", "app/Lex.V3.Api.dll", LONG, "app/v3-corpus/build-report.json", "app/v3-web/index.html"], "sorted by path; a directory ends in /");
  assert.deepEqual(read.map((entry) => [entry.mode, entry.uid, entry.gid]), [[0o755, 0, 0], [0o755, 0, 0], [0o755, 0, 0], [0o644, 0, 0], [0o644, 0, 0]], "modes and owners are kept");
  assert.equal(read[2].bytes.toString(), "sqlite");
  for (let offset = 0; offset < written.length - 1024; offset += 512) {
    const header = written.subarray(offset, offset + 512);
    if (header.subarray(257, 262).toString() !== "ustar") continue;
    assert.equal(parseInt(header.subarray(136, 148).toString(), 8), EPOCH, "every header carries the source date");
    assert.notEqual(String.fromCharCode(header[156]), "x", "a path ustar can hold gets no pax header");
  }

  const component = `app/${"c".repeat(120)}`;
  const long = writeTar([{ path: component, type: "file", mode: 0o644, uid: 0, gid: 0, bytes: Buffer.from("x") }], { mtime: EPOCH });
  assert.equal(long.subarray(0, 14).toString(), "././@PaxHeader", "a name no ustar split holds gets a pax header with a fixed name");
  assert.deepEqual(readTar(long).map((entry) => entry.path), [component]);

  assert.throws(() => writeTar([{ path: "app/link", type: "2", mode: 0o777, uid: 0, gid: 0, bytes: Buffer.alloc(0) }], { mtime: EPOCH }), /is a 2 entry/);
  assert.throws(() => writeTar([entries[1], entries[1]], { mtime: EPOCH }), /appears twice/);
});

test("two builds of the same files, at different times by different processes, are one image", () => {
  const first = reproducibleImage(sdkImage(), { epoch: EPOCH });
  const second = reproducibleImage(sdkImage({ pid: 29100, time: 1790798620 }), { epoch: EPOCH });
  assert.equal(first.manifestDigest, second.manifestDigest);
  assert.notEqual(readOciImage(sdkImage()).manifestDigest, readOciImage(sdkImage({ pid: 29100, time: 1790798620 })).manifestDigest, "the SDK's own images differ");

  const image = readOciImage(first);
  const app = image.layers.at(-1);
  assert.equal(app.mediaType, REPRODUCIBLE_LAYER, "the app layer is a plain tar");
  assert.equal(image.config.rootfs.diff_ids.at(-1), app.digest, "so its digest is its diff id");
  assert.equal(image.config.created, "2026-09-30T19:59:53Z");
  assert.equal(image.config.history.at(-1).created, "2026-09-30T19:59:53Z", "the app layer's history entry takes the source date");
  assert.equal(image.config.history[0].created, "2026-08-10T17:01:17.9907477Z", "the base image's history is kept");
  assert.ok(image.layers[0].bytes.equals(sdkImage().base), "the base layer is kept byte for byte");
  assert.equal(first.index.manifests[0].annotations["org.opencontainers.image.ref.name"], "rehearsal", "the index keeps its annotations");
  assert.deepEqual(readTar(layerTar(app)).map((entry) => [entry.path, entry.bytes.toString()]).filter(([path]) => !path.endsWith("/")),
    FILES.filter((file) => file.bytes).map((file) => [file.path, file.bytes.toString()]).sort(([a], [b]) => (a < b ? -1 : 1)), "every file is the SDK's, byte for byte");
  assert.deepEqual(imageFailures(image, { web: [["index.html", FILES[3].bytes]], mount: [["build-report.json", FILES[4].bytes]], report: { files: [{ Name: "build-report.json", Sha256: sha256(FILES[4].bytes) }] } }), [],
    "the rehearsal's image checks read the plain app layer");
});

test("a build of different files, or the same files with another mode, is another image", () => {
  const same = reproducibleImage(sdkImage(), { epoch: EPOCH }).manifestDigest;
  const changed = FILES.map((file) => (file.path === "app/Lex.V3.Api.dll" ? { ...file, bytes: Buffer.from("MZ another api") } : file));
  assert.notEqual(reproducibleImage(sdkImage({ files: changed }), { epoch: EPOCH }).manifestDigest, same);
  const moded = FILES.map((file) => (file.path === "app/v3-web/index.html" ? { ...file, mode: 0o755 } : file));
  assert.notEqual(reproducibleImage(sdkImage({ files: moded }), { epoch: EPOCH }).manifestDigest, same);
  assert.notEqual(reproducibleImage(sdkImage(), { epoch: EPOCH + 1 }).manifestDigest, same, "another source date is another image");
});

test("the repack refuses an image whose layer, history or entries it cannot rewrite honestly", () => {
  const rewrite = (change) => {
    const built = sdkImage();
    const image = readOciImage(built);
    const config = change(structuredClone(image.config));
    const configBytes = Buffer.from(JSON.stringify(config));
    const manifest = Buffer.from(JSON.stringify({ ...image.manifest, config: { ...image.manifest.config, size: configBytes.length, digest: `sha256:${sha256(configBytes)}` } }));
    built.blobs.set(`sha256:${sha256(configBytes)}`, configBytes);
    built.blobs.set(`sha256:${sha256(manifest)}`, manifest);
    return { index: { ...built.index, manifests: [{ ...built.index.manifests[0], size: manifest.length, digest: `sha256:${sha256(manifest)}` }] }, blobs: built.blobs };
  };
  assert.throws(() => reproducibleImage(rewrite((config) => { config.rootfs.diff_ids[1] = `sha256:${"0".repeat(64)}`; return config; }), { epoch: EPOCH }), /not the sha256:0+ the config names/);
  assert.throws(() => reproducibleImage(rewrite((config) => { config.history.pop(); return config; }), { epoch: EPOCH }), /the history names 1 layers, the manifest 2/);
  const linked = sdkTar([{ path: "app", type: "directory" }], { pid: 1, time: 1 });
  const withLink = Buffer.concat([linked.subarray(0, linked.length - 1024), block({ name: "app/link", mode: 0o777, type: "2" }), Buffer.alloc(1024)]);
  const layer = gzipSync(withLink);
  const linkImage = rewrite((config) => { config.rootfs.diff_ids[1] = `sha256:${sha256(withLink)}`; return config; });
  const manifest = JSON.parse(linkImage.blobs.get(linkImage.index.manifests[0].digest).toString());
  manifest.layers[1] = { ...manifest.layers[1], size: layer.length, digest: `sha256:${sha256(layer)}` };
  const manifestBytes = Buffer.from(JSON.stringify(manifest));
  linkImage.blobs.set(`sha256:${sha256(layer)}`, layer);
  linkImage.blobs.set(`sha256:${sha256(manifestBytes)}`, manifestBytes);
  linkImage.index.manifests[0] = { ...linkImage.index.manifests[0], size: manifestBytes.length, digest: `sha256:${sha256(manifestBytes)}` };
  assert.throws(() => reproducibleImage(linkImage, { epoch: EPOCH }), /app\/link is a 2 entry/, "a link is refused, not dropped");
});

test("the layout archive is deterministic and reads back as the same image", () => {
  const image = reproducibleImage(sdkImage(), { epoch: EPOCH });
  const archive = writeLayout(image, { mtime: EPOCH });
  assert.ok(archive.equals(writeLayout(reproducibleImage(sdkImage({ pid: 3, time: 1790799999 }), { epoch: EPOCH }), { mtime: EPOCH })));
  const layout = readLayout(archive);
  assert.equal(readOciImage(layout).manifestDigest, image.manifestDigest);
  assert.deepEqual(JSON.parse(readTar(archive).find((entry) => entry.path === "oci-layout").bytes), { imageLayoutVersion: "1.0.0" });
});

test("two builds that differ are told apart down to the files that differ", () => {
  const one = readOciImage(reproducibleImage(sdkImage(), { epoch: EPOCH }));
  assert.deepEqual(reproductionFailures(one, readOciImage(reproducibleImage(sdkImage({ pid: 7 }), { epoch: EPOCH }))), []);
  const changed = FILES.map((file) => (file.path === "app/v3-web/index.html" ? { ...file, bytes: Buffer.from("<!doctype html><title>Another</title>") } : file));
  const failures = reproductionFailures(one, readOciImage(reproducibleImage(sdkImage({ files: changed }), { epoch: EPOCH })));
  assert.ok(failures[0].startsWith("the second build's manifest is sha256:"));
  assert.ok(failures.includes("the config's rootfs differs"));
  assert.ok(failures.includes("layer 2: 1 entries differ: app/v3-web/index.html"), failures.join("\n"));
  assert.ok(!failures.some((failure) => failure.startsWith("layer 1")), "the base layer is the same");
  assert.deepEqual(reproductionFailures(one, readOciImage(sdkImage())).filter((failure) => failure.includes("holds the same entries")), ["layer 2 holds the same entries; only how it is written differs"],
    "the SDK's own layer holds the same files, written differently");
});

test("the source date is SOURCE_DATE_EPOCH when set, else the commit's time", () => {
  const set = sourceDate({ SOURCE_DATE_EPOCH: "1700000000" });
  assert.equal(set.epoch, 1700000000);
  assert.equal(set.from, "SOURCE_DATE_EPOCH");
  const commit = sourceDate({});
  assert.equal(commit.from, "commit");
  assert.ok(Number.isInteger(commit.epoch) && commit.epoch > 1_600_000_000);
  assert.match(commit.commit, /^[0-9a-f]{40}$/);
  assert.equal(typeof commit.clean, "boolean");
});
