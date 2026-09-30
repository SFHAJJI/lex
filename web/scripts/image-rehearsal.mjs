// The release pipeline's image steps, rehearsed with no production credential: one command builds the
// one-server OCI image (the API, the live web pages and a corpus mount), verifies what it holds,
// signs it with a rehearsal identity, verifies the signature, and removes it (the launch contract's
// release path; production signing, credentials and deployment stay with the owner).
//
//   node scripts/image-rehearsal.mjs --mount <a v3-corpus directory with its build-report.json> [--keep]
//
// The image is built without a container daemon, by the .NET SDK (`dotnet publish -t:PublishContainer`,
// the base image pinned by digest in `Lex.V3.Api.csproj`), as an OCI image layout archive. Then:
// - every blob the index names (manifest, config, layers) must have the digest and size it is named by;
// - the app layer must hold the API, every file of the live pages under `app/v3-web/` and every file of
//   the mount under `app/v3-corpus/`, byte for byte, and each file the mount's build report lists with
//   its recorded digest;
// - the image must run as a non-root user, start the API, and name its base image by digest.
// The signature is the rehearsal's own: an ECDSA P-256 key made for the run and never kept, over a
// signing payload in the shape container signatures use, naming the manifest digest and saying it is a
// rehearsal. Verifying it checks the signature, the digest and that rehearsal label. Nothing is pushed
// anywhere, and the archive, its extraction and the publish directory are removed at the end, which the
// report records. Running the image and probing it is the next step.

import { spawnSync } from "node:child_process";
import { createHash, createPublicKey, generateKeyPairSync, sign, verify } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdtemp, readFile, readdir, rm, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, relative, resolve, sep } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { gunzipSync } from "node:zlib";

const repository = resolve(fileURLToPath(new URL("../../", import.meta.url)));
const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");

/** The rehearsal identity: named as what it is, so no one mistakes it for the release signer. */
export const REHEARSAL_IDENTITY = "lex-v3-rehearsal (not a release identity)";

/**
 * The entries of a tar archive, as `{ path, type, bytes }`: ustar names with their prefix, and the long
 * names pax headers (`x`) and GNU headers (`L`) carry.
 */
