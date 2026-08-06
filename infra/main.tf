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

locals {
  container_app_id = "${data.azurerm_resource_group.platform.id}/providers/Microsoft.App/containerApps/${var.container_app_name}"
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
  name      = "github-lex-production"
  parent_id = azurerm_user_assigned_identity.deploy.id
  audience  = ["api://AzureADTokenExchange"]
  issuer    = "https://token.actions.githubusercontent.com"
  subject   = "repo:SFHAJJI/lex:environment:production"
}

resource "azurerm_federated_identity_credential" "publisher_github" {
  name      = "github-lex-ops-main"
  parent_id = azurerm_user_assigned_identity.publisher.id
  audience  = ["api://AzureADTokenExchange"]
  issuer    = "https://token.actions.githubusercontent.com"
  subject   = "repo:SFHAJJI/lex-ops:ref:refs/heads/main"
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

resource "azurerm_role_assignment" "deploy_runtime_identity" {
  scope                = azurerm_user_assigned_identity.runtime.id
  role_definition_name = "Managed Identity Operator"
  principal_id         = azurerm_user_assigned_identity.deploy.principal_id
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
