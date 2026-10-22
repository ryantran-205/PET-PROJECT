<#
.SYNOPSIS
  Chạy API (kèm worker in-process) + Vite dev server cho môi trường DEVELOPMENT.
.DESCRIPTION
  Yêu cầu đã cấu hình secrets qua `dotnet user-secrets` (xem README) hoặc biến môi trường:
    Jwt__SigningKey, Ordering__TrackingTokenSecret, Seed__DevPassword (chỉ dev).
  Mặc định dùng SQL Server LocalDB, InMemory message bus, memory cache (không cần Docker).
.EXAMPLE
  ./scripts/dev-run.ps1 -Seed
#>
param(
  [switch]$Seed,
  [switch]$NoFrontend
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root 'source/backend'
$frontend = Join-Path $root 'source/frontend'

$apiArgs = @('run', '--project', 'src/Fnb.Api', '--launch-profile', 'http', '--', '--migrate')
if ($Seed) { $apiArgs += '--seed' }

Write-Host "==> Khởi động API tại http://localhost:5080 (migrate$(if ($Seed) { ' + seed' }))" -ForegroundColor Cyan
$api = Start-Process dotnet -ArgumentList $apiArgs -WorkingDirectory $backend -PassThru -NoNewWindow

try {
  if (-not $NoFrontend) {
    Push-Location $frontend
    if (-not (Test-Path node_modules)) { npm ci }
    Write-Host "==> Khởi động Vite tại http://localhost:5173" -ForegroundColor Cyan
    npm run dev -- --port 5173
    Pop-Location
  } else {
    Wait-Process -Id $api.Id
  }
}
finally {
  if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force }
}
