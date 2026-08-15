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

output "evaluation_review_key_id" {
  value = azurerm_key_vault_key.evaluation_review.id
}

output "evaluation_review_public_key_pem" {
  description = "Project-owner evaluation-review public material embedded in the release evaluator."
  value       = azurerm_key_vault_key.evaluation_review.public_key_pem
}

output "tenant_id" {
  value = var.tenant_id
}

output "subscription_id" {
  value = var.subscription_id
}

output "index_vm_candidate" {
  description = "Zero-traffic index-host candidate; null until the measured host gate enables it."
  value = var.enable_index_vm ? {
    name         = azurerm_linux_virtual_machine.index[0].name
    hostname     = trimsuffix(azurerm_dns_a_record.index_candidate[0].fqdn, ".")
    public_ip    = azurerm_public_ip.index[0].ip_address
    data_disk_gb = azurerm_managed_disk.index[0].disk_size_gb
  } : null
}
