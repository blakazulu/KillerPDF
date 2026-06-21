<#
.SYNOPSIS
    Builds a Microsoft Store / sideload MSIX package for Scalpel.

.DESCRIPTION
    1. Publishes the single-file Scalpel.exe (Release, net48, win-x64).
    2. Stages a package layout (exe + assets + manifest with tokens substituted).
    3. Generates resources.pri.
    4. Packs the layout into an .msix with makeappx.
    5. Optionally signs it (self-signed for local testing, or a supplied cert).

    For a real Store submission, pass the Identity / Publisher / PublisherDisplayName
    values from Partner Center and DO NOT sign — the Store signs the package itself
    (use -NoSign). For local sideload testing, use -SelfSign: the script creates a
    matching self-signed cert and writes the .cer you must trust once (see -SelfSign
    output / STORE-PUBLISHING.md).

.EXAMPLE
    # Local test package, self-signed:
    pwsh -File packaging\build-msix.ps1 -SelfSign

.EXAMPLE
    # Store submission package — RECOMMENDED. Bakes in the Partner Center identity,
    # forces a Store-legal .0 version, and leaves it unsigned (the Store signs it):
    pwsh -File packaging\build-msix.ps1 -Store

.EXAMPLE
    # Store submission package with explicit identity (advanced):
    pwsh -File packaging\build-msix.ps1 -NoSign `
         -IdentityName "Publisher.AppName" `
         -Publisher "CN=00000000-0000-0000-0000-000000000000" `
         -PublisherDisplayName "Your Publisher Display Name"
#>
[CmdletBinding(DefaultParameterSetName = 'SelfSign')]
param(
    [string]$Version             = '',                       # defaults to csproj <Version> with Revision forced to .0
    [string]$IdentityName        = 'Scalpel',
    [string]$Publisher           = 'CN=Scalpel Dev',       # MUST match signing cert subject
    [string]$PublisherDisplayName= 'Scalpel',
    [string]$DisplayName         = 'Scalpel',

    [Parameter(ParameterSetName='SelfSign')][switch]$SelfSign,   # create+use a self-signed cert
    [Parameter(ParameterSetName='Cert')][string]$CertPath,       # sign with an existing .pfx
    [Parameter(ParameterSetName='Cert')][string]$CertPassword,
    [Parameter(ParameterSetName='NoSign')][switch]$NoSign,       # leave unsigned (Store signs it)
    [Parameter(ParameterSetName='Store')][switch]$Store,         # NoSign + this app's Partner Center identity

    [switch]$SkipPublish
)

# ── -Store: use this app's real Partner Center identity (Product Identity page) ──
if ($Store) {
    $IdentityName         = 'LirazShakaAmir.ScalpelPDF'
    $Publisher            = 'CN=8B3919EF-5B9D-4935-A322-FC9435A969F6'
    $PublisherDisplayName = 'LirazShakaAmir'
    $DisplayName          = 'Scalpel PDF'
}

$ErrorActionPreference = 'Stop'
$repo  = Resolve-Path (Join-Path $PSScriptRoot '..')
$proj  = Join-Path $repo 'Scalpel.csproj'
$pkgDir= $PSScriptRoot
$outDir= Join-Path $pkgDir 'out'
$layout= Join-Path $outDir 'layout'
$publishDir = Join-Path $repo 'bin\Release\net48\publish'

# ── Resolve version (default from csproj) ──────────────────────────────────
if (-not $Version) {
    $csproj = [xml](Get-Content $proj)
    $v = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
    if (-not $v) { $v = '1.0.0' }
    $parts = @($v.ToString().Split('.'))
    while ($parts.Count -lt 4) { $parts += '0' }
    # The Microsoft Store reserves the 4th part (Revision) and requires it to be 0
    # (WACK "app count" test). Keep the internal build counter OUT of the package version.
    $Version = "$($parts[0]).$($parts[1]).$($parts[2]).0"
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "Package version '$Version' is invalid for the Store: it must be Major.Minor.Build.0 (Revision must be 0)."
}
Write-Host "==> Package version: $Version" -ForegroundColor Cyan

# ── Locate the .NET SDK ────────────────────────────────────────────────────
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $cand = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $cand) { $dotnet = $cand }
}
if (-not $dotnet) { throw "dotnet SDK not found. Install the .NET 8 SDK (https://dot.net) or add it to PATH." }

# ── Locate Windows SDK tools (newest version, host architecture) ───────────
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$binRoots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")
function Find-SdkTool([string]$name) {
    foreach ($root in $binRoots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -match '^10\.' } | Sort-Object Name -Descending |
               ForEach-Object { Join-Path $_.FullName "$arch\$name" } |
               Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    throw "$name not found in the Windows 10/11 SDK. Install the SDK (component 'Windows SDK Signing Tools')."
}
$makeappx = Find-SdkTool 'makeappx.exe'
$makepri  = Find-SdkTool 'makepri.exe'
$signtool = Find-SdkTool 'signtool.exe'
Write-Host "==> makeappx: $makeappx"

# ── 1. Publish the EXE ─────────────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Host "`n==> Publishing Scalpel.exe (Release, net48, win-x64)..." -ForegroundColor Cyan
    & $dotnet publish $proj -c Release /p:PublishProfile=FolderProfile1
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}
$exe = Join-Path $publishDir 'Scalpel.exe'
if (-not (Test-Path $exe)) { throw "Published EXE not found at $exe" }

