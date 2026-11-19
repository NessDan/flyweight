# Runs build.ps1 on a hidden desktop: every test, then dist\FlyweightSetup.exe. Plain build.ps1 is
# safe anywhere, but off the hidden desktop it skips the tests that show a window or open the sound
# output. Exits with 1 unless every test ran and passed and a new program was written.
#
#   pwsh tests/run-build.ps1
param([string]$Log = 'build', [int]$Seconds = 570)
. (Join-Path $PSScriptRoot 'hidden-desktop\HiddenDesktop.ps1')

$exe = Join-Path $Root 'dist\FlyweightSetup.exe'
$code = Invoke-Hidden "pwsh -NoProfile -File `"$(Join-Path $Root 'build.ps1')`"" $Log $Seconds

$logFile = Join-Path $Logs "$Log.log"
Get-Content -LiteralPath $logFile | Select-String -Pattern 'error |warning |\[FAIL\]|Failed |Built dist' | Select-Object -First 30 | ForEach-Object { $_.Line }
if (-not (Test-AllPassed (Read-TestTotals $logFile) $code $Seconds $logFile)) { exit 1 }
# The copy keeps the date of the build it came from, so look for build.ps1 saying it wrote it.
if (-not (Select-String -LiteralPath $logFile -Pattern '^Built dist' -Quiet)) { Write-Output "NOT BUILT: build.ps1 didn't write dist\FlyweightSetup.exe. Log: $logFile"; exit 1 }
"dist\FlyweightSetup.exe: {0:N0} bytes, SHA-256 {1}" -f (Get-Item -LiteralPath $exe).Length, (Get-FileHash -LiteralPath $exe).Hash
