// The release path's bytes in pieces (`large-bytes.mjs`): the population image's app layer is 2.5 GB, and Node refuses one
// SHA-256 update over 2 GiB and one fs.readFile of a file over 2 GiB (the release rehearsal of 2026-10-13 stopped there).

import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { PIECE, readLargeFile, sha256Hex } from "../scripts/large-bytes.mjs";

test("hashed in pieces, the digest is the one-call digest, whatever the piece size", () => {
  const bytes = randomBytes(1000);
  const whole = createHash("sha256").update(bytes).digest("hex");
  for (const piece of [1, 7, 999, 1000, 1001, PIECE]) assert.equal(sha256Hex(bytes, piece), whole, `piece ${piece}`);
  assert.equal(sha256Hex(Buffer.alloc(0)), createHash("sha256").digest("hex"), "the empty input");
  assert.ok(PIECE < 2 ** 31, "a piece is under the 2 GiB a single update is refused at");
});

test("read in pieces, a file is its bytes, whatever the piece size", async () => {
  const directory = await mkdtemp(join(tmpdir(), "lex-large-bytes-"));
  try {
    const bytes = randomBytes(4099);
    const path = join(directory, "file.bin");
    await writeFile(path, bytes);
    for (const piece of [1, 4096, 4099, 10_000]) assert.ok((await readLargeFile(path, piece)).equals(bytes), `piece ${piece}`);
    await writeFile(path, Buffer.alloc(0));
    assert.equal((await readLargeFile(path)).length, 0);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
