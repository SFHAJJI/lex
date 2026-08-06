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

variable "key_vault_name" {
  description = "Globally unique Key Vault name for the non-exportable artifact signing key."
  type        = string
  default     = "kv-lex-soufien"
}
