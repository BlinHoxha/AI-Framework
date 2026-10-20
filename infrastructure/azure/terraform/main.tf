resource "azurerm_cognitive_account" "openai" {
  name                  = "${var.prefix}-openai"
  resource_group_name   = var.resource_group_name
  location              = var.location
  kind                  = "OpenAI"
  sku_name              = "S0"
  custom_subdomain_name = "${var.prefix}-openai"
  local_auth_enabled    = false
  tags                  = var.tags
}

resource "azurerm_cognitive_deployment" "chat" {
  name                 = "chat"
  cognitive_account_id = azurerm_cognitive_account.openai.id
  model {
    format  = "OpenAI"
    name    = var.chat_model
    version = var.chat_model_version
  }
  sku {
    name     = "Standard"
    capacity = var.chat_capacity
  }
}

resource "azurerm_cognitive_deployment" "embeddings" {
  name                 = "embeddings"
  cognitive_account_id = azurerm_cognitive_account.openai.id
  model {
    format  = "OpenAI"
    name    = var.embedding_model
    version = var.embedding_model_version
  }
  sku {
    name     = "Standard"
    capacity = var.embedding_capacity
  }
}

resource "azurerm_search_service" "main" {
  name                         = "${var.prefix}-search"
  resource_group_name          = var.resource_group_name
  location                     = var.location
  sku                          = "basic"
  local_authentication_enabled = false
  tags                         = var.tags
}

resource "azurerm_role_assignment" "openai_user" {
  count                = var.runtime_principal_id == null ? 0 : 1
  scope                = azurerm_cognitive_account.openai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = var.runtime_principal_id
}

resource "azurerm_role_assignment" "search_contributor" {
  count                = var.runtime_principal_id == null ? 0 : 1
  scope                = azurerm_search_service.main.id
  role_definition_name = "Search Index Data Contributor"
  principal_id         = var.runtime_principal_id
}

resource "azurerm_role_assignment" "search_reader" {
  count                = var.runtime_principal_id == null ? 0 : 1
  scope                = azurerm_search_service.main.id
  role_definition_name = "Search Index Data Reader"
  principal_id         = var.runtime_principal_id
}