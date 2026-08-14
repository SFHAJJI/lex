data "azurerm_storage_account" "indexes" {
  name                = var.index_storage_account_name
  resource_group_name = data.azurerm_resource_group.platform.name
}

data "azurerm_dns_zone" "public" {
  name                = var.public_dns_zone_name
  resource_group_name = data.azurerm_resource_group.platform.name
}

locals {
  index_blob_container_scope = "${data.azurerm_storage_account.indexes.id}/blobServices/default/containers/lex"
}

resource "azurerm_role_assignment" "runtime_index_reader" {
  scope                = local.index_blob_container_scope
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_user_assigned_identity.runtime.principal_id
}

resource "azurerm_role_assignment" "publisher_index_writer" {
  scope                = local.index_blob_container_scope
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.publisher.principal_id
}

resource "azurerm_role_assignment" "deploy_index_inventory_reader" {
  scope                = local.index_blob_container_scope
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_user_assigned_identity.deploy.principal_id
}

resource "azurerm_virtual_network" "index" {
  count               = var.enable_index_vm ? 1 : 0
  name                = "vnet-lex-index"
  address_space       = ["10.42.0.0/24"]
  location            = var.location
  resource_group_name = data.azurerm_resource_group.platform.name
  tags                = local.tags
}

resource "azurerm_subnet" "index" {
  count                = var.enable_index_vm ? 1 : 0
  name                 = "snet-lex-index"
  resource_group_name  = data.azurerm_resource_group.platform.name
  virtual_network_name = azurerm_virtual_network.index[0].name
  address_prefixes     = ["10.42.0.0/27"]
}

resource "azurerm_network_security_group" "index" {
  count               = var.enable_index_vm ? 1 : 0
  name                = "nsg-lex-index"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.platform.name
  tags                = local.tags

  security_rule {
    name                       = "https"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "443"
    source_address_prefix      = "Internet"
    destination_address_prefix = "*"
  }

  security_rule {
    name                       = "http-acme"
    priority                   = 110
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "80"
    source_address_prefix      = "Internet"
    destination_address_prefix = "*"
  }
}

resource "azurerm_subnet_network_security_group_association" "index" {
  count                     = var.enable_index_vm ? 1 : 0
  subnet_id                 = azurerm_subnet.index[0].id
  network_security_group_id = azurerm_network_security_group.index[0].id
}

resource "azurerm_public_ip" "index" {
  count               = var.enable_index_vm ? 1 : 0
  name                = "pip-lex-index"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.platform.name
  allocation_method   = "Static"
  sku                 = "Standard"
  tags                = local.tags
}

resource "azurerm_network_interface" "index" {
  count               = var.enable_index_vm ? 1 : 0
  name                = "nic-lex-index"
  location            = var.location
  resource_group_name = data.azurerm_resource_group.platform.name
  tags                = local.tags

  ip_configuration {
    name                          = "primary"
    subnet_id                     = azurerm_subnet.index[0].id
    private_ip_address_allocation = "Dynamic"
    public_ip_address_id          = azurerm_public_ip.index[0].id
  }
}

resource "azurerm_linux_virtual_machine" "index" {
  count                           = var.enable_index_vm ? 1 : 0
  name                            = "vm-lex-index"
  computer_name                   = "lex-index"
  location                        = var.location
  resource_group_name             = data.azurerm_resource_group.platform.name
  size                            = var.index_vm_size
  admin_username                  = "lexadmin"
  disable_password_authentication = true
  network_interface_ids           = [azurerm_network_interface.index[0].id]
  secure_boot_enabled             = true
  vtpm_enabled                    = true
  patch_assessment_mode           = "AutomaticByPlatform"
  patch_mode                      = "AutomaticByPlatform"
  tags                            = local.tags

  admin_ssh_key {
    username   = "lexadmin"
    public_key = var.index_vm_admin_ssh_public_key
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.runtime.id]
  }

  os_disk {
    caching              = "ReadWrite"
    storage_account_type = "StandardSSD_LRS"
    disk_size_gb         = 32
  }

  source_image_reference {
    publisher = "Canonical"
    offer     = "ubuntu-24_04-lts"
    sku       = "server"
    version   = "latest"
  }

  custom_data = base64encode(file("${path.module}/index-host-cloud-init.yaml"))

  boot_diagnostics {}
}

resource "azurerm_managed_disk" "index" {
  count                = var.enable_index_vm ? 1 : 0
  name                 = "disk-lex-index"
  location             = var.location
  resource_group_name  = data.azurerm_resource_group.platform.name
  storage_account_type = "StandardSSD_LRS"
  create_option        = "Empty"
  disk_size_gb         = var.index_data_disk_size_gb
  tags                 = local.tags
}

resource "azurerm_virtual_machine_data_disk_attachment" "index" {
  count              = var.enable_index_vm ? 1 : 0
  managed_disk_id    = azurerm_managed_disk.index[0].id
  virtual_machine_id = azurerm_linux_virtual_machine.index[0].id
  lun                = 0
  caching            = "ReadOnly"
}

resource "azurerm_dns_a_record" "index_candidate" {
  count               = var.enable_index_vm ? 1 : 0
  name                = "law-candidate"
  zone_name           = data.azurerm_dns_zone.public.name
  resource_group_name = data.azurerm_dns_zone.public.resource_group_name
  ttl                 = 60
  records             = [azurerm_public_ip.index[0].ip_address]
  tags                = local.tags
}

resource "azurerm_role_assignment" "deploy_index_vm" {
  count                = var.enable_index_vm ? 1 : 0
  scope                = azurerm_linux_virtual_machine.index[0].id
  role_definition_name = "Virtual Machine Contributor"
  principal_id         = azurerm_user_assigned_identity.deploy.principal_id
}
