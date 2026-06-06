$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramFiles 'KillerPDF'
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }

$startMenuPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\KillerPDF.lnk'
if (Test-Path $startMenuPath) { Remove-Item $startMenuPath -Force }
