$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:CJM_APPLY_DB = Join-Path $PSScriptRoot "..\Data\cjm-apply.db"
$env:CJM_APPLY_API_KEY = "dev-key"
Stop-Process -Name CjmApplyApi -Force -ErrorAction SilentlyContinue
Set-Location $PSScriptRoot
dotnet run --no-launch-profile --urls http://127.0.0.1:5088