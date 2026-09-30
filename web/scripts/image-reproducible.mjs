// The one-server image made reproducible. The .NET SDK's container tooling stamps each build with the
// time it ran (the config's `created`, the app layer's history entry, every file's modification time)
// and names each pax header after its own process id, so two builds of the same sources and mount give
// different digests although every file in them is byte for byte the same. `reproducibleImage` rewrites
// only what the SDK stamped:
// - the app layer is written again from the same entries (path, kind, mode, owner, bytes), sorted by
//   path, every modification time the source date, with no pax header unless a path needs one, and
//   uncompressed: its digest is then the digest of its tar alone, whatever compressor a machine has;
// - the config's `created` and the app layer's history entry take the source date, and its last diff
//   id the new layer's;
// - the manifest and the index name the new config and layer.
// The base layers are kept as the base image published them, byte for byte, as its digest pins them.
// The source date is SOURCE_DATE_EPOCH when set, else the time of the commit the image is built from.

import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";
import { layerTar, readOciImage, readTar } from "./image-rehearsal.mjs";

const repository = resolve(fileURLToPath(new URL("../../", import.meta.url)));
const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");

/** The media type of the app layer the repack writes: a tar, uncompressed. */
export const REPRODUCIBLE_LAYER = "application/vnd.oci.image.layer.v1.tar";

/**
 * The source date in seconds and where it came from: SOURCE_DATE_EPOCH when set, else the commit time
 * of HEAD, with the commit and whether the working tree matches it.
 */
export function sourceDate(env = process.env) {
  const git = (...args) => spawnSync("git", args, { cwd: repository, encoding: "utf8" });
  const commit = git("rev-parse", "HEAD").stdout.trim() || null;
  const clean = commit !== null && git("status", "--porcelain", "--untracked-files=no").stdout.trim() === "";
  if (/^\d+$/.test(env.SOURCE_DATE_EPOCH ?? "")) return { epoch: Number(env.SOURCE_DATE_EPOCH), from: "SOURCE_DATE_EPOCH", commit, clean };
  const time = git("log", "-1", "--format=%ct").stdout.trim();
  if (!/^\d+$/.test(time)) throw new Error("no source date: SOURCE_DATE_EPOCH is not set and HEAD has no commit time");
  return { epoch: Number(time), from: "commit", commit, clean };
}

/** An octal header field of `length` bytes: digits, zero padded, and a NUL. */
function octal(value, length) {
  const digits = value.toString(8);
  if (digits.length > length - 1) throw new Error(`${value} does not fit a ${length}-byte tar field`);
  return digits.padStart(length - 1, "0") + "\0";
}

/** A path as ustar holds it, `{ name, prefix }`, or null when only a pax header can hold it. */
function ustarPath(path) {
  if (!/^[\x20-\x7e]*$/.test(path)) return null;
  if (path.length <= 100) return { name: path, prefix: "" };
  for (let cut = path.indexOf("/"); cut >= 0; cut = path.indexOf("/", cut + 1)) {
    const prefix = path.slice(0, cut);
    const name = path.slice(cut + 1);
    if (prefix.length <= 155 && name.length > 0 && name.length <= 100) return { name, prefix };
  }
  return null;
}

function header({ name, prefix = "", mode, uid, gid, size, mtime, type }) {
  const block = Buffer.alloc(512);
  const put = (text, at) => block.write(text, at, "utf8");
  put(name, 0);
  put(octal(mode, 8), 100);
  put(octal(uid, 8), 108);
  put(octal(gid, 8), 116);
  put(octal(size, 12), 124);
  put(octal(mtime, 12), 136);
  put("        ", 148);
  put(type, 156);
  put("ustar\0", 257);
  put("00", 263);
  put(prefix, 345);
  let sum = 0;
  for (const byte of block) sum += byte;
  put(`${sum.toString(8).padStart(6, "0")}\0 `, 148);
  return block;
}

/** A pax record, `<length> <key>=<value>\n`, whose length counts itself. */
function paxRecord(key, value) {
  const body = ` ${key}=${value}\n`;
  let length = Buffer.byteLength(body) + 1;
  while (String(length).length + Buffer.byteLength(body) !== length) length = String(length).length + Buffer.byteLength(body);
  return `${length}${body}`;
}

const padding = (size) => Buffer.alloc((512 - (size % 512)) % 512);

/**
 * A tar archive of `entries` (`{ path, type: "file" | "directory", mode, uid, gid, bytes }`) that
 * depends on nothing but them and `mtime`: sorted by path, every modification time `mtime`, no owner
 * names, and a pax header (with a fixed name) only for a path ustar cannot hold. Directories end in `/`.
 */
