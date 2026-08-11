data "azurerm_client_config" "current" {}

data "azurerm_resource_group" "platform" {
  name = var.platform_resource_group
}

data "azurerm_container_registry" "shared" {
  name                = var.shared_acr_name
  resource_group_name = var.shared_acr_resource_group
}

data "azurerm_cognitive_account" "openai" {
  name                = var.azure_openai_name
  resource_group_name = var.azure_openai_resource_group
}

data "azurerm_cognitive_account" "assistant_grader" {
  name                = var.assistant_grader_openai_name
  resource_group_name = var.assistant_grader_openai_resource_group
}

data "azurerm_application_insights" "web" {
  name                = var.application_insights_name
  resource_group_name = data.azurerm_resource_group.platform.name
}

locals {
  container_app_id                = "${data.azurerm_resource_group.platform.id}/providers/Microsoft.App/containerApps/${var.container_app_name}"
  container_app_environment_id    = "${data.azurerm_resource_group.platform.id}/providers/Microsoft.App/managedEnvironments/${var.container_app_environment_name}"
  log_analytics_resource_group_id = join("/", slice(split("/", data.azurerm_application_insights.web.workspace_id), 0, 5))
  tags = {
    app       = "lex"
    managedBy = "terraform"
  }
}

resource "azurerm_user_assigned_identity" "runtime" {
  name                = "uami-lex-runtime"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.platform.name
  tags                = local.tags
}

resource "azurerm_user_assigned_identity" "deploy" {
  name                = "uami-lex-deploy"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.platform.name
  tags                = local.tags
}

resource "azurerm_user_assigned_identity" "publisher" {
  name                = "uami-lex-publisher"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.platform.name
  tags                = local.tags
}

resource "azurerm_federated_identity_credential" "deploy_github" {
  name                      = "github-lex-production"
  user_assigned_identity_id = azurerm_user_assigned_identity.deploy.id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  subject                   = "repo:SFHAJJI@26882784/lex@1318835305:environment:production"
}

resource "azurerm_federated_identity_credential" "publisher_github" {
  name                      = "github-lex-ops-production"
  user_assigned_identity_id = azurerm_user_assigned_identity.publisher.id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  subject                   = "repo:SFHAJJI@26882784/lex-ops@1319033296:environment:production"
}

resource "azurerm_role_assignment" "runtime_acr_pull" {
  scope                = data.azurerm_container_registry.shared.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.runtime.principal_id
}

resource "azurerm_role_assignment" "runtime_openai" {
  scope                = data.azurerm_cognitive_account.openai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_user_assigned_identity.runtime.principal_id
}

resource "azurerm_role_assignment" "deploy_acr_tasks" {
  scope                = data.azurerm_container_registry.shared.id
  role_definition_name = "Container Registry Tasks Contributor"
  principal_id         = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_assignment" "deploy_container_app" {
  scope                = local.container_app_id
  role_definition_name = "Container Apps Contributor"
  principal_id         = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_definition" "deploy_environment_join" {
  name        = "Lex Container Apps Environment Join"
  scope       = data.azurerm_resource_group.platform.id
  description = "Lets the Lex deployment identity join only the existing Container Apps environment."

  permissions {
    actions = ["Microsoft.App/managedEnvironments/join/action"]
  }

  assignable_scopes = [data.azurerm_resource_group.platform.id]
}

resource "azurerm_role_assignment" "deploy_environment_join" {
  scope              = local.container_app_environment_id
  role_definition_id = azurerm_role_definition.deploy_environment_join.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_assignment" "deploy_runtime_identity" {
  scope                = azurerm_user_assigned_identity.runtime.id
  role_definition_name = "Managed Identity Operator"
  principal_id         = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_definition" "deploy_application_insights_metadata_reader" {
  name        = "Lex Application Insights Metadata Reader"
  scope       = data.azurerm_resource_group.platform.id
  description = "Lets the Lex deployment identity resolve only the published Application Insights component."

  permissions {
    actions = ["Microsoft.Insights/components/read"]
  }

  assignable_scopes = [data.azurerm_resource_group.platform.id]
}

