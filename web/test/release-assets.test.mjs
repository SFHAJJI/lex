// The release publication (`release-assets.mjs`): published assets read back and verified by their
// hashes and signatures, each check held to fail on the release it must refuse.
//
// The image is a small OCI layout made in memory; the evaluation card is the platform's own, the one
// the live pages carry.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { rehearsalKey, signRehearsal } from "../scripts/image-rehearsal.mjs";
import { writeLayout, writeTar } from "../scripts/image-reproducible.mjs";
import { ASSETS, RELEASE_MANIFEST, RELEASE_SIGNATURE, imageSignatureAsset, publishRelease, releaseFailures, releaseVersion } from "../scripts/release-assets.mjs";

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");
const CARD = readFileSync(new URL("../../schemas/v3-platform/evaluation-card.json", import.meta.url));
const SOURCE = { epoch: 1790799760, from: "commit", commit: "23af798d9c78602726280978edcedf07f9408e98", clean: true };

/** A small image layout archive and its manifest digest. */
function imageArchive(content = "MZ the api") {
  const layer = writeTar([{ path: "app/Lex.V3.Api.dll", type: "file", mode: 0o644, uid: 0, gid: 0, bytes: Buffer.from(content) }], { mtime: SOURCE.epoch });
  const config = Buffer.from(JSON.stringify({ architecture: "amd64", os: "linux", rootfs: { type: "layers", diff_ids: [`sha256:${sha256(layer)}`] } }));
  const manifest = Buffer.from(JSON.stringify({
    schemaVersion: 2,
    config: { mediaType: "application/vnd.oci.image.config.v1+json", size: config.length, digest: `sha256:${sha256(config)}` },
    layers: [{ mediaType: "application/vnd.oci.image.layer.v1.tar", size: layer.length, digest: `sha256:${sha256(layer)}` }],
  }));
  const manifestDigest = `sha256:${sha256(manifest)}`;
  const index = { schemaVersion: 2, manifests: [{ mediaType: "application/vnd.oci.image.manifest.v1+json", size: manifest.length, digest: manifestDigest }] };
  const blobs = new Map([layer, config, manifest].map((bytes) => [`sha256:${sha256(bytes)}`, bytes]));
  return { archive: writeLayout({ index, blobs }, { mtime: SOURCE.epoch }), manifestDigest };
}

/** Publishes a release into a fresh directory; `change` may alter what is published. Answers where, and the key. */
async function published(change = (release) => release) {
  const root = await mkdtemp(join(tmpdir(), "lex-release-"));
  const key = rehearsalKey();
  const { archive, manifestDigest } = imageArchive();
  const signed = signRehearsal({ manifestDigest, reference: "lex-v3-rehearsal:rehearsal", key });
  const release = change({
    version: releaseVersion(SOURCE), source: SOURCE, manifestDigest, corpusSha256: "c".repeat(64), key,
    assets: [[ASSETS.image, archive], [ASSETS.imageSignature, imageSignatureAsset(signed)], [ASSETS.card, CARD], [ASSETS.mountReport, Buffer.from("{\"files\":[]}")]],
  });
  const { directory, manifestSha256 } = await publishRelease(root, release);
  return { root, directory, manifestSha256, key, release };
}

