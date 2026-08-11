# Lex Azure infrastructure

This configuration owns Lex identities, role assignments, the non-exportable artifact signing key,
the independently authorized assistant-evaluation review key and the optional local-index VM path.
The existing shared registry, Azure OpenAI account, storage
account, DNS zone, resource group and Container App are data or explicit role-assignment scopes.
Terraform does not recreate or silently import them.

The runtime identity pulls from ACR and calls Azure OpenAI. The deployment identity receives a
GitHub OIDC token for the `production` environment and can build an ACR image, assign the runtime
identity and update only `ca-lex-web`. The publisher identity receives a GitHub OIDC token only
after approval in the main-only `lex-ops` production environment, can read the exact Container App
revision and candidate/grader model identities, and can sign or verify with the Key Vault key. Its
only runtime mutation rights are the two Container Apps revision activate/deactivate actions used
to evaluate an inactive zero-traffic candidate and return it to the inactive state. It cannot update
the app, configuration, image, replica settings or traffic weights. No private signing key leaves
Key Vault.

Assistant-evaluation approval uses a second RBAC-enabled Key Vault. Only the explicitly configured
human `evaluation_reviewer_object_id` receives Crypto Officer rights there; the publisher identity
has no access. Its public P-256 root is pinned into the evaluator release, so the process publishing
an artifact cannot manufacture the independent approval required to run the release gate. The
human reviewer can manage the review key but has no artifact-sign operation; the OIDC publisher
can sign artifacts but has no access to the review vault.

Initialize the remote state backend with explicit values:

```text
terraform init \
  -backend-config="resource_group_name=..." \
  -backend-config="storage_account_name=..." \
  -backend-config="container_name=..." \
  -backend-config="key=lex.tfstate"
```

Then provide subscription, tenant and globally unique vault name through an uncommitted tfvars
file or `TF_VAR_*`. Run `terraform plan -out lex.tfplan` and review every role scope before apply.
The existing Container App remains outside Terraform until its secret-backed configuration can be
imported with a proven no-op plan.

## Index-host transition

The Container App remains the live host while the mounted verified artifact set passes the size,
cold-start, latency and memory gates recorded in `docs/hybrid-eu-roadmap.md`. Blob is the durable
artifact distribution layer, never the SQLite or vector query path.

When the gate selects the VM path, Terraform provisions a candidate hostname, a static public IP,
network controls and a managed data disk. The same Lex container downloads a signed release from
Blob into a versioned directory on that disk, verifies it, warms it and atomically switches the
`current` link. Smoke tests use the candidate hostname before `law.soufien.lu` is changed. The
Container App and its last production revision stay intact until live acceptance succeeds.

Disk capacity is calculated for three release sets—active, previous and incoming—plus 10 percent
headroom, then rounded up to an Azure managed-disk tier. For example, a measured 40 GiB artifact
set requires more than 132 GiB and therefore a 256 GiB data disk; it does not become a 40 GiB
container image or an Azure Files-mounted database.

The GitHub OIDC subjects include the immutable owner and repository IDs emitted by this account's
custom subject template. This intentionally makes a repository transfer or replacement fail
closed until Terraform is reviewed with the new assertion claim.

Container App updates require the linked managed-environment join action. Lex grants a custom role
with only that action on `cae-platform-law`; the deployment identity does not administer the shared
environment.
