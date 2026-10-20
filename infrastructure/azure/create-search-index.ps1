param(
    [Parameter(Mandatory = $true)] [string] $SearchEndpoint
)

$ErrorActionPreference = 'Stop'
$token = (az account get-access-token --resource 'https://search.azure.com' --query accessToken -o tsv)
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Azure CLI did not return a search access token.' }
$body = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'search-index.json') -Raw
$uri = "$($SearchEndpoint.TrimEnd('/'))/indexes/knowledge-chunks?api-version=2025-09-01"
Invoke-RestMethod -Method Put -Uri $uri -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json' -Body $body
