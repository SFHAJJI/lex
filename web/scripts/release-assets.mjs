// The release path's publication, rehearsed with no production credential: the versioned release assets
// are published, read back, and verified by their hashes and signatures (the launch contract's release
// path; ruling 2 puts the evaluation card beside the release assets).
//
// A release is a directory named by its version, which is never overwritten. It holds:
// - `lex-v3-image.oci.tar`: the image, as the OCI layout archive the rehearsal built;
// - `lex-v3-image.sig.json`: the image's signature over its manifest digest;
// - `evaluation-card.json`: the evaluation card the image's Trust and Coverage page carries;
// - `mount-report.json`: what the mount says it holds (its build report, or the fixture's manifest);
// - `release-manifest.json`: the version, the source commit, the image's manifest digest, the corpus
//   digest, and each asset by name, size and SHA-256;
// - `release-manifest.sig.json`: a signature over the release manifest's bytes.
// Both signatures are the rehearsal identity's, one key for the run. Reading back trusts nothing the
// directory says about itself: the key is the caller's; the directory, the version and the source the
// manifest signs must name each other; every asset is hashed again; the image is read blob by blob and
// its signature checked against the digest the archive gives; and the card must be the very card the
// image serves at its route, and read by the same rules the page applies (review of #833: each was
// checked alone, so a release could carry a card the image does not serve, or a version naming another
// commit than the source it signs).

import { createHash, createPublicKey, sign, verify } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { basename, join } from "node:path";
import { REHEARSAL_IDENTITY, layerTar, readLayout, readOciImage, readTar, rehearsalSignatureFailures } from "./image-rehearsal.mjs";
import { CARD_ROUTE, readEvaluationCard } from "./evaluation-card.mjs";

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");

export const RELEASE_SCHEMA = "lex-v3-release-rehearsal/1";
export const RELEASE_MANIFEST = "release-manifest.json";
export const RELEASE_SIGNATURE = "release-manifest.sig.json";
export const ASSETS = Object.freeze({
  image: "lex-v3-image.oci.tar",
  imageSignature: "lex-v3-image.sig.json",
  card: "evaluation-card.json",
  mountReport: "mount-report.json",
});

/** A release version from the source: the source date and the commit, so a version names its sources. */
export function releaseVersion({ epoch, commit }) {
  const date = new Date(epoch * 1000).toISOString().replace(/[-:]/g, "").replace(/\.\d{3}Z$/, "Z");
  return `v3-rehearsal-${date}-${(commit ?? "nocommit").slice(0, 12)}`;
}

/** An image signature as the release holds it: the payload's bytes in base64 beside the signature. */
export function imageSignatureAsset({ payload, signature, publicKeyPem }) {
  return Buffer.from(`${JSON.stringify({ payload: payload.toString("base64"), signature, public_key_pem: publicKeyPem }, null, 2)}\n`, "utf8");
}

/**
 * Publishes a release into `root/<version>/`: each asset, the release manifest naming them, and its
 * signature by `key` (`{ privateKey, publicKeyPem }`). Refuses a version already published.
 * Answers `{ directory, manifestSha256 }`.
 */
export async function publishRelease(root, { version, source, manifestDigest, corpusSha256, assets, key }) {
  const directory = join(root, version);
  if (existsSync(directory)) throw new Error(`release ${version} is already published, and a published version is never overwritten`);
  await mkdir(directory, { recursive: true });
  const listed = [];
  for (const [name, bytes] of [...assets].sort(([a], [b]) => (a < b ? -1 : 1))) {
    await writeFile(join(directory, name), bytes);
    listed.push({ name, size: bytes.length, sha256: sha256(bytes) });
  }
  const manifest = Buffer.from(`${JSON.stringify({
    schema: RELEASE_SCHEMA,
    version,
    rehearsal: true,
    signer: REHEARSAL_IDENTITY,
    source,
    image: { manifest_digest: manifestDigest },
    corpus: { sha256: corpusSha256 },
    assets: listed,
  }, null, 2)}\n`, "utf8");
  await writeFile(join(directory, RELEASE_MANIFEST), manifest);
  await writeFile(join(directory, RELEASE_SIGNATURE), `${JSON.stringify({
    signature: sign("sha256", manifest, key.privateKey).toString("base64"),
    public_key_pem: key.publicKeyPem,
    signer: REHEARSAL_IDENTITY,
    rehearsal: true,
  }, null, 2)}\n`);
  return { directory, manifestSha256: sha256(manifest) };
}

