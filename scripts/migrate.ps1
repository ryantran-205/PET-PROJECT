<#
.SYNOPSIS
  Áp dụng EF Core migrations (non-destructive) và tuỳ chọn seed dữ liệu DEVELOPMENT.
.EXAMPLE
  ./scripts/migrate.ps1            # chỉ migrate
  ./scripts/migrate.ps1 -Seed      # migrate + seed dữ liệu demo (dev-only)
#>
param([switch]$Seed)
$ErrorActionPreference = 'Stop'
$backend = Join-Path (Split-Path -Parent $PSScriptRoot) 'source/backend'
$mode = if ($Seed) { @('--migrate-only', '--seed-only') } else { @('--migrate-only') }
Push-Location $backend
try {
  dotnet run --project src/Fnb.Api --launch-profile http -- @mode
  if ($LASTEXITCODE -ne 0) { throw "Migration thất bại (exit $LASTEXITCODE)" }
  Write-Host 'Migration hoàn tất.' -ForegroundColor Green
} finally { Pop-Location }
