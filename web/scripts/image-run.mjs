// Runs the release image as a container would, on this machine's WSL Ubuntu, with no daemon and nothing
// installed: its layers unpacked in order into a root filesystem, then the API started from it as the
// image says (its user, environment, working directory and entrypoint).
//
// The container is hardened the way a production deployment is: a private mount namespace, the whole
// root filesystem read-only, and a private tmpfs at `/tmp`, which the API needs because mounting a
// corpus verifies its index into a private temporary file (`LuxembourgIndexBuilder`). A watcher in the
// same namespace records every change on that `/tmp`, so a run can be held to changing nothing there
// after its first answer, a file written and deleted included, as the journey holds the host API to
// its directory. The server this answers is the journey's (`journey.mjs` `run`): `{ origin, output(),
// outputAtStart, fileWatch, changedFiles(), close() }`.

import { spawn, spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { createServer } from "node:net";

const DISTRIBUTION = process.env.LEX_WSL_DISTRIBUTION ?? "Ubuntu-24.04";

/** A Windows path as WSL mounts it. */
export function wslPath(path) {
  return path.replace(/^([A-Za-z]):/, (whole, drive) => `/mnt/${drive.toLowerCase()}`).replaceAll("\\", "/");
}

/** Runs a shell script as root in the distribution, directly (`-e`), so nothing expands it twice. */
function wsl(script) {
  const result = spawnSync("wsl", ["-d", DISTRIBUTION, "-u", "root", "-e", "sh", "-c", script], { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
  if (result.status !== 0) throw new Error(`WSL (${DISTRIBUTION}) failed (${result.status}): ${result.stdout}${result.stderr}`);
  return result.stdout;
}

/** A shell word, quoted. */
const quote = (text) => `'${String(text).replaceAll("'", "'\\''")}'`;

/**
 * Ends every process whose command line names the run directory. The pattern brackets its first
 * character, so it does not match the shell that runs it (a plain `pkill -f <dir>` killed itself).
 */
export const endProcesses = (run) => `pkill -f ${quote(`[${run[0]}]${run.slice(1)}`)} || true`;

/** Records every change under a directory, one line per event (mask in hex, then the path). */
const WATCHER = String.raw`import ctypes, os, struct, sys
libc = ctypes.CDLL("libc.so.6", use_errno=True)
MASK = 0x2 | 0x4 | 0x8 | 0x40 | 0x80 | 0x100 | 0x200 | 0x400
fd = libc.inotify_init()
watches = {}
def add(path):
    wd = libc.inotify_add_watch(fd, path.encode(), MASK)
    if wd >= 0:
        watches[wd] = path
root, log = sys.argv[1], open(sys.argv[2], "a", buffering=1)
for directory, _, _ in os.walk(root):
    add(directory)
log.write("ready\n")
while True:
    buffer = os.read(fd, 65536)
    offset = 0
    while offset < len(buffer):
        wd, mask, cookie, length = struct.unpack_from("iIII", buffer, offset)
        name = buffer[offset + 16:offset + 16 + length].rstrip(b"\0").decode(errors="replace")
        offset += 16 + length
        path = os.path.join(watches.get(wd, "?"), name)
        log.write("%x %s\n" % (mask, path))
        if mask & 0x40000000 and mask & 0x100:
            add(path)
`;

/**
 * Unpacks an OCI image layout archive into a root filesystem in WSL, layer by layer in the manifest's
 * order, removing what each layer's whiteouts remove. Answers the run directory's WSL path.
 */
export function unpackImage({ archive, layers }) {
  const run = `/tmp/lex-image-run-${randomUUID()}`;
  const steps = layers.map((digest) => {
    const blob = `${run}/layout/blobs/sha256/${digest.replace(/^sha256:/, "")}`;
    return `
tar -tzf ${blob} | grep -E '(^|/)\\.wh\\.\\.wh\\.\\.opq$' && { echo "an opaque whiteout is not supported"; exit 4; } || true
tar -tzf ${blob} | grep -E '(^|/)\\.wh\\.[^/]+$' | while read -r marker; do
  target="$(dirname "$marker")/$(basename "$marker" | sed 's/^\\.wh\\.//')"
  rm -rf "${run}/rootfs/$target"
done
tar -xzf ${blob} -C ${run}/rootfs --numeric-owner --exclude='.wh.*'`;
  }).join("\n");
  wsl(`set -e
mkdir -p ${run}/layout ${run}/rootfs
tar -xf ${quote(wslPath(archive))} -C ${run}/layout
${steps}
mkdir -p ${run}/rootfs/proc ${run}/rootfs/dev ${run}/rootfs/tmp
cat > ${run}/watch.py <<'WATCHER'
${WATCHER}WATCHER
: > ${run}/events.log`);
  return run;
}

/**
 * The devices the container is given, each bound alone: the runtime reads random numbers and writes
 * to /dev/null. Binding the host's whole /dev gave it /dev/shm, a writable tmpfs the /tmp watcher did
 * not see (review of #825).
 */
export const DEVICES = Object.freeze(["null", "zero", "random", "urandom"]);

/**
 * An awk program over `findmnt -R -rn -o TARGET,OPTIONS <root>` that prints every mount a process in
 * the container could write to other than the watched /tmp and the devices: the start refuses to run
 * the image while any is left.
 */
export const WRITABLE_MOUNTS_AWK = [
  "{ split($2, options, \",\"); ro = 0; for (i in options) if (options[i] == \"ro\") ro = 1 }",
  "ro == 0 && $1 != root \"/tmp\" && $1 !~ (\"^\" root \"/dev/(" + "null|zero|random|urandom" + ")$\") { print $1 }",
].join(" ");

/** A free TCP port on this machine. */
async function freePort() {
  const probe = createServer();
  await new Promise((resolve) => probe.listen(0, "127.0.0.1", resolve));
  const { port } = probe.address();
  await new Promise((resolve) => probe.close(resolve));
  return port;
}

/**
 * Starts the unpacked image and answers once it serves (`POST /api/v3/coverage` answering 200). `config`
 * is the image config: its user, environment, working directory and entrypoint are the container's.
 */
export async function startImage({ run, config, deadlineMs = 90000 }) {
  const port = await freePort();
  const image = config.config;
  const env = [...(image.Env ?? []), `ASPNETCORE_URLS=http://0.0.0.0:${port}`];
  const user = image.User.includes(":") ? image.User : `${image.User}:${image.User}`;
  const script = `set -e
R=${run}/rootfs
mount --bind "$R" "$R"
mount -t proc -o ro,nosuid,nodev,noexec proc "$R/proc"
mount -t tmpfs -o size=64k,mode=755,nosuid,noexec tmpfs "$R/dev"
for device in ${DEVICES.join(" ")}; do touch "$R/dev/$device"; mount --bind "/dev/$device" "$R/dev/$device"; done
mount -o remount,bind,ro "$R/dev"
mount -t tmpfs -o size=256m,mode=1777,nosuid,nodev tmpfs "$R/tmp"
mount -o remount,bind,ro "$R"
if touch "$R/app/.lex-written" 2>/dev/null; then echo "the root filesystem is writable"; exit 3; fi
writable=$(findmnt -R -rn -o TARGET,OPTIONS "$R" | awk -v root="$R" '${WRITABLE_MOUNTS_AWK}')
if [ -n "$writable" ]; then echo "writable mounts the watcher does not cover: $writable"; exit 5; fi
python3 ${run}/watch.py "$R/tmp" ${run}/events.log &
while ! grep -q '^ready$' ${run}/events.log; do sleep 0.1; done
cd "$R${image.WorkingDir ?? "/"}"
exec env -i ${env.map(quote).join(" ")} chroot --userspec=${user} "$R" ${image.Entrypoint.map(quote).join(" ")}`;
  const child = spawn("wsl", ["-d", DISTRIBUTION, "-u", "root", "-e", "unshare", "--mount", "--pid", "--fork", "sh", "-c", script], { stdio: ["ignore", "pipe", "pipe"] });
  let output = "";
  child.stdout.on("data", (chunk) => { output += chunk; });
  child.stderr.on("data", (chunk) => { output += chunk; });
  let exited = null;
  child.on("exit", (code) => { exited = code; });
  const origin = `http://127.0.0.1:${port}`;
  const close = async () => {
    child.kill();
    // The processes live in WSL; ending the Windows side does not end them.
    spawnSync("wsl", ["-d", DISTRIBUTION, "-u", "root", "-e", "sh", "-c", endProcesses(run)], { encoding: "utf8" });
    await new Promise((resolve) => setTimeout(resolve, 300));
  };
  const events = () => wsl(`cat ${run}/events.log`).split("\n").filter((line) => line !== "" && line !== "ready");
  const started = Date.now();
  while (Date.now() - started < deadlineMs) {
    if (exited !== null) throw new Error(`the image exited (${exited}) before it answered: ${output.slice(0, 2000)}`);
    try {
      const answer = await fetch(`${origin}/api/v3/coverage`, { method: "POST", headers: { "content-type": "application/json" }, body: '{"operation_id":"coverage","parameters":{}}' });
      if (answer.status === 200) {
        await answer.arrayBuffer();
        const eventsAtStart = events().length;
        return {
          origin,
          output: () => output,
          outputAtStart: output.length,
          fileWatch: {
            /** What changed on the container's /tmp after it first answered, as `[mask, path]`. */
            async stop() {
              return events().slice(eventsAtStart).map((line) => [line.slice(0, line.indexOf(" ")), line.slice(line.indexOf(" ") + 1)]);
            },
          },
          /** Nothing can change outside /tmp: the root filesystem is mounted read-only, which the start proved. */
          async changedFiles() { return []; },
          close,
          eventsAtStart,
        };
      }
    } catch {
      // Not listening yet.
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  await close();
  throw new Error(`the image did not answer within ${deadlineMs} ms: ${output.slice(0, 2000)}`);
}

/** Removes a run directory from WSL, and answers whether it is gone. */
export function removeRun(run) {
  // Two commands: a shell whose own command line named the directory (the removal) would match the
  // pattern and end itself.
  wsl(endProcesses(run));
  wsl(`rm -rf ${quote(run)}`);
  return wsl(`test -e ${quote(run)} && echo present || echo gone`).trim() === "gone";
}
