// The image runner's pure parts (`image-run.mjs`): the paths it hands WSL and the pattern that ends a
// run's processes, which must not end the shell that runs it (it did, twice, before it was bracketed
// and split from the removal).

import assert from "node:assert/strict";
import test from "node:test";

import { endProcesses, wslPath } from "../scripts/image-run.mjs";

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
