# Deploy CjmApplyApi to Azure Container Apps (same env/share as pipeline-mcp).
# Requires Docker Desktop running unless -SkipBuild is set (image already in ACR).
# Usage: .\deploy.ps1 -ApiKey "your-secret"
#        .\deploy.ps1 -ApiKey "your-secret" -SkipBuild -ImageTag 20261124170807
#        .\deploy.ps1 -ApiKey "your-secret" -SpaOrigins @("https://other.azurestaticapps.net")

param(
    [Parameter(Mandatory = $true)]
    [string]$ApiKey,
    [string]$ResourceGroup = "pipeline-mcp-rg",
    [string]$AppName = "cjm-apply-api",
    [string]$AcrName = "cjmpipelinemcp",
    [string]$Environment = "pipeline-mcp-env",
    [string]$ImageTag = $(Get-Date -Format "yyyyMMddHHmmss"),
    # Browser origins allowed by Cors:Origins (ASP.NET binds Cors__Origins__N).
    [string[]]$SpaOrigins = @(
        "https://mango-mud-03b14d70f.4.azurestaticapps.net"
    ),
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$apiDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $apiDir
Set-Location $apiDir

$image = "$AcrName.azurecr.io/cjm-apply-api:$ImageTag"

$corsEnvPairs = @()
$corsYamlLines = @()
$corsIndex = 0
foreach ($rawOrigin in $SpaOrigins) {
    $origin = $rawOrigin.Trim().TrimEnd('/')
    if (-not $origin) { continue }
    $corsEnvPairs += "Cors__Origins__$corsIndex=$origin"
    $corsYamlLines += "      - name: Cors__Origins__$corsIndex"
    $corsYamlLines += "        value: $origin"
    $corsIndex++
}
if ($corsEnvPairs.Count -eq 0) {
    Write-Warning "SpaOrigins is empty; browser CORS will remain denied until you set Cors__Origins__N."
}

if (-not $SkipBuild) {
    docker info | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker daemon is not running. Start Docker Desktop and retry."
    }

    Write-Host "Building $image locally (context: repo root) ..."
    docker build -f (Join-Path $apiDir "Dockerfile") -t $image $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "docker build failed" }

    Write-Host "Logging into ACR ..."
    az acr login -n $AcrName | Out-Null
    docker push $image
    if ($LASTEXITCODE -ne 0) { throw "docker push failed" }
}
else {
    Write-Host "Skipping build; using $image"
}

$appExists = $false
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
az containerapp show -g $ResourceGroup -n $AppName -o none 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { $appExists = $true }
$ErrorActionPreference = $prevEap

# A failed prior revision (e.g. ACR auth before registry was set) blocks updates — recreate.
if ($appExists) {
    $state = az containerapp show -g $ResourceGroup -n $AppName --query "properties.provisioningState" -o tsv
    if ($state -eq "Failed") {
        Write-Host "Container App $AppName is in Failed state; deleting to recreate ..."
        az containerapp delete -g $ResourceGroup -n $AppName --yes
        if ($LASTEXITCODE -ne 0) { throw "containerapp delete failed" }
        $appExists = $false
    }
}

# Create with a public placeholder image first so we can assign AcrPull before pulling from ACR.
if (-not $appExists) {
    Write-Host "Creating Container App $AppName (placeholder image) ..."
    az containerapp create `
        -g $ResourceGroup `
        -n $AppName `
        --environment $Environment `
        --image "mcr.microsoft.com/dotnet/aspnet:8.0" `
        --ingress external `
        --target-port 8080 `
        --min-replicas 1 `
        --max-replicas 1 `
        --cpu 0.25 `
        --memory 0.5Gi `
        --system-assigned `
        --env-vars "CJM_APPLY_DB=/mnt/pipeline/cjm-apply.db" "CJM_APPLY_API_KEY=$ApiKey" @corsEnvPairs
    if ($LASTEXITCODE -ne 0) { throw "containerapp create failed" }
}

