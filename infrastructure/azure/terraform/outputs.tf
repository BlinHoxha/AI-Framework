output "openai_endpoint" { value = azurerm_cognitive_account.openai.endpoint }
output "search_endpoint" { value = "https://${azurerm_search_service.main.name}.search.windows.net/" }