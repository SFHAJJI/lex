// The image runner's pure parts (`image-run.mjs`): the paths it hands WSL and the pattern that ends a
// run's processes, which must not end the shell that runs it (it did, twice, before it was bracketed
// and split from the removal).

import assert from "node:assert/strict";
import test from "node:test";

import { endProcesses, startImage, tmpMebibytesFor, TMP_MARGIN_MEBIBYTES, wslPath } from "../scripts/image-run.mjs";

test("the container's /tmp holds a copy of every index the mount carries, generations included, plus the margin", async () => {
  // The combined population mount's indexes are 2.3 GB; the fixed 256m tmpfs could not hold their copies (2026-10-13).
  const { mkdtemp, mkdir, rm, writeFile } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const mount = await mkdtemp(join(tmpdir(), "lex-tmp-size-"));
  try {
    await writeFile(join(mount, "luxembourg-index.sqlite3"), Buffer.alloc(3 * 1024 * 1024 + 1));
    await writeFile(join(mount, "europe-index.sqlite3"), Buffer.alloc(1024 * 1024));
    await mkdir(join(mount, "generations", "one"), { recursive: true });
    await writeFile(join(mount, "generations", "one", "luxembourg-index.sqlite3"), Buffer.alloc(512 * 1024));
    await writeFile(join(mount, "lex-corpus-6.json"), Buffer.alloc(8 * 1024 * 1024));
    // 4.5 MiB and one byte of indexes: 5 MiB whole, and the corpus file is read, not copied.
    assert.equal(await tmpMebibytesFor(mount), 5 + TMP_MARGIN_MEBIBYTES);
  } finally {
    await rm(mount, { recursive: true, force: true });
  }
  await assert.rejects(startImage({ run: "/nowhere", config: { config: {} }, tmpMebibytes: 255 }), /not a whole size of at least 256/);
  await assert.rejects(startImage({ run: "/nowhere", config: { config: {} }, tmpMebibytes: 300.5 }), /not a whole size/);
});

test("ending a container's run ends its first process, which the API is, so its /tmp goes with it (review of #950)", async (t) => {
  const { CONTAINER_COMMAND } = await import("../scripts/image-run.mjs");
  assert.deepEqual(CONTAINER_COMMAND, ["unshare", "--mount", "--pid", "--fork", "--kill-child"]);
  // Shown where WSL runs (the rehearsal's machine): a stand-in first process with no signal handler, as the API has none
  // for the hang-up a closed wsl.exe sends, survived the run's end without --kill-child.
  const { spawn, spawnSync } = await import("node:child_process");
  const distribution = process.env.LEX_WSL_DISTRIBUTION ?? "Ubuntu-24.04";
  const inWsl = (script) => spawnSync("wsl", ["-d", distribution, "-u", "root", "-e", "sh", "-c", script], { encoding: "utf8" });
  if (process.platform !== "win32" || inWsl("true").status !== 0) {
    t.skip("WSL is not available here");
    return;
  }
  const run = `/tmp/lex-image-run-kill-${process.pid}`;
  const marker = `sleep 731 # ${run}`;
  const child = spawn("wsl", ["-d", distribution, "-u", "root", "-e", ...CONTAINER_COMMAND, "sh", "-c", `exec ${marker}`], { stdio: "ignore" });
  const count = () => Number(inWsl("pgrep -c -f '^sleep 731$' || true").stdout.trim() || 0);
  try {
    for (let tries = 0; tries < 50 && count() === 0; tries++) await new Promise((resolve) => setTimeout(resolve, 200));
    assert.equal(count(), 1, "the stand-in started");
    child.kill();
    inWsl(endProcesses(run));
    for (let tries = 0; tries < 25 && count() > 0; tries++) await new Promise((resolve) => setTimeout(resolve, 200));
    assert.equal(count(), 0, "the container's first process ended with its run");
  } finally {
    inWsl("pkill -9 -f '^sleep 731$' || true");
  }
});

test("a Windows path is handed to WSL as WSL mounts it", () => {
  assert.equal(wslPath(String.raw`C:\Users\Hajji\AppData\Local\Temp\lex-image\image.tar`), "/mnt/c/Users/Hajji/AppData/Local/Temp/lex-image/image.tar");
  assert.equal(wslPath("D:/work/x"), "/mnt/d/work/x");
});

test("the pattern that ends a run's processes matches them and never the shell that runs it", () => {
  const run = "/tmp/lex-image-run-68fdcc24-edd8-45dc-a37e-2e6dba547f8e";
  const script = endProcesses(run);
  const pattern = script.match(/pkill -f '([^']+)'/)[1];
  // pkill -f matches the pattern as an extended regular expression against each full command line.
  const matches = (commandLine) => new RegExp(pattern).test(commandLine);
  assert.ok(matches(`chroot --userspec=1654:1654 ${run}/rootfs dotnet /app/Lex.V3.Api.dll`), "the image's API");
  assert.ok(matches(`python3 ${run}/watch.py ${run}/rootfs/tmp ${run}/events.log`), "the /tmp watcher");
  assert.ok(!matches(`sh -c ${script}`), "not the shell running the pattern itself");
  assert.ok(matches(`sh -c ${script}; rm -rf '${run}'`), "a shell that also names the directory would match, which is why the removal is a command of its own");
});

test("the start refuses any writable mount but the watched /tmp and the four devices (review of #825)", async () => {
  const { spawnSync } = await import("node:child_process");
  const { WRITABLE_MOUNTS_AWK, DEVICES } = await import("../scripts/image-run.mjs");
  const root = "/tmp/lex-image-run-5f1e";
  const table = [
    `${root} ro,relatime`,
    `${root}/proc ro,nosuid,nodev,noexec,relatime`,
    `${root}/dev ro,nosuid,noexec,relatime`,
    ...DEVICES.map((device) => `${root}/dev/${device} rw,nosuid,relatime`),
    `${root}/tmp rw,nosuid,nodev,relatime`,
    `${root}/dev/shm rw,nosuid,nodev,noatime`,
    `${root}/var/log rw,relatime`,
  ].join("\n");
  const result = spawnSync("awk", ["-v", `root=${root}`, WRITABLE_MOUNTS_AWK], { input: table, encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(result.stdout.trim().split("\n"), [`${root}/dev/shm`, `${root}/var/log`], "the host's /dev/shm and any other writable mount are named; /tmp, the devices and the read-only mounts are not");
});
