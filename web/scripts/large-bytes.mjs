// Bytes too large for one call: the release image carries the population mount (2.5 GB in its app layer), and Node refuses
// one SHA-256 update over 2 GiB ("data is too long") and one fs.readFile of a file over 2 GiB. The release rehearsal over
// the population stopped there (2026-10-13). These do the same work in pieces: the same digest, the same bytes.

import { createHash } from "node:crypto";
import { open } from "node:fs/promises";

/** The piece size: well below both limits. */
export const PIECE = 1 << 30;

/** The lowercase SHA-256 of `bytes`, hashed a piece at a time. */
export function sha256Hex(bytes, piece = PIECE) {
  const hash = createHash("sha256");
  for (let at = 0; at < bytes.length; at += piece) hash.update(bytes.subarray(at, Math.min(bytes.length, at + piece)));
  return hash.digest("hex");
}

/** A file's bytes, read a piece at a time into one buffer of its size. */
export async function readLargeFile(path, piece = PIECE) {
  const file = await open(path, "r");
  try {
    const { size } = await file.stat();
    const bytes = Buffer.allocUnsafe(size);
    let at = 0;
    while (at < size) {
      const { bytesRead } = await file.read(bytes, at, Math.min(piece, size - at), at);
      if (bytesRead === 0) throw new Error(`${path} ended at ${at} of ${size} bytes`);
      at += bytesRead;
    }
    return bytes;
  } finally {
    await file.close();
  }
}
