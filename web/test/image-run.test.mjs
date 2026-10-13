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

test("ending a container's run ends every process of the container, so its /tmp goes with it (review of #950)", async (t) => {
  const { CONTAINER_COMMAND } = await import("../scripts/image-run.mjs");
  assert.deepEqual(CONTAINER_COMMAND, ["unshare", "--mount", "--pid", "--fork", "--kill-child"]);
  // Shown where WSL runs (the rehearsal's machine), with a stand-in started as the image is: in the run's root filesystem,
  // bound on itself in the container's mount namespace, by `chroot --userspec` under another user, with no handler for
  // the hang-up a closed wsl.exe sends. The change of user clears the parent-death signal --kill-child sets, so ending
  // unshare left the stand-in (and every population probe's API, each holding its 2.2 GB /tmp) running: the release's
  // probes filled WSL's memory by the third start and the next missed its deadline (2026-10-13). The run's end ends the
  // processes whose root is the run's, and returns only once none is left.
  const { spawn, spawnSync } = await import("node:child_process");
  const distribution = process.env.LEX_WSL_DISTRIBUTION ?? "Ubuntu-24.04";
  const inWsl = (script) => spawnSync("wsl", ["-d", distribution, "-u", "root", "-e", "sh", "-c", script], { encoding: "utf8" });
  if (process.platform !== "win32" || inWsl("true").status !== 0) {
    t.skip("WSL is not available here");
    return;
  }
  const run = `/tmp/lex-image-run-kill-${process.pid}`;
  // A root filesystem holding `sleep` and the libraries it loads.
  const prepared = inWsl(`set -e; R=${run}/rootfs; for file in /usr/bin/sleep $(ldd /usr/bin/sleep | grep -o '/[^ ]*'); do mkdir -p "$R$(dirname "$file")"; cp "$file" "$R$file"; done`);
  assert.equal(prepared.status, 0, prepared.stderr);
  const start = `R=${run}/rootfs; mount --bind "$R" "$R"; exec chroot --userspec=65534:65534 "$R" /usr/bin/sleep 731`;
  const child = spawn("wsl", ["-d", distribution, "-u", "root", "-e", ...CONTAINER_COMMAND, "sh", "-c", start], { stdio: "ignore" });
  const count = () => Number(inWsl("pgrep -c -f '^/usr/bin/sleep 731$' || true").stdout.trim() || 0);
  try {
    for (let tries = 0; tries < 50 && count() === 0; tries++) await new Promise((resolve) => setTimeout(resolve, 200));
    assert.equal(count(), 1, "the stand-in started");
    child.kill();
    const ended = inWsl(endProcesses(run));
    assert.equal(ended.status, 0, `${ended.stdout}${ended.stderr}`);
    assert.equal(count(), 0, "the container's process ended with its run, before the run's end returned");
  } finally {
    inWsl(`pkill -9 -f '^/usr/bin/sleep 731$'; rm -rf ${run}; true`);
  }
});

test("a Windows path is handed to WSL as WSL mounts it", () => {
  assert.equal(wslPath(String.raw`C:\Users\Hajji\AppData\Local\Temp\lex-image\image.tar`), "/mnt/c/Users/Hajji/AppData/Local/Temp/lex-image/image.tar");
  assert.equal(wslPath("D:/work/x"), "/mnt/d/work/x");
});

test("the pattern that ends a run's processes matches them and never the shell that runs it", () => {
  const run = "/tmp/lex-image-run-68fdcc24-edd8-45dc-a37e-2e6dba547f8e";
  const script = endProcesses(run);
  const pattern = script.match(/pkill -KILL -f '([^']+)'/)[1];
  assert.ok(!script.includes(run), "the script never names the run directory whole, its root included");
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
