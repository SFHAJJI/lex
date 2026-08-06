output "deploy_client_id" {
  value = azurerm_user_assigned_identity.deploy.client_id
}

output "publisher_client_id" {
  value = azurerm_user_assigned_identity.publisher.client_id
}

output "runtime_identity_id" {
  value = azurerm_user_assigned_identity.runtime.id
}

output "artifact_signing_key_id" {
  value = azurerm_key_vault_key.artifact_signing.id
}

output "artifact_signing_public_key_pem" {
  description = "Public material to pin in the application release before enabling Key Vault signing."
  value       = azurerm_key_vault_key.artifact_signing.public_key_pem
}

output "tenant_id" {
  value = var.tenant_id
}

output "subscription_id" {
  value = var.subscription_id
}
