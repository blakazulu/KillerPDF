<#
.SYNOPSIS
  Run every Scalpel E2E suite in ONE parallel harness process.

.DESCRIPTION
  The harness now spreads the suites across a pool of isolated app instances and runs
  them concurrently; only physical clicks/typing take the shared foreground (the global
  AppDriver.ForegroundGate), so UIA Invoke clicks and all verification overlap freely.
  This replaces the old "fresh process per suite, serial" approach.

  Publishes Scalpel.exe if it is missing, then runs --suite all --parallel, writing the
  combined report to e2e-reports/ and printing the pass/fail summary.

.EXAMPLE
  pwsh -File Scalpel.E2E\run-all-suites.ps1 -Seed 1234
  pwsh -File Scalpel.E2E\run-all-suites.ps1 -Instances 3   # cap concurrency
#>
param(
    [string]$App = "bin\Release\net48\publish\Scalpel.exe",
    [string]$ReportDir = "e2e-reports",
    [int]$Seed = 1234,
    [int]$Instances = 0   # 0 = auto (min(jobCount, max(2, cores/2)))
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# Resolve dotnet (may be user-local).
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = Join-Path $HOME ".dotnet\dotnet.exe" }

if (-not (Test-Path $App)) {
    Write-Host "Publishing Scalpel.exe (not found at $App)..." -ForegroundColor Yellow
    & $dotnet publish "$repo\Scalpel.csproj" -c Release | Out-Null
}

New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
Get-Process Scalpel -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$extra = @()
if ($Instances -gt 0) { $extra += @("--instances", "$Instances") }

Write-Host "=== all suites (parallel) ===" -ForegroundColor Cyan
& $dotnet run --project "$repo\Scalpel.E2E" -- `
    --suite all --parallel --app $App --report-dir $ReportDir --stamp "all" --seed $Seed @extra
$exit = $LASTEXITCODE

Get-Process Scalpel -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$json = Join-Path $ReportDir "test-report-all.json"
if (Test-Path $json) {
    $r = Get-Content $json -Raw | ConvertFrom-Json
    Write-Host "`n===== Summary =====" -ForegroundColor Green
    [pscustomobject]@{ Total = $r.total; Passed = $r.passed; Failed = $r.failed } | Format-Table -AutoSize
}
exit $exit
