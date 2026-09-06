"""Counterfactuals on a copied archive; the original image and product source stay intact."""
import copy
import hashlib
import io
import json
import pathlib
import subprocess
import sys
import tarfile

archive, published, scratch, result = map(pathlib.Path, sys.argv[1:])
scratch.mkdir(parents=True, exist_ok=True)
empty = scratch / "empty-publish"
empty.mkdir(exist_ok=True)
inspector = pathlib.Path(__file__).with_name("inspect_image.py")
with tarfile.open(archive) as source:
    original = {m.name: source.extractfile(m).read() for m in source}
index = json.loads(original["index.json"])
manifest_name = "blobs/sha256/" + index["manifests"][0]["digest"][7:]
manifest = json.loads(original[manifest_name])
config_name = "blobs/sha256/" + manifest["config"]["digest"][7:]
config = json.loads(original[config_name])


def bind(blobs, descriptor, value):
    content = json.dumps(value).encode()
    digest = hashlib.sha256(content).hexdigest()
    descriptor.update(digest="sha256:" + digest, size=len(content))
    blobs["blobs/sha256/" + digest] = content


results = []
cases = {"blob-bytes": "blob digest", "descriptor-size": "descriptor size",
         "platform": "image platform", "root-user": "non-root user",
         "entrypoint": "entrypoint", "base-layers": "pinned base layers",
         "diff-id": "layer diff digest", "missing-published": "published files differ",
         "duplicate-member": "duplicate archive member"}
for name, expected in cases.items():
    blobs = dict(original)
    altered_index, altered_manifest, altered_config = copy.deepcopy((index, manifest, config))
    if name == "blob-bytes":
        blobs[config_name] += b" "
    elif name == "descriptor-size":
        altered_index["manifests"][0]["size"] += 1
        blobs["index.json"] = json.dumps(altered_index).encode()
    elif name not in ("missing-published", "duplicate-member"):
        if name == "platform": altered_config["architecture"] = "arm64"
        if name == "root-user": altered_config["config"]["User"] = "0"
        if name == "entrypoint": altered_config["config"]["Entrypoint"] = ["/bin/sh"]
        if name == "diff-id": altered_config["rootfs"]["diff_ids"][-1] = "sha256:" + "0" * 64
        if name == "base-layers": altered_manifest["layers"][0]["mediaType"] = "application/wrong"
        bind(blobs, altered_manifest["config"], altered_config)
        bind(blobs, altered_index["manifests"][0], altered_manifest)
        blobs["index.json"] = json.dumps(altered_index).encode()
    mutant = scratch / "mutant.tar"
    with tarfile.open(mutant, "w") as destination:
        for path, content in blobs.items():
            entry = tarfile.TarInfo(path)
            entry.size = len(content)
            destination.addfile(entry, io.BytesIO(content))
        if name == "duplicate-member":
            destination.addfile(entry, io.BytesIO(content))
    command = [sys.executable, str(inspector), str(mutant),
               str(empty if name == "missing-published" else published), str(scratch / "rejected.json")]
    run = subprocess.run(command, capture_output=True, text=True)
    caught = run.returncode != 0 and ("ValueError: " + expected) in run.stderr
    results.append({"case": name, "command": command, "exitCode": run.returncode,
                    "caught": caught, "diagnostic": run.stderr.strip().splitlines()[-1] if run.stderr else run.stdout})
result.write_bytes((json.dumps(results, indent=2) + "\n").encode())
if not all(r["caught"] for r in results):
    raise ValueError("An inspection counterfactual did not fail at its intended guard")
print(f"Caught {len(results)} artifact counterfactuals; original archive untouched")
