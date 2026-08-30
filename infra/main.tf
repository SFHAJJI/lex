data "azurerm_client_config" "current" {}

data "azurerm_resource_group" "platform" {
  name = var.platform_resource_group
}

locals {
  container_app_id             = "${data.azurerm_resource_group.platform.id}/providers/Microsoft.App/containerApps/${var.container_app_name}"
  container_app_environment_id = "${data.azurerm_resource_group.platform.id}/providers/Microsoft.App/managedEnvironments/${var.container_app_environment_name}"
  shared_acr_id                = "/subscriptions/${var.subscription_id}/resourceGroups/${var.shared_acr_resource_group}/providers/Microsoft.ContainerRegistry/registries/${var.shared_acr_name}"
  openai_account_id            = "/subscriptions/${var.subscription_id}/resourceGroups/${var.azure_openai_resource_group}/providers/Microsoft.CognitiveServices/accounts/${var.azure_openai_name}"
  assistant_grader_account_id  = "/subscriptions/${var.subscription_id}/resourceGroups/${var.assistant_grader_openai_resource_group}/providers/Microsoft.CognitiveServices/accounts/${var.assistant_grader_openai_name}"
  application_insights_id      = "${data.azurerm_resource_group.platform.id}/providers/Microsoft.Insights/components/${var.application_insights_name}"
  telemetry_policy             = jsondecode(file("${path.module}/../deploy/telemetry-policy.json"))
  telemetry_container_apps_workspace = one([
    for workspace in local.telemetry_policy.workspaces : workspace
    if workspace.purpose == "container_apps"
  ])
  telemetry_application_insights_workspace = one([
    for workspace in local.telemetry_policy.workspaces : workspace
    if workspace.purpose == "application_insights"
  ])
  telemetry_container_apps_workspace_id       = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/resourceGroups/${local.telemetry_container_apps_workspace.resource_group_name}/providers/Microsoft.OperationalInsights/workspaces/${local.telemetry_container_apps_workspace.name}"
  telemetry_application_insights_workspace_id = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/resourceGroups/${local.telemetry_application_insights_workspace.resource_group_name}/providers/Microsoft.OperationalInsights/workspaces/${local.telemetry_application_insights_workspace.name}"
  tags = {
    app       = "lex"
    managedBy = "terraform"
  }
}

# The deployment preflight reads Application Insights WorkspaceResourceId and validates it against
# telemetry-policy.json. Keeping that live read outside Terraform avoids persisting the component's
# instrumentation key and connection string in state.

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
  scope                = local.shared_acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.runtime.principal_id
}

resource "azurerm_role_assignment" "runtime_openai" {
  scope                = local.openai_account_id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_user_assigned_identity.runtime.principal_id
}

resource "azurerm_role_assignment" "deploy_acr_tasks" {
  scope                = local.shared_acr_id
  role_definition_name = "Container Registry Tasks Contributor"
  principal_id         = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_assignment" "deploy_acr_inventory_reader" {
  scope                = local.shared_acr_id
  role_definition_name = "AcrPull"
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
  name        = "Lex Application Insights Query Reader"
  scope       = data.azurerm_resource_group.platform.id
  description = "Lets the Lex deployment identity resolve and count-query only the published Application Insights component."

  permissions {
    actions = [
      "Microsoft.Insights/components/read",
      "Microsoft.Insights/components/query/read",
    ]
  }

  assignable_scopes = [data.azurerm_resource_group.platform.id]
}

resource "azurerm_role_assignment" "deploy_application_insights_reader" {
  scope              = local.application_insights_id
  role_definition_id = azurerm_role_definition.deploy_application_insights_metadata_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_definition" "deploy_log_analytics_table_policy_reader" {
  name        = "Lex Log Analytics Table Policy Reader"
  scope       = "/subscriptions/${var.subscription_id}"
  description = "Lets the Lex deployment identity verify only the published Log Analytics table-retention policy."

  permissions {
    actions = ["Microsoft.OperationalInsights/workspaces/tables/read"]
  }

  # Azure denies custom role-definition writes inside Application Insights'
  # managed workspace resource group. The role is therefore defined at the
  # nearest permitted ancestor, but assigned only to the exact workspace below.
  assignable_scopes = ["/subscriptions/${var.subscription_id}"]
}

