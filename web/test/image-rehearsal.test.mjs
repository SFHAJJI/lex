// The image rehearsal's checks (`image-rehearsal.mjs`), each held to fail on the image it must refuse.
//
// A real image takes a .NET publish and a network pull, so these build a small OCI image layout in
// memory instead: a manifest, a config and one gzipped app layer, as the SDK writes them. Every check
// the rehearsal runs on the real image is run here on a good image, and on an image broken the way the
// check exists to catch.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { gzipSync } from "node:zlib";

import { REHEARSAL_IDENTITY, imageFailures, mountReport, readOciImage, readTar, rehearsalSignatureFailures, signRehearsal } from "../scripts/image-rehearsal.mjs";

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");

/** A tar archive of `[path, bytes]` files: ustar headers, with a pax header for a name over 100 bytes. */
function tar(files) {
  const blocks = [];
  const header = (name, size, type) => {
    const block = Buffer.alloc(512);
    block.write(name.slice(0, 100), 0, "utf8");
    block.write("0000644\0", 100);
    block.write("0000000\0", 108);
    block.write("0000000\0", 116);
    block.write(`${size.toString(8).padStart(11, "0")}\0`, 124);
    block.write("00000000000\0", 136);
    block.write("        ", 148);
    block.write(type, 156);
    block.write("ustar\0", 257);
    block.write("00", 263);
    let checksum = 0;
    for (const byte of block) checksum += byte;
    block.write(`${checksum.toString(8).padStart(6, "0")}\0 `, 148);
    return block;
  };
  const pad = (bytes) => Buffer.concat([bytes, Buffer.alloc((512 - (bytes.length % 512)) % 512)]);
  for (const [path, bytes] of files) {
    if (Buffer.byteLength(path) > 100) {
      const record = ` path=${path}\n`;
      let length = record.length + 2;
      while (`${length}${record}`.length !== length) length += 1;
      const pax = Buffer.from(`${length}${record}`, "utf8");
      blocks.push(header("PaxHeader", pax.length, "x"), pad(pax));
    }
    blocks.push(header(path, bytes.length, "0"), pad(bytes));
  }
  blocks.push(Buffer.alloc(1024));
  return Buffer.concat(blocks);
}

const WEB = [["index.html", Buffer.from("<!doctype html><title>Trust and Coverage</title>")], ["client-live.js", Buffer.from("hydrate()")]];
const MOUNT_FILE = Buffer.from("{\"schema\":\"lex-v3-source-corpus/6\"}");
const LONG = `preview-graph/${"a".repeat(96)}.sqlite3`;

/** A good image: its layout, and what it was built from. */
function image({ user = "1654", entrypoint = ["dotnet", "/app/Lex.V3.Api.dll"], base = `mcr.microsoft.com/dotnet/aspnet:10.0@sha256:${"c".repeat(64)}`, appFiles } = {}) {
  const report = { files: [{ Name: "lex-corpus-6.json", ByteLength: MOUNT_FILE.length, Sha256: sha256(MOUNT_FILE) }] };
  const reportBytes = Buffer.from(JSON.stringify(report));
  const files = appFiles ?? [
    ["app/Lex.V3.Api.dll", Buffer.from("MZ")],
    [`app/${LONG}`, Buffer.from("sqlite")],
    ...WEB.map(([path, bytes]) => [`app/v3-web/${path}`, bytes]),
    ["app/v3-corpus/lex-corpus-6.json", MOUNT_FILE],
    ["app/v3-corpus/build-report.json", reportBytes],
  ];
  const layer = gzipSync(tar(files));
  const config = Buffer.from(JSON.stringify({ architecture: "amd64", os: "linux", config: { User: user, Entrypoint: entrypoint, Labels: { "org.opencontainers.image.base.name": base } } }));
  const manifest = Buffer.from(JSON.stringify({
    schemaVersion: 2,
    config: { digest: `sha256:${sha256(config)}`, size: config.length },
    layers: [{ mediaType: "application/vnd.oci.image.layer.v1.tar+gzip", digest: `sha256:${sha256(layer)}`, size: layer.length }],
  }));
  const blobs = new Map([[`sha256:${sha256(manifest)}`, manifest], [`sha256:${sha256(config)}`, config], [`sha256:${sha256(layer)}`, layer]]);
  const index = { manifests: [{ digest: `sha256:${sha256(manifest)}`, size: manifest.length }] };
  const built = { web: WEB, mount: [["lex-corpus-6.json", MOUNT_FILE], ["build-report.json", reportBytes]], report };
  return { index, blobs, built, layer };
}

test("the tar reader reads ustar entries and the long names pax headers carry", () => {
  const entries = readTar(tar([["app/short.txt", Buffer.from("one")], [`app/${LONG}`, Buffer.from("two")]]));
  assert.deepEqual(entries.map((entry) => [entry.path, entry.type, entry.bytes.toString()]), [["app/short.txt", "file", "one"], [`app/${LONG}`, "file", "two"]]);
});

