"""Inspect the bounded development OCI archive; never extract or execute its layers."""
import gzip
import hashlib
import io
import json
import pathlib
import subprocess
import sys
import tarfile


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def sha(data):
    return hashlib.sha256(data).hexdigest()


archive, published, output = map(pathlib.Path, sys.argv[1:])
evidence = pathlib.Path(__file__).resolve().parent
base_index = (evidence / "base-index.json").read_bytes()
base_manifest = (evidence / "base-manifest.json").read_bytes()
require(sha(base_index) == "0839314d08bb65da369135389a5d8291f75ace587fbb0488f469eb92c62eef68", "base index digest")
require(sha(base_manifest) == "edec6ea65a92f432083a8f75fc3c18addd004015bbd4d523ce1d13e23b347008", "base manifest digest")
base_descriptor = [m for m in json.loads(base_index)["manifests"]
                   if m["platform"]["os"] == "linux" and m["platform"]["architecture"] == "amd64"]
require(len(base_descriptor) == 1 and base_descriptor[0]["digest"] == "sha256:" + sha(base_manifest)
        and base_descriptor[0]["size"] == len(base_manifest), "base platform binding")
data = archive.read_bytes()
with tarfile.open(fileobj=io.BytesIO(data)) as container:
    entries = container.getmembers()
    require(all(m.isfile() for m in entries), "archive member kind")
    require(len({m.name for m in entries}) == len(entries), "duplicate archive member")
    blobs = {m.name: container.extractfile(m).read() for m in entries}
require(json.loads(blobs["oci-layout"]) == {"imageLayoutVersion": "1.0.0"}, "OCI layout")
for name, content in blobs.items():
    require(name in ("index.json", "oci-layout") or name == "blobs/sha256/" + sha(content), "blob digest: " + name)


def open_descriptor(descriptor):
    require(descriptor["digest"].startswith("sha256:"), "descriptor algorithm")
    content = blobs["blobs/sha256/" + descriptor["digest"][7:]]
    require(len(content) == descriptor["size"], "descriptor size")
    return content


index = json.loads(blobs["index.json"])
require(len(index["manifests"]) == 1, "one image")
manifest = json.loads(open_descriptor(index["manifests"][0]))
config = json.loads(open_descriptor(manifest["config"]))
require(config["os"] == "linux" and config["architecture"] == "amd64", "image platform")
require(config["config"]["User"] == "1654", "non-root user")
require(config["config"]["Entrypoint"] == ["dotnet", "/app/Lex.V3.Custody.Probe.dll"], "entrypoint")
require(not config["config"].get("Cmd"), "unexpected command")
require(manifest["layers"][:-1] == json.loads(base_manifest)["layers"], "pinned base layers")
require(len(config["rootfs"]["diff_ids"]) == len(manifest["layers"]), "layer count")
app_files = {}
for position, descriptor in enumerate(manifest["layers"]):
    layer = gzip.decompress(open_descriptor(descriptor))
    require("sha256:" + sha(layer) == config["rootfs"]["diff_ids"][position], "layer diff digest")
    with tarfile.open(fileobj=io.BytesIO(layer)) as contents:
        for entry in contents:
            name = entry.name.removeprefix("./")
            if position == len(manifest["layers"]) - 1:
                require(name == "app" or name.startswith("app/"), "application layer path")
                require(entry.isdir() or entry.isfile(), "application layer member kind")
            if name.startswith("app/") and not entry.isdir():
                require(entry.isfile() and name not in app_files, "application override or link")
                app_files[name] = contents.extractfile(entry).read()
expected = {"app/" + p.relative_to(published).as_posix(): p.read_bytes()
            for p in published.rglob("*") if p.is_file()}
require(app_files == expected, "published files differ from image")
head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
paths = subprocess.check_output(["git", "ls-files", "src/Lex.V3.Contracts", "src/Lex.V3.Custody.Azure",
                                "src/Lex.V3.Custody.Probe", "global.json", "Directory.Build.props",
                                "Directory.Build.targets", "Directory.Packages.props", "NuGet.config"], text=True).splitlines()
inputs = []
for path in paths:
    if pathlib.Path(path).suffix not in (".cs", ".csproj", ".props", ".targets") and pathlib.Path(path).name not in ("global.json", "packages.lock.json", "NuGet.config"):
        continue
    content = pathlib.Path(path).read_bytes()
    require(content == subprocess.check_output(["git", "show", head + ":" + path]), "uncommitted build input: " + path)
    inputs.append({"path": path, "sha256": sha(content), "bytes": len(content)})
receipt = {"schema": "lex-custody-replay-image-inspection/1", "sourceHead": head,
           "archiveSha256": sha(data), "archiveBytes": len(data), "manifestDigest": index["manifests"][0]["digest"],
           "config": config, "members": [{"path": n, "sha256": sha(b), "bytes": len(b)} for n, b in blobs.items()],
           "publishedFiles": [{"path": n, "sha256": sha(b), "bytes": len(b)} for n, b in sorted(app_files.items())],
           "sourceInputs": inputs, "linuxExecutionEstablished": False, "productionAcceptanceEstablished": False}
output.write_bytes((json.dumps(receipt, indent=2) + "\n").encode())
print(json.dumps({"members": len(blobs), "publishedFiles": len(app_files), "sourceInputs": len(inputs),
                  "manifestDigest": receipt["manifestDigest"], "archiveSha256": receipt["archiveSha256"]}))
