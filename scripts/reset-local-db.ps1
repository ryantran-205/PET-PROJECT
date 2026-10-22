<#
.SYNOPSIS
  XOÁ và tạo lại database DEVELOPMENT local (LocalDB FnbDev hoặc Docker volumes).
.DESCRIPTION
  ⚠️  THAO TÁC PHÁ HUỶ DỮ LIỆU. Chỉ dùng cho máy dev. Script luôn hỏi xác nhận
  (gõ đúng "RESET") trừ khi truyền -Force. Không bao giờ chạy với môi trường staging/production.
.PARAMETER Docker
  Reset dữ liệu Docker Compose (docker compose down -v → xoá named volumes sqlserver/redis/rabbitmq).
.EXAMPLE
  ./scripts/reset-local-db.ps1            # LocalDB FnbDev: drop → migrate → seed
  ./scripts/reset-local-db.ps1 -Docker    # Docker volumes
#>
param([switch]$Docker, [switch]$Force)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($env:ASPNETCORE_ENVIRONMENT -and $env:ASPNETCORE_ENVIRONMENT -ne 'Development') {
  throw "Từ chối: ASPNETCORE_ENVIRONMENT=$($env:ASPNETCORE_ENVIRONMENT). Script chỉ dành cho Development."
}

$target = if ($Docker) { 'Docker volumes (sqldata, redisdata, rabbitdata)' } else { 'LocalDB database FnbDev' }
Write-Warning "Sắp XOÁ TOÀN BỘ dữ liệu của: $target"
if (-not $Force) {
  $answer = Read-Host 'Gõ RESET để tiếp tục'
  if ($answer -cne 'RESET') { Write-Host 'Đã huỷ.'; exit 1 }
}

if ($Docker) {
  Push-Location $root
  try {
    docker compose down -v
    docker compose up -d --build
    Write-Host 'Đã khởi động lại stack. Service "migrator" sẽ áp dụng migrations; seed bằng: docker compose run --rm migrator --migrate-only --seed-only' -ForegroundColor Green
  } finally { Pop-Location }
  exit 0
}

sqllocaldb start MSSQLLocalDB | Out-Null
sqlcmd -S '(localdb)\MSSQLLocalDB' -Q "IF DB_ID('FnbDev') IS NOT NULL BEGIN ALTER DATABASE [FnbDev] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [FnbDev]; END"
if ($LASTEXITCODE -ne 0) { throw 'Không xoá được FnbDev (cần sqlcmd trong PATH).' }
& (Join-Path $PSScriptRoot 'migrate.ps1') -Seed
