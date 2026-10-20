variable "resource_group_name" { type = string }
variable "prefix" { type = string }
variable "location" { type = string }
variable "tags" {
  type    = map(string)
  default = {}
}
variable "runtime_principal_id" {
  type        = string
  default     = null
  description = "Optional existing runtime managed identity object ID for AI data-plane roles."
}
variable "chat_model" {
  type    = string
  default = "gpt-4o-mini"
}
variable "chat_model_version" { type = string }
variable "chat_capacity" {
  type    = number
  default = 10
}
variable "embedding_model" {
  type    = string
  default = "text-embedding-3-small"
}
variable "embedding_model_version" { type = string }
variable "embedding_capacity" {
  type    = number
  default = 10
}