/**
 * A published release read back from disk and verified, as failures (empty when it holds), against
 * the key the caller expects (`publicKeyPem`): the manifest's signature; every asset listed, present,
 * of its size and hash, and nothing unlisted; the image read blob by blob and named by the manifest's
 * digest; the image's signature over that digest by the same key; and the card readable by the page's
 * rules.
 */
export async function releaseFailures(directory, { publicKeyPem }) {
  const failures = [];
  const expectedKey = createPublicKey(publicKeyPem).export({ type: "spki", format: "der" });
  const sameKey = (pem) => { try { return createPublicKey(pem).export({ type: "spki", format: "der" }).equals(expectedKey); } catch { return false; } };

  const manifestBytes = await readFile(join(directory, RELEASE_MANIFEST));
  const signed = JSON.parse(await readFile(join(directory, RELEASE_SIGNATURE), "utf8"));
  if (!sameKey(signed.public_key_pem)) failures.push("the release manifest is signed with another key than the release's");
  if (!verify("sha256", manifestBytes, createPublicKey(publicKeyPem), Buffer.from(signed.signature ?? "", "base64"))) failures.push("the release manifest's signature does not verify with the release's key");
  const manifest = JSON.parse(manifestBytes.toString("utf8"));
  if (manifest.schema !== RELEASE_SCHEMA) failures.push(`the release manifest is not ${RELEASE_SCHEMA}`);
  if (manifest.rehearsal !== true || manifest.signer !== REHEARSAL_IDENTITY || signed.signer !== REHEARSAL_IDENTITY) failures.push("the release does not say it is the rehearsal's");
  const named = manifest.source && Number.isInteger(manifest.source.epoch) ? releaseVersion(manifest.source) : null;
  if (manifest.version !== named) failures.push(`the manifest's version ${manifest.version} is not the one its source names, ${named}`);
  if (basename(directory) !== manifest.version) failures.push(`the release is published as ${basename(directory)}, not as its version ${manifest.version}`);

  const listed = new Map((manifest.assets ?? []).map((asset) => [asset.name, asset]));
  const present = new Set(await readdir(directory));
  for (const name of [...present].sort()) {
    if (!listed.has(name) && name !== RELEASE_MANIFEST && name !== RELEASE_SIGNATURE) failures.push(`${name} is in the release but not in its manifest`);
  }
  const held = new Map();
  for (const [name, asset] of listed) {
    if (!present.has(name)) { failures.push(`${name} is in the manifest but not in the release`); continue; }
    const bytes = await readFile(join(directory, name));
    if (bytes.length !== asset.size) failures.push(`${name} is ${bytes.length} bytes, not the ${asset.size} the manifest records`);
    if (sha256(bytes) !== asset.sha256) failures.push(`${name} is sha256:${sha256(bytes)}, not the ${asset.sha256} the manifest records`);
    held.set(name, bytes);
  }
  for (const name of Object.values(ASSETS)) if (!listed.has(name)) failures.push(`the release lists no ${name}`);

  const archive = held.get(ASSETS.image);
  let manifestDigest = null;
  let servedCard;
  if (archive) {
    try {
      const image = readOciImage(readLayout(archive));
      manifestDigest = image.manifestDigest;
      if (manifestDigest !== manifest.image?.manifest_digest) failures.push(`the image read back is ${manifestDigest}, not the ${manifest.image?.manifest_digest} the manifest names`);
      // The card the image serves at its route: the live pages sit under app/v3-web in the app layer.
      const app = image.layers.at(-1);
      servedCard = app ? readTar(layerTar(app)).find((entry) => entry.path === `app/v3-web${CARD_ROUTE}`)?.bytes : undefined;
      if (servedCard === undefined) failures.push(`the image serves no card: its app layer holds no app/v3-web${CARD_ROUTE}`);
    } catch (error) {
      failures.push(`the image does not read back: ${error.message}`);
    }
  }
  const imageSignature = held.get(ASSETS.imageSignature);
  if (imageSignature && manifestDigest) {
    const parsed = JSON.parse(imageSignature.toString("utf8"));
    if (!sameKey(parsed.public_key_pem)) failures.push("the image is signed with another key than the release's");
    for (const failure of rehearsalSignatureFailures({ payload: Buffer.from(parsed.payload ?? "", "base64"), signature: parsed.signature ?? "", publicKeyPem, manifestDigest })) {
      failures.push(`the image signature: ${failure}`);
    }
  }
  const card = held.get(ASSETS.card);
  if (card) {
    try { readEvaluationCard(JSON.parse(card.toString("utf8"))); } catch (error) { failures.push(`the evaluation card does not read: ${error.message}`); }
    if (servedCard !== undefined && !servedCard.equals(card)) failures.push(`the release's card is not the card the image serves at ${CARD_ROUTE}`);
  }
  return failures;
}
