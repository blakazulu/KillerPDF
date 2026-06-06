$ErrorActionPreference = 'Stop'
$toolsDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$version   = $env:ChocolateyPackageVersion
$installDir = Join-Path $env:ProgramFiles 'KillerPDF'
$installExe = Join-Path $installDir 'KillerPDF.exe'

$packageArgs = @{
    packageName    = $env:ChocolateyPackageName
    fileFullPath   = Join-Path $toolsDir 'KillerPDF.exe'
    url64bit       = "https://github.com/SteveTheKiller/KillerPDF/releases/download/v$version/KillerPDF.exe"
    checksum64     = 'REPLACE_HASH'
    checksumType64 = 'sha256'
}

Get-ChocolateyWebFile @packageArgs

# Copy to Program Files
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $toolsDir 'KillerPDF.exe') $installExe -Force

# Start Menu shortcut (All Users)
$startMenuPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\KillerPDF.lnk'
Install-ChocolateyShortcut -ShortcutFilePath $startMenuPath -TargetPath $installExe
