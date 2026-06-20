$ErrorActionPreference = 'Stop'
$toolsDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$version   = $env:ChocolateyPackageVersion
$installDir = Join-Path $env:ProgramFiles 'Scalpel'
$installExe = Join-Path $installDir 'Scalpel.exe'

$packageArgs = @{
    packageName    = $env:ChocolateyPackageName
    fileFullPath   = Join-Path $toolsDir 'Scalpel.exe'
    url64bit       = "https://github.com/blakazulu/ScalpelPDF/releases/download/v$version/Scalpel.exe"
    checksum64     = 'REPLACE_HASH'
    checksumType64 = 'sha256'
}

Get-ChocolateyWebFile @packageArgs

# Copy to Program Files
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $toolsDir 'Scalpel.exe') $installExe -Force

# Start Menu shortcut (All Users)
$startMenuPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Scalpel.lnk'
Install-ChocolateyShortcut -ShortcutFilePath $startMenuPath -TargetPath $installExe