export function writeTar(entries, { mtime }) {
  const seen = new Set();
  const sorted = entries.map((entry) => ({ ...entry, path: entry.path.replace(/\/+$/, "") }))
    .sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
  const parts = [];
  for (const entry of sorted) {
    if (entry.type !== "file" && entry.type !== "directory") throw new Error(`${entry.path} is a ${entry.type} entry, which the reproducible layer does not write`);
    if (seen.has(entry.path)) throw new Error(`${entry.path} appears twice`);
    seen.add(entry.path);
    const path = entry.type === "directory" ? `${entry.path}/` : entry.path;
    const bytes = entry.type === "file" ? entry.bytes : Buffer.alloc(0);
    const fields = { mode: entry.mode & 0o7777, uid: entry.uid, gid: entry.gid, mtime };
    let split = ustarPath(path);
    if (split === null) {
      const records = Buffer.from(paxRecord("path", path), "utf8");
      parts.push(header({ name: "././@PaxHeader", ...fields, mode: 0o644, size: records.length, type: "x" }), records, padding(records.length));
      split = { name: path.replace(/[^\x20-\x7e]/g, "_").slice(-100), prefix: "" };
    }
    parts.push(header({ ...split, ...fields, size: bytes.length, type: entry.type === "directory" ? "5" : "0" }), bytes, padding(bytes.length));
  }
  parts.push(Buffer.alloc(1024));
  return Buffer.concat(parts);
}

const json = (value) => Buffer.from(JSON.stringify(value), "utf8");
const descriptor = (mediaType, bytes) => ({ mediaType, size: bytes.length, digest: `sha256:${sha256(bytes)}` });

/**
 * The image an OCI layout holds (`{ index, blobs }`, blobs keyed `sha256:<hex>`), with its app layer,
 * config, manifest and index rewritten to depend only on their content and `epoch`. Answers
 * `{ index, blobs, manifestDigest }`. Throws, changing nothing, if the SDK's own layer does not match
 * its diff id, if the history does not map one entry to each layer, or if the app layer holds an entry
 * that is neither a file nor a directory.
 */
export function reproducibleImage({ index, blobs }, { epoch }) {
  const image = readOciImage({ index, blobs });
  const app = image.layers.at(-1);
  if (!app) throw new Error("the image has no layer");
  const tar = layerTar(app);
  const diffIds = image.config.rootfs?.diff_ids ?? [];
  if (diffIds.length !== image.layers.length) throw new Error(`the config names ${diffIds.length} diff ids for ${image.layers.length} layers`);
  if (diffIds.at(-1) !== `sha256:${sha256(tar)}`) throw new Error(`the app layer's tar is sha256:${sha256(tar)}, not the ${diffIds.at(-1)} the config names`);
  const history = image.config.history ?? [];
  const layered = history.map((entry, position) => (entry.empty_layer ? -1 : position)).filter((position) => position >= 0);
  if (layered.length !== image.layers.length) throw new Error(`the history names ${layered.length} layers, the manifest ${image.layers.length}`);

  const layer = writeTar(readTar(tar), { mtime: epoch });
  const created = new Date(epoch * 1000).toISOString().replace(/\.000Z$/, "Z");
  const config = structuredClone(image.config);
  config.created = created;
  config.rootfs.diff_ids[diffIds.length - 1] = `sha256:${sha256(layer)}`;
  config.history[layered.at(-1)].created = created;
  const configBytes = json(config);
  const manifest = {
    ...image.manifest,
    config: descriptor(image.manifest.config.mediaType, configBytes),
    layers: [...image.manifest.layers.slice(0, -1), descriptor(REPRODUCIBLE_LAYER, layer)],
  };
  const manifestBytes = json(manifest);
  const manifestDescriptor = { ...index.manifests[0], ...descriptor(index.manifests[0].mediaType, manifestBytes) };
  const kept = new Map(image.manifest.layers.slice(0, -1).map((one) => [one.digest, blobs.get(one.digest)]));
  for (const bytes of [layer, configBytes, manifestBytes]) kept.set(`sha256:${sha256(bytes)}`, bytes);
  return { index: { ...index, manifests: [manifestDescriptor] }, blobs: kept, manifestDigest: manifestDescriptor.digest };
}

/** An OCI image layout archive of `{ index, blobs }`, as deterministic as the layer inside it. */
export function writeLayout({ index, blobs }, { mtime }) {
  const file = (path, bytes) => ({ path, type: "file", mode: 0o644, uid: 0, gid: 0, bytes });
  const directory = (path) => ({ path, type: "directory", mode: 0o755, uid: 0, gid: 0 });
  return writeTar([
    file("oci-layout", json({ imageLayoutVersion: "1.0.0" })),
    file("index.json", json(index)),
    directory("blobs"),
    directory("blobs/sha256"),
    ...[...blobs].map(([digest, bytes]) => file(`blobs/sha256/${digest.replace(/^sha256:/, "")}`, bytes)),
  ], { mtime });
}