resource "azurerm_role_assignment" "deploy_application_insights_reader" {
  scope              = data.azurerm_application_insights.web.id
  role_definition_id = azurerm_role_definition.deploy_application_insights_metadata_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_definition" "deploy_log_analytics_table_policy_reader" {
  name        = "Lex Log Analytics Table Policy Reader"
  scope       = local.log_analytics_resource_group_id
  description = "Lets the Lex deployment identity verify only the published Log Analytics table-retention policy."

  permissions {
    actions = ["Microsoft.OperationalInsights/workspaces/tables/read"]
  }

  assignable_scopes = [local.log_analytics_resource_group_id]
}

resource "azurerm_role_assignment" "deploy_log_analytics_table_reader" {
  scope              = data.azurerm_application_insights.web.workspace_id
  role_definition_id = azurerm_role_definition.deploy_log_analytics_table_policy_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_key_vault" "signing" {
  name                       = var.key_vault_name
  location                   = var.location
  resource_group_name        = data.azurerm_resource_group.platform.name
  tenant_id                  = var.tenant_id
  sku_name                   = "standard"
  purge_protection_enabled   = true
  soft_delete_retention_days = 90
  tags                       = local.tags

  access_policy {
    tenant_id = var.tenant_id
    object_id = data.azurerm_client_config.current.object_id
    key_permissions = [
      "Create", "Delete", "Get", "List", "Purge", "Recover", "Update",
      "GetRotationPolicy", "SetRotationPolicy"
    ]
  }

  access_policy {
    tenant_id       = var.tenant_id
    object_id       = azurerm_user_assigned_identity.publisher.principal_id
    key_permissions = ["Get", "Sign", "Verify"]
  }
}

resource "azurerm_key_vault_key" "artifact_signing" {
  name         = "lex-artifact-signing-v2"
  key_vault_id = azurerm_key_vault.signing.id
  key_type     = "EC"
  curve        = "P-256"
  key_opts     = ["sign", "verify"]

  rotation_policy {
    automatic {
      time_before_expiry = "P30D"
    }
    expire_after         = "P365D"
    notify_before_expiry = "P60D"
  }
}

resource "azurerm_role_assignment" "publisher_container_app_reader" {
  scope                = local.container_app_id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.publisher.principal_id
}

resource "azurerm_role_definition" "publisher_revision_lifecycle" {
  name        = "Lex Evaluation Candidate Revision Lifecycle"
  scope       = data.azurerm_resource_group.platform.id
  description = "Lets the Lex evidence publisher activate and deactivate only Container App revisions for bounded zero-traffic evaluation."

  permissions {
    actions = [
      "Microsoft.App/containerApps/revisions/activate/action",
      "Microsoft.App/containerApps/revisions/deactivate/action",
    ]
  }

  assignable_scopes = [data.azurerm_resource_group.platform.id]
}

resource "azurerm_role_assignment" "publisher_revision_lifecycle" {
  scope              = local.container_app_id
  role_definition_id = azurerm_role_definition.publisher_revision_lifecycle.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.publisher.principal_id
}

resource "azurerm_role_assignment" "publisher_candidate_model_reader" {
  scope                = data.azurerm_cognitive_account.openai.id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.publisher.principal_id
}

resource "azurerm_role_assignment" "publisher_grader_model_reader" {
  scope                = data.azurerm_cognitive_account.assistant_grader.id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.publisher.principal_id
}

resource "azurerm_key_vault" "evaluation_review" {
  name                       = "kv-lex-eval-review"
  location                   = var.location
  resource_group_name        = data.azurerm_resource_group.platform.name
  tenant_id                  = var.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = true
  soft_delete_retention_days = 90
  tags                       = local.tags
}

resource "azurerm_role_assignment" "evaluation_reviewer" {
  scope                = azurerm_key_vault.evaluation_review.id
  role_definition_name = "Key Vault Crypto Officer"
  principal_id         = var.evaluation_reviewer_object_id
}

resource "azurerm_key_vault_key" "evaluation_review" {
  name         = "lex-evaluation-review-v1"
  key_vault_id = azurerm_key_vault.evaluation_review.id
  key_type     = "EC"
  curve        = "P-256"
  key_opts     = ["sign", "verify"]
  depends_on   = [azurerm_role_assignment.evaluation_reviewer]

  rotation_policy {
    automatic {
      time_before_expiry = "P30D"
    }
    expire_after         = "P365D"
    notify_before_expiry = "P60D"
  }
}
