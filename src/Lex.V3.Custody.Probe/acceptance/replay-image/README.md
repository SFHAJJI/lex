# Accepted custody replay image preparation

Issue #459, S1-A04. Governance `37b6f5763c1b15778522e16296194efd27799cc2`.
Source integration: `aeac066baa0e52640d723836d13281f2704683d2`.
This packages accepted code; it changes no C# implementation or deployment template.

The existing deployment proposal pins an older image without the accepted `replay` command.
This separate artifact makes that command available for a future, separately reviewed operation.
The old image, its receipts, and the pending five-execution proposal remain intact.

## Artifact personally built and opened

- Local archive: `C:/lex-v3/worktrees/codex-custody-replay-image/artifacts/replay-image/custody-replay.tar`.
- Size: **57,585,152 bytes**.
- Archive SHA-256: `7a02e38fd7290376accad8a1d09a02eed2814e08afe0f35ab3fa9ff50fb17b29`.
- OCI manifest: `sha256:710b0d3a128dbc37aa4a58f7942b2586e645e3d847f552deb11b71257e210281`.
- OCI reference name: `aeac066baa0e52640d723836d13281f2704683d2`.

`inspection.json` records all **10 archive members**, **29 published files** and **144 source
inputs**. All content-addressed members, descriptor lengths and uncompressed layer digests were
verified. The five inherited layers match the exact Linux/amd64 base manifest, itself bound to
the pinned multi-platform index. Both raw base responses were fetched from MCR and checked against
their SHA-256 identities before retention here. The final application layer matches the complete
published directory byte-for-byte, including dependencies, configuration and symbols. The source
inputs match the accepted Git objects; no application source changed during build or inspection.

Configuration is Linux/amd64, user `1654`, entrypoint
`dotnet /app/Lex.V3.Custody.Probe.dll`, with no inherited command. ORAS independently opened the
same archive and returned the manifest identity above (`oras-descriptor.json`).
The image is locally retained and is **not registry-published or deployed**. Its created timestamp
means a rebuild is not a claim to reproduce this digest. Reviewers must open this exact archive
for claims about this artifact, or record a newly built artifact's distinct identity.

## Personally executed checks

SDK: `10.0.400`. Locked solution restore and OCI publication succeeded. The focused
`FactCustodyReplayTests|AzureCustodyProbeContractTests` selection passed **44 tests**, zero skipped
or failed. The complete solution passed **2,708 tests**, with **six skips**, zero failures, in
**1m04.625s**. Skips are five opt-in publisher canaries and the Windows symlink test unavailable
on this host. The published managed assembly, invoked on Windows as `replay` with a valid-format
digest and Azure/custody/identity environment variables removed in its child process, refused
with exit **1**, stdout **0 bytes**, and exactly `custody_probe_failed` on stderr.
This is not execution in a Linux container or under Azure managed identity.

`probe_inspection.py` made nine separate counterfactual archives and observed the intended
`ValueError` for each: changed blob bytes, wrong descriptor length, wrong architecture, root user,
wrong entrypoint, changed inherited base layer, wrong uncompressed layer digest, missing published
files, and duplicate archive member. It recomputes enclosing digests for configuration mutations
so those cases reach the configuration guard. Product code and the original archive stay intact.
The inspector uses explicit exceptions, so Python's optimized mode cannot remove its checks.
These checks establish the stated bounded archive properties, not a general OCI verifier or a
complete adversarial security certification.

Exact commands and results are in `commands.json`, `counterfactuals.json`, `cli-refusal.json`, and
the retained logs. Scoped Git attributes preserve the raw Windows log bytes and exact MCR response
bytes; logs remain readable text files despite binary Git diff treatment. To repeat inspection
from the checkout root:

```powershell
python src/Lex.V3.Custody.Probe/acceptance/replay-image/inspect_image.py artifacts/replay-image/custody-replay.tar src/Lex.V3.Custody.Probe/bin/Release/net10.0/linux-x64/publish artifacts/replay-image/inspection-repeat.json
python src/Lex.V3.Custody.Probe/acceptance/replay-image/probe_inspection.py artifacts/replay-image/custody-replay.tar src/Lex.V3.Custody.Probe/bin/Release/net10.0/linux-x64/publish artifacts/replay-image/counterfactuals artifacts/replay-image/counterfactuals-repeat.json
```

The source head in a repeated receipt names that checkout; its source hashes, not that label
alone, must be compared to the original build inputs. The published directory must belong to the
archive under inspection; a different build is not interchangeable merely because source matches.

## Remaining acceptance dependencies

This is a preparation artifact and cannot satisfy #459 production acceptance. Actual operation
still requires the explicit owner decision on new spending and irreversible retention, a concrete
reviewed image/operation tuple, and an authoritative non-synthetic accepted-Fact population with
its retained input, routes, receipts and exact bodies. No population is invented here. Synthetic
tests and an empty population cannot establish that requirement. This image is not substituted
into `deployment/main.bicep`, which still pins the previously proposed write/read image.

No publisher traffic, Azure writes, ACR upload, new resource, retention-policy change, deployment,
Linux container execution, production replay, release lineage, signature or promotion is claimed.
The image inherits the base's port metadata; no service or ingress is introduced by this console
program. Stage 1 remains open.