export function readTar(buffer) {
  const entries = [];
  let offset = 0;
  let longName = null;
  const text = (start, length) => buffer.subarray(start, start + length).toString("utf8").replace(/\0.*$/s, "");
  while (offset + 512 <= buffer.length) {
    const header = buffer.subarray(offset, offset + 512);
    if (header.every((byte) => byte === 0)) break;
    const name = text(offset, 100);
    const size = parseInt(text(offset + 124, 12).trim() || "0", 8);
    const type = String.fromCharCode(header[156] || 48);
    const prefix = text(offset + 345, 155);
    const bytes = buffer.subarray(offset + 512, offset + 512 + size);
    offset += 512 + Math.ceil(size / 512) * 512;
    if (type === "x") {
      const records = bytes.toString("utf8");
      const path = records.match(/\d+ path=([^\n]*)\n/);
      if (path) longName = path[1];
      continue;
    }
    if (type === "L") { longName = bytes.toString("utf8").replace(/\0.*$/s, ""); continue; }
    if (type === "g") continue;
    const path = longName ?? (prefix ? `${prefix}/${name}` : name);
    longName = null;
    entries.push({ path: path.replace(/^\.\//, ""), type: type === "5" ? "directory" : type === "0" ? "file" : type, bytes });
  }
  return entries;
}

/**
 * The image an OCI image layout holds: its manifest, config and layers, each blob checked against the
 * digest and size it is named by. `blobs` maps `sha256:<hex>` to the blob's bytes.
 */
export function readOciImage({ index, blobs }) {
  const blob = (descriptor, what) => {
    const bytes = blobs.get(descriptor.digest);
    if (bytes === undefined) throw new Error(`the image names ${what} ${descriptor.digest}, which the layout does not hold`);
    if (`sha256:${sha256(bytes)}` !== descriptor.digest) throw new Error(`${what} ${descriptor.digest} does not have that digest: its bytes are sha256:${sha256(bytes)}`);
    if (bytes.length !== descriptor.size) throw new Error(`${what} ${descriptor.digest} is ${bytes.length} bytes, not the ${descriptor.size} it is named with`);
    return bytes;
  };
  if (!Array.isArray(index.manifests) || index.manifests.length !== 1) throw new Error("the image layout must name exactly one manifest");
  const manifestDescriptor = index.manifests[0];
  const manifest = JSON.parse(blob(manifestDescriptor, "the manifest").toString("utf8"));
  const config = JSON.parse(blob(manifest.config, "the config").toString("utf8"));
  const layers = manifest.layers.map((layer, position) => ({ ...layer, bytes: blob(layer, `layer ${position + 1}`) }));
  return { manifestDigest: manifestDescriptor.digest, manifest, config, layers };
}

/** Every file under a directory, as `[relative path with forward slashes, bytes]`. */
async function filesUnder(root) {
  const found = [];
  for (const entry of await readdir(root, { recursive: true, withFileTypes: true })) {
    if (!entry.isFile()) continue;
    const path = join(entry.parentPath ?? entry.path, entry.name);
    found.push([relative(root, path).split(sep).join("/"), await readFile(path)]);
  }
  return found;
}

/**
 * What the image must hold and how it must run, as failures (empty when it holds). `web` and `mount`
 * are the directories the image was built from, as `[path, bytes]` lists; `report` is the mount's
 * build report.
 */
export function imageFailures(image, { web, mount, report }) {
  const failures = [];
  const { config } = image;
  if (config.os !== "linux" || config.architecture !== "amd64") failures.push(`the image is ${config.os}/${config.architecture}, not linux/amd64`);
  // A user is `user[:group]`, by name or number; root is the name `root` or the number 0, whatever group
  // follows (review of #821: `0:1654` and `root:1654` run as root).
  const user = config.config?.User ?? "";
  const who = user.split(":")[0];
  if (who === "" || who === "root" || (/^\d+$/.test(who) && Number(who) === 0)) {
    failures.push(`the image runs as ${user === "" ? "root (no user set)" : user}, not a non-root user`);
  }
  if (JSON.stringify(config.config?.Entrypoint) !== JSON.stringify(["dotnet", "/app/Lex.V3.Api.dll"])) failures.push(`the image starts ${JSON.stringify(config.config?.Entrypoint)}, not the API`);
  const base = config.config?.Labels?.["org.opencontainers.image.base.name"] ?? "";
  if (!/@sha256:[0-9a-f]{64}$/.test(base)) failures.push(`the base image is not named by digest: ${JSON.stringify(base)}`);
  const app = image.layers.at(-1);
  if (!app) return [...failures, "the image has no layer"];
  const files = new Map(readTar(gunzipSync(app.bytes)).filter((entry) => entry.type === "file").map((entry) => [entry.path, entry.bytes]));
  if (!files.has("app/Lex.V3.Api.dll")) failures.push("the app layer holds no app/Lex.V3.Api.dll");
  for (const [label, prefix, list] of [["live page file", "app/v3-web/", web], ["mount file", "app/v3-corpus/", mount]]) {
    if (list.length === 0) failures.push(`no ${label} was given to check`);
    for (const [path, bytes] of list) {
      const held = files.get(`${prefix}${path}`);
      if (held === undefined) failures.push(`the image holds no ${prefix}${path}`);
      else if (!held.equals(bytes)) failures.push(`${prefix}${path} in the image differs from the ${label} it was built from`);
    }
  }
  for (const file of report.files ?? []) {
    const held = files.get(`app/v3-corpus/${file.Name}`);
    if (held === undefined) failures.push(`the image holds no ${file.Name}, which the build report lists`);
    else if (sha256(held) !== file.Sha256) failures.push(`${file.Name} in the image is sha256:${sha256(held)}, not the ${file.Sha256} the build report records`);
  }
  if ((report.files ?? []).length === 0) failures.push("the build report lists no file");
  return failures;
}

/**
 * A rehearsal signature over the image's manifest digest: a key made for the run, a signing payload in
 * the shape container signatures use, and the payload signed with ECDSA P-256 over SHA-256.
 */
export function signRehearsal({ manifestDigest, reference }) {
  const { privateKey, publicKey } = generateKeyPairSync("ec", { namedCurve: "P-256" });
  const payload = Buffer.from(JSON.stringify({
    critical: {
      identity: { "docker-reference": reference },
      image: { "docker-manifest-digest": manifestDigest },
      type: "cosign container image signature",
    },
    optional: { signer: REHEARSAL_IDENTITY, rehearsal: true },
  }), "utf8");
  return {
    payload,
    signature: sign("sha256", payload, privateKey).toString("base64"),
    publicKeyPem: publicKey.export({ type: "spki", format: "pem" }).toString(),
  };
}

/** Whether a rehearsal signature holds for this manifest digest: the signature, the digest and the label. */
export function rehearsalSignatureFailures({ payload, signature, publicKeyPem, manifestDigest }) {
  const failures = [];
  if (!verify("sha256", payload, createPublicKey(publicKeyPem), Buffer.from(signature, "base64"))) failures.push("the signature does not verify over its payload with this key");
  let parsed = null;
  try { parsed = JSON.parse(payload.toString("utf8")); } catch { failures.push("the signing payload is not JSON"); }
  if (parsed !== null) {
    if (parsed.critical?.image?.["docker-manifest-digest"] !== manifestDigest) failures.push(`the signature names ${parsed.critical?.image?.["docker-manifest-digest"]}, not this image's manifest ${manifestDigest}`);
    if (parsed.critical?.type !== "cosign container image signature") failures.push("the signing payload is not a container image signature");
    if (parsed.optional?.rehearsal !== true || parsed.optional?.signer !== REHEARSAL_IDENTITY) failures.push("the signature does not say it is the rehearsal's");
  }
  return failures;
}

/** Runs a command, failing with its output if it fails. */
function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: "utf8", maxBuffer: 64 * 1024 * 1024, ...options });
  if (result.status !== 0) throw new Error(`${command} ${args.join(" ")} failed (${result.status}): ${(result.stdout ?? "").slice(-2000)}${(result.stderr ?? "").slice(-2000)}`);
  return result;
}