test("a release is published under its version, and reads back whole", async () => {
  const { root, directory, key, release } = await published();
  try {
    assert.equal(releaseVersion(SOURCE), "v3-rehearsal-20260930T202240Z-23af798d9c78");
    assert.ok(directory.endsWith(releaseVersion(SOURCE)));
    assert.deepEqual(await releaseFailures(directory, { publicKeyPem: key.publicKeyPem }), []);
    const manifest = JSON.parse(await readFile(join(directory, RELEASE_MANIFEST), "utf8"));
    assert.deepEqual(manifest.assets.map((asset) => asset.name), [ASSETS.card, ASSETS.image, ASSETS.imageSignature, ASSETS.mountReport].sort());
    assert.equal(manifest.assets.find((asset) => asset.name === ASSETS.card).sha256, sha256(CARD), "the card beside the image is the card the pages carry");
    assert.equal(manifest.rehearsal, true);
    assert.deepEqual(manifest.source, SOURCE, "the release names the commit it was built from");
    await assert.rejects(() => publishRelease(root, release), /already published/, "a published version is never overwritten");
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("an asset changed after publication, added or removed is caught", async () => {
  const { root, directory, key } = await published();
  try {
    const image = join(directory, ASSETS.image);
    const bytes = await readFile(image);
    bytes[bytes.length - 2000] ^= 0xff;
    await writeFile(image, bytes);
    await writeFile(join(directory, "notes.txt"), "an unlisted file");
    await rm(join(directory, ASSETS.mountReport));
    const failures = await releaseFailures(directory, { publicKeyPem: key.publicKeyPem });
    assert.ok(failures.some((failure) => failure.startsWith(`${ASSETS.image} is sha256:`)), failures.join("\n"));
    assert.ok(failures.includes("notes.txt is in the release but not in its manifest"));
    assert.ok(failures.includes(`${ASSETS.mountReport} is in the manifest but not in the release`));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("a release is trusted only under the release's own key: the manifest and the image signed by it", async () => {
  const { root, directory, key } = await published();
  try {
    const other = rehearsalKey();
    const failures = await releaseFailures(directory, { publicKeyPem: other.publicKeyPem });
    assert.ok(failures.includes("the release manifest is signed with another key than the release's"));
    assert.ok(failures.includes("the release manifest's signature does not verify with the release's key"));
    assert.ok(failures.includes("the image is signed with another key than the release's"));

    // A manifest rewritten after signing, even re-signed by another key, does not hold.
    const manifestPath = join(directory, RELEASE_MANIFEST);
    await writeFile(manifestPath, (await readFile(manifestPath, "utf8")).replace("\"rehearsal\": true", "\"rehearsal\": true "));
    assert.ok((await releaseFailures(directory, { publicKeyPem: key.publicKeyPem })).includes("the release manifest's signature does not verify with the release's key"));
    const { sign } = await import("node:crypto");
    await writeFile(join(directory, RELEASE_SIGNATURE), JSON.stringify({ signature: sign("sha256", await readFile(manifestPath), other.privateKey).toString("base64"), public_key_pem: other.publicKeyPem, signer: "lex-v3-rehearsal (not a release identity)", rehearsal: true }));
    const resigned = await releaseFailures(directory, { publicKeyPem: key.publicKeyPem });
    assert.ok(resigned.includes("the release manifest is signed with another key than the release's") && resigned.includes("the release manifest's signature does not verify with the release's key"));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("the image must be the one the manifest and its signature name, and the card must read", async () => {
  const wrongDigest = await published((release) => ({ ...release, manifestDigest: `sha256:${"0".repeat(64)}` }));
  const otherSignature = await published((release) => {
    const signed = signRehearsal({ manifestDigest: imageArchive("another api").manifestDigest, reference: "lex-v3-rehearsal:rehearsal", key: release.key });
    return { ...release, assets: release.assets.map(([name, bytes]) => [name, name === ASSETS.imageSignature ? imageSignatureAsset(signed) : bytes]) };
  });
  const badCard = await published((release) => ({ ...release, assets: release.assets.map(([name, bytes]) => [name, name === ASSETS.card ? Buffer.from("{\"schema\":\"lex-v3-evaluation-card/1\"}") : bytes]) }));
  try {
    assert.ok((await releaseFailures(wrongDigest.directory, { publicKeyPem: wrongDigest.key.publicKeyPem })).some((failure) => /^the image read back is sha256:[0-9a-f]{64}, not the sha256:0+ the manifest names$/.test(failure)));
    assert.ok((await releaseFailures(otherSignature.directory, { publicKeyPem: otherSignature.key.publicKeyPem })).some((failure) => failure.startsWith("the image signature: the signature names sha256:")),
      "a signature over another image's digest");
    assert.ok((await releaseFailures(badCard.directory, { publicKeyPem: badCard.key.publicKeyPem })).some((failure) => failure.startsWith("the evaluation card does not read:")),
      "a card the page could not print");
  } finally {
    for (const one of [wrongDigest, otherSignature, badCard]) await rm(one.root, { recursive: true, force: true });
  }
});
