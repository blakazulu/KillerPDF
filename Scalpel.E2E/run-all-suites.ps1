<#
.SYNOPSIS
  Run every Scalpel E2E suite, each in its OWN fresh harness process.

.DESCRIPTION
  A fresh process per suite is the reliable way to run the full set: each launch
  wins the foreground that physical clicks need (see README "run each suite as its
  own process"). Running --suite all in one process degrades after the first suite.

  Publishes Scalpel.exe if it is missing, then runs singles/journeys/pairwise/monkey,
  writing per-suite reports to e2e-reports/ and printing a combined pass/fail summary.

.EXAMPLE
  pwsh -File Scalpel.E2E\run-all-suites.ps1 -Seed 1234
#>
param(
    [string]$App = "bin\Release\net48\publish\Scalpel.exe",
    [string]$ReportDir = "e2e-reports",
    [int]$Seed = 1234
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

$suites = @("singles", "journeys", "pairwise", "monkey")
$results = @()

foreach ($suite in $suites) {
    Get-Process Scalpel -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Host "=== $suite ===" -ForegroundColor Cyan
    $stamp = "all-$suite"
    & $dotnet run --project "$repo\Scalpel.E2E" -- `
        --suite $suite --app $App --report-dir $ReportDir --stamp $stamp --seed $Seed
    $json = Join-Path $ReportDir "test-report-$stamp.json"
    if (Test-Path $json) {
        $r = Get-Content $json -Raw | ConvertFrom-Json
        $results += [pscustomobject]@{ Suite = $suite; Total = $r.total; Passed = $r.passed; Failed = $r.failed }
    }
    Get-Process Scalpel -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host "`n===== Combined summary =====" -ForegroundColor Green
$results | Format-Table -AutoSize
$totalFailed = ($results | Measure-Object -Property Failed -Sum).Sum
exit ([int]($totalFailed -gt 0))