Write-Host "Ensuring AcrPull + registry identity ..."
$principalId = az containerapp show -g $ResourceGroup -n $AppName --query identity.principalId -o tsv
if (-not $principalId) { throw "Container App has no system-assigned principalId" }
$acrId = az acr show -n $AcrName --query id -o tsv
$ErrorActionPreference = "Continue"
az role assignment create --assignee $principalId --role AcrPull --scope $acrId 2>$null | Out-Null
$ErrorActionPreference = "Stop"
az containerapp registry set -g $ResourceGroup -n $AppName --server "$AcrName.azurecr.io" --identity system
if ($LASTEXITCODE -ne 0) { throw "registry set failed" }

# RBAC can lag briefly after role assignment.
Write-Host "Waiting 20s for AcrPull to propagate ..."
Start-Sleep -Seconds 20

Write-Host "Updating image on $AppName ..."
az containerapp update -g $ResourceGroup -n $AppName --image $image --min-replicas 1 --max-replicas 1
if ($LASTEXITCODE -ne 0) { throw "containerapp update failed" }

Write-Host "Setting API key secret and Azure Files mount ..."
az containerapp secret set -g $ResourceGroup -n $AppName --secrets "cjm-apply-api-key=$ApiKey"
if ($LASTEXITCODE -ne 0) { throw "secret set failed" }

$corsYamlBlock = if ($corsYamlLines.Count -gt 0) {
    "`n" + ($corsYamlLines -join "`n")
} else {
    ""
}

$yaml = @"
properties:
  template:
    containers:
    - name: $AppName
      image: $image
      env:
      - name: CJM_APPLY_DB
        value: /mnt/pipeline/cjm-apply.db
      - name: CJM_APPLY_API_KEY
        secretRef: cjm-apply-api-key$corsYamlBlock
      resources:
        cpu: 0.25
        memory: 0.5Gi
      volumeMounts:
      - volumeName: pipeline-volume
        mountPath: /mnt/pipeline
      probes:
      - type: Liveness
        httpGet:
          path: /api/health
          port: 8080
        initialDelaySeconds: 5
        periodSeconds: 30
    scale:
      minReplicas: 1
      maxReplicas: 1
    volumes:
    - name: pipeline-volume
      storageType: AzureFile
      storageName: pipelinefiles
"@

$yamlPath = Join-Path $env:TEMP "cjm-apply-api-mount.yaml"
Set-Content -Path $yamlPath -Value $yaml -Encoding utf8
az containerapp update -g $ResourceGroup -n $AppName --yaml $yamlPath
if ($LASTEXITCODE -ne 0) { throw "yaml update failed" }

# Overlapping revisions share the Azure Files SQLite DB and can lock each other out.
Write-Host "Deactivating older revisions ..."
$latest = az containerapp revision list -g $ResourceGroup -n $AppName --query "reverse(sort_by([?properties.active], &properties.createdTime))[0].name" -o tsv
$active = az containerapp revision list -g $ResourceGroup -n $AppName --query "[?properties.active].name" -o tsv
foreach ($rev in $active) {
    if ($rev -and $latest -and ($rev -ne $latest)) {
        Write-Host "  deactivate $rev"
        az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $rev 2>$null | Out-Null
    }
}

$fqdn = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.fqdn" -o tsv
Write-Host ""
Write-Host "Deployed: https://$fqdn"
Write-Host "Health:   https://$fqdn/api/health"
Write-Host "Parse:    POST https://$fqdn/api/parse"
Write-Host "Import:   POST https://$fqdn/api/jobs/import"
Write-Host "Jobs:     GET  https://$fqdn/api/jobs"
Write-Host "SQL:      POST https://$fqdn/api/sql"
Write-Host "Header:   X-Api-Key: <your key>"
if ($corsEnvPairs.Count -gt 0) {
    Write-Host "CORS:     $($SpaOrigins -join ', ')"
}