# ── 1b. Purge stale/foreign package artifacts (e.g. old KillerPDF builds) ───
# Ensures 'out' only ever contains the current Scalpel package + its own staging.
if (Test-Path $outDir) {
    Get-ChildItem -Path (Join-Path $outDir '*') -Include *.msix,*.appx,*.cer,*.pfx -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike 'Scalpel_*.msix' } |
        ForEach-Object {
            Write-Host "    Removing stale artifact: $($_.Name)" -ForegroundColor DarkGray
            Remove-Item $_.FullName -Force
        }
}

# ── 2. Stage the package layout ────────────────────────────────────────────
Write-Host "`n==> Staging package layout..." -ForegroundColor Cyan
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path $layout | Out-Null
Copy-Item $exe (Join-Path $layout 'Scalpel.exe')
Copy-Item (Join-Path $pkgDir 'Assets') (Join-Path $layout 'Assets') -Recurse

# Substitute manifest tokens
$manifest = Get-Content (Join-Path $pkgDir 'AppxManifest.xml') -Raw
$manifest = $manifest.
    Replace('{IdentityName}',         $IdentityName).
    Replace('{Publisher}',            $Publisher).
    Replace('{PublisherDisplayName}', $PublisherDisplayName).
    Replace('{DisplayName}',          $DisplayName).
    Replace('{Version}',              $Version)
Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding UTF8

# ── 3. Generate resources.pri ──────────────────────────────────────────────
Write-Host "`n==> Generating resources.pri..." -ForegroundColor Cyan
$priConfig = Join-Path $outDir 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /o | Out-Null
Push-Location $layout
try {
    & $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') `
        /of (Join-Path $layout 'resources.pri') /o
    if ($LASTEXITCODE -ne 0) { throw "makepri failed." }
} finally { Pop-Location }

# ── 4. Pack ────────────────────────────────────────────────────────────────
$msix = Join-Path $outDir "Scalpel_$Version`_x64.msix"
Write-Host "`n==> Packing $msix ..." -ForegroundColor Cyan
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed." }
Write-Host "    Packed: $msix" -ForegroundColor Green

# ── 5. Sign ────────────────────────────────────────────────────────────────
switch ($PSCmdlet.ParameterSetName) {
    'NoSign' {
        Write-Host "`n==> -NoSign: package left unsigned. Upload the .msix to Partner Center; the Store signs it." -ForegroundColor Yellow
    }
    'Store' {
        Write-Host "`n==> -Store: built with this app's Partner Center identity, left unsigned." -ForegroundColor Yellow
        Write-Host "    Identity : $IdentityName" -ForegroundColor DarkGray
        Write-Host "    Publisher: $Publisher" -ForegroundColor DarkGray
        Write-Host "    Upload the .msix to Partner Center; the Store signs it." -ForegroundColor Yellow
    }
    'Cert' {
        if (-not (Test-Path $CertPath)) { throw "CertPath not found: $CertPath" }
        Write-Host "`n==> Signing with $CertPath ..." -ForegroundColor Cyan
        $args = @('sign','/fd','SHA256','/f',$CertPath)
        if ($CertPassword) { $args += @('/p',$CertPassword) }
        $args += $msix
        & $signtool @args
        if ($LASTEXITCODE -ne 0) { throw "signtool sign failed." }
        Write-Host "    Signed." -ForegroundColor Green
    }
    'SelfSign' {
        Write-Host "`n==> Creating self-signed cert (subject $Publisher) for local testing..." -ForegroundColor Cyan
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Publisher `
                    -KeyUsage DigitalSignature -FriendlyName 'Scalpel Dev Signing' `
                    -CertStoreLocation 'Cert:\CurrentUser\My' `
                    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
        $pfx = Join-Path $outDir 'scalpel-dev.pfx'
        $cer = Join-Path $outDir 'scalpel-dev.cer'
        $pw  = ConvertTo-SecureString -String 'scalpel' -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pw | Out-Null
        Export-Certificate    -Cert $cert -FilePath $cer | Out-Null
        & $signtool sign /fd SHA256 /f $pfx /p 'scalpel' $msix
        if ($LASTEXITCODE -ne 0) { throw "signtool sign failed." }
        Write-Host "    Signed with self-signed cert." -ForegroundColor Green
        Write-Host "`n    To install locally, trust the cert ONCE (elevated PowerShell):" -ForegroundColor Yellow
        Write-Host "      Import-Certificate -FilePath `"$cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Yellow
        Write-Host "    then double-click the .msix (or: Add-AppxPackage `"$msix`")." -ForegroundColor Yellow
    }
}

Write-Host "`n==> Done." -ForegroundColor Green
Write-Host "    Output: $msix"