resource "azurerm_role_assignment" "deploy_log_analytics_table_reader" {
  scope              = local.telemetry_application_insights_workspace_id
  role_definition_id = azurerm_role_definition.deploy_log_analytics_table_policy_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_definition" "deploy_telemetry_configuration_reader" {
  name        = "Lex Telemetry Configuration Reader"
  scope       = data.azurerm_resource_group.platform.id
  description = "Lets the Lex deployment identity verify the exact telemetry configuration without changing it."

  permissions {
    actions = [
      "Microsoft.App/managedEnvironments/read",
      "Microsoft.App/containerApps/read",
      "Microsoft.Insights/components/read",
      "Microsoft.Insights/diagnosticSettings/read",
      "Microsoft.Insights/diagnosticSettingsCategories/read",
    ]
  }

  assignable_scopes = [data.azurerm_resource_group.platform.id]
}

resource "azurerm_role_assignment" "deploy_telemetry_environment_reader" {
  scope              = local.container_app_environment_id
  role_definition_id = azurerm_role_definition.deploy_telemetry_configuration_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_assignment" "deploy_telemetry_container_app_reader" {
  scope              = local.container_app_id
  role_definition_id = azurerm_role_definition.deploy_telemetry_configuration_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_assignment" "deploy_telemetry_application_insights_reader" {
  scope              = local.application_insights_id
  role_definition_id = azurerm_role_definition.deploy_telemetry_configuration_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_definition" "deploy_telemetry_workspace_reader" {
  name        = "Lex Telemetry Workspace Metadata Reader"
  scope       = "/subscriptions/${var.subscription_id}"
  description = "Lets the Lex deployment identity verify the exact telemetry workspace metadata."

  permissions {
    actions = ["Microsoft.OperationalInsights/workspaces/read"]
  }

  assignable_scopes = ["/subscriptions/${var.subscription_id}"]
}

resource "azurerm_role_assignment" "deploy_telemetry_container_apps_workspace_reader" {
  scope              = local.telemetry_container_apps_workspace_id
  role_definition_id = azurerm_role_definition.deploy_telemetry_workspace_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_assignment" "deploy_telemetry_application_insights_workspace_reader" {
  scope              = local.telemetry_application_insights_workspace_id
  role_definition_id = azurerm_role_definition.deploy_telemetry_workspace_reader.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_role_definition" "deploy_container_apps_privacy_query_reader" {
  name        = "Lex Container Apps Privacy Query Reader"
  scope       = "/subscriptions/${var.subscription_id}"
  description = "Lets the Lex deployment identity run count-only privacy checks over the three Container Apps log tables."

  permissions {
    actions = [
      "Microsoft.OperationalInsights/workspaces/query/read",
      "Microsoft.OperationalInsights/workspaces/query/ContainerAppHTTPLogs/read",
      "Microsoft.OperationalInsights/workspaces/query/ContainerAppSystemLogs/read",
      "Microsoft.OperationalInsights/workspaces/query/ContainerAppConsoleLogs/read",
    ]
  }

  assignable_scopes = ["/subscriptions/${var.subscription_id}"]
}

resource "azurerm_role_assignment" "deploy_container_apps_privacy_query_reader" {
  scope              = local.telemetry_container_apps_workspace_id
  role_definition_id = azurerm_role_definition.deploy_container_apps_privacy_query_reader.role_definition_resource_id
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
  scope                = local.openai_account_id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.publisher.principal_id
}

resource "azurerm_role_assignment" "publisher_grader_model_reader" {
  scope                = local.assistant_grader_account_id
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