test("the image is read blob by blob, each held to the digest and size it is named by", () => {
  const good = image();
  const read = readOciImage(good);
  assert.equal(read.manifestDigest, good.index.manifests[0].digest);
  assert.equal(read.layers.length, 1);

  const tampered = image();
  const layerDigest = `sha256:${sha256(tampered.layer)}`;
  const bytes = Buffer.from(tampered.blobs.get(layerDigest));
  bytes[bytes.length - 1] ^= 0xff;
  tampered.blobs.set(layerDigest, bytes);
  assert.throws(() => readOciImage(tampered), /does not have that digest/, "a layer whose bytes changed is caught");

  const missing = image();
  missing.blobs.delete(`sha256:${sha256(missing.layer)}`);
  assert.throws(() => readOciImage(missing), /which the layout does not hold/);

  const two = image();
  two.index = { manifests: [...two.index.manifests, ...two.index.manifests] };
  assert.throws(() => readOciImage(two), /exactly one manifest/);
});

test("the image must hold the API, the live pages and the mount byte for byte, and run as it must", () => {
  const good = image();
  assert.deepEqual(imageFailures(readOciImage(good), good.built), [], "a good image passes");

  const failing = (options, change = () => {}) => {
    const one = image(options);
    change(one.built);
    return imageFailures(readOciImage(one), one.built);
  };
  assert.ok(failing({}, (built) => { built.web = [...built.web, ["radar.html", Buffer.from("x")]]; }).includes("the image holds no app/v3-web/radar.html"), "a live page left out");
  assert.ok(failing({}, (built) => { built.mount = [["lex-corpus-6.json", Buffer.from("another corpus")], built.mount[1]]; })
    .includes("app/v3-corpus/lex-corpus-6.json in the image differs from the mount file it was built from"), "a mount file that is not the one built from");
  assert.ok(failing({}, (built) => { built.report = { files: [{ Name: "lex-corpus-6.json", Sha256: "d".repeat(64) }] }; })
    .some((failure) => failure.startsWith("lex-corpus-6.json in the image is sha256:")), "a file whose digest is not the build report's");
  assert.ok(failing({ user: "0" }).includes("the image runs as 0, not a non-root user"));
  assert.ok(failing({ user: "" }).includes("the image runs as root (no user set), not a non-root user"));
  for (const root of ["0:1654", "root:1654", "00", "root"]) {
    assert.ok(failing({ user: root }).includes(`the image runs as ${root}, not a non-root user`), `${root} runs as root, whatever group follows (review of #821)`);
  }
  assert.deepEqual(failing({ user: "1654:1654" }), [], "a non-root user with its group passes");
  assert.ok(failing({ entrypoint: ["sh"] }).some((failure) => failure.startsWith("the image starts")));
  assert.ok(failing({ base: "mcr.microsoft.com/dotnet/aspnet:10.0" }).some((failure) => failure.startsWith("the base image is not named by digest")));
  assert.ok(failing({ appFiles: [["app/v3-web/index.html", WEB[0][1]]] }).includes("the app layer holds no app/Lex.V3.Api.dll"));
});

test("the rehearsal signature holds for its image's manifest digest, and for nothing else", () => {
  const digest = `sha256:${"e".repeat(64)}`;
  const signed = signRehearsal({ manifestDigest: digest, reference: "lex-v3-rehearsal:rehearsal" });
  assert.deepEqual(rehearsalSignatureFailures({ ...signed, manifestDigest: digest }), []);
  assert.equal(JSON.parse(signed.payload).optional.signer, REHEARSAL_IDENTITY, "the payload says whose signature it is");

  assert.ok(rehearsalSignatureFailures({ ...signed, manifestDigest: `sha256:${"f".repeat(64)}` }).some((failure) => failure.includes("not this image's manifest")), "another image's digest fails");
  const payload = Buffer.from(signed.payload.toString().replace(digest, `sha256:${"f".repeat(64)}`));
  assert.ok(rehearsalSignatureFailures({ ...signed, payload, manifestDigest: `sha256:${"f".repeat(64)}` }).includes("the signature does not verify over its payload with this key"), "a payload changed after signing fails");
  const other = signRehearsal({ manifestDigest: digest, reference: "lex-v3-rehearsal:rehearsal" });
  assert.ok(rehearsalSignatureFailures({ ...signed, publicKeyPem: other.publicKeyPem, manifestDigest: digest }).includes("the signature does not verify over its payload with this key"), "another key fails");
});

test("a real mount is held to its build report; the journey's fixture mount to the digests it names", async () => {
  const directory = await mkdtemp(join(tmpdir(), "lex-mount-report-"));
  try {
    const report = { corpus: { Sha256: "c".repeat(64) }, files: [{ Name: "lex-corpus-6.json", Sha256: "c".repeat(64) }] };
    await writeFile(join(directory, "build-report.json"), JSON.stringify(report));
    assert.deepEqual(await mountReport(directory), { kind: "real", report });

    await writeFile(join(directory, "journey-mount.json"), JSON.stringify({ schema: "lex-v3-journey-mount/1", corpus_sha256: "a".repeat(64), index_sha256: "b".repeat(64), work_key: "w" }));
    const fixture = await mountReport(directory);
    assert.equal(fixture.kind, "fixture", "a mount with journey-mount.json is the fixture mount");
    assert.deepEqual(fixture.report.files, [{ Name: "luxembourg-index.sqlite3", Sha256: "b".repeat(64) }],
      "the index is held to the file digest the fixture names; the corpus digest it names is a snapshot digest, not its file's");
    assert.equal(fixture.report.corpus.Sha256, "a".repeat(64), "the snapshot digest the coverage probe holds the page to");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
