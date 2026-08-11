variable "subscription_id" {
  description = "Azure subscription containing the existing Lex resources."
  type        = string
}

variable "tenant_id" {
  description = "Microsoft Entra tenant id."
  type        = string
}

variable "location" {
  description = "Location used by Lex-owned identities and the signing vault."
  type        = string
  default     = "francecentral"
}

variable "platform_resource_group" {
  type    = string
  default = "rg-platform"
}

variable "container_app_name" {
  type    = string
  default = "ca-lex-web"
}

variable "container_app_environment_name" {
  type    = string
  default = "cae-platform-law"
}

variable "shared_acr_resource_group" {
  type    = string
  default = "rg-soufien-portfolio"
}

variable "shared_acr_name" {
  type    = string
  default = "crsoufien3orem"
}

variable "azure_openai_resource_group" {
  type    = string
  default = "rg-soufien-portfolio"
}

variable "azure_openai_name" {
  type    = string
  default = "oai-soufien-dev"
}

variable "application_insights_name" {
  type    = string
  default = "ai-lex-web"
}

variable "assistant_grader_openai_resource_group" {
  type    = string
  default = "rg-enercop-dev"
}

variable "assistant_grader_openai_name" {
  type    = string
  default = "aoai-enercop-dev"
}

variable "key_vault_name" {
  description = "Globally unique Key Vault name for the non-exportable artifact signing key."
  type        = string
  default     = "kv-lex-soufien"
}

variable "evaluation_reviewer_object_id" {
  description = "Microsoft Entra object id of the human authority allowed to sign assistant-evaluation approvals."
  type        = string
}

variable "index_storage_account_name" {
  description = "Existing Lex-owned storage account used for immutable index releases."
  type        = string
  default     = "stlexindexes"
}

variable "public_dns_zone_name" {
  description = "Existing public DNS zone containing the Lex host names."
  type        = string
  default     = "soufien.lu"
}

variable "enable_index_vm" {
  description = "Provision the zero-traffic VM candidate after an index-host gate selects it."
  type        = bool
  default     = false
}

variable "index_vm_size" {
  description = "Measured VM candidate SKU. The default preserves x64 image compatibility."
  type        = string
  default     = "Standard_B2als_v2"
}

variable "index_data_disk_size_gb" {
  description = "Managed data-disk capacity for active, previous and incoming index releases."
  type        = number
  default     = 64

  validation {
    condition     = var.index_data_disk_size_gb >= 32
    error_message = "The index data disk must be at least 32 GiB."
  }
}

variable "index_vm_admin_ssh_public_key" {
  description = "Break-glass SSH public key. Port 22 is not exposed by the VM network policy."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition = !var.enable_index_vm || try(
      startswith(trimspace(var.index_vm_admin_ssh_public_key), "ssh-"), false
    )
    error_message = "enable_index_vm requires an OpenSSH public key."
  }
}