/** The rehearsal, end to end. Returns its report; throws on the first step that fails. */
export async function rehearse({ mount, keep = false, probe = true, log = () => {} }) {
  const mountPath = resolve(mount);
  const report = JSON.parse(await readFile(join(mountPath, "build-report.json"), "utf8"));
  const work = await mkdtemp(join(tmpdir(), "lex-image-rehearsal-"));
  const publishDirectory = join(repository, "src", "Lex.V3.Api", "bin", "Release", "net10.0", "linux-x64", "publish");
  const archive = join(work, "lex-v3-rehearsal.tar");
  const result = { mount: mountPath, corpusSha256: report.corpus?.Sha256 ?? null };
  let container = null;
  try {
    log("building the live pages");
    const { buildLive } = await import("./build-live.mjs");
    const webRoot = join(work, "v3-web");
    await buildLive(pathToFileURL(`${webRoot}/`), { buildTag: "image-rehearsal" });

    log("building the image (dotnet publish -t:PublishContainer)");
    await rm(publishDirectory, { recursive: true, force: true });
    run("dotnet", ["publish", join(repository, "src", "Lex.V3.Api", "Lex.V3.Api.csproj"), "-c", "Release", "-r", "linux-x64", "--self-contained", "false",
      `-p:LexImageWebRoot=${webRoot}`, `-p:LexImageMount=${mountPath}`, `-p:ContainerArchiveOutputPath=${archive}`,
      "-p:ContainerRepository=lex-v3-rehearsal", "-p:ContainerImageTag=rehearsal", "-t:PublishContainer", "-m:1", "-nodeReuse:false", "-v", "q"]);
    spawnSync("dotnet", ["build-server", "shutdown"], { encoding: "utf8" });
    result.archiveBytes = (await stat(archive)).size;

    log("verifying the image");
    const layout = readTar(await readFile(archive));
    const named = new Map(layout.filter((entry) => entry.type === "file").map((entry) => [entry.path, entry.bytes]));
    const index = JSON.parse(named.get("index.json").toString("utf8"));
    const blobs = new Map([...named].filter(([path]) => path.startsWith("blobs/sha256/")).map(([path, bytes]) => [`sha256:${path.slice("blobs/sha256/".length)}`, bytes]));
    const image = readOciImage({ index, blobs });
    const web = await filesUnder(webRoot);
    const corpus = await filesUnder(mountPath);
    const failures = imageFailures(image, { web, mount: corpus, report });
    Object.assign(result, {
      manifestDigest: image.manifestDigest,
      configDigest: image.manifest.config.digest,
      layers: image.layers.map((layer) => layer.digest),
      base: image.config.config.Labels["org.opencontainers.image.base.name"],
      user: image.config.config.User,
      webFilesVerified: web.length,
      mountFilesVerified: corpus.length,
      reportFilesVerified: (report.files ?? []).length,
      imageFailures: failures,
    });
    if (failures.length > 0) throw new Error(`the image does not hold what it must:\n- ${failures.join("\n- ")}`);

    log("signing with the rehearsal identity and verifying");
    const signed = signRehearsal({ manifestDigest: image.manifestDigest, reference: "lex-v3-rehearsal:rehearsal" });
    const signatureFailures = rehearsalSignatureFailures({ ...signed, manifestDigest: image.manifestDigest });
    Object.assign(result, { signer: REHEARSAL_IDENTITY, signatureVerified: signatureFailures.length === 0, publicKeyPem: signed.publicKeyPem });
    if (signatureFailures.length > 0) throw new Error(`the rehearsal signature does not hold:\n- ${signatureFailures.join("\n- ")}`);

    if (probe) {
      // The zero-traffic probes, against the image itself: health (it answers), the API (each page's
      // request, answered from the mount it carries), the browser (every live screen, served by the
      // image), privacy (nothing written after its first answer, on its output or its /tmp) and the
      // security headers the page arrives with.
      log("running the image in WSL and probing it");
      const { unpackImage, startImage } = await import("./image-run.mjs");
      const { realMountRuns } = await import("./journey.mjs");
      const { findBrowser } = await import("./browser-evidence.mjs");
      container = unpackImage({ archive, layers: image.layers.map((layer) => layer.digest) });
      const runs = await realMountRuns(null, mountPath, { servedByApi: true, keyboard: false, startServer: () => startImage({ run: container, config: image.config }) }, await findBrowser(), null);
      result.probes = runs.map(([label, { observed, failures }]) => ({
        step: label, state: observed.answerState, failures,
        paintedElements: observed.paint?.painted ?? 0, citationsVerified: observed.verifications?.length ?? 0,
      }));
      const failing = result.probes.filter((one) => one.failures.length > 0);
      if (failing.length > 0) throw new Error(`the image failed its probes:\n${failing.map((one) => `- ${one.step}: ${one.failures.join("; ")}`).join("\n")}`);
    }
    return result;
  } finally {
    if (!keep) {
      await rm(work, { recursive: true, force: true });
      await rm(publishDirectory, { recursive: true, force: true });
      result.removed = { archive: !existsSync(archive), work: !existsSync(work), publish: !existsSync(publishDirectory) };
      if (container !== null) {
        const { removeRun } = await import("./image-run.mjs");
        result.removed.container = removeRun(container);
      }
    }
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const argv = process.argv.slice(2);
  const at = argv.indexOf("--mount");
  if (at < 0 || at + 1 >= argv.length) {
    console.error("usage: node scripts/image-rehearsal.mjs --mount <v3-corpus directory> [--keep]");
    process.exit(2);
  }
  rehearse({ mount: argv[at + 1], keep: argv.includes("--keep"), probe: !argv.includes("--no-probe"), log: (line) => console.error(`- ${line}`) }).then(
    (result) => { console.log(JSON.stringify(result, null, 2)); },
    (error) => { console.error(error.message); process.exit(1); },
  );
}
