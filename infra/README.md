# Lex Azure infrastructure

This configuration owns only Lex identities, role assignments and the non-exportable artifact
signing key. The existing shared registry, Azure OpenAI account, resource group and Container App
are data or explicit role-assignment scopes. Terraform does not recreate or silently import them.

The runtime identity pulls from ACR and calls Azure OpenAI. The deployment identity receives a
GitHub OIDC token for the `production` environment and can build an ACR image, assign the runtime
identity and update only `ca-lex-web`. The publisher identity receives a GitHub OIDC token only
from `lex-ops` main and can sign or verify with the Key Vault key. No private signing key leaves
Key Vault.

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

The GitHub OIDC subjects include the immutable owner and repository IDs emitted by this account's
custom subject template. This intentionally makes a repository transfer or replacement fail
closed until Terraform is reviewed with the new assertion claim.

Container App updates require the linked managed-environment join action. Lex grants a custom role
with only that action on `cae-platform-law`; the deployment identity does not administer the shared
environment.